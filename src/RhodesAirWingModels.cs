using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BBI.Core.ComponentModel;
using BBI.Game.Events;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using EndfieldDoK.ModelIntake;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public enum RhodesAirWingStage { Launch,Flight,Approach,Touchdown,Stow,Bombing }

    internal sealed class AirWingCurve
    {
        internal string Node;internal float[] Times;internal Quaternion[] Values;
        internal Quaternion Sample(float time)
        {
            if(time<=Times[0])return Values[0];int last=Times.Length-1;if(time>=Times[last])return Values[last];
            int lo=0,hi=last;
            while(hi-lo>1){int mid=(lo+hi)/2;if(Times[mid]<=time)lo=mid;else hi=mid;}
            return RhodesAviationPoseMath.Blend(Values[lo],Values[hi],Mathf.Clamp01((time-Times[lo])/(Times[hi]-Times[lo])));
        }
    }
    internal sealed class AirWingMotion
    {
        internal AirWingCurve[] Curves;
        static string Text(BinaryReader reader)
        {
            int length=reader.ReadInt32();if(length<1||length>256)throw new InvalidDataException("Air-wing motion text length differs");
            byte[] bytes=reader.ReadBytes(length);if(bytes.Length!=length)throw new EndOfStreamException();return new UTF8Encoding(false,true).GetString(bytes);
        }
        internal static AirWingMotion Read(string file,string modelFile)
        {
            string modelHash;using(SHA256 hash=SHA256.Create())using(FileStream stream=File.OpenRead(modelFile))
                modelHash=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-",string.Empty).ToLowerInvariant();
            using(FileStream stream=File.OpenRead(file))using(BinaryReader reader=new BinaryReader(stream))
            {
                if(stream.Length<160||stream.Length>1048576||Encoding.ASCII.GetString(reader.ReadBytes(4))!="AFM1"||reader.ReadInt32()!=1)
                    throw new InvalidDataException("Air-wing motion format differs");
                if(Text(reader)!=modelHash)throw new InvalidDataException("Air-wing motion does not bind the exact model");
                string sourceHash=Text(reader);if(sourceHash.Length!=64)throw new InvalidDataException("Air-wing source-motion hash missing");
                int count=reader.ReadInt32();if(count<1||count>64)throw new InvalidDataException("Air-wing motion curve budget differs");
                AirWingMotion result=new AirWingMotion();result.Curves=new AirWingCurve[count];HashSet<string> names=new HashSet<string>(StringComparer.Ordinal);
                for(int index=0;index<count;index++)
                {
                    AirWingCurve curve=new AirWingCurve();curve.Node=Text(reader);int samples=reader.ReadInt32();
                    if(!curve.Node.StartsWith("PIVOT_",StringComparison.Ordinal)||!names.Add(curve.Node)||samples<2||samples>1200)
                        throw new InvalidDataException("Air-wing curve identity/sample count differs");
                    curve.Times=new float[samples];curve.Values=new Quaternion[samples];
                    for(int i=0;i<samples;i++)
                    {
                        float time=reader.ReadSingle();Quaternion value=new Quaternion(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                        float magnitude=value.x*value.x+value.y*value.y+value.z*value.z+value.w*value.w;
                        if(float.IsNaN(time)||float.IsInfinity(time)||time<0f||time>60f||(i>0&&time<=curve.Times[i-1])||
                            float.IsNaN(magnitude)||float.IsInfinity(magnitude)||Mathf.Abs(magnitude-1f)>.001f)
                            throw new InvalidDataException("Air-wing authored curve has invalid time/quaternion");
                        curve.Times[i]=time;curve.Values[i]=value;
                    }
                    result.Curves[index]=curve;
                }
                if(stream.Position!=stream.Length)throw new InvalidDataException("Trailing air-wing motion bytes");return result;
            }
        }
    }

    // A private prefab carries only immutable authored data and cached local
    // node references. No global scene search, simulation transform write,
    // new damage operation, or per-frame material/mesh allocation is performed.
    public sealed class RhodesAirWingBinding:IsolatedUnitPresentation
    {
        public string UnitKey;
        public Transform GeometryRoot;
        public Transform[] Nodes;
        public string[] NodeNames;
        public Renderer[] Geometry;
        public Transform PrimaryFxEmitter;
        public bool GuideActive {get{return GuideState.Active;}}
        public RhodesAirWingStage GuidedStage {get{return GuideState.Stage;}}
        UnitView View;Entity BoundEntity=Entity.None;AirWingMotion Motion;
        Transform[][] Targets;int[][] TargetLods;bool[][] CriticalTargets;Quaternion[][] DisplayedRotations;bool Ready,Registered;float BombHold;
        RhodesAirWingGuideState GuideState;int LastVisibleLod=-1;
        Transform AttitudeGuide;float AttitudeWeight;
        Quaternion AuthoredGeometryRotation,ReleaseRotation;bool GeometryPoseReady,ReleasingAttitude;
        int AttitudeReleaseTick=-1;float AttitudeReleaseSub;
        internal void Guide(RhodesAirWingStage stage,float fraction)
        {
            UnitView view=GetComponent<UnitView>();if(view==null||view.EntityID.Equals(Entity.None))return;
            GuideState.Set(view.EntityID,stage,fraction);
        }
        internal void Guide(RhodesAirWingStage stage,float fraction,Transform guide,float contactWeight)
        {Guide(stage,fraction);AttitudeGuide=guide;AttitudeWeight=Mathf.Clamp01(contactWeight);ReleasingAttitude=false;}
        internal void Store()
        {
            // The native queue owns visibility/removal. Its storage notification
            // must retain the final authored fold instead of selecting Flight.
            Guide(RhodesAirWingStage.Stow,1f);AttitudeGuide=null;ReleasingAttitude=false;
        }
        internal void ReleaseToFlight()
        {
            Guide(RhodesAirWingStage.Flight,0f);AttitudeGuide=null;
            if(GeometryRoot==null||!GeometryPoseReady)return;
            ReleaseRotation=GeometryRoot.localRotation;ReleasingAttitude=true;
            AttitudeReleaseTick=ShipbreakersMain.CurrentSimTick;AttitudeReleaseSub=ShipbreakersMain.CurrentSimSubFrame;
        }
        internal void ClearGuide()
        {
            GuideState.Clear();
            AttitudeGuide=null;ReleasingAttitude=false;
            // V17 releases a dead aircraft's guide before its native carcass
            // swap. Clear the guide bookkeeping without snapping its last pose.
            bool dying=View!=null&&(View.IsDriverDead||(View.NonInterpolatedTickState!=null&&View.NonInterpolatedTickState.UnitIsDead));
            if(GeometryPoseReady&&GeometryRoot!=null&&!dying)GeometryRoot.localRotation=AuthoredGeometryRotation;
        }
        void ApplyAttitude()
        {
            if(AttitudeGuide!=null&&GuideActive)
                GeometryRoot.localRotation=RhodesAviationPoseMath.GuideLocalRotation(GeometryRoot.parent.rotation,AuthoredGeometryRotation,AttitudeGuide.rotation,AttitudeWeight);
            else if(ReleasingAttitude)
            {
                float age=RhodesAviationLaunchProfile.NativePresentationAge(AttitudeReleaseTick,AttitudeReleaseSub,ShipbreakersMain.CurrentSimTick,ShipbreakersMain.CurrentSimSubFrame);
                GeometryRoot.localRotation=RhodesAviationPoseMath.ReleaseLocalRotation(ReleaseRotation,AuthoredGeometryRotation,age);
                if(age>=RhodesAviationPoseMath.ReleaseSeconds)ReleasingAttitude=false;
            }
        }
        bool Bind()
        {
            UnitView view=View==null?GetComponent<UnitView>():View;
            if(view==null||view.EntityID.Equals(Entity.None)||GeometryRoot==null||!GeometryRoot.IsChildOf(view.transform))return false;
            if(Ready&&View==view&&BoundEntity.Equals(view.EntityID))return true;
            // An already-bound death can outlive the simulation snapshot. A
            // different/null/dead generation must never rebuild its live pose.
            if(view.NonInterpolatedTickState==null||view.NonInterpolatedTickState.UnitIsDead||view.IsDriverDead)return false;
            try
            {
            if(Registered)BvmNativeState.Unbind(BoundEntity);
            if(!GeometryPoseReady){AuthoredGeometryRotation=GeometryRoot.localRotation;GeometryPoseReady=true;}
            if(!GuideState.BelongsTo(view.EntityID))ClearGuide();
            View=view;BoundEntity=view.EntityID;Motion=RhodesAirWingModels.MotionFor(UnitKey);Targets=new Transform[Motion.Curves.Length][];TargetLods=new int[Motion.Curves.Length][];CriticalTargets=new bool[Motion.Curves.Length][];DisplayedRotations=new Quaternion[Motion.Curves.Length][];
            for(int curve=0;curve<Motion.Curves.Length;curve++)
            {
                List<Transform> matches=new List<Transform>();
                for(int i=0;i<NodeNames.Length;i++)if(NodeNames[i].EndsWith("::"+Motion.Curves[curve].Node,StringComparison.Ordinal))
                {if(Nodes[i]==null||!Nodes[i].IsChildOf(GeometryRoot))throw new InvalidOperationException("Air-wing clone node escaped its owned geometry");matches.Add(Nodes[i]);}
                if(matches.Count!=3)throw new InvalidOperationException("Authored air-wing motion must bind each of three LODs: "+Motion.Curves[curve].Node);
                Targets[curve]=matches.ToArray();TargetLods[curve]=new int[matches.Count];CriticalTargets[curve]=new bool[matches.Count];DisplayedRotations[curve]=new Quaternion[matches.Count];
                for(int i=0;i<matches.Count;i++)
                {
                    string name=matches[i].name;if(name.Length<4||name[0]!='L'||name[1]<'0'||name[1]>'2'||name.Substring(2,2)!="::")throw new InvalidOperationException("Air-wing motion lacks an exact authored LOD identity");
                    TargetLods[curve][i]=name[1]-'0';CriticalTargets[curve][i]=PrimaryFxEmitter!=null&&PrimaryFxEmitter.IsChildOf(matches[i]);DisplayedRotations[curve][i]=matches[i].localRotation;
                }
            }
            if(UnitKey==RhodesCarrierUnits.HeavyBomberKey){BvmNativeState.Bind(BoundEntity);Registered=true;}
            NativeRead.Set(view,"mGeoRenderObjects",Geometry);NativeRead.Call(view,"UpdateUnitMaterialPropertyBlock",true);
            Ready=true;BombHold=0f;LastVisibleLod=-1;
            Runtime.Log("RHODES_AIR_WING_VIEW_BOUND key="+UnitKey+"; entity="+BoundEntity+"; exactAuthoredCurves="+Motion.Curves.Length+
                "; allThreeLODs=True; nativeLOD=True; perRenderFrameInterpolation=True; nativeAircraftTransform=True; actualVisualAcceptancePending=True");
            return true;
            }
            catch
            {if(Registered)BvmNativeState.Unbind(BoundEntity);Registered=Ready=false;throw;}
        }
        void LateUpdate()
        {
            if(PresentationFailed||!enabled)return;
            try{ApplyPresentationFrame();}
            catch(Exception error){DisablePresentation(View,"air_wing_rig",error);}
        }
        void ApplyPresentationFrame()
        {
            if(!Bind())return;
            bool hasState=View.NonInterpolatedTickState!=null,nativeDying=View.IsDriverDead;
            bool alive=hasState&&!View.NonInterpolatedTickState.UnitIsDead&&!nativeDying;
            bool shown=(hasState||nativeDying)&&(View.SimStateInterpolator!=null||nativeDying)&&Convert.ToInt32(View.Visibility)==2&&!View.ForceIconOnlyMode;
            if(GeometryRoot.gameObject.activeSelf!=shown)GeometryRoot.gameObject.SetActive(shown);
            // Native death physics carries this frozen local pose until the
            // carcass is spawned and native ShowModel(false) hides the GEOs.
            if(!alive)return;
            if(!shown){LastVisibleLod=-1;return;}ApplyAttitude();if(Runtime.PresentationDeltaTime<=0f)return;
            float dt=Runtime.PresentationDeltaTime;
            if(Registered)BombHold=Mathf.Max(0f,1.1f-BvmNativeState.GetLastRoundPresentationAge(BoundEntity));
            int visibleLod=NativeUnitLodAdapter.CurrentLod(View);
            RhodesAirWingStage stage=GuideActive?GuidedStage:BombHold>0f?RhodesAirWingStage.Bombing:RhodesAirWingStage.Flight;
            float time=RhodesAirWingModels.PoseTime(UnitKey,stage,GuideState.Fraction);
            float blend=GuideActive||visibleLod!=LastVisibleLod?1f:(float)FactionPresentationMath.Blend(8f,dt);LastVisibleLod=visibleLod;
            for(int i=0;i<Motion.Curves.Length;i++)
            {
                // V02 fans make multiple full turns between the authored
                // 30 Hz quaternion keys. Recover their cumulative source
                // phase instead of Slerp's aliased shortest arc. The authored
                // guide clock remains authoritative; output is interpolated
                // each visible render frame without a second wall clock.
                Quaternion pose=UnitKey==RhodesCarrierUnits.HeavyBomberKey&&RhodesAirWingModels.IsBomberLiftFan(Motion.Curves[i].Node)
                    ?Quaternion.AngleAxis(RhodesAirWingModels.BomberLiftFanDegrees(time*30f),Vector3.up)
                    :Motion.Curves[i].Sample(time);
                for(int target=0;target<Targets[i].Length;target++)
                    {
                        // Actual mesh LOD is selected by Unity independently
                        // of the native actuation token. Fold/gear mechanisms
                        // must be ready in every authored LOD before a switch.
                        Quaternion previous=DisplayedRotations[i][target],next=Quaternion.Slerp(previous,pose,blend);
                        if(Mathf.Abs(previous.x-next.x)+Mathf.Abs(previous.y-next.y)+Mathf.Abs(previous.z-next.z)+Mathf.Abs(previous.w-next.w)>.00000001f)
                        {Targets[i][target].localRotation=next;DisplayedRotations[i][target]=next;}
                    }
            }
        }
        // A native visibility/pooling disable does not itself identify a new
        // aircraft. Keep its stored fold; Bind clears it on an Entity change.
        void OnDisable(){if(Registered)BvmNativeState.Unbind(BoundEntity);Registered=false;Ready=false;}
        void OnDestroy(){if(Registered)BvmNativeState.Unbind(BoundEntity);Registered=false;}
    }

    public static class RhodesAirWingModels
    {
        static GameObject Templates;static Mesh Empty;
        static readonly List<RhodesLoadedModel> Loaded=new List<RhodesLoadedModel>();
        static readonly Dictionary<string,AirWingMotion> Motions=new Dictionary<string,AirWingMotion>(StringComparer.Ordinal);
        internal static AirWingMotion MotionFor(string key){AirWingMotion result;if(!Motions.TryGetValue(key,out result))throw new InvalidOperationException("Air-wing motion was not prepared: "+key);return result;}
        public static bool IsAirWingKey(string key){return key==RhodesCarrierUnits.AircraftKey||key==RhodesCarrierUnits.HeavyBomberKey;}
        static string PackagePath(string key)
        {if(!IsAirWingKey(key))throw new ArgumentException("Unknown air-wing key");return Path.Combine(Path.Combine(Path.Combine(CustomUnits.AssetDirectory,"air_wing_v01"),key),"model.bin");}
        internal static float PoseTime(string key,RhodesAirWingStage stage,float progress)
        {
            bool fighter=key==RhodesCarrierUnits.AircraftKey;float frame;progress=Mathf.Clamp01(progress);
            switch(stage)
            {
                case RhodesAirWingStage.Launch:frame=Mathf.Lerp(1f,45f,progress);break;
                case RhodesAirWingStage.Approach:frame=fighter?Mathf.Lerp(100f,180f,progress):Mathf.Lerp(230f,280f,progress);break;
                case RhodesAirWingStage.Touchdown:
                    // B01 gear reaches authored frame320 at native age6.2s;
                    // V17's vertical descent begins at age6.6s. Keep the
                    // remaining contact pose until native docking completes.
                    frame=fighter?Mathf.Lerp(180f,230f,progress):progress<=.2f
                        ?Mathf.Lerp(280f,320f,progress/.2f):Mathf.Lerp(320f,345f,(progress-.2f)/.8f);break;
                case RhodesAirWingStage.Stow:frame=fighter?Mathf.Lerp(230f,300f,progress):Mathf.Lerp(345f,420f,progress);break;
                case RhodesAirWingStage.Bombing:frame=fighter?100f:180f;break;
                default:frame=100f;break;
            }
            return frame/30f;
        }
        internal static bool IsBomberLiftFan(string name)
        {return name=="PIVOT_B01_LiftFan_Rotor_L"||name=="PIVOT_B01_LiftFan_Rotor_R";}
        internal static float BomberLiftFanDegrees(float frame)
        {
            // Blender local +Z became glTF local +Y. These are the author's
            // unwrapped Euler turns, not turns inferred from wrapped quats.
            float turns=frame<=245f?0f:frame<=345f?.8f*(frame-245f):frame<360f?80f+(frame-345f)/3f:85f;
            return Mathf.Repeat(360f*turns,360f);
        }
        public static void SetGuidedPose(UnitView view,RhodesAirWingStage stage,float fraction)
        {if(view==null)return;RhodesAirWingBinding binding=view.GetComponent<RhodesAirWingBinding>();if(binding!=null)binding.Guide(stage,fraction);}
        internal static void SetGuidedPose(UnitView view,RhodesAirWingStage stage,float fraction,Transform guide,float contactWeight)
        {if(view==null)return;RhodesAirWingBinding binding=view.GetComponent<RhodesAirWingBinding>();if(binding!=null)binding.Guide(stage,fraction,guide,contactWeight);}
        internal static void SetStoredPose(UnitView view)
        {if(view==null)return;RhodesAirWingBinding binding=view.GetComponent<RhodesAirWingBinding>();if(binding!=null)binding.Store();}
        internal static void ReleaseToFlight(UnitView view)
        {if(view==null)return;RhodesAirWingBinding binding=view.GetComponent<RhodesAirWingBinding>();if(binding!=null)binding.ReleaseToFlight();}
        public static void ClearGuide(UnitView view)
        {if(view==null)return;RhodesAirWingBinding binding=view.GetComponent<RhodesAirWingBinding>();if(binding!=null)binding.ClearGuide();}
        public static void ConfigureView(string key,UnitViewAttributes target,UnitViewAttributes source)
        {
            if(!IsAirWingKey(key)||target==null||source==null||source.ModelPrefab==null||object.ReferenceEquals(target,source)||
                object.ReferenceEquals(target.LODTunings,source.LODTunings))throw new InvalidOperationException("Air-wing needs an independently owned view/LOD source");
            if(Motions.ContainsKey(key))throw new InvalidOperationException("Air-wing model generation already exists: "+key);
            if(Templates==null)
            {
                Templates=new GameObject("EF_Rhodes_AirWing_Templates");Templates.SetActive(false);Templates.hideFlags=HideFlags.HideAndDontSave;UnityEngine.Object.DontDestroyOnLoad(Templates);
                Empty=new Mesh();Empty.name="EF_AirWing_BlankDonor";Empty.hideFlags=HideFlags.HideAndDontSave;
            }
            string path=PackagePath(key);AirWingMotion motion=AirWingMotion.Read(Path.Combine(Path.GetDirectoryName(path),"motion.afm"),path);
            GameObject prefab=(GameObject)UnityEngine.Object.Instantiate(source.ModelPrefab);prefab.name=key+"_AuthoredPrefab";
            prefab.transform.parent=Templates.transform;prefab.transform.localPosition=Vector3.zero;
            prefab.transform.localRotation=source.ModelPrefab.transform.localRotation;prefab.transform.localScale=source.ModelPrefab.transform.localScale;
            Material native=null;int layer=prefab.layer;
            foreach(Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if(!(renderer is MeshRenderer)&&!(renderer is SkinnedMeshRenderer))continue;
                if(native==null&&renderer.sharedMaterial!=null&&renderer.sharedMaterial.shader!=null&&renderer.sharedMaterial.shader.isSupported)
                {native=renderer.sharedMaterial;layer=renderer.gameObject.layer;}
                SkinnedMeshRenderer skin=renderer as SkinnedMeshRenderer;if(skin!=null)skin.sharedMesh=Empty;
                MeshFilter filter=renderer.GetComponent<MeshFilter>();if(filter!=null)filter.sharedMesh=Empty;renderer.enabled=false;
            }
            if(native==null)throw new InvalidOperationException("Air-wing donor has no supported native material");
            foreach(LODGroup lod in prefab.GetComponentsInChildren<LODGroup>(true))lod.enabled=false;
            foreach(ParticleSystem particle in prefab.GetComponentsInChildren<ParticleSystem>(true))UnityEngine.Object.DestroyImmediate(particle.gameObject);
            foreach(Light light in prefab.GetComponentsInChildren<Light>(true))UnityEngine.Object.DestroyImmediate(light);
            foreach(Animator animator in prefab.GetComponentsInChildren<Animator>(true))animator.enabled=false;
            foreach(Animation animation in prefab.GetComponentsInChildren<Animation>(true))animation.enabled=false;
            RhodesLoadedModel model=RhodesModelBuilder.LoadSmallUnit(path,native,layer);Loaded.Add(model);Motions.Add(key,motion);
            // RHS Visual remains the exact static mesh envelope required by its
            // validator. Selection is the separately authored full-motion box.
            // Use that conservative envelope for this animated preview/LOD only.
            RhodesBounds envelope=model.Package.Selection;
            Vector3 envelopeMin=new Vector3(envelope.Min[0],envelope.Min[1],envelope.Min[2]);
            Vector3 envelopeMax=new Vector3(envelope.Max[0],envelope.Max[1],envelope.Max[2]);
            model.PresentationBounds=new Bounds((envelopeMin+envelopeMax)*.5f,envelopeMax-envelopeMin);
            if(model.LodGroup!=null)
            {model.LodGroup.localReferencePoint=model.PresentationBounds.center;Vector3 size=model.PresentationBounds.size;model.LodGroup.size=Mathf.Max(size.x,Mathf.Max(size.y,size.z));}
            model.AttachInMetreSpace(prefab.transform);model.Root.name="EF_Rhodes_AirWing_AuthoredGeometry";model.Root.SetActive(true);
            RhodesAirWingBinding binding=prefab.AddComponent<RhodesAirWingBinding>();binding.UnitKey=key;binding.GeometryRoot=model.Root.transform;
            NativeUnitLodAdapter.AdoptGeometry(prefab.GetComponent<UnitView>(),model.LodGroup);
            LODGroup nativeLod=prefab.GetComponent<LODGroup>();
            Bounds nativeBounds=new Bounds();Vector3 envelopeLo=model.PresentationBounds.min,envelopeHi=model.PresentationBounds.max;
            for(int corner=0;corner<8;corner++)
            {
                Vector3 local=new Vector3((corner&1)==0?envelopeLo.x:envelopeHi.x,(corner&2)==0?envelopeLo.y:envelopeHi.y,(corner&4)==0?envelopeLo.z:envelopeHi.z);
                Vector3 point=prefab.transform.InverseTransformPoint(model.Root.transform.TransformPoint(local));
                if(corner==0)nativeBounds=new Bounds(point,Vector3.zero);else nativeBounds.Encapsulate(point);
            }
            nativeLod.localReferencePoint=nativeBounds.center;Vector3 extent=nativeBounds.size;nativeLod.size=Mathf.Max(extent.x,Mathf.Max(extent.y,extent.z));
            binding.Nodes=model.Nodes;binding.Geometry=model.Renderers;binding.NodeNames=new string[model.Package.Nodes.Length];
            for(int i=0;i<binding.NodeNames.Length;i++)binding.NodeNames[i]=model.Package.Nodes[i].Name;
            Transform emitter=null;
            foreach(Transform node in model.Nodes)if(node.name=="L0::"+(key==RhodesCarrierUnits.AircraftKey?"HP_F01_Gun_-1":"HP_B01_BombRelease_L_0")){emitter=node;break;}
            if(emitter==null)throw new InvalidOperationException("Authored aircraft weapon hardpoint missing: "+key);
            GameObject emission=new GameObject(key+"_WeaponEmission");emission.layer=layer;emission.transform.parent=emitter;
            emission.transform.localPosition=Vector3.zero;emission.transform.localRotation=Quaternion.identity;emission.transform.localScale=Vector3.one;binding.PrimaryFxEmitter=emission.transform;
            if(target.TurretDefinitions!=null)foreach(UnitViewAttributes.TurretAttributes turret in target.TurretDefinitions)
            {turret.CenterEmissionBone=emission.name;turret.FXEmissionBones=new string[]{emission.name};}
            target.NLIPSScale=new AnimationCurve(new Keyframe(0f,1f),new Keyframe(1f,1f));
            target.UnitEffectDefs=new BBI.Unity.Core.World.EffectDefinition[0];target.WheelFXDefinitions=new UnitViewAttributes.WheelAttributes[0];target.TreadDefinitions=new UnitViewAttributes.TreadAttributes[0];
            RhodesAirEffects.Configure(key,target,source,model);
            object animations=target.AnimationDefinitions;
            if(animations!=null)
            {
                foreach(string field in new string[]{"EnableMovementLayerX","EnableMovementLayerY","EnableMovementLayerZ","EnableFiringLayer"})NativeRead.Set(animations,field,false);
                foreach(string field in new string[]{"ResearchUpgrades","AbilityStates","AnimationTriggers","ProductionEffects","ProductionAnimations"})
                {Array values=NativeRead.Get(animations,field) as Array;if(values!=null)NativeRead.Set(animations,field,Array.CreateInstance(values.GetType().GetElementType(),0));}
            }
            target.ModelPrefab=prefab;
            UnitView view=prefab.GetComponent<UnitView>();if(view==null||view.SelectionCollider==null)throw new InvalidOperationException("Air-wing donor lost native view/selection collider");
            Bounds body=model.PresentationBounds,selection=new Bounds();bool first=true;
            for(int corner=0;corner<8;corner++)
            {
                Vector3 p=new Vector3((corner&1)==0?body.min.x:body.max.x,(corner&2)==0?body.min.y:body.max.y,(corner&4)==0?body.min.z:body.max.z);
                p=view.SelectionCollider.transform.InverseTransformPoint(model.Root.transform.TransformPoint(p));
                if(first){selection=new Bounds(p,Vector3.zero);first=false;}else selection.Encapsulate(p);
            }
            view.SelectionCollider.center=selection.center;view.SelectionCollider.size=selection.size;
            NativeRead.Set(view,"mGeoRenderObjects",model.Renderers);
            Runtime.Log("RHODES_AIR_WING_MODEL_PREPARED key="+key+"; nodes="+model.Nodes.Length+"; authoredMotionCurves="+motion.Curves.Length+
                "; modelMetres="+body.size+"; nominalScale=True; nativeDonorUnchanged=True; runtimeVisualAcceptancePending=True");
        }
        public static void Reset()
        {
            RhodesAirEffects.Reset();
            foreach(RhodesLoadedModel model in Loaded)model.Dispose();Loaded.Clear();Motions.Clear();
            if(Templates!=null)UnityEngine.Object.DestroyImmediate(Templates);Templates=null;
            if(Empty!=null)UnityEngine.Object.DestroyImmediate(Empty);Empty=null;
        }
    }
}
