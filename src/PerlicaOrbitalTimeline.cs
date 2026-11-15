using System;

namespace EndfieldDoK.MapUnitLab
{
    // Presentation phases only. Actual native weapon/projectile observations
    // move the boundaries; elapsed time can never create a shot or impact.
    public sealed class PerlicaOrbitalTimeline
    {
        public enum Phase { None, Charging, InTransit, Firing, Afterglow }
        public const float ChargeSeconds = 2.5f, FireSeconds = 1f, AfterglowSeconds = .8f;
        public Phase Current { get; private set; }
        public float Age { get; private set; }
        public bool Active { get { return Current != Phase.None; } }
        public bool Channeling { get { return Current == Phase.Charging || Current == Phase.InTransit || Current == Phase.Firing; } }
        public void Begin() { Current = Phase.Charging; Age = 0f; }
        public void Launched()
        {
            if (Current == Phase.None) Begin();
            if (Current == Phase.Charging) { Current = Phase.InTransit; Age = ChargeSeconds - .001f; }
        }
        public void Arrived()
        {
            if (Current == Phase.Firing || Current == Phase.Afterglow) return;
            Current = Phase.Firing; Age = ChargeSeconds;
        }
        public void SetNativeDetonationProgress(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) throw new ArgumentOutOfRangeException("seconds");
            Arrived();
            if (Current == Phase.Firing) Age = ChargeSeconds + Math.Min(FireSeconds - .001f, seconds);
        }
        public void Detonated() { if (Current != Phase.Afterglow) { Current = Phase.Afterglow; Age = ChargeSeconds + FireSeconds; } }
        public void CancelBeforeLaunch() { if (Current == Phase.Charging) Reset(); }
        public void Reset() { Current = Phase.None; Age = 0f; }
        public void Step(float delta)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0f) throw new ArgumentOutOfRangeException("delta");
            if (delta == 0f || Current == Phase.None) return;
            if (Current == Phase.Charging) Age = Math.Min(ChargeSeconds - .001f, Age + delta);
            else if (Current == Phase.Firing) Age = Math.Min(ChargeSeconds + FireSeconds - .001f, Age + delta);
            else if (Current == Phase.Afterglow)
            {
                Age += delta;
                if (Age >= ChargeSeconds + FireSeconds + AfterglowSeconds) Reset();
            }
        }
    }
}
