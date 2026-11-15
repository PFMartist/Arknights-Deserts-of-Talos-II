using System;
using System.Collections;
using System.Collections.Generic;
using Stopwatch=System.Diagnostics.Stopwatch;
using BBI.Core.Data;
using BBI.Core.Pathfinding;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Simulation;
using BBI.Unity.Game;

namespace EndfieldDoK.MapUnitLab
{
    // Keep the real custom footprint and navigation clearance. Official maps
    // deserialize their own baked cache and never enter BakeSpatialPartition,
    // so registration alone cannot make a new profile available to followers.
    public static class RhodesNavigation59
    {
        sealed class Request
        {
            internal SerializableNavMeshAttributes Profile;
            internal string UnitTypes;
        }
        static readonly object Gate=new object();
        static Request[] Requested=new Request[0];

        // Main-thread registration boundary: copy native Fixed64 values before
        // handing them to map initialization. No Unity objects cross this API.
        public static void CaptureRegisteredProfiles()
        {
            if(!Runtime.SkirmishMode||Runtime.HubRegion)return;
            EntityTypeCollection registry=(EntityTypeCollection)NativeRead.Get(typeof(ShipbreakersMain),"sEntityTypes");
            if(registry==null)throw new InvalidOperationException("Navigation capture requires current unit registry");
            List<string> keys=new List<string>();
            foreach(FactionUnits.UnitSpec spec in FactionUnits.Specs)keys.Add(spec.Key);
            keys.AddRange(HumanoidUnits.Keys);keys.Add(RhodesCarrierUnits.UnitKey);
            keys.Add(RhodesCarrierUnits.AircraftKey);keys.Add(RhodesCarrierUnits.HeavyBomberKey);
            List<Request> captured=new List<Request>();
            foreach(string key in keys)
            {
                EntityTypeAttributes definition=registry.GetEntityType(key);
                UnitAttributes unit=definition==null?null:definition.Get<UnitAttributes>();
                if(unit==null)continue;
                NavMeshAttributes profile=unit.NavMeshAttributes;
                if(profile==null)throw new InvalidOperationException("Registered custom unit has no navigation profile: "+key);
                Validate(profile);Request prior=null;
                foreach(Request row in captured)if(Same(row.Profile,profile)){prior=row;break;}
                if(prior!=null){prior.UnitTypes+=","+key;continue;}
                captured.Add(new Request{Profile=new SerializableNavMeshAttributes(profile),UnitTypes=key});
            }
            lock(Gate){Requested=captured.ToArray();}
            Runtime.Log("RHODES_NAV59_PROFILES_CAPTURED uniqueProfiles="+captured.Count+"; realClearancePreserved=True; exactFixed64Snapshot=True; nativeRosterUntouched=True");
        }

        // Called once on the native PrepareSimMap completion branch, after
        // serialized Read or ordinary native baking and before InitState=Ready.
        // The caller must NOT put this hook into a per-frame movement callback.
        public static void AfterMapPrepared(object simulation)
        {
            if(!Runtime.SkirmishMode||Runtime.HubRegion)return;
            object map=simulation==null?null:NativeRead.Get(simulation,"Map");
            if(map==null)throw new InvalidOperationException("Navigation repair requires a prepared native map");
            Request[] requested;lock(Gate){requested=Requested;}
            if(requested.Length==0)throw new InvalidOperationException("Custom navigation profile capture did not complete before map preparation");
            NavMeshAttributes[] profiles=new NavMeshAttributes[requested.Length];string[] names=new string[requested.Length];
            for(int i=0;i<requested.Length;i++){profiles[i]=requested[i].Profile;names[i]=requested[i].UnitTypes;}
            PrepareMap(map,profiles,names);
        }

