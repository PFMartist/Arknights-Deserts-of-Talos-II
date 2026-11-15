using System;

namespace EndfieldDoK.MapUnitLab
{
    // Simulation presentation clock only; no Unity, HP, orders or damage.
    internal sealed class HumanoidDeathPlayback
    {
        public const float BlendInSeconds=.12f,HoldSeconds=2f,FallbackFallSeconds=.45f;
        public readonly bool HasSourceClip;
        public readonly float ClipSeconds,LifetimeSeconds;
        double Age;
        public HumanoidDeathPlayback(float clipSeconds)
        {
            HasSourceClip=Finite(clipSeconds)&&clipSeconds>0f;
            ClipSeconds=HasSourceClip?clipSeconds:FallbackFallSeconds;
            LifetimeSeconds=HasSourceClip?ClipSeconds+HoldSeconds:4f;
        }
        public float Elapsed { get { return (float)Age; } }
        public float PoseTime { get { return (float)Math.Min(Age,ClipSeconds); } }
        public bool Finished { get { return Age>=LifetimeSeconds; } }
        public float Blend
        {
            get { float t=(float)Math.Min(1d,Age/BlendInSeconds);return t*t*(3f-2f*t); }
        }
        public void Step(float delta)
        {
            if(!Finite(delta)||delta<=0f||Finished)return;
            Age=Math.Min(LifetimeSeconds,Age+delta);
        }
        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
    }
}
