using System;
using BBI.Core.ComponentModel;
using BBI.Unity.Game.World;

namespace EndfieldDoK.MapUnitLab
{
    // Populated by exact native UnitView lifecycle hooks in the pinned build.
    // No Unity scene/resource scan, timer, worker or mutable-state cache.
    public static class UnitViewDiscovery
    {
        static readonly LiveObjectRegistry<Entity,UnitView> Registry=new LiveObjectRegistry<Entity,UnitView>();
        static readonly UnityEngine.Object[] Empty=new UnityEngine.Object[0];
        static string FailureMessage;
        static bool GuideRemovalWarningReported;
        public static long Version{get{return Registry.Version;}}
        public static int Count{get{return Registry.Count;}}
        public static string Failure{get{return FailureMessage;}}

        // Successful OnSpawned return: EntityID and native state already exist.
        // Do not filter visibility/selection here; consumers check current state.
        public static void OnNativeSpawned(UnitView view)
        {
            if(FailureMessage!=null)return;
            try{
                if(view==null)return;
                Entity entity=view.EntityID;
                if(entity.Equals(Entity.None))return;
                if(Registry.Register(entity,view))
                {
                    foreach(IsolatedUnitPresentation driver in view.GetComponents<IsolatedUnitPresentation>())
                    {
                        if(driver==null)continue;
                        try{driver.PrepareNativeSpawn(view);}
                        catch(Exception error){driver.QuarantineBinding(view,"spawn_recovery",error);}
                    }
                    UnitBindingRouter.OnSpawned(view);
                }
            }catch(Exception error){FailureMessage=error.GetType().FullName+": "+error.Message;}
        }
        // Prefix runs before native despawn clears settings or pooling disables
        // the view. Destroy is also hooked as a final cleanup backstop.
        public static void OnNativeRemoved(UnitView view)
        {
            try{RhodesV17AviationGuideDriver.OnNativeRemoved(view);}
            catch(Exception error)
            {
                if(!GuideRemovalWarningReported)
                {GuideRemovalWarningReported=true;Runtime.Log("OPTIONAL_GUIDE_CLEANUP_FAILED nativeViewRemovalContinues=True; error="+error);}
            }
            try{UnitBindingRouter.OnRemoved(view);Registry.Remove(view);}
            catch(Exception error){if(FailureMessage==null)FailureMessage=error.GetType().FullName+": "+error.Message;}
        }
        // Preserve existing callers; covariance does not copy the typed array.
        // Treat this snapshot as read-only. Its reference changes only on roster
        // mutation; ordinary polling no longer allocates or scans the scene.
        public static UnityEngine.Object[] Get(){return FailureMessage==null?Registry.Snapshot():Empty;}
        public static bool TryGet(Entity entity,out UnitView view){return Registry.TryGet(entity,out view);}
        public static void Reset(){UnitBindingRouter.EndMatch();Registry.Clear();FailureMessage=null;GuideRemovalWarningReported=false;}
    }
}
