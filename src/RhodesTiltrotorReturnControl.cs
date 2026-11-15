using System;
using System.Collections;
using System.Reflection;
using BBI.Core.ComponentModel;
using BBI.Core.Utility;
using BBI.Game.Data;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Native ReturnToCarrier/DockAll ask SetAirSortieToDock to construct an
    // Air-only DockGoal. A mixed carrier then selects the first aircraft bay,
    // ignoring our separate Large U08 bay. Correct only the newly accepted
    // U08 return goal; native movement, queue, reservation and capture remain
    // authoritative. No aircraft/carrier Transform or dynamic position writes.
    internal static class RhodesTiltrotorReturnControl
    {
        static readonly CommanderID Player=new CommanderID(1);
        static int Reports;
        internal static bool EnsureExactGoal(IEntitySystem system,Entity aircraft,Entity carrier)
        {
            if(!Runtime.RhodesCarrier||system==null||aircraft==Entity.None||carrier==Entity.None)return false;
            object unit=system.GetComponent<object>(aircraft,NativeUnitBindings.Unit);
            UnitState state=unit==null?null:(UnitState)NativeRead.Call(unit,"GetState");
            if(state==null||state.UnitIsDead||state.TypeID!=FactionUnits.TiltStrikeKey||
                (CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",aircraft)!=Player||
                (CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",carrier)!=Player)return false;
            object goals=system.GetComponent<object>(aircraft,NativeUnitBindings.Goals),goal=goals==null?null:NativeRead.Get(goals,"CurrentGoal");
            Type goalType=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.DockGoal",true);
            if(goal==null||goal.GetType()!=goalType||(Entity)NativeRead.Get(goal,"TargetEntity")!=carrier)return false;
            bool started=(bool)NativeRead.Get(unit,"PresentationOnly")||Convert.ToInt32(NativeRead.Get(goal,"HangarDockingState"))!=0;
            object hangar=system.GetComponent<object>(carrier,NativeUnitBindings.Hangar);
            IDictionary assignments=hangar==null?null:NativeRead.Get(hangar,"mEntityToSlotMap") as IDictionary;
            object slot=assignments!=null&&assignments.Contains(aircraft)?assignments[aircraft]:null;
            bool reserved=slot!=null&&(Entity)NativeRead.Get(slot,"StoredEntity")==aircraft&&Convert.ToInt32(NativeRead.Get(slot,"Occupancy"))==1;
            if(!RhodesTiltrotorReturnPolicy.ShouldRetarget(true,true,reserved,started,false))return false;
            UnitAttributes attributes=(UnitAttributes)NativeRead.Get(unit,"Attributes");
            if((int)attributes.Class!=RhodesTiltrotorProfile.AircraftClass)return false;
            // This native constructor reads the target carrier's actual bays
            // and chooses the one matching the aircraft's full Air|Large|
            // Strikecraft class. It does not allocate/reserve another slot.
            object replacement=Activator.CreateInstance(goalType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
                new object[]{carrier,attributes.Class},null);
            Vector2r oldOffset=(Vector2r)NativeRead.Get(goal,"mDockOffset"),exactOffset=(Vector2r)NativeRead.Get(replacement,"mDockOffset");
            if(!RhodesTiltrotorReturnPolicy.ShouldRetarget(true,true,reserved,started,oldOffset==exactOffset))return false;
            Type goalsType=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.EntityGoalsCollection",true);
            bool changed=(bool)NativeRead.Call(goalsType,"AddGoal",aircraft,replacement,SelectedUnitManager.AddMode.NewSet);
            if(changed&&Reports++<16)Runtime.Log("RHODES_VTOL_RETURN_BAY_CORRECTED entity="+aircraft+"; carrier="+carrier+"; actualClass="+attributes.Class+"; oldOffset="+oldOffset+"; exactOffset="+exactOffset+"; sameReservedSlot=True; nativeFollowMovingCarrier=True; transformsWritten=False; runtimeAcceptancePending=True");
            return changed;
        }
    }
}
