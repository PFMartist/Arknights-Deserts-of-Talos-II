using System;

namespace EndfieldDoK.MapUnitLab
{
    internal enum RhodesTiltrotorMotionState { Hover,LowSpeed,Forward,Reverse,Strafe,Landing,Guided }
    internal struct RhodesTiltrotorMotionPose
    {
        internal RhodesTiltrotorMotionState State;
        internal float NacelleDegrees;
    }
    internal static class RhodesTiltrotorMotionProfile
    {
        internal const float MinimumTilt=-20f,MaximumTilt=75f,TiltDegreesPerSecond=50f;
        static float Smooth(float x){x=Math.Max(0f,Math.Min(1f,x));return x*x*(3f-2f*x);}
        internal static RhodesTiltrotorMotionPose Evaluate(float forward,float lateral,bool holding,bool landing,bool guided,float guideAngle)
        {
            RhodesTiltrotorMotionPose p=new RhodesTiltrotorMotionPose();
            if(guided){p.State=RhodesTiltrotorMotionState.Guided;p.NacelleDegrees=Clamp(guideAngle);return p;}
            if(landing){p.State=RhodesTiltrotorMotionState.Landing;return p;}
            float speed=(float)Math.Sqrt(forward*forward+lateral*lateral);
            if(speed<2f){p.State=RhodesTiltrotorMotionState.Hover;return p;}
            if(forward< -2f){p.State=RhodesTiltrotorMotionState.Reverse;p.NacelleDegrees=MinimumTilt*Smooth((-forward-2f)/18f);return p;}
            if(Math.Abs(lateral)>3f&&Math.Abs(forward)<4f){p.State=RhodesTiltrotorMotionState.Strafe;return p;}
            p.State=forward>=15f&&!holding?RhodesTiltrotorMotionState.Forward:RhodesTiltrotorMotionState.LowSpeed;
            p.NacelleDegrees=MaximumTilt*Smooth((forward-2f)/58f);
            return p;
        }
        static float Clamp(float value){return Math.Max(MinimumTilt,Math.Min(MaximumTilt,value));}
        // Phase advances with simulation display time. Native LOD controls
        // which cached nodes are written; there is no independent wall clock.
    }
}
