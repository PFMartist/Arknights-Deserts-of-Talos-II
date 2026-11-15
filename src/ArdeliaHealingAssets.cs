using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using BBI.Core.Data;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Clone-only native G baserunner Self/AoE/status chain. No scripted health
    // assignments, frame scan, custom ticking damage or global native mutation.
    public static class ArdeliaHealingAssets
    {
        public const string UnitKey="EF_Ardelia_Test",AbilityKey="EF_Ardelia_HealingField";
        public const string WeaponKey="EF_Ardelia_HealingField_Weapon",WeaponID="EF_ARDELIA_HEALING";
        public const string ProjectileKey="EF_Ardelia_HealingField_Projectile",HealStatusKey="EF_Ardelia_HealingPulse";
        public const int WeaponIndex=1;
        const string DonorAbility="Ability_G_BaseRunnerActiveRegen",DonorWeapon="G_Baserunner_Weapon_HealingAura_MP";
        const string DonorProjectile="G_BaserunnerHealing_ProjectileAssetContainer_MP";
        static EntityTypeCollection Registry;
        static Context Current;
        public static bool Ready{get;private set;}
        static readonly MethodInfo CloneMethod=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
        sealed class Context
        {
            internal UnitAssetContainer Custom;
            internal ScriptableObject Core,View,Hud,Ability,Weapon,WeaponPriorities,NativeWeaponPriorities,Projectile,ProjectileAttributes,HealStatus;
            internal readonly Dictionary<object,object> AssetMap=new Dictionary<object,object>(new IdentityComparer());
            internal readonly List<ScriptableObject> Owned=new List<ScriptableObject>();
            internal readonly List<SourceRecord> Sources=new List<SourceRecord>();
        }
        sealed class SourceRecord{internal ScriptableObject Asset;internal string Fingerprint;internal EntityTypeAttributes Definition;}
        sealed class IdentityComparer:IEqualityComparer<object>
        {public new bool Equals(object a,object b){return object.ReferenceEquals(a,b);}public int GetHashCode(object v){return RuntimeHelpers.GetHashCode(v);}}
        public static void Prepare(EntityTypeCollection registry,UnitAssetContainer custom,ScriptableObject core,ScriptableObject view,ScriptableObject hud,string ammoID)
        {
            if(registry==null||custom==null||core==null||view==null||hud==null||custom.name!=UnitKey)throw new ArgumentException("Ardelia private context required");
            if(Current!=null){if(Registry==registry&&Current.Custom==custom){VerifyRegistered();return;}throw new InvalidOperationException("Release stale Ardelia assets first");}
            foreach(string key in new string[]{AbilityKey,WeaponKey,ProjectileKey,HealStatusKey})if(registry.GetEntityType(key)!=null)throw new InvalidOperationException("Ardelia key collision: "+key);
            Registry=registry;Ready=false;Context c=new Context();Current=c;c.Custom=custom;c.Core=core;c.View=view;c.Hud=hud;
            ScriptableObject ability=FindRegistered(DonorAbility,"m_Attributes");
            UnitAttributes baserunner=registry.GetEntityType("G_Baserunner_MP").Get<UnitAttributes>();
            WeaponAttributes nativeWeapon=null;
            foreach(WeaponBinding binding in baserunner.WeaponLoadout)if(binding.WeaponID=="RegenAbility"){if(nativeWeapon!=null)throw new InvalidOperationException("Ambiguous native G healing weapon");nativeWeapon=binding.Weapon;}
            ScriptableObject weapon=null;
            foreach(ScriptableObject item in Resources.FindObjectsOfTypeAll(typeof(WeaponAttributesAsset)))
                if(item!=null&&item.name==DonorWeapon&&object.ReferenceEquals(Read(item,"m_Simulation"),nativeWeapon))weapon=item;
            if(weapon==null||nativeWeapon==null||nativeWeapon.DamageType!=DamageType.Heal)throw new InvalidOperationException("Native G healing weapon missing");
            c.Ability=CloneAsset(c,ability,AbilityKey);c.Weapon=CloneAsset(c,weapon,WeaponKey);
            // Native WeaponAttributesAsset.OnEnable rebuilds the simulation
            // priority reference from this serialized ScriptableObject. A deep
            // copy of m_Simulation alone is overwritten by that native rebuild.
            c.NativeWeaponPriorities=Read(weapon,"m_TargetPriorization") as ScriptableObject;
            if(c.NativeWeaponPriorities==null)throw new InvalidOperationException("Native G healing target priorities missing");
            c.WeaponPriorities=CloneAsset(c,c.NativeWeaponPriorities,WeaponKey+"_Priorities");
            EntityTypeAttributes projectileDefinition=registry.GetEntityType(DonorProjectile);ScriptableObject projectile=null;
            foreach(ScriptableObject item in Resources.FindObjectsOfTypeAll(typeof(ProjectileAssetContainer)))
                if(item!=null&&item.name==DonorProjectile&&projectileDefinition!=null&&object.ReferenceEquals(projectileDefinition.Get<ProjectileAttributes>(),Read(Read(item,"m_ProjectileAttributes"),"m_Data")))projectile=item;
            if(projectile==null)throw new InvalidOperationException("Native G healing projectile missing");
            c.Projectile=CloneAsset(c,projectile,ProjectileKey);
            c.ProjectileAttributes=CloneAsset(c,(ScriptableObject)Read(projectile,"m_ProjectileAttributes"),ProjectileKey+"_Attributes");
            Array nativeStatuses=(Array)Read(Read(Read(ability,"m_Attributes"),"m_ApplyStatusEffect"),"m_StatusEffectsToApply");
            ScriptableObject status=nativeStatuses.Length==1?nativeStatuses.GetValue(0) as ScriptableObject:null;
            if(status==null||status.name!="Regen_BaseRunner_StatusEffect")throw new InvalidOperationException("Native G regeneration status missing");
            c.HealStatus=CloneAsset(c,status,HealStatusKey);
            foreach(ScriptableObject item in c.Owned)RemapAsset(c,item);
            ConfigureStatus(Read(c.HealStatus,"m_Attributes"));
            ConfigureAbility(Read(c.Ability,"m_Attributes"),Read(c.Ability,"m_View"));
            ConfigureWeapon(Read(c.Weapon,"m_Simulation"),Read(c.Weapon,"m_View"),c.HealStatus);
            ConfigureProjectile(Read(c.ProjectileAttributes,"m_Data"));
            AttachAbility(c);AttachWeapon(c);AttachHUD(c);
            for(int i=c.Owned.Count-1;i>=0;i--){WriteCacheNull(c.Owned[i]);CallIfPresent(c.Owned[i],"OnEnable");}
            VerifyPrepared();
            foreach(ScriptableObject item in new[]{c.HealStatus,c.Ability,c.Weapon,c.Projectile})registry.Add(BuildDefinition(item));
            VerifyRegistered();Ready=true;
            Runtime.Log("ARDELIA_HEALING_ASSETS_READY nativeDonor=G_Baserunner_MP; selfTarget=True; radius=400; pulses=10; pulseMilliseconds=1000; healPerSecond=40; friendAndSelfOnly=True; directAoEHeal=0; nativeHoT=True; nativeReplaceOneStack=True; firstHealDelaySeconds=1; maxTailSeconds=1.125; heroCooldownSeconds=30; scriptHealthWrites=False; runtimeAcceptance=False");
        }
        internal static void ConfigureStatus(object data)
        {
            ((BBI.Core.NamedObjectBase)data).InitializeName(HealStatusKey);
            SetEnum(data,"m_Lifetime",0);Write(data,"m_Duration",ArdeliaHealingProfile.HealLeaseSeconds);
            SetEnum(data,"m_WeaponFireTriggerEndEvent",0);Write(data,"m_MaxStacks",1);SetEnum(data,"m_StackingBehaviour",0);
            ClearArray(data,"m_BuffsToApplyToTarget");ClearArray(data,"m_UnitTypeBuffsToApply");SetEnum(data,"m_FXSetStatesWhenActive",0);
            ModifierAttributes modifier=new ModifierAttributes();modifier.ModifierType=ModifierType.HealthOverTime;
            modifier.HealthOverTimeAttributes=new HealthOverTimeAttributes{ID=HealStatusKey,Amount=ArdeliaHealingProfile.HealPerSecond,MSTickDuration=1000,DamageType=DamageType.Heal};
            Write(data,"m_Modifiers",new[]{modifier});
        }
        internal static void ConfigureAbility(object data,object view)
        {
            SetEnum(data,"m_AbilityType",90);SetEnum(data,"m_TargetingType",1);SetEnum(data,"m_TargetAlignment",0);
            Write(data,"m_CooldownTimeSecs",(float)ArdeliaHealingProfile.CooldownSeconds);Write(data,"m_WarmupTimeSecs",0f);
            Write(data,"m_CasterMovesToTarget",false);Write(data,"m_IsToggleable",false);Write(data,"m_CastOnDeath",false);
            SetEnum(data,"m_StartsRemovedIn",0);SetEnum(data,"m_SharedCooldownChannel",0);
            Write(Read(data,"m_Cost"),"m_Resource1Cost",0);Write(Read(data,"m_Cost"),"m_Resource2Cost",0);
            Write(Read(data,"m_Charges"),"m_ChargesInventoryID",string.Empty);
            Write(Read(data,"m_Autocast"),"m_AutocastEnabled",false);Write(Read(data,"m_Autocast"),"m_AutocastEnabledOnSpawn",false);
            ClearArray(Read(data,"m_ResearchDeps"),"m_DependentResearch");ClearArray(Read(data,"m_ApplyStatusEffect"),"m_StatusEffectsToApply");
            Write(Read(data,"m_UseWeapon"),"m_WeaponID",WeaponID);Write(Read(data,"m_UseWeapon"),"m_ClearGoalsOnUse",false);
            Write(view,"LocalizedTitleStringID","丰饶之境");Write(view,"LocalizedShortDescriptionStringID","持续治疗自身周围的友军");
            Write(view,"LocalizedLongDescriptionStringID","在自身周围400米范围维持治疗光环，持续10秒。每秒恢复自身和友军40点生命，同类治疗不叠加。移动不会中断已生效的治疗光环，范围随施法者移动。首次恢复需等待1秒，离开范围后治疗短暂保留。冷却30秒，可在旋翼机上使用。");
            Write(view,"IconName","Icon-Command-Regen");SetEnum(view,"Hotkey",(int)KeyCode.Q);Write(view,"DrawPreviewRouteLine",false);
            Write(Read(view,"TargetingReticle"),"m_ReticleEnabled",false);Write(Read(view,"VOFeedback"),"DialogueCode",string.Empty);
            OperatorSkillIcons.Configure(view,UnitKey);
        }
        internal static void ConfigureWeapon(object data,object view,ScriptableObject status)
        {
            SetEnum(data,"m_DamageType",1);Write(data,"m_BaseDamagePerRound",0);Write(data,"m_BaseWreckDamagePerRound",0);
            Write(data,"m_AreaOfEffectMeters",(float)ArdeliaHealingProfile.RadiusMetres);SetEnum(data,"m_AreaOfEffectFalloffType",0);
            Write(data,"m_ExcludeWeaponOwnerFromAreaOfEffect",false);Write(data,"m_ExcludeFromAutoFire",true);Write(data,"m_ExcludeFromAutoTargetAcquisition",true);
            Write(data,"m_WindUpTime",ArdeliaHealingProfile.WeaponWindupMilliseconds);Write(data,"m_RateOfFire",1);Write(data,"m_NumberOfBursts",10);Write(data,"m_DamagePacketsPerShot",1);
            Write(data,"m_BurstPeriodMinTime",1000);Write(data,"m_BurstPeriodMaxTime",1000);Write(data,"m_CooldownTime",0);Write(data,"m_ReloadTime",0);Write(data,"m_WindDownTime",500);
            Write(data,"m_LineOfSightRequired",false);Write(data,"m_ProjectileEntityTypeToSpawn",ProjectileKey);Write(data,"m_RevealTriggers",Enum.ToObject(Read(data,"m_RevealTriggers").GetType(),0));
            SetEnum(data,"m_UnitStatusAttackingTriggers",0);ClearArray(data,"m_Modifiers");ClearArray(data,"m_EntityTypesToSpawnOnImpact");
            SetEnum(data,"m_StatusEffectsTargetAlignment",5);SetEnum(data,"m_StatusEffectsExcludeTargetType",0);Write(data,"m_StatusEffectsStartingIndex",0);
            FieldInfo field=Fields(data.GetType()).Find(delegate(FieldInfo f){return f.Name=="m_StatusEffectsToApplyOnTargets";});
            if(field==null||!field.FieldType.IsArray)throw new InvalidOperationException("Native healing status-set contract changed");
            Type entryType=field.FieldType.GetElementType();object entry=Activator.CreateInstance(entryType,true);
            FieldInfo list=Fields(entryType).Find(delegate(FieldInfo f){return f.Name=="m_StatusEffects";});
            if(list==null||!list.FieldType.IsArray)throw new InvalidOperationException("Native healing status-name list contract changed");
            Type element=list.FieldType.GetElementType();Array values=Array.CreateInstance(element,1);
            if(element==typeof(string))values.SetValue(HealStatusKey,0);
            else if(element.IsInstanceOfType(status))values.SetValue(status,0);
            else throw new InvalidOperationException("Unsupported native status-set item: "+element.FullName);
            list.SetValue(entry,values);Array sets=Array.CreateInstance(entryType,1);sets.SetValue(entry,0);field.SetValue(data,sets);
            Write(view,"ImpactForce",0f);ClearArray(view,"WeaponFX");
        }
        static void ConfigureProjectile(object data)
        {
            Write(data,"m_DetonationDelay",0f);Write(data,"m_MinimumMissDistance",0f);Write(data,"m_MaximumMissDistance",0f);
            Array stages=(Array)Read(data,"m_MotionStages");
            foreach(object stage in stages){ClearArray(stage,"FXIntroDefinitions");ClearArray(stage,"FXOutroDefinitions");}
            // Preserve the native empty projectile model: ProjectileView.Spawn
            // dereferences ModelPrefab. Only the donor healing FX are removed.
            object view=Read(data,"m_ViewAttributes");if(Read(view,"m_ModelPrefab")==null)throw new InvalidOperationException("Native healing projectile model missing");Write(view,"m_OnArrivalFXPrefab",null);Write(view,"m_DetonationFXPrefab",null);
        }
        static void AttachAbility(Context c)
        {
            Array old=(Array)Read(c.Custom,"m_Abilities");if(old==null)throw new InvalidOperationException("Ardelia basic ability array missing");
            Array all=Array.CreateInstance(old.GetType().GetElementType(),old.Length+1);Array.Copy(old,all,old.Length);all.SetValue(c.Ability,old.Length);Write(c.Custom,"m_Abilities",all);
        }
        static void AttachWeapon(Context c)
        {
            Array old=(Array)Read(c.Core,"m_WeaponLoadout");if(old==null||old.Length!=1)throw new InvalidOperationException("Healing expects exactly one basic Ardelia weapon");
            object binding=CopyData(old.GetValue(0),new Dictionary<object,object>(new IdentityComparer()));
            Write(binding,"m_WeaponID",WeaponID);Write(binding,"m_Weapon",c.Weapon);Write(binding,"m_AmmoID",string.Empty);Write(binding,"m_TurretIndex",0);Write(binding,"m_TurretDefaultAngleOffsetDegrees",0f);
            // ForceFire bypasses ExcludeFromAutoFire, not DisabledOnSpawn.
            Write(binding,"m_DisabledOnSpawn",false);Write(binding,"m_ShowAmmoOnHUD",false);
            Array all=Array.CreateInstance(old.GetType().GetElementType(),2);all.SetValue(old.GetValue(0),0);all.SetValue(binding,1);Write(c.Core,"m_WeaponLoadout",all);
            Array weapons=(Array)Read(c.Custom,"m_Weapons");Array combined=Array.CreateInstance(weapons.GetType().GetElementType(),weapons.Length+1);Array.Copy(weapons,combined,weapons.Length);combined.SetValue(c.Weapon,weapons.Length);Write(c.Custom,"m_Weapons",combined);
        }
        static void AttachHUD(Context c)
        {
            Array contexts=(Array)Read(Read(c.Hud,"m_Attributes"),"m_HUDContexts");bool found=false;
            foreach(object context in contexts)
            {
                if((string)Read(context,"m_ContextName")!="Default")continue;found=true;
                string[] old=(string[])Read(context,"m_AbilityOrder");
                if(Array.IndexOf(old,AbilityKey)>=0)throw new InvalidOperationException("Healing HUD entry already exists");
                string[] all=(string[])old.Clone();int gap=Array.IndexOf(all,string.Empty);
                if(gap<0){all=new string[old.Length+1];Array.Copy(old,all,old.Length);gap=old.Length;}
                all[gap]=AbilityKey;Write(context,"m_AbilityOrder",all);
            }
            if(!found)throw new InvalidOperationException("Ardelia native default HUD context absent");
        }
        static void VerifyPrepared()
        {
            Context c=Current;if(c==null||Registry==null)throw new InvalidOperationException("Healing context absent");
            List<ScriptableObject> sources=new List<ScriptableObject>();
            foreach(SourceRecord source in c.Sources){if(Fingerprint(source.Asset)!=source.Fingerprint||!object.ReferenceEquals(Registry.GetEntityType(source.Asset.name),source.Definition))throw new InvalidOperationException("Native healing source changed: "+source.Asset.name);sources.Add(source.Asset);}
            AssertNoSharedMutable(sources,c.Owned);
            VerifyPriorityCache(c.Weapon,c.WeaponPriorities,c.NativeWeaponPriorities);
            AbilityAttributes ability=(AbilityAttributes)Read(c.Ability,"m_Attributes");WeaponAttributes weapon=(WeaponAttributes)Read(c.Weapon,"m_Simulation");
            StatusEffectAttributes status=(StatusEffectAttributes)Read(c.HealStatus,"m_Attributes");
            if(ability.AbilityType!=AbilityClass.UseWeapon||ability.TargetingType!=AbilityTargetingType.Self||ability.UseWeapon.WeaponID!=WeaponID||ability.UseWeapon.ClearGoalsOnUse||
               Fixed64.UnsafeFloatValue(ability.CooldownTimeSecs)!=ArdeliaHealingProfile.CooldownSeconds)throw new InvalidOperationException("Private Ardelia native Self ability contract differs");
            if(weapon.DamageType!=DamageType.Heal||weapon.BaseDamagePerRound!=Fixed64.Zero||Convert.ToSingle(Read(weapon,"m_AreaOfEffectMeters"))!=400||weapon.ProjectileEntityTypeToSpawn!=ProjectileKey||
               Convert.ToInt32(weapon.StatusEffectsTargetAlignment)!=5||weapon.StatusEffectsSets.Length!=1||weapon.StatusEffectsSets[0].StatusEffects.Length!=1||weapon.StatusEffectsSets[0].StatusEffects[0]!=HealStatusKey)
                throw new InvalidOperationException("Private Ardelia ally-only native AoE status contract differs");
            if(Convert.ToInt32(status.StackingBehaviour)!=0||status.MaxStacks!=1||Fixed64.UnsafeFloatValue(status.Duration)!=ArdeliaHealingProfile.HealLeaseSeconds||status.Modifiers.Length!=1||
               status.Modifiers[0].ModifierType!=ModifierType.HealthOverTime||status.Modifiers[0].HealthOverTimeAttributes.Amount!=40||status.Modifiers[0].HealthOverTimeAttributes.MSTickDuration!=1000||status.Modifiers[0].HealthOverTimeAttributes.ID!=HealStatusKey)
                throw new InvalidOperationException("Private Ardelia native HoT contract differs");
            Array loadout=(Array)Read(c.Core,"m_WeaponLoadout");
            if(loadout.Length!=2||(string)Read(loadout.GetValue(1),"m_WeaponID")!=WeaponID||!object.ReferenceEquals(Read(loadout.GetValue(1),"m_Weapon"),c.Weapon))throw new InvalidOperationException("Private Ardelia weapon attachment differs");
        }
        public static void VerifyRegistered()
        {
            VerifyPrepared();Context c=Current;
            foreach(ScriptableObject item in new[]{c.HealStatus,c.Ability,c.Weapon,c.Projectile})
                if(Registry.GetEntityType(item.name)==null)throw new InvalidOperationException("Ardelia registration missing: "+item.name);
        }
        internal static void VerifyPriorityCache(object weaponAsset,object privatePriorities,object nativePriorities)
        {
            if(weaponAsset==null||privatePriorities==null||nativePriorities==null||object.ReferenceEquals(privatePriorities,nativePriorities))
                throw new InvalidOperationException("Healing priority closure must own a distinct native-data asset");
            object privateData=Read(privatePriorities,"Data"),nativeData=Read(nativePriorities,"Data");
            object simulation=Read(weaponAsset,"m_Simulation");
            if(privateData==null||nativeData==null||simulation==null||object.ReferenceEquals(privateData,nativeData)||
                !object.ReferenceEquals(Read(weaponAsset,"m_TargetPriorization"),privatePriorities)||
                !object.ReferenceEquals(Read(simulation,"m_TargetPriorization"),privateData))
                throw new InvalidOperationException("Healing weapon priority cache does not reference its private serialized priority Data");
            // Current native priority data are nine scalar weights. Retain the
            // full graph check if that serialized schema later gains arrays.
            HashSet<object> original=new HashSet<object>(new IdentityComparer()),seen=new HashSet<object>(new IdentityComparer());
            Gather(nativeData,original);Check(privateData,original,seen);
            RequirePriorityGraphValues(privateData,new HashSet<object>(new IdentityComparer()));
        }
        static void RequirePriorityGraphValues(object value,HashSet<object> seen)
        {
            if(value==null)throw new InvalidOperationException("Healing target-priority graph contains a null value");
            Type type=value.GetType();
            if(type.IsPrimitive||type.IsEnum||type==typeof(string)||typeof(UnityEngine.Object).IsAssignableFrom(type)||!seen.Add(value))return;
            Array values=value as Array;
            if(values!=null){foreach(object item in values)RequirePriorityGraphValues(item,seen);return;}
            if(type.Namespace!=null&&type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))
                foreach(FieldInfo field in Fields(type))RequirePriorityGraphValues(field.GetValue(value),seen);
        }
        public static void Release()
        {
            if(Current!=null)
            {
                foreach(UnitView view in Resources.FindObjectsOfTypeAll(typeof(UnitView)))if(view!=null&&view.gameObject.activeInHierarchy&&Current.Core!=null&&object.ReferenceEquals(view.UnitAttrs,Read(Current.Core,"m_Attributes")))throw new InvalidOperationException("Healing assets still in use by active Ardelia");
                for(int i=Current.Owned.Count-1;i>=0;i--)if(Current.Owned[i]!=null)UnityEngine.Object.DestroyImmediate(Current.Owned[i]);
            }
            Current=null;Registry=null;Ready=false;
        }
        static ScriptableObject FindRegistered(string key,string field)
        {
            EntityTypeAttributes definition=Registry.GetEntityType(key);
            if(definition==null)throw new InvalidOperationException("Required native orbital registry key missing: "+key);
            foreach(ScriptableObject candidate in Resources.FindObjectsOfTypeAll(typeof(ScriptableObject)))
            {
                if(candidate==null||candidate.name!=key)continue;
                object data=Read(candidate,field);
                if((key==DonorAbility&&object.ReferenceEquals(definition.Get<AbilityAttributes>(),data))||
                    (key==DonorWeapon&&object.ReferenceEquals(definition.Get<WeaponAttributes>(),data)))return candidate;
            }
            throw new InvalidOperationException("Loaded native orbital asset differs from current registry: "+key);
        }
        static void SetEnum(object value,string name,int number)
        {object old=Read(value,name);Write(value,name,old.GetType().IsEnum?Enum.ToObject(old.GetType(),number):(object)number);}
        static void WriteCacheNull(object value)
        {foreach(FieldInfo field in Fields(value.GetType()))if(field.Name=="mDataCache")field.SetValue(value,null);}
        static EntityTypeAttributes BuildDefinition(ScriptableObject asset)
        {
            EntityTypeAttributes definition=new EntityTypeAttributes(asset.name);object data=null;IEnumerable many=asset as IEnumerable;
            if(many==null){data=Read(asset,"Data");many=data as IEnumerable;}
            if(many!=null){foreach(object value in many)if(value!=null&&!definition.Add(value))throw new InvalidOperationException("Duplicate orbital datum type");}
            else if(data==null||!definition.Add(data))throw new InvalidOperationException("Orbital asset has no datum");return definition;
        }
        static ScriptableObject CloneAsset(Context c, ScriptableObject source, string name)
        {
            if (source == null) throw new InvalidOperationException("Required orbital asset absent: " + name);
            object previous; if (c.AssetMap.TryGetValue(source, out previous)) return (ScriptableObject)previous;
            c.Sources.Add(new SourceRecord { Asset = source, Fingerprint = Fingerprint(source), Definition = Registry.GetEntityType(source.name) });
            ScriptableObject clone = RhodesAssetClone59.CreateEmpty(source.GetType()); c.AssetMap.Add(source, clone);
            Dictionary<object, object> copied = new Dictionary<object, object>(new IdentityComparer());
            foreach (FieldInfo field in Fields(source.GetType())) if (field.Name != "mDataCache") field.SetValue(clone, CopyData(field.GetValue(source), copied));
            Own(c, clone, name); return clone;
        }
        static void Own(Context c, ScriptableObject asset, string name) { asset.name = name; asset.hideFlags = HideFlags.HideAndDontSave; c.Owned.Add(asset); }
        static object CopyData(object value, Dictionary<object, object> seen)
        {
            if (value == null) return null; Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || typeof(UnityEngine.Object).IsAssignableFrom(type)) return value;
            object previous; if (seen.TryGetValue(value, out previous)) return previous;
            Array array = value as Array;
            if (array != null)
            { if (array.Rank != 1) throw new InvalidOperationException("Unexpected orbital multidimensional array"); Array clone = (Array)array.Clone(); seen.Add(value, clone); for (int i = 0; i < array.Length; i++) clone.SetValue(CopyData(array.GetValue(i), seen), i); return clone; }
            if (type.Namespace == null || !type.Namespace.StartsWith("BBI.", StringComparison.Ordinal)) return value;
            object result = CloneMethod.Invoke(value, null); seen.Add(value, result);
            foreach (FieldInfo field in Fields(type)) field.SetValue(result, CopyData(field.GetValue(value), seen)); return result;
        }
        static void RemapAsset(Context c, ScriptableObject asset)
        { HashSet<object> seen = new HashSet<object>(new IdentityComparer()); foreach (FieldInfo field in Fields(asset.GetType())) if (field.Name != "mDataCache") field.SetValue(asset, Remap(c, field.GetValue(asset), seen)); }
        static object Remap(Context c, object value, HashSet<object> seen)
        {
            if (value == null) return null; object mapped; if (c.AssetMap.TryGetValue(value, out mapped)) return mapped;
            Type type = value.GetType(); if (type.IsPrimitive || type.IsEnum || type == typeof(string) || typeof(UnityEngine.Object).IsAssignableFrom(type) || !seen.Add(value)) return value;
            Array array = value as Array; if (array != null) { for (int i = 0; i < array.Length; i++) array.SetValue(Remap(c, array.GetValue(i), seen), i); return array; }
            if (type.Namespace != null && type.Namespace.StartsWith("BBI.", StringComparison.Ordinal)) foreach (FieldInfo field in Fields(type)) field.SetValue(value, Remap(c, field.GetValue(value), seen));
            return value;
        }
        static bool Mutable(object value)
        { if (value == null) return false; Type type = value.GetType(); return !type.IsValueType && type != typeof(string) && !typeof(UnityEngine.Object).IsAssignableFrom(type) && (value is Array || (type.Namespace != null && type.Namespace.StartsWith("BBI.", StringComparison.Ordinal))); }
        static void Gather(object value, HashSet<object> result)
        { if (!Mutable(value) || !result.Add(value)) return; Array array = value as Array; if (array != null) { foreach (object item in array) Gather(item, result); return; } foreach (FieldInfo field in Fields(value.GetType())) Gather(field.GetValue(value), result); }
        static void Check(object value, HashSet<object> sources, HashSet<object> seen)
        { if (!Mutable(value) || !seen.Add(value)) return; if (sources.Contains(value)) throw new InvalidOperationException("Orbital mutable data aliases another source: " + value.GetType().FullName); Array array = value as Array; if (array != null) { foreach (object item in array) Check(item, sources, seen); return; } foreach (FieldInfo field in Fields(value.GetType())) Check(field.GetValue(value), sources, seen); }
        static void AssertNoSharedMutable(List<ScriptableObject> sources, List<ScriptableObject> targets)
        { HashSet<object> original = new HashSet<object>(new IdentityComparer()), seen = new HashSet<object>(new IdentityComparer()); foreach (ScriptableObject source in sources) foreach (FieldInfo field in Fields(source.GetType())) Gather(field.GetValue(source), original); foreach (ScriptableObject target in targets) foreach (FieldInfo field in Fields(target.GetType())) Check(field.GetValue(target), original, seen); }
        static string Fingerprint(ScriptableObject asset)
        { StringBuilder text = new StringBuilder(); HashSet<object> seen = new HashSet<object>(new IdentityComparer()); foreach (FieldInfo field in Fields(asset.GetType())) { text.Append(field.DeclaringType.FullName).Append('.').Append(field.Name).Append('='); AppendValue(text, field.GetValue(asset), seen); } using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", ""); }
        static void AppendValue(StringBuilder text, object value, HashSet<object> seen)
        {
            if (value == null) { text.Append("null;"); return; } Type type = value.GetType(); text.Append(type.FullName).Append(':'); UnityEngine.Object unity = value as UnityEngine.Object;
            if (!object.ReferenceEquals(unity, null)) { text.Append(unity.GetInstanceID()).Append('/').Append(unity.name).Append(';'); return; }
            if (type.IsPrimitive || type.IsEnum || type == typeof(string)) { text.Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append(';'); return; }
            if (type.IsValueType) { foreach (FieldInfo field in Fields(type)) { text.Append(field.Name).Append('='); AppendValue(text, field.GetValue(value), seen); } return; }
            if (!seen.Add(value)) { text.Append("seen;"); return; } Array array = value as Array;
            if (array != null) { text.Append(array.Length).Append('['); foreach (object item in array) AppendValue(text, item, seen); text.Append(']'); return; }
            if (type.Namespace != null && type.Namespace.StartsWith("BBI.", StringComparison.Ordinal)) foreach (FieldInfo field in Fields(type)) { text.Append(field.Name).Append('='); AppendValue(text, field.GetValue(value), seen); }
            else text.Append(RuntimeHelpers.GetHashCode(value));
        }
        static List<FieldInfo> Fields(Type type)
        { List<FieldInfo> result = new List<FieldInfo>(); for (Type current = type; current != null && current != typeof(object) && current != typeof(UnityEngine.Object) && current != typeof(ScriptableObject); current = current.BaseType) foreach (FieldInfo field in current.GetFields(Flags)) if (!field.IsStatic) result.Add(field); result.Sort(delegate(FieldInfo a, FieldInfo b) { return string.CompareOrdinal(a.DeclaringType.FullName + "." + a.Name, b.DeclaringType.FullName + "." + b.Name); }); return result; }
        static object Read(object value, string name)
        { if (value == null) throw new ArgumentNullException(name); for (Type type = value.GetType(); type != null; type = type.BaseType) { FieldInfo field = type.GetField(name, Flags); if (field != null) return field.GetValue(value); PropertyInfo property = type.GetProperty(name, Flags); if (property != null) return property.GetValue(value, null); } throw new MissingMemberException(value.GetType().FullName, name); }
        static void ClearArray(object value, string name)
        {
            foreach (FieldInfo field in Fields(value.GetType())) if (field.Name == name)
            {
                if (!field.FieldType.IsArray) throw new InvalidDataException("Expected orbital array: " + name);
                field.SetValue(value, Array.CreateInstance(field.FieldType.GetElementType(), 0)); return;
            }
            throw new MissingFieldException(value.GetType().FullName, name);
        }
        static void Write(object value, string name, object data)
        { for (Type type = value.GetType(); type != null; type = type.BaseType) { FieldInfo field = type.GetField(name, Flags); if (field != null) { field.SetValue(value, data); return; } } throw new MissingFieldException(value.GetType().FullName, name); }
        static bool CallIfPresent(object value, string name)
        { for (Type type = value.GetType(); type != null; type = type.BaseType) { MethodInfo method = type.GetMethod(name, Flags, null, Type.EmptyTypes, null); if (method != null) { method.Invoke(value, null); return true; } } return false; }
        static void CallRequired(object value, string name) { if (!CallIfPresent(value, name)) throw new MissingMethodException(value.GetType().FullName, name); }
        static Type NativeType(string name) { return typeof(WeaponAttributesAsset).Assembly.GetType("BBI.Unity.Game.Data." + name, true); }
    }
}
