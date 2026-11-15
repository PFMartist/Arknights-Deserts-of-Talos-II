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
        static readonly Type EmbarkedCoreAssetType=typeof(UnitAssetContainer).Assembly.GetType("BBI.Unity.Game.Data.UnitAttributesAsset",true);
        static readonly Type EmbarkedRawBindingType=typeof(UnitAssetContainer).Assembly.GetType("BBI.Unity.Game.Data.WeaponAssetBinding",true);
        static readonly ConstructorInfo EmbarkedRawBindingConstructor=EmbarkedRawBindingType.GetConstructor(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,
            null,new Type[]{typeof(string),typeof(WeaponAttributesAsset),typeof(string),typeof(int),typeof(float),typeof(bool)},null);
        static void PrepareEmbarkedSkills(Context c,List<ScriptableObject> abilities)
        {
            if(c.Spec.Key!=TiltStrikeKey)throw new InvalidOperationException("Embarked skills require private U08");
            // UnitAttributesAsset owns editable WeaponAssetBinding entries.
            // Its nested m_Attributes is the already-built runtime cache.
            object core=c.Core;
            ScriptableObject togglePulse=PrepareAutocastPulse(c);
            for(int kind=0;kind<RhodesEmbarkedOperatorSkills.SkillCount;kind++)
            {
                string type=RhodesEmbarkedOperatorSkills.OperatorType(kind),original=RhodesEmbarkedOperatorSkills.OriginalAbilityKey(kind);
                UnitAssetContainer actor=FindSource(type);ScriptableObject sourceAbility=null;
                foreach(ScriptableObject ability in (Array)Read(actor,"m_Abilities"))if(ability!=null&&ability.name==original)sourceAbility=ability;
                if(sourceAbility==null)throw new InvalidOperationException("Embarked skill original ability missing: "+original);
                string sourceWeaponID=kind==0?LaevatainFlameAssets.WeaponID:kind==1?GilbertaGravityAssets.WeaponID:ArdeliaHealingAssets.WeaponID;
                object originalCore=Read(actor,"m_Attributes");
                object sourceBinding=FindEmbarkedSerializedBinding(originalCore,sourceWeaponID);
                ScriptableObject weapon=CloneAsset(c,(ScriptableObject)Read(sourceBinding,"m_Weapon"),RhodesEmbarkedOperatorSkills.ProxyWeaponID(kind)+"_Weapon");
                object weaponData=Read(weapon,"m_Simulation");
                Write(weaponData,"m_ExcludeFromAutoFire",true);Write(weaponData,"m_ExcludeFromAutoTargetAcquisition",true);
                string title=kind==0?"机载：烈焰爆发":kind==1?"机载：引力场":"机载：丰饶之境";
                ScriptableObject proxy=OperatorAbility(c,sourceAbility,RhodesEmbarkedOperatorSkills.ProxyAbilityKey(kind),title,kind==0?KeyCode.Q:kind==1?KeyCode.E:KeyCode.T,
                    "由舱内就绪的"+(kind==0?"莱万汀":kind==1?"洁尔佩塔":"艾尔黛拉")+"在旋翼机上施放。以本机为施法位置，沿用干员技能的施放范围与效果；与地面施法共享冷却。先进源石抑制剂可缩短机载冷却。接驳、起降与返航中不可使用；施放技能会暴露本机。");
                object data=Read(proxy,"m_Attributes");Write(data,"m_CooldownTimeSecs",(float)RhodesEmbarkedOperatorSkills.CooldownSeconds(kind));
                Write(Read(data,"m_UseWeapon"),"m_WeaponID",RhodesEmbarkedOperatorSkills.ProxyWeaponID(kind));
                Write(Read(data,"m_UseWeapon"),"m_ClearGoalsOnUse",false);Write(data,"m_CasterMovesToTarget",false);
                object bindingCopy=BuildEmbarkedSerializedBinding(sourceBinding,(WeaponAttributesAsset)weapon,RhodesEmbarkedOperatorSkills.ProxyWeaponID(kind));
                Array loadout=ReadEmbarkedSerializedLoadout(core);
                Write(core,"m_WeaponLoadout",AppendEmbarkedSerializedBinding(loadout,bindingCopy));
                RhodesEmbarkedOperatorSkills.SetBindingIndex(kind,loadout.Length);
                Array weapons=(Array)Read(c.Custom,"m_Weapons"),all=Array.CreateInstance(weapons.GetType().GetElementType(),weapons.Length+1);
                Array.Copy(weapons,all,weapons.Length);all.SetValue(weapon,weapons.Length);Write(c.Custom,"m_Weapons",all);
                abilities.Add(proxy);
                abilities.Add(PrepareAutocastToggle(c,sourceAbility,kind,togglePulse));
                // The transport publishes all command cells together after it
                // has expanded the compact native order and preserved any
                // command displaced by a reserved skill or cargo cell.
            }
        }
        internal static Array ReadEmbarkedSerializedLoadout(object core)
        {
            if(core==null||!EmbarkedCoreAssetType.IsInstanceOfType(core))throw new ArgumentException("Embarked skills require a UnitAttributesAsset, not runtime UnitAttributesData");
            Array values=Read(core,"m_WeaponLoadout") as Array;
            if(values==null||values.GetType().GetElementType()!=EmbarkedRawBindingType)throw new InvalidOperationException("Embarked skills require the serialized UnitAttributesAsset weapon loadout");
            return values;
        }
        internal static object FindEmbarkedSerializedBinding(object core,string weaponID)
        {
            object found=null;
            foreach(object binding in ReadEmbarkedSerializedLoadout(core))
            {
                if(binding==null||(string)Read(binding,"m_WeaponID")!=weaponID)continue;
                if(found!=null)throw new InvalidOperationException("Embarked skill source weapon binding is not unique: "+weaponID);
                found=binding;
            }
            if(found==null)throw new InvalidOperationException("Embarked skill original serialized weapon binding missing: "+weaponID);
            return found;
        }
        internal static object BuildEmbarkedSerializedBinding(object source,WeaponAttributesAsset weapon,string weaponID)
        {
            if(source==null||source.GetType()!=EmbarkedRawBindingType||object.ReferenceEquals(weapon,null)||string.IsNullOrEmpty(weaponID)||EmbarkedRawBindingConstructor==null)
                throw new ArgumentException("Embarked serialized weapon inputs missing or runtime binding supplied");
            object binding=EmbarkedRawBindingConstructor.Invoke(new object[]{weaponID,weapon,string.Empty,EmbarkedSkillTurretIndex(weaponID),
                Convert.ToSingle(Read(source,"m_TurretDefaultAngleOffsetDegrees")),false});
            Write(binding,"m_ShowAmmoOnHUD",false);
            return binding;
        }
        internal static int EmbarkedSkillTurretIndex(string weaponID)
        {
            for(int kind=0;kind<RhodesEmbarkedOperatorSkills.SkillCount;kind++)
                if(weaponID==RhodesEmbarkedOperatorSkills.ProxyWeaponID(kind))return kind+1;
            throw new ArgumentException("Unknown private embarked weapon: "+weaponID);
        }
        internal static UnitViewAttributes.TurretAttributes[] BuildEmbarkedSkillTurrets(UnitViewAttributes.TurretAttributes[] original)
        {
            if(original==null||original.Length!=1||original[0]==null)throw new InvalidOperationException("U08 native physical turret contract changed");
            UnitViewAttributes.TurretAttributes[] result=new UnitViewAttributes.TurretAttributes[1+RhodesEmbarkedOperatorSkills.SkillCount];result[0]=original[0];
            for(int kind=0;kind<RhodesEmbarkedOperatorSkills.SkillCount;kind++)
            {
                UnitViewAttributes.TurretAttributes turret=new UnitViewAttributes.TurretAttributes();
                turret.BoneName="EF_U08_SkillYaw_"+kind;turret.PitchBoneName="EF_U08_SkillPitch_"+kind;
                // Preserve the existing native origin and emitter. Only the
                // logic/aim channels split; no visible turret is added.
                turret.SimTurretOrigin=original[0].SimTurretOrigin;turret.CenterEmissionBone=original[0].CenterEmissionBone;
                turret.FXEmissionBones=original[0].FXEmissionBones==null?new string[0]:(string[])original[0].FXEmissionBones.Clone();
                turret.PitchMin=-90f;turret.PitchMax=90f;turret.MaxPitchDegreesPerSecond=360f;turret.MaxRollDegreesPerSecond=0f;
                turret.StateAnimationTriggers=new UnitViewAttributes.TurretOperatingAnimationTrigger[0];result[kind+1]=turret;
            }
            return result;
        }
        internal static void ConfigureEmbarkedSkillView(UnitViewAttributes view,Transform root)
        {
            UnitViewAttributes.TurretAttributes[] turrets=BuildEmbarkedSkillTurrets(view.TurretDefinitions);
            for(int kind=0;kind<RhodesEmbarkedOperatorSkills.SkillCount;kind++)
            {
                UnitViewAttributes.TurretAttributes turret=turrets[kind+1];
                GameObject yaw=new GameObject(turret.BoneName);yaw.layer=root.gameObject.layer;yaw.transform.parent=root;
                yaw.transform.localPosition=Vector3.zero;yaw.transform.localRotation=Quaternion.identity;yaw.transform.localScale=Vector3.one;
                GameObject pitch=new GameObject(turret.PitchBoneName);pitch.layer=root.gameObject.layer;pitch.transform.parent=yaw.transform;
                pitch.transform.localPosition=Vector3.zero;pitch.transform.localRotation=Quaternion.identity;pitch.transform.localScale=Vector3.one;
            }
            view.TurretDefinitions=turrets;
        }
        internal static Array AppendEmbarkedSerializedBinding(Array existing,object binding)
        {
            if(existing==null||existing.GetType().GetElementType()!=EmbarkedRawBindingType||binding==null||binding.GetType()!=EmbarkedRawBindingType)
                throw new ArgumentException("Embarked serialized weapon loadout missing or runtime binding supplied");
            string id=(string)Read(binding,"m_WeaponID");
            foreach(object item in existing)
                if(item!=null&&(string)Read(item,"m_WeaponID")==id)throw new InvalidOperationException("Duplicate embarked serialized weapon binding: "+id);
            Array result=Array.CreateInstance(EmbarkedRawBindingType,existing.Length+1);
            Array.Copy(existing,result,existing.Length);result.SetValue(binding,existing.Length);return result;
        }
        static void RegisterEmbarkedSkills(Context c)
        {
            RegisterAutocastPulse(c);
            foreach(ScriptableObject ability in (Array)Read(c.Custom,"m_Abilities"))
            {
                if(ability==null||(!RhodesEmbarkedOperatorSkills.IsProxyAbilityKey(ability.name)&&!RhodesEmbarkedOperatorSkills.IsAutoToggleAbilityKey(ability.name)))continue;
                if(Registry.GetEntityType(ability.name)!=null)throw new InvalidOperationException("Embarked skill registry collision");
                EntityTypeAttributes definition=new EntityTypeAttributes(ability.name);object data=ability as IEnumerable;
                if(data==null)data=Read(ability,"Data");
                AddEmbarkedAbilityData(definition,data);
                Registry.Add(definition);
            }
        }
        internal static void AddEmbarkedAbilityData(EntityTypeAttributes definition,object data)
        {
            if(definition==null||data==null)throw new InvalidOperationException("Embarked skill has no native data");
            IEnumerable values=data as IEnumerable;int count=0;
            if(values==null){if(!definition.Add(data))throw new InvalidOperationException("Duplicate embarked skill data");return;}
            foreach(object item in values)if(item!=null){if(!definition.Add(item))throw new InvalidOperationException("Duplicate embarked skill data");count++;}
            if(count==0)throw new InvalidOperationException("Embarked skill has no native data");
        }
        static void VerifyEmbarkedSkills(Context c)
        {
            UnitAttributes core=c.Definition.Get<UnitAttributes>();
            UnitViewAttributes view=c.Definition.Get<UnitViewAttributes>();
            if(view==null||view.TurretDefinitions==null||view.TurretDefinitions.Length!=1+RhodesEmbarkedOperatorSkills.SkillCount)
                throw new InvalidOperationException("U08 independent skill turret views are missing");
            VerifyEmbarkedRuntimeBindings(c.Core,core);
            VerifyAutocastToggles(c);
            for(int kind=0;kind<RhodesEmbarkedOperatorSkills.SkillCount;kind++)
            {
                string key=RhodesEmbarkedOperatorSkills.ProxyAbilityKey(kind),id=RhodesEmbarkedOperatorSkills.ProxyWeaponID(kind);
                UnitViewAttributes.TurretAttributes skillTurret=view.TurretDefinitions[EmbarkedSkillTurretIndex(id)];
                if(skillTurret==null||skillTurret.BoneName!="EF_U08_SkillYaw_"+kind||skillTurret.PitchBoneName!="EF_U08_SkillPitch_"+kind)
                    throw new InvalidOperationException("U08 private skill turret identity differs");
                EntityTypeAttributes definition=Registry.GetEntityType(key);AbilityAttributes ability=definition==null?null:definition.Get<AbilityAttributes>();
                if(ability==null||ability.AbilityType!=AbilityClass.UseWeapon||ability.UseWeapon.WeaponID!=id||ability.UseWeapon.ClearGoalsOnUse||
                    ability.CasterMovesToTarget||Math.Abs((float)Read(ability,"m_CooldownTimeSecs")-RhodesEmbarkedOperatorSkills.CooldownSeconds(kind))>.001f)
                    throw new InvalidOperationException("Embarked native ability contract differs: "+key);
                int matches=0;
                foreach(WeaponBinding binding in core.WeaponLoadout)
                {
                    if(binding==null||binding.WeaponID!=id)continue;matches++;
                    WeaponAttributes weapon=binding.Weapon;
                    if(weapon==null||binding.TurretIndex!=EmbarkedSkillTurretIndex(id)||!string.IsNullOrEmpty(binding.AmmoID)||binding.DisabledOnSpawn||!weapon.ExcludeFromAutoFire||!weapon.ExcludeFromAutoTargetAcquisition||
                        weapon.ProjectileEntityTypeToSpawn!=(kind==0?LaevatainFlameAssets.ProjectileKey:kind==1?GilbertaGravityAssets.ProjectileKey:ArdeliaHealingAssets.ProjectileKey))
                        throw new InvalidOperationException("Embarked manual unlimited-ammo weapon contract differs: "+id);
                }
                if(matches!=1)throw new InvalidOperationException("Embarked weapon binding must be unique: "+id);
                foreach(object context in (Array)Read(Read(c.Hud,"m_Attributes"),"m_HUDContexts"))
                {string[] order=(string[])Read(context,"m_AbilityOrder");if(order.Length<=7||order[6+kind]!=key)throw new InvalidOperationException("Embarked native HUD cell changed");}
            }
        }
        // Called after custom.OnEnable, Registry.Add and native cache rebuild.
        // Each editable asset association must survive as the same native datum.
        internal static void VerifyEmbarkedRuntimeBindings(object asset,UnitAttributes core)
        {
            Array serialized=ReadEmbarkedSerializedLoadout(asset);
            WeaponBinding[] runtime=core==null?null:core.WeaponLoadout;
            if(runtime==null||runtime.Length!=serialized.Length)throw new InvalidOperationException("Embarked native weapon cache length differs from serialized loadout");
            for(int index=0;index<serialized.Length;index++)
            {
                object raw=serialized.GetValue(index);WeaponBinding built=runtime[index];
                if(raw==null||built==null||built.WeaponBindingIndex!=index||built.WeaponID!=(string)Read(raw,"m_WeaponID")||
                    !object.ReferenceEquals(built.Weapon,Read(Read(raw,"m_Weapon"),"m_Simulation"))||
                    built.AmmoID!=(string)Read(raw,"m_AmmoID")||built.TurretIndex!=Convert.ToInt32(Read(raw,"m_TurretIndex"))||
                    built.DisabledOnSpawn!=(bool)Read(raw,"m_DisabledOnSpawn")||built.ShowAmmoOnHUD!=(bool)Read(raw,"m_ShowAmmoOnHUD"))
                    throw new InvalidOperationException("Embarked native weapon cache differs from serialized binding at index "+index);
            }
        }
    }
}
