using System;
using System.Collections.Generic;

namespace EndfieldDoK.MapUnitLab
{
    internal static class GilbertaGravityPolicy
    {
        internal const int MaximumTargets=12,MaximumGoals=16,MaximumCasts=4;
        internal const int MaximumCandidates=32,MaximumSpatialCandidates=96,MaximumAttempts=24;
        internal const int MaximumReachChecks=64,MaximumPathQueries=32,MaximumObstacleTests=256;
        internal const int RefreshFrames=4,MaximumPathSegments=32;
        internal const float Spacing=.65f;

        // One initial native order plus at most one replacement per controlled
        // unit per simulation frame. The live 44-frame field gets 540 attempts,
        // so twelve continuously re-tasked targets cannot exhaust it early.
        internal static int GoalReissueBudget(int activeFrames)
        {
            if(activeFrames<1||activeFrames>256)throw new ArgumentOutOfRangeException("activeFrames");
            return checked(MaximumTargets*(activeFrames+1));
        }
        internal static bool ShouldMaintain(uint frame,uint endFrame,bool hasPrevious,uint previousFrame)
        {return frame<endFrame&&(!hasPrevious||frame!=previousFrame);}
        internal static bool ShouldRefresh(uint frame,uint nextRefresh,bool ownershipReleased)
        {return frame>=nextRefresh||ownershipReleased;}
        internal static bool TerminalExit(string reason)
        {return reason=="dead_or_missing";}

        internal static float SourceClock(float age,float pullSeconds,float activeSeconds)
        {
            if(age<=pullSeconds)return .7f+Math.Max(0f,age)*2.8f/pullSeconds;
            float tail=Math.Max(.001f,activeSeconds-pullSeconds);
            return 3.5f+Math.Min(1f,(age-pullSeconds)/tail)*.8f;
        }

        internal struct Point
        {
            internal float X,Z,Radius;
            internal int Layer;
            internal Point(float x,float z,float radius){X=x;Z=z;Radius=radius;Layer=0;}
            internal Point(float x,float z,float radius,int layer){X=x;Z=z;Radius=radius;Layer=layer;}
        }

        internal static bool Affects(bool alive,bool enemy,bool activeWorld,bool inRange)
        {return alive&&enemy&&activeWorld&&inRange;}

        // Eligibility of the native movement attempt, not of the EMP. Class,
        // size and a harvester's current work order are deliberately irrelevant.
        internal static bool Eligible(int flags,bool testHarvester,bool hasMove,bool blocked,bool docked,float radius)
        {return hasMove&&!blocked&&!docked;}

        internal static bool SafeGoal(int type)
        {
            // All goal types in this native build may be copied using their
            // own StatelessCopy contract. A missing/failed copy still means
            // EMP-only control; it never excludes the unit from suppression.
            return type>=0&&type<=16;
        }

        internal static bool Separated(Point first,Point second)
        {
            if(first.Layer!=second.Layer)return true;
            float x=first.X-second.X,z=first.Z-second.Z;
            float radius=first.Radius+second.Radius+Spacing;
            return x*x+z*z>=radius*radius;
        }

        internal static bool Place(float footprint,float radius,float preferred,IList<Point> used,Func<Point,bool> reachable,out Point point)
        {return Place(footprint,radius,preferred,0,used,reachable,out point);}
        internal static bool Place(float footprint,float radius,float preferred,int layer,IList<Point> used,Func<Point,bool> reachable,out Point point)
        {
            Point center=new Point(0f,0f,footprint,layer);
            bool centerFree=footprint<=radius;
            for(int index=0;index<used.Count&&centerFree;index++)centerFree=Separated(center,used[index]);
            if(centerFree&&reachable(center)){point=center;return true;}
            for(int ring=0;ring<5;ring++)
            {
                float ringRadius=footprint+.75f+ring*(footprint*1.45f+Spacing);
                if(ringRadius+footprint>radius)break;
                for(int angle=0;angle<24;angle++)
                {
                    float theta=preferred+angle*(float)(Math.PI*2/24);
                    Point candidate=new Point((float)Math.Sin(theta)*ringRadius,(float)Math.Cos(theta)*ringRadius,footprint,layer);
                    bool free=true;
                    for(int index=0;index<used.Count;index++)if(!Separated(candidate,used[index])){free=false;break;}
                    if(free&&reachable(candidate)){point=candidate;return true;}
                }
            }
            point=new Point();
            return false;
        }
    }
}
