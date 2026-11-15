using System;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Author: PFMartist. AI collaboration: GPT (Codex).
    // Native CreateInstance sends OnEnable before the caller can populate the
    // serialized fields. The supported BBI container/movement callbacks assume
    // those fields are already present. Delay only that controlled empty-shell
    // callback; existing deep-copy/remap and final explicit OnEnable stay owned
    // by the caller. This helper neither clones donor data nor borrows a donor
    // cache, and it must remain independent of Runtime static initialization.
    public static class RhodesAssetClone59
    {
        public static ScriptableObject CreateEmpty(Type assetType)
        {
            if(assetType==null)throw new ArgumentNullException("assetType");
            if(assetType.IsAbstract||!typeof(ScriptableObject).IsAssignableFrom(assetType))
                throw new ArgumentException("Native asset creation requires a concrete ScriptableObject type","assetType");
            RhodesAssetCreationPolicy59.Scope scope=RhodesAssetCreationPolicy59.Begin(assetType);
            ScriptableObject result=null;
            try
            {
                result=ScriptableObject.CreateInstance(assetType);
                if(result==null)throw new InvalidOperationException("Native asset creation returned no live instance: "+assetType.FullName);
                scope.RequireReturned(result,RequiresCallbackGuard(assetType));
                return result;
            }
            catch
            {
                if(result!=null)UnityEngine.Object.DestroyImmediate(result);
                throw;
            }
            finally{scope.Dispose();}
        }

        public static T CreateEmpty<T>() where T:ScriptableObject
        {return (T)CreateEmpty(typeof(T));}

        // Patcher prefixes only AssetContainer.OnEnable,
        // UnitAssetContainer.OnEnable and UnitMovementAttributesAsset.OnEnable.
        // This method performs no Unity native call, file IO or logging.
        public static bool ShouldDeferEnable(ScriptableObject asset)
        {return RhodesAssetCreationPolicy59.ShouldDefer(asset);}

        static bool RequiresCallbackGuard(Type type)
        {
            for(Type current=type;current!=null;current=current.BaseType)
                if(current.FullName=="BBI.Unity.Game.Data.AssetContainer"||
                    current.FullName=="BBI.Unity.Game.Data.UnitMovementAttributesAsset")return true;
            return false;
        }
    }
}
