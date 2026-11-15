using System;
using System.Collections;

namespace EndfieldDoK.ModelIntake
{
    // Explicitly pumped on the presentation thread. Each MoveNext performs a
    // bounded structural step (node batch, material, mesh or sidecar). A single
    // Unity upload/PNG decode remains atomic; there is no background Unity work.
    public sealed class RhodesModelBuildJob:IDisposable
    {
        IEnumerator Routine;
        RhodesLoadedModel Completed;
        bool Done,Disposed;
        public bool IsWaitingForInput{get;private set;}
        internal void Initialize(IEnumerator routine){Routine=routine;}
        internal void Publish(RhodesLoadedModel value){Completed=value;}
        public bool IsComplete{get{return Done&&!Disposed&&Completed!=null;}}
        public bool Step()
        {
            if(Disposed)throw new ObjectDisposedException("model build");
            if(Done)return false;
            try
            {
                if(Routine.MoveNext()){IsWaitingForInput=Object.ReferenceEquals(Routine.Current,RhodesBuildSignal.WaitingForInput);return true;}
                IsWaitingForInput=false;
                Done=true;DisposeRoutine();
                if(Completed==null)throw new InvalidOperationException("Model build completed without a result");return false;
            }
            catch{Dispose();throw;}
        }
        public RhodesLoadedModel TakeResult()
        {
            if(!IsComplete)throw new InvalidOperationException("Model build has not completed");
            RhodesLoadedModel result=Completed;Completed=null;return result;
        }
        void DisposeRoutine(){IDisposable disposable=Routine as IDisposable;Routine=null;if(disposable!=null)disposable.Dispose();}
        public void Dispose()
        {
            if(Disposed)return;Disposed=true;DisposeRoutine();
            if(Completed!=null)Completed.Dispose();Completed=null;
        }
    }
}
