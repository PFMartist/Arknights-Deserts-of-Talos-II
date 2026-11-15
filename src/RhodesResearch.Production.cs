using System;
using System.Collections.Generic;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesResearch
    {
        public const string ProductionLine2="Research_RI_P01_ParallelProduction";
        const int ProductionQueueAttributeID=65544; // Unit_NumProductionQueues

        static string[] WithProductionResearchKey(string[] original)
        {
            if(!Runtime.SkirmishMode)return original;
            string[] result=new string[original.Length+1];Array.Copy(original,result,original.Length);
            result[original.Length]=ProductionLine2;return result;
        }

        static void RegisterProductionResearch()
        {
            if(!Runtime.SkirmishMode)return;
            Add(ProductionLine2,"生产线扩建",ResearchType.Engineering,new string[]{LightAssembly});
            ScriptableObject research=Items[ProductionLine2];object data=NativeRead.Get(research,"m_Attributes");
            Set(data,"m_LocalizedShortDescriptionStringID","本舰并行生产队列 1 → 2");
            Set(data,"m_LocalizedLongDescriptionStringID","本舰生产线增至两条，可同时生产相同或不同单位。单件造价与生产用时不变；科研和旋翼机的干员编入队列不受影响。");

            ScriptableObject buff=Create(NativeType("BuffSetAttributesAsset"),ProductionLine2+"_QueueBuff");
            object set=NewData("AttributeBuffSetData"),entry=NewData("AttributeBuffData");
            Set(entry,"m_Name",string.Empty);SetEnum(entry,"m_AttributeID",ProductionQueueAttributeID);
            SetEnum(entry,"m_Mode",0);Set(entry,"m_Value",1);Set(set,"m_Buffs",ArrayOf(entry));
            Set(buff,"m_Attributes",set);Enable(buff);

            object target=NewData("UnitTypeBuffData");
            Set(target,"m_UnitTypePrefix",RhodesCarrierUnits.UnitKey);Set(target,"m_UseAsPrefix",false);
            SetEnum(target,"m_UnitClass",0);SetEnum(target,"m_ClassOperator",1);Set(target,"m_BuffSet",buff);
            Set(data,"m_UnitTypeBuffs",ArrayOf(target));Enable(research);
            Runtime.Log("RHODES_PRODUCTION_RESEARCH_REGISTERED key="+ProductionLine2+"; CU="+ResearchCU(ProductionLine2)+"; RU="+ResearchRU(ProductionLine2)+"; seconds="+DurationSeconds(ProductionLine2)+"; requires="+LightAssembly+
                "; nativeUnitAttribute=65544; additiveQueues=1; privateCarrierOnly=True; nativeBuffChangedResizesExistingQueues=True; researchQueueUnchanged=True; tiltrotorQueueUnchanged=True; perFrameWrites=False; gameplayAcceptancePending=True");
        }

        static void VerifyProductionResearch()
        {
            if(!Runtime.SkirmishMode)return;
            ResearchItemAttributes research=Registry.GetEntityType(ProductionLine2).Get<ResearchItemAttributes>();
            VerifyResearchBalance(research,ProductionLine2);
            if(research.Name!=ProductionLine2)throw new InvalidOperationException("Private second production line research identity differs");
            string[] dependencies=NativeRead.Get(research,"m_ResearchDependencies") as string[];
            if(dependencies==null||dependencies.Length!=1||dependencies[0]!=LightAssembly)
                throw new InvalidOperationException("Second production line must require light assembly");
            Array targets=NativeRead.Get(research,"m_UnitTypeBuffs") as Array;
            if(targets==null||targets.Length!=1)throw new InvalidOperationException("Second production line has unexpected buff scope");
            object target=targets.GetValue(0);
            if((string)NativeRead.Get(target,"m_UnitTypePrefix")!=RhodesCarrierUnits.UnitKey||(bool)NativeRead.Get(target,"m_UseAsPrefix"))
                throw new InvalidOperationException("Second production line must target only the exact Rhodes carrier");
            Array entries=NativeRead.Get(NativeRead.Get(NativeRead.Get(target,"m_BuffSet"),"m_Attributes"),"m_Buffs") as Array;
            if(entries==null||entries.Length!=1)throw new InvalidOperationException("Second production line must have one additive native attribute");
            object entry=entries.GetValue(0);
            if(Convert.ToInt32(NativeRead.Get(entry,"m_AttributeID"))!=ProductionQueueAttributeID||
                Convert.ToInt32(NativeRead.Get(entry,"m_Mode"))!=0||Convert.ToInt32(NativeRead.Get(entry,"m_Value"))!=1||
                (string)NativeRead.Get(entry,"m_Name")!=string.Empty)
                throw new InvalidOperationException("Second production line must add exactly one queue");
            TechTreeAttributes tree=(TechTreeAttributes)NativeRead.Get(Tree,"m_Attributes");
            if(research.TypeOfResearch!=ResearchType.Engineering||EngineeringAppearances(tree,ProductionLine2)!=1||UpgradeAppearances(tree,ProductionLine2)!=0||Array.IndexOf(ExclusiveRouteKeys,ProductionLine2)>=0)
                throw new InvalidOperationException("Second production line must appear once and remain independent of the carrier route");
        }
    }
}
