namespace EndfieldDoK.MapUnitLab
{
    public struct FactionNativeGroundCalibration
    {
        public double SettleOffset,BottomWorld;
    }

    // A datum adapter only: UnitView's displayed position and rotation remain
    // authoritative. No terrain plane fitting, animation clock or body filter.
    public static class FactionNativeGroundPose
    {
        public static bool TryCalibrate(double settleOffset,double modelBottom,double metreScale,
            out FactionNativeGroundCalibration calibration)
        {
            calibration=new FactionNativeGroundCalibration();
            if(!FactionGroundPose.Finite(settleOffset)||!FactionGroundPose.Finite(modelBottom)||
                !FactionGroundPose.Finite(metreScale)||metreScale<=0)return false;
            double bottom=modelBottom*metreScale;
            if(!FactionGroundPose.Finite(bottom))return false;
            calibration=new FactionNativeGroundCalibration{SettleOffset=settleOffset,BottomWorld=bottom};
            return true;
        }

        public static bool UseCustomBootstrap(bool customTerrain,bool nativeSampleAvailable,ref bool nativeAuthorityReady)
        {
            if(nativeSampleAvailable)nativeAuthorityReady=true;
            return customTerrain&&!nativeAuthorityReady;
        }

        public static bool TryAdapt(FactionPoseVector nativePosition,FactionPoseVector right,FactionPoseVector up,
            FactionPoseVector forward,FactionNativeGroundCalibration calibration,out FactionGroundPoseResult pose)
        {
            pose=new FactionGroundPoseResult();
            if(!FactionGroundPose.Finite(nativePosition)||!Unit(right)||!Unit(up)||!Unit(forward)||
                !FactionGroundPose.Finite(calibration.SettleOffset)||!FactionGroundPose.Finite(calibration.BottomWorld)||
                System.Math.Abs(FactionGroundPose.Dot(right,up))>.001||
                System.Math.Abs(FactionGroundPose.Dot(right,forward))>.001||
                System.Math.Abs(FactionGroundPose.Dot(up,forward))>.001)return false;
            // Native nonphysical ground placement adds settle along WORLD Y.
            // Authored bottomY belongs to model space, so remove it along the
            // displayed body's UP. Mixing these axes makes pitched cars hop.
            FactionPoseVector position=new FactionPoseVector(nativePosition.X-up.X*calibration.BottomWorld,
                nativePosition.Y-calibration.SettleOffset-up.Y*calibration.BottomWorld,
                nativePosition.Z-up.Z*calibration.BottomWorld);
            if(!FactionGroundPose.Finite(position))return false;
            pose=new FactionGroundPoseResult{Position=position,Right=right,Up=up,Forward=forward};
            return true;
        }
        static bool Unit(FactionPoseVector value)
        {return FactionGroundPose.Finite(value)&&System.Math.Abs(FactionGroundPose.Dot(value,value)-1.0)<.001;}
    }
}
