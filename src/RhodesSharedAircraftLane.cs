using System;

namespace EndfieldDoK.MapUnitLab
{
    // Pure native-frame policy for one physical U08/B01 side facility. Values
    // come from assigned native slots/queue phases, never pending list counts.
    internal static class RhodesSharedAircraftLane
    {
        internal const int TiltDock=1,TiltLaunch=2,BomberDock=4,BomberLaunch=8;
        internal const int FighterDock=16,FighterLaunch=32;
        internal const int RecoveryResetTicks=RhodesBomberRecoveryProfile.StandardResetTicks;
        internal struct State
        {internal int OccupiedMask,FighterDockSlots,FighterLaunchSlots,RecoveryClearUntilTick,FighterDockClearUntilTick,FighterLaunchClearUntilTick,LastTick;internal bool Observed;}
        internal static void Observe(ref State state,int occupiedMask,int tick)
        {Observe(ref state,occupiedMask,tick,(occupiedMask&FighterDock)!=0?1:0,(occupiedMask&FighterLaunch)!=0?1:0);}
        internal static void Observe(ref State state,int occupiedMask,int tick,int fighterDockSlots,int fighterLaunchSlots)
        {Observe(ref state,occupiedMask,tick,fighterDockSlots,fighterLaunchSlots,false);}
        internal static void Observe(ref State state,int occupiedMask,int tick,int fighterDockSlots,int fighterLaunchSlots,bool fastBomberRecovery)
        {
            if((occupiedMask&~63)!=0||tick<0||(fighterDockSlots&~7)!=0||(fighterLaunchSlots&~7)!=0)throw new ArgumentOutOfRangeException("native lane snapshot");
            if(state.Observed&&tick<state.LastTick)state=new State();
            if(state.Observed&&(state.OccupiedMask&(TiltDock|BomberDock))!=0&&
                (occupiedMask&(TiltDock|BomberDock))==0)
                state.RecoveryClearUntilTick=checked(tick+(fastBomberRecovery?RhodesBomberRecoveryProfile.SharedResetTicks(state.OccupiedMask&(TiltDock|BomberDock)):RecoveryResetTicks));
            // Any freed fighter slot needs its own empty return. The native
            // checkpoint precedes slot choice, so conservatively pause that
            // direction while preserving the opposite direction's concurrency.
            if(state.Observed&&(state.FighterDockSlots&~fighterDockSlots)!=0)
                state.FighterDockClearUntilTick=checked(tick+RecoveryResetTicks);
            if(state.Observed&&(state.FighterLaunchSlots&~fighterLaunchSlots)!=0)
                state.FighterLaunchClearUntilTick=checked(tick+RecoveryResetTicks);
            state.Observed=true;state.OccupiedMask=occupiedMask;state.FighterDockSlots=fighterDockSlots;state.FighterLaunchSlots=fighterLaunchSlots;state.LastTick=tick;
        }
        internal static bool CanUse(State state,bool bomber,bool receiving,int tick)
        {
            if(!state.Observed||tick<state.RecoveryClearUntilTick)return false;
            int ownDirection=bomber?(receiving?BomberDock:BomberLaunch):(receiving?TiltDock:TiltLaunch);
            // Same-direction slot capacity remains native. The other direction
            // of this bay and both directions of the other bay share hardware.
            return (state.OccupiedMask&15&~ownDirection)==0;
        }
        internal static bool CanUseFighter(State state,bool receiving,int tick)
        {return state.Observed&&tick>=(receiving?state.FighterDockClearUntilTick:state.FighterLaunchClearUntilTick);}
        internal static bool IsDrained(State state,int tick)
        {return state.Observed&&state.OccupiedMask==0&&tick>=state.RecoveryClearUntilTick&&
            tick>=state.FighterDockClearUntilTick&&tick>=state.FighterLaunchClearUntilTick;}
        internal static bool AssignedPhase(int phase){return phase>=1&&phase<=4;}
    }
}
