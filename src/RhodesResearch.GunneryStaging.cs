using System;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesResearch
    {
        // Hardware stages track deployed stations. Charge research is independent.
        // Power allocation gates firing independently of deployed geometry.
        internal static int GunneryStage(bool fore,bool aft,bool fullCharge)
        {
            if((aft||fullCharge)&&!fore)return -1;
            return aft?2:fore?1:0;
        }
        static string[] GunneryChargeDependencies()
        {return Runtime.SkirmishMode?new string[]{Gunnery1,ReactorCapacity2}:new string[]{Gunnery1};}
        static void RegisterGunneryStaging()
        {
            Add(Gunnery2,"炮战改装 II：后主炮",ResearchType.Engineering,new string[]{Gunnery1});
            Add(Gunnery3,"主炮全装药",ResearchType.Upgrade,GunneryChargeDependencies());
            // Attach before Register restores declared owners: the public
            // helper correctly forbids modifying research after binding.
            // Native commander type buffs form the INNER layer (300 -> 600);
            // temporary instance power buffs then multiply that researched value.
            AddWeaponBuff(Gunnery3,RhodesCarrierUnits.MainForeWeaponID,100);
            AddWeaponBuff(Gunnery3,RhodesCarrierUnits.MainAftWeaponID,100);
            AddGunneryRangeBuff(Gunnery1,850,false);
            AddGunneryRangeBuff(Gunnery2,200,true);
            AddGunneryRangeBuff(Gunnery3,200,true);
        }
        static object BuildGunneryRangeBuffData(int combatBonus,bool bands)
        {
            object set=NewData("AttributeBuffSetData");Array entries=Array.CreateInstance(NewData("AttributeBuffData").GetType(),bands?7:1);int index=0;
            if(bands)foreach(string weapon in RhodesCarrierUnits.MainWeaponIDs)
                for(int band=0;band<3;band++)entries.SetValue(GunneryEntry(weapon,33554432+band,0,200),index++);
            entries.SetValue(GunneryEntry(string.Empty,1073741824,0,combatBonus),index);Set(set,"m_Buffs",entries);return set;
        }
        static object GunneryEntry(string scope,int attribute,int mode,int amount)
        {
            object entry=NewData("AttributeBuffData");Set(entry,"m_Name",scope);SetEnum(entry,"m_AttributeID",attribute);
            SetEnum(entry,"m_Mode",mode);Set(entry,"m_Value",amount);return entry;
        }
        static void AddGunneryRangeBuff(string key,int combatBonus,bool bands)
        {
            UnityEngine.ScriptableObject buff=Create(NativeType("BuffSetAttributesAsset"),key+"_RangesAndCombat_Buff");
            Set(buff,"m_Attributes",BuildGunneryRangeBuffData(combatBonus,bands));Enable(buff);
            object target=NewData("UnitTypeBuffData");ConfigureFleetUpgradeTarget(target,RhodesCarrierUnits.UnitKey);Set(target,"m_BuffSet",buff);
            object data=NativeRead.Get(Items[key],"m_Attributes");Array prior=(Array)NativeRead.Get(data,"m_UnitTypeBuffs");
            Array all=Array.CreateInstance(target.GetType(),prior.Length+1);prior.CopyTo(all,0);all.SetValue(target,prior.Length);Set(data,"m_UnitTypeBuffs",all);Enable(Items[key]);
        }
        static void VerifyGunneryEntry(object entry,string scope,int attribute,int mode,int amount)
        {
            if((string)NativeRead.Get(entry,"m_Name")!=scope||Convert.ToInt32(NativeRead.Get(entry,"m_AttributeID"))!=attribute||
                Convert.ToInt32(NativeRead.Get(entry,"m_Mode"))!=mode||Convert.ToInt32(NativeRead.Get(entry,"m_Value"))!=amount)
                throw new InvalidOperationException("Gunnery native buff scope, mode or amount differs");
        }
        static Array BuildCarrierRouteTiers(object choice)
        {
            object aft=NewGameData("TechTreeTierData");Set(aft,"m_TierName","后主炮接入");
            Set(aft,"m_ResearchItems",new string[]{Gunnery2});
            Array tiers=Array.CreateInstance(choice.GetType(),2);tiers.SetValue(choice,0);tiers.SetValue(aft,1);return tiers;
        }
        static Array BuildGunneryUpgradeEntries()
        {
            object upgrade=NewGameData("TechUpgradeData");Set(upgrade,"m_UpgradeName",Gunnery3);Set(upgrade,"m_ResearchItem",Gunnery3);
            if(!Runtime.SkirmishMode)return ArrayOf(upgrade);
            string[] reactors=ReactorResearchKeys();Array entries=Array.CreateInstance(upgrade.GetType(),reactors.Length+1);entries.SetValue(upgrade,0);
            for(int i=0;i<reactors.Length;i++){object item=NewGameData("TechUpgradeData");Set(item,"m_UpgradeName",reactors[i]);Set(item,"m_ResearchItem",reactors[i]);entries.SetValue(item,i+1);}
            return entries;
        }
        static void VerifyGunneryStaging(TechTreeAttributes tree)
        {
            foreach(string key in new string[]{Gunnery1,Gunnery2,Gunnery3})
            {
                ResearchItemAttributes item=Registry.GetEntityType(key).Get<ResearchItemAttributes>();
                string prerequisite=key==Gunnery1?SuppressionAssembly:Gunnery1;
                string[] deps=NativeRead.Get(item,"m_ResearchDependencies") as string[];
                if(item.TypeOfResearch!=(key==Gunnery3?ResearchType.Upgrade:ResearchType.Engineering)||
                    deps==null||deps.Length!=(key==Gunnery3&&Runtime.SkirmishMode?2:1)||deps[0]!=prerequisite||
                    (key==Gunnery3&&Runtime.SkirmishMode&&deps[1]!=ReactorCapacity2)||
                    EngineeringAppearances(tree,key)!=(key==Gunnery3?0:1)||UpgradeAppearances(tree,key)!=(key==Gunnery3?1:0))
                    throw new InvalidOperationException("Staged gunnery native research contract changed: "+key);
                VerifyResearchBalance(item,key);
                Array targets=(Array)NativeRead.Get(item,"m_UnitTypeBuffs");
                int damageTargets=key==Gunnery3?RhodesCarrierUnits.MainWeaponIDs.Length:0;
                if(targets.Length!=damageTargets+1)throw new InvalidOperationException("Gunnery research buff target count differs");
                for(int targetIndex=0;targetIndex<targets.Length;targetIndex++)
                {
                    object target=targets.GetValue(targetIndex);
                    if((string)NativeRead.Get(target,"m_UnitTypePrefix")!=RhodesCarrierUnits.UnitKey||(bool)NativeRead.Get(target,"m_UseAsPrefix")||
                        Convert.ToInt32(NativeRead.Get(target,"m_UnitClass"))!=0||Convert.ToInt32(NativeRead.Get(target,"m_ClassOperator"))!=1)
                        throw new InvalidOperationException("Gunnery effects must target only the exact private carrier");
                    Array entries=(Array)NativeRead.Get(NativeRead.Get(NativeRead.Get(target,"m_BuffSet"),"m_Attributes"),"m_Buffs");
                    if(targetIndex<damageTargets)
                    {
                        if(entries.Length!=1)throw new InvalidOperationException("Main charge damage target must contain one weapon attribute");
                        VerifyGunneryEntry(entries.GetValue(0),RhodesCarrierUnits.MainWeaponIDs[targetIndex],262148,1,100);
                    }
                    else
                    {
                        bool bands=key!=Gunnery1;
                        if(entries.Length!=(bands?7:1))throw new InvalidOperationException("Gunnery band/combat-range buff count differs");
                        int entry=0;
                        if(bands)foreach(string weapon in RhodesCarrierUnits.MainWeaponIDs)
                            for(int band=0;band<3;band++)VerifyGunneryEntry(entries.GetValue(entry++),weapon,33554432+band,0,200);
                        VerifyGunneryEntry(entries.GetValue(entry),string.Empty,1073741824,0,bands?200:850);
                    }
                }
            }
        }
    }
}
