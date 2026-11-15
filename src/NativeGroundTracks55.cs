using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BBI.Core.Data;
using BBI.Game.Data;
using BBI.Unity.Core.World;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.FX;
using EndfieldDoK.ModelIntake;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Only the newly authored Escort boost print uses this private data type.
    // Native creation reads FXType with callvirt. Query its current world
    // manager at that point, so a preserved template cannot retain last match's
    // WheelFX choice. Disabled is rejected before any updater/mesh is allocated.
    [Serializable]
    internal sealed class NativeGroundBoostTracks55:EffectDefinition
    {
        public NativeGroundBoostTracks55(){}
        internal FrillType ConfiguredType {get{return base.FXType;}}
        public override FrillType FXType
        {
            get
            {
                FXManager manager=FXManager.Manager;
                bool exists=!object.ReferenceEquals(manager,null)&&manager!=null;
                return NativeGroundTracks55.BoostTypeForPolicy(exists,exists&&manager.FrillTypeEnabled(FrillType.WheelContactRange));
            }
        }
    }

    // Native FXTrackComponent explicitly supports an absent WheelCollider. The
    // direct-bone UnitFX path supplies terrain position/up; the native component
    // owns distance sampling, reverse/corner splits, mesh budgets and lifetime.
    // This file constructs templates/anchors only. It adds no Update or physics.
    internal static class NativeGroundTracks55
    {
        const string RootName="EF_NativeGroundTracks55";
        static readonly MethodInfo Memberwise=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly string[] NativePrefabNames={"FX_PRFB_Tracks_Med","FX_PRFB_Tracks_Large","FX_PRFB_Tracks_XtraLarge","FX_PRFB_Tracks_XtraLarge_Mid"};
        static readonly string[] NativeMaterialNames={"FX_MAT_Tracks_01","FX_MAT_Tracks_02_Cruisers","FX_MAT_Tracks_03_Carrier","FX_MAT_Tracks_04_Carrier_Mid"};
        static readonly float[] NativeWidths={15f,12f,27f,27f};
        static readonly float[] NativeSpacing={8f,10f,12f,12f};
        static readonly string[] LinearFields={"m_TrackWidth","m_TrackLineWorldOffset","m_MinDistBetweenPoints","m_TrackTailFadeDistance","m_TrackHeadFadeDistance"};
        static GameObject Templates;
        internal static bool HasDeferredRetirement {get;private set;}
        static readonly Dictionary<string,Transform> Cache=new Dictionary<string,Transform>(StringComparer.Ordinal);
        static readonly Dictionary<GameObject,List<Contract>> Contracts=new Dictionary<GameObject,List<Contract>>();
        sealed class Contract
        {
            internal EffectDefinition Original,Definition;
            internal UnitViewAttributes SourceView;
            internal Transform NativePrefab,PrivatePrefab,Alias,Parent;
            internal int Profile;
            internal float Width;
            internal bool Boost;
        }

        internal static void AppendFaction(UnitViewAttributes target,UnitViewAttributes source,FactionModelBinding binding,FactionTrackPackage tracks)
        {
            if(binding==null||binding.Air)return;
            GameObject root=null;List<GameObject> aliases=new List<GameObject>();
            try
            {
                RequirePrivate(target,source,binding.GeometryRoot);
                if(binding.GeometryRoot.Find(RootName)!=null)return;
                bool tracked=tracks!=null;
                // The original wheeled MP Escort has no wheel-print definition;
                // the new eight-wheel chassis can use the native medium print.
                // Harvester's original disabled Tracks LOD0 is left immutable;
                // its new four-tire model borrows the enabled Baserunner print.
                UnitViewAttributes donor=NativeView(tracked?"C_ArtilleryCruiser_MP":"C_Baserunner_MP");
                int profile=tracked?1:0;
                EffectDefinition original=FindTrack(donor,"Tracks",profile);
                RequireBudget(donor,original,2);
                List<NativeGroundEffects54.Contact> contacts=tracked?NativeGroundEffects54.TrackContacts(binding,tracks):NativeGroundEffects54.WheelContacts(binding);
                int[] pair=SelectPrintPair(contacts,binding.GeometryRoot,false);
                root=NewRoot(binding.GeometryRoot);
                List<Contract> prepared=new List<Contract>();
                BuildPair(prepared,aliases,target,donor,original,profile,contacts,pair,binding.GeometryRoot,binding.UnitKey,"Rear",false);
                if(binding.UnitKey==FactionUnits.EscortKey)
                {
                    EffectDefinition boost=FindBoost(source);
                    // Use the same two footprints and private track templates;
                    // native 2300/Nitrous1500 flags make their activity exclusive.
                    BuildPair(prepared,aliases,target,source,boost,profile,contacts,pair,binding.GeometryRoot,binding.UnitKey,"NitrousRear",true,original.FXPrefab);
                }
                Commit(target,prepared);
                Runtime.Log("RHODES_NATIVE_TRACKS55_READY key="+binding.UnitKey+"; borrowedFrom="+(tracked?"C_ArtilleryCruiser_MP/Tracks_Large":"C_Baserunner_MP/Tracks_Med")+
                    "; privateWidths="+Widths(prepared)+"; privateDefinitions="+prepared.Count+"; activePrintBudget=2; nativeLOD=15; terrainRaycast=NativeDown; terrainNormal=True; nativeTrackMeshLifetimeAndSampling=True; nativeSuspension=False; addedColliderWrites=0; customUpdates=0; boostedPair="+(binding.UnitKey==FactionUnits.EscortKey)+"; visualAcceptancePending=True; installationAuthorized=False");
            }
            catch(Exception error)
            {
                DestroyAliases(aliases);if(root!=null)UnityEngine.Object.DestroyImmediate(root);
                Runtime.Log("RHODES_NATIVE_TRACKS55_DISABLED key="+binding.UnitKey+"; scope=UnitPrintsOnly; reason="+error.Message);
            }
        }

        internal static void AppendCarrier(UnitViewAttributes target,UnitViewAttributes source,RhodesCarrierBinding binding)
        {
            GameObject root=null;List<GameObject> aliases=new List<GameObject>();
            try
            {
                if(binding==null||!binding.IsV05)throw new InvalidOperationException("Carrier authored track rig missing");
                RequirePrivate(target,source,binding.GeometryRoot);
                if(binding.GeometryRoot.Find(RootName)!=null)return;
                EffectDefinition rear=FindTrack(source,"Tracks",2),middle=FindTrack(source,"Tracks Mid",3);
                RequireBudget(source,rear,2);RequireBudget(source,middle,2);
                List<NativeGroundEffects54.Contact> contacts=CarrierContacts(binding);
                int[] rearPair=SelectPrintPair(contacts,binding.GeometryRoot,false);
                int[] middlePair=SelectPrintPair(contacts,binding.GeometryRoot,true);
                root=NewRoot(binding.GeometryRoot);List<Contract> prepared=new List<Contract>();
                BuildPair(prepared,aliases,target,source,rear,2,contacts,rearPair,binding.GeometryRoot,"RhodesCarrier","Rear",false);
                BuildPair(prepared,aliases,target,source,middle,3,contacts,middlePair,binding.GeometryRoot,"RhodesCarrier","Middle",false);
                Commit(target,prepared);
                Runtime.Log("RHODES_NATIVE_TRACKS55_READY key=RhodesCarrier; nativePrintBudget=4; privateDefinitions=4; actualBeltCandidates="+contacts.Count+
                    "; selected=RearLeft/RearRight/MiddleLeft/MiddleRight; privateWidths="+Widths(prepared)+"; all28BeltsNotDuplicated=True; terrainRaycast=NativeDown; terrainNormal=True; nativeTrackMeshLifetimeAndSampling=True; nativeSuspension=False; addedColliderWrites=0; customUpdates=0; visualAcceptancePending=True; installationAuthorized=False");
            }
            catch(Exception error)
            {
                DestroyAliases(aliases);if(root!=null)UnityEngine.Object.DestroyImmediate(root);
                Runtime.Log("RHODES_NATIVE_TRACKS55_DISABLED key=RhodesCarrier; scope=CarrierPrintsOnly; reason="+error.Message);
            }
        }

        static void BuildPair(List<Contract> prepared,List<GameObject> aliases,UnitViewAttributes target,UnitViewAttributes donor,EffectDefinition original,int profile,
            List<NativeGroundEffects54.Contact> contacts,int[] pair,Transform geometry,string key,string band,bool boost,Transform trackPrefab=null)
        {
            if(pair==null||pair.Length!=2||pair[0]==pair[1])throw new InvalidOperationException("Track pair budget differs");
            foreach(int index in pair)
            {
                NativeGroundEffects54.Contact contact=contacts[index];
                if(contact.Parent==null||!contact.Parent.IsChildOf(geometry)||!Finite(contact.Width)||contact.Width<=0f||!Finite(contact.ProbeLift)||contact.ProbeLift<=0f)
                    throw new InvalidOperationException("Actual wheel/shoe width or ray origin missing: "+contact.Provenance);
                string suffix=key+"_"+band+"_"+(prepared.Count%2==0?"L":"R");
                Transform privatePrefab=Template(trackPrefab??original.FXPrefab,profile,contact.Width);
                GameObject alias=new GameObject("EF_Tracks55_"+suffix);aliases.Add(alias);alias.layer=geometry.gameObject.layer;
                alias.transform.parent=contact.Parent;
                Vector3 point=band=="Middle"?contact.Center:contact.Rear;
                // Native Down ray starts above the real shoe/tire underside by
                // measured support height, so a point on the surface cannot
                // miss due to roundoff. The native hit places the actual mark.
                alias.transform.position=contact.Parent.TransformPoint(point)+geometry.up*contact.ProbeLift;
                alias.transform.rotation=geometry.rotation;alias.transform.localScale=Vector3.one;
                EffectDefinition copy=boost?CloneForBoost(original):Clone(original);copy.Name="EF_NativeTracks55_"+suffix;copy.FXPrefab=privatePrefab;copy.BoneRef=new string[]{alias.name};
                copy.RaycastDir=RaycastDirection.Down;copy.InheritRaycastNormal=true;
                Contract c=new Contract{Original=original,Definition=copy,SourceView=donor,NativePrefab=trackPrefab??original.FXPrefab,PrivatePrefab=privatePrefab,
                    Alias=alias.transform,Parent=contact.Parent,Profile=profile,Width=contact.Width,Boost=boost};
                CheckDefinition(c,geometry);prepared.Add(c);
            }
        }

        internal static List<NativeGroundEffects54.Contact> CarrierContacts(RhodesCarrierBinding binding)
        {
            RhodesTrackRigBinding rig=binding.GeometryRoot.GetComponent<RhodesTrackRigBinding>();
            if(rig==null||rig.Groups==null||rig.Groups.Length==0)throw new InvalidOperationException("Carrier TKR paths missing");
            List<NativeGroundEffects54.Contact> contacts=new List<NativeGroundEffects54.Contact>();
            foreach(RhodesTrackGroupState group in rig.Groups)
            {
                if(group==null||group.Renderer==null||group.Renderer.sharedMesh==null||group.Renderer.sharedMesh.vertexCount==0||group.Path==null)
                    throw new InvalidOperationException("Carrier track geometry missing");
                Transform space=group.Renderer.transform.parent;
                if(space==null||!space.IsChildOf(binding.GeometryRoot))throw new InvalidOperationException("Carrier track rig ownership differs");
                RhodesTrackSegmentState bottom=null;float min=float.PositiveInfinity;
                foreach(RhodesTrackSegmentState segment in group.Path)if(segment.Kind==0&&(segment.A.x+segment.B.x)*.5f<min)
                {min=(segment.A.x+segment.B.x)*.5f;bottom=segment;}
                if(bottom==null)throw new InvalidOperationException("Carrier lower track straight missing");
                Bounds mesh=group.Renderer.sharedMesh.bounds;float rear=Mathf.Min(bottom.A.y,bottom.B.y),front=Mathf.Max(bottom.A.y,bottom.B.y);
                contacts.Add(new NativeGroundEffects54.Contact{Parent=space,Front=new Vector3(mesh.center.x,mesh.min.y,front),Rear=new Vector3(mesh.center.x,mesh.min.y,rear),
                    Center=new Vector3(mesh.center.x,mesh.min.y,(front+rear)*.5f),Width=mesh.size.x*Mathf.Abs(space.lossyScale.x),ProbeLift=mesh.size.y*Mathf.Abs(space.lossyScale.y),Provenance=group.BogieName+"/TKR.actualShoe"});
            }
            return contacts;
        }

        static int[] SelectPrintPair(List<NativeGroundEffects54.Contact> contacts,Transform geometry,bool middle)
        {
            List<NativeGroundEffects54.Contact> sides=new List<NativeGroundEffects54.Contact>();List<int> map=new List<int>();
            for(int i=0;i<contacts.Count;i++)
            {
                // U06 has a third, central rear belt. A two-print native budget
                // selects the actual left/right paths; never classify its x=0
                // rear belt as the right side and make an asymmetric pair.
                Vector3 point=NativeGroundEffects54.ContactPoint(contacts[i],geometry,false,!middle);
                if(Mathf.Abs(point.x)<=.001f)continue;sides.Add(contacts[i]);map.Add(i);
            }
            int[] pair=NativeGroundEffects54.PickPair(sides,geometry,false,!middle,middle);
            return new int[]{map[pair[0]],map[pair[1]]};
        }

        internal static int VerifyCarrierPrefix(UnitViewAttributes target,UnitViewAttributes source,int offset)
        {
            if(target==null||target.ModelPrefab==null||offset<0)throw new InvalidOperationException("Carrier tracks prefix context missing");
            RhodesCarrierBinding binding=target.ModelPrefab.GetComponent<RhodesCarrierBinding>();List<Contract> prepared;
            bool marker=binding!=null&&binding.GeometryRoot!=null&&binding.GeometryRoot.Find(RootName)!=null;
            if(!Contracts.TryGetValue(target.ModelPrefab,out prepared))
            {if(marker)throw new InvalidOperationException("Carrier track marker exists without private contract");return 0;}
            if(!marker||prepared.Count!=4||target.UnitEffectDefs==null||target.UnitEffectDefs.Length<offset+prepared.Count)
                throw new InvalidOperationException("Carrier track prefix truncated");
            RequireBudget(source,FindTrack(source,"Tracks",2),2);RequireBudget(source,FindTrack(source,"Tracks Mid",3),2);
            for(int i=0;i<prepared.Count;i++)
            {
                Contract c=prepared[i];
                if(!object.ReferenceEquals(c.SourceView,source)||!object.ReferenceEquals(target.UnitEffectDefs[offset+i],c.Definition)||c.Profile!=(i<2?2:3)||c.Boost)
                    throw new InvalidOperationException("Carrier track prefix order/source contract changed");
                CheckDefinition(c,binding.GeometryRoot);CheckNativeTemplate(c.NativePrefab,c.Profile);CheckPrivateTemplate(c.PrivatePrefab,c.NativePrefab,c.Profile,c.Width);
            }
            return prepared.Count;
        }

        static UnitViewAttributes NativeView(string key)
        {
            EntityTypeCollection registry=NativeRead.Get(typeof(ShipbreakersMain),"sEntityTypes") as EntityTypeCollection;
            EntityTypeAttributes definition=registry==null?null:registry.GetEntityType(key);
            UnitViewAttributes view=definition==null?null:definition.Get<UnitViewAttributes>();
            if(view==null)throw new InvalidOperationException("Native track donor unavailable: "+key);return view;
        }
        static EffectDefinition FindTrack(UnitViewAttributes view,string name,int profile)
        {
            EffectDefinition found=null;
            if(view!=null&&view.UnitEffectDefs!=null)foreach(EffectDefinition d in view.UnitEffectDefs)if(d!=null&&d.Name==name)
            {if(found!=null)throw new InvalidOperationException("Ambiguous native track source");found=d;}
            if(found==null||found.FXType!=FrillType.WheelContactRange||found.FXPrefab==null||found.FXPrefab.name!=NativePrefabNames[profile]||
                (int)found.ActiveAtLOD!=15||found.Sound!="_UnSet_"||found.PolyGroup!=""||found.BoneRef==null||found.BoneRef.Length!=0||
                found.RaycastDir!=RaycastDirection.None||found.FollowNLIPSScale||found.InheritRaycastNormal)
                throw new InvalidOperationException("Native enabled track definition changed: "+name);
            CheckNativeTemplate(found.FXPrefab,profile);return found;
        }
        static EffectDefinition FindBoost(UnitViewAttributes source)
        {
            EffectDefinition found=null;
            if(source.UnitEffectDefs!=null)foreach(EffectDefinition d in source.UnitEffectDefs)if(d!=null&&d.Name=="FX - Dust Trail Nitrous")
            {if(found!=null)throw new InvalidOperationException("Ambiguous native Nitrous source");found=d;}
            if(found==null||found.FXType!=FrillType.Nitrous||found.FXPrefab==null||found.FXPrefab.name!="FX_PRFB_EscortTrail_Nitrous_Chassis"||
                (int)found.ActiveAtLOD!=15||found.Sound!=""||found.PolyGroup!=""||found.BoneRef==null||found.BoneRef.Length!=1||found.BoneRef[0]!="RIG_Escort_Chassis"||
                found.RaycastDir!=RaycastDirection.None||!found.FollowNLIPSScale||found.InheritRaycastNormal)
                throw new InvalidOperationException("Native Escort Nitrous source contract changed");
            return found;
        }
        static void RequireBudget(UnitViewAttributes source,EffectDefinition d,int expected)
        {
            int count=0;if(source.WheelFXDefinitions!=null)foreach(UnitViewAttributes.WheelAttributes wheel in source.WheelFXDefinitions)
                if(wheel!=null&&wheel.WheelEffects!=null&&Array.IndexOf(wheel.WheelEffects,d.Name)>=0)count++;
            if(count!=expected)throw new InvalidOperationException("Native wheel print budget changed: "+d.Name+" count="+count);
        }

        static Transform Template(Transform source,int profile,float width)
        {
            if(HasDeferredRetirement)throw new InvalidOperationException("Private track pool retirement is pending");
            CheckNativeTemplate(source,profile);
            string key=source.GetInstanceID().ToString(CultureInfo.InvariantCulture)+"/"+width.ToString("R",CultureInfo.InvariantCulture);
            Transform existing;if(Cache.TryGetValue(key,out existing)&&existing!=null){CheckPrivateTemplate(existing,source,profile,width);return existing;}
            if(Templates==null)
            {Templates=new GameObject("EF_NativeTrackTemplates55");Templates.SetActive(false);Templates.hideFlags=HideFlags.HideAndDontSave;UnityEngine.Object.DontDestroyOnLoad(Templates);}
            GameObject go=new GameObject("EF_Private_"+NativePrefabNames[profile]+"_W"+width.ToString("R",CultureInfo.InvariantCulture));go.SetActive(false);
            go.layer=source.gameObject.layer;go.transform.parent=Templates.transform;go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.identity;go.transform.localScale=Vector3.one;
            try
            {
                FXElement native=source.GetComponent<FXElement>(),element=go.AddComponent<FXElement>();element.FXType=native.FXType;element.OnParentDespawn=native.OnParentDespawn;
                FXTrackComponent component=go.AddComponent<FXTrackComponent>();float[] lengths=AdaptLinearPolicy(profile,width);
                for(int i=0;i<LinearFields.Length;i++)NativeRead.Set(component,LinearFields[i],lengths[i]);
                NativeRead.Set(component,"m_TrackMaterial",NativeRead.Get(source.GetComponent<FXTrackComponent>(),"m_TrackMaterial"));
                FXDespawnTimerEvent nativeTimer=source.GetComponent<FXDespawnTimerEvent>(),timer=go.AddComponent<FXDespawnTimerEvent>();
                timer.EventTrigger=nativeTimer.EventTrigger;NativeRead.Set(timer,"m_DespawnTimer",NativeRead.Get(nativeTimer,"m_DespawnTimer"));
                NativeRead.Set(timer,"m_StopFXEmissionOnTimerStart",NativeRead.Get(nativeTimer,"m_StopFXEmissionOnTimerStart"));
                go.SetActive(true);CheckPrivateTemplate(go.transform,source,profile,width);Cache[key]=go.transform;return go.transform;
            }
            catch{UnityEngine.Object.DestroyImmediate(go);throw;}
        }
        // Scale every authored linear sampling/offset/fade value by the exact
        // measured footprint width ratio. Keep native textures, elapsed timers,
        // fifty-point strip and ten-thousand-vertex combine budgets unchanged.
        internal static float[] AdaptLinearPolicy(int profile,float width)
        {
            if(profile<0||profile>=NativeWidths.Length||!Finite(width)||width<=0f)throw new InvalidOperationException("Invalid measured track width");
            float scale=width/NativeWidths[profile];return new float[]{width,.5f*scale,NativeSpacing[profile]*scale,10f*scale,1f*scale};
        }
        static void CheckNativeTemplate(Transform source,int profile)
        {
            if(source==null||profile<0||profile>=NativePrefabNames.Length||source.name!=NativePrefabNames[profile]||source.childCount!=0||source.GetComponents<Component>().Length!=4)
                throw new InvalidOperationException("Native track prefab composition changed");
            FXElement element=source.GetComponent<FXElement>();FXTrackComponent track=source.GetComponent<FXTrackComponent>();FXDespawnTimerEvent timer=source.GetComponent<FXDespawnTimerEvent>();
            if(element==null||(int)element.FXType!=16||(int)element.OnParentDespawn!=1||track==null||timer==null||!track.enabled||!timer.enabled||
                timer.EventTrigger!=FXTriggerEventFlags.Ungrouped||(float)NativeRead.Get(timer,"m_DespawnTimer")!=0f||(bool)NativeRead.Get(timer,"m_StopFXEmissionOnTimerStart"))
                throw new InvalidOperationException("Native track lifecycle/category contract changed");
            float[] expected={NativeWidths[profile],.5f,NativeSpacing[profile],10f,1f};
            for(int i=0;i<LinearFields.Length;i++)if(Mathf.Abs((float)NativeRead.Get(track,LinearFields[i])-expected[i])>.00001f)
                throw new InvalidOperationException("Native track policy changed: "+LinearFields[i]);
            Material material=NativeRead.Get(track,"m_TrackMaterial") as Material;
            if(material==null||material.name!=NativeMaterialNames[profile]||material.mainTexture==null||material.mainTexture.width!=512||material.mainTexture.height!=256)
                throw new InvalidOperationException("Native track material/texture contract changed");
        }
        static void CheckPrivateTemplate(Transform copy,Transform source,int profile,float width)
        {
            if(copy==null||copy==source||Templates==null||copy.parent!=Templates.transform||copy.gameObject.activeInHierarchy||!copy.gameObject.activeSelf||copy.childCount!=0||copy.GetComponents<Component>().Length!=4)
                throw new InvalidOperationException("Private track prefab ownership differs");
            FXElement original=source.GetComponent<FXElement>(),element=copy.GetComponent<FXElement>();FXTrackComponent track=copy.GetComponent<FXTrackComponent>();FXDespawnTimerEvent timer=copy.GetComponent<FXDespawnTimerEvent>();
            if(element==null||element.FXType!=original.FXType||element.OnParentDespawn!=original.OnParentDespawn||track==null||timer==null||
                timer.EventTrigger!=FXTriggerEventFlags.Ungrouped||(float)NativeRead.Get(timer,"m_DespawnTimer")!=0f||(bool)NativeRead.Get(timer,"m_StopFXEmissionOnTimerStart")||
                !object.ReferenceEquals(NativeRead.Get(track,"m_TrackMaterial"),NativeRead.Get(source.GetComponent<FXTrackComponent>(),"m_TrackMaterial")))
                throw new InvalidOperationException("Private track native policy/material differs");
            float[] lengths=AdaptLinearPolicy(profile,width);for(int i=0;i<LinearFields.Length;i++)if(Mathf.Abs((float)NativeRead.Get(track,LinearFields[i])-lengths[i])>.00001f)
                throw new InvalidOperationException("Private track measured-width policy differs");
        }
        static void CheckDefinition(Contract c,Transform geometry)
        {
            EffectDefinition d=c.Definition,o=c.Original;
            if(d==null||o==null||object.ReferenceEquals(d,o)||object.ReferenceEquals(d.BoneRef,o.BoneRef)||ConfiguredType(d)!=o.FXType||c.Boost!=(d is NativeGroundBoostTracks55)||d.ActiveAtLOD!=o.ActiveAtLOD||d.Sound!=o.Sound||d.PolyGroup!=o.PolyGroup||
                d.FollowNLIPSScale!=o.FollowNLIPSScale||d.RaycastDir!=RaycastDirection.Down||!d.InheritRaycastNormal||d.FXPrefab!=c.PrivatePrefab||d.BoneRef==null||d.BoneRef.Length!=1||c.Alias==null||c.Parent==null||
                c.Alias.parent!=c.Parent||!c.Alias.IsChildOf(geometry)||d.BoneRef[0]!=c.Alias.name||!d.Name.StartsWith("EF_NativeTracks55_",StringComparison.Ordinal)||CountNamed(geometry,c.Alias.name)!=1)
                throw new InvalidOperationException("Private track definition/emitter/source contract changed");
        }
        static void RequirePrivate(UnitViewAttributes target,UnitViewAttributes source,Transform geometry)
        {
            if(HasDeferredRetirement)throw new InvalidOperationException("Private track pool retirement is pending");
            if(target==null||source==null||object.ReferenceEquals(target,source)||target.ModelPrefab==null||geometry==null||!geometry.IsChildOf(target.ModelPrefab.transform)||
                target.LODTunings==null||Convert.ToInt32(NativeRead.Get(target.LODTunings,"SuspensionActiveLODs"))!=0||Convert.ToInt32(NativeRead.Get(target.LODTunings,"BodyMotionActiveLODs"))!=0)
                throw new InvalidOperationException("Track prints require private geometry with native suspension/body motion disabled");
        }
        static void Commit(UnitViewAttributes target,List<Contract> prepared)
        {
            if(Contracts.ContainsKey(target.ModelPrefab))throw new InvalidOperationException("Duplicate track contract");
            List<EffectDefinition> all=new List<EffectDefinition>();if(target.UnitEffectDefs!=null)all.AddRange(target.UnitEffectDefs);
            foreach(Contract c in prepared)all.Add(c.Definition);Contracts.Add(target.ModelPrefab,prepared);target.UnitEffectDefs=all.ToArray();
        }
        static EffectDefinition Clone(EffectDefinition source)
        {EffectDefinition copy=(EffectDefinition)Memberwise.Invoke(source,null);copy.BoneRef=source.BoneRef==null?new string[0]:(string[])source.BoneRef.Clone();return copy;}
        internal static EffectDefinition CloneForBoost(EffectDefinition source)
        {
            if(source==null)throw new ArgumentNullException("source");
            NativeGroundBoostTracks55 copy=new NativeGroundBoostTracks55();
            // Preserve every base/private field, including future serialized
            // policies. Arrays are private copies; Unity asset references stay
            // immutable shared references until the caller supplies its own FX.
            for(Type t=typeof(EffectDefinition);t!=null&&t!=typeof(object);t=t.BaseType)
                foreach(FieldInfo field in t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                {object value=field.GetValue(source);Array array=value as Array;field.SetValue(copy,array==null?value:array.Clone());}
            return copy;
        }
        internal static FrillType BoostTypeForPolicy(bool managerAvailable,bool nativeWheelFrillEnabled)
        {return managerAvailable&&nativeWheelFrillEnabled?FrillType.Nitrous:FrillType.Disabled;}
        internal static FrillType ConfiguredType(EffectDefinition definition)
        {NativeGroundBoostTracks55 boost=definition as NativeGroundBoostTracks55;return boost==null?definition.FXType:boost.ConfiguredType;}
        static GameObject NewRoot(Transform geometry)
        {GameObject root=new GameObject(RootName);root.layer=geometry.gameObject.layer;root.transform.parent=geometry;root.transform.localPosition=Vector3.zero;root.transform.localRotation=Quaternion.identity;root.transform.localScale=Vector3.one;return root;}
        static int CountNamed(Transform geometry,string name)
        {int n=0;foreach(Transform t in geometry.GetComponentsInChildren<Transform>(true))if(t.name==name)n++;return n;}
        static bool Finite(float x){return !float.IsNaN(x)&&!float.IsInfinity(x);}
        static string Widths(List<Contract> contracts)
        {List<string> widths=new List<string>();foreach(Contract c in contracts)widths.Add(c.Width.ToString("R",CultureInfo.InvariantCulture));return string.Join("/",widths.ToArray());}
        static void DestroyAliases(List<GameObject> aliases)
        {foreach(GameObject alias in aliases)if(alias!=null)UnityEngine.Object.DestroyImmediate(alias);}
        // Native UnitFX reset may detach an instance before its delayed despawn.
        // Retire each exact private prefab pool synchronously before destroying
        // a template. Native FXTrackComponent.DoDespawnOps still schedules its
        // already-created TrackMesh for the original 1.5*persistence lifetime.
        internal static void Reset()
        {
            Contracts.Clear();HashSet<Transform> retired=new HashSet<Transform>();HasDeferredRetirement=false;
            foreach(Transform template in Cache.Values)
            {
                if(object.ReferenceEquals(template,null)||retired.Contains(template))continue;
                try
                {
                    RhodesThermalVisuals.RetirePrivatePool(template);
                    RhodesThermalVisuals.ForgetTemplateTracker(template);
                    if(template!=null)UnityEngine.Object.DestroyImmediate(template.gameObject);
                    retired.Add(template);
                }
                catch(Exception error)
                {HasDeferredRetirement=true;Runtime.Log("RHODES_NATIVE_TRACKS55_RETIRE_DEFERRED exactPrivateTemplateRetained=True; reason="+error.Message);}
            }
            List<string> remove=new List<string>();foreach(KeyValuePair<string,Transform> pair in Cache)
                if(object.ReferenceEquals(pair.Value,null)||retired.Contains(pair.Value))remove.Add(pair.Key);
            foreach(string key in remove)Cache.Remove(key);
            if(HasDeferredRetirement)return;
            Cache.Clear();if(Templates!=null)UnityEngine.Object.DestroyImmediate(Templates);Templates=null;
        }
    }
}
