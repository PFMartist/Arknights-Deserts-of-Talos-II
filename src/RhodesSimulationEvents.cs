using System.Collections.Generic;
using BBI.Core.ComponentModel;

namespace EndfieldDoK.MapUnitLab
{
    // A single pre-native callback installs observers before Sim.Tick emits
    // commands and weapon events. All progression remains in Runtime.SimTick.
    public static class RhodesSimulationEvents
    {
        // Pinned native Entity.GetHashCode is (Index << 16) | CreationIDCheck;
        // the packed identity is stable across peers and entity generations.
        sealed class EntityOrder : IComparer<Entity>
        {public int Compare(Entity first,Entity second){return CompareEntities(first,second);}}
        internal static readonly IComparer<Entity> EntityComparer=new EntityOrder();
        internal static int CompareEntities(Entity first,Entity second)
        {return first.GetHashCode().CompareTo(second.GetHashCode());}
        public static void BeforeNativeTick(object sim)
        {
            if(!Runtime.RhodesCarrier||sim==null)return;
            RhodesResearch.PrepareSimulation(sim);
            RhodesTiltrotorFlightControl.PrepareEvents(sim);
            RhodesOperatorTransport.PrepareEvents(sim);
            RhodesEmbarkedOperatorSkills.PrepareEvents(sim);
            RhodesTiltrotorConcealment.PrepareEvents(sim);
            LaevatainFlameBridge.PrepareEvents(sim);
            GilbertaGravityBridge.PrepareEvents(sim);
            ArdeliaHealingBridge.PrepareEvents(sim);
        }
    }
}
