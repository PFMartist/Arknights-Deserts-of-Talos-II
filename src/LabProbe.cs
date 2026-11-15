using System;
using System.Collections;
using System.IO;
using BBI.Core;
using BBI.Core.ComponentModel;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Actual native-command arrival on an independent flat map (or explicit native-only unit test); no unit position writes.
    public static class LabProbe
    {
        static readonly bool Enabled = File.Exists(Path.Combine(Runtime.LabDirectory, "MapUnitLab.autoprobe"));
        static bool Started, Finished;
        static uint FirstFrame, LastReport;
        static Vector2r Start, Goal;
        static Fixed64 InitialDistance, ArrivalDistance;
        static int Samples;
        static float MaxAbsFlatHeight;

        public static void Tick(object sim, Entity entity)
        {
            if (!Enabled || Finished) return;
            object unit = null;
            foreach (object value in (IEnumerable)NativeRead.Get(NativeRead.Get(sim, "UnitManager"), "Units"))
                if (((Entity)NativeRead.Get(value, "Entity")).Equals(entity)) { unit = value; break; }
            if (unit == null) throw new Exception("Probe custom unit disappeared");
            object state = NativeRead.Call(unit, "GetState");
            Vector2r position = (Vector2r)NativeRead.Get(NativeRead.Get(state, "StartOfFrameDynamicState"), "Position");
            uint frame = Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim, "GlobalFrameCount"), "FrameNumber"));
            if (!Started)
            {
                Start = position; Goal = Runtime.UnitsOnly ? CustomUnits.NativeTestGoal : CustomFlatMap.Goal;
                InitialDistance = (Goal - Start).Length;
                UnitMovementAttributes movement = (UnitMovementAttributes)NativeRead.Get(unit, "MovementAttributes");
                ArrivalDistance = movement.Maneuvers.ArrivalDistance;
                Type factory = typeof(Sim).Assembly.GetType("BBI.Game.Commands.SimCommandFactory", true);
                object command = NativeRead.Call(factory, "CreateUnitMoveToCommand", new CommanderID(1), new Entity[] { entity }, Goal, false);
                if (!(bool)NativeRead.Call(command, "RunCommand", sim, NativeRead.Get(sim, "InteractionProvider"), NativeRead.Get(sim, "GlobalFrameCount"))) throw new Exception("Native movement command rejected");
                FirstFrame = frame; Started = true;
                Runtime.Log("PROBE movement START: entity=" + entity + "; start=" + Start + "; goal=" + Goal + "; initialDistance=" + InitialDistance + "; nativeArrivalDistance=" + ArrivalDistance + "; no position writes.");
            }
            Fixed64 displacement = (position - Start).Length;
            Fixed64 remaining = (Goal - position).Length;
            object heightMap = NativeRead.Get(NativeRead.Get(sim, "Map"), "HeightMap");
            float actualHeight = Fixed64.UnsafeFloatValue((Fixed64)NativeRead.Call(heightMap, "GetWorldHeight", position));
            if (!Runtime.UnitsOnly) MaxAbsFlatHeight = Math.Max(MaxAbsFlatHeight, Math.Abs(actualHeight));
            if (Samples == 0 || frame - LastReport >= 16)
            {
                Runtime.Log("PROBE path sample: frame=" + frame + "; position=" + position + "; displacement=" + displacement + "; remaining=" + remaining + "; actualSimHeight=" + actualHeight);
                LastReport = frame; Samples++;
            }
            // Both actual arrival (native tolerance) and substantial route progress are required.
            if (remaining <= ArrivalDistance && InitialDistance - remaining >= InitialDistance / Fixed64.FromInt(2) && Samples >= 3 && (Runtime.UnitsOnly || MaxAbsFlatHeight <= 0.05f))
            {
                Finish("PASS", (Runtime.UnitsOnly ? "independent unit native arrival on original map; remaining=" : "native arrival across independent flat map; remaining=") + remaining + "; tolerance=" + ArrivalDistance + "; displacement=" + displacement + "; maxAbsFlatHeight=" + MaxAbsFlatHeight + "; samples=" + Samples + "; frames=" + (frame - FirstFrame));
                return;
            }
            if (frame - FirstFrame >= 1600) Finish("FAIL", "native arrival or route evidence timeout; remaining=" + remaining + "; tolerance=" + ArrivalDistance + "; displacement=" + displacement + "; maxAbsFlatHeight=" + MaxAbsFlatHeight + "; samples=" + Samples);
        }
        static void Finish(string result, string message)
        {
            Finished = true;
            Runtime.Log("PROBE movement " + result + ": " + message + ". This does not verify combat or a complete original map import.");
            File.WriteAllText(Path.Combine(Runtime.LabDirectory, "MapUnitLab.probe.result.txt"), result + Environment.NewLine + message + Environment.NewLine);
        }
    }
}
