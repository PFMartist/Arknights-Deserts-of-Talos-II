using System;
using System.Collections.Generic;

namespace EndfieldDoK.MapUnitLab.ModCore
{
    // Pure lifecycle queue. Membership snapshots allocate only after mutation;
    // a pooled object's old removal cannot cancel a replacement with the same key.
    public sealed class DeferredBindingQueue<TKey,TValue> where TValue:class
    {
        public delegate bool BindAttempt(TKey key,TValue value);
        public delegate void Expired(TKey key,TValue value);
        sealed class Entry
        {public TKey Key;public TValue Value;public double Next;public int Attempts;}
        readonly Dictionary<TKey,Entry> Pending=new Dictionary<TKey,Entry>();
        Entry[] Cached=new Entry[0];bool Dirty;int Cursor,Inspected;
        public int Count{get{return Pending.Count;}}
        public int LastInspected{get{return Inspected;}}
        public bool Enqueue(TKey key,TValue value)
        {
            if(Object.ReferenceEquals(value,null))throw new ArgumentNullException("value");
            Entry old;
            if(Pending.TryGetValue(key,out old)&&Object.ReferenceEquals(old.Value,value))return false;
            Pending[key]=new Entry{Key=key,Value=value};Dirty=true;return true;
        }
        public bool Remove(TKey key,TValue value)
        {
            Entry entry;
            if(!Pending.TryGetValue(key,out entry)||!Object.ReferenceEquals(entry.Value,value))return false;
            Pending.Remove(key);Dirty=true;return true;
        }
        public int Drain(double now,double retrySeconds,int maximum,BindAttempt bind)
        {return Drain(now,retrySeconds,maximum,Int32.MaxValue,bind,null);}
        public int Drain(double now,double retrySeconds,int maximum,int maximumAttempts,BindAttempt bind,Expired expired)
        {
            if(Double.IsNaN(now)||Double.IsInfinity(now)||now<0d)throw new ArgumentOutOfRangeException("now");
            if(Double.IsNaN(retrySeconds)||Double.IsInfinity(retrySeconds)||retrySeconds<=0d)throw new ArgumentOutOfRangeException("retrySeconds");
            if(maximum<1||maximumAttempts<1||bind==null)throw new ArgumentException("A bounded binding attempt is required");
            if(Dirty){Cached=new Entry[Pending.Count];Pending.Values.CopyTo(Cached,0);Dirty=false;}
            int attempts=0;Inspected=0;
            int inspectionBudget=(int)Math.Min(4096L,(long)maximum*4L);
            while(Inspected<Cached.Length&&Inspected<inspectionBudget&&attempts<maximum)
            {
                if(Cursor>=Cached.Length)Cursor=0;
                Entry entry=Cached[Cursor++];Inspected++;
                Entry current;
                if(!Pending.TryGetValue(entry.Key,out current)||!Object.ReferenceEquals(entry,current)||now<entry.Next)continue;
                attempts++;
                // Set the retry before the callback: a reentrant spawn/removal is
                // allowed to replace the entry without its deadline being changed.
                entry.Next=now+retrySeconds;
                entry.Attempts++;
                bool complete=bind(entry.Key,entry.Value);
                if(Pending.TryGetValue(entry.Key,out current)&&Object.ReferenceEquals(entry,current))
                {
                    if(complete||entry.Attempts>=maximumAttempts)
                    {
                        Pending.Remove(entry.Key);Dirty=true;
                        if(!complete&&expired!=null)expired(entry.Key,entry.Value);
                    }
                }
            }
            return attempts;
        }
        public void Clear(){Pending.Clear();Cached=new Entry[0];Dirty=false;Cursor=0;Inspected=0;}
    }
}
