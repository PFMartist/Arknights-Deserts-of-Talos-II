using System;

namespace EndfieldDoK.MapUnitLab
{
    // Measured from the frozen V16.1 source and its accepted metre-space RHS.
    // Pure data/math: no Unity, native calls or per-frame file operations.
    internal static class RhodesTiltrotorProfile
    {
        internal const string BayName="EF_Rhodes_Tiltrotor";
        internal const string LaunchBone="EF_VT_UndockControl_01",DockBone="EF_VT_DockControl_01";
        internal const string HullSha256="7784b454356c9fcefcb9bdf91d099fbe470a65d8b27cebe20ec5369cc39fc2ff";
        // Air | Large | Strikecraft. A separate existing size category keeps
        // native class-only hangar events unambiguous; no new enum bit exists.
        internal const int AircraftClass=1+262144+1048576,Capacity=1;
        // Includes clearance above the small physical transverse runway lip.
        internal const float RunwayX=-114.16756f,RunwayY=64.10f,StartZ=-115f,ExitZ=180f;
        internal const float ReadySeconds=5f,ReleaseSeconds=9f,NativeSequenceSeconds=8.875f,WholeBaySeconds=9.625f;
        internal const float LiftSeconds=.125f,ReleaseSpeed=90f,BringInSeconds=8f;
        internal const float AlignmentSeconds=5f,ApproachSeconds=6f;
        internal const float FlightRootY=129.85247f;
        // Final LH geometry: the independent rectangular VT platform beside
        // the main module, not the three upper-deck interceptor elevators.
        internal const float LiftX=-70.35685f,LiftZ=-5.039889f;
        internal const float CaptureRootY=100.75f,LiftTravel=24.419152f;
        internal const float RecoveryHoverY=140.91528f,RecoveryOutsideX=-179.17273f;
        // Build12339551 DockingCoordinator adds100m above the guide for Air
        // during alignment, then removes it throughout the native approach.
        internal const float NativeAirAlignmentHeight=100f;
        internal const float RecoveryFlightAltitude=RecoveryHoverY+NativeAirAlignmentHeight;
        internal const float HatchOpenSeconds=3f,PlatformRaiseSeconds=5f;
        internal const float FacilityDeploymentSeconds=HatchOpenSeconds+PlatformRaiseSeconds,LiftResetSeconds=5f;
        internal struct Point
        {
            internal float X,Y,Z;
            internal Point(float x,float y,float z){X=x;Y=y;Z=z;}
        }
        internal static Point Start {get{return new Point(RunwayX,RunwayY,StartZ);}}
        internal static Point Exit {get{return new Point(RunwayX,FlightRootY,ExitZ);}}
        internal static Point Approach {get{return new Point(RecoveryOutsideX,RecoveryHoverY,LiftZ);}}
        internal static Point OverLift {get{return new Point(LiftX,RecoveryHoverY,LiftZ);}}
        internal static Point Capture {get{return new Point(LiftX,CaptureRootY,LiftZ);}}
        internal static Point Storage {get{return new Point(LiftX,CaptureRootY-LiftTravel,LiftZ);}}
        internal static float Smooth(float value)
        {float t=Math.Max(0f,Math.Min(1f,value));return t*t*(3f-2f*t);}
        internal static float RecoveryHandoffRemaining(int nativePhase,float nativeProgress)
        {return nativePhase==4?1f-Smooth(nativeProgress):0f;}
        internal static Point Mix(Point a,Point b,float t)
        {return new Point(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t,a.Z+(b.Z-a.Z)*t);}
        internal static Point Launch(float age)
        {
            if(float.IsNaN(age)||float.IsInfinity(age))throw new ArgumentOutOfRangeException("age");
            if(age<=ReadySeconds)return Start;if(age>=ReleaseSeconds)return Exit;
            double t=(age-ReadySeconds)/(ReleaseSeconds-ReadySeconds);
            double power=ReleaseSpeed*(ReleaseSeconds-ReadySeconds)/(ExitZ-StartZ);
            float p=(float)Math.Pow(t,power);
            return new Point(RunwayX,RunwayY+(FlightRootY-RunwayY)*Smooth((p-.6f)/.4f),StartZ+(ExitZ-StartZ)*p);
        }
        internal static Point Receive(float age)
        {
            float half=ApproachSeconds*.5f,t=age-AlignmentSeconds;
            return t<=half?Mix(Approach,OverLift,Smooth(t/half)):
                Mix(OverLift,Capture,Smooth((t-half)/half));
        }
        internal static Point Stow(float age)
        {return Mix(Capture,Storage,Smooth((age-.5f)/(BringInSeconds-.5f)));}
        internal static float LaunchNacelleAngle(float age)
        {return 75f*Smooth((age-ReadySeconds)/(ReleaseSeconds-ReadySeconds));}
        internal static float ReceiveNacelleAngle(float age)
        {return 75f*(1f-Smooth(age/3f));}
        internal static float CruiseNacelleAngle(float horizontalSpeed)
        {return 75f*Smooth((Math.Max(0f,horizontalSpeed)-2f)/58f);}
        internal static float FacilityHatch(float unlockAge)
        {return unlockAge<0f?0f:Smooth(unlockAge/HatchOpenSeconds);}
        internal static float FacilityLiftOffset(float unlockAge)
        {return -LiftTravel*(1f-(unlockAge<0f?0f:Smooth((unlockAge-HatchOpenSeconds)/PlatformRaiseSeconds)));}
        internal static float RecoveryLiftOffset(float stowAge)
        {return Stow(stowAge).Y-CaptureRootY;}
        internal static float ResetLiftOffset(float resetAge)
        {return -LiftTravel*(1f-Smooth(resetAge/LiftResetSeconds));}
    }
}
