using System;
using System.Collections.Generic;
using System.Threading;

namespace EndfieldDoK.MapUnitLab.ModCore
{
    // Pure managed work only. One worker/budget across all session generations
    // for T; no Unity objects, logging, timers, joins or VO event publication.
    internal sealed class BoundedVoicePcmPrewarm48<T>:IDisposable where T:class
    {
        internal const long MaximumBufferedBytes=12L*1024*1024,WorkReservationBytes=4L*1024*1024,MaximumResultBytes=2L*1024*1024;
        internal const int MaximumPending=8,MaximumReady=16;
        static readonly object Gate=new object();
        static readonly Queue<Job> Work=new Queue<Job>();
        static readonly List<Ready> Buffered=new List<Ready>();
        static readonly WaitCallback Callback=Run;
        static Job Running;
        static bool WorkerQueued;
        static long BufferedBytes,ReservedBytes,Clock;
        readonly Func<string,T> Decode;
        readonly Func<T,long> Size;
        readonly Dictionary<string,Job> Pending=new Dictionary<string,Job>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string,Ready> Results=new Dictionary<string,Ready>(StringComparer.OrdinalIgnoreCase);
        bool Closed;
        int Completed,Failures;
        string LastFailure;
        sealed class Job {internal BoundedVoicePcmPrewarm48<T> Owner;internal string Path;internal bool Cancelled;}
        sealed class Ready {internal BoundedVoicePcmPrewarm48<T> Owner;internal string Path;internal T Value;internal long Bytes,Use;}
        internal struct Snapshot
        {
            internal long GlobalReadyBytes,GlobalWorkReservation,GlobalBufferedBytes;
            internal int GlobalPending,OwnerReady,OwnerCompleted,OwnerFailures;
            internal bool Disposed;internal string Failure;
        }
        internal BoundedVoicePcmPrewarm48(Func<string,T> decode,Func<T,long> size)
        {if(decode==null||size==null)throw new ArgumentNullException("decode/size");Decode=decode;Size=size;}
        internal bool Request(string path)
        {
            if(string.IsNullOrEmpty(path))return false;bool start=false;
            lock(Gate){
                if(Closed)return false;Ready ready;
                if(Results.TryGetValue(path,out ready)){ready.Use=++Clock;return true;}
                if(Pending.ContainsKey(path))return true;
                if(Work.Count+(Running==null?0:1)>=MaximumPending)return false;
                Job job=new Job{Owner=this,Path=path};Pending.Add(path,job);Work.Enqueue(job);
                if(!WorkerQueued){WorkerQueued=true;start=true;}
            }
            if(start){
                try{if(!ThreadPool.QueueUserWorkItem(Callback))ScheduleFailed("ThreadPool rejected voice warmup");}
                catch(Exception error){ScheduleFailed(error.GetType().Name+": "+error.Message);}
            }
            return true;
        }
        // Never wait on the worker at playback. A miss cancels pending warmup
        // for this path and lets the existing synchronous first-play path run.
        internal bool TryTakeOrCancel(string path,out T value)
        {
            value=null;lock(Gate){
                if(Closed)return false;Ready ready;
                if(Results.TryGetValue(path,out ready)){value=ready.Value;RemoveReady(ready);return true;}
                Job pending;if(Pending.TryGetValue(path,out pending)){pending.Cancelled=true;Pending.Remove(path);}
                return false;
            }
        }
        static void ScheduleFailed(string failure)
        {
            lock(Gate){
                while(Work.Count>0){Job job=Work.Dequeue();Forget(job);job.Owner.Failures++;job.Owner.LastFailure=failure;}
                WorkerQueued=false;
            }
        }
        static void Forget(Job job)
        {Job actual;if(job.Owner.Pending.TryGetValue(job.Path,out actual)&&ReferenceEquals(actual,job))job.Owner.Pending.Remove(job.Path);}
        static void RemoveReady(Ready ready)
        {ready.Owner.Results.Remove(ready.Path);Buffered.Remove(ready);BufferedBytes-=ready.Bytes;ready.Value=null;}
        static void MakeRoom(long bytes,int incoming)
        {
            while(Buffered.Count>0&&(BufferedBytes+bytes>MaximumBufferedBytes||Buffered.Count+incoming>MaximumReady)){
                Ready oldest=Buffered[0];foreach(Ready item in Buffered)if(item.Use<oldest.Use)oldest=item;RemoveReady(oldest);
            }
        }
        static void Run(object unused)
        {
            for(;;){
                Job job;
                lock(Gate){
                    if(Work.Count==0){WorkerQueued=false;return;}
                    job=Work.Dequeue();
                    if(job.Cancelled||job.Owner.Closed){Forget(job);continue;}
                    MakeRoom(WorkReservationBytes,0);ReservedBytes=WorkReservationBytes;Running=job;
                }
                T decoded=null;long bytes=0;string failure=null;
                try{
                    decoded=job.Owner.Decode(job.Path);
                    if(decoded==null)throw new InvalidOperationException("Voice decoder returned null");
                    bytes=job.Owner.Size(decoded);
                    if(bytes<1||bytes>MaximumResultBytes)throw new InvalidOperationException("Voice warmup decoded byte budget exceeded");
                }catch(Exception error){failure=error.GetType().Name+": "+error.Message;}
                lock(Gate){
                    Running=null;ReservedBytes=0;Forget(job);
                    if(failure!=null){job.Owner.Failures++;job.Owner.LastFailure=failure;}
                    else if(!job.Cancelled&&!job.Owner.Closed){
                        MakeRoom(bytes,1);
                        Ready ready=new Ready{Owner=job.Owner,Path=job.Path,Value=decoded,Bytes=bytes,Use=++Clock};
                        job.Owner.Results.Add(job.Path,ready);Buffered.Add(ready);BufferedBytes+=bytes;job.Owner.Completed++;
                    }
                }
            }
        }
        internal Snapshot Inspect()
        {
            lock(Gate){return new Snapshot{GlobalReadyBytes=BufferedBytes,GlobalWorkReservation=ReservedBytes,
                GlobalBufferedBytes=BufferedBytes+ReservedBytes,GlobalPending=Work.Count+(Running==null?0:1),OwnerReady=Results.Count,
                OwnerCompleted=Completed,OwnerFailures=Failures,Disposed=Closed,Failure=LastFailure};}
        }
        public void Dispose()
        {
            lock(Gate){
                if(Closed)return;Closed=true;
                foreach(Job job in Pending.Values)job.Cancelled=true;Pending.Clear();
                int count=Work.Count;for(int i=0;i<count;i++){Job job=Work.Dequeue();if(!ReferenceEquals(job.Owner,this))Work.Enqueue(job);}
                for(int i=Buffered.Count-1;i>=0;i--)if(ReferenceEquals(Buffered[i].Owner,this))RemoveReady(Buffered[i]);
                Results.Clear();
                // An in-flight managed read/decode may finish, but cannot publish
                // after Closed. Its global reservation remains until it finishes.
            }
        }
    }
}
