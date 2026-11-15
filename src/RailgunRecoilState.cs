using System;
namespace EndfieldDoK.ModelIntake
{
    // Presentation timing only. Does not fire a weapon, spend ammunition, apply
    // damage or alter turret targeting. Coordinates are project design units.
    public sealed class RailgunRecoilState
    {
        public const double Stroke=.55,KickSeconds=.06,HoldSeconds=.025,ReturnSeconds=.42;
        int Sequence;
        double Age=100,StartOffset;
        public void Initialize(int sequence){Sequence=sequence;Age=100;StartOffset=0;}
        static double Smooth(double t){t=Math.Max(0,Math.Min(1,t));return t*t*(3-2*t);}
        double Offset()
        {
            if(Age<KickSeconds){double t=Math.Max(0,Age/KickSeconds);return StartOffset+(Stroke-StartOffset)*(1-Math.Pow(1-t,3));}
            if(Age<KickSeconds+HoldSeconds)return Stroke;
            return Stroke*(1-Smooth((Age-KickSeconds-HoldSeconds)/ReturnSeconds));
        }
        public double Step(int sequence,double deltaSeconds,bool shownAlive)
        {
            if(double.IsNaN(deltaSeconds)||double.IsInfinity(deltaSeconds)||deltaSeconds<0)throw new ArgumentOutOfRangeException("deltaSeconds");
            int change=unchecked(sequence-Sequence);Sequence=sequence;
            if(!shownAlive){Age=100;StartOffset=0;return 0;}
            if(change<0){Age=100;StartOffset=0;return 0;}
            if(change>0){StartOffset=Offset();Age=0;}
            if(deltaSeconds>0)Age=Math.Min(100,Age+deltaSeconds);
            return Offset();
        }
    }
}
