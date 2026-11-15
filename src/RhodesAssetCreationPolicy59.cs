using System;

namespace EndfieldDoK.MapUnitLab
{
    // Pure managed ownership of a single synchronous Unity creation callback.
    // The expected type and the first matching instance are both required;
    // unrelated native assets and callbacks on other threads are untouched.
    internal static class RhodesAssetCreationPolicy59
    {
        [ThreadStatic] static Scope Current;

        internal sealed class Scope : IDisposable
        {
            readonly Type ExpectedType;
            readonly Scope Parent;
            object BoundInstance;
            bool Disposed;

            internal Scope(Type expectedType)
            {
                if(expectedType==null)throw new ArgumentNullException("expectedType");
                ExpectedType=expectedType;Parent=Current;Current=this;
            }

            internal bool TryBind(object value)
            {
                if(value==null||value.GetType()!=ExpectedType)return false;
                if(BoundInstance==null)BoundInstance=value;
                return object.ReferenceEquals(BoundInstance,value);
            }

            internal void RequireReturned(object value,bool callbackRequired)
            {
                if(value==null||value.GetType()!=ExpectedType)
                    throw new InvalidOperationException("Native asset creation returned a different type");
                if(BoundInstance!=null&&!object.ReferenceEquals(BoundInstance,value))
                    throw new InvalidOperationException("Native creation callback belonged to a different asset instance");
                if(callbackRequired&&BoundInstance==null)
                    throw new InvalidOperationException("Hotfix59 native asset creation guard was not observed: "+ExpectedType.FullName);
            }

            public void Dispose()
            {
                if(Disposed)return;
                if(!object.ReferenceEquals(Current,this))
                    throw new InvalidOperationException("Native asset creation scopes must retire in stack order");
                Current=Parent;Disposed=true;
            }
        }

        internal static Scope Begin(Type type){return new Scope(type);}
        internal static bool ShouldDefer(object value)
        {Scope scope=Current;return scope!=null&&scope.TryBind(value);}
    }
}
