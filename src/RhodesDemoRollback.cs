using BBI.Core.ComponentModel;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Retired demonstration entry points retained for the existing Runtime
    // call sites. A chosen native research route is permanent for this match.
    public static class RhodesDemoRollback
    {
        public static void Reset(){}
        public static bool Request(Entity entity,CommanderID requester){return false;}
        public static void MainTick(){}
        public static void SimTick(object sim){}
    }
}
