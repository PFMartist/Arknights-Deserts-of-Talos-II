namespace EndfieldDoK.MapUnitLab
{
    // Shared planar pickup boundary: native Hover arrival may stop short of
    // the operator. It must agree with the later native-DockGoal handoff gate.
    internal static class RhodesOperatorPickupPolicy
    {
        internal const float TransferMetres=20f,RetargetMetres=4f;
        internal static bool WithinTransferRange(float distanceSquared)
        {return distanceSquared>=0f&&distanceSquared<=TransferMetres*TransferMetres;}
        internal static bool Arrived(float distanceSquared,float speedSquared)
        {return WithinTransferRange(distanceSquared)&&speedSquared>=0f&&speedSquared<=RhodesTiltrotorOrderPolicy.ArrivalSpeedMetresPerSecond*RhodesTiltrotorOrderPolicy.ArrivalSpeedMetresPerSecond;}
        internal static bool ReuseHover(bool samePassenger,bool holding,float distanceSquared,float speedSquared)
        {return samePassenger&&holding&&Arrived(distanceSquared,speedSquared);}
        internal static bool ReuseApproach(bool samePassenger,bool ownMove,float targetShiftSquared)
        {return samePassenger&&ownMove&&targetShiftSquared>=0f&&targetShiftSquared<=RetargetMetres*RetargetMetres;}
    }
}
