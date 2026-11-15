using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BBI.Core;
using BBI.Core.Data;
using BBI.Game.Data;
using BBI.Unity.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class FactionUnits
    {
        public const string SurveyProbeKey="RI_U02_SURVEY_PROBE";
        public const string SurveyProbeProductionKey="Ability_RI_U02_ProduceProbe";
        public const string SurveyProbeLaunchKey="Ability_RI_U02_LaunchProbe";
        const string NativeSurveyProbeKey="C_Probe_MP";
        static Context SurveyProbeContext;

        static void RegisterSurveyProbe()
        {
            if(SurveyProbeContext!=null||Registry.GetEntityType(SurveyProbeKey)!=null)
                throw new InvalidOperationException("Private survey probe identity collision");
            Context c=new Context();
            SurveyProbeContext=c;
            c.Source=FindSource(NativeSurveyProbeKey);
            c.Custom=(UnitAssetContainer)CloneAsset(c,c.Source,SurveyProbeKey);
            CloneClosure(c);
            foreach(ScriptableObject asset in c.Owned)RemapAsset(c,asset);
            c.Core=(ScriptableObject)Read(c.Custom,"m_Attributes");
            c.View=(ScriptableObject)Read(c.Custom,"m_View");
            c.Hud=(ScriptableObject)Read(c.Custom,"m_HUD");
            RhodesUnitVoiceAliases.ConfigureUnitAudio(c.Custom,SurveyProbeKey);
            foreach(ScriptableObject asset in c.Owned)if(!object.ReferenceEquals(asset,c.Custom))CallIfPresent(asset,"OnEnable");
            Write(c.Custom,"mDataCache",null);CallRequired(c.Custom,"OnEnable");
            EntityTypeAttributes definition=new EntityTypeAttributes(SurveyProbeKey);
            foreach(object datum in (IEnumerable)c.Custom)
                if(datum!=null&&!definition.Add(datum))throw new InvalidOperationException("Duplicate private survey probe datum "+datum.GetType().Name);
            Registry.Add(definition);c.Definition=definition;
            VerifySurveyProbe();
        }

        static void PrepareSurveyProbeProduction(Context c)
        {
            foreach(ScriptableObject asset in c.Owned)
            {
                HashSet<object> seen=new HashSet<object>(new IdentityComparer());
                foreach(FieldInfo field in Fields(asset.GetType()))if(field.Name!="mDataCache")
                    field.SetValue(asset,RewriteSurveyProbeIDs(field.GetValue(asset),seen));
            }
            foreach(ScriptableObject asset in (Array)Read(c.Custom,"m_Abilities"))
            {
                string key=SurveyProbeAbilityKey(asset.name);
                if(key==null)continue;
                asset.name=key;
                ((NamedObjectBase)Read(asset,"m_Attributes")).InitializeName(key);
                ((NamedObjectBase)Read(asset,"m_View")).InitializeName(key);
            }
            VerifySurveyProbeProduction(c);
        }

        static string SurveyProbeAbilityKey(string key)
        {
            if(key=="Ability_C_ProduceProbe_MP")return SurveyProbeProductionKey;
            if(key=="Ability_C_LaunchProbe_MP")return SurveyProbeLaunchKey;
            return null;
        }

        // Native prerequisite validation resolves by the global ability name,
        // but queue insertion reads the producer's local ability object. Both
        // must identify the same private probe before the tracked slot is reserved.
        static void RegisterSurveyProbeAbilities(Context c)
        {
            foreach(ScriptableObject ability in (Array)Read(c.Custom,"m_Abilities"))
            {
                if(ability.name!=SurveyProbeProductionKey&&ability.name!=SurveyProbeLaunchKey)continue;
                if(Registry.GetEntityType(ability.name)!=null)throw new InvalidOperationException("Survey command duplicate "+ability.name);
                EntityTypeAttributes definition=new EntityTypeAttributes(ability.name);object data=null;IEnumerable many=ability as IEnumerable;
                if(many==null){data=Read(ability,"Data");many=data as IEnumerable;}
                if(many!=null){foreach(object item in many)if(item!=null&&!definition.Add(item))throw new InvalidOperationException("Duplicate survey command datum");}
                else if(data==null||!definition.Add(data))throw new InvalidOperationException("Survey command has no native datum");
                Registry.Add(definition);
                VerifySurveyProbeAbilityBinding(Registry,(AbilityAttributes)Read(ability,"m_Attributes"),(AbilityViewAttributes)Read(ability,"m_View"));
            }
        }

        static void VerifySurveyProbeAbilityBinding(EntityTypeCollection registry,AbilityAttributes local,AbilityViewAttributes view)
        {
            bool produce=local!=null&&local.Name==SurveyProbeProductionKey;
            if(local==null||(!produce&&local.Name!=SurveyProbeLaunchKey)||view==null||view.Name!=local.Name||
                local.ProduceUnit==null||local.ProduceUnit.UnitTypeToProduce!=SurveyProbeKey||local.ProduceUnit.BypassQueue||
                local.AbilityType!=(produce?AbilityClass.ProduceUnit:AbilityClass.LaunchDocked))
                throw new InvalidOperationException("Survey probe command identity or native route differs");
            EntityTypeAttributes global=registry.GetEntityType(local.Name);
            if(global==null||!object.ReferenceEquals(global.Get<AbilityAttributes>(),local)||!object.ReferenceEquals(global.Get<AbilityViewAttributes>(),view))
                throw new InvalidOperationException("Survey probe local and global command attributes differ");
        }
        static object RewriteSurveyProbeIDs(object value,HashSet<object> seen)
        {
            if(value==null)return null;
            string text=value as string;
            if(text!=null)return text==NativeSurveyProbeKey?SurveyProbeKey:(SurveyProbeAbilityKey(text)??text);
            Type type=value.GetType();
            if(type.IsPrimitive||type.IsEnum||typeof(UnityEngine.Object).IsAssignableFrom(type)||!seen.Add(value))return value;
            Array array=value as Array;
            if(array!=null)
            {
                for(int i=0;i<array.Length;i++)array.SetValue(RewriteSurveyProbeIDs(array.GetValue(i),seen),i);
                return array;
            }
            if(type.Namespace!=null&&type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))
                foreach(FieldInfo field in Fields(type))field.SetValue(value,RewriteSurveyProbeIDs(field.GetValue(value),seen));
            return value;
        }

        static void VerifySurveyProbeProduction(Context c)
        {
            int launch=0,produce=0,bays=0;
            foreach(ScriptableObject asset in (Array)Read(c.Custom,"m_Abilities"))
            {
                if(asset.name=="Ability_C_LaunchProbe_MP"||asset.name=="Ability_C_ProduceProbe_MP")throw new InvalidOperationException("Survey retained a native probe ability identity");
                if(asset.name!=SurveyProbeLaunchKey&&asset.name!=SurveyProbeProductionKey)continue;
                object ability=Read(asset,"m_Attributes");
                if((string)Read(Read(ability,"m_ProduceUnit"),"m_UnitTypeToProduce")!=SurveyProbeKey)
                    throw new InvalidOperationException("Private survey probe production entry differs");
                if(asset.name==SurveyProbeLaunchKey)launch++;else produce++;
                if(c.Definition!=null)VerifySurveyProbeAbilityBinding(Registry,(AbilityAttributes)ability,(AbilityViewAttributes)Read(asset,"m_View"));
            }
            object hangar=Read(Read(c.Custom,"m_UnitHangar"),"m_Attributes");
            foreach(object bay in (Array)Read(hangar,"m_StorableEntities"))
            {
                string entity=(string)Read(bay,"m_EntityType");
                if(entity==NativeSurveyProbeKey)throw new InvalidOperationException("Survey hangar retains a native probe entry");
                if(entity==SurveyProbeKey)bays++;
            }
            if(launch!=1||produce!=1||bays!=1)throw new InvalidOperationException("Survey must retain one private probe launch, rebuild and bay");
        }

        static void VerifySurveyProbe()
        {
            Context c=SurveyProbeContext;
            if(c==null||c.Definition==null||!object.ReferenceEquals(Registry.GetEntityType(SurveyProbeKey),c.Definition))
                throw new InvalidOperationException("Private survey probe registry generation differs");
            UnitAttributes source=Registry.GetEntityType(NativeSurveyProbeKey).Get<UnitAttributes>();
            UnitAttributes own=c.Definition.Get<UnitAttributes>();
            if(own==null||object.ReferenceEquals(own,source)||own.MaxHealth!=source.MaxHealth||own.Armour!=source.Armour||
                own.PopCapCost!=source.PopCapCost||own.Resource1Cost!=source.Resource1Cost||own.Resource2Cost!=source.Resource2Cost)
                throw new InvalidOperationException("Private survey probe base attributes differ");
            VerifySourceIsolation(c);
        }

        static void ReleaseSurveyProbe()
        {
            if(SurveyProbeContext==null)return;
            Context c=SurveyProbeContext;
            for(int i=c.Owned.Count-1;i>=0;i--)if(c.Owned[i]!=null)UnityEngine.Object.DestroyImmediate(c.Owned[i]);
            SurveyProbeContext=null;
        }
    }
}
