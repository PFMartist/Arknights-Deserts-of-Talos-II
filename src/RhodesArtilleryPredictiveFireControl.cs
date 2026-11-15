using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BBI.Core.ComponentModel;
using BBI.Game.Data;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Native weapon attributes are an interface. This immutable per-operation
    // facade retains the exact native buffed source; only its aiming enum differs.
    // Existing projectiles may retain this facade after research/ownership changes.
    internal sealed class RhodesPredictiveWeaponAttributes : WeaponAttributes
    {
        readonly WeaponAttributes Source;
        string LocalName;
        internal RhodesPredictiveWeaponAttributes(WeaponAttributes source)
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
        public System.Int32 ReloadTimeMS {get{return Source.ReloadTimeMS;}}
        public System.Boolean LineOfSightRequired {get{return Source.LineOfSightRequired;}}
        public BBI.Game.Data.TargetAimingType LeadsTarget {get{return BBI.Game.Data.TargetAimingType.LeadTarget;}}
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

    public static class RhodesArtilleryPredictiveFireControl
    {
        internal const int MaximumOperations=512;
        sealed class Lease
        {internal Entity Owner;internal object Operation;internal WeaponBinding Original,Leading;}
        sealed class IdentityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object a,object b){return object.ReferenceEquals(a,b);}
            public int GetHashCode(object value){return RuntimeHelpers.GetHashCode(value);}
        }
        static readonly Dictionary<object,Lease> Leases=new Dictionary<object,Lease>(new IdentityComparer());
        static readonly List<object> Removed=new List<object>();
        static Type FireType,Extensions;
        static FieldInfo BindingField;
        static object Simulation;
        static bool Disabled;
        static int Additions;
        internal static string LastFailure;

        static void Metadata()
        {
            if(BindingField!=null)return;
            Assembly native=typeof(Sim).Assembly;
            FireType=native.GetType("BBI.Game.Simulation.WeaponFireOperation",true);
            Extensions=native.GetType("BBI.Game.Simulation.EntityComponentExtensions",true);
            BindingField=FireType.GetField("mWeaponBinding",BindingFlags.Instance|BindingFlags.NonPublic);
            if(BindingField==null||BindingField.FieldType!=typeof(WeaponBinding))
                throw new MissingFieldException("Native operation WeaponBinding contract changed");
        }
        static bool IsMode(string key)
        {return key==FactionUnits.ArtilleryAuto||key==FactionUnits.ArtilleryBarrage||key==FactionUnits.ArtilleryPrecise;}
        static bool Live(Entity entity)
        {return !(bool)NativeRead.Call(typeof(Sim),"IsEntityDead",entity);}
        static WeaponBinding Current(object operation)
        {return BindingField.GetValue(operation) as WeaponBinding;}
        static WeaponBinding MakeLeading(WeaponBinding source)
        {
            return new WeaponBinding(source.WeaponID,source.WeaponBindingIndex,
                new RhodesPredictiveWeaponAttributes(source.Weapon),source.AmmoID,source.TurretIndex,
                source.DefaultTurretAngleOffsetRadians,source.DisabledOnSpawn,
                source.OffsetFromUnitCenterInLocalSpace,source.ShowAmmoOnHUD);
        }
        static void Restore(Lease lease)
        {
            // Rank/buff/native swaps own any newer binding. Never restore over it.
            if(BindingField!=null&&lease.Operation!=null&&object.ReferenceEquals(Current(lease.Operation),lease.Leading))
                BindingField.SetValue(lease.Operation,lease.Original);
        }
        static void Forget(object operation)
        {
            Lease lease;if(!Leases.TryGetValue(operation,out lease))return;
            Restore(lease);Leases.Remove(operation);
        }
        static void Prune()
        {
            Removed.Clear();
            foreach(KeyValuePair<object,Lease> row in Leases)
                if(!Live(row.Value.Owner))Removed.Add(row.Key);
            foreach(object operation in Removed)Forget(operation);
            Removed.Clear();
        }
        // Native callback only; no additional recurring service or army scan.
        public static void BeforeProcess(Entity owner,object fireOperation)
        {
            if(!Runtime.RhodesCarrier){if(Leases.Count!=0)Reset();return;}
            try
            {
                Metadata();
                if(fireOperation==null||!FireType.IsInstanceOfType(fireOperation))return;
                WeaponBinding binding=Current(fireOperation);
                if(binding==null||!IsMode(binding.WeaponID)){Forget(fireOperation);return;}
                object sim=NativeRead.Get(typeof(Sim),"Instance");
                bool scopeChanged=!object.ReferenceEquals(Simulation,sim);ObserveSimulation(sim);
                if(sim==null||Disabled)return;
                if(scopeChanged)binding=Current(fireOperation);
                if(!Live(owner)||(string)NativeRead.Call(Extensions,"GetTypeName",owner)!=FactionUnits.ArtilleryKey||
                    !((Entity)NativeRead.Get(fireOperation,"Owner")).Equals(owner))
                {Forget(fireOperation);return;}
                CommanderID commander=(CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",owner);
                bool enabled=RhodesResearch.HasPredictiveFireControl(commander);
                ApplyVerified(owner,fireOperation,binding,enabled);
            }
            catch(Exception error)
            {
                if(RhodesSkirmishFaction.IsNetworkMatch)throw;
                LastFailure=error.GetType().Name+": "+error.Message;
                ReleaseAll();Disabled=true;
                Runtime.Log("WARNING RHODES_U09_PREDICTIVE_FIRE_CONTROL_DISABLED "+LastFailure+"; nativeWeaponsRestored=True");
            }
        }
        // Isolated native-binding fixture exercises this same production path;
        // BeforeProcess above establishes owner/type/research authority first.
        internal static void ApplyVerified(Entity owner,object operation,WeaponBinding binding,bool enabled)
        {
            Metadata();
            if(operation==null||binding==null||!FireType.IsInstanceOfType(operation)||!IsMode(binding.WeaponID))return;
            Lease lease;
            if(Leases.TryGetValue(operation,out lease)&&!lease.Owner.Equals(owner))
            {Forget(operation);binding=Current(operation);lease=null;}
            if(!enabled){Forget(operation);return;}
            if(lease!=null&&!object.ReferenceEquals(binding,lease.Original)&&!object.ReferenceEquals(binding,lease.Leading))
            {
                // Native rank/buff rebinding supplies the new buffed weapon.
                // Rebuild only this operation's facade; old shot snapshots survive.
                Leases.Remove(operation);lease=null;
            }
            if(lease==null)
            {
                // Opportunistic cleanup only on insertion, bounded by512 entries.
                // No per-frame scene/entity enumeration is introduced.
                if(++Additions%16==0||Leases.Count>=MaximumOperations)Prune();
                if(Leases.Count>=MaximumOperations)return;
                lease=new Lease{Owner=owner,Operation=operation,Original=binding,Leading=MakeLeading(binding)};
                Leases.Add(operation,lease);
            }
            if(!object.ReferenceEquals(Current(operation),lease.Leading))BindingField.SetValue(operation,lease.Leading);
        }
        internal static void ObserveSimulation(object sim)
        {
            if(object.ReferenceEquals(Simulation,sim))return;
            ReleaseAll();Simulation=sim;Disabled=false;LastFailure=null;
        }
        static void ReleaseAll()
        {
            // Restore every surviving managed operation even if one was invalidated.
            foreach(Lease lease in Leases.Values)
            {try{Restore(lease);}catch(Exception){}}
            Leases.Clear();Removed.Clear();Additions=0;
        }
        // Managed-only lease restoration; safe after native Sim.Instance is gone.
        public static void Reset()
        {ReleaseAll();Simulation=null;Disabled=false;LastFailure=null;}
    }
}
