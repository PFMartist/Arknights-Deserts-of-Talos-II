using System;
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
    public struct RhodesEmbarkedSkillStatus
    {
        public readonly bool Ready;
        public readonly float RemainingSeconds;
        public readonly string Reason;
        public readonly bool AutoResearched,AutoEnabled;
        public RhodesEmbarkedSkillStatus(bool ready,float remaining,string reason){Ready=ready;RemainingSeconds=remaining;Reason=reason;AutoResearched=AutoEnabled=false;}
        public RhodesEmbarkedSkillStatus(bool ready,float remaining,string reason,bool researched,bool automatic)
        {Ready=ready;RemainingSeconds=remaining;Reason=reason;AutoResearched=researched;AutoEnabled=automatic;}
    }

    // Native abilities and weapon operations remain the authorities. This
    // adapter exposes three aircraft proxies, each backed by one real passenger.
    // A completely docked passenger keeps list17 and native cooldown processing;
    // no World component, duplicate actor or independent timer is introduced.
    public static partial class RhodesEmbarkedOperatorSkills
    {
        public const int SkillCount=3;
        public const string HealingAbilityKey="Ability_EF_U08_ArdeliaSkill",HealingWeaponID="EF_U08_ARDELIA_HEALING";
        public const string FlameAbilityKey="Ability_EF_U08_LaevatainSkill",GravityAbilityKey="Ability_EF_U08_GilbertaSkill";
        public const string FlameWeaponID="EF_U08_LAEVAT_FLAME",GravityWeaponID="EF_U08_GILBERTA_GRAVITY";
        const int MaximumCarriers=128;
        sealed class Cast
        {internal Entity Passenger;internal bool Fired;}
        sealed class Slot
        {internal object Proxy,Original,Operation,AutoGoal,AutoUnit;internal Entity Passenger=Entity.None,AutoTargetEntity=Entity.None;internal Vector2r AutoAim;internal Cast Active;internal bool AutoEnabled=true;}
        sealed class Entry
        {internal Entity Carrier;internal CommanderID Owner;internal bool Seen,Faulted;internal int NextAutoTick,ManualUntilTick;internal readonly Slot[] Slots={new Slot(),new Slot(),new Slot()};}
        static readonly int[] BindingIndices={-1,-1,-1};
        static readonly SortedDictionary<Entity,Entry> Entries=new SortedDictionary<Entity,Entry>(RhodesSimulationEvents.EntityComparer);
        static readonly Dictionary<Entity,RhodesEmbarkedSkillStatus[]> Status=new Dictionary<Entity,RhodesEmbarkedSkillStatus[]>();
        static readonly List<Entity> Removed=new List<Entity>();
        static readonly object Gate=new object();
        static readonly Type NativeAbilityHelper=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.AbilityHelper",true);
        static readonly Type NativeWeaponsProcessor=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.WeaponsProcessor",true);
        static readonly BBI.Core.Events.EventHandler<WeaponTriggerEvent> FireHandler=OnWeapon;
        static object Simulation;
        static IEntitySystem System;
        static IEventReceiver Receiver;
        static int Warnings,Reports;

        public static bool IsProxyAbilityKey(string key){return key==FlameAbilityKey||key==GravityAbilityKey||key==HealingAbilityKey;}
        public static bool IsProxyWeaponID(string key){return key==FlameWeaponID||key==GravityWeaponID||key==HealingWeaponID;}
        internal static string ProxyAbilityKey(int kind){return kind==0?FlameAbilityKey:kind==1?GravityAbilityKey:HealingAbilityKey;}
        internal static string ProxyWeaponID(int kind){return kind==0?FlameWeaponID:kind==1?GravityWeaponID:HealingWeaponID;}
        internal static string OriginalAbilityKey(int kind){return kind==0?LaevatainFlameAssets.AbilityKey:kind==1?GilbertaGravityAssets.AbilityKey:ArdeliaHealingAssets.AbilityKey;}
        internal static string OperatorType(int kind){return kind==0?HumanoidUnits.LaevatainKey:kind==1?HumanoidUnits.GilbertaKey:HumanoidUnits.ArdeliaKey;}
        internal static int CooldownSeconds(int kind){return kind==0?LaevatainFlameAssets.CooldownSeconds:kind==1?GilbertaGravityAssets.CooldownSeconds:ArdeliaHealingProfile.CooldownSeconds;}
        internal static void SetBindingIndex(int kind,int index){if(kind<0||kind>=SkillCount||index<1)throw new ArgumentException("Invalid embarked binding");BindingIndices[kind]=index;}
        public static bool TryGetStatus(Entity carrier,string operatorType,out RhodesEmbarkedSkillStatus status)
        {
            int kind=operatorType==HumanoidUnits.LaevatainKey?0:operatorType==HumanoidUnits.GilbertaKey?1:operatorType==HumanoidUnits.ArdeliaKey?2:-1;
            lock(Gate){RhodesEmbarkedSkillStatus[] values;if(kind>=0&&Status.TryGetValue(carrier,out values)){status=values[kind];return true;}}
            status=new RhodesEmbarkedSkillStatus(false,0f,"状态同步中");return false;
        }
        internal static bool IsProxyEvent(WeaponTriggerEvent evt,int kind)
        {
            if(evt==null||evt.WeaponBindingIndex!=BindingIndices[kind])return false;
            object operation=NativeRead.Get(evt,"WeaponFireOp");
            WeaponBinding binding=operation==null?null:NativeRead.Get(operation,"WeaponBinding") as WeaponBinding;
            return binding!=null&&binding.WeaponID==ProxyWeaponID(kind);
        }
        static int Kind(object operation)
        {
            WeaponBinding binding=operation==null?null:NativeRead.Get(operation,"WeaponBinding") as WeaponBinding;
            return binding==null?-1:binding.WeaponID==FlameWeaponID?0:binding.WeaponID==GravityWeaponID?1:binding.WeaponID==HealingWeaponID?2:-1;
        }
        static object Ability(Entity entity,string key){return NativeRead.Call(NativeAbilityHelper,"GetAbilityInstanceForUnit",entity,key);}
        static Fixed64 Remaining(object ability){return ability==null?Fixed64.Zero:(Fixed64)NativeRead.Get(ability,"CooldownTimeRemaining");}
        static void Enabled(object ability,bool value)
        {if(ability!=null&&(bool)NativeRead.Get(ability,"IsEnabled")!=value)NativeRead.Set(ability,"IsEnabled",value);}
        static void Extend(object ability,Fixed64 remaining,Fixed64 duration)
        {
            if(ability==null||remaining<=Remaining(ability))return;
            // StartSharedCooldown is a no-op when already cooling. Extend the
            // exact private native ability instance only, never shorten it.
            if(Remaining(ability)<=Fixed64.Zero)NativeRead.Call(ability,"StartSharedCooldown",remaining);
            else NativeRead.Set(ability,"CooldownTimeRemaining",remaining);
            if(duration>Fixed64.Zero)NativeRead.Set(ability,"CurrentCooldownDuration",duration);
        }
        internal static void SynchronizeNativeCooldown(object original,object proxy)
        {
            if(original==null||proxy==null)return;
            Fixed64 a=Remaining(original),b=Remaining(proxy);
            if(a>b)Extend(proxy,a,(Fixed64)NativeRead.Get(original,"CurrentCooldownDuration"));
            else if(b>a)Extend(original,b,(Fixed64)NativeRead.Get(proxy,"CurrentCooldownDuration"));
        }
        internal static void PrepareEvents(object sim)
        {
            if(!Runtime.RhodesCarrier||sim==null)return;
            if(!object.ReferenceEquals(Simulation,sim)){Reset();Simulation=sim;}
            System=(IEntitySystem)NativeRead.Get(sim,"EntitySystem");
            IEventReceiver current=(IEventReceiver)NativeRead.Get(sim,"SimEventSystem");
            if(!object.ReferenceEquals(current,Receiver))
            {if(Receiver!=null){Receiver.RemoveHandler<WeaponTriggerEvent>(FireHandler);Receiver.RemoveHandler<AbilityDeploymentEvent>(AutocastAbilityHandler);}Receiver=current;if(Receiver!=null){Receiver.AddHandler<WeaponTriggerEvent>(FireHandler);Receiver.AddHandler<AbilityDeploymentEvent>(AutocastAbilityHandler);}}
        }
        internal static void BeginTick(object sim)
        {
            PrepareEvents(sim);
            foreach(Entry entry in Entries.Values)entry.Seen=false;
        }
        internal static void ObserveCarrier(Entity carrier)
        {
            try{ObserveCarrierCore(carrier);}
            catch(Exception error){Fault(carrier,error);}
        }
        static void ObserveCarrierCore(Entity carrier)
        {
            Entry entry;if(!Entries.TryGetValue(carrier,out entry))
            {if(Entries.Count>=MaximumCarriers)return;entry=new Entry{Carrier=carrier};Entries.Add(carrier,entry);}
            entry.Seen=true;
            CommanderID owner=NativeUnitBindings.OwnerOf(carrier);
            if(entry.Owner!=owner){ReleaseEntry(entry);entry.Owner=owner;entry.Faulted=false;entry.NextAutoTick=entry.ManualUntilTick=0;for(int k=0;k<SkillCount;k++)entry.Slots[k]=new Slot();}
            bool researched=RhodesResearch.HasAdvancedSuppressant(owner);
            RhodesEmbarkedSkillStatus[] snapshots=new RhodesEmbarkedSkillStatus[SkillCount];
            for(int kind=0;kind<SkillCount;kind++)
            {
                Slot slot=entry.Slots[kind];string reason="技能不可用";Entity passenger=Entity.None;
                if(slot.Proxy==null)slot.Proxy=Ability(carrier,ProxyAbilityKey(kind));
                RhodesEmbarkedCooldown.EnsureAbility(slot.Proxy,researched,kind);
                bool available=!entry.Faulted&&TryReadyPassenger(carrier,kind,out passenger,out reason);
                if(entry.Faulted){passenger=Entity.None;reason="技能不可用";}
                if(!available){CancelAutomaticGoal(entry,slot);slot.Active=null;Enabled(slot.Proxy,false);snapshots[kind]=new RhodesEmbarkedSkillStatus(false,0f,reason,researched,slot.AutoEnabled);continue;}
                if(slot.Passenger!=passenger){slot.Passenger=passenger;slot.Original=Ability(passenger,OriginalAbilityKey(kind));slot.Active=null;}
                if(slot.Original==null||slot.Proxy==null){Enabled(slot.Proxy,false);snapshots[kind]=new RhodesEmbarkedSkillStatus(false,0f,"状态同步中",researched,slot.AutoEnabled);continue;}
                if(!OriginalEnabled(slot.Original)){CancelAutomaticGoal(entry,slot);slot.Active=null;Enabled(slot.Proxy,false);snapshots[kind]=new RhodesEmbarkedSkillStatus(false,0f,"技能禁用",researched,slot.AutoEnabled);continue;}
                SynchronizeNativeCooldown(slot.Original,slot.Proxy);Enabled(slot.Proxy,true);
                // Native Stop and no-feedback order clearing may remove the
                // Self target before any deployment event or pre-fire callback.
                // Keep the accepted finite cycle on this same native operation.
                if(kind==2)RestoreAcceptedHealingTarget(carrier,AcceptedHealingOperation(carrier));
                float remaining=Math.Max(0f,Fixed64.UnsafeFloatValue(Remaining(slot.Original)));
                snapshots[kind]=new RhodesEmbarkedSkillStatus(remaining<=0f&&slot.Active==null,remaining,remaining>0f?"技能冷却":slot.Active!=null?"施放中":"就绪",researched,slot.AutoEnabled);
            }
            lock(Gate)Status[carrier]=snapshots;
            ProcessAutocast(entry,researched);
        }
        static bool TryReadyPassenger(Entity carrier,int kind,out Entity passenger,out string reason)
        {
            passenger=Entity.None;reason="未搭载";
            return System!=null&&RhodesOperatorTransport.TryReadyEmbarkedPassenger(System,carrier,OperatorType(kind),out passenger,out reason);
        }
        static bool OriginalEnabled(object ability)
        {return ability!=null&&(bool)NativeRead.Get(ability,"IsEnabled")&&!(bool)NativeRead.Get(ability,"RequiredResearchIsLocked");}
        // First-shot cooldown must not cancel the remaining rounds of the
        // accepted native healing cycle. Other proxies remain one-shot casts.
        internal static bool ContinuesNativeCycle(int kind,bool active,bool fired,bool samePassenger)
        {return kind==2&&active&&fired&&samePassenger;}
        internal static object AcceptedHealingOperation(Entity carrier)
        {
            Entry entry;
            if(System==null||!Entries.TryGetValue(carrier,out entry)||entry.Faulted)return null;
            Slot slot=entry.Slots[2];Entity passenger;string reason;
            if(slot.Operation==null||!OriginalEnabled(slot.Original)||
                !TryReadyPassenger(carrier,2,out passenger,out reason)||slot.Passenger!=passenger||
                !ContinuesNativeCycle(2,slot.Active!=null,slot.Active!=null&&slot.Active.Fired,
                    slot.Active!=null&&slot.Active.Passenger==passenger))return null;
            return slot.Operation;
        }
        internal static object MovingHealingOperation(Entity carrier)
        {
            object operation=AcceptedHealingOperation(carrier);
            return operation!=null&&(bool)NativeRead.Get(operation,"ForceFire")?operation:null;
        }
        internal static void RestoreAcceptedHealingTarget(Entity carrier,object operation)
        {
            if(operation==null)return;
            if((bool)NativeRead.Get(operation,"ForceFire")&&
                ((Entity)NativeRead.Get(operation,"CurrentTarget")).Equals(carrier)&&
                Convert.ToInt32(NativeRead.Get(operation,"TargetingPolicy"))==3&&
                !(bool)NativeRead.Get(operation,"DisabledBySharedTurretWeapon"))return;
            // ForceTarget changes only target/policy/FOW. Native burst counts,
            // wait state, elapsed time, shared cooldown and completion stay put.
            NativeRead.Call(NativeWeaponsProcessor,"ForceTarget",operation,carrier,
                (bool)NativeRead.Get(operation,"IgnoreFOW"));
            NativeRead.Set(operation,"ForceFire",true);
            // Retain the accepted cycle through native Stop/target clearing.
            // Current proxies have separate logic turrets; this also clears a
            // stale suppression flag if native order cleanup left one behind.
            NativeRead.Set(operation,"DisabledBySharedTurretWeapon",false);
        }
        internal static void EndTick()
        {
            Removed.Clear();foreach(Entry entry in Entries.Values)if(!entry.Seen)Removed.Add(entry.Carrier);
            foreach(Entity entity in Removed){ReleaseEntry(Entries[entity]);Entries.Remove(entity);lock(Gate)Status.Remove(entity);}
        }
        public static void BeforeProcess(Entity carrier,object operation)
        {
            int kind=Kind(operation);if(kind<0)return;
            try
            {
                Entry entry;Entity passenger;string reason;
                if(System==null||!Entries.TryGetValue(carrier,out entry))
                {NativeRead.Set(operation,"FiringCriteriaMet",false);return;}
                if(entry.Faulted||!TryReadyPassenger(carrier,kind,out passenger,out reason))
                {entry.Slots[kind].Active=null;NativeRead.Set(operation,"FiringCriteriaMet",false);return;}
                Slot slot=entry.Slots[kind];
                if(!ValidateAutomaticAim(entry,slot,operation,kind))return;
                bool researched=RhodesResearch.HasAdvancedSuppressant(NativeUnitBindings.OwnerOf(carrier));
                RhodesEmbarkedCooldown.EnsureAbility(slot.Proxy,researched,kind);
                if(slot.Operation!=null&&!object.ReferenceEquals(slot.Operation,operation))RhodesEmbarkedCooldown.ReleaseWeapon(slot.Operation);
                slot.Operation=operation;RhodesEmbarkedCooldown.EnsureWeapon(operation,researched,kind);
                if(slot.Passenger!=passenger||!OriginalEnabled(slot.Original)){NativeRead.Set(operation,"FiringCriteriaMet",false);slot.Active=null;return;}
                if(!(bool)NativeRead.Get(operation,"ForceFire"))
                {NativeRead.Set(operation,"FiringCriteriaMet",false);slot.Active=null;return;}
                bool continuingHealing=ContinuesNativeCycle(kind,slot.Active!=null,slot.Active!=null&&slot.Active.Fired,slot.Active!=null&&slot.Active.Passenger==passenger);
                if(slot.Active!=null&&(slot.Active.Passenger!=passenger||slot.Active.Fired&&!continuingHealing))
                {NativeRead.Set(operation,"FiringCriteriaMet",false);return;}
                if(!continuingHealing&&Remaining(slot.Original)>Fixed64.Zero)
                {NativeRead.Set(operation,"FiringCriteriaMet",false);slot.Active=null;return;}
                if(slot.Active==null)
                {
                    slot.Active=new Cast{Passenger=passenger};
                }
            }
            catch(Exception error){if(RhodesSkirmishFaction.IsNetworkMatch)throw;NativeRead.Set(operation,"FiringCriteriaMet",false);Fault(carrier,error);}
        }
        static void OnWeapon(WeaponTriggerEvent evt)
        {
            if(evt==null||System==null)return;
            int kind=Kind(NativeRead.Get(evt,"WeaponFireOp"));if(kind<0)return;
            Entry entry;if(!Entries.TryGetValue(evt.Entity,out entry)||entry.Faulted)return;Slot slot=entry.Slots[kind];
            if(evt.Trigger==WeaponFireTrigger.FirstRoundFired)
            {
                try
                {
                    Entity passenger;string reason;
                    if(slot.Active==null||slot.Active.Fired||!TryReadyPassenger(evt.Entity,kind,out passenger,out reason)||slot.Active.Passenger!=passenger)return;
                    slot.Active.Fired=true;
                    AbilityAttributes attributes=NativeRead.Get(slot.Proxy,"AbilityAttributes") as AbilityAttributes;
                    Fixed64 duration=attributes==null?Fixed64.Zero:attributes.CooldownTimeSecs;
                    if(duration<=Fixed64.Zero)duration=Fixed64.FromInt(RhodesEmbarkedCooldown.CooldownSeconds(kind,RhodesResearch.HasAdvancedSuppressant(entry.Owner)));
                    NativeRead.Call(slot.Original,"StartSharedCooldown",duration);
                    RhodesTiltrotorConcealment.ObserveEmbarkedSkillFire(evt.Entity);
                    // Leave proxy StartCooldown to its native UseWeaponGoal:
                    // preempting it would skip native warmed/queued bookkeeping.
                    if(Reports++<12)Runtime.Log("RHODES_EMBARKED_SKILL_FIRED carrier="+evt.Entity+"; passenger="+passenger+"; skill="+OriginalAbilityKey(kind)+"; nativeDamage=True; sharedPassengerCooldown=True; worldDeployment=False; visualAcceptancePending=True");
                }
                catch(Exception error){Fault(evt.Entity,error);}
            }
            else if(evt.Trigger==WeaponFireTrigger.EnteringIdle||evt.Trigger==WeaponFireTrigger.WindDownStart)slot.Active=null;
        }
        static void Fault(Entity carrier,Exception error)
        {
            // A local fallback would make peers run different skill rules.
            // Let the native scheduler surface a network authority failure.
            if(RhodesSkirmishFaction.IsNetworkMatch)throw new InvalidOperationException("Rhodes embarked skill authority failed",error);
            Entry entry;if(Entries.TryGetValue(carrier,out entry)){entry.Faulted=true;foreach(Slot slot in entry.Slots){try{CancelAutomaticGoal(entry,slot);}catch(Exception){}slot.Active=null;Enabled(slot.Proxy,false);}}
            if(Warnings++<3)Runtime.Log("WARNING RHODES_EMBARKED_SKILLS_DISABLED carrier="+carrier+"; "+error.Message+"; originalGroundSkillsUnchanged=True");
        }
        public static void Reset()
        {
            if(Receiver!=null){Receiver.RemoveHandler<WeaponTriggerEvent>(FireHandler);Receiver.RemoveHandler<AbilityDeploymentEvent>(AutocastAbilityHandler);}Receiver=null;
            foreach(Entry entry in Entries.Values)ReleaseEntry(entry);
            RhodesEmbarkedCooldown.Reset();Simulation=null;System=null;
            Entries.Clear();Removed.Clear();AutoCandidates.Clear();AimCandidates.Clear();lock(Gate)Status.Clear();Warnings=Reports=0;
        }
    }
}
