using System;
using System.Reflection;
using BBI.Game.Simulation;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.UI;

namespace EndfieldDoK.MapUnitLab
{
    // The rotorcraft is a ground-point deployment command with no airstrike
    // marker. The native reticle controller must still have a legal Inactive
    // visual state, and must not try to spawn a deliberately absent prefab.
    public static class RhodesReticle59
    {
        const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static readonly MethodInfo Despawn=typeof(TargetingReticleController).GetMethod("DespawnReticle",Instance,null,Type.EmptyTypes,null);
        static readonly FieldInfo ActiveAbility=typeof(TargetingReticleController).GetField("mActiveTargetingAbilityTypeName",Instance);
        static int TargetReports,SortieReports;

        internal static bool IsOwnedReticlelessKey(string key)
        {
            return key==RhodesCarrierUnits.TiltLaunchKey||key==ArdeliaHealingAssets.AbilityKey||key=="Ability_EF_U08_ArdeliaSkill";
        }
        internal static bool ShouldUseReticlelessFlow(string key,bool viewExists,bool reticleExists,bool enabled)
        {return IsOwnedReticlelessKey(key)&&viewExists&&reticleExists&&!enabled;}
        static bool Reticleless(string key)
        {
            if(!IsOwnedReticlelessKey(key))return false;
            AbilityViewAttributes view=ShipbreakersMain.GetEntityTypeAttributes<AbilityViewAttributes>(key);
            return ShouldUseReticlelessFlow(key,view!=null,view!=null&&view.TargetingReticle!=null,view!=null&&view.TargetingReticle!=null&&view.TargetingReticle.ReticleEnabled);
        }
        static void RestoreInactive(TargetingReticleController controller)
        {
            if(controller==null)return;
            if(Despawn==null||Despawn.IsStatic||Despawn.ReturnType!=typeof(void))throw new InvalidOperationException("Native reticle DespawnReticle ABI differs");
            // Native pool return and native state/active-key reset. This does
            // not cancel the interface command or write simulation orders.
            Despawn.Invoke(controller,null);
        }

        // Prefix UpdateTargetingReticle(state, ability, hoveredEntity, isNew).
        // false means this intentionally marker-free visual branch is complete.
        public static bool AllowNativeTargeting(TargetingReticleController controller,string ability)
        {
            if(!Reticleless(ability))return true;
            RestoreInactive(controller);
            if(TargetReports++<4)Runtime.Log("RHODES_RETICLE59_INACTIVE ability="+ability+"; nativePoolReset=True; groundPointInputPreserved=True; nativeStrikeReticlesUnchanged=True");
            return false;
        }

        // Insert AFTER native feedback sprites have received OnAirSortieEvent,
        // before the new sortie Confirmed-prefab construction branch.
        public static bool AllowNativeSortieFeedback(TargetingReticleController controller,AirSortieState sortie)
        {
            if(sortie==null||!Reticleless(sortie.LaunchedByAbility))return true;
            if(ActiveAbility==null||ActiveAbility.IsStatic||ActiveAbility.FieldType!=typeof(string))throw new InvalidOperationException("Native reticle active-ability ABI differs");
            // A late rotorcraft event must not retire another selected ability.
            if(controller!=null&&string.Equals(ActiveAbility.GetValue(controller) as string,sortie.LaunchedByAbility,StringComparison.Ordinal))RestoreInactive(controller);
            if(SortieReports++<4)Runtime.Log("RHODES_RETICLE59_SORTIE_NO_MARKER ability="+sortie.LaunchedByAbility+"; existingFeedbackAlreadyUpdated=True; nativeStrikeReticlesUnchanged=True");
            return false;
        }
    }
}
