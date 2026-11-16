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
        static readonly string[] Names = { "bind", "hud", "humanoid", "faction", "aircraftFx", "carrier", "factionRig", "tracks", "hudIcons", "atlas", "humanoidSelection" };
        static readonly long[] Ticks = new long[11], Worst = new long[11];
        static readonly int[] Calls = new int[11];
        static float SessionStart, WindowStart;
        static int Reports;
        static bool Started;
        static bool Active { get { return Runtime.FrameTimingEnabled; } }
        // Keep early-match detail, then continue sampling late battles at a
        // bounded log rate. Fixed histogram/counter storage is reused forever.
        static float WindowSeconds { get { return Reports < 60 ? 10f : 60f; } }

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
            if (!Started) { SessionStart = WindowStart = now; Started = true; }
            Window.Observe(Time.unscaledDeltaTime);
            float duration = now - WindowStart;
            if (duration < WindowSeconds) return;
            string[] sections = new string[Names.Length];
            string[] perFrame = new string[Names.Length], callsPerFrame = new string[Names.Length];
            double toMs = 1000.0 / Stopwatch.Frequency;
            for (int i = 0; i < Names.Length; i++)
            {
                sections[i] = Names[i] + "=" + F(Calls[i] == 0 ? 0 : Ticks[i] * toMs / Calls[i]) + "/" + F(Worst[i] * toMs);
                perFrame[i] = Names[i] + "=" + F(Window.Frames == 0 ? 0 : Ticks[i] * toMs / Window.Frames);
                callsPerFrame[i] = Names[i] + "=" + F(Window.Frames == 0 ? 0 : (double)Calls[i] / Window.Frames);
            }
            Runtime.Log("RHODES_SKIRMISH_FRAME_TIMING elapsedSeconds=" + F(now - SessionStart) +
                "; windowSeconds=" + F(duration) + "; targetWindowSeconds=" + F(WindowSeconds) +
                "; trackedUnitViews=" + UnitViewDiscovery.Count +
                "; trackedUnitViewsScope=registeredNativeViews_notActiveOrVisibleCount" +
                "; vSyncCount=" + QualitySettings.vSyncCount + "; targetFrameRate=" + Application.targetFrameRate +
                "; frames=" + Window.Frames +
                "; meanMs=" + F(Window.Frames == 0 ? 0 : Window.TotalSeconds * 1000 / Window.Frames) +
                "; p50Ms=" + F(Window.PercentileMs(.50)) + "; p95Ms=" + F(Window.PercentileMs(.95)) +
                "; p99Ms=" + F(Window.PercentileMs(.99)) + "; worstMs=" + F(Window.WorstMs) +
                "; over33ms=" + Window.Over33 + "; over66ms=" + Window.Over66 +
                "; presentationSectionAvgMaxMs=" + string.Join(",", sections) +
                "; presentationSectionTotalMsPerFrame=" + string.Join(",", perFrame) +
                "; presentationSectionCallsPerFrame=" + string.Join(",", callsPerFrame) +
                "; factionRigAndTracksAreNested=True; sectionTimesNotAdditive=True" +
                "; percentileBinMs=0.25; overflowMs=1024; passiveOnly=True; nativeCpuGpuCostAndWindowFocusNotSeparated=True");
            if (Reports < int.MaxValue) Reports++;
            ClearWindow(); WindowStart = now;
        }
        static void ClearWindow()
        {
            Window.Reset();
            Array.Clear(Ticks, 0, Ticks.Length); Array.Clear(Worst, 0, Worst.Length); Array.Clear(Calls, 0, Calls.Length);
        }
        public static void Reset() { ClearWindow(); SessionStart = WindowStart = 0; Reports = 0; Started = false; }
        static string F(double value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }
    }
}
