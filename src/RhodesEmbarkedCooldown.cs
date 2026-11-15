using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BBI.Game.Data;
using BBI.Game.Simulation;
using BBI.Core.Utility.FixedPoint;

namespace EndfieldDoK.MapUnitLab
{
    // Exact per-instance view of native/buffed data; no registry object is mutated.
    internal sealed class RhodesEmbarkedAbilityAttributes : AbilityAttributes
    {
        internal readonly AbilityAttributes Source;
        string LocalName;
        internal RhodesEmbarkedAbilityAttributes(AbilityAttributes source){if(source==null)throw new ArgumentNullException("source");Source=source;}
        public string Name {get{return LocalName??Source.Name;}}
        public void InitializeName(string name){LocalName=name;}
        public BBI.Game.Data.AbilityClass AbilityType {get{return Source.AbilityType;}}
        public BBI.Game.Data.AbilityTargetingType TargetingType {get{return Source.TargetingType;}}
        public BBI.Game.Data.AbilityTargetAlignment TargetAlignment {get{return Source.TargetAlignment;}}
        public BBI.Game.Data.UnitClass AbilityMapTargetLayers {get{return Source.AbilityMapTargetLayers;}}
        public BBI.Game.Data.GroundAutoTargetAlignmentType GroundAutoTargetAlignment {get{return Source.GroundAutoTargetAlignment;}}
        public BBI.Core.Utility.FixedPoint.Fixed64 EdgeOfTargetShapeMinDistance {get{return Source.EdgeOfTargetShapeMinDistance;}}
        public BBI.Game.Data.TargetHighlightingAttributes TargetHighlighting {get{return Source.TargetHighlighting;}}
        public System.Boolean CasterMovesToTarget {get{return Source.CasterMovesToTarget;}}
        public BBI.Game.Data.AbilityGroupActivationType GroupActivationType {get{return Source.GroupActivationType;}}
        public BBI.Game.Data.AbilityRemovedInMode StartsRemovedInGameMode {get{return Source.StartsRemovedInGameMode;}}
        public BBI.Core.Utility.FixedPoint.Fixed64 CooldownTimeSecs {get{return Source.CooldownTimeSecs*Fixed64.FromInt(4)/Fixed64.FromInt(5);}}
        public BBI.Core.Utility.FixedPoint.Fixed64 WarmupTimeSecs {get{return Source.WarmupTimeSecs;}}
        public System.Int32 SharedCooldownChannel {get{return Source.SharedCooldownChannel;}}
        public BBI.Game.Data.SkipCastOnArrivalConditions SkipCastOnArrivalConditions {get{return Source.SkipCastOnArrivalConditions;}}
        public System.Boolean IsToggleable {get{return Source.IsToggleable;}}
        public System.Boolean CastOnDeath {get{return Source.CastOnDeath;}}
        public BBI.Game.Data.CostAttributes Cost {get{return Source.Cost;}}
        public BBI.Game.Data.ChargeAttributes Charges {get{return Source.Charges;}}
        public BBI.Game.Data.AutocastAttributes Autocast {get{return Source.Autocast;}}
        public BBI.Game.Data.ProduceUnitAttributes ProduceUnit {get{return Source.ProduceUnit;}}
        public BBI.Game.Data.UseWeaponAttributes UseWeapon {get{return Source.UseWeapon;}}
        public BBI.Game.Data.ChangeContextAttributes ChangeContext {get{return Source.ChangeContext;}}
        public BBI.Game.Data.AirSortieAttributes AirSortie {get{return Source.AirSortie;}}
        public BBI.Game.Data.SalvageAttributes Salvage {get{return Source.Salvage;}}
        public BBI.Game.Data.ApplyStatusEffectAttributes ApplyStatusEffect {get{return Source.ApplyStatusEffect;}}
        public BBI.Game.Data.RepairAttributes Repair {get{return Source.Repair;}}
        public BBI.Game.Data.SelfDestructAttributes SelfDestruct {get{return Source.SelfDestruct;}}
        public BBI.Game.Data.ModifyInventoryAttributes ModifyInventory {get{return Source.ModifyInventory;}}
        public BBI.Game.Data.ResearchDependenciesAttributes ResearchDependencies {get{return Source.ResearchDependencies;}}
        public BBI.Game.Data.ActivationDependenciesAttributes ActivationDependencies {get{return Source.ActivationDependencies;}}
        public BBI.Game.Data.AutoToggleOffConditionsAttributes AutoToggleOffConditions {get{return Source.AutoToggleOffConditions;}}
        public BBI.Game.Data.ChainCastingAttributes ChainCasting {get{return Source.ChainCasting;}}
    }
    internal sealed class RhodesEmbarkedWeaponAttributes : WeaponAttributes
    {
        readonly WeaponAttributes Source;
        string LocalName;
        internal RhodesEmbarkedWeaponAttributes(WeaponAttributes source)
        {if(source==null)throw new ArgumentNullException("source");Source=source;}
        public string Name {get{return LocalName??Source.Name;}}
        // INamed is inherited by WeaponAttributes. A local metadata rename must
        // never rename the shared/native underlying weapon definition.
        public void InitializeName(string name){LocalName=name;}
        public System.Boolean ExcludeFromAutoTargetAcquisition {get{return Source.ExcludeFromAutoTargetAcquisition;}}
        public System.Boolean ExcludeFromAutoFire {get{return Source.ExcludeFromAutoFire;}}
        public System.Boolean ExcludeFromHeightAdvantage {get{return Source.ExcludeFromHeightAdvantage;}}
        public BBI.Game.Data.DamageType DamageType {get{return Source.DamageType;}}
        public System.Boolean IsTracer {get{return Source.IsTracer;}}
        public BBI.Core.Utility.FixedPoint.Fixed64 TracerSpeed {get{return Source.TracerSpeed;}}
        public BBI.Core.Utility.FixedPoint.Fixed64 TracerLength {get{return Source.TracerLength;}}
        public BBI.Core.Utility.FixedPoint.Fixed64 BaseDamagePerRound {get{return Source.BaseDamagePerRound;}}
        public BBI.Core.Utility.FixedPoint.Fixed64 BaseWreckDamagePerRound {get{return Source.BaseWreckDamagePerRound;}}
        public System.Single FiringRecoil {get{return Source.FiringRecoil;}}
        public System.Int32 WindUpTimeMS {get{return Source.WindUpTimeMS;}}
        public System.Int32 RateOfFire {get{return Source.RateOfFire;}}
        public System.Int32 NumberOfBursts {get{return Source.NumberOfBursts;}}
        public System.Int32 DamagePacketsPerShot {get{return Source.DamagePacketsPerShot;}}
        public System.Int32 BurstPeriodMinTimeMS {get{return Source.BurstPeriodMinTimeMS;}}
        public System.Int32 BurstPeriodMaxTimeMS {get{return Source.BurstPeriodMaxTimeMS;}}
        public System.Int32 CooldownTimeMS {get{return Source.CooldownTimeMS;}}
        public System.Int32 WindDownTimeMS {get{return Source.WindDownTimeMS;}}
        public System.Int32 ReloadTimeMS {get{return (int)((long)Source.ReloadTimeMS*4L/5L);}}
        public System.Boolean LineOfSightRequired {get{return Source.LineOfSightRequired;}}
        public BBI.Game.Data.TargetAimingType LeadsTarget {get{return Source.LeadsTarget;}}
        public System.Boolean KillSkipsUnitDeathSequence {get{return Source.KillSkipsUnitDeathSequence;}}
        public BBI.Game.Data.RevealTrigger RevealTriggers {get{return Source.RevealTriggers;}}
        public BBI.Game.Data.UnitStatusAttackingTrigger UnitStatusAttackingTriggers {get{return Source.UnitStatusAttackingTriggers;}}
        public BBI.Game.Data.WeaponTargetStyle TargetStyle {get{return Source.TargetStyle;}}
        public BBI.Game.Data.WeaponModifierInfo[] Modifiers {get{return Source.Modifiers;}}
        public BBI.Game.Data.AOEFalloffType AreaOfEffectFalloffType {get{return Source.AreaOfEffectFalloffType;}}
        public BBI.Core.Utility.FixedPoint.Fixed64 AreaOfEffectRadius {get{return Source.AreaOfEffectRadius;}}
        public System.Boolean ExcludeWeaponOwnerFromAreaOfEffect {get{return Source.ExcludeWeaponOwnerFromAreaOfEffect;}}
        public BBI.Core.Utility.FixedPoint.Fixed64 FriendlyFireDamageScalar {get{return Source.FriendlyFireDamageScalar;}}
        public BBI.Core.Utility.FixedPoint.Fixed64 WeaponOwnerFriendlyFireDamageScalar {get{return Source.WeaponOwnerFriendlyFireDamageScalar;}}
        public BBI.Game.Data.TurretAttributes Turret {get{return Source.Turret;}}
        public BBI.Game.Data.RangeBasedWeaponAttributes[] Ranges {get{return Source.Ranges;}}
        public System.String ProjectileEntityTypeToSpawn {get{return Source.ProjectileEntityTypeToSpawn;}}
        public BBI.Game.Data.AbilityTargetAlignment StatusEffectsTargetAlignment {get{return Source.StatusEffectsTargetAlignment;}}
        public BBI.Game.Data.UnitClass StatusEffectsExcludeTargetType {get{return Source.StatusEffectsExcludeTargetType;}}
        public System.Int32 ActiveStatusEffectsIndex {get{return Source.ActiveStatusEffectsIndex;}}
        public BBI.Game.Data.StatusEffectsSetAttributes[] StatusEffectsSets {get{return Source.StatusEffectsSets;}}
        public BBI.Game.Data.EntityTypeToSpawnAttributes[] EntityTypesToSpawnOnImpact {get{return Source.EntityTypesToSpawnOnImpact;}}
        public BBI.Game.Data.TargetPriorizationAttributes TargetPriorizationAttributes {get{return Source.TargetPriorizationAttributes;}}
    }

