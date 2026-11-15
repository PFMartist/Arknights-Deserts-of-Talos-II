// Native RepairTarget + native heal weapon. Uses append-only helpers in
// RhodesCarrierWeapons.CANDIDATE.cs; never changes Health from script.
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
    public static partial class RhodesCarrierUnits
    {
        public const string SupportAbilityKey="Ability_EF_Rhodes_Repair";
        public const string SupportWeaponKey="EF_Rhodes_Weapon_Repair";
        public const string SupportWeaponID="EF_RHODES_REPAIR";
        public const string SupportStarboardAbilityKey="Ability_EF_Rhodes_Repair_Starboard";
        public const string SupportStarboardWeaponID="EF_RHODES_REPAIR_STARBOARD";
        public const float SupportFieldOfViewDegrees=178f;
        public const float SupportFieldOfFireDegrees=6f;
        public const float SupportRotationDegreesPerSecond=120f;
        const string SupportDonorKey="C_SupportCruiser_MP";
        public const string GunneryEnableAbilityKey="Ability_EF_Rhodes_EnableMainBattery";
        public const string GunneryEnableStatusKey="EF_Rhodes_GunneryEnable_StatusEffect";
        public const string GunneryAftEnableAbilityKey="Ability_EF_Rhodes_EnableAftBattery";
        public const string GunneryAftEnableStatusKey="EF_Rhodes_GunneryAftEnable_StatusEffect";
        // Native passive warmup begins only after the Gunnery1 research
        // prerequisite passes. Same V09 fast motion contract:21.5s
        // deployment plus one native tick; visual code never enables weapons.
        public const float GunneryNativeWarmupSeconds=RhodesDeploymentProfile.MainWarmupSeconds;
        // Native passive -> native status modifier -> disabled weapon enable.
        // A research completion event never directly changes weapon flags.
        static void PrepareGunneryUnlock(Context c)
        {
            ScriptableObject donor=null;
            EntityTypeAttributes sourceDefinition=Registry.GetEntityType("Ability_C_SupportCruiser_AA");
            AbilityAttributes expected=sourceDefinition==null?null:sourceDefinition.Get<AbilityAttributes>();
            foreach(ScriptableObject candidate in Resources.FindObjectsOfTypeAll(typeof(ScriptableObject)))
                if(candidate!=null&&candidate.name=="Ability_C_SupportCruiser_AA"&&object.ReferenceEquals(Read(candidate,"m_Attributes"),expected))donor=candidate;
            if(donor==null||expected==null||expected.AbilityType!=AbilityClass.ApplyStatusEffect||(expected.TargetingType&AbilityTargetingType.Passive)==0)
                throw new InvalidOperationException("Native passive weapon-enable ability donor changed");
            Array sourceEffects=(Array)Read(Read(Read(donor,"m_Attributes"),"m_ApplyStatusEffect"),"m_StatusEffectsToApply");
            if(sourceEffects.Length!=1)throw new InvalidOperationException("Support AA must use one native status effect");
            ScriptableObject sourceEffect=sourceEffects.GetValue(0) as ScriptableObject;
            if(sourceEffect==null||sourceEffect.name!="SupportCruiserAA_StatusEffect")throw new InvalidOperationException("Native enable weapon status donor changed");
            Array sourceModifiers=(Array)Read(Read(sourceEffect,"m_Attributes"),"m_Modifiers");
            if(sourceModifiers.Length!=1||Convert.ToInt32(Read(sourceModifiers.GetValue(0),"ModifierType"))!=4)
                throw new InvalidOperationException("Native support AA must use EnableWeapon modifier type4");
            for(int station=0;station<2;station++)
            {
                string statusKey=station==0?GunneryEnableStatusKey:GunneryAftEnableStatusKey;
                string abilityKey=station==0?GunneryEnableAbilityKey:GunneryAftEnableAbilityKey;
                string dependency=station==0?RhodesResearch.Gunnery1:RhodesResearch.Gunnery2;
                ScriptableObject status=station==0?CloneAsset(c,sourceEffect,statusKey):CloneSupportAbilitySibling(c,sourceEffect,statusKey);
                object statusData=Read(status,"m_Attributes");((NamedObjectBase)statusData).InitializeName(statusKey);
                Array modifiers=Array.CreateInstance(sourceModifiers.GetType().GetElementType(),1);
                object modifier=CopyData(sourceModifiers.GetValue(0),new Dictionary<object,object>(new IdentityComparer()));
                object enable=Read(modifier,"EnableWeaponAttributes");Write(enable,"WeaponID",station==0?MainForeWeaponID:MainAftWeaponID);Write(modifier,"EnableWeaponAttributes",enable);modifiers.SetValue(modifier,0);
                Write(statusData,"m_Modifiers",modifiers);
                ClearArray(statusData,"m_BuffsToApplyToTarget");ClearArray(statusData,"m_UnitTypeBuffsToApply");
                RemapAsset(c,status);CallIfPresent(status,"OnEnable");
                ScriptableObject ability=station==0?CloneAsset(c,donor,abilityKey):CloneSupportAbilitySibling(c,donor,abilityKey);RemapAsset(c,ability);
                object data=Read(ability,"m_Attributes"),view=Read(ability,"m_View");
                ((NamedObjectBase)data).InitializeName(abilityKey);((NamedObjectBase)view).InitializeName(abilityKey);
                object apply=Read(data,"m_ApplyStatusEffect");Array prior=(Array)Read(apply,"m_StatusEffectsToApply");Array own=Array.CreateInstance(prior.GetType().GetElementType(),1);own.SetValue(status,0);Write(apply,"m_StatusEffectsToApply",own);
                SetProductionEnum(data,"m_StartsRemovedIn",0);Write(data,"m_CooldownTimeSecs",0f);Write(data,"m_WarmupTimeSecs",station==0?GunneryNativeWarmupSeconds:15.625f);
                SetProductionEnum(data,"m_TargetAlignment",4);Write(data,"m_EdgeOfTargetShapeMinDistance",0f);
                Write(Read(data,"m_ResearchDeps"),"m_DependentResearch",new string[]{dependency});
                Write(view,"LocalizedTitleStringID",station==0?"前主炮":"后主炮");Write(view,"LocalizedShortDescriptionStringID","对应炮位完成展开后，可打击远距离地面目标");
                Write(view,"LocalizedLongDescriptionStringID",station==0?"炮战改装 I 开放前主炮；后主炮接入独立展开后炮，前炮保持作战。主炮全装药将两座炮位的单发伤害由300提高至600，已展开模块保持就位。":"后主炮接入独立展开后炮。主炮全装药同时将前、后炮位的单发伤害由300提高至600；全装药研究可先于后炮接入完成，已展开模块保持就位。");Write(view,"ForcePassiveVisuals",true);
                object voice=Read(view,"VOFeedback");Write(voice,"DialogueCode",string.Empty);Write(view,"VOFeedback",voice);c.Commands.Add(ability);CallIfPresent(ability,"OnEnable");Registry.Add(ProductionAbilityDefinition(ability));
            }
            RebuildSupportCommands(c);
            Runtime.Log("RHODES_GUNNERY_UNLOCK_PREPARED separateForeAndAftNativePassives=True; foreResearch="+RhodesResearch.Gunnery1+"; aftResearch="+RhodesResearch.Gunnery2+"; foreWarmupSeconds=21.625; aftWarmupSeconds=15.625; queuedAftDeploymentHasIndependentFireGate=True; scriptWeaponEnableWrites=False; runtimeAcceptancePending=True");
        }

        // Both stations use native RepairTarget and weapon/autocast paths.
        // Shared immutable tuning, distinct binding IDs and mutable abilities.
        static void PrepareSupport(Context c,string portBone,string starboardBone)
        {
            UnitViewAttributes unitView=(UnitViewAttributes)Read(c.View,"Data");
            Transform port=RequireRhodesWeaponBone(unitView.ModelPrefab,portBone);
            Transform starboard=RequireRhodesWeaponBone(unitView.ModelPrefab,starboardBone);
            if(port==starboard)throw new InvalidOperationException("Rhodes repair stations require two distinct service anchors");
            if(Registry.GetEntityType(SupportAbilityKey)!=null||Registry.GetEntityType(SupportStarboardAbilityKey)!=null)
                throw new InvalidOperationException("Private repair ability already registered");
            UnitAssetContainer source=null;EntityTypeAttributes definition=Registry.GetEntityType(SupportDonorKey);
            UnitAttributes expected=definition==null?null:definition.Get<UnitAttributes>();
            if(expected==null)throw new InvalidOperationException("Native MP support cruiser definition absent");
            foreach(UnitAssetContainer candidate in Resources.FindObjectsOfTypeAll(typeof(UnitAssetContainer)))
                if(candidate!=null&&candidate.name==SupportDonorKey&&object.ReferenceEquals(Read(Read(candidate,"m_Attributes"),"m_Attributes"),expected))
                {if(source!=null)throw new InvalidOperationException("Multiple live native support cruiser donors");source=candidate;}
            if(source==null)throw new InvalidOperationException("Registered native support cruiser container is not loaded");
            ScriptableObject abilitySource=null,weaponSource=null;object donorBinding=null;
            foreach(ScriptableObject item in (Array)Read(source,"m_Abilities"))if(item!=null&&item.name=="Ability_C_Repair_MP")abilitySource=item;
            foreach(object item in (Array)Read(Read(source,"m_Attributes"),"m_WeaponLoadout"))if((string)Read(item,"m_WeaponID")=="REPAIR")
            {if(donorBinding!=null)throw new InvalidOperationException("Native support REPAIR binding is not unique");donorBinding=item;weaponSource=(ScriptableObject)Read(item,"m_Weapon");}
            if(abilitySource==null||weaponSource==null||weaponSource.name!="C_SupportCruiser_Weapon_Repair_Small_MP")throw new InvalidOperationException("Native MP repair donor contract changed");
            WeaponAttributes nativeWeapon=null;foreach(WeaponBinding binding in expected.WeaponLoadout)if(binding.WeaponID=="REPAIR")nativeWeapon=binding.Weapon;
            if(nativeWeapon==null||!object.ReferenceEquals(nativeWeapon,Read(weaponSource,"m_Simulation")))throw new InvalidOperationException("Repair weapon asset is not the authoritative registered donor");
            WeaponAttributesAsset weapon=(WeaponAttributesAsset)CloneAsset(c,weaponSource,SupportWeaponKey);
            // Native Weapon.OnEnable rehydrates this mutable datum from its
            // referenced asset; clone it before remap or isolation will fail.
            ScriptableObject priority=Read(weaponSource,"m_TargetPriorization") as ScriptableObject;
            if(priority!=null)CloneAsset(c,priority,UnitKey+"_RepairTargetPriorities");
            RemapAsset(c,weapon);
            object simulation=Read(weapon,"m_Simulation"),weaponView=Read(weapon,"m_View");
            ((NamedObjectBase)simulation).InitializeName(SupportWeaponKey);((NamedObjectBase)weaponView).InitializeName(SupportWeaponKey);
            if(((WeaponAttributes)simulation).DamageType!=DamageType.Heal)throw new InvalidOperationException("Repair donor is not a native heal weapon");
            // Outward non-overlapping centre-point sectors. Keep donor heal
            // rate/range/cooldown; do not duplicate two full-circle heal beams.
            // Native effective yaw span is (FoV-FoF), hence a narrow fire cone
            // with native yaw actuation rather than FoV=FoF=178 (zero travel).
            object turretData=Read(simulation,"m_Turret");
            Write(turretData,"m_FieldOfView",SupportFieldOfViewDegrees);
            Write(turretData,"m_FieldOfFire",SupportFieldOfFireDegrees);
            Write(turretData,"m_RotationSpeed",SupportRotationDegreesPerSecond);
            // Preserve donor auto-target/fire exclusions: native repair autocast
            // sets the per-WeaponID ForceAutoTargetAcquisiton flag itself.
            string[] names={"PORT","STARBOARD"},ids={SupportWeaponID,SupportStarboardWeaponID};
            Transform[] anchors={port,starboard};
            ScriptableObject portAbility=null,starboardAbility=null;
            for(int station=0;station<2;station++)
            {
                string suffix=names[station],yaw="EF_RHODES_REPAIR_"+suffix+"_YAW",emitter="EF_RHODES_REPAIR_"+suffix+"_EMITTER";
                PrepareSupportEmitter(anchors[station],yaw,emitter);
                UnitViewAttributes.TurretAttributes turret=new UnitViewAttributes.TurretAttributes();
                turret.BoneName=yaw;turret.PitchBoneName=string.Empty;turret.SimTurretOrigin=yaw;
                turret.FXEmissionBones=new string[]{emitter};turret.CenterEmissionBone=emitter;
                turret.PitchMin=0f;turret.PitchMax=0f;turret.MaxPitchDegreesPerSecond=0f;turret.MaxRollDegreesPerSecond=0f;
                turret.StateAnimationTriggers=new UnitViewAttributes.TurretOperatingAnimationTrigger[0];
                int turretIndex=AppendRhodesTurret(c,turret);
                // Simulation CCW offsets: +90 port (-modelX), -90 starboard.
                AppendRhodesWeapon(c,weapon,ids[station],turretIndex,station==0?90f:-90f,false);
                string key=station==0?SupportAbilityKey:SupportStarboardAbilityKey;
                // CloneAsset intentionally maps each canonical donor once.
                // A second call would return port; a map chain from private
                // port to starboard would incorrectly remap both commands.
                ScriptableObject ability=station==0?CloneAsset(c,abilitySource,key):CloneSupportAbilitySibling(c,abilitySource,key);
                RemapAsset(c,ability);
                object data=Read(ability,"m_Attributes"),view=Read(ability,"m_View");
                ((NamedObjectBase)data).InitializeName(key);((NamedObjectBase)view).InitializeName(key);
                Write(Read(data,"m_Repair"),"m_WeaponID",ids[station]);SetProductionEnum(data,"m_StartsRemovedIn",0);
                ClearArray(Read(data,"m_ResearchDeps"),"m_DependentResearch");
                Write(data,"m_CasterMovesToTarget",false);Write(data,"m_EdgeOfTargetShapeMinDistance",0f);
                Write(Read(data,"m_Autocast"),"m_AutocastEnabled",true);Write(Read(data,"m_Autocast"),"m_AutocastEnabledOnSpawn",true);
                Write(view,"LocalizedTitleStringID","维修站");Write(view,"LocalizedShortDescriptionStringID","自动维修两舷范围内的受损友军");
                Write(view,"LocalizedLongDescriptionStringID","两舷维修站可修复400米范围内的受损友军。右键图标切换自动维修，或按 R 指定目标，由对应舷侧的维修站响应。为损管系统分配能量可提高维修效率。");
                Write(view,"Hotkey",station==0?KeyCode.R:KeyCode.None);Write(view,"BindingIsExclusive",false);
                object voice=Read(view,"VOFeedback");Write(voice,"DialogueCode",string.Empty);Write(view,"VOFeedback",voice);
                c.Commands.Add(ability);CallIfPresent(ability,"OnEnable");Registry.Add(ProductionAbilityDefinition(ability));
                AbilityAttributes attrs=(AbilityAttributes)Read(ability,"m_Attributes");
                if(attrs.AbilityType!=AbilityClass.RepairTarget||attrs.Repair.WeaponID!=ids[station])throw new InvalidOperationException("Private native RepairTarget contract failed: "+suffix);
                if(station==0)portAbility=ability;else starboardAbility=ability;
            }
            VerifySupportAbilityIsolation(portAbility,starboardAbility);
            // Exposes repair pickup range to native fleet/AI repair behavior.
            Write(Read(c.Core,"m_Attributes"),"m_RepairPickupRange",400);
            RebuildSupportCommands(c);
            Runtime.Log("RHODES_NATIVE_REPAIR_PREPARED stations=2; abilityPort="+SupportAbilityKey+"; abilityStarboard="+SupportStarboardAbilityKey+"; weaponPort="+SupportWeaponID+"; weaponStarboard="+SupportStarboardWeaponID+"; servicePort="+portBone+"; serviceStarboard="+starboardBone+"; nativeHeal=True; nativeAutoCastBoth=True; visibleRepairIcons=1; portHotkey=R; starboardHotkey=None; outwardFOV=178; fireCone=6; donorHealRateRetained=True; nativeGoalAndFireOperation=True; scriptHealthWrites=False; perFrameRepairHooks=False; runtimeAcceptancePending=True");
        }
        static void PrepareSupportEmitter(Transform source,string yawName,string emitterName)
        {
            // Empty native actuation rig at the exact authored beam origin.
            // No render geometry or original model pivot is reparented/moved.
            GameObject yaw=new GameObject(yawName);yaw.layer=source.gameObject.layer;
            yaw.transform.parent=source.parent;yaw.transform.localPosition=source.localPosition;
            yaw.transform.localRotation=Quaternion.identity;yaw.transform.localScale=Vector3.one;
            GameObject emitter=new GameObject(emitterName);emitter.layer=source.gameObject.layer;
            emitter.transform.parent=yaw.transform;emitter.transform.localPosition=Vector3.zero;
            emitter.transform.localRotation=Quaternion.identity;emitter.transform.localScale=Vector3.one;
            if((emitter.transform.position-source.position).sqrMagnitude>.0001f)throw new InvalidOperationException("Repair emitter drifted from authored marker");
        }
        static ScriptableObject CloneSupportAbilitySibling(Context c,ScriptableObject source,string name)
        {
            if(!c.Map.ContainsKey(source))throw new InvalidOperationException("Canonical repair donor must be cloned first");
            ScriptableObject clone=RhodesAssetClone59.CreateEmpty(source.GetType());
            Dictionary<object,object> copied=new Dictionary<object,object>(new IdentityComparer());
            foreach(FieldInfo field in Fields(source.GetType()))if(field.Name!="mDataCache")field.SetValue(clone,CopyData(field.GetValue(source),copied));
            clone.name=name;clone.hideFlags=HideFlags.HideAndDontSave;c.Owned.Add(clone);return clone;
        }
        static void VerifySupportAbilityIsolation(ScriptableObject port,ScriptableObject starboard)
        {
            if(port==null||starboard==null||object.ReferenceEquals(port,starboard)||port.name!=SupportAbilityKey||starboard.name!=SupportStarboardAbilityKey)
                throw new InvalidOperationException("Repair ability sibling identity contract failed");
            object a=Read(port,"m_Attributes"),b=Read(starboard,"m_Attributes");
            if(object.ReferenceEquals(a,b)||object.ReferenceEquals(Read(port,"m_View"),Read(starboard,"m_View")))throw new InvalidOperationException("Repair abilities alias mutable root data");
            foreach(string field in new string[]{"m_Repair","m_Autocast","m_ResearchDeps"})
                if(object.ReferenceEquals(Read(a,field),Read(b,field)))throw new InvalidOperationException("Repair abilities alias mutable datum: "+field);
        }
        static void RebuildSupportCommands(Context c)
        {
            Array previous=(Array)Read(c.Custom,"m_Abilities");Array all=Array.CreateInstance(previous.GetType().GetElementType(),c.Commands.Count+c.Products.Count);int index=0;
            foreach(ScriptableObject command in c.Commands)all.SetValue(command,index++);foreach(ProductionItem product in c.Products)all.SetValue(product.Ability,index++);Write(c.Custom,"m_Abilities",all);
            foreach(object context in (Array)Read(Read(c.Hud,"m_Attributes"),"m_HUDContexts"))
            {
                List<string> order=new List<string>((string[])Read(context,"m_AbilityOrder"));
                // Native command icons are created only from this context list.
                // Keep the starboard ability active in the native command table,
                // but expose one canonical repair button and no second hotkey.
                order.RemoveAll(delegate(string key){return key==SupportStarboardAbilityKey;});
                foreach(ScriptableObject command in c.Commands)if((command.name==SupportAbilityKey||command.name==GunneryEnableAbilityKey)&&!order.Contains(command.name))order.Add(command.name);
                Write(context,"m_AbilityOrder",order.ToArray());
            }
        }

    }
}
