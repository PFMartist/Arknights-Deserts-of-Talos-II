using System;

namespace EndfieldDoK.MapUnitLab
{
    // Consumes the inherited animation permission and Unity renderer visibility;
    // no distance thresholds, extra LOD selection or simulation state lives here.
    internal static class TrackAnimationPolicy60
    {
        internal static int BoneLods(int visibleMask){return visibleMask&3;}
        internal static int WheelLods(int visibleMask,bool nativeWheelsAllowed)
        {return nativeWheelsAllowed?(visibleMask&1):0;}
        internal static bool NeedsWrites(bool dirty,int previousMask,int activeMask)
        {return activeMask!=0&&(dirty||previousMask!=activeMask);}
        internal static void FillUsedBones(bool[][] usedByLod,int activeMask,bool[] result)
        {
            Array.Clear(result,0,result.Length);
            for(int lod=0;lod<2;lod++)if((activeMask&(1<<lod))!=0)
                for(int bone=0;bone<result.Length;bone++)
                    if(usedByLod[lod][bone])result[bone]=true;
        }
    }
}
