using System;
using System.Collections.Generic;
using BBI.Core;
using BBI.Core.Data;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesResearch
    {
        public const string EscortDamage1="Research_RI_Escort_Damage1",EscortDamage2="Research_RI_Escort_Damage2";
        public const string EscortArmour1="Research_RI_Escort_Armour1",EscortArmour2="Research_RI_Escort_Armour2";
        public const string TrackedArmour1="Research_RI_Tracked_Armour1",TrackedArmour2="Research_RI_Tracked_Armour2",TrackedArmour3="Research_RI_Tracked_Armour3";
        public const string RailgunDamage="Research_RI_Railgun_Damage";
        sealed class FleetUpgrade
        {
            public string Key,Donor,Title,Dependency,Description;
            public int CU,RU;public float NativeSeconds;public string[] Targets;
            public FleetUpgrade(string key,string donor,string title,string dep,string detail,int cu,int ru,float seconds,params string[] targets)
            {Key=key;Donor=donor;Title=title;Dependency=dep;Description=detail;CU=cu;RU=ru;NativeSeconds=seconds;Targets=targets;}
        }
        static FleetUpgrade[] FleetUpgrades()
        {
            return new FleetUpgrade[]{
                new FleetUpgrade(EscortDamage1,"Research_Buff_C_Escort_Damage01","轻型突击车火力 I",LightAssembly,"轻型突击车主武器每发伤害增加2点。",500,75,30,FactionUnits.EscortKey),
                new FleetUpgrade(EscortDamage2,"Research_Buff_C_Escort_Damage02","轻型突击车火力 II",EscortDamage1,"轻型突击车主武器每发伤害再增加2点。",500,150,60,FactionUnits.EscortKey),
                new FleetUpgrade(EscortArmour1,"Research_Buff_C_Escort_VehicleArmor01","轻型突击车装甲 I",LightAssembly,"轻型突击车装甲增加2点。",200,75,15,FactionUnits.EscortKey),
                new FleetUpgrade(EscortArmour2,"Research_Buff_C_Escort_VehicleArmor02","轻型突击车装甲 II",EscortArmour1,"轻型突击车装甲再增加2点。",200,150,20,FactionUnits.EscortKey),
                new FleetUpgrade(TrackedArmour1,"Research_Buff_C_TrackedUnits_Armor01","履带车辆装甲 I",SuppressionAssembly,"装甲突击车、轨道炮车和防空导弹车装甲增加3点。",250,100,15,FactionUnits.SuppressionKey,FactionUnits.RailgunKey,FactionUnits.AirDefenceKey),
                new FleetUpgrade(TrackedArmour2,"Research_Buff_C_TrackedUnits_Armor02","履带车辆装甲 II",TrackedArmour1,"装甲突击车、轨道炮车和防空导弹车装甲再增加2点，生命上限增加100点。",250,200,20,FactionUnits.SuppressionKey,FactionUnits.RailgunKey,FactionUnits.AirDefenceKey),
                new FleetUpgrade(TrackedArmour3,"Research_Buff_C_TrackedUnits_Armor03","履带车辆装甲 III",TrackedArmour2,"装甲突击车、轨道炮车和防空导弹车装甲再增加1点，生命上限再增加200点。",300,300,30,FactionUnits.SuppressionKey,FactionUnits.RailgunKey,FactionUnits.AirDefenceKey),
                new FleetUpgrade(RailgunDamage,"Research_Buff_C_Railgun","轨道炮火力升级",RailgunAssembly,"轨道炮车主武器每发伤害增加60点。",280,165,50,FactionUnits.RailgunKey)};
        }
        static string[] WithFactionUpgradeKeys(string[] original)
        {
            if(!Runtime.SkirmishMode)return original;
            List<string> keys=new List<string>(original);foreach(FleetUpgrade spec in FleetUpgrades())keys.Add(spec.Key);keys.Add(PredictiveFireControl);return keys.ToArray();
        }
        static FleetUpgrade FleetUpgradeFor(string key)
        {foreach(FleetUpgrade spec in FleetUpgrades())if(spec.Key==key)return spec;throw new InvalidOperationException("Unknown fleet upgrade "+key);}

        // Native unit and dynamics attributes ignore Name. Weapon and range
        // attributes require the exact WeaponID; DEFAULT is not a wildcard.
        static void ConfigureFleetUpgradeBuff(object data,string key)
        {
            Array source=(Array)NativeRead.Get(data,"m_Buffs");List<object> kept=new List<object>();HashSet<string> signatures=new HashSet<string>(StringComparer.Ordinal);
            foreach(object entry in source)
            {
                int id=Convert.ToInt32(NativeRead.Get(entry,"m_AttributeID"));int category=id&unchecked((int)0xffff0000);
                string scope=(string)NativeRead.Get(entry,"m_Name");
                if((category==262144||category==33554432)&&scope!="DEFAULT")continue;
                string signature=scope+":"+id+":"+NativeRead.Get(entry,"m_Mode")+":"+NativeRead.Get(entry,"m_Value");
                if(signatures.Add(signature))kept.Add(entry);
            }
            Array output=Array.CreateInstance(source.GetType().GetElementType(),kept.Count);for(int i=0;i<kept.Count;i++)output.SetValue(kept[i],i);Set(data,"m_Buffs",output);
            VerifyFleetUpgradeBuff(data,key);
        }
        static void VerifyFleetUpgradeBuff(object data,string key)
        {
            int armour=key==TrackedArmour1?3:key==TrackedArmour2?2:key==TrackedArmour3?1:2;
            int health=key==TrackedArmour2?100:key==TrackedArmour3?200:0;
            bool damage=key==EscortDamage1||key==EscortDamage2||key==RailgunDamage;
            Array entries=(Array)NativeRead.Get(data,"m_Buffs");
            if(entries.Length!=(health==0?1:2))throw new InvalidOperationException("Fleet upgrade effect count changed "+key);
            HashSet<int> ids=new HashSet<int>();
            foreach(object entry in entries)
            {
                int id=Convert.ToInt32(NativeRead.Get(entry,"m_AttributeID")),amount=Convert.ToInt32(NativeRead.Get(entry,"m_Value"));
                if(!ids.Add(id)||Convert.ToInt32(NativeRead.Get(entry,"m_Mode"))!=0||
                    (damage?(id!=262148||amount!=(key==RailgunDamage?60:2)||(string)NativeRead.Get(entry,"m_Name")!="DEFAULT"):
                        (id==65540?amount!=armour:id==65536?amount!=health||health==0:true)))
                    throw new InvalidOperationException("Fleet upgrade native effect mismatch "+key);
            }
        }
        static void RegisterFactionUpgrades()
        {
            if(!Runtime.SkirmishMode)return;
            VerifyCoalitionFleetUpgradeAuthority();
            foreach(FleetUpgrade spec in FleetUpgrades())
            {
                EntityTypeAttributes donor=Registry.GetEntityType(spec.Donor);ResearchItemAttributes original=donor==null?null:donor.Get<ResearchItemAttributes>();
                if(original==null||original.TypeOfResearch!=ResearchType.Upgrade||Convert.ToInt32(NativeRead.Get(original,"m_Resource1Cost"))!=spec.CU||
                    Convert.ToInt32(NativeRead.Get(original,"m_Resource2Cost"))!=spec.RU||original.ResearchTime!=Fixed64.FromConstFloat(spec.NativeSeconds)||
                    original.CommandsToRun==null||original.CommandsToRun.Length!=0)
                    throw new InvalidOperationException("Coalition MP fleet research donor changed "+spec.Donor);
                Array nativeTargets=(Array)NativeRead.Get(original,"m_UnitTypeBuffs");
                if(nativeTargets.Length==0)throw new InvalidOperationException("Fleet research has no native buff "+spec.Donor);
                ScriptableObject nativeBuff=(ScriptableObject)NativeRead.Get(nativeTargets.GetValue(0),"m_BuffSet");
                object buffData=Copy(NativeRead.Get(nativeBuff,"m_Attributes"),new Dictionary<object,object>(new Identity()));ConfigureFleetUpgradeBuff(buffData,spec.Key);
                ScriptableObject buff=Create(NativeType("BuffSetAttributesAsset"),spec.Key+"_Buff");Set(buff,"m_Attributes",buffData);Enable(buff);
                Array targets=Array.CreateInstance(nativeTargets.GetType().GetElementType(),spec.Targets.Length);
                for(int i=0;i<spec.Targets.Length;i++)
                {
                    object target=Copy(nativeTargets.GetValue(0),new Dictionary<object,object>(new Identity()));
                    ConfigureFleetUpgradeTarget(target,spec.Targets[i]);Set(target,"m_BuffSet",buff);targets.SetValue(target,i);
                }
                object data=Copy(original,new Dictionary<object,object>(new Identity()));((NamedObjectBase)data).InitializeName(spec.Key);
                ApplyResearchIcon(data,spec.Key);
                ApplyResearchBalance(data,spec.Key);Set(data,"m_ResearchDependencies",new string[]{spec.Dependency});Set(data,"m_UnitTypeBuffs",targets);
                Set(data,"m_LocalizedResearchTitleStringID",spec.Title);Set(data,"m_LocalizedShortDescriptionStringID",spec.Description);
                Set(data,"m_LocalizedLongDescriptionStringID",spec.Description+" 仅强化己方上述单位。");
                ScriptableObject research=Create(NativeType("ResearchItemAttributesAsset"),spec.Key);Set(research,"m_Attributes",data);Enable(research);Items.Add(spec.Key,research);AddDefinition(spec.Key,data);
            }
            RegisterPredictiveFireControl();
            Runtime.Log("RHODES_FACTION_ATTRIBUTE_RESEARCH_REGISTERED actualCoalitionMPItems=8; privateFireControlItems=1; stagedGunneryItems=3; totalItems="+Keys.Length+"; nativeUpgradePanelItems="+(15+SpecialtyResearchKeys().Length)+"; engineeringTrees="+(CapabilityEngineeringChains().Length+1)+"; fleetResearchType=Upgrade; researchTiming="+RhodesResearchProfile.Name+"; exactPrivateTypeTargets=True; U09TrackedArmourExcluded=True; campaignAndAIExcluded=True; runtimeAcceptancePending=True");
        }
        static void VerifyCoalitionFleetUpgradeAuthority()
        {
            EntityTypeAttributes definition=Registry.GetEntityType("Coalition");
            TechTreeAttributes tree=definition==null?null:definition.Get<TechTreeAttributes>();
            if(tree==null)throw new InvalidOperationException("Audited Coalition MP research tree missing");
            foreach(FleetUpgrade spec in FleetUpgrades())
            {
                int count=0;
                foreach(object branch in tree.TechTrees)
                {
                    foreach(object upgrade in (Array)NativeRead.Get(branch,"m_Upgrades"))if((string)NativeRead.Get(upgrade,"m_ResearchItem")==spec.Donor)count++;
                    foreach(object tier in (Array)NativeRead.Get(branch,"m_TechTiers"))foreach(string key in (string[])NativeRead.Get(tier,"m_ResearchItems"))if(key==spec.Donor)count++;
                }
                if(count!=1)throw new InvalidOperationException("Research must be mounted once in actual Coalition MP tree "+spec.Donor);
            }
        }
        static void ConfigureFleetUpgradeTarget(object target,string key)
        {
            Set(target,"m_UnitTypePrefix",key);Set(target,"m_UseAsPrefix",false);SetEnum(target,"m_UnitClass",0);SetEnum(target,"m_ClassOperator",1);
        }
        static Array BuildFactionUpgradeBranches(object carrier)
        {
            if(!Runtime.SkirmishMode)return BuildCapabilityEngineeringBranches(carrier);
            // Attribute studies and the artillery fire-control study belong to
            // native Upgrades; independent capability unlocks stay Engineering.
            // Capability unlocks are mounted in independent Engineering trees.
            Array prior=(Array)NativeRead.Get(carrier,"m_Upgrades");
            if(prior==null||prior.Length!=6||(string)NativeRead.Get(prior.GetValue(0),"m_ResearchItem")!=Gunnery3)
                throw new InvalidOperationException("Rhodes route Upgrade list must contain main charge and five reactor expansions before fleet upgrades");
            FleetUpgrade[] fleet=FleetUpgrades();Array upgrades=Array.CreateInstance(prior.GetType().GetElementType(),prior.Length+fleet.Length+1);
            prior.CopyTo(upgrades,0);
            for(int i=0;i<fleet.Length;i++)
            {
                object item=NewGameData("TechUpgradeData");Set(item,"m_UpgradeName",fleet[i].Key);Set(item,"m_ResearchItem",fleet[i].Key);upgrades.SetValue(item,prior.Length+i);
            }
            object fireControl=NewGameData("TechUpgradeData");Set(fireControl,"m_UpgradeName",PredictiveFireControl);Set(fireControl,"m_ResearchItem",PredictiveFireControl);upgrades.SetValue(fireControl,prior.Length+fleet.Length);
            Set(carrier,"m_Upgrades",AppendSpecialtyUpgrades(upgrades));return BuildCapabilityEngineeringBranches(carrier);
        }
        static void VerifyFactionUpgrades()
        {
            if(!Runtime.SkirmishMode)return;
            TechTreeAttributes tree=(TechTreeAttributes)NativeRead.Get(Tree,"m_Attributes");
            if(tree.TechTrees.Length!=CapabilityEngineeringChains().Length+1||tree.TechTrees[0].Tiers.Length!=2||((Array)NativeRead.Get(tree.TechTrees[0],"m_Upgrades")).Length!=15+SpecialtyResearchKeys().Length)throw new InvalidOperationException("Fleet and specialty research Upgrade layout differs from the declared catalog");
            if(Keys.Length!=ResearchBalanceCatalog().Length)throw new InvalidOperationException("Skirmish research keys and formal balance catalog must agree");
            VerifyPredictiveFireControl();
            foreach(FleetUpgrade spec in FleetUpgrades())
            {
                ResearchItemAttributes item=Registry.GetEntityType(spec.Key).Get<ResearchItemAttributes>();
                string[] dependencies=(string[])NativeRead.Get(item,"m_ResearchDependencies");
                VerifyResearchBalance(item,spec.Key);
                if(item.TypeOfResearch!=ResearchType.Upgrade||
                    dependencies.Length!=1||dependencies[0]!=spec.Dependency||item.CommandsToRun.Length!=0)throw new InvalidOperationException("Private fleet research contract changed "+spec.Key);
                Array targets=(Array)NativeRead.Get(item,"m_UnitTypeBuffs");if(targets.Length!=spec.Targets.Length)throw new InvalidOperationException("Private fleet target count changed");
                for(int i=0;i<targets.Length;i++)
                {
                    object target=targets.GetValue(i);
                    if((string)NativeRead.Get(target,"m_UnitTypePrefix")!=spec.Targets[i]||(bool)NativeRead.Get(target,"m_UseAsPrefix")||Convert.ToInt32(NativeRead.Get(target,"m_UnitClass"))!=0||Convert.ToInt32(NativeRead.Get(target,"m_ClassOperator"))!=1)
                        throw new InvalidOperationException("Private fleet research has a class/prefix leak "+spec.Key);
                    VerifyFleetUpgradeBuff(NativeRead.Get(NativeRead.Get(target,"m_BuffSet"),"m_Attributes"),spec.Key);
                }
                int appearances=0;foreach(object branch in tree.TechTrees)foreach(object upgrade in (Array)NativeRead.Get(branch,"m_Upgrades"))if((string)NativeRead.Get(upgrade,"m_ResearchItem")==spec.Key)appearances++;
                if(appearances!=1||Array.IndexOf(ExclusiveRouteKeys,spec.Key)>=0)throw new InvalidOperationException("Fleet research display/exclusive-route scope changed "+spec.Key);
            }
        }
    }
}
