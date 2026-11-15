using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BBI.Core.ComponentModel;
using BBI.Game.Data;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Authoritative native-simulation state; presentation receives value copies only.
    public static class RhodesDeploymentState
    {
        sealed class Entry
        {public Entity Entity;public CommanderID Owner;public RhodesDeploymentSnapshot State;public object Hangar;public int CIWSLevel,RepairLevel;public RhodesSharedAircraftLane.State SharedLane;public object CachedDeployQueues;public readonly object[] AircraftBays=new object[3];}
        static readonly object Gate=new object();
        static readonly Dictionary<Entity,Entry> Entries=new Dictionary<Entity,Entry>();
        static readonly List<Entity> Remove=new List<Entity>();
        static readonly List<Entity> Active=new List<Entity>();
        static readonly RhodesWreckTerminalMemo WreckTerminals=new RhodesWreckTerminalMemo();
        static readonly Type Extensions=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.EntityComponentExtensions",true);
        static MethodInfo HangarGetter;
        static volatile bool Suspended;
        // Display/error/unload callers discard only local deployment state.
        // Native research completion and route choices remain authoritative.
        public static void Reset(){lock(Gate){RhodesPowerWeaponGate.Reset();Entries.Clear();Remove.Clear();Active.Clear();WreckTerminals.Clear();Suspended=false;}}
        public static void Suspend(){lock(Gate){Suspended=true;RhodesPowerWeaponGate.Reset();Entries.Clear();Remove.Clear();Active.Clear();WreckTerminals.Clear();}}
        public static bool TryGet(Entity entity,CommanderID owner,out RhodesDeploymentSnapshot state)
        {
            lock(Gate){Entry entry;if(!Suspended&&Entries.TryGetValue(entity,out entry)&&entry.Owner.Equals(owner)){state=entry.State;return true;}}
            state=new RhodesDeploymentSnapshot();return false;
        }
        internal static bool TryGetForWreck(Entity entity,CommanderID owner,out RhodesDeploymentSnapshot state)
        {
            lock(Gate)
            {
                if(!Suspended)
                {
                    Entry entry;
                    if(Entries.TryGetValue(entity,out entry)&&entry.Owner.Equals(owner)){state=entry.State;return true;}
                    if(WreckTerminals.TryGet(entity,owner,out state))return true;
                }
            }
            state=new RhodesDeploymentSnapshot();return false;
        }
        internal static void ClearWreckMemoForRegistry(){lock(Gate)WreckTerminals.Clear();}
        static void RetireEntry(Entity entity)
        {
            Entry entry;
            if(Entries.TryGetValue(entity,out entry))WreckTerminals.Remember(entity,entry.Owner,entry.State);
            RhodesPowerWeaponGate.ReleaseOwner(entity);Entries.Remove(entity);
        }
        static bool IsRhodes(Entity entity)
        {return !entity.Equals(Entity.None)&&(bool)NativeRead.Call(Extensions,"IsValid",entity)&&(string)NativeRead.Call(Extensions,"GetTypeName",entity)==RhodesCarrierUnits.UnitKey;}
        static int Frame()
        {
            object sim=NativeRead.Get(typeof(Sim),"Instance");
            return checked((int)Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim,"GlobalFrameCount"),"FrameNumber")));
        }
        static object GetHangar(Entity entity)
        {
            if(HangarGetter==null)
            {
                Type hangar=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.UnitHangar",true);
                foreach(MethodInfo method in Extensions.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static))
                    if(method.Name=="GetComponent"&&method.IsGenericMethodDefinition&&method.GetParameters().Length==2)
                    {HangarGetter=method.MakeGenericMethod(new Type[]{hangar});break;}
                if(HangarGetter==null)throw new MissingMethodException("Native hangar component getter absent");
            }
            return HangarGetter.Invoke(null,new object[]{entity,23});
        }
        static Entry Update(Entity entity,object knownHangar)
        {
            if(Suspended||!IsRhodes(entity)||(bool)NativeRead.Call(typeof(Sim),"IsEntityDead",entity))return null;
            RhodesPowerWeaponGate.UpdateEligibility(entity);
            CommanderID owner=(CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",entity);
            RhodesResearch.Snapshot research;if(!RhodesResearch.TrySnapshot(owner,out research))return null;
            int branch=research.GunneryLevel>0?2:research.AviationLevel>0?1:0;
            int level=branch==2?research.GunneryLevel:branch==1?research.AviationLevel:0;
            int first=branch==2?research.GunneryCompletedSimTick:research.AviationCompletedSimTick;
            int second=branch==2?research.Gunnery2CompletedSimTick:research.Aviation2CompletedSimTick;
            int tick=Frame();
            lock(Gate)
            {
                if(Suspended)return null;
                Entry entry;
                if(!Entries.TryGetValue(entity,out entry)||!entry.Owner.Equals(owner))
                {
                    // Registered at simulation spawn/first native use, not when FOW reveals it.
                    // Existing completed technology on a new unit is a committed final pose.
                    entry=new Entry{Entity=entity,Owner=owner,State=RhodesDeploymentMachine.Settled(branch,level),Hangar=knownHangar??GetHangar(entity),CIWSLevel=research.CIWSLevel,RepairLevel=research.RepairLevel};
                    Entries[entity]=entry;
                }
                if(knownHangar!=null)entry.Hangar=knownHangar;
                if(entry.Hangar==null)entry.Hangar=GetHangar(entity);
                ObserveSharedLane(entry,tick);
                // R19 aft deployment has its own clock; it never retracts fore
                // hardware, drains aviation lanes, or blocks fore firing.
                RhodesDeploymentMachine.Advance(ref entry.State,branch,level,first,second,tick,true);
                if(research.CIWSLevel>entry.CIWSLevel)entry.State.CIWS2StartTick=research.CIWS2CompletedSimTick;
                if(research.RepairLevel>entry.RepairLevel)entry.State.Repair2StartTick=research.Repair2CompletedSimTick;
                entry.CIWSLevel=research.CIWSLevel;entry.RepairLevel=research.RepairLevel;
                return entry;
            }
        }
        // Compatibility rejection for callers from earlier demonstration builds.
        // No research, buffs, deployment pose or native aircraft queue is changed.
        public static bool TryBeginDemoRollback(object sim,Entity entity,CommanderID requester,out string reason)
        {reason="carrier_route_is_permanent";return false;}

        public static void SimTick(object sim,Entity[] scenarioEntities)
        {
            if(Suspended||!Runtime.RhodesCarrier||sim==null)return;
            if(scenarioEntities!=null)foreach(Entity entity in scenarioEntities)
            {bool known;lock(Gate)known=Entries.ContainsKey(entity);if(!known&&IsRhodes(entity))Update(entity,null);}
            // Only registered Rhodes identities, no Unity scene or full-unit scan.
            lock(Gate)
            {
                Remove.Clear();Active.Clear();
                foreach(KeyValuePair<Entity,Entry> pair in Entries)
                    if(!IsRhodes(pair.Key)||(bool)NativeRead.Call(typeof(Sim),"IsEntityDead",pair.Key))Remove.Add(pair.Key);
                    else Active.Add(pair.Key);
                // Stable same-tick insertion order makes bounded FIFO eviction
                // deterministic; the native dead entity is never modified.
                if(Remove.Count>1)Remove.Sort(RhodesWreckTerminalMemo.EntityOrder);
                foreach(Entity entity in Remove)RetireEntry(entity);
                foreach(Entity entity in Active)Update(entity,null);
            }
        }
        public static void BeforeProcess(Entity owner,object operation)
        {
            if(!Runtime.RhodesCarrier||operation==null||!IsRhodes(owner))return;
            WeaponBinding binding=NativeRead.Get(operation,"WeaponBinding") as WeaponBinding;
            if(binding==null)return;
            bool ciws=RhodesPowerWeaponGate.IsCiws(binding.WeaponID);
            if(!ciws&&binding.WeaponID!=RhodesCarrierUnits.MainForeWeaponID&&binding.WeaponID!=RhodesCarrierUnits.MainAftWeaponID)return;
            if(Suspended){NativeRead.Set(operation,"FiringCriteriaMet",false);return;}
            RhodesPowerWeaponGate.BeforeProcess(owner,operation,ciws);
            if(ciws)return;
            RhodesResearch.RefreshCompletedAtNativeGate();
            Entry entry=Update(owner,null);
            // Lower only; native range, LOS, ammo, targets and reload remain authoritative.
            bool ready=entry!=null&&(binding.WeaponID==RhodesCarrierUnits.MainForeWeaponID?entry.State.ForeReady:entry.State.AftReady);
            if(Suspended||!ready)NativeRead.Set(operation,"FiringCriteriaMet",false);
        }
        public static bool FilterCanStart(bool nativeCan,object hangar,object queue)
        {return FilterQueue(nativeCan,hangar,queue,false);}
        public static bool FilterCanReceive(bool nativeCan,object hangar,object queue)
        {return FilterQueue(nativeCan,hangar,queue,true);}
        static bool FilterQueue(bool nativeCan,object hangar,object queue,bool receiving)
        {
            if(!nativeCan||!Runtime.RhodesCarrier||hangar==null||queue==null)return nativeCan;
            string bayName=(string)NativeRead.Get(queue,"Name");
            bool tilt=bayName==RhodesTiltrotorProfile.BayName;
            bool bomber=bayName==RhodesCarrierUnits.HeavyBomberBayName;
            if(!tilt&&!bomber&&bayName!="EF_Rhodes_Interceptor")return nativeCan;
            Entity owner=(Entity)NativeRead.Get(hangar,"Owner");if(!IsRhodes(owner))return nativeCan;
            if(Suspended)return false;
            RhodesResearch.RefreshCompletedAtNativeGate();Entry entry=Update(owner,hangar);
            if(entry==null)return false;
            if((tilt||bomber)&&!RhodesSharedAircraftLane.CanUse(entry.SharedLane,bomber,receiving,Frame()))return false;
            if(!tilt&&!bomber&&!RhodesSharedAircraftLane.CanUseFighter(entry.SharedLane,receiving,Frame()))return false;
            // During lane draining, new arrivals may finish; new departures wait.
            // During the actual refit, only *starting* new native approaches is deferred.
            if(tilt)
            {
                RhodesResearch.Snapshot facilities;
                if(!RhodesResearch.TrySnapshot(entry.Owner,out facilities)||facilities.TiltCompletedSimTick<0||
                    (Frame()-facilities.TiltCompletedSimTick)*.125f<RhodesTiltrotorProfile.FacilityDeploymentSeconds)return false;
                return !Suspended&&(entry.State.Kind==0||entry.State.Kind==5||receiving&&entry.State.Kind==3);
            }
            return !Suspended&&entry.State.Branch==1&&(entry.State.DeckReady||receiving&&entry.State.Kind==3);
        }
        static void ObserveSharedLane(Entry entry,int tick)
        {
            if(entry.Hangar==null)return;
            object queues=NativeRead.Get(entry.Hangar,"DeployQueues");
            if(!object.ReferenceEquals(queues,entry.CachedDeployQueues))
            {
                for(int i=0;i<entry.AircraftBays.Length;i++)entry.AircraftBays[i]=null;
                foreach(object bay in (IEnumerable)NativeRead.Get(queues,"Bays"))
                {
                    string name=(string)NativeRead.Get(bay,"Name");
                    int index=name==RhodesTiltrotorProfile.BayName?0:name==RhodesCarrierUnits.HeavyBomberBayName?1:name=="EF_Rhodes_Interceptor"?2:-1;
                    if(index<0)continue;if(entry.AircraftBays[index]!=null)throw new InvalidOperationException("Duplicate private aircraft bay "+name);
                    entry.AircraftBays[index]=bay;
                }
                entry.CachedDeployQueues=queues;
            }
            int occupied=0,fighterDockSlots=0,fighterLaunchSlots=0;
            for(int index=0;index<entry.AircraftBays.Length;index++)
            {
                object bay=entry.AircraftBays[index];if(bay==null)continue;
                int dockSlots=NativeDirectionSlots(bay,true),launchSlots=NativeDirectionSlots(bay,false);
                bool dock=dockSlots!=0,launch=launchSlots!=0;
                if(index==2){fighterDockSlots=dockSlots;fighterLaunchSlots=launchSlots;}
                if(dock)occupied|=index==0?RhodesSharedAircraftLane.TiltDock:index==1?RhodesSharedAircraftLane.BomberDock:RhodesSharedAircraftLane.FighterDock;
                if(launch)occupied|=index==0?RhodesSharedAircraftLane.TiltLaunch:index==1?RhodesSharedAircraftLane.BomberLaunch:RhodesSharedAircraftLane.FighterLaunch;
            }
            RhodesSharedAircraftLane.Observe(ref entry.SharedLane,occupied,tick,fighterDockSlots,fighterLaunchSlots,RhodesCarrierUnits.V17AviationConfigured);
        }
        static int NativeDirectionSlots(object bay,bool receiving)
        {
            // SlotData can become Idle before the native final detach event.
            // Keep the assigned entity's Ready/UndockWaiting phases until its
            // real queue removal; unassigned Idle entries never reserve a lane.
            Array slots=(Array)NativeRead.Get(bay,receiving?"mDockSlots":"mUndockSlots");
            if(slots.Length<1||slots.Length>3)throw new InvalidOperationException("Private native aircraft slot count differs");
            int occupied=0;
            for(int index=0;index<slots.Length;index++)
            {object slot=slots.GetValue(index);if(slot!=null&&RhodesSharedAircraftLane.AssignedPhase(Convert.ToInt32(NativeRead.Get(slot,"State"))))occupied|=1<<index;}
            if(receiving)
            {
                IDictionary entries=(IDictionary)NativeRead.Get(bay,"DockingList");
                foreach(object state in entries.Values)if(AssignedQueueState(state))occupied|=NativeEntrySlot(state,slots.Length);
            }
            else foreach(object state in (IEnumerable)NativeRead.Get(bay,"UndockList"))if(AssignedQueueState(state))occupied|=NativeEntrySlot(state,slots.Length);
            return occupied;
        }
        static int NativeEntrySlot(object state,int count)
        {int index=Convert.ToInt32(NativeRead.Get(state,"SlotIndex"));if(index<0||index>=count)throw new InvalidOperationException("Private native aircraft assigned slot differs");return 1<<index;}
        static bool AssignedQueueState(object state)
        {return state!=null&&Convert.ToInt32(NativeRead.Get(state,"SlotIndex"))>=0&&RhodesSharedAircraftLane.AssignedPhase(Convert.ToInt32(NativeRead.Get(state,"SlotState")));}
    }
}
