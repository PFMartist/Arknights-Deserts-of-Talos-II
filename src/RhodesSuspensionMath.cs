using System;

namespace EndfieldDoK.MapUnitLab
{
    // Pure System-only profile. No Unity/BBI/terrain API or native simulation.
    // All Y/Z coordinates are model-metre offsets from the immutable rest bogie
    // pivot. Positive Unity X pitch lowers its positive-Z (forward) contact.
    public struct RhodesSuspensionLimits
    {
        public float MinHeave,MaxHeave,MaxPitchDegrees;
        public float HeaveMetresPerSecond,PitchDegreesPerSecond,ResponseSeconds;
        public float MissGraceSeconds,OffGroundDroopMetres,ContactClearance;
        public void Validate()
        {
            if(!RhodesSuspensionMath.Finite(MinHeave)||!RhodesSuspensionMath.Finite(MaxHeave)||MinHeave>0f||MaxHeave<0f||MinHeave>=MaxHeave||
               !RhodesSuspensionMath.Finite(MaxPitchDegrees)||MaxPitchDegrees<0f||MaxPitchDegrees>10f||
               !RhodesSuspensionMath.Finite(HeaveMetresPerSecond)||HeaveMetresPerSecond<=0f||
               !RhodesSuspensionMath.Finite(PitchDegreesPerSecond)||PitchDegreesPerSecond<=0f||
               !RhodesSuspensionMath.Finite(ResponseSeconds)||ResponseSeconds<=0f||
               !RhodesSuspensionMath.Finite(MissGraceSeconds)||MissGraceSeconds<0f||
               !RhodesSuspensionMath.Finite(OffGroundDroopMetres)||OffGroundDroopMetres<0f||OffGroundDroopMetres>-MinHeave||
               !RhodesSuspensionMath.Finite(ContactClearance)||ContactClearance<0f||ContactClearance>.5f)
                throw new ArgumentOutOfRangeException("limits","Finite, bounded authored suspension limits required");
        }
        public static RhodesSuspensionLimits Default
        {
            get{return new RhodesSuspensionLimits{MinHeave=-1.511966658f,MaxHeave=.907179995f,MaxPitchDegrees=.5f,
                HeaveMetresPerSecond=.65f,PitchDegreesPerSecond=.5f,ResponseSeconds=.3f,
                //20mm design separation +220mm conservative existing TKR
                // shoe-phase allowance. This is a sampled safety envelope,
                // not a claim that every shoe always touches the terrain.
                MissGraceSeconds=.35f,OffGroundDroopMetres=1f,ContactClearance=.24f};}
        }
    }
    public struct RhodesSuspensionSupport
    {
        // End: +1 forward, -1 rear, 0 middle. SampleZ/GroundY describe the
        // actual last Terrain ray intersection, not an invented infinite plane.
        public float RestY,RestZ,SampleZ,GroundY;
        public int End;
        public bool Hit;
    }
    public struct RhodesSuspensionSolution
    {
        public float TargetHeave,TargetPitchDegrees,UnclampedHeave,UnclampedPitchDegrees;
        public float SampledTargetPenetration,SampledTargetGap;
        public int Hits;
        public bool HavePitchSpan,HeaveSaturated,PitchSaturated,ResidualKnown,Unsupported;
    }
    public struct RhodesSuspensionResidual
    {
        public float Penetration,Gap;
        public int Hits;
        public bool Known;
    }
    public struct RhodesGroundPoint
    {
        public float X,Y,Z;
        public RhodesGroundPoint(float x,float y,float z){X=x;Y=y;Z=z;}
    }
    // Verified static-upper V13 geometry. Effective B/R are axial reaches
    // from the attachment pins: insertion = B + R - distance(base,end).
    public static class RhodesSuspensionV13Contract
    {
        public static readonly bool InterfaceVerified=true;
        public const string GeometrySHA256="db20f3e455f9e534413e1d9d4893ba0a92a4d758c74fd1aad0d72c0d61d39b32";
        public const string InterfaceSHA256="7aa6e65e1729b5233b8b6fcd1058c42b6c0f0c4c0bbc40ac56dfacfae600e184";
        public static readonly float[] FootBarrelLengths=new float[]{10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f};
        public static readonly float[] FootRodLengths=new float[]{10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f,10.07977772f};
        public static RhodesSuspensionLimits Limits
        {
            get{return new RhodesSuspensionLimits{MinHeave=-3f,MaxHeave=2f,MaxPitchDegrees=1f,
                HeaveMetresPerSecond=.65f,PitchDegreesPerSecond=.5f,ResponseSeconds=.3f,
                MissGraceSeconds=.35f,OffGroundDroopMetres=1f,ContactClearance=.24f};}
        }
    }

