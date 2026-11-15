using System;
using System.Collections;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Game.Commands;
using BBI.Game.Data;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Only the known Rhodes entity is sampled. Effects, inventory allocation,
    // charging and refunds remain owned by the native PowerShuntProcessor.
    public static class RhodesPowerGate
    {
        sealed class Entry
        {
            internal readonly RhodesPowerTiming Timing=new RhodesPowerTiming();
            internal CommanderID Owner;internal int Budget;internal bool Reported,InvalidReported,Failed;
            internal object VerifiedPower;
        }
        static readonly object Gate=new object();
        static readonly Dictionary<Entity,Entry> Entries=new Dictionary<Entity,Entry>();
        static readonly List<Entity> Removal=new List<Entity>();
        static Type Extensions,Processor;
        static object Simulation;
        static uint LastFrame;
        static int Reports,Errors,CreationReports;
        static bool Disabled;

        static void Metadata()
        {
            if(Extensions==null)Extensions=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.EntityComponentExtensions",true);
            if(Processor==null)Processor=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.PowerShuntProcessor",true);
        }
        static bool Scope(Entity entity,out CommanderID owner)
        {
            owner=CommanderID.None;
            if(!Runtime.SkirmishMode||!RhodesSkirmishFaction.IsMatchActive)return false;
            Metadata();
            if(!(bool)NativeRead.Call(Extensions,"IsValid",entity)||
                (string)NativeRead.Call(Extensions,"GetTypeName",entity)!=RhodesCarrierUnits.UnitKey)return false;
            owner=(CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",entity);
            return RhodesResearch.IsDeclaredOwner(owner)&&!(bool)NativeRead.Call(typeof(Sim),"IsEntityDead",entity);
        }
        static void SimulationAt(object sim,uint frame)
        {
            if(!object.ReferenceEquals(Simulation,sim)){Entries.Clear();Simulation=sim;Reports=Errors=0;Disabled=false;}
            LastFrame=frame;
        }
        static Entry EntryFor(Entity entity,CommanderID owner)
        {
            Entry entry;
            if(!Entries.TryGetValue(entity,out entry)||!entry.Owner.Equals(owner))
            {entry=new Entry{Owner=owner,Budget=RhodesPowerProfile.InitialPower};Entries[entity]=entry;}
            return entry;
        }
        static int TotalPower(object power)
        {
            int total=Convert.ToInt32(NativeRead.Get(NativeRead.Get(power,"ReservePowerPool"),"CurrentAmount"));
            if(total<0)throw new InvalidOperationException("Native reserve power cannot be negative");
            IDictionary systems=NativeRead.Get(power,"PowerSystems") as IDictionary;
            if(systems==null)throw new InvalidOperationException("Native power system dictionary missing");
            foreach(DictionaryEntry item in systems)
                total=checked(total+ReservedPower(item.Value,item.Key));
            return total;
        }
        static void VerifyPowerLayout(object power,Entry entry)
        {
            if(object.ReferenceEquals(entry.VerifiedPower,power))return;
            PowerShuntAttributes attributes=NativeRead.Get(power,"Attributes") as PowerShuntAttributes;
            if(attributes==null||attributes.PowerSystems==null||attributes.PowerSystems.Length!=RhodesPowerProfile.SystemCount)
                throw new InvalidOperationException("This power layout requires a new Hotfix44 match");
            object produced=NativeRead.Get(power,"ProducedHeatSystem"),overheating=NativeRead.Get(power,"OverheatingPool");
            if(produced==null||overheating==null||
                Convert.ToInt32(NativeRead.Get(produced,"Capacity"))!=RhodesThermalProfile.HeatCapacity||
                Convert.ToInt32(NativeRead.Get(overheating,"Capacity"))!=RhodesThermalProfile.OverheatingCapacity)
                throw new InvalidOperationException("Saved thermal inventory is incompatible with Hotfix44; start a new match");
            foreach(PowerSystemAttributes system in attributes.PowerSystems)
            {
                if(system==null||system.PowerLevels==null||system.PowerLevels.Length!=RhodesPowerProfile.MaximumLevel+1)
                    throw new InvalidOperationException("Saved power level layout is incompatible with Hotfix44");
                for(int level=0;level<system.PowerLevels.Length;level++)
                    if(system.PowerLevels[level]==null||system.PowerLevels[level].PowerUnitsRequired!=RhodesPowerProfile.ExtraPowerCost(level)||
                        system.PowerLevels[level].HeatPointsProvided!=RhodesThermalProfile.HeatPointsForLevel(level))
                        throw new InvalidOperationException("Saved power/heat costs are incompatible with Hotfix44");
            }
            entry.VerifiedPower=power;
        }
        static int ReservedPower(object system,object systemType)
        {
            if(system==null)throw new InvalidOperationException("Native power system missing; system="+systemType);
            int reserved=Convert.ToInt32(NativeRead.Get(system,"CurrentHighestReservedPowerLevelIndex"));
            Array levels=NativeRead.Get(system,"PowerLevels") as Array;
            // Both native constructors start at -1. An untouched zero-level
            // system (including an empty placeholder) has reserved no points.
            // Do not turn that sentinel into a level, refund, or power grant.
            if(reserved==-1)return 0;
            if(reserved< -1||levels==null||reserved>=levels.Length)
                throw new InvalidOperationException("Native power reservation index differs; system="+systemType+
                    "; reserved="+reserved+"; levelCount="+(levels==null?"null":levels.Length.ToString()));
            int total=0;
            for(int i=1;i<=reserved;i++)
                total=checked(total+NativeLevelCost(NativeRead.Get(levels.GetValue(i),"Attributes")));
            return total;
        }
        static string NativeStateSummary(object power)
        {
            // One snapshot per carrier for both valid and invalid inventories.
            // This is read-only: an invalid budget must never trigger a refill.
            System.Text.StringBuilder text=new System.Text.StringBuilder();
            try{
            text.Append("; reserve=").Append(NativeRead.Get(NativeRead.Get(power,"ReservePowerPool"),"CurrentAmount"));
            text.Append("; reserveCapacity=").Append(NativeRead.Get(NativeRead.Get(power,"ReservePowerPool"),"Capacity"));
            text.Append("; producedHeat=").Append(NativeRead.Get(NativeRead.Get(power,"ProducedHeatSystem"),"CurrentAmount"))
                .Append("; totalHeat=").Append(NativeRead.Get(power,"TotalHeat"))
                .Append("; ambientHeat=").Append(NativeRead.Get(power,"AmbientHeatPoints"))
                .Append("; overheatingPool=").Append(NativeRead.Get(NativeRead.Get(power,"OverheatingPool"),"CurrentAmount"));
            PowerShuntAttributes attributes=NativeRead.Get(power,"Attributes") as PowerShuntAttributes;
            AppendAttributes(text,"runtimeAttributes",attributes);
            PowerShuntAttributes prepared=RhodesCarrierUnits.PreparedPowerAttributes;
            AppendAttributes(text,"preparedAttributes",prepared);
            PowerShuntAttributes[] registered=RhodesCarrierUnits.RegisteredPowerAttributes;
            text.Append("; registeredPowerAttributeCount=").Append(registered==null?-1:registered.Length);
            if(registered!=null)for(int i=0;i<registered.Length;i++)AppendAttributes(text,"registeredAttributes["+i+"]",registered[i]);
            object root=attributes,next;
            for(int depth=0;depth<8&&root!=null&&NativeRead.TryGet(root,"Base",out next)&&next!=null&&!object.ReferenceEquals(root,next);depth++)root=next;
            text.Append("; runtimeAttributesRootIsPrepared=").Append(object.ReferenceEquals(root,prepared));
            IDictionary systems=(IDictionary)NativeRead.Get(power,"PowerSystems");
            text.Append("; nativeSystemCount=").Append(systems.Count);
            foreach(DictionaryEntry item in systems)
            {
                object system=item.Value;Array levels=NativeRead.Get(system,"PowerLevels") as Array;
                text.Append("; system[").Append(item.Key).Append("]={target:").Append(NativeRead.Get(system,"CurrentTargetPowerLevelIndex"))
                    .Append(",effect:").Append(NativeRead.Get(system,"CurrentPowerLevelInEffectIndex"))
                    .Append(",reserved:").Append(NativeRead.Get(system,"CurrentHighestReservedPowerLevelIndex"))
                    .Append(",max:").Append(NativeRead.Get(system,"MaxPowerLevelIndex"))
                    .Append(",levels:").Append(levels==null?"null":levels.Length.ToString()).Append(",costs:");
                if(levels!=null)for(int level=0;level<levels.Length;level++)
                {if(level>0)text.Append(',');object value=levels.GetValue(level);text.Append(value==null?"null":NativeLevelCost(NativeRead.Get(value,"Attributes")).ToString());}
                text.Append("}");
            }
            }catch(Exception error){text.Append("; snapshotError=").Append(error.GetType().Name).Append(':').Append(error.Message);}
            return text.ToString();
        }
        static void AppendAttributes(System.Text.StringBuilder text,string label,PowerShuntAttributes attributes)
        {
            text.Append("; ").Append(label).Append("={type:").Append(attributes==null?"null":attributes.GetType().FullName);
            if(attributes!=null)
            {
                InventoryAttributes reserve=attributes.ReservePowerPool;
                text.Append(",name:").Append(attributes.Name).Append(",reserveStart:").Append(reserve==null?-1:reserve.StartingAmount)
                    .Append(",reserveCapacity:").Append(reserve==null?-1:reserve.Capacity).Append(",systems:");
                PowerSystemAttributes[] systems=attributes.PowerSystems;
                if(systems==null)text.Append("null");
                else foreach(PowerSystemAttributes system in systems)
                {
                    if(system==null){text.Append("[null]");continue;}
                    text.Append('[').Append(system.PowerSystemType).Append(":start=").Append(system.StartingPowerLevelIndex)
                        .Append(",max=").Append(system.StartingMaxPowerLevelIndex).Append(",costs=");
                    if(system.PowerLevels==null)text.Append("null");
                    else for(int i=0;i<system.PowerLevels.Length;i++){if(i>0)text.Append(',');text.Append(system.PowerLevels[i]==null?-1:system.PowerLevels[i].PowerUnitsRequired);}
                    text.Append(']');
                }
            }
            text.Append('}');
        }
        static int RequiredReserve(object system,int current,int desired)
        {
            Array levels=(Array)NativeRead.Get(system,"PowerLevels");int amount=0;
            for(int i=current+1;i<=desired;i++)
            {
                object level=levels.GetValue(i);
                // Native IncreaseTarget retains reservations while a level is
                // draining; cancelling that drain does not spend the point twice.
                if(Convert.ToInt32(NativeRead.Get(level,"CurrentChargingState"))==2)continue;
                amount+=NativeLevelCost(NativeRead.Get(level,"Attributes"));
            }
            return amount;
        }
        static int NativeLevelCost(object attributes)
        {
            // PowerLevelAttributesData implements this property explicitly.
            // Reflection on a short concrete property name cannot read it.
            PowerLevelAttributes level=attributes as PowerLevelAttributes;
            if(level==null)throw new InvalidOperationException("Native power-level attributes are missing or incompatible");
            int cost=level.PowerUnitsRequired;
            if(cost<0)throw new InvalidOperationException("Native power-level cost cannot be negative");
            return cost;
        }

        // Read-only native factory checkpoint. The preloader leaves the native
        // PowerShunt.Create return object and all constructor work unchanged.
        // Capture this before later startup, ownership, commands or save load
        // can change the component; no inventory or system fields are written.
        public static void AfterNativeCreate(object power,Entity entity,object constructorAttributes)
        {
            try
            {
                CommanderID owner;if(power==null||!Scope(entity,out owner))return;
                lock(Gate)
                {
                    if(CreationReports++>=16)return;
                    System.Text.StringBuilder source=new System.Text.StringBuilder();
                    AppendAttributes(source,"constructorAttributes",constructorAttributes as PowerShuntAttributes);
                    Runtime.Log("RHODES_POWER_NATIVE_CREATED entity="+entity+"; owner="+owner+"; totalPower="+TotalPower(power)+
                        "; readOnlyFactoryCheckpoint=True"+source+NativeStateSummary(power));
                }
            }
            catch(Exception error)
            {
                lock(Gate){if(CreationReports++<16)Runtime.Log("WARNING RHODES_POWER_CREATION_SNAPSHOT "+error.GetType().Name+": "+error.Message);}
            }
        }

        // Typed ABI at the native per-entity command checkpoint. Native
        // validation, loops, return value and non-Rhodes entities stay intact.
        public static bool BeforePowerChange(object command,Entity entity,object power,object system,SimFrameNumber frame)
        {
            bool ours=false;
            try
            {
                CommanderID owner;if(!Scope(entity,out owner))return true;ours=true;
                lock(Gate){if(Disabled)return false;}
                int action=Convert.ToInt32(NativeRead.Get(command,"Action"));
                if((bool)NativeRead.Get(command,"InstantCharge")||action<0||action>2)return false;
                int amount=Convert.ToInt32(NativeRead.Get(command,"Amount"));if(amount<0)return false;
                int current=Convert.ToInt32(NativeRead.Get(system,"CurrentTargetPowerLevelIndex"));
                int maximum=Math.Min(RhodesPowerProfile.MaximumLevel,Convert.ToInt32(NativeRead.Get(system,"MaxPowerLevelIndex")));
                long request=action==0?amount:action==1?(long)current+amount:(long)current-amount;
                int desired=(int)Math.Max(0,Math.Min(maximum,request));
                if(desired==current)return true;
                if(desired>current&&RequiredReserve(system,current,desired)>
                    Convert.ToInt32(NativeRead.Get(NativeRead.Get(power,"ReservePowerPool"),"CurrentAmount")))return false;
                object sim=NativeRead.Get(typeof(Sim),"Instance");
                lock(Gate)
                {
                    SimulationAt(sim,frame.FrameNumber);Entry entry=EntryFor(entity,owner);
                    if(entry.Failed)return false;
                    VerifyPowerLayout(power,entry);
                    int total=TotalPower(power);
                    if(RhodesPowerBudget.GrantRequired(total,RhodesResearch.CompletedReactorLevel(owner))<0)
                    {entry.Budget=0;return false;}
                    entry.Budget=total;
                    bool accepted=entry.Timing.Accept(frame.FrameNumber,true);
                    if(Reports++<16)Runtime.Log("RHODES_POWER_REQUEST entity="+entity+"; accepted="+accepted+"; system="+NativeRead.Get(command,"PowerSystemType")+
                        "; target="+current+"->"+desired+"; frame="+frame.FrameNumber+"; nativeCharging=True; modAllocationCooldown=False");
                    return accepted;
                }
            }
            catch(Exception error)
            {
                // A local fallback would let peers apply different power
                // commands. Abort through the native lockstep error path.
                if(RhodesSkirmishFaction.IsNetworkMatch)throw;
                if(Errors++<4)Runtime.Log("WARNING RHODES_POWER_GATE "+error.GetType().Name+": "+error.Message+"; scopedRequestRejected="+ours);
                return !ours;
            }
        }

        public static void SimTick(object sim,Entity[] knownCarriers)
        {
            if(!Runtime.SkirmishMode||sim==null)return;
            lock(Gate){if(Disabled)return;}
            try{TickNative(sim,knownCarriers);}
            catch(Exception error)
            {
                if(RhodesSkirmishFaction.IsNetworkMatch)throw;
                lock(Gate)
                {
                    Disabled=true;
                    foreach(Entry entry in Entries.Values)entry.Budget=0;
                }
                // Do not let this optional controller stop the whole mod's
                // model/deployment driver. Reject all future power edits until
                // the session resets, and report the fault exactly once.
                Runtime.Log("ERROR RHODES_POWER_CONTROLLER_DISABLED "+error.GetType().Name+": "+error.Message+
                    "; furtherPowerRequestsRejected=True; sessionResetRequired=True; otherServicesContinue=True");
            }
        }
        static void TickNative(object sim,Entity[] knownCarriers)
        {
            if(!Runtime.SkirmishMode||sim==null)return;Metadata();
            uint frame=Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim,"GlobalFrameCount"),"FrameNumber"));
            IEntitySystem entities=(IEntitySystem)NativeRead.Get(sim,"EntitySystem");
            lock(Gate)
            {
                SimulationAt(sim,frame);Removal.Clear();
                foreach(KeyValuePair<Entity,Entry> item in Entries)
                {CommanderID owner;if(!Scope(item.Key,out owner)||!owner.Equals(item.Value.Owner))Removal.Add(item.Key);}
                foreach(Entity entity in Removal)Entries.Remove(entity);
                if(knownCarriers==null)return;
                foreach(Entity entity in knownCarriers)
                {
                    CommanderID owner;if(!Scope(entity,out owner)||!entities.HasComponent(entity,NativeUnitBindings.PowerShunt))continue;
                    object power=entities.GetComponent<object>(entity,NativeUnitBindings.PowerShunt);if(power==null)continue;
                    Entry entry=EntryFor(entity,owner);if(!entry.Failed)UpdatePowerEntry(entity,owner,power,entry,frame);
                }
            }
        }
        // Scope and native metadata are validated by TickNative. A malformed
        // component belongs to this carrier; its failure cannot disable another
        // player's reactor. Global ABI failures retain SimTick's outer guard.
        static void UpdatePowerEntry(Entity entity,CommanderID owner,object power,Entry entry,uint frame)
        {
            try
            {
                VerifyPowerLayout(power,entry);
                int total=TotalPower(power);
                int reactorLevel=RhodesResearch.CompletedReactorLevel(owner);
                int grant=RhodesPowerBudget.GrantRequired(total,reactorLevel);
                if(grant<0)
                {
                    entry.Budget=0;
                    if(!entry.InvalidReported)
                    {entry.InvalidReported=true;Runtime.Log("WARNING RHODES_POWER_BUDGET entity="+entity+"; invalidTotal="+total+"; authorizedTotal="+RhodesPowerProfile.CapacityForResearchLevel(reactorLevel)+"; scopeDisabled=True; frame="+frame+NativeStateSummary(power));}
                    return;
                }
                if(grant>0)
                {
                    NativeRead.Call(Processor,"IncreaseCurrentReservePower",entity,power,grant);
                    total=TotalPower(power);
                    if(total!=RhodesPowerProfile.CapacityForResearchLevel(reactorLevel))throw new InvalidOperationException("Reactor expansion failed its exact native reserve grant");
                    Runtime.Log("RHODES_REACTOR_CAPACITY_GRANTED entity="+entity+"; totalPower="+total+"; reactorLevel="+reactorLevel+"; exactNativeReserveGrant="+grant+"; saveReloadNoDuplicate=True");
                }
                entry.Budget=total;
                if(!entry.Reported)
                {
                    entry.Reported=true;
                    Runtime.Log("RHODES_POWER_RUNTIME_READY entity="+entity+"; totalPower="+total+"; typedNativeAttributes=True; nativeAllocator=True"+NativeStateSummary(power));
                }
            }
            catch(Exception error)
            {
                if(RhodesSkirmishFaction.IsNetworkMatch)throw;
                entry.Failed=true;entry.Budget=0;
                Runtime.Log("ERROR RHODES_POWER_ENTITY_DISABLED entity="+entity+"; owner="+owner+"; "+error.GetType().Name+": "+error.Message+
                    "; entityPowerRequestsRejected=True; otherCarriersContinue=True; sessionResetRequired=True");
            }
        }
        public static bool TryGetTiming(Entity entity,SimStateFrame frame,out float adjustmentSeconds,out float lockSeconds)
        {
            lock(Gate)
            {
                adjustmentSeconds=lockSeconds=0f;Entry entry;
                if(Disabled||!Entries.TryGetValue(entity,out entry)||entry.Failed)return false;
                entry.Timing.Remaining(frame==null?LastFrame:frame.FrameNumber.FrameNumber,out adjustmentSeconds,out lockSeconds);return true;
            }
        }
        public static bool TryGetBudget(Entity entity,out int budget)
        {
            lock(Gate)
            {
                Entry entry;budget=0;if(Disabled||!Entries.TryGetValue(entity,out entry)||entry.Failed)return false;
                budget=entry.Budget;return budget>=RhodesPowerProfile.InitialPower&&budget<=RhodesPowerProfile.MaximumPower;
            }
        }
        public static void Reset(){lock(Gate){Entries.Clear();Removal.Clear();Simulation=null;LastFrame=0;Reports=Errors=CreationReports=0;Disabled=false;}}
    }
}
