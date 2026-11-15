using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    internal static class RhodesConcealmentScopePolicy
    {
        internal static readonly CommanderID LabOwner=new CommanderID(1),LabObserver=new CommanderID(2);
        internal static CommanderID Owner(bool skirmish,CommanderID rhodesOwner)
        {return skirmish?rhodesOwner:LabOwner;}
        internal static bool ManagedOwner(bool skirmish,CommanderID owner,bool declared)
        {return owner!=CommanderID.None&&(skirmish?declared:owner==LabOwner);}
        internal static bool Observe(bool skirmish,CommanderID owner,CommanderID observer,int relationship)
        {
            if(owner==CommanderID.None||observer==CommanderID.None||owner==observer)return false;
            return skirmish?relationship==4:owner==LabOwner&&observer==LabObserver;
        }
    }
}
