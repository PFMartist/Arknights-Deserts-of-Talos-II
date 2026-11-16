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
using BBI.Game.Data;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // First large-hull gate: independently registered, selectable native mover.
    // Factory exits, weapons, special support abilities and hill traversal are
    // intentionally separate acceptance gates; the original carrier is immutable.
    public static partial class RhodesCarrierUnits
    {
        public const string UnitKey = "EF_RhodesCarrier_Test_MP";
        public const string SourceKey = "C_Carrier_MP";
        public static bool Ready { get; private set; }
        public static bool ViewsVerified { get; private set; }
        public static float LengthMetres { get { return Settings == null ? 600f : Settings.Length; } }
        static EntityTypeCollection Registry;
        static Context Current;
        static Configuration Settings;
        static readonly ModCore.DeferredBindingQueue<BBI.Core.ComponentModel.Entity,UnitView>.BindAttempt BindSpawnedCarrier=TryBindSpawnedCarrier;
        static readonly MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        sealed class Configuration
        {
            public float Length=600f,Speed=60f,Sensor=800f,Contact=1250f;
            public int Health=15000,Armour=25;
            public float Width { get { return Length * (13.499716f/29.762559f); } }
            public float Height { get { return Length * (9.203893f/29.762559f); } }
        }
        sealed class Context
        {
            public UnitAssetContainer Source,Custom;
            public ScriptableObject Core,Movement,Nav,View,Hud,Power;
            public GameObject NativePrefab;
            public readonly Dictionary<object,object> Map=new Dictionary<object,object>(new IdentityComparer());
            public readonly List<ScriptableObject> Owned=new List<ScriptableObject>();
            public readonly List<SourceRecord> Sources=new List<SourceRecord>();
            public readonly List<ScriptableObject> Commands=new List<ScriptableObject>();
            public readonly List<ProductionItem> Products=new List<ProductionItem>();
            public readonly List<ScriptableObject> PowerSystems=new List<ScriptableObject>(),PowerStatuses=new List<ScriptableObject>(),PowerBuffs=new List<ScriptableObject>();
        }
        sealed class SourceRecord { public ScriptableObject Asset;public string Fingerprint;public EntityTypeAttributes Definition; }
        sealed class IdentityComparer:IEqualityComparer<object>
        { public new bool Equals(object a,object b){return object.ReferenceEquals(a,b);} public int GetHashCode(object value){return RuntimeHelpers.GetHashCode(value);} }

        public static void Register()
        {
            EntityTypeCollection current=(EntityTypeCollection)NativeRead.Get(typeof(ShipbreakersMain),"sEntityTypes");
            if(current==null)throw new InvalidOperationException("Rhodes registration requires native entity types");
            if(object.ReferenceEquals(current,Registry))
            { if(!Ready)throw new InvalidOperationException("Rhodes registration previously failed; restart before retry");VerifyRegistered();return; }
            ReleaseStale();Registry=current;Ready=false;ViewsVerified=false;Settings=LoadConfiguration();
            if(!FactionUnits.Ready)throw new InvalidOperationException("Register the eight-unit faction before its carrier");
            FactionUnits.VerifyRegistered();
            EntityTypeAttributes sourceType=current.GetEntityType(SourceKey);
            if(sourceType==null||sourceType.Get<UnitAttributes>()==null)throw new InvalidOperationException("Native Coalition carrier is not registered");
            UnitAssetContainer source=null;
            foreach(UnitAssetContainer candidate in Resources.FindObjectsOfTypeAll(typeof(UnitAssetContainer)))
                if(candidate!=null&&candidate.name==SourceKey&&object.ReferenceEquals(Read(Read(candidate,"m_Attributes"),"m_Attributes"),sourceType.Get<UnitAttributes>())){source=candidate;break;}
            if(source==null)throw new InvalidOperationException("Current native carrier container not found");
            if(current.GetEntityType(UnitKey)!=null)throw new InvalidOperationException("Rhodes independent unit key collision");
            Context c=new Context();Current=c;c.Source=source;c.Custom=(UnitAssetContainer)CloneAsset(c,source,UnitKey);
            foreach(FieldInfo field in Fields(source.GetType()))
            {
                if(field.Name=="mDataCache")continue;
                ScriptableObject component=field.GetValue(source) as ScriptableObject;
                if(component!=null)CloneAsset(c,component,UnitKey+"_"+field.Name.TrimStart('m','_'));
            }
            ScriptableObject nativeExperience=Read(source,"m_Experience") as ScriptableObject;
            ScriptableObject experienceView=nativeExperience==null?null:Read(nativeExperience,"m_View") as ScriptableObject;
            if(experienceView!=null)CloneAsset(c,experienceView,UnitKey+"_ExperienceView");
            c.Core=(ScriptableObject)c.Map[Read(source,"m_Attributes")];c.Movement=(ScriptableObject)c.Map[Read(source,"m_Movement")];
            c.View=(ScriptableObject)c.Map[Read(source,"m_View")];c.Hud=(ScriptableObject)c.Map[Read(source,"m_HUD")];
            ScriptableObject originalNav=Read(c.Core,"m_NavMesh") as ScriptableObject;
            if(originalNav==null)throw new InvalidOperationException("Native carrier lacks its navigation asset");
            c.Nav=CloneAsset(c,originalNav,UnitKey+"_Nav");
            PrepareNestedComponentCopies(c);
            // No carrier combat/factory capability is inferred from a static shell.
            ClearArray(c.Core,"m_WeaponLoadout");ClearArray(c.Custom,"m_Weapons");
            List<ScriptableObject> commands=new List<ScriptableObject>();bool move=false,stop=false;
            Array sourceAbilities=Read(source,"m_Abilities") as Array;
            if(sourceAbilities!=null)foreach(ScriptableObject ability in sourceAbilities)
            {
                if(ability==null)continue;AbilityAttributes attrs=Read(ability,"m_Attributes") as AbilityAttributes;
                if(attrs==null||string.IsNullOrEmpty(attrs.Name)||attrs.Name!=ability.name)throw new InvalidOperationException("Carrier command identity is malformed");
                if(!attrs.Name.StartsWith("Ability_Common_",StringComparison.Ordinal))continue;
                ScriptableObject clone=CloneAsset(c,ability,ability.name);commands.Add(clone);c.Commands.Add(clone);
                if(attrs.Name=="Ability_Common_MoveAbility")move=true;
                if(attrs.Name.IndexOf("Stop",StringComparison.OrdinalIgnoreCase)>=0)stop=true;
            }
            if(!move)throw new InvalidOperationException("Native carrier has no common move command");
            Array commandArray=Array.CreateInstance(sourceAbilities.GetType().GetElementType(),commands.Count);
            for(int index=0;index<commands.Count;index++)commandArray.SetValue(commands[index],index);
            Write(c.Custom,"m_Abilities",commandArray);
            foreach(ScriptableObject asset in c.Owned)RemapAsset(c,asset);
            Configure(c);
            PrepareProduction(c);
            PrepareWeapons(c);
            PrepareGunneryUnlock(c);
            PrepareSupport(c,"V03_HP_RepairBeamOrigin_PORT","V03_HP_RepairBeamOrigin_STARBOARD");
            PrepareAviation(c,RhodesResearch.Aviation1,GetV05Runway());
            PrepareAirWing(c);
            ConfigureV17AirBays(c);
            PreparePower(c);
            VerifyRhodesWeaponFX(c);
            PrepareReferencedLeafCopies(c);
            foreach(ScriptableObject asset in c.Owned)RemapAsset(c,asset);
            foreach(ScriptableObject asset in c.Owned)
                if(!object.ReferenceEquals(asset,c.Custom)&&!object.ReferenceEquals(asset,c.Core))CallIfPresent(asset,"OnEnable");
            Write(c.Custom,"mDataCache",null);CallRequired(c.Custom,"OnEnable");
            FinishWeapons(c);
            FinishPower(c);
            VerifyPrepared(c);
            EntityTypeAttributes definition=new EntityTypeAttributes(UnitKey);
            foreach(object value in (IEnumerable)c.Custom)if(value!=null&&!definition.Add(value))throw new InvalidOperationException("Duplicate Rhodes datum "+value.GetType().FullName);
            current.Add(definition);VerifyRegistered();Ready=true;
            object registeredDynamics=Read(Read(c.Movement,"m_Simulation"),"m_Dynamics"),registeredNav=Read(c.Nav,"m_NavMeshConfig");
            Runtime.Log("RHODES_UNIT_REGISTERED key="+UnitKey+"; donor="+SourceKey+"; sourceUnchanged=True; coreMovementHudViewNavAndCommonCommandsPrivate=True"+
                "; lengthMetres="+Settings.Length+"; widthMetres="+Settings.Width+"; heightMetres="+Settings.Height+"; dimensionsAreStressTestNotCanon=True"+
                "; nativeCapsuleWidth="+Read(registeredDynamics,"m_Width")+"; nativeCapsuleLength="+Read(registeredDynamics,"m_Length")+
                "; nativeNavClearance="+Read(registeredNav,"m_DistanceFromObstacles")+"; navigationGeometryReference="+SourceKey+
                "; visualAndNavigationDimensionsIndependent=True; visualFormationSpacingRetained=True"+
                "; maxSpeedNativeValue="+Settings.Speed+"; speedReference=C_Carrier_MP; commonMove=True; commonStop="+stop+"; weaponBindings=8; formalFactoryProducts=8; nativeProductionQueue=True; nativeResearch=True; mainResearchGated=True; activeFlatMapAcceptancePending=True");
        }

        // These native OnEnable/Initialize methods reconstruct flattened data
        // from nested ScriptableObjects. Copying only the outer component is
        // insufficient: it would reattach the original mutable Attributes.
        // Keep canonical nested asset names/IDs; identity isolation is by object.
        static void PrepareNestedComponentCopies(Context c)
        {
            int systems=0,effects=0,inventories=0;
            ScriptableObject power=Read(c.Source,"m_PowerShunt") as ScriptableObject;
            if(power!=null)
            {
                Array sourceSystems=Read(Read(power,"m_Attributes"),"m_PowerSystemAssets") as Array;
                if(sourceSystems!=null)foreach(ScriptableObject system in sourceSystems)
                {
                    if(system==null)continue;
                    CloneAsset(c,system,system.name);systems++;
                    Array levels=Read(Read(system,"m_Attributes"),"m_PowerLevels") as Array;
                    if(levels!=null)foreach(object level in levels)
                    {
                        if(level==null)continue;
                        Array sourceEffects=Read(level,"m_StatusEffectsToApply") as Array;
                        if(sourceEffects!=null)foreach(ScriptableObject effect in sourceEffects)
                            if(effect!=null){CloneAsset(c,effect,effect.name);effects++;}
                    }
                }
            }
            ScriptableObject storage=Read(c.Source,"m_Storage") as ScriptableObject;
            if(storage!=null)
            {
                Array loadout=Read(storage,"m_InventoryLoadout") as Array;
                if(loadout!=null)foreach(object binding in loadout)
                {
                    if(binding==null)continue;
                    ScriptableObject inventory=Read(binding,"m_InventoryAttribute") as ScriptableObject;
                    if(inventory!=null){CloneAsset(c,inventory,inventory.name);inventories++;}
                }
            }
            Runtime.Log("RHODES_NESTED_COMPONENT_COPIES powerSystems="+systems+"; statusEffectLinks="+effects+"; inventoryLinks="+inventories+
                "; canonicalNestedNamesRetained=True; sourceOnEnableNotInvoked=True; outerRemapAndCacheRebuildPending=True; sharedMutableVerificationStillRequired=True");
        }

        // Ability.OnEnable also rehydrates ApplyStatusEffect caches. This pass
        // intentionally runs after production/rally clones have been attached.
        // Follow BBI managed fields only and clone a bounded set of data leaves;
        // never walk into native unit/prefab/texture or arbitrary Unity assets.
        static void PrepareReferencedLeafCopies(Context c)
        {
            int before=c.Owned.Count;
            for(int index=0;index<c.Owned.Count;index++)
            {
                if(c.Owned.Count>1024)throw new InvalidOperationException("Rhodes nested data-asset closure exceeded its bound");
                ScriptableObject asset=c.Owned[index];HashSet<object> seen=new HashSet<object>(new IdentityComparer());
                foreach(FieldInfo field in Fields(asset.GetType()))if(field.Name!="mDataCache")CollectReferencedLeaves(c,field.GetValue(asset),seen);
            }
            Runtime.Log("RHODES_REFERENCED_LEAF_COPIES added="+(c.Owned.Count-before)+"; totalPrivateAssets="+c.Owned.Count+
                "; afterProductionClones=True; supportedLeaves=StatusEffect,Inventory,PowerSystem; canonicalNamesRetained=True; completeOwnedRemapBeforeOnEnable=True; sharedMutableCheckUnchanged=True");
        }
        static void CollectReferencedLeaves(Context c,object value,HashSet<object> seen)
        {
            if(value==null)return;
            ScriptableObject asset=value as ScriptableObject;
            if(asset!=null)
            {
                foreach(ScriptableObject owned in c.Owned)if(object.ReferenceEquals(owned,asset))return;
                string typeName=asset.GetType().FullName;
                if(typeName=="BBI.Unity.Game.Data.StatusEffectAttributesAsset"||typeName=="BBI.Unity.Game.Data.InventoryAttributesAsset"||typeName=="BBI.Unity.Game.Data.PowerSystemAttributesAsset")
                    CloneAsset(c,asset,asset.name);
                return;
            }
            Type type=value.GetType();
            if(type.IsPrimitive||type.IsEnum||type==typeof(string)||typeof(UnityEngine.Object).IsAssignableFrom(type))return;
            if(!type.IsValueType&&!seen.Add(value))return;
            Array array=value as Array;
            if(array!=null){foreach(object item in array)CollectReferencedLeaves(c,item,seen);return;}
            if(type.Namespace!=null&&type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))
                foreach(FieldInfo field in Fields(type))CollectReferencedLeaves(c,field.GetValue(value),seen);
        }

        static void Configure(Context c)
        {
            object core=Read(c.Core,"m_Attributes");
            Write(core,"m_MaxHealth",Settings.Health);Write(core,"m_Armour",Settings.Armour);
            Write(core,"m_WorldHeightOffset",0f);Write(core,"m_Selectable",true);Write(core,"m_Controllable",true);Write(core,"m_StartsInHangar",false);
            Write(core,"m_SensorRadius",Settings.Sensor);Write(core,"m_ContactRadius",Settings.Contact);
            Write(core,"m_AggroRange",0);Write(core,"m_AlertRange",0);Write(core,"m_LeashRange",0);
            object movement=Read(c.Movement,"m_Simulation"),dynamics=Read(movement,"m_Dynamics");
            ConfigureCarrierCombat(movement);
            object maneuvers=Read(movement,"m_Maneuvers"),avoidance=Read(movement,"m_Avoidance");
            object donorMovement=Read(Read(c.Source,"m_Movement"),"m_Simulation"),donorDynamics=Read(donorMovement,"m_Dynamics");
            object donorManeuvers=Read(donorMovement,"m_Maneuvers");
            object donorCore=Read(c.Source,"m_Attributes"),donorNav=Read(Read(donorCore,"m_NavMesh"),"m_NavMeshConfig");
            ConfigureNativeCarrierNavigation(Read(c.Nav,"m_NavMeshConfig"),dynamics,maneuvers,avoidance,
                donorNav,donorDynamics,donorManeuvers,Read(donorMovement,"m_Avoidance"));
            NativeFormationSpacing spacing=FormationSpacingPolicy.Evaluate(Settings.Width,Settings.Length,
                (float)Read(donorDynamics,"m_Width"),(float)Read(donorDynamics,"m_Length"),
                (float)Read(donorManeuvers,"m_FormationSpacingDistance"),(float)Read(donorManeuvers,"m_FormationSectionGapAhead"));
            float donorMax=Convert.ToSingle(Read(donorDynamics,"m_MaxSpeed"),CultureInfo.InvariantCulture);
            float donorCruise=Convert.ToSingle(Read(donorDynamics,"m_MinCruiseSpeed"),CultureInfo.InvariantCulture);
            if(float.IsNaN(donorMax)||float.IsInfinity(donorMax)||donorMax<=0f||float.IsNaN(donorCruise)||float.IsInfinity(donorCruise)||donorCruise<0f||donorCruise>donorMax)
                throw new InvalidOperationException("Native Coalition carrier speed ratio contract changed");
            float cruiseRatio=donorCruise/donorMax;
            Write(movement,"m_DriveType",UnitDriveType.Tracked);
            Write(dynamics,"m_MaxSpeed",Settings.Speed);
            Write(dynamics,"m_MinCruiseSpeed",Settings.Speed*cruiseRatio);Write(dynamics,"m_ReverseFactor",.35f);
            Write(dynamics,"m_AccelerationTime",10f);Write(dynamics,"m_BrakingTime",6f);
            Write(dynamics,"m_MaxEaseIntoTurnTime",2f);Write(dynamics,"m_PermanentlyImmobile",false);
            foreach(string field in new string[]{"m_AccelerationTimeRandomVariancePercentage","m_MaxSpeedTurnRadiusRandomVariancePercentage","m_DriftType","m_DriftTypeRandomVariancePercentage","m_DriftOvershootFactor","m_DriftOvershootFactorRandomVariancePercentage","m_FishTailControlRecover","m_MinDriftSlipSpeedRandomVariancePercentage","m_DeathDriftTime"})Write(dynamics,field,0f);
            Write(dynamics,"m_ReverseDriftMultiplier",0f);Write(dynamics,"m_FishTailingTimeIntervals",1f);Write(dynamics,"m_MinDriftSlipSpeed",1000f);Write(dynamics,"m_MaxDriftRecoverTime",.1f);
            foreach(string field in new string[]{"m_CruiseSpeedVariations","m_CruiseDirectionVariations"})ClearArray(dynamics,field);
            Write(maneuvers,"m_FormationSpacingDistance",spacing.MemberGap);
            Write(maneuvers,"m_FormationSectionGapAhead",spacing.SectionGapAhead);
            foreach(string field in new string[]{"m_FastDirectTurn","m_FastAwayTurn","m_SlowDirectTurn","m_SlowAwayTurn"})Write(maneuvers,field,TurnStyle.Controlled);
            Write(maneuvers,"m_MustReachMinSpeedBeforeTurn",false);Write(Read(maneuvers,"m_ThreePointTurn"),"m_MustReachMinSpeedBeforeTurn",false);
            Write(Read(maneuvers,"m_JTurn"),"m_TurnStyle",TurnStyle.None);Write(maneuvers,"m_DriftRecoverOptions",new DriftRecoverStyle[]{DriftRecoverStyle.Neutral});
            Runtime.Log("RHODES_MOVEMENT_PREPARED maxSpeedNativeValue="+Settings.Speed+"; minCruiseNativeValue="+(Settings.Speed*cruiseRatio)+"; donorMaxNativeValue="+donorMax+"; donorCruiseNativeValue="+donorCruise+
                "; speedReference=C_Carrier_MP; navigationGeometryReference=C_Carrier_MP; nativeCapsuleWidth="+Read(dynamics,"m_Width")+
                "; nativeCapsuleLength="+Read(dynamics,"m_Length")+"; nativeTurnRadius="+Read(dynamics,"m_MaxSpeedTurnRadius")+
                "; visualLength="+Settings.Length+"; visualFormationSpacingRetained=True; physicalUnitConversionNotClaimed=True");
            UnitViewAttributes view=(UnitViewAttributes)Read(c.View,"Data"),native=(UnitViewAttributes)Read(Read(c.Source,"m_View"),"Data");
            c.NativePrefab=native.ModelPrefab;
            foreach(string field in new string[]{"SuspensionActiveLODs","WheelActuationActiveLODs","TurretActuationActiveLODs","BodyMotionActiveLODs"})
            { object value=Read(view.LODTunings,field);Write(view.LODTunings,field,Enum.ToObject(value.GetType(),0)); }
            Write(view.LODTunings,"ForceIconOnlyInSensors",false);
            view.NLIPSScale=new AnimationCurve(new Keyframe(0f,1f),new Keyframe(100000f,1f));
            // Hull-level carrier effects reference donor-only bones. Native
            // weapon muzzle/tracer/impact/repair FX are preserved separately in
            // private WeaponViewAttributes.WeaponFX and rebound per turret.
            // V05 currently uses an honest fixed-guide aviation test; no donor
            // hull animation strings are treated as real V05 clips.
            view.UnitEffectDefs=CopyNativeMovementAudio(native);view.WheelFXDefinitions=new UnitViewAttributes.WheelAttributes[0];
            view.TreadDefinitions=new UnitViewAttributes.TreadAttributes[0];view.TurretDefinitions=new UnitViewAttributes.TurretAttributes[0];
            object animations=view.AnimationDefinitions;
            if(animations!=null)
            {
                foreach(string field in new string[]{"EnableMovementLayerX","EnableMovementLayerY","EnableMovementLayerZ","EnableFiringLayer"})Write(animations,field,false);
                foreach(string field in new string[]{"ResearchUpgrades","AbilityStates","AnimationTriggers","ProductionEffects","ProductionAnimations"})ClearArray(animations,field);
            }
            if(view.FocusSettings==null)view.FocusSettings=new UnitViewAttributes.CameraFocusSettings();
            view.FocusSettings.ClosestCameraFocus=Settings.Length*.7f;
            view.ModelPrefab=RhodesCarrierModel.Create(c.NativePrefab,Settings.Length);
            RhodesCarrierBinding nativeLodBinding=view.ModelPrefab.GetComponent<RhodesCarrierBinding>();
            if(nativeLodBinding==null||native.LODTunings==null)throw new InvalidOperationException("Rhodes carrier native LOD contract missing");
            nativeLodBinding.NativeWheelActiveLODs=Convert.ToInt32(Read(native.LODTunings,"WheelActuationActiveLODs"),CultureInfo.InvariantCulture);
            nativeLodBinding.NativeSuspensionActiveLODs=Convert.ToInt32(Read(native.LODTunings,"SuspensionActiveLODs"),CultureInfo.InvariantCulture);
            nativeLodBinding.NativeAlwaysHighLOD=native.AlwaysHighLOD;nativeLodBinding.NativeLodConfigured=true;
            NativeGroundEffects54.AppendCarrier(view,native,nativeLodBinding);
            NativeGroundTracks55.AppendCarrier(view,native,nativeLodBinding);
            NativeGroundStatuses55.AppendCarrier(view,native,nativeLodBinding);
            EntityTypeAttributes smallLightDonorType=Registry.GetEntityType("C_Escort_MP");
            RhodesNativeLightFactory.Append(view,native,smallLightDonorType==null?null:smallLightDonorType.Get<UnitViewAttributes>(),nativeLodBinding);
            EntityTypeAttributes beaconDonorType=Registry.GetEntityType("G_Carrier_MP");
            RhodesBeaconFactory.Append(view,beaconDonorType==null?null:beaconDonorType.Get<UnitViewAttributes>(),nativeLodBinding);
            object hud=Read(c.Hud,"m_Attributes");Write(hud,"mUnitLocalizedNameStringID","罗德岛本舰");
            Write(hud,"mUnitLocalizedShortDescriptionStringID","机动指挥与生产中心，可选择航空或炮战改装");
            Write(hud,"mUnitLocalizedLongDescriptionStringID","负责资源接收、车辆生产、维修支援与近防火力。航空改装开放舰载截击机和重型轰炸机，炮战改装部署舰体主炮；两条路线互斥。倾转旋翼机机库独立建设，可用于干员运输与部署。");
            object icons=Read(hud,"m_IconSettings");Write(icons,"m_IconYOffset",Settings.Height+10f);Write(icons,"m_UnderlayOffset",.15f);
            RhodesHudIcon.Configure(hud,Read(Read(c.Source,"m_HUD"),"m_Attributes"));
            object deathAsset=Read(c.Custom,"m_Death");
            if(deathAsset!=null)
            {
                object death=Read(deathAsset,"m_Attributes"),destruction=Read(death,"DeathAttributes");
                if(destruction!=null)foreach(string field in new string[]{"CarcassPrefab","InitialBurstPrefab","DeathPingPrefab"})Write(destruction,field,null);
                object ground=Read(death,"GroundUnitDestruction");
                if(ground!=null){Write(ground,"TripOnDeath",false);Write(ground,"MapWheelBones",false);Write(ground,"SteeringWheels",new string[0]);}
            }
        }

        public static void ObserveViews()
        {if(Ready&&Current!=null)UnitBindingRouter.BindCarrier(BindSpawnedCarrier);}
        static bool TryBindSpawnedCarrier(BBI.Core.ComponentModel.Entity entity,UnitView view)
        {
            if(view==null||!entity.Equals(view.EntityID))return true;
            if(!view.gameObject.activeInHierarchy||view.SimStateInterpolator==null||view.UnitAttrs==null)return false;
            // Native power/status buffs wrap UnitAttrs; the private prefab
            // binding remains the stable identity for this authored geometry.
            RhodesCarrierBinding binding=view.GetComponent<RhodesCarrierBinding>();
            if(binding==null)return false;
            RhodesCarrierView driver=view.GetComponent<RhodesCarrierView>();
            if(driver!=null&&driver.PresentationFailed)return true;
            if(driver==null)driver=view.gameObject.AddComponent<RhodesCarrierView>();
            try
            {
                if(binding.GeometryRoot==null||!binding.GeometryRoot.IsChildOf(view.transform)||binding.Geometry==null||binding.Geometry.Length==0||(!binding.IsV05&&binding.Geometry.Length!=1))throw new InvalidOperationException("Spawned Rhodes view lacks private geometry binding");
                foreach(Renderer renderer in binding.Geometry)if(renderer==null||!renderer.transform.IsChildOf(binding.GeometryRoot))throw new InvalidOperationException("Rhodes renderer lies outside its private geometry root");
                driver.Initialize(view,binding);
                ObserveWeaponView(view);RhodesDefaultColours.ReportSpawnColours(view);
                if(driver.Validated)ViewsVerified=true;
                return driver.Validated;
            }
            catch(Exception error){driver.QuarantineBinding(view,"carrier_bind",error);return true;}
        }
        public static void Reset()
        { ViewsVerified=false;RhodesWeaponPresentation.ResetAll();RhodesCarrierModel.ResetViews(); }

        static void VerifyPrepared(Context c)
        {
            foreach(SourceRecord source in c.Sources)
            {
                if(Fingerprint(source.Asset)!=source.Fingerprint)throw new InvalidOperationException("Native carrier asset changed: "+source.Asset.name);
                if(!object.ReferenceEquals(Registry.GetEntityType(source.Asset.name),source.Definition))throw new InvalidOperationException("Native carrier registry identity changed: "+source.Asset.name);
            }
            HashSet<object> native=new HashSet<object>(new IdentityComparer()),seen=new HashSet<object>(new IdentityComparer());
            foreach(SourceRecord source in c.Sources)foreach(FieldInfo field in Fields(source.Asset.GetType()))if(field.Name!="mDataCache")Gather(field.GetValue(source.Asset),native);
            foreach(ScriptableObject asset in c.Owned)foreach(FieldInfo field in Fields(asset.GetType()))if(field.Name!="mDataCache")Check(field.GetValue(asset),native,seen,asset.name+"<"+asset.GetType().Name+">."+field.Name);
            UnitAttributes core=(UnitAttributes)Read(c.Core,"m_Attributes");
            UnitViewAttributes view=(UnitViewAttributes)Read(c.View,"Data"),sourceView=(UnitViewAttributes)Read(Read(c.Source,"m_View"),"Data");
            if(core.MaxHealth!=Settings.Health||core.WeaponLoadout==null||core.WeaponLoadout.Length!=8||view.ModelPrefab==null||view.ModelPrefab==sourceView.ModelPrefab||view.ModelPrefab.GetComponent<RhodesCarrierBinding>()==null)
                throw new InvalidOperationException("Prepared Rhodes core/model is incomplete");
            if(object.ReferenceEquals(core.NavMeshAttributes,((UnitAttributes)Read(Read(c.Source,"m_Attributes"),"m_Attributes")).NavMeshAttributes)||object.ReferenceEquals(view.LODTunings,sourceView.LODTunings)||object.ReferenceEquals(view.FocusSettings,sourceView.FocusSettings))
                throw new InvalidOperationException("Rhodes navigation/LOD/focus data aliases native carrier");
            object dynamics=Read(Read(c.Movement,"m_Simulation"),"m_Dynamics");
            object movement=Read(c.Movement,"m_Simulation"),donorMovement=Read(Read(c.Source,"m_Movement"),"m_Simulation");
            object donorNav=Read(Read(Read(c.Source,"m_Attributes"),"m_NavMesh"),"m_NavMeshConfig");
            VerifyNativeCarrierNavigation(Read(c.Nav,"m_NavMeshConfig"),dynamics,Read(movement,"m_Maneuvers"),Read(movement,"m_Avoidance"),
                donorNav,Read(donorMovement,"m_Dynamics"),Read(donorMovement,"m_Maneuvers"),Read(donorMovement,"m_Avoidance"));
            Array abilities=Read(c.Custom,"m_Abilities") as Array;
            if(abilities==null||abilities.Length!=c.Commands.Count+c.Products.Count)throw new InvalidOperationException("Rhodes command/production array changed");
            foreach(ScriptableObject command in abilities)
            {
                bool known=c.Commands.Contains(command);
                foreach(ProductionItem product in c.Products)if(object.ReferenceEquals(command,product.Ability))known=true;
                if(command==null||!known)throw new InvalidOperationException("Unexpected Rhodes production or special ability");
            }
            VerifyProduction(c);VerifyWeapons(c,true);VerifyAviation(c);VerifySupport(c);VerifyPower(c);VerifyMovementAudio(view,sourceView);VerifyCarrierCombat(c);
        }
        static void VerifyRegistered()
        {
            Context c=Current;if(c==null)throw new InvalidOperationException("Rhodes context missing");VerifyPrepared(c);
            EntityTypeAttributes definition=Registry.GetEntityType(UnitKey);
            if(definition==null||!object.ReferenceEquals(definition.Get<UnitAttributes>(),Read(c.Core,"m_Attributes"))||!object.ReferenceEquals(definition.Get<UnitMovementAttributes>(),Read(c.Movement,"m_Simulation")))
                throw new InvalidOperationException("Rhodes registry does not reference private core/movement data");
            foreach(ScriptableObject command in c.Commands)if(!object.ReferenceEquals(definition.Get<AbilityAttributes>(command.name),Read(command,"m_Attributes")))throw new InvalidOperationException("Rhodes common command is not independently cached");
        }
        static Configuration LoadConfiguration()
        {
            Configuration config=new Configuration();string path=Path.Combine(CustomUnits.AssetDirectory,"rhodes_carrier/rhodes_unit.ini");
            if(File.Exists(path))foreach(string raw in File.ReadAllLines(path))
            {
                string line=raw.Trim();if(line.Length==0||line.StartsWith("#")||line.StartsWith(";")||line.StartsWith("["))continue;
                int split=line.IndexOf('=');if(split<1)throw new InvalidDataException("Malformed Rhodes setting");string key=line.Substring(0,split).Trim(),text=line.Substring(split+1).Trim();float value;
                if(!float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||float.IsNaN(value)||float.IsInfinity(value))throw new InvalidDataException("Invalid Rhodes setting: "+key);
                if(key=="LengthMetres")config.Length=value;else if(key=="MaxSpeedMetresPerSecond")config.Speed=value;else if(key=="SensorRadiusMetres")config.Sensor=value;else if(key=="ContactRadiusMetres")config.Contact=value;
                else if(key=="HitPoints")config.Health=(int)value;else if(key=="Armour")config.Armour=(int)value;else throw new InvalidDataException("Unknown Rhodes setting: "+key);
            }
            if(config.Length<100f||config.Length>1000f||config.Speed<=0f||config.Speed>60f||config.Sensor<config.Length*.6f||config.Contact<config.Sensor||config.Health<1||config.Armour<0)
                throw new InvalidDataException("Rhodes prototype settings outside safe bounds");
            return config;
        }
        static void ReleaseStale()
        {
            if(Current==null)return;
            foreach(UnitView view in Resources.FindObjectsOfTypeAll(typeof(UnitView)))
                if(view!=null&&view.gameObject.activeInHierarchy&&view.GetComponent<RhodesCarrierBinding>()!=null)
                    throw new InvalidOperationException("Cannot release Rhodes assets used by an active UnitView");
            foreach(UnitView view in Resources.FindObjectsOfTypeAll(typeof(UnitView)))
            {
                if(view==null||!view.gameObject.activeInHierarchy)continue;
                BBI.Game.Simulation.UnitState state=view.NonInterpolatedTickState;
                if(state!=null&&(state.TypeID==AircraftKey||state.TypeID==HeavyBomberKey))throw new InvalidOperationException("Cannot release private aircraft assets used by an active UnitView");
            }
            RhodesWeaponPresentation.ResetAll();ReleaseAviationAssets();RhodesCarrierModel.ReleaseTemplates();
            for(int index=Current.Owned.Count-1;index>=0;index--)if(Current.Owned[index]!=null)UnityEngine.Object.DestroyImmediate(Current.Owned[index]);Current=null;
        }

        static ScriptableObject CloneAsset(Context c,ScriptableObject source,string name)
        {
            if(source==null)throw new InvalidOperationException("Rhodes source asset absent: "+name);object prior;if(c.Map.TryGetValue(source,out prior))return (ScriptableObject)prior;
            c.Sources.Add(new SourceRecord{Asset=source,Fingerprint=Fingerprint(source),Definition=Registry.GetEntityType(source.name)});
            ScriptableObject clone=RhodesAssetClone59.CreateEmpty(source.GetType());c.Map.Add(source,clone);
            Dictionary<object,object> copied=new Dictionary<object,object>(new IdentityComparer());
            foreach(FieldInfo field in Fields(source.GetType()))if(field.Name!="mDataCache")field.SetValue(clone,CopyData(field.GetValue(source),copied));
            clone.name=name;clone.hideFlags=HideFlags.HideAndDontSave;c.Owned.Add(clone);return clone;
        }
        static object CopyData(object value,Dictionary<object,object> seen)
        {
            if(value==null)return null;Type type=value.GetType();if(type.IsPrimitive||type.IsEnum||type==typeof(string)||typeof(UnityEngine.Object).IsAssignableFrom(type))return value;
            object previous;if(seen.TryGetValue(value,out previous))return previous;Array array=value as Array;
            if(array!=null){if(array.Rank!=1)throw new InvalidOperationException("Unexpected multidimensional carrier data");Array clone=(Array)array.Clone();seen.Add(value,clone);for(int index=0;index<array.Length;index++)clone.SetValue(CopyData(array.GetValue(index),seen),index);return clone;}
            if(type.Namespace==null||!type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))return value;
            object result=CloneMethod.Invoke(value,null);seen.Add(value,result);foreach(FieldInfo field in Fields(type))field.SetValue(result,CopyData(field.GetValue(value),seen));return result;
        }
        static void RemapAsset(Context c,ScriptableObject asset)
        {HashSet<object> seen=new HashSet<object>(new IdentityComparer());foreach(FieldInfo field in Fields(asset.GetType()))if(field.Name!="mDataCache")field.SetValue(asset,Remap(c,field.GetValue(asset),seen));}
        static object Remap(Context c,object value,HashSet<object> seen)
        {
            if(value==null)return null;object mapped;if(c.Map.TryGetValue(value,out mapped))return mapped;
            Type type=value.GetType();if(type.IsPrimitive||type.IsEnum||type==typeof(string)||typeof(UnityEngine.Object).IsAssignableFrom(type)||!seen.Add(value))return value;
            Array array=value as Array;if(array!=null){for(int index=0;index<array.Length;index++)array.SetValue(Remap(c,array.GetValue(index),seen),index);return array;}
            if(type.Namespace!=null&&type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))foreach(FieldInfo field in Fields(type))field.SetValue(value,Remap(c,field.GetValue(value),seen));return value;
        }
        static bool Mutable(object value)
        {if(value==null)return false;Type type=value.GetType();return !type.IsValueType&&type!=typeof(string)&&!typeof(UnityEngine.Object).IsAssignableFrom(type)&&(value is Array||(type.Namespace!=null&&type.Namespace.StartsWith("BBI.",StringComparison.Ordinal)));}
        static void Gather(object value,HashSet<object> result)
        {if(!Mutable(value)||!result.Add(value))return;Array array=value as Array;if(array!=null){foreach(object item in array)Gather(item,result);return;}foreach(FieldInfo field in Fields(value.GetType()))Gather(field.GetValue(value),result);}
        static void Check(object value,HashSet<object> native,HashSet<object> seen,string path)
        {if(!Mutable(value)||!seen.Add(value))return;if(native.Contains(value))throw new InvalidOperationException("Rhodes mutable data aliases native source: "+value.GetType().FullName+"; ownedPath="+path);Array array=value as Array;if(array!=null){for(int index=0;index<array.Length;index++)Check(array.GetValue(index),native,seen,path+"["+index+"]");return;}foreach(FieldInfo field in Fields(value.GetType()))Check(field.GetValue(value),native,seen,path+"."+field.Name);}
        static string Fingerprint(ScriptableObject asset)
        {StringBuilder text=new StringBuilder();HashSet<object> seen=new HashSet<object>(new IdentityComparer());foreach(FieldInfo field in Fields(asset.GetType())){text.Append(field.DeclaringType.FullName).Append('.').Append(field.Name).Append('=');AppendValue(text,field.GetValue(asset),seen);}using(SHA256 hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-","");}
        static void AppendValue(StringBuilder text,object value,HashSet<object> seen)
        {
            if(value==null){text.Append("null;");return;}Type type=value.GetType();text.Append(type.FullName).Append(':');UnityEngine.Object unity=value as UnityEngine.Object;
            if(!object.ReferenceEquals(unity,null)){text.Append(unity.GetInstanceID()).Append('/').Append(unity.name).Append(';');return;}
            if(type.IsPrimitive||type.IsEnum||type==typeof(string)){text.Append(Convert.ToString(value,CultureInfo.InvariantCulture)).Append(';');return;}
            if(type.IsValueType){foreach(FieldInfo field in Fields(type)){text.Append(field.Name).Append('=');AppendValue(text,field.GetValue(value),seen);}return;}
            if(!seen.Add(value)){text.Append("seen;");return;}Array array=value as Array;if(array!=null){text.Append(array.Length).Append('[');foreach(object item in array)AppendValue(text,item,seen);text.Append(']');return;}
            if(type.Namespace!=null&&type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))foreach(FieldInfo field in Fields(type)){text.Append(field.Name).Append('=');AppendValue(text,field.GetValue(value),seen);}else text.Append(RuntimeHelpers.GetHashCode(value));
        }
        static List<FieldInfo> Fields(Type type)
        {List<FieldInfo> result=new List<FieldInfo>();for(Type current=type;current!=null&&current!=typeof(object)&&current!=typeof(UnityEngine.Object)&&current!=typeof(ScriptableObject);current=current.BaseType)foreach(FieldInfo field in current.GetFields(Flags))if(!field.IsStatic)result.Add(field);result.Sort(delegate(FieldInfo a,FieldInfo b){return string.CompareOrdinal(a.DeclaringType.FullName+"."+a.Name,b.DeclaringType.FullName+"."+b.Name);});return result;}
        static object Read(object value,string name)
        {if(value==null)throw new ArgumentNullException(name);for(Type type=value.GetType();type!=null;type=type.BaseType){FieldInfo field=type.GetField(name,Flags);if(field!=null)return field.GetValue(value);PropertyInfo property=type.GetProperty(name,Flags);if(property!=null)return property.GetValue(value,null);}throw new MissingMemberException(value.GetType().FullName,name);}
        static void Write(object value,string name,object data)
        {for(Type type=value.GetType();type!=null;type=type.BaseType){FieldInfo field=type.GetField(name,Flags);if(field!=null){field.SetValue(value,data);return;}}throw new MissingFieldException(value.GetType().FullName,name);}
        static void ClearArray(object value,string name)
        {foreach(FieldInfo field in Fields(value.GetType()))if(field.Name==name){if(!field.FieldType.IsArray)throw new InvalidDataException("Expected carrier array: "+name);field.SetValue(value,Array.CreateInstance(field.FieldType.GetElementType(),0));return;}throw new MissingFieldException(value.GetType().FullName,name);}
        static bool CallIfPresent(object value,string name)
        {for(Type type=value.GetType();type!=null;type=type.BaseType){MethodInfo method=type.GetMethod(name,Flags,null,Type.EmptyTypes,null);if(method!=null){method.Invoke(value,null);return true;}}return false;}
        static void CallRequired(object value,string name){if(!CallIfPresent(value,name))throw new MissingMethodException(value.GetType().FullName,name);}
    }
}
