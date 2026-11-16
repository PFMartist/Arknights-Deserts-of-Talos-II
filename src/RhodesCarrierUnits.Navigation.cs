using System;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesCarrierUnits
    {
        // Pure managed configuration shared by actual registration and the
        // original-data fixture. Navigation clearance is not the model radius:
        // native carriers use their own capsule plus the map's carrier profile.
        // Keep art, selection, production exits and visual formation spacing
        // independent from these authoritative movement-space attributes.
        internal static void ConfigureNativeCarrierNavigation(object customNavConfig,object dynamics,object maneuvers,object avoidance,
            object donorNavConfig,object donorDynamics,object donorManeuvers,object donorAvoidance)
        {
            CopyNavigationFields(customNavConfig,donorNavConfig,new string[]{"m_DistanceFromObstacles","m_DistanceErrorPercentageTolerance","m_BlockedBy"});
            CopyNavigationFields(dynamics,donorDynamics,new string[]{"m_Width","m_Length","m_MaxSpeedTurnRadius"});
            CopyNavigationFields(maneuvers,donorManeuvers,new string[]{"m_ObstacleAvoidanceDistance","m_ArrivalDistance"});
            CopyNavigationFields(avoidance,donorAvoidance,new string[]{"m_AvoidanceDistance"});
            VerifyNativeCarrierNavigation(customNavConfig,dynamics,maneuvers,avoidance,donorNavConfig,donorDynamics,donorManeuvers,donorAvoidance);
        }

        static void CopyNavigationFields(object custom,object donor,string[] fields)
        {
            if(custom==null||donor==null||object.ReferenceEquals(custom,donor))
                throw new InvalidOperationException("Carrier navigation requires independent prepared native data");
            foreach(string field in fields)Write(custom,field,Read(donor,field));
        }

        internal static void VerifyNativeCarrierNavigation(object customNavConfig,object dynamics,object maneuvers,object avoidance,
            object donorNavConfig,object donorDynamics,object donorManeuvers,object donorAvoidance)
        {
            VerifyNavigationFields(customNavConfig,donorNavConfig,new string[]{"m_DistanceFromObstacles","m_DistanceErrorPercentageTolerance","m_BlockedBy"});
            VerifyNavigationFields(dynamics,donorDynamics,new string[]{"m_Width","m_Length","m_MaxSpeedTurnRadius"});
            VerifyNavigationFields(maneuvers,donorManeuvers,new string[]{"m_ObstacleAvoidanceDistance","m_ArrivalDistance"});
            VerifyNavigationFields(avoidance,donorAvoidance,new string[]{"m_AvoidanceDistance"});
        }

        static void VerifyNavigationFields(object custom,object donor,string[] fields)
        {
            if(custom==null||donor==null||object.ReferenceEquals(custom,donor))
                throw new InvalidOperationException("Carrier navigation data aliases or lacks its donor");
            foreach(string field in fields)if(!object.Equals(Read(custom,field),Read(donor,field)))
                throw new InvalidOperationException("Prepared carrier navigation differs from "+SourceKey+" field "+field);
        }
    }
}
