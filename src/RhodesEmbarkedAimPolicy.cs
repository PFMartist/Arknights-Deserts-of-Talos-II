using System;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;

namespace EndfieldDoK.MapUnitLab
{
    // Deterministic policy only: observations are read from currently sensed
    // units by the adapter. This never changes an entity or an in-flight shot.
    internal static class RhodesEmbarkedAimPolicy
    {
        internal const int MaximumSamples=64,RangeMetres=LaevatainFlameAssets.RangeMeters,MaximumLeadMetres=60,MaximumSpeedMetresPerSecond=80;
        internal const int MinimumAutomaticValue=1,DetonationDelayMilliseconds=125;
        internal static int WindupMilliseconds(int kind)
        {return kind==0?LaevatainFlameAssets.WindUpMilliseconds:GilbertaGravityAssets.WindUpMilliseconds;}
        internal static int RadiusMetres(int kind)
        {return kind==0?LaevatainFlameAssets.RadiusMeters:GilbertaGravityAssets.RadiusMeters;}
        internal static Fixed64 CoverageRadius(int kind)
        {return Fixed64.FromRational(RadiusMetres(kind)*85,100);}
        internal static Vector2r Predict(Vector2r position,Vector2r velocity,int remainingWindupMilliseconds)
        {
            // A fixed 1.25s horizon, 80m/s speed and 60m displacement bound
            // prevents corrupt/stale motion from producing a distant marker.
            Fixed64 speed=velocity.Length,maximum=Fixed64.FromInt(MaximumSpeedMetresPerSecond);
            if(speed>maximum)velocity=velocity*(maximum/speed);
            int milliseconds=Math.Max(0,Math.Min(1125,remainingWindupMilliseconds))+DetonationDelayMilliseconds;
            Vector2r lead=velocity*Fixed64.FromRational(milliseconds,1000);
            Fixed64 length=lead.Length,cap=Fixed64.FromInt(MaximumLeadMetres);
            if(length>cap)lead=lead*(cap/length);
            return position+lead;
        }
        internal static Vector2r ClampToRange(Vector2r origin,Vector2r position)
        {
            Vector2r delta=position-origin;Fixed64 length=delta.Length,range=Fixed64.FromInt(RangeMetres);
            return length>range?origin+delta*(range/length):position;
        }
        internal static bool Covers(int kind,Vector2r aim,Vector2r expected)
        {Fixed64 radius=CoverageRadius(kind);return (expected-aim).LengthSquared<=radius*radius;}
        internal static bool ClearlyMisses(int kind,Vector2r aim,Vector2r expected,Vector2r velocity)
        {
            // Native processing can release within the next 125ms tick. Only
            // cancel beyond the real AoE plus that bounded timing tolerance.
            Fixed64 margin=Fixed64.Min(Fixed64.FromInt(10),velocity.Length*Fixed64.FromRational(125,1000));
            Fixed64 radius=Fixed64.FromInt(RadiusMetres(kind))+margin;
            return (expected-aim).LengthSquared>radius*radius;
        }
        internal static int Value(UnitClass unitClass,bool armed,bool inCombat,int hitpoints,int maximumHitpoints,int resourceCost)
        {
            if(hitpoints<=0)return 0;
            bool economy=(unitClass&UnitClass.Harvester)!=0;
            int value=armed&&!economy?(inCombat?180:100):economy?20:35;
            if(armed&&!economy)value+=Math.Min(60,Math.Max(0,resourceCost)/20);
            if(maximumHitpoints>0)
            {
                int percent=(int)Math.Min(100,Math.Max(0,(long)hitpoints*100/maximumHitpoints));
                // An almost-dead isolated enemy is usually a poor AoE/CC use.
                value=value*Math.Max(15,percent)/100;
            }
            return Math.Max(1,value);
        }
    }
}
