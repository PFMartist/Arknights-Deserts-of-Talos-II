using System;
using System.Collections.Generic;

namespace EndfieldDoK.MapUnitLab.ModCore
{
    // Optional presentation services only. Registration/ABI and authoritative
    // simulation errors deliberately do not pass through this fault boundary.
    public sealed class ServiceFaultDomain
    {
        public delegate void Work(object context);
        public delegate void Failure(string service,Exception error);
        readonly Dictionary<string,string> Disabled=new Dictionary<string,string>(StringComparer.Ordinal);
        readonly Failure Report;
        public ServiceFaultDomain(Failure report){Report=report;}
        public bool IsDisabled(string service){return Disabled.ContainsKey(service);}
        public bool Run(string service,Work work,object context)
        {
            if(service==null||work==null)throw new ArgumentNullException("service");
            if(Disabled.ContainsKey(service))return false;
            try{work(context);return true;}
            catch(Exception error)
            {
                Disabled.Add(service,error.GetType().FullName+": "+error.Message);
                // A diagnostic sink cannot escape an optional fault boundary.
                if(Report!=null)try{Report(service,error);}catch{}
                return false;
            }
        }
        public void EndMatch(){Disabled.Clear();}
    }
}
