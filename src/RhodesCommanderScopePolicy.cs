using System;
using System.Collections.Generic;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Publish one immutable owner order when membership changes. Simulation
    // services can share it without an allocation or hash-table order per tick.
    internal static class RhodesCommanderScopePolicy
    {
        sealed class OwnerOrder:IComparer<CommanderID>
        {
            public int Compare(CommanderID left,CommanderID right)
            {return left.ID.CompareTo(right.ID);}
        }
        static readonly IComparer<CommanderID> Order=new OwnerOrder();

        internal static CommanderID[] OrderedOwners(IEnumerable<CommanderID> owners)
        {
            if(owners==null)throw new ArgumentNullException("owners");
            List<CommanderID> result=new List<CommanderID>(owners);
            result.Sort(Order);return result.ToArray();
        }
    }
}
