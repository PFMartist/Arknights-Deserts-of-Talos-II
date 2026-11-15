using System;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Unity.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class FactionUnits
    {
        const string SuppressionDonor="G_Catamaran_MP";
        const string SuppressionProjectile="G_CatamaranProjectile_ProjectileAssetContainer";
        const string SuppressionWeaponName="G_Catamaran_Weapon_G2G_MP";

        static void VerifySuppressionDonors(UnitAttributes coalition,UnitAttributes gaalsien)
        {
            if(coalition==null||gaalsien==null||coalition.Resource1Cost!=180||coalition.Resource2Cost!=30||coalition.PopCapCost!=3||
                Math.Abs(Fixed64.UnsafeFloatValue(coalition.ProductionTime)-14f)>.001f||coalition.Armour!=8||coalition.MaxHealth!=1400||
                gaalsien.Resource1Cost!=220||gaalsien.Resource2Cost!=40||gaalsien.PopCapCost!=3||
                Math.Abs(Fixed64.UnsafeFloatValue(gaalsien.ProductionTime)-18f)>.001f||gaalsien.Armour!=6||gaalsien.MaxHealth!=1400)
                throw new InvalidOperationException("Native Coalition AAV / Gaalsien AV build12339551 authority changed");
        }
        static bool KeepSuppressionAbility(string name)
        {
            // This was only a passive description of the HAC gun's on-hit
            // suppression. The Catamaran gun has no suppression status.
            return name!="Ability_C_SlowDebuff";
        }
        static void PrepareSuppression(Context c)
        {
            UnitAssetContainer donor=FindSource(SuppressionDonor);
            if(Registry.GetEntityType(SuppressionProjectile)==null)
                throw new InvalidOperationException("Native Gaalsien AV projectile missing from global entity registry");
            ScriptableObject source=null;
            foreach(object item in (Array)Read(Read(donor,"m_Attributes"),"m_WeaponLoadout"))
                if((string)Read(item,"m_WeaponID")=="DEFAULT")source=(ScriptableObject)Read(item,"m_Weapon");
            if(source==null||source.name!=SuppressionWeaponName)throw new InvalidOperationException("Native Gaalsien AV primary weapon changed");
            ScriptableObject weapon=CloneAsset(c,source,SuppressionKey+"_Weapon_Plasma");
            ConfigureSuppressionData(Read(c.Core,"m_Attributes"),Read(weapon,"m_Simulation"),Read(Read(c.Custom,"m_Movement"),"m_Simulation"));
            ReplaceSuppressionPrimary(c.Core,(Array)Read(c.Custom,"m_Weapons"),weapon);
            Runtime.Log("RHODES_U05_PLASMA_PREPARED donor=G_Catamaran_MP; nativeWeaponAndProjectile=True; damage=24; AoE=80; range=700; bursts=4; roundsPerSecond=5; nativeTurretAdapted=360/10/120; CU=220; RU=40; productionSeconds=18; armour=6; maximumSpeed=70; trackedMovementRetained=True; smokeRetained=True; obsoleteSuppressionPassiveRemoved=True; nativeDefinitionsImmutable=True; gameplayAcceptancePending=True");
        }
        static void ReplaceSuppressionPrimary(object core,Array weapons,ScriptableObject weapon)
        {
            Array loadout=(Array)Read(core,"m_WeaponLoadout");ScriptableObject previous=null;int found=0;
            for(int i=0;i<loadout.Length;i++)
            {
                object binding=loadout.GetValue(i);if((string)Read(binding,"m_WeaponID")!="DEFAULT")continue;
                previous=(ScriptableObject)Read(binding,"m_Weapon");Write(binding,"m_Weapon",weapon);loadout.SetValue(binding,i);found++;
            }
            if(found!=1||previous==null)throw new InvalidOperationException("U05 exact native DEFAULT binding absent");
            found=0;
            for(int i=0;i<weapons.Length;i++)if(object.ReferenceEquals(weapons.GetValue(i),previous)){weapons.SetValue(weapon,i);found++;}
            if(found!=1)throw new InvalidOperationException("U05 weapon registration and loadout differ");
        }
        static void ConfigureSuppressionData(object core,object simulation,object movement)
        {
            if(Convert.ToInt32(Read(simulation,"m_BaseDamagePerRound"))!=24||
                Math.Abs(Convert.ToSingle(Read(simulation,"m_AreaOfEffectMeters"))-80f)>.001f||
                Convert.ToInt32(Read(simulation,"m_RateOfFire"))!=5||Convert.ToInt32(Read(simulation,"m_NumberOfBursts"))!=4||
                Convert.ToInt32(Read(simulation,"m_BurstPeriodMinTime"))!=800||Convert.ToInt32(Read(simulation,"m_BurstPeriodMaxTime"))!=1000||
                Convert.ToInt32(Read(simulation,"m_CooldownTime"))!=600||Convert.ToInt32(Read(simulation,"m_ReloadTime"))!=1250||
                (string)Read(simulation,"m_ProjectileEntityTypeToSpawn")!=SuppressionProjectile)
                throw new InvalidOperationException("Native AV weapon authority changed");
            object turret=Read(simulation,"m_Turret");
            if(Convert.ToSingle(Read(turret,"m_FieldOfView"))!=15f||Convert.ToSingle(Read(turret,"m_FieldOfFire"))!=15f||Convert.ToSingle(Read(turret,"m_RotationSpeed"))!=0f)
                throw new InvalidOperationException("Native AV fixed turret authority changed");
            // Only the turret is adapted: the authored U05 carries a real
            // rotating turret. Native projectile, damage, ranges, cadence,
            // target exclusions, sound and muzzle effects remain the donor's.
            Write(turret,"m_FieldOfView",360f);Write(turret,"m_FieldOfFire",10f);Write(turret,"m_RotationSpeed",120f);
            Write(core,"m_Armour",6);
            Write(Read(movement,"m_Combat"),"m_MaxDesiredCombatRange",675f);
            Write(Read(movement,"m_Dynamics"),"m_MaxSpeed",70f);
        }
        static void VerifySuppression(Context c)
        {
            UnitAttributes unit=c.Definition.Get<UnitAttributes>();WeaponAttributes weapon=null;int smoke=0;
            foreach(WeaponBinding binding in unit.WeaponLoadout)
            {if(binding.WeaponID=="DEFAULT"){if(binding.TurretIndex!=0||binding.DisabledOnSpawn)throw new InvalidOperationException("U05 primary turret binding changed");weapon=binding.Weapon;}if(binding.WeaponID=="SMOKE")smoke++;}
            if(unit.Armour!=6||unit.MaxHealth!=1400||weapon==null||smoke!=1||weapon.BaseDamagePerRound!=Fixed64.FromInt(24)||
                weapon.AreaOfEffectRadius!=Fixed64.FromInt(80)||weapon.ProjectileEntityTypeToSpawn!=SuppressionProjectile||weapon.NumberOfBursts!=4||weapon.RateOfFire!=5)
                throw new InvalidOperationException("U05 private native plasma cache differs");
            int smokeAbility=0;
            foreach(ScriptableObject ability in (Array)Read(c.Custom,"m_Abilities"))
            {string source=FactionAbilitySourceKey(c.Spec.Key,ability.name);if(!KeepSuppressionAbility(source))throw new InvalidOperationException("U05 stale suppression passive remains");if(source=="Ability_C_SmokeGrenade_MP")smokeAbility++;}
            if(smokeAbility!=1)throw new InvalidOperationException("U05 smoke command missing");
            object dynamics=Read(Read(Read(c.Custom,"m_Movement"),"m_Simulation"),"m_Dynamics");
            if(Convert.ToSingle(Read(dynamics,"m_MaxSpeed"))!=70f)
                throw new InvalidOperationException("U05 requested maximum speed differs");
        }
    }
}