    public static class RhodesSuspensionMath
    {
        public static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        public static bool Valid(RhodesSuspensionSupport value)
        {return value.Hit&&Finite(value.GroundY)&&Finite(value.SampleZ)&&Finite(value.RestY)&&Finite(value.RestZ);}
        public static float Clamp(float value,float minimum,float maximum){return Math.Max(minimum,Math.Min(maximum,value));}
        public static float RotatedY(float y,float z,float pitchDegrees)
        {double a=pitchDegrees*Math.PI/180.0;return (float)(Math.Cos(a)*y-Math.Sin(a)*z);}
        public static float RotatedZ(float y,float z,float pitchDegrees)
        {double a=pitchDegrees*Math.PI/180.0;return (float)(Math.Sin(a)*y+Math.Cos(a)*z);}
        public static float Chase(float current,float target,float delta,float response,float rate)
        {
            if(!Finite(current)||!Finite(target)||!Finite(delta)||delta<=0f)return current;
            double blend=1.0-Math.Exp(-delta/response);
            float change=(float)((target-current)*blend),limit=rate*delta;
            return current+Clamp(change,-limit,limit);
        }
        public static RhodesSuspensionSolution Solve(RhodesSuspensionSupport[] supports,int count,RhodesSuspensionLimits limits,
            float previousTargetHeave,float previousTargetPitch,float noHitAge)
        {
            limits.Validate();if(supports==null||count<1||count>supports.Length)throw new ArgumentOutOfRangeException("supports/count");
            if(!Finite(previousTargetHeave))previousTargetHeave=0f;if(!Finite(previousTargetPitch))previousTargetPitch=0f;
            RhodesSuspensionSolution result=new RhodesSuspensionSolution();
            float front=float.NegativeInfinity,rear=float.NegativeInfinity,frontZ=0f,rearZ=0f;
            bool haveFront=false,haveRear=false;
            for(int i=0;i<count;i++)
            {
                RhodesSuspensionSupport s=supports[i];
                if(!Valid(s))continue;
                result.Hits++;
                // The high side supports the rigid double-belt foot. Averaging
                // left/right would knowingly push its high side into terrain.
                if(s.End>0&&(!haveFront||s.GroundY>front)){front=s.GroundY;frontZ=s.SampleZ;haveFront=true;}
                if(s.End<0&&(!haveRear||s.GroundY>rear)){rear=s.GroundY;rearZ=s.SampleZ;haveRear=true;}
            }
            if(result.Hits==0)
            {
                result.Unsupported=true;
                bool grace=Finite(noHitAge)&&noHitAge<=limits.MissGraceSeconds;
                result.TargetHeave=grace?Clamp(previousTargetHeave,limits.MinHeave,limits.MaxHeave):-limits.OffGroundDroopMetres;
                result.TargetPitchDegrees=grace?Clamp(previousTargetPitch,-limits.MaxPitchDegrees,limits.MaxPitchDegrees):0f;
                result.UnclampedHeave=result.TargetHeave;result.UnclampedPitchDegrees=result.TargetPitchDegrees;
                // No samples means unknown clearance, never a zero-penetration pass.
                result.ResidualKnown=false;return result;
            }
            result.HavePitchSpan=haveFront&&haveRear&&frontZ-rearZ>.1f;
            float angle=result.HavePitchSpan?(float)(-Math.Atan2(front-rear,frontZ-rearZ)*180.0/Math.PI):previousTargetPitch;
            if(!Finite(angle))angle=0f;
            result.UnclampedPitchDegrees=angle;
            result.TargetPitchDegrees=Clamp(angle,-limits.MaxPitchDegrees,limits.MaxPitchDegrees);
            result.PitchSaturated=Math.Abs(angle-result.TargetPitchDegrees)>.00001f;
            float required=float.NegativeInfinity;
            for(int i=0;i<count;i++)
            {
                RhodesSuspensionSupport s=supports[i];if(!Valid(s))continue;
                required=Math.Max(required,s.GroundY+limits.ContactClearance-RotatedY(s.RestY,s.RestZ,result.TargetPitchDegrees));
            }
            result.UnclampedHeave=required;result.TargetHeave=Clamp(required,limits.MinHeave,limits.MaxHeave);
            result.HeaveSaturated=Math.Abs(required-result.TargetHeave)>.00001f;
            RhodesSuspensionResidual residual=Residual(supports,count,result.TargetHeave,result.TargetPitchDegrees,limits.ContactClearance);
            result.ResidualKnown=residual.Known;result.SampledTargetPenetration=residual.Penetration;result.SampledTargetGap=residual.Gap;
            return result;
        }
        public static RhodesSuspensionResidual Residual(RhodesSuspensionSupport[] supports,int count,float heave,float pitch,float clearance)
        {
            RhodesSuspensionResidual result=new RhodesSuspensionResidual();
            for(int i=0;i<count;i++)
            {
                RhodesSuspensionSupport s=supports[i];if(!Valid(s))continue;
                float gap=heave+RotatedY(s.RestY,s.RestZ,pitch)-s.GroundY-clearance;
                result.Gap=Math.Max(result.Gap,gap);result.Penetration=Math.Max(result.Penetration,-gap);result.Hits++;
            }
            result.Known=result.Hits>0;return result;
        }
        // Re-expression of an already applied pose for known root rotation.
        // PivotY arguments exclude root translation. No ground extrapolation.
        public static float ReexpressHeaveForRootRotation(float heave,float oldPivotY,float oldUpY,float newPivotY,float newUpY,RhodesSuspensionLimits limits)
        {
            if(!Finite(heave)||!Finite(oldPivotY)||!Finite(oldUpY)||!Finite(newPivotY)||!Finite(newUpY)||newUpY<.25f)return heave;
            return Clamp((oldPivotY+oldUpY*heave-newPivotY)/newUpY,limits.MinHeave,limits.MaxHeave);
        }
        public static float ReexpressPitchForRootRotation(float pitch,float localForwardY,float localForwardZ,RhodesSuspensionLimits limits)
        {
            if(!Finite(localForwardY)||!Finite(localForwardZ)||localForwardY*localForwardY+localForwardZ*localForwardZ<.000001f)return pitch;
            return Clamp((float)(Math.Atan2(-localForwardY,localForwardZ)*180.0/Math.PI),-limits.MaxPitchDegrees,limits.MaxPitchDegrees);
        }
        public static float RequiredHeave(RhodesSuspensionSupport[] supports,int count,float pitch,float clearance,out int hits)
        {
            float required=float.NegativeInfinity;hits=0;
            for(int i=0;i<count;i++)if(Valid(supports[i]))
            {RhodesSuspensionSupport p=supports[i];required=Math.Max(required,p.GroundY+clearance-RotatedY(p.RestY,p.RestZ,pitch));hits++;}
            return required;
        }
        // Upward contact projection is a safety constraint, not a stiffer spring.
        // Normal downward settling still follows Chase; hard travel is unchanged.
        public static float ProjectContactHeave(float heave,RhodesSuspensionSupport[] supports,int count,float pitch,RhodesSuspensionLimits limits)
        {
            int hits;float floor=RequiredHeave(supports,count,pitch,limits.ContactClearance,out hits);
            return hits==0?heave:Clamp(Math.Max(heave,floor),limits.MinHeave,limits.MaxHeave);
        }
        public static float PlaneRequiredHeave(float normalDotGroundMinusPivot,float soleY,float normalDotUp,float clearance)
        {
            return !Finite(normalDotGroundMinusPivot)||!Finite(soleY)||!Finite(normalDotUp)||normalDotUp<.25f?float.NaN:
                (normalDotGroundMinusPivot-soleY)/normalDotUp+clearance;
        }
        public static float PlaneRootLiftDelta(float requiredHeave,float maximumHeave,float normalDotUp,float normalWorldY)
        {
            return !Finite(requiredHeave)||!Finite(normalDotUp)||!Finite(normalWorldY)||normalDotUp<.25f||normalWorldY<.25f?float.NaN:
                (requiredHeave-maximumHeave)*normalDotUp/normalWorldY;
        }
        public static float AbsoluteNativeSupportLift(float rootY,float groundY,float requiredWorldDelta,float maximumLift,float headroom)
        {
            if(!Finite(rootY)||!Finite(groundY)||!Finite(requiredWorldDelta))return float.NaN;
            // Keep the SIGNED correction until the final absolute clamp. Using
            // previousLift += max(0,error) would accumulate latency and never lower.
            float raw=rootY-groundY+requiredWorldDelta;
            return Clamp(raw>0f?raw+headroom:0f,0f,maximumLift);
        }
        static float EdgeDistance(RhodesGroundPoint a,RhodesGroundPoint b,RhodesGroundPoint p)
        {return (b.X-a.X)*(p.Z-a.Z)-(b.Z-a.Z)*(p.X-a.X);}
        // Exact extrema of terrainPlane-footPlane on one terrain triangle clipped
        // to the convex projected shoe-sweep quad. At most seven vertices; caller
        // owns both eight-element work arrays. No scene queries or allocations.
        public static void AccumulateTriangleSupport(RhodesGroundPoint a,RhodesGroundPoint b,RhodesGroundPoint c,
            RhodesGroundPoint[] quad,RhodesGroundPoint[] workA,RhodesGroundPoint[] workB,float slopeX,float slopeZ,
            ref bool known,ref double maximum,ref RhodesGroundPoint support)
        {
            if(quad==null||quad.Length!=4||workA==null||workB==null||workA.Length<8||workB.Length<8)throw new ArgumentException("Bounded support buffers");
            workA[0]=a;workA[1]=b;workA[2]=c;int count=3;RhodesGroundPoint[] input=workA,output=workB;
            for(int edge=0;edge<4&&count>0;edge++)
            {
                int written=0;RhodesGroundPoint qa=quad[edge],qb=quad[(edge+1)%4],previous=input[count-1];
                float previousDistance=EdgeDistance(qa,qb,previous);bool previousInside=previousDistance>=0f;
                for(int i=0;i<count;i++)
                {
                    RhodesGroundPoint current=input[i];float distance=EdgeDistance(qa,qb,current);bool inside=distance>=0f;
                    if(inside!=previousInside)
                    {
                        float t=previousDistance/(previousDistance-distance);
                        if(written>=8)throw new InvalidOperationException("Support clip overflow");
                        output[written++]=new RhodesGroundPoint(previous.X+(current.X-previous.X)*t,previous.Y+(current.Y-previous.Y)*t,previous.Z+(current.Z-previous.Z)*t);
                    }
                    if(inside){if(written>=8)throw new InvalidOperationException("Support clip overflow");output[written++]=current;}
                    previous=current;previousDistance=distance;previousInside=inside;
                }
                count=written;RhodesGroundPoint[] swap=input;input=output;output=swap;
            }
            for(int i=0;i<count;i++)
            {
                RhodesGroundPoint p=input[i];double value=p.Y-(double)slopeX*p.X-(double)slopeZ*p.Z;
                if(!known||value>maximum){known=true;maximum=value;support=p;}
            }
        }
        public static float FanHeight(float tx,float tz,float a,float b,float c,float d,float middle)
        {
            if(tz<=tx&&tz<=1f-tx)return a*(1f-tx-tz)+b*(tx-tz)+middle*(2f*tz);
            if(tx>=tz&&tx>=1f-tz)return b*(tx-tz)+d*(tx+tz-1f)+middle*(2f*(1f-tx));
            if(tz>=tx&&tz>=1f-tx)return c*(tz-tx)+d*(tx+tz-1f)+middle*(2f*(1f-tz));
            return a*(1f-tx-tz)+c*(tz-tx)+middle*(2f*tx);
        }
        public static float SampleInterval(float cameraMetres)
        {return !Finite(cameraMetres)||cameraMetres>=2500f?1f:(cameraMetres>=1000f?.5f:.2f);}
        public static float PoseInterval(float cameraMetres)
        {return !Finite(cameraMetres)||cameraMetres>=2500f?.2f:(cameraMetres>=1000f?1f/15f:0f);}
        public static bool CanStep(bool nativeShown,bool rootActive,bool alive,bool ownerResearchReady,float delta)
        {return nativeShown&&rootActive&&alive&&ownerResearchReady&&Finite(delta)&&delta>0f;}
    }
    // The live host owns exactly one STATIC instance across all carriers.
    // No Unity objects or transforms are ever cached by this pure budget.
    public sealed class RhodesSuspensionRayBudget
    {
        int LastFrame=-1,Used;
        public int UsedThisFrame{get{return Used;}}
        public bool TryTake(int frame,int rays)
        {
            if(frame!=LastFrame){LastFrame=frame;Used=0;}
            if(rays<1||rays>12||Used+rays>12)return false;Used+=rays;return true;
        }
        public bool TryTakeOwned(int frame,int rays,int requester,bool[] ready,ref int nextOwner)
        {
            if(ready==null||ready.Length==0||requester<0||requester>=ready.Length)return false;
            nextOwner=(nextOwner%ready.Length+ready.Length)%ready.Length;
            for(int checkedOwners=0;checkedOwners<ready.Length;checkedOwners++)
            {
                if(!ready[nextOwner]){nextOwner=(nextOwner+1)%ready.Length;continue;}
                if(nextOwner!=requester)return false;
                if(!TryTake(frame,rays))return false;nextOwner=(nextOwner+1)%ready.Length;return true;
            }
            return false;
        }
        public void Reset(){LastFrame=-1;Used=0;}
    }
}
