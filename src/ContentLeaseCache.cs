using System;
using System.Collections.Generic;

namespace EndfieldDoK.ModelIntake
{
    // Resource ownership, not an LRU: a zero-reference value is removed and
    // released immediately. A failed factory is never published. All callers
    // of one instance must use its owning thread (Unity caches use main only).
    internal sealed class ContentLeaseCache<T> where T:class
    {
        internal sealed class Entry
        {internal string Key;internal T Value;internal Action<T> Release;internal int References;}
        internal sealed class Lease:IDisposable
        {
            ContentLeaseCache<T> Owner;Entry ValueEntry;
            internal Lease(ContentLeaseCache<T> owner,Entry entry){Owner=owner;ValueEntry=entry;}
            internal T Value{get{if(ValueEntry==null)throw new ObjectDisposedException("resource lease");return ValueEntry.Value;}}
            public void Dispose()
            {ContentLeaseCache<T> owner=Owner;Entry entry=ValueEntry;Owner=null;ValueEntry=null;if(owner!=null)owner.Drop(entry);}
        }
        readonly Dictionary<string,Entry> Values=new Dictionary<string,Entry>(StringComparer.Ordinal);
        internal int Count{get{return Values.Count;}}
        internal int References{get{int count=0;foreach(Entry entry in Values.Values)count+=entry.References;return count;}}
        internal Lease Acquire(string key,Func<T> create,Action<T> release)
        {
            if(string.IsNullOrEmpty(key)||create==null||release==null)throw new ArgumentException("A resource needs content identity, factory and release");
            Entry entry;
            if(!Values.TryGetValue(key,out entry))
            {
                T value=create();if(value==null)throw new InvalidOperationException("Resource factory returned null");
                entry=new Entry{Key=key,Value=value,Release=release};Values.Add(key,entry);
            }
            entry.References=checked(entry.References+1);return new Lease(this,entry);
        }
        void Drop(Entry entry)
        {
            if(entry==null||entry.References<=0)throw new InvalidOperationException("Unbalanced resource lease");
            entry.References--;
            if(entry.References!=0)return;
            Values.Remove(entry.Key);T value=entry.Value;entry.Value=null;entry.Release(value);
        }
    }
}
