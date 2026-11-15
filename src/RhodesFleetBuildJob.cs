using System;
using System.Collections;

namespace EndfieldDoK.MapUnitLab
{
    // Owned by one customization-panel request. Cancelling disposes the
    // current model build and every already completed private model instance.
    public sealed class RhodesFleetBuildJob:IDisposable
    {
        IEnumerator Routine;
        RhodesFleetPreviewModel Completed;
        bool Done,Disposed;
        public bool IsWaitingForInput{get;private set;}
        internal int ModelsCompleted{get;set;}
        internal void Initialize(IEnumerator routine){Routine=routine;}
        internal void Publish(RhodesFleetPreviewModel value){Completed=value;}
        public bool IsComplete{get{return Done&&!Disposed&&Completed!=null;}}
        public bool Step()
        {
            if(Disposed)throw new ObjectDisposedException("fleet build");
            if(Done)return false;
            try
            {
                if(Routine.MoveNext()){IsWaitingForInput=Object.ReferenceEquals(Routine.Current,EndfieldDoK.ModelIntake.RhodesBuildSignal.WaitingForInput);return true;}
                IsWaitingForInput=false;
                Done=true;DisposeRoutine();
                if(Completed==null)throw new InvalidOperationException("Fleet build completed without a result");return false;
            }
            catch{Dispose();throw;}
        }
        public RhodesFleetPreviewModel TakeResult()
        {
            if(!IsComplete)throw new InvalidOperationException("Fleet build has not completed");
            RhodesFleetPreviewModel result=Completed;Completed=null;return result;
        }
        void DisposeRoutine(){IDisposable disposable=Routine as IDisposable;Routine=null;if(disposable!=null)disposable.Dispose();}
        public void Dispose()
        {if(Disposed)return;Disposed=true;DisposeRoutine();if(Completed!=null)Completed.Dispose();Completed=null;}
    }
}
