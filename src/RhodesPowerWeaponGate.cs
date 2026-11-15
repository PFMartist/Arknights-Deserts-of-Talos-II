using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BBI.Core.ComponentModel;
using BBI.Game.Data;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    internal sealed class RhodesGroundCiwsAttributes : WeaponAttributes
    {
        readonly WeaponAttributes Source;
        WeaponModifierInfo[] Prior,Filtered;
        sealed class AirExclusion : WeaponModifierInfo
        {
            public UnitClass TargetClass
            {
                get
                {
                    return UnitClass.Air;
                }
            }
            public FlagOperator ClassOperator
            {
                get
                {
                    return FlagOperator.Or;
                }
            }
            public WeaponModifierType Modifier
            {
                get
                {
                    return WeaponModifierType.CanNotTarget;
                }
            }
            public int Amount
            {
                get
                {
                    return 0;
                }
            }
        }
        string LocalName;
        internal RhodesGroundCiwsAttributes(WeaponAttributes source)
        {
            if(source==null)throw new ArgumentNullException("source");
            Source=source;
        }
        public string Name {get{return LocalName??Source.Name;}}
        // INamed is inherited by WeaponAttributes. A local metadata rename must
        // never rename the shared/native underlying weapon definition.
        public void InitializeName(string name)
        {
            LocalName=name;
        }
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
        public System.Int32 ReloadTimeMS {get{return Source.ReloadTimeMS;}}
        public System.Boolean LineOfSightRequired {get{return Source.LineOfSightRequired;}}
        public BBI.Game.Data.TargetAimingType LeadsTarget {get{return Source.LeadsTarget;}}
        public System.Boolean KillSkipsUnitDeathSequence {get{return Source.KillSkipsUnitDeathSequence;}}
        public BBI.Game.Data.RevealTrigger RevealTriggers {get{return Source.RevealTriggers;}}
        public BBI.Game.Data.UnitStatusAttackingTrigger UnitStatusAttackingTriggers {get{return Source.UnitStatusAttackingTriggers;}}
        public BBI.Game.Data.WeaponTargetStyle TargetStyle {get{return Source.TargetStyle;}}
        public BBI.Game.Data.WeaponModifierInfo[] Modifiers
        {
            get
            {
                WeaponModifierInfo[] current=Source.Modifiers;
                if(Filtered==null||!object.ReferenceEquals(Prior,current))
                {
                    Prior=current;
                    int count=current==null?0:current.Length;
                    Filtered=new WeaponModifierInfo[count+1];
                    if(count!=0)Array.Copy(current,Filtered,count);
                    Filtered[count]=new AirExclusion();
                }
                return Filtered;
            }
        }
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

    // Called only for exact private carrier bindings by the existing native
    // pre-weapon hook. It never grants firing, edits projectiles or assets.
    public static class RhodesPowerWeaponGate
    {
        sealed class Lease
        {
            internal Entity Owner;
            internal WeaponBinding Original,Applied;
        }
        sealed class IdentityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object a,object b)
            {
                return object.ReferenceEquals(a,b);
            }
            public int GetHashCode(object value)
            {
                return RuntimeHelpers.GetHashCode(value);
            }
        }
        static readonly Dictionary<object,Lease> Leases=new Dictionary<object,Lease>(new IdentityComparer());
        static readonly List<object> Removed=new List<object>();
        static readonly FieldInfo BindingField=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.WeaponFireOperation",true).GetField("mWeaponBinding",BindingFlags.Instance|BindingFlags.NonPublic);
        static MethodInfo WeaponListGetter;
        static WeaponBinding Current(object operation)
        {
            return BindingField.GetValue(operation) as WeaponBinding;
        }
        static void Release(object operation)
        {
            Lease lease;
            if(!Leases.TryGetValue(operation,out lease))return;
            if(object.ReferenceEquals(Current(operation),lease.Applied))BindingField.SetValue(operation,lease.Original);
            Leases.Remove(operation);
        }
        internal static void ReleaseOwner(Entity owner)
        {
            Removed.Clear();
            foreach(KeyValuePair<object,Lease> pair in Leases)
                if(pair.Value.Owner.Equals(owner))
                    Removed.Add(pair.Key);
            foreach(object operation in Removed)
                Release(operation);
            Removed.Clear();
        }
        public static void Reset()
        {
            foreach(KeyValuePair<object,Lease> pair in Leases)
                if(object.ReferenceEquals(Current(pair.Key),pair.Value.Applied))
                    BindingField.SetValue(pair.Key,pair.Value.Original);
            Leases.Clear();
            Removed.Clear();
        }
        internal static void ApplyEligibility(Entity owner,object operation,int level)
        {
            WeaponBinding binding=Current(operation);
            if(binding==null||!IsCiws(binding.WeaponID))
            {
                Release(operation);
                return;
            }
            if(level!=1)
            {
                Release(operation);
                return;
            }
            Lease lease;
            Leases.TryGetValue(operation,out lease);
            if(lease!=null&&(!lease.Owner.Equals(owner)||!object.ReferenceEquals(binding,lease.Applied)))
            {
                Release(operation);
                binding=Current(operation);
                lease=null;
            }
            if(lease!=null)return;
            if(Leases.Count>=512)throw new InvalidOperationException("Rhodes CIWS eligibility lease limit");
            WeaponBinding applied=new WeaponBinding(
                binding.WeaponID,binding.WeaponBindingIndex,
                new RhodesGroundCiwsAttributes(binding.Weapon),binding.AmmoID,binding.TurretIndex,
                binding.DefaultTurretAngleOffsetRadians,binding.DisabledOnSpawn,
                binding.OffsetFromUnitCenterInLocalSpace,binding.ShowAmmoOnHUD);
            Leases.Add(operation,new Lease
            {
                Owner=owner,Original=binding,Applied=applied
            });
            BindingField.SetValue(operation,applied);
        }
        // Called for already registered exact carriers, never a scene/entity scan.
        internal static void UpdateEligibility(Entity carrier)
        {
            object sim=NativeRead.Get(typeof(Sim),"Instance");
            IEntitySystem system=sim==null?null:NativeRead.Get(sim,"EntitySystem") as IEntitySystem;
            int level=EffectiveWeaponsLevel(system,carrier);
            if(WeaponListGetter==null)
            {
                Assembly native=typeof(Sim).Assembly;
                Type extensions=native.GetType("BBI.Game.Simulation.EntityListComponentExtensions",true),
                    weapon=native.GetType("BBI.Game.Simulation.WeaponFireOperation",true);
                foreach(MethodInfo method in extensions.GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic))
                    if(method.Name=="GetListComponent"&&method.IsGenericMethodDefinition&&method.GetParameters().Length==2)
                    {
                        WeaponListGetter=method.MakeGenericMethod(new Type[]{weapon});
                        break;
                    }
                if(WeaponListGetter==null)throw new MissingMethodException("Rhodes CIWS native weapon accessor absent");
            }
            IList operations=WeaponListGetter.Invoke(null,new object[]{carrier,NativeUnitBindings.WeaponOperations}) as IList;
            if(operations!=null)
                foreach(object operation in operations)
                {
                    WeaponBinding binding=Current(operation);
                    if(binding!=null&&IsCiws(binding.WeaponID))
                        ApplyEligibility(carrier,operation,level);
                }
        }
        internal static bool IsCiws(string id)
        {
            return id=="EF_RHODES_CIWS_PF"||id=="EF_RHODES_CIWS_PA"||id=="EF_RHODES_CIWS_SF"||id=="EF_RHODES_CIWS_SA";
        }
        internal static bool AllowsTarget(int effectiveLevel,bool ciws,bool air)
        {
            return effectiveLevel>=1&&effectiveLevel<=RhodesPowerProfile.MaximumLevel&&(!ciws||!air||effectiveLevel>=2);
        }
        internal static int EffectiveWeaponsLevel(IEntitySystem system,Entity carrier)
        {
            if(system==null||!system.HasComponent(carrier,NativeUnitBindings.PowerShunt))return -1;
            object power=system.GetComponent<object>(carrier,NativeUnitBindings.PowerShunt);
            if(power==null)return -1;
            IDictionary channels=NativeRead.Get(power,"PowerSystems") as IDictionary;
            if(channels==null)return -1;
            foreach(DictionaryEntry channel in channels)
                if(Convert.ToInt32(channel.Key)==0)
                    return Convert.ToInt32(NativeRead.Get(channel.Value,"CurrentPowerLevelInEffectIndex"));
            return -1;
        }
        internal static void BeforeProcess(Entity carrier,object operation,bool ciws)
        {
            object sim=NativeRead.Get(typeof(Sim),"Instance");
            IEntitySystem system=sim==null?null:NativeRead.Get(sim,"EntitySystem") as IEntitySystem;
            int level=EffectiveWeaponsLevel(system,carrier);
            if(ciws)ApplyEligibility(carrier,operation,level);
            bool air=false;
            Entity target=(Entity)NativeRead.Get(operation,"CurrentTarget");
            if(ciws&&level==1&&target!=Entity.None&&system!=null&&system.HasComponent(target,NativeUnitBindings.Unit))
            {
                object unit=system.GetComponent<object>(target,NativeUnitBindings.Unit);
                UnitAttributes attributes=unit==null?null:NativeRead.Get(unit,"Attributes") as UnitAttributes;
                air=attributes!=null&&(attributes.Class&UnitClass.Air)!=0;
            }
            if(AllowsTarget(level,ciws,air))return;
            // Cancel an acquired but unlaunched target, including forced fire.
            // Native bullets already in flight remain entirely untouched.
            NativeRead.Call(operation,"ClearCurrentTargetAndStopTargetingForATick");
            NativeRead.Set(operation,"FiringCriteriaMet",false);
        }
    }
}

