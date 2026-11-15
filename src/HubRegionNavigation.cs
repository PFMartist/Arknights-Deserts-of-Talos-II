using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using BBI.Core.Data;
using BBI.Core.IO.Streams;
using BBI.Core.Utility;
using BBI.Game.Data;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Cache production belongs to a separate offline process. The game creates
    // no worker, timer or runtime bake. IsBackground did not provide a Unity
    // QUIT/domain-unload cancellation contract for the former native bake.
    public static class HubRegionNavigation
    {
        const string Schema="EndfieldDoK.HubNavigationCache.v1";
        static readonly object Gate=new object();
        static SerializableNavMeshAttributes Attributes;
        static Vector2r Minimum,Maximum;
        static string MapPath,ProfileHex,QHash,ValidHash;
        static int PathCount,LOFCount,AllHeightCount;
        static bool Configured,Checked;
        static byte[] Payload;

        // Runtime's menu gate remains compatible, with no background work.
        public static bool IsPreparing { get { return false; } }
        public static bool Prepared { get { lock(Gate){return Payload!=null;} } }

        // Main-thread read of the real registry entry. Keep exact native raw
        // Fixed64 profile bytes; never reconstruct production values from floats.
        public static void Configure(NavMeshAttributes bvm,HeightMapAttributes height,byte[] sourceValid,MapObstacle[] obstacles,string mapFile)
        {
            if(!Runtime.HubRegion)throw new InvalidOperationException("Hub cache configured outside HubRegion mode");
            if(bvm==null||height==null||height.HeightMap==null||sourceValid==null||obstacles==null)throw new ArgumentNullException("Hub cache configuration is incomplete");
            if(height.HeightMap.Length!=checked(height.GridWidth*height.GridLength)||sourceValid.Length!=height.HeightMap.Length)throw new InvalidOperationException("Hub cache source height/valid dimensions differ");
            SerializableNavMeshAttributes captured=new SerializableNavMeshAttributes(bvm);byte[] profile;
            using(MemoryStream stream=new MemoryStream()){captured.Write(new BinaryStreamWriter(stream));profile=stream.ToArray();}
            if(profile.Length!=20)throw new InvalidOperationException("Unexpected native navigation profile serialization length");
            int path=0,lof=0,all=0;foreach(MapObstacle obstacle in obstacles)
            {
                if(obstacle==null||obstacle.ObstacleShape==null)throw new InvalidOperationException("Hub cache configuration contains a null obstacle");
                if(obstacle.ObstacleShape.LayerFlag!=0)path++;
                if(obstacle.BlocksLineOfFire){lof++;if(obstacle.BlockAllHeights)all++;}
            }
            lock(Gate)
            {
                Attributes=captured;Minimum=height.MinBounds;Maximum=height.MaxBounds;MapPath=Path.GetFullPath(mapFile);ProfileHex=Hex(profile);
                QHash=Hash(height.HeightMap);ValidHash=Hash(sourceValid);PathCount=path;LOFCount=lof;AllHeightCount=all;
                Payload=null;Checked=false;Configured=true;
            }
            Runtime.Log("HUB NAV OFFLINE CACHE PROFILE: unit="+CustomUnits.UnitKey+"; navProfileHex="+ProfileHex+
                "; distanceFromObstacles="+captured.DistanceFromObstacles+"; errorTolerance="+captured.DistanceErrorPercentageTolerance+"; blockedBy="+captured.BlockedBy+
                "; pathShapes="+path+"; lofShapes="+lof+"; allHeightShapes="+all+"; sourceQSha256="+QHash+"; sourceValidSha256="+ValidHash+
                "; actualNativeFixed64Bytes=True; runtimeWorkers=0; runtimeTimers=0; runtimeBakeAllowed=False");
        }

        // One bounded file/manifest check. Missing or rejected data leaves the
        // original menu active. Repeated frame polls do not retry IO or bake.
        public static bool EnsurePrepared()
        {
            string map,profile,q,valid;int paths,lofs,all;
            lock(Gate)
            {
                if(Payload!=null)return true;if(Checked)return false;Checked=true;
                if(!Configured){Runtime.Log("HUB NAV OFFLINE CACHE UNAVAILABLE: profileNotConfigured=True; menuContinues=True; runtimeBakeForbidden=True");return false;}
                map=MapPath;profile=ProfileHex;q=QHash;valid=ValidHash;paths=PathCount;lofs=LOFCount;all=AllHeightCount;
            }
            string directory=Path.GetDirectoryName(map),manifestFile=Path.Combine(directory,"hub_navigation_cache.ini"),cacheFile=Path.Combine(directory,"hub_navigation_simmap.bin");
            try
            {
                if(!File.Exists(manifestFile)||!File.Exists(cacheFile))throw new FileNotFoundException("Generate the validated offline cache and manifest before entering the Hub: "+manifestFile+" / "+cacheFile);
                Dictionary<string,string> manifest=ReadManifest(manifestFile);
                if(Required(manifest,"schema")!=Schema)throw new InvalidDataException("Unsupported Hub navigation cache manifest schema");
                MatchHex(Required(manifest,"navProfileHex"),profile,"actual BVM raw navigation profile");
                MatchHex(Required(manifest,"mapSha256"),HashFile(map),"current Hub package SHA256");
                if(ReadCount(manifest,"pathCount")!=paths||ReadCount(manifest,"lofCount")!=lofs||ReadCount(manifest,"allHeightCount")!=all)throw new InvalidDataException("Hub cached path/LOF/all-height counts differ from the current source");
                if(!bool.Parse(Required(manifest,"offlineNativeReadVerified"))||!bool.Parse(Required(manifest,"offlineBvmLookupVerified")))throw new InvalidDataException("Hub cache has no successful offline native Read/BVM lookup validation");
                string managed=Path.Combine(Path.GetDirectoryName(Runtime.LabDirectory),"Data/Managed");
                MatchHex(Required(manifest,"nativeGameSha256"),HashFile(Path.Combine(managed,"BBI.Game.dll")),"native BBI.Game disk assembly");
                MatchHex(Required(manifest,"nativeCoreSha256"),HashFile(Path.Combine(managed,"BBI.Core.dll")),"native BBI.Core disk assembly");
                MatchHex(Required(manifest,"nativeGameDataSha256"),HashFile(Path.Combine(managed,"BBI.Game.Data.dll")),"native BBI.Game.Data disk assembly");
                string declared;if(manifest.TryGetValue("sourceQSha256",out declared))MatchHex(declared,q,"source Q bytes");
                if(manifest.TryGetValue("sourceValidSha256",out declared))MatchHex(declared,valid,"source Valid bytes");
                long bytes=long.Parse(Required(manifest,"cacheBytes"),CultureInfo.InvariantCulture);
                if(bytes<=0||bytes>536870912L||new FileInfo(cacheFile).Length!=bytes)throw new InvalidDataException("Hub serialized cache length differs from manifest or exceeds the loader limit");
                byte[] payload=File.ReadAllBytes(cacheFile);MatchHex(Required(manifest,"cacheSha256"),Hash(payload),"complete native SimMap payload");
                lock(Gate){Payload=payload;}
                Runtime.Log("HUB NAV OFFLINE CACHE READY: file="+cacheFile+"; bytes="+payload.Length+"; navProfileHex="+profile+"; pathShapes="+paths+"; lofShapes="+lofs+"; allHeightShapes="+all+
                    "; manifestAndActualProfileAndNativeHashesVerified=True; nativeReadValidatedByOfflineProducer=True; runtimeNativeBakeOrReadDuringMenu=False; runtimeWorkers=0; runtimeTimers=0");
                return true;
            }
            catch(Exception error)
            {
                Runtime.Log("HUB NAV OFFLINE CACHE REJECTED: "+error.GetType().Name+": "+error.Message+
                    "; menuContinues=True; autoRetry=False; runtimeBakeForbidden=True; backgroundWorkStarted=False");return false;
            }
        }
        public static byte[] GetPreparedSimMapData()
        {lock(Gate){if(Payload==null)throw new InvalidOperationException("Hub scene load requires a matching validated offline SimMap cache; runtime bake fallback is forbidden");return Payload;}}

        public static void VerifyLiveMap(object map)
        {
            SerializableNavMeshAttributes attrs;Vector2r min,max;int paths,lofs,all;
            lock(Gate){if(Payload==null)throw new InvalidOperationException("Hub live map has no validated offline cache");attrs=Attributes;min=Minimum;max=Maximum;paths=PathCount;lofs=LOFCount;all=AllHeightCount;}
            if((Vector2r)NativeRead.Get(map,"Min")!=min||(Vector2r)NativeRead.Get(map,"Max")!=max||ShapeCount(map,"mPathfindingObstaclesHash")!=paths||ShapeCount(map,"mStaticLineOfFireHash")!=lofs||
                Count(NativeRead.Get(map,"mAllHeightBlockers"))!=all||NativeRead.Call(map,"GetNavMeshFor",attrs)==null)throw new InvalidOperationException("Hub live native Read differs from its validated offline cache");
            Runtime.Log("HUB NAV LIVE MAP VERIFIED: pathShapes="+paths+"; lofShapes="+lofs+"; allHeightShapes="+all+"; actualBvmCacheFound=True; serializedMapMatchedOfflineManifest=True; actualManualRouteAcceptancePending=True");
        }

        // Patcher ABI stays unchanged. No in-game Hub bake is permitted. Other
        // modes and other map bounds retain their original array by reference.
        public static NavMeshAttributes[] FilterBake(object map,NavMeshAttributes[] attributes)
        {
            if(!Runtime.HubRegion)return attributes;bool configured;Vector2r min,max;lock(Gate){configured=Configured;min=Minimum;max=Maximum;}
            if(!configured||map==null)return attributes;
            if((Vector2r)NativeRead.Get(map,"Min")!=min||(Vector2r)NativeRead.Get(map,"Max")!=max)return attributes;
            Runtime.Log("HUB NAV RUNTIME BAKE BLOCKED: validatedOfflineCacheRequired=True; menuOrLoadMustNotStartNativeBake=True");
            throw new InvalidOperationException("Unexpected native Hub runtime bake: use the matching validated offline SerializedSimMapData");
        }
        public static void AfterBake(object map){ }

        static Dictionary<string,string> ReadManifest(string file)
        {
            Dictionary<string,string> result=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(string raw in File.ReadAllLines(file))
            {
                string line=raw.Trim();if(line.Length==0||line.StartsWith("#")||line.StartsWith(";")||(line.StartsWith("[")&&line.EndsWith("]")))continue;
                int split=line.IndexOf('=');if(split<1)throw new InvalidDataException("Hub cache manifest expected key=value");string key=line.Substring(0,split).Trim(),value=line.Substring(split+1).Trim();
                if(result.ContainsKey(key))throw new InvalidDataException("Repeated Hub cache manifest key: "+key);result.Add(key,value);
            }
            return result;
        }
        static string Required(Dictionary<string,string> values,string key)
        {string result;if(!values.TryGetValue(key,out result)||result.Length==0)throw new InvalidDataException("Missing Hub cache manifest key: "+key);return result;}
        static int ReadCount(Dictionary<string,string> values,string key)
        {int result=int.Parse(Required(values,key),CultureInfo.InvariantCulture);if(result<0)throw new InvalidDataException("Negative Hub cache count: "+key);return result;}
        static void MatchHex(string declared,string actual,string label)
        {if(!string.Equals(declared,actual,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Hub cache mismatch: "+label);}
        static string Hash(byte[] data){using(SHA256 sha=SHA256.Create())return Hex(sha.ComputeHash(data));}
        static string HashFile(string file){using(SHA256 sha=SHA256.Create())using(FileStream stream=File.OpenRead(file))return Hex(sha.ComputeHash(stream));}
        static string Hex(byte[] data){return BitConverter.ToString(data).Replace("-",string.Empty).ToLowerInvariant();}
        static int ShapeCount(object map,string field){return Count(NativeRead.Get(NativeRead.Get(map,field),"AllObstacles"));}
        static int Count(object value){ICollection collection=value as ICollection;if(collection!=null)return collection.Count;int n=0;IEnumerable sequence=value as IEnumerable;if(sequence!=null)foreach(object item in sequence)n++;return n;}
    }
}
