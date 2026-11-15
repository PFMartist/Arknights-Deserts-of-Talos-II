using System;
using BBI.Core.ComponentModel;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Simulation;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesResearch
    {
        public const string AdvancedOriginiumSuppressant="Research_RI_AdvancedOriginiumSuppressant";
        public static int AdvancedOriginiumSuppressantCU{get{return ResearchCU(AdvancedOriginiumSuppressant);}}
        public static int AdvancedOriginiumSuppressantRU{get{return ResearchRU(AdvancedOriginiumSuppressant);}}
        const string AdvancedOriginiumSuppressantIcon=TiltResearchIcon;
        const string AdvancedOriginiumSuppressantTitle="先进源石抑制剂";
        const string AdvancedOriginiumSuppressantShort="机载干员自动施法；机载技能冷却缩短20%";
        const string AdvancedOriginiumSuppressantDescription="解锁机载干员自动施法，机载干员技能冷却时间缩短20%。地面干员技能不变。机载干员施法会暴露搭载他的本机。已部署与后续生产的己方旋翼机均生效。";

        // The native Commander and the shared observer own completion. This
        // query only reads the initialized per-owner native-completion cache.
        public static bool HasAdvancedSuppressant(CommanderID owner)
        {
            if(!Ready||owner==CommanderID.None)return false;
            lock(Gate)
            {
                CommanderResearchState state;
                return CommanderStates.TryGetValue(owner,out state)&&state.Initialized&&
                    state.ClockSim!=null&&state.ClockCommander!=null&&state.Completed.Contains(AdvancedOriginiumSuppressant);
            }
        }

        static object BuildAdvancedSuppressantData()
        {
            object data=NewData("ResearchItemAttributesData");((BBI.Core.NamedObjectBase)data).InitializeName(AdvancedOriginiumSuppressant);
            Set(data,"m_ResearchType",ResearchType.Engineering);Set(data,"m_IconSpriteName",AdvancedOriginiumSuppressantIcon);
            Set(data,"m_LocalizedResearchTitleStringID",AdvancedOriginiumSuppressantTitle);
            Set(data,"m_LocalizedShortDescriptionStringID",AdvancedOriginiumSuppressantShort);
            Set(data,"m_LocalizedLongDescriptionStringID",AdvancedOriginiumSuppressantDescription);
            ApplyResearchBalance(data,AdvancedOriginiumSuppressant);Set(data,"m_ResearchDependencies",new string[]{TiltAssembly});
            Set(data,"m_ResearchVOCode",ResearchVoiceCode(AdvancedOriginiumSuppressant));Empty(data,"m_UnitTypeBuffs");Empty(data,"m_CommandsToRun");return data;
        }

        static void RegisterAdvancedSuppressant()
        {
            object data=BuildAdvancedSuppressantData();
            ScriptableObject research=Create(NativeType("ResearchItemAttributesAsset"),AdvancedOriginiumSuppressant);
            Set(research,"m_Attributes",data);Enable(research);Items.Add(AdvancedOriginiumSuppressant,research);AddDefinition(AdvancedOriginiumSuppressant,data);
            Runtime.Log("RHODES_ADVANCED_ORIGINIUM_SUPPRESSANT_RESEARCH key="+AdvancedOriginiumSuppressant+
                "; CU="+AdvancedOriginiumSuppressantCU+"; RU="+AdvancedOriginiumSuppressantRU+"; seconds="+DurationSeconds(AdvancedOriginiumSuppressant)+
                "; prerequisite="+TiltAssembly+"; nativePanel=Engineering; retainedEngineeringRows="+(CapabilityEngineeringChains().Length+1)+"; tiltHangarChainTier=2; commanderCompletedResearchOwnsAvailability=True; fakeAttributeBuffs=False; permanentNativeRouteChoice=True; runtimeAcceptancePending=True");
        }

        static void VerifyAdvancedSuppressantData(ResearchItemAttributes item,TechTreeAttributes tree)
        {
            string[] dependencies=item==null?null:(string[])NativeRead.Get(item,"m_ResearchDependencies");
            VerifyResearchBalance(item,AdvancedOriginiumSuppressant);
            if(item.Name!=AdvancedOriginiumSuppressant||item.TypeOfResearch!=ResearchType.Engineering||
                Convert.ToInt32(NativeRead.Get(item,"m_Resource1Cost"))!=AdvancedOriginiumSuppressantCU||
                Convert.ToInt32(NativeRead.Get(item,"m_Resource2Cost"))!=AdvancedOriginiumSuppressantRU||
                dependencies==null||dependencies.Length!=1||dependencies[0]!=TiltAssembly||item.CommandsToRun.Length!=0||
                ((Array)NativeRead.Get(item,"m_UnitTypeBuffs")).Length!=0||
                (string)NativeRead.Get(item,"m_IconSpriteName")!=AdvancedOriginiumSuppressantIcon||
                (string)NativeRead.Get(item,"m_LocalizedResearchTitleStringID")!=AdvancedOriginiumSuppressantTitle||
                (string)NativeRead.Get(item,"m_LocalizedShortDescriptionStringID")!=AdvancedOriginiumSuppressantShort||
                (string)NativeRead.Get(item,"m_LocalizedLongDescriptionStringID")!=AdvancedOriginiumSuppressantDescription||
                (string)NativeRead.Get(item,"m_ResearchVOCode")!=ResearchVoiceCode(AdvancedOriginiumSuppressant))
                throw new InvalidOperationException("Advanced Originium Suppressant research data contract changed");
            // Its only prerequisite is the preceding hangar tier. Keeping both
            // on the hangar's private cursor adds no hidden prerequisite and
            // preserves every existing row and the route's separate cursor.
            if(tree==null||EngineeringAppearances(tree,AdvancedOriginiumSuppressant)!=1||UpgradeAppearances(tree,AdvancedOriginiumSuppressant)!=0||
                tree.TechTrees.Length!=CapabilityEngineeringChains().Length+1||tree.TechTrees[4].Tiers.Length!=2||
                tree.TechTrees[4].Tiers[0].ResearchItems.Length!=1||tree.TechTrees[4].Tiers[0].ResearchItems[0]!=TiltAssembly||
                tree.TechTrees[4].Tiers[1].ResearchItems.Length!=1||tree.TechTrees[4].Tiers[1].ResearchItems[0]!=AdvancedOriginiumSuppressant||
                Array.IndexOf(Keys,AdvancedOriginiumSuppressant)<0||Array.IndexOf(ExclusiveRouteKeys,AdvancedOriginiumSuppressant)>=0)
                throw new InvalidOperationException("Advanced Originium Suppressant must follow only the hangar, appear once in Engineering, and remain independent of the carrier route");
        }

        static void VerifyAdvancedSuppressant()
        {
            VerifyAdvancedSuppressantData(Registry.GetEntityType(AdvancedOriginiumSuppressant).Get<ResearchItemAttributes>(),
                (TechTreeAttributes)NativeRead.Get(Tree,"m_Attributes"));
        }
    }
}
