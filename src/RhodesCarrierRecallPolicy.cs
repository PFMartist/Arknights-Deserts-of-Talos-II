using System;
using BBI.Core.ComponentModel;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    public static class RhodesCarrierRecallPolicy
    {
        static Type Extensions;
        static bool ReportedFailure;

        public static bool ShouldRecall(object request,Entity carrier,Entity aircraft)
        {
            if(!Runtime.RhodesCarrier||request==null)return true;
            try
            {
                if((string)NativeRead.Get(request,"AbilityTypeName")!=RhodesCarrierUnits.AircraftRecallKey)return true;
                if(Extensions==null)Extensions=typeof(Sim).Assembly.GetType("BBI.Game.Simulation.EntityComponentExtensions",true);
                string aircraftType=(string)NativeRead.Call(Extensions,"GetTypeName",aircraft);
                if(aircraftType!=FactionUnits.TiltStrikeKey)return true;
                string carrierType=(string)NativeRead.Call(Extensions,"GetTypeName",carrier);
                if(carrierType!=RhodesCarrierUnits.UnitKey)return true;
                object carrierOwner=NativeRead.Call(typeof(Sim),"GetEntityCommanderID",carrier);
                object aircraftOwner=NativeRead.Call(typeof(Sim),"GetEntityCommanderID",aircraft);
                return !object.Equals(carrierOwner,aircraftOwner);
            }
            catch(Exception error)
            {
                // The native command remains available if metadata drifts.
                if(!ReportedFailure){ReportedFailure=true;Runtime.Log("WARNING RHODES_CARRIER_RECALL_FILTER_FAILED "+error.GetType().Name+": "+error.Message);}
                return true;
            }
        }
    }
}