        // A pure managed map adapter also exercised against original native
        // cache/obstacle geometry in the isolated regression fixture.
        internal static void PrepareMap(object map,NavMeshAttributes[] profiles,string[] unitTypes)
        {
            if(map==null||profiles==null||unitTypes==null||profiles.Length!=unitTypes.Length)
                throw new ArgumentException("Matching custom navigation requests required");
            IDictionary inventory=NativeRead.Get(map,"mBakedMeshes") as IDictionary;
            QuadTree obstacles=NativeRead.Get(map,"mPathfindingObstaclesHash") as QuadTree;
            if(inventory==null||obstacles==null)throw new InvalidOperationException("Native prepared map cache/obstacle contract differs");
            Vector2r minimum=(Vector2r)NativeRead.Get(map,"Min"),maximum=(Vector2r)NativeRead.Get(map,"Max");
            Stopwatch total=Stopwatch.StartNew();int exact=0,conservative=0,baked=0;
            for(int requestIndex=0;requestIndex<profiles.Length;requestIndex++)
            {
                NavMeshAttributes profile=profiles[requestIndex];Validate(profile);
                if(NativeRead.Call(map,"GetNavMeshFor",profile)!=null){exact++;continue;}
                NavMeshAttributes selected=null;PathNodeNavigationCache cache=null;
                foreach(DictionaryEntry entry in inventory)
                {
                    NavMeshAttributes candidate=entry.Key as NavMeshAttributes;
                    PathNodeNavigationCache candidateCache=entry.Value as PathNodeNavigationCache;
                    if(candidateCache==null||!ConservativelyCovers(candidate,profile))continue;
                    if(selected==null||candidate.DistanceFromObstacles<selected.DistanceFromObstacles)
                    {selected=candidate;cache=candidateCache;}
                }
                Stopwatch timer=Stopwatch.StartNew();
                if(cache!=null)conservative++;
                else
                {
                    // Use the same native cache producer as BakeSpatialPartition,
                    // but only for this missing profile. Existing map caches,
                    // obstacles, bounds, height and original units are retained.
                    cache=new PathNodeNavigationCache();
                    cache.BakeNavigationCache(obstacles.AllObstacles,minimum,maximum,profile.DistanceFromObstacles,
                        profile.DistanceErrorPercentageTolerance,(uint)profile.BlockedBy);
                    baked++;
                }
                NativeRead.Call(map,"AddBakedPathNodeCache",profile,cache);
                if(!object.ReferenceEquals(NativeRead.Call(map,"GetNavMeshFor",profile),cache))throw new InvalidOperationException("Custom navigation cache publication failed");
                timer.Stop();
                Runtime.Log("RHODES_NAV59_PROFILE_READY units="+unitTypes[requestIndex]+"; blockedBy="+(uint)profile.BlockedBy+
                    "; clearance="+profile.DistanceFromObstacles+"; tolerance="+profile.DistanceErrorPercentageTolerance+
                    "; mode="+(selected==null?"native_missing_profile_bake":"conservative_existing_cache")+
                    "; cacheClearance="+(selected==null?profile.DistanceFromObstacles:selected.DistanceFromObstacles)+
                    "; milliseconds="+timer.ElapsedMilliseconds+"; originalCachesRetained=True; footprintAndClearanceUnchanged=True");
            }
            for(int i=0;i<profiles.Length;i++)if(NativeRead.Call(map,"GetNavMeshFor",profiles[i])==null)
                throw new InvalidOperationException("Custom unit navigation profile remains unavailable: "+unitTypes[i]);
            total.Stop();
            Runtime.Log("RHODES_NAV59_MAP_READY requested="+profiles.Length+"; exact="+exact+"; conservative="+conservative+
                "; addedNativeBakes="+baked+"; milliseconds="+total.ElapsedMilliseconds+
                "; everyCustomProfileResolved=True; nativeMovementUnchanged=True; runtimeRouteAcceptancePending=True");
        }

        internal static bool Same(NavMeshAttributes a,NavMeshAttributes b)
        {return a!=null&&b!=null&&a.BlockedBy==b.BlockedBy&&a.DistanceFromObstacles==b.DistanceFromObstacles&&a.DistanceErrorPercentageTolerance==b.DistanceErrorPercentageTolerance;}

        internal static bool ConservativelyCovers(NavMeshAttributes candidate,NavMeshAttributes requested)
        {return candidate!=null&&requested!=null&&candidate.BlockedBy==requested.BlockedBy&&candidate.DistanceErrorPercentageTolerance==requested.DistanceErrorPercentageTolerance&&candidate.DistanceFromObstacles>=requested.DistanceFromObstacles;}

        static void Validate(NavMeshAttributes profile)
        {
            if(profile==null||profile.DistanceFromObstacles<Fixed64.Zero||profile.DistanceErrorPercentageTolerance<Fixed64.Zero)
                throw new InvalidOperationException("Negative custom navigation profile value");
        }
    }
}
