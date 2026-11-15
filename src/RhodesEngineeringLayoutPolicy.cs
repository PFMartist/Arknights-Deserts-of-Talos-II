using System;

namespace EndfieldDoK.MapUnitLab
{
    // Geometry only. No research state, command, refund, or slider mutation.
    internal static class RhodesEngineeringLayoutPolicy
    {
        internal const int CancelWidth=36,CancelHeight=32,RightPadding=6,TimerGap=8,NameGap=8;
        internal static float ExtraHeight(int visibleChoices,float rowPitch)
        {
            if(visibleChoices<0||rowPitch<=0f||float.IsNaN(rowPitch)||float.IsInfinity(rowPitch))throw new ArgumentOutOfRangeException("row layout");
            return Math.Max(0,visibleChoices-1)*rowPitch;
        }
        internal static float CancelCenter(float width)
        {return width-RightPadding-CancelWidth*.5f;}
        internal static float TimerRight(float width)
        {return width-RightPadding-CancelWidth-TimerGap;}
        internal static int NameReservedWidth(int timerWidth)
        {return RightPadding+CancelWidth+TimerGap+Math.Max(0,timerWidth)+NameGap;}
    }
}
