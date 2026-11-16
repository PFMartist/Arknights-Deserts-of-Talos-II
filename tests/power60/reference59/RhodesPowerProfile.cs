using System;

namespace EndfieldDoK.MapUnitLab
{
    // Profile28: native allocator owns reservations, charging and refunds.
    public static class RhodesPowerProfile
    {
        public const int SystemCount=4,MaximumLevel=5,InitialPower=5,MaximumPower=24;
        public const float ChargeSecondsPerLevel=3f,DrainSecondsPerLevel=0f;
        public const int HeatThreshold=RhodesThermalProfile.HeatThreshold;
        static readonly int[] Capacities={5,7,10,14,19,24},Armour={0,2,4,7,10,14},CiwsDamages={0,6,8,8,10,12};
        static readonly string[] Systems={"EF_RHODES_POWER_WEAPONS","EF_RHODES_POWER_DAMAGE_CONTROL","EF_RHODES_POWER_MANEUVER","EF_RHODES_POWER_FIRE_CONTROL"};
        public static int StartingLevel(int system){CheckSystem(system);return 1;}
        public static int ExtraPowerCost(int level){CheckLevel(level);return level;}
        public static int CumulativePowerCost(int level){CheckLevel(level);return level*(level+1)/2;}
        public static int CapacityForResearchLevel(int stage)
        {CheckLevel(stage);return Capacities[stage];}
        public static int HullWeaponDamagePercent(int level){CheckLevel(level);return 8*level;}
        public static int AircraftDamagePercent(int level){CheckLevel(level);return 4*level;}
        public static int InterceptorServicePercent(int level){CheckLevel(level);return 0;}
        public static int RepairPulse(int level){CheckLevel(level);return 1+level/2;}
        public static int RepairDamagePercent(int level){return (RepairPulse(level)-1)*100;}
        public static int DamageReceivedPercent(int level){CheckLevel(level);return 0;}
        public static int ArmourIncrease(int level){CheckLevel(level);return Armour[level];}
        public static int SpeedPercent(int level){CheckLevel(level);return 8*level;}
        public static int SensorContactIncrease(int level){CheckLevel(level);return 100*level;}
        public static int MainRangePercent(int level){CheckLevel(level);return 4*level;}
        public static int CiwsDamage(int level){CheckLevel(level);return CiwsDamages[level];}
        public static int CiwsRateOfFire(int level){CheckLevel(level);return 2*level;}
        public static int CiwsRange(int level){CheckLevel(level);return level==0?0:600+150*level;}
        public static string SystemName(int system)
        {CheckSystem(system);return Systems[system];}
        public static string StatusName(int system,int level){CheckLevel(level);return SystemName(system)+"_P28_L"+level+"_Status";}
        public static void CheckSystem(int system){if(system<0||system>=SystemCount)throw new ArgumentOutOfRangeException("system");}
        public static void CheckLevel(int level){if(level<0||level>MaximumLevel)throw new ArgumentOutOfRangeException("level");}
    }
}
