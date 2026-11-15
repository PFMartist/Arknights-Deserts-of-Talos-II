namespace EndfieldDoK.MapUnitLab
{
    // Native DockGoal selects its first bay by subset class matching.
    internal static class RhodesTiltrotorReturnPolicy
    {
        internal static int FirstCompatibleBay(int passengerClass,int[] bayClasses)
        {if(bayClasses==null)return -1;for(int i=0;i<bayClasses.Length;i++)if((bayClasses[i]&passengerClass)==passengerClass)return i;return -1;}
        internal static bool ShouldRetarget(bool privateOwnedAircraft,bool sameCarrierDockGoal,bool exactReservedSlot,bool nativeCaptureStarted,bool offsetAlreadyExact)
        {return privateOwnedAircraft&&sameCarrierDockGoal&&exactReservedSlot&&!nativeCaptureStarted&&!offsetAlreadyExact;}
    }
}
