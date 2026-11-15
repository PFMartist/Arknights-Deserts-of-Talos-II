using System;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Value-only presentation history. The caller holds DeploymentState.Gate;
    // no Unity object, Sim action, timer or extra polling is retained here.
    internal sealed class RhodesWreckTerminalMemo
    {
        internal const int Capacity=128;
        internal static readonly Comparison<Entity> EntityOrder=CompareEntities;
        struct Key:IEquatable<Key>
        {
            internal Entity Entity;internal CommanderID Owner;
            internal Key(Entity entity,CommanderID owner){Entity=entity;Owner=owner;}
            public bool Equals(Key other){return Entity.Equals(other.Entity)&&Owner.Equals(other.Owner);}
            public override bool Equals(object value){return value is Key&&Equals((Key)value);}
            public override int GetHashCode(){return unchecked(Entity.GetHashCode()*397^Owner.GetHashCode());}
        }
        readonly Dictionary<Key,RhodesDeploymentSnapshot> Values=new Dictionary<Key,RhodesDeploymentSnapshot>();
        readonly Queue<Key> Order=new Queue<Key>();
        internal int Count{get{return Values.Count;}}
        internal void Remember(Entity entity,CommanderID owner,RhodesDeploymentSnapshot state)
        {
            if(entity.Equals(Entity.None)||owner.Equals(CommanderID.None))return;
            Key key=new Key(entity,owner);
            if(Values.ContainsKey(key)){Values[key]=state;return;}
            while(Values.Count>=Capacity)Values.Remove(Order.Dequeue());
            Values.Add(key,state);Order.Enqueue(key);
        }
        internal bool TryGet(Entity entity,CommanderID owner,out RhodesDeploymentSnapshot state)
        {return Values.TryGetValue(new Key(entity,owner),out state);}
        internal void Clear(){Values.Clear();Order.Clear();}
        internal static int CompareEntities(Entity a,Entity b)
        {
            // Audited native Entity.GetHashCode is the exact 32-bit packing
            // (Int16 Index << 16) | UInt16 CreationIDCheck, not a salted hash.
            return a.GetHashCode().CompareTo(b.GetHashCode());
        }
    }

    internal static class RhodesCarrierWreckSelection
    {
        // Exactly three settled silhouettes are authored. Research completion
        // during first deployment keeps the previous committed base silhouette.
        // L1/L2 and arbitrary intermediate physical poses are not separately baked.
        internal static int FormIndex(bool found,RhodesDeploymentSnapshot state)
        {
            if(!found)return 0;
            string form=RhodesCarrierFormVoice.Form(state);
            return form=="aviation"?1:form=="gunnery"?2:0;
        }
        internal static int VisibleMask(int index){return 1<<(index>=0&&index<3?index:0);}
        internal static void UpdateCachedRoots<T>(KeyValuePair<T,bool>[] states,T[] roots,int mask) where T:class
        {
            if(states==null||roots==null||roots.Length!=3||object.ReferenceEquals(roots[0],null)||object.ReferenceEquals(roots[1],null)||object.ReferenceEquals(roots[2],null)||
                object.ReferenceEquals(roots[0],roots[1])||object.ReferenceEquals(roots[0],roots[2])||object.ReferenceEquals(roots[1],roots[2])||
                mask<0||mask>4||(mask!=0&&(mask&(mask-1))!=0))throw new InvalidOperationException("Invalid carrier wreck root/cache mask");
            int a=-1,b=-1,c=-1;
            for(int i=0;i<states.Length;i++)
            {
                T key=states[i].Key;
                if(object.ReferenceEquals(key,roots[0]))a=i;
                else if(object.ReferenceEquals(key,roots[1]))b=i;
                else if(object.ReferenceEquals(key,roots[2]))c=i;
            }
            if(a<0||b<0||c<0)throw new InvalidOperationException("Native instance FX cache does not contain all carrier wreck roots");
            // Only these three owned root records change. Native helper nodes,
            // descendants, particles and another prefab's cache stay untouched.
            states[a]=new KeyValuePair<T,bool>(roots[0],(mask&1)!=0);
            states[b]=new KeyValuePair<T,bool>(roots[1],(mask&2)!=0);
            states[c]=new KeyValuePair<T,bool>(roots[2],(mask&4)!=0);
        }
    }
}
