using System;
using System.Text;

namespace EndfieldDoK.MapUnitLab.ModCore
{
    public sealed class ServiceReleaseBatch
    {
        public delegate void Release();
        public delegate void Failure(string scope,string service,Exception error);
        public struct Entry
        {
            public readonly string Name;public readonly Release Action;
            public Entry(string name,Release action){Name=name;Action=action;}
        }
        readonly string Scope;readonly string[] Names;readonly Release[] Actions;
        public ServiceReleaseBatch(string scope,Entry[] entries)
        {
            if(scope==null||entries==null)throw new ArgumentException("A named lifetime batch is required");
            Scope=scope;Names=new string[entries.Length];Actions=new Release[entries.Length];
            for(int i=0;i<entries.Length;i++)
            {
                if(String.IsNullOrEmpty(entries[i].Name)||entries[i].Action==null)throw new ArgumentException("A release entry is incomplete");
                Names[i]=entries[i].Name;Actions[i]=entries[i].Action;
            }
        }
        public ServiceReleaseBatch(string scope,string[] names,Release[] actions)
        {
            if(scope==null||names==null||actions==null||names.Length!=actions.Length)throw new ArgumentException("A named lifetime batch is required");
            Scope=scope;Names=(string[])names.Clone();Actions=(Release[])actions.Clone();
            for(int i=0;i<Names.Length;i++)if(String.IsNullOrEmpty(Names[i])||Actions[i]==null)throw new ArgumentException("A release entry is incomplete");
        }
        public void Run(Failure report)
        {
            Exception first=null;StringBuilder failed=null;
            for(int i=0;i<Actions.Length;i++)
            {
                try{Actions[i]();}
                catch(Exception error)
                {
                    if(first==null){first=error;failed=new StringBuilder();}
                    if(failed.Length!=0)failed.Append(", ");failed.Append(Names[i]);
                    // Reporting failure must not prevent the remaining owners
                    // from releasing their resources behind the shutdown gate.
                    if(report!=null)try{report(Scope,Names[i],error);}catch{}
                }
            }
            if(first!=null)throw new InvalidOperationException("Lifetime release incomplete ("+Scope+"): "+failed,first);
        }
    }
}
