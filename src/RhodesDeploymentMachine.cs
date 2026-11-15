using System;

namespace EndfieldDoK.MapUnitLab
{
    // Pure deterministic state transitions. R19 L1 deploys fore hardware;
    // L2 opens the aft bay. Charge research does not change hardware levels.
    public static class RhodesDeploymentMachine
    {
        public static RhodesDeploymentSnapshot Settled(int branch,int level)
        {
            return new RhodesDeploymentSnapshot{Branch=branch,SourceLevel=level,TargetLevel=level,
                Kind=0,StartTick=-1,AftStartTick=-1,VTStartTick=-1,CIWS2StartTick=-1,Repair2StartTick=-1,
                DeckReady=branch!=0,MainReady=branch==2&&level>=1,ForeReady=branch==2&&level>=1,AftReady=branch==2&&level>=2};
        }
        // Retained pure compatibility entry: a completed route cannot return.
        public static bool BeginDemoReturn(ref RhodesDeploymentSnapshot state,int tick)
        {return false;}
        public static void Advance(ref RhodesDeploymentSnapshot state,int branch,int level,
            int firstCompletedTick,int secondCompletedTick,int tick,bool aviationLanesDrained)
        {
            if(state.Kind==4)
            {
                if(branch!=state.Branch)throw new InvalidOperationException("Demo return research changed before its native reset commit");
                state.DeckReady=false;state.MainReady=false;state.ForeReady=false;state.AftReady=false;return;
            }
            if(state.Branch!=0&&branch!=state.Branch)
                throw new InvalidOperationException("Rhodes route reversal is not supported by native exclusive research");
            if(state.Branch==0&&branch!=0)
            {
                if(firstCompletedTick<0)throw new InvalidOperationException("Rhodes deployment needs native completion tick");
                state.Branch=branch;state.SourceLevel=0;state.TargetLevel=1;state.Kind=1;
                state.StartTick=firstCompletedTick;state.VTStartTick=firstCompletedTick;state.VTTransition=true;
                state.AftStartTick=-1;state.ForeReady=false;state.AftReady=false;
            }
            if(level>state.TargetLevel)
            {
                if(level<1||level>2||state.TargetLevel<1||secondCompletedTick<0)
                    throw new InvalidOperationException("Unexpected Rhodes hardware upgrade transition");
                state.TargetLevel=level;
                if(branch==2&&level>=2&&state.AftStartTick<0&&!state.AftReady)
                {
                    // Test research can finish before the first 21.5s motion.
                    // Queue aft motion after the fore/platform path, never stack
                    // the two sampled geometries or restart the first epoch.
                    int initialEnd=firstCompletedTick<0?secondCompletedTick:checked(firstCompletedTick+RhodesDeploymentProfile.GunneryFirstTicks);
                    state.AftStartTick=Math.Max(secondCompletedTick,initialEnd);
                }
            }
            if(state.Kind==1&&RhodesDeploymentProfile.ReadyAt(state.Branch,1,state.StartTick,tick))
            {state.SourceLevel=1;state.Kind=0;}
            if(state.VTTransition&&(long)tick-state.VTStartTick>=RhodesDeploymentProfile.VTTicks)state.VTTransition=false;
            bool firstReady=state.Branch!=0&&state.Kind!=1;
            state.ForeReady=state.Branch==2&&firstReady;
            if(state.Branch==2&&state.TargetLevel>=2&&state.AftStartTick>=0)
                state.AftReady=(long)tick-state.AftStartTick>=RhodesDeploymentProfile.GunneryAftTicks;
            if(state.Branch==2&&firstReady&&state.TargetLevel>=2&&!state.AftReady)state.Kind=5;
            if(state.Kind==5&&state.AftReady)state.Kind=0;
            if(state.Kind==0)state.SourceLevel=state.TargetLevel;
            state.DeckReady=firstReady;
            state.MainReady=state.ForeReady;
        }
    }
}
