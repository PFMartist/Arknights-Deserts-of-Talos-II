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
        static string ResearchForFactionAbility(string unitKey,string abilityName)
        {
            if(unitKey==EscortKey&&FactionAbilitySourceKey(unitKey,abilityName)=="Ability_C_Nitrous_MP")return RhodesResearch.EscortBoost;
            if(unitKey==TiltStrikeKey)
                for(int kind=0;kind<RhodesEmbarkedOperatorSkills.SkillCount;kind++)
                    if(abilityName==RhodesEmbarkedOperatorSkills.AutoToggleAbilityKey(kind))return RhodesResearch.AdvancedOriginiumSuppressant;
            return null;
        }

        static void ConfigureFactionAbilityResearch(string unitKey,string abilityName,object data)
        {
            if(IsSharedFactionAbility(abilityName))return;
            string research=ResearchForFactionAbility(unitKey,abilityName);
            Write(Read(data,"m_ResearchDeps"),"m_DependentResearch",research==null?new string[0]:new string[]{research});
            object starts=Read(data,"m_StartsRemovedIn");
            Write(data,"m_StartsRemovedIn",starts.GetType().IsEnum?Enum.ToObject(starts.GetType(),0):(object)0);
        }

        static bool IsSharedFactionAbility(string key)
        {return key=="Ability_Veterancy"||key.StartsWith("Ability_Common_",StringComparison.Ordinal);}

        internal static string FactionAbilityPrivateKey(string unitKey,string key)
        {
            if(IsSharedFactionAbility(key)||key.StartsWith("Ability_RI_",StringComparison.Ordinal)||key.StartsWith("Ability_EF_",StringComparison.Ordinal))return key;
            string suffix=key.StartsWith("Ability_",StringComparison.Ordinal)?key.Substring(8):key;
            return "Ability_"+unitKey+"_"+suffix;
        }

        static string FactionAbilitySourceKey(string unitKey,string key)
        {
            string prefix="Ability_"+unitKey+"_";
            return key.StartsWith(prefix,StringComparison.Ordinal)?"Ability_"+key.Substring(prefix.Length):key;
        }

        // HUD requests, prerequisite validation and execution all resolve the
        // ability by its global key. Give each adapted local command that same
        // private identity before the native asset caches are initialized.
        static void PrepareFactionAbilityIdentities(Context c)
        {
            Dictionary<string,string> aliases=new Dictionary<string,string>(StringComparer.Ordinal);
            Array abilities=(Array)Read(c.Custom,"m_Abilities");
            foreach(ScriptableObject ability in abilities)
            {
                if(IsSharedFactionAbility(ability.name))continue;
                string key=FactionAbilityPrivateKey(c.Spec.Key,ability.name);
                AddFactionAbilityAlias(aliases,ability.name,key);
                AddFactionAbilityAlias(aliases,((AbilityAttributes)Read(ability,"m_Attributes")).Name,key);
                AddFactionAbilityAlias(aliases,((AbilityViewAttributes)Read(ability,"m_View")).Name,key);
            }
            foreach(ScriptableObject asset in c.Owned)
            {
                HashSet<object> seen=new HashSet<object>(new IdentityComparer());
                foreach(FieldInfo field in Fields(asset.GetType()))if(field.Name!="mDataCache")
                    field.SetValue(asset,RewriteFactionAbilityIDs(field.GetValue(asset),aliases,seen));
            }
            foreach(ScriptableObject ability in abilities)
            {
                if(IsSharedFactionAbility(ability.name))continue;
                string key=FactionAbilityPrivateKey(c.Spec.Key,ability.name);
                ability.name=key;
                ((NamedObjectBase)Read(ability,"m_Attributes")).InitializeName(key);
                ((NamedObjectBase)Read(ability,"m_View")).InitializeName(key);
            }
        }

        static void AddFactionAbilityAlias(Dictionary<string,string> aliases,string source,string target)
        {
            if(string.IsNullOrEmpty(source)||source==target)return;
            string existing;
            if(aliases.TryGetValue(source,out existing)&&existing!=target)throw new InvalidOperationException("Ambiguous faction ability identity "+source);
            aliases[source]=target;
        }

        static object RewriteFactionAbilityIDs(object value,Dictionary<string,string> aliases,HashSet<object> seen)
        {
            if(value==null)return null;
            string key=value as string,replacement;
            if(key!=null)return aliases.TryGetValue(key,out replacement)?replacement:key;
            Type type=value.GetType();
            if(type.IsPrimitive||type.IsEnum||typeof(UnityEngine.Object).IsAssignableFrom(type)||!seen.Add(value))return value;
            Array array=value as Array;
            if(array!=null)
            {
                for(int i=0;i<array.Length;i++)array.SetValue(RewriteFactionAbilityIDs(array.GetValue(i),aliases,seen),i);
                return array;
            }
            if(type.Namespace!=null&&type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))
                foreach(FieldInfo field in Fields(type))field.SetValue(value,RewriteFactionAbilityIDs(field.GetValue(value),aliases,seen));
            return value;
        }

        static void RegisterFactionAbilityCommands(Context c)
        {
            int count=0;
            foreach(ScriptableObject ability in (Array)Read(c.Custom,"m_Abilities"))
            {
                if(IsSharedFactionAbility(ability.name))continue;
                EntityTypeAttributes definition=Registry.GetEntityType(ability.name);
                if(definition==null)
                {
                    definition=new EntityTypeAttributes(ability.name);object data=null;IEnumerable many=ability as IEnumerable;
                    if(many==null){data=Read(ability,"Data");many=data as IEnumerable;}
                    if(many!=null){foreach(object item in many)if(item!=null&&!definition.Add(item))throw new InvalidOperationException("Duplicate faction ability datum "+ability.name);}
                    else if(data==null||!definition.Add(data))throw new InvalidOperationException("Faction ability has no native datum "+ability.name);
                    Registry.Add(definition);
                }
                VerifyFactionAbilityBinding(Registry,ability.name,(AbilityAttributes)Read(ability,"m_Attributes"),(AbilityViewAttributes)Read(ability,"m_View"));
                count++;
            }
            Runtime.Log("RHODES_FACTION_ABILITIES_REGISTERED unit="+c.Spec.Key+"; privateCommands="+count+
                "; localGlobalDataAndViewSame=True; sharedCommonCommandsRetained=True; rewrittenHudAndChainReferences=True; nativeFactionDefinitionsUnchanged=True");
        }

        static void VerifyFactionAbilityBinding(EntityTypeCollection registry,string key,AbilityAttributes local,AbilityViewAttributes view)
        {
            EntityTypeAttributes definition=registry.GetEntityType(key);
            if(local==null||view==null||local.Name!=key||view.Name!=key||definition==null||
                !object.ReferenceEquals(definition.Get<AbilityAttributes>(),local)||!object.ReferenceEquals(definition.Get<AbilityViewAttributes>(),view))
                throw new InvalidOperationException("Faction ability local and global identity differs "+key);
        }

        static void VerifyFactionAbilityResearch(Context c)
        {
            int boost=0;
            foreach(ScriptableObject asset in (Array)Read(c.Custom,"m_Abilities"))
            {
                AbilityAttributes data=Read(asset,"m_Attributes") as AbilityAttributes;
                if(data==null)throw new InvalidOperationException("Private faction ability has no native attributes "+asset.name);
                if(IsSharedFactionAbility(asset.name))continue;
                VerifyFactionAbilityBinding(Registry,asset.name,data,(AbilityViewAttributes)Read(asset,"m_View"));
                if(c.Definition!=null&&!object.ReferenceEquals(c.Definition.Get<AbilityAttributes>(asset.name),data))
                    throw new InvalidOperationException("Faction unit command differs from its global registry "+asset.name);
                string expected=ResearchForFactionAbility(c.Spec.Key,asset.name);
                string[] required=(string[])Read(Read(data,"m_ResearchDeps"),"m_DependentResearch");
                if(required.Length!=(expected==null?0:1)||(expected!=null&&required[0]!=expected))
                    throw new InvalidOperationException("Private faction ability research differs "+c.Spec.Key+" / "+asset.name);
                if(expected==RhodesResearch.EscortBoost)boost++;
            }
            if(boost!=(c.Spec.Key==EscortKey?1:0))throw new InvalidOperationException("Private LAV boost ability inventory differs");
        }
    }
}
