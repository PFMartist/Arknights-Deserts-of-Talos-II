using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BBI.Core.ComponentModel;
using BBI.Game.Data;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Passive sim-thread observer. It never changes units, goals, time or commands.
    public static class HumanoidMovementDiagnostics
    {
        static uint LastReport;
        static bool ErrorReported;
        static MethodInfo ManeuverGetter;
        static readonly Dictionary<Entity, string> Previous = new Dictionary<Entity, string>();

        public static void Reset() { LastReport = 0; ErrorReported = false; Previous.Clear(); }

        public static void Tick(object sim)
        {
            try
            {
                uint frame = Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim, "GlobalFrameCount"), "FrameNumber"));
                if (LastReport != 0 && frame - LastReport < 16) return;
                LastReport = frame;
                foreach (object unit in (IEnumerable)NativeRead.Get(NativeRead.Get(sim, "UnitManager"), "Units"))
                {
                    string key = (string)NativeRead.Get(unit, "TypeName");
                    if (key != HumanoidUnits.PerlicaKey && key != HumanoidUnits.GuardSquadKey) continue;
                    Entity entity = (Entity)NativeRead.Get(unit, "Entity");
                    UnitState state = (UnitState)NativeRead.Call(unit, "GetState");
                    UnitDynamicState motion = state.StartOfFrameDynamicState;
                    UnitMovementAttributes configured = (UnitMovementAttributes)NativeRead.Get(unit, "MovementAttributes");
                    StringBuilder report = new StringBuilder();
                    report.Append("key=").Append(key).Append("; entity=").Append(entity)
                        .Append("; position=").Append(motion.Position).Append("; velocity=").Append(motion.Velocity)
                        .Append("; acceleration=").Append(motion.Acceleration).Append("; orientation=").Append(motion.Orientation)
                        .Append("; movementState=").Append(motion.MovementState)
                        .Append("; immobile=").Append(state.Immobile).Append("; stateMaxSpeed=").Append(state.MaxSpeed)
                        .Append("; status=").Append(state.Status).Append("; braking=").Append(state.BrakingToStop)
                        .Append("; idle=").Append(state.IsIdle).Append("; docked=").Append(state.IsDocked)
                        .Append("; suspended=").Append(state.IsSuspended)
                        .Append("; configuredDrive=").Append(configured == null ? "null" : configured.DriveType.ToString());
                    AppendDynamics(report, "configured", configured == null ? null : configured.Dynamics);
                    AppendDynamics(report, "effective", motion.Dynamics);
                    report.Append("; goals=").Append(Goals(state.AllGoals));
                    try
                    {
                        object maneuver = GetManeuver(entity);
                        if (maneuver != null)
                        {
                            int waypointCount = Convert.ToInt32(NativeRead.Get(maneuver, "WaypointCount"));
                            object behavior = NativeRead.Get(maneuver, "BehaviorOutput");
                            report.Append("; driverState=").Append(NativeRead.Get(maneuver, "DrivingState"))
                                .Append("; goalMode=").Append(NativeRead.Get(maneuver, "GoalManeuverMode"))
                                .Append("; waypointCount=").Append(waypointCount)
                                .Append("; destination=").Append(NativeRead.Get(maneuver, "CurrentDestination"))
                                .Append("; maneuverPlannedTime=").Append(NativeRead.Get(maneuver, "PlannedManeuverTime"))
                                .Append("; behaviorDriveSpeed=").Append(NativeRead.Get(behavior, "DriveSpeed"))
                                .Append("; behaviorDrivePoint=").Append(NativeRead.Get(behavior, "DriveToPosition"));
                            if (waypointCount > 0) report.Append("; waypoint=").Append(NativeRead.Get(maneuver, "CurrentWaypoint"));
                        }
                        else report.Append("; maneuverComponent=null");
                    }
                    catch (Exception error) { report.Append("; optionalManeuverReadError=").Append(error.GetType().Name).Append(':').Append(error.Message); }
                    string payload = report.ToString(), previous;
                    bool changed = !Previous.TryGetValue(entity, out previous) || previous != payload;
                    Previous[entity] = payload;
                    if (changed || frame % 80 < 16)
                        Runtime.Log("HUMANOID_MOVEMENT_DIAGNOSTIC frame=" + frame + "; " + payload + "; observerCommands=0");
                }
            }
            catch (Exception error)
            {
                if (!ErrorReported) { ErrorReported = true; Runtime.Log("HUMANOID_MOVEMENT_DIAGNOSTIC_ERROR passiveOnly=True; " + error); }
            }
        }

        static object GetManeuver(Entity entity)
        {
            if (ManeuverGetter == null)
            {
                Type extensions = typeof(Sim).Assembly.GetType("BBI.Game.Simulation.EntityComponentExtensions", true);
                Type maneuver = typeof(Sim).Assembly.GetType("BBI.Game.Simulation.Maneuvering", true);
                foreach (MethodInfo method in extensions.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    if (method.Name == "GetComponent" && method.IsGenericMethodDefinition && method.GetParameters().Length == 2)
                    { ManeuverGetter = method.MakeGenericMethod(new Type[] { maneuver }); break; }
                if (ManeuverGetter == null) throw new MissingMethodException(extensions.FullName, "GetComponent<T>(Entity, int)");
            }
            return ManeuverGetter.Invoke(null, new object[] { entity, 12 });
        }

        static void AppendDynamics(StringBuilder text, string label, UnitDynamicsAttributes dynamics)
        {
            if (dynamics == null) { text.Append("; ").Append(label).Append("Dynamics=null"); return; }
            text.Append("; ").Append(label).Append("DynamicsType=").Append(dynamics.GetType().FullName)
                .Append("; ").Append(label).Append("MaxSpeed=").Append(dynamics.MaxSpeed)
                .Append("; ").Append(label).Append("MinCruiseSpeed=").Append(dynamics.MinCruiseSpeed)
                .Append("; ").Append(label).Append("PermanentlyImmobile=").Append(dynamics.PermanentlyImmobile)
                .Append("; ").Append(label).Append("AccelerationTime=").Append(dynamics.AccelerationTime)
                .Append("; ").Append(label).Append("TurnRadius=").Append(dynamics.MaxSpeedTurnRadius);
        }

        static string Goals(EntityGoalState[] goals)
        {
            if (goals == null || goals.Length == 0) return "none";
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < goals.Length && i < 6; i++)
            {
                if (i > 0) text.Append('|');
                EntityGoalState goal = goals[i];
                text.Append(goal.GoalType).Append('[');
                if (goal.GoalWaypoints != null)
                {
                    text.Append("n=").Append(goal.GoalWaypoints.Length);
                    if (goal.GoalWaypoints.Length > 0)
                        text.Append(",first=").Append(goal.GoalWaypoints[0]).Append(",last=").Append(goal.GoalWaypoints[goal.GoalWaypoints.Length - 1]);
                }
                text.Append(']');
            }
            return text.ToString();
        }
    }
}
