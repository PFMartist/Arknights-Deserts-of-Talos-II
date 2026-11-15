using System;
using System.Reflection;
using BBI.Unity.Core.World;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Keep the native wrapper's LODGroup: UnitView.Awake caches this exact
    // root component, and ChangePhysicsLOD already owns its automatic/forced
    // mode. Only the private clone's renderer lists and bounds are replaced.
    internal static class NativeUnitLodAdapter
    {
        delegate LODGroupController.ModelLOD NativeLodGetter(UnitView view);
        static readonly NativeLodGetter ReadLod=BindNativeLodGetter();

        static NativeLodGetter BindNativeLodGetter()
        {
            // This inspected build exposes a private pure getter. Bind its
            // exact native ABI once, rather than reflection-invoking it from
            // every unit/frame or adding another native patch surface.
            PropertyInfo property=typeof(UnitView).GetProperty("CurrentModelLOD",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            MethodInfo getter=property==null?null:property.GetGetMethod(true);
            if(getter==null||getter.DeclaringType!=typeof(UnitView)||getter.IsStatic||
                getter.ReturnType!=typeof(LODGroupController.ModelLOD)||getter.GetParameters().Length!=0)
                throw new InvalidOperationException("Native UnitView LOD getter ABI differs");
            return (NativeLodGetter)Delegate.CreateDelegate(typeof(NativeLodGetter),getter);
        }

        internal static int CurrentLod(UnitView view)
        {return object.ReferenceEquals(view,null)?3:(int)ReadLod(view);}

        internal static bool CompatibleLevelCounts(int nativeCount,int authoredCount)
        {return nativeCount>0&&nativeCount<=8&&nativeCount==authoredCount;}

        internal static int ResolveLod(int nativeLod,bool alwaysHigh)
        {return nativeLod<0||nativeLod>3?3:alwaysHigh?0:nativeLod;}

        internal static bool AnimationActive(int nativeLod,int mask,bool alwaysHigh)
        {
            int lod=ResolveLod(nativeLod,alwaysHigh);
            return lod<3&&(mask&(1<<lod))!=0;
        }

        internal static bool NeedsActuationWrites(bool dirty,int priorVisibleMask,int visibleMask)
        {return visibleMask!=0&&(dirty||priorVisibleMask!=visibleMask);}

        internal static bool AimWriteAllowed(int jointKind,bool nativeActive,bool fxCritical)
        {return jointKind!=2&&jointKind!=3||nativeActive||fxCritical;}

        internal static LODGroup AdoptGeometry(UnitView view,LODGroup authored)
        {
            if(view==null||authored==null||!authored.transform.IsChildOf(view.transform))
                throw new InvalidOperationException("Native LOD adoption requires owned authored geometry");
            LODGroup native=view.GetComponent<LODGroup>();
            if(native==null||native==authored)
                throw new InvalidOperationException("Native wrapper root LODGroup is missing or aliases authored geometry");
            LOD[] nativeLevels=native.GetLODs(),authoredLevels=authored.GetLODs();
            if(!CompatibleLevelCounts(nativeLevels.Length,authoredLevels.Length))
                throw new InvalidOperationException("Native and authored LOD level counts must match explicitly");
            LOD[] combined=new LOD[nativeLevels.Length];
            float prior=1.01f;
            for(int level=0;level<combined.Length;level++)
            {
                float threshold=nativeLevels[level].screenRelativeTransitionHeight;
                if(float.IsNaN(threshold)||float.IsInfinity(threshold)||threshold<0f||threshold>1f||threshold>=prior)
                    throw new InvalidOperationException("Native LOD thresholds are invalid or unordered");
                prior=threshold;
                Renderer[] renderers=authoredLevels[level].renderers;
                if(renderers==null||renderers.Length==0)
                    throw new InvalidOperationException("Authored native LOD renderer level is empty");
                foreach(Renderer renderer in renderers)
                    if(renderer==null||!renderer.transform.IsChildOf(authored.transform))
                        throw new InvalidOperationException("Authored native LOD renderer escaped its owned geometry");
                combined[level]=new LOD(threshold,renderers);
                combined[level].fadeTransitionWidth=nativeLevels[level].fadeTransitionWidth;
            }
            bool nativeEnabled=native.enabled,authoredEnabled=authored.enabled;
            try
            {
                // Do not leave a renderer registered with two active groups.
                authored.SetLODs(new LOD[0]);authored.enabled=false;
                native.SetLODs(combined);native.RecalculateBounds();native.enabled=true;
            }
            catch
            {
                try{native.SetLODs(nativeLevels);native.enabled=nativeEnabled;}catch{}
                try{authored.SetLODs(authoredLevels);authored.enabled=authoredEnabled;}catch{}
                throw;
            }
            return native;
        }
    }
}
