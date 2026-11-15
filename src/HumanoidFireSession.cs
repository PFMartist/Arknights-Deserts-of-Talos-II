using System;

namespace EndfieldDoK.MapUnitLab
{
    // Presentation only. A native activity sequence begins/refreshes an
    // engagement; it never creates a round, a command or simulated damage.
    public sealed class HumanoidFireSession
    {
        public const float InactivityTimeoutSeconds=1.8f;
        public const float MovingSpeedThreshold=.06f;
        const double MemberPhaseSeconds=.037;
        readonly int[] Choices;
        readonly int VariantCount;
        uint RandomState;
        long LastEventSequence;
        int KnownTarget;
        double Elapsed,Quiet;
        public bool Active { get; private set; }
        public int Episode { get; private set; }
        public float Clock { get { return (float)Elapsed; } }
        public float Silence { get { return (float)Quiet; } }

        public HumanoidFireSession(int members,int variants,uint seed)
        {
            if(members<1)throw new ArgumentOutOfRangeException("members");
            if(variants<1)throw new ArgumentOutOfRangeException("variants");
            Choices=new int[members];VariantCount=variants;
            RandomState=seed==0?0x6D2B79F5u:seed;
        }

        // Returns true for a new accepted activity, false for an old/duplicate
        // sequence. Compare Episode to detect a newly selected engagement.
        // Unknown target zero does not erase an existing target identity.
        public bool ObserveNativeActivity(long eventSequence,int targetToken)
        {
            if(eventSequence<=LastEventSequence)return false;
            LastEventSequence=eventSequence;
            bool changedTarget=targetToken!=0&&KnownTarget!=0&&targetToken!=KnownTarget;
            if(!Active||changedTarget)
            {
                Active=true;Episode=unchecked(Episode+1);Elapsed=0.0;
                for(int member=0;member<Choices.Length;member++)
                    Choices[member]=(int)(NextUInt()%(uint)VariantCount);
                KnownTarget=targetToken;
            }
            else if(targetToken!=0)KnownTarget=targetToken;
            Quiet=0.0;return true;
        }

        public void Step(float delta,float nativeSpeed)
        {
            if(!Finite(delta)||delta<0f)throw new ArgumentOutOfRangeException("delta");
            if(!Finite(nativeSpeed)||nativeSpeed<0f)throw new ArgumentOutOfRangeException("nativeSpeed");
            // Pause freezes even movement cancellation. Callers also leave
            // native activities queued while simulation presentation is paused.
            if(delta==0f||!Active)return;
            if(nativeSpeed>MovingSpeedThreshold){End();return;}
            Elapsed+=delta;Quiet+=delta;
            if(Quiet>=InactivityTimeoutSeconds)End();
        }

        public void End()
        {
            Active=false;Elapsed=0.0;Quiet=0.0;KnownTarget=0;
            // Keep the last received sequence so a delayed duplicate cannot
            // resurrect a completed engagement. The local PRNG also continues.
        }

        public int VariantFor(int member)
        {
            CheckMember(member);return Choices[member];
        }

        public float ClipClockFor(int member,float duration)
        {
            CheckMember(member);
            if(!Finite(duration)||duration<=0f)throw new ArgumentOutOfRangeException("duration");
            if(!Active)return 0f;
            float result=(float)((Elapsed+member*MemberPhaseSeconds)%duration);
            // A double remainder just below the end can round up on conversion.
            return result>=duration?0f:result;
        }

        uint NextUInt()
        {
            // Local xorshift32; no Unity/global random state is read or changed.
            uint value=RandomState;value^=value<<13;value^=value>>17;value^=value<<5;
            RandomState=value;return value;
        }
        void CheckMember(int member)
        {if(member<0||member>=Choices.Length)throw new ArgumentOutOfRangeException("member");}
        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
    }
}
