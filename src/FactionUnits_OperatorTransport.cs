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
        static void PrepareOperatorTransport(Context c)
        {
            if(c.Spec.Key!=TiltStrikeKey)throw new InvalidOperationException("Operator transport requires private U08");
            if(RhodesOperatorRoster.Count>RhodesOperatorTransport.Capacity)throw new InvalidOperationException("Operator catalog exceeds the aircraft cargo budget");
            // Native runway position code supports indices 0..2. More logical
            // passengers may queue, but never allocate an unsupported fourth door.
            int parallelSlots=Math.Min(RhodesOperatorRoster.Count,3);
            UnitAssetContainer carrier=FindSource("C_Carrier_MP");
            ScriptableObject sourceHangar=Read(carrier,"m_UnitHangar") as ScriptableObject;
            ScriptableObject hangar=CloneAsset(c,sourceHangar,TiltStrikeKey+"_OperatorHangar");
            Write(c.Custom,"m_UnitHangar",hangar);object hangarData=Read(hangar,"m_Attributes");
            Array original=(Array)Read(hangarData,"m_StorableEntities");Type bayType=original.GetType().GetElementType();
            Array bays=Array.CreateInstance(bayType,RhodesOperatorRoster.Count);
            for(int index=0;index<RhodesOperatorRoster.Count;index++)
            {
                RhodesOperatorDefinition identity=RhodesOperatorRoster.At(index);
                EntityTypeAttributes hero=Registry.GetEntityType(identity.TypeKey);
                UnitAttributes passenger=hero==null?null:hero.Get<UnitAttributes>();
                if(passenger==null||(passenger.Class&UnitClass.Air)!=0)throw new InvalidOperationException("Register each ground operator before U08 transport");
                object bay=Activator.CreateInstance(bayType,true);
                Write(bay,"m_Name","EF_U08_OperatorBay_"+identity.EnglishName);Write(bay,"m_EntityType",identity.TypeKey);
                Write(bay,"m_Class",passenger.Class);Write(bay,"m_UsesStrictClassMatching",true);
                // MaxCount is the identity limit. Native deployment queues
                // merge equal UnitClass values, so the first shared-class bay
                // must expose enough simultaneous launch/receive positions.
                Write(bay,"m_MaxCount",1);Write(bay,"m_SlotCount",parallelSlots);
                Write(bay,"m_UndockPresCtrlBone",string.Empty);Write(bay,"m_DockPresCtrlBone",string.Empty);
                float offset=-(parallelSlots-1)*1f;
                Write(bay,"m_UndockXOffsetPos",offset);Write(bay,"m_UndockYOffsetPos",8f);
                // Native DockFormationPattern uses Forward*X + Left*Y,
                // unlike the runway's Back*Y + Right*X convention.
                Write(bay,"m_DockReceivingXOffset",-8f);Write(bay,"m_DockReceivingYOffset",0f);
                Write(bay,"m_UndockSlotXSeperationOffset",2f);Write(bay,"m_UndockDegreesOffsetAngle",180f);
                Write(bay,"m_UndockTotalSeconds",1f);Write(bay,"m_UndockTotalSecondsPerSlot",1f);
                Write(bay,"m_UndockSlotStaggerSeconds",.1f);Write(bay,"m_DockSlotStaggerSeconds",.1f);
                Write(bay,"m_UndockLiftTime",0f);Write(bay,"m_UndockSpeed",2f);
                Write(bay,"m_DockBringInAnimationSeconds",0f);Write(bay,"m_DoorAnimationSeconds",0f);
                Write(bay,"m_MinDockCoolingSeconds",30f);Write(bay,"m_MaxDamageCoolingSeconds",0f);Write(bay,"m_MaxPayloadCoolingSeconds",0f);
                bays.SetValue(bay,index);
            }
            Write(hangarData,"m_StorableEntities",bays);
            foreach(string field in new string[]{"m_UnitDockingTriggers","m_HangarDockingTriggers","m_BoneResetTriggers"})ClearArray(hangarData,field);
            Write(hangarData,"m_AlignmentTime",.1f);Write(hangarData,"m_ApproachTime",1f);
            object core=Read(c.Core,"m_Attributes");Write(core,"m_NumProductionQueues",1);Write(core,"m_ProductionQueueDepth",1);Write(core,"m_ShowProductionQueues",true);
            ScriptableObject productSource=null,stopSource=null,recallSource=null;
            foreach(ScriptableObject ability in (Array)Read(carrier,"m_Abilities"))
            {
                AbilityAttributes data=ability==null?null:Read(ability,"m_Attributes") as AbilityAttributes;
                if(data!=null&&data.AbilityType==AbilityClass.ProduceUnit&&data.ProduceUnit.UnitTypeToProduce==CustomUnits.ControlKey)productSource=ability;
                if(ability!=null&&ability.name=="Ability_C_DockAll")recallSource=ability;
            }
            foreach(ScriptableObject ability in (Array)Read(FindSource("C_Escort_MP"),"m_Abilities"))if(ability!=null&&ability.name=="Ability_Common_StopAbility")stopSource=ability;
            if(productSource==null||stopSource==null)throw new InvalidOperationException("Native operator transport command templates missing");
            List<ScriptableObject> products=new List<ScriptableObject>();
            for(int index=0;index<RhodesOperatorRoster.Count;index++)
            {
                RhodesOperatorDefinition identity=RhodesOperatorRoster.At(index);
                ScriptableObject produce=OperatorAbility(c,productSource,identity.ProduceAbilityKey,"编入："+identity.DisplayName,(KeyCode)identity.ProductionHotkey,
                    "将干员编入本机机舱。同一指挥官只能同时拥有一位同名干员，包括机舱内、已部署和编入队列中的干员；不同干员可共同搭载。");
                object production=Read(produce,"m_Attributes");Write(Read(production,"m_ProduceUnit"),"m_UnitTypeToProduce",identity.TypeKey);Write(Read(production,"m_ProduceUnit"),"m_BypassQueue",false);
                Write(Read(production,"m_Cost"),"m_Resource1Cost",0);Write(Read(production,"m_Cost"),"m_Resource2Cost",0);
                UnitHUDAttributes heroHud=Registry.GetEntityType(identity.TypeKey).Get<UnitHUDAttributes>();
                if(heroHud!=null&&heroHud.IconSettings!=null&&!string.IsNullOrEmpty(heroHud.IconSettings.UnitProductionIconName))Write(Read(produce,"m_View"),"IconName",heroHud.IconSettings.UnitProductionIconName);
                products.Add(produce);
            }
            ScriptableObject unload=OperatorAbility(c,stopSource,RhodesOperatorTransport.UnloadAbilityKey,"部署全部干员",KeyCode.N,"悬停部署机舱内全部就绪干员，下方需有可通行的安全地面。无需降落，可同时部署多人；整备中的干员继续留舱。");
            ScriptableObject recall=OperatorAbility(c,stopSource,RhodesOperatorTransport.RecallAbilityKey,"召回全部干员",KeyCode.B,"旋翼机主动前往本机已部署的干员附近，悬停接回所有可召回干员。无需降落，可同时接回多人；新的移动或攻击命令会中止接驳。回舱后需整备30秒。");
            if(recallSource!=null){object icon=Read(Read(recallSource,"m_View"),"IconName");Write(Read(unload,"m_View"),"IconName",icon);Write(Read(recall,"m_View"),"IconName",icon);}
            List<ScriptableObject> all=new List<ScriptableObject>();foreach(ScriptableObject ability in (Array)Read(c.Custom,"m_Abilities"))all.Add(ability);
            all.AddRange(products);all.Add(unload);all.Add(recall);PrepareEmbarkedSkills(c,all);SetAbilities(c,all);
            string[] productKeys=new string[products.Count];for(int index=0;index<products.Count;index++)productKeys[index]=products[index].name;
            Runtime.Log("RHODES_U08_HUD_LAYOUT "+PrepareOperatorTransportHud(Read(c.Hud,"m_Attributes"),productKeys));
            CloneClosure(c);foreach(ScriptableObject asset in c.Owned)RemapAsset(c,asset);
            Runtime.Log("RHODES_OPERATOR_TRANSPORT_PREPARED carrier=U08; typedIdentities="+RhodesOperatorRoster.Count+"; identityMaxCount=1; sharedClassDeploymentSlots="+parallelSlots+"; logicalCargoBudget=4; livingIdentityLimit=1perOperatorTypePerCommanderAcrossAllStates; produceLaevatain=J; produceGilberta=K; unloadAll=N; recallAll=B; nativeQueueAndReservedSlots=True; worldTestPending=True");
        }

        static ScriptableObject OperatorAbility(Context c,ScriptableObject source,string key,string title,KeyCode hotkey,string description)
        {
            if(Registry.GetEntityType(key)!=null)throw new InvalidOperationException("Operator command key already registered "+key);
            bool recorded=false;foreach(SourceRecord record in c.Sources)if(object.ReferenceEquals(record.Asset,source))recorded=true;
            if(!recorded)c.Sources.Add(new SourceRecord{Asset=source,Fingerprint=Fingerprint(source)});
            ScriptableObject clone=RhodesAssetClone59.CreateEmpty(source.GetType());Dictionary<object,object> copied=new Dictionary<object,object>(new IdentityComparer());
            foreach(FieldInfo field in Fields(source.GetType()))if(field.Name!="mDataCache")field.SetValue(clone,CopyData(field.GetValue(source),copied));
            clone.name=key;clone.hideFlags=HideFlags.HideAndDontSave;c.Owned.Add(clone);RemapAsset(c,clone);
            object data=Read(clone,"m_Attributes"),view=Read(clone,"m_View");((NamedObjectBase)data).InitializeName(key);((NamedObjectBase)view).InitializeName(key);
            ClearArray(Read(data,"m_ResearchDeps"),"m_DependentResearch");object removed=Read(data,"m_StartsRemovedIn");Write(data,"m_StartsRemovedIn",Enum.ToObject(removed.GetType(),0));
            Write(data,"m_CooldownTimeSecs",0f);Write(data,"m_WarmupTimeSecs",0f);Write(data,"m_SharedCooldownChannel",0);Write(data,"m_IsToggleable",false);
            Write(Read(data,"m_Autocast"),"m_AutocastEnabled",false);Write(Read(data,"m_Autocast"),"m_AutocastEnabledOnSpawn",false);
            Write(view,"LocalizedTitleStringID",title);Write(view,"LocalizedShortDescriptionStringID",title);Write(view,"LocalizedLongDescriptionStringID",description);
            Write(view,"Hotkey",hotkey);Write(view,"ToggledHotkey",KeyCode.None);Write(view,"BindingIsExclusive",false);return clone;
        }

        static void RegisterOperatorTransportAbilities(Context c)
        {
            foreach(ScriptableObject ability in (Array)Read(c.Custom,"m_Abilities"))
            {
                if(!RhodesOperatorTransport.IsAbility(ability.name))continue;
                if(Registry.GetEntityType(ability.name)!=null)throw new InvalidOperationException("Operator command duplicate "+ability.name);
                EntityTypeAttributes definition=new EntityTypeAttributes(ability.name);object data=null;IEnumerable many=ability as IEnumerable;
                if(many==null){data=Read(ability,"Data");many=data as IEnumerable;}
                if(many!=null){foreach(object item in many)if(item!=null&&!definition.Add(item))throw new InvalidOperationException("Duplicate operator command datum");}
                else if(data==null||!definition.Add(data))throw new InvalidOperationException("Operator command has no native datum");
                Registry.Add(definition);
            }
            RegisterEmbarkedSkills(c);
        }

        static void VerifyOperatorTransport(Context c)
        {
            UnitAttributes core=c.Definition.Get<UnitAttributes>();UnitHangarAttributes hangar=c.Definition.Get<UnitHangarAttributes>();
            if(core.NumProductionQueues!=1||core.ProductionQueueDepth!=1||!core.ShowProductionQueues||hangar==null||hangar.HangarBays.Length!=RhodesOperatorRoster.Count)
                throw new InvalidOperationException("U08 native operator production/hangar missing");
            for(int index=0;index<RhodesOperatorRoster.Count;index++)
            {
                HangarBay bay=hangar.HangarBays[index];RhodesOperatorDefinition identity=RhodesOperatorRoster.At(index);
                if(bay.EntityType!=identity.TypeKey||bay.MaxCount!=1||bay.SlotCount!=Math.Min(RhodesOperatorRoster.Count,3)||!bay.UsesStrictClassMatching||
                    (bay.HoldsClass&UnitClass.Air)!=0||bay.MinDockCoolingSeconds!=BBI.Core.Utility.FixedPoint.Fixed64.FromInt(30))
                    throw new InvalidOperationException("U08 exact typed ground passenger contract differs");
            }
            int count=0;foreach(ScriptableObject ability in (Array)Read(c.Custom,"m_Abilities"))
            {
                if(!RhodesOperatorTransport.IsAbility(ability.name))continue;count++;
                EntityTypeAttributes registered=Registry.GetEntityType(ability.name);AbilityAttributes attributes=registered==null?null:registered.Get<AbilityAttributes>();
                if(attributes==null||!object.ReferenceEquals(attributes,Read(ability,"m_Attributes")))throw new InvalidOperationException("Operator command registry identity differs");
                RhodesOperatorDefinition identity;
                if(RhodesOperatorRoster.TryGetProduction(ability.name,out identity))
                {
                    if(attributes.AbilityType!=AbilityClass.ProduceUnit||attributes.ProduceUnit.UnitTypeToProduce!=identity.TypeKey||attributes.ProduceUnit.BypassQueue)
                        throw new InvalidOperationException("Operator must be natively queued into its exact tracked bay");
                }
                else if(attributes.AbilityType!=AbilityClass.Stop)throw new InvalidOperationException("Passenger action accidentally uses aviation sortie logic");
            }
            if(count!=RhodesOperatorRoster.Count+2)throw new InvalidOperationException("U08 operator command count differs");
            VerifyEmbarkedSkills(c);
        }
    }
}
