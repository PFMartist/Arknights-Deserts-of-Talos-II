using System;
using System.Collections.Generic;
using System.Reflection;
using BBI.Core;
using BBI.Core.Data;
using BBI.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class HumanoidUnits
    {
        static void PrepareOperatorReturn(Context c)
        {
            const string nativeKey="Ability_Common_StopAbility";
            ScriptableObject source=null;
            foreach(ScriptableObject candidate in (Array)Read(c.Source,"m_Abilities"))
                if(candidate!=null&&candidate.name==nativeKey){source=candidate;break;}
            if(source==null)throw new InvalidOperationException("Native stop command required for operator return");
            // A separate owned clone is essential: CloneAsset's donor map
            // already contains the normal Stop command and must keep that ID.
            ScriptableObject ability=RhodesAssetClone59.CreateEmpty(source.GetType());
            Dictionary<object,object> copied=new Dictionary<object,object>(new IdentityComparer());
            foreach(FieldInfo field in Fields(source.GetType()))if(field.Name!="mDataCache")
                field.SetValue(ability,CopyData(field.GetValue(source),copied));
            Own(c,ability,RhodesOperatorTransport.ReturnAbilityKey);c.OperatorReturnAbility=ability;
            object data=Read(ability,"m_Attributes"),view=Read(ability,"m_View");
            ((NamedObjectBase)data).InitializeName(ability.name);((NamedObjectBase)view).InitializeName(ability.name);
            ClearArray(Read(data,"m_ResearchDeps"),"m_DependentResearch");
            object removed=Read(data,"m_StartsRemovedIn");Write(data,"m_StartsRemovedIn",Enum.ToObject(removed.GetType(),0));
            Write(data,"m_CooldownTimeSecs",0f);Write(data,"m_WarmupTimeSecs",0f);Write(data,"m_SharedCooldownChannel",0);
            Write(data,"m_IsToggleable",false);
            Write(Read(data,"m_Autocast"),"m_AutocastEnabled",false);Write(Read(data,"m_Autocast"),"m_AutocastEnabledOnSpawn",false);
            Write(view,"LocalizedTitleStringID","返回旋翼机");Write(view,"LocalizedShortDescriptionStringID","返回旋翼机");
            Write(view,"LocalizedLongDescriptionStringID","呼叫所属旋翼机前来接回干员。旋翼机悬停后完成空中接驳，无需降落；支持同时召回多名干员。若所属旋翼机不可用，干员将留在地面。");
            Write(view,"Hotkey",KeyCode.B);Write(view,"ToggledHotkey",KeyCode.None);Write(view,"BindingIsExclusive",false);
            // Verified native Ability_C_DockAll icon in build12339551.
            Write(view,"IconName","Icon-Command-DockAll");
            Array old=(Array)Read(c.Custom,"m_Abilities");Array next=Array.CreateInstance(old.GetType().GetElementType(),old.Length+1);
            Array.Copy(old,next,old.Length);next.SetValue(ability,old.Length);Write(c.Custom,"m_Abilities",next);
            c.BasicAbilities.Add(ability);
            bool attached=false;
            foreach(object context in (Array)Read(Read(c.Hud,"m_Attributes"),"m_HUDContexts"))
            {
                if((string)Read(context,"m_ContextName")!="Default")continue;
                string[] order=(string[])Read(context,"m_AbilityOrder");int gap=Array.IndexOf(order,string.Empty);
                string[] updated=new string[gap<0?order.Length+1:order.Length];Array.Copy(order,updated,order.Length);
                if(gap<0)gap=order.Length;updated[gap]=ability.name;Write(context,"m_AbilityOrder",updated);attached=true;
            }
            if(!attached)throw new InvalidOperationException("Operator return default command context absent");
            Runtime.Log("RHODES_OPERATOR_RETURN_COMMAND_PREPARED unit="+c.Key+"; ability="+ability.name+"; hotkey=B; perUnitDataPrivate=True; batchSelectionSupported=True; nativeStopThenSimReturn=True; runtimeAcceptancePending=True");
        }

        static void VerifyOperatorReturn(Context c)
        {
            EntityTypeAttributes definition=Registry.GetEntityType(RhodesOperatorTransport.ReturnAbilityKey);
            AbilityAttributes shared=definition==null?null:definition.Get<AbilityAttributes>();
            AbilityAttributes local=Read(c.OperatorReturnAbility,"m_Attributes") as AbilityAttributes;
            if(shared==null||local==null||shared.AbilityType!=AbilityClass.Stop||local.AbilityType!=AbilityClass.Stop||
                local.Name!=RhodesOperatorTransport.ReturnAbilityKey||
                (KeyCode)Read(Read(c.OperatorReturnAbility,"m_View"),"Hotkey")!=KeyCode.B)
                throw new InvalidOperationException("Operator return command registration differs: "+c.Key);
            int abilities=0,entries=0;
            foreach(ScriptableObject ability in (Array)Read(c.Custom,"m_Abilities"))if(ability.name==local.Name)abilities++;
            foreach(object context in (Array)Read(Read(c.Hud,"m_Attributes"),"m_HUDContexts"))
                if((string)Read(context,"m_ContextName")=="Default")foreach(string key in (string[])Read(context,"m_AbilityOrder"))if(key==local.Name)entries++;
            if(abilities!=1||entries!=1)throw new InvalidOperationException("Operator return command must appear once: "+c.Key);
        }
    }
}
