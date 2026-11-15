using System;
using BBI.Core.ComponentModel;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    internal struct RhodesAirWingGuideState
    {
        internal Entity Owner;internal bool Active;internal RhodesAirWingStage Stage;internal float Fraction;
        internal void Set(Entity owner,RhodesAirWingStage stage,float fraction)
        {Owner=owner;Active=stage!=RhodesAirWingStage.Flight;Stage=stage;Fraction=Math.Max(0f,Math.Min(1f,fraction));}
        internal bool BelongsTo(Entity owner){return !owner.Equals(Entity.None)&&Owner.Equals(owner);}
        internal void Clear(){Owner=Entity.None;Active=false;Stage=RhodesAirWingStage.Flight;Fraction=0f;}
    }
    // Managed quaternion math is shared by the production geometry adapter and
    // the offline native-type fixture. No Unity internal calls or scene state.
    internal static class RhodesAviationPoseMath
    {
        internal const float ReleaseSeconds=.65f;
        internal static Quaternion YawDegrees(float degrees)
        {double half=degrees*Math.PI/360d;return new Quaternion(0f,(float)Math.Sin(half),0f,(float)Math.Cos(half));}
        internal static Quaternion Multiply(Quaternion a,Quaternion b)
        {return new Quaternion(a.w*b.x+a.x*b.w+a.y*b.z-a.z*b.y,a.w*b.y-a.x*b.z+a.y*b.w+a.z*b.x,
            a.w*b.z+a.x*b.y-a.y*b.x+a.z*b.w,a.w*b.w-a.x*b.x-a.y*b.y-a.z*b.z);}
        internal static Quaternion Inverse(Quaternion q)
        {float n=q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;return new Quaternion(-q.x/n,-q.y/n,-q.z/n,q.w/n);}
        internal static Quaternion Blend(Quaternion a,Quaternion b,float fraction)
        {
            float t=Math.Max(0f,Math.Min(1f,fraction));double dot=a.x*b.x+a.y*b.y+a.z*b.z+a.w*b.w;
            if(dot<0d){dot=-dot;b=new Quaternion(-b.x,-b.y,-b.z,-b.w);}
            double left=1d-t,right=t;
            if(dot<.9995d){double angle=Math.Acos(Math.Min(1d,dot)),sine=Math.Sin(angle);left=Math.Sin((1d-t)*angle)/sine;right=Math.Sin(t*angle)/sine;}
            Quaternion q=new Quaternion((float)(left*a.x+right*b.x),(float)(left*a.y+right*b.y),(float)(left*a.z+right*b.z),(float)(left*a.w+right*b.w));
            float norm=(float)Math.Sqrt(q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w);return new Quaternion(q.x/norm,q.y/norm,q.z/norm,q.w/norm);
        }
        internal static Quaternion GuideLocalRotation(Quaternion parent,Quaternion authored,Quaternion guide,float weight)
        {return Multiply(Multiply(Inverse(parent),Blend(parent,guide,weight)),authored);}
        internal static float RecoveryContactWeight(int mode,float age)
        {return mode==1?RhodesAviationLaunchProfile.SmoothFraction(age/RhodesBomberRecoveryProfile.TotalApproachSeconds):1f;}
        internal static float ReleaseFraction(float age)
        {return RhodesAviationLaunchProfile.SmoothFraction(age/ReleaseSeconds);}
        internal static Quaternion ReleaseLocalRotation(Quaternion start,Quaternion authored,float age)
        {return Blend(start,authored,ReleaseFraction(age));}
    }
}
