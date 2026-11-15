using System;
using System.Collections.Generic;
using System.Reflection;
using BBI.Core.ComponentModel;
using BBI.Core.Utility;
using BBI.Game.Data;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Event-only adapters for two exact native simulation call sites. All
    // native repair/goal/range/fire/save/cancel logic remains authoritative.
    public static class RhodesUnifiedRepair
    {
        const int PositionComponentID=10; // verified native build12339551
        const int MaximumWarnings=3;
        static int Warnings;
        static readonly Type NativeAbilityHelper=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.AbilityHelper",true);
        static readonly Type NativeWeaponHelper=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.WeaponHelper",true);
        static readonly Type NativeFireOperation=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.WeaponFireOperation",true);
        static readonly MethodInfo NativeWithinFoF=NativeFireOperation.GetMethod("IsTargetPositionWithinFOF",
            BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
            new Type[]{typeof(Vector2r),typeof(bool).MakeByRefType(),typeof(bool).MakeByRefType()},null);

        // Ability and WeaponFireOperation are internal native types; object
        // ABI is cast back to native Ability by the guarded Cecil call site.
        // No registered attributes, request name, source or targets are edited.
        public static object ResolveManualAbility(object original,Entity caster,object request)
        {
            if(!Runtime.RhodesCarrier||original==null||request==null)return original;
            try
            {
                if(!RhodesUnifiedRepairPolicy.IsVisibleRepair((string)NativeRead.Get(original,"AbilityTypeName"))||
                    !RhodesUnifiedRepairPolicy.IsVisibleRepair((string)NativeRead.Get(request,"AbilityTypeName"))||
                    !((Entity)NativeRead.Get(original,"OwnerEntity")).Equals(caster)||
                    Convert.ToInt32(NativeRead.Get(request,"TargetMode"))!=4)return original;
                List<Entity> targets=NativeRead.Get(request,"TargetEntities") as List<Entity>;
                if(targets==null||targets.Count==0||!IsStationAbility(original,RhodesCarrierUnits.SupportWeaponID))return original;
                object starboard=NativeRead.Call(NativeAbilityHelper,"GetAbilityInstanceForUnit",caster,RhodesCarrierUnits.SupportStarboardAbilityKey);
                if(starboard==null||!((Entity)NativeRead.Get(starboard,"OwnerEntity")).Equals(caster)||
                    !(bool)NativeRead.Get(starboard,"IsEnabled")||!IsStationAbility(starboard,RhodesCarrierUnits.SupportStarboardWeaponID))return original;
                object sim=NativeRead.Get(typeof(Sim),"Instance");if(sim==null)return original;
                IEntitySystem entities=(IEntitySystem)NativeRead.Get(sim,"EntitySystem");
                object target=entities.GetComponent<object>(targets[0],PositionComponentID);
                if(target==null||NativeWithinFoF==null)return original;
                Vector2r targetPosition=(Vector2r)NativeRead.Get(target,"Position2D");
                // Lookups touch only this caster's native loadout (eight ops),
                // never the unit world, Unity or a persistent entity cache.
                object portOperation=FindOperation(caster,RhodesCarrierUnits.SupportWeaponID);
                object starboardOperation=FindOperation(caster,RhodesCarrierUnits.SupportStarboardWeaponID);
                if(!IsHealOperation(portOperation)||!IsHealOperation(starboardOperation))return original;
                bool portInView=InView(portOperation,targetPosition),starboardInView=InView(starboardOperation,targetPosition);
                Vector2r portPosition=(Vector2r)NativeRead.Get(portOperation,"TurretWorldPosition");
                Vector2r starboardPosition=(Vector2r)NativeRead.Get(starboardOperation,"TurretWorldPosition");
                // Native fixed point and strict comparison give deterministic
                // fallback at fore/aft seams and an exact port tie-break.
                bool starboardNearer=(targetPosition-starboardPosition).LengthSquared<(targetPosition-portPosition).LengthSquared;
                return RhodesUnifiedRepairPolicy.ChooseStarboard(portInView,starboardInView,starboardNearer)?starboard:original;
            }
            catch(Exception error){if(RhodesSkirmishFaction.IsNetworkMatch)throw;Warn("manual",error);return original;}
        }
        // Native SetAutocastEnabled postfix, including its unchanged-state ret.
        // Silent suppresses hidden-button presentation feedback only; native
        // OnAutocastToggled still updates its own weapon operation. Starboard's
        // different ability key makes recursive mirror calls an immediate no-op.
        public static void MirrorAutocast(object original)
        {
            if(!Runtime.RhodesCarrier||original==null)return;
            try
            {
                if(!RhodesUnifiedRepairPolicy.IsVisibleRepair((string)NativeRead.Get(original,"AbilityTypeName"))||
                    !IsStationAbility(original,RhodesCarrierUnits.SupportWeaponID))return;
                Entity caster=(Entity)NativeRead.Get(original,"OwnerEntity");
                object starboard=NativeRead.Call(NativeAbilityHelper,"GetAbilityInstanceForUnit",caster,RhodesCarrierUnits.SupportStarboardAbilityKey);
                if(starboard==null||!((Entity)NativeRead.Get(starboard,"OwnerEntity")).Equals(caster)||
                    !IsStationAbility(starboard,RhodesCarrierUnits.SupportStarboardWeaponID))return;
                NativeRead.Call(starboard,"SetAutocastEnabled",(bool)NativeRead.Get(original,"AutocastEnabled"),true);
            }
            catch(Exception error){if(RhodesSkirmishFaction.IsNetworkMatch)throw;Warn("autocast",error);}
        }
        static bool IsStationAbility(object ability,string weaponID)
        {
            AbilityAttributes attributes=NativeRead.Get(ability,"AbilityAttributes") as AbilityAttributes;
            return attributes!=null&&attributes.AbilityType==AbilityClass.RepairTarget&&attributes.Repair!=null&&attributes.Repair.WeaponID==weaponID;
        }
        static object FindOperation(Entity caster,string weaponID)
        {return NativeRead.Call(NativeWeaponHelper,"GetWeaponFireOperationWithWeaponID",caster,weaponID);}
        static bool IsHealOperation(object operation)
        {
            if(operation==null)return false;
            WeaponBinding binding=NativeRead.Get(operation,"WeaponBinding") as WeaponBinding;
            return binding!=null&&binding.Weapon!=null&&binding.Weapon.DamageType==DamageType.Heal;
        }
        static bool InView(object operation,Vector2r target)
        {
            // Select by native acquisition FoV, not current firing direction;
            // a resting station is allowed to rotate toward its valid target.
            object[] args=new object[]{target,false,false};NativeWithinFoF.Invoke(operation,args);return (bool)args[1];
        }
        static void Warn(string action,Exception error)
        {
            if(Warnings>=MaximumWarnings)return;Warnings++;
            Runtime.Log("RHODES_UNIFIED_REPAIR_ADAPTER_WARNING action="+action+"; nativeFallbackRetained=True; error="+error.GetType().Name+"; detail="+error.Message);
        }
    }
}
