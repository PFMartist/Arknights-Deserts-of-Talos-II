using System;

namespace EndfieldDoK.MapUnitLab
{
    // One timing contract for the private native bomber bay, its authored
    // aircraft/lift motion and the simulation-owned shared-lane tail. The
    // native DockingCoordinator's carrier-wide 5s alignment + 6s approach
    // remains unchanged; changing only its DeployQueue would desynchronise it.
    internal static class RhodesBomberRecoveryProfile
    {
        internal const float AlignmentSeconds=5f;
        internal const float ApproachSeconds=6f;
        internal const float TotalApproachSeconds=AlignmentSeconds+ApproachSeconds;
        internal const float BomberBringInSeconds=3f;
        internal const float StandardBringInSeconds=8f;
        internal const float BomberResetSeconds=2f;
        internal const float StandardResetSeconds=4f;
        internal const int BomberResetTicks=16;
        internal const int StandardResetTicks=32;
        internal static float BringInSeconds(bool bomber)
        {return bomber?BomberBringInSeconds:StandardBringInSeconds;}
        internal static float ResetSeconds(bool bomber)
        {return bomber?BomberResetSeconds:StandardResetSeconds;}
        internal static int SharedResetTicks(int completedDockMask)
        {
            // A malformed mixed snapshot takes the longer, conservative tail.
            return completedDockMask==RhodesSharedAircraftLane.BomberDock?BomberResetTicks:StandardResetTicks;
        }
        static float Fraction(float age,float seconds)
        {return float.IsNaN(age)||age<=0f?0f:age>=seconds?1f:age/seconds;}
        internal static float RecoveryPathTime(bool bomber,int mode,float age)
        {return mode==1?5f*Fraction(age,TotalApproachSeconds):5f+13f*Fraction(age,BringInSeconds(bomber));}
        internal static float RecoveryResetPathTime(float age,float resetSeconds)
        {return 18f+6f*Fraction(age,resetSeconds);}
    }
}
