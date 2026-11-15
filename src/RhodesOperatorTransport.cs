using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BBI.Core.ComponentModel;
using BBI.Core.Events;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Events;
using BBI.Game.Simulation;
using BBI.Unity.Game.HUD;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Copy-only bridge from the simulation owner to the native HUD events.
    // UI consumers never walk native hangars or access live unit components.
    public struct RhodesOperatorCargoStatus
    {
        public readonly int Stored,Ready,Cooling,Deployed,Occupied,Transferring,Revision;
        public readonly bool LandingReady,Warning;
        public readonly string LastAction;
        public RhodesOperatorCargoStatus(int stored,int ready,int cooling,int deployed,int occupied,int transferring,bool landingReady,string lastAction,bool warning,int revision)
        {Stored=stored;Ready=ready;Cooling=cooling;Deployed=deployed;Occupied=occupied;Transferring=transferring;LandingReady=landingReady;LastAction=lastAction;Warning=warning;Revision=revision;}
    }
    // Real native tracked cargo, not a nearby spawn or a hidden world unit.
    // Registered identities have independent exact native typed bays and
    // type-keyed claims; different operators can deploy simultaneously.
    public static class RhodesOperatorTransport
    {
        public const int Capacity=4;
        public const string ProduceAbilityKey="Ability_EF_U08_ProduceLaevatain";
        public const string UnloadAbilityKey="Ability_EF_U08_UnloadOperator";
        public const string RecallAbilityKey="Ability_EF_U08_RecallOperator";
        public const string ReturnAbilityKey="Ability_EF_Operator_ReturnToTransport";
        const int MaximumCarriers=64,MaximumPending=128,MinimumHoverTicks=4,MaximumPickupTicks=960;
        static object SessionSim;
        static bool RecoveryPending;
        enum RequestKind { Observe, Unload, Recall, ReturnPassenger, CreatedStored, Produced, ProduceOrdered, PassengerCommand }
        struct Request {internal Entity Carrier,Passenger;internal RequestKind Kind;internal bool Immediate;internal CommanderID Sender;}
        sealed class Entry
        {
            internal Entity Carrier;
            internal CommanderID Owner;
            internal readonly SortedDictionary<Entity,ReturnOperation> Boarding=new SortedDictionary<Entity,ReturnOperation>(RhodesSimulationEvents.EntityComparer);
            internal readonly HashSet<Entity> Unloading=new HashSet<Entity>();
            internal readonly List<Entity> Completed=new List<Entity>(Capacity);
            internal int HoverSince=-1,NextStatusTick,NextPickupTick;
            internal bool PendingUnload;internal Entity PendingUnloadPassenger;internal int UnloadRevision,UnloadTick;
            internal string LastAction="N 全部部署  /  B 全部召回";
            internal bool Warning;
        }
        sealed class ReturnOperation {internal bool Docking;internal int StartedTick,AircraftOrderRevision;}
        sealed class Claim {internal Entity Passenger,Carrier;}
        static readonly object Gate=new object();
        static readonly Queue<Request> Pending=new Queue<Request>();
        static readonly List<Request> Scratch=new List<Request>(MaximumPending);
        static readonly SortedDictionary<Entity,Entry> Entries=new SortedDictionary<Entity,Entry>(RhodesSimulationEvents.EntityComparer);
        static readonly List<Entity> RecoveryEntities=new List<Entity>(MaximumCarriers);
        static readonly Dictionary<CommanderIdentityKey,Claim> Claims=new Dictionary<CommanderIdentityKey,Claim>();
        static readonly Dictionary<Entity,bool> Touchdown=new Dictionary<Entity,bool>();
        static readonly HashSet<Entity> Busy=new HashSet<Entity>();
        static readonly Dictionary<Entity,RhodesOperatorCargoStatus> Status=new Dictionary<Entity,RhodesOperatorCargoStatus>();
        static readonly Dictionary<Entity,RhodesOperatorManifest> Manifests=new Dictionary<Entity,RhodesOperatorManifest>();
        static readonly HashSet<Entity> KnownOperators=new HashSet<Entity>();
        static readonly List<Entity> DeadOperators=new List<Entity>();
        struct IdentityAvailability {internal bool Blocked;internal string Reason;}
        static readonly Dictionary<CommanderIdentityKey,IdentityAvailability> IdentityStates=new Dictionary<CommanderIdentityKey,IdentityAvailability>();
        static int NextIdentityTick;
        static readonly List<Entity> Removal=new List<Entity>();
        static readonly List<CommanderIdentityKey> ClaimRemoval=new List<CommanderIdentityKey>();
        static readonly BBI.Core.Events.EventHandler<AbilityDeploymentEvent> AbilityHandler=OnAbility;
        static readonly BBI.Core.Events.EventHandler<SimUnitCreatedEvent> CreationHandler=OnCreated;
        static readonly BBI.Core.Events.EventHandler<ProductionEvent> ProductionHandler=OnProduction;
        static IEventReceiver Receiver;
        static int Reports,SnapshotReports,StatusRevision;

        public static bool IsAbility(string key)
        {RhodesOperatorDefinition definition;return RhodesOperatorRoster.TryGetProduction(key,out definition)||key==UnloadAbilityKey||key==RecallAbilityKey||key==ReturnAbilityKey;}
        public static bool OperationsPending(Entity carrier){lock(Gate)return Busy.Contains(carrier);}
        public static bool CanTakeOff(Entity carrier){return !OperationsPending(carrier);}
        internal static bool CanTakeOffNow(IEntitySystem system,Entity carrier)
        {
            Entry entry;if(!Entries.TryGetValue(carrier,out entry))return true;
            if(entry.Unloading.Count>0)return false;
            object hangar=system.GetComponent<object>(carrier,NativeUnitBindings.Hangar);
            if(hangar!=null)foreach(KeyValuePair<Entity,ReturnOperation> pair in entry.Boarding)
                if(QueueHas(hangar,pair.Key))return false;
            return true;
        }
        public static bool TryGetStatus(Entity carrier,out RhodesOperatorCargoStatus status)
        {lock(Gate)return Status.TryGetValue(carrier,out status);}
        public static bool TryGetManifest(Entity carrier,out RhodesOperatorManifest manifest)
        {lock(Gate)return Manifests.TryGetValue(carrier,out manifest);}
        // Live simulation lookup, also used at the exact pre-fire checkpoint.
        // Published cargo cards are not an authority for a newly issued cast.
        internal static bool FullyDockedForEmbarkedSkill(int occupancy,bool assigned,bool queued,bool docked,bool suspended,bool inWorld,bool maneuvering,bool presentationOnly)
        {
            // Native servicing still owns healing, rearming and permission to
            // leave the bay. A fully stored operator may use the aircraft proxy
            // while servicing; native skill cooldown remains a separate gate.
            return (occupancy==2||occupancy==3)&&assigned&&!queued&&docked&&!suspended&&!inWorld&&!maneuvering&&!presentationOnly;
        }
        internal static bool TryReadyEmbarkedPassenger(IEntitySystem system,Entity carrier,string type,out Entity passenger,out string reason)
        {
            passenger=Entity.None;reason="未搭载";Entry entry;object unit;UnitState state;
            if(!Entries.TryGetValue(carrier,out entry)||!Unit(system,carrier,FactionUnits.TiltStrikeKey,out unit,out state))return false;
            if(entry.Owner!=state.OwnerCommander){reason="归属变更中";return false;}
            if(state.IsDocked||state.IsSuspended||!system.HasComponent(carrier,NativeUnitBindings.World)||(bool)NativeRead.Get(unit,"PresentationOnly")||
                !RhodesTiltrotorFlightControl.CanCastEmbarkedSkills(carrier)){reason="起降或返航中";return false;}
            if(entry.Boarding.Count>0||entry.Unloading.Count>0||entry.PendingUnload){reason="接驳中";return false;}
            object hangar=system.GetComponent<object>(carrier,NativeUnitBindings.Hangar);Array slots=hangar==null?null:PassengerSlots(hangar,type);
            if(slots==null||slots.Length==0)return false;
            object slot=slots.GetValue(0);int occupancy=Occupancy(slot);Entity stored=Stored(slot);
            if((occupancy!=2&&occupancy!=3)||stored==Entity.None)return false;
            if(!OwnedRosterAlive(system,stored,entry.Owner,out unit,out state)||state.TypeID!=type||
                !FullyDockedForEmbarkedSkill(occupancy,AssignedSlot(hangar,slot,stored),QueueHas(hangar,stored),state.IsDocked,state.IsSuspended,
                    system.HasComponent(stored,NativeUnitBindings.World),system.HasComponent(stored,NativeUnitBindings.Movement),(bool)NativeRead.Get(unit,"PresentationOnly")))
            {reason="接驳中";return false;}
            passenger=stored;reason="就绪";return true;
        }
        // Player/UI actions enter only via the native ability scheduler and
        // AbilityDeploymentEvent. Queue below is reserved for native events.
        public static bool TryGetProductionBlockReason(CommanderID owner,string type,out string reason)
        {
            lock(Gate)
            {
                if(!RhodesOperatorRoster.Contains(type)){reason="无法识别该干员";return true;}
                IdentityAvailability state;if(!IdentityStates.TryGetValue(new CommanderIdentityKey(owner,type),out state)){reason="干员状态正在更新，请稍候";return true;}
                reason=state.Reason;return state.Blocked;
            }
        }
        public static void ReportTouchdown(Entity carrier,bool grounded)
        {lock(Gate){if(Touchdown.ContainsKey(carrier)||Touchdown.Count<MaximumCarriers)Touchdown[carrier]=grounded;}}
        // A culled/icon-only view has no current altitude observation. It is
        // distinct from a visible aircraft that is positively still airborne.
        // Only the former may use the conservative simulation-time fallback.
        public static void ReportUnobserved(Entity carrier)
        {lock(Gate)Touchdown.Remove(carrier);}
        static void Queue(Entity entity,RequestKind kind)
        {Queue(entity,Entity.None,kind,false);}
        static void Queue(Entity carrier,Entity passenger,RequestKind kind,bool immediate)
        {Queue(carrier,passenger,kind,immediate,CommanderID.None);}
        static void Queue(Entity carrier,Entity passenger,RequestKind kind,bool immediate,CommanderID sender)
        {lock(Gate){if(Pending.Count<MaximumPending)Pending.Enqueue(new Request{Carrier=carrier,Passenger=passenger,Kind=kind,Immediate=immediate,Sender=sender});}}
        static void OnCreated(SimUnitCreatedEvent evt)
        {
            if(evt==null)return;
            Queue(evt.NewEntity,RequestKind.Observe);
            if((int)evt.State==1)Queue(Entity.None,evt.NewEntity,RequestKind.CreatedStored,false);
        }
        static void OnProduction(ProductionEvent evt)
        {
            if(evt!=null&&(int)evt.CurrentProductionState==4&&RhodesOperatorRoster.Contains(evt.UnitProductionType))
                Queue(evt.Source,evt.EntityProduced,RequestKind.Produced,evt.ProducedImmediately);
        }
        static void OnAbility(AbilityDeploymentEvent evt)
        {
            if(evt==null||!RhodesResearch.IsDeclaredOwner(evt.CommandSender)||evt.ActionResult!=AbilityDeploymentActionResult.DeploymentSucceeded||evt.SourceEntities==null)return;
            RequestKind kind;RhodesOperatorDefinition definition;
            if(evt.AbilityType==ReturnAbilityKey)
            {foreach(Entity passenger in evt.SourceEntities)if(NativeUnitBindings.OwnerOf(passenger)==evt.CommandSender)Queue(Entity.None,passenger,RequestKind.ReturnPassenger,false,evt.CommandSender);return;}
            if(evt.AbilityType==UnloadAbilityKey)kind=RequestKind.Unload;
            else if(evt.AbilityType==RecallAbilityKey)kind=RequestKind.Recall;
            else if(RhodesOperatorRoster.TryGetProduction(evt.AbilityType,out definition))kind=RequestKind.ProduceOrdered;
            else if(IsMovementCommand(evt.AbilityType,evt.TargetEntities!=null&&evt.TargetEntities.Length>0))
            {foreach(Entity passenger in evt.SourceEntities)if(NativeUnitBindings.OwnerOf(passenger)==evt.CommandSender)Queue(Entity.None,passenger,RequestKind.PassengerCommand,false,evt.CommandSender);return;}
            else
            {
                object sim=NativeRead.Get(typeof(Sim),"Instance");
                object settings=sim==null?null:NativeRead.Get(sim,"Settings");
                object types=settings==null?null:NativeRead.Get(settings,"EntityTypes");
                object typeDefinition=types==null?null:NativeRead.Call(types,"GetEntityType",evt.AbilityType);
                AbilityAttributes ability=null;
                BBI.Core.Data.EntityTypeAttributes typed=typeDefinition as BBI.Core.Data.EntityTypeAttributes;
                if(typed!=null)ability=typed.Get<AbilityAttributes>();
                // Successful player Q/other active skills override pickup;
                // passive system events do not cancel a waiting operator.
                if(!IsPlayerActivePassengerAbility(ability))return;
                foreach(Entity passenger in evt.SourceEntities)
                    if(KnownOperators.Contains(passenger)&&NativeUnitBindings.OwnerOf(passenger)==evt.CommandSender)Queue(Entity.None,passenger,RequestKind.PassengerCommand,false,evt.CommandSender);
                return;
            }
            foreach(Entity entity in evt.SourceEntities)if(NativeUnitBindings.OwnerOf(entity)==evt.CommandSender)Queue(entity,Entity.None,kind,false,evt.CommandSender);
        }
        static bool IsMovementCommand(string key,bool hasTarget)
        {NativeUnitBindings.CommandRole role=NativeUnitBindings.ResolveRole(NativeRead.Get(typeof(Sim),"Instance"),key,hasTarget);return role==NativeUnitBindings.CommandRole.Stop||role==NativeUnitBindings.CommandRole.Move||role==NativeUnitBindings.CommandRole.AttackTarget||role==NativeUnitBindings.CommandRole.AttackMove;}
        internal static bool IsPlayerActivePassengerAbility(AbilityAttributes ability)
        {return ability!=null&&(ability.TargetingType&AbilityTargetingType.Passive)==0&&(ability.Autocast==null||!ability.Autocast.IsAutocastable);}
        static bool Unit(IEntitySystem system,Entity entity,string type,out object unit,out UnitState state)
        {
            unit=system.GetComponent<object>(entity,NativeUnitBindings.Unit);state=unit==null?null:(UnitState)NativeRead.Call(unit,"GetState");
            return state!=null&&!state.UnitIsDead&&state.TypeID==type&&
                RhodesResearch.IsDeclaredOwner(state.OwnerCommander);
        }
        static bool AnyRosterAlive(IEntitySystem system,Entity entity,out object unit,out UnitState state)
        {
            unit=system.GetComponent<object>(entity,NativeUnitBindings.Unit);state=unit==null?null:(UnitState)NativeRead.Call(unit,"GetState");
            return state!=null&&!state.UnitIsDead&&RhodesOperatorRoster.Contains(state.TypeID)&&
                RhodesResearch.IsDeclaredOwner(state.OwnerCommander);
        }
        static bool OwnedRosterAlive(IEntitySystem system,Entity entity,CommanderID owner,out object unit,out UnitState state)
        {return AnyRosterAlive(system,entity,out unit,out state)&&state.OwnerCommander==owner;}
        static Entry Observe(IEntitySystem system,Entity entity)
        {
            object unit;UnitState state;
            if(AnyRosterAlive(system,entity,out unit,out state))KnownOperators.Add(entity);
            if(!Unit(system,entity,FactionUnits.TiltStrikeKey,out unit,out state))return null;
            Entry entry;if(Entries.TryGetValue(entity,out entry)&&entry.Owner!=state.OwnerCommander){Entries.Remove(entity);entry=null;}
            if(entry==null)
            {if(Entries.Count>=MaximumCarriers)return null;entry=new Entry{Carrier=entity,Owner=state.OwnerCommander};Entries.Add(entity,entry);}
            return entry;
        }
        static Array PassengerSlots(object hangar,string type)
        {
            IDictionary map=NativeRead.Get(hangar,"mEntityOccupancyMap") as IDictionary;
            return map!=null&&map.Contains(type)?map[type] as Array:null;
        }
        static bool AssignedSlot(object hangar,object slot,Entity passenger)
        {
            IDictionary map=NativeRead.Get(hangar,"mEntityToSlotMap") as IDictionary;
            return map!=null&&map.Contains(passenger)&&object.ReferenceEquals(map[passenger],slot);
        }
        static int Occupancy(object slot){return Convert.ToInt32(NativeRead.Get(slot,"Occupancy"));}
        static Entity Stored(object slot){return (Entity)NativeRead.Get(slot,"StoredEntity");}
        static int QueueStatus(object hangar,Entity entity)
        {
            if(entity==Entity.None)return 0;
            object queues=NativeRead.Get(hangar,"DeployQueues");
            return queues==null?0:Convert.ToInt32(NativeRead.Call(queues,"GetStatusOfEntity",entity));
        }
        static void PublishStatus(IEntitySystem system,Entry entry,int tick)
        {
            if(tick<entry.NextStatusTick)return;entry.NextStatusTick=tick+4;
            object hangar=system.GetComponent<object>(entry.Carrier,NativeUnitBindings.Hangar);
            RhodesOperatorSlotStatus[] snapshots=new RhodesOperatorSlotStatus[Capacity];
            int stored=0,ready=0,cooling=0,deployed=0,occupied=0,transferring=0;
            for(int index=0;index<Capacity;index++)
            {
                if(index>=RhodesOperatorRoster.Count){snapshots[index]=RhodesOperatorSlotStatus.Empty(index);continue;}
                string type=RhodesOperatorRoster.At(index).TypeKey;Array slots=hangar==null?null:PassengerSlots(hangar,type);
                if(slots==null||slots.Length==0){snapshots[index]=RhodesOperatorSlotStatus.Empty(index);continue;}
                // One exact typed native bay slot per roster identity. The
                // remaining logical cargo positions stay empty.
                object slot=slots.GetValue(0);int occupancy=Occupancy(slot);Entity passenger=Stored(slot);
                bool hasSlot=occupancy!=0,isReady=false,isCooling=false,isDeployed=false,isTransfer=false,isProducing=hasSlot&&passenger==Entity.None;
                float remaining=0f,progress=0f;if(hasSlot)occupied++;
                object unit;UnitState state;
                if(passenger!=Entity.None&&AssignedSlot(hangar,slot,passenger)&&Unit(system,passenger,type,out unit,out state)&&state.OwnerCommander==entry.Owner)
                {
                    KnownOperators.Add(passenger);
                    bool world=system.HasComponent(passenger,NativeUnitBindings.World),presentation=(bool)NativeRead.Get(unit,"PresentationOnly");
                    isTransfer=entry.Boarding.ContainsKey(passenger)||entry.Unloading.Contains(passenger)||QueueHas(hangar,passenger)||(world&&presentation);
                    if(isTransfer)transferring++;
                    else if(world&&!presentation){isDeployed=true;deployed++;}
                    else if(occupancy==2||occupancy==3)
                    {stored++;isCooling=occupancy==2;isReady=occupancy==3;if(isCooling)cooling++;else ready++;}
                    else if(occupancy==1){isTransfer=true;transferring++;}
                    if(isCooling)
                    {
                        IDictionary coolingMap=NativeRead.Get(hangar,"mDockedEntityCooling") as IDictionary;
                        if(coolingMap!=null&&coolingMap.Contains(passenger))
                        {
                            object data=coolingMap[passenger];float maximum=Fixed64.UnsafeFloatValue((Fixed64)NativeRead.Get(data,"MaxCoolingSeconds"));
                            remaining=Math.Max(0f,Fixed64.UnsafeFloatValue((Fixed64)NativeRead.Get(data,"CoolingSecondsRemaining")));
                            progress=maximum>0f?Math.Max(0f,Math.Min(1f,1f-remaining/maximum)):0f;
                        }
                    }
                    else if(isReady)progress=1f;
                }
                snapshots[index]=new RhodesOperatorSlotStatus(index,passenger,hasSlot?type:string.Empty,hasSlot,isReady,isCooling,isDeployed,isTransfer,isProducing,remaining,progress);
            }
            bool landed=TransferReady(system,entry,tick);
            lock(Gate)
            {
                RhodesOperatorCargoStatus prior;RhodesOperatorManifest priorManifest;
                bool same=Status.TryGetValue(entry.Carrier,out prior)&&prior.Stored==stored&&prior.Ready==ready&&prior.Cooling==cooling&&prior.Deployed==deployed&&
                    prior.Occupied==occupied&&prior.Transferring==transferring&&prior.LandingReady==landed&&prior.LastAction==entry.LastAction&&prior.Warning==entry.Warning;
                if(same&&Manifests.TryGetValue(entry.Carrier,out priorManifest)&&priorManifest.Matches(snapshots,landed))return;
                int revision=++StatusRevision;
                Status[entry.Carrier]=new RhodesOperatorCargoStatus(stored,ready,cooling,deployed,occupied,transferring,landed,entry.LastAction,entry.Warning,revision);
                Manifests[entry.Carrier]=new RhodesOperatorManifest(snapshots,landed,revision);
            }
        }
        static void Snapshot(IEntitySystem system,Entry entry,string reason,Entity eventPassenger,bool immediate,int tick)
        {
            if(SnapshotReports++>=64)return;
            object hangar=system.GetComponent<object>(entry.Carrier,NativeUnitBindings.Hangar);
            int nativeSlots=0;for(int i=0;i<RhodesOperatorRoster.Count;i++){Array typed=hangar==null?null:PassengerSlots(hangar,RhodesOperatorRoster.At(i).TypeKey);if(typed!=null)nativeSlots+=typed.Length;}
            string touchdown;lock(Gate){bool grounded;touchdown=Touchdown.TryGetValue(entry.Carrier,out grounded)?grounded.ToString():"unobserved";}
            object carrierUnit;UnitState carrierState;bool valid=Unit(system,entry.Carrier,FactionUnits.TiltStrikeKey,out carrierUnit,out carrierState);
            StringBuilder text=new StringBuilder();
            text.Append("RHODES_OPERATOR_CARGO_SNAPSHOT reason=").Append(reason).Append("; carrier=").Append(entry.Carrier).Append("; eventPassenger=").Append(eventPassenger)
                .Append("; producedImmediately=").Append(immediate).Append("; tick=").Append(tick).Append("; hangar=").Append(hangar!=null)
                .Append("; slots=").Append(nativeSlots).Append("; touchdown=").Append(touchdown).Append("; landingSince=").Append(entry.HoverSince)
                .Append("; flightPhase=").Append(RhodesTiltrotorFlightControl.GetFlightPhase(entry.Carrier))
                .Append("; landingReady=").Append(TransferReady(system,entry,tick)).Append("; speed=").Append(valid?Fixed64.UnsafeFloatValue(carrierState.StartOfFrameDynamicState.Velocity.Length):float.NaN);
            for(int index=0;index<RhodesOperatorRoster.Count;index++)
            {
                string type=RhodesOperatorRoster.At(index).TypeKey;Array slots=hangar==null?null:PassengerSlots(hangar,type);if(slots==null||slots.Length==0)continue;
                object slot=slots.GetValue(0);Entity passenger=Stored(slot);int occupancy=Occupancy(slot);
                object unit=passenger==Entity.None?null:system.GetComponent<object>(passenger,NativeUnitBindings.Unit);
                UnitState state=unit==null?null:(UnitState)NativeRead.Call(unit,"GetState");
                bool world=passenger!=Entity.None&&system.HasComponent(passenger,NativeUnitBindings.World);
                text.Append("; slot[").Append(index).Append("]={type=").Append(type).Append(",occ=").Append(occupancy).Append(",entity=").Append(passenger)
                    .Append(",assigned=").Append(passenger!=Entity.None&&AssignedSlot(hangar,slot,passenger)).Append(",alive=").Append(state!=null&&!state.UnitIsDead)
                    .Append(",world10=").Append(world).Append(",maneuver12=").Append(passenger!=Entity.None&&system.HasComponent(passenger,NativeUnitBindings.Movement))
                    .Append(",airSortie32=").Append(passenger!=Entity.None&&system.HasComponent(passenger,NativeUnitBindings.AirSortie))
                    .Append(",presentationOnly=").Append(unit!=null&&(bool)NativeRead.Get(unit,"PresentationOnly"))
                    .Append(",docked=").Append(state!=null&&state.IsDocked).Append(",queue=").Append(QueueStatus(hangar,passenger));
                if(unit!=null)text.Append(",startsInHangar=").Append(((UnitAttributes)NativeRead.Get(unit,"Attributes")).StartsInHangar);
                if(world&&state!=null)text.Append(",position=").Append(state.StartOfFrameDynamicState.Position);
                text.Append("}");
            }
            Runtime.Log(text.ToString());
        }
        // Historical HUD field LandingReady now means transfer-ready hover.
        // Simulation state is authoritative: no visual touchdown/ground sample.
        static bool TransferReady(IEntitySystem system,Entry entry,int tick)
        {
            object unit;UnitState state;
            if(!Unit(system,entry.Carrier,FactionUnits.TiltStrikeKey,out unit,out state)||!system.HasComponent(entry.Carrier,NativeUnitBindings.World)||
                (bool)NativeRead.Get(unit,"PresentationOnly")||!RhodesTiltrotorFlightControl.IsOperatorTransferHover(entry.Carrier)||
                Fixed64.UnsafeFloatValue(state.StartOfFrameDynamicState.Velocity.Length)>2f)
            {entry.HoverSince=-1;return false;}
            if(entry.HoverSince<0)entry.HoverSince=tick;
            return tick-entry.HoverSince>=MinimumHoverTicks;
        }
        static bool NearAircraft(UnitState aircraft,UnitState passenger)
        {return RhodesOperatorPickupPolicy.WithinTransferRange(Fixed64.UnsafeFloatValue((aircraft.StartOfFrameDynamicState.Position-passenger.StartOfFrameDynamicState.Position).LengthSquared));}
        static bool GroundFootprintValid(object map,Vector2r point,UnitAttributes attributes)
        {
            if(map==null||attributes==null||attributes.NavMeshAttributes==null)return false;
            UnitClass layer=attributes.NavMeshAttributes.BlockedBy;
            float radius=Math.Max(.5f,Fixed64.UnsafeFloatValue(attributes.NavMeshAttributes.DistanceFromObstacles));
            for(int i=0;i<9;i++)
            {
                float x=i==0?0f:(float)Math.Sin((i-1)*Math.PI/4)*radius;
                float y=i==0?0f:(float)Math.Cos((i-1)*Math.PI/4)*radius;
                Vector2r sample=point+new Vector2r(Fixed64.UnsafeFromFloat(x),Fixed64.UnsafeFromFloat(y));
                if(!(bool)NativeRead.Call(map,"MapPositionValid",sample,layer))return false;
            }
            return true;
        }
        static bool GroundSweepValid(object map,Vector2r from,Vector2r to,UnitAttributes attributes)
        {
            if(attributes==null||attributes.NavMeshAttributes==null)return false;
            Fixed64 clearance=attributes.NavMeshAttributes.DistanceFromObstacles;
            if(clearance<Fixed64.FromRational(1,2))clearance=Fixed64.FromRational(1,2);
            Vector2r midpoint=(from+to)*Fixed64.FromRational(1,2);
            Fixed64 broadRadius=(to-from).Length*Fixed64.FromRational(1,2)+clearance;
            IEnumerator obstacles=(IEnumerator)NativeRead.Call(map,"GetObstacleCircleBroadSearch",midpoint,broadRadius,attributes.NavMeshAttributes.BlockedBy);
            int checks=0;Segment2 segment=new Segment2(from,to);
            try
            {
                while(obstacles.MoveNext())
                {
                    if(++checks>128)return false;
                    if((Fixed64)NativeRead.Call(obstacles.Current,"DistanceToSegment",segment)<=clearance)return false;
                }
            }
            finally{IDisposable disposable=obstacles as IDisposable;if(disposable!=null)disposable.Dispose();}
            return true;
        }
        static MethodInfo UndockPointMethod;
        // Native queue owns the eventual spawn. Validate every possible native
        // slot and its short exit footprint BEFORE reserving an unload request.
        // A failed check leaves the same stored entity, HP and identity intact.
        internal static bool SafeUndockFootprint(object sim,object hangar,Entity carrier,Entity passenger,UnitAttributes attributes)
        {
            if(sim==null||hangar==null||attributes==null)return false;
            object queues=NativeRead.Get(hangar,"DeployQueues");
            object queue=queues==null?null:NativeRead.Call(queues,"GetMatchingUnitClassBay",passenger);
            if(queue==null)return false;
            int count=Convert.ToInt32(NativeRead.Get(queue,"mMaxUndockSlots"));if(count<1||count>3)return false;
            Type type=queue.GetType();
            if(UndockPointMethod==null)UndockPointMethod=type.GetMethod("TryGetUndockingRunwaySpawnPositionAndOrientation",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
            if(UndockPointMethod==null)throw new MissingMethodException("Native cargo runway point function missing");
            object map=NativeRead.Get(sim,"Map");
            float distance=Fixed64.UnsafeFloatValue((Fixed64)NativeRead.Get(queue,"UndockSpeed"))*Fixed64.UnsafeFloatValue((Fixed64)NativeRead.Get(queue,"mTimeTotalLaunchSequenceSeconds"));
            if(distance<0f||distance>50f)return false;
            for(int slot=0;slot<count;slot++)
            {
                object[] args={carrier,queue,slot,Vector2r.Zero,Orientation2.LocalForward};
                if(!(bool)UndockPointMethod.Invoke(null,args))return false;
                Vector2r point=(Vector2r)args[3];Orientation2 orientation=(Orientation2)args[4];
                Vector2r end=point+orientation.Forward*Fixed64.UnsafeFromFloat(distance);
                if(!GroundFootprintValid(map,point,attributes)||!GroundFootprintValid(map,end,attributes)||!GroundSweepValid(map,point,end,attributes))return false;
            }
            return true;
        }
        internal static bool IsOwnedCarrier(IEntitySystem system,Entity carrier)
        {object unit;UnitState state;return Unit(system,carrier,FactionUnits.TiltStrikeKey,out unit,out state);}
        internal static bool IsOwnedCarrier(IEntitySystem system,Entity carrier,CommanderID owner)
        {object unit;UnitState state;return Unit(system,carrier,FactionUnits.TiltStrikeKey,out unit,out state)&&state.OwnerCommander==owner;}
        static bool CarrierOwnsIdentity(IEntitySystem system,Entity carrier,CommanderID owner,string type,out string reason)
        {
            reason=string.Empty;if(!IsOwnedCarrier(system,carrier,owner))return false;
            object hangar=system.GetComponent<object>(carrier,NativeUnitBindings.Hangar);Array slots=hangar==null?null:PassengerSlots(hangar,type);
            if(slots!=null)foreach(object slot in slots)
            {
                if(Occupancy(slot)==0)continue;
                Entity passenger=Stored(slot);object unit;UnitState state;
                if(passenger==Entity.None){reason="同名干员正在编入机舱";return true;}
                if(Unit(system,passenger,type,out unit,out state)&&state.OwnerCommander==owner)
                {KnownOperators.Add(passenger);reason=system.HasComponent(passenger,NativeUnitBindings.World)?"同名干员已部署或正在转运，请召回现有干员":"同名干员已在机舱内；请使用现有干员";return true;}
            }
            object production=system.GetComponent<object>(carrier,NativeUnitBindings.Production);
            if(production!=null&&Convert.ToInt32(NativeRead.Call(production,"GetNumUnitTypeInProduction",type))>0)
            {reason="同名干员已进入生产队列";return true;}
            return false;
        }
        // Called on a production command only. Audits current native entities,
        // never an Update/presentation scan. Native reservation is authoritative.
        internal static bool IdentityExistsNow(object sim,IEntitySystem system,CommanderID owner,string type,bool fullAudit,out string reason)
        {
            reason=string.Empty;if(!RhodesOperatorRoster.Contains(type)){reason="无法识别该干员";return true;}
            if(fullAudit)foreach(object candidate in (IEnumerable)NativeRead.Get(NativeRead.Get(sim,"UnitManager"),"Units"))
            {
                Entity entity=(Entity)NativeRead.Get(candidate,"Entity");object unit;UnitState state;
                if(AnyRosterAlive(system,entity,out unit,out state))
                {KnownOperators.Add(entity);if(state.OwnerCommander==owner&&state.TypeID==type){reason="同名干员已编入；请使用或召回现有干员";return true;}}
                if(CarrierOwnsIdentity(system,entity,owner,type,out reason))return true;
            }
            DeadOperators.Clear();bool living=false;
            foreach(Entity entity in KnownOperators)
            {
                object unit;UnitState state;
                // Auditing A must retain living B: only dead/unregistered
                // entities are removed from this cross-identity registry.
                if(AnyRosterAlive(system,entity,out unit,out state)){if(state.OwnerCommander==owner&&state.TypeID==type)living=true;}
                else DeadOperators.Add(entity);
            }
            foreach(Entity entity in DeadOperators)KnownOperators.Remove(entity);
            if(living){reason="同名干员已编入；请使用或召回现有干员";return true;}
            foreach(Entry entry in Entries.Values)if(CarrierOwnsIdentity(system,entry.Carrier,owner,type,out reason))return true;
            return false;
        }
        internal static void CacheIdentityAvailability(CommanderID owner,string type,bool blocked,string reason)
        {if(!RhodesOperatorRoster.Contains(type)||owner==CommanderID.None)return;lock(Gate)IdentityStates[new CommanderIdentityKey(owner,type)]=new IdentityAvailability{Blocked=blocked,Reason=blocked?reason:string.Empty};}
        static void RefreshIdentityState(object sim,IEntitySystem system,int tick)
        {
            if(tick<NextIdentityTick)return;NextIdentityTick=tick+4;
            foreach(CommanderID owner in RhodesResearch.DeclaredOwners())for(int i=0;i<RhodesOperatorRoster.Count;i++)
            {string type=RhodesOperatorRoster.At(i).TypeKey,reason;bool blocked=IdentityExistsNow(sim,system,owner,type,false,out reason);CacheIdentityAvailability(owner,type,blocked,reason);}
        }
        static bool WorldPassengerExists(object sim,IEntitySystem system,CommanderID owner,string type)
        {
            foreach(object unit in (IEnumerable)NativeRead.Get(NativeRead.Get(sim,"UnitManager"),"Units"))
            {
                Entity entity=(Entity)NativeRead.Get(unit,"Entity");object found;UnitState state;
                if(Unit(system,entity,type,out found,out state)&&state.OwnerCommander==owner&&system.HasComponent(entity,NativeUnitBindings.World))return true;
            }
            return false;
        }
        static bool QueueHas(object hangar,Entity entity)
        {
            if(entity==Entity.None)return false;
            object queues=NativeRead.Get(hangar,"DeployQueues");
            object queue=queues==null?null:NativeRead.Call(queues,"GetMatchingUnitClassBay",entity);
            if(queue==null)return false;
            // Native SlotState zero means BOTH absent and newly idle-queued.
            // Check membership, so a just-created request cannot escape the
            // departure guard before its first native queue tick.
            IDictionary docking=NativeRead.Get(queue,"mDockQueue") as IDictionary;
            return (docking!=null&&docking.Contains(entity))||NativeRead.Call(queue,"GetUndockQueueByEntity",entity)!=null;
        }
        static void UpdateBusy(IEntitySystem system,Entry entry)
        {
            // A distant passenger awaiting an airborne aircraft does not
            // reserve the aircraft's movement. Only actual native docking or
            // an active native cargo animation locks departure.
            bool busy=entry.Unloading.Count>0;
            object hangar=system.GetComponent<object>(entry.Carrier,NativeUnitBindings.Hangar);
            if(!busy)foreach(KeyValuePair<Entity,ReturnOperation> pair in entry.Boarding)
                if(hangar!=null&&QueueHas(hangar,pair.Key)){busy=true;break;}
            lock(Gate){if(busy)Busy.Add(entry.Carrier);else Busy.Remove(entry.Carrier);}
        }
        static object CurrentGoal(IEntitySystem system,Entity passenger)
        {object goals=system.GetComponent<object>(passenger,NativeUnitBindings.Goals);return goals==null?null:NativeRead.Get(goals,"CurrentGoal");}
        static bool HasReturnGoal(object goal,Entity carrier,bool docking)
        {
            if(goal==null||goal.GetType().Name!=(docking?"DockGoal":"FollowGoal"))return false;
            if(docking)return (Entity)NativeRead.Get(goal,"TargetEntity")==carrier;
            // FollowGoal.TargetEntity is a lazy mEntityToFollow cache filled by
            // its first DoPerform. Validate the constructor's immutable target
            // array instead, including before that first native goal tick.
            Entity[] targets=NativeRead.Get(goal,"mTargetEntities") as Entity[];
            return targets!=null&&targets.Length==1&&targets[0]==carrier;
        }
        static bool SetReturnGoal(IEntitySystem system,Entry entry,Entity passenger,UnitState state,bool docking,int tick)
        {
            ReturnOperation operation=new ReturnOperation();
            if(!ReplaceReturnGoal(system,entry,passenger,state,docking,operation,tick))return false;
            entry.Boarding[passenger]=operation;return true;
        }
        static bool ReplaceReturnGoal(IEntitySystem system,Entry entry,Entity passenger,UnitState state,bool docking,ReturnOperation operation,int tick)
        {
            Type goalType=typeof(Sim).Assembly.GetType(docking?"BBI.Game.Simulation.DockGoal":"BBI.Game.Simulation.FollowGoal",true);
            object goal;
            if(docking)
            {
                // A DockGoal is permitted only after this aircraft is actually
                // hovering and this passenger's tracked reservation is verified.
                object unit=system.GetComponent<object>(passenger,NativeUnitBindings.Unit);
                UnitAttributes attributes=(UnitAttributes)NativeRead.Get(unit,"Attributes");
                goal=Activator.CreateInstance(goalType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
                    new object[]{entry.Carrier,attributes.Class},null);
            }
            else
            {
                // Wait at the operator's own ground position. The aircraft
                // comes to this reserved passenger; no ground chase is needed.
                Type collection=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.EntityGoalsCollection",true);
                NativeRead.Call(collection,"ClearGoals",passenger);object movement=system.GetComponent<object>(passenger,NativeUnitBindings.Movement);
                if(movement!=null)NativeRead.Call(movement,"SetToIdle");
                operation.Docking=false;operation.StartedTick=tick;
                operation.AircraftOrderRevision=RhodesTiltrotorFlightControl.OperatorOrderRevision(entry.Carrier);
                return true;
            }
            Type goalsType=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.EntityGoalsCollection",true);
            if(!(bool)NativeRead.Call(goalsType,"AddGoal",passenger,goal,SelectedUnitManager.AddMode.NewSet))return false;
            operation.Docking=docking;operation.StartedTick=tick;
            operation.AircraftOrderRevision=RhodesTiltrotorFlightControl.OperatorOrderRevision(entry.Carrier);
            return true;
        }
        static bool ReservedPassenger(object hangar,Entity passenger,string type)
        {
            Array slots=PassengerSlots(hangar,type);if(slots==null)return false;
            foreach(object slot in slots)if(Occupancy(slot)==1&&Stored(slot)==passenger&&AssignedSlot(hangar,slot,passenger))return true;
            return false;
        }
        static void CancelPassengerOrder(IEntitySystem system,Entity passenger)
        {
            foreach(Entry entry in Entries.Values)
            {
                ReturnOperation operation;if(!entry.Boarding.TryGetValue(passenger,out operation))continue;
                object hangar=system.GetComponent<object>(entry.Carrier,NativeUnitBindings.Hangar);
                if(hangar!=null&&QueueHas(hangar,passenger))continue; // Native transfer already owns progress.
                entry.Boarding.Remove(passenger);Report("BOARDING_CANCELLED",entry.Carrier,"passenger-received-another-order; passenger-kept-in-world");
                if(entry.Boarding.Count==0)RhodesTiltrotorFlightControl.FinishOperatorPickup(entry.Carrier);
            }
        }
        static void Refresh(object sim,IEntitySystem system,Entry entry,int tick)
        {
            bool ready=TransferReady(system,entry,tick);object hangar=system.GetComponent<object>(entry.Carrier,NativeUnitBindings.Hangar);
            if(hangar==null)return;
            object carrierUnit;UnitState carrierState;
            bool carrierInWorld=Unit(system,entry.Carrier,FactionUnits.TiltStrikeKey,out carrierUnit,out carrierState)&&
                system.HasComponent(entry.Carrier,NativeUnitBindings.World)&&!(bool)NativeRead.Get(carrierUnit,"PresentationOnly");
            entry.Completed.Clear();
            foreach(KeyValuePair<Entity,ReturnOperation> pair in entry.Boarding)
            {
                Entity passenger=pair.Key;ReturnOperation operation=pair.Value;object unit;UnitState state;
                if(!OwnedRosterAlive(system,passenger,entry.Owner,out unit,out state))
                {entry.Completed.Add(passenger);Report("PASSENGER_LOST",entry.Carrier,"recall-passenger-no-longer-alive");continue;}
                if(!system.HasComponent(passenger,NativeUnitBindings.World))
                {entry.Completed.Add(passenger);Report("RECALL_COMPLETED",entry.Carrier,"native-dock-finished; servicing30s");continue;}
                object goal=CurrentGoal(system,passenger);bool queued=QueueHas(hangar,passenger);
                bool ownGoal=HasReturnGoal(goal,entry.Carrier,true);
                bool cancelled=!carrierInWorld||!ReservedPassenger(hangar,passenger,state.TypeID)||
                    operation.AircraftOrderRevision!=RhodesTiltrotorFlightControl.OperatorOrderRevision(entry.Carrier)||tick-operation.StartedTick>MaximumPickupTicks;
                if(cancelled&&!queued)
                {
                    if(ownGoal)
                    {NativeRead.Call(typeof(Sim).Assembly.GetType("BBI.Game.Simulation.EntityGoalsCollection",true),"ClearGoals",passenger);object movement=system.GetComponent<object>(passenger,NativeUnitBindings.Movement);if(movement!=null)NativeRead.Call(movement,"SetToIdle");}
                    entry.Completed.Add(passenger);Report("BOARDING_CANCELLED",entry.Carrier,"aircraft-order-or-reservation-changed-or-timeout; passenger-kept-in-world");continue;
                }
                if(operation.Docking)
                {
                    if(!ownGoal&&!queued&&!(bool)NativeRead.Get(unit,"PresentationOnly")&&tick>operation.StartedTick+1)
                    {entry.Completed.Add(passenger);Report("BOARDING_CANCELLED",entry.Carrier,"passenger-received-another-order; reserved-slot-retained");}
                }
                else if(ready&&NearAircraft(carrierState,state)&&GroundFootprintValid(NativeRead.Get(sim,"Map"),state.StartOfFrameDynamicState.Position,(UnitAttributes)NativeRead.Get(unit,"Attributes")))
                {
                    if(ReplaceReturnGoal(system,entry,passenger,state,true,operation,tick))
                        Report("RECALL_DOCKING",entry.Carrier,"passenger="+passenger+"; ownReservedSlot=True; airborneAircraft=True; nativeGroundDockGoal=True");
                }
            }
            foreach(Entity passenger in entry.Completed)entry.Boarding.Remove(passenger);
            entry.Completed.Clear();
            foreach(Entity passenger in entry.Unloading)
            {
                object unit;UnitState state;
                if(!OwnedRosterAlive(system,passenger,entry.Owner,out unit,out state))
                {entry.Completed.Add(passenger);Report("PASSENGER_LOST",entry.Carrier,"unload-passenger-no-longer-alive");}
                else if(system.HasComponent(passenger,NativeUnitBindings.World)&&!(bool)NativeRead.Get(unit,"PresentationOnly")&&!QueueHas(hangar,passenger))
                {entry.Completed.Add(passenger);Report("UNLOAD_COMPLETED",entry.Carrier,"native-undock-finished; sameEntity=True; groundSimulation=True");}
            }
            foreach(Entity passenger in entry.Completed)entry.Unloading.Remove(passenger);
            entry.Completed.Clear();UpdateBusy(system,entry);
            if(entry.PendingUnload)
            {
                if(!carrierInWorld||entry.UnloadRevision!=RhodesTiltrotorFlightControl.OperatorOrderRevision(entry.Carrier)||tick-entry.UnloadTick>80)
                {entry.PendingUnload=false;Report("UNLOAD_CANCELLED",entry.Carrier,"aircraft-order-changed-or-hover-timeout; crew-kept-in-hangar");}
                else if(ready){Entity requested=entry.PendingUnloadPassenger;entry.PendingUnload=false;Unload(sim,system,entry,requested,tick);}
            }
            if(entry.Boarding.Count==0){RhodesTiltrotorFlightControl.FinishOperatorPickup(entry.Carrier);return;}
            if(!carrierInWorld||entry.Unloading.Count>0||tick<entry.NextPickupTick)return;
            // Only explicitly requested, already reserved passengers are visited.
            // No all-unit scan and no aircraft movement during native docking.
            foreach(KeyValuePair<Entity,ReturnOperation> pair in entry.Boarding)
                if(pair.Value.Docking||QueueHas(hangar,pair.Key))return;
            entry.NextPickupTick=tick+4;
            foreach(KeyValuePair<Entity,ReturnOperation> pair in entry.Boarding)
            {
                object unit;UnitState state;if(!OwnedRosterAlive(system,pair.Key,entry.Owner,out unit,out state)||!system.HasComponent(pair.Key,NativeUnitBindings.World))continue;
                bool issued;
                if(RhodesTiltrotorFlightControl.MoveToOperator(sim,entry.Carrier,pair.Key,state.StartOfFrameDynamicState.Position,out issued))
                {if(issued)Report("PICKUP_APPROACH",entry.Carrier,"passenger="+pair.Key+"; nativeMoveGoal=True; passengerWaitsOnGround=True");break;}
            }
        }
        static void RefreshClaims(IEntitySystem system)
        {
            ClaimRemoval.Clear();
            foreach(KeyValuePair<CommanderIdentityKey,Claim> pair in Claims)
            {
                Claim claim=pair.Value;object unit;UnitState state;
                if(!Unit(system,claim.Passenger,pair.Key.Type,out unit,out state)||state.OwnerCommander!=pair.Key.Commander||NativeUnitBindings.OwnerOf(claim.Carrier)!=pair.Key.Commander){ClaimRemoval.Add(pair.Key);continue;}
                if(system.HasComponent(claim.Passenger,NativeUnitBindings.World))continue;
                object hangar=system.GetComponent<object>(claim.Carrier,NativeUnitBindings.Hangar);Array slots=hangar==null?null:PassengerSlots(hangar,pair.Key.Type);
                if(slots!=null)foreach(object slot in slots)
                    if(Stored(slot)==claim.Passenger&&(Occupancy(slot)==2||Occupancy(slot)==3)){ClaimRemoval.Add(pair.Key);break;}
            }
            foreach(CommanderIdentityKey key in ClaimRemoval)Claims.Remove(key);
        }
        static void Unload(object sim,IEntitySystem system,Entry entry,Entity requested,int tick)
        {
            Snapshot(system,entry,"N-request",Entity.None,false,tick);
            object carrierUnit;UnitState carrierState;
            if(!Unit(system,entry.Carrier,FactionUnits.TiltStrikeKey,out carrierUnit,out carrierState)||!system.HasComponent(entry.Carrier,NativeUnitBindings.World)||
                (bool)NativeRead.Get(carrierUnit,"PresentationOnly"))
            {Report("UNLOAD_DENIED",entry.Carrier,"aircraft-not-in-world; crew-kept-in-hangar");return;}
            object hangar=system.GetComponent<object>(entry.Carrier,NativeUnitBindings.Hangar);bool blocked=false,unsafeGround=false;int accepted=0;
            List<Entity> candidates=new List<Entity>(Capacity);
            // Validate before the Stop-class cargo command changes our pickup
            // intent/revision. An empty or cooling cabin must not cancel an
            // already valid recall of operators who are still on the ground.
            for(int identity=0;identity<RhodesOperatorRoster.Count;identity++)
            {
                string type=RhodesOperatorRoster.At(identity).TypeKey;Array slots=hangar==null?null:PassengerSlots(hangar,type);
                if(slots==null||slots.Length==0)continue;object slot=slots.GetValue(0);
                Entity passenger=Stored(slot);object unit;UnitState state;
                if(requested!=Entity.None&&passenger!=requested)continue;
                if(entry.Unloading.Contains(passenger)){accepted++;continue;}
                if(entry.Boarding.ContainsKey(passenger))continue;
                if(Occupancy(slot)!=3||!AssignedSlot(hangar,slot,passenger)||system.HasComponent(passenger,NativeUnitBindings.World)||!Unit(system,passenger,type,out unit,out state)||state.OwnerCommander!=entry.Owner)continue;
                if(Claims.ContainsKey(new CommanderIdentityKey(entry.Owner,type))||WorldPassengerExists(sim,system,entry.Owner,type)){blocked=true;continue;}
                if(!SafeUndockFootprint(sim,hangar,entry.Carrier,passenger,(UnitAttributes)NativeRead.Get(unit,"Attributes")))
                {unsafeGround=true;continue;}
                candidates.Add(passenger);
            }
            if(candidates.Count==0)
            {
                if(accepted>0){Report("UNLOAD_BATCH",entry.Carrier,"count="+accepted+"; nativeQueueOwnsConcurrentProgress=True; requestedAll="+(requested==Entity.None));return;}
                if(unsafeGround){Report("UNLOAD_DENIED",entry.Carrier,"no-safe-ground-footprint; crew-and-reservation-kept-in-hangar");return;}
                if(blocked){Report("UNLOAD_DENIED",entry.Carrier,"same-operator-type-already-fielded-or-queued; other-types-unrestricted");return;}
                Report("UNLOAD_DENIED",entry.Carrier,"no-ready-passenger; reembarked-crew-require-30s-native-servicing");return;
            }
            if(!TransferReady(system,entry,tick))
            {
                if(!RhodesTiltrotorFlightControl.HoldForOperatorTransfer(sim,entry.Carrier))
                {Report("UNLOAD_DENIED",entry.Carrier,"aircraft-not-in-world; crew-kept-in-hangar");return;}
                entry.PendingUnload=true;entry.PendingUnloadPassenger=requested;entry.UnloadTick=tick;
                entry.UnloadRevision=RhodesTiltrotorFlightControl.OperatorOrderRevision(entry.Carrier);
                Report("HOVER_PREPARING",entry.Carrier,"cargo-operation; hovering-before-airborne-deployment=True");return;
            }
            foreach(Entity passenger in candidates)
            {
                object unit;UnitState state;if(!OwnedRosterAlive(system,passenger,entry.Owner,out unit,out state))continue;
                // Native reservation and physical queue still own transfer.
                CommanderIdentityKey identityKey=new CommanderIdentityKey(entry.Owner,state.TypeID);Claims[identityKey]=new Claim{Passenger=passenger,Carrier=entry.Carrier};
                if(!(bool)NativeRead.Call(hangar,"UndockUnit",passenger)){Claims.Remove(identityKey);blocked=true;continue;}
                entry.Unloading.Add(passenger);accepted++;
                Report("UNLOAD_QUEUED",entry.Carrier,"passenger="+passenger+"; sameEntity=True; realNativeHangar=True; type="+state.TypeID);
            }
            UpdateBusy(system,entry);
            if(accepted>0){Report("UNLOAD_BATCH",entry.Carrier,"count="+accepted+"; nativeQueueOwnsConcurrentProgress=True; requestedAll="+(requested==Entity.None));return;}
            Report("UNLOAD_DENIED",entry.Carrier,"same-operator-type-already-fielded-or-queued; other-types-unrestricted");
        }
        static void Recall(IEntitySystem system,Entry entry,Entity requested,int tick)
        {
            Snapshot(system,entry,"B-request",Entity.None,false,tick);
            object carrierUnit;UnitState carrierState;
            if(!Unit(system,entry.Carrier,FactionUnits.TiltStrikeKey,out carrierUnit,out carrierState)||!system.HasComponent(entry.Carrier,NativeUnitBindings.World)||
                (bool)NativeRead.Get(carrierUnit,"PresentationOnly"))
            {Report("RECALL_DENIED",entry.Carrier,"aircraft-not-in-world; passenger-kept-in-world");return;}
            bool landed=TransferReady(system,entry,tick);int accepted=0;entry.PendingUnload=false;
            object hangar=system.GetComponent<object>(entry.Carrier,NativeUnitBindings.Hangar);
            for(int identity=0;identity<RhodesOperatorRoster.Count;identity++)
            {
                string type=RhodesOperatorRoster.At(identity).TypeKey;Array slots=hangar==null?null:PassengerSlots(hangar,type);
                if(slots==null||slots.Length==0)continue;object slot=slots.GetValue(0);Entity passenger=Stored(slot);object unit;UnitState state;
                if(requested!=Entity.None&&passenger!=requested)continue;
                if(entry.Unloading.Contains(passenger))continue;
                ReturnOperation existing;
                if(passenger!=Entity.None&&entry.Boarding.TryGetValue(passenger,out existing)&&
                    (QueueHas(hangar,passenger)||!existing.Docking||HasReturnGoal(CurrentGoal(system,passenger),entry.Carrier,true)))
                {accepted++;continue;}
                if(Occupancy(slot)!=1||!AssignedSlot(hangar,slot,passenger)||!system.HasComponent(passenger,NativeUnitBindings.World)||
                    !Unit(system,passenger,type,out unit,out state)||state.OwnerCommander!=entry.Owner||(bool)NativeRead.Get(unit,"PresentationOnly"))continue;
                // The native map already owns this passenger's RESERVED slot.
                // Never issue unchecked arbitrary pickup: DockGoal destroys a
                // unit if a new reservation fails when it reaches the carrier.
                if(!SetReturnGoal(system,entry,passenger,state,landed&&NearAircraft(carrierState,state),tick))continue;
                accepted++;
                Report(landed?"RECALL_QUEUED":"RECALL_WAITING",entry.Carrier,"passenger="+passenger+"; ownReservedSlot=True; nativeGroundGoal=True; nativeFullHealRearmAfterSeconds=30");
            }
            UpdateBusy(system,entry);
            if(accepted>0){Report(landed?"RECALL_BATCH":"RECALL_WAITING",entry.Carrier,"count="+accepted+"; requestedAll="+(requested==Entity.None)+"; nativeQueueOwnsConcurrentProgress=True");return;}
            Report("RECALL_DENIED",entry.Carrier,"no-live-deployed-passenger-assigned-to-this-aircraft; cross-aircraft-pickup-not-implemented");
        }
        static void ReturnPassenger(IEntitySystem system,Entity passenger,int tick)
        {
            object unit;UnitState state;
            if(!AnyRosterAlive(system,passenger,out unit,out state)||!system.HasComponent(passenger,NativeUnitBindings.World)||(bool)NativeRead.Get(unit,"PresentationOnly"))return;
            KnownOperators.Add(passenger);
            // B on a ground operator resolves its existing native reservation,
            // never an arbitrary nearby aircraft. No slot migration/despawn,
            // copied identity or unchecked DockGoal is introduced here.
            foreach(Entry entry in Entries.Values)
            {
                if(entry.Owner!=state.OwnerCommander)continue;
                object hangar=system.GetComponent<object>(entry.Carrier,NativeUnitBindings.Hangar);
                if(hangar!=null&&ReservedPassenger(hangar,passenger,state.TypeID))
                {Recall(system,entry,passenger,tick);return;}
            }
            Report("RECALL_DENIED",Entity.None,"passenger="+passenger+"; original-aircraft-or-reservation-unavailable; passenger-kept-in-world");
        }
        // Bind before native events. Do not consume requests, recover cargo,
        // publish status or advance boarding until the normal postfix tick.
        internal static void PrepareEvents(object sim)
        {
            if(!Runtime.RhodesCarrier||sim==null)return;
            if(!object.ReferenceEquals(SessionSim,sim))
            {
                int transient=Pending.Count;foreach(Entry prior in Entries.Values)transient+=prior.Boarding.Count+prior.Unloading.Count+(prior.PendingUnload?1:0);
                Reset();SessionSim=sim;
                if(transient>0)Runtime.Log("RHODES_OPERATOR_RECOVERY invalidatedTransientOperations="+transient+"; nativeReservationsRebuilt=True; oldCommandsReplayed=False");
            }
            IEventReceiver receiver=(IEventReceiver)NativeRead.Get(sim,"SimEventSystem");
            if(!object.ReferenceEquals(receiver,Receiver))
            {
                Unsubscribe();Receiver=receiver;
                if(Receiver!=null){Receiver.AddHandler<AbilityDeploymentEvent>(AbilityHandler);Receiver.AddHandler<SimUnitCreatedEvent>(CreationHandler);Receiver.AddHandler<ProductionEvent>(ProductionHandler);}
                RecoveryPending=true;
            }
        }
        public static void SimTick(object sim)
        {
            if(!Runtime.RhodesCarrier||sim==null)return;
            PrepareEvents(sim);
            IEntitySystem system=(IEntitySystem)NativeRead.Get(sim,"EntitySystem");
            RhodesEmbarkedOperatorSkills.BeginTick(sim);
            if(RecoveryPending)
            {
                RecoveryEntities.Clear();
                foreach(object unit in (IEnumerable)NativeRead.Get(NativeRead.Get(sim,"UnitManager"),"Units"))
                {
                    Entity entity=(Entity)NativeRead.Get(unit,"Entity");object found;UnitState state;
                    if(Unit(system,entity,FactionUnits.TiltStrikeKey,out found,out state)||AnyRosterAlive(system,entity,out found,out state))RecoveryEntities.Add(entity);
                }
                RecoveryEntities.Sort(RhodesSimulationEvents.EntityComparer);
                foreach(Entity entity in RecoveryEntities)Observe(system,entity);
                RecoveryEntities.Clear();RecoveryPending=false;
            }
            int tick=checked((int)Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim,"GlobalFrameCount"),"FrameNumber")));
            Scratch.Clear();lock(Gate){while(Pending.Count>0)Scratch.Add(Pending.Dequeue());}
            foreach(Request request in Scratch)
            {
                Entity source=request.Carrier!=Entity.None?request.Carrier:request.Passenger;
                if(request.Sender!=CommanderID.None&&NativeUnitBindings.OwnerOf(source)!=request.Sender)continue;
                if(request.Kind==RequestKind.PassengerCommand){CancelPassengerOrder(system,request.Passenger);continue;}
                if(request.Kind==RequestKind.ReturnPassenger){ReturnPassenger(system,request.Passenger,tick);continue;}
                if(request.Kind==RequestKind.CreatedStored)
                {
                    object passengerUnit;UnitState passengerState;
                    if(!AnyRosterAlive(system,request.Passenger,out passengerUnit,out passengerState))continue;
                    KnownOperators.Add(request.Passenger);
                    // Creation events do not carry the owner hangar. Search
                    // only our bounded carrier entries and four native slots.
                    foreach(Entry known in Entries.Values)
                    {
                        object hangar=system.GetComponent<object>(known.Carrier,NativeUnitBindings.Hangar);
                        if(hangar!=null&&(bool)NativeRead.Call(hangar,"StoresEntity",request.Passenger))
                        {Snapshot(system,known,"UnitCreated-InHangar1",request.Passenger,false,tick);break;}
                    }
                    continue;
                }
                Entry entry=Observe(system,request.Carrier);if(entry==null)continue;
                entry.NextStatusTick=-1;
                if(request.Kind==RequestKind.Unload)Unload(sim,system,entry,request.Passenger,tick);
                else if(request.Kind==RequestKind.Recall)Recall(system,entry,request.Passenger,tick);
                else if(request.Kind==RequestKind.Produced)
                {object passengerUnit;UnitState passengerState;if(AnyRosterAlive(system,request.Passenger,out passengerUnit,out passengerState))KnownOperators.Add(request.Passenger);Snapshot(system,entry,"Production-complete4",request.Passenger,request.Immediate,tick);entry.LastAction="干员已编入机舱";entry.Warning=false;}
                else if(request.Kind==RequestKind.ProduceOrdered)
                {entry.LastAction="正在编入干员";entry.Warning=false;}
                else TransferReady(system,entry,tick);
            }
            // Consume player cancellation before automatic pickup progression.
            Removal.Clear();foreach(KeyValuePair<Entity,Entry> pair in Entries)
            {
                object unit;UnitState state;
                if(!Unit(system,pair.Key,FactionUnits.TiltStrikeKey,out unit,out state)||state.OwnerCommander!=pair.Value.Owner){Removal.Add(pair.Key);continue;}
                Refresh(sim,system,pair.Value,tick);
            }
            foreach(Entity entity in Removal){Entries.Remove(entity);lock(Gate){Busy.Remove(entity);Touchdown.Remove(entity);Status.Remove(entity);Manifests.Remove(entity);}}
            RefreshClaims(system);
            RefreshIdentityState(sim,system,tick);
            // Half-second refresh of at most 64 known carriers x four slots;
            // explicit commands publish immediately. No UnitManager or scene
            // discovery is added; main-thread HUD reads immutable copies.
            foreach(Entry entry in Entries.Values){PublishStatus(system,entry,tick);RhodesEmbarkedOperatorSkills.ObserveCarrier(entry.Carrier);}
            RhodesEmbarkedOperatorSkills.EndTick();
            Scratch.Clear();Removal.Clear();
        }
        static void Report(string action,Entity carrier,string detail)
        {
            Entry entry;if(Entries.TryGetValue(carrier,out entry))
            {
                entry.NextStatusTick=-1;
                entry.Warning=action.EndsWith("DENIED",StringComparison.Ordinal)||action.EndsWith("CANCELLED",StringComparison.Ordinal)||action=="PASSENGER_LOST";
                if(detail.StartsWith("no-safe-ground",StringComparison.Ordinal))entry.LastAction="下方无安全落脚点，请移动旋翼机";
                else if(detail.StartsWith("cargo-operation",StringComparison.Ordinal))entry.LastAction="干员转运中，请稍候";
                else if(detail.StartsWith("same-operator",StringComparison.Ordinal))entry.LastAction="同名干员已部署，需先召回";
                else if(action=="UNLOAD_QUEUED"||action=="UNLOAD_BATCH")entry.LastAction="正在部署全部就绪干员";
                else if(action=="RECALL_QUEUED"||action=="RECALL_BATCH"||action=="RECALL_DOCKING")entry.LastAction="正在接回干员";
                else if(action=="RECALL_WAITING")entry.LastAction="旋翼机正在前往接回干员";
                else if(action=="PICKUP_APPROACH")entry.LastAction="正在接回干员；移动或攻击命令可中止";
                else if(action=="UNLOAD_CANCELLED")entry.LastAction="部署中止；干员仍在机舱";
                else if(action=="UNLOAD_COMPLETED")entry.LastAction="干员已部署；B 召回本机全部干员";
                else if(action=="RECALL_COMPLETED")entry.LastAction="干员已回舱";
                else if(action=="PASSENGER_LOST")entry.LastAction="转运干员已损失";
                else if(detail.StartsWith("no-ready",StringComparison.Ordinal))entry.LastAction="暂无就绪干员；回舱后需整备30秒";
                else if(detail.StartsWith("no-live-deployed",StringComparison.Ordinal))entry.LastAction="本机没有可召回的已部署干员";
                else if(detail.StartsWith("aircraft-not-in-world",StringComparison.Ordinal))entry.LastAction="旋翼机尚未出击，干员继续留在地面";
                else if(action=="BOARDING_CANCELLED")entry.LastAction="召回中止；干员仍在地面";
                else entry.LastAction="转运指令暂不可用，请重试";
            }
            if(Reports++<160)Runtime.Log("RHODES_OPERATOR_"+action+" carrier="+carrier+"; "+detail+"; runtimeAcceptancePending=True");
        }
        static void Unsubscribe()
        {if(Receiver!=null){Receiver.RemoveHandler<AbilityDeploymentEvent>(AbilityHandler);Receiver.RemoveHandler<SimUnitCreatedEvent>(CreationHandler);Receiver.RemoveHandler<ProductionEvent>(ProductionHandler);}Receiver=null;}
        public static void Reset()
        {
            RhodesEmbarkedOperatorSkills.Reset();
            Unsubscribe();SessionSim=null;RecoveryPending=false;Entries.Clear();Claims.Clear();KnownOperators.Clear();DeadOperators.Clear();RecoveryEntities.Clear();Scratch.Clear();Removal.Clear();ClaimRemoval.Clear();Reports=0;SnapshotReports=0;StatusRevision=0;NextIdentityTick=0;
            lock(Gate){Pending.Clear();Touchdown.Clear();Busy.Clear();Status.Clear();Manifests.Clear();IdentityStates.Clear();}
        }

        // Presentation-only settings clone. Classification never changes the
        // operator's Ground class or attaches aircraft sortie simulation.
        public static NGUIIconController PassengerIcon(NGUIIconController original,Component owner)
        {
            if(original==null||owner==null)throw new InvalidOperationException("Native passenger row prefab missing");
            RhodesOperatorCargoIconOwner cache=owner.GetComponent<RhodesOperatorCargoIconOwner>();
            if(cache==null)cache=owner.gameObject.AddComponent<RhodesOperatorCargoIconOwner>();
            return cache.GetOrCreate(original);
        }
    }

    public sealed class RhodesOperatorCargoIconOwner:MonoBehaviour
    {
        NGUIIconController Prefab;
        Texture2D Texture;
        Material Material;
        public NGUIIconController GetOrCreate(NGUIIconController source)
        {
            if(Prefab!=null)return Prefab;
            GameObject node=new GameObject("EF_OperatorCargoTemplates");node.hideFlags=HideFlags.HideAndDontSave;node.transform.parent=transform;node.SetActive(false);
            Prefab=UnityEngine.Object.Instantiate(source) as NGUIIconController;Prefab.name="EF_OperatorCargoSlot";Prefab.transform.parent=node.transform;Prefab.gameObject.SetActive(false);Prefab.gameObject.hideFlags=HideFlags.HideAndDontSave;
            UISprite reference=null;foreach(UISprite sprite in Prefab.GetComponentsInChildren<UISprite>(true))if(sprite.name=="Sprite-Icon")reference=sprite;
            if(reference==null||reference.atlas==null)throw new InvalidOperationException("Native passenger slot Icon sprite missing");
            Texture=new Texture2D(28,28,TextureFormat.RGBA32,false);Texture.name="EF_OperatorCargo_PersonGlyph";Texture.hideFlags=HideFlags.HideAndDontSave;
            Color32[] pixels=new Color32[28*28];Color32 white=new Color32(245,245,238,255);
            for(int y=0;y<28;y++)for(int x=0;x<28;x++)
            {
                bool head=(x-14)*(x-14)+(y-21)*(y-21)<=9;
                bool body=x>=10&&x<=17&&y>=10&&y<=16;
                bool arms=y>=11&&y<=15&&x>=7&&x<=20;
                bool legs=y>=4&&y<=10&&((x>=10&&x<=12)||(x>=15&&x<=17));
                if(head||body||arms||legs)pixels[y*28+x]=white;
            }
            Texture.SetPixels32(pixels);Texture.Apply(false,false);Texture.filterMode=FilterMode.Point;Texture.wrapMode=TextureWrapMode.Clamp;
            Material=new Material(reference.atlas.spriteMaterial);Material.name="EF_OperatorCargoGlyph_Material";Material.hideFlags=HideFlags.HideAndDontSave;Material.mainTexture=Texture;
            GameObject atlasNode=new GameObject("EF_OperatorCargoAtlas");atlasNode.transform.parent=node.transform;atlasNode.hideFlags=HideFlags.HideAndDontSave;
            UIAtlas atlas=atlasNode.AddComponent<UIAtlas>();atlas.spriteMaterial=Material;atlas.pixelSize=reference.atlas.pixelSize;
            atlas.spriteList=new List<UISpriteData>{new UISpriteData{name="EF_OperatorPerson",x=0,y=0,width=28,height=28}};atlas.MarkSpriteListAsChanged();
            foreach(UISprite sprite in Prefab.GetComponentsInChildren<UISprite>(true))
                if(sprite.name=="Sprite-Icon"||sprite.name=="Sprite-Icon-onSortie"){sprite.atlas=atlas;sprite.spriteName="EF_OperatorPerson";sprite.width=28;sprite.height=28;}
            Runtime.Log("RHODES_OPERATOR_PASSENGER_ICON nativeBomberSlotClone=True; codeDrawnPersonGlyph=True; nativeClickOnlySelectsDeployedEntity=True; originalPrefabsAndAtlasesUnchanged=True");return Prefab;
        }
        void OnDestroy(){if(Material!=null)UnityEngine.Object.Destroy(Material);if(Texture!=null)UnityEngine.Object.Destroy(Texture);}
    }
}
