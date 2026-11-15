using System;

namespace EndfieldDoK.MapUnitLab
{
    public sealed class RhodesOperatorDefinition
    {
        public readonly string TypeKey,ProduceAbilityKey,DisplayName,EnglishName,AssetFolder;
        public readonly int ProductionHotkey;
        internal RhodesOperatorDefinition(string key,string produce,string name,string english,string folder,int hotkey)
        {TypeKey=key;ProduceAbilityKey=produce;DisplayName=name;EnglishName=english;AssetFolder=folder;ProductionHotkey=hotkey;}
    }
    // One immutable identity catalog shared by native production and transport.
    // Four is the aircraft cargo budget, not a count of duplicate hero copies.
    public static class RhodesOperatorRoster
    {
        static readonly RhodesOperatorDefinition[] Entries={
            new RhodesOperatorDefinition(HumanoidUnits.LaevatainKey,"Ability_EF_U08_ProduceLaevatain","莱万汀","Laevatain","humanoid_trial/laevatain",106),
            new RhodesOperatorDefinition(HumanoidUnits.GilbertaKey,"Ability_EF_U08_ProduceGilberta","洁尔佩塔","Gilberta","humanoid_trial/gilberta",107),
            new RhodesOperatorDefinition(HumanoidUnits.ArdeliaKey,"Ability_EF_U08_ProduceArdelia","艾尔黛拉","Ardelia","ardelia_v01",111)
        };
        public static int Count {get{return Entries.Length;}}
        public static RhodesOperatorDefinition At(int index){return Entries[index];}
        public static bool TryGet(string typeKey,out RhodesOperatorDefinition definition)
        {for(int i=0;i<Entries.Length;i++)if(Entries[i].TypeKey==typeKey){definition=Entries[i];return true;}definition=null;return false;}
        public static bool TryGetProduction(string abilityKey,out RhodesOperatorDefinition definition)
        {for(int i=0;i<Entries.Length;i++)if(Entries[i].ProduceAbilityKey==abilityKey){definition=Entries[i];return true;}definition=null;return false;}
        public static bool Contains(string typeKey){RhodesOperatorDefinition ignored;return TryGet(typeKey,out ignored);}
    }
}
