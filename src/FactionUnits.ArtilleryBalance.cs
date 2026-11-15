using System;
using System.Collections.Generic;
using BBI.Core.Data;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Unity.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class FactionUnits
    {
        internal const int ArtilleryCostCU=220,ArtilleryCostRU=80,ArtilleryPopulation=3;
        internal const float ArtilleryProductionSeconds=24f;
        const int ArtilleryHealth=600,ArtilleryArmour=2,ArtilleryDamage=160;
        const int ArtilleryReloadMS=5500,ArtilleryBurstMS=250,ArtilleryWindupMS=250;
        static readonly string[] MediumArtilleryModes={ArtilleryAuto,ArtilleryBarrage,ArtilleryPrecise};

        static void VerifyArtilleryDonor(UnitAttributes native)
        {
            if(native==null||native.MaxHealth!=2500||native.Armour!=5||native.Resource1Cost!=450||native.Resource2Cost!=250||
                native.PopCapCost!=6||native.ProductionTime!=Fixed64.FromInt(70)||(native.Class&UnitClass.XLarge)==0)
                throw new InvalidOperationException("Native artillery donor differs from audited build12339551");
        }
        static void ConfigureMediumArtilleryUnit(object unit,object movement)
        {
            Write(unit,"m_Class",UnitClass.Ground|UnitClass.Medium|UnitClass.Ranged);
            Write(unit,"m_MaxHealth",ArtilleryHealth);Write(unit,"m_Armour",ArtilleryArmour);
            Write(unit,"m_ExperienceValue",350);Write(unit,"m_AggroRange",1500);Write(unit,"m_LeadPriority",3);
            object threat=Read(unit,"m_ThreatData");Write(threat,"BaseThreat",3);Write(threat,"Tier",1);
            object dynamics=Read(movement,"m_Dynamics");
            Write(dynamics,"m_MaxSpeed",70f);Write(dynamics,"m_MinCruiseSpeed",25f);
            Write(dynamics,"m_AccelerationTime",.8f);Write(dynamics,"m_BrakingTime",.5f);Write(dynamics,"m_MaxEaseIntoTurnTime",.75f);
            Write(Read(movement,"m_Maneuvers"),"m_MustReachMinSpeedBeforeTurn",false);
            object combat=Read(movement,"m_Combat");Write(combat,"m_MinDesiredCombatRange",500f);Write(combat,"m_MaxDesiredCombatRange",1500f);
        }
        static void ConfigureMediumArtilleryWeapon(object weapon,int mode)
        {
            if(mode<0||mode>=3)throw new ArgumentOutOfRangeException("mode");
            Write(weapon,"m_BaseDamagePerRound",ArtilleryDamage);Write(weapon,"m_AreaOfEffectMeters",80f);
            Write(weapon,"m_RateOfFire",1);Write(weapon,"m_NumberOfBursts",1);Write(weapon,"m_DamagePacketsPerShot",1);
            Write(weapon,"m_BurstPeriodMinTime",ArtilleryBurstMS);Write(weapon,"m_BurstPeriodMaxTime",ArtilleryBurstMS);
            Write(weapon,"m_WindUpTime",ArtilleryWindupMS);Write(weapon,"m_CooldownTime",0);Write(weapon,"m_WindDownTime",250);Write(weapon,"m_ReloadTime",ArtilleryReloadMS);
            Write(weapon,"m_ExcludeFromAutoTargetAcquisition",mode!=0);Write(weapon,"m_ExcludeFromAutoFire",mode!=0);
            Write(weapon,"m_ExcludeFromHeightAdvantage",true);
            Array ranges=(Array)Read(weapon,"m_Ranges");if(ranges==null||ranges.Length!=3)throw new InvalidOperationException("Native artillery requires three range bands");
            int[] distances={750,1100,1500};
            for(int i=0;i<ranges.Length;i++){object range=ranges.GetValue(i);Write(range,"m_Distance",distances[i]);Write(range,"m_MinDistance",450);}
            // Retain the native projectile key, damage type, indirect LOS rule,
            // aiming/scatter, friendly fire and range accuracy. No new damage
            // or projectile path is introduced by the medium profile.
        }
        static object MediumArtilleryBuff(string scope,int attribute,int mode,int amount)
        {
            object entry=Activator.CreateInstance(typeof(UnitAssetContainer).Assembly.GetType("BBI.Unity.Game.Data.AttributeBuffData",true),true);
            Write(entry,"m_Name",scope);Write(entry,"m_AttributeID",Enum.ToObject(Read(entry,"m_AttributeID").GetType(),attribute));
            Write(entry,"m_Mode",Enum.ToObject(Read(entry,"m_Mode").GetType(),mode));Write(entry,"m_Value",amount);return entry;
        }
        static Array MediumArtilleryRankBuffs(int rank)
        {
            List<object> entries=new List<object>();
            if(rank==1)entries.Add(MediumArtilleryBuff(string.Empty,65540,0,1));
            else if(rank==2)entries.Add(MediumArtilleryBuff(string.Empty,65536,0,75));
            else if(rank>=3&&rank<=5)foreach(string key in MediumArtilleryModes)
                entries.Add(MediumArtilleryBuff(key,rank==3?33554434:rank==4?262148:262151,rank==3?0:1,rank==3?50:rank==4?10:-10));
            else throw new ArgumentOutOfRangeException("rank");
            Array result=Array.CreateInstance(entries[0].GetType(),entries.Count);for(int i=0;i<entries.Count;i++)result.SetValue(entries[i],i);return result;
        }
        static int MediumArtilleryRank(string name)
        {
            for(int rank=1;rank<=5;rank++)if(name=="ArtilleryCruiser_Veterancy_Rank"+rank+"_BuffSet_MP")return rank;
            return 0;
        }
        static void ConfigureMediumArtilleryBuffs(object attributes,string name)
        {
            int rank=MediumArtilleryRank(name);if(rank!=0){Write(attributes,"m_Buffs",MediumArtilleryRankBuffs(rank));return;}
            Array source=(Array)Read(attributes,"m_Buffs");List<object> kept=new List<object>();HashSet<int> flags=new HashSet<int>();
            foreach(object entry in source)
            {
                int id=Convert.ToInt32(Read(entry,"m_AttributeID")),category=id&unchecked((int)0xffff0000);
                if(category==262144||category==33554432)
                {
                    // Native toggle may disable AUTO. All other weapon/range
                    // modifiers in this private closure must not restore the
                    // cruiser's damage, rate, reload or range through a status.
                    string scope=(string)Read(entry,"m_Name");int mode=Convert.ToInt32(Read(entry,"m_Mode")),value=Convert.ToInt32(Read(entry,"m_Value"));
                    if(scope!=ArtilleryAuto||(id!=262153&&id!=262154)||mode!=2||(value!=0&&value!=1)||!flags.Add(id))continue;
                }
                kept.Add(entry);
            }
            Array output=Array.CreateInstance(source.GetType().GetElementType(),kept.Count);for(int i=0;i<kept.Count;i++)output.SetValue(kept[i],i);Write(attributes,"m_Buffs",output);
        }
        static void ConfigureMediumArtilleryAbilityOrStatus(object attributes,string name)
        {
            if(name=="Ability_C_ArtilleryBarrage"||name=="Ability_C_ArtilleryBarrage_Precise")
            {
                Write(attributes,"m_CooldownTimeSecs",12f);Write(Read(attributes,"m_Cost"),"m_Resource1Cost",name.EndsWith("_Precise",StringComparison.Ordinal)?20:0);
                Write(Read(attributes,"m_ChainCasting"),"m_QueuedAbility",string.Empty);
                Write(Read(attributes,"m_ChainCasting"),"m_AbilityOnToggledOff",string.Empty);
            }
            if(name=="C_Artillery_DisableDefault_StatusEffect")Write(attributes,"m_Duration",.75f);
            if(name=="C_ArtilleryCruiser_Experience_MP")
            {
                // Native Railgun's medium-unit thresholds, with the private
                // five buff assets retained by reference in this clone.
                int[] thresholds={0,350,665,1264,2401,4561};
                string[] descriptions={"基础型","老练I：护甲+1","老练II：生命+75","老练III：最大射程+50","老练IV：单发伤害+10%","老练V：装填时间-10%"};
                Array levels=(Array)Read(attributes,"m_Levels");if(levels==null||levels.Length!=6)throw new InvalidOperationException("U09 requires six native experience levels");
                for(int i=0;i<levels.Length;i++){object level=levels.GetValue(i);Write(level,"m_RequiredExperience",thresholds[i]);Write(level,"m_BuffTooltipLocID",descriptions[i]);}
            }
        }
        static void VerifyMediumArtilleryProfile(EntityTypeAttributes definition,int rank)
        {
            UnitAttributes unit=definition.Get<UnitAttributes>();UnitMovementAttributes movement=definition.Get<UnitMovementAttributes>();
            if(unit==null||unit.Class!=(UnitClass.Ground|UnitClass.Medium|UnitClass.Ranged)||unit.MaxHealth!=ArtilleryHealth+(rank>=2?75:0)||unit.Armour!=ArtilleryArmour+(rank>=1?1:0)||
                unit.Resource1Cost!=ArtilleryCostCU||unit.Resource2Cost!=ArtilleryCostRU||unit.PopCapCost!=ArtilleryPopulation||unit.ProductionTime!=Fixed64.FromInt(24))
                throw new InvalidOperationException("U09 private medium body/economy differs at veterancy "+rank);
            if(movement==null||movement.Dynamics.MaxSpeed!=Fixed64.FromInt(70)||movement.Dynamics.MinCruiseSpeed!=Fixed64.FromInt(25)||
                movement.Dynamics.AccelerationTime!=Fixed64.FromConstFloat(.8f)||movement.Dynamics.BrakingTime!=Fixed64.FromConstFloat(.5f))
                throw new InvalidOperationException("U09 private medium mobility differs");
            if(movement.Combat.ActiveCombatApproachMode!=CombatApproachType.Snipe||movement.Combat.PassiveCombatApproachMode!=CombatApproachType.Hold||
                movement.Combat.MinDesiredCombatRange!=Fixed64.FromInt(500)||movement.Combat.MaxDesiredCombatRange!=Fixed64.FromInt(1500))
                throw new InvalidOperationException("U09 native combat approach differs from private indirect-fire range");
            if(unit.WeaponLoadout==null||unit.WeaponLoadout.Length!=3)throw new InvalidOperationException("U09 logical fire mode count differs");
            for(int mode=0;mode<3;mode++)
            {
                WeaponBinding binding=unit.WeaponLoadout[mode];WeaponAttributes weapon=binding.Weapon;
                if(binding.WeaponID!=MediumArtilleryModes[mode]||binding.TurretIndex!=0||weapon==null||Math.Abs(Fixed64.UnsafeFloatValue(weapon.BaseDamagePerRound)-(rank>=4?176f:160f))>.001f||
                    weapon.RateOfFire!=1||weapon.NumberOfBursts!=1||weapon.DamagePacketsPerShot!=1||weapon.BurstPeriodMinTimeMS!=250||weapon.BurstPeriodMaxTimeMS!=250||
                    weapon.WindUpTimeMS!=250||weapon.ReloadTimeMS!=(rank>=5?4950:5500)||weapon.CooldownTimeMS!=0||weapon.AreaOfEffectRadius!=Fixed64.FromInt(80)||
                    weapon.LineOfSightRequired||!weapon.ExcludeFromHeightAdvantage||(weapon.EntityTypesToSpawnOnImpact!=null&&weapon.EntityTypesToSpawnOnImpact.Length!=0))
                    throw new InvalidOperationException("U09 single-shot profile differs at mode="+mode+" veterancy="+rank);
                string projectile=mode==1?"C_Artillery_Shell_ProjectileAssetContainer_MP":"C_PreciseArtillery_Shell_ProjectileAssetContainer_MP";
                if(weapon.ProjectileEntityTypeToSpawn!=projectile||weapon.Ranges==null||weapon.Ranges.Length!=3)throw new InvalidOperationException("U09 native indirect projectile/range graph differs");
                int[] distances={750,1100,1500};
                for(int band=0;band<3;band++)if(weapon.Ranges[band].MinDistance!=Fixed64.FromInt(450)||weapon.Ranges[band].Distance!=Fixed64.FromInt(distances[band]+(rank>=3&&band==2?50:0)))
                    throw new InvalidOperationException("U09 private range or minimum-range guard differs");
            }
        }
        static void VerifyMediumArtilleryOwnedBuffs(Context c)
        {
            Dictionary<int,AttributeBuffSet> ranks=new Dictionary<int,AttributeBuffSet>();List<AttributeBuffSet> toggles=new List<AttributeBuffSet>();
            foreach(ScriptableObject asset in c.Owned)
            {
                object data;try{data=Read(asset,"m_Attributes");}catch(MissingMemberException){continue;}
                AttributeBuffSet set=data as AttributeBuffSet;if(set==null)continue;
                int rank=MediumArtilleryRank(asset.name);if(rank!=0){ranks.Add(rank,set);continue;}
                Array entries=Read(data,"m_Buffs") as Array;if(entries==null)continue;
                foreach(object entry in entries)
                {
                    int id=Convert.ToInt32(Read(entry,"m_AttributeID")),category=id&unchecked((int)0xffff0000);
                    if(category!=262144&&category!=33554432)continue;
                    if((string)Read(entry,"m_Name")!=ArtilleryAuto||(id!=262153&&id!=262154)||Convert.ToInt32(Read(entry,"m_Mode"))!=2||
                        (Convert.ToInt32(Read(entry,"m_Value"))!=0&&Convert.ToInt32(Read(entry,"m_Value"))!=1))
                        throw new InvalidOperationException("U09 status closure restores an unaudited cruiser weapon modifier: "+asset.name);
                    if(!toggles.Contains(set))toggles.Add(set);
                }
            }
            if(ranks.Count!=5||toggles.Count==0)throw new InvalidOperationException("U09 needs five privately scaled ranks and native AUTO toggle modifiers");
            Type native=typeof(BBI.Game.Simulation.Sim).Assembly.GetType("BBI.Game.Simulation.EntityTypeBuffExtensions",true);
            EntityTypeAttributes probe=new EntityTypeAttributes(c.Definition);
            for(int rank=1;rank<=5;rank++)
            {NativeRead.Call(native,"AddBuffs",probe,ranks[rank]);VerifyMediumArtilleryProfile(probe,rank);}
            foreach(AttributeBuffSet toggle in toggles)
            {NativeRead.Call(native,"AddBuffs",probe,toggle);VerifyMediumArtilleryProfile(probe,5);NativeRead.Call(native,"RemoveBuffs",probe,toggle);VerifyMediumArtilleryProfile(probe,5);}
        }
    }
}
