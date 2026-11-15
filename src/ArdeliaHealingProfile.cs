using System;

namespace EndfieldDoK.MapUnitLab
{
    // First-test candidate. The G baserunner pulse geometry/cadence is native;
    // the 30s hero cooldown and ally-only finite HoT are explicit adaptations.
    public static class ArdeliaHealingProfile
    {
        public const int RadiusMetres=400,HealPerSecond=40,PulseCount=10,PulseMilliseconds=1000;
        public const int CooldownSeconds=30,WeaponWindupMilliseconds=0;
        public const float FieldSeconds=10f,HealLeaseSeconds=1.125f;
        public const int Health=1600,Armour=3,ProductionCU=300,ProductionRU=50,ProductionSeconds=10,Population=1;
        public static bool FriendlyRelationship(int relationship){return relationship==1||relationship==2;}
        public static bool IsDamagedAlive(int health,int maximum,bool dead,bool docked,bool suspended,bool world,bool presentationOnly)
        {return !dead&&!docked&&!suspended&&world&&!presentationOnly&&health>0&&maximum>health;}
        public static bool IsWithinRadius(long squaredMetres){return squaredMetres>=0&&squaredMetres<=(long)RadiusMetres*RadiusMetres;}
    }
}
