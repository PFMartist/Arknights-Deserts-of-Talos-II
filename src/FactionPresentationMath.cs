using System;

namespace EndfieldDoK.MapUnitLab
{
    // Pure presentation math. No simulation writes, Unity allocation or queries.
    public static class FactionPresentationMath
    {
        public const double GroundResponsePerSecond=3.0;
        public static double Blend(double rate,double seconds)
        {
            if(!FactionGroundPose.Finite(rate)||!FactionGroundPose.Finite(seconds)||rate<=0||seconds<=0)return 0;
            return 1.0-Math.Exp(-rate*seconds);
        }
        public static double Damp(double current,double target,double seconds,double dampingSeconds)
        {
            if(!FactionGroundPose.Finite(target))return current;
            if(!FactionGroundPose.Finite(current))return target;
            if(seconds<=0||!FactionGroundPose.Finite(seconds))return current;
            return dampingSeconds<=0?target:current+(target-current)*Blend(1.0/dampingSeconds,seconds);
        }
        public static FactionPoseVector SmoothNormal(FactionPoseVector current,FactionPoseVector target,double seconds)
        {
            if(!FactionGroundPose.UsableNormal(target))target=new FactionPoseVector(0,1,0);
            if(!FactionGroundPose.UsableNormal(current))return Unit(target);
            current=Unit(current);target=Unit(target);
            double weight=Blend(GroundResponsePerSecond,seconds);
            double dot=Math.Max(-1.0,Math.Min(1.0,FactionGroundPose.Dot(current,target)));
            if(dot>.999999)return Unit(new FactionPoseVector(current.X+(target.X-current.X)*weight,current.Y+(target.Y-current.Y)*weight,current.Z+(target.Z-current.Z)*weight));
            double angle=Math.Acos(dot),divisor=Math.Sin(angle);
            double a=Math.Sin((1-weight)*angle)/divisor,b=Math.Sin(weight*angle)/divisor;
            return Unit(new FactionPoseVector(current.X*a+target.X*b,current.Y*a+target.Y*b,current.Z*a+target.Z*b));
        }
        static FactionPoseVector Unit(FactionPoseVector value)
        {
            double length=Math.Sqrt(FactionGroundPose.Dot(value,value));
            return length<1e-12?new FactionPoseVector(0,1,0):new FactionPoseVector(value.X/length,value.Y/length,value.Z/length);
        }
        public static double AngleStep(double current,double target,double seconds,double degreesPerSecond)
        {
            if(!FactionGroundPose.Finite(current)||!FactionGroundPose.Finite(target))return FactionGroundPose.Finite(target)?target:0;
            if(seconds<=0||!FactionGroundPose.Finite(seconds)||degreesPerSecond<=0)return current;
            double delta=(target-current)%360.0;if(delta>180)delta-=360;if(delta< -180)delta+=360;
            double step=degreesPerSecond*seconds;
            return current+Math.Max(-step,Math.Min(step,delta));
        }
        // A smoothed body can have a different slope from the sampled tangent.
        // Lift only as far as needed by its final displayed bottom footprint.
        public static FactionGroundPoseResult SupportDisplayedPose(FactionGroundPoseResult pose,FactionPoseVector ground,FactionPoseVector normal,double minX,double maxX,double minZ,double maxZ,double bottom)
        {
            if(!FactionGroundPose.UsableNormal(normal))return pose;
            double lift=0;
            for(int corner=0;corner<4;corner++)
            {
                double x=(corner&1)==0?minX:maxX,z=(corner&2)==0?minZ:maxZ;
                FactionPoseVector world=new FactionPoseVector(pose.Position.X+pose.Right.X*x+pose.Up.X*bottom+pose.Forward.X*z,
                    pose.Position.Y+pose.Right.Y*x+pose.Up.Y*bottom+pose.Forward.Y*z,
                    pose.Position.Z+pose.Right.Z*x+pose.Up.Z*bottom+pose.Forward.Z*z);
                lift=Math.Max(lift,FactionGroundPose.HeightAt(ground,normal,world.X,world.Z)-world.Y);
            }
            pose.Position.Y+=lift;return pose;
        }
    }
}
