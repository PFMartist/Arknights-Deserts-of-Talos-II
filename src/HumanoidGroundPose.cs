using System;

namespace EndfieldDoK.MapUnitLab
{
    public struct HumanoidGroundSurface
    {
        public bool NativeSample;
        public FactionPoseVector Point,Normal;
        public double FallbackHeight;
    }
    // Pure presentation math. Simulation XZ, formation speed and actor scale
    // remain untouched; no new terrain queries or simulation state writes.
    public static class HumanoidGroundPose
    {
        public static HumanoidGroundSurface Capture(FactionPoseVector simulation,FactionPoseVector nativePosition,
            double settleOffset,bool terrainCollider,FactionPoseVector point,FactionPoseVector normal)
        {
            double fallback=FactionGroundPose.Finite(nativePosition)&&FactionGroundPose.Finite(settleOffset)?
                nativePosition.Y-settleOffset:FactionGroundPose.Finite(simulation.Y)?simulation.Y:0;
            bool sample=terrainCollider&&FactionGroundPose.PlausiblePoint(point,nativePosition,simulation);
            return new HumanoidGroundSurface{NativeSample=sample,FallbackHeight=fallback,
                Point=sample?point:new FactionPoseVector(simulation.X,fallback,simulation.Z),
                Normal=sample&&FactionGroundPose.UsableNormal(normal)?normal:new FactionPoseVector(0,1,0)};
        }
        public static double HeightAt(HumanoidGroundSurface surface,double x,double z)
        {
            if(!surface.NativeSample||!FactionGroundPose.Finite(x)||!FactionGroundPose.Finite(z))return surface.FallbackHeight;
            double height=FactionGroundPose.HeightAt(surface.Point,surface.Normal,x,z);
            return FactionGroundPose.Finite(height)?height:surface.FallbackHeight;
        }
    }
}
