using System;

namespace EndfieldDoK.MapUnitLab
{
    // Fixed storage regardless of frame rate. Percentiles are upper bounds in
    // 0.25 ms bins up to 1024 ms; overflow percentiles use the actual worst frame.
    internal sealed class FrameWindowHistogram
    {
        readonly int[] Bins = new int[4097];
        public int Frames { get; private set; }
        public int Over33 { get; private set; }
        public int Over66 { get; private set; }
        public double TotalSeconds { get; private set; }
        public double WorstMs { get; private set; }

        public void Observe(double seconds)
        {
            if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return;
            double ms = seconds * 1000;
            int bin = ms >= 1024 ? 4096 : Math.Max(0, (int)Math.Ceiling(ms * 4) - 1);
            Bins[bin]++; Frames++; TotalSeconds += seconds;
            if (seconds > 1.0 / 30) Over33++;
            if (seconds > 1.0 / 15) Over66++;
            if (ms > WorstMs) WorstMs = ms;
        }

        public double PercentileMs(double fraction)
        {
            if (fraction <= 0 || fraction > 1 || double.IsNaN(fraction)) throw new ArgumentOutOfRangeException("fraction");
            if (Frames == 0) return 0;
            int rank = Math.Max(1, (int)Math.Ceiling(Frames * fraction)), count = 0;
            for (int i = 0; i < Bins.Length; i++)
            {
                count += Bins[i];
                if (count >= rank) return i == 4096 ? WorstMs : (i + 1) * .25;
            }
            return WorstMs;
        }

        public void Reset()
        {
            Array.Clear(Bins, 0, Bins.Length);
            Frames = Over33 = Over66 = 0; TotalSeconds = WorstMs = 0;
        }
    }
}
