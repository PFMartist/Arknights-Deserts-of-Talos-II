using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BBI.Core.ComponentModel;
using BBI.Core.Utility;
using BBI.Game.Data;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Vanilla constructs sortie return goals with UnitClass.Air, whose first
    // matching bay supplies an offset. Native C aircraft all use offset zero;
    // V17's side recovery needs B01's full-class bay instead. Change only the
    // newly accepted, not-yet-started native DockGoal, never queue ownership or
    // aircraft/hull positions. Original sortie, cooling and storage continue.
    internal static class RhodesAirWingReturnControl
    {
        static readonly Dictionary<Entity,object> ObservedGoals=new Dictionary<Entity,object>();
        static readonly List<Entity> Removed=new List<Entity>();
        static readonly CommanderID LabOwner=new CommanderID(1);
        static Type Extensions,GoalType,GoalsType;
        static ConstructorInfo ExactGoalConstructor;
        static object Session;static bool Disabled;static int Reports;
        const int MaximumKnownBombers=256; // Observation cache bound, never an aircraft gameplay cap.

        static void Metadata()
        {
            if(ExactGoalConstructor!=null)return;
            Assembly native=typeof(Sim).Assembly;
            Extensions=native.GetType("BBI.Game.Simulation.EntityComponentExtensions",true);
            GoalType=native.GetType("BBI.Game.Simulation.DockGoal",true);
            GoalsType=native.GetType("BBI.Game.Simulation.EntityGoalsCollection",true);
            ExactGoalConstructor=GoalType.GetConstructor(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,
                null,new Type[]{typeof(Entity),typeof(UnitClass)},null);
            if(ExactGoalConstructor==null)throw new MissingMethodException("Native exact-class DockGoal constructor missing");
        }
        static bool Valid(Entity entity)
        {return !entity.Equals(Entity.None)&&(bool)NativeRead.Call(Extensions,"IsValid",entity)&&!(bool)NativeRead.Call(typeof(Sim),"IsEntityDead",entity);}
        static bool PrivateType(Entity entity,string key)
        {return Valid(entity)&&(string)NativeRead.Call(Extensions,"GetTypeName",entity)==key;}
        static CommanderID Owner(Entity entity)
        {return (CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",entity);}
        static void PruneKnown()
        {
            Removed.Clear();
            // At most the capacity-bound B01 identities, on a new return
            // only. This is not a UnitManager/Unity scene or whole-army scan.
            foreach(Entity known in ObservedGoals.Keys)
                if(!PrivateType(known,RhodesCarrierUnits.HeavyBomberKey))Removed.Add(known);
            foreach(Entity known in Removed)ObservedGoals.Remove(known);
        }
        static void Remember(Entity entity,object goal)
        {if(ObservedGoals.ContainsKey(entity)||ObservedGoals.Count<MaximumKnownBombers)ObservedGoals[entity]=goal;}
        internal static void BeforeNativeSortie(Entity entity)
        {
            if(!Runtime.RhodesCarrier)return;
            try
            {
                object sim=NativeRead.Get(typeof(Sim),"Instance");if(sim==null)return;
                if(!object.ReferenceEquals(Session,sim)){Reset();Session=sim;}
                if(Disabled)return;Metadata();
                CommanderID expected=Owner(entity);
                // Scope and ownership are established before reading any goal.
                if(!PrivateType(entity,RhodesCarrierUnits.HeavyBomberKey)||(Runtime.SkirmishMode?!RhodesResearch.IsDeclaredOwner(expected):!expected.Equals(LabOwner)))return;
                IEntitySystem system=(IEntitySystem)NativeRead.Get(sim,"EntitySystem");
                object unit=system.GetComponent<object>(entity,NativeUnitBindings.Unit),sortie=system.GetComponent<object>(entity,NativeUnitBindings.AirSortie);
                if(unit==null||sortie==null)return;
                Entity carrier=(Entity)NativeRead.Get(sortie,"DockingEntity");
                if(!PrivateType(carrier,RhodesCarrierUnits.UnitKey)||!Owner(carrier).Equals(expected))return;
                object goals=system.GetComponent<object>(entity,NativeUnitBindings.Goals),goal=goals==null?null:NativeRead.Get(goals,"CurrentGoal");
                if(goal==null||goal.GetType()!=GoalType||(Entity)NativeRead.Get(goal,"TargetEntity")!=carrier)return;
                object observed;if(ObservedGoals.TryGetValue(entity,out observed)&&object.ReferenceEquals(observed,goal))return;
                bool started=(bool)NativeRead.Get(unit,"PresentationOnly")||Convert.ToInt32(NativeRead.Get(goal,"HangarDockingState"))!=0;
                if(started)return;
                object hangar=system.GetComponent<object>(carrier,NativeUnitBindings.Hangar);
                IDictionary assignments=hangar==null?null:NativeRead.Get(hangar,"mEntityToSlotMap") as IDictionary;
                object slot=assignments!=null&&assignments.Contains(entity)?assignments[entity]:null;
                bool reserved=slot!=null&&(Entity)NativeRead.Get(slot,"StoredEntity")==entity&&Convert.ToInt32(NativeRead.Get(slot,"Occupancy"))==1;
                if(!RhodesTiltrotorReturnPolicy.ShouldRetarget(true,true,reserved,started,false))return;
                UnitAttributes attributes=NativeRead.Get(unit,"Attributes") as UnitAttributes;
                if(attributes==null||(int)attributes.Class!=1572865)return;
                PruneKnown();
                if(!ObservedGoals.ContainsKey(entity)&&ObservedGoals.Count>=MaximumKnownBombers)return;
                object replacement=ExactGoalConstructor.Invoke(new object[]{carrier,attributes.Class});
                Vector2r oldOffset=(Vector2r)NativeRead.Get(goal,"mDockOffset"),exactOffset=(Vector2r)NativeRead.Get(replacement,"mDockOffset");
                if(!RhodesTiltrotorReturnPolicy.ShouldRetarget(true,true,reserved,started,oldOffset==exactOffset))
                {Remember(entity,goal);return;}
                bool changed=(bool)NativeRead.Call(GoalsType,"AddGoal",entity,replacement,SelectedUnitManager.AddMode.NewSet);
                if(changed)
                {
                    Remember(entity,replacement);
                    if(Reports++<16)Runtime.Log("RHODES_B01_RETURN_BAY_CORRECTED entity="+entity+"; carrier="+carrier+
                        "; oldOffset="+oldOffset+"; exactOffset="+exactOffset+"; sameNativeReservedSlot=True; nativeSortieContinues=True; transformsWritten=False; runtimeAcceptancePending=True");
                }
            }
            catch(Exception error)
            {
                Disabled=true;ObservedGoals.Clear();
                Runtime.Log("WARNING RHODES_B01_RETURN_CORRECTION_DISABLED "+error.GetType().Name+": "+error.Message+
                    "; nativeSortieContinues=True; sessionResetRequired=True; otherServicesContinue=True");
            }
        }
        internal static void Reset(){ObservedGoals.Clear();Removed.Clear();Session=null;Disabled=false;Reports=0;}
    }
}
