using System;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Unity.Game.World;
using UnityEngine;
using EndfieldDoK.MapUnitLab.ModCore;

namespace EndfieldDoK.MapUnitLab
{
    // Presentation-thread only. Native spawn/removal hooks are the primary path.
    // A five-second roster reconciliation recovers missed identity dispatch.
    // Known/quarantined entities are not repeatedly rebound. This is not three
    // separate four-Hz full-roster discovery loops or an automatic fault retry.
    internal static class UnitBindingRouter
    {
        static readonly DeferredBindingQueue<Entity,UnitView> Unknown=new DeferredBindingQueue<Entity,UnitView>();
        static readonly DeferredBindingQueue<Entity,UnitView> Faction=new DeferredBindingQueue<Entity,UnitView>();
        static readonly DeferredBindingQueue<Entity,UnitView> Humanoid=new DeferredBindingQueue<Entity,UnitView>();
        static readonly DeferredBindingQueue<Entity,UnitView> Carrier=new DeferredBindingQueue<Entity,UnitView>();
        static readonly DeferredBindingQueue<Entity,UnitView>.BindAttempt Classifier=Classify;
        static readonly DeferredBindingQueue<Entity,UnitView>.Expired Expiration=OnExpired;
        static readonly Dictionary<Entity,UnitView> Known=new Dictionary<Entity,UnitView>();
        static int LastFrame=-1;static float NextReconcile;
        public static void OnSpawned(UnitView view)
        {
            if(view==null||view.EntityID.Equals(Entity.None))return;
            UnitView prior;if(Known.TryGetValue(view.EntityID,out prior)&&System.Object.ReferenceEquals(prior,view))return;
            Known[view.EntityID]=view;Unknown.Enqueue(view.EntityID,view);
        }
        public static void OnRemoved(UnitView view)
        {
            if(System.Object.ReferenceEquals(view,null))return;Entity entity=view.EntityID;
            UnitView known;if(Known.TryGetValue(entity,out known)&&System.Object.ReferenceEquals(known,view))Known.Remove(entity);
            Unknown.Remove(entity,view);Faction.Remove(entity,view);Humanoid.Remove(entity,view);Carrier.Remove(entity,view);
        }
        static bool Classify(Entity entity,UnitView view)
        {
            if(view==null||!entity.Equals(view.EntityID))return true;
            if(!view.gameObject.activeInHierarchy||view.NonInterpolatedTickState==null)return false;
            string key=view.NonInterpolatedTickState.TypeID;
            if(key==RhodesCarrierUnits.UnitKey)Carrier.Enqueue(entity,view);
            else if(FactionUnits.IsFactionKey(key))Faction.Enqueue(entity,view);
            else if(Array.IndexOf(HumanoidUnits.Keys,key)>=0)Humanoid.Enqueue(entity,view);
            return true;
        }
        static void Prepare()
        {
            if(LastFrame==Time.frameCount)return;LastFrame=Time.frameCount;
            float now=Time.realtimeSinceStartup;
            if(now>=NextReconcile)
            {
                NextReconcile=now+5f;
                foreach(UnitView view in UnitViewDiscovery.Get())OnSpawned(view);
            }
            Unknown.Drain(now,.1d,64,120,Classifier,Expiration);
        }
        static void OnExpired(Entity entity,UnitView view)
        {Runtime.Log("UNIT_BINDING_EXPIRED entity="+entity+"; maximumAttempts=120; authorityContinues=True; retry=new_entity");}
        public static void BindFaction(DeferredBindingQueue<Entity,UnitView>.BindAttempt bind)
        {Prepare();Faction.Drain(Time.realtimeSinceStartup,.1d,32,120,bind,Expiration);}
        public static void BindHumanoid(DeferredBindingQueue<Entity,UnitView>.BindAttempt bind)
        {Prepare();Humanoid.Drain(Time.realtimeSinceStartup,.1d,32,120,bind,Expiration);}
        public static void BindCarrier(DeferredBindingQueue<Entity,UnitView>.BindAttempt bind)
        {Prepare();Carrier.Drain(Time.realtimeSinceStartup,.1d,8,120,bind,Expiration);}
        public static void EndMatch()
        {Unknown.Clear();Faction.Clear();Humanoid.Clear();Carrier.Clear();Known.Clear();LastFrame=-1;NextReconcile=0f;}
    }
}
