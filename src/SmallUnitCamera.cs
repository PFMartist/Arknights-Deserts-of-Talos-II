using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BBI.Unity.Game.World;
using BBI.Unity.Game.Data;
using EndfieldDoK.MapUnitLab.ModCore;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Camera-only profiles. No unit scale/data, input binding, UI, fog, audio or
    // clocks are changed. Native camera LateUpdate remains the motion owner.
    public static class SmallUnitCamera
    {
        public sealed class Configuration
        {
            public bool Enabled=true,FrameOnNativeFocus=false,PreserveNativeFocus=true;
            public float FreeMinimumDistanceM=1.8f,VehicleClosestDistanceM=4.5f,HumanoidClosestDistanceM=2.2f;
            public float SmallUnitMaximumSizeM=20f,HumanoidMaximumSizeM=3.2f;
            public float VehicleNearPitchDegrees=38f,HumanoidNearPitchDegrees=34f,NearMinimumPitchDegrees=5f;
            public float PitchBlendStartDistanceM=30f,PitchBlendEndDistanceM=180f;
            public int GameplayScrollTicks=48;
            public float NearPanScreensPerSecond=.35f,PanBlendStartDistanceM=40f,PanBlendEndDistanceM=250f;
            public float NearCollisionRadiusM=1f;
            public float VehicleNearClipM=.18f,HumanoidNearClipM=.10f;
            public float ClipBlendStartDistanceM=60f,ClipBlendEndDistanceM=250f,WideNearClipMinimumM=1f,WideNearClipDistanceFactor=.008f,MaximumNearClipM=64f;
            public float FocusFramingPadding=1.20f;
        }
        public static bool NativeFocusPreserved { get { return Config==null || Config.PreserveNativeFocus; } }
        static Configuration Config;
        static readonly List<CameraState> States=new List<CameraState>();
        static float NextDiscovery,NextConfigCheck;
        static DateTime ConfigStamp;
        static bool ConfigKnown,ConfigWarningReported;
        static readonly NativeReferenceAccessor48<UserCamera,Transform> BaseTargetReference=new NativeReferenceAccessor48<UserCamera,Transform>("m_BaseCamTarget",false,true);
        static readonly NativeReferenceAccessor48<UserCamera,Transform> CameraTargetReference=new NativeReferenceAccessor48<UserCamera,Transform>("m_CamTarget",true,true);
        static readonly NativeReferenceAccessor48<UserCamera,UnitViewAttributes.CameraFocusSettings> FocusReference=new NativeReferenceAccessor48<UserCamera,UnitViewAttributes.CameraFocusSettings>("mCurrentFocusSettings",true,true);
        static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
        // Reuse the camera discovery already owned here; effects must not add
        // a scene scan for each cast or keep a stale Camera.main across modes.
        internal static UserCamera ActiveGameplayCamera
        {
            get
            {
                foreach(CameraState state in States)
                    if(state.Camera!=null&&state.Camera.enabled&&state.Camera.gameObject.activeInHierarchy&&state.Camera.ActiveCamera!=null)
                        return state.Camera;
                return null;
            }
        }

        public static void MainTick(object main)
        {
            try{CheckConfiguration();}catch(Exception error){if(!ConfigWarningReported){ConfigWarningReported=true;Runtime.Log("SMALL_CAMERA_CONFIG_WARNING "+error.GetType().Name+": "+error.Message+"; previousOrDefaultSettingsRetained=True");}if(Config==null)Config=new Configuration();ConfigKnown=true;}
            if(Config==null||!Config.Enabled){RestoreStates();return;}
            if(Time.realtimeSinceStartup>=NextDiscovery)
            {
                NextDiscovery=Time.realtimeSinceStartup+.5f;
                object current;UserCamera native=main!=null&&NativeRead.TryGet(main,"mGameCamera",out current)?current as UserCamera:null;
                if(Usable(native))AddCamera(native);
                // A valid native camera owns free/follow/sensors transitions.
                // Do not allocate scene-search arrays while it remains usable.
                if(States.Count==0)foreach(UserCamera camera in UnityEngine.Object.FindObjectsOfType(typeof(UserCamera)))
                {
                    if(Usable(camera))AddCamera(camera);
                }
            }
            for(int i=States.Count-1;i>=0;i--)
            {
                CameraState state=States[i];
                if(!Usable(state.Camera)){state.Restore();States.RemoveAt(i);continue;}
                try{state.Tick();}
                catch(Exception error){state.Restore();States.RemoveAt(i);Runtime.Log("SMALL_CAMERA_ADAPTER_WARNING "+error.GetType().Name+": "+error.Message+"; nativeProfilesRestored=True; gameplayContinues=True");}
            }
        }
        static bool Usable(UserCamera camera){return camera!=null&&camera.enabled&&camera.gameObject.activeInHierarchy&&camera.ActiveCamera!=null;}
        static void AddCamera(UserCamera camera){foreach(CameraState state in States)if(state.Camera==camera)return;States.Add(new CameraState(camera));}
        public static void Reset(){RestoreStates();Config=null;ConfigKnown=ConfigWarningReported=false;NextDiscovery=NextConfigCheck=0f;}
        static void RestoreStates(){foreach(CameraState state in States)state.Restore();States.Clear();}

        sealed class CameraState
        {
            public readonly UserCamera Camera;
            UserCamera.MotionData OriginalMotion,Motion;
            UserCamera.ThresholdData OriginalThresholds,Thresholds;
            UserCamera.CollisionData OriginalCollision,Collision;
            UnityEngine.Camera Lens;
            float OriginalNear,OriginalFar,OriginalPan,PitchStyle=float.NaN,CollisionBlend=-1f;
            AnimationCurve GameplayCurve,OriginalPitch,OriginalMinPitch,OriginalCollisionCurve,VehiclePitch,HumanoidPitch,NearMinPitch,WorkingCollision;
            Keyframe[] CollisionKeys;
            readonly Dictionary<string,object> Frames=new Dictionary<string,object>();
            Transform SeenBase,BoundBase,OriginalTarget;
            UnitView BoundView;
            Transform GeometryRoot;
            Bounds GeometryBounds;
            GameObject Anchor;
            UnitViewAttributes.CameraFocusSettings NativeFocus,OwnedFocus;
            bool Installed,Humanoid,NativeFocusObserved;
            SmallCameraNoise Noise;
            bool NoiseWarned;
            int Reports;
            float NextReport;
            public CameraState(UserCamera camera){Camera=camera;}

            public void Tick()
            {
                if(Camera.IsCinematicMode)
                {
                    if(Noise!=null) Noise.Tick(Camera.DistanceToTargetMeters,Camera.SensorsManagerActive);
                    return;
                }
                if(!Installed)
                {
                    // Avoid replacing zoom parameterisation in a native sensors
                    // transition; attach on its first steady camera frame.
                    object motionState=Read(Camera,"mMotionState");
                    if(motionState!=null&&Convert.ToInt32(motionState)==9)return;
                    Install();
                }
                bool sensors=Camera.SensorsManagerActive;
                Transform target=BaseTargetReference.Read(Camera);
                bool changed=target!=SeenBase;SeenBase=target;
                if(changed)NativeFocusObserved=!sensors;
                if(target!=BoundBase)BindTarget(target,!sensors&&changed);
                if(BoundBase!=null&&BoundView!=null&&GeometryRoot!=null)
                {
                    if(ReferenceEquals(FocusReference.Read(Camera),NativeFocus))FocusReference.Write(Camera,OwnedFocus);
                    if(!Config.PreserveNativeFocus && Anchor!=null)
                    {
                        Anchor.transform.position=GeometryRoot.TransformPoint(GeometryBounds.center);
                        Transform actual=CameraTargetReference.Read(Camera);
                        if(actual==OriginalTarget||actual==Anchor.transform)CameraTargetReference.Write(Camera,Anchor.transform);
                    }
                }
                float distance=Math.Max(Config.FreeMinimumDistanceM,Camera.DistanceToTargetMeters);
                if(Noise!=null)
                {
                    Noise.Tick(distance,sensors);
                    if(!NoiseWarned && Noise.LastError.Length>0)
                    {
                        NoiseWarned=true;
                        Runtime.Log("SMALL_CAMERA_NOISE_WARNING "+Noise.LastError+"; noiseReductionDisabled=True; gameplayContinues=True");
                    }
                }
                SetPitchStyle(sensors?float.PositiveInfinity:(Humanoid?Config.HumanoidNearPitchDegrees:Config.VehicleNearPitchDegrees));
                float blend=sensors?1f:Smooth(Config.PanBlendStartDistanceM,Config.PanBlendEndDistanceM,distance);
                Motion.ScreenSpacePanVelocityMSPerScreen=Lerp(Math.Min(Config.NearPanScreensPerSecond,OriginalPan),OriginalPan,blend);
                if(Math.Abs(blend-CollisionBlend)>.03f)
                {
                    UpdateCollision(blend);CollisionBlend=blend;
                }
                Lens.nearClipPlane=AdaptiveNear(distance,OriginalNear,Lens.farClipPlane,Humanoid,Config);
                if(Reports<10&&Time.realtimeSinceStartup>=NextReport)
                {
                    Runtime.Log("SMALL_CAMERA_STATE sensors="+sensors+"; distance="+distance+"; closest="+Camera.ClosestCameraDistanceToTarget+
                        "; gameplayMax="+Thresholds.FurthestCameraDistanceGameplay+"; sensorsMinMax="+Thresholds.MinimumCameraDistanceSensors+".."+Thresholds.FurthestCameraDistanceSensors+
                        "; near="+Lens.nearClipPlane+"; far="+Lens.farClipPlane+"; panScreensPerSecond="+Motion.ScreenSpacePanVelocityMSPerScreen+
                        "; gameplayScrollTicks="+Motion.NumScrollTicksGameplay+"; target="+(BoundView==null?"native/world":BoundView.name)+
                        "; realGeometryBounds="+GeometryBounds+"; anchor="+(Anchor==null?"none":Anchor.transform.position.ToString())+
                        "; nativeFollowingIdentityRetained=True; directCameraTransformWrites=False; offlineVisualAcceptancePending=True");
                    Reports++;NextReport=Time.realtimeSinceStartup+1f;
                }
            }

            void Install()
            {
                Lens=Camera.ActiveCamera;OriginalNear=Lens.nearClipPlane;OriginalFar=Lens.farClipPlane;
                OriginalMotion=Camera.MotionValues;OriginalThresholds=Camera.Thresholds;OriginalCollision=Camera.CollisionValues;
                if(OriginalMotion==null||OriginalThresholds==null||OriginalCollision==null||OriginalMotion.ScrollTickLERPCurveGameplay==null)
                    throw new InvalidOperationException("Native camera profiles are not ready");
                float actual=Camera.DistanceToTargetMeters,desired=Convert.ToSingle(Read(Camera,"DesiredDistanceToTarget")),oldMinimum=Camera.ClosestCameraDistanceToTarget;
                Motion=(UserCamera.MotionData)Copy(OriginalMotion);Thresholds=(UserCamera.ThresholdData)Copy(OriginalThresholds);Collision=(UserCamera.CollisionData)Copy(OriginalCollision);
                OriginalPan=OriginalMotion.ScreenSpacePanVelocityMSPerScreen;
                OriginalPitch=CopyCurve(OriginalMotion.DefaultPitchVersusDistance);OriginalMinPitch=CopyCurve(OriginalMotion.MinPitchVersusDistanceFromTarget);
                OriginalCollisionCurve=CopyCurve(OriginalCollision.ColliderSizeVersusVelocityMS);
                CollisionKeys=OriginalCollisionCurve.keys;WorkingCollision=Collision.ColliderSizeVersusVelocityMS;
                VehiclePitch=NearPitchCurve(OriginalPitch,Config.VehicleNearPitchDegrees);
                HumanoidPitch=NearPitchCurve(OriginalPitch,Config.HumanoidNearPitchDegrees);
                NearMinPitch=NearPitchCurve(OriginalMinPitch,Config.NearMinimumPitchDegrees);
                Thresholds.MinimumDistanceToTargetM=Config.FreeMinimumDistanceM;
                Thresholds.LowestPitchDeg=Math.Min(OriginalThresholds.LowestPitchDeg,Config.NearMinimumPitchDegrees);
                GameplayCurve=LogCurve(Config.FreeMinimumDistanceM,OriginalThresholds.FurthestCameraDistanceGameplay);
                Motion.ScrollTickLERPCurveGameplay=GameplayCurve;Motion.NumScrollTicksGameplay=Config.GameplayScrollTicks;
                // Preserve original physical default range when its stored LERP
                // coordinate is used again by native sensors return logic.
                if(Motion.DefaultView!=null&&OriginalMotion.DefaultView!=null)
                {
                    float oldDefault=Lerp(oldMinimum,OriginalThresholds.FurthestCameraDistanceGameplay,OriginalMotion.ScrollTickLERPCurveGameplay.Evaluate(OriginalMotion.DefaultView.LERPCurvePosition));
                    Motion.DefaultView.LERPCurvePosition=Inverse(GameplayCurve,Config.FreeMinimumDistanceM,OriginalThresholds.FurthestCameraDistanceGameplay,oldDefault);
                    Motion.DefaultView.FinalDistanceToTarget=oldDefault;
                }
                // Arm rollback before the first camera-owned reference is replaced.
                Installed=true;
                foreach(string name in new[]{"mGameViewSettings","mSensorsViewSettings","mGoalViewSettings","mTransitioningFromSettings","mDefaultViewOnInstantiate"})
                {
                    object original=Read(Camera,name);Frames.Add(name,original);if(original!=null)Write(Camera,name,Copy(original));
                }
                Camera.MotionValues=Motion;Camera.Thresholds=Thresholds;Camera.CollisionValues=Collision;
                Reparameterise(actual,desired);
                Noise=new SmallCameraNoise(Camera);
                Runtime.Log("SMALL_CAMERA_PROFILES_CLONED gameplayMin="+Thresholds.MinimumDistanceToTargetM+"; originalMin="+OriginalThresholds.MinimumDistanceToTargetM+
                    "; farGameplayUnchanged="+Thresholds.FurthestCameraDistanceGameplay+"; sensorsRangeAndCurveUnchanged=True; nativeCollisionMask="+Collision.CollideWith.value+
                    "; nativeCameraSphereMinimum=1m; originalNearFar="+OriginalNear+"/"+OriginalFar+"; positionPreservedOnInstall=True; userSettingsAndInputsUntouched=True");
            }

            void BindTarget(Transform target,bool frame)
            {
                RestoreFocus();BoundBase=target;Humanoid=false;NativeFocus=null;OwnedFocus=null;
                if(target==null)return;UnitView view=target.GetComponent<UnitView>();if(view==null)return;
                Transform root;Bounds bounds;int vertexCount;
                if(!Measure(view,out root,out bounds,out vertexCount))return;
                Vector3 physical=PhysicalSize(bounds,root);if(Maximum(physical)>Config.SmallUnitMaximumSizeM)return;
                BoundView=view;GeometryRoot=root;GeometryBounds=bounds;Humanoid=Maximum(physical)<=Config.HumanoidMaximumSizeM;
                NativeFocus=FocusReference.Read(Camera);
                OwnedFocus=NativeFocus==null?new UnitViewAttributes.CameraFocusSettings():(UnitViewAttributes.CameraFocusSettings)Copy(NativeFocus);
                float current=Camera.DistanceToTargetMeters,desired=Convert.ToSingle(Read(Camera,"DesiredDistanceToTarget"));
                OwnedFocus.ClosestCameraFocus=Math.Max(Humanoid?Config.HumanoidClosestDistanceM:Config.VehicleClosestDistanceM,physical.magnitude*.325f);
                OriginalTarget=CameraTargetReference.Read(Camera);
                if(!Config.PreserveNativeFocus)
                {
                    if(Anchor==null){Anchor=new GameObject("EF_SmallUnit_CameraFocus_"+Camera.GetInstanceID());Anchor.hideFlags=HideFlags.HideAndDontSave;UnityEngine.Object.DontDestroyOnLoad(Anchor);}
                    Anchor.transform.position=root.TransformPoint(bounds.center);
                    CameraTargetReference.Write(Camera,Anchor.transform);
                }
                FocusReference.Write(Camera,OwnedFocus);Reparameterise(current,desired);
                SetPitchStyle(Humanoid?Config.HumanoidNearPitchDegrees:Config.VehicleNearPitchDegrees);
                float pitch=Camera.AutoPitch?(Humanoid?Config.HumanoidNearPitchDegrees:Config.VehicleNearPitchDegrees):NormalPitch(Camera.Rotation.eulerAngles.x);
                Quaternion orientation=Quaternion.Euler(pitch,Camera.Rotation.eulerAngles.y,0f);
                float framing=FrameDistance(bounds,root,orientation,Lens.fieldOfView,Lens.aspect,Config.FocusFramingPadding,OwnedFocus.ClosestCameraFocus);
                if(frame&&!Config.PreserveNativeFocus&&Config.FrameOnNativeFocus&&NativeFocusObserved&&!Camera.SensorsManagerActive)
                {
                    // This is the same native desired-zoom channel used by wheel
                    // input. Native following/dolly easing owns the transition.
                    Write(Camera,"mDesiredDistanceLERPValue",Inverse(GameplayCurve,OwnedFocus.ClosestCameraFocus,Camera.CurrentMaxDistance,framing));
                }
                NativeFocusObserved=false;
                Runtime.Log("SMALL_CAMERA_NATIVE_FOCUS realVertices="+vertexCount+"; bodyBounds="+bounds+"; profile="+(Humanoid?"human-scale":"vehicle-scale")+
                    "; closest="+OwnedFocus.ClosestCameraFocus+"; fitDistance="+framing+"; requestedNativeZoom="+(frame&&!Config.PreserveNativeFocus&&Config.FrameOnNativeFocus)+
                    "; fov="+Lens.fieldOfView+"; aspect="+Lens.aspect+"; selectionChangesDoNotTrigger=True; nativeBaseTargetUnchanged=True; nativeFocusPreserved="+Config.PreserveNativeFocus+"; anchorOnlyCameraOwned=True");
            }
            void RestoreFocus()
            {
                if(Camera!=null)
                {
                    if(OwnedFocus!=null&&ReferenceEquals(FocusReference.Read(Camera),OwnedFocus))FocusReference.Write(Camera,NativeFocus);
                    if(Anchor!=null&&ReferenceEquals(CameraTargetReference.Read(Camera),Anchor.transform))CameraTargetReference.Write(Camera,OriginalTarget);
                }
                BoundView=null;GeometryRoot=null;OriginalTarget=null;NativeFocus=OwnedFocus=null;
            }
            void Reparameterise(float current,float desired)
            {
                if(Camera.SensorsManagerActive)return;
                float min=Camera.ClosestCameraDistanceToTarget,max=Camera.CurrentMaxDistance;
                Write(Camera,"mCurrentDistanceLERPValue",Inverse(GameplayCurve,min,max,current));Write(Camera,"mDesiredDistanceLERPValue",Inverse(GameplayCurve,min,max,desired));
            }
            void SetPitchStyle(float near)
            {
                if(near==PitchStyle)return;PitchStyle=near;
                Motion.DefaultPitchVersusDistance=float.IsPositiveInfinity(near)?OriginalPitch:Humanoid?HumanoidPitch:VehiclePitch;
                Motion.MinPitchVersusDistanceFromTarget=float.IsPositiveInfinity(near)?OriginalMinPitch:NearMinPitch;
            }
            void UpdateCollision(float blend)
            {
                for(int i=0;i<CollisionKeys.Length;i++)
                {Keyframe key=CollisionKeys[i];key.value=Lerp(Config.NearCollisionRadiusM,key.value,blend);key.inTangent*=blend;key.outTangent*=blend;WorkingCollision.MoveKey(i,key);}
                if(!ReferenceEquals(Collision.ColliderSizeVersusVelocityMS,WorkingCollision))Collision.ColliderSizeVersusVelocityMS=WorkingCollision;
            }
            public void Restore()
            {
                if(Noise!=null){Noise.Restore();Noise=null;}
                if(Camera!=null)
                {
                    RestoreFocus();
                    if(Installed)
                    {
                        float actual=Camera.DistanceToTargetMeters,desired=Convert.ToSingle(Read(Camera,"DesiredDistanceToTarget"));
                        if(ReferenceEquals(Camera.MotionValues,Motion))Camera.MotionValues=OriginalMotion;
                        if(ReferenceEquals(Camera.Thresholds,Thresholds))Camera.Thresholds=OriginalThresholds;
                        if(ReferenceEquals(Camera.CollisionValues,Collision))Camera.CollisionValues=OriginalCollision;
                        foreach(KeyValuePair<string,object> frame in Frames)Write(Camera,frame.Key,frame.Value);
                        if(!Camera.SensorsManagerActive&&OriginalMotion!=null)
                        {
                            Write(Camera,"mCurrentDistanceLERPValue",Inverse(OriginalMotion.ScrollTickLERPCurveGameplay,Camera.ClosestCameraDistanceToTarget,Camera.CurrentMaxDistance,actual));
                            Write(Camera,"mDesiredDistanceLERPValue",Inverse(OriginalMotion.ScrollTickLERPCurveGameplay,Camera.ClosestCameraDistanceToTarget,Camera.CurrentMaxDistance,desired));
                        }
                    }
                }
                if(Lens!=null)Lens.nearClipPlane=OriginalNear;
                if(Anchor!=null)UnityEngine.Object.DestroyImmediate(Anchor);Anchor=null;Installed=false;Frames.Clear();
            }
        }

        static bool Measure(UnitView view,out Transform root,out Bounds bounds,out int count)
        {
            BvmRigBinding[] rigs=view.GetComponentsInChildren<BvmRigBinding>(true);BvmRigBinding rig=rigs.Length==1?rigs[0]:null;
            root=rig==null?view.transform:rig.ModelRoot;bounds=new Bounds();count=0;if(root==null)return false;
            HashSet<Mesh> meshes=new HashSet<Mesh>();List<float> heights=new List<float>();bool ready=false;
            foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if(renderer is ParticleSystemRenderer||renderer is TrailRenderer||renderer is LineRenderer)continue;
                SkinnedMeshRenderer skin=renderer as SkinnedMeshRenderer;MeshFilter filter=renderer.GetComponent<MeshFilter>();Mesh mesh=skin!=null?skin.sharedMesh:(filter==null?null:filter.sharedMesh);
                if(mesh==null||mesh.vertexCount==0)continue;
                if(rig!=null)
                {
                    if(!mesh.name.StartsWith("EF_BVM_Chunk",StringComparison.Ordinal))continue;
                    int kind=BvmRigBinding.Static;
                    if(skin!=null&&skin.bones!=null&&skin.bones.Length==1)
                        for(int i=0;i<rig.Bones.Length;i++)if(rig.Bones[i]==skin.bones[0]){kind=rig.RigKinds[i];break;}
                    if(kind==BvmRigBinding.Pitch||kind==BvmRigBinding.Recoil)continue;
                }
                else
                {
                    string name=renderer.gameObject.name.ToLowerInvariant();
                    if(name.Contains("selection")||name.Contains("sensor")||name.Contains("dust")||name.Contains("light")||name.Contains("shadow")||name.Contains("particle"))continue;
                }
                if(rig!=null&&!meshes.Add(mesh))continue;
                foreach(Vector3 vertex in Vertices(mesh,skin))
                {
                    Vector3 point=rig!=null?vertex:root.InverseTransformPoint(renderer.transform.TransformPoint(vertex));
                    if(!ready){bounds=new Bounds(point,Vector3.zero);ready=true;}else bounds.Encapsulate(point);heights.Add(point.y);count++;
                }
            }
            if(!ready||count==0)return false;
            if(rig!=null)
            {
                heights.Sort();float top=heights[Math.Min(heights.Count-1,(int)(heights.Count*.995f))];
                Vector3 lo=bounds.min,hi=bounds.max;hi.y=Math.Max(lo.y+.1f,top);bounds.SetMinMax(lo,hi);
            }
            return bounds.size.sqrMagnitude>.001f;
        }
        static Vector3[] Vertices(Mesh mesh,SkinnedMeshRenderer skin)
        {
            // Humanoid source vertices use their original FBX bind space.
            // Measure the actual posed shape, rather than those raw bind vertices.
            // This runs only when native focus changes, not on every frame.
            if(skin!=null&&mesh.name.StartsWith("EF_HUMANOID_",StringComparison.OrdinalIgnoreCase))
            {
                Mesh posed=new Mesh();
                try{skin.BakeMesh(posed);if(posed.vertexCount>0)return posed.vertices;}
                finally{UnityEngine.Object.DestroyImmediate(posed);}
            }
            // Unity 5 may log an error and return an empty array instead of
            // throwing when UploadMeshData(true) has released CPU vertices.
            if(mesh.isReadable)
            {
                try{Vector3[] readable=mesh.vertices;if(readable!=null&&readable.Length>0)return readable;}
                catch(UnityException){ }
            }
            if(skin!=null)
            {
                Mesh baked=new Mesh();
                try{skin.BakeMesh(baked);if(baked.vertexCount>0)return baked.vertices;}
                catch(UnityException){ }
                finally{UnityEngine.Object.DestroyImmediate(baked);}
            }
            // GPU-only meshes retain local geometry bounds. This keeps focus
            // measurement valid without retaining another CPU vertex copy.
            Bounds b=mesh.bounds;Vector3[] points=new Vector3[8];
            for(int i=0;i<8;i++)points[i]=new Vector3((i&1)==0?b.min.x:b.max.x,(i&2)==0?b.min.y:b.max.y,(i&4)==0?b.min.z:b.max.z);
            return points;
        }
        public static float FrameDistance(Bounds local,Transform root,Quaternion orientation,float verticalFov,float aspect,float padding,float minimum)
        {
            Vector3 center=root.TransformPoint(local.center);Quaternion inverse=Quaternion.Inverse(orientation);
            double tanV=Math.Tan(verticalFov*Math.PI/360.0),tanH=tanV*Math.Max(.1f,aspect);double required=minimum;
            for(int i=0;i<8;i++)
            {
                Vector3 corner=new Vector3((i&1)==0?local.min.x:local.max.x,(i&2)==0?local.min.y:local.max.y,(i&4)==0?local.min.z:local.max.z);
                Vector3 p=inverse*(root.TransformPoint(corner)-center);
                required=Math.Max(required,Math.Max(Math.Abs(p.x)*padding/tanH,Math.Abs(p.y)*padding/tanV)-p.z);
            }
            return (float)Math.Max(minimum,required+.15);
        }
        public static float AdaptiveNear(float distance,float nativeNear,float far,bool humanoid,Configuration cfg)
        {
            float close=humanoid?cfg.HumanoidNearClipM:cfg.VehicleNearClipM;
            float wide=Math.Max(nativeNear,Math.Max(cfg.WideNearClipMinimumM,distance*cfg.WideNearClipDistanceFactor));
            float value=Lerp(close,wide,Smooth(cfg.ClipBlendStartDistanceM,cfg.ClipBlendEndDistanceM,distance));
            return Math.Max(.01f,Math.Min(Math.Min(cfg.MaximumNearClipM,far*.1f),Math.Min(value,distance*.20f)));
        }
        static Vector3 PhysicalSize(Bounds bounds,Transform root)
        {Vector3 scale=root.lossyScale;return new Vector3(bounds.size.x*Math.Abs(scale.x),bounds.size.y*Math.Abs(scale.y),bounds.size.z*Math.Abs(scale.z));}
        static float Maximum(Vector3 v){return Math.Max(v.x,Math.Max(v.y,v.z));}
        static float NormalPitch(float angle){return angle>180f?angle-360f:angle;}
        static float Lerp(float a,float b,float t){return a+(b-a)*Math.Max(0f,Math.Min(1f,t));}
        static float Smooth(float start,float end,float x){float t=Math.Max(0f,Math.Min(1f,(x-start)/(end-start)));return t*t*(3f-2f*t);}
        static AnimationCurve LogCurve(float minimum,float maximum)
        {
            if(maximum<=minimum)throw new InvalidOperationException("Native gameplay camera range is invalid");
            Keyframe[] keys=new Keyframe[65];double ratio=Math.Log(maximum/minimum);
            for(int i=0;i<keys.Length;i++)
            {float x=i/(float)(keys.Length-1);double distance=minimum*Math.Exp(ratio*x);float y=(float)((distance-minimum)/(maximum-minimum)),slope=(float)(ratio*distance/(maximum-minimum));keys[i]=new Keyframe(x,y,slope,slope);}
            return new AnimationCurve(keys);
        }
        static float Inverse(AnimationCurve curve,float minimum,float maximum,float distance)
        {
            if(curve==null||maximum<=minimum)return 0f;float value=Math.Max(0f,Math.Min(1f,(distance-minimum)/(maximum-minimum))),lo=0f,hi=1f;
            for(int i=0;i<32;i++){float mid=(lo+hi)*.5f;if(curve.Evaluate(mid)<value)lo=mid;else hi=mid;}return(lo+hi)*.5f;
        }
        static AnimationCurve NearPitchCurve(AnimationCurve source,float near)
        {
            float start=Config.PitchBlendStartDistanceM,end=Config.PitchBlendEndDistanceM,far=source.Evaluate(end),slope=(far-near)/(end-start);
            float derivative=(source.Evaluate(end+1f)-source.Evaluate(end-1f))*.5f;
            List<Keyframe> keys=new List<Keyframe>();keys.Add(new Keyframe(0f,near,0f,0f));keys.Add(new Keyframe(start,near,0f,slope));keys.Add(new Keyframe(end,far,slope,derivative));
            foreach(Keyframe key in source.keys)if(key.time>end)keys.Add(key);AnimationCurve result=new AnimationCurve(keys.ToArray());result.preWrapMode=source.preWrapMode;result.postWrapMode=source.postWrapMode;return result;
        }
        static AnimationCurve BlendCollision(AnimationCurve source,float blend)
        {
            Keyframe[] keys=source.keys;for(int i=0;i<keys.Length;i++){keys[i].value=Lerp(Config.NearCollisionRadiusM,keys[i].value,blend);keys[i].inTangent*=blend;keys[i].outTangent*=blend;}
            AnimationCurve result=new AnimationCurve(keys);result.preWrapMode=source.preWrapMode;result.postWrapMode=source.postWrapMode;return result;
        }
        static AnimationCurve CopyCurve(AnimationCurve source)
        {if(source==null)throw new InvalidOperationException("Native camera curve missing");AnimationCurve result=new AnimationCurve(source.keys);result.preWrapMode=source.preWrapMode;result.postWrapMode=source.postWrapMode;return result;}
        sealed class Identity:IEqualityComparer<object>
        {public new bool Equals(object a,object b){return ReferenceEquals(a,b);}public int GetHashCode(object a){return RuntimeHelpers.GetHashCode(a);}}
        static object Copy(object source){return Copy(source,new Dictionary<object,object>(new Identity()));}
        static object Copy(object source,Dictionary<object,object> seen)
        {
            if(source==null)return null;Type type=source.GetType();
            if(type.IsPrimitive||type.IsEnum||type==typeof(string)||type.IsValueType||source is UnityEngine.Object)return source;
            AnimationCurve curve=source as AnimationCurve;if(curve!=null)return CopyCurve(curve);object old;if(seen.TryGetValue(source,out old))return old;
            Array array=source as Array;if(array!=null){Array result=(Array)array.Clone();seen.Add(source,result);for(int i=0;i<result.Length;i++)result.SetValue(Copy(array.GetValue(i),seen),i);return result;}
            if(type.Namespace==null||!type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))return source;
            object clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(source,null);seen.Add(source,clone);
            for(Type t=type;t!=null&&t!=typeof(object);t=t.BaseType)foreach(FieldInfo field in t.GetFields(Flags))if(!field.IsStatic)field.SetValue(clone,Copy(field.GetValue(source),seen));return clone;
        }
        static object Read(object target,string name)
        {return NativeRead.Get(target,name);}
        static void Write(object target,string name,object value)
        {NativeRead.Set(target,name,value);}

        static void CheckConfiguration()
        {
            if(ConfigKnown&&(!Runtime.DetailedDiagnosticsEnabled||Time.realtimeSinceStartup<NextConfigCheck))return;NextConfigCheck=Time.realtimeSinceStartup+1f;
            string file=Path.Combine(CustomUnits.AssetDirectory,"camera_small_units.ini");DateTime stamp=File.Exists(file)?File.GetLastWriteTimeUtc(file):DateTime.MinValue;
            if(ConfigKnown&&stamp==ConfigStamp)return;
            // A native sensors transition owns its current goal/time pair.
            // Reload on the next steady frame rather than replacing that goal.
            if(ConfigKnown)foreach(CameraState state in States)
            {
                if(state.Camera==null||!state.Camera.gameObject.activeInHierarchy)continue;
                object motionState=Read(state.Camera,"mMotionState");
                if(motionState!=null&&Convert.ToInt32(motionState)==9)return;
            }
            Configuration next=Load(file);RestoreStates();Config=next;ConfigStamp=stamp;ConfigKnown=true;NextDiscovery=0f;
            Runtime.Log("SMALL_CAMERA_CONFIGURATION loaded="+file+"; enabled="+next.Enabled+"; dialogs=False; assetOrGameProcessChanges=False; actualVisualAcceptancePending=True");
        }
        public static Configuration Load(string file)
        {
            Configuration result=new Configuration();Dictionary<string,FieldInfo> fields=new Dictionary<string,FieldInfo>(StringComparer.OrdinalIgnoreCase);
            foreach(FieldInfo field in typeof(Configuration).GetFields(BindingFlags.Public|BindingFlags.Instance))fields.Add(field.Name,field);
            HashSet<string> used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if(File.Exists(file))foreach(string raw in File.ReadAllLines(file))
            {
                string line=raw.Trim();if(line.Length==0||line.StartsWith("#")||line.StartsWith(";")||(line.StartsWith("[")&&line.EndsWith("]")))continue;
                int split=line.IndexOf('=');if(split<1)throw new InvalidDataException("Camera config expected key=value");string key=line.Substring(0,split).Trim(),value=line.Substring(split+1).Trim();
                int comment=value.IndexOfAny(new char[]{'#',';'});if(comment>=0)value=value.Substring(0,comment).Trim();FieldInfo field;
                if(!fields.TryGetValue(key,out field)||!used.Add(key))throw new InvalidDataException("Unknown or repeated camera config key: "+key);
                object parsed=field.FieldType==typeof(bool)?(object)bool.Parse(value):(field.FieldType==typeof(int)?(object)int.Parse(value,CultureInfo.InvariantCulture):(object)float.Parse(value,CultureInfo.InvariantCulture));field.SetValue(result,parsed);
            }
            foreach(FieldInfo field in typeof(Configuration).GetFields(BindingFlags.Public|BindingFlags.Instance))if(field.FieldType==typeof(float))
            {float value=(float)field.GetValue(result);if(float.IsNaN(value)||float.IsInfinity(value)||value<=0f||value>100000f)throw new InvalidDataException("Invalid camera value: "+field.Name);}
            if(result.GameplayScrollTicks<8||result.GameplayScrollTicks>128||result.FreeMinimumDistanceM<1.1f||result.NearCollisionRadiusM<1f||
                result.ClipBlendEndDistanceM<=result.ClipBlendStartDistanceM||result.PanBlendEndDistanceM<=result.PanBlendStartDistanceM||result.PitchBlendEndDistanceM<=result.PitchBlendStartDistanceM||
                result.VehicleNearPitchDegrees>=80f||result.HumanoidNearPitchDegrees>=80f||result.FocusFramingPadding<1f||result.HumanoidMaximumSizeM>=result.SmallUnitMaximumSizeM)
                throw new InvalidDataException("Camera configuration range is inconsistent");
            return result;
        }
    }
}
