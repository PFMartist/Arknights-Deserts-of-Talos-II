using System;
using System.Collections.Generic;
using System.Reflection;
using BBI.Core;
using BBI.Core.Data;
using BBI.Game.Data;
using BBI.Unity.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesCarrierUnits
    {
        const int PowerWeaponDamageAttribute=NativeUnitBindings.WeaponDamageAttribute;
        const int PowerDamageReceivedAttribute=NativeUnitBindings.DamageReceivedAttribute;
        const int PowerSensorAttribute=NativeUnitBindings.SensorRangeAttribute,PowerContactAttribute=NativeUnitBindings.ContactRangeAttribute;
        const int PowerSpeedAttribute=NativeUnitBindings.MaximumSpeedAttribute,PowerCruiseAttribute=NativeUnitBindings.MinimumCruiseSpeedAttribute;
        const string PowerBomberWeapon="DEFAULT";
        const string PowerInterceptorWeapon="AIR-TO-GROUND";
        sealed class PowerBuffEntry
        {
            internal string Name;internal int Attribute,Mode,Value;
            internal PowerBuffEntry(string name,int attribute,int mode,int value){Name=name;Attribute=attribute;Mode=mode;Value=value;}
        }

        static void PreparePower(Context c)
        {
            ScriptableObject original=Read(c.Source,"m_PowerShunt") as ScriptableObject;
            if(original==null)throw new InvalidOperationException("Rhodes requires its native power component");
            c.Power=(ScriptableObject)c.Map[original];
            object data=CreatePowerConfigurationData();
            Array nativeSystems=(Array)Read(Read(original,"m_Attributes"),"m_PowerSystemAssets");
            Array systems=Array.CreateInstance(nativeSystems.GetType().GetElementType(),RhodesPowerProfile.SystemCount);
            int[] iconSources={1,0,2,3};
            for(int system=0;system<RhodesPowerProfile.SystemCount;system++)
            {
                ScriptableObject asset=NewPowerAsset(c,"PowerSystemAttributesAsset",RhodesPowerProfile.SystemName(system));
                object own=NewPowerData("PowerSystemAttributesData"),view=Read(nativeSystems.GetValue(iconSources[system]),"m_Attributes");
                PowerEnum(own,"m_PowerSystemType",system);Write(own,"m_StartingPowerLevelIndex",RhodesPowerProfile.StartingLevel(system));
                Write(own,"m_StartingMaxPowerLevelIndex",RhodesPowerProfile.MaximumLevel);
                foreach(string field in new string[]{"m_EnabledIconName","m_DisabledIconName"})Write(own,field,Read(view,field));
                string title=system==0?"武备系统":system==1?"损管系统":system==2?"机动系统":"探测火控";
                Write(own,"m_LocalizedTitleStringID",title);
                // This native tooltip field is static channel copy, not the
                // current level. Actual current/target values belong to the HUD.
                Write(own,"m_LocalizedShortDescriptionStringID",system==0?"分档开启近防，强化主炮与舰载对地火力":system==1?"强化两舷维修与舰体装甲":system==2?"强化本舰机动":"强化本舰侦测与主炮射程");
                Write(own,"m_LocalizedLongDescriptionStringID",PowerLongDescription(system));
                Write(own,"m_LocalizedSystemDisabledStringID",system==0?"近防与主炮停火；已造飞机保留基础作战能力。":"维持基础能力，未分配强化功率。");
                object first=CreatePowerLevelData(system,0);Array levels=Array.CreateInstance(first.GetType(),RhodesPowerProfile.MaximumLevel+1);
                for(int level=0;level<=RhodesPowerProfile.MaximumLevel;level++)
                {
                    object entry=level==0?first:CreatePowerLevelData(system,level);
                    if(level==0)ClearArray(entry,"m_StatusEffectsToApply");
                    else
                    {
                        ScriptableObject effect=BuildPowerStatus(c,system,level);
                        Array effects=Array.CreateInstance(effect.GetType(),1);effects.SetValue(effect,0);Write(entry,"m_StatusEffectsToApply",effects);
                    }
                    levels.SetValue(entry,level);
                }
                Write(own,"m_PowerLevels",levels);Write(asset,"m_Attributes",own);c.PowerSystems.Add(asset);systems.SetValue(asset,system);
            }
            Write(data,"m_PowerSystemAssets",systems);Write(c.Power,"m_Attributes",data);Write(c.Custom,"m_PowerShunt",c.Power);
            PrepareDeferredThermalVisuals(c);
            // These two systems are deliberately independent. Status effects
            // are native and temporary; no research or per-tick attribute write.
            Runtime.Log("RHODES_POWER_PREPARED systems=4; levels=0-5; totalPower=5; initialAllocation=1,1,1,1; nativeIncrementalCosts=0,1,2,3,4,5; nativeIncrementalHeat=0,1,2,3,4,5; heatThreshold="+RhodesThermalProfile.HeatThreshold+
                "; overheatPool="+RhodesThermalProfile.OverheatingCapacity+"; cooldownPerNativeInterval="+RhodesThermalProfile.CooldownRate+"; overheatDamage="+RhodesThermalProfile.OverheatDamage+
                "; nativeThermalSimulation=True; customThermalVisualsDeferred=True; donorPowerStatusChainDiscarded=True; aircraftScope=CommanderPlusExactPrivateType; multiCarrierPerOwnerUnsupported=True");
        }

        // Pure native serialized-data construction, also used by offline
        // processor fixtures. It does not allocate Unity objects or a timer.
        static object CreatePowerConfigurationData()
        {
            object data=NewPowerData("PowerShuntAttributesData");
            Write(data,"m_PowerLevelChargeTimeSeconds",RhodesPowerProfile.ChargeSecondsPerLevel);
            Write(data,"m_PowerLevelDrainTimeSeconds",RhodesPowerProfile.DrainSecondsPerLevel);
            Write(data,"m_HeatThreshold",RhodesThermalProfile.HeatThreshold);Write(data,"m_CooldownRate",RhodesThermalProfile.CooldownRate);
            Write(data,"m_OverheatDamage",RhodesThermalProfile.OverheatDamage);Write(data,"m_NearOverheatWarningMargin",RhodesThermalProfile.NearOverheatWarningMargin);
            Write(data,"m_OverheatingReminderPeriodMS",RhodesThermalProfile.ReminderPeriodMS);
            Write(data,"m_ReservePowerPool",PowerInventory("POWER",RhodesPowerProfile.InitialPower,RhodesPowerProfile.MaximumPower));
            Write(data,"m_HeatSystem",PowerInventory("HEAT",0,RhodesThermalProfile.HeatCapacity));
            Write(data,"m_OverheatingPool",PowerInventory("OVERHEAT",0,RhodesThermalProfile.OverheatingCapacity));
            Write(data,"m_ReservePowerPoolLocalizedTitleStringID","可分配功率");
            Write(data,"m_ReservePowerPoolLocalizedShortDescriptionStringID","在武备、损管、机动与探测火控之间分配功率");
            Write(data,"m_ReservePowerPoolLocalizedLongDescriptionStringID","初始总功率5点，扩容依次提高至7、10、14、19、24点。每升一级新增占用1、2、3、4、5点，充能3秒，撤能立即。储备功率不产热；已生效的占用功率产生等量热负荷，散热极限19。武备0档近防与主炮停火，其余基础能力保留。");
            Write(data,"m_TemperatureGaugeLocalizedTitleStringID","热负荷与散热");
            Write(data,"m_TemperatureGaugeLocalizedShortDescriptionStringID","超出散热极限将积累热损伤，降低供能档位可散热");
            Write(data,"m_TemperatureGaugeLocalizedLongDescriptionStringID","总热负荷为地图环境热与已生效的占用功率之和；散热极限19。超出部分每秒积累热损伤，累计120点时损失750点舰体生命并重新计量。降至极限以内后，每秒消退4点残热。温度条沿用原生热负荷显示，不表示摄氏温度。功率扩容增加分配余量，不提高散热极限。");
            return data;
        }
        static object CreatePowerLevelData(int system,int level)
        {
            RhodesPowerProfile.CheckSystem(system);RhodesPowerProfile.CheckLevel(level);
            object entry=NewPowerData("PowerLevelAttributesData");
            Write(entry,"m_PowerUnitsRequired",RhodesPowerProfile.ExtraPowerCost(level));
            Write(entry,"m_HeatPointsProvided",RhodesThermalProfile.HeatPointsForLevel(level));
            Write(entry,"m_LocalizedShortDescriptionStringID",PowerDescription(system,level));
            return entry;
        }

        static ScriptableObject BuildPowerStatus(Context c,int system,int level)
        {
            string name=RhodesPowerProfile.StatusName(system,level);ScriptableObject status=NewPowerAsset(c,"StatusEffectAttributesAsset",name);
            object data=NewPowerData("StatusEffectAttributesData");InitializePowerName(data,name);
            PowerEnum(data,"m_Lifetime",1);Write(data,"m_Duration",0f);PowerEnum(data,"m_WeaponFireTriggerEndEvent",0);
            Write(data,"m_MaxStacks",1);PowerEnum(data,"m_StackingBehaviour",0);PowerEnum(data,"m_FXSetStatesWhenActive",0);
            ClearArray(data,"m_Modifiers");
            List<PowerBuffEntry> own=new List<PowerBuffEntry>();
            if(system==0)
            {
                foreach(string id in MainWeaponIDs)own.Add(new PowerBuffEntry(id,PowerWeaponDamageAttribute,1,RhodesPowerProfile.HullWeaponDamagePercent(level)));
                foreach(string id in CiwsWeaponIDs)
                {
                    own.Add(new PowerBuffEntry(id,PowerWeaponDamageAttribute,2,RhodesPowerProfile.CiwsDamage(level)));
                    own.Add(new PowerBuffEntry(id,262149,2,RhodesPowerProfile.CiwsRateOfFire(level)));
                    AddPowerRanges(own,id,2,RhodesPowerProfile.CiwsRange(level));
                }
                SetPowerWeaponEnables(data);
                ScriptableObject aircraft=CreatePowerBuff(c,name+"_AircraftBuff",new PowerBuffEntry[]{new PowerBuffEntry(PowerInterceptorWeapon,PowerWeaponDamageAttribute,1,RhodesPowerProfile.AircraftDamagePercent(level))});
                ScriptableObject bomber=CreatePowerBuff(c,name+"_BomberBuff",new PowerBuffEntry[]{new PowerBuffEntry(PowerBomberWeapon,PowerWeaponDamageAttribute,1,RhodesPowerProfile.AircraftDamagePercent(level))});
                object target=PowerTarget(AircraftKey,aircraft),bombTarget=PowerTarget(HeavyBomberKey,bomber);
                Array targets=Array.CreateInstance(target.GetType(),2);targets.SetValue(target,0);targets.SetValue(bombTarget,1);Write(data,"m_UnitTypeBuffsToApply",targets);
            }
            else if(system==1)
            {
                own.Add(new PowerBuffEntry(SupportWeaponID,PowerWeaponDamageAttribute,2,RhodesPowerProfile.RepairPulse(level)));
                own.Add(new PowerBuffEntry(SupportStarboardWeaponID,PowerWeaponDamageAttribute,2,RhodesPowerProfile.RepairPulse(level)));
                own.Add(new PowerBuffEntry(string.Empty,65540,0,RhodesPowerProfile.ArmourIncrease(level)));
                ClearArray(data,"m_UnitTypeBuffsToApply");
            }
            else if(system==2)
            {
                own.Add(new PowerBuffEntry(string.Empty,PowerSpeedAttribute,1,RhodesPowerProfile.SpeedPercent(level)));
                own.Add(new PowerBuffEntry(string.Empty,PowerCruiseAttribute,1,RhodesPowerProfile.SpeedPercent(level)));
                ClearArray(data,"m_UnitTypeBuffsToApply");
            }
            else
            {
                own.Add(new PowerBuffEntry(string.Empty,PowerSensorAttribute,0,RhodesPowerProfile.SensorContactIncrease(level)));
                own.Add(new PowerBuffEntry(string.Empty,PowerContactAttribute,0,RhodesPowerProfile.SensorContactIncrease(level)));
                AddMainPowerRanges(own,level);
                ClearArray(data,"m_UnitTypeBuffsToApply");
            }
            ScriptableObject self=CreatePowerBuff(c,name+"_HullBuff",own.ToArray());
            Write(data,"m_BuffsToApplyToTarget",PowerArray(PowerTarget(UnitKey,self)));Write(status,"m_Attributes",data);c.PowerStatuses.Add(status);return status;
        }
        // Native range category is separate from the weapon category; exact
        // binding names scope both. MinimumRange is intentionally untouched.
        static void AddPowerRanges(List<PowerBuffEntry> entries,string id,int mode,int value)
        {
            for(int range=0;range<3;range++)entries.Add(new PowerBuffEntry(id,33554432+range,mode,mode==2?value*(range+1)/3:value));
        }
        static void AddMainPowerRanges(List<PowerBuffEntry> entries,int level)
        {
            int percent=RhodesPowerProfile.MainRangePercent(level);
            foreach(string id in MainWeaponIDs)AddPowerRanges(entries,id,1,percent);
        }
        static void SetPowerWeaponEnables(object data)
        {
            FieldInfo field=null;foreach(FieldInfo f in Fields(data.GetType()))if(f.Name=="m_Modifiers"){field=f;break;}
            if(field==null||!field.FieldType.IsArray)throw new MissingFieldException("Native status modifier array missing");
            Type type=field.FieldType.GetElementType();Array modifiers=Array.CreateInstance(type,4);
            for(int i=0;i<4;i++)
            {
                object modifier=Activator.CreateInstance(type,true);PowerEnum(modifier,"ModifierType",4);
                object enable=Read(modifier,"EnableWeaponAttributes");Write(enable,"WeaponID",CiwsWeaponIDs[i]);Write(modifier,"EnableWeaponAttributes",enable);modifiers.SetValue(modifier,i);
            }
            field.SetValue(data,modifiers);
        }
        static ScriptableObject CreatePowerBuff(Context c,string name,PowerBuffEntry[] entries)
        {
            ScriptableObject asset=NewPowerAsset(c,"BuffSetAttributesAsset",name);
            object data=CreatePowerBuffData(entries);
            Write(asset,"m_Attributes",data);c.PowerBuffs.Add(asset);return asset;
        }
        static object CreatePowerBuffData(PowerBuffEntry[] entries)
        {
            // ABS_Data is a shared native serialization TYPE discriminator.
            // Buff instances are private data objects; never change the static
            // AttributeBuffSetData.UniqueSubClassName for per-buff identity.
            object data=NewPowerData("AttributeBuffSetData");
            object first=NewPowerData("AttributeBuffData");Array buffs=Array.CreateInstance(first.GetType(),entries.Length);
            for(int i=0;i<entries.Length;i++)
            {
                object entry=i==0?first:NewPowerData("AttributeBuffData");PowerBuffEntry spec=entries[i];
                Write(entry,"m_Name",spec.Name);PowerEnum(entry,"m_AttributeID",spec.Attribute);PowerEnum(entry,"m_Mode",spec.Mode);Write(entry,"m_Value",spec.Value);buffs.SetValue(entry,i);
            }
            Write(data,"m_Buffs",buffs);return data;
        }
        static object PowerTarget(string unit,ScriptableObject buff)
        {object target=NewPowerData("UnitTypeBuffData");Write(target,"m_UnitTypePrefix",unit);Write(target,"m_UseAsPrefix",false);PowerEnum(target,"m_UnitClass",0);PowerEnum(target,"m_ClassOperator",1);Write(target,"m_BuffSet",buff);return target;}
        static object PowerInventory(string id,int start,int capacity)
        {object data=new InventoryAttributesData();Write(data,"m_InventoryID",id);Write(data,"m_IsUnlimited",false);Write(data,"m_StartingAmount",start);Write(data,"m_MaxAmount",capacity);return data;}
        static Array PowerArray(object value){Array array=Array.CreateInstance(value.GetType(),1);array.SetValue(value,0);return array;}
        static ScriptableObject NewPowerAsset(Context c,string type,string name)
        {ScriptableObject asset=RhodesAssetClone59.CreateEmpty(typeof(UnitAssetContainer).Assembly.GetType("BBI.Unity.Game.Data."+type,true));asset.name=name;asset.hideFlags=HideFlags.HideAndDontSave;c.Owned.Add(asset);return asset;}
        static object NewPowerData(string type)
        {return Activator.CreateInstance(typeof(UnitAssetContainer).Assembly.GetType("BBI.Unity.Game.Data."+type,true),true);}
        internal static PowerShuntAttributes PreparedPowerAttributes
        {get{return Current==null||Current.Power==null?null:Read(Current.Power,"Data") as PowerShuntAttributes;}}
        internal static PowerShuntAttributes[] RegisteredPowerAttributes
        {get{EntityTypeAttributes type=Registry==null?null:Registry.GetEntityType(UnitKey);return type==null?null:type.GetAll<PowerShuntAttributes>();}}
        static void InitializePowerName(object data,string name){NamedObjectBase named=data as NamedObjectBase;if(named!=null)named.InitializeName(name);}
        static void PowerEnum(object data,string field,int value)
        {foreach(FieldInfo candidate in Fields(data.GetType()))if(candidate.Name==field){candidate.SetValue(data,Enum.ToObject(candidate.FieldType,value));return;}throw new MissingFieldException(data.GetType().FullName,field);}

        static string PowerDescription(int system,int level)
        {
            string cost="累计占用"+RhodesPowerProfile.CumulativePowerCost(level)+"点"+(level<5?"；下一级新增"+(level+1)+"点":"；已满档");
            if(level==0)return "0档："+(system==0?"近防与主炮停火，飞机保持基础能力。":"维持基础能力。")+cost;
            if(system==0)return level+"档：近防"+(level==1?"仅对地":"对空地")+"，伤害"+RhodesPowerProfile.CiwsDamage(level)+"，射程"+RhodesPowerProfile.CiwsRange(level)+"；主炮伤害+"+RhodesPowerProfile.HullWeaponDamagePercent(level)+"%；截击对地与重轰伤害+"+RhodesPowerProfile.AircraftDamagePercent(level)+"%；"+cost;
            if(system==1)return level+"档：维修脉冲"+RhodesPowerProfile.RepairPulse(level)+"点；本舰装甲+"+RhodesPowerProfile.ArmourIncrease(level)+"；"+cost;
            if(system==2)return level+"档：本舰速度+"+RhodesPowerProfile.SpeedPercent(level)+"%；"+cost;
            return level+"档：侦测与接触范围+"+RhodesPowerProfile.SensorContactIncrease(level)+"；主炮射程+"+RhodesPowerProfile.MainRangePercent(level)+"%；"+cost;
        }
        static string PowerLongDescription(int system)
        {
            string rules="每级新增占用1/2/3/4/5点，累计占用及热负荷为1/3/6/10/15点。加档3秒，撤能立即；全舰总热负荷超过19时积累过热损伤。";
            if(system==0)return rules+"0档近防与主炮停火；1档四座近防对地，2档起兼对空。主炮还需永久研究及完成展开；撤能不撤回模块。近防不叠主炮伤害或探测射程。截击对地与重轰炸弹每档+4%，飞机0档仍可作战；不影响截击对空、旋翼机、干员或地面单位。";
            if(system==1)return rules+"1至5档维修每次恢复1/2/2/3/3点，装甲增加5/15/35/55/75；维修范围不变，不能维修自身，不提供额外百分比减伤。";
            if(system==2)return rules+"本舰最大与最低巡航速度每档+8%；不改变飞机、弹丸、采矿车或整备速度。";
            return rules+"1至5档本舰传感与接触范围分别增加100/300/600/900/1200，主炮三段射程每档+4%；350米近距盲区固定。不扩大近防、干员或飞机武器范围。";
        }
        static void FinishPower(Context c)
        {
            // Native save-load resolves an active StatusEffect by its global
            // exact name. Register these definitions, not any donor C status.
            foreach(ScriptableObject status in c.PowerStatuses)
            {
                StatusEffectAttributes data=(StatusEffectAttributes)Read(status,"Data");InitializePowerName(data,status.name);
                if(Registry.GetEntityType(status.name)!=null)throw new InvalidOperationException("Private power status definition collision: "+status.name);
                EntityTypeAttributes definition=new EntityTypeAttributes(status.name);if(!definition.Add(data))throw new InvalidOperationException("Private power status could not be registered");Registry.Add(definition);
            }
            VerifyPower(c);
        }
        static void VerifyPower(Context c)
        {
            if(c.Power==null||object.ReferenceEquals(c.Power,Read(c.Source,"m_PowerShunt"))||!object.ReferenceEquals(Read(c.Custom,"m_PowerShunt"),c.Power))throw new InvalidOperationException("Private Rhodes power component missing/aliased");
            PowerShuntAttributes power=(PowerShuntAttributes)Read(c.Power,"Data");
            if(power.PowerSystems==null||power.PowerSystems.Length!=4||power.ReservePowerPool.StartingAmount!=5||power.ReservePowerPool.Capacity!=24||
                power.OverheatDamage!=RhodesThermalProfile.OverheatDamage||power.HeatThreshold!=RhodesThermalProfile.HeatThreshold||
                power.CooldownRate!=RhodesThermalProfile.CooldownRate||power.NearOverheatWarningMargin!=RhodesThermalProfile.NearOverheatWarningMargin||
                power.OverheatReminderPeriod!=RhodesThermalProfile.ReminderPeriodMS||power.HeatSystem.Capacity!=RhodesThermalProfile.HeatCapacity||
                power.OverheatingPool.Capacity!=RhodesThermalProfile.OverheatingCapacity)
                throw new InvalidOperationException("Rhodes power pool, heat or system layout mismatch");
            if(power.HeatSystem.StartingAmount!=0||power.OverheatingPool.StartingAmount!=0||power.PowerLevelChargeTimeSeconds!=BBI.Core.Utility.FixedPoint.Fixed64.FromInt(3)||power.PowerLevelDrainTimeSeconds!=BBI.Core.Utility.FixedPoint.Fixed64.Zero)
                throw new InvalidOperationException("Rhodes private charge/heat configuration mismatch");
            int starting=0;
            for(int system=0;system<4;system++)
            {
                PowerSystemAttributes own=power.PowerSystems[system];
                if((int)own.PowerSystemType!=system||own.StartingPowerLevelIndex!=RhodesPowerProfile.StartingLevel(system)||own.StartingMaxPowerLevelIndex!=5||own.PowerLevels==null||own.PowerLevels.Length!=6)
                    throw new InvalidOperationException("Rhodes native power level count/initial allocation mismatch");starting+=RhodesPowerProfile.CumulativePowerCost(own.StartingPowerLevelIndex);
                for(int level=0;level<6;level++)
                {
                    PowerLevelAttributes item=own.PowerLevels[level];
                    StatusEffectAttributes status=VerifyPowerLevel(item,system,level);
                    if(level==0)continue;
                    UnitTypeBuff self=status.BuffsToApplyToTarget[0];if(self.UnitType!=UnitKey||self.UseAsPrefix||self.BuffSet==null||self.BuffSet.UniqueTypeName!="ABS_Data")throw new InvalidOperationException("Rhodes direct power buff scope/serialization type mismatch");
                    if(status.UnitTypeBuffsToApply.Length!=(system==0?2:0))throw new InvalidOperationException("Unexpected broad fleet power buff");
                    if(system==0)for(int aircraftIndex=0;aircraftIndex<2;aircraftIndex++){UnitTypeBuff aircraft=status.UnitTypeBuffsToApply[aircraftIndex];if(aircraft==null||aircraft.UnitType!=(aircraftIndex==0?AircraftKey:HeavyBomberKey)||aircraft.UseAsPrefix||aircraft.UnitClass!=0||aircraft.BuffSet==null||aircraft.BuffSet.UniqueTypeName!="ABS_Data")throw new InvalidOperationException("Aircraft power scope differs");}
                    EntityTypeAttributes registered=Registry.GetEntityType(status.Name);if(registered==null||!object.ReferenceEquals(registered.Get<StatusEffectAttributes>(),status))throw new InvalidOperationException("Power status save-load definition missing");
                }
            }
            if(starting!=RhodesPowerProfile.InitialPower-1||c.PowerStatuses.Count!=20||c.PowerBuffs.Count!=30||c.PowerSystems.Count!=4)throw new InvalidOperationException("Power graph/inventory accounting mismatch");
            List<object> buffInstances=new List<object>();
            foreach(ScriptableObject asset in c.PowerBuffs)
            {
                object data=Read(asset,"Data");
                foreach(object previous in buffInstances)if(object.ReferenceEquals(previous,data))throw new InvalidOperationException("Power buff instances must not share mutable data");
                buffInstances.Add(data);
            }
        }

        // The native PowerShuntAttributesAsset.OnEnable flattens each level's
        // asset array through PrepareStatusEffectAttributes. Its empty-input
        // representation is null, so a null zero-tier result is legitimate.
        // A nonzero tier must still resolve one real status with strict data.
        static StatusEffectAttributes VerifyPowerLevel(PowerLevelAttributes item,int system,int level)
        {
            string scope="Rhodes power system="+system+" level="+level+" field=";
            if(item==null)throw new InvalidOperationException(scope+"level: missing native attributes");
            int expectedCost=RhodesPowerProfile.ExtraPowerCost(level);
            if(item.PowerUnitsRequired!=expectedCost)throw new InvalidOperationException(scope+"PowerUnitsRequired: expected "+expectedCost+", got "+item.PowerUnitsRequired);
            int expectedHeat=RhodesThermalProfile.HeatPointsForLevel(level);
            if(item.HeatPointsProvided!=expectedHeat)throw new InvalidOperationException(scope+"HeatPointsProvided: expected "+expectedHeat+", got "+item.HeatPointsProvided);
            StatusEffectAttributes[] effects=item.StatusEffectsToApply;
            if(level==0)
            {
                if(effects!=null&&effects.Length!=0)throw new InvalidOperationException(scope+"StatusEffectsToApply: zero tier must have no effects");
                return null;
            }
            if(effects==null||effects.Length!=1||effects[0]==null)
                throw new InvalidOperationException(scope+"StatusEffectsToApply: nonzero tier must resolve exactly one nonnull status");
            StatusEffectAttributes status=effects[0];
            if(status.Name!=RhodesPowerProfile.StatusName(system,level))throw new InvalidOperationException(scope+"Status.Name: unexpected private identity");
            if((int)status.Lifetime!=1)throw new InvalidOperationException(scope+"Status.Lifetime: expected Indefinite");
            if(status.MaxStacks!=1)throw new InvalidOperationException(scope+"Status.MaxStacks: expected 1");
            if(status.Modifiers==null||status.Modifiers.Length!=(system==0?4:0))throw new InvalidOperationException(scope+"Status.Modifiers: unexpected native weapon enable count");
            if(status.BuffsToApplyToTarget==null||status.BuffsToApplyToTarget.Length!=1||status.BuffsToApplyToTarget[0]==null)
                throw new InvalidOperationException(scope+"Status.BuffsToApplyToTarget: expected one private hull target");
            if(status.UnitTypeBuffsToApply==null||status.UnitTypeBuffsToApply.Length!=(system==0?2:0)||
                (system==0&&status.UnitTypeBuffsToApply[0]==null))
                throw new InvalidOperationException(scope+"Status.UnitTypeBuffsToApply: unexpected aircraft target count");
            return status;
        }
    }
}
