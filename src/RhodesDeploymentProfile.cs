using System;

namespace EndfieldDoK.MapUnitLab
{
    // Pure state handed from the native simulation service to presentation.
    // No Transform, UnitView, Unity or wall clock is retained here.
    public struct RhodesDeploymentSnapshot
    {
        // Branch:0 none,1 aviation,2 gunnery. Kind:0 settled,1 first,2 legacy refit,3 AV lane drain,4 legacy return,5 R19 aft deploy.
        public int Branch,SourceLevel,TargetLevel,Kind,StartTick,AftStartTick,VTStartTick,CIWS2StartTick,Repair2StartTick;
        public bool VTTransition,DeckReady,MainReady,ForeReady,AftReady;
    }
    public struct RhodesDeploymentPoseSample
    {
        public float DeckShutter,VTShutter,DeckOutgoingOffset,DeckIncomingOffset,VTOffset,MinerFold,GunYaw,GunTransit;
        public bool UseOutgoingDeck;
    }
    public static class RhodesDeploymentProfile
    {
        // V09 fast MOTION_CONTRACT 493d7b04... / native tick0.125s. Each phase is rounded up.
        public const float NativeTickSeconds=.125f,MainWarmupSeconds=21.625f;
        public const int GunneryFirstTicks=173,GunneryAftTicks=125,GunneryRefitTicks=152,AviationFirstTicks=192,AviationRefitTicks=128,VTTicks=56;
        public const float GunneryDepthMetres=53.42282190322876f,AviationDepthMetres=28.22337760925293f,VTDepthMetres=5f,AddonDepthMetres=2f;
        public static int DurationTicks(int branch,int kind)
        {return kind==1||kind==4?(branch==2?GunneryFirstTicks:branch==1?AviationFirstTicks:0):kind==2?(branch==2?GunneryRefitTicks:branch==1?AviationRefitTicks:0):0;}
        public static bool ReadyAt(int branch,int kind,int startTick,int currentTick)
        {return kind==0||kind!=3&&startTick>=0&&(long)currentTick-startTick>=DurationTicks(branch,kind);}
        public static float AgeTicks(int startTick,int currentTick,float subFrame)
        {
            if(startTick<0||currentTick<startTick)return 0f;
            if(float.IsNaN(subFrame)||float.IsInfinity(subFrame))subFrame=0f;
            return (float)((long)currentTick-startTick)+Clamp(subFrame);
        }
        public static float Clamp(float x){return Math.Max(0f,Math.Min(1f,x));}
        public static float Smooth(float x){x=Clamp(x);return x*x*(3f-2f*x);}
        public static float AddonOffset(int startTick,int tick,float subFrame)
        {return startTick<0?0f:-AddonDepthMetres*(1f-Smooth(AgeTicks(startTick,tick,subFrame)/12f));}
        public static RhodesDeploymentPoseSample Evaluate(RhodesDeploymentSnapshot state,int tick,float subFrame)
        {
            if(state.Kind==4)
            {
                // Reverse the proven L1 hardware path under the SAME native
                // tick clock. Efficiency L2 never changes this travel path.
                float returnAge=AgeTicks(state.StartTick,tick,subFrame);
                float reverse=Math.Max(0f,DurationTicks(state.Branch,4)-returnAge);
                int whole=(int)Math.Floor(reverse);
                RhodesDeploymentSnapshot forward=state;forward.Kind=1;forward.StartTick=0;forward.VTTransition=false;
                RhodesDeploymentPoseSample outgoing=Evaluate(forward,whole,reverse-whole);
                outgoing.UseOutgoingDeck=true;outgoing.DeckOutgoingOffset=outgoing.DeckIncomingOffset;outgoing.DeckIncomingOffset=0f;
                float vtReverse=Math.Max(0f,VTTicks-returnAge);
                outgoing.VTShutter=Smooth(vtReverse/32f);
                outgoing.VTOffset=-VTDepthMetres*(1f-Smooth((vtReverse-32f)/24f));
                return outgoing;
            }
            RhodesDeploymentPoseSample p=new RhodesDeploymentPoseSample();
            if(state.Branch==0)return p;
            p.DeckShutter=1f;p.VTShutter=1f;p.MinerFold=state.Branch>0?1f:0f;
            float age=AgeTicks(state.StartTick,tick,subFrame);
            float depth=state.Branch==2?GunneryDepthMetres:AviationDepthMetres;
            if(state.Kind==1)
            {
                float riseStart=96f;
                p.DeckShutter=Smooth(age/96f);
                p.DeckIncomingOffset=-depth*(1f-Smooth((age-riseStart)/64f));
                if(state.Branch==1)p.MinerFold=Smooth((age-96f)/12f);
                if(state.Branch==2){p.MinerFold=Smooth((age-96f)/12f);p.GunTransit=1f-Smooth((age-160f)/12f);}
            }
            else if(state.Kind==2)
            {
                float sinkStart=state.Branch==2?12f:0f,riseStart=sinkStart+64f;
                p.UseOutgoingDeck=age<riseStart;
                p.DeckOutgoingOffset=-depth*Smooth((age-sinkStart)/64f);
                p.DeckIncomingOffset=-depth*(1f-Smooth((age-riseStart)/64f));
                if(state.Branch==2)p.GunTransit=age<12f?Smooth(age/12f):1f-Smooth((age-140f)/12f);
            }
            else if(state.Kind==3)p.UseOutgoingDeck=true;
            if(state.VTTransition)
            {
                float vtAge=AgeTicks(state.VTStartTick,tick,subFrame);
                p.VTShutter=Smooth(vtAge/32f);p.VTOffset=-VTDepthMetres*(1f-Smooth((vtAge-32f)/24f));
            }
            p.GunYaw=90f*p.GunTransit; // target heading diagnostic, never an additive native-yaw offset
            return p;
        }
    }
}
