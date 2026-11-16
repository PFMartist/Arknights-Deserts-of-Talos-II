namespace EndfieldDoK.MapUnitLab
{
    // Native PowerShunt owns heat accumulation, damage, cooling and events.
    // These are heat-load units, not degrees or a client-side temperature loop.
    public static class RhodesThermalProfile
    {
        public const int HeatThreshold=19,HeatCapacity=RhodesPowerProfile.MaximumPower;
        public const int OverheatingCapacity=120,CooldownRate=4,OverheatDamage=750;
        public const int NearOverheatWarningMargin=5,ReminderPeriodMS=30000;
        public static int HeatPointsForLevel(int level){return RhodesPowerProfile.ExtraPowerCost(level);}
        public static int CumulativeHeat(int level){return RhodesPowerProfile.CumulativePowerCost(level);}
    }
}
