// Local human / squad movement experiment. The native simulation still owns
// commands, health and one selectable entity per squad; members are visual only.
// This module does not spawn units, alter native assets, or call BVM tuning.
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
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class HumanoidUnits
    {
        public const int PerlicaBasicReloadMilliseconds=900,NativeBasicWindupMilliseconds=250;
        public const string PerlicaKey = "EF_Perlica_Test";
        public const string LaevatainKey = "EF_Laevatain_Test";
        public const string GilbertaKey = "EF_Gilberta_Test";
        public const string ArdeliaKey = "EF_Ardelia_Test";
        public const int GilbertaBasicWindupMilliseconds=450;
        public const int LaevatainBasicReloadMilliseconds=900,LaevatainBasicWindupMilliseconds=250;
        // Reference trial: public relay-distance measurement reports 70m in
        // 10s while running. This is not an audited official gameplay constant.
        public const float OperatorRunMetresPerSecond=7f;
        public const string GuardSquadKey = "EF_GuardSquad_Test";
        public const string NativeSourceKey = "C_Escort_MP";
        public const int GuardMemberCount = 6;
        public const int GuardSquadHitpoints = 1200;
        public static bool Ready { get; private set; }
        // Copy-on-read: callers cannot mutate our list of registered unit keys.
        public static string[] Keys { get { return Runtime.SkirmishMode ? new string[] { LaevatainKey, GilbertaKey, ArdeliaKey } : Runtime.RhodesCarrier ? new string[] { PerlicaKey, GuardSquadKey, LaevatainKey, GilbertaKey, ArdeliaKey } : new string[] { PerlicaKey, GuardSquadKey }; } }
        static EntityTypeCollection Registry;
        static readonly List<Context> Contexts = new List<Context>();
        static readonly MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        sealed class Context
        {
            public string Key;
            public UnitAssetContainer Source, Custom;
            public ScriptableObject Core, Movement, Nav, View, Hud, Storage;
            public WeaponAttributesAsset Weapon;
            public InventoryAttributesAsset Ammo;
            public readonly List<ScriptableObject> BasicAbilities = new List<ScriptableObject>();
            public ScriptableObject OperatorReturnAbility;
            public GameObject NativePrefab;
            public int Health, Armour;
            public HumanoidRangeProfile Ranges;
            public float Width, Length, Speed;
            public readonly Dictionary<object, object> AssetMap = new Dictionary<object, object>(new IdentityComparer());
            public readonly List<ScriptableObject> Owned = new List<ScriptableObject>();
            public readonly List<SourceRecord> Sources = new List<SourceRecord>();
        }
        sealed class SourceRecord
        {
            public ScriptableObject Asset;
            public string Fingerprint;
            public EntityTypeAttributes Definition;
        }
        sealed class IdentityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) { return object.ReferenceEquals(a, b); }
            public int GetHashCode(object value) { return RuntimeHelpers.GetHashCode(value); }
        }

        // Unity main thread, after native entity registration. No sim commands.
        // Two human definitions plus private basic weapons/ammo; Perlica owns
        // an additional, independently registered native support-weapon chain.
        public static void Register()
        {
            EntityTypeCollection current = (EntityTypeCollection)NativeRead.Get(typeof(ShipbreakersMain), "sEntityTypes");
            if (current == null) throw new InvalidOperationException("Humanoid registration requires native entity types");
            if (object.ReferenceEquals(current, Registry))
            {
                if (!Ready) throw new InvalidOperationException("Humanoid registration previously failed in this registry; reload before retry");
                VerifyRegistered(); return;
            }
            ReleaseStale(); Registry = current; Ready = false;
            EntityTypeAttributes native = current.GetEntityType(NativeSourceKey);
            if (native == null || native.Get<UnitAttributes>() == null)
                throw new InvalidOperationException("Humanoid native template missing: " + NativeSourceKey);
            UnitAssetContainer source = FindCurrentContainer(native.Get<UnitAttributes>());
            if (source == null) throw new InvalidOperationException("Current native C_Escort_MP container not found");
            HumanoidModel.AssetDirectory = CustomUnits.AssetDirectory;
            if(!Runtime.SkirmishMode)
            {Prepare(source, PerlicaKey, false);Prepare(source, GuardSquadKey, true);}
            else Runtime.Log("RHODES_SKIRMISH_LAB_HUMANOIDS_SKIPPED perlicaPrototype=False; guardSquadPrototype=False; playableOperatorsPreserved=True");
            if(Runtime.RhodesCarrier){Prepare(source, LaevatainKey, false);Prepare(source, GilbertaKey, false);Prepare(source, ArdeliaKey, false);}
            List<EntityTypeAttributes> definitions = new List<EntityTypeAttributes>();
            foreach (Context c in Contexts)
            {
                VerifyPrepared(c);
                definitions.Add(BuildDefinition(c.Weapon));
                definitions.Add(BuildDefinition(c.Ammo));
                definitions.Add(BuildDefinition(c.Custom));
            }
            foreach(Context c in Contexts)if(c.OperatorReturnAbility!=null)
            {definitions.Add(BuildDefinition(c.OperatorReturnAbility));break;}
            foreach (EntityTypeAttributes definition in definitions)
                if (current.GetEntityType(definition.Name) != null)
                    throw new InvalidOperationException("Humanoid independent key collision: " + definition.Name);
            foreach (EntityTypeAttributes definition in definitions) current.Add(definition);
            VerifyRegistered(); Ready = true;
            Runtime.Log("HUMANOID_UNITS_REGISTERED units="+Contexts.Count+"; keys=" + string.Join(",",Keys) +
                "; visualGuardMembers="+(Runtime.SkirmishMode?0:6)+"; selectableEntitiesPerSquad=1; nativeSource=" + NativeSourceKey +
                "; nativeSourceFingerprintsUnchanged=True; separateUnitHealthMovementHudNavWeaponAmmo=True" +
                "; nativeSimpleSymbolsRetained=True; globalAtlasChanged=False; nativeVehicleVisualMotion=False" +
                "; nativeRangeTestWeaponsEnabled=True; perlicaDirectHitPrototype="+(!Runtime.SkirmishMode)+"; orbitalSkillPrepared="+(!Runtime.SkirmishMode)+"; runtimeRenderingNotYetVerified=True");
        }

        static UnitAssetContainer FindCurrentContainer(UnitAttributes native)
        {
            foreach (UnitAssetContainer candidate in Resources.FindObjectsOfTypeAll(typeof(UnitAssetContainer)))
                if (candidate != null && string.Equals(candidate.name, NativeSourceKey, StringComparison.OrdinalIgnoreCase) &&
                    object.ReferenceEquals(Read(Read(candidate, "m_Attributes"), "m_Attributes"), native)) return candidate;
            return null;
        }

        static void Prepare(UnitAssetContainer source, string key, bool squad)
        {
            Context c = new Context(); c.Source = source; c.Key = key;
            c.Health = squad ? GuardSquadHitpoints : 1000; c.Armour = squad ? 2 : 5;
            c.Width = squad ? 8f : .9f; c.Length = squad ? 8f : .9f;
            c.Speed = squad ? 3.6f : OperatorRunMetresPerSecond;
            c.Ranges = squad ? HumanoidRangeProfile.Guard : HumanoidRangeProfile.Perlica;
            if(key==LaevatainKey){c.Health=1700;c.Armour=5;c.Speed=OperatorRunMetresPerSecond;c.Ranges=new HumanoidRangeProfile(45,220,260,0,0,0);}
            if(key==GilbertaKey){c.Health=1600;c.Armour=3;c.Speed=OperatorRunMetresPerSecond;c.Ranges=new HumanoidRangeProfile(55,220,260,0,0,0);}
            if(key==ArdeliaKey){c.Health=ArdeliaHealingProfile.Health;c.Armour=ArdeliaHealingProfile.Armour;c.Speed=OperatorRunMetresPerSecond;c.Ranges=new HumanoidRangeProfile(55,220,260,0,0,0);}
            Contexts.Add(c);
            c.Custom = (UnitAssetContainer)CloneAsset(c, source, key);
            // Every direct component asset is independent, including detectable,
            // audio and death data. Native mesh/FX/audio resources are immutable.
            foreach (FieldInfo field in Fields(source.GetType()))
            {
                if (field.Name == "mDataCache") continue;
                ScriptableObject component = field.GetValue(source) as ScriptableObject;
                if (component != null) CloneAsset(c, component, key + "_" + field.Name.TrimStart('m', '_'));
            }
            // Experience is a MultiAssetBase: its nested view datum is flattened
            // into UnitAssetContainer.mDataCache by OnEnable. Clone that data
            // asset before Remap so the cache cannot retain the donor's datum.
            ScriptableObject nativeExperience = Read(source, "m_Experience") as ScriptableObject;
            ScriptableObject nativeExperienceView = nativeExperience == null ? null : Read(nativeExperience, "m_View") as ScriptableObject;
            if (nativeExperienceView != null) CloneAsset(c, nativeExperienceView, key + "_ExperienceView");
            c.Core = (ScriptableObject)c.AssetMap[Read(source, "m_Attributes")];
            c.Movement = (ScriptableObject)c.AssetMap[Read(source, "m_Movement")];
            c.View = (ScriptableObject)c.AssetMap[Read(source, "m_View")];
            c.Hud = (ScriptableObject)c.AssetMap[Read(source, "m_HUD")];
            ScriptableObject nativeNav = Read(c.Core, "m_NavMesh") as ScriptableObject;
            if (nativeNav == null) throw new InvalidOperationException("Humanoid donor navigation is absent");
            c.Nav = CloneAsset(c, nativeNav, key + "_Nav");
            Array loadout = Read(c.Core, "m_WeaponLoadout") as Array;
            if (loadout == null || loadout.Length == 0) throw new InvalidOperationException("Humanoid donor basic weapon is absent");
            object first = loadout.GetValue(0);
            WeaponAttributesAsset nativeWeapon = Read(first, "m_Weapon") as WeaponAttributesAsset;
            if (nativeWeapon == null) throw new InvalidOperationException("Humanoid donor weapon has no asset");
            c.Weapon = (WeaponAttributesAsset)CloneAsset(c, nativeWeapon, key + "_BasicWeapon");
            ScriptableObject priorities = Read(c.Weapon, "m_TargetPriorization") as ScriptableObject;
            if (priorities != null) CloneAsset(c, priorities, key + "_WeaponPriorities");
            Array oneBinding = Array.CreateInstance(loadout.GetType().GetElementType(), 1); oneBinding.SetValue(first, 0);
            Write(c.Core, "m_WeaponLoadout", oneBinding);
            // Keep native basic command abilities; right-click movement is itself
            // Ability_Common_MoveAbility. Exclude donor-specific vehicle skills.
            Write(c.Custom, "m_Weapons", new WeaponAttributesAsset[] { c.Weapon });
            PrepareBasicCommands(c);
            PrepareAmmo(c);
            foreach (ScriptableObject owned in c.Owned) RemapAsset(c, owned);
            ConfigureCore(c); ConfigureMovement(c); ConfigureWeapon(c, squad);
            ConfigurePresentation(c, squad);
            if(c.Key==LaevatainKey)LaevatainFlameAssets.Prepare(Registry,c.Custom,c.Core,c.View,c.Hud,c.Ammo.name);
            else if(c.Key==GilbertaKey)GilbertaGravityAssets.Prepare(Registry,c.Custom,c.Core,c.View,c.Hud,c.Ammo.name);
            else if(c.Key==ArdeliaKey)ArdeliaHealingAssets.Prepare(Registry,c.Custom,c.Core,c.View,c.Hud,c.Ammo.name);
            else if (!squad) PerlicaOrbitalAssets.Prepare(Registry, c.Custom, c.Core, c.View, c.Hud, c.Ammo.name);
            if(RhodesOperatorRoster.Contains(c.Key))PrepareOperatorReturn(c);
            RhodesUnitVoiceAliases.ConfigureUnitAudio(c.Custom,c.Key);
            // Sub-assets rebuild first; core loadout is rebuilt by container.
            foreach (ScriptableObject asset in c.Owned)
                if (!object.ReferenceEquals(asset, c.Custom) && !object.ReferenceEquals(asset, c.Core)) CallIfPresent(asset, "OnEnable");
            Write(c.Custom, "mDataCache", null); CallRequired(c.Custom, "OnEnable");
            VerifyPrepared(c);
            Runtime.Log("HUMANOID_UNIT_PREPARED key=" + key + "; HP=" + c.Health + "; armour=" + c.Armour +
                "; footprint=" + c.Width + "x" + c.Length + "; maxMetersPerSecond=" + c.Speed +
                "; range=" + c.Ranges.Weapon + "; sensor=" + c.Ranges.Sensor + "; contact=" + c.Ranges.Contact +
                "; nativeDrive=Tracked; driveIsInfantrySimulation=False; drift=0; jTurn=False; visualMembers=" + (squad ? GuardMemberCount : 1) +
                "; weaponDisabledOnSpawn=False; nativeSimpleHudSymbol=True; globalAtlasUnchanged=True");
        }

        static void PrepareBasicCommands(Context c)
        {
            Array sourceAbilities = Read(c.Source, "m_Abilities") as Array;
            bool hasMove = false, hasVeterancy = false;
            List<string> names = new List<string>();
            if (sourceAbilities != null) foreach (ScriptableObject source in sourceAbilities)
            {
                if (source == null) continue;
                AbilityAttributes attributes = Read(source, "m_Attributes") as AbilityAttributes;
                if (attributes == null || string.IsNullOrEmpty(attributes.Name) || attributes.Name != source.name)
                    throw new InvalidOperationException("Humanoid donor ability identity is incomplete");
                if (!attributes.Name.StartsWith("Ability_Common_", StringComparison.Ordinal) && attributes.Name != "Ability_Veterancy") continue;
                // Native UI and global ability lookup use these canonical IDs.
                // SO/data are private copies; do not rename or globally register them.
                ScriptableObject clone = CloneAsset(c, source, attributes.Name);
                c.BasicAbilities.Add(clone); names.Add(attributes.Name);
                if (attributes.Name == "Ability_Common_MoveAbility") hasMove = true;
                if (attributes.Name == "Ability_Veterancy") hasVeterancy = true;
            }
            if (!hasMove) throw new InvalidOperationException("Humanoid native basic move ability is absent");
            if (!hasVeterancy) throw new InvalidOperationException("Humanoid native veterancy display ability is absent");
            Array abilities = Array.CreateInstance(sourceAbilities.GetType().GetElementType(), c.BasicAbilities.Count);
            for (int i = 0; i < c.BasicAbilities.Count; i++) abilities.SetValue(c.BasicAbilities[i], i);
            Write(c.Custom, "m_Abilities", abilities);
            Runtime.Log("HUMANOID_BASIC_COMMANDS_PREPARED key=" + c.Key + "; canonicalAbilities=" + string.Join(",", names.ToArray()) +
                "; independentData=True; vehicleSpecificSkillsExcluded=True; nativeRangeTestWeapon=True");
        }
        static void PrepareAmmo(Context c)
        {
            ScriptableObject sourceStorage = Read(c.Source, "m_Storage") as ScriptableObject;
            c.Storage = sourceStorage == null ? RhodesAssetClone59.CreateEmpty(NativeType("StorageAttributesAsset")) :
                (ScriptableObject)c.AssetMap[sourceStorage];
            if (sourceStorage == null)
            {
                Own(c, c.Storage, c.Key + "_Storage");
                Write(c.Storage, "m_Attributes", Activator.CreateInstance(NativeType("StorageAttributesData"), true));
            }
            Write(c.Custom, "m_Storage", c.Storage);
            c.Ammo = RhodesAssetClone59.CreateEmpty<InventoryAttributesAsset>(); Own(c, c.Ammo, c.Key + "_Ammo");
            object data = new InventoryAttributesData();
            Write(data, "m_InventoryID", c.Ammo.name); Write(data, "m_IsUnlimited", true);
            Write(data, "m_StartingAmount", 9999); Write(data, "m_MaxAmount", 9999);
            Write(c.Ammo, "m_Attributes", data);
            object binding = Activator.CreateInstance(NativeType("InventoryAssetBinding"),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new object[] { c.Ammo.name, c.Ammo }, CultureInfo.InvariantCulture);
            Array bindings = Array.CreateInstance(NativeType("InventoryAssetBinding"), 1); bindings.SetValue(binding, 0);
            Write(c.Storage, "m_InventoryLoadout", bindings); Write(Read(c.Storage, "m_Attributes"), "m_LinkToPlayerBank", false);
        }

        static void ConfigureCore(Context c)
        {
            object core = Read(c.Core, "m_Attributes");
            Write(core, "m_MaxHealth", c.Health); Write(core, "m_Armour", c.Armour);
            Write(core, "m_Class", UnitClass.Ground | UnitClass.Small);
            Write(core, "m_WorldHeightOffset", 0f); Write(core, "m_Selectable", true); Write(core, "m_Controllable", true);
            Write(core, "m_StartsInHangar", RhodesOperatorRoster.Contains(c.Key));
            if(RhodesOperatorRoster.Contains(c.Key)){Write(core,"m_Resource1Cost",300);Write(core,"m_Resource2Cost",50);Write(core,"m_ProductionTime",10f);Write(core,"m_PopCapCost",1);}
            Write(core, "m_SensorRadius", (float)c.Ranges.Sensor); Write(core, "m_ContactRadius", (float)c.Ranges.Contact);
            Write(core, "m_AggroRange", c.Ranges.Aggro); Write(core, "m_AlertRange", c.Ranges.Alert); Write(core, "m_LeashRange", c.Ranges.Leash);
            // Existing native tolerance and BlockedBy bits remain unchanged.
            Write(Read(c.Nav, "m_NavMeshConfig"), "m_DistanceFromObstacles", c.Length * .5f + .15f);
        }

        static void ConfigureMovement(Context c)
        {
            object movement = Read(c.Movement, "m_Simulation"), dynamics = Read(movement, "m_Dynamics");
            object maneuvers = Read(movement, "m_Maneuvers"), avoidance = Read(movement, "m_Avoidance");
            // DoK has no infantry drive enum. This small native ground surrogate
            // avoids car reverse/J-turn/drift and presents authored human motion.
            Write(movement, "m_DriveType", UnitDriveType.Tracked);
            // All movement data are already recursively copied. Keep this
            // private combat datum inside the shortened native weapon range.
            object combat = Read(movement, "m_Combat");
            Write(combat, "m_ActiveCombatApproachMode", Enum.ToObject(Read(combat, "m_ActiveCombatApproachMode").GetType(), 1));
            Write(combat, "m_PassiveCombatApproachMode", Enum.ToObject(Read(combat, "m_PassiveCombatApproachMode").GetType(), 0));
            Write(combat, "m_MinDesiredCombatRange", 0f);
            Write(combat, "m_MaxDesiredCombatRange", c.Ranges.Weapon * .85f);
            Write(dynamics, "m_Width", c.Width); Write(dynamics, "m_Length", c.Length);
            Write(dynamics, "m_MaxSpeed", c.Speed); Write(dynamics, "m_MinCruiseSpeed", c.Speed * .25f);
            Write(dynamics, "m_ReverseFactor", .5f); Write(dynamics, "m_AccelerationTime", .35f);
            Write(dynamics, "m_BrakingTime", .2f); Write(dynamics, "m_MaxSpeedTurnRadius", .2f);
            Write(dynamics, "m_MaxEaseIntoTurnTime", .1f); Write(dynamics, "m_PermanentlyImmobile", false);
            foreach (string field in new string[] { "m_AccelerationTimeRandomVariancePercentage", "m_MaxSpeedTurnRadiusRandomVariancePercentage",
                "m_DriftType", "m_DriftTypeRandomVariancePercentage", "m_DriftOvershootFactor", "m_DriftOvershootFactorRandomVariancePercentage",
                "m_FishTailControlRecover", "m_MinDriftSlipSpeedRandomVariancePercentage", "m_DeathDriftTime" }) Write(dynamics, field, 0f);
            Write(dynamics, "m_ReverseDriftMultiplier", 0f); Write(dynamics, "m_FishTailingTimeIntervals", 1f);
            Write(dynamics, "m_MinDriftSlipSpeed", 100f); Write(dynamics, "m_MaxDriftRecoverTime", .1f);
            foreach (string field in new string[] { "m_CruiseSpeedVariations", "m_CruiseDirectionVariations" })
            { Array old = Read(dynamics, field) as Array; if (old != null) Write(dynamics, field, Array.CreateInstance(old.GetType().GetElementType(), 0)); }
            Write(maneuvers, "m_ObstacleAvoidanceDistance", c.Length * .5f + .25f);
            Write(maneuvers, "m_FormationSpacingDistance", c.Length * .5f + .5f);
            Write(maneuvers, "m_FormationSectionGapAhead", c.Length * .5f + .5f);
            Write(maneuvers, "m_ArrivalDistance", .3f); Write(avoidance, "m_AvoidanceDistance", c.Length * .5f + .25f);
            foreach (string field in new string[] { "m_FastDirectTurn", "m_FastAwayTurn", "m_SlowDirectTurn", "m_SlowAwayTurn" })
                Write(maneuvers, field, TurnStyle.Controlled);
            Write(maneuvers, "m_MustReachMinSpeedBeforeTurn", false);
            Write(Read(maneuvers, "m_ThreePointTurn"), "m_MustReachMinSpeedBeforeTurn", false);
            Write(Read(maneuvers, "m_JTurn"), "m_TurnStyle", TurnStyle.None);
            Write(maneuvers, "m_DriftRecoverOptions", new DriftRecoverStyle[] { DriftRecoverStyle.Neutral });
        }

        static void ConfigureWeapon(Context c, bool squad)
        {
            object binding = ((Array)Read(c.Core, "m_WeaponLoadout")).GetValue(0);
            Write(binding, "m_AmmoID", c.Ammo.name); Write(binding, "m_ShowAmmoOnHUD", false);
            // Native simulation owns these manual range-test shots. Perlica's
            // hand-origin direct hit is a prototype, not a recovered source spell.
            Write(binding, "m_DisabledOnSpawn", false);
            object weapon = Read(c.Weapon, "m_Simulation");
            Write(weapon, "m_BaseDamagePerRound", c.Key==LaevatainKey?135:c.Key==GilbertaKey?72:c.Key==ArdeliaKey?66:squad ? 36 : 66);
            if(RhodesOperatorRoster.Contains(c.Key))Write(weapon,"m_ExcludeFromAutoTargetAcquisition",true);
            Write(weapon, "m_RateOfFire", 1); Write(weapon, "m_NumberOfBursts", 1);
            Write(weapon, "m_DamagePacketsPerShot", 1); Write(weapon, "m_ReloadTime", c.Key==LaevatainKey?LaevatainBasicReloadMilliseconds:squad ? 500 : PerlicaBasicReloadMilliseconds);
            // Perlica's source attack1 launches at frame 8/30. A 250 ms native
            // tick-aligned windup is paired with that audited visual marker.
            Write(weapon, "m_WindUpTime", c.Key==LaevatainKey?LaevatainBasicWindupMilliseconds:c.Key==GilbertaKey?GilbertaBasicWindupMilliseconds:NativeBasicWindupMilliseconds); Write(weapon, "m_WindDownTime", 0); Write(weapon, "m_CooldownTime", 0);
            Runtime.Log("HUMANOID_BASIC_CADENCE key="+c.Key+"; reloadMilliseconds="+(c.Key==LaevatainKey?LaevatainBasicReloadMilliseconds:squad?500:PerlicaBasicReloadMilliseconds)+"; windupMilliseconds="+(c.Key==LaevatainKey?LaevatainBasicWindupMilliseconds:c.Key==GilbertaKey?GilbertaBasicWindupMilliseconds:NativeBasicWindupMilliseconds)+"; nativeRoundsPerShot=1; cadenceOwnedBySimulation=True");
            Write(weapon, "m_BurstPeriodMinTime", 1); Write(weapon, "m_BurstPeriodMaxTime", 1);
            Write(weapon, "m_ExcludeFromAutoFire", false);
            ClearArray(weapon, "m_Modifiers");
            object weaponView = Read(c.Weapon, "m_View");
            ClearArray(weaponView, "WeaponFX");
            Write(weapon, "m_FiringRecoil", 0f);
            Write(weapon, "m_ProjectileEntityTypeToSpawn", string.Empty);
            Write(weapon, "m_TracerSpeed", 300f);
            Array ranges = Read(weapon, "m_Ranges") as Array;
            if (ranges == null || ranges.Length == 0) throw new InvalidOperationException("Humanoid basic weapon has no range bands");
            for (int i = 0; i < ranges.Length; i++)
            { object band = ranges.GetValue(i); Write(band, "m_MinDistance", 0); Write(band, "m_Distance", c.Ranges.Weapon * (i + 1) / ranges.Length); Write(band, "m_Accuracy", 75f); }
        }

        static void ConfigurePresentation(Context c, bool squad)
        {
            object hud = Read(c.Hud, "m_Attributes");
            if(RhodesOperatorRoster.Contains(c.Key))OperatorPortraitIcons.Configure(hud,Read(Read(c.Source,"m_HUD"),"m_Attributes"),c.Key);
            else if(c.Key==PerlicaKey)OperatorPortraitIcons.ConfigureEnglishName(hud,"Perlica");
            Write(hud, "mUnitLocalizedNameStringID", c.Key==LaevatainKey?"莱万汀":c.Key==GilbertaKey?"洁尔佩塔":c.Key==ArdeliaKey?"艾尔黛拉":squad ? "武装卫兵小队" : "佩丽卡");
            Write(hud, "mUnitLocalizedShortDescriptionStringID", c.Key==LaevatainKey?"隐蔽突击干员，以烈焰爆发打击敌军":c.Key==GilbertaKey?"隐蔽控场干员，以引力场聚敌并施加电磁压制":c.Key==ArdeliaKey?"隐蔽医疗干员，以治疗光环支援周围友军":squad ? "武装步兵编队，停步后射击" : "火力支援干员，可引导轨道打击");
            Write(hud, "mUnitLocalizedLongDescriptionStringID", c.Key==ArdeliaKey?"由武装倾转旋翼机编入和运送的医疗干员。丰饶之境持续治疗400米内的自身和友军，可在机上施放；同类治疗不叠加。同一位干员只能同时编入一名。":c.Key==GilbertaKey?"由武装倾转旋翼机编入和运送的控场干员。引力场持续聚拢并压制范围内最多12个敌方单位，无法被牵引的目标仍受电磁压制。可返回所属旋翼机整备；同一位干员只能同时编入一名。":c.Key==LaevatainKey?"由武装倾转旋翼机编入和运送的突击干员。普通攻击需停步，烈焰爆发可迅速打击目标区域。可返回所属旋翼机整备；同一位干员只能同时编入一名。":squad ?
                "六人武装步兵小队，停步后使用步枪射击。小队随受损逐步减员，治疗不会补回阵亡成员。" :
                "独立行动的火力支援干员。停步后进行普通攻击，并可标定目标区域，引导小规模轨道打击。");
            // The named operator uses an isolated native-atlas portrait entry.
            UnitViewAttributes view = (UnitViewAttributes)Read(c.View, "Data");
            UnitViewAttributes nativeView = (UnitViewAttributes)Read(Read(c.Source, "m_View"), "Data");
            c.NativePrefab = nativeView.ModelPrefab;
            if (c.NativePrefab == null) throw new InvalidOperationException("Humanoid native wrapper model missing");
            object lod = view.LODTunings;
            if (lod == null || object.ReferenceEquals(lod, nativeView.LODTunings)) throw new InvalidOperationException("Humanoid LOD aliases native data");
            foreach (string field in new string[] { "SuspensionActiveLODs", "WheelActuationActiveLODs", "BodyMotionActiveLODs" })
            { object mask = Read(lod, field); Write(lod, field, Enum.ToObject(mask.GetType(), 0)); }
            view.NLIPSScale = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(100000f, 1f));
            view.UnitEffectDefs = new BBI.Unity.Core.World.EffectDefinition[0];
            view.WheelFXDefinitions = new UnitViewAttributes.WheelAttributes[0];
            view.TreadDefinitions = new UnitViewAttributes.TreadAttributes[0];
            view.ModelPrefab = HumanoidModel.CreateModel(c.NativePrefab, c.Key==ArdeliaKey?"ardelia_v01":c.Key==LaevatainKey?"humanoid_trial/laevatain":c.Key==GilbertaKey?"humanoid_trial/gilberta":squad ? "humanoid_trial/guard" : "humanoid_trial/perlica", squad, squad ? GuardMemberCount : 1);
            if (view.ModelPrefab == null || object.ReferenceEquals(view.ModelPrefab, c.NativePrefab)) throw new InvalidOperationException("Humanoid model must be an independent wrapper clone");
            foreach (UnitViewAttributes.TurretAttributes turret in view.TurretDefinitions)
            {
                turret.CenterEmissionBone = HumanoidModel.NativeEmissionAlias;
                turret.FXEmissionBones = new string[] { HumanoidModel.NativeEmissionAlias };
                turret.SimTurretOrigin = HumanoidModel.NativeSimOriginAlias;
            }
            // Remove copied car wreck/explosion resources from our death data.
            object deathAsset = Read(c.Custom, "m_Death");
            if (deathAsset != null)
            {
                object death = Read(deathAsset, "m_Attributes"), destruction = Read(death, "DeathAttributes");
                if (destruction != null)
                    foreach (string field in new string[] { "CarcassPrefab", "InitialBurstPrefab", "DeathPingPrefab" }) Write(destruction, field, null);
                object ground = Read(death, "GroundUnitDestruction");
                if (ground != null) { Write(ground, "TripOnDeath", false); Write(ground, "MapWheelBones", false); Write(ground, "SteeringWheels", new string[0]); }
            }
        }

        static EntityTypeAttributes BuildDefinition(ScriptableObject asset)
        {
            EntityTypeAttributes definition = new EntityTypeAttributes(asset.name);
            // AssetContainer implements IEnumerable. MultiAssetBase and
            // AssetBase instead expose multiple or single datums through Data.
            object data = null;
            IEnumerable many = asset as IEnumerable;
            if (many == null) { data = Read(asset, "Data"); many = data as IEnumerable; }
            if (many != null)
            {
                foreach (object value in many)
                    if (value != null && !definition.Add(value)) throw new InvalidOperationException("Duplicate humanoid attribute type: " + value.GetType().FullName);
            }
            else if (data == null || !definition.Add(data))
                throw new InvalidOperationException("Humanoid asset has no registry datum: " + asset.name);
            return definition;
        }

        // Can be called by integration after registration, or on a repeated hook.
        public static void VerifyRegistered()
        {
            VerifyContextCatalogue();
            foreach (Context c in Contexts)
            {
                VerifyPrepared(c);
                VerifyRegistryContext(c);
            }
            VerifyContextIsolation();
            if(!Runtime.SkirmishMode)PerlicaOrbitalAssets.VerifyRegistered();
            if(Runtime.RhodesCarrier){LaevatainFlameAssets.VerifyRegistered();GilbertaGravityAssets.VerifyRegistered();ArdeliaHealingAssets.VerifyRegistered();}
        }
        static void VerifyContextCatalogue()
        {
            string[] expected=Keys;
            if(Registry==null||Contexts.Count!=expected.Length)throw new InvalidOperationException("Humanoid registry context absent; expected="+expected.Length+"; actual="+Contexts.Count);
            HashSet<string> remaining=new HashSet<string>(expected,StringComparer.Ordinal);
            foreach(Context c in Contexts)
                if(c==null||string.IsNullOrEmpty(c.Key)||!remaining.Remove(c.Key))throw new InvalidOperationException("Humanoid registry has an unexpected or duplicate unit key: "+(c==null?"<null>":c.Key));
            if(remaining.Count!=0)throw new InvalidOperationException("Humanoid registry is missing expected unit keys");
        }
        static void VerifyRegistryContext(Context c)
        {
            EntityTypeAttributes registered = Registry.GetEntityType(c.Key);
            if (registered == null || !object.ReferenceEquals(registered.Get<UnitAttributes>(), Read(c.Core, "m_Attributes")) ||
                !object.ReferenceEquals(registered.Get<UnitMovementAttributes>(), Read(c.Movement, "m_Simulation")))
                throw new InvalidOperationException("Humanoid registered core/movement differs: " + c.Key);
            foreach (ScriptableObject ability in c.BasicAbilities)
                if (!object.ReferenceEquals(registered.Get<AbilityAttributes>(ability.name), Read(ability, "m_Attributes")))
                    throw new InvalidOperationException("Humanoid basic command registry differs: " + c.Key + "/" + ability.name);
            if(c.OperatorReturnAbility!=null)VerifyOperatorReturn(c);
            EntityTypeAttributes weapon = Registry.GetEntityType(c.Weapon.name), ammo = Registry.GetEntityType(c.Ammo.name);
            if (weapon == null || ammo == null || !object.ReferenceEquals(weapon.Get<WeaponAttributes>(), Read(c.Weapon, "m_Simulation")) ||
                !object.ReferenceEquals(ammo.Get<InventoryAttributes>(), Read(c.Ammo, "m_Attributes")))
                throw new InvalidOperationException("Humanoid weapon/ammo registry differs: " + c.Key);
        }
        static void VerifyContextIsolation()
        {
            // Cross-unit independence, not merely independence from the donor.
            for(int ai=0;ai<Contexts.Count;ai++)for(int bi=ai+1;bi<Contexts.Count;bi++){Context a=Contexts[ai],b=Contexts[bi];
                foreach(ScriptableObject left in a.Owned)foreach(ScriptableObject right in b.Owned)
                    if(object.ReferenceEquals(left,right))throw new InvalidOperationException("Humanoid units share an owned asset");
                AssertNoSharedMutable(a.Owned,b.Owned);}
        }

        static void VerifyPrepared(Context c)
        {
            foreach (SourceRecord source in c.Sources)
                if (Fingerprint(source.Asset) != source.Fingerprint || !object.ReferenceEquals(Registry.GetEntityType(source.Asset.name), source.Definition))
                    throw new InvalidOperationException("Native humanoid source changed: " + source.Asset.name);
            List<ScriptableObject> sources = new List<ScriptableObject>(); foreach (SourceRecord source in c.Sources) sources.Add(source.Asset);
            AssertNoSharedMutable(sources, c.Owned);
            ScriptableObject nativeExperience = Read(c.Source, "m_Experience") as ScriptableObject;
            if (nativeExperience != null)
            {
                ScriptableObject nativeExperienceView = Read(nativeExperience, "m_View") as ScriptableObject;
                if (nativeExperienceView != null)
                {
                    object expected;
                    object actual = Read(Read(c.Custom, "m_Experience"), "m_View");
                    if (!c.AssetMap.TryGetValue(nativeExperienceView, out expected) || !object.ReferenceEquals(actual, expected) ||
                        object.ReferenceEquals(actual, nativeExperienceView) ||
                        object.ReferenceEquals(Read(actual, "Data"), Read(nativeExperienceView, "Data")))
                        throw new InvalidOperationException("Humanoid experience view aliases native data: " + c.Key);
                }
            }
            UnitAttributes core = (UnitAttributes)Read(c.Core, "m_Attributes");
            UnitMovementAttributes movement = (UnitMovementAttributes)Read(c.Movement, "m_Simulation");
            if (core.SensorRadius != Fixed64.FromInt(c.Ranges.Sensor) || core.ContactRadius != Fixed64.FromInt(c.Ranges.Contact) ||
                core.AggroRange != Fixed64.FromInt(c.Ranges.Aggro) || core.AlertRange != Fixed64.FromInt(c.Ranges.Alert) || core.LeashRange != Fixed64.FromInt(c.Ranges.Leash))
                throw new InvalidOperationException("Humanoid sight/contact/pursuit readback differs: " + c.Key);
            WeaponAttributes configuredWeapon = (WeaponAttributes)Read(c.Weapon, "m_Simulation");
            if((int)Read(configuredWeapon,"m_ReloadTime")!=(c.Key==LaevatainKey?LaevatainBasicReloadMilliseconds:c.Key==PerlicaKey||c.Key==GilbertaKey||c.Key==ArdeliaKey?PerlicaBasicReloadMilliseconds:500)||
                (int)Read(configuredWeapon,"m_WindUpTime")!=(c.Key==LaevatainKey?LaevatainBasicWindupMilliseconds:c.Key==GilbertaKey?GilbertaBasicWindupMilliseconds:NativeBasicWindupMilliseconds)||
                (int)Read(configuredWeapon,"m_DamagePacketsPerShot")!=1)
                throw new InvalidOperationException("Humanoid native basic cadence readback differs: "+c.Key);
            if (configuredWeapon.Ranges == null || configuredWeapon.Ranges.Length == 0) throw new InvalidOperationException("Humanoid weapon ranges missing");
            for (int band = 0; band < configuredWeapon.Ranges.Length; band++)
                if (configuredWeapon.Ranges[band].Distance != Fixed64.FromInt(c.Ranges.Weapon * (band + 1) / configuredWeapon.Ranges.Length) ||
                    (int)Read(((Array)Read(configuredWeapon, "m_Ranges")).GetValue(band), "m_MinDistance") != 0)
                    throw new InvalidOperationException("Humanoid native range band differs: " + c.Key);
            Array modifiers = Read(configuredWeapon, "m_Modifiers") as Array;
            Array effects = Read(Read(c.Weapon, "m_View"), "WeaponFX") as Array;
            if (modifiers == null || modifiers.Length != 0 || effects == null || effects.Length != 0 ||
                (float)Read(configuredWeapon, "m_FiringRecoil") != 0f ||
                (string)Read(configuredWeapon, "m_ProjectileEntityTypeToSpawn") != string.Empty ||
                (float)Read(configuredWeapon, "m_TracerSpeed") <= 0f)
                throw new InvalidOperationException("Humanoid native direct-hit prototype contract differs: " + c.Key);
            object combat = Read(movement, "m_Combat");
            if (Convert.ToInt32(Read(combat, "m_ActiveCombatApproachMode")) != 1 ||
                Convert.ToInt32(Read(combat, "m_PassiveCombatApproachMode")) != 0 ||
                (float)Read(combat, "m_MinDesiredCombatRange") != 0f ||
                Math.Abs((float)Read(combat, "m_MaxDesiredCombatRange") - c.Ranges.Weapon * .85f) > .0001f)
                throw new InvalidOperationException("Humanoid desired combat range differs: " + c.Key);
            if (core.MaxHealth != c.Health || core.Class != (UnitClass.Ground | UnitClass.Small) || core.WeaponLoadout == null || core.WeaponLoadout.Length != (c.Key == GuardSquadKey ? 1 : 2) ||
                core.WeaponLoadout[0].DisabledOnSpawn || !object.ReferenceEquals(core.WeaponLoadout[0].Weapon, Read(c.Weapon, "m_Simulation")) || core.WeaponLoadout[0].AmmoID != c.Ammo.name)
                throw new InvalidOperationException("Humanoid independent core/loadout failed readback: " + c.Key);
            if (movement.DriveType != UnitDriveType.Tracked || movement.Dynamics.Width.RawValue != Fixed64.FromConstFloat(c.Width).RawValue ||
                movement.Dynamics.Length.RawValue != Fixed64.FromConstFloat(c.Length).RawValue || movement.Dynamics.MaxSpeed.RawValue != Fixed64.FromConstFloat(c.Speed).RawValue)
                throw new InvalidOperationException("Humanoid movement failed readback: " + c.Key);
            UnitViewAttributes nativeView = (UnitViewAttributes)Read(Read(c.Source, "m_View"), "Data");
            if (nativeView.ModelPrefab != c.NativePrefab) throw new InvalidOperationException("Native humanoid wrapper prefab changed");
            UnitViewAttributes preparedView = (UnitViewAttributes)Read(c.View, "Data");
            if (preparedView.ModelPrefab == null || preparedView.TurretDefinitions == null || preparedView.TurretDefinitions.Length == 0)
                throw new InvalidOperationException("Humanoid prepared model / turret definition missing: " + c.Key);
            foreach (UnitViewAttributes.TurretAttributes turret in preparedView.TurretDefinitions)
                if (turret.CenterEmissionBone != HumanoidModel.NativeEmissionAlias ||
                    turret.SimTurretOrigin != HumanoidModel.NativeSimOriginAlias ||
                    turret.FXEmissionBones == null || turret.FXEmissionBones.Length != 1 ||
                    turret.FXEmissionBones[0] != HumanoidModel.NativeEmissionAlias)
                    throw new InvalidOperationException("Humanoid native emitter references differ: " + c.Key);
            int emissionAliases = 0, originAliases = 0;
            foreach (Transform node in preparedView.ModelPrefab.GetComponentsInChildren<Transform>(true))
            {
                if (node.name == HumanoidModel.NativeEmissionAlias) emissionAliases++;
                if (node.name == HumanoidModel.NativeSimOriginAlias) originAliases++;
            }
            if (emissionAliases != 1 || originAliases != 1)
                throw new InvalidOperationException("Humanoid native emitter aliases must be unique: " + c.Key);
            object nativeNav = ((UnitAttributes)Read(Read(c.Source, "m_Attributes"), "m_Attributes")).NavMeshAttributes;
            if (object.ReferenceEquals(core.NavMeshAttributes, nativeNav)) throw new InvalidOperationException("Humanoid nav aliases native data");
        }

        static void ReleaseStale()
        {
            foreach (Context c in Contexts)
                foreach (UnitView view in Resources.FindObjectsOfTypeAll(typeof(UnitView)))
                    if (c.Core != null && view != null && view.gameObject.activeInHierarchy && object.ReferenceEquals(view.UnitAttrs, Read(c.Core, "m_Attributes")))
                        throw new InvalidOperationException("Cannot release humanoid assets still used by an active UnitView");
            foreach (Context c in Contexts)
                for (int i = c.Owned.Count - 1; i >= 0; i--) if (c.Owned[i] != null) UnityEngine.Object.DestroyImmediate(c.Owned[i]);
            Contexts.Clear();
            PerlicaOrbitalAssets.Release();
            LaevatainFlameAssets.Release();
            GilbertaGravityAssets.Release();
            ArdeliaHealingAssets.Release();
        }

        static ScriptableObject CloneAsset(Context c, ScriptableObject source, string name)
        {
            if (source == null) throw new InvalidOperationException("Required humanoid asset absent: " + name);
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
            { if (array.Rank != 1) throw new InvalidOperationException("Unexpected humanoid multidimensional array"); Array clone = (Array)array.Clone(); seen.Add(value, clone); for (int i = 0; i < array.Length; i++) clone.SetValue(CopyData(array.GetValue(i), seen), i); return clone; }
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
        { if (!Mutable(value) || !seen.Add(value)) return; if (sources.Contains(value)) throw new InvalidOperationException("Humanoid mutable data aliases another source: " + value.GetType().FullName); Array array = value as Array; if (array != null) { foreach (object item in array) Check(item, sources, seen); return; } foreach (FieldInfo field in Fields(value.GetType())) Check(field.GetValue(value), sources, seen); }
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
                if (!field.FieldType.IsArray) throw new InvalidDataException("Expected humanoid array: " + name);
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