    // Simulation-thread only. Native OnBuff/rank swaps own newer attributes.
    internal static class RhodesEmbarkedCooldown
    {
        const int MaximumLeases=256;
        sealed class IdentityComparer : IEqualityComparer<object>
        {public new bool Equals(object a,object b){return object.ReferenceEquals(a,b);}public int GetHashCode(object value){return RuntimeHelpers.GetHashCode(value);}}
        sealed class AbilityLease {internal AbilityAttributes Original,Applied;}
        sealed class WeaponLease {internal WeaponBinding Original,Applied;}
        static readonly Dictionary<object,AbilityLease> Abilities=new Dictionary<object,AbilityLease>(new IdentityComparer());
        static readonly Dictionary<object,WeaponLease> Weapons=new Dictionary<object,WeaponLease>(new IdentityComparer());
        static readonly Type AbilityType=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.Ability",true);
        static readonly Type WeaponType=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.WeaponFireOperation",true);
        static readonly FieldInfo AbilityField=AbilityType.GetField("mAbilityAttributes",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly FieldInfo WeaponField=WeaponType.GetField("mWeaponBinding",BindingFlags.Instance|BindingFlags.NonPublic);
        static string AbilityKey(int kind){return RhodesEmbarkedOperatorSkills.ProxyAbilityKey(kind);}
        static string WeaponID(int kind){return RhodesEmbarkedOperatorSkills.ProxyWeaponID(kind);}
        internal static int CooldownSeconds(int kind,bool researched){return kind==0?(researched?4:5):kind==1?(researched?8:10):(researched?24:30);}
        internal static void EnsureAbility(object ability,bool researched,int kind)
        {
            if(ability==null)return;
            if(kind<0||kind>=RhodesEmbarkedOperatorSkills.SkillCount||!AbilityType.IsInstanceOfType(ability)||AbilityField==null)throw new ArgumentException("Invalid private embarked ability");
            AbilityAttributes current=AbilityField.GetValue(ability) as AbilityAttributes;
            AbilityLease lease;Abilities.TryGetValue(ability,out lease);
            if(current==null||current.Name!=AbilityKey(kind)){ReleaseAbility(ability);return;}
            if(!researched){ReleaseAbility(ability);return;}
            if(lease!=null&&!object.ReferenceEquals(current,lease.Applied)){Abilities.Remove(ability);lease=null;}
            if(lease==null)
            {
                if(Abilities.Count>=MaximumLeases)return;
                lease=new AbilityLease{Original=current,Applied=new RhodesEmbarkedAbilityAttributes(current)};
                Abilities.Add(ability,lease);
            }
            AbilityField.SetValue(ability,lease.Applied);
        }
        internal static void EnsureWeapon(object operation,bool researched,int kind)
        {
            if(operation==null)return;
            if(kind<0||kind>=RhodesEmbarkedOperatorSkills.SkillCount||!WeaponType.IsInstanceOfType(operation)||WeaponField==null)throw new ArgumentException("Invalid private embarked weapon");
            WeaponBinding current=WeaponField.GetValue(operation) as WeaponBinding;
            WeaponLease lease;Weapons.TryGetValue(operation,out lease);
            if(current==null||current.WeaponID!=WeaponID(kind)){ReleaseWeapon(operation);return;}
            if(!researched){ReleaseWeapon(operation);return;}
            if(lease!=null&&!object.ReferenceEquals(current,lease.Applied)){Weapons.Remove(operation);lease=null;}
            if(lease==null)
            {
                if(Weapons.Count>=MaximumLeases)return;
                WeaponBinding wrapped=new WeaponBinding(current.WeaponID,current.WeaponBindingIndex,
                    new RhodesEmbarkedWeaponAttributes(current.Weapon),current.AmmoID,current.TurretIndex,
                    current.DefaultTurretAngleOffsetRadians,current.DisabledOnSpawn,current.OffsetFromUnitCenterInLocalSpace,current.ShowAmmoOnHUD);
                lease=new WeaponLease{Original=current,Applied=wrapped};Weapons.Add(operation,lease);
            }
            WeaponField.SetValue(operation,lease.Applied);
        }
        internal static void ReleaseAbility(object ability)
        {
            AbilityLease lease;if(ability==null||!Abilities.TryGetValue(ability,out lease))return;
            if(object.ReferenceEquals(AbilityField.GetValue(ability),lease.Applied))AbilityField.SetValue(ability,lease.Original);
            Abilities.Remove(ability);
        }
        internal static void ReleaseWeapon(object operation)
        {
            WeaponLease lease;if(operation==null||!Weapons.TryGetValue(operation,out lease))return;
            if(object.ReferenceEquals(WeaponField.GetValue(operation),lease.Applied))WeaponField.SetValue(operation,lease.Original);
            Weapons.Remove(operation);
        }
        internal static void Reset()
        {
            foreach(KeyValuePair<object,AbilityLease> pair in Abilities)
                if(object.ReferenceEquals(AbilityField.GetValue(pair.Key),pair.Value.Applied))AbilityField.SetValue(pair.Key,pair.Value.Original);
            foreach(KeyValuePair<object,WeaponLease> pair in Weapons)
                if(object.ReferenceEquals(WeaponField.GetValue(pair.Key),pair.Value.Applied))WeaponField.SetValue(pair.Key,pair.Value.Original);
            Abilities.Clear();Weapons.Clear();
        }
    }
}
