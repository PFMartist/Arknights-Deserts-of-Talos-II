using System;
using System.Collections;
using BBI.Core.ComponentModel;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // These are PRE-validation hooks, scoped to registered private production abilities.
    // They only prune the request-owned source list. Native costs, queue,
    // hangar reservation, cancellation/refund and production still run normally.
    public static class RhodesOperatorProductionPolicy
    {
        static bool Request(object request,out IList sources,out string type)
        {
            sources=null;type=null;RhodesOperatorDefinition definition;
            if(!Runtime.RhodesCarrier||request==null||
                !RhodesOperatorRoster.TryGetProduction((string)NativeRead.Get(request,"AbilityTypeName"),out definition))return false;
            type=definition.TypeKey;sources=NativeRead.Get(request,"SourceEntities") as IList;return sources!=null;
        }
        static void Deny(object request,IList sources)
        {
            sources.Clear();object result=NativeRead.Get(request,"Result");
            NativeRead.Set(request,"Result",Enum.ToObject(result.GetType(),Convert.ToInt32(result)|64)); // native HangarFull
        }
        public static void FilterFrame(object frame,object request)
        {
            IList sources;string type;if(!Request(request,out sources,out type))return;
            if(!RhodesResearch.IsDeclaredOwner((CommanderID)NativeRead.Get(request,"CommandSender"))){Deny(request,sources);return;}
            string reason;if(RhodesOperatorTransport.TryGetProductionBlockReason((CommanderID)NativeRead.Get(request,"CommandSender"),type,out reason))Deny(request,sources);
            // Native frame validation remains copy-only and non-reserving. Do
            // not reduce sources here: native must select a valid affordable
            // producer before simulation-time single-identity arbitration.
        }
        public static void FilterSim(object request)
        {
            IList sources;string type;if(!Request(request,out sources,out type))return;
            if(!RhodesResearch.IsDeclaredOwner((CommanderID)NativeRead.Get(request,"CommandSender"))){Deny(request,sources);return;}
            object sim=NativeRead.Get(typeof(Sim),"Instance");if(sim==null)return;
            IEntitySystem system=(IEntitySystem)NativeRead.Get(sim,"EntitySystem");string reason;
            bool blocked=RhodesOperatorTransport.IdentityExistsNow(sim,system,(CommanderID)NativeRead.Get(request,"CommandSender"),type,true,out reason);
            RhodesOperatorTransport.CacheIdentityAvailability((CommanderID)NativeRead.Get(request,"CommandSender"),type,blocked,reason);
            if(RhodesOperatorIdentityRules.AllowedSourceCount(sources.Count,blocked,false)==0){Deny(request,sources);return;}
            // A non-bypass production action runs synchronously after native
            // validation; its tracked bay reservation is synchronous *inside*
            // native validation. Retain one exact owned U08, so two selected
            // U08s cannot reserve the identity twice in this same request.
            Entity chosen=Entity.None;
            foreach(object candidate in sources)
            {Entity entity=(Entity)candidate;if(RhodesOperatorTransport.IsOwnedCarrier(system,entity,(CommanderID)NativeRead.Get(request,"CommandSender"))){chosen=entity;break;}}
            sources.Clear();if(chosen!=Entity.None)sources.Add(chosen);
        }
    }
}
