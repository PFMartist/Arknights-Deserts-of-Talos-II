using System;

namespace EndfieldDoK.MapUnitLab
{
    internal enum RhodesTiltrotorOrder { Hold,Move,AttackTarget,AttackMove,Return }
    internal static class RhodesTiltrotorOrderPolicy
    {
        internal const float ArrivalMetres=16f,ArrivalSpeedMetresPerSecond=2f;
        internal const float VerticalMetresPerSecond=12f,GroundClearanceMetres=.35f,CruiseAltitudeMetres=120f;
        // One full cruise-to-ground descent, plus two seconds for an unseen
        // aircraft to settle. Keep both cargo and flight guards in sync.
        internal static int UnobservedLandingTicks
        {get{return (int)Math.Ceiling((CruiseAltitudeMetres-GroundClearanceMetres)/VerticalMetresPerSecond/.125f)+16;}}
        internal static float Altitude(float start,bool landing,int elapsedTicks)
        {
            float target=landing?GroundClearanceMetres:CruiseAltitudeMetres;
            return Altitude(start,target,elapsedTicks);
        }
        internal static float Altitude(float start,float target,int elapsedTicks)
        {
            float travel=Math.Max(0,elapsedTicks)*.125f*VerticalMetresPerSecond;
            return start<target?Math.Min(target,start+travel):Math.Max(target,start-travel);
        }
        internal static bool AllowsFire(RhodesTiltrotorOrder order,bool matchesOrderedTarget)
        {return order==RhodesTiltrotorOrder.AttackMove||(order==RhodesTiltrotorOrder.AttackTarget&&matchesOrderedTarget);}
        internal static bool UsesNativeSortie(RhodesTiltrotorOrder order)
        {return order==RhodesTiltrotorOrder.Return;}
        internal static bool Arrived(float distanceSquared,float speedSquared)
        {return distanceSquared<=ArrivalMetres*ArrivalMetres&&speedSquared<=ArrivalSpeedMetresPerSecond*ArrivalSpeedMetresPerSecond;}
    }
}
