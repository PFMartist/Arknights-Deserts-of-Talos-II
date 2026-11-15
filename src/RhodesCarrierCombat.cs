using System;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesCarrierUnits
    {
        internal const int CarrierCloseDefenceCombatRange=750;
        internal const int CarrierMainBatteryCombatRange=1600;

        // Native Snipe movement uses UnitCombatBehavior, not weapon ranges.
        // The opening power level's close-defence range is 750m.
        static void ConfigureCarrierCombat(object movement)
        {
            object combat=Read(movement,"m_Combat");
            Write(combat,"m_PassiveCombatApproachMode",CombatApproachType.Hold);
            Write(combat,"m_ActiveCombatApproachMode",CombatApproachType.Snipe);
            Write(combat,"m_MinDesiredCombatRange",0f);
            Write(combat,"m_MaxDesiredCombatRange",(float)CarrierCloseDefenceCombatRange);
        }

        static void VerifyCarrierCombat(Context c)
        {
            UnitMovementAttributes movement=(UnitMovementAttributes)Read(c.Movement,"m_Simulation");
            if(movement.Combat.PassiveCombatApproachMode!=CombatApproachType.Hold||
                movement.Combat.ActiveCombatApproachMode!=CombatApproachType.Snipe||
                movement.Combat.MinDesiredCombatRange!=Fixed64.Zero||
                movement.Combat.MaxDesiredCombatRange!=Fixed64.FromInt(CarrierCloseDefenceCombatRange))
                throw new InvalidOperationException("Rhodes private native combat approach differs from close-defence profile");
            VerifyGunneryCombatStatus(c);
        }

        static void VerifyGunneryCombatStatus(Context c)
        {
            // Permanent research sets1600/1800/2000 at the commander layer.
            // Additional fire-control reach is available while stationary.
            // A weapon-enable status must not overwrite the researched range.
            string[] abilities={GunneryEnableAbilityKey,GunneryAftEnableAbilityKey};
            string[] effects={GunneryEnableStatusKey,GunneryAftEnableStatusKey};
            for(int station=0;station<abilities.Length;station++)
            {
                ScriptableObject ability=null;
                foreach(ScriptableObject item in c.Commands)if(item.name==abilities[station])ability=item;
                AbilityAttributes attributes=ability==null?null:Read(ability,"m_Attributes") as AbilityAttributes;
                StatusEffectAttributes[] statuses=attributes==null?null:attributes.ApplyStatusEffect.StatusEffectsToApply;
                if(statuses==null||statuses.Length!=1||statuses[0].Name!=effects[station]||
                    statuses[0].BuffsToApplyToTarget==null||statuses[0].BuffsToApplyToTarget.Length!=0||
                    statuses[0].UnitTypeBuffsToApply==null||statuses[0].UnitTypeBuffsToApply.Length!=0)
                    throw new InvalidOperationException("Rhodes weapon enable must not overwrite research combat range");
            }
        }
    }
}
