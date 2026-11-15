using System;

namespace EndfieldDoK.MapUnitLab
{
    // Per logical solo unit. Selects source clips from authoritative native
    // events only. No entities, orders, shots, projectiles or damage.
    public sealed class HumanoidSoloAttackCombo
    {
        readonly int Variants;
        readonly float ResetGap;
        double Clock,LastActivity;
        long LastEvent;
        int Target;
        bool Active,Moving;
        public int CurrentVariant { get; private set; }
        public int NextVariant { get; private set; }
        public int Episode { get; private set; }
        public bool WindupPending { get; private set; }
        public bool RoundMatchedWindup { get; private set; }
        public bool HasSelection { get; private set; }
        public HumanoidSoloAttackCombo(int variants):this(variants,8f) { }
        public HumanoidSoloAttackCombo(int variants,float resetGapSeconds)
        {
            if(variants<1||variants>16)throw new ArgumentOutOfRangeException("variants");
            if(!Finite(resetGapSeconds)||resetGapSeconds<=0f)throw new ArgumentOutOfRangeException("resetGapSeconds");
            Variants=variants;ResetGap=resetGapSeconds;
        }

        // Same dt as the actor driver. Freeze when dt==0; speed is the current
        // native horizontal speed, not a noisy per-render position difference.
        public void Step(float dt,float nativeSpeed)
        {
            if(!Finite(dt)||dt<0f)throw new ArgumentOutOfRangeException("dt");
            if(!Finite(nativeSpeed)||nativeSpeed<0f)throw new ArgumentOutOfRangeException("nativeSpeed");
            if(dt==0f)return;
            Clock+=dt;
            Moving=nativeSpeed>.06f;
            if(Moving||Active&&Clock-LastActivity>=ResetGap)End();
        }
        // All callbacks receive shared message.EventSequence in native queue
        // order. Do not use WindupSequence/RoundSequence as cross-kind IDs.
        public bool ObserveWindup(long eventSequence,int targetToken,out int variant)
        {
            variant=CurrentVariant;if(!Accept(eventSequence)||Moving)return false;
            PrepareTarget(targetToken);
            CurrentVariant=NextVariant;HasSelection=true;WindupPending=true;
            RoundMatchedWindup=false;LastActivity=Clock;variant=CurrentVariant;return true;
        }
        public bool ObserveRound(long eventSequence,int targetToken,out int variant)
        {
            variant=CurrentVariant;if(!Accept(eventSequence)||Moving)return false;
            PrepareTarget(targetToken);
            RoundMatchedWindup=WindupPending;
            if(!WindupPending)CurrentVariant=NextVariant;
            HasSelection=true;WindupPending=false;variant=CurrentVariant;
            // Only an actual round commits the chain step. A cancelled/repeated
            // windup cannot skip a segment or add a synthetic attack.
            NextVariant=(CurrentVariant+1)%Variants;LastActivity=Clock;return true;
        }
        public bool ObserveIdleOrWinddown(long eventSequence)
        {
            if(!Accept(eventSequence))return false;
            WindupPending=false;RoundMatchedWindup=false;
            // DoK may emit idle/winddown around native operations. This is not
            // an accepted user stop order and must not reset every reload.
            return true;
        }
        // Actual movement/target change/gap calls End. Keep the displayed last
        // clip selection for its recovery pose until a new native event selects
        // the reset first segment; do not jump a long fourth clip on timeout.
        public void End()
        {Active=false;WindupPending=false;RoundMatchedWindup=false;NextVariant=0;Target=0;}
        public void Reset()
        {Clock=LastActivity=0d;LastEvent=0;Target=0;Active=Moving=false;CurrentVariant=NextVariant=Episode=0;WindupPending=RoundMatchedWindup=HasSelection=false;}
        bool Accept(long sequence)
        {if(sequence<=0||sequence<=LastEvent)return false;LastEvent=sequence;return true;}
        void PrepareTarget(int targetToken)
        {
            if(Active&&Clock-LastActivity>=ResetGap)End();
            if(Active&&Target!=0&&targetToken!=0&&Target!=targetToken)End();
            if(!Active){Active=true;Episode++;NextVariant=0;WindupPending=false;RoundMatchedWindup=false;}
            if(targetToken!=0)Target=targetToken;
        }
        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
    }
}
