using System;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // A bounded presentation approximation of the source's single quan_jnt
    // write-back damper. The source .15s orientation smoothing is retained;
    // gains and the small 5-degree bound are adapter tuning, not source physics.
    // No Magica cloth, garment/body collision or source translation compensation.
    internal sealed class LaevatainAccessoryDamping
    {
        Transform Target,Accessory,AccessoryParent;
        Renderer VisibleRenderer;Camera CameraCache;UserCamera CameraOwner;
        const float NearDistanceMetres=120f;
        readonly Quaternion ReferenceLocalRotation;
        readonly LaevatainAccessorySpring Spring=new LaevatainAccessorySpring();
        Quaternion PreviousReference,AuthoredRotation;
        Vector3 PreviousVelocity,PreviousPosition;
        bool HaveReference,Applied;
        internal static LaevatainAccessoryDamping Bind(HumanoidBinding binding,HumanoidData data)
        {
            if(binding==null||data==null||binding.MemberCount!=1||binding.AssetFolder!="humanoid_trial/laevatain")return null;
            int target=-1,accessory=-1;
            for(int i=0;i<data.Nodes.Length;i++)
            {
                if(data.Nodes[i].Name=="Root/Bip001")target=i;
                if(data.Nodes[i].Name=="Root/quan_jnt")accessory=i;
            }
            if(target<0||accessory<0||data.Nodes[target].Parent!=data.Nodes[accessory].Parent)return null;
            Transform body=binding.Bones[target],ring=binding.Bones[accessory];
            if(body==null||ring==null||ring.parent==null)return null;
            Runtime.Log("LAEVATAIN_ACCESSORY_DAMPING_BOUND sourceTarget=Root/Bip001; sourceWriteBack=Root/quan_jnt; sourceQuatSeconds=.15; adapterAngularCapDegrees=5; locomotionOnly=True; authoredAttackAndSkillTracksRetained=True; sourceTranslationAndMagicaClothRecreated=False; newUpdateMethods=0; recurringBoneLookup=False; runtimeAcceptancePending=True");
            Renderer visible=null;
            for(int i=0;i<data.Parts.Length;i++)if(binding.Renderers[i]!=null&&
                (binding.PartVisibility==null||i>=binding.PartVisibility.Length||binding.PartVisibility[i]))
            {if(visible==null)visible=binding.Renderers[i];if(data.Parts[i].Name.IndexOf("body",StringComparison.OrdinalIgnoreCase)>=0){visible=binding.Renderers[i];break;}}
            return new LaevatainAccessoryDamping(body,ring,data.Nodes[target].Rotation,visible);
        }
        LaevatainAccessoryDamping(Transform target,Transform accessory,Quaternion reference,Renderer visible)
        {
            Target=target;Accessory=accessory;AccessoryParent=accessory.parent;ReferenceLocalRotation=reference;VisibleRenderer=visible;
            // Camera discovery happens at binding only; never in the step loop.
            CameraCache=Camera.main;
            if(CameraCache==null)foreach(UserCamera user in UnityEngine.Object.FindObjectsOfType(typeof(UserCamera)))
                if(user!=null&&user.gameObject.activeInHierarchy&&user.ActiveCamera!=null&&user.ActiveCamera.enabled)
                {CameraOwner=user;CameraCache=user.ActiveCamera;break;}
        }

        // Remove only our last additive before the driver's transition From[]
        // snapshot. Otherwise a clip change would capture that delta and add it
        // a second time. The freshly sampled source pose is always the base.
        internal void RemoveAppliedPose()
        {if(Applied&&Accessory!=null)Accessory.localRotation=AuthoredRotation;Applied=false;}
        internal void Step(UnitView view,HumanoidClip clip,float dt,Vector3 nativeVelocity,bool alive)
        {
            if(Target==null||Accessory==null||AccessoryParent==null)return;
            Camera camera=CameraOwner==null?CameraCache:CameraOwner.ActiveCamera;
            bool eligible=alive&&view!=null&&(int)view.Visibility==2&&!view.ForceIconOnlyMode&&
                !UserCamera.IsSensorsModeActive&&VisibleRenderer!=null&&VisibleRenderer.isVisible&&
                camera!=null&&camera.enabled&&camera.gameObject.activeInHierarchy&&
                (camera.transform.position-Target.position).sqrMagnitude<=NearDistanceMetres*NearDistanceMetres;
            if(!eligible){ResetMotion();return;}
            Quaternion reference=Target.rotation*Quaternion.Inverse(ReferenceLocalRotation);
            Vector3 position=Target.position;
            if(!LaevatainAccessorySpring.Finite(dt)||dt<0f||!Finite(nativeVelocity)||!Finite(position))
            {ResetMotion();return;}
            if(!HaveReference||dt>.25f||(position-PreviousPosition).sqrMagnitude>25f)
            {ResetMotion();Seed(reference,position,nativeVelocity);}
            bool locomotion=clip!=null&&(clip.Role=="idle"||clip.Role=="walk"||clip.Role=="run");
            if(dt>0f)
            {
                Quaternion delta=Quaternion.Inverse(PreviousReference)*reference;
                if(delta.w<0f)delta=new Quaternion(-delta.x,-delta.y,-delta.z,-delta.w);
                float angle;Vector3 axis;delta.ToAngleAxis(out angle,out axis);
                if(angle>180f)angle-=360f;
                Vector3 angular=Finite(axis)&&LaevatainAccessorySpring.Finite(angle)?axis*(angle/dt):Vector3.zero;
                Vector3 acceleration=Quaternion.Inverse(reference)*((nativeVelocity-PreviousVelocity)/dt);
                Spring.Advance(dt,locomotion,angular.x,angular.y,angular.z,acceleration.z,acceleration.x);
                Seed(reference,position,nativeVelocity);
            }
            // Applying a decaying additive never replaces an authored attack,
            // skill or death quaternion. Hidden/dead actors reset immediately.
            AuthoredRotation=Accessory.localRotation;
            Quaternion offset=Quaternion.Euler(Spring.Pitch,Spring.Yaw,Spring.Roll);
            Quaternion worldDelta=reference*offset*Quaternion.Inverse(reference);
            Quaternion localDelta=Quaternion.Inverse(AccessoryParent.rotation)*worldDelta*AccessoryParent.rotation;
            Accessory.localRotation=localDelta*AuthoredRotation;Applied=true;
        }
        void Seed(Quaternion reference,Vector3 position,Vector3 velocity)
        {PreviousReference=reference;PreviousPosition=position;PreviousVelocity=velocity;HaveReference=true;}
        void ResetMotion(){Spring.Reset();HaveReference=false;}
        static bool Finite(Vector3 v)
        {return LaevatainAccessorySpring.Finite(v.x)&&LaevatainAccessorySpring.Finite(v.y)&&LaevatainAccessorySpring.Finite(v.z);}
        internal void Release()
        {RemoveAppliedPose();ResetMotion();Target=null;Accessory=null;AccessoryParent=null;VisibleRenderer=null;CameraCache=null;CameraOwner=null;}
    }

    // Pure, allocation-free small-angle critically damped response. Closed
    // form per axis remains stable at different rendering frame rates.
    internal sealed class LaevatainAccessorySpring
    {
        internal const float SmoothTimeSeconds=.15f,MaximumDegrees=5f;
        internal float Pitch,Yaw,Roll;
        float PitchVelocity,YawVelocity,RollVelocity;
        internal void Reset(){Pitch=Yaw=Roll=PitchVelocity=YawVelocity=RollVelocity=0f;}
        internal void Advance(float dt,bool locomotion,float angularX,float angularY,float angularZ,float forwardAcceleration,float lateralAcceleration)
        {
            if(!Finite(dt)||dt<=0f)return;
            float tx=0f,ty=0f,tz=0f;
            if(locomotion)
            {
                tx=-Clamp(angularX,180f)*.035f-Clamp(forwardAcceleration,12f)*.18f;
                ty=-Clamp(angularY,180f)*.025f;
                tz=-Clamp(angularZ,180f)*.035f+Clamp(lateralAcceleration,12f)*.18f;
                Limit(ref tx,ref ty,ref tz,MaximumDegrees);
            }
            Axis(ref Pitch,ref PitchVelocity,tx,dt);Axis(ref Yaw,ref YawVelocity,ty,dt);Axis(ref Roll,ref RollVelocity,tz,dt);
            float scale=Limit(ref Pitch,ref Yaw,ref Roll,MaximumDegrees);
            PitchVelocity*=scale;YawVelocity*=scale;RollVelocity*=scale;
        }
        static void Axis(ref float value,ref float velocity,float target,float dt)
        {
            double omega=2d/SmoothTimeSeconds,error=value-target,c=velocity+omega*error,decay=Math.Exp(-omega*dt);
            value=(float)(target+(error+c*dt)*decay);velocity=(float)((velocity-omega*c*dt)*decay);
            if(!Finite(value)||!Finite(velocity)){value=target;velocity=0f;}
        }
        static float Limit(ref float x,ref float y,ref float z,float maximum)
        {
            double length=Math.Sqrt((double)x*x+(double)y*y+(double)z*z);if(length<=maximum)return 1f;
            float scale=(float)(maximum/length);x*=scale;y*=scale;z*=scale;return scale;
        }
        static float Clamp(float value,float maximum)
        {return !Finite(value)?0f:Math.Max(-maximum,Math.Min(maximum,value));}
        internal static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
    }
}

