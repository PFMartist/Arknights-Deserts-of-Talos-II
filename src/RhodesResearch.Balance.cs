using System;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesResearch
    {
        internal struct ResearchBalanceSpec
        {
            internal readonly string Key;
            internal readonly int CU,RU,Seconds;
            internal ResearchBalanceSpec(string key,int cu,int ru,int seconds)
            {Key=key;CU=cu;RU=ru;Seconds=seconds;}
        }
        // Formal first-pass prices and times for every currently implemented
        // study. Other planned technologies are not silently registered here.
        static ResearchBalanceSpec[] ResearchBalanceCatalog()
        {
            return new ResearchBalanceSpec[]{
                new ResearchBalanceSpec(LightAssembly,300,0,35),
                new ResearchBalanceSpec(SuppressionAssembly,350,85,50),
                new ResearchBalanceSpec(RailgunAssembly,400,135,50),
                new ResearchBalanceSpec(AirDefenceAssembly,200,100,35),
                new ResearchBalanceSpec(TiltAssembly,350,100,45),
                new ResearchBalanceSpec(ArtilleryAssembly,300,125,60),
                new ResearchBalanceSpec(Aviation1,450,150,45),
                new ResearchBalanceSpec(Gunnery1,900,350,80),
                new ResearchBalanceSpec(Gunnery2,750,325,70),
                new ResearchBalanceSpec(Gunnery3,750,375,80),
                new ResearchBalanceSpec(Population1,250,0,10),
                new ResearchBalanceSpec(Population2,310,0,10),
                new ResearchBalanceSpec(Population3,390,0,10),
                new ResearchBalanceSpec(Population4,490,0,10),
                new ResearchBalanceSpec(Population5,610,0,10),
                new ResearchBalanceSpec(ProductionLine2,500,100,45),
                new ResearchBalanceSpec(ReactorCapacity,250,100,30),
                new ResearchBalanceSpec(ReactorCapacity2,400,180,45),
                new ResearchBalanceSpec(ReactorCapacity3,600,300,60),
                new ResearchBalanceSpec(ReactorCapacity4,850,450,75),
                new ResearchBalanceSpec(ReactorCapacity5,1100,600,90),
                new ResearchBalanceSpec(EscortDamage1,500,75,30),
                new ResearchBalanceSpec(EscortDamage2,500,150,60),
                new ResearchBalanceSpec(EscortArmour1,200,75,15),
                new ResearchBalanceSpec(EscortArmour2,200,150,20),
                new ResearchBalanceSpec(TrackedArmour1,250,100,15),
                new ResearchBalanceSpec(TrackedArmour2,250,200,20),
                new ResearchBalanceSpec(TrackedArmour3,300,300,30),
                new ResearchBalanceSpec(RailgunDamage,280,165,50),
                new ResearchBalanceSpec(PredictiveFireControl,250,80,40),
                new ResearchBalanceSpec(AdvancedOriginiumSuppressant,300,100,40),
                new ResearchBalanceSpec(AirDefenceReload,250,100,35),
                new ResearchBalanceSpec(EngineeringRepair,300,75,35),
                new ResearchBalanceSpec(SurveyProbeDurability,200,50,25),
                new ResearchBalanceSpec(InterceptorService,300,150,40),
                new ResearchBalanceSpec(BomberProtection,400,200,50),
                new ResearchBalanceSpec(EscortBoost,200,125,50)};
        }
        static ResearchBalanceSpec BalanceFor(string key)
        {
            foreach(ResearchBalanceSpec spec in ResearchBalanceCatalog())if(spec.Key==key)return spec;
            throw new InvalidOperationException("Research has no formal price/time: "+key);
        }
        internal static int ResearchCU(string key){return BalanceFor(key).CU;}
        internal static int ResearchRU(string key){return BalanceFor(key).RU;}
        internal static float DurationSeconds(string key){return RhodesResearchProfile.Duration(BalanceFor(key).Seconds);}
        static void ApplyResearchBalance(object data,string key)
        {
            ResearchBalanceSpec spec=BalanceFor(key);
            Set(data,"m_Resource1Cost",spec.CU);Set(data,"m_Resource2Cost",spec.RU);Set(data,"m_ResearchTime",DurationSeconds(key));
        }
        static void VerifyResearchBalance(ResearchItemAttributes item,string key)
        {
            ResearchBalanceSpec spec=BalanceFor(key);
            if(item==null||item.ResearchTime!=Fixed64.FromConstFloat(DurationSeconds(key))||
                Convert.ToInt32(NativeRead.Get(item,"m_Resource1Cost"))!=spec.CU||Convert.ToInt32(NativeRead.Get(item,"m_Resource2Cost"))!=spec.RU)
                throw new InvalidOperationException("Formal research price/time mismatch: "+key);
        }
        static void VerifyResearchBalanceCatalog()
        {
            var unique=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach(ResearchBalanceSpec spec in ResearchBalanceCatalog())
                if(spec.CU<0||spec.RU<0||spec.Seconds<=0||!unique.Add(spec.Key))
                    throw new InvalidOperationException("Invalid formal research catalog");
            foreach(string key in Keys)if(!unique.Contains(key))throw new InvalidOperationException("Missing formal research balance: "+key);
        }
    }
}
