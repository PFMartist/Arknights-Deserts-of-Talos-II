using System;
using System.Collections.Generic;
using BBI.Core;
using BBI.Core.Data;
using BBI.Game.Data;
using BBI.Unity.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesCarrierUnits
    {
        // Unity-LH converted metre-space marker endpoints. Native local Right/Back
        // is (Unity X,-Unity Z); relative native yaw is -Unity yaw. Source
        // SHA-256 and slot fitting residuals are recorded in V05_ANCHORS.json.
        static readonly string[] V05UndockMarkers={"V03_HP_AV_01_Undock","V03_HP_AV_02_Undock","V03_HP_AV_03_Undock"};
        static readonly string[] V05DockMarkers={"V03_HP_AV_01_Dock","V03_HP_AV_02_Dock","V03_HP_AV_03_Dock"};
        static readonly float[] V05UndockNativeRightBack=new float[]{67.656242371f,-85.466560364f,28.656240463f,-85.466560364f,-10.343756676f,-85.466560364f};
        static readonly float[] V05DockNativeRightBack=new float[]{44.659122467f,59.731506348f,5.659124851f,59.731506348f,-33.340873718f,59.731506348f};
        static readonly bool V05RunwayCoordinatesReady=true;
        static readonly float V05RunwayForwardYawDegrees=-9f;
        static AviationRunway GetV05Runway()
        {
            if(HasV17Hull())return GetV17Runway();
            if(!V05RunwayCoordinatesReady||V05UndockNativeRightBack.Length!=6||V05DockNativeRightBack.Length!=6)
                throw new InvalidOperationException("Final V05 runway marker static data has not been supplied; do not launch this candidate");
            foreach(float value in V05UndockNativeRightBack)if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidOperationException("Invalid V05 undock coordinate");
            foreach(float value in V05DockNativeRightBack)if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidOperationException("Invalid V05 dock coordinate");
            UnitViewAttributes view=(UnitViewAttributes)Read(Current.View,"Data");
            for(int i=0;i<3;i++){RequireRhodesWeaponBone(view.ModelPrefab,V05UndockMarkers[i]);RequireRhodesWeaponBone(view.ModelPrefab,V05DockMarkers[i]);RequireRhodesWeaponBone(view.ModelPrefab,"EF_AV_UndockControl_0"+(i+1));RequireRhodesWeaponBone(view.ModelPrefab,"EF_AV_DockControl_0"+(i+1));}
            float separation=V05UndockNativeRightBack[2]-V05UndockNativeRightBack[0];
            if(Math.Abs(V05UndockNativeRightBack[4]-V05UndockNativeRightBack[2]-separation)>.05f||Math.Abs(V05UndockNativeRightBack[3]-V05UndockNativeRightBack[1])>.05f||Math.Abs(V05UndockNativeRightBack[5]-V05UndockNativeRightBack[1])>.05f)
                throw new InvalidOperationException("V05 undock lanes do not fit the native equal-offset three-slot bay; configure explicit lanes before launch");
            return new AviationRunway{Right=V05UndockNativeRightBack[0],Back=V05UndockNativeRightBack[1],Separation=separation,Degrees=V05RunwayForwardYawDegrees,
                ReceivingRight=V05DockNativeRightBack[0],ReceivingBack=V05DockNativeRightBack[1],UndockBone="EF_AV_UndockControl_0$Slot$",DockBone="EF_AV_DockControl_0$Slot$"};
        }
        static void VerifySupport(Context c)
        {
            EntityTypeAttributes unlockDefinition=Registry.GetEntityType(GunneryEnableAbilityKey);
            AbilityAttributes unlock=unlockDefinition==null?null:unlockDefinition.Get<AbilityAttributes>();
            if(unlock==null||unlock.AbilityType!=AbilityClass.ApplyStatusEffect||(unlock.TargetingType&AbilityTargetingType.Passive)==0||unlock.ResearchDependencies.RequiredResearch.Length!=1||unlock.ResearchDependencies.RequiredResearch[0]!=RhodesResearch.Gunnery1)
                throw new InvalidOperationException("Rhodes native main unlock registry/dependency contract failed");
            EntityTypeAttributes aftDefinition=Registry.GetEntityType(GunneryAftEnableAbilityKey);
            AbilityAttributes aft=aftDefinition==null?null:aftDefinition.Get<AbilityAttributes>();
            if(aft==null||aft.AbilityType!=AbilityClass.ApplyStatusEffect||(aft.TargetingType&AbilityTargetingType.Passive)==0||aft.ResearchDependencies.RequiredResearch.Length!=1||aft.ResearchDependencies.RequiredResearch[0]!=RhodesResearch.Gunnery2)
                throw new InvalidOperationException("Rhodes native aft unlock registry/dependency contract failed");
            string[] abilities={SupportAbilityKey,SupportStarboardAbilityKey},ids={SupportWeaponID,SupportStarboardWeaponID};
            ScriptableObject[] commands=new ScriptableObject[2];
            UnitAttributes core=(UnitAttributes)Read(c.Core,"m_Attributes");UnitViewAttributes view=(UnitViewAttributes)Read(c.View,"Data");
            for(int station=0;station<2;station++)
            {
                EntityTypeAttributes definition=Registry.GetEntityType(abilities[station]);
                AbilityAttributes repair=definition==null?null:definition.Get<AbilityAttributes>();
                if(repair==null||repair.AbilityType!=AbilityClass.RepairTarget||repair.Repair.WeaponID!=ids[station]||repair.ResearchDependencies.RequiredResearch.Length!=0)
                    throw new InvalidOperationException("Rhodes repair registry/dependency contract failed: "+ids[station]);
                foreach(ScriptableObject command in c.Commands)if(command.name==abilities[station])commands[station]=command;
                bool found=false;foreach(WeaponBinding binding in core.WeaponLoadout)
                    if(binding.WeaponID==ids[station])
                    {
                        if(found||binding.Weapon.DamageType!=DamageType.Heal)throw new InvalidOperationException("Rhodes repair weapon contract failed: "+ids[station]);found=true;
                        UnitViewAttributes.TurretAttributes turret=view.TurretDefinitions[binding.TurretIndex];
                        string side=station==0?"PORT":"STARBOARD";
                        if(turret.BoneName!="EF_RHODES_REPAIR_"+side+"_YAW"||turret.CenterEmissionBone!="EF_RHODES_REPAIR_"+side+"_EMITTER")throw new InvalidOperationException("Rhodes repair emitter bound to wrong station");
                        RequireRhodesWeaponBone(view.ModelPrefab,turret.BoneName);RequireRhodesWeaponBone(view.ModelPrefab,turret.CenterEmissionBone);
                    }
                if(!found)throw new InvalidOperationException("Rhodes native repair weapon missing: "+ids[station]);
            }
            VerifySupportAbilityIsolation(commands[0],commands[1]);
        }
        static void VerifyRhodesWeaponFX(Context c)
        {
            int combatEffects=0,repairEffects=0;
            foreach(WeaponAttributesAsset weapon in (WeaponAttributesAsset[])Read(c.Custom,"m_Weapons"))
            {
                WeaponViewAttributes visual=(WeaponViewAttributes)Read(weapon,"m_View");
                if(visual==null||visual.WeaponFX==null)throw new InvalidOperationException("Private native weapon FX data missing: "+weapon.name);
                if(weapon.name==SupportWeaponKey)repairEffects+=visual.WeaponFX.Length;
                else{if(visual.WeaponFX.Length==0)throw new InvalidOperationException("Native combat weapon donor has no FX: "+weapon.name);combatEffects+=visual.WeaponFX.Length;}
            }
            Runtime.Log("RHODES_NATIVE_WEAPON_FX_BOUND combatEffects="+combatEffects+"; repairEffects="+repairEffects+"; weaponViewFXRetained=True; explicitTurretEmitterBones=True; donorHullUnitEffectsExcluded=True; V05FixedGuideAviation=True; animatedElevatorAcceptancePending=True; runtimeFXAcceptancePending=True");
        }
    }
}
