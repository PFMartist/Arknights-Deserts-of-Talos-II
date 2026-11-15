using System;
using BBI.Core.ComponentModel;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Simulation;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // No Update/LateUpdate. The host calls Step AFTER the same frame's native
    // visibility/module gate. This component never changes simulation or hull
    // transforms and never changes vertices, UV arrays or material state.
    public sealed class RhodesTrackMotionDriver:MonoBehaviour
    {
        UnitView View;
        RhodesTrackLiveRig Rig;
        Transform Root;
        Func<string,Transform> FindNode;
        Entity Owner;
        double[] Distances,AppliedDistances;
        bool[] Dirty,DetailActive;
        float[] WorldShoePitch;
        Vector3[][] AppliedPositions;
        float[][] AppliedAngles;
        float[][] SegmentEnds;
        int LastFrame=-1;
        bool Initialized,Attempted;
        static Camera CachedCamera;
        static int CameraFrame=-1;
        static float CameraRetryAt;
        static CameraSample FrameCamera;
        struct CameraSample
        {
            internal bool Valid,Orthographic;
            internal Vector3 Position,Forward,Right,Up;
            internal float Near,Far,TanHorizontal,TanVertical,HalfWidth,HalfHeight,PixelsPerMetre;
        }
        public void Initialize(UnitView view,Transform presentationRoot,Func<string,Transform> findNode)
        {
            if(view==null||view.EntityID.Equals(Entity.None))return;
            if(Attempted&&View==view&&Root==presentationRoot&&Owner.Equals(view.EntityID))return;
            View=view;Root=presentationRoot;FindNode=findNode;Owner=view.EntityID;Attempted=true;LastFrame=-1;Initialized=false;Rig=null;
            try
            {
            RhodesTrackLiveRig rig=RhodesTrackLoader.RebindLive(presentationRoot,view.transform,findNode);Rig=rig;
            Distances=new double[rig.Groups.Length];AppliedDistances=new double[rig.Groups.Length];Dirty=new bool[rig.Groups.Length];
            DetailActive=new bool[rig.Groups.Length];WorldShoePitch=new float[rig.Groups.Length];
            AppliedPositions=new Vector3[rig.Groups.Length][];AppliedAngles=new float[rig.Groups.Length][];
            SegmentEnds=new float[rig.Groups.Length][];
            for(int i=0;i<rig.Groups.Length;i++)
            {
                RhodesTrackGroupState group=rig.Groups[i];
                if(group==null||group.Path==null||group.Bones==null||group.Kinds.Length!=group.Bones.Length||group.InitialPositions.Length!=group.Bones.Length||group.InitialAngles.Length!=group.Bones.Length||group.BaseDistances.Length!=group.Bones.Length||group.Radii.Length!=group.Bones.Length)
                    throw new InvalidOperationException("Rhodes cloned track rig serialization incomplete");
                if(group.Renderer==null||!group.Renderer.transform.IsChildOf(rig.PresentationRoot))
                    throw new InvalidOperationException("Rhodes cloned track renderer escaped its live presentation root");
                SegmentEnds[i]=new float[group.Path.Length];float sum=0f;
                for(int k=0;k<group.Path.Length;k++){sum+=group.Path[k].Length;SegmentEnds[i][k]=sum;}
                if(Math.Abs(sum-group.Perimeter)>.005f)throw new InvalidOperationException("Rhodes track path changed after cloning");
                AppliedPositions[i]=new Vector3[group.Bones.Length];AppliedAngles[i]=new float[group.Bones.Length];
                int shoes=0;DetailActive[i]=true;
                for(int k=0;k<group.Bones.Length;k++)
                {
                    if(group.Bones[k]==null||!group.Bones[k].IsChildOf(rig.PresentationRoot))throw new InvalidOperationException("Rhodes cloned track bone escaped its live presentation root");
                    // Rebuild runtime phase only; the asset bind pose is immutable.
                    group.Bones[k].localPosition=group.InitialPositions[k];group.Bones[k].localRotation=Quaternion.AngleAxis(group.InitialAngles[k]*Mathf.Rad2Deg,Vector3.right);
                    AppliedPositions[i][k]=group.InitialPositions[k];AppliedAngles[i][k]=group.InitialAngles[k];
                    if(group.Kinds[k]==0)shoes++;
                }
                if(shoes<1)throw new InvalidOperationException("Rhodes track group has no circulating shoes");
                WorldShoePitch[i]=group.Perimeter*group.DistanceScale/shoes;
            }
            Initialized=true;
            int bones=0;foreach(RhodesTrackGroupState group in rig.Groups)bones+=group.Bones.Length;
            Runtime.Log("RHODES_TRACK_RIG_BOUND entity="+Owner+"; revision=03; bogies="+rig.Groups.Length+"; bones="+bones+"; legacyBindingPresent="+rig.LegacyBindingPresent+"; legacyGroupsPresent="+rig.LegacyGroupsPresent+"; nativeBoneArrays="+rig.NativeBoneArrays+"; repairedBoneArrays="+rig.RepairedBoneArrays+"; bogieLookupFallbacks="+rig.BogieLookupFallbacks+"; managedLiveCacheRebuilt=True; existingSkinsAndBonesOnly=True; liveCloneOwnershipVerified=True; signedNativeMotion=True; nativeVisibilityAndPauseGate=True; runtimeVisualAcceptancePending=True");
            }
            catch(Exception error)
            {Rig=null;Runtime.Log("RHODES_TRACK_LIVE_REBIND_FAILED entity="+Owner+"; revision=03; attemptedOnceForEntityRoot=True; noLiveDiskRead=True; noGeometryCreated=True; error="+error.Message);}
        }
        public void Step(UnitView view,bool shown,bool animate)
        {
            if(!Initialized||view==null||view!=View||!Owner.Equals(view.EntityID))
            {if(view!=null&&Root!=null)Initialize(view,Root,FindNode);else return;}
            if(!Initialized||LastFrame==Time.frameCount)return;LastFrame=Time.frameCount;
            float dt=Runtime.PresentationDeltaTime;
            if(dt<=0f||Rig==null||Rig.PresentationRoot==null||view.SimStateInterpolator==null)return;
            UnitState state=view.NonInterpolatedTickState;if(state==null||state.UnitIsDead)return;
            UnitDynamicState dynamic=state.StartOfFrameDynamicState;
            float forward=Fixed64.UnsafeFloatValue(dynamic.ForwardSpeed),angular=Fixed64.UnsafeFloatValue(dynamic.AngularMomentum);
            if(float.IsNaN(forward)||float.IsInfinity(forward)||float.IsNaN(angular)||float.IsInfinity(angular))return;
            bool canWrite=shown&&animate&&Rig.PresentationRoot.gameObject.activeInHierarchy;
            CameraSample camera=canWrite?PresentationCamera():new CameraSample();
            for(int i=0;i<Rig.Groups.Length;i++)
            {
                RhodesTrackGroupState group=Rig.Groups[i];
                // Native angular velocity is radians/s, positive opposite to
                // Unity Y yaw. Evaluate actual speed at this bogie's X offset.
                Distances[i]=RhodesTrackPresentationPolicy.Advance(Distances[i],forward,angular,group.LocalRight,dt,group.DistanceScale);
                Dirty[i]=Math.Abs(Distances[i]-AppliedDistances[i])>=1e-8;
                // Phase follows native motion while outside the camera; avoid
                // thousands of Transform writes while the native wheel LOD is
                // inactive or its skin is invisible, then
                // catch up its pose once visible, even if it has since stopped.
                if(!canWrite||!Dirty[i]||group.Renderer==null||!group.Renderer.isVisible)continue;
                if(camera.Valid)
                {
                    Bounds bounds=group.Renderer.bounds;Vector3 offset=bounds.center-camera.Position;float radius=bounds.extents.magnitude;
                    float depth=Vector3.Dot(offset,camera.Forward);
                    if(!RhodesTrackPresentationPolicy.SphereInView(depth,Vector3.Dot(offset,camera.Right),Vector3.Dot(offset,camera.Up),radius,
                        camera.Near,camera.Far,camera.TanHorizontal,camera.TanVertical,camera.Orthographic,camera.HalfWidth,camera.HalfHeight))continue;
                    float pixels=RhodesTrackPresentationPolicy.ShoePixels(WorldShoePitch[i],depth,radius,camera.Near,camera.PixelsPerMetre,camera.Orthographic);
                    DetailActive[i]=RhodesTrackPresentationPolicy.DetailActive(DetailActive[i],pixels);
                    if(!DetailActive[i])continue;
                }
                ApplyGroup(group,SegmentEnds[i],Distances[i],AppliedPositions[i],AppliedAngles[i]);AppliedDistances[i]=Distances[i];Dirty[i]=false;
            }
        }
        static bool Usable(Camera camera){return camera!=null&&camera.enabled&&camera.gameObject.activeInHierarchy;}
        static CameraSample PresentationCamera()
        {
            if(CameraFrame==Time.frameCount)return FrameCamera;CameraFrame=Time.frameCount;
            UserCamera user=SmallUnitCamera.ActiveGameplayCamera;
            Camera active=user==null?null:user.ActiveCamera;
            if(Usable(active))CachedCamera=active;
            else if(!Usable(CachedCamera)&&Time.realtimeSinceStartup>=CameraRetryAt)
            {CachedCamera=Camera.main;CameraRetryAt=Time.realtimeSinceStartup+2f;}
            FrameCamera=new CameraSample();if(!Usable(CachedCamera))return FrameCamera;
            Camera camera=CachedCamera;Transform t=camera.transform;
            float tangent=Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f);
            if(!RhodesTrackPresentationPolicy.Finite(tangent)||tangent<=0f||camera.aspect<=0f)return FrameCamera;
            FrameCamera=new CameraSample{Valid=true,Orthographic=camera.orthographic,Position=t.position,Forward=t.forward,Right=t.right,Up=t.up,
                Near=camera.nearClipPlane,Far=camera.farClipPlane,TanVertical=tangent,TanHorizontal=tangent*camera.aspect,
                HalfHeight=camera.orthographicSize,HalfWidth=camera.orthographicSize*camera.aspect,
                PixelsPerMetre=RhodesTrackPresentationPolicy.PixelsPerMetre(camera.fieldOfView,camera.pixelHeight,camera.orthographic,camera.orthographicSize)};
            return FrameCamera;
        }
        static void ApplyGroup(RhodesTrackGroupState group,float[] ends,double distance,Vector3[] positions,float[] angles)
        {
            for(int i=0;i<group.Bones.Length;i++)
            {
                Transform bone=group.Bones[i];if(bone==null)continue;
                Vector3 p;float angle;
                if(group.Kinds[i]==0)
                {
                    // Authored positive path goes aft on the top run; physical
                    // forward travel therefore advances the path negatively.
                    double s=(group.BaseDistances[i]-distance)%group.Perimeter;if(s<0)s+=group.Perimeter;
                    Sample(group.Path,ends,(float)s,out p,out angle);
                }
                else
                {
                    p=group.InitialPositions[i];angle=(float)((group.InitialAngles[i]+distance/group.Radii[i])%(Math.PI*2.0));
                }
                // Wheels never translate in their owned rig space. Shoes on a
                // straight run keep the same angle until they enter an arc.
                if(group.Kinds[i]==0&&(p-positions[i]).sqrMagnitude>1e-12f){bone.localPosition=p;positions[i]=p;}
                if(RhodesTrackPresentationPolicy.RotationChanged(angles[i],angle))
                {bone.localRotation=Quaternion.AngleAxis(angle*Mathf.Rad2Deg,Vector3.right);angles[i]=angle;}
            }
        }
        public static void Sample(RhodesTrackSegmentState[] path,float[] ends,float distance,out Vector3 position,out float angle)
        {
            int selected=0;while(selected<ends.Length-1&&distance>ends[selected])selected++;
            RhodesTrackSegmentState segment=path[selected];float local=distance-(selected==0?0f:ends[selected-1]);
            if(segment.Kind==0)
            {Vector2 p=Vector2.Lerp(segment.A,segment.B,Mathf.Clamp01(local/segment.Length));position=new Vector3(0f,p.x,p.y);angle=segment.Angle;}
            else
            {angle=segment.Angle-local/segment.Radius;position=new Vector3(0f,segment.A.x+segment.Radius*Mathf.Cos(angle),segment.A.y+segment.Radius*Mathf.Sin(angle));}
        }
        void OnDisable(){LastFrame=-1;}
    }
}
