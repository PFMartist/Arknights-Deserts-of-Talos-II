using System;
using System.Collections.Generic;
using BBI.Core.Network;
using BBI.Game.Data;
using BBI.Game.Simulation;
using BBI.Unity.Game.Utility;
using BBI.Unity.Game.World;

namespace EndfieldDoK.MapUnitLab
{
    // No team, map, spawn or native player-count policy is reimplemented here.
    // Native setup validates those rules; this only validates private identity.
    internal static class RhodesMatchSelectionPolicy
    {
        internal static CommanderID[] Validate(IDictionary<CommanderID,PlayerSelection> selections,int factionIndex)
        {
            if(selections==null)throw new ArgumentNullException("selections");
            SortedDictionary<int,CommanderID> owners=new SortedDictionary<int,CommanderID>();
            foreach(KeyValuePair<CommanderID,PlayerSelection> item in selections)
            {
                CommanderDescription description=item.Value.Desc;
                if(description.FactionIndex!=factionIndex||factionIndex<0)continue;
                if(item.Key==CommanderID.None||!item.Key.Equals(description.CommanderID))
                    throw new InvalidOperationException("Private faction commander identity differs");
                if(description.PlayerType!=PlayerType.Human||description.RandomFaction)
                    throw new InvalidOperationException("Rhodes must be explicitly selected by a human player");
                if(description.SelectedUnitSkinPack!=DLCPackID.kInvalidID)
                    throw new InvalidOperationException("Rhodes does not use native faction skin packs");
                owners.Add(item.Key.ID,item.Key);
            }
            CommanderID[] result=new CommanderID[owners.Count];owners.Values.CopyTo(result,0);return result;
        }
        internal static bool MatchesPrepared(IDictionary<CommanderID,PlayerSelection> current,CommanderID[] owners,
            IDictionary<CommanderID,CommanderAttributes> prepared,IDictionary<CommanderID,CommanderDescription> descriptions)
        {
            if(prepared.Count!=owners.Length||descriptions.Count!=current.Count)return false;
            foreach(CommanderID owner in owners)if(!prepared.ContainsKey(owner))return false;
            foreach(KeyValuePair<CommanderID,PlayerSelection> item in current)
            {
                CommanderDescription prior;
                if(!descriptions.TryGetValue(item.Key,out prior)||!prior.Equals(item.Value.Desc))return false;
            }
            return true;
        }
    }
}
