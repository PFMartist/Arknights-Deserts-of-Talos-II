namespace EndfieldDoK.MapUnitLab
{
    internal static class RhodesTiltrotorConcealmentPolicy
    {
        internal const int SampleTicks=4,FireRevealTicks=48,LeaseMilliseconds=1000;
        internal const float ProximityMetres=180f;
        internal static bool IsConcealed(int tick,int revealedUntil,float nearestEnemySquared)
        {return tick>=revealedUntil&&nearestEnemySquared>ProximityMetres*ProximityMetres;}
    }
}
