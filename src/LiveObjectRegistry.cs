using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace EndfieldDoK.MapUnitLab
{
    // Presentation-thread lifecycle table. Keys include the engine's creation
    // identity, while values are compared by reference (including pooled views).
    // Caches identities only: position, health and other mutable state remain
    // owned by the native object. Snapshot allocation occurs only after a change.
    internal sealed class LiveObjectRegistry<TKey,TValue> where TValue:class
    {
        sealed class ReferenceComparer:IEqualityComparer<TValue>
        {
            public bool Equals(TValue left,TValue right){return Object.ReferenceEquals(left,right);}
            public int GetHashCode(TValue value){return RuntimeHelpers.GetHashCode(value);}
        }
        readonly Dictionary<TKey,TValue> ByKey=new Dictionary<TKey,TValue>();
        readonly Dictionary<TValue,TKey> ByValue=new Dictionary<TValue,TKey>(new ReferenceComparer());
        readonly List<TValue> Ordered=new List<TValue>();
        static readonly TValue[] Empty=new TValue[0];
        TValue[] Cached=Empty;
        bool Dirty;
        long CurrentVersion;

        public int Count{get{return Ordered.Count;}}
        public long Version{get{return CurrentVersion;}}
        public bool Register(TKey key,TValue value)
        {
            if(Object.ReferenceEquals(value,null))throw new ArgumentNullException("value");
            TKey oldKey;TValue oldValue;
            if(ByValue.TryGetValue(value,out oldKey)){
                if(EqualityComparer<TKey>.Default.Equals(oldKey,key))return false;
                Remove(value); // Same pooled object now represents a new entity.
            }
            if(ByKey.TryGetValue(key,out oldValue))Remove(oldValue);
            ByKey.Add(key,value);ByValue.Add(value,key);Ordered.Add(value);Changed();return true;
        }
        public bool Remove(TValue value)
        {
            if(Object.ReferenceEquals(value,null))return false;
            TKey key;if(!ByValue.TryGetValue(value,out key))return false;
            ByValue.Remove(value);ByKey.Remove(key);
            for(int index=0;index<Ordered.Count;index++)if(Object.ReferenceEquals(Ordered[index],value)){Ordered.RemoveAt(index);break;}
            Changed();return true;
        }
        public bool TryGet(TKey key,out TValue value){return ByKey.TryGetValue(key,out value);}
        public TValue[] Snapshot()
        {
            if(Dirty){Cached=Ordered.Count==0?Empty:Ordered.ToArray();Dirty=false;}
            return Cached;
        }
        public void Clear()
        {
            if(Ordered.Count==0&&ByKey.Count==0&&ByValue.Count==0){Cached=Empty;Dirty=false;return;}
            ByKey.Clear();ByValue.Clear();Ordered.Clear();Cached=Empty;Dirty=false;CurrentVersion++;
        }
        void Changed(){Dirty=true;CurrentVersion++;}
    }
}
