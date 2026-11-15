// Independent Perlica small orbital strike. Native ability/weapon/projectile
// simulation owns targeting, cooldown, delayed detonation and area damage.
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
    public static class PerlicaOrbitalAssets
    {
        public const string AbilityKey="EF_Perlica_OrbitalStrike";
        public const string WeaponKey="EF_Perlica_OrbitalStrike_Weapon";
        public const string ProjectileKey="EF_Perlica_OrbitalStrike_Projectile";
        public const string WeaponID="EF_ORBITAL_UPLINK";
        public const int WeaponIndex=1,RangeMeters=300,RadiusMeters=18,CenterDamage=1200,WindUpMilliseconds=2500,CooldownSeconds=20;
        public const float DetonationDelaySeconds=1f;
        public static bool Ready { get; private set; }
        const string DonorAbility="Ability_C_OrbitalWeapon_Narrow";
        const string DonorWeapon="C_Rachel_Weapon_Superweapon_Narrow";
        const string DonorProjectile="SuperWeapon_Narrow_ProjectileAssetContainer";
        static EntityTypeCollection Registry;
        static Context Current;
        static readonly MethodInfo CloneMethod=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
        sealed class Context
        {
            public UnitAssetContainer Custom;
            public ScriptableObject Core,View,Hud,Ability,Weapon,Projectile,ProjectileAttributes;
            public readonly List<GameObject> ReticlePrefabs=new List<GameObject>();
            public string AmmoID;
            public readonly Dictionary<object,object> AssetMap=new Dictionary<object,object>(new IdentityComparer());
            public readonly List<ScriptableObject> Owned=new List<ScriptableObject>();
            public readonly List<SourceRecord> Sources=new List<SourceRecord>();
        }
        sealed class SourceRecord { public ScriptableObject Asset;public string Fingerprint;public EntityTypeAttributes Definition; }
        sealed class IdentityComparer:IEqualityComparer<object>
        {
            public new bool Equals(object a,object b){return object.ReferenceEquals(a,b);}
            public int GetHashCode(object v){return RuntimeHelpers.GetHashCode(v);}
        }

        // Call once after the private Perlica model/core/HUD/basic commands are
        // prepared, but before the parent container's OnEnable and registration.
        public static void Prepare(EntityTypeCollection registry,UnitAssetContainer custom,ScriptableObject core,ScriptableObject view,ScriptableObject hud,string ammoID)
        {
            if(registry==null||custom==null||core==null||view==null||hud==null||string.IsNullOrEmpty(ammoID))throw new ArgumentException("Orbital attach inputs absent");
            if(custom.name!=HumanoidUnits.PerlicaKey)throw new InvalidOperationException("Orbital skill may attach only to private Perlica");
            if(Current!=null){if(Registry==registry&&object.ReferenceEquals(Current.Custom,custom)){VerifyRegistered();return;}throw new InvalidOperationException("Release stale orbital assets before another registry");}
            foreach(string key in new string[]{AbilityKey,WeaponKey,ProjectileKey})if(registry.GetEntityType(key)!=null)throw new InvalidOperationException("Orbital private key collision: "+key);
            Registry=registry;Ready=false;
            Context c=new Context();Current=c;c.Custom=custom;c.Core=core;c.View=view;c.Hud=hud;c.AmmoID=ammoID;
            ScriptableObject ability=FindRegistered(DonorAbility,"m_Attributes");
            ScriptableObject weapon=FindRachelWeapon();
            ScriptableObject projectile=FindProjectile();
            c.Ability=CloneAsset(c,ability,AbilityKey);c.Weapon=CloneAsset(c,weapon,WeaponKey);
            ScriptableObject priorities=Read(weapon,"m_TargetPriorization") as ScriptableObject;
            if(priorities!=null)CloneAsset(c,priorities,WeaponKey+"_Priorities");
            c.Projectile=CloneAsset(c,projectile,ProjectileKey);
            ScriptableObject projectileData=(ScriptableObject)Read(projectile,"m_ProjectileAttributes");
            c.ProjectileAttributes=CloneAsset(c,projectileData,ProjectileKey+"_Attributes");
            foreach(ScriptableObject asset in c.Owned)RemapAsset(c,asset);
            ConfigureAbility(Read(c.Ability,"m_Attributes"),Read(c.Ability,"m_View"));
            ConfigureWeapon(Read(c.Weapon,"m_Simulation"),Read(c.Weapon,"m_View"));
            ConfigureProjectile(Read(c.ProjectileAttributes,"m_Data"));
            AttachAbility(c);AttachWeapon(c);AttachHUD(c);
            for(int i=c.Owned.Count-1;i>=0;i--){WriteCacheNull(c.Owned[i]);CallIfPresent(c.Owned[i],"OnEnable");}
            VerifyPrepared();
            foreach(ScriptableObject asset in new ScriptableObject[]{c.Ability,c.Weapon,c.Projectile})Registry.Add(BuildDefinition(asset));
            VerifyRegistered();Ready=true;
            Runtime.Log("PERLICA_ORBITAL_ASSETS_READY ability="+AbilityKey+"; binding="+WeaponID+"; weaponIndex=1; turretIndex=0; nativeUseWeapon=True; disabledOnSpawn=False; autoFireExcluded=True; range="+RangeMeters+"; radius="+RadiusMeters+"; centerDamage=1200; windupMS=2500; detonationDelaySeconds=1; cooldownSeconds="+CooldownSeconds+"; friendlyDamage=0; originalLargeFXExcluded=True; nativeFOWNotBypassed=True; runtimeAcceptancePending=True");
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
        // Weapon assets need not have standalone registry keys. Rachel's
        // registered loadout is authoritative; asset name alone is not enough.
        static WeaponAttributes FindRachelWeaponDatum()
        {
            EntityTypeAttributes rachel=Registry.GetEntityType("C_Rachel");
            UnitAttributes attributes=rachel==null?null:rachel.Get<UnitAttributes>();
            if(attributes==null||attributes.WeaponLoadout==null)throw new InvalidOperationException("Registered native Rachel loadout missing");
            WeaponAttributes result=null;int matches=0;
            foreach(WeaponBinding binding in attributes.WeaponLoadout)
                if(binding!=null&&binding.WeaponID=="SUPERWEAPON_NARROW"){result=binding.Weapon;matches++;}
            if(matches!=1||result==null)throw new InvalidOperationException("Rachel SUPERWEAPON_NARROW binding must be unique and live");
            EntityTypeAttributes standalone=Registry.GetEntityType(DonorWeapon);
            if(standalone!=null&&!object.ReferenceEquals(standalone.Get<WeaponAttributes>(),result))throw new InvalidOperationException("Optional orbital weapon registry key differs from Rachel binding");
            return result;
        }
        static ScriptableObject FindRachelWeapon()
        {
            WeaponAttributes expected=FindRachelWeaponDatum();
            foreach(ScriptableObject candidate in Resources.FindObjectsOfTypeAll(typeof(WeaponAttributesAsset)))
                if(candidate!=null&&candidate.name==DonorWeapon&&object.ReferenceEquals(Read(candidate,"m_Simulation"),expected))return candidate;
            throw new InvalidOperationException("Loaded Narrow weapon asset differs from registered Rachel binding");
        }
        static ScriptableObject FindProjectile()
        {
            EntityTypeAttributes definition=Registry.GetEntityType(DonorProjectile);
            if(definition==null)throw new InvalidOperationException("Required native orbital projectile registry missing");
            foreach(ScriptableObject candidate in Resources.FindObjectsOfTypeAll(typeof(ProjectileAssetContainer)))
                if(candidate!=null&&candidate.name==DonorProjectile&&
                    object.ReferenceEquals(definition.Get<ProjectileAttributes>(),Read(Read(candidate,"m_ProjectileAttributes"),"m_Data")))return candidate;
            throw new InvalidOperationException("Native orbital projectile asset not live in current registry");
        }
        static void ConfigureAbility(object data,object view)
        {
            SetEnum(data,"m_StartsRemovedIn",0);Write(data,"m_CooldownTimeSecs",(float)CooldownSeconds);Write(data,"m_WarmupTimeSecs",0f);
            Write(data,"m_CasterMovesToTarget",false);Write(data,"m_EdgeOfTargetShapeMinDistance",0f);
            Write(data,"m_IsToggleable",false);Write(data,"m_CastOnDeath",false);
            SetEnum(data,"m_SharedCooldownChannel",0);
            Write(Read(data,"m_Cost"),"m_Resource1Cost",0);Write(Read(data,"m_Cost"),"m_Resource2Cost",0);
            Write(Read(data,"m_Charges"),"m_ChargesInventoryID",string.Empty);
            Write(Read(data,"m_Autocast"),"m_AutocastEnabled",false);Write(Read(data,"m_Autocast"),"m_AutocastEnabledOnSpawn",false);
            ClearArray(Read(data,"m_ResearchDeps"),"m_DependentResearch");
            Write(Read(data,"m_UseWeapon"),"m_WeaponID",WeaponID);Write(Read(data,"m_UseWeapon"),"m_ClearGoalsOnUse",true);
            Write(view,"LocalizedTitleStringID","轨道火力引导");
            Write(view,"LocalizedShortDescriptionStringID","标定目标区域，引导小规模轨道打击");
            Write(view,"LocalizedLongDescriptionStringID","引导范围"+RangeMeters+"米，毁伤半径"+RadiusMeters+"米。标定后蓄能2.5秒，弹着延迟1秒；冷却"+CooldownSeconds+"秒，不会伤害友军。");
            Write(view,"IconName","Icon-Command-OrbitalWeapon");SetEnum(view,"Hotkey",(int)KeyCode.N);
            Write(view,"DrawPreviewRouteLine",true);
            object reticle=Read(view,"TargetingReticle");
            // Native dimensions are HALF sizes, doubled by the controller.
            // Fix both the private configuration and the unanchored 512px
            // descendant sprites inherited from Rachel's much larger skill.
            Write(reticle,"m_IconWidth",RadiusMeters);Write(reticle,"m_IconHeight",RadiusMeters);
            Write(reticle,"m_IconSizeOverrideWeaponAOERadius",true);Write(reticle,"m_ReticleEnabled",true);
            ConfigurePrivateReticles(reticle);
            Write(reticle,"m_DefaultColour",new Color(1f,.88f,.12f,1f));
            // Do not play Rachel's native character dialogue on a Perlica unit.
            Write(Read(view,"VOFeedback"),"DialogueCode",string.Empty);
        }
        static readonly string[] ReticleFields={"m_DefaultPrefab","m_HoverPrefab","m_OutOfRangePrefab","m_InsideMinRangePrefab","m_InvalidMapPositionPrefab","m_ConfirmedPrefab","m_CancelledPrefab","m_DeployedPrefab"};
        static void ConfigurePrivateReticles(object reticle)
        {
            Dictionary<UISprite,UISprite> copies=new Dictionary<UISprite,UISprite>();
            foreach(string field in ReticleFields)
            {
                UISprite source=Read(reticle,field) as UISprite;if(source==null)continue;
                UISprite copy;
                if(!copies.TryGetValue(source,out copy))
                {
                    float factor=(2f*RadiusMeters)/Math.Max(1,Math.Max(source.width,source.height));
                    GameObject root=CopyReticleVisual(source.transform,null,factor);
                    root.name="EF_Perlica_12m_"+source.gameObject.name;root.SetActive(false);
                    copy=root.GetComponent<UISprite>();if(copy==null)throw new InvalidDataException("Private orbital reticle root lacks sprite");
                    copy.width=copy.height=2*RadiusMeters;copies.Add(source,copy);Current.ReticlePrefabs.Add(root);
                }
                Write(reticle,field,copy);
            }
            Runtime.Log("PERLICA_ORBITAL_RETICLE_PRIVATE radius="+RadiusMeters+"; diameter="+(2*RadiusMeters)+"; normalizedUnanchoredDescendants=True; originalPrefabsUnchanged=True; previewConfirmedDeployedShareRadius=True");
        }
        static GameObject CopyReticleVisual(Transform source,Transform parent,float factor)
        {
            GameObject node=new GameObject(source.name);node.SetActive(false);node.hideFlags=HideFlags.HideAndDontSave;
            node.layer=source.gameObject.layer;node.transform.parent=parent;
            node.transform.localPosition=source.localPosition*factor;node.transform.localRotation=source.localRotation;
            node.transform.localScale=source.localScale;
            UISprite old=source.GetComponent<UISprite>();
            if(old!=null)
            {
                UISprite sprite=node.AddComponent<UISprite>();sprite.atlas=old.atlas;sprite.spriteName=old.spriteName;
                sprite.type=old.type;sprite.pivot=old.pivot;sprite.color=old.color;sprite.depth=old.depth;
                sprite.fillDirection=old.fillDirection;sprite.fillAmount=old.fillAmount;sprite.invert=old.invert;sprite.flip=old.flip;
                // Clamp decorative hilites to the actual boundary as well.
                sprite.width=Math.Max(1,Math.Min(2*RadiusMeters,Mathf.RoundToInt(old.width*factor)));
                sprite.height=Math.Max(1,Math.Min(2*RadiusMeters,Mathf.RoundToInt(old.height*factor)));
            }
            // Only visual Transform/UISprite data are copied. Fabric triggers,
            // scale tweens and live gameplay scripts are not instantiated.
            for(int i=0;i<source.childCount;i++)CopyReticleVisual(source.GetChild(i),node.transform,factor);
            if(parent!=null)node.SetActive(source.gameObject.activeSelf);return node;
        }
        static void ConfigureWeapon(object data,object view)
        {
            Write(data,"m_ExcludeFromAutoTargetAcquisition",true);Write(data,"m_ExcludeFromAutoFire",true);Write(data,"m_ExcludeFromHeightAdvantage",true);
            Write(data,"m_BaseDamagePerRound",CenterDamage);Write(data,"m_BaseWreckDamagePerRound",0);
            Write(data,"m_AreaOfEffectMeters",(float)RadiusMeters);SetEnum(data,"m_AreaOfEffectFalloffType",1);
            Write(data,"m_ExcludeWeaponOwnerFromAreaOfEffect",true);Write(data,"m_FriendlyFireDamageScalar",0f);Write(data,"m_WeaponOwnerFriendlyFireDamageScalar",0f);
            Write(data,"m_TracerSpeed",10000f);Write(data,"m_TracerLength",0f);Write(data,"m_FiringRecoil",0f);
            Write(data,"m_WindUpTime",WindUpMilliseconds);Write(data,"m_RateOfFire",1);Write(data,"m_NumberOfBursts",1);Write(data,"m_DamagePacketsPerShot",1);
            Write(data,"m_BurstPeriodMinTime",1);Write(data,"m_BurstPeriodMaxTime",1);Write(data,"m_CooldownTime",0);Write(data,"m_WindDownTime",0);Write(data,"m_ReloadTime",CooldownSeconds*1000);
            Write(data,"m_LineOfSightRequired",false);Write(data,"m_LeadsTarget",false);Write(data,"m_ProjectileEntityTypeToSpawn",ProjectileKey);
            ClearArray(data,"m_Modifiers");ClearArray(data,"m_StatusEffectsToApplyOnTargets");ClearArray(data,"m_EntityTypesToSpawnOnImpact");
            Array ranges=(Array)Read(data,"m_Ranges");if(ranges==null||ranges.Length==0)throw new InvalidOperationException("Native orbital range datum absent");
            foreach(object range in ranges){Write(range,"m_MinDistance",0);Write(range,"m_Distance",RangeMeters);Write(range,"m_Accuracy",100f);}
            Write(view,"ImpactForce",0f);ClearArray(view,"WeaponFX");
        }
        static void ConfigureProjectile(object data)
        {
            Write(data,"m_DetonationDelay",DetonationDelaySeconds);Write(data,"m_MinimumMissDistance",0f);Write(data,"m_MaximumMissDistance",0f);
            Array stages=(Array)Read(data,"m_MotionStages");if(stages==null||stages.Length==0)throw new InvalidOperationException("Native orbital projectile stage absent");
            foreach(object stage in stages){Write(stage,"MinSpeed",10000f);Write(stage,"MaxSpeed",10000f);Write(stage,"TurnsToTarget",false);Write(stage,"UsesGravity",false);ClearArray(stage,"FXIntroDefinitions");ClearArray(stage,"FXOutroDefinitions");}
            object view=Read(data,"m_ViewAttributes");
            if(Read(view,"m_ModelPrefab")==null)throw new InvalidOperationException("Native empty projectile model missing");
            Write(view,"m_OnArrivalFXPrefab",null);Write(view,"m_DetonationFXPrefab",null);
        }
        static void AttachAbility(Context c)
        {
            Array old=(Array)Read(c.Custom,"m_Abilities");if(old==null)throw new InvalidOperationException("Perlica basic ability array missing");
            Array all=Array.CreateInstance(old.GetType().GetElementType(),old.Length+1);Array.Copy(old,all,old.Length);all.SetValue(c.Ability,old.Length);Write(c.Custom,"m_Abilities",all);
        }
        static void AttachWeapon(Context c)
        {
            Array old=(Array)Read(c.Core,"m_WeaponLoadout");if(old==null||old.Length!=1)throw new InvalidOperationException("Orbital expects exactly one basic Perlica weapon");
            object binding=CopyData(old.GetValue(0),new Dictionary<object,object>(new IdentityComparer()));
            Write(binding,"m_WeaponID",WeaponID);Write(binding,"m_Weapon",c.Weapon);Write(binding,"m_AmmoID",c.AmmoID);Write(binding,"m_TurretIndex",0);Write(binding,"m_TurretDefaultAngleOffsetDegrees",0f);
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
                if(Array.IndexOf(old,AbilityKey)>=0)throw new InvalidOperationException("Orbital HUD entry already exists");
                string[] all=(string[])old.Clone();int gap=Array.IndexOf(all,string.Empty);
                if(gap<0){all=new string[old.Length+1];Array.Copy(old,all,old.Length);gap=old.Length;}
                all[gap]=AbilityKey;Write(context,"m_AbilityOrder",all);
            }
            if(!found)throw new InvalidOperationException("Perlica native default HUD context absent");
        }
        public static void VerifyRegistered()
        {
            VerifyPrepared();Context c=Current;
            EntityTypeAttributes ability=Registry.GetEntityType(AbilityKey),weapon=Registry.GetEntityType(WeaponKey),projectile=Registry.GetEntityType(ProjectileKey);
            if(ability==null||weapon==null||projectile==null||!object.ReferenceEquals(ability.Get<AbilityAttributes>(),Read(c.Ability,"m_Attributes"))||
                !object.ReferenceEquals(weapon.Get<WeaponAttributes>(),Read(c.Weapon,"m_Simulation"))||
                !object.ReferenceEquals(projectile.Get<ProjectileAttributes>(),Read(c.ProjectileAttributes,"m_Data")))throw new InvalidOperationException("Orbital independent registration differs");
        }
        static void VerifyPrepared()
        {
            Context c=Current;if(c==null||Registry==null)throw new InvalidOperationException("Orbital preparation context absent");
            List<ScriptableObject> sources=new List<ScriptableObject>();
            foreach(SourceRecord source in c.Sources){if(Fingerprint(source.Asset)!=source.Fingerprint||!object.ReferenceEquals(Registry.GetEntityType(source.Asset.name),source.Definition))throw new InvalidOperationException("Native orbital source changed: "+source.Asset.name);sources.Add(source.Asset);}
            AssertNoSharedMutable(sources,c.Owned);
            object ability=Read(c.Ability,"m_Attributes"),weapon=Read(c.Weapon,"m_Simulation"),projectile=Read(c.ProjectileAttributes,"m_Data");
            if(((AbilityAttributes)ability).Name!=AbilityKey||(string)Read(Read(ability,"m_UseWeapon"),"m_WeaponID")!=WeaponID||Convert.ToInt32(Read(ability,"m_StartsRemovedIn"))!=0||(float)Read(ability,"m_CooldownTimeSecs")!=CooldownSeconds||
                (float)Read(ability,"m_WarmupTimeSecs")!=0f||(bool)Read(ability,"m_CasterMovesToTarget"))throw new InvalidOperationException("Orbital ability readback differs");
            object reticle=Read(Read(c.Ability,"m_View"),"TargetingReticle");
            if(!(bool)Read(reticle,"m_IconSizeOverrideWeaponAOERadius")||(int)Read(reticle,"m_IconWidth")!=RadiusMeters||(int)Read(reticle,"m_IconHeight")!=RadiusMeters||c.ReticlePrefabs.Count==0)throw new InvalidOperationException("Private orbital reticle half-size differs from damage radius");
            foreach(string field in ReticleFields)
            {
                UISprite sprite=Read(reticle,field) as UISprite;if(sprite==null)continue;
                if(!c.ReticlePrefabs.Contains(sprite.gameObject)||sprite.width!=2*RadiusMeters||sprite.height!=2*RadiusMeters)throw new InvalidOperationException("Orbital reticle does not use its private diameter");
                foreach(UISprite layer in sprite.GetComponentsInChildren<UISprite>(true))if(layer.width>2*RadiusMeters||layer.height>2*RadiusMeters)throw new InvalidOperationException("Orbital descendant exceeds damage diameter");
            }
            if((int)Read(weapon,"m_BaseDamagePerRound")!=CenterDamage||(float)Read(weapon,"m_AreaOfEffectMeters")!=RadiusMeters||
                (float)Read(weapon,"m_FriendlyFireDamageScalar")!=0f||(float)Read(weapon,"m_WeaponOwnerFriendlyFireDamageScalar")!=0f||
                !(bool)Read(weapon,"m_ExcludeFromAutoFire")||!(bool)Read(weapon,"m_ExcludeFromAutoTargetAcquisition")||
                (string)Read(weapon,"m_ProjectileEntityTypeToSpawn")!=ProjectileKey||(int)Read(weapon,"m_WindUpTime")!=WindUpMilliseconds||(int)Read(weapon,"m_ReloadTime")!=CooldownSeconds*1000||((Array)Read(Read(c.Weapon,"m_View"),"WeaponFX")).Length!=0)throw new InvalidOperationException("Orbital weapon readback differs");
            foreach(object range in (Array)Read(weapon,"m_Ranges"))if((int)Read(range,"m_Distance")!=RangeMeters||(int)Read(range,"m_MinDistance")!=0)throw new InvalidOperationException("Orbital range readback differs");
            object pv=Read(projectile,"m_ViewAttributes");
            if((float)Read(projectile,"m_DetonationDelay")!=DetonationDelaySeconds||Read(pv,"m_OnArrivalFXPrefab")!=null||Read(pv,"m_DetonationFXPrefab")!=null)throw new InvalidOperationException("Orbital delayed projectile readback differs");
            Array loadout=(Array)Read(c.Core,"m_WeaponLoadout");object binding=loadout.Length==2?loadout.GetValue(WeaponIndex):null;
            if(binding==null||(string)Read(binding,"m_WeaponID")!=WeaponID||!object.ReferenceEquals(Read(binding,"m_Weapon"),c.Weapon)||
                (string)Read(binding,"m_AmmoID")!=c.AmmoID||(int)Read(binding,"m_TurretIndex")!=0||(bool)Read(binding,"m_DisabledOnSpawn"))throw new InvalidOperationException("Orbital second weapon binding differs");
            UnitViewAttributes view=(UnitViewAttributes)Read(c.View,"Data");if(view.TurretDefinitions==null||view.TurretDefinitions.Length==0)throw new InvalidOperationException("Orbital requires Perlica's valid shared turret zero");
            int skills=0;foreach(ScriptableObject item in (Array)Read(c.Custom,"m_Abilities"))if(object.ReferenceEquals(item,c.Ability))skills++;
            int hudEntries=0;foreach(object context in (Array)Read(Read(c.Hud,"m_Attributes"),"m_HUDContexts"))if((string)Read(context,"m_ContextName")=="Default")foreach(string key in (string[])Read(context,"m_AbilityOrder"))if(key==AbilityKey)hudEntries++;
            if(skills!=1||hudEntries!=1)throw new InvalidOperationException("Orbital skill/HUD attachment must be unique");
        }
        public static void Release()
        {
            if(Current!=null)
            {
                foreach(UnitView view in Resources.FindObjectsOfTypeAll(typeof(UnitView)))if(view!=null&&view.gameObject.activeInHierarchy&&Current.Core!=null&&object.ReferenceEquals(view.UnitAttrs,Read(Current.Core,"m_Attributes")))throw new InvalidOperationException("Orbital assets still in use by active Perlica");
                for(int i=Current.Owned.Count-1;i>=0;i--)if(Current.Owned[i]!=null)UnityEngine.Object.DestroyImmediate(Current.Owned[i]);
                foreach(GameObject root in Current.ReticlePrefabs)if(root!=null)UnityEngine.Object.DestroyImmediate(root);
            }
            Current=null;Registry=null;Ready=false;
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
