using System;
using System.Diagnostics;
using System.Globalization;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Opt-in independently of detailed diagnostics. No quality, simulation or
    // frame-cap changes; no per-frame allocation or IO. This is not a GPU profiler.
    internal static class RhodesSkirmishFrameTiming
    {
        static readonly FrameWindowHistogram Window = new FrameWindowHistogram();
        static readonly string[] Names = { "bind", "hud", "humanoid", "faction", "aircraftFx", "carrier" };
        static readonly long[] Ticks = new long[6], Worst = new long[6];
        static readonly int[] Calls = new int[6];
        static float WindowStart;
        static int Reports;
        static bool Active { get { return Runtime.FrameTimingEnabled && Reports < 60; } }

        public static long Begin() { return Active ? Stopwatch.GetTimestamp() : 0L; }
        public static void End(int section, long start)
        {
            if (!Active || start == 0) return;
            long elapsed = Stopwatch.GetTimestamp() - start;
            Ticks[section] += elapsed; Calls[section]++;
            if (elapsed > Worst[section]) Worst[section] = elapsed;
        }
        public static void Frame()
        {
            if (!Active) return;
            float now = Time.realtimeSinceStartup;
            if (WindowStart == 0) WindowStart = now;
            Window.Observe(Time.unscaledDeltaTime);
            if (now - WindowStart < 10f) return;
            string[] sections = new string[Names.Length];
            double toMs = 1000.0 / Stopwatch.Frequency;
            for (int i = 0; i < Names.Length; i++)
                sections[i] = Names[i] + "=" + F(Calls[i] == 0 ? 0 : Ticks[i] * toMs / Calls[i]) + "/" + F(Worst[i] * toMs);
            Runtime.Log("RHODES_SKIRMISH_FRAME_TIMING frames=" + Window.Frames +
                "; meanMs=" + F(Window.Frames == 0 ? 0 : Window.TotalSeconds * 1000 / Window.Frames) +
                "; p50Ms=" + F(Window.PercentileMs(.50)) + "; p95Ms=" + F(Window.PercentileMs(.95)) +
                "; p99Ms=" + F(Window.PercentileMs(.99)) + "; worstMs=" + F(Window.WorstMs) +
                "; over33ms=" + Window.Over33 + "; over66ms=" + Window.Over66 +
                "; presentationSectionAvgMaxMs=" + string.Join(",", sections) +
                "; percentileBinMs=0.25; overflowMs=1024; passiveOnly=True; nativeCpuGpuCostAndWindowFocusNotSeparated=True");
            Reports++; ClearWindow(); WindowStart = now;
        }
        static void ClearWindow()
        {
            Window.Reset();
            Array.Clear(Ticks, 0, Ticks.Length); Array.Clear(Worst, 0, Worst.Length); Array.Clear(Calls, 0, Calls.Length);
        }
        public static void Reset() { ClearWindow(); WindowStart = 0; Reports = 0; }
        static string F(double value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }
    }
}
