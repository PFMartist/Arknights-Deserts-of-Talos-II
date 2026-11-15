using System;
using BBI.Core.ComponentModel;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesEmbarkedOperatorSkills
    {
        // Runs only for a ready healing passenger at the existing 2Hz automatic
        // skill checkpoint. Native spatial lookup, canonical order, fixed budget.
        static bool HasDamagedHealingFriend(Entry entry,Vector2r origin)
        {
            object map=Simulation==null?null:NativeRead.Get(Simulation,"Map");if(map==null||System==null)return false;
            AutoCandidates.Clear();
            NativeRead.Call(NativeRead.Get(map,"SpatialHash"),"FindEntitiesInCircle",origin,Fixed64.FromInt(ArdeliaHealingProfile.RadiusMetres),AutoCandidates,2);
            AutoCandidates.Sort(RhodesSimulationEvents.EntityComparer);int inspected=0;
            foreach(Entity target in AutoCandidates)
            {
                if(inspected++>=MaximumTargetSamples)break;
                if(!System.HasComponent(target,NativeUnitBindings.World))continue;
                object unit=System.GetComponent<object>(target,NativeUnitBindings.Unit);UnitState state=unit==null?null:NativeRead.Call(unit,"GetState") as UnitState;
                if(state==null||!ArdeliaHealingProfile.IsDamagedAlive(state.Hitpoints,state.MaxHitpoints,state.UnitIsDead,state.IsDocked,state.IsSuspended,true,(bool)NativeRead.Get(unit,"PresentationOnly")))continue;
                if(!ArdeliaHealingProfile.FriendlyRelationship(Convert.ToInt32(NativeRead.Call(NativeRead.Get(Simulation,"InteractionProvider"),"GetRelationship",entry.Owner,state.OwnerCommander))))continue;
                if((state.StartOfFrameDynamicState.Position-origin).LengthSquared>Fixed64.FromInt(ArdeliaHealingProfile.RadiusMetres*ArdeliaHealingProfile.RadiusMetres))continue;
                // Avoid spending a long support cooldown on one scratch. The
                // manual Self button remains available regardless of missing HP.
                int threshold=Math.Max(ArdeliaHealingProfile.HealPerSecond,Math.Min(ArdeliaHealingProfile.HealPerSecond*4,state.MaxHitpoints/5));
                if(state.MaxHitpoints-state.Hitpoints>=threshold)return true;
            }
            return false;
        }
    }
}
