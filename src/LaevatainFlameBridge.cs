using System;
using System.Collections.Generic;
using System.IO;
using BBI.Core.ComponentModel;
using BBI.Core.Events;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Events;
using BBI.Game.Simulation;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Native weapon/projectile observations only. This layer owns no damage,
    // cooldown, target acquisition or skill commands.
    public static class LaevatainFlameBridge
    {
        enum Kind { Windup,Fire,Cancelled,Detonated }
        struct Observation {internal Kind Kind;internal Entity Caster;internal float X,Z;internal bool Aim;}
        sealed class Presentation
        {internal UnitView View;internal Vector3 Target;internal float PoseAge=100f;internal bool Pose,WasVisible,Committed;internal LaevatainFlameEffect Effect;internal long VoiceSequence,DeliveredVoice;}
        static readonly object Gate=new object();
        static readonly Queue<Observation> Pending=new Queue<Observation>();
        static readonly Dictionary<Entity,Presentation> Views=new Dictionary<Entity,Presentation>();
        static readonly HashSet<Entity> Detonated=new HashSet<Entity>();
        static readonly List<Entity> Removed=new List<Entity>();
        static readonly BBI.Core.Events.EventHandler<WeaponTriggerEvent> Handler=OnWeapon;
        static IEventReceiver Receiver;
        static object Simulation;
        static uint Frame,WatchUntil;
        static int Reports;
        public static void Bind(UnitView view)
        {
            if(view==null||view.NonInterpolatedTickState==null||view.NonInterpolatedTickState.TypeID!=HumanoidUnits.LaevatainKey)return;
            Presentation p;if(!Views.TryGetValue(view.EntityID,out p)){p=new Presentation();Views.Add(view.EntityID,p);}
            p.View=view;p.WasVisible=Convert.ToInt32(view.Visibility)==2;
        }
        public static void Unbind(Entity entity){Presentation p;if(Views.TryGetValue(entity,out p))p.View=null;}
        public static void BindCarrier(UnitView view)
        {
            if(view==null||view.NonInterpolatedTickState==null||view.NonInterpolatedTickState.TypeID!=FactionUnits.TiltStrikeKey)return;
            Presentation p;if(!Views.TryGetValue(view.EntityID,out p)){p=new Presentation();Views.Add(view.EntityID,p);}
            p.View=view;p.WasVisible=Convert.ToInt32(view.Visibility)==2;
        }
        public static bool TryGetChannel(Entity entity,out Vector3 aim,out float age)
        {
            Presentation p;if(Views.TryGetValue(entity,out p)&&p.Pose&&p.PoseAge<5.75f){aim=p.Target;age=p.PoseAge;return true;}
            aim=Vector3.zero;age=0f;return false;
        }
        public static bool TryTakeVoiceAcceptance(Entity entity,out long sequence)
        {
            Presentation p;if(Views.TryGetValue(entity,out p)&&p.VoiceSequence>p.DeliveredVoice){p.DeliveredVoice=p.VoiceSequence;sequence=p.VoiceSequence;return true;}
            sequence=0;return false;
        }
        static void Push(Observation o){lock(Gate){if(Pending.Count<128)Pending.Enqueue(o);}}
        static void OnWeapon(WeaponTriggerEvent e)
        {
            if(e==null||Simulation==null)return;
            IEntitySystem system=(IEntitySystem)NativeRead.Get(Simulation,"EntitySystem");object unit=system.GetComponent<object>(e.Entity,2);
            UnitState state=unit==null?null:(UnitState)NativeRead.Call(unit,"GetState");
            if(state==null||state.UnitIsDead)return;
            bool ground=state.TypeID==HumanoidUnits.LaevatainKey&&e.WeaponBindingIndex==LaevatainFlameAssets.WeaponIndex;
            bool embarked=state.TypeID==FactionUnits.TiltStrikeKey&&RhodesEmbarkedOperatorSkills.IsProxyEvent(e,0);
            if(!ground&&!embarked)return;
            Observation o=new Observation{Caster=e.Entity};
            if(e.Trigger==WeaponFireTrigger.WindUpStart)o.Kind=Kind.Windup;
            else if(e.Trigger==WeaponFireTrigger.FirstRoundFired){o.Kind=Kind.Fire;WatchUntil=Frame+24;}
            else if(e.Trigger==WeaponFireTrigger.EnteringIdle||e.Trigger==WeaponFireTrigger.WindDownStart)o.Kind=Kind.Cancelled;
            else return;
            object operation;if(NativeRead.TryGet(e,"WeaponFireOp",out operation)&&operation!=null)
            {Vector2r target=(Vector2r)NativeRead.Get(operation,"AimTurretAtPosition");o.X=Fixed64.UnsafeFloatValue(target.X);o.Z=Fixed64.UnsafeFloatValue(target.Y);o.Aim=true;}
            Push(o);
        }
        internal static void PrepareEvents(object sim)
        {
            if(!Runtime.RhodesCarrier||sim==null)return;
            if(Simulation!=null&&!object.ReferenceEquals(Simulation,sim))
            {Detonated.Clear();lock(Gate)Pending.Clear();Frame=WatchUntil=0;}
            Simulation=sim;
            IEventReceiver current=(IEventReceiver)NativeRead.Get(sim,"SimEventSystem");
            if(!object.ReferenceEquals(current,Receiver))
            {if(Receiver!=null)Receiver.RemoveHandler<WeaponTriggerEvent>(Handler);Receiver=current;if(Receiver!=null)Receiver.AddHandler<WeaponTriggerEvent>(Handler);}
        }
        public static void SimTick(object sim)
        {
            if(!Runtime.RhodesCarrier||sim==null)return;
            PrepareEvents(sim);
            Frame=Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim,"GlobalFrameCount"),"FrameNumber"));
            if(Frame>WatchUntil){Detonated.Clear();return;}
            IEntitySystem system=(IEntitySystem)NativeRead.Get(sim,"EntitySystem");
            foreach(Entity entity in system.Query().Has(27))
            {
                object projectile=system.GetComponent<object>(entity,27);
                if(projectile==null||(string)NativeRead.Get(projectile,"ProjectileTypeName")!=LaevatainFlameAssets.ProjectileKey||!(bool)NativeRead.Get(projectile,"Detonated")||!Detonated.Add(entity))continue;
                Entity caster=(Entity)NativeRead.Get(NativeRead.Get(projectile,"ShotSender"),"WeaponOwner");
                Vector2r target=(Vector2r)NativeRead.Get(projectile,"OriginalAimAtPosition");
                Push(new Observation{Kind=Kind.Detonated,Caster=caster,X=Fixed64.UnsafeFloatValue(target.X),Z=Fixed64.UnsafeFloatValue(target.Y),Aim=true});
                if(Reports++<16)Runtime.Log("LAEVATAIN_NATIVE_FLAME_DETONATED caster="+caster+"; radius="+LaevatainFlameAssets.RadiusMeters+"; configuredCenterDamage="+LaevatainFlameAssets.CenterDamage+"; nativeAOEAlreadyApplied=True; presentationAddsNoDamage=True");
            }
        }
        public static void MainTick()
        {
            float dt=Runtime.PresentationDeltaTime;if(dt<=0f)return;
            for(;;)
            {
                Observation o;lock(Gate){if(Pending.Count==0)break;o=Pending.Dequeue();}
                Presentation p;if(!Views.TryGetValue(o.Caster,out p))continue;
                if(o.Aim)p.Target=new Vector3(o.X,0f,o.Z);
                if(o.Kind==Kind.Windup)
                {p.Pose=true;p.Committed=false;p.PoseAge=0f;p.VoiceSequence++;if(p.View!=null)p.WasVisible=Convert.ToInt32(p.View.Visibility)==2;}
                else if(o.Kind==Kind.Fire)p.Committed=true;
                else if(o.Kind==Kind.Cancelled&&!p.Committed)p.Pose=false;
                else if(o.Kind==Kind.Detonated&&p.WasVisible)
                {
                    try
                    {
                        if(p.Effect==null){GameObject host=new GameObject("EF_Laevatain_ChainFlame");host.hideFlags=HideFlags.DontSave;p.Effect=host.AddComponent<LaevatainFlameEffect>();}
                        p.Effect.Emit(p.Target,LaevatainFlameAssets.RadiusMeters);
                    }
                    catch(Exception error){Runtime.Log("LAEVATAIN_FLAME_VISUAL_UNAVAILABLE "+error.Message+"; nativeDamageUnchanged=True");}
                }
            }
            Removed.Clear();
            foreach(KeyValuePair<Entity,Presentation> row in Views)
            {
                Presentation p=row.Value;p.PoseAge+=dt;if(p.PoseAge>=5.75f)p.Pose=false;
                if(p.View!=null&&p.View.NonInterpolatedTickState!=null&&p.View.NonInterpolatedTickState.UnitIsDead)p.Pose=false;
                if(p.Effect!=null)p.Effect.Step(dt);
                if(p.View==null&&!p.Pose&&(p.Effect==null||!p.Effect.Active))Removed.Add(row.Key);
            }
            foreach(Entity entity in Removed){Presentation p=Views[entity];if(p.Effect!=null)UnityEngine.Object.Destroy(p.Effect.gameObject);Views.Remove(entity);}
        }
        public static void Reset()
        {
            if(Receiver!=null)Receiver.RemoveHandler<WeaponTriggerEvent>(Handler);Receiver=null;Simulation=null;
            foreach(Presentation p in Views.Values)if(p.Effect!=null)UnityEngine.Object.Destroy(p.Effect.gameObject);
            Views.Clear();Removed.Clear();Detonated.Clear();lock(Gate)Pending.Clear();Frame=WatchUntil=0;Reports=0;LaevatainFlameEffect.ReleaseShared();
        }
    }

    // Source-derived Unity5 presentation: fixed batched pool, original combo
    // masks/UV sheets/colour gradients, native additive and alpha shaders.
    // Modern distortion, VAT and full source VFXgraph are not recreated.
    public sealed class LaevatainFlameEffect:MonoBehaviour
    {
        sealed class Batch
        {
            internal Mesh Mesh;internal MeshRenderer Renderer;internal Vector3[] Positions;internal Vector2[] UV;internal Color[] Colors;internal int Count;
        }
        static Texture2D Atlas;
        static Material AdditiveMaterial,SmokeMaterial;
        readonly LaevatainFlameVisualProfile.Sprite[] Sprites=new LaevatainFlameVisualProfile.Sprite[LaevatainFlameVisualProfile.Capacity];
        Batch Fire,Smoke;
        Camera Camera;
        UserCamera UserCameraRef;
        Transform CameraTransform;
        float Age=100f,Radius,NextSample;
        public bool Active {get{return Age<LaevatainFlameVisualProfile.Duration;}}
        static Shader FindShader(string first,string second)
        {Shader shader=Shader.Find(first);if(shader==null||!shader.isSupported)shader=Shader.Find(second);if(shader==null||!shader.isSupported)throw new InvalidDataException("Native particle shader unavailable");return shader;}
        static void PrepareShared()
        {
            if(Atlas!=null&&AdditiveMaterial!=null&&SmokeMaterial!=null)return;
            string path=Path.Combine(HumanoidModel.AssetDirectory,"humanoid_trial/laevatain/chain_vfx/combo_hit_atlas.png");
            FileInfo info=new FileInfo(path);if(!info.Exists||info.Length<=0||info.Length>4*1024*1024)throw new InvalidDataException("Source combo atlas absent or exceeds budget");
            try
            {
                Atlas=new Texture2D(2,2,TextureFormat.RGBA32,true);Atlas.hideFlags=HideFlags.HideAndDontSave;
                if(!Atlas.LoadImage(File.ReadAllBytes(path))||Atlas.width!=1024||Atlas.height!=1024)throw new InvalidDataException("Source combo atlas must be1024 square");
                Atlas.name="EF_Laevatain_ComboHit_SourceAtlas";Atlas.wrapMode=TextureWrapMode.Clamp;Atlas.filterMode=FilterMode.Trilinear;
                AdditiveMaterial=new Material(FindShader("Particles/Additive","Legacy Shaders/Particles/Additive"));AdditiveMaterial.name="EF_Laevatain_ComboHit_Additive";AdditiveMaterial.hideFlags=HideFlags.HideAndDontSave;AdditiveMaterial.mainTexture=Atlas;float tint=.5f*LaevatainFlameVisualProfile.FireRadiance;AdditiveMaterial.SetColor("_TintColor",new Color(tint,tint,tint,.5f));
                SmokeMaterial=new Material(FindShader("Particles/Alpha Blended","Legacy Shaders/Particles/Alpha Blended"));SmokeMaterial.name="EF_Laevatain_ComboHit_Smoke";SmokeMaterial.hideFlags=HideFlags.HideAndDontSave;SmokeMaterial.mainTexture=Atlas;SmokeMaterial.SetColor("_TintColor",new Color(.5f,.5f,.5f,.5f));
            }
            catch{ReleaseShared();throw;}
        }
        Batch CreateBatch(string name,Material material,int count)
        {
            Batch batch=new Batch();batch.Positions=new Vector3[count*4];batch.UV=new Vector2[count*4];batch.Colors=new Color[count*4];
            int[] indices=new int[count*6];for(int i=0;i<count;i++){int v=i*4,t=i*6;indices[t]=v;indices[t+1]=v+2;indices[t+2]=v+1;indices[t+3]=v+1;indices[t+4]=v+2;indices[t+5]=v+3;}
            batch.Mesh=new Mesh();batch.Mesh.name=name;batch.Mesh.hideFlags=HideFlags.HideAndDontSave;batch.Mesh.MarkDynamic();batch.Mesh.vertices=batch.Positions;batch.Mesh.uv=batch.UV;batch.Mesh.colors=batch.Colors;batch.Mesh.triangles=indices;
            GameObject node=new GameObject(name);node.hideFlags=HideFlags.HideAndDontSave;node.transform.parent=transform;node.transform.localPosition=Vector3.zero;node.transform.localRotation=Quaternion.identity;node.transform.localScale=Vector3.one;
            node.AddComponent<MeshFilter>().sharedMesh=batch.Mesh;batch.Renderer=node.AddComponent<MeshRenderer>();batch.Renderer.sharedMaterial=material;batch.Renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;batch.Renderer.receiveShadows=false;batch.Renderer.enabled=false;return batch;
        }
        public void Emit(Vector3 target,float radius)
        {
            if(float.IsNaN(radius)||float.IsInfinity(radius)||radius<=0f||radius>200f)throw new ArgumentOutOfRangeException("radius");
            PrepareShared();Radius=radius;Age=0f;NextSample=0f;RaycastHit hit;
            if(Physics.Raycast(new Vector3(target.x,10000f,target.z),Vector3.down,out hit,20000f,1<<8))target.y=hit.point.y+.14f;
            transform.position=target;transform.rotation=Quaternion.identity;
            UserCameraRef=SmallUnitCamera.ActiveGameplayCamera;
            Camera=UserCameraRef==null?UnityEngine.Camera.main:UserCameraRef.ActiveCamera;CameraTransform=Camera==null?null:Camera.transform;
            if(Fire==null)Fire=CreateBatch("EF_Laevatain_SourceCombo_FireBatch",AdditiveMaterial,80);
            if(Smoke==null)Smoke=CreateBatch("EF_Laevatain_SourceCombo_SmokeBatch",SmokeMaterial,16);
            Bounds bounds=new Bounds(new Vector3(0,Radius*.6f,0),new Vector3(Radius*2.8f,Radius*2.2f,Radius*2.8f));Fire.Mesh.bounds=bounds;Smoke.Mesh.bounds=bounds;
            Render();
        }
        public void Step(float dt)
        {if(!Active||dt<=0f)return;Age+=dt;Render();}
        void Render()
        {
            if(UserCameraRef!=null&&UserCameraRef.ActiveCamera!=null)Camera=UserCameraRef.ActiveCamera;
            CameraTransform=Camera==null?null:Camera.transform;
            float distance=CameraTransform==null?0f:(CameraTransform.position-transform.position).sqrMagnitude;
            bool visible=LaevatainFlameVisualProfile.ShouldRender(Age,UserCamera.IsSensorsModeActive);
            Fire.Renderer.enabled=visible;Smoke.Renderer.enabled=visible;if(!visible||Age<NextSample)return;
            bool reduced=distance>600f*600f;NextSample=Age+(reduced?1f/15f:1f/30f);
            int count=LaevatainFlameVisualProfile.Evaluate(Age,Radius,reduced,Sprites);Fire.Count=0;Smoke.Count=0;
            Vector3 cameraRight=CameraTransform==null?Vector3.right:CameraTransform.right,cameraUp=CameraTransform==null?Vector3.up:CameraTransform.up;
            for(int i=0;i<count;i++)
            {
                LaevatainFlameVisualProfile.Sprite s=Sprites[i];Batch batch=s.Smoke?Smoke:Fire;if(batch.Count>=batch.Positions.Length/4)continue;int at=batch.Count++*4;
                Vector3 center=new Vector3(s.X,s.Y,s.Z),right,up;
                float sine=Mathf.Sin(s.Angle),cosine=Mathf.Cos(s.Angle);
                if(s.Mode==1){right=new Vector3(cosine,0,-sine);up=new Vector3(sine,0,cosine);}
                else if(s.Mode==0){right=new Vector3(cosine,0,-sine);up=Vector3.up;center+=up*(s.Height*.5f);}
                else{right=cameraRight;up=cameraUp;}
                Vector3 rw=right*(s.Width*.5f),uh=up*(s.Height*.5f);
                batch.Positions[at]=center-rw-uh;batch.Positions[at+1]=center+rw-uh;batch.Positions[at+2]=center-rw+uh;batch.Positions[at+3]=center+rw+uh;
                if(s.Mode==3){batch.Positions[at+2]=center+uh;batch.Positions[at+3]=batch.Positions[at+2];}
                float u0=(s.Frame*128f+3.5f)/1024f,u1=((s.Frame+1)*128f-3.5f)/1024f,v0=1f-((s.Role+1)*128f-3.5f)/1024f,v1=1f-(s.Role*128f+3.5f)/1024f;
                batch.UV[at]=new Vector2(u0,v0);batch.UV[at+1]=new Vector2(u1,v0);batch.UV[at+2]=new Vector2(u0,v1);batch.UV[at+3]=new Vector2(u1,v1);
                Color color=new Color(s.R,s.G,s.B,s.A);for(int j=0;j<4;j++)batch.Colors[at+j]=color;
            }
            Upload(Fire);Upload(Smoke);
        }
        static void Upload(Batch batch)
        {
            for(int i=batch.Count*4;i<batch.Positions.Length;i++){batch.Positions[i]=Vector3.zero;batch.Colors[i]=Color.clear;}
            batch.Mesh.vertices=batch.Positions;batch.Mesh.uv=batch.UV;batch.Mesh.colors=batch.Colors;batch.Renderer.enabled=batch.Count>0;
        }
        void OnDestroy(){if(Fire!=null&&Fire.Mesh!=null)UnityEngine.Object.Destroy(Fire.Mesh);if(Smoke!=null&&Smoke.Mesh!=null)UnityEngine.Object.Destroy(Smoke.Mesh);}
        public static void ReleaseShared()
        {if(AdditiveMaterial!=null)UnityEngine.Object.Destroy(AdditiveMaterial);if(SmokeMaterial!=null)UnityEngine.Object.Destroy(SmokeMaterial);if(Atlas!=null)UnityEngine.Object.Destroy(Atlas);AdditiveMaterial=null;SmokeMaterial=null;Atlas=null;}
    }
}
