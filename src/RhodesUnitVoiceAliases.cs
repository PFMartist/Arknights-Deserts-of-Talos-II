using System;
using System.Collections.Generic;
using BBI.Game.Events;
using BBI.Unity.Game.Data;

namespace EndfieldDoK.MapUnitLab
{
    // Private UnitCode tokens keep production and destroyed-unit identities in
    // native events that carry no entity. Ordinary unit responses retain the
    // donor code through the existing ExecuteVO hook.
    public static class RhodesUnitVoiceAliases
    {
        public const string ArtilleryVoiceToken="EF_RI_U09_NATIVE_AA";
        public const string TiltrotorVoiceToken="EF_RI_U08_VOICE";
        public const string EngineeringVoiceToken="EF_RI_U03_VOICE";
        public const string ProbeVoiceToken="EF_RI_U02_PROBE_VOICE";
        public const string LaevatainVoiceToken="EF_RI_LAEVAT_VOICE";
        public const string GilbertaVoiceToken="EF_RI_GILBERTA_VOICE";
        public const string ArdeliaVoiceToken="EF_RI_ARDELIA_VOICE";
        static readonly Dictionary<string,string> DonorCodes=new Dictionary<string,string>(StringComparer.Ordinal);

        internal static void ConfigureUnitAudio(UnitAssetContainer privateContainer,string unitType)
        {
            string token=TokenForUnitType(unitType);
            if(token==null||unitType==FactionUnits.ArtilleryKey)return;
            object audioAsset=NativeRead.Get(privateContainer,"m_Audio");
            object audio=audioAsset==null?null:NativeRead.Get(audioAsset,"m_Attributes");
            object hookups=audio==null?null:NativeRead.Get(audio,"AudioHookups");
            string donor=hookups==null?null:NativeRead.Get(hookups,"UnitCode") as string;
            if(hookups==null||string.IsNullOrEmpty(donor)||UnitTypeForToken(donor)!=null)
                throw new InvalidOperationException("Private voice requires an unmodified donor UnitCode: "+unitType);
            DonorCodes[token]=donor;
            NativeRead.Set(hookups,"UnitCode",token);
            // AudioHookups is a value-type datum on supported native builds.
            NativeRead.Set(audio,"AudioHookups",hookups);
        }
        internal static string TokenForUnitType(string unitType)
        {
            if(unitType==FactionUnits.ArtilleryKey)return ArtilleryVoiceToken;
            if(unitType==FactionUnits.TiltStrikeKey)return TiltrotorVoiceToken;
            if(unitType==FactionUnits.EngineeringKey)return EngineeringVoiceToken;
            if(unitType==FactionUnits.SurveyProbeKey)return ProbeVoiceToken;
            if(unitType==HumanoidUnits.LaevatainKey)return LaevatainVoiceToken;
            if(unitType==HumanoidUnits.GilbertaKey)return GilbertaVoiceToken;
            if(unitType==HumanoidUnits.ArdeliaKey)return ArdeliaVoiceToken;
            return null;
        }
        internal static string UnitTypeForToken(string token)
        {
            if(token==ArtilleryVoiceToken)return FactionUnits.ArtilleryKey;
            if(token==TiltrotorVoiceToken)return FactionUnits.TiltStrikeKey;
            if(token==EngineeringVoiceToken)return FactionUnits.EngineeringKey;
            if(token==ProbeVoiceToken)return FactionUnits.SurveyProbeKey;
            if(token==LaevatainVoiceToken)return HumanoidUnits.LaevatainKey;
            if(token==GilbertaVoiceToken)return HumanoidUnits.GilbertaKey;
            if(token==ArdeliaVoiceToken)return HumanoidUnits.ArdeliaKey;
            return null;
        }
        internal static string ProductionEventKey(VoiceOverEvent value)
        {
            if(value==null||!string.Equals(value.Action,"prc",StringComparison.OrdinalIgnoreCase)||
                !string.Equals(value.PrimarySummary,"opsfleet",StringComparison.OrdinalIgnoreCase))return null;
            string unitType=UnitTypeForToken(value.SecondaryDetail);
            if(unitType==FactionUnits.TiltStrikeKey)return "RI_BAD_GUY_PRODUCTION_READY";
            if(unitType==FactionUnits.ArtilleryKey)return "RI_ARTILLERY_PRODUCTION_READY";
            if(unitType==HumanoidUnits.LaevatainKey)return "RI_OPERATOR_LAEVAT_READY";
            if(unitType==HumanoidUnits.GilbertaKey)return "RI_OPERATOR_GILBERTA_READY";
            if(unitType==HumanoidUnits.ArdeliaKey)return "RI_OPERATOR_ARDELIA_READY";
            return null;
        }
        static bool Alias(ref string summary,ref string detail)
        {
            if(detail==ArtilleryVoiceToken){summary="armored";detail="mib";return true;}
            string donor;
            if(detail==null||!DonorCodes.TryGetValue(detail,out donor))return false;
            detail=donor;return true;
        }
        public static VoiceOverEvent Prepare(VoiceOverEvent value)
        {
            if(value==null)return null;
            string a=value.PrimarySummary,b=value.PrimaryDetail,c=value.SecondarySummary,d=value.SecondaryDetail,
                e=value.TertiarySummary,f=value.TertiaryDetail,g=value.QuaternarySummary,h=value.QuaternaryDetail;
            bool changed=Alias(ref a,ref b);changed=Alias(ref c,ref d)||changed;changed=Alias(ref e,ref f)||changed;changed=Alias(ref g,ref h)||changed;
            if(!changed)return value;
            return new VoiceOverEvent(value.Priority,value.Action,value.Group,value.Faction,a,b,c,d,e,f,g,h,value.PositionalEntity,value.Intensity);
        }
    }
}
