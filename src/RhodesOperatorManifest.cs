using System;
using BBI.Core.ComponentModel;

namespace EndfieldDoK.MapUnitLab
{
    public struct RhodesOperatorSlotStatus
    {
        public readonly int SlotIndex;
        public readonly Entity Passenger;
        public readonly string TypeKey;
        public readonly bool Occupied,Ready,Cooling,Deployed,Transferring,Producing;
        public readonly float CooldownRemainingSeconds,CooldownProgress;
        public RhodesOperatorSlotStatus(int index,Entity passenger,string type,bool occupied,bool ready,bool cooling,bool deployed,bool transferring,bool producing,float remaining,float progress)
        {SlotIndex=index;Passenger=passenger;TypeKey=type;Occupied=occupied;Ready=ready;Cooling=cooling;Deployed=deployed;Transferring=transferring;Producing=producing;CooldownRemainingSeconds=remaining;CooldownProgress=progress;}
        internal static RhodesOperatorSlotStatus Empty(int index)
        {return new RhodesOperatorSlotStatus(index,Entity.None,string.Empty,false,false,false,false,false,false,0f,0f);}
        internal bool Matches(RhodesOperatorSlotStatus other)
        {return SlotIndex==other.SlotIndex&&Passenger==other.Passenger&&TypeKey==other.TypeKey&&Occupied==other.Occupied&&Ready==other.Ready&&Cooling==other.Cooling&&Deployed==other.Deployed&&Transferring==other.Transferring&&Producing==other.Producing&&Math.Abs(CooldownRemainingSeconds-other.CooldownRemainingSeconds)<.05f&&Math.Abs(CooldownProgress-other.CooldownProgress)<.002f;}
    }
    // A snapshot owns its four values. Array consumers get another copy and
    // cannot mutate the simulation's published manifest or its revision.
    public sealed class RhodesOperatorManifest
    {
        readonly RhodesOperatorSlotStatus[] Values;
        public readonly bool LandingReady;
        public readonly int Revision;
        public RhodesOperatorSlotStatus[] Slots {get{return (RhodesOperatorSlotStatus[])Values.Clone();}}
        internal RhodesOperatorManifest(RhodesOperatorSlotStatus[] slots,bool landingReady,int revision)
        {
            if(slots==null||slots.Length!=RhodesOperatorTransport.Capacity)throw new ArgumentException("Operator manifest requires four slots");
            Values=(RhodesOperatorSlotStatus[])slots.Clone();LandingReady=landingReady;Revision=revision;
        }
        internal bool Matches(RhodesOperatorSlotStatus[] slots,bool landed)
        {if(LandingReady!=landed||slots==null||slots.Length!=Values.Length)return false;for(int i=0;i<Values.Length;i++)if(!Values[i].Matches(slots[i]))return false;return true;}
    }
}
