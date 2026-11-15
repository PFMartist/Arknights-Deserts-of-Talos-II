using System;

namespace EndfieldDoK.MapUnitLab
{
    // Compressed-map gameplay distances in world metres, not real weapon data.
    public sealed class HumanoidRangeProfile
    {
        public readonly int Weapon, Sensor, Contact, Aggro, Alert, Leash;
        public static readonly HumanoidRangeProfile Guard = new HumanoidRangeProfile(100, 180, 220, 100, 140, 160);
        public static readonly HumanoidRangeProfile Perlica = new HumanoidRangeProfile(140, 220, 260, 140, 180, 200);
        public const int TargetForwardDistance = 150;

        public HumanoidRangeProfile(int weapon, int sensor, int contact, int aggro, int alert, int leash)
        {
            if (weapon < 1 || weapon > sensor || sensor > contact || contact > 2000 ||
                aggro < 0 || aggro > weapon || aggro > alert || alert > leash || leash > contact || alert > sensor)
                throw new ArgumentException("Inconsistent humanoid sight, contact, weapon or pursuit distances");
            Weapon = weapon; Sensor = sensor; Contact = contact; Aggro = aggro; Alert = alert; Leash = leash;
        }
    }
}
