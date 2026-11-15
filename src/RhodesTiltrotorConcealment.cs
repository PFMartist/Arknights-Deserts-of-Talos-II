using System;
using System.Collections;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Core.Events;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Events;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Native detection leases only. A skirmish lease belongs to one actual
    // (covert entity, hostile observer) pair; owner/allies are never concealed.
    // The laboratory keeps its historical Commander1 -> Commander2 scope.
    public static class RhodesTiltrotorConcealment
    {
        sealed class ObserverLease
        {internal CommanderID Observer;internal object Lease;internal bool HaveState,Concealed;}
        sealed class Entry
        {
            internal Entity Entity;internal CommanderID Owner;internal string UnitKey;internal object Detectable;
            internal int RevealedUntil;internal bool Seen;internal Vector2r Position;
            internal readonly Dictionary<CommanderID,ObserverLease> Observers=new Dictionary<CommanderID,ObserverLease>();
        }
        sealed class ObserverBatch
        {internal bool Active;internal readonly List<Vector2r> Positions=new List<Vector2r>();}
        static readonly SortedDictionary<Entity,Entry> Entries=new SortedDictionary<Entity,Entry>(RhodesSimulationEvents.EntityComparer);
        static readonly Dictionary<CommanderID,ObserverBatch> Observers=new Dictionary<CommanderID,ObserverBatch>();
        static readonly List<Entity> Removal=new List<Entity>();
        static readonly List<CommanderID> ObserverRemoval=new List<CommanderID>();
        static readonly HashSet<Entity> Fired=new HashSet<Entity>();
        static readonly List<Entity> FireScratch=new List<Entity>();
        static readonly object FireGate=new object(),ScopeGate=new object();
        static readonly BBI.Core.Events.EventHandler<WeaponTriggerEvent> FireHandler=OnFire;
        static IEventReceiver Receiver;
        static object Session;
        static readonly Dictionary<CommanderID,HashSet<CommanderID>> OwnerObservers=new Dictionary<CommanderID,HashSet<CommanderID>>();
        static int NextTick,Reports;
        static bool CovertUnit(string key){return key==FactionUnits.TiltStrikeKey||RhodesOperatorRoster.Contains(key);}
        static void OnFire(WeaponTriggerEvent evt)
        {
            if(evt!=null&&(evt.Trigger==WeaponFireTrigger.FireRoundHit||evt.Trigger==WeaponFireTrigger.FireRoundMiss))
                lock(FireGate)Fired.Add(evt.Entity);
        }
        internal static void ObserveEmbarkedSkillFire(Entity entity)
        {lock(FireGate)Fired.Add(entity);}
        static bool OwnHiddenLease(ObserverLease lease,object current)
        {
            // Native ForceState methods can reuse the same state object. A
            // permanent/extended external Hidden write must not be mistaken
            // for our original one-second timed lease just by reference.
            return current!=null&&object.ReferenceEquals(current,lease.Lease)&&
                (DetectionState)NativeRead.Get(current,"State")==DetectionState.Hidden&&
                Convert.ToInt32(NativeRead.Get(current,"Lifetime"))==0&&
                (int)NativeRead.Get(current,"TimeLeftMS")<=RhodesTiltrotorConcealmentPolicy.LeaseMilliseconds;
        }
        static bool ActiveForeignLease(ObserverLease lease,object current)
        {return current!=null&&!OwnHiddenLease(lease,current)&&(Convert.ToInt32(NativeRead.Get(current,"Lifetime"))==1||(int)NativeRead.Get(current,"TimeLeftMS")>0);}
        static IDictionary Forced(object detectable)
        {return detectable==null?null:NativeRead.Get(detectable,"ForcedDetectionState") as IDictionary;}
        static void Release(Entry entry,ObserverLease lease)
        {
            if(entry.Detectable!=null&&lease.Lease!=null)
            {
                IDictionary forced=Forced(entry.Detectable);
                object current=forced!=null&&forced.Contains(lease.Observer)?forced[lease.Observer]:null;
                if(OwnHiddenLease(lease,current))NativeRead.Call(entry.Detectable,"RemoveForcedState",lease.Observer);
            }
            lease.Lease=null;
        }
        static void ReleaseAll(Entry entry)
        {foreach(ObserverLease lease in entry.Observers.Values)Release(entry,lease);entry.Observers.Clear();}
        static void Drop(Entity entity)
        {Entry entry;if(Entries.TryGetValue(entity,out entry)){ReleaseAll(entry);Entries.Remove(entity);}}
        static Entry Track(Entity entity,UnitState state,object detectable)
        {
            Entry entry;
            if(!Entries.TryGetValue(entity,out entry))
            {entry=new Entry{Entity=entity,UnitKey=state.TypeID};Entries.Add(entity,entry);}
            if(!object.ReferenceEquals(entry.Detectable,detectable)||entry.Owner!=state.OwnerCommander)
            {ReleaseAll(entry);entry.Detectable=detectable;entry.RevealedUntil=0;}
            entry.Owner=state.OwnerCommander;entry.UnitKey=state.TypeID;return entry;
        }
        static bool ManagedOwner(bool skirmish,CommanderID owner)
        {return RhodesConcealmentScopePolicy.ManagedOwner(skirmish,owner,RhodesResearch.IsDeclaredOwner(owner));}
        static void RefreshObservers(object sim,bool skirmish)
        {
            foreach(ObserverBatch batch in Observers.Values){batch.Active=false;batch.Positions.Clear();}
            foreach(HashSet<CommanderID> scope in OwnerObservers.Values)scope.Clear();
            CommanderID[] owners=skirmish?RhodesResearch.DeclaredOwners():new CommanderID[]{RhodesConcealmentScopePolicy.LabOwner};
            object manager=NativeRead.Get(sim,"CommanderManager");
            IEnumerable ids=manager==null?null:NativeRead.Get(manager,"CommanderIDs") as IEnumerable;
            ICommanderInteractionProvider interaction=NativeRead.Get(sim,"InteractionProvider") as ICommanderInteractionProvider;
            foreach(CommanderID owner in owners)
            {
                HashSet<CommanderID> scope;if(!OwnerObservers.TryGetValue(owner,out scope)){scope=new HashSet<CommanderID>();OwnerObservers.Add(owner,scope);}
                if(!skirmish){scope.Add(RhodesConcealmentScopePolicy.LabObserver);ActivateObserver(RhodesConcealmentScopePolicy.LabObserver);continue;}
                if(ids==null||interaction==null)continue;
                foreach(CommanderID observer in ids)
                    if(RhodesConcealmentScopePolicy.Observe(true,owner,observer,(int)interaction.GetRelationship(owner,observer)))
                    {scope.Add(observer);ActivateObserver(observer);}
            }
        }
        static void ActivateObserver(CommanderID observer)
        {
            ObserverBatch batch;if(!Observers.TryGetValue(observer,out batch))
            {batch=new ObserverBatch();Observers.Add(observer,batch);}batch.Active=true;
        }
        public static void SimTick(object sim)
        {
            if(!Runtime.RhodesCarrier||sim==null)return;
            lock(ScopeGate)Tick(sim);
        }
        internal static void PrepareEvents(object sim)
        {
            if(!Runtime.RhodesCarrier||sim==null)return;
            lock(ScopeGate)PrepareEventsCore(sim);
        }
        static void PrepareEventsCore(object sim)
        {
            if(!object.ReferenceEquals(Session,sim)){ResetCore();Session=sim;}
            IEventReceiver current=(IEventReceiver)NativeRead.Get(sim,"SimEventSystem");
            if(!object.ReferenceEquals(current,Receiver))
            {
                if(Receiver!=null)Receiver.RemoveHandler<WeaponTriggerEvent>(FireHandler);
                Receiver=current;if(Receiver!=null)Receiver.AddHandler<WeaponTriggerEvent>(FireHandler);
            }
        }
        static void Tick(object sim)
        {
            PrepareEventsCore(sim);
            bool skirmish=Runtime.SkirmishMode;
            int tick=checked((int)Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim,"GlobalFrameCount"),"FrameNumber")));
            IEntitySystem system=(IEntitySystem)NativeRead.Get(sim,"EntitySystem");
            FireScratch.Clear();lock(FireGate){foreach(Entity entity in Fired)FireScratch.Add(entity);Fired.Clear();}
            FireScratch.Sort(RhodesSimulationEvents.EntityComparer);
            foreach(Entity entity in FireScratch)
            {
                if(!system.HasComponent(entity,NativeUnitBindings.World)){Drop(entity);continue;}
                object firingUnit=system.GetComponent<object>(entity,NativeUnitBindings.Unit);
                UnitState state=firingUnit==null?null:(UnitState)NativeRead.Call(firingUnit,"GetState");
                if(state==null||state.UnitIsDead||!ManagedOwner(skirmish,state.OwnerCommander)||!CovertUnit(state.TypeID)||
                    (bool)NativeRead.Get(firingUnit,"PresentationOnly")){Drop(entity);continue;}
                Entry entry=Track(entity,state,system.GetComponent<object>(entity,NativeUnitBindings.Detectable));
                entry.RevealedUntil=tick+RhodesTiltrotorConcealmentPolicy.FireRevealTicks;
                // Stop only our own leases immediately for every current
                // observer; native firing/mission leases retain their identity.
                foreach(ObserverLease lease in entry.Observers.Values)Release(entry,lease);
            }
            if(tick<NextTick)return;NextTick=tick+RhodesTiltrotorConcealmentPolicy.SampleTicks;
            RefreshObservers(sim,skirmish);
            foreach(Entry entry in Entries.Values)entry.Seen=false;
            // Exactly one existing UnitManager pass per four-tick sample.
            // Cargo without Position never enters either distance set.
            foreach(object unit in (IEnumerable)NativeRead.Get(NativeRead.Get(sim,"UnitManager"),"Units"))
            {
                Entity entity=(Entity)NativeRead.Get(unit,"Entity");
                if(!system.HasComponent(entity,NativeUnitBindings.World))continue;
                UnitState state=(UnitState)NativeRead.Call(unit,"GetState");
                if(state==null||state.UnitIsDead||(bool)NativeRead.Get(unit,"PresentationOnly"))continue;
                CommanderID unitOwner=state.OwnerCommander;ObserverBatch batch;
                if(Observers.TryGetValue(unitOwner,out batch)&&batch.Active)batch.Positions.Add(state.StartOfFrameDynamicState.Position);
                if(!ManagedOwner(skirmish,unitOwner)||!CovertUnit(state.TypeID))continue;
                Entry entry=Track(entity,state,system.GetComponent<object>(entity,NativeUnitBindings.Detectable));
                entry.Seen=true;entry.Position=state.StartOfFrameDynamicState.Position;
            }
            Removal.Clear();foreach(Entry entry in Entries.Values)if(!entry.Seen)Removal.Add(entry.Entity);
            foreach(Entity entity in Removal)Drop(entity);
            foreach(Entry entry in Entries.Values)
            {
                HashSet<CommanderID> scope;if(!OwnerObservers.TryGetValue(entry.Owner,out scope)){ReleaseAll(entry);continue;}
                ObserverRemoval.Clear();
                foreach(KeyValuePair<CommanderID,ObserverLease> item in entry.Observers)
                {
                    ObserverBatch batch;
                    if(!Observers.TryGetValue(item.Key,out batch)||!batch.Active||!scope.Contains(item.Key))
                    {Release(entry,item.Value);ObserverRemoval.Add(item.Key);}
                }
                foreach(CommanderID observer in ObserverRemoval)entry.Observers.Remove(observer);
                IDictionary forced=Forced(entry.Detectable);
                if(forced==null){ReleaseAll(entry);continue;}
                foreach(KeyValuePair<CommanderID,ObserverBatch> item in Observers)
                {
                    if(!item.Value.Active||!scope.Contains(item.Key))continue;
                    ObserverLease lease;if(!entry.Observers.TryGetValue(item.Key,out lease))
                    {lease=new ObserverLease{Observer=item.Key};entry.Observers.Add(item.Key,lease);}
                    float nearest=float.PositiveInfinity;
                    foreach(Vector2r enemy in item.Value.Positions)
                    {Vector2r delta=enemy-entry.Position;float x=Fixed64.UnsafeFloatValue(delta.X),y=Fixed64.UnsafeFloatValue(delta.Y);nearest=Math.Min(nearest,x*x+y*y);}
                    object existing=forced.Contains(item.Key)?forced[item.Key]:null;
                    bool foreign=ActiveForeignLease(lease,existing);
                    bool concealed=RhodesTiltrotorConcealmentPolicy.IsConcealed(tick,entry.RevealedUntil,nearest)&&!foreign;
                    if(concealed)
                    {
                        NativeRead.Call(entry.Detectable,"ForceStateForMilliseconds",item.Key,DetectionState.Hidden,RhodesTiltrotorConcealmentPolicy.LeaseMilliseconds);
                        lease.Lease=forced[item.Key];
                    }
                    else Release(entry,lease);
                    if(!lease.HaveState||lease.Concealed!=concealed)
                    {
                        lease.HaveState=true;lease.Concealed=concealed;
                        if(Reports++<48)Runtime.Log("RHODES_COVERT_CONCEALMENT entity="+entry.Entity+"; unit="+entry.UnitKey+
                            "; owner="+entry.Owner+"; observer="+item.Key+"; concealed="+concealed+
                            "; reason="+(tick<entry.RevealedUntil?"recent-fire":nearest<=32400f?"proximity":foreign?"foreign-native-lease":"normal-concealment")+
                            "; nativeDetectionLease=True; ownerAndAlliesUnchanged=True; fireRevealSeconds=6; proximityBreakMetres=180; observerScope="+
                            (skirmish?"actual-hostile-commanders":"manual-trial-Commander2")+"; runtimeAcceptancePending=True");
                    }
                }
            }
            ObserverRemoval.Clear();foreach(KeyValuePair<CommanderID,ObserverBatch> item in Observers)if(!item.Value.Active)ObserverRemoval.Add(item.Key);
            foreach(CommanderID observer in ObserverRemoval)Observers.Remove(observer);
        }
        static void ResetCore()
        {
            if(Receiver!=null)Receiver.RemoveHandler<WeaponTriggerEvent>(FireHandler);Receiver=null;
            // Reset is also called after simulation teardown on the main thread.
            // Never mutate old simulation components there. Our native leases
            // remain timed at the original 1000ms; no lease reference or reveal
            // clock can cross into a new session/entity generation.
            Entries.Clear();Observers.Clear();Removal.Clear();ObserverRemoval.Clear();FireScratch.Clear();
            lock(FireGate)Fired.Clear();NextTick=0;Reports=0;Session=null;OwnerObservers.Clear();
        }
        public static void Reset(){lock(ScopeGate)ResetCore();}
    }
}
