using System;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesResearch
    {
        // NotificationController carries this code in the completion event's
        // SecondarySummary. The native event still supplies priority and owner.
        internal static string ResearchVoiceCode(string key)
        {
            if(key==TiltAssembly)return "RI_BAD_GUY_HANGAR_RESEARCH";
            if(key==PredictiveFireControl)return "RI_ARTILLERY_PREDICTIVE_FIRE_CONTROL";
            if(key==Aviation1)return "RI_CARRIER_AVIATION_REFIT";
            if(key==Gunnery1)return "RI_CARRIER_GUNNERY_FORE";
            if(key==Gunnery2)return "RI_CARRIER_GUNNERY_AFT";
            if(key==Gunnery3)return "RI_CARRIER_MAIN_FULL_CHARGE";
            if(IsReactorResearch(key))return "RI_REACTOR_CAPACITY_UPGRADE";
            if(key==EscortBoost)return "RI_ESCORT_BOOST_RESEARCH";
            if(key==AirDefenceReload)return "RI_AIR_DEFENCE_RELOAD_RESEARCH";
            if(key==EngineeringRepair)return "RI_ENGINEERING_REPAIR_RESEARCH";
            if(key==SurveyProbeDurability)return "RI_SURVEY_PROBE_RESEARCH";
            if(key==InterceptorService)return "RI_INTERCEPTOR_SERVICE_RESEARCH";
            if(key==BomberProtection)return "RI_BOMBER_PROTECTION_RESEARCH";
            if(key==AdvancedOriginiumSuppressant)return "RI_ADVANCED_SUPPRESSANT_RESEARCH";
            return string.Empty;
        }
    }
}
