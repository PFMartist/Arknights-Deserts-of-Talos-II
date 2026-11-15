using System;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Core.Events;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Events;
using BBI.Game.Simulation;
using BBI.Unity.Game.FX;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Observe one private native healing operation. HP, victim filtering, pulse
    // cadence and cooldown remain entirely in native weapon/status processors.
    // This layer only cancels an invalid caster and owns bounded presentation.
    public static class ArdeliaHealingBridge
    {
        enum Kind { Start, End, WorldChanged }
        struct Observation {internal Kind Kind;internal Entity Caster;internal float X,Z;}
        sealed class Active
        {internal object Operation;internal Entity Passenger;internal CommanderID Owner;internal bool Embarked,Started;internal uint Began;}
        sealed class Presentation
        {internal UnitView View;internal Transform Root;internal Entity Owner;internal Vector3 Target;internal bool Pose,Ending,EffectFailed;internal float Age=100f;internal long Voice,Delivered;internal ArdeliaHealingEffect Effect;}
        const int MaximumCasters=128,MaximumPending=256;
        static readonly SortedDictionary<Entity,Active> ActiveCasts=new SortedDictionary<Entity,Active>(RhodesSimulationEvents.EntityComparer);
        static readonly Dictionary<Entity,Presentation> Views=new Dictionary<Entity,Presentation>();
        static readonly List<Entity> SimRemoved=new List<Entity>(),Removed=new List<Entity>();
        static readonly Queue<Observation> Pending=new Queue<Observation>();
        static readonly object Gate=new object();
        static readonly BBI.Core.Events.EventHandler<WeaponTriggerEvent> Handler=OnWeapon;
        static IEventReceiver Receiver;static object Simulation;static IEntitySystem System;static uint Frame;static int Reports;
        static void Push(Observation o){lock(Gate)if(Pending.Count<MaximumPending)Pending.Enqueue(o);}
        public static void Bind(UnitView view){BindType(view,HumanoidUnits.ArdeliaKey);}
        public static void BindCarrier(UnitView view){BindType(view,FactionUnits.TiltStrikeKey);}
        static void BindType(UnitView view,string key)
        {
            if(view==null||view.NonInterpolatedTickState==null||view.NonInterpolatedTickState.TypeID!=key)return;
            Presentation p;if(!Views.TryGetValue(view.EntityID,out p)){if(Views.Count>=MaximumCasters)return;p=new Presentation();Views.Add(view.EntityID,p);}p.View=view;p.Owner=view.EntityID;
            if(key==HumanoidUnits.ArdeliaKey){HumanoidBinding binding=view.GetComponent<HumanoidBinding>();p.Root=binding==null?null:binding.PresentationRoot;}
            else{FactionModelBinding binding=view.GetComponent<FactionModelBinding>();p.Root=binding==null?null:binding.GeometryRoot;}
        }
        public static void Unbind(Entity entity){Presentation p;if(Views.TryGetValue(entity,out p)){p.View=null;p.Root=null;p.Pose=false;if(p.Effect!=null)p.Effect.Hide();}}
        public static bool TryGetChannel(Entity entity,out Vector3 aim,out float age,out bool ending)
        {Presentation p;if(Views.TryGetValue(entity,out p)&&p.Pose){aim=p.Target;age=p.Age;ending=p.Ending;return true;}aim=Vector3.zero;age=0f;ending=false;return false;}
        public static bool TryTakeVoiceAcceptance(Entity entity,out long sequence)
        {Presentation p;if(Views.TryGetValue(entity,out p)&&p.Voice>p.Delivered){p.Delivered=p.Voice;sequence=p.Voice;return true;}sequence=0;return false;}
        static UnitState State(Entity entity)
        {object unit=System==null?null:System.GetComponent<object>(entity,NativeUnitBindings.Unit);return unit==null?null:NativeRead.Call(unit,"GetState") as UnitState;}
        static bool WorldReady(Entity entity,UnitState state)
        {return state!=null&&!state.UnitIsDead&&!state.IsDocked&&!state.IsSuspended&&System.HasComponent(entity,NativeUnitBindings.World);}
        static bool Valid(Entity entity,Active cast)
        {
            UnitState state=State(entity);if(!WorldReady(entity,state)||state.OwnerCommander!=cast.Owner)return false;
            if(!cast.Embarked)return state.TypeID==HumanoidUnits.ArdeliaKey;
            Entity passenger;string reason;
            return state.TypeID==FactionUnits.TiltStrikeKey&&RhodesOperatorTransport.TryReadyEmbarkedPassenger(System,entity,HumanoidUnits.ArdeliaKey,out passenger,out reason)&&passenger==cast.Passenger;
        }
        static void Cancel(Active cast)
        {
            object operation=cast.Operation;if(operation==null)return;
            NativeRead.Set(operation,"ForceFire",false);NativeRead.Set(operation,"FiringCriteriaMet",false);
            NativeRead.Set(operation,"RequestEnabled",false);NativeRead.Call(operation,"ClearCurrentTarget");
        }
        static void OnWeapon(WeaponTriggerEvent evt)
        {
            if(evt==null||System==null)return;
            UnitState state=State(evt.Entity);if(state==null)return;
            bool embarked=state.TypeID==FactionUnits.TiltStrikeKey&&RhodesEmbarkedOperatorSkills.IsProxyEvent(evt,2);
            if(!embarked&&(state.TypeID!=HumanoidUnits.ArdeliaKey||evt.WeaponBindingIndex!=ArdeliaHealingAssets.WeaponIndex))return;
            Active cast;
            if(evt.Trigger==WeaponFireTrigger.WindDownStart||evt.Trigger==WeaponFireTrigger.EnteringIdle)
            {if(ActiveCasts.TryGetValue(evt.Entity,out cast)){ActiveCasts.Remove(evt.Entity);if(cast.Started)Push(new Observation{Kind=Kind.End,Caster=evt.Entity});}return;}
            if(evt.Trigger!=WeaponFireTrigger.WindUpStart&&evt.Trigger!=WeaponFireTrigger.FirstRoundFired)return;
            object operation=NativeRead.Get(evt,"WeaponFireOp");
            if(!ActiveCasts.TryGetValue(evt.Entity,out cast))
            {
                if(ActiveCasts.Count>=MaximumCasters)return;
                Entity passenger=Entity.None;string reason;
                if(embarked&&!RhodesOperatorTransport.TryReadyEmbarkedPassenger(System,evt.Entity,HumanoidUnits.ArdeliaKey,out passenger,out reason))return;
                cast=new Active{Operation=operation,Passenger=passenger,Embarked=embarked,Owner=state.OwnerCommander,Began=Frame};ActiveCasts.Add(evt.Entity,cast);
            }
            if(!Valid(evt.Entity,cast)){Cancel(cast);return;}
            if(evt.Trigger!=WeaponFireTrigger.FirstRoundFired||cast.Started)return;
            cast.Started=true;cast.Began=Frame;
            BBI.Core.Utility.Vector2r center=state.StartOfFrameDynamicState.Position;
            Push(new Observation{Kind=Kind.Start,Caster=evt.Entity,X=Fixed64.UnsafeFloatValue(center.X),Z=Fixed64.UnsafeFloatValue(center.Y)});
            if(Reports++<12)Runtime.Log("ARDELIA_NATIVE_HEALING_ACCEPTED caster="+evt.Entity+"; embarked="+embarked+"; passenger="+cast.Passenger+"; radius=400; nativePulses=10; nativeHoT=40per1000ms; friendAndSelfMask=5; privateStatusReplace=True; presentationOnly=True; noHealthWrites=True; actualHealingAcceptancePending=True");
        }
        internal static void PrepareEvents(object sim)
        {
            if(!Runtime.RhodesCarrier||sim==null)return;
            if(Simulation!=null&&!object.ReferenceEquals(Simulation,sim))
            {if(Receiver!=null)Receiver.RemoveHandler<WeaponTriggerEvent>(Handler);Receiver=null;ActiveCasts.Clear();lock(Gate)Pending.Clear();Push(new Observation{Kind=Kind.WorldChanged});Frame=0;}
            Simulation=sim;System=(IEntitySystem)NativeRead.Get(sim,"EntitySystem");
            IEventReceiver receiver=(IEventReceiver)NativeRead.Get(sim,"SimEventSystem");
            if(object.ReferenceEquals(receiver,Receiver))return;
            if(Receiver!=null)Receiver.RemoveHandler<WeaponTriggerEvent>(Handler);Receiver=receiver;if(Receiver!=null)Receiver.AddHandler<WeaponTriggerEvent>(Handler);
        }
        public static void SimTick(object sim)
        {
            if(!Runtime.RhodesCarrier||sim==null)return;PrepareEvents(sim);
            Frame=Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim,"GlobalFrameCount"),"FrameNumber"));
            SimRemoved.Clear();foreach(KeyValuePair<Entity,Active> row in ActiveCasts)
            {
                // 14s is a defensive stale-observation ceiling, not a pulse
                // clock. Normal completion comes from native winddown/idle.
                // Pinned DoK sim step is125ms (8 ticks/s).
                if(Valid(row.Key,row.Value)&&Frame-row.Value.Began<112)continue;
                Cancel(row.Value);SimRemoved.Add(row.Key);if(row.Value.Started)Push(new Observation{Kind=Kind.End,Caster=row.Key});
            }
            foreach(Entity entity in SimRemoved)ActiveCasts.Remove(entity);SimRemoved.Clear();
        }
        public static void MainTick()
        {
            // Visibility can change while paused (sensors/icon-only/pooling).
            // Suppress existing renderers first; never create, advance clocks
            // or consume queued observations until presentation time resumes.
            foreach(Presentation current in Views.Values)if(current.Effect!=null&&!NativeVisible(current))current.Effect.Hide();
            float dt=Runtime.PresentationDeltaTime;if(dt<=0f)return;
            for(;;)
            {
                Observation o;lock(Gate){if(Pending.Count==0)break;o=Pending.Dequeue();}
                if(o.Kind==Kind.WorldChanged){foreach(Presentation old in Views.Values){old.Pose=false;if(old.Effect!=null)old.Effect.Hide();}continue;}
                Presentation p;if(!Views.TryGetValue(o.Caster,out p))continue;
                if(o.Kind==Kind.Start){p.Target=new Vector3(o.X,0f,o.Z);p.Pose=true;p.Ending=false;p.Age=0f;p.Voice++;}
                else if(p.Pose){p.Ending=true;p.Age=0f;}
            }
            Removed.Clear();foreach(KeyValuePair<Entity,Presentation> row in Views)
            {
                Presentation p=row.Value;p.Age+=dt;
                bool alive=p.View!=null&&p.View.NonInterpolatedTickState!=null&&!p.View.NonInterpolatedTickState.UnitIsDead&&!p.View.NonInterpolatedTickState.IsDocked;
                if(!alive)p.Pose=false;else if(p.Pose)
                {Vector3 point=p.View.transform.position;p.Target=new Vector3(point.x,0f,point.z);if(p.Ending&&p.Age>=1.983334f||!p.Ending&&p.Age>=14f)p.Pose=false;}
                bool visible=alive&&p.Pose&&NativeVisible(p);
                if(visible&&!p.EffectFailed&&p.Effect==null)
                {try{GameObject root=new GameObject("EF_Ardelia_SourceHealingField");root.hideFlags=HideFlags.DontSave;p.Effect=root.AddComponent<ArdeliaHealingEffect>();}catch(Exception error){p.EffectFailed=true;Runtime.Log("ARDELIA_HEALING_VFX_UNAVAILABLE "+error.Message+"; nativeHealingUnchanged=True");}}
                if(p.Effect!=null&&!p.EffectFailed)
                {try{p.Effect.Step(p.Target,p.Age,p.Ending,visible);}catch(Exception error){p.EffectFailed=true;p.Effect.Hide();Runtime.Log("ARDELIA_HEALING_VFX_DISABLED "+error.Message+"; nativeHealingUnchanged=True");}}
                if(p.View==null&&!p.Pose)Removed.Add(row.Key);
            }
            foreach(Entity entity in Removed){Presentation p=Views[entity];if(p.Effect!=null)UnityEngine.Object.Destroy(p.Effect.gameObject);Views.Remove(entity);}Removed.Clear();
        }
        static bool NativeVisible(Presentation p)
        {
            UnitView view=p.View;
            if(view==null||!p.Owner.Equals(view.EntityID)||!view.enabled||!view.gameObject.activeInHierarchy||view.SimStateInterpolator==null||
                p.Root==null||!p.Root.gameObject.activeInHierarchy||!p.Root.IsChildOf(view.transform)||view.ForceIconOnlyMode||Convert.ToInt32(view.Visibility)!=2||UserCamera.IsSensorsModeActive||
                view.NonInterpolatedTickState==null||view.NonInterpolatedTickState.UnitIsDead||view.NonInterpolatedTickState.IsDocked)return false;
            FXManager fx=FXManager.Manager;
            return fx!=null&&fx.enabled&&fx.gameObject.activeInHierarchy&&fx.FXMasterEnabled;
        }
        public static void Reset()
        {
            if(Receiver!=null)Receiver.RemoveHandler<WeaponTriggerEvent>(Handler);Receiver=null;Simulation=null;System=null;
            // Match teardown owns native weapons. Never write to old entity IDs.
            ActiveCasts.Clear();lock(Gate)Pending.Clear();foreach(Presentation p in Views.Values)if(p.Effect!=null)UnityEngine.Object.Destroy(p.Effect.gameObject);
            Views.Clear();Removed.Clear();SimRemoved.Clear();Frame=0;Reports=0;ArdeliaHealingEffect.ReleaseShared();
        }
    }
}
