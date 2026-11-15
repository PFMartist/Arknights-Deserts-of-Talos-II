using System;
using System.Collections.Generic;

namespace EndfieldDoK.MapUnitLab
{
    // Pure authored-hierarchy policy, independent of PhysX scene registration.
    // Collider.attachedRigidbody is a native runtime association; it is not a
    // prerequisite for inspecting an unloaded/inactive serialized FX prefab.
    internal static class RhodesWreckPrefabPolicy
    {
        internal static bool IsRootBodyCollider<T>(T node,T root,Func<T,T> parent,Predicate<T> hasBody) where T:class
        {
            if(object.ReferenceEquals(node,null)||object.ReferenceEquals(root,null))return false;
            for(int depth=0;depth<256;depth++)
            {
                if(object.ReferenceEquals(node,null))return false;
                if(hasBody(node))return object.ReferenceEquals(node,root);
                if(object.ReferenceEquals(node,root))return false;
                node=parent(node);
            }
            throw new InvalidOperationException("Native carcass collider hierarchy exceeds its inspection budget");
        }
        internal static TCollider RequireRootCollider<T,TCollider>(IList<TCollider> colliders,T root,
            Func<TCollider,T> node,Func<T,T> parent,Predicate<T> hasBody) where T:class where TCollider:class
        {
            if(colliders==null)throw new ArgumentNullException("colliders");
            TCollider found=null;
            foreach(TCollider candidate in colliders)
            {
                if(object.ReferenceEquals(candidate,null)||!IsRootBodyCollider(node(candidate),root,parent,hasBody))continue;
                if(!object.ReferenceEquals(found,null))throw new InvalidOperationException("Native root carcass has multiple authored body colliders");
                found=candidate;
            }
            if(object.ReferenceEquals(found,null))throw new InvalidOperationException("Native root carcass has no authored body collider");
            return found;
        }
        internal static bool QueueDrained(int next,int total,bool modelJobPending,bool itemPending)
        {return next>=total&&!modelJobPending&&!itemPending;}
    }
}
