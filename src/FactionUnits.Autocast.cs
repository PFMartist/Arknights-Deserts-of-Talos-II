using System;
using BBI.Core;
using BBI.Core.Data;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Unity.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class FactionUnits
    {
        internal const string AutocastPulseKey="StatusEffect_EF_U08_AutoTogglePulse";
        static readonly Type AutocastPulseAssetType=typeof(UnitAssetContainer).Assembly.GetType("BBI.Unity.Game.Data.StatusEffectAttributesAsset",true);
        static readonly Type AutocastPulseDataType=typeof(UnitAssetContainer).Assembly.GetType("BBI.Unity.Game.Data.StatusEffectAttributesData",true);
        static readonly Type AutocastApplyDataType=typeof(UnitAssetContainer).Assembly.GetType("BBI.Unity.Game.Data.ApplyStatusEffectAttributesData",true);

        // A private zero-duration marker makes this a valid native instant
        // self ability. It carries no buff, modifier, weapon trigger or FX.
        // ApplyStatusEffect does not clear goals or invoke aircraft sorties.
        internal static object CreateAutocastPulseData()
        {
            object data=Activator.CreateInstance(AutocastPulseDataType,true);((NamedObjectBase)data).InitializeName(AutocastPulseKey);
            Write(data,"m_Lifetime",StatusEffectLifetime.Finite);Write(data,"m_Duration",0f);Write(data,"m_MaxStacks",1);Write(data,"m_StackingBehaviour",StackingBehaviour.Ignore);
            Write(data,"m_WeaponFireTriggerEndEvent",(WeaponFireTrigger)0);
            Type buffs=typeof(UnitAssetContainer).Assembly.GetType("BBI.Unity.Game.Data.UnitTypeBuffData",true);
            Write(data,"m_BuffsToApplyToTarget",Array.CreateInstance(buffs,0));Write(data,"m_UnitTypeBuffsToApply",Array.CreateInstance(buffs,0));Write(data,"m_Modifiers",new ModifierAttributes[0]);
            object flags=Read(data,"m_FXSetStatesWhenActive");Write(data,"m_FXSetStatesWhenActive",Enum.ToObject(flags.GetType(),0));return data;
        }
        static ScriptableObject PrepareAutocastPulse(Context c)
        {
            if(Registry.GetEntityType(AutocastPulseKey)!=null)throw new InvalidOperationException("Autocast pulse key collision");
            ScriptableObject pulse=RhodesAssetClone59.CreateEmpty(AutocastPulseAssetType);pulse.name=AutocastPulseKey;pulse.hideFlags=HideFlags.HideAndDontSave;
            Write(pulse,"m_Attributes",CreateAutocastPulseData());c.Owned.Add(pulse);return pulse;
        }
        internal static void ConfigureAutocastToggleData(object data,Array serializedEffects)
        {
            if(data==null||serializedEffects==null||serializedEffects.GetType().GetElementType()!=AutocastPulseAssetType||serializedEffects.Length!=1||serializedEffects.GetValue(0)==null)
                throw new ArgumentException("Autocast requires one serialized private pulse asset");
            Write(data,"m_AbilityType",AbilityClass.ApplyStatusEffect);Write(data,"m_TargetingType",AbilityTargetingType.Self);Write(data,"m_TargetAlignment",AbilityTargetAlignment.Self);
            Write(data,"m_CasterMovesToTarget",false);Write(data,"m_IsToggleable",false);Write(data,"m_CastOnDeath",false);
            Write(data,"m_CooldownTimeSecs",0f);Write(data,"m_WarmupTimeSecs",0f);Write(data,"m_SharedCooldownChannel",0);
            Write(Read(data,"m_ResearchDeps"),"m_DependentResearch",new string[]{RhodesResearch.AdvancedOriginiumSuppressant});
            Write(Read(data,"m_Cost"),"m_Resource1Cost",0);Write(Read(data,"m_Cost"),"m_Resource2Cost",0);
            Write(Read(data,"m_Autocast"),"m_AutocastEnabled",false);Write(Read(data,"m_Autocast"),"m_AutocastEnabledOnSpawn",false);
            object apply=Activator.CreateInstance(AutocastApplyDataType,true);
            // Only the asset array is editable. AbilityAttributesAsset.OnEnable
            // must construct mStatusEffectsToApply before registry publication.
            Write(apply,"m_StatusEffectsToApply",serializedEffects);Write(data,"m_ApplyStatusEffect",apply);
        }
        static ScriptableObject PrepareAutocastToggle(Context c,ScriptableObject source,int kind,ScriptableObject pulse)
        {
            string title=(kind==0?"莱万汀":kind==1?"洁尔佩塔":"艾尔黛拉")+"：自动施法开关";
            ScriptableObject ability=OperatorAbility(c,source,RhodesEmbarkedOperatorSkills.AutoToggleAbilityKey(kind),title,KeyCode.None,
                "研发先进源石抑制剂后，切换本机舱内该干员的自动施法。只改变自动施法偏好；不影响当前指令、手动技能或剩余冷却。默认开启。");
            Array effects=Array.CreateInstance(AutocastPulseAssetType,1);effects.SetValue(pulse,0);ConfigureAutocastToggleData(Read(ability,"m_Attributes"),effects);return ability;
        }
        static ScriptableObject AutocastPulseAsset(Context c)
        {
            ScriptableObject result=null;
            foreach(ScriptableObject asset in c.Owned)if(asset!=null&&asset.name==AutocastPulseKey)
            {if(result!=null)throw new InvalidOperationException("Autocast pulse must have one owned asset");result=asset;}
            if(result==null||result.GetType()!=AutocastPulseAssetType)throw new InvalidOperationException("Autocast pulse owned asset missing");return result;
        }
        static void RegisterAutocastPulse(Context c)
        {
            ScriptableObject pulse=AutocastPulseAsset(c);EntityTypeAttributes definition=new EntityTypeAttributes(AutocastPulseKey);
            if(Registry.GetEntityType(AutocastPulseKey)!=null)throw new InvalidOperationException("Autocast pulse registry collision");
            AddEmbarkedAbilityData(definition,Read(pulse,"Data"));Registry.Add(definition);
        }
        internal static void VerifyAutocastPulseData(StatusEffectAttributes pulse)
        {
            if(pulse==null||pulse.Name!=AutocastPulseKey||pulse.Lifetime!=StatusEffectLifetime.Finite||pulse.Duration!=Fixed64.Zero||pulse.MaxStacks!=1||
                pulse.StackingBehaviour!=StackingBehaviour.Ignore||Convert.ToInt32(pulse.WeaponFireTriggerEndEvent)!=0||
                pulse.BuffsToApplyToTarget==null||pulse.BuffsToApplyToTarget.Length!=0||pulse.UnitTypeBuffsToApply==null||pulse.UnitTypeBuffsToApply.Length!=0||pulse.Modifiers==null||pulse.Modifiers.Length!=0||
                Convert.ToInt32(Read(pulse,"m_FXSetStatesWhenActive"))!=0)throw new InvalidOperationException("Autocast pulse must have no gameplay or visual effects");
        }
        internal static void VerifyAutocastEffectCache(AbilityAttributes ability,object pulseAsset)
        {
            if(ability==null||pulseAsset==null||pulseAsset.GetType()!=AutocastPulseAssetType)throw new InvalidOperationException("Autocast serialized pulse asset missing");
            object apply=Read(ability,"m_ApplyStatusEffect");Array raw=apply==null?null:Read(apply,"m_StatusEffectsToApply") as Array;
            StatusEffectAttributes[] built=ability.ApplyStatusEffect==null?null:ability.ApplyStatusEffect.StatusEffectsToApply;
            if(raw==null||raw.GetType().GetElementType()!=AutocastPulseAssetType||raw.Length!=1||!object.ReferenceEquals(raw.GetValue(0),pulseAsset)||
                built==null||built.Length!=1||!object.ReferenceEquals(built[0],Read(pulseAsset,"m_Attributes")))throw new InvalidOperationException("Autocast native effect cache differs from serialized private asset");
        }
        static void VerifyAutocastToggles(Context c)
        {
            ScriptableObject pulseAsset=AutocastPulseAsset(c);StatusEffectAttributes pulse=(StatusEffectAttributes)Read(pulseAsset,"m_Attributes");VerifyAutocastPulseData(pulse);
            EntityTypeAttributes pulseDefinition=Registry.GetEntityType(AutocastPulseKey);
            if(pulseDefinition==null||!object.ReferenceEquals(pulseDefinition.Get<StatusEffectAttributes>(),pulse))throw new InvalidOperationException("Autocast pulse registry identity differs");
            for(int kind=0;kind<RhodesEmbarkedOperatorSkills.SkillCount;kind++)
            {
                string key=RhodesEmbarkedOperatorSkills.AutoToggleAbilityKey(kind);ScriptableObject asset=null;int count=0;
                foreach(ScriptableObject item in (Array)Read(c.Custom,"m_Abilities"))if(item!=null&&item.name==key){asset=item;count++;}
                EntityTypeAttributes definition=Registry.GetEntityType(key);AbilityAttributes ability=definition==null?null:definition.Get<AbilityAttributes>();
                if(count!=1||asset==null||ability==null||!object.ReferenceEquals(ability,Read(asset,"m_Attributes"))||ability.AbilityType!=AbilityClass.ApplyStatusEffect||
                    ability.TargetingType!=AbilityTargetingType.Self||ability.TargetAlignment!=AbilityTargetAlignment.Self||ability.CasterMovesToTarget||ability.IsToggleable||ability.CastOnDeath||
                    ability.CooldownTimeSecs!=Fixed64.Zero||ability.WarmupTimeSecs!=Fixed64.Zero||ability.SharedCooldownChannel!=0||ability.Autocast.IsAutocastable||ability.Autocast.AutocastEnabledOnSpawn||
                    (KeyCode)Read(Read(asset,"m_View"),"Hotkey")!=KeyCode.None||(KeyCode)Read(Read(asset,"m_View"),"ToggledHotkey")!=KeyCode.None)
                    throw new InvalidOperationException("Autocast native self command contract differs: "+key);
                Array dependencies=(Array)Read(Read(ability,"m_ResearchDeps"),"m_DependentResearch");
                if(dependencies.Length!=1||(string)dependencies.GetValue(0)!=RhodesResearch.AdvancedOriginiumSuppressant)throw new InvalidOperationException("Autocast native research requirement differs");
                VerifyAutocastEffectCache(ability,pulseAsset);
                foreach(object context in (Array)Read(Read(c.Hud,"m_Attributes"),"m_HUDContexts"))
                    foreach(string entry in (string[])Read(context,"m_AbilityOrder"))if(entry==key)throw new InvalidOperationException("Autocast command must not displace existing native command cells");
            }
        }
    }
}
