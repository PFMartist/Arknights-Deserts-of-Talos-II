using System;
using System.Collections.Generic;
using System.Reflection;
using BBI.Core.ComponentModel;
using BBI.Core.Events;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Events;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesEmbarkedOperatorSkills
    {
        const int AutoSampleTicks=4,MaximumTargetSamples=RhodesEmbarkedAimPolicy.MaximumSamples;
        const string FlameAutoKey="Ability_EF_U08_LaevatainAuto",GravityAutoKey="Ability_EF_U08_GilbertaAuto",HealingAutoKey="Ability_EF_U08_ArdeliaAuto";
        static readonly List<Entity> AutoCandidates=new List<Entity>(128);
        struct AimCandidate
        {internal Entity Entity;internal Vector2r Expected,Aim;internal int Value;}
        static readonly List<AimCandidate> AimCandidates=new List<AimCandidate>(MaximumTargetSamples);
        static readonly BBI.Core.Events.EventHandler<AbilityDeploymentEvent> AutocastAbilityHandler=OnAutocastAbility;
        static readonly Type InstantGoalType=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.UseWeaponAbilityGoal",true);
        static readonly ConstructorInfo InstantGoalConstructor=InstantGoalType.GetConstructor(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
            new Type[]{typeof(AbilityTargetingType),typeof(Vector2r),typeof(Entity),typeof(AbilityAttributes),typeof(CommanderID),typeof(bool)},null);
        static readonly MethodInfo InstantActivation=InstantGoalType.GetMethod("AttemptInstantActivation",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new Type[]{typeof(Entity)},null);
        internal static string AutoToggleAbilityKey(int kind){return kind==0?FlameAutoKey:kind==1?GravityAutoKey:HealingAutoKey;}
        public static bool IsAutoToggleAbilityKey(string key){return key==FlameAutoKey||key==GravityAutoKey||key==HealingAutoKey;}
        static int AutoTick()
        {
            object frame=Simulation==null?null:NativeRead.Get(Simulation,"GlobalFrameCount");
            return frame==null?0:checked((int)Convert.ToUInt32(NativeRead.Get(frame,"FrameNumber")));
        }
        static void ReleaseEntry(Entry entry)
        {
            foreach(Slot slot in entry.Slots)
            {CancelAutomaticGoal(entry,slot);RhodesEmbarkedCooldown.ReleaseAbility(slot.Proxy);RhodesEmbarkedCooldown.ReleaseWeapon(slot.Operation);slot.Active=null;}
        }
        static void CancelAutomaticGoal(Entry entry,Slot slot)
        {
            object goal=slot.AutoGoal;if(goal==null)return;
            slot.AutoGoal=null;slot.AutoTargetEntity=Entity.None;slot.AutoAim=Vector2r.Zero;
            // A replacement match can reuse Entity IDs. Static native cleanup
            // is valid only while Sim.Instance still denotes this generation.
            try
            {
                if(object.ReferenceEquals(Simulation,NativeRead.Get(typeof(Sim),"Instance")))
                    NativeRead.Call(goal,"OnGoalRemoved",entry.Carrier);
                else if(slot.AutoUnit!=null)NativeRead.Call(goal,"UnsubscribeFromUnitEvents",slot.AutoUnit);
            }
            finally{slot.AutoUnit=null;slot.Active=null;}
        }
        public static void BeforeManualAbility(object request)
        {
            if(request==null||System==null||Entries.Count==0)return;
            string key=NativeRead.Get(request,"AbilityTypeName") as string;
            if(!IsProxyAbilityKey(key))return;
            CommanderID sender=(CommanderID)NativeRead.Get(request,"CommandSender");
            IEnumerable<Entity> sources=NativeRead.Get(request,"SourceEntities") as IEnumerable<Entity>;
            if(sources==null)return;
            foreach(Entity carrier in sources)
            {
                Entry entry;if(!Entries.TryGetValue(carrier,out entry)||entry.Owner!=sender||NativeUnitBindings.OwnerOf(carrier)!=sender)continue;
                try
                {
                    object healing=key==HealingAbilityKey?null:AcceptedHealingOperation(carrier);
                    foreach(Slot slot in entry.Slots)
                    {
                        if(healing!=null&&object.ReferenceEquals(slot.Operation,healing))continue;
                        CancelAutomaticGoal(entry,slot);
                    }
                    // A new healing request replaces the previous acceptance,
                    // including manual instant goals that have no AutoGoal.
                    if(key==HealingAbilityKey)entry.Slots[2].Active=null;
                    entry.ManualUntilTick=AutoTick()+16;
                }
                catch(Exception error){Fault(carrier,error);}
            }
        }
        static void OnAutocastAbility(AbilityDeploymentEvent evt)
        {
            if(evt==null||System==null||evt.ActionResult!=AbilityDeploymentActionResult.DeploymentSucceeded||evt.SourceEntities==null||
                !RhodesResearch.IsDeclaredOwner(evt.CommandSender))return;
            foreach(Entity carrier in evt.SourceEntities)
            {
                Entry entry;if(!Entries.TryGetValue(carrier,out entry)||entry.Owner!=evt.CommandSender||NativeUnitBindings.OwnerOf(carrier)!=evt.CommandSender)continue;
                if(IsAutoToggleAbilityKey(evt.AbilityType))
                {
                    int kind=evt.AbilityType==FlameAutoKey?0:evt.AbilityType==GravityAutoKey?1:2;Entity passenger;string reason;
                    if(!entry.Faulted&&RhodesResearch.HasAdvancedSuppressant(entry.Owner)&&TryReadyPassenger(carrier,kind,out passenger,out reason))
                        entry.Slots[kind].AutoEnabled=!entry.Slots[kind].AutoEnabled;
                    continue;
                }
                if(IsProxyAbilityKey(evt.AbilityType))
                {
                    // Native manual acceptance already owns its target and
                    // weapon flags. An automatic request never replaces it.
                    entry.ManualUntilTick=AutoTick()+16;
                    if(evt.AbilityType!=HealingAbilityKey)
                        RestoreAcceptedHealingTarget(carrier,AcceptedHealingOperation(carrier));
                    continue;
                }
                NativeUnitBindings.CommandRole role=NativeUnitBindings.ResolveRole(Simulation,evt.AbilityType,evt.TargetEntities!=null&&evt.TargetEntities.Length>0);
                if(role!=NativeUnitBindings.CommandRole.Unknown)
                {
                    bool preserveHealing=role==NativeUnitBindings.CommandRole.Stop||role==NativeUnitBindings.CommandRole.Move||
                        role==NativeUnitBindings.CommandRole.AttackMove||role==NativeUnitBindings.CommandRole.AttackTarget;
                    object healing=preserveHealing?AcceptedHealingOperation(carrier):null;
                    if(role==NativeUnitBindings.CommandRole.Stop)RestoreAcceptedHealingTarget(carrier,healing);
                    foreach(Slot slot in entry.Slots)
                    {
                        if(healing!=null&&object.ReferenceEquals(slot.Operation,healing))continue;
                        CancelAutomaticGoal(entry,slot);
                    }
                    entry.ManualUntilTick=AutoTick()+AutoSampleTicks;
                }
            }
        }
        static object CarrierGoal(Entity carrier)
        {object goals=System.GetComponent<object>(carrier,NativeUnitBindings.Goals);return goals==null?null:NativeRead.Get(goals,"CurrentGoal");}
        // Only actually sensed hostile units enter the aim list. A ground
        // marker alone does not imply that its underlying enemy is visible.
        static bool AutoTarget(Entity entity,CommanderID owner,Vector2r origin,out Vector2r position)
        {
            position=Vector2r.Zero;
            if(entity==Entity.None||!System.HasComponent(entity,NativeUnitBindings.World))return false;
            object unit=System.GetComponent<object>(entity,NativeUnitBindings.Unit);
            UnitState state=unit==null?null:NativeRead.Call(unit,"GetState") as UnitState;
            if(state==null||state.UnitIsDead||state.Hitpoints<=0||state.IsDocked||state.IsSuspended||(bool)NativeRead.Get(unit,"PresentationOnly"))return false;
            UnitAttributes attributes=NativeRead.Get(unit,"Attributes") as UnitAttributes;
            if(attributes==null||(attributes.Class&UnitClass.Ground)==0||attributes.NonAutoTargetable)return false;
            if(Convert.ToInt32(NativeRead.Call(NativeRead.Get(Simulation,"InteractionProvider"),"GetRelationship",owner,state.OwnerCommander))!=4)return false;
            object detectable=System.GetComponent<object>(entity,NativeUnitBindings.Detectable);
            if(detectable==null||(DetectionState)NativeRead.Call(detectable,"GetSharedDetectionState",owner,true)!=DetectionState.Sensed)return false;
            position=state.StartOfFrameDynamicState.Position;
            return (position-origin).LengthSquared<=Fixed64.FromInt(RhodesEmbarkedAimPolicy.RangeMetres*RhodesEmbarkedAimPolicy.RangeMetres);
        }
        static int AutoWindupRemaining(object operation,int kind)
        {
            int nominal=RhodesEmbarkedAimPolicy.WindupMilliseconds(kind);if(operation==null)return nominal;
            WeaponFiringState phase=(WeaponFiringState)NativeRead.Get(operation,"WaitState");
            if(phase==WeaponFiringState.WindingUp)return Math.Max(0,(int)NativeRead.Get(operation,"CurrentTimer"));
            if(phase==WeaponFiringState.BurstStart||phase==WeaponFiringState.FireRoundHit||phase==WeaponFiringState.FireRoundMiss)return 0;
            if(phase==WeaponFiringState.WaitingForWeaponStart)return nominal+Math.Min(nominal/2,Math.Max(0,(int)NativeRead.Get(operation,"CurrentTimer")));
            return nominal;
        }
        static bool AimObservation(Entity entity,CommanderID owner,Vector2r origin,int kind,int remainingWindup,out AimCandidate candidate)
        {
            candidate=new AimCandidate();Vector2r position;if(!AutoTarget(entity,owner,origin,out position))return false;
            object unit=System.GetComponent<object>(entity,NativeUnitBindings.Unit);
            UnitState state=NativeRead.Call(unit,"GetState") as UnitState;
            UnitAttributes attributes=NativeRead.Get(unit,"Attributes") as UnitAttributes;
            bool armed=(attributes.Class&(UnitClass.Strikecraft|UnitClass.Armored|UnitClass.Ranged|UnitClass.Carrier))!=0;
            if(!armed&&attributes.WeaponLoadout!=null)
            {
                foreach(WeaponBinding binding in attributes.WeaponLoadout)
                    if(binding!=null&&binding.Weapon!=null&&binding.Weapon.BaseDamagePerRound>Fixed64.Zero){armed=true;break;}
            }
            candidate.Entity=entity;
            candidate.Expected=RhodesEmbarkedAimPolicy.Predict(position,state.StartOfFrameDynamicState.Velocity,remainingWindup);
            candidate.Aim=RhodesEmbarkedAimPolicy.ClampToRange(origin,candidate.Expected);
            candidate.Value=RhodesEmbarkedAimPolicy.Value(attributes.Class,armed,state.InCombat,state.Hitpoints,state.MaxHitpoints,
                (int)Math.Min(100000L,Math.Max(0L,(long)attributes.Resource1Cost+2L*attributes.Resource2Cost)));
            return RhodesEmbarkedAimPolicy.Covers(kind,candidate.Aim,candidate.Expected);
        }
        static bool FindAutoAim(Entry entry,int kind,Vector2r origin,Entity preferred,out Entity selected,out Vector2r position)
        {
            selected=Entity.None;position=Vector2r.Zero;if(kind==2){selected=entry.Carrier;position=origin;return HasDamagedHealingFriend(entry,origin);}
            AimCandidate candidate;int remainingWindup=AutoWindupRemaining(entry.Slots[kind].Operation,kind);
            // An explicit attack order remains first, including low-health
            // targets. Automatic valuation never overrides that player choice.
            if(AimObservation(preferred,entry.Owner,origin,kind,remainingWindup,out candidate))
            {selected=preferred;position=candidate.Aim;return true;}
            AutoCandidates.Clear();AimCandidates.Clear();object map=NativeRead.Get(Simulation,"Map");
            if(map==null)return false;
            try
            {
                NativeRead.Call(NativeRead.Get(map,"SpatialHash"),"FindEntitiesInCircle",origin,Fixed64.FromInt(RhodesEmbarkedAimPolicy.RangeMetres),AutoCandidates,2);
                // Bound sampling after canonical ordering, so spatial-hash
                // bucket order cannot change target choice between peers.
                AutoCandidates.Sort(RhodesSimulationEvents.EntityComparer);
                int inspected=0;
                foreach(Entity entity in AutoCandidates)
                {
                    if(inspected++>=MaximumTargetSamples)break;
                    if(AimObservation(entity,entry.Owner,origin,kind,remainingWindup,out candidate))AimCandidates.Add(candidate);
                }
                int best=RhodesEmbarkedAimPolicy.MinimumAutomaticValue-1;Fixed64 nearest=Fixed64.FromInt(RhodesEmbarkedAimPolicy.RangeMetres*RhodesEmbarkedAimPolicy.RangeMetres);
                foreach(AimCandidate aim in AimCandidates)
                {
                    int score=0,coverage=0;
                    foreach(AimCandidate target in AimCandidates)
                    {
                        if(!RhodesEmbarkedAimPolicy.Covers(kind,aim.Aim,target.Expected))continue;
                        score+=target.Value;coverage++;
                    }
                    score+=Math.Max(0,coverage-1)*20;
                    Fixed64 distance=(aim.Aim-origin).LengthSquared;
                    if(score<best||score==best&&(selected==Entity.None||distance>=nearest))continue;
                    if(score<RhodesEmbarkedAimPolicy.MinimumAutomaticValue)continue;
                    best=score;nearest=distance;selected=aim.Entity;position=aim.Aim;
                }
                return selected!=Entity.None;
            }
            finally{AutoCandidates.Clear();AimCandidates.Clear();}
        }
        // Called only for our retained automatic lease at native weapon entry.
        // The fixed marker never follows motion; validation may cancel before
        // the first shot. No projectile is inspected or moved after release.
        static bool ValidateAutomaticAim(Entry entry,Slot slot,object operation,int kind)
        {
            if(slot.AutoGoal==null)return true;
            if(kind==2){if((bool)NativeRead.Get(slot.AutoGoal,"mFirstRoundFired")||slot.Active!=null&&slot.Active.Fired)return true;UnitState origin=NativeRead.Call(System.GetComponent<object>(entry.Carrier,NativeUnitBindings.Unit),"GetState") as UnitState;if(origin!=null&&HasDamagedHealingFriend(entry,origin.StartOfFrameDynamicState.Position))return true;CancelAutomaticGoal(entry,slot);NativeRead.Set(operation,"FiringCriteriaMet",false);return false;}
            if((bool)NativeRead.Get(slot.AutoGoal,"mFirstRoundFired")||slot.Active!=null&&slot.Active.Fired)return true;
            Entity preferred;Vector2r position;bool valid=(bool)NativeRead.Get(operation,"ForceFire")&&
                RhodesTiltrotorFlightControl.TryAutocastIntent(entry.Carrier,out preferred)&&
                AutoTarget(slot.AutoTargetEntity,entry.Owner,((UnitState)NativeRead.Call(System.GetComponent<object>(entry.Carrier,NativeUnitBindings.Unit),"GetState")).StartOfFrameDynamicState.Position,out position);
            if(valid)
            {
                UnitState target=NativeRead.Call(System.GetComponent<object>(slot.AutoTargetEntity,NativeUnitBindings.Unit),"GetState") as UnitState;
                int remaining=AutoWindupRemaining(operation,kind);
                // Use the real randomized native windup timer. One tick of
                // uncertainty is already well inside the conservative radius.
                Vector2r expected=RhodesEmbarkedAimPolicy.Predict(target.StartOfFrameDynamicState.Position,target.StartOfFrameDynamicState.Velocity,remaining);
                valid=!RhodesEmbarkedAimPolicy.ClearlyMisses(kind,slot.AutoAim,expected,target.StartOfFrameDynamicState.Velocity);
            }
            if(valid)return true;
            CancelAutomaticGoal(entry,slot);NativeRead.Set(operation,"FiringCriteriaMet",false);
            entry.NextAutoTick=Math.Max(entry.NextAutoTick,AutoTick()+AutoSampleTicks);return false;
        }
        static bool PendingSkill(Entry entry)
        {
            for(int kind=0;kind<SkillCount;kind++)
            {
                Slot slot=entry.Slots[kind];
                if(slot.AutoGoal!=null&&(bool)NativeRead.Get(slot.AutoGoal,"mWeaponCycleComplete")){slot.AutoGoal=null;slot.AutoUnit=null;slot.AutoTargetEntity=Entity.None;slot.AutoAim=Vector2r.Zero;}
                if(ContinuesNativeCycle(kind,slot.Active!=null,slot.Active!=null&&slot.Active.Fired,slot.Active!=null&&slot.Active.Passenger==slot.Passenger))continue;
                if(slot.Active!=null||slot.AutoGoal!=null||slot.Operation!=null&&(bool)NativeRead.Get(slot.Operation,"ForceFire"))return true;
            }
            object goal=CarrierGoal(entry.Carrier);
            if(goal==null||goal.GetType()!=InstantGoalType)return false;
            Slot healing=entry.Slots[2];
            return !HealingGoalAllowsOtherAutocasts(NativeRead.Get(goal,"mAbilityWeaponID") as string,
                ContinuesNativeCycle(2,healing.Active!=null,healing.Active!=null&&healing.Active.Fired,healing.Active!=null&&healing.Active.Passenger==healing.Passenger));
        }
        internal static bool HealingGoalAllowsOtherAutocasts(string weaponID,bool acceptedHealing)
        {return acceptedHealing&&weaponID==HealingWeaponID;}
        static void ProcessAutocast(Entry entry,bool researched)
        {
            if(!researched||entry.Faulted||Simulation==null||System==null)return;
            int tick=AutoTick();if(tick<entry.NextAutoTick||tick<entry.ManualUntilTick)return;
            entry.NextAutoTick=tick+AutoSampleTicks;
            Entity preferred;bool combatIntent=RhodesTiltrotorFlightControl.TryAutocastIntent(entry.Carrier,out preferred);if(PendingSkill(entry))return;
            object unit=System.GetComponent<object>(entry.Carrier,NativeUnitBindings.Unit);
            UnitState state=unit==null?null:NativeRead.Call(unit,"GetState") as UnitState;
            if(state==null||state.UnitIsDead||state.OwnerCommander!=entry.Owner)return;
            // Try one native instantaneous request per sample, control first.
            // No queued goal, ClearGoals or artificial path request is created.
            for(int order=0;order<SkillCount;order++)
            {
                int kind=order==0?1:order==1?2:0;if(kind!=2&&!combatIntent)continue;Slot slot=entry.Slots[kind];Entity passenger;string reason;
                if(!slot.AutoEnabled||slot.Proxy==null||slot.Original==null||!OriginalEnabled(slot.Original)||
                    !(bool)NativeRead.Get(slot.Proxy,"IsEnabled")||Remaining(slot.Original)>Fixed64.Zero||Remaining(slot.Proxy)>Fixed64.Zero||
                    !TryReadyPassenger(entry.Carrier,kind,out passenger,out reason)||passenger!=slot.Passenger)continue;
                Vector2r target;Entity selected;if(!FindAutoAim(entry,kind,state.StartOfFrameDynamicState.Position,preferred,out selected,out target))continue;
                AbilityAttributes attributes=NativeRead.Get(slot.Proxy,"AbilityAttributes") as AbilityAttributes;
                if(attributes==null||InstantGoalConstructor==null||InstantActivation==null)throw new InvalidOperationException("Native instant embarked skill ABI differs");
                object goal=InstantGoalConstructor.Invoke(new object[]{kind==2?AbilityTargetingType.Self:AbilityTargetingType.Ground,target,kind==2?entry.Carrier:Entity.None,attributes,entry.Owner,false});
                // AttemptInstantActivation performs native ability validation,
                // range/FOV, LOF, weapon readiness and native force targeting.
                // Failure simply returns: unlike OnPerform, it cannot enqueue
                // NewSet and consume the player's current movement/attack goal.
                if((bool)InstantActivation.Invoke(goal,new object[]{entry.Carrier})){slot.AutoGoal=goal;slot.AutoUnit=unit;slot.AutoTargetEntity=selected;slot.AutoAim=target;}
                return;
            }
        }
    }
}
