using System;
using System.Diagnostics;
using System.Globalization;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Bounded passive measurements. No profiling API, frame cap, time scale or
    // quality change. Section counters are written on the presentation thread.
    internal static class LabFrameTiming
    {
        static readonly string[] Names = { "map", "camera", "unitDiscovery", "hud", "carrier" };
        static readonly long[] Ticks = new long[5], Worst = new long[5];
        static readonly int[] Calls = new int[5];
        static double TotalSeconds;
        static float WindowStart, WorstFrame;
        static int Frames, Over33, Over66, Reports;
        public static long Begin() { return Runtime.DetailedDiagnosticsEnabled && Reports < 60 ? Stopwatch.GetTimestamp() : 0L; }
        public static void End(int section, long start)
        {
            if (!Runtime.DetailedDiagnosticsEnabled || Reports >= 60) return;
            long elapsed = Stopwatch.GetTimestamp() - start;
            Ticks[section] += elapsed; Calls[section]++;
            if (elapsed > Worst[section]) Worst[section] = elapsed;
        }
        public static void Frame()
        {
            if (!Runtime.DetailedDiagnosticsEnabled || Reports >= 60) return;
            float now = Time.realtimeSinceStartup;
            if (WindowStart == 0f) WindowStart = now;
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f)
            {
                Frames++; TotalSeconds += dt; WorstFrame = Math.Max(WorstFrame, dt);
                if (dt > .033333f) Over33++;
                if (dt > .066667f) Over66++;
            }
            if (now - WindowStart < 10f) return;
            string[] sections = new string[Names.Length];
            double toMs = 1000.0 / Stopwatch.Frequency;
            for (int i = 0; i < Names.Length; i++)
            {
                sections[i] = Names[i] + "=" + F(Calls[i] == 0 ? 0 : Ticks[i] * toMs / Calls[i]) + "/" + F(Worst[i] * toMs);
                Ticks[i] = Worst[i] = 0; Calls[i] = 0;
            }
            Runtime.Log("LAB_FRAME_TIMING frames=" + Frames + "; meanMs=" + F(Frames == 0 ? 0 : TotalSeconds * 1000 / Frames) +
                "; worstMs=" + F(WorstFrame * 1000) + "; over33ms=" + Over33 + "; over66ms=" + Over66 +
                "; bridgeSectionAvgMaxMs=" + string.Join(",", sections) +
                "; passiveOnly=True; nativeCpuGpuCostAndWindowFocusNotSeparated=True");
            Reports++; WindowStart = now; Frames = Over33 = Over66 = 0;
            TotalSeconds = 0; WorstFrame = 0;
        }
        public static void Reset()
        {
            Array.Clear(Ticks, 0, Ticks.Length); Array.Clear(Worst, 0, Worst.Length); Array.Clear(Calls, 0, Calls.Length);
            WindowStart = WorstFrame = 0;
            TotalSeconds = 0; Frames = Over33 = Over66 = Reports = 0;
        }
        static string F(double value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }
    }
}
