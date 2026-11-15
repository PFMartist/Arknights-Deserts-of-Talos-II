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
    // Registration-time prototype balance, not real WT armour/125mm ballistics.
    public static class BvmTuning
    {
        public sealed class Configuration
        {
            public float MaxSpeedKph=60f, ReverseSpeedFactor=.35f;
            public float AccelerationSeconds=6f, BrakingSeconds=3f;
            public float TurnRadiusMeters=20f, TurnEaseSeconds=.6f;
            public bool UseNativeAavMovement=true;
            public float DriftFactor=.225f, ReverseDriftMultiplier=2f, DriftOvershootFactor=.125f;
            public float FishTailingIntervalSeconds=1f, FishTailRecoveryFactor=.1f;
            public float MinDriftSlipSpeedMetersPerSecond=4f, MaxDriftRecoverSeconds=1.25f;
            public float HullWidthMeters=4f, HullLengthMeters=8f, NavClearanceMeters=4.5f;
            public float ObstacleAvoidanceDistanceMeters=4.5f, AvoidanceDistanceMeters=4.5f;
            public float FormationSpacingMeters=2f, FormationGapAheadMeters=3f, ArrivalDistanceMeters=1.5f;
            public int HitPoints=3200, Armour=35, BaseDamagePerShot=650;
            public float ShotIntervalSeconds=6.5f, AccuracyPercent=95f;
            public int RangeMeters=260, AmmoCapacity=40;
            // Compressed-map gameplay distances, independently sized from the donor.
            public float SensorRadiusMeters=380f, ContactRadiusMeters=440f;
            public int AggroRangeMeters=260, AlertRangeMeters=300, LeashRangeMeters=320;
            public float TurretSpeedDegreesPerSecond=40f;
            public bool UnlimitedAmmo=true, PreserveNativeTargetModifiers=false;
        }
        public sealed class Result
        {
            public Configuration Values;
            public string ConfigPath, AmmoID, UnitClassSource, NavProfileHex;
            public string[] WeaponAssetKeys;
            public UnitClass UnitClass;
            public int HitPoints { get { return Values.HitPoints; } }
            public int Armour { get { return Values.Armour; } }
        }
        sealed class SourceRecord
        {
            public ScriptableObject Asset;
            public string Fingerprint;
            public EntityTypeAttributes RegisteredDefinition;
        }
        sealed class Context
        {
            public UnitAssetContainer Custom,Original;
            public EntityTypeCollection Registry;
            public Result Result;
            public readonly Dictionary<object,object> AssetMap=new Dictionary<object,object>(new IdentityComparer());
            public readonly List<ScriptableObject> Owned=new List<ScriptableObject>();
            public readonly List<SourceRecord> Sources=new List<SourceRecord>();
            public readonly List<WeaponAttributesAsset> Weapons=new List<WeaponAttributesAsset>();
            public readonly List<InventoryAttributesAsset> Inventories=new List<InventoryAttributesAsset>();
            public ScriptableObject Core,Movement,Storage,Nav;
            public long NativeNavToleranceRaw;
            public UnitClass NativeNavBlockedBy;
            public InventoryAttributesAsset Ammo;
        }
        sealed class IdentityComparer:IEqualityComparer<object>
        {
            public new bool Equals(object a,object b){return object.ReferenceEquals(a,b);}
            public int GetHashCode(object value){return RuntimeHelpers.GetHashCode(value);}
        }
        static readonly Dictionary<object,Context> Contexts=new Dictionary<object,Context>(new IdentityComparer());
        static readonly MethodInfo CloneMethod=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;

        public static Result Apply(UnitAssetContainer custom,UnitAssetContainer original,EntityTypeCollection registry)
        {return Apply(custom,original,registry,Path.Combine(CustomUnits.AssetDirectory,"bvm_unit.ini"));}

        public static Result Apply(UnitAssetContainer custom,UnitAssetContainer original,EntityTypeCollection registry,string configPath)
        {
            if(custom==null||original==null||registry==null)throw new ArgumentNullException("BVM tuning requires custom/original/registry");
            if(object.ReferenceEquals(custom,original))throw new InvalidOperationException("Refusing to tune native asset");
            Context existing;
            if(Contexts.TryGetValue(custom,out existing))
            {
                if(!object.ReferenceEquals(existing.Original,original)||!object.ReferenceEquals(existing.Registry,registry))
                    throw new InvalidOperationException("BVM tuning context changed");
                VerifyPrepared(existing);return existing.Result;
            }
            Configuration values=Load(configPath);
            Context c=new Context{Custom=custom,Original=original,Registry=registry};
            c.Result=new Result{Values=values,ConfigPath=Path.GetFullPath(configPath),AmmoID=custom.name+"_Ammo_Main"};
            RecordSource(c,original);
            foreach(string name in new string[]{"m_Attributes","m_Movement","m_Storage"})
                RecordSource(c,Read(original,name) as ScriptableObject);
            // Remapping contained assets must not mutate a shallow-copied array
            // on the caller's custom container.
            foreach(FieldInfo field in Fields(custom.GetType()))
                if(field.Name!="mDataCache"&&field.GetValue(custom) is Array)
                    field.SetValue(custom,CopyData(field.GetValue(custom),new Dictionary<object,object>(new IdentityComparer())));
            c.Core=CloneAsset(c,(ScriptableObject)Read(custom,"m_Attributes"),custom.name+"_Tune_Core");
            c.Movement=CloneAsset(c,(ScriptableObject)Read(custom,"m_Movement"),custom.name+"_Tune_Movement");
            Write(custom,"m_Attributes",c.Core);Write(custom,"m_Movement",c.Movement);
            ScriptableObject nav=Read(c.Core,"m_NavMesh") as ScriptableObject;
            if(nav==null)throw new InvalidOperationException("Native BVM donor navigation asset missing");
            c.Nav=CloneAsset(c,nav,custom.name+"_Tune_Nav");
            UnitAttributes sourceCore=(UnitAttributes)Read(Read(original,"m_Attributes"),"m_Attributes");
            if(sourceCore.NavMeshAttributes==null)throw new InvalidOperationException("Native donor navigation profile missing");
            c.NativeNavToleranceRaw=sourceCore.NavMeshAttributes.DistanceErrorPercentageTolerance.RawValue;
            c.NativeNavBlockedBy=sourceCore.NavMeshAttributes.BlockedBy;
            Array bindings=Read(c.Core,"m_WeaponLoadout") as Array;
            if(bindings==null||bindings.Length==0)throw new InvalidOperationException("Native donor has no weapon binding");
            foreach(object binding in bindings)
            {
                WeaponAttributesAsset weapon=Read(binding,"m_Weapon") as WeaponAttributesAsset;
                if(weapon==null)throw new InvalidOperationException("Weapon binding has no native asset");
                EnsureWeapon(c,weapon);
            }
            WeaponAttributesAsset[] extras=Read(custom,"m_Weapons") as WeaponAttributesAsset[];
            if(extras!=null)foreach(WeaponAttributesAsset weapon in extras)if(weapon!=null)EnsureWeapon(c,weapon);
            PrepareStorage(c);
            AssertNoSharedMutableData(c);
            foreach(ScriptableObject asset in c.Owned)RemapAsset(c,asset);
            RemapAsset(c,custom);
            AssertNoSharedMutableData(c);

            object core=Read(c.Core,"m_Attributes");
            Write(core,"m_MaxHealth",values.HitPoints);Write(core,"m_Armour",values.Armour);
            Write(core,"m_SensorRadius",values.SensorRadiusMeters);Write(core,"m_ContactRadius",values.ContactRadiusMeters);
            Write(core,"m_AggroRange",values.AggroRangeMeters);Write(core,"m_AlertRange",values.AlertRangeMeters);
            Write(core,"m_LeashRange",values.LeashRangeMeters);
            EntityTypeAttributes tank=registry.GetEntityType("C_Railgun_MP");
            UnitAttributes tankCore=tank==null?null:tank.Get<UnitAttributes>();
            c.Result.UnitClass=tankCore==null?UnitClass.Ground|UnitClass.Medium|UnitClass.Armored:tankCore.Class;
            c.Result.UnitClassSource=tankCore==null?"fallback Ground|Medium|Armored":"C_Railgun_MP";
            Write(core,"m_Class",c.Result.UnitClass);
            object movement=Read(c.Movement,"m_Simulation"),dynamics=Read(movement,"m_Dynamics");
            object combat=Read(movement,"m_Combat");
            object donorCombat=Read(Read(Read(original,"m_Movement"),"m_Simulation"),"m_Combat");
            if(combat==null||object.ReferenceEquals(combat,donorCombat))
                throw new InvalidOperationException("BVM combat behavior is missing or aliases native donor");
            ConfigureCombatRange(movement,values);
            // The native spawn path creates a capsule: radius=Width/2, axis=Length-Width.
            // Scale the authoritative simulation body as well as its path clearance.
            Write(dynamics,"m_Width",values.HullWidthMeters);Write(dynamics,"m_Length",values.HullLengthMeters);
            object maneuvers=Read(movement,"m_Maneuvers"),avoidance=Read(movement,"m_Avoidance");
            Write(maneuvers,"m_ObstacleAvoidanceDistance",values.ObstacleAvoidanceDistanceMeters);
            Write(maneuvers,"m_FormationSpacingDistance",values.FormationSpacingMeters);
            Write(maneuvers,"m_FormationSectionGapAhead",values.FormationGapAheadMeters);
            Write(maneuvers,"m_ArrivalDistance",values.ArrivalDistanceMeters);
            Write(avoidance,"m_AvoidanceDistance",values.AvoidanceDistanceMeters);
            if(!object.ReferenceEquals(Read(c.Core,"m_NavMesh"),c.Nav))throw new InvalidOperationException("BVM navigation clone remap lost");
            Write(Read(c.Nav,"m_NavMeshConfig"),"m_DistanceFromObstacles",values.NavClearanceMeters);
            // Preserve the donor tolerance and BlockedBy bits exactly.
            Call(c.Nav,"OnEnable");
            Write(movement,"m_DriveType",UnitDriveType.Tracked);
            Write(dynamics,"m_MaxSpeed",values.MaxSpeedKph/3.6f);
            // Native LAV cruise floor/waves were tuned for MaxSpeed=110.
            // Keep the independent tracked prototype internally consistent.
            Write(dynamics,"m_MinCruiseSpeed",values.MaxSpeedKph/3.6f*.25f);
            foreach(string field in new string[]{"m_CruiseSpeedVariations","m_CruiseDirectionVariations"})
            {
                Array variation=Read(dynamics,field) as Array;
                if(variation!=null)Write(dynamics,field,Array.CreateInstance(variation.GetType().GetElementType(),0));
            }
            Write(dynamics,"m_ReverseFactor",values.ReverseSpeedFactor);
            Write(dynamics,"m_AccelerationTime",values.AccelerationSeconds);
            Write(dynamics,"m_BrakingTime",values.BrakingSeconds);
            Write(dynamics,"m_MaxSpeedTurnRadius",values.TurnRadiusMeters);
            Write(dynamics,"m_MaxEaseIntoTurnTime",values.TurnEaseSeconds);
            Write(dynamics,"m_AccelerationTimeRandomVariancePercentage",0f);
            Write(dynamics,"m_MaxSpeedTurnRadiusRandomVariancePercentage",0f);
            if(values.UseNativeAavMovement)ApplyNativeAavMovement(dynamics,maneuvers,values);
            Call(c.Movement,"OnEnable");

            bindings=(Array)Read(c.Core,"m_WeaponLoadout");
            object primary=bindings.GetValue(0);
            WeaponAttributesAsset cannon=(WeaponAttributesAsset)Read(primary,"m_Weapon");
            Write(primary,"m_AmmoID",c.Result.AmmoID);Write(primary,"m_ShowAmmoOnHUD",true);
            object simulation=Read(cannon,"m_Simulation");
            Write(simulation,"m_BaseDamagePerRound",values.BaseDamagePerShot);
            Write(simulation,"m_RateOfFire",1);Write(simulation,"m_NumberOfBursts",1);
            Write(simulation,"m_DamagePacketsPerShot",1);
            Write(simulation,"m_BurstPeriodMinTime",1);Write(simulation,"m_BurstPeriodMaxTime",1);
            Write(simulation,"m_ReloadTime",(int)Math.Round(values.ShotIntervalSeconds*1000f));
            Write(simulation,"m_WindUpTime",0);Write(simulation,"m_WindDownTime",0);Write(simulation,"m_CooldownTime",0);
            Write(Read(simulation,"m_Turret"),"m_RotationSpeed",values.TurretSpeedDegreesPerSecond);
            Array modifiers=Read(simulation,"m_Modifiers") as Array;
            if(!values.PreserveNativeTargetModifiers&&modifiers!=null)
                Write(simulation,"m_Modifiers",Array.CreateInstance(modifiers.GetType().GetElementType(),0));
            Array ranges=Read(simulation,"m_Ranges") as Array;
            if(ranges==null||ranges.Length==0)throw new InvalidOperationException("Native weapon has no range bands");
            for(int i=0;i<ranges.Length;i++)
            {
                object band=ranges.GetValue(i);Write(band,"m_MinDistance",0);
                Write(band,"m_Distance",Math.Max(1,values.RangeMeters*(i+1)/ranges.Length));
                Write(band,"m_Accuracy",values.AccuracyPercent);
            }
            foreach(WeaponAttributesAsset asset in c.Weapons)Call(asset,"OnEnable");
            Write(custom,"mDataCache",null);Call(custom,"OnEnable");
            List<EntityTypeAttributes> definitions=new List<EntityTypeAttributes>();
            foreach(WeaponAttributesAsset asset in c.Weapons)definitions.Add(BuildDefinition(asset));
            foreach(InventoryAttributesAsset asset in c.Inventories)definitions.Add(BuildDefinition(asset));
            foreach(EntityTypeAttributes definition in definitions)
                if(registry.GetEntityType(definition.Name)!=null)throw new InvalidOperationException("Independent BVM key collision: "+definition.Name);
            VerifyPrepared(c);
            c.Result.NavProfileHex=NavigationProfileHex(((UnitAttributes)Read(c.Core,"m_Attributes")).NavMeshAttributes);
            foreach(EntityTypeAttributes definition in definitions)registry.Add(definition);
            c.Result.WeaponAssetKeys=new string[c.Weapons.Count];
            for(int i=0;i<c.Weapons.Count;i++)c.Result.WeaponAssetKeys[i]=c.Weapons[i].name;
            Contexts.Add(custom,c);
            Runtime.Log("BVM_TUNING_APPLIED prototypeNotReal125mm=True; config="+c.Result.ConfigPath+
                "; HP="+values.HitPoints+"; armour="+values.Armour+"; class="+c.Result.UnitClass+"; classSource="+c.Result.UnitClassSource+
                "; drive=Tracked; speedKph="+values.MaxSpeedKph+"; cruiseFloorFactor=.25; inheritedCruiseWaves=0; accelerationSeconds="+values.AccelerationSeconds+
                "; brakingSeconds="+values.BrakingSeconds+"; turnRadius="+values.TurnRadiusMeters+
                "; nativeAavMovement="+values.UseNativeAavMovement+"; trackedPolicySource=C_HAC_MP; originalRegistrationTemplate=C_Escort_MP"+
                "; driftFactor="+values.DriftFactor+"; reverseDriftMultiplier="+values.ReverseDriftMultiplier+
                "; driftOvershootFactor="+values.DriftOvershootFactor+"; minDriftSlipSpeed="+values.MinDriftSlipSpeedMetersPerSecond+
                "; nativeViewPhysicsNotReplaced=True; inheritedJTurnDisabled="+values.UseNativeAavMovement+
                "; hullWidth="+values.HullWidthMeters+"; hullLength="+values.HullLengthMeters+
                "; nativeCapsuleRadius="+(values.HullWidthMeters*.5f)+"; nativeCapsuleAxis="+(values.HullLengthMeters-values.HullWidthMeters)+
                "; navClearance="+values.NavClearanceMeters+"; navProfileHex="+c.Result.NavProfileHex+
                "; donorNavToleranceAndBlockedByPreserved=True; nativeSimulationBodyResized=True"+
                "; baseDamage="+values.BaseDamagePerShot+"; nativeRateOfFire=1; roundsPerBurst=1; reloadMs="+Math.Round(values.ShotIntervalSeconds*1000f)+
                "; range="+values.RangeMeters+"; compressedMapGameplayPrototype=True"+
                "; sensorRadius="+values.SensorRadiusMeters+"; contactRadius="+values.ContactRadiusMeters+
                "; aggroRange="+values.AggroRangeMeters+"; alertRange="+values.AlertRangeMeters+"; leashRange="+values.LeashRangeMeters+
                "; combatPassive=Hold; combatActive=Snipe; desiredCombatMin="+(values.RangeMeters*.6f)+"; desiredCombatMax="+(values.RangeMeters*.85f)+
                "; ammo="+values.AmmoCapacity+"; unlimited="+values.UnlimitedAmmo+
                "; nativeTargetModifiersPreserved="+values.PreserveNativeTargetModifiers+"; sourceFingerprintsUnchanged=True; unitNotRegisteredByModule=True");
            return c.Result;
        }

        public static void VerifyRegistered(UnitAssetContainer custom,UnitAssetContainer original,EntityTypeCollection registry)
        {
            Context c;
            if(!Contexts.TryGetValue(custom,out c)||!object.ReferenceEquals(c.Original,original)||!object.ReferenceEquals(c.Registry,registry))
                throw new InvalidOperationException("BVM tuning not prepared for this unit/registry");
            VerifyPrepared(c);
            EntityTypeAttributes registered=registry.GetEntityType(custom.name);
            if(registered==null||!object.ReferenceEquals(registered.Get<UnitAttributes>(),Read(c.Core,"m_Attributes"))||
                !object.ReferenceEquals(registered.Get<UnitMovementAttributes>(),Read(c.Movement,"m_Simulation")))
                throw new InvalidOperationException("Registered BVM lost independent core/movement");
            foreach(WeaponAttributesAsset asset in c.Weapons)
            {
                EntityTypeAttributes type=registry.GetEntityType(asset.name);
                if(type==null||!object.ReferenceEquals(type.Get<WeaponAttributes>(),Read(asset,"m_Simulation")))
                    throw new InvalidOperationException("Independent weapon registry missing: "+asset.name);
            }
            foreach(InventoryAttributesAsset asset in c.Inventories)
            {
                EntityTypeAttributes type=registry.GetEntityType(asset.name);
                if(type==null||!object.ReferenceEquals(type.Get<InventoryAttributes>(),Read(asset,"m_Attributes")))
                    throw new InvalidOperationException("Independent ammo registry missing: "+asset.name);
            }
            Runtime.Log("BVM_TUNING_REGISTERED_VERIFIED navProfileHex="+c.Result.NavProfileHex+"; nativeShapeDerivedFromWidthLength=True; coreMovementWeaponAmmoIndependent=True; localBindingIDsPreserved=True; cacheRebuildStable=True; originalUnchanged=True");
        }

        // Call before the caller destroys old custom containers during a NEW
        // registry generation. Never destroys assets still used by this registry
        // or an active UnitView. Do not pass null at a normal scene exit.
        public static int ReleaseForRegistryReset(EntityTypeCollection currentRegistry)
        {
            if(currentRegistry==null)throw new ArgumentNullException("currentRegistry");
            List<Context> stale=new List<Context>();
            foreach(Context c in Contexts.Values)
                if(!object.ReferenceEquals(c.Registry,currentRegistry))stale.Add(c);
            foreach(Context c in stale)
                foreach(UnitView view in Resources.FindObjectsOfTypeAll(typeof(UnitView)))
                    if(view!=null&&view.gameObject.activeInHierarchy&&object.ReferenceEquals(view.UnitAttrs,Read(c.Core,"m_Attributes")))
                        throw new InvalidOperationException("Cannot release BVM tuning assets used by an active UnitView");
            int count=0;
            foreach(Context c in stale)
            {
                for(int i=c.Owned.Count-1;i>=0;i--)if(c.Owned[i]!=null){UnityEngine.Object.DestroyImmediate(c.Owned[i]);count++;}
                Contexts.Remove(c.Custom);
            }
            if(count>0)Runtime.Log("BVM_TUNING_STALE_ASSETS_RELEASED count="+count+"; currentRegistryAssetsRetained=True; activeViewsChecked=True");
            return count;
        }

        static void PrepareStorage(Context c)
        {
            string prefix=c.Custom.name+"_Tune_";
            ScriptableObject source=Read(c.Custom,"m_Storage") as ScriptableObject;
            if(source!=null)c.Storage=CloneAsset(c,source,prefix+"Storage");
            else
            {
                c.Storage=RhodesAssetClone59.CreateEmpty(NativeType("StorageAttributesAsset"));Own(c,c.Storage,prefix+"Storage");
                Write(c.Storage,"m_Attributes",Activator.CreateInstance(NativeType("StorageAttributesData"),true));
                Write(c.Storage,"m_InventoryLoadout",Array.CreateInstance(NativeType("InventoryAssetBinding"),0));
            }
            Write(c.Custom,"m_Storage",c.Storage);
            Array originals=Read(c.Storage,"m_InventoryLoadout") as Array;
            List<object> bindings=new List<object>();
            if(originals!=null)
                for(int i=0;i<originals.Length;i++)
                {
                    object binding=originals.GetValue(i);
                    InventoryAttributesAsset inventory=Read(binding,"m_InventoryAttribute") as InventoryAttributesAsset;
                    if(inventory==null)throw new InvalidOperationException("Native inventory binding has no asset");
                    InventoryAttributesAsset clone=(InventoryAttributesAsset)CloneAsset(c,inventory,prefix+"Inventory_"+i);
                    if(!c.Inventories.Contains(clone))c.Inventories.Add(clone);
                    bindings.Add(binding);
                }
            c.Ammo=RhodesAssetClone59.CreateEmpty<InventoryAttributesAsset>();Own(c,c.Ammo,prefix+"Ammo_Main");
            object data=new InventoryAttributesData();
            Write(data,"m_InventoryID",c.Result.AmmoID);Write(data,"m_IsUnlimited",c.Result.Values.UnlimitedAmmo);
            Write(data,"m_StartingAmount",c.Result.Values.AmmoCapacity);Write(data,"m_MaxAmount",c.Result.Values.AmmoCapacity);
            Write(c.Ammo,"m_Attributes",data);c.Inventories.Add(c.Ammo);
            // The native constructor is internal in this build; invoke the exact
            // verified (string,InventoryAttributesAsset) constructor.
            bindings.Add(Activator.CreateInstance(NativeType("InventoryAssetBinding"),
                BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
                new object[]{c.Result.AmmoID,c.Ammo},CultureInfo.InvariantCulture));
            Array combined=Array.CreateInstance(NativeType("InventoryAssetBinding"),bindings.Count);
            for(int i=0;i<bindings.Count;i++)combined.SetValue(bindings[i],i);
            Write(c.Storage,"m_InventoryLoadout",combined);
            Write(Read(c.Storage,"m_Attributes"),"m_LinkToPlayerBank",false);
        }

        static WeaponAttributesAsset EnsureWeapon(Context c,WeaponAttributesAsset source)
        {
            object known;if(c.AssetMap.TryGetValue(source,out known))return (WeaponAttributesAsset)known;
            WeaponAttributesAsset clone=(WeaponAttributesAsset)CloneAsset(c,source,c.Custom.name+"_Tune_Weapon_"+c.Weapons.Count);
            c.Weapons.Add(clone);
            ScriptableObject priority=Read(clone,"m_TargetPriorization") as ScriptableObject;
            if(priority!=null)CloneAsset(c,priority,clone.name+"_Priorities");
            return clone;
        }

        static void VerifyPrepared(Context c)
        {
            AssertOriginalUnchanged(c);
            AssertNoSharedMutableData(c);
            object nativeCore=Read(Read(c.Original,"m_Attributes"),"m_Attributes");
            object nativeMovement=Read(Read(c.Original,"m_Movement"),"m_Simulation");
            UnitAttributes core=Read(c.Core,"m_Attributes") as UnitAttributes;
            UnitMovementAttributes movement=Read(c.Movement,"m_Simulation") as UnitMovementAttributes;
            if(core==null||movement==null||object.ReferenceEquals(core,nativeCore)||object.ReferenceEquals(movement,nativeMovement)||
                core.MaxHealth!=c.Result.Values.HitPoints||core.Armour!=c.Result.Values.Armour||core.Class!=c.Result.UnitClass)
                throw new InvalidOperationException("Tuned BVM core/movement aliases source or lost configured values");
            object dynamics=Read(Read(c.Movement,"m_Simulation"),"m_Dynamics");
            if(Math.Abs((float)Read(dynamics,"m_MaxSpeed")-c.Result.Values.MaxSpeedKph/3.6f)>.0001f||
                movement.DriveType!=UnitDriveType.Tracked)throw new InvalidOperationException("Tuned native movement values changed");
            Configuration cfg=c.Result.Values;
            // Read the public native interfaces, rather than just our assigned fields.
            if(core.SensorRadius.RawValue!=Fixed64.UnsafeFromFloat(cfg.SensorRadiusMeters).RawValue||
                core.ContactRadius.RawValue!=Fixed64.UnsafeFromFloat(cfg.ContactRadiusMeters).RawValue||
                core.AggroRange.RawValue!=Fixed64.FromInt(cfg.AggroRangeMeters).RawValue||
                core.AlertRange.RawValue!=Fixed64.FromInt(cfg.AlertRangeMeters).RawValue||
                core.LeashRange.RawValue!=Fixed64.FromInt(cfg.LeashRangeMeters).RawValue)
                throw new InvalidOperationException("BVM native sensor/contact/response range readback differs");
            if(cfg.UseNativeAavMovement)VerifyNativeAavMovement(dynamics,Read(Read(c.Movement,"m_Simulation"),"m_Maneuvers"),cfg);
            UnitMovementAttributes donor=(UnitMovementAttributes)nativeMovement;
            VerifyCombatRange(movement,donor,cfg);
            if(object.ReferenceEquals(movement.Dynamics,donor.Dynamics)||object.ReferenceEquals(movement.Maneuvers,donor.Maneuvers)||
                object.ReferenceEquals(movement.Avoidance,donor.Avoidance)||object.ReferenceEquals(core.NavMeshAttributes,((UnitAttributes)nativeCore).NavMeshAttributes))
                throw new InvalidOperationException("BVM footprint/navigation/avoidance still aliases native donor");
            if(!object.ReferenceEquals(Read(c.Core,"m_NavMesh"),c.Nav)||!object.ReferenceEquals(core.NavMeshAttributes,Read(c.Nav,"m_NavMeshConfig"))||
                core.NavMeshAttributes.DistanceFromObstacles.RawValue!=Fixed64.FromConstFloat(cfg.NavClearanceMeters).RawValue||
                core.NavMeshAttributes.DistanceErrorPercentageTolerance.RawValue!=c.NativeNavToleranceRaw||core.NavMeshAttributes.BlockedBy!=c.NativeNavBlockedBy)
                throw new InvalidOperationException("BVM independent navigation profile did not rebuild exactly");
            if(movement.Dynamics.Width.RawValue!=Fixed64.FromConstFloat(cfg.HullWidthMeters).RawValue||
                movement.Dynamics.Length.RawValue!=Fixed64.FromConstFloat(cfg.HullLengthMeters).RawValue||
                movement.Maneuvers.ObstacleAvoidanceDistance.RawValue!=Fixed64.FromConstFloat(cfg.ObstacleAvoidanceDistanceMeters).RawValue||
                movement.Maneuvers.FormationSpacingDistance.RawValue!=Fixed64.FromConstFloat(cfg.FormationSpacingMeters).RawValue||
                movement.Maneuvers.FormationSectionGapAhead.RawValue!=Fixed64.FromConstFloat(cfg.FormationGapAheadMeters).RawValue||
                movement.Maneuvers.ArrivalDistance.RawValue!=Fixed64.FromConstFloat(cfg.ArrivalDistanceMeters).RawValue||
                movement.Avoidance.AvoidanceDistance.RawValue!=Fixed64.FromConstFloat(cfg.AvoidanceDistanceMeters).RawValue)
                throw new InvalidOperationException("BVM native collision capsule/maneuver/avoidance readback differs");
            WeaponBinding[] loadout=core.WeaponLoadout;
            Array definitions=Read(c.Core,"m_WeaponLoadout") as Array;
            if(loadout==null||definitions==null||loadout.Length!=definitions.Length)
                throw new InvalidOperationException("Native BVM loadout cache did not rebuild");
            for(int i=0;i<loadout.Length;i++)
            {
                WeaponAttributesAsset asset=Read(definitions.GetValue(i),"m_Weapon") as WeaponAttributesAsset;
                if(asset==null||!c.Weapons.Contains(asset)||loadout[i]==null||!object.ReferenceEquals(loadout[i].Weapon,Read(asset,"m_Simulation")))
                    throw new InvalidOperationException("Native cache rebuilt with an original/shared weapon");
                foreach(WeaponBinding original in ((UnitAttributes)nativeCore).WeaponLoadout)
                    if(object.ReferenceEquals(loadout[i],original)||object.ReferenceEquals(loadout[i].Weapon,original.Weapon))
                        throw new InvalidOperationException("BVM aliases a native weapon/binding");
            }
            if(loadout[0].AmmoID!=c.Result.AmmoID)throw new InvalidOperationException("Cannon lost independent ammunition binding");
            object cannon=Read((WeaponAttributesAsset)Read(definitions.GetValue(0),"m_Weapon"),"m_Simulation");
            if((int)Read(cannon,"m_BaseDamagePerRound")!=c.Result.Values.BaseDamagePerShot||
                (int)Read(cannon,"m_RateOfFire")!=1||(int)Read(cannon,"m_NumberOfBursts")!=1)
                throw new InvalidOperationException("Native cannon damage/burst values changed");
            Array configuredRanges=Read(cannon,"m_Ranges") as Array;
            if(configuredRanges==null||configuredRanges.Length==0||loadout[0].Weapon.Ranges==null)
                throw new InvalidOperationException("BVM cannon lost native range bands");
            int bandIndex=0;
            foreach(RangeBasedWeaponAttributes band in loadout[0].Weapon.Ranges)
            {
                if(bandIndex>=configuredRanges.Length||band==null||band.MinDistance.RawValue!=Fixed64.Zero.RawValue||
                    band.Distance.RawValue!=Fixed64.FromInt(Math.Max(1,cfg.RangeMeters*(bandIndex+1)/configuredRanges.Length)).RawValue)
                    throw new InvalidOperationException("BVM native cannon range band readback differs at "+bandIndex);
                bandIndex++;
            }
            if(bandIndex!=configuredRanges.Length)
                throw new InvalidOperationException("BVM native cannon range band count changed");
            StorageAttributes storage=Read(c.Storage,"m_Attributes") as StorageAttributes;bool found=false;
            foreach(InventoryBinding binding in storage.InventoryLoadout)
                if(binding.InventoryID==c.Result.AmmoID)
                {found=object.ReferenceEquals(binding.InventoryAttributes,Read(c.Ammo,"m_Attributes"));break;}
            if(!found)throw new InvalidOperationException("Native ammo cache did not rebuild to independent inventory");
        }

        // m_Combat is ordinary BBI data, recursively copied with the movement
        // asset. Keep its desired standoff inside the independently tuned gun
        // envelope instead of inheriting the LAV's long desired combat ranges.
        static void ConfigureCombatRange(object movement,Configuration cfg)
        {
            object combat=Read(movement,"m_Combat");
            if(combat==null)throw new InvalidOperationException("BVM combat behavior data is missing");
            Write(combat,"m_PassiveCombatApproachMode",CombatApproachType.Hold);
            Write(combat,"m_ActiveCombatApproachMode",CombatApproachType.Snipe);
            Write(combat,"m_MinDesiredCombatRange",cfg.RangeMeters*.6f);
            Write(combat,"m_MaxDesiredCombatRange",cfg.RangeMeters*.85f);
        }
        static void VerifyCombatRange(UnitMovementAttributes movement,UnitMovementAttributes donor,Configuration cfg)
        {
            UnitCombatBehavior combat=movement.Combat,original=donor.Combat;
            if(combat==null||original==null||object.ReferenceEquals(combat,original)||
                !object.ReferenceEquals(combat,Read(movement,"m_Combat")))
                throw new InvalidOperationException("BVM combat behavior aliases native data or lost its public binding");
            if(combat.PassiveCombatApproachMode!=CombatApproachType.Hold||combat.ActiveCombatApproachMode!=CombatApproachType.Snipe||
                combat.MinDesiredCombatRange.RawValue!=Fixed64.FromConstFloat(cfg.RangeMeters*.6f).RawValue||
                combat.MaxDesiredCombatRange.RawValue!=Fixed64.FromConstFloat(cfg.RangeMeters*.85f).RawValue)
                throw new InvalidOperationException("BVM native desired combat range/mode readback differs");
            if(combat.HoverStrafeCycleDuration!=original.HoverStrafeCycleDuration||
                combat.HoverStrafeLateralDisplacement!=original.HoverStrafeLateralDisplacement||
                combat.AdvanceThroughSmokeScreen!=original.AdvanceThroughSmokeScreen)
                throw new InvalidOperationException("BVM changed unrelated native combat behavior settings");
        }

        // C_HAC_MP (native Coalition AAV) supplies only dimensionless turn/drift
        // policy. BVM retains its configured speed, acceleration, 4x8m capsule,
        // clearance and arrival distances; neither source asset is modified.
        static void ApplyNativeAavMovement(object dynamics,object maneuvers,Configuration cfg)
        {
            Write(dynamics,"m_DriftType",cfg.DriftFactor);
            Write(dynamics,"m_ReverseDriftMultiplier",cfg.ReverseDriftMultiplier);
            Write(dynamics,"m_DriftOvershootFactor",cfg.DriftOvershootFactor);
            Write(dynamics,"m_FishTailingTimeIntervals",cfg.FishTailingIntervalSeconds);
            Write(dynamics,"m_FishTailControlRecover",cfg.FishTailRecoveryFactor);
            Write(dynamics,"m_MinDriftSlipSpeed",cfg.MinDriftSlipSpeedMetersPerSecond);
            Write(dynamics,"m_MaxDriftRecoverTime",cfg.MaxDriftRecoverSeconds);
            foreach(string field in new string[]{"m_DriftTypeRandomVariancePercentage","m_DriftOvershootFactorRandomVariancePercentage","m_MinDriftSlipSpeedRandomVariancePercentage"})
                Write(dynamics,field,0f);
            Write(maneuvers,"m_FastDirectTurn",TurnStyle.Drift);
            Write(maneuvers,"m_FastAwayTurn",TurnStyle.Controlled);
            Write(maneuvers,"m_SlowDirectTurn",TurnStyle.Drift);
            Write(maneuvers,"m_SlowAwayTurn",TurnStyle.Controlled);
            Write(maneuvers,"m_DriftRecoverOptions",new DriftRecoverStyle[]{DriftRecoverStyle.Neutral,DriftRecoverStyle.BrakesOn,DriftRecoverStyle.AccelerateOut});
            Write(maneuvers,"m_MustReachMinSpeedBeforeTurn",false);
            Write(Read(maneuvers,"m_ThreePointTurn"),"m_MustReachMinSpeedBeforeTurn",false);
            Write(Read(maneuvers,"m_JTurn"),"m_TurnStyle",TurnStyle.None);
            // Copy only the AAV's scale-free peel-out response, not its large
            // reverse-arrival/turn trigger regions or view-physics body pitch.
            object peel=Read(maneuvers,"m_PeelOut");
            Write(peel,"m_InitialForwardAccelerationPercentage",.25f);
            Write(peel,"m_RadiusProportionalGrowthConstant",.4f);
            Write(peel,"m_InitialTurnRadiusAsMaxTurnRadiusPercentage",.2f);
            Write(peel,"m_MaxTurnRatePercentage",1.2f);
        }
        static void VerifyNativeAavMovement(object dynamics,object maneuvers,Configuration cfg)
        {
            string[] names={"m_DriftType","m_ReverseDriftMultiplier","m_DriftOvershootFactor","m_FishTailingTimeIntervals","m_FishTailControlRecover","m_MinDriftSlipSpeed","m_MaxDriftRecoverTime"};
            float[] expected={cfg.DriftFactor,cfg.ReverseDriftMultiplier,cfg.DriftOvershootFactor,cfg.FishTailingIntervalSeconds,cfg.FishTailRecoveryFactor,cfg.MinDriftSlipSpeedMetersPerSecond,cfg.MaxDriftRecoverSeconds};
            for(int i=0;i<names.Length;i++)if(Math.Abs((float)Read(dynamics,names[i])-expected[i])>.0001f)
                throw new InvalidOperationException("BVM AAV dynamics changed: "+names[i]);
            DriftRecoverStyle[] recover=Read(maneuvers,"m_DriftRecoverOptions") as DriftRecoverStyle[];
            if((TurnStyle)Read(maneuvers,"m_FastDirectTurn")!=TurnStyle.Drift||(TurnStyle)Read(maneuvers,"m_FastAwayTurn")!=TurnStyle.Controlled||
                (TurnStyle)Read(maneuvers,"m_SlowDirectTurn")!=TurnStyle.Drift||(TurnStyle)Read(maneuvers,"m_SlowAwayTurn")!=TurnStyle.Controlled||
                (TurnStyle)Read(Read(maneuvers,"m_JTurn"),"m_TurnStyle")!=TurnStyle.None||
                (bool)Read(Read(maneuvers,"m_ThreePointTurn"),"m_MustReachMinSpeedBeforeTurn")||
                recover==null||recover.Length!=3||recover[0]!=DriftRecoverStyle.Neutral||recover[1]!=DriftRecoverStyle.BrakesOn||recover[2]!=DriftRecoverStyle.AccelerateOut)
                throw new InvalidOperationException("BVM native AAV turn policy changed");
        }

        static string NavigationProfileHex(NavMeshAttributes nav)
        {
            if(nav==null||!BitConverter.IsLittleEndian)throw new InvalidOperationException("BVM nav profile requires native little-endian attributes");
            using(MemoryStream stream=new MemoryStream())
            using(BinaryWriter writer=new BinaryWriter(stream))
            {
                writer.Write(nav.DistanceFromObstacles.RawValue);writer.Write(nav.DistanceErrorPercentageTolerance.RawValue);writer.Write((int)nav.BlockedBy);
                writer.Flush();return BitConverter.ToString(stream.ToArray()).Replace("-","").ToLowerInvariant();
            }
        }

        static EntityTypeAttributes BuildDefinition(ScriptableObject asset)
        {
            EntityTypeAttributes result=new EntityTypeAttributes(asset.name);
            object data=Read(asset,"Data");IEnumerable many=data as IEnumerable;
            if(many!=null){foreach(object item in many)if(item!=null&&!result.Add(item))throw new InvalidOperationException("Duplicate cloned datum");}
            else if(data==null||!result.Add(data))throw new InvalidOperationException("Cloned asset has no registry datum");
            return result;
        }

        static ScriptableObject CloneAsset(Context c,ScriptableObject source,string name)
        {
            if(source==null)throw new InvalidOperationException("Required native tuning asset is null");
            object existing;if(c.AssetMap.TryGetValue(source,out existing))return (ScriptableObject)existing;
            RecordSource(c,source);
            ScriptableObject clone=RhodesAssetClone59.CreateEmpty(source.GetType());c.AssetMap.Add(source,clone);
            Dictionary<object,object> copied=new Dictionary<object,object>(new IdentityComparer());
            foreach(FieldInfo field in Fields(source.GetType()))
                if(field.Name!="mDataCache")field.SetValue(clone,CopyData(field.GetValue(source),copied));
            Own(c,clone,name);return clone;
        }
        static void Own(Context c,ScriptableObject asset,string name)
        {asset.name=name;asset.hideFlags=HideFlags.HideAndDontSave;c.Owned.Add(asset);}

        static object CopyData(object value,Dictionary<object,object> copied)
        {
            if(value==null)return null;Type type=value.GetType();
            if(type.IsPrimitive||type.IsEnum||type==typeof(string)||typeof(UnityEngine.Object).IsAssignableFrom(type))return value;
            object previous;if(copied.TryGetValue(value,out previous))return previous;
            Array array=value as Array;
            if(array!=null)
            {
                if(array.Rank!=1)throw new InvalidOperationException("Unexpected multidimensional tuning array");
                Array result=(Array)array.Clone();copied.Add(value,result);
                for(int i=0;i<array.Length;i++)result.SetValue(CopyData(array.GetValue(i),copied),i);return result;
            }
            if(type.Namespace==null||!type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))return value;
            object clone=CloneMethod.Invoke(value,null);copied.Add(value,clone);
            foreach(FieldInfo field in Fields(type))field.SetValue(clone,CopyData(field.GetValue(value),copied));return clone;
        }
        static void RemapAsset(Context c,ScriptableObject asset)
        {
            HashSet<object> seen=new HashSet<object>(new IdentityComparer());
            foreach(FieldInfo field in Fields(asset.GetType()))
                if(field.Name!="mDataCache")field.SetValue(asset,Remap(c,field.GetValue(asset),seen));
        }
        static object Remap(Context c,object value,HashSet<object> seen)
        {
            if(value==null)return null;object mapped;if(c.AssetMap.TryGetValue(value,out mapped))return mapped;
            Type type=value.GetType();
            if(type.IsPrimitive||type.IsEnum||type==typeof(string)||typeof(UnityEngine.Object).IsAssignableFrom(type))return value;
            if(!seen.Add(value))return value;Array array=value as Array;
            if(array!=null){for(int i=0;i<array.Length;i++)array.SetValue(Remap(c,array.GetValue(i),seen),i);return array;}
            if(type.Namespace!=null&&type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))
                foreach(FieldInfo field in Fields(type))field.SetValue(value,Remap(c,field.GetValue(value),seen));
            return value;
        }

        static void RecordSource(Context c,ScriptableObject source)
        {
            if(source==null)return;
            foreach(SourceRecord previous in c.Sources)if(object.ReferenceEquals(previous.Asset,source))return;
            c.Sources.Add(new SourceRecord{Asset=source,Fingerprint=Fingerprint(source),RegisteredDefinition=c.Registry.GetEntityType(source.name)});
        }
        static void AssertOriginalUnchanged(Context c)
        {
            foreach(SourceRecord source in c.Sources)
            {
                if(Fingerprint(source.Asset)!=source.Fingerprint)throw new InvalidOperationException("Source asset changed: "+source.Asset.name);
                if(!object.ReferenceEquals(c.Registry.GetEntityType(source.Asset.name),source.RegisteredDefinition))
                    throw new InvalidOperationException("Source registry identity replaced: "+source.Asset.name);
            }
        }
        static void AssertNoSharedMutableData(Context c)
        {
            HashSet<object> sourceData=new HashSet<object>(new IdentityComparer());
            foreach(SourceRecord source in c.Sources)
                foreach(FieldInfo field in Fields(source.Asset.GetType()))
                    GatherMutable(field.GetValue(source.Asset),sourceData);
            HashSet<object> checkedData=new HashSet<object>(new IdentityComparer());
            foreach(ScriptableObject asset in c.Owned)
                foreach(FieldInfo field in Fields(asset.GetType()))
                    CheckMutable(field.GetValue(asset),sourceData,checkedData);
        }
        static bool IsMutableData(object value)
        {
            if(value==null)return false;Type t=value.GetType();
            return !t.IsValueType&&t!=typeof(string)&&!typeof(UnityEngine.Object).IsAssignableFrom(t)&&
                (value is Array||(t.Namespace!=null&&t.Namespace.StartsWith("BBI.",StringComparison.Ordinal)));
        }
        static void GatherMutable(object value,HashSet<object> gathered)
        {
            if(!IsMutableData(value)||!gathered.Add(value))return;Array array=value as Array;
            if(array!=null){foreach(object element in array)GatherMutable(element,gathered);return;}
            foreach(FieldInfo field in Fields(value.GetType()))GatherMutable(field.GetValue(value),gathered);
        }
        static void CheckMutable(object value,HashSet<object> sources,HashSet<object> seen)
        {
            if(!IsMutableData(value)||!seen.Add(value))return;
            if(sources.Contains(value))throw new InvalidOperationException("Independent tuning data still shares mutable source: "+value.GetType().FullName);
            Array array=value as Array;
            if(array!=null){foreach(object element in array)CheckMutable(element,sources,seen);return;}
            foreach(FieldInfo field in Fields(value.GetType()))CheckMutable(field.GetValue(value),sources,seen);
        }
        static string Fingerprint(ScriptableObject asset)
        {
            StringBuilder text=new StringBuilder();HashSet<object> seen=new HashSet<object>(new IdentityComparer());
            foreach(FieldInfo field in Fields(asset.GetType()))
            {text.Append(field.DeclaringType.FullName).Append('.').Append(field.Name).Append('=');AppendValue(text,field.GetValue(asset),seen);}
            using(SHA256 hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","");
        }
        static void AppendValue(StringBuilder text,object value,HashSet<object> seen)
        {
            if(value==null){text.Append("null;");return;}Type type=value.GetType();text.Append(type.FullName).Append(':');
            UnityEngine.Object unity=value as UnityEngine.Object;
            if(!object.ReferenceEquals(unity,null)){text.Append(unity.GetInstanceID()).Append('/').Append(unity.name).Append(';');return;}
            if(type.IsPrimitive||type.IsEnum||type==typeof(string)){text.Append(Convert.ToString(value,CultureInfo.InvariantCulture)).Append(';');return;}
            // Reflection boxes a fresh Vector3/Color/Quaternion on every read.
            // Structural value hashing must precede identity-based graph checks.
            if(type.IsValueType)
            {
                foreach(FieldInfo field in Fields(type)){text.Append(field.Name).Append('=');AppendValue(text,field.GetValue(value),seen);}
                return;
            }
            if(!seen.Add(value)){text.Append("seen;");return;}Array array=value as Array;
            if(array!=null){text.Append(array.Length).Append('[');foreach(object item in array)AppendValue(text,item,seen);text.Append(']');return;}
            if(type.Namespace!=null&&type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))
                foreach(FieldInfo field in Fields(type)){text.Append(field.Name).Append('=');AppendValue(text,field.GetValue(value),seen);}
            else text.Append(RuntimeHelpers.GetHashCode(value));
        }

        public static Configuration Load(string path)
        {
            Configuration result=new Configuration();
            if(File.Exists(path))
            {
                Dictionary<string,FieldInfo> known=new Dictionary<string,FieldInfo>(StringComparer.OrdinalIgnoreCase);
                foreach(FieldInfo field in typeof(Configuration).GetFields(BindingFlags.Instance|BindingFlags.Public))known.Add(field.Name,field);
                HashSet<string> assigned=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int number=0;
                foreach(string raw in File.ReadAllLines(path))
                {
                    number++;string line=raw.Trim();
                    if(line.Length==0||line.StartsWith("#")||line.StartsWith(";")||(line.StartsWith("[")&&line.EndsWith("]")))continue;
                    int equal=line.IndexOf('=');if(equal<1)throw new InvalidDataException("Expected key=value at INI line "+number);
                    string key=line.Substring(0,equal).Trim(),token=line.Substring(equal+1).Trim();
                    int comment=token.IndexOfAny(new char[]{'#',';'});if(comment>=0)token=token.Substring(0,comment).Trim();
                    FieldInfo field;if(!known.TryGetValue(key,out field)||!assigned.Add(key))throw new InvalidDataException("Unknown or duplicate tuning key: "+key);
                    object parsed;
                    if(field.FieldType==typeof(bool))parsed=bool.Parse(token);
                    else if(field.FieldType==typeof(int))parsed=int.Parse(token,CultureInfo.InvariantCulture);
                    else parsed=float.Parse(token,CultureInfo.InvariantCulture);
                    field.SetValue(result,parsed);
                }
            }
            Check(result.MaxSpeedKph,1,150,"MaxSpeedKph");Check(result.ReverseSpeedFactor,.01f,1,"ReverseSpeedFactor");
            Check(result.AccelerationSeconds,.1f,60,"AccelerationSeconds");Check(result.BrakingSeconds,.1f,60,"BrakingSeconds");
            Check(result.TurnRadiusMeters,.1f,300,"TurnRadiusMeters");Check(result.TurnEaseSeconds,.01f,10,"TurnEaseSeconds");
            Check(result.DriftFactor,0,1,"DriftFactor");Check(result.ReverseDriftMultiplier,0,10,"ReverseDriftMultiplier");
            Check(result.DriftOvershootFactor,0,3,"DriftOvershootFactor");Check(result.FishTailingIntervalSeconds,.01f,30,"FishTailingIntervalSeconds");
            Check(result.FishTailRecoveryFactor,0,1,"FishTailRecoveryFactor");Check(result.MinDriftSlipSpeedMetersPerSecond,0,100,"MinDriftSlipSpeedMetersPerSecond");
            Check(result.MaxDriftRecoverSeconds,.01f,30,"MaxDriftRecoverSeconds");
            Check(result.HullWidthMeters,.5f,30,"HullWidthMeters");Check(result.HullLengthMeters,.5f,60,"HullLengthMeters");
            Check(result.NavClearanceMeters,.5f,60,"NavClearanceMeters");
            if(result.HullLengthMeters<result.HullWidthMeters||result.NavClearanceMeters<result.HullLengthMeters*.5f)
                throw new InvalidDataException("Native capsule requires Length>=Width and NavClearance>=Length/2");
            Check(result.ObstacleAvoidanceDistanceMeters,.1f,100,"ObstacleAvoidanceDistanceMeters");
            Check(result.AvoidanceDistanceMeters,.1f,100,"AvoidanceDistanceMeters");
            Check(result.FormationSpacingMeters,.1f,100,"FormationSpacingMeters");Check(result.FormationGapAheadMeters,.1f,100,"FormationGapAheadMeters");
            Check(result.ArrivalDistanceMeters,.1f,30,"ArrivalDistanceMeters");
            Check(result.HitPoints,1,1000000,"HitPoints");Check(result.Armour,0,10000,"Armour");
            Check(result.BaseDamagePerShot,1,100000,"BaseDamagePerShot");Check(result.ShotIntervalSeconds,.1f,120,"ShotIntervalSeconds");
            Check(result.RangeMeters,10,4000,"RangeMeters");Check(result.AccuracyPercent,0,100,"AccuracyPercent");
            Check(result.SensorRadiusMeters,10,8000,"SensorRadiusMeters");Check(result.ContactRadiusMeters,10,8000,"ContactRadiusMeters");
            Check(result.AggroRangeMeters,0,8000,"AggroRangeMeters");Check(result.AlertRangeMeters,0,8000,"AlertRangeMeters");
            Check(result.LeashRangeMeters,0,8000,"LeashRangeMeters");
            if(result.RangeMeters>result.SensorRadiusMeters||result.SensorRadiusMeters>result.ContactRadiusMeters||
                result.AggroRangeMeters>result.RangeMeters||result.AggroRangeMeters>result.AlertRangeMeters||
                result.AlertRangeMeters>result.SensorRadiusMeters||result.AlertRangeMeters>result.LeashRangeMeters||
                result.LeashRangeMeters>result.ContactRadiusMeters)
                throw new InvalidDataException("BVM requires weapon<=sensor<=contact, aggro<=weapon, and aggro<=alert<=leash<=contact with alert<=sensor");
            Check(result.TurretSpeedDegreesPerSecond,1,180,"TurretSpeedDegreesPerSecond");Check(result.AmmoCapacity,1,100000,"AmmoCapacity");
            return result;
        }
        static void Check(float value,float min,float max,string key)
        {if(float.IsNaN(value)||float.IsInfinity(value)||value<min||value>max)throw new InvalidDataException(key+" must be finite in "+min+".."+max);}
        static Type NativeType(string name)
        {return typeof(WeaponAttributesAsset).Assembly.GetType("BBI.Unity.Game.Data."+name,true);}
        static List<FieldInfo> Fields(Type type)
        {
            List<FieldInfo> result=new List<FieldInfo>();
            for(Type current=type;current!=null&&current!=typeof(object)&&current!=typeof(UnityEngine.Object)&&current!=typeof(ScriptableObject);current=current.BaseType)
                foreach(FieldInfo field in current.GetFields(Flags))if(!field.IsStatic)result.Add(field);
            result.Sort(delegate(FieldInfo a,FieldInfo b){return string.CompareOrdinal(a.DeclaringType.FullName+"."+a.Name,b.DeclaringType.FullName+"."+b.Name);});return result;
        }
        static object Read(object value,string name)
        {
            if(value==null)throw new ArgumentNullException(name);
            for(Type type=value.GetType();type!=null;type=type.BaseType)
            {
                FieldInfo field=type.GetField(name,Flags);if(field!=null)return field.GetValue(value);
                PropertyInfo property=type.GetProperty(name,Flags);if(property!=null)return property.GetValue(value,null);
            }
            throw new MissingMemberException(value.GetType().FullName,name);
        }
        static void Write(object value,string name,object data)
        {
            for(Type type=value.GetType();type!=null;type=type.BaseType)
            {FieldInfo field=type.GetField(name,Flags);if(field!=null){field.SetValue(value,data);return;}}
            throw new MissingFieldException(value.GetType().FullName,name);
        }
        static void Call(object value,string name)
        {
            for(Type type=value.GetType();type!=null;type=type.BaseType)
            {MethodInfo method=type.GetMethod(name,Flags,null,Type.EmptyTypes,null);if(method!=null){method.Invoke(value,null);return;}}
            throw new MissingMethodException(value.GetType().FullName,name);
        }
    }
}
