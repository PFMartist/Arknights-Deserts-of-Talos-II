using System;

namespace EndfieldDoK.MapUnitLab
{
    // System-only geometry math, shared with the offline fixture. No Unity
    // calls, terrain queries, mutable global cache or simulation writes.
    public struct FactionPoseVector
    {
        public double X,Y,Z;
        public FactionPoseVector(double x,double y,double z){X=x;Y=y;Z=z;}
    }
    public struct FactionGroundPoseResult
    {
        public FactionPoseVector Position,Right,Up,Forward;
    }
    public static class FactionGroundPose
    {
        public const double MaximumSampleDistanceSquared=64.0;
        // Terrain-facing, nonvertical normals only. Reject walls, inverted
        // hits, missing/zero normals and nonfinite data rather than lean over.
        public const double MinimumUpNormal=0.15;
        public static bool Finite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value);}
        public static bool Finite(FactionPoseVector value){return Finite(value.X)&&Finite(value.Y)&&Finite(value.Z);}
        public static double Dot(FactionPoseVector a,FactionPoseVector b){return a.X*b.X+a.Y*b.Y+a.Z*b.Z;}
        static FactionPoseVector Scale(FactionPoseVector a,double b){return new FactionPoseVector(a.X*b,a.Y*b,a.Z*b);}
        static FactionPoseVector Cross(FactionPoseVector a,FactionPoseVector b)
        {return new FactionPoseVector(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);}
        static FactionPoseVector Unit(FactionPoseVector value,FactionPoseVector fallback)
        {
            double length=Dot(value,value);
            return !Finite(value)||!Finite(length)||length<1e-12?fallback:Scale(value,1.0/Math.Sqrt(length));
        }
        public static bool UsableNormal(FactionPoseVector value)
        {
            if(!Finite(value)||Dot(value,value)<1e-12)return false;
            return Unit(value,new FactionPoseVector(0,0,0)).Y>MinimumUpNormal;
        }
        public static bool PlausiblePoint(FactionPoseVector point,FactionPoseVector nativePosition,FactionPoseVector simPosition)
        {
            if(!Finite(point)||!Finite(nativePosition)||!Finite(simPosition))return false;
            double nx=point.X-nativePosition.X,nz=point.Z-nativePosition.Z;
            double sx=point.X-simPosition.X,sz=point.Z-simPosition.Z;
            return nx*nx+nz*nz<=MaximumSampleDistanceSquared&&sx*sx+sz*sz<=MaximumSampleDistanceSquared;
        }
        public static double HeightAt(FactionPoseVector point,FactionPoseVector normal,double x,double z)
        {
            if(!UsableNormal(normal))return point.Y;
            return point.Y-(normal.X*(x-point.X)+normal.Z*(z-point.Z))/normal.Y;
        }
        public static FactionGroundPoseResult Solve(FactionPoseVector groundPoint,FactionPoseVector heading,FactionPoseVector normal,double bottomY)
        {
            if(!Finite(groundPoint)||!Finite(bottomY))throw new ArgumentException("Finite ground anchor and model bottom required");
            FactionPoseVector up=UsableNormal(normal)?Unit(normal,new FactionPoseVector(0,1,0)):new FactionPoseVector(0,1,0);
            FactionPoseVector flat=Unit(new FactionPoseVector(heading.X,0,heading.Z),new FactionPoseVector(0,0,1));
            // Keep the simulated XZ heading exactly. Its required Y follows the
            // tangent plane; generic orthogonal projection can change azimuth.
            FactionPoseVector forward=Unit(new FactionPoseVector(flat.X,-(up.X*flat.X+up.Z*flat.Z)/up.Y,flat.Z),flat);
            FactionPoseVector right=Unit(Cross(up,forward),new FactionPoseVector(1,0,0));
            // Rotate about the authored bottom datum, not the model origin.
            // The complete local y=bottomY plane lies on this tangent plane.
            return new FactionGroundPoseResult{Right=right,Up=up,Forward=forward,
                Position=new FactionPoseVector(groundPoint.X-up.X*bottomY,groundPoint.Y-up.Y*bottomY,groundPoint.Z-up.Z*bottomY)};
        }
        public static FactionPoseVector ToLocal(FactionGroundPoseResult pose,FactionPoseVector worldDirection)
        {return new FactionPoseVector(Dot(worldDirection,pose.Right),Dot(worldDirection,pose.Up),Dot(worldDirection,pose.Forward));}
        public static FactionPoseVector WorldAim(double yawDegrees,double elevationDegrees)
        {
            double yaw=(Finite(yawDegrees)?yawDegrees:0)*Math.PI/180.0;
            double elevation=(Finite(elevationDegrees)?elevationDegrees:0)*Math.PI/180.0;
            return new FactionPoseVector(Math.Sin(yaw)*Math.Cos(elevation),Math.Sin(elevation),Math.Cos(yaw)*Math.Cos(elevation));
        }
        public static void AimDegrees(FactionPoseVector localDirection,out double yaw,out double pitch)
        {
            FactionPoseVector d=Unit(localDirection,new FactionPoseVector(0,0,1));
            yaw=Math.Atan2(d.X,d.Z)*180.0/Math.PI;
            // Positive Unity local X lowers a forward-pointing barrel.
            pitch=-Math.Atan2(d.Y,Math.Sqrt(d.X*d.X+d.Z*d.Z))*180.0/Math.PI;
        }
    }
}
