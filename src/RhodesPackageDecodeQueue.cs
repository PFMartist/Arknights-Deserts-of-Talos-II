using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace EndfieldDoK.ModelIntake
{
    internal static class RhodesBuildSignal
    {internal static readonly object WaitingForInput=new object();}

    // A finite, bounded pure-data queue. At most one worker reads/validates a
    // package at a time; it exits when drained. No Unity or native-game API is
    // referenced here. Cancellation discards an in-flight read's result.
    internal static class RhodesPackageDecodeQueue
    {
        internal sealed class Request:IDisposable
        {
            internal readonly string File;
            readonly object Gate=new object();
            RhodesModelPackage Result;Exception Failure;bool Ready,Cancelled,Taken;
            internal Request(string file){File=file;}
            internal bool IsComplete{get{lock(Gate)return Ready||Cancelled;}}
            internal bool IsCancelled{get{lock(Gate)return Cancelled;}}
            internal void Complete(RhodesModelPackage result,Exception failure)
            {lock(Gate){if(Cancelled)return;Result=result;Failure=failure;Ready=true;}}
            internal RhodesModelPackage TakeResult()
            {
                lock(Gate)
                {
                    if(Cancelled)throw new ObjectDisposedException("package input request");
                    if(!Ready||Taken)throw new InvalidOperationException("Package input is not available");
                    if(Failure!=null)throw new InvalidDataException("Package input failed: "+File,Failure);
                    Taken=true;RhodesModelPackage result=Result;Result=null;return result;
                }
            }
            public void Dispose(){lock(Gate){Cancelled=true;Result=null;Failure=null;}}
        }
        const int MaximumPending=16;
        static readonly object Gate=new object();
        static readonly Queue<Request> Pending=new Queue<Request>();
        static bool Running;
        internal static Request Submit(string file)
        {
            if(string.IsNullOrEmpty(file))throw new ArgumentException("Package path required");
            Request request=new Request(file);
            lock(Gate)
            {
                // Remove cancelled queued requests before enforcing the bound.
                int count=Pending.Count;
                for(int i=0;i<count;i++){Request prior=Pending.Dequeue();if(!prior.IsCancelled)Pending.Enqueue(prior);}
                if(Pending.Count>=MaximumPending)throw new InvalidOperationException("Package input queue is full");
                Pending.Enqueue(request);
                if(!Running)
                {
                    Running=true;
                    try{if(!ThreadPool.QueueUserWorkItem(Drain))throw new InvalidOperationException("Package worker could not be scheduled");}
                    catch(Exception error)
                    {Running=false;while(Pending.Count>0)Pending.Dequeue().Complete(null,error);throw;}
                }
            }
            return request;
        }
        static void Drain(object ignored)
        {
            while(true)
            {
                Request request;
                lock(Gate){if(Pending.Count==0){Running=false;return;}request=Pending.Dequeue();}
                if(request.IsCancelled)continue;
                try{request.Complete(RhodesModelPackage.Read(request.File),null);}
                catch(Exception error){request.Complete(null,error);}
            }
        }
    }
}
