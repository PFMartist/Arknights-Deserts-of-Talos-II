using System;
using BBI.Core;
using BBI.Core.ComponentModel;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Simulation;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesResearch
    {
        public const string PredictiveFireControl="Research_RI_U09_PredictiveFireControl";
        public static int PredictiveFireControlCU{get{return ResearchCU(PredictiveFireControl);}}
        public static int PredictiveFireControlRU{get{return ResearchRU(PredictiveFireControl);}}
        const string PredictiveFireControlIcon=ArtilleryResearchIcon;

        // The native Commander owns completion. The existing research observer
        // and ResearchEvent fill this per-owner cache; no global researched bit
        // or independently editable unit flag is introduced here.
        public static bool HasPredictiveFireControl(CommanderID owner)
        {
            if(!Ready||owner==CommanderID.None)return false;
            lock(Gate)
            {
                CommanderResearchState state;
                return CommanderStates.TryGetValue(owner,out state)&&state.Initialized&&
                    state.ClockSim!=null&&state.ClockCommander!=null&&state.Completed.Contains(PredictiveFireControl);
            }
        }

        static object BuildPredictiveFireControlData()
        {
            object data=NewData("ResearchItemAttributesData");((BBI.Core.NamedObjectBase)data).InitializeName(PredictiveFireControl);
            Set(data,"m_ResearchType",ResearchType.Upgrade);
            Set(data,"m_IconSpriteName",PredictiveFireControlIcon);
            Set(data,"m_LocalizedResearchTitleStringID","预测火控");
            Set(data,"m_LocalizedShortDescriptionStringID","自行火炮对移动目标进行预瞄");
            Set(data,"m_LocalizedLongDescriptionStringID","为己方自行火炮加装预测火控，根据目标当前运动估算落点，支持普通炮击与两种手动弹幕。已部署与后续生产的自行火炮均生效。不会使炮弹在飞行中追踪目标；对手动指定的固定地面位置仍按原落点射击。");
            ApplyResearchBalance(data,PredictiveFireControl);
            Set(data,"m_ResearchDependencies",new string[]{ArtilleryAssembly});Set(data,"m_ResearchVOCode",ResearchVoiceCode(PredictiveFireControl));
            Empty(data,"m_UnitTypeBuffs");Empty(data,"m_CommandsToRun");return data;
        }

        static void RegisterPredictiveFireControl()
        {
            object data=BuildPredictiveFireControlData();
            ScriptableObject research=Create(NativeType("ResearchItemAttributesAsset"),PredictiveFireControl);
            Set(research,"m_Attributes",data);Enable(research);Items.Add(PredictiveFireControl,research);AddDefinition(PredictiveFireControl,data);
            Runtime.Log("RHODES_U09_PREDICTIVE_FIRE_CONTROL_RESEARCH key="+PredictiveFireControl+"; CU="+PredictiveFireControlCU+
                "; RU="+PredictiveFireControlRU+"; seconds="+DurationSeconds(PredictiveFireControl)+"; prerequisite="+ArtilleryAssembly+
                "; nativePanel=Upgrades; commanderCompletedResearchOwnsAvailability=True; fakeAttributeBuffs=False; originalFactionTreesUnchanged=True; runtimeAcceptancePending=True");
        }

        static void VerifyPredictiveFireControl()
        {
            ResearchItemAttributes item=Registry.GetEntityType(PredictiveFireControl).Get<ResearchItemAttributes>();
            string[] dependencies=(string[])NativeRead.Get(item,"m_ResearchDependencies");
            VerifyResearchBalance(item,PredictiveFireControl);
            if(item.TypeOfResearch!=ResearchType.Upgrade||
                Convert.ToInt32(NativeRead.Get(item,"m_Resource1Cost"))!=PredictiveFireControlCU||
                Convert.ToInt32(NativeRead.Get(item,"m_Resource2Cost"))!=PredictiveFireControlRU||
                dependencies.Length!=1||dependencies[0]!=ArtilleryAssembly||item.CommandsToRun.Length!=0||
                ((Array)NativeRead.Get(item,"m_UnitTypeBuffs")).Length!=0||
                (string)NativeRead.Get(item,"m_IconSpriteName")!=PredictiveFireControlIcon||
                (string)NativeRead.Get(item,"m_ResearchVOCode")!=ResearchVoiceCode(PredictiveFireControl))
                throw new InvalidOperationException("U09 predictive fire-control research data contract changed");
            TechTreeAttributes tree=(TechTreeAttributes)NativeRead.Get(Tree,"m_Attributes");
            if(UpgradeAppearances(tree,PredictiveFireControl)!=1||EngineeringAppearances(tree,PredictiveFireControl)!=0||
                Array.IndexOf(ExclusiveRouteKeys,PredictiveFireControl)>=0)
                throw new InvalidOperationException("U09 predictive fire control must appear once in Upgrades and remain independent of the carrier route");
        }
    }
}
