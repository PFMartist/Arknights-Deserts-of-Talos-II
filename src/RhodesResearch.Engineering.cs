using System;
using System.Collections.Generic;
using BBI.Game.Data;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesResearch
    {
        // Native Engineering advances one cursor per TechTree, so independent
        // capabilities require independent trees. A same-tier list is a choice.
        static string[][] CapabilityEngineeringChains()
        {
            List<string[]> chains=new List<string[]>();
            // This is already the exact dependency chain. Sharing its cursor
            // adds no new prerequisite and keeps the Coalition's eight rows.
            chains.Add(new string[]{LightAssembly,SuppressionAssembly,ArtilleryAssembly});
            chains.Add(new string[]{RailgunAssembly});
            chains.Add(new string[]{AirDefenceAssembly});
            // Exactly the declared prerequisite chain; no extra row or shared
            // route cursor, and no unrelated unlock can consume this tier.
            chains.Add(new string[]{TiltAssembly,AdvancedOriginiumSuppressant});
            if(Runtime.SkirmishMode)
            {
                chains.Add(PopulationResearchKeys());
                chains.Add(new string[]{ProductionLine2});
            }
            chains.Add(new string[]{EscortBoost});
            return chains.ToArray();
        }
        static Array BuildCapabilityEngineeringBranches(object route)
        {
            string[][] chains=CapabilityEngineeringChains();
            string[] titles={"地面车辆装配","轨道炮车装配","防空导弹车装配","倾转旋翼机机库","舰队扩编","生产线扩建","轻突战术加速"};
            if(!Runtime.SkirmishMode)titles=new string[]{"地面车辆装配","轨道炮车装配","防空导弹车装配","倾转旋翼机机库","轻突战术加速"};
            Array result=Array.CreateInstance(route.GetType(),chains.Length+1);result.SetValue(route,0);
            for(int i=0;i<chains.Length;i++)
            {
                object branch=NewGameData("TechTreeData");Set(branch,"m_TechTreeName",titles[i]);
                Set(branch,"m_IconSpriteName",ResearchIconFor(chains[i][0]));
                Array tiers=Array.CreateInstance(NewGameData("TechTreeTierData").GetType(),chains[i].Length);
                for(int level=0;level<chains[i].Length;level++)
                {
                    object tier=NewGameData("TechTreeTierData");Set(tier,"m_TierName",titles[i]);
                    Set(tier,"m_ResearchItems",new string[]{chains[i][level]});tiers.SetValue(tier,level);
                }
                Set(branch,"m_TechTiers",tiers);
                Set(branch,"m_Upgrades",Array.CreateInstance(NewGameData("TechUpgradeData").GetType(),0));
                result.SetValue(branch,i+1);
            }
            return result;
        }
        static int EngineeringAppearances(TechTreeAttributes tree,string key)
        {
            int count=0;foreach(TechTree branch in tree.TechTrees)
                foreach(TechTreeTier tier in branch.Tiers)foreach(string item in tier.ResearchItems)if(item==key)count++;
            return count;
        }
        static int UpgradeAppearances(TechTreeAttributes tree,string key)
        {
            int count=0;foreach(object branch in tree.TechTrees)
                foreach(object item in (Array)NativeRead.Get(branch,"m_Upgrades"))if((string)NativeRead.Get(item,"m_ResearchItem")==key)count++;
            return count;
        }
        static void VerifyCapabilityEngineeringLayout(TechTreeAttributes tree)
        {
            string[][] chains=CapabilityEngineeringChains();
            if(tree.TechTrees.Length!=chains.Length+1||tree.TechTrees[0].Tiers.Length!=2)
                throw new InvalidOperationException("Rhodes Engineering tree/cursor count differs from private capability layout");
            string[] choice=tree.TechTrees[0].Tiers[0].ResearchItems;
            if(choice.Length!=2||choice[0]!=Aviation1||choice[1]!=Gunnery1)
                throw new InvalidOperationException("Rhodes native route choice tier changed");
            string[] aft=tree.TechTrees[0].Tiers[1].ResearchItems;
            if(aft.Length!=1||aft[0]!=Gunnery2||EngineeringAppearances(tree,Gunnery2)!=1||UpgradeAppearances(tree,Gunnery2)!=0)
                throw new InvalidOperationException("Aft main station must follow the permanent route in Engineering");
            for(int i=0;i<chains.Length;i++)
            {
                TechTree branch=tree.TechTrees[i+1];
                if(branch.Tiers.Length!=chains[i].Length||((Array)NativeRead.Get(branch,"m_Upgrades")).Length!=0)
                    throw new InvalidOperationException("Rhodes capability branch shape changed");
                for(int level=0;level<chains[i].Length;level++)
                {
                    string key=chains[i][level];string[] items=branch.Tiers[level].ResearchItems;
                    if(items.Length!=1||items[0]!=key||EngineeringAppearances(tree,key)!=1||UpgradeAppearances(tree,key)!=0||
                        Registry.GetEntityType(key).Get<ResearchItemAttributes>().TypeOfResearch!=ResearchType.Engineering)
                        throw new InvalidOperationException("Rhodes capability must appear once in Engineering only: "+key);
                }
            }
        }
    }
}
