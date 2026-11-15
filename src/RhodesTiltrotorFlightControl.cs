using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BBI.Core.ComponentModel;
using BBI.Core.Events;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Events;
using BBI.Game.Simulation;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Native commands and Hover dynamics own all planar movement. The private
    // sortie controller opts out of fixed-wing autopilot while in manual mode;
    // native sortie metadata, hangar, ammunition and docking remain intact.
    public static class RhodesTiltrotorFlightControl
    {
        public const string LandingAbilityKey="Ability_EF_U08_ToggleLanding";
        enum RequestKind { StopObserved,MoveObserved,AttackObserved,AttackMoveObserved,ReturnObserved,LaunchReady }
        struct Request {internal Entity Entity,Target;internal RequestKind Kind;internal Vector2r Point;internal CommanderID Sender;}
        struct Intent {internal bool Hold,Land,FieldPhase,WaitingForStop,RecoveredUnknownIntent;internal float Altitude,TargetAltitude;internal int PhaseEpoch;internal RhodesTiltrotorOrder Order;}
        sealed class VisualSample {internal bool HasValue,Visible,Guided,Grounded;internal float BottomAGL,ViewAGL;}
        sealed class SimEntry
        {
            internal RhodesTiltrotorOrder Order=RhodesTiltrotorOrder.Hold;
            internal CommanderID Owner;
            internal bool RecoveredUnknownIntent;
            internal Entity UnitEntity=Entity.None,OrderedTarget=Entity.None,Carrier=Entity.None;
            internal Vector2r Anchor,PickupDestination;
            internal bool Landing,HaveAltitudePhase,WaitingForStop;
            internal float StartAltitude=RhodesTiltrotorOrderPolicy.CruiseAltitudeMetres;
            internal float TargetAltitude=RhodesTiltrotorOrderPolicy.CruiseAltitudeMetres;
            internal int AltitudeStartTick,ManualOrderRevision;
            internal Entity PickupPassenger=Entity.None;
        }
        const int MaximumUnits=64,MaximumPending=128;
        static object SessionSim;
        static bool RecoveryPending;
        static readonly object Gate=new object();
        static readonly Queue<Request> Pending=new Queue<Request>();
        static readonly List<Request> Scratch=new List<Request>(MaximumPending);
        static readonly List<Entity> Removal=new List<Entity>(MaximumUnits);
        static readonly HashSet<Entity> Bound=new HashSet<Entity>(),Managed=new HashSet<Entity>();
        static readonly Dictionary<Entity,Intent> Intents=new Dictionary<Entity,Intent>();
        static readonly Dictionary<Entity,VisualSample> Visuals=new Dictionary<Entity,VisualSample>();
        static readonly SortedDictionary<Entity,SimEntry> Entries=new SortedDictionary<Entity,SimEntry>(RhodesSimulationEvents.EntityComparer);
        static readonly List<Entity> RecoveryEntities=new List<Entity>(MaximumUnits);
        static readonly Dictionary<Entity,RhodesTiltrotorAltitudeDriver> Drivers=new Dictionary<Entity,RhodesTiltrotorAltitudeDriver>();
        static readonly BBI.Core.Events.EventHandler<AbilityDeploymentEvent> AbilityHandler=OnAbility;
        static readonly BBI.Core.Events.EventHandler<DockingUnitOperationEvent> DockingHandler=OnDocking;
        static readonly MethodInfo Clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        static IEventReceiver Receiver;
        static IEntitySystem ActiveSystem;
        static string StopKey,MoveKey;
        static AbilityTargetingType StopTargeting;
        static MethodInfo WeaponListGetter;
        static Type TargetProcessor;
        static int Reports,CurrentTick;
        internal static object Copy(object value){return value==null?null:Clone.Invoke(value,null);}
        internal static void ConfigureStopKey(UnitAssetContainer unit)
        {
            if(unit==null||unit.name!=FactionUnits.TiltStrikeKey)throw new InvalidOperationException("Expected private U08 commands");
            StopKey=MoveKey=null;bool landing=false;
            foreach(object asset in (Array)NativeRead.Get(unit,"m_Abilities"))
            {
                AbilityAttributes ability=asset==null?null:NativeRead.Get(asset,"m_Attributes") as AbilityAttributes;if(ability==null)continue;
                // Other private Stop-class commands (landing/cargo) are valid,
                // but must never become the internal movement Stop command.
                if(ability.Name=="Ability_Common_StopAbility"&&ability.AbilityType==AbilityClass.Stop){StopKey=ability.Name;StopTargeting=ability.TargetingType;}
                if(ability.Name=="Ability_Common_MoveAbility")MoveKey=ability.Name;
                if(ability.Name==LandingAbilityKey)landing=ability.AbilityType==AbilityClass.Stop;
            }
            if(string.IsNullOrEmpty(StopKey)||string.IsNullOrEmpty(MoveKey)||landing)throw new InvalidOperationException("U08 requires native Move/Stop and no manual ground-landing button");
        }
        public static void Bind(UnitView view)
        {
            if(!Runtime.RhodesCarrier||view==null||view.NonInterpolatedTickState==null||view.NonInterpolatedTickState.TypeID!=FactionUnits.TiltStrikeKey||view.EntityID.Equals(Entity.None))return;
            lock(Gate){if(!Bound.Contains(view.EntityID)&&Bound.Count>=MaximumUnits)return;Bound.Add(view.EntityID);}
            RhodesTiltrotorAltitudeDriver driver=view.GetComponent<RhodesTiltrotorAltitudeDriver>();
            if(driver==null)driver=view.gameObject.AddComponent<RhodesTiltrotorAltitudeDriver>();driver.Initialize(view);driver.enabled=true;Drivers[view.EntityID]=driver;
            LaevatainFlameBridge.BindCarrier(view);GilbertaGravityBridge.BindCarrier(view);ArdeliaHealingBridge.BindCarrier(view);
        }
        public static void Unbind(Entity entity)
        {
            if(entity.Equals(Entity.None))return;
            // Visibility/pooling is not simulation death: keep manual intent
            // when this view culls so offscreen aircraft cannot auto-return.
            lock(Gate)Bound.Remove(entity);Drivers.Remove(entity);
            LaevatainFlameBridge.Unbind(entity);GilbertaGravityBridge.Unbind(entity);ArdeliaHealingBridge.Unbind(entity);
        }
        internal static bool WantsLanding(Entity entity){lock(Gate){Intent value;return Intents.TryGetValue(entity,out value)&&value.Hold&&value.Land;}}
        public static bool IsLandingOrHovering(Entity entity,out bool landing)
        {lock(Gate){Intent value;bool hold=Intents.TryGetValue(entity,out value)&&value.Hold;landing=hold&&value.Land;return hold;}}
        internal static bool TryFieldAltitude(Entity entity,out float altitude)
        {lock(Gate){Intent value;bool ready=Intents.TryGetValue(entity,out value)&&value.FieldPhase;altitude=value.Altitude;return ready;}}
        public static bool TryGetFlightStatus(Entity entity,out RhodesTiltrotorFlightStatus status)
        {
            lock(Gate)
            {
                Intent intent;VisualSample visual;bool have=Intents.TryGetValue(entity,out intent);Visuals.TryGetValue(entity,out visual);
                bool shown=visual!=null&&visual.Visible,grounded=shown&&visual.Grounded;
                string label=visual!=null&&visual.Guided?"舰上起降":!have?"待命":intent.RecoveredUnknownIntent?"恢复待命":intent.Order==RhodesTiltrotorOrder.Return?"返航中":
                    intent.Land?(intent.WaitingForStop?"减速降落":grounded?"已落地":intent.Altitude<=1f?"等待接地确认":"降落中"):
                    intent.FieldPhase&&intent.Altitude<RhodesTiltrotorOrderPolicy.CruiseAltitudeMetres-.5f?"升空中":
                    intent.Order==RhodesTiltrotorOrder.Move?"飞行中":intent.Order==RhodesTiltrotorOrder.AttackTarget||intent.Order==RhodesTiltrotorOrder.AttackMove?"执行攻击":"悬停待命";
                status=new RhodesTiltrotorFlightStatus(label,have?intent.Altitude:RhodesTiltrotorOrderPolicy.CruiseAltitudeMetres,
                    visual!=null?visual.BottomAGL:float.NaN,visual!=null?visual.ViewAGL:float.NaN,shown,grounded,have&&intent.Land);
                return have||visual!=null;
            }
        }
        public static string GetFlightPhase(Entity entity)
        {RhodesTiltrotorFlightStatus status;return TryGetFlightStatus(entity,out status)?status.PhaseLabel:"待命";}
        internal static void ReadVisualIntent(Entity entity,out bool field,out bool landing,out float altitude,out float targetAltitude,out int epoch)
        {lock(Gate){Intent intent;bool found=Intents.TryGetValue(entity,out intent);field=found&&intent.FieldPhase;landing=found&&intent.Land;altitude=intent.Altitude;targetAltitude=intent.TargetAltitude;epoch=intent.PhaseEpoch;}}
        internal static void VisualMeasurement(Entity entity,bool visible,bool guided,float geometryBottomAGL,float viewAGL,bool grounded)
        {
            lock(Gate)
            {
                VisualSample sample;if(!Visuals.TryGetValue(entity,out sample)){if(Visuals.Count>=MaximumUnits)return;sample=new VisualSample();Visuals.Add(entity,sample);}
                sample.Visible=visible;sample.Guided=guided;sample.Grounded=visible&&grounded;
                if(visible){sample.HasValue=true;sample.BottomAGL=geometryBottomAGL;sample.ViewAGL=viewAGL;}
            }
        }
        public static void ApplyGeometryAltitude(UnitView view,FactionModelBinding binding,bool visible)
        {
            RhodesTiltrotorAltitudeDriver driver;
            if(view!=null&&binding!=null&&binding.Air&&Drivers.TryGetValue(view.EntityID,out driver)&&driver!=null)driver.ApplyGeometry(binding,visible);
        }
        // Retain the prior ABI as an empty method. There is no field landing
        // hotkey or global F7 listener; mother-ship deck recovery stays native.
        public static void MainTick(){}
        static void Enqueue(Request request,bool unconditional)
        {lock(Gate){if(Pending.Count<MaximumPending)Pending.Enqueue(request);}}
        static void OnAbility(AbilityDeploymentEvent evt)
        {
            if(evt==null||!RhodesResearch.IsDeclaredOwner(evt.CommandSender)||evt.ActionResult!=AbilityDeploymentActionResult.DeploymentSucceeded||evt.SourceEntities==null||string.IsNullOrEmpty(evt.AbilityType))return;
            if(evt.AbilityType==RhodesCarrierUnits.AircraftRecallKey||evt.AbilityType==LandingAbilityKey)return;
            object sim=NativeRead.Get(typeof(Sim),"Instance");if(sim==null)return;
            NativeUnitBindings.CommandRole role=NativeUnitBindings.ResolveRole(sim,evt.AbilityType,evt.TargetEntities!=null&&evt.TargetEntities.Length>0);
            RequestKind kind;
            if(role==NativeUnitBindings.CommandRole.Stop)kind=RequestKind.StopObserved;
            else if(role==NativeUnitBindings.CommandRole.Move)kind=RequestKind.MoveObserved;
            else if(role==NativeUnitBindings.CommandRole.AttackTarget)kind=RequestKind.AttackObserved;
            else if(role==NativeUnitBindings.CommandRole.AttackMove)kind=RequestKind.AttackMoveObserved;
            else if(role==NativeUnitBindings.CommandRole.Return)kind=RequestKind.ReturnObserved;
            else return;
            // Cargo buttons use a Stop-class donor but are not flight Stop.
            if(RhodesOperatorTransport.IsAbility(evt.AbilityType))return;
            IEntitySystem system=(IEntitySystem)NativeRead.Get(sim,"EntitySystem");
            Entity target=evt.TargetEntities!=null&&evt.TargetEntities.Length>0?evt.TargetEntities[0]:Entity.None;
            foreach(Entity entity in evt.SourceEntities)
            {
                object unit;UnitState state;if(!Alive(system,entity,out unit,out state)||state.OwnerCommander!=evt.CommandSender)continue;
                Enqueue(new Request{Entity=entity,Kind=kind,Point=evt.TargetPosition,Target=target,Sender=evt.CommandSender},false);
            }
        }
        static void OnDocking(DockingUnitOperationEvent evt)
        {
            if(evt==null||(int)evt.Action!=1||evt.BoneName!=RhodesTiltrotorProfile.LaunchBone)return;
            Enqueue(new Request{Entity=evt.DockingEntity,Kind=RequestKind.LaunchReady,Sender=CommanderID.None},true);
        }
        static object Component(IEntitySystem system,Entity entity,int id){return system.GetComponent<object>(entity,id);}
        static bool Alive(IEntitySystem system,Entity entity,out object unit,out UnitState state)
        {
            unit=Component(system,entity,NativeUnitBindings.Unit);state=unit==null?null:(UnitState)NativeRead.Call(unit,"GetState");
            return state!=null&&!state.UnitIsDead&&state.TypeID==FactionUnits.TiltStrikeKey&&
                RhodesResearch.IsDeclaredOwner(state.OwnerCommander)&&!Convert.ToBoolean(NativeRead.Get(unit,"PresentationOnly"))&&Component(system,entity,NativeUnitBindings.World)!=null;
        }
        static void Publish(Entity entity,SimEntry entry)
        {lock(Gate)Intents[entity]=new Intent{Hold=entry.Order==RhodesTiltrotorOrder.Hold,Land=entry.Landing,FieldPhase=entry.HaveAltitudePhase,Altitude=Altitude(entry),TargetAltitude=entry.TargetAltitude,WaitingForStop=entry.WaitingForStop,PhaseEpoch=entry.AltitudeStartTick,Order=entry.Order,RecoveredUnknownIntent=entry.RecoveredUnknownIntent};}
        static float Altitude(SimEntry entry)
        {return !entry.HaveAltitudePhase||entry.WaitingForStop?entry.StartAltitude:RhodesTiltrotorOrderPolicy.Altitude(entry.StartAltitude,entry.TargetAltitude,CurrentTick-entry.AltitudeStartTick);}
        static void SetLanding(SimEntry entry,bool landing,bool nativeLaunch=false)
        {
            landing=false; // Field ground landing was removed; deck recovery stays native.
            float target=entry.Order==RhodesTiltrotorOrder.Return?RhodesTiltrotorProfile.RecoveryFlightAltitude:RhodesTiltrotorOrderPolicy.CruiseAltitudeMetres;
            if(entry.HaveAltitudePhase&&entry.Landing==landing&&entry.TargetAltitude==target&&!nativeLaunch)return;
            float start=Altitude(entry);
            if(!entry.HaveAltitudePhase||nativeLaunch)
            {lock(Gate){VisualSample sample;if(Visuals.TryGetValue(entry.UnitEntity,out sample)&&sample.HasValue)start=Math.Max(RhodesTiltrotorOrderPolicy.GroundClearanceMetres,sample.BottomAGL);}}
            entry.StartAltitude=start;entry.TargetAltitude=target;entry.AltitudeStartTick=CurrentTick;
            entry.HaveAltitudePhase=true;entry.WaitingForStop=landing;entry.Landing=landing;
        }
        static bool RunCommand(object sim,Entity entity,string key,AbilityTargetingType targeting,Vector2r point)
        {
            Type factory=typeof(Sim).Assembly.GetType("BBI.Game.Commands.SimCommandFactory",true);
            CommanderID owner=NativeUnitBindings.OwnerOf(entity);if(owner==CommanderID.None)return false;
            object command=NativeRead.Call(factory,"CreateUseAbilityCommand",owner,key,AbilityAction.Activate,new Entity[]{entity},new Entity[0],point,targeting,SelectedUnitManager.AddMode.NewSet);
            return (bool)NativeRead.Call(command,"RunCommand",sim,NativeRead.Get(sim,"InteractionProvider"),NativeRead.Get(sim,"GlobalFrameCount"));
        }
        static bool RunStop(object sim,Entity entity){return RunCommand(sim,entity,StopKey,StopTargeting,new Vector2r(Fixed64.Zero,Fixed64.Zero));}
        static object CurrentGoal(IEntitySystem system,Entity entity)
        {object goals=Component(system,entity,NativeUnitBindings.Goals);return goals==null?null:NativeRead.Get(goals,"CurrentGoal");}
        static void EnsureWeaponAccess()
        {
            if(WeaponListGetter!=null)return;Assembly native=typeof(Sim).Assembly;
            Type extensions=native.GetType("BBI.Game.Simulation.EntityListComponentExtensions",true),weapon=native.GetType("BBI.Game.Simulation.WeaponFireOperation",true);
            foreach(MethodInfo method in extensions.GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic))
                if(method.Name=="GetListComponent"&&method.IsGenericMethodDefinition&&method.GetParameters().Length==2){WeaponListGetter=method.MakeGenericMethod(new Type[]{weapon});break;}
            if(WeaponListGetter==null)throw new MissingMethodException("U08 native weapon accessor absent");
            TargetProcessor=native.GetType("BBI.Game.Simulation.TargetAcquisitionProcessor",true);
        }
        static void ApplyWeaponIntent(Entity entity,SimEntry entry)
        {
            EnsureWeaponAccess();IList weapons=WeaponListGetter.Invoke(null,new object[]{entity,NativeUnitBindings.WeaponOperations}) as IList;
            if(weapons!=null)foreach(object weapon in weapons)
            {
                WeaponBinding binding=NativeRead.Get(weapon,"WeaponBinding") as WeaponBinding;
                bool manualSkill=binding!=null&&RhodesEmbarkedOperatorSkills.IsProxyWeaponID(binding.WeaponID);
                NativeRead.Set(weapon,"ForceAutoTargetAcquisiton",!manualSkill&&entry.Order==RhodesTiltrotorOrder.AttackMove);
            }
            if(entry.Order!=RhodesTiltrotorOrder.AttackMove)NativeRead.Call(TargetProcessor,"RemoveAllCPUTargets",entity);
            if(entry.Order!=RhodesTiltrotorOrder.AttackMove&&entry.Order!=RhodesTiltrotorOrder.AttackTarget)
            {
                object healing=entry.Order==RhodesTiltrotorOrder.Return?null:RhodesEmbarkedOperatorSkills.MovingHealingOperation(entity);
                ClearMovementWeaponTargets(weapons,healing);
            }
        }
        internal static void ClearMovementWeaponTargets(IList weapons,object healing)
        {
            if(weapons==null)return;
            foreach(object weapon in weapons)
            {
                if(healing!=null&&object.ReferenceEquals(weapon,healing))continue;
                NativeRead.Call(weapon,"ClearCurrentTargetAndStopTargetingForATick");
            }
        }
        static void NonReturning(IEntitySystem system,Entity entity)
        {
            object sortie=Component(system,entity,NativeUnitBindings.AirSortie);if(sortie==null)return;
            NativeRead.Set(sortie,"HasBeenAskedToDock",false);NativeRead.Set(sortie,"AttackOpportunityTargets",false);NativeRead.Set(sortie,"TargetSearchRadius",Fixed64.Zero);
            NativeRead.Set(sortie,"CurrentTarget",Entity.None);NativeRead.Call(NativeRead.Get(sortie,"TrackedTargets"),"Clear");
        }
        static void Hold(IEntitySystem system,Entity entity,SimEntry entry,UnitState state)
        {
            entry.Order=RhodesTiltrotorOrder.Hold;entry.OrderedTarget=Entity.None;
            SetLanding(entry,entry.Landing);
            object movement=Component(system,entity,NativeUnitBindings.Movement);entry.Anchor=movement==null?state.StartOfFrameDynamicState.Position:(Vector2r)NativeRead.Get(movement,"CurrentDestination");
            NonReturning(system,entity);ApplyWeaponIntent(entity,entry);Publish(entity,entry);
        }
        static void Return(Entity entity,SimEntry entry)
        {entry.Order=RhodesTiltrotorOrder.Return;entry.OrderedTarget=Entity.None;RhodesTiltrotorReturnControl.EnsureExactGoal(ActiveSystem,entity,entry.Carrier);SetLanding(entry,false);ApplyWeaponIntent(entity,entry);Publish(entity,entry);}
        static bool CanDepart(object sim,IEntitySystem system,Entity entity,SimEntry entry,object unit)
        {
            if(RhodesOperatorTransport.CanTakeOffNow(system,entity))return true;
            if(RunStop(sim,entity))Hold(system,entity,entry,(UnitState)NativeRead.Call(unit,"GetState"));
            if(Reports++<64)Runtime.Log("RHODES_VTOL_DEPARTURE_BLOCKED entity="+entity+"; passengerOperationPending=True; nativeStop=True");
            return false;
        }
        // Cargo requests use the normal native MoveToGoal, with the already
        // registered Recall ability as an immutable goal tag. No synthetic
        // player event, transform write or all-unit discovery is involved.
        static void ObserveOperatorPilotOverride(SimEntry entry)
        {entry.ManualOrderRevision++;entry.PickupPassenger=Entity.None;entry.PickupDestination=Vector2r.Zero;}
        internal static int OperatorOrderRevision(Entity entity)
        {SimEntry entry;return Entries.TryGetValue(entity,out entry)?entry.ManualOrderRevision:0;}
        internal static bool IsOperatorTransferHover(Entity entity)
        {lock(Gate){Intent intent;return Intents.TryGetValue(entity,out intent)&&intent.Hold&&intent.FieldPhase&&!intent.Land;}}
        internal static bool OwnOperatorMove(object goal)
        {return goal!=null&&goal.GetType().Name=="MoveToGoal"&&(string)NativeRead.Get(goal,"mFromAbilityTypeName")==RhodesOperatorTransport.RecallAbilityKey;}
        static bool SetNativeIdle(IEntitySystem system,Entity entity)
        {
            Type goals=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.EntityGoalsCollection",true);
            NativeRead.Call(goals,"ClearGoals",entity);object movement=Component(system,entity,NativeUnitBindings.Movement);
            if(movement==null)return false;NativeRead.Call(movement,"SetToIdle");return true;
        }
        internal static bool HoldForOperatorTransfer(object sim,Entity entity)
        {
            IEntitySystem system=(IEntitySystem)NativeRead.Get(sim,"EntitySystem");object unit;UnitState state;
            SimEntry entry;if(!Alive(system,entity,out unit,out state)||!Entries.TryGetValue(entity,out entry))return false;
            if(!SetNativeIdle(system,entity))return false;
            entry.ManualOrderRevision++;entry.PickupPassenger=Entity.None;entry.PickupDestination=Vector2r.Zero;
            Hold(system,entity,entry,(UnitState)NativeRead.Call(unit,"GetState"));return true;
        }
        internal static bool MoveToOperator(object sim,Entity entity,Entity passenger,Vector2r destination)
        {bool issued;return MoveToOperator(sim,entity,passenger,destination,out issued);}
        internal static bool MoveToOperator(object sim,Entity entity,Entity passenger,Vector2r destination,out bool issued)
        {
            issued=false;
            if(!NativeUnitBindings.SameOwner(entity,passenger))return false;
            IEntitySystem system=(IEntitySystem)NativeRead.Get(sim,"EntitySystem");object unit;UnitState state;
            SimEntry entry;if(!Alive(system,entity,out unit,out state)||!Entries.TryGetValue(entity,out entry))return false;
            bool same=entry.PickupPassenger==passenger;
            Vector2r fromPassenger=state.StartOfFrameDynamicState.Position-destination;
            // Native Hover.SetToIdle changes CurrentDestination to the stopped
            // position. Hold copies that value into the ordinary Anchor, so it
            // cannot identify an unchanged pickup destination after arrival.
            if(RhodesOperatorPickupPolicy.ReuseHover(same,entry.Order==RhodesTiltrotorOrder.Hold,
                Fixed64.UnsafeFloatValue(fromPassenger.LengthSquared),Fixed64.UnsafeFloatValue(state.StartOfFrameDynamicState.Velocity.LengthSquared)))return true;
            Vector2r changed=entry.PickupDestination-destination;
            if(RhodesOperatorPickupPolicy.ReuseApproach(same,OwnOperatorMove(CurrentGoal(system,entity)),Fixed64.UnsafeFloatValue(changed.LengthSquared)))return true;
            Type type=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.MoveToGoal",true);
            object goal=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
                new object[]{destination,RhodesOperatorTransport.RecallAbilityKey},null);
            Type goals=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.EntityGoalsCollection",true);
            if(!(bool)NativeRead.Call(goals,"AddGoal",entity,goal,SelectedUnitManager.AddMode.NewSet))return false;
            entry.PickupPassenger=passenger;entry.PickupDestination=destination;entry.Order=RhodesTiltrotorOrder.Move;entry.OrderedTarget=Entity.None;entry.Anchor=destination;
            SetLanding(entry,false);NonReturning(system,entity);ApplyWeaponIntent(entity,entry);Publish(entity,entry);
            issued=true;return true;
        }
        internal static void FinishOperatorPickup(Entity entity)
        {
            SimEntry entry;if(!Entries.TryGetValue(entity,out entry)||entry.PickupPassenger==Entity.None)return;
            entry.PickupPassenger=Entity.None;entry.PickupDestination=Vector2r.Zero;
            if(ActiveSystem!=null&&OwnOperatorMove(CurrentGoal(ActiveSystem,entity)))
            {object unit;UnitState state;if(Alive(ActiveSystem,entity,out unit,out state)&&SetNativeIdle(ActiveSystem,entity))Hold(ActiveSystem,entity,entry,(UnitState)NativeRead.Call(unit,"GetState"));}
        }
        // Exact prefix callback for AirSortieProcessor.ProcessAirSortie(Entity).
        // Unregistered/native units and explicit Return use the untouched body.
        public static bool ShouldProcessAirSortie(Entity entity)
        {
            if(GilbertaGravityBridge.HasPullMovement(entity))return false;
            RhodesAirWingReturnControl.BeforeNativeSortie(entity);
            SimEntry entry;if(!Entries.TryGetValue(entity,out entry))return true;
            if(!RhodesTiltrotorOrderPolicy.UsesNativeSortie(entry.Order))return false;
            if(ActiveSystem==null||Component(ActiveSystem,entity,NativeUnitBindings.World)==null)return true;
            // Native commands execute before the event queue is consumed here.
            // If Move/Stop/Attack already replaced a return DockGoal, do not let
            // the latched old sortie overwrite that new command for one tick.
            object goal=CurrentGoal(ActiveSystem,entity);return goal!=null&&goal.GetType().Name=="DockGoal";
        }
        // Reuses the existing native pre-fire hook; never forces a weapon to
        // fire or changes ammunition, damage, cooldown, LOS or targetability.
        public static void BeforeProcessWeapon(Entity owner,object operation)
        {
            SimEntry entry;if(operation==null||!Entries.TryGetValue(owner,out entry))return;
            WeaponBinding binding=NativeRead.Get(operation,"WeaponBinding") as WeaponBinding;
            if(binding!=null&&RhodesEmbarkedOperatorSkills.IsProxyWeaponID(binding.WeaponID))return;
            Entity target=(Entity)NativeRead.Get(operation,"CurrentTarget");
            if(!RhodesTiltrotorOrderPolicy.AllowsFire(entry.Order,target==entry.OrderedTarget))NativeRead.Set(operation,"FiringCriteriaMet",false);
        }
        internal static bool CanCastEmbarkedSkills(Entity entity)
        {SimEntry entry;return Entries.TryGetValue(entity,out entry)&&entry.Order!=RhodesTiltrotorOrder.Return&&!entry.Landing;}
        internal static bool TryAutocastIntent(Entity entity,out Entity preferred)
        {
            preferred=Entity.None;SimEntry entry;
            if(!Entries.TryGetValue(entity,out entry)||entry.RecoveredUnknownIntent||entry.Landing||entry.Order==RhodesTiltrotorOrder.Move||
                entry.Order==RhodesTiltrotorOrder.Return||entry.PickupPassenger!=Entity.None)return false;
            object goal=ActiveSystem==null?null:CurrentGoal(ActiveSystem,entity);
            if(goal!=null)
            {
                string name=goal.GetType().Name;
                if(name=="MoveToGoal"||name=="DockGoal"||name=="FollowGoal"||name=="UseWeaponAbilityGoal")return false;
            }
            preferred=entry.OrderedTarget;return true;
        }
        // Subscription and generation binding happen before native event
        // delivery. Recovery and command progression remain in the postfix.
        internal static void PrepareEvents(object sim)
        {
            if(!Runtime.RhodesCarrier||sim==null)return;
            ActiveSystem=(IEntitySystem)NativeRead.Get(sim,"EntitySystem");
            if(!object.ReferenceEquals(SessionSim,sim))
            {
                bool replaced=SessionSim!=null;SessionSim=sim;
                if(replaced){Entries.Clear();lock(Gate){Pending.Clear();Managed.Clear();Intents.Clear();}}
                RecoveryPending=true;
            }
            IEventReceiver current=(IEventReceiver)NativeRead.Get(sim,"SimEventSystem");
            if(!object.ReferenceEquals(current,Receiver))
            {
                if(Receiver!=null){Receiver.RemoveHandler<AbilityDeploymentEvent>(AbilityHandler);Receiver.RemoveHandler<DockingUnitOperationEvent>(DockingHandler);}
                Receiver=current;if(Receiver!=null){Receiver.AddHandler<AbilityDeploymentEvent>(AbilityHandler);Receiver.AddHandler<DockingUnitOperationEvent>(DockingHandler);}
            }
        }
        public static void SimTick(object sim)
        {
            if(!Runtime.RhodesCarrier||sim==null)return;
            PrepareEvents(sim);
            IEntitySystem system=ActiveSystem;
            CurrentTick=checked((int)Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim,"GlobalFrameCount"),"FrameNumber")));
            if(RecoveryPending){RecoverNativeEntries(sim,system);RecoveryPending=false;}
            lock(Gate){if(Pending.Count==0&&Entries.Count==0)return;}
            Scratch.Clear();lock(Gate){while(Pending.Count>0)Scratch.Add(Pending.Dequeue());}
            foreach(Request request in Scratch)
            {
                object unit;UnitState state;if(!Alive(system,request.Entity,out unit,out state)||(request.Sender!=CommanderID.None&&request.Sender!=state.OwnerCommander))continue;
                SimEntry entry;if(!Entries.TryGetValue(request.Entity,out entry))
                {
                    if(Entries.Count>=MaximumUnits)continue;entry=new SimEntry{UnitEntity=request.Entity,Owner=state.OwnerCommander};object sortie=Component(system,request.Entity,NativeUnitBindings.AirSortie);
                    if(sortie!=null)entry.Carrier=(Entity)NativeRead.Get(sortie,"DockingEntity");Entries.Add(request.Entity,entry);lock(Gate)Managed.Add(request.Entity);
                }
                entry.RecoveredUnknownIntent=false;
                // These requests are observed native commands for this owner. Internal cargo
                // moves are native tagged goals and never generate this event.
                if(request.Kind!=RequestKind.LaunchReady)
                {ObserveOperatorPilotOverride(entry);}
                if((request.Kind==RequestKind.ReturnObserved||request.Kind==RequestKind.MoveObserved||request.Kind==RequestKind.AttackObserved||request.Kind==RequestKind.AttackMoveObserved)&&
                    !CanDepart(sim,system,request.Entity,entry,unit))continue;
                if(request.Kind==RequestKind.ReturnObserved){Return(request.Entity,entry);continue;}
                if(request.Kind==RequestKind.LaunchReady)
                {
                    object sortie=Component(system,request.Entity,NativeUnitBindings.AirSortie);entry.Anchor=sortie==null?state.StartOfFrameDynamicState.Position:(Vector2r)NativeRead.Get(sortie,"LastKnownTargetLocation");
                    entry.Order=RhodesTiltrotorOrder.Move;SetLanding(entry,false,true);NonReturning(system,request.Entity);ApplyWeaponIntent(request.Entity,entry);
                    if(!RunCommand(sim,request.Entity,MoveKey,AbilityTargetingType.Ground,entry.Anchor))
                    {if(RunStop(sim,request.Entity))Hold(system,request.Entity,entry,(UnitState)NativeRead.Call(unit,"GetState"));}
                }
                else if(request.Kind==RequestKind.StopObserved)Hold(system,request.Entity,entry,state);
                else
                {
                    entry.Anchor=request.Point;entry.OrderedTarget=request.Target;
                    entry.Order=request.Kind==RequestKind.AttackObserved?RhodesTiltrotorOrder.AttackTarget:request.Kind==RequestKind.AttackMoveObserved?RhodesTiltrotorOrder.AttackMove:RhodesTiltrotorOrder.Move;
                    SetLanding(entry,false);
                    NonReturning(system,request.Entity);ApplyWeaponIntent(request.Entity,entry);
                }
                Publish(request.Entity,entry);
                if(Reports++<64)Runtime.Log("RHODES_VTOL_CONTROL entity="+request.Entity+"; command="+request.Kind+"; mode="+entry.Order+"; destination="+entry.Anchor+"; landingRequested="+entry.Landing+"; nativeXY=True; noIdleFire=True; emptyAmmoAutoReturn=False; fieldLandingAbility=False; airborneOperatorTransfer=True");
            }
            Removal.Clear();
            foreach(KeyValuePair<Entity,SimEntry> pair in Entries)
            {
                object unit;UnitState state;if(!Alive(system,pair.Key,out unit,out state)||pair.Value.Owner!=state.OwnerCommander){Removal.Add(pair.Key);continue;}
                SimEntry entry=pair.Value;object goal=CurrentGoal(system,pair.Key);
                if(entry.WaitingForStop&&Fixed64.UnsafeFloatValue(state.StartOfFrameDynamicState.Velocity.Length)<=RhodesTiltrotorOrderPolicy.ArrivalSpeedMetresPerSecond)
                {entry.WaitingForStop=false;entry.AltitudeStartTick=CurrentTick;}
                if(goal!=null&&goal.GetType().Name=="DockGoal"&&entry.Order!=RhodesTiltrotorOrder.Return&&CanDepart(sim,system,pair.Key,entry,unit))Return(pair.Key,entry);
                if(entry.Order==RhodesTiltrotorOrder.Move)
                {
                    bool pickup=entry.PickupPassenger!=Entity.None;
                    Vector2r destination=pickup?entry.PickupDestination:entry.Anchor;
                    Vector2r delta=destination-state.StartOfFrameDynamicState.Position,velocity=state.StartOfFrameDynamicState.Velocity;
                    float dx=Fixed64.UnsafeFloatValue(delta.X),dy=Fixed64.UnsafeFloatValue(delta.Y),vx=Fixed64.UnsafeFloatValue(velocity.X),vy=Fixed64.UnsafeFloatValue(velocity.Y);
                    bool arrived=pickup?RhodesOperatorPickupPolicy.Arrived(dx*dx+dy*dy,vx*vx+vy*vy):RhodesTiltrotorOrderPolicy.Arrived(dx*dx+dy*dy,vx*vx+vy*vy);
                    // Completing a move must not issue native Stop, which also
                    // clears the self-target of an accepted healing cycle.
                    bool keepHealing=arrived&&RhodesEmbarkedOperatorSkills.MovingHealingOperation(pair.Key)!=null;
                    if(arrived&&(pickup||keepHealing?SetNativeIdle(system,pair.Key):RunStop(sim,pair.Key)))
                    {Hold(system,pair.Key,entry,(UnitState)NativeRead.Call(unit,"GetState"));if(Reports++<64)Runtime.Log("RHODES_VTOL_DEPLOYMENT_ARRIVED entity="+pair.Key+"; nativeMoveCompleted=True; holdingAtOrderedPoint=True");}
                }
                Publish(pair.Key,entry);
            }
            foreach(Entity entity in Removal){Entries.Remove(entity);lock(Gate){Managed.Remove(entity);Intents.Remove(entity);Visuals.Remove(entity);}RhodesOperatorTransport.ReportUnobserved(entity);}
            Scratch.Clear();Removal.Clear();
        }
        static void RecoverNativeEntries(object sim,IEntitySystem system)
        {
            int recovered=0,unknown=0;RecoveryEntities.Clear();
            foreach(object candidate in (IEnumerable)NativeRead.Get(NativeRead.Get(sim,"UnitManager"),"Units"))
            {
                Entity entity=(Entity)NativeRead.Get(candidate,"Entity");object unit;UnitState state;
                if(Alive(system,entity,out unit,out state))RecoveryEntities.Add(entity);
            }
            RecoveryEntities.Sort(RhodesSimulationEvents.EntityComparer);
            foreach(Entity entity in RecoveryEntities)
            {
                object unit;UnitState state;
                if(!Alive(system,entity,out unit,out state)||Entries.ContainsKey(entity)||Entries.Count>=MaximumUnits)continue;
                SimEntry entry=new SimEntry{UnitEntity=entity,Owner=state.OwnerCommander,Anchor=state.StartOfFrameDynamicState.Position};
                object sortie=Component(system,entity,NativeUnitBindings.AirSortie),goal=CurrentGoal(system,entity);
                if(sortie!=null)entry.Carrier=(Entity)NativeRead.Get(sortie,"DockingEntity");
                string name=goal==null?string.Empty:goal.GetType().Name;
                if(name=="DockGoal")entry.Order=RhodesTiltrotorOrder.Return;
                else if(name=="MoveToGoal")
                {entry.Order=RhodesTiltrotorOrder.Move;object movement=Component(system,entity,NativeUnitBindings.Movement);if(movement!=null)entry.Anchor=(Vector2r)NativeRead.Get(movement,"CurrentDestination");}
                else if(goal!=null){entry.RecoveredUnknownIntent=true;unknown++;}
                SetLanding(entry,false);Entries.Add(entity,entry);lock(Gate)Managed.Add(entity);ApplyWeaponIntent(entity,entry);Publish(entity,entry);recovered++;
            }
            RecoveryEntities.Clear();
            if(recovered>0)Runtime.Log("RHODES_FLIGHT_RECOVERY nativeEntities="+recovered+"; unrecoverableIntents="+unknown+"; unknownIntentFireClosedUntilNativeCommand=True; nativeGoalsNotReplayed=True; saveReplayIntegrationVerified=False");
        }
        public static void Reset()
        {
            RhodesAirWingReturnControl.Reset();
            if(Receiver!=null){Receiver.RemoveHandler<AbilityDeploymentEvent>(AbilityHandler);Receiver.RemoveHandler<DockingUnitOperationEvent>(DockingHandler);}Receiver=null;
            List<RhodesTiltrotorAltitudeDriver> drivers=new List<RhodesTiltrotorAltitudeDriver>(Drivers.Values);Drivers.Clear();foreach(RhodesTiltrotorAltitudeDriver driver in drivers)if(driver!=null)driver.Release();
            lock(Gate){Pending.Clear();Bound.Clear();Managed.Clear();Intents.Clear();Visuals.Clear();}Entries.Clear();Scratch.Clear();Removal.Clear();RecoveryEntities.Clear();ActiveSystem=null;SessionSim=null;RecoveryPending=false;Reports=CurrentTick=0;
        }
    }

    // The existing faction model driver remains the sole geometry owner.
    // Prepare its U08 frame in Update before native camera LateUpdate; its
    // frame-gated LateUpdate is a fallback. UnitView/Rigidbody stay untouched.
    public sealed class RhodesTiltrotorAltitudeDriver:MonoBehaviour
    {
        UnitView View;Entity Entity=Entity.None;FactionModelBinding Binding;
        RhodesTiltrotorPresentationAnchor PresentationAnchor;
        FactionUnitModelDriver PresentationDriver;
        readonly RhodesTiltrotorAttitude51 FreeFlightAttitude=new RhodesTiltrotorAttitude51();
        bool Ready,HaveRenderAltitude,HaveTouchdownReport,LastTouchdown,HaveFieldDatum,HaveDisplayedBottom;
        object DockingCoordinator;
        float RenderAltitude,RecoveryHeightOffset,FieldBottomDatum,LastDisplayedBottomAGL;
        bool HadV17Attitude;Quaternion LastV17WorldRotation,V17ReleaseRotation;
        int V17ReleaseTick=-1;float V17ReleaseSub;
        int LastRecoveryEpoch=int.MinValue,LastPhaseEpoch=int.MinValue,GroundedEpoch=int.MinValue,AscentEpoch=int.MinValue,Traces;
        internal void Initialize(UnitView view)
        {
            if(Ready&&View==view&&Entity.Equals(view.EntityID))return;
            RestorePose();Unobserved(false);View=view;Entity=view.EntityID;Ready=true;
            PresentationDriver=view.GetComponent<FactionUnitModelDriver>();
            HaveRenderAltitude=HaveTouchdownReport=false;LastPhaseEpoch=GroundedEpoch=AscentEpoch=int.MinValue;Traces=0;
            HaveFieldDatum=HaveDisplayedBottom=false;
            DockingCoordinator=null;LastRecoveryEpoch=int.MinValue;RecoveryHeightOffset=0f;
            HadV17Attitude=false;V17ReleaseTick=-1;
            FreeFlightAttitude.Reset();
            PresentationAnchor=RhodesTiltrotorPresentationAnchor.Ensure(view,view.GetComponent<FactionModelBinding>());
        }
        void Update()
        {
            if(!Ready||View==null||!Entity.Equals(View.EntityID)||PresentationDriver==null)return;
            PresentationDriver.PrepareAirPresentationFrame();
        }
        void Touchdown(bool value)
        {if(HaveTouchdownReport&&LastTouchdown==value)return;HaveTouchdownReport=true;LastTouchdown=value;RhodesOperatorTransport.ReportTouchdown(Entity,value);}
        void Unobserved(bool guided)
        {
            HaveRenderAltitude=HaveTouchdownReport=HaveDisplayedBottom=false;
            if(!Entity.Equals(Entity.None))
            {RhodesTiltrotorFlightControl.VisualMeasurement(Entity,false,guided,0f,0f,false);RhodesOperatorTransport.ReportUnobserved(Entity);}
        }
        void RestorePose()
        {
            if(Binding==null||Binding.GeometryRoot==null)return;
            Binding.GeometryRoot.localPosition=Binding.GeometryRestPosition;Binding.GeometryRoot.localRotation=Binding.GeometryRestRotation;
            if(View!=null&&View.SelectionCollider!=null)View.SelectionCollider.center=Binding.SelectionRestCenter;
        }
        static float BottomOffset(Transform geometry,Bounds bounds)
        {
            float minimum=float.PositiveInfinity;
            for(int corner=0;corner<8;corner++)
            {
                Vector3 point=new Vector3((corner&1)==0?bounds.min.x:bounds.max.x,(corner&2)==0?bounds.min.y:bounds.max.y,(corner&4)==0?bounds.min.z:bounds.max.z);
                minimum=Mathf.Min(minimum,geometry.TransformVector(point).y);
            }
            return minimum;
        }
        // Free flight holds the authored upright datum, rather than lifting
        // the aircraft whenever a bank changes the lowest AABB corner. Yaw
        // does not affect this vertical reference. The scale is instance-owned
        // but constant for this private U08 model.
        internal static float FieldRestBottomDatum(Bounds bounds,Quaternion rest,Vector3 scale)
        {
            Vector3 center=bounds.center,extent=bounds.extents;
            float norm=rest.x*rest.x+rest.y*rest.y+rest.z*rest.z+rest.w*rest.w;
            if(norm<.000001f)throw new ArgumentException("U08 authored datum rotation is invalid");
            float twice=2f/norm;
            float x=twice*(rest.x*rest.y+rest.w*rest.z)*scale.x;
            float y=(1f-twice*(rest.x*rest.x+rest.z*rest.z))*scale.y;
            float z=twice*(rest.y*rest.z-rest.w*rest.x)*scale.z;
            return x*center.x+y*center.y+z*center.z-Math.Abs(x)*extent.x-Math.Abs(y)*extent.y-Math.Abs(z)*extent.z;
        }
        internal static float RecoveryOffsetFromDisplayedBottom(bool have,float displayedBottom,float guidedBottom,float remaining)
        {return have&&remaining>.001f?(displayedBottom-guidedBottom)/remaining:0f;}
        void Trace(string phase,float desired,float actualView,float actualBottom)
        {
            if(Traces++>=16)return;
            Runtime.Log("RHODES_VTOL_VISUAL_"+phase+" entity="+Entity+"; phaseAGL="+desired+"; actualViewAGL="+actualView+
                "; geometryBottomAGL="+actualBottom+"; geometryYOwner=FactionUnitModelDriver; nativeRigidbodyWritten=False; fieldRestDatumStable=True; recoveryUsesDisplayedBottom=True; selectionProxyAligned=True");
        }
        void ApplyV17Attitude(FactionModelBinding binding)
        {
            Transform geometry=binding.GeometryRoot,guide;float weight;
            if(RhodesV17AviationGuideDriver.TryAircraftAttitude(Entity,out guide,out weight))
            {
                geometry.localRotation=RhodesAviationPoseMath.GuideLocalRotation(geometry.parent.rotation,binding.GeometryRestRotation,guide.rotation,weight);
                LastV17WorldRotation=geometry.rotation;HadV17Attitude=true;V17ReleaseTick=-1;return;
            }
            if(HadV17Attitude)
            {
                // Begin from the last displayed deck attitude, then return to
                // this frame's existing Hover/flight visual rotation. Height,
                // landing, field intent and native movement remain unchanged.
                V17ReleaseRotation=RhodesAviationPoseMath.Multiply(RhodesAviationPoseMath.Inverse(geometry.parent.rotation),LastV17WorldRotation);
                V17ReleaseTick=BBI.Unity.Game.ShipbreakersMain.CurrentSimTick;V17ReleaseSub=BBI.Unity.Game.ShipbreakersMain.CurrentSimSubFrame;HadV17Attitude=false;
            }
            if(V17ReleaseTick<0)return;
            float age=RhodesAviationLaunchProfile.NativePresentationAge(V17ReleaseTick,V17ReleaseSub,BBI.Unity.Game.ShipbreakersMain.CurrentSimTick,BBI.Unity.Game.ShipbreakersMain.CurrentSimSubFrame);
            geometry.localRotation=RhodesAviationPoseMath.ReleaseLocalRotation(V17ReleaseRotation,geometry.localRotation,age);
            if(age>=RhodesAviationPoseMath.ReleaseSeconds)V17ReleaseTick=-1;
        }
        internal void ApplyGeometry(FactionModelBinding binding,bool visible)
        {
            if(Binding!=binding){Binding=binding;HaveFieldDatum=false;}
            if(!Ready||View==null||binding==null||binding.GeometryRoot==null||!Entity.Equals(View.EntityID)||
                View.NonInterpolatedTickState==null||View.NonInterpolatedTickState.UnitIsDead)
            {RestorePose();Unobserved(false);return;}
            if(PresentationAnchor==null)PresentationAnchor=RhodesTiltrotorPresentationAnchor.Ensure(View,binding);
            if(Runtime.PresentationDeltaTime<=0f){if(PresentationAnchor!=null)PresentationAnchor.UpdateBody(binding);return;}
            float guideAngle;
            bool guided=RhodesTiltrotorGuideDriver.TryNacelleAngle(Entity,out guideAngle);
            bool field,landing;float phaseAltitude,targetAltitude;int epoch;
            RhodesTiltrotorFlightControl.ReadVisualIntent(Entity,out field,out landing,out phaseAltitude,out targetAltitude,out epoch);
            float speed=Fixed64.UnsafeFloatValue(View.NonInterpolatedTickState.StartOfFrameDynamicState.Velocity.Length);
            // Always derive the base pose afresh from native XY + authored
            // local pose. A previous vertical offset never feeds back into XY.
            RestorePose();Transform geometry=binding.GeometryRoot;Vector3 point=geometry.position;
            Vector2r mapPoint=new Vector2r(Fixed64.UnsafeFromFloat(point.x),Fixed64.UnsafeFromFloat(point.z));
            float ground=CustomFlatMap.Ready?CustomFlatMap.SampleOriginalHeight(mapPoint):View.MostRecentGroundHit.point.y;
            if(guided)
            {
                FreeFlightAttitude.Reset();
                ApplyV17Attitude(binding);
                // Native deck guidance owns the pose. Cache its real height so
                // field flight starts at this pose when the hangar releases it.
                float guidedBottom=point.y+BottomOffset(geometry,binding.ModelBounds)-ground;
                int recoveryEpoch;
                if(RhodesTiltrotorGuideDriver.TryRecoveryEpoch(Entity,out recoveryEpoch))
                {
                    if(DockingCoordinator==null)DockingCoordinator=NativeRead.Get(View,"mDockingCoordinator");
                    int phase=DockingCoordinator==null?0:Convert.ToInt32(NativeRead.Get(DockingCoordinator,"Phase"));
                    float progress=DockingCoordinator==null?1f:Convert.ToSingle(NativeRead.Get(DockingCoordinator,"PhaseProgress"));
                    float remaining=RhodesTiltrotorProfile.RecoveryHandoffRemaining(phase,progress);
                    if(LastRecoveryEpoch!=recoveryEpoch)
                    {
                        LastRecoveryEpoch=recoveryEpoch;
                        RecoveryHeightOffset=RecoveryOffsetFromDisplayedBottom(HaveDisplayedBottom,LastDisplayedBottomAGL,guidedBottom,remaining);
                        Trace("RECOVERY_ALIGNMENT_HANDOFF",phaseAltitude,View.Position.y-ground,guidedBottom+RecoveryHeightOffset*remaining);
                    }
                    // Native alignment starts from UnitViewY, while field
                    // Hover height belongs to GeometryRoot. Retain that visual
                    // difference only through the native Hermite alignment.
                    float displacement=RecoveryHeightOffset*remaining;
                    point.y+=displacement;geometry.position=point;guidedBottom+=displacement;
                    if(View.SelectionCollider!=null)View.SelectionCollider.center=binding.SelectionRestCenter+
                        View.SelectionCollider.transform.InverseTransformVector(Vector3.up*displacement);
                }
                else{LastRecoveryEpoch=int.MinValue;RecoveryHeightOffset=0f;}
                HaveRenderAltitude=HaveTouchdownReport=false;
                LastDisplayedBottomAGL=guidedBottom;HaveDisplayedBottom=true;
                if(PresentationAnchor!=null)PresentationAnchor.UpdateBody(binding);
                RhodesTiltrotorFlightControl.VisualMeasurement(Entity,visible,true,guidedBottom,View.Position.y-ground,false);
                RhodesOperatorTransport.ReportUnobserved(Entity);
                return;
            }
            LastRecoveryEpoch=int.MinValue;RecoveryHeightOffset=0f;
            // Counter the native interceptor's hover-time pitch accumulation
            // on our geometry only. Keep native heading, XY/altitude physics,
            // deck guides and the existing V17 release blend authoritative.
            Vector3 flightForward=View.ForwardHorizontal;
            if(flightForward.sqrMagnitude>.001f)
            {
                Quaternion heading=Quaternion.LookRotation(flightForward,Vector3.up);
                Quaternion body=FreeFlightAttitude.Step(heading,geometry.parent.rotation,speed,Runtime.PresentationDeltaTime,landing);
                geometry.rotation=RhodesAviationPoseMath.Multiply(body,binding.GeometryRestRotation);
            }
            if(field)
            {
                ApplyV17Attitude(binding);
                if(!HaveFieldDatum){FieldBottomDatum=FieldRestBottomDatum(binding.ModelBounds,binding.GeometryRestRotation,geometry.lossyScale);HaveFieldDatum=true;}
                if(!HaveRenderAltitude)
                {
                    // The native launch phase starts from the displayed bottom.
                    // Convert it once to the stable rest datum, retaining the
                    // last deck pose before converging on the field phase.
                    RenderAltitude=phaseAltitude+FieldBottomDatum-BottomOffset(geometry,binding.ModelBounds);HaveRenderAltitude=true;
                }
                else RenderAltitude=Mathf.MoveTowards(RenderAltitude,phaseAltitude,RhodesTiltrotorOrderPolicy.VerticalMetresPerSecond*Mathf.Min(Runtime.PresentationDeltaTime,.1f));
                point.y=ground+RenderAltitude-FieldBottomDatum;float displacement=point.y-geometry.position.y;geometry.position=point;
                // This is the native mouse-selection collider only. Native
                // simulation shape/navigation and Rigidbody XY/Y stay native.
                if(View.SelectionCollider!=null)View.SelectionCollider.center=binding.SelectionRestCenter+
                    View.SelectionCollider.transform.InverseTransformVector(Vector3.up*displacement);
            }
            else{HaveRenderAltitude=false;ApplyV17Attitude(binding);}
            // Maintain the same inexpensive transform pose when meshes are
            // hidden or icon-only. Otherwise the stable icon/focus target would
            // fall back to the planar simulation root at long camera ranges.
            if(PresentationAnchor!=null)PresentationAnchor.UpdateBody(binding);
            float bottom=geometry.position.y+BottomOffset(geometry,binding.ModelBounds)-ground;
            LastDisplayedBottomAGL=bottom;HaveDisplayedBottom=true;
            if(!visible)
            {
                HaveTouchdownReport=false;
                RhodesTiltrotorFlightControl.VisualMeasurement(Entity,false,false,0f,0f,false);
                RhodesOperatorTransport.ReportUnobserved(Entity);return;
            }
            float viewAGL=View.Position.y-ground;
            bool grounded=landing&&speed<=RhodesTiltrotorOrderPolicy.ArrivalSpeedMetresPerSecond&&bottom>=-.1f&&bottom<1f;
            RhodesTiltrotorFlightControl.VisualMeasurement(Entity,true,false,bottom,viewAGL,grounded);Touchdown(grounded);
            if(field&&epoch!=LastPhaseEpoch){LastPhaseEpoch=epoch;Trace("PHASE_START",phaseAltitude,viewAGL,bottom);}
            if(grounded&&GroundedEpoch!=epoch){GroundedEpoch=epoch;Trace("FIELD_TOUCHDOWN",phaseAltitude,viewAGL,bottom);}
            if(field&&!landing&&RenderAltitude>=targetAltitude-.5f&&AscentEpoch!=epoch)
            {AscentEpoch=epoch;Trace("ASCENT_COMPLETE",phaseAltitude,viewAGL,bottom);}
        }
        void ReleaseAnchor(){if(PresentationAnchor!=null)PresentationAnchor.Release();PresentationAnchor=null;}
        internal void Release(){RestorePose();Unobserved(false);ReleaseAnchor();PresentationDriver=null;Ready=false;enabled=false;}
        void OnDisable(){RestorePose();Unobserved(false);ReleaseAnchor();PresentationDriver=null;Ready=false;RhodesTiltrotorFlightControl.Unbind(Entity);}
        void OnDestroy(){RestorePose();Unobserved(false);ReleaseAnchor();PresentationDriver=null;Ready=false;RhodesTiltrotorFlightControl.Unbind(Entity);View=null;Binding=null;Entity=Entity.None;}
    }
}
