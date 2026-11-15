using System;
using System.Collections.Generic;
using BBI.Core;
using BBI.Core.Data;
using BBI.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesResearch
    {
        public const string AirDefenceReload="Research_RI_AirDefenceReload";
        public const string EngineeringRepair="Research_RI_EngineeringRepair";
        public const string SurveyProbeDurability="Research_RI_SurveyProbeDurability";
        public const string InterceptorService="Research_RI_InterceptorService";
        public const string BomberProtection="Research_RI_BomberProtection";
        public const string EscortBoost="Research_RI_Escort_Boost";
        const string InterceptorResearchBay="EF_Rhodes_Interceptor";
        sealed class SpecialtyResearchSpec
        {
            internal readonly string Key,Title,Dependency,Target,Scope,Description;
            internal readonly int Mode,Value;internal readonly int[] Attributes;
            internal SpecialtyResearchSpec(string key,string title,string dependency,string target,string scope,int mode,int value,string description,params int[] attributes)
            {Key=key;Title=title;Dependency=dependency;Target=target;Scope=scope;Mode=mode;Value=value;Description=description;Attributes=attributes;}
        }
        static SpecialtyResearchSpec[] SpecialtyResearchSpecs()
        {
            return new SpecialtyResearchSpec[]{
                new SpecialtyResearchSpec(AirDefenceReload,"防空快速再装填",AirDefenceAssembly,FactionUnits.AirDefenceKey,"ANTI-AIR",1,-15,
                    "己方防空导弹车的对空导弹再装填时间缩短15%。",262151),
                new SpecialtyResearchSpec(EngineeringRepair,"工程维修工具",ProductionLine2,FactionUnits.EngineeringKey,FactionUnits.RepairWeaponID,1,20,
                    "己方工程支援车的维修频率提高20%，每次修复1点生命。",262149),
                new SpecialtyResearchSpec(SurveyProbeDurability,"勘探探针耐久",LightAssembly,FactionUnits.SurveyProbeKey,string.Empty,1,100,
                    "己方外勤勘探车生产的侦察探针最大生命提高100%。",65536),
                new SpecialtyResearchSpec(InterceptorService,"截击快速整备",Aviation1,RhodesCarrierUnits.UnitKey,InterceptorResearchBay,1,-20,
                    "本舰截击机维修与补弹服务时间缩短20%，最低停靠时间仍然适用。",536870913,536870914),
                new SpecialtyResearchSpec(BomberProtection,"重轰防破片",Aviation1,RhodesCarrierUnits.HeavyBomberKey,string.Empty,0,150,
                    "己方舰载重型轰炸机最大生命增加150点。",65536)};
        }
        static string[] SpecialtyResearchKeys()
        {SpecialtyResearchSpec[] specs=SpecialtyResearchSpecs();string[] result=new string[specs.Length];for(int i=0;i<specs.Length;i++)result[i]=specs[i].Key;return result;}
        static bool IsSpecialtyResearch(string key)
        {return key==AirDefenceReload||key==EngineeringRepair||key==SurveyProbeDurability||key==InterceptorService||key==BomberProtection;}
        static string[] WithSpecialtyResearchKeys(string[] original)
        {if(!Runtime.SkirmishMode)return original;List<string> keys=new List<string>(original);keys.AddRange(SpecialtyResearchKeys());return keys.ToArray();}
        static void RegisterSpecialtyResearch()
        {
            if(!Runtime.SkirmishMode)return;
            foreach(SpecialtyResearchSpec spec in SpecialtyResearchSpecs())
            {
                Add(spec.Key,spec.Title,ResearchType.Upgrade,new string[]{spec.Dependency});
                object data=NativeRead.Get(Items[spec.Key],"m_Attributes");
                Set(data,"m_LocalizedShortDescriptionStringID",spec.Description);Set(data,"m_LocalizedLongDescriptionStringID",spec.Description);
                object set=BuildSpecialtyBuffData(spec);ScriptableObject buff=Create(NativeType("BuffSetAttributesAsset"),spec.Key+"_PrivateBuff");Set(buff,"m_Attributes",set);Enable(buff);
                object target=NewData("UnitTypeBuffData");ConfigureFleetUpgradeTarget(target,spec.Target);Set(target,"m_BuffSet",buff);
                Set(data,"m_UnitTypeBuffs",ArrayOf(target));Enable(Items[spec.Key]);
            }
        }
        static object BuildSpecialtyBuffData(SpecialtyResearchSpec spec)
        {
            object set=NewData("AttributeBuffSetData");Array entries=Array.CreateInstance(NewData("AttributeBuffData").GetType(),spec.Attributes.Length);
            for(int i=0;i<spec.Attributes.Length;i++)
            {
                object entry=NewData("AttributeBuffData");Set(entry,"m_Name",spec.Scope);SetEnum(entry,"m_AttributeID",spec.Attributes[i]);
                SetEnum(entry,"m_Mode",spec.Mode);Set(entry,"m_Value",spec.Value);entries.SetValue(entry,i);
            }
            Set(set,"m_Buffs",entries);return set;
        }
        static Array AppendSpecialtyUpgrades(Array original)
        {
            string[] keys=SpecialtyResearchKeys();Array result=Array.CreateInstance(original.GetType().GetElementType(),original.Length+keys.Length);original.CopyTo(result,0);
            for(int i=0;i<keys.Length;i++){object item=NewGameData("TechUpgradeData");Set(item,"m_UpgradeName",keys[i]);Set(item,"m_ResearchItem",keys[i]);result.SetValue(item,original.Length+i);}return result;
        }
        static void VerifySpecialtyResearch(TechTreeAttributes tree)
        {
            if(!Runtime.SkirmishMode)return;
            foreach(SpecialtyResearchSpec spec in SpecialtyResearchSpecs())
            {
                ResearchItemAttributes item=Registry.GetEntityType(spec.Key).Get<ResearchItemAttributes>();VerifyResearchBalance(item,spec.Key);
                string[] deps=NativeRead.Get(item,"m_ResearchDependencies") as string[];Array targets=(Array)NativeRead.Get(item,"m_UnitTypeBuffs");
                if(item.TypeOfResearch!=ResearchType.Upgrade||deps==null||deps.Length!=1||deps[0]!=spec.Dependency||
                    EngineeringAppearances(tree,spec.Key)!=0||UpgradeAppearances(tree,spec.Key)!=1||targets.Length!=1||item.CommandsToRun.Length!=0)
                    throw new InvalidOperationException("Specialty research registration mismatch: "+spec.Key);
                object target=targets.GetValue(0);
                if((string)NativeRead.Get(target,"m_UnitTypePrefix")!=spec.Target||(bool)NativeRead.Get(target,"m_UseAsPrefix")||
                    Convert.ToInt32(NativeRead.Get(target,"m_UnitClass"))!=0||Convert.ToInt32(NativeRead.Get(target,"m_ClassOperator"))!=1)
                    throw new InvalidOperationException("Specialty research target must be the exact declared private type: "+spec.Key);
                Array buffs=(Array)NativeRead.Get(NativeRead.Get(NativeRead.Get(target,"m_BuffSet"),"m_Attributes"),"m_Buffs");
                if(buffs.Length!=spec.Attributes.Length)throw new InvalidOperationException("Specialty research buff count differs: "+spec.Key);
                for(int i=0;i<buffs.Length;i++)
                {
                    object buff=buffs.GetValue(i);
                    if((string)NativeRead.Get(buff,"m_Name")!=spec.Scope||Convert.ToInt32(NativeRead.Get(buff,"m_AttributeID"))!=spec.Attributes[i]||
                        Convert.ToInt32(NativeRead.Get(buff,"m_Mode"))!=spec.Mode||Convert.ToInt32(NativeRead.Get(buff,"m_Value"))!=spec.Value)
                        throw new InvalidOperationException("Specialty research effect differs: "+spec.Key);
                }
                // Carrier and bomber are registered after research in the native
                // entry point. Defer only these two exact, declared targets.
                if(!IsDeferredSpecialtyTarget(spec))VerifySpecialtyTarget(spec);
            }
        }
        static bool IsDeferredSpecialtyTarget(SpecialtyResearchSpec spec)
        {
            return (spec.Key==InterceptorService&&spec.Target==RhodesCarrierUnits.UnitKey)||
                (spec.Key==BomberProtection&&spec.Target==RhodesCarrierUnits.HeavyBomberKey);
        }
        // Called once immediately after RhodesCarrierUnits.Register. Missing
        // late targets are fatal here; no arbitrary missing definition is skipped.
        public static void VerifyRegisteredSpecialtyTargets()
        {
            if(!Runtime.SkirmishMode)return;
            foreach(SpecialtyResearchSpec spec in SpecialtyResearchSpecs())VerifySpecialtyTarget(spec);
        }
        static void VerifySpecialtyTarget(SpecialtyResearchSpec spec)
        {
            EntityTypeAttributes definition=Registry.GetEntityType(spec.Target);
            if(definition==null)throw new InvalidOperationException("Specialty research target is not registered: "+spec.Key+" -> "+spec.Target);
            UnitAttributes unit=definition.Get<UnitAttributes>();
            if(unit==null)throw new InvalidOperationException("Specialty target has no unit attributes: "+spec.Target);
            if(spec.Key==AirDefenceReload||spec.Key==EngineeringRepair)
            {
                int matching=0;
                if(unit.WeaponLoadout!=null)foreach(WeaponBinding binding in unit.WeaponLoadout)if(binding!=null&&binding.WeaponID==spec.Scope)matching++;
                if(matching!=1)throw new InvalidOperationException("Specialty research requires exactly one named weapon: "+spec.Key);
            }
            if(spec.Key==InterceptorService)
            {
                UnitHangarAttributes hangar=definition.Get<UnitHangarAttributes>();int matching=0;
                if(hangar!=null&&hangar.HangarBays!=null)foreach(HangarBay bay in hangar.HangarBays)if(bay!=null&&bay.Name==spec.Scope)matching++;
                if(matching!=1)throw new InvalidOperationException("Interceptor service requires exactly one private named bay");
            }
        }
        static void RegisterEscortBoost()
        {
            const string donorKey="Research_Buff_C_Escort";
            EntityTypeAttributes definition=Registry.GetEntityType(donorKey);ResearchItemAttributes donor=definition==null?null:definition.Get<ResearchItemAttributes>();
            object data=BuildEscortBoostData(donor);
            ScriptableObject asset=Create(NativeType("ResearchItemAttributesAsset"),EscortBoost);Set(asset,"m_Attributes",data);Enable(asset);Items.Add(EscortBoost,asset);AddDefinition(EscortBoost,data);
            Runtime.Log("RHODES_LAV_BOOST_REGISTERED donorType="+donor.TypeOfResearch+"; privateType="+((ResearchItemAttributes)data).TypeOfResearch+
                "; privateEngineeringUnlock=True; nativeCommandsAndBuffsEmpty=True; nativeDonorUnchanged=True");
        }
        static object BuildEscortBoostData(ResearchItemAttributes donor)
        {
            if(donor==null||donor.TypeOfResearch!=ResearchType.Upgrade||donor.CommandsToRun.Length!=0||((Array)NativeRead.Get(donor,"m_UnitTypeBuffs")).Length!=0)
                throw new InvalidOperationException("Native LAV boost research donor must remain a pure capability unlock");
            if(Convert.ToInt32(NativeRead.Get(donor,"m_Resource1Cost"))!=200||Convert.ToInt32(NativeRead.Get(donor,"m_Resource2Cost"))!=125||
                donor.ResearchTime!=BBI.Core.Utility.FixedPoint.Fixed64.FromInt(50))throw new InvalidOperationException("Native LAV boost research balance changed");
            object data=Copy(donor,new Dictionary<object,object>(new Identity()));((NamedObjectBase)data).InitializeName(EscortBoost);
            SetEnum(data,"m_ResearchType",(int)ResearchType.Engineering);
            Set(data,"m_ResearchDependencies",new string[]{LightAssembly});ApplyResearchIcon(data,EscortBoost);ApplyResearchBalance(data,EscortBoost);
            Set(data,"m_ResearchVOCode",ResearchVoiceCode(EscortBoost));
            Set(data,"m_LocalizedResearchTitleStringID","轻突战术加速");Set(data,"m_LocalizedShortDescriptionStringID","解锁轻型突击车战术加速");
            Set(data,"m_LocalizedLongDescriptionStringID","轻型突击车可使用战术加速，短时间提高移动速度。");
            return data;
        }
        static void VerifyEscortBoost(TechTreeAttributes tree)
        {
            ResearchItemAttributes item=Registry.GetEntityType(EscortBoost).Get<ResearchItemAttributes>();VerifyResearchBalance(item,EscortBoost);
            string[] deps=(string[])NativeRead.Get(item,"m_ResearchDependencies");
            if(item.TypeOfResearch!=ResearchType.Engineering||deps.Length!=1||deps[0]!=LightAssembly||EngineeringAppearances(tree,EscortBoost)!=1||UpgradeAppearances(tree,EscortBoost)!=0||
                item.CommandsToRun.Length!=0||((Array)NativeRead.Get(item,"m_UnitTypeBuffs")).Length!=0)
                throw new InvalidOperationException("LAV boost must be an independent private Engineering unlock");
        }
        static void VerifyResearchDependencyGraph()
        {
            HashSet<string> declared=new HashSet<string>(StringComparer.Ordinal),visiting=new HashSet<string>(StringComparer.Ordinal),visited=new HashSet<string>(StringComparer.Ordinal);
            foreach(string key in Keys)if(!declared.Add(key))throw new InvalidOperationException("Duplicate private research key: "+key);
            foreach(string key in Keys)VisitResearchDependency(key,declared,visiting,visited);
        }
        static void VisitResearchDependency(string key,HashSet<string> declared,HashSet<string> visiting,HashSet<string> visited)
        {
            if(visited.Contains(key))return;
            if(!declared.Contains(key)||!visiting.Add(key))throw new InvalidOperationException("Missing or cyclic private research dependency: "+key);
            ResearchItemAttributes item=Registry.GetEntityType(key).Get<ResearchItemAttributes>();
            foreach(string dependency in (string[])NativeRead.Get(item,"m_ResearchDependencies"))VisitResearchDependency(dependency,declared,visiting,visited);
            visiting.Remove(key);visited.Add(key);
        }
    }
}
