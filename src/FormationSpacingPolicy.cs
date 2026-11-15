using System;

namespace EndfieldDoK.MapUnitLab
{
    // Native MemberGap is a center-to-center pitch shared by both axes.
    // ForwardGap is separate edge clearance between different sections.
    // This policy configures private definitions once; native formation and
    // avoidance processors still own every destination and movement update.
    public struct NativeFormationSpacing
    {
        public readonly float MemberGap,SectionGapAhead;
        internal NativeFormationSpacing(float memberGap,float sectionGapAhead)
        {MemberGap=memberGap;SectionGapAhead=sectionGapAhead;}
    }

    public static class FormationSpacingPolicy
    {
        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        static void Positive(float value,string name)
        {if(!Finite(value)||value<=0f)throw new ArgumentOutOfRangeException(name);}
        static void Nonnegative(float value,string name)
        {if(!Finite(value)||value<0f)throw new ArgumentOutOfRangeException(name);}

        public static NativeFormationSpacing Evaluate(float width,float length,float donorWidth,float donorLength,float donorMemberGap,float donorSectionGapAhead)
        {
            Positive(width,"width");Positive(length,"length");Positive(donorWidth,"donorWidth");Positive(donorLength,"donorLength");
            Nonnegative(donorMemberGap,"donorMemberGap");Nonnegative(donorSectionGapAhead,"donorSectionGapAhead");
            float body=Math.Max(width,length),scale=body/Math.Max(donorWidth,donorLength);
            float member=Math.Max(donorMemberGap*scale,body*1.15f+1f),section=donorSectionGapAhead*scale;
            Positive(member,"memberGap");Nonnegative(section,"sectionGapAhead");
            return new NativeFormationSpacing(member,section);
        }

        public static float PreserveDynamicAvoidance(float geometricClearance,float donorDistance)
        {
            Positive(geometricClearance,"geometricClearance");Nonnegative(donorDistance,"donorDistance");
            return Math.Max(geometricClearance,donorDistance);
        }
    }
}
