using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BBI.Core.ComponentModel;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using EndfieldDoK.ModelIntake;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public sealed class FactionModelBinding:MonoBehaviour
    {
        public string UnitKey;
        public Transform GeometryRoot;
        public Transform[] Nodes;
        public Quaternion[] RestRotations;
        public string[] Names;
        public Renderer[] Geometry,LodProbes;
        public Renderer NativeProbeDonor;
        public Bounds ModelBounds;
        public bool Air;
        public int NativeWheelActiveLODs;
        public int NativeTurretActiveLODs;
        public bool NativeAlwaysHighLOD;
        public int[] RigNodes,RigRoles,RigLods;
        public Vector3[] RigAxes;
        public float[] RigWheelRadii,RigAngleScales;
        public float SteeringDamping;
        public Transform PrimaryFxEmitter;
        public Vector3 GeometryRestPosition,SelectionRestCenter;
        public Quaternion GeometryRestRotation=Quaternion.identity;
        public Transform AuthoredMortar,MortarYaw,MortarPitch,MortarEmitter;
    }

    // All file decoding, material construction and rig discovery happen once at
    // private registry/prefab construction, never during an individual unit tick.
    public static class FactionUnitModels
    {
        static GameObject Templates;
        static Mesh Empty;
        static readonly List<RhodesLoadedModel> Loaded=new List<RhodesLoadedModel>();
        static readonly Dictionary<string,Bounds> BoundsByKey=new Dictionary<string,Bounds>(StringComparer.Ordinal);
        static readonly ModCore.DeferredBindingQueue<Entity,UnitView>.BindAttempt BindSpawnedFaction=TryBindSpawnedFaction;
        public static Bounds BoundsFor(string unitKey)
        {
            Bounds value;
            if(BoundsByKey.TryGetValue(unitKey,out value))return value;
            RhodesModelPackage package=RhodesModelPackage.Read(PackagePath(unitKey));
            Vector3 min=Vector(package.Visual.Min),max=Vector(package.Visual.Max);
            value=new Bounds((min+max)*.5f,max-min);BoundsByKey.Add(unitKey,value);return value;
        }
        static string PackagePath(string unitKey)
        {
            if(unitKey==null||unitKey.IndexOfAny(new char[]{'/','\\',':'})>=0)throw new ArgumentException("Invalid faction model key");
            return Path.Combine(Path.Combine(Path.Combine(CustomUnits.AssetDirectory,"faction_v01"),unitKey),"model.bin");
        }
        // Probe participation is a renderer property, not a material setting.
        // Match the real native harvester once while building its shared model
        // template; do not alter authored textures, F0, lighting or other units.
        internal static void AdoptHarvesterRendererProbes(Renderer[] renderers,Renderer donor)
        {
            if(renderers==null||donor==null)throw new InvalidOperationException("Harvester native probe donor is missing");
            UnityEngine.Rendering.ReflectionProbeUsage reflection=donor.reflectionProbeUsage;
            bool lightProbes=donor.useLightProbes;int count=0;
            foreach(Renderer renderer in renderers)
            {
                if(renderer==null)continue;
                renderer.reflectionProbeUsage=reflection;renderer.useLightProbes=lightProbes;count++;
            }
            Runtime.Log("U01_NATIVE_RENDERER_PROBES source="+donor.name+"; reflection="+reflection+"; useLightProbes="+lightProbes+
                "; rendererCount="+count+"; templateConstructionOnly=True; noColourOrMaterialWrites=True; blueCauseNotEstablished=True");
        }
        public static void ConfigureView(string unitKey,UnitViewAttributes target,UnitViewAttributes source)
        {
            if(target==null||source==null||source.ModelPrefab==null||object.ReferenceEquals(target,source))
                throw new InvalidOperationException("Faction model requires a private view and real donor prefab");
            if(Templates==null)
            {
                Templates=new GameObject("EF_Faction_ModelTemplates");Templates.SetActive(false);Templates.hideFlags=HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(Templates);
                Empty=new Mesh();Empty.name="EF_Faction_BlankDonor";Empty.hideFlags=HideFlags.HideAndDontSave;
            }
            GameObject prefab=(GameObject)UnityEngine.Object.Instantiate(source.ModelPrefab);prefab.name=unitKey+"_Prefab";
            prefab.transform.parent=Templates.transform;prefab.transform.localPosition=Vector3.zero;
            prefab.transform.localRotation=source.ModelPrefab.transform.localRotation;prefab.transform.localScale=source.ModelPrefab.transform.localScale;
            Material native=null;Renderer nativeRenderer=null;int layer=prefab.layer;
            foreach(Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if(!(renderer is MeshRenderer)&&!(renderer is SkinnedMeshRenderer))continue;
                if(native==null&&renderer.sharedMaterial!=null&&renderer.sharedMaterial.shader!=null&&renderer.sharedMaterial.shader.isSupported)
                {native=renderer.sharedMaterial;nativeRenderer=renderer;layer=renderer.gameObject.layer;}
                SkinnedMeshRenderer skin=renderer as SkinnedMeshRenderer;if(skin!=null)skin.sharedMesh=Empty;
                MeshFilter mesh=renderer.GetComponent<MeshFilter>();if(mesh!=null)mesh.sharedMesh=Empty;
                renderer.enabled=false;
            }
            if(native==null)throw new InvalidOperationException("Faction donor has no supported geometry material: "+unitKey);
            foreach(LODGroup group in prefab.GetComponentsInChildren<LODGroup>(true))group.enabled=false;
            // Donor running lights/dust belong to a different body. Native weapon
            // FX is still emitted by the private view at its remapped hardpoint.
            foreach(ParticleSystem particles in prefab.GetComponentsInChildren<ParticleSystem>(true))UnityEngine.Object.DestroyImmediate(particles.gameObject);
            foreach(Light light in prefab.GetComponentsInChildren<Light>(true))UnityEngine.Object.DestroyImmediate(light);
            RhodesLoadedModel loaded=RhodesModelBuilder.LoadSmallUnit(PackagePath(unitKey),native,layer);Loaded.Add(loaded);
            if(unitKey==FactionUnits.HarvesterKey)AdoptHarvesterRendererProbes(loaded.Renderers,nativeRenderer);
            loaded.AttachInMetreSpace(prefab.transform);loaded.Root.name="EF_Faction_AuthoredGeometry";loaded.Root.SetActive(true);
            FactionModelBinding binding=prefab.AddComponent<FactionModelBinding>();binding.UnitKey=unitKey;
            if(unitKey==FactionUnits.HarvesterKey)binding.NativeProbeDonor=nativeRenderer;
            binding.GeometryRoot=loaded.Root.transform;binding.Nodes=loaded.Nodes;binding.Geometry=loaded.Renderers;
            binding.ModelBounds=BoundsFor(unitKey);binding.Air=unitKey=="RI_U08_TILT_STRIKE";
            // Ground and U08 both retain the native wrapper/controller. The
            // inspected C_Interceptor donor has the same three authored levels.
            NativeUnitLodAdapter.AdoptGeometry(prefab.GetComponent<UnitView>(),loaded.LodGroup);
            binding.NativeAlwaysHighLOD=target.AlwaysHighLOD;
            binding.NativeWheelActiveLODs=Convert.ToInt32(NativeRead.Get(target.LODTunings,"WheelActuationActiveLODs"));
            binding.NativeTurretActiveLODs=Convert.ToInt32(NativeRead.Get(target.LODTunings,"TurretActuationActiveLODs"));
            binding.SteeringDamping=target.AnimationDefinitions==null?0f:Mathf.Max(0f,target.AnimationDefinitions.SteeringDamping);
            FactionRigContract rig=FactionRigContract.ReadVerified(Path.Combine(Path.GetDirectoryName(PackagePath(unitKey)),"rig.tsv"),loaded.Package.ContentSha256,unitKey,loaded.Package.Nodes);
            binding.RigNodes=new int[rig.Joints.Length];binding.RigRoles=new int[rig.Joints.Length];binding.RigLods=new int[rig.Joints.Length];binding.RigAxes=new Vector3[rig.Joints.Length];
            binding.RigWheelRadii=new float[rig.Joints.Length];binding.RigAngleScales=new float[rig.Joints.Length];
            for(int joint=0;joint<rig.Joints.Length;joint++)
            {
                FactionRigJoint item=rig.Joints[joint];binding.RigNodes[joint]=item.Node;binding.RigRoles[joint]=item.Role;binding.RigLods[joint]=item.Lod;
                char axis=item.Axis[item.Axis.Length-1];Vector3 direction=axis=='X'?Vector3.right:axis=='Y'?Vector3.up:Vector3.forward;
                binding.RigAxes[joint]=item.Axis[0]=='-'?-direction:direction;
                binding.RigWheelRadii[joint]=item.WheelRadius;binding.RigAngleScales[joint]=item.AngleScale;
            }
            binding.GeometryRestPosition=binding.GeometryRoot.localPosition;binding.GeometryRestRotation=binding.GeometryRoot.localRotation;
            binding.Names=new string[loaded.Package.Nodes.Length];for(int i=0;i<binding.Names.Length;i++)binding.Names[i]=loaded.Package.Nodes[i].Name;
            binding.RestRotations=new Quaternion[binding.Nodes.Length];for(int i=0;i<binding.Nodes.Length;i++)binding.RestRotations[i]=binding.Nodes[i].localRotation;
            binding.LodProbes=new Renderer[3];
            foreach(Renderer renderer in binding.Geometry)
            {
                string name=renderer.gameObject.name;
                for(int i=0;i<3;i++)if(name.StartsWith("GEO_L"+i+"_",StringComparison.Ordinal)&&binding.LodProbes[i]==null)binding.LodProbes[i]=renderer;
            }
            Transform muzzle=FindMarker(binding,unitKey=="RI_U03_ENGINEERING"?"HP_Repair_Beam":unitKey=="RI_U04_LIGHT_ESCORT"?"MUZZLE_MAIN":unitKey=="RI_U07_AIR_DEFENCE"?"HP_Missile":"Muzzle");
            if(muzzle==null&&unitKey=="RI_U04_LIGHT_ESCORT")muzzle=FindMarker(binding,"MUZZLE_");
            if(unitKey==FactionUnits.ArtilleryKey)muzzle=FindArtilleryMarker(binding,"HP_U09_Muzzle_01");
            if(muzzle==null&&binding.Air)muzzle=FindMarker(binding,"HP_A08_Missile");
            if(muzzle!=null)
            {
                GameObject emission=new GameObject("EF_Faction_Muzzle");emission.layer=layer;emission.transform.parent=muzzle;
                emission.transform.localPosition=Vector3.zero;emission.transform.localRotation=Quaternion.identity;
                binding.PrimaryFxEmitter=emission.transform;
                // The entire view graph is already private, including this array.
                if(target.TurretDefinitions!=null)for(int i=0;i<target.TurretDefinitions.Length;i++)
                {
                    UnitViewAttributes.TurretAttributes turret=target.TurretDefinitions[i];
                    turret.CenterEmissionBone=emission.name;turret.FXEmissionBones=new string[]{emission.name};
                }
                if(unitKey=="RI_U03_ENGINEERING")
                {
                    GameObject yawBone=new GameObject("EF_Faction_RepairYaw");yawBone.transform.parent=prefab.transform;
                    yawBone.transform.position=muzzle.position;yawBone.transform.localRotation=Quaternion.identity;
                    GameObject pitchBone=new GameObject("EF_Faction_RepairPitch");pitchBone.transform.parent=yawBone.transform;
                    pitchBone.transform.localPosition=Vector3.zero;pitchBone.transform.localRotation=Quaternion.identity;
                    if(target.TurretDefinitions!=null)foreach(UnitViewAttributes.TurretAttributes turret in target.TurretDefinitions)
                    {turret.BoneName=yawBone.name;turret.PitchBoneName=pitchBone.name;}
                }
                if(unitKey==FactionUnits.ArtilleryKey)
                {
                    Transform authoredYaw=FindArtilleryMarker(binding,"PIVOT_U09_TurretYaw"),authoredPitch=FindArtilleryMarker(binding,"PIVOT_U09_GunPitch");
                    if(authoredYaw==null||authoredPitch==null||target.TurretDefinitions==null||target.TurretDefinitions.Length!=1)
                        throw new InvalidOperationException("U09 requires the authored single-turret hierarchy");
                    GameObject yawBone=new GameObject("EF_Faction_ArtilleryYaw");yawBone.transform.parent=prefab.transform;
                    yawBone.transform.position=authoredYaw.position;yawBone.transform.localRotation=Quaternion.identity;
                    GameObject pitchBone=new GameObject("EF_Faction_ArtilleryPitch");pitchBone.transform.parent=yawBone.transform;
                    pitchBone.transform.position=authoredPitch.position;pitchBone.transform.localRotation=Quaternion.identity;
                }
                if(unitKey=="RI_U07_AIR_DEFENCE")
                {
                    Transform mortar=FindMarker(binding,"HP_Mortar");
                    if(mortar!=null)
                    {
                        GameObject point=new GameObject("EF_Faction_Mortar");point.transform.parent=mortar;
                        point.transform.localPosition=Vector3.zero;point.transform.localRotation=Quaternion.identity;
                        GameObject yawBone=new GameObject("EF_Faction_MortarYaw");yawBone.transform.parent=prefab.transform;
                        yawBone.transform.position=mortar.position;yawBone.transform.localRotation=Quaternion.identity;
                        GameObject pitchBone=new GameObject("EF_Faction_MortarPitch");pitchBone.transform.parent=yawBone.transform;
                        pitchBone.transform.localPosition=Vector3.zero;pitchBone.transform.localRotation=Quaternion.identity;
                        point.transform.parent=pitchBone.transform;point.transform.localPosition=Vector3.zero;point.transform.localRotation=Quaternion.identity;
                        binding.AuthoredMortar=mortar;binding.MortarYaw=yawBone.transform;binding.MortarPitch=pitchBone.transform;binding.MortarEmitter=point.transform;
                        if(target.TurretDefinitions==null||target.TurretDefinitions.Length!=2)
                            throw new InvalidOperationException("AA and mortar require separate private turret bindings");
                        UnitViewAttributes.TurretAttributes turret=target.TurretDefinitions[1];
                        turret.BoneName=yawBone.name;turret.PitchBoneName=pitchBone.name;
                        turret.CenterEmissionBone=point.name;turret.FXEmissionBones=new string[]{point.name};
                    }
                }
            }
            if(unitKey==FactionUnits.TiltStrikeKey)FactionUnits.ConfigureEmbarkedSkillView(target,binding.GeometryRoot);
            target.NLIPSScale=new AnimationCurve(new Keyframe(0f,1f),new Keyframe(100000f,1f));
            target.UnitEffectDefs=unitKey==FactionUnits.HarvesterKey?FactionHarvesterEffects.Configure(target,source,binding):new BBI.Unity.Core.World.EffectDefinition[0];
            target.WheelFXDefinitions=new UnitViewAttributes.WheelAttributes[0];
            target.TreadDefinitions=new UnitViewAttributes.TreadAttributes[0];
            if(binding.Air||unitKey==FactionUnits.ArtilleryKey)
            {
                // U08 has authored rotor actuation, not the donor jet's state
                // machine. Its inherited takeoff/afterburner animation effects
                // must not recreate engine flames after particle cleanup.
                object animations=target.AnimationDefinitions;
                if(animations!=null)
                {
                    foreach(string field in new string[]{"EnableMovementLayerX","EnableMovementLayerY","EnableMovementLayerZ","EnableFiringLayer"})NativeRead.Set(animations,field,false);
                    foreach(string field in new string[]{"ResearchUpgrades","AbilityStates","AnimationTriggers","ProductionEffects","ProductionAnimations"})
                    {
                        Array values=NativeRead.Get(animations,field) as Array;
                        if(values!=null)NativeRead.Set(animations,field,Array.CreateInstance(values.GetType().GetElementType(),0));
                    }
                }
                foreach(Animator animator in prefab.GetComponentsInChildren<Animator>(true))animator.enabled=false;
                foreach(Animation animation in prefab.GetComponentsInChildren<Animation>(true))animation.enabled=false;
            }
            object tuning=target.LODTunings;
            if(tuning!=null&&object.ReferenceEquals(tuning,source.LODTunings))
                throw new InvalidOperationException("Faction LOD settings still alias native donor");
            if(tuning!=null)foreach(string field in new string[]{"SuspensionActiveLODs","WheelActuationActiveLODs","BodyMotionActiveLODs"})
            {object old=NativeRead.Get(tuning,field);NativeRead.Set(tuning,field,Enum.ToObject(old.GetType(),0));}
            // Ground vehicles retain the donor's icon-only sensor policy.
            // Keeping their full authored hierarchy alive here multiplies
            // native visibility/property-block work by hundreds of renderers.
            if(binding.Air&&tuning!=null)NativeRead.Set(tuning,"ForceIconOnlyInSensors",false);
            if(binding.Air&&tuning!=null)
            {
                LODSettingsAsset lod=NativeRead.Get(tuning,"LODSettings") as LODSettingsAsset;
                if(lod==null)throw new InvalidOperationException("U08 private LOD settings missing");
                object settings=NativeRead.Get(lod,"m_Settings");
                if(settings==null)throw new InvalidOperationException("U08 LOD screen-size thresholds missing");
                // The entire asset closure was privately cloned by FactionUnits.
                // Keep the low mesh at smaller projected sizes; high/medium are
                // left unchanged so this does not retain full detail at range.
                float threshold=(float)NativeRead.Get(settings,"LowLODThreshold");
                NativeRead.Set(settings,"LowLODThreshold",Mathf.Max(.0001f,threshold*.4f));
                LODGroup nativeGroup=prefab.GetComponent<LODGroup>();LOD[] levels=nativeGroup.GetLODs();
                levels[levels.Length-1].screenRelativeTransitionHeight=Mathf.Max(.0001f,levels[levels.Length-1].screenRelativeTransitionHeight*.4f);nativeGroup.SetLODs(levels);
                Runtime.Log("RHODES_U08_ROTORMOTION_RANGE lowScreenThresholdMultiplier=0.4; nearMidThresholdsUnchanged=True; allThreeLodsAnimated=True; minimumVisibleUpdateHz=20; originalAircraftSettingsUnchanged=True");
            }
            target.ModelPrefab=prefab;
            if(!binding.Air)
            {
                NativeGroundEffects54.AppendFaction(target,source,binding,loaded.Package.Tracks);
                NativeGroundTracks55.AppendFaction(target,source,binding,loaded.Package.Tracks);
                NativeGroundStatuses55.AppendFaction(target,source,binding);
            }
            UnitView unitView=prefab.GetComponent<UnitView>();
            if(unitView==null)throw new InvalidOperationException("Faction wrapper lost native UnitView");
            BoxCollider selection=unitView.SelectionCollider;
            if(selection==null)throw new InvalidOperationException("Faction donor lost native selection collider");
            Bounds colliderBounds=new Bounds();bool first=true;
            Bounds body=binding.ModelBounds;
            for(int corner=0;corner<8;corner++)
            {
                Vector3 p=new Vector3((corner&1)==0?body.min.x:body.max.x,(corner&2)==0?body.min.y:body.max.y,(corner&4)==0?body.min.z:body.max.z);
                p=selection.transform.InverseTransformPoint(binding.GeometryRoot.TransformPoint(p));
                if(first){colliderBounds=new Bounds(p,Vector3.zero);first=false;}else colliderBounds.Encapsulate(p);
            }
            selection.center=colliderBounds.center;selection.size=colliderBounds.size;
            binding.SelectionRestCenter=selection.center;
            target.FocusSettings.ClosestCameraFocus=Mathf.Max(3f,body.size.z*1.4f);
            NativeRead.Set(unitView,"mGeoRenderObjects",binding.Geometry);
            Runtime.Log("FACTION_MODEL_PREPARED key="+unitKey+"; nodes="+binding.Nodes.Length+"; meshes="+binding.Geometry.Length+
                "; threeAuthoredLODs=True; designUnitToGameUnit=1; nativeDonorUnchanged=True; actualVisualAcceptancePending=True");
        }
        static Transform FindMarker(FactionModelBinding binding,string part)
        {
            for(int i=0;i<binding.Names.Length;i++)if(binding.Names[i].StartsWith("L0::",StringComparison.Ordinal)&&
                binding.Names[i].IndexOf(part,StringComparison.OrdinalIgnoreCase)>=0&&binding.Names[i].IndexOf("RigidMesh",StringComparison.Ordinal)<0)
                return binding.Nodes[i];return null;
        }
        static Transform FindArtilleryMarker(FactionModelBinding binding,string name)
        {
            Transform result=null;
            for(int i=0;i<binding.Names.Length;i++)if(binding.Names[i]==name)
            {if(result!=null)throw new InvalidOperationException("Duplicate U09 authored marker "+name);result=binding.Nodes[i];}
            if(result==null)throw new InvalidOperationException("Missing U09 authored marker "+name);return result;
        }
        public static void MainTick(){UnitBindingRouter.BindFaction(BindSpawnedFaction);}
        static bool TryBindSpawnedFaction(Entity entity,UnitView view)
        {
            if(view==null||!entity.Equals(view.EntityID))return true;
            if(!view.gameObject.activeInHierarchy||view.SimStateInterpolator==null)return false;
            FactionModelBinding binding=view.GetComponent<FactionModelBinding>();if(binding==null)return false;
            FactionUnitModelDriver driver=view.GetComponent<FactionUnitModelDriver>();
            if(driver!=null&&driver.PresentationFailed)return true;
            if(driver==null)driver=view.gameObject.AddComponent<FactionUnitModelDriver>();
            try{driver.Bind(view,binding);}
            catch(Exception error){driver.QuarantineBinding(view,"faction_bind",error);}
            return true;
        }
        public static void Reset()
        {
            BoundsByKey.Clear();
            foreach(RhodesLoadedModel model in Loaded)model.Dispose();Loaded.Clear();
            FactionTrackLoader.ClearCaches();
            if(Templates!=null)UnityEngine.Object.DestroyImmediate(Templates);Templates=null;
            if(Empty!=null)UnityEngine.Object.DestroyImmediate(Empty);Empty=null;
            NativeGroundTracks55.Reset();
            NativeGroundStatuses55.Reset();
            NativeGroundActivation56.Reset();
        }
        static Vector3 Vector(float[] v){return new Vector3(v[0],v[1],v[2]);}
    }

    public sealed class FactionUnitModelDriver:IsolatedUnitPresentation
    {
        UnitView View;FactionModelBinding Binding;Entity Entity;bool Ready,PreviousReady,GroundReported;
        Transform BoundGeometryRoot;
        int AirPresentationFrame=-1;
        FactionTrackLiveRig Tracks;
        FactionRailgunRecoil Recoil;
        FactionArtilleryMotion ArtilleryMotion;
        FactionArtilleryElevation ArtilleryElevation;
        NativeGroundActivation56 GroundFX;
        readonly Transform[] NativeAimYaw=new Transform[2],NativeAimPitch=new Transform[2];
        bool AimCacheReady;float NextAimCacheAttempt;
        Vector3 PreviousPosition,PreviousForward;float Distance,TurnDistance,MotionElapsed,DisplayedSteering;
        bool GroundDisplayReady,NativeGroundAuthorityReady,HasAimJoints;Vector3 PreviousGroundAnchor;
        FactionNativeGroundCalibration GroundCalibration;
        readonly bool[] AimDisplayReady=new bool[2],AimFallbackReported=new bool[2];
        readonly int[] AimResolvedFrame={-1,-1};readonly Vector3[] AimDirections=new Vector3[2];
        readonly float[] AimYaw=new float[2],AimElevation=new float[2],AimYawRate=new float[2],AimPitchRate=new float[2];
        readonly List<Joint> Joints=new List<Joint>();
        sealed class Joint
        {
            internal Transform Transform;internal Quaternion Rest;internal Vector3 Axis;internal int Kind,Lod;internal float Angle,Radius,Side,AngleScale,LastWrittenAngle=float.NaN;internal bool CriticalAim;
        }
        public void Bind(UnitView view,FactionModelBinding binding)
        {
            if(Ready&&View==view&&Entity.Equals(view.EntityID)&&Binding==binding&&BoundGeometryRoot==binding.GeometryRoot)return;
            try
            {
            EndfieldDoK.ModelIntake.RhodesRigidBatches.RebindLive(binding.GeometryRoot);
            FactionTrackLiveRig tracks=FactionTrackLoader.RebindLive(binding.GeometryRoot,binding.UnitKey);
            if(Ready)BvmNativeState.Unbind(Entity);
            if(ArtilleryMotion!=null)ArtilleryMotion.Reset();
            if(ArtilleryElevation!=null)ArtilleryElevation.Reset();
            GroundFX=NativeGroundActivation56.BindFaction(view,binding);
            Tracks=tracks;
            View=view;Binding=binding;BoundGeometryRoot=binding.GeometryRoot;Entity=view.EntityID;Ready=true;PreviousReady=GroundReported=false;Distance=TurnDistance=MotionElapsed=DisplayedSteering=0f;GroundDisplayReady=HasAimJoints=false;Joints.Clear();
            AirPresentationFrame=-1;NativeGroundAuthorityReady=false;
            if(!binding.Air&&!FactionNativeGroundPose.TryCalibrate(view.SuspensionSettleYOffset,binding.ModelBounds.min.y,binding.GeometryRoot.lossyScale.y,out GroundCalibration))
                throw new InvalidOperationException("Faction native ground datum is not finite");
            AimCacheReady=false;NextAimCacheAttempt=0f;for(int i=0;i<2;i++){NativeAimYaw[i]=null;NativeAimPitch[i]=null;AimDisplayReady[i]=AimFallbackReported[i]=false;AimResolvedFrame[i]=-1;AimYawRate[i]=AimPitchRate[i]=0f;}
            foreach(WeaponBinding weapon in view.UnitAttrs.WeaponLoadout)
                if(weapon!=null&&weapon.Weapon!=null&&weapon.Weapon.Turret!=null&&weapon.TurretIndex>=0&&weapon.TurretIndex<2)
                    AimYawRate[weapon.TurretIndex]=Mathf.Max(AimYawRate[weapon.TurretIndex],Fixed64.UnsafeFloatValue(weapon.Weapon.Turret.RotationSpeed));
            object spawn=NativeRead.Get(view,"mSpawnSettings");UnitViewAttributes settings=NativeRead.Get(spawn,"UnitViewSettings") as UnitViewAttributes;
            if(settings!=null&&settings.TurretDefinitions!=null)for(int i=0;i<Math.Min(2,settings.TurretDefinitions.Length);i++)AimPitchRate[i]=Mathf.Max(0f,settings.TurretDefinitions[i].MaxPitchDegreesPerSecond);
            BvmNativeState.Bind(Entity);
            Recoil=FactionRailgunRecoil.Bind(binding,BvmNativeState.GetRoundSequence(Entity));
            ArtilleryMotion=FactionArtilleryMotion.Bind(binding,BvmNativeState.GetRoundSequence(Entity));
            ArtilleryElevation=FactionArtilleryElevation.Bind(view,binding,BvmNativeState.GetRoundSequence(Entity));
            if(binding.Air)RhodesTiltrotorFlightControl.Bind(view);
            NativeRead.Set(view,"mGeoRenderObjects",binding.Geometry);NativeRead.Call(view,"UpdateUnitMaterialPropertyBlock",true);
            RhodesTeamColours.RecordHarvesterColours(view,binding.UnitKey,binding.Geometry,binding.NativeProbeDonor);
            for(int joint=0;joint<binding.RigNodes.Length;joint++)
            {
                int i=binding.RigNodes[joint],kind=binding.RigRoles[joint],lod=binding.RigLods[joint];
                Transform t=binding.Nodes[i];
                if(t==null||!t.IsChildOf(binding.GeometryRoot))throw new InvalidOperationException("Faction rig instance escaped its exact authored hierarchy");
                HasAimJoints|=kind==2||kind==3;
                if(kind==1&&Tracks!=null&&Tracks.OwnsWheel(t))continue; // FTK owns exact radii and side offsets.
                Vector3 local=binding.GeometryRoot.InverseTransformPoint(t.position);
                float radius=binding.UnitKey=="RI_U04_LIGHT_ESCORT"?.773f:Mathf.Max(.4f,binding.ModelBounds.size.y*.10f);
                if(binding.RigWheelRadii[joint]>0f)radius=binding.RigWheelRadii[joint];
                bool criticalAim=!binding.Air&&(kind==2||kind==3)&&
                    ((binding.PrimaryFxEmitter!=null&&binding.PrimaryFxEmitter.IsChildOf(t))||
                    (binding.AuthoredMortar!=null&&binding.AuthoredMortar.IsChildOf(t))||
                    (binding.MortarEmitter!=null&&binding.MortarEmitter.IsChildOf(t)));
                Joints.Add(new Joint{Transform=t,Rest=binding.RestRotations[i],Kind=kind,Lod=lod,Axis=binding.RigAxes[joint],Radius=radius,Side=Mathf.Sign(local.x),AngleScale=binding.RigAngleScales[joint],CriticalAim=criticalAim});
            }
            Runtime.Log("FACTION_VIEW_BOUND key="+binding.UnitKey+"; entity="+Entity+"; cachedJoints="+Joints.Count+
                "; nativeVisibility=True; presentationOnly=True; animatedBelts="+(Tracks==null?0:Tracks.BeltCount)+"; beltLinkTravelImplemented="+(Tracks!=null)+"; runtimeVisualAcceptancePending=True");
            }
            catch
            {
                if(View!=null)BvmNativeState.Unbind(Entity);
                if(Binding!=null&&Binding.Air)RhodesTiltrotorFlightControl.Unbind(Entity);
                AirPresentationFrame=-1;Ready=PreviousReady=GroundDisplayReady=false;throw;
            }
        }
        void CacheNativeAim()
        {
            if(AimCacheReady)
            {
                bool primary=NativeAimYaw[0]!=null||NativeAimPitch[0]!=null,secondary=NativeAimYaw[1]!=null||NativeAimPitch[1]!=null;
                if(!HasAimJoints||primary&&(Binding.UnitKey!="RI_U07_AIR_DEFENCE"||secondary))return;
                AimCacheReady=false;
            }
            if(Binding.UnitKey=="RI_U01_HARVESTER"||Binding.UnitKey=="RI_U02_FIELD_SURVEY"){AimCacheReady=true;return;}
            if(Time.realtimeSinceStartup<NextAimCacheAttempt)return;NextAimCacheAttempt=Time.realtimeSinceStartup+.25f;
            Array turrets=NativeRead.Get(View,"mTurretData") as Array;if(turrets==null)return;
            int count=Math.Min(2,turrets.Length);
            for(int i=0;i<count;i++)
            {
                object turret=turrets.GetValue(i);
                if(turret==null)continue;
                object yaw=NativeRead.Get(turret,"Yaw"),pitch=NativeRead.Get(turret,"Pitch");
                NativeAimYaw[i]=yaw==null?null:NativeRead.Get(yaw,"Bone") as Transform;
                NativeAimPitch[i]=pitch==null?null:NativeRead.Get(pitch,"Bone") as Transform;
            }
            AimCacheReady=count>0&&(NativeAimYaw[0]!=null||NativeAimPitch[0]!=null)&&
                (Binding.UnitKey!="RI_U07_AIR_DEFENCE"||count>1&&(NativeAimYaw[1]!=null||NativeAimPitch[1]!=null));
        }
        Vector3 NativeAimDirection(BvmNativeState.Snapshot state,int turret)
        {
            if(AimResolvedFrame[turret]==Time.frameCount)return AimDirections[turret];
            Transform bone=NativeAimPitch[turret]!=null?NativeAimPitch[turret]:NativeAimYaw[turret];
            if(bone!=null)
            {
                Vector3 direction=bone.forward;
                if(FactionGroundPose.Finite(Pose(direction))&&direction.sqrMagnitude>.000001f)
                {
                    AimYaw[turret]=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
                    AimElevation[turret]=Mathf.Atan2(direction.y,new Vector2(direction.x,direction.z).magnitude)*Mathf.Rad2Deg;
                    AimDisplayReady[turret]=true;return FinishAim(turret,direction);
                }
            }
            float angle=state.TurretYawDegrees;
            float[] rotations=View.SimStateInterpolator.TurretRotations;
            if(turret>0&&rotations!=null&&rotations.Length>turret)angle=rotations[turret];
            if(!FactionGroundPose.Finite(angle))angle=Mathf.Atan2(state.Forward.x,state.Forward.z)*Mathf.Rad2Deg;
            // Preserve the previously available primary pitch if its own bone
            // cache is delayed; turret1 has no separate Snapshot pitch value.
            float elevation=turret==0&&state.HasTurretPitch?state.TurretPitchDegrees:0f;
            if(!AimDisplayReady[turret]){AimYaw[turret]=angle;AimElevation[turret]=elevation;AimDisplayReady[turret]=true;}
            else
            {
                float delta=Runtime.PresentationDeltaTime;
                AimYaw[turret]=(float)FactionPresentationMath.AngleStep(AimYaw[turret],angle,delta,AimYawRate[turret]);
                AimElevation[turret]=Mathf.MoveTowards(AimElevation[turret],elevation,Mathf.Max(0f,delta)*AimPitchRate[turret]);
            }
            if(HasAimJoints&&!AimFallbackReported[turret])
            {AimFallbackReported[turret]=true;Runtime.Log("FACTION_AIM_FALLBACK entity="+Entity+"; turret="+turret+"; source=simulation_target; nativeRateLimited=True; runtimeVisualAcceptancePending=True");}
            return FinishAim(turret,Vector(FactionGroundPose.WorldAim(AimYaw[turret],AimElevation[turret])));
        }
        Vector3 FinishAim(int turret,Vector3 direction)
        {AimResolvedFrame[turret]=Time.frameCount;AimDirections[turret]=direction;return direction;}
        static FactionPoseVector Pose(Vector3 v){return new FactionPoseVector(v.x,v.y,v.z);}
        static Vector3 Vector(FactionPoseVector v){return new Vector3((float)v.X,(float)v.Y,(float)v.Z);}
        void AlignGround(BvmNativeState.Snapshot state)
        {
            // UnitView already owns terrain height, ground orientation and any
            // Rigidbody interpolation. Read its displayed transform as one pose;
            // never mix its fixed-step Position with a fresh raw ground normal.
            Transform native=View.transform,root=Binding.GeometryRoot;
            Vector3 nativePosition=native.position;
            Quaternion nativeRotation=native.rotation;
            Vector3 right=nativeRotation*Vector3.right,up=nativeRotation*Vector3.up,forward=nativeRotation*Vector3.forward;
            FactionGroundPoseResult pose;
            if(!FactionNativeGroundPose.TryAdapt(Pose(nativePosition),Pose(right),Pose(up),Pose(forward),GroundCalibration,out pose))return;
            bool nativeSample=NativeGroundAuthorityReady;
            if(!NativeGroundAuthorityReady)
            {
                RaycastHit hit=View.MostRecentGroundHit;Collider collider=hit.collider;
                nativeSample=collider!=null&&collider.gameObject.layer==BBI.Unity.Core.Rendering.RenderLayer.TerrainLayer&&
                    FactionGroundPose.PlausiblePoint(Pose(hit.point),Pose(nativePosition),Pose(state.Position));
            }
            bool customBootstrap=FactionNativeGroundPose.UseCustomBootstrap(CustomFlatMap.Ready,nativeSample,ref NativeGroundAuthorityReady);
            Vector3 sourcePosition=nativePosition;
            // Generated terrain exposes real Terrain-layer colliders. Keep the
            // existing heightfield only for its initial pre-raycast bootstrap;
            // once native sampling is ready, a transient stale/missing hit can
            // never switch the height source back and cause an oscillation.
            if(customBootstrap)
            {
                sourcePosition.y=CustomFlatMap.SampleOriginalHeight(new BBI.Core.Utility.Vector2r(
                    Fixed64.UnsafeFromFloat(nativePosition.x),Fixed64.UnsafeFromFloat(nativePosition.z)))+(float)GroundCalibration.SettleOffset;
                if(!FactionNativeGroundPose.TryAdapt(Pose(sourcePosition),Pose(right),Pose(up),Pose(forward),GroundCalibration,out pose))return;
            }
            float snapDistance=Mathf.Max(25f,Binding.ModelBounds.size.z*2f);
            bool snap=!GroundDisplayReady||(sourcePosition-PreviousGroundAnchor).sqrMagnitude>snapDistance*snapDistance;
            if(snap){DisplayedSteering=0f;for(int i=0;i<2;i++){AimDisplayReady[i]=false;AimResolvedFrame[i]=-1;}}
            GroundDisplayReady=true;PreviousGroundAnchor=sourcePosition;
            root.position=Vector(pose.Position);root.rotation=nativeRotation;
            BoxCollider selection=View.SelectionCollider;
            if(selection!=null)
            {
                // Exact AABB of this rotated/scaled authored box in selection
                // space, using three basis vectors rather than per-frame scans.
                Bounds body=Binding.ModelBounds;Transform space=selection.transform;
                Vector3 center=space.InverseTransformPoint(root.TransformPoint(body.center));
                Vector3 x=space.InverseTransformVector(root.TransformVector(new Vector3(body.size.x,0f,0f)));
                Vector3 y=space.InverseTransformVector(root.TransformVector(new Vector3(0f,body.size.y,0f)));
                Vector3 z=space.InverseTransformVector(root.TransformVector(new Vector3(0f,0f,body.size.z)));
                Vector3 size=new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x),Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y),Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z));
                // Avoid rebuilding a stationary collider shape every frame.
                if((selection.center-center).sqrMagnitude>.00000001f)selection.center=center;
                if((selection.size-size).sqrMagnitude>.00000001f)selection.size=size;
            }
            if(!GroundReported&&(NativeGroundAuthorityReady||customBootstrap))
            {
                GroundReported=true;
                Runtime.Log("FACTION_GROUND_ALIGNED key="+Binding.UnitKey+"; entity="+Entity+"; simY="+state.Position.y+
                    "; nativeViewY="+nativePosition.y+"; geometryY="+root.position.y+"; customTerrain="+CustomFlatMap.Ready+
                    "; source="+(customBootstrap?"custom_height_bootstrap_with_native_rotation":"native_render_transform")+"; customBootstrap="+customBootstrap+"; nativeSettle="+GroundCalibration.SettleOffset+
                    "; nativeHeightAuthority="+(!customBootstrap)+"; nativeRotationAuthority=True; extraFootprintLift=False; bottomDatumPivot=True; selectionAabb=True; extraRaycasts=0; actualVisualAcceptancePending=True");
            }
        }
        void LateUpdate()
        {
            if(PresentationFailed||!enabled)return;
            long timing=RhodesSkirmishFrameTiming.Begin();
            try{ApplyPresentationFrame();}
            catch(Exception error){DisablePresentation(View,"faction_rig",error);}
            finally{RhodesSkirmishFrameTiming.End(6,timing);}
        }
        // U08 prepares its body target before every native camera LateUpdate.
        // LateUpdate remains the fallback; the frame gate below advances the
        // complete air presentation only once, including paused body syncing.
        internal void PrepareAirPresentationFrame()
        {
            if(PresentationFailed||!enabled||Binding==null||!Binding.Air)return;
            long timing=RhodesSkirmishFrameTiming.Begin();
            try{ApplyPresentationFrame();}
            catch(Exception error){DisablePresentation(View,"faction_rig",error);}
            finally{RhodesSkirmishFrameTiming.End(6,timing);}
        }
        void ApplyPresentationFrame()
        {
            if(!Ready||View==null||Binding==null||Binding.GeometryRoot==null||!Entity.Equals(View.EntityID))return;
            if(GroundFX!=null&&GroundFX.Observe(View))GroundFX=null;
            if(Binding.Air)
            {
                int frame=Time.frameCount;if(AirPresentationFrame==frame)return;
                AirPresentationFrame=frame;
            }
            bool hasState=View.NonInterpolatedTickState!=null;
            bool nativeDying=View.IsDriverDead;
            bool alive=hasState&&!View.NonInterpolatedTickState.UnitIsDead&&!nativeDying;
            bool shown=(hasState||nativeDying)&&(int)View.Visibility==2&&!View.ForceIconOnlyMode;
            if(Binding.GeometryRoot.gameObject.activeSelf!=shown)Binding.GeometryRoot.gameObject.SetActive(shown);
            // Native destruction keeps the body through its initial burst and
            // model-swap delay, then spawns the carcass before ShowModel(false)
            // disables the authored GEO objects in mGeoRenderObjects. Keep this
            // parent present even if the sim state is released, and its last
            // local pose frozen until that handoff;
            // the native wrapper still owns death physics, visibility and pool
            // despawn. Do not reset recoil, aim or flight offsets on death.
            if(!alive)return;
            if(Recoil!=null)Recoil.Step(BvmNativeState.GetRoundSequence(Entity),Runtime.PresentationDeltaTime,shown);
            if(ArtilleryMotion!=null)ArtilleryMotion.Step(BvmNativeState.GetRoundSequence(Entity),Runtime.PresentationDeltaTime,shown);
            if(ArtilleryElevation!=null&&!shown)ArtilleryElevation.Step(Vector3.forward,BvmNativeState.GetRoundSequence(Entity),Runtime.PresentationDeltaTime,false);
            if(Binding.Air)RhodesTiltrotorFlightControl.ApplyGeometryAltitude(View,Binding,shown);
            if(!shown){PreviousReady=GroundDisplayReady=false;for(int i=0;i<2;i++){AimDisplayReady[i]=false;AimResolvedFrame[i]=-1;}return;}
            BvmNativeState.Snapshot state=BvmNativeState.Read(View);if(!state.Valid)return;
            // Locomotion phase uses only simulation XZ, never body tilt, height
            // correction or the rotated bottom-datum offset.
            Vector3 position=state.Position;position.y=0f;
            if(!Binding.Air)AlignGround(state);
            if(ArtilleryElevation!=null)
            // U09 has one shared ballistic pitch parent of its real muzzle,
            // rather than three cosmetic LOD pivots. Keep that required FX
            // alignment intact even when native cosmetic turret actuation stops.
            {CacheNativeAim();ArtilleryElevation.Step(NativeAimDirection(state,0),state.RoundSequence,Runtime.PresentationDeltaTime,shown);}
            if(!PreviousReady){PreviousPosition=position;PreviousForward=state.Forward;PreviousReady=true;return;}
            Vector3 delta=position-PreviousPosition;delta.y=0f;
            float travel=Vector3.Dot(delta,state.Forward);
            float turn=Mathf.Atan2(Vector3.Cross(PreviousForward,state.Forward).y,Vector3.Dot(PreviousForward,state.Forward));
            Vector3 averageForward=PreviousForward+state.Forward;
            if(averageForward.sqrMagnitude<.000001f)averageForward=state.Forward;else averageForward.Normalize();
            float trackedTravel=Vector3.Dot(delta,averageForward);
            PreviousPosition=position;PreviousForward=state.Forward;
            if(delta.sqrMagnitude<10000f&&Mathf.Abs(turn)<1f){Distance+=travel;TurnDistance+=turn*Binding.ModelBounds.size.x*.4f;}
            float renderDelta=Runtime.PresentationDeltaTime;
            if(renderDelta>0f)
            {
                MotionElapsed+=renderDelta;
                if(Tracks!=null&&delta.sqrMagnitude<10000f&&Mathf.Abs(turn)<1f)Tracks.Advance(trackedTravel,turn);
            }
            // The native token controls ground actuation eligibility, while
            // the wrapper's original Unity group controls actual mesh LOD.
            // Off freezes mechanical detail; it does not hide a native-visible
            // low mesh. No renderer.enabled guess or extra ground clock exists.
            int nativeLod=NativeUnitLodAdapter.CurrentLod(View);
            bool wheelMotion=Binding.Air||NativeUnitLodAdapter.AnimationActive(nativeLod,Binding.NativeWheelActiveLODs,Binding.NativeAlwaysHighLOD);
            bool turretMotion=Binding.Air||NativeUnitLodAdapter.AnimationActive(nativeLod,Binding.NativeTurretActiveLODs,Binding.NativeAlwaysHighLOD);
            int visibleLod=NativeUnitLodAdapter.ResolveLod(nativeLod,Binding.NativeAlwaysHighLOD);
            float dt=MotionElapsed;MotionElapsed=0f;
            if(dt<=0f)return;
            if(Tracks!=null)
            {
                if(Binding.Air)Tracks.Apply(visibleLod);
                // Refine work inside the inherited native permission: near
                // has both, an allowed medium has the belt only, far freezes.
                // Do not re-enable medium actuation on donors that already
                // stop it there; that would increase many-unit CPU work.
                else if(wheelMotion)Tracks.ApplyNative(wheelMotion);
            }
            float hullYaw=Mathf.Atan2(state.Forward.x,state.Forward.z)*Mathf.Rad2Deg;
            float yaw=state.HasTurretYaw?Mathf.DeltaAngle(hullYaw,state.TurretYawDegrees):0f;
            float pitch=state.HasTurretPitch?-state.TurretPitchDegrees:0f;
            Vector3 secondaryAim=Vector3.forward;
            if(!Binding.Air)
            {
                CacheNativeAim();
                Vector3 localAim=Binding.GeometryRoot.InverseTransformDirection(NativeAimDirection(state,0));
                double localYaw,localPitch;FactionGroundPose.AimDegrees(Pose(localAim),out localYaw,out localPitch);
                yaw=(float)localYaw;pitch=(float)localPitch;
                if(Binding.MortarEmitter!=null)secondaryAim=NativeAimDirection(state,1);
            }
            if(Binding.UnitKey=="RI_U03_ENGINEERING")yaw=Mathf.Clamp(yaw,-90f,90f);
            if(Binding.UnitKey==FactionUnits.ArtilleryKey)pitch=Mathf.Clamp(pitch,-72f,0f);
            float desiredSteering=Mathf.Abs(travel)>.0001f?Mathf.Clamp(turn/Mathf.Abs(travel)*Binding.ModelBounds.size.z*Mathf.Rad2Deg,-25f,25f):0f;
            DisplayedSteering=(float)FactionPresentationMath.Damp(DisplayedSteering,desiredSteering,dt,Binding.SteeringDamping);
            float steering=DisplayedSteering;
            float guideTilt=0f;
            bool nativeGuide=Binding.Air&&RhodesTiltrotorGuideDriver.TryNacelleAngle(Entity,out guideTilt);
            bool landing=false,holding=Binding.Air&&RhodesTiltrotorFlightControl.IsLandingOrHovering(Entity,out landing);
            float forwardSpeed=Vector3.Dot(state.Velocity,state.Forward);
            float sideSpeed=Vector3.Dot(state.Velocity,Vector3.Cross(Vector3.up,state.Forward));
            RhodesTiltrotorMotionPose flight=RhodesTiltrotorMotionProfile.Evaluate(forwardSpeed,sideSpeed,holding,landing,nativeGuide,guideTilt);
            foreach(Joint joint in Joints)
            {
                if(joint.Transform==null)continue;
                if(ArtilleryElevation!=null&&joint.Kind==3)continue; // Own native-ballistic elevation, including the LOD0 FX muzzle.
                // Ground locomotion keeps phase while the native actuation
                // channel is off. On return all authored levels catch up, so
                // Unity can switch meshes without revealing a stale wheel.
                if(joint.Kind==1)joint.Angle=Mathf.Repeat((Distance-joint.Side*TurnDistance)/joint.Radius*Mathf.Rad2Deg,360f);
                else if(joint.Kind==4)joint.Angle=Mathf.Repeat(joint.Angle+dt*1800f,360f);
                else if(joint.Kind==6)joint.Angle=Mathf.MoveTowards(joint.Angle,flight.NacelleDegrees,dt*RhodesTiltrotorMotionProfile.TiltDegreesPerSecond);
                // U08's Unity LODGroup chooses its rendered mesh independently
                // of the native actuation token. Keep all twelve authored
                // rotor/nacelle joints current so every visible LOD can move.
                // Ground units retain the native actuation mask below.
                if(!Binding.Air)
                {
                    if(!wheelMotion&&(joint.Kind==1||joint.Kind==5))continue;
                    // Only ancestors of a real emission marker stay current
                    // outside the native turret mask. This protects a first
                    // shot/repair beam without animating every far LOD turret.
                    if(!NativeUnitLodAdapter.AimWriteAllowed(joint.Kind,turretMotion,joint.CriticalAim))continue;
                }
                float angle=0f;Vector3 axis=joint.Axis;
                switch(joint.Kind)
                {
                    case 1:angle=joint.Angle;break;
                    case 2:angle=yaw;break;
                    case 3:angle=pitch;break;
                    case 4:angle=joint.Angle;break;
                    case 5:angle=steering;break;
                    case 6:angle=joint.Angle;break;
                }
                angle*=joint.AngleScale;
                if(float.IsNaN(joint.LastWrittenAngle)||Mathf.Abs(Mathf.DeltaAngle(joint.LastWrittenAngle,angle))>.000001f)
                {joint.Transform.localRotation=Quaternion.AngleAxis(angle,axis)*joint.Rest;joint.LastWrittenAngle=angle;}
            }
            if(Binding.MortarEmitter!=null&&Binding.AuthoredMortar!=null)
            {
                if(NativeAimYaw[1]!=null||NativeAimPitch[1]!=null)
                {
                    Binding.MortarYaw.position=Binding.AuthoredMortar.position;
                    // Preserve native turret1's yaw/pitch solution. Move only
                    // its custom emitter to the now-tilted authored tube; never
                    // reset the native yaw to a horizontal world Euler angle.
                    Binding.MortarEmitter.position=Binding.AuthoredMortar.position;
                    Binding.MortarEmitter.rotation=Quaternion.LookRotation(secondaryAim,Binding.GeometryRoot.up);
                }
            }
        }
        void OnDisable(){AirPresentationFrame=-1;if(ArtilleryMotion!=null)ArtilleryMotion.Reset();if(ArtilleryElevation!=null)ArtilleryElevation.Reset();if(Ready){BvmNativeState.Unbind(Entity);if(Binding!=null&&Binding.Air)RhodesTiltrotorFlightControl.Unbind(Entity);}Ready=false;PreviousReady=GroundDisplayReady=false;}
        void OnDestroy(){AirPresentationFrame=-1;if(ArtilleryMotion!=null)ArtilleryMotion.Reset();if(ArtilleryElevation!=null)ArtilleryElevation.Reset();if(Ready){BvmNativeState.Unbind(Entity);if(Binding!=null&&Binding.Air)RhodesTiltrotorFlightControl.Unbind(Entity);}Ready=false;}
    }
}
