using System;

namespace EndfieldDoK.MapUnitLab
{
    // Small fixed catalog: lookup only; no scene discovery or per-frame I/O.
    internal sealed class OperatorUiIdentity
    {
        internal readonly string TypeKey,DisplayName,EnglishName,Prefix,PortraitFile,SkillFile,AbilityKey;
        internal string ProductionName {get{return Prefix+"_Portrait_Production";}}
        internal string InfoName {get{return Prefix+"_Portrait_Info";}}
        internal string FleetName {get{return Prefix+"_Portrait_Fleet";}}
        internal string CargoName {get{return Prefix+"_Portrait_Cargo";}}
        internal string SkillName {get{return Prefix=="EF_LAEVAT"?"EF_LAEVAT_ComboPattern":Prefix=="EF_ARDELIA"?"EF_ARDELIA_HealingPattern":Prefix+"_GravityPattern";}}
        OperatorUiIdentity(string key,string display,string english,string prefix,string portrait,string skill,string ability)
        {TypeKey=key;DisplayName=display;EnglishName=english;Prefix=prefix;PortraitFile=portrait;SkillFile=skill;AbilityKey=ability;}
        internal const int Capacity=4;
        internal static readonly OperatorUiIdentity[] All={
            new OperatorUiIdentity("EF_Laevatain_Test","莱万汀","Laevatain","EF_LAEVAT","laevatain_portrait.png","laevatain_combo_pattern.png","EF_Laevatain_ChainFlame"),
            new OperatorUiIdentity("EF_Gilberta_Test","洁尔佩塔","Gilberta","EF_GILBERTA","gilberta_portrait.png","gilberta_gravity_pattern.png","EF_Gilberta_GravityWell"),
            new OperatorUiIdentity("EF_Ardelia_Test","艾尔黛拉","Ardelia","EF_ARDELIA","ardelia_portrait.png","ardelia_healing_pattern.png",ArdeliaHealingAssets.AbilityKey)
        };
        internal static bool TryGet(string key,out OperatorUiIdentity identity)
        {foreach(OperatorUiIdentity item in All)if(string.Equals(item.TypeKey,key,StringComparison.Ordinal)){identity=item;return true;}identity=null;return false;}
        internal static OperatorUiIdentity Require(string key)
        {OperatorUiIdentity identity;if(All.Length>Capacity||!TryGet(key,out identity))throw new ArgumentException("Unregistered operator UI identity: "+key);return identity;}
    }
}
