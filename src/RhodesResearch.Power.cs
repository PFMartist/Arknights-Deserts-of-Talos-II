using System;
using System.Collections.Generic;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesResearch
    {
        public const string ReactorCapacity="Research_RI_ReactorCapacity";
        public const string ReactorCapacity2="Research_RI_ReactorCapacity_2",ReactorCapacity3="Research_RI_ReactorCapacity_3";
        public const string ReactorCapacity4="Research_RI_ReactorCapacity_4",ReactorCapacity5="Research_RI_ReactorCapacity_5";

        static string[] ReactorResearchKeys()
        {return new string[]{ReactorCapacity,ReactorCapacity2,ReactorCapacity3,ReactorCapacity4,ReactorCapacity5};}
        internal static bool IsReactorResearch(string key)
        {return Array.IndexOf(ReactorResearchKeys(),key)>=0;}
        public static int CompletedReactorLevel(BBI.Game.Simulation.CommanderID owner)
        {
            int level=0;
            foreach(string key in ReactorResearchKeys())
            {if(!IsCompleted(owner,key))break;level++;}
            return level;
        }
        static string[] ReactorDependencies(int index)
        {
            if(index==0)return new string[]{LightAssembly};
            string previous=ReactorResearchKeys()[index-1];
            return index==2?new string[]{previous,ProductionLine2}:new string[]{previous};
        }

        static void RegisterReactorResearch()
        {
            if(!Runtime.SkirmishMode)return;
            string[] keys=ReactorResearchKeys();string[] numerals={"I","II","III","IV","V"};
            for(int index=0;index<keys.Length;index++)
            {
                int before=RhodesPowerProfile.CapacityForResearchLevel(index),after=RhodesPowerProfile.CapacityForResearchLevel(index+1);
                Add(keys[index],"反应堆扩容 "+numerals[index],ResearchType.Upgrade,ReactorDependencies(index));
                object data=NativeRead.Get(Items[keys[index]],"m_Attributes");
                Set(data,"m_LocalizedShortDescriptionStringID","可分配功率 "+before+" → "+after);
                Set(data,"m_LocalizedLongDescriptionStringID","本舰总功率增加"+(after-before)+"点，达到"+after+"点。供能分为武备、损管、机动、探测火控四系，每系最高5档；维持1至5档分别累计占用1、3、6、10、15点功率，并产生等量热负荷。储备功率不产热，散热极限仍为19；超出极限的分配适合短时过载。");
                Enable(Items[keys[index]]);
            }
        }
        static void VerifyReactorResearch()
        {
            foreach(string removed in new string[]{CIWS2,Repair2,Aviation2})
                if(Items.ContainsKey(removed)||Array.IndexOf(Keys,removed)>=0||Registry.GetEntityType(removed)!=null)
                    throw new InvalidOperationException("Removed performance research remains registered: "+removed);
            if(!Runtime.SkirmishMode)return;
            TechTreeAttributes tree=(TechTreeAttributes)NativeRead.Get(Tree,"m_Attributes");string[] keys=ReactorResearchKeys();
            for(int index=0;index<keys.Length;index++)
            {
                string key=keys[index];ResearchItemAttributes item=Registry.GetEntityType(key).Get<ResearchItemAttributes>();
                VerifyResearchBalance(item,key);string[] deps=NativeRead.Get(item,"m_ResearchDependencies") as string[];
                string[] expected=ReactorDependencies(index);
                if(item.TypeOfResearch!=ResearchType.Upgrade||deps==null||deps.Length!=expected.Length||
                    Array.IndexOf(ExclusiveRouteKeys,key)>=0||EngineeringAppearances(tree,key)!=0||UpgradeAppearances(tree,key)!=1)
                    throw new InvalidOperationException("Reactor capacity research category/dependencies differ: "+key);
                for(int dep=0;dep<expected.Length;dep++)if(deps[dep]!=expected[dep])
                    throw new InvalidOperationException("Reactor capacity prerequisite differs: "+key);
            }
        }
    }
}
