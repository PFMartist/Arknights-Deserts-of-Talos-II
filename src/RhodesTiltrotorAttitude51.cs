using System;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // U08 presentation only. Native air physics can integrate toward a vertical
    // pitch while horizontal speed is zero. Never reveal that accumulated pose
    // when crossing the former 8m/s upright/native-attitude switch.
    internal sealed class RhodesTiltrotorAttitude51
    {
        internal const float MaximumPitch=8f,MaximumRoll=12f,PitchRate=20f,RollRate=25f;
        internal float Pitch,Roll;
        internal void Reset(){Pitch=Roll=0f;}
        internal Quaternion Step(Quaternion heading,Quaternion native,float speed,float delta,bool level)
        {
            Quaternion relative=RhodesAviationPoseMath.Multiply(RhodesAviationPoseMath.Inverse(heading),native);
            float pitch,roll;ReadAngles(relative,out pitch,out roll);
            float weight=level?0f:SpeedWeight(speed);
            float dt=Finite(delta)?Math.Max(0f,Math.Min(.1f,delta)):0f;
            Pitch=Approach(Pitch,Clamp(pitch,-MaximumPitch,MaximumPitch)*weight,PitchRate*dt);
            Roll=Approach(Roll,Clamp(roll,-MaximumRoll,MaximumRoll)*weight,RollRate*dt);
            double px=Pitch*Math.PI/360d,rz=Roll*Math.PI/360d;
            Quaternion x=new Quaternion((float)Math.Sin(px),0f,0f,(float)Math.Cos(px));
            Quaternion z=new Quaternion(0f,0f,(float)Math.Sin(rz),(float)Math.Cos(rz));
            return RhodesAviationPoseMath.Multiply(heading,RhodesAviationPoseMath.Multiply(x,z));
        }
        internal static float SpeedWeight(float speed)
        {
            if(!Finite(speed))return 0f;
            float t=Clamp((speed-2f)/58f,0f,1f);return t*t*(3f-2f*t);
        }
        internal static void ReadAngles(Quaternion q,out float pitch,out float roll)
        {
            double n=(double)q.x*q.x+(double)q.y*q.y+(double)q.z*q.z+(double)q.w*q.w;
            if(double.IsNaN(n)||double.IsInfinity(n)||n<.000001d){pitch=roll=0f;return;}
            double k=2d/n;
            double fx=k*(q.x*q.z+q.w*q.y),fy=k*(q.y*q.z-q.w*q.x),fz=1d-k*(q.x*q.x+q.y*q.y);
            double ux=k*(q.x*q.y-q.w*q.z),uy=1d-k*(q.x*q.x+q.z*q.z);
            pitch=(float)(-Math.Atan2(fy,Math.Sqrt(fx*fx+fz*fz))*180d/Math.PI);
            roll=(float)(Math.Atan2(-ux,uy)*180d/Math.PI);
            // A nearly vertical native forward makes roll projection singular.
            // It is the hover accumulation we are rejecting, not a bank cue.
            if(Math.Abs(pitch)>60f)roll=0f;
        }
        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        static float Clamp(float value,float low,float high){return Math.Max(low,Math.Min(high,value));}
        static float Approach(float value,float target,float amount)
        {return value<target?Math.Min(target,value+amount):Math.Max(target,value-amount);}
    }
}
