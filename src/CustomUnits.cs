// Independent map/unit experiment. Core, movement, weapon, HUD and view are
// custom-owned; the native game still owns commands, simulation and damage.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BBI.Core;
using BBI.Core.ComponentModel;
using BBI.Core.Data;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Simulation;
using BBI.Unity.Core.World;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.UI;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Runtime-copy-safe references. Geometry and pivots are already visual-scale
    // metres; axes are parent-local. The driver changes presentation only.
    public sealed class BvmRigBinding : MonoBehaviour
    {
        public const int Hull=0,Yaw=1,Pitch=2,Recoil=3,Wheel=4,Track=5,Anchor=6,Static=7;
        public Transform ModelRoot;
        public Transform[] Bones,PartTransforms;
        public string[] RigNames;
        public int[] RigKinds,RigSides;
        public Vector3[] BindLocalPositions,RigAxes;
        public Quaternion[] BindLocalRotations;
        public float[] WheelRadii,WheelSpinSigns;
        public Material[] TrackMaterials;
        public int[] TrackMaterialSides;
        public float[] TrackLoopLengths,TrackUVRepeats,TrackVSigns;
        public float TrackGauge,VisualScale;
        public Transform Muzzle;
        public int YawNode,PitchNode,RecoilNode;
        public Vector3 YawAxis,PitchAxis,RecoilAxis;
        public float RestElevation,PitchAngleSign,YawMin,YawMax,PitchMin,PitchMax,YawSpeed,PitchSpeed;
    }

    public static class CustomUnits
    {
        public const string UnitKey = "EF_BVM_Test_MP";
        public const string ControlKey = "C_Escort_MP";
        public const string TankIconSourceKey = "C_Railgun_MP";
        public static bool Ready { get; private set; }
        public static bool Spawned { get; private set; }
        public static bool ViewsVerified { get; private set; }
        public static int OriginalHP { get; private set; }
        public static int CustomHP { get; private set; }
        public static int ImportedVertices { get; private set; }
        public static int ImportedTriangles { get; private set; }
        public static int ImportedMeshes { get; private set; }
        public static bool NativeMoveGoalReady { get; private set; }
        public static Vector2r NativeTestStart { get; private set; }
        public static Vector2r NativeTestGoal { get; private set; }
        static readonly CommanderID Player = new CommanderID(1);
        static readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();
        static EntityTypeCollection RegisteredCollection;
        static readonly bool[] Requested = new bool[1];
        static readonly uint[] RequestFrame = new uint[1];
        static Entity[] Pair;
        static GameObject Templates;
        static bool SpawnLogged;
        static string ObjHash;
        static int RenderReports;
        static float NextRenderReport;
        static int SpawnMode; // 0=unset, 1=flat-map, 2=explicit units-only/native-map test

        public static string AssetDirectory
        {
            get
            {
                DirectoryInfo folder = new DirectoryInfo(Path.GetDirectoryName(typeof(CustomUnits).Assembly.Location));
                for (DirectoryInfo current = folder; current != null; current = current.Parent)
                    if (string.Equals(current.Name, "BepInEx", StringComparison.OrdinalIgnoreCase))
                        return Path.Combine(current.FullName, "assets/map_unit_lab");
                throw new InvalidOperationException("Map/unit lab assembly is outside BepInEx");
            }
        }

        // Called on Unity main thread immediately after the native registry is built.
        public static void Register()
        {
            EntityTypeCollection collection = (EntityTypeCollection)NativeRead.Get(typeof(ShipbreakersMain), "sEntityTypes");
            if (object.ReferenceEquals(collection, RegisteredCollection) && Ready) return;
            Ready = false; Spawned = false; ViewsVerified = false; SpawnLogged = false; Pair = null;
            ImportedVertices = ImportedTriangles = ImportedMeshes = 0;
            RenderReports = 0; NextRenderReport = 0f;
            NativeMoveGoalReady = false; NativeTestStart = default(Vector2r); NativeTestGoal = default(Vector2r); SpawnMode = 0;
            Requested[0] = false; RequestFrame[0] = 0;
            BvmTuning.ReleaseForRegistryReset(collection);
            ClearOwned();
            RegisteredCollection = collection;
            if (collection == null || collection.GetEntityType(ControlKey) == null)
            {
                Runtime.Log("Custom units: native LAV is not in this registry; lab registration skipped");
                return;
            }
            if (collection.GetEntityType(UnitKey) != null) throw new Exception("Lab unit key collision: " + UnitKey);
            UnitAttributes originalData = collection.GetEntityType(ControlKey).Get<UnitAttributes>();
            UnitAssetContainer source = null;
            foreach (UnitAssetContainer candidate in Resources.FindObjectsOfTypeAll(typeof(UnitAssetContainer)))
            {
                if (candidate == null || !string.Equals(candidate.name, ControlKey, StringComparison.OrdinalIgnoreCase)) continue;
                object attrs = NativeRead.Get(NativeRead.Get(candidate, "m_Attributes"), "m_Attributes");
                if (object.ReferenceEquals(attrs, originalData)) { source = candidate; break; }
            }
            if (source == null) throw new Exception("Current registered native LAV container not found");
            OriginalHP = originalData.MaxHealth;
            CustomHP = OriginalHP;
            object sourceViewAsset = NativeRead.Get(source, "m_View");
            UnitViewAttributes sourceView = (UnitViewAttributes)NativeRead.Get(sourceViewAsset, "Data");
            GameObject originalModel = sourceView.ModelPrefab;
            if (originalModel == null) throw new Exception("Native LAV model prefab missing");

            UnitAssetContainer custom = (UnitAssetContainer)CloneAsset(source, UnitKey, false);
            ScriptableObject attributes = CloneAsset((ScriptableObject)NativeRead.Get(source, "m_Attributes"), "EF_LabScout_Attributes", false);
            Set(NativeRead.Get(attributes, "m_Attributes"), "m_MaxHealth", CustomHP);
            Set(custom, "m_Attributes", attributes);
            ScriptableObject hud = CloneAsset((ScriptableObject)NativeRead.Get(source, "m_HUD"), "EF_LabScout_HUD", true);
            object hudData = NativeRead.Get(hud, "m_Attributes");
            Set(hudData, "mUnitLocalizedNameStringID", "T-80BVM");
            Set(hudData, "mUnitLocalizedShortDescriptionStringID", "主战坦克，适合正面交战");
            Set(hudData, "mUnitLocalizedLongDescriptionStringID", "以装甲和单发主炮对抗地面目标，需防范侧翼攻击。");
            BvmIdentity.ApplyHud((UnitHUDAttributes)hudData, (UnitHUDAttributes)NativeRead.Get(NativeRead.Get(source, "m_HUD"), "m_Attributes"));
            Set(custom, "m_HUD", hud);
            ScriptableObject view = CloneAsset((ScriptableObject)sourceViewAsset, "EF_LabScout_View", true);
            UnitViewAttributes viewData = (UnitViewAttributes)NativeRead.Get(view, "Data");
            ConfigureBvmView(viewData, sourceView);
            viewData.ModelPrefab = CreateBvmPrefab(originalModel);
            BvmIdentity.ApplyView(viewData,sourceView);
            BvmWreck.Apply(custom,source,viewData);
            Set(custom, "m_View", view);
            BvmTuning.Result tuning = BvmTuning.Apply(custom,source,collection);
            CustomHP=tuning.HitPoints;
            // Clear the copied flattened cache, then rebuild weapon hardpoint connections
            // against the independent view while preserving the original definitions.
            Set(custom, "mDataCache", null);
            NativeRead.Call(custom, "OnEnable");
            EntityTypeAttributes definition = new EntityTypeAttributes(UnitKey);
            foreach (object data in (IEnumerable)custom)
                if (data != null && !definition.Add(data)) throw new Exception("Duplicate custom attribute type: " + data.GetType().FullName);
            collection.Add(definition);
            BvmTuning.VerifyRegistered(custom,source,collection);
            // Original types remain unchanged; custom tuning owns its weapon and
            // ammo identities while preserving native command/binding contracts.
            UnitAttributes registered = collection.GetEntityType(UnitKey).Get<UnitAttributes>();
            if (registered == null || registered.MaxHealth != CustomHP) throw new Exception("Custom core registration failed");
            if (object.ReferenceEquals(registered, originalData)) throw new Exception("Custom core aliases native LAV");
            if (collection.GetEntityType(ControlKey).Get<UnitAttributes>().MaxHealth != OriginalHP || sourceView.ModelPrefab != originalModel)
                throw new Exception("Native LAV changed during independent registration");
            Ready = true;
            Runtime.Log("CUSTOM_UNIT_REGISTERED key=" + UnitKey + "; original=" + ControlKey + "; customHP=" + CustomHP +
                "; originalHP=" + OriginalHP + "; independentCore=True; originalUnchanged=True; weapons=" + registered.WeaponLoadout.Length + "; externalBVMBuffer=" + ObjHash);
            foreach (WeaponBinding weapon in registered.WeaponLoadout)
            {
                string ranges = "";
                if (weapon.Weapon != null && weapon.Weapon.Ranges != null)
                    foreach (RangeBasedWeaponAttributes range in weapon.Weapon.Ranges)
                        ranges += range.Range + ":" + range.MinDistance + ".." + range.Distance + " ";
                Runtime.Log("CUSTOM_NATIVE_WEAPON id=" + weapon.WeaponID + "; disabledOnSpawn=" + weapon.DisabledOnSpawn + "; ranges=" + ranges);
            }
        }

        // Presentation-only: borrow the native tracked railgun tank's five icon names.
        // Keep HUD prefab/layout, selection behavior, unit class and all LAV game logic.
        static void ApplyTankIconSettings(EntityTypeCollection collection, object targetHUD, object originalHUD)
        {
            UnitHUDAttributes tankHUD = null;
            EntityTypeAttributes tankType = collection.GetEntityType(TankIconSourceKey);
            if (tankType != null) tankHUD = tankType.Get<UnitHUDAttributes>();
            if (tankHUD == null)
            {
                foreach (UnitAssetContainer candidate in Resources.FindObjectsOfTypeAll(typeof(UnitAssetContainer)))
                    if (candidate != null && string.Equals(candidate.name, TankIconSourceKey, StringComparison.OrdinalIgnoreCase))
                    {
                        tankHUD = NativeRead.Get(NativeRead.Get(candidate, "m_HUD"), "m_Attributes") as UnitHUDAttributes;
                        if (tankHUD != null) break;
                    }
            }
            if (tankHUD == null) throw new Exception("Native tracked-tank HUD not loaded: " + TankIconSourceKey);
            object targetIcon = NativeRead.Get(targetHUD, "m_IconSettings");
            object originalIcon = NativeRead.Get(originalHUD, "m_IconSettings");
            object tankIcon = NativeRead.Get(tankHUD, "m_IconSettings");
            if (targetIcon == null || tankIcon == null || originalIcon == null ||
                object.ReferenceEquals(targetIcon, originalIcon) || object.ReferenceEquals(targetIcon, tankIcon))
                throw new Exception("BVM icon settings are not an independent clone");
            string[] fields = { "mUnitIconName", "mUnitSensorBlipName", "mUnitProductionIconName", "mUnitFleetPanelIconName", "m_UnitInfoPanelIconName" };
            string[] originals = new string[fields.Length];
            int changed = 0;
            List<string> applied = new List<string>();
            for (int i = 0; i < fields.Length; i++)
            {
                originals[i] = (string)NativeRead.Get(originalIcon, fields[i]);
                string value = (string)NativeRead.Get(tankIcon, fields[i]);
                if (!string.Equals((string)NativeRead.Get(targetIcon, fields[i]), value, StringComparison.Ordinal)) changed++;
                Set(targetIcon, fields[i], value);
                if (!string.Equals((string)NativeRead.Get(targetIcon, fields[i]), value, StringComparison.Ordinal))
                    throw new Exception("BVM tank icon assignment failed: " + fields[i]);
                applied.Add(fields[i] + "=" + value);
            }
            for (int i = 0; i < fields.Length; i++)
                if (!string.Equals((string)NativeRead.Get(originalIcon, fields[i]), originals[i], StringComparison.Ordinal))
                    throw new Exception("Native LAV HUD changed while assigning BVM tank icons");
            if (changed == 0) throw new Exception("Tracked tank icon names did not differ from native LAV; marker change not applied");
            Runtime.Log("BVM_TANK_ICONS_APPLIED source=" + TankIconSourceKey + "; changedFields=" + changed +
                "; independentHUD=True; nativeLAVHUDUnchanged=True; unitClassAndLogicUnchanged=True; " + string.Join("; ", applied.ToArray()));
        }

        // Sim thread only. The map driver has already prepared terrain/navigation.
        // The singleton array contains only the custom BVM; the donor stays registered.
        public static Entity[] EnsureSpawn(object sim)
        {
            if (!Ready || !CustomFlatMap.Ready) return null;
            SelectSpawnMode(1);
            return EnsureSpawnCore(sim, CustomFlatMap.Start);
        }

        // Explicit separate experiment: read native map validity only. Never calls
        // CustomMap.Configure/Prepare and never changes native terrain/navigation.
        public static Entity[] EnsureSpawnOnNativeMap(object sim, Vector2r origin)
        {
            if (!Ready) return null;
            SelectSpawnMode(2);
            if (!NativeMoveGoalReady) PrepareNativeMovementCorridor(sim, origin);
            return EnsureSpawnCore(sim, NativeTestStart);
        }

        // Lifecycle test adopts native StartingUnits. Missing/dead units are never
        // replaced by the ordinary playground's spawn fallback.
        public static Entity[] AdoptStartingBvm(object sim,Entity entity)
        {
            if(!Ready||!CustomFlatMap.Ready)return null;
            SelectSpawnMode(1);if(Spawned)return Pair;
            object found=null;
            foreach(object value in (IEnumerable)NativeRead.Get(NativeRead.Get(sim,"UnitManager"),"Units"))
                if(((Entity)NativeRead.Get(value,"Entity")).Equals(entity)){found=value;break;}
            if(found==null)return null;
            if((string)NativeRead.Get(found,"TypeName")!=UnitKey||(CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",entity)!=Player)
                throw new InvalidOperationException("Lifecycle starting-unit adoption did not identify a friendly BVM");
            BBI.Game.Simulation.UnitState state=(BBI.Game.Simulation.UnitState)NativeRead.Call(found,"GetState");
            if(state.UnitIsDead)throw new InvalidOperationException("Lifecycle starting BVM died before initial adoption");
            Pair=new Entity[]{entity};Spawned=SpawnLogged=true;
            Runtime.Log("CUSTOM_UNIT_ADOPTED entity="+entity+"; nativeStartingFleet=True; additionalBVMSpawnCommand=False; carrierRequired=False; missingUnitRespawn=False");
            return Pair;
        }
        static void SelectSpawnMode(int mode)
        {
            if (SpawnMode != 0 && SpawnMode != mode) throw new Exception("Cannot switch map/unit lab spawn mode inside an active registration");
            SpawnMode = mode;
        }

        static void PrepareNativeMovementCorridor(object sim, Vector2r origin)
        {
            object map = NativeRead.Get(sim, "Map");
            UnitClass unitClass = RegisteredCollection.GetEntityType(UnitKey).Get<UnitAttributes>().Class;
            int[,] directions = { {1,0}, {0,1}, {-1,0}, {0,-1}, {1,1}, {-1,1}, {-1,-1}, {1,-1} };
            int[] radii = { 120, 180, 240, 320 };
            foreach (int radius in radii)
                for (int d = 0; d < directions.GetLength(0); d++)
                {
                    Vector2r start = origin + new Vector2r(Fixed64.FromInt(directions[d,0] * radius), Fixed64.FromInt(directions[d,1] * radius));
                    if (!NativeFootprintValid(map, start, unitClass)) continue;
                    for (int gd = 0; gd < directions.GetLength(0); gd++)
                    {
                        bool diagonal = directions[gd,0] != 0 && directions[gd,1] != 0;
                        int length = diagonal ? 180 : 256; // 254.6m diagonal or 256m cardinal
                        Vector2r delta = new Vector2r(Fixed64.FromInt(directions[gd,0] * length), Fixed64.FromInt(directions[gd,1] * length));
                        bool valid = true;
                        for (int sample = 0; sample <= 16; sample++)
                            if (!NativeFootprintValid(map, start + delta * Fixed64.FromRational(sample,16), unitClass)) { valid = false; break; }
                        if (!valid) continue;
                        NativeTestStart = start; NativeTestGoal = start + delta; NativeMoveGoalReady = true;
                        Runtime.Log("NATIVE_UNITS_ONLY_CORRIDOR_READY start=" + NativeTestStart + "; goal=" + NativeTestGoal +
                            "; length=" + delta.Length + "; samples=17x5; mapModified=False; nativeMapValidityOnly=True; actualArrivalNotYetVerified=True");
                        return;
                    }
                }
            throw new Exception("No native-valid 200-300m units-only movement corridor near origin=" + origin);
        }

        static bool NativeFootprintValid(object map, Vector2r point, UnitClass unitClass)
        {
            int[,] offsets = { {0,0}, {8,0}, {-8,0}, {0,8}, {0,-8} };
            for (int i=0;i<offsets.GetLength(0);i++)
                if (!(bool)NativeRead.Call(map, "MapPositionValid", point + new Vector2r(Fixed64.FromInt(offsets[i,0]),Fixed64.FromInt(offsets[i,1])), unitClass)) return false;
            return true;
        }

        static Entity[] EnsureSpawnCore(object sim, Vector2r spawnStart)
        {
            if(Spawned)return Pair;
            uint frame=Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim,"GlobalFrameCount"),"FrameNumber"));
            object unit=FindOwned(sim,UnitKey,null);
            if(unit==null&&!Requested[0])
            {
                UnitClass unitClass=RegisteredCollection.GetEntityType(UnitKey).Get<UnitAttributes>().Class;
                if(!(bool)NativeRead.Call(NativeRead.Get(sim,"Map"),"MapPositionValid",spawnStart,unitClass))
                    throw new Exception("Lab BVM spawn is not native-valid: "+spawnStart);
                Type factory=typeof(Sim).Assembly.GetType("BBI.Game.Commands.SimCommandFactory",true);
                object command=NativeRead.Call(factory,"CreateSpawnUnitsCommand",Player,new string[]{UnitKey},new string[0],spawnStart,Orientation2.LocalForward);
                if(!(bool)NativeRead.Call(command,"RunCommand",sim,NativeRead.Get(sim,"InteractionProvider"),NativeRead.Get(sim,"GlobalFrameCount")))
                    throw new Exception("Native spawn rejected "+UnitKey);
                Requested[0]=true;RequestFrame[0]=frame;
                Runtime.Log("CUSTOM_UNIT_SPAWN_REQUEST key="+UnitKey+"; requested="+spawnStart+"; frame="+frame+"; controlLAVRequested=False; mode="+(SpawnMode==2?"UnitsOnlyNativeMap":"FlatMap"));
                unit=FindOwned(sim,UnitKey,null);
            }
            if(unit==null)
            {
                if(frame-RequestFrame[0]>80)throw new Exception("Native spawn did not create "+UnitKey);
                return null;
            }
            Pair=new Entity[]{(Entity)NativeRead.Get(unit,"Entity")};
            if(!SpawnLogged)
            {
                UnitMovementAttributes movement=(UnitMovementAttributes)NativeRead.Get(unit,"MovementAttributes");
                object state=NativeRead.Call(unit,"GetState");
                Vector2r position=(Vector2r)NativeRead.Get(NativeRead.Get(state,"StartOfFrameDynamicState"),"Position");
                Runtime.Log("CUSTOM_UNIT_SPAWNED key="+UnitKey+"; entity="+Pair[0]+"; position="+position+"; arrivalDistance="+movement.Maneuvers.ArrivalDistance+
                    "; owner="+NativeRead.Call(typeof(Sim),"GetEntityCommanderID",Pair[0])+"; customUnitCount=1; controlLAVSpawned=False; nativeDonorDefinitionRetained=True");
                SpawnLogged=true;
            }
            Spawned=true;return Pair;
        }
        static object FindOwned(object sim, string key, HashSet<Entity> excluded)
        {
            foreach (object unit in (IEnumerable)NativeRead.Get(NativeRead.Get(sim, "UnitManager"), "Units"))
                if ((string)NativeRead.Get(unit, "TypeName") == key && (CommanderID)NativeRead.Call(typeof(Sim), "GetEntityCommanderID", (Entity)NativeRead.Get(unit, "Entity")) == Player &&
                    (excluded == null || !excluded.Contains((Entity)NativeRead.Get(unit, "Entity")))) return unit;
            return null;
        }

        // Main thread: inspect native rendering registration and actual renderer state.
        // isVisible/property-block checks still are not an image/pixel acceptance test.
        public static void UpdateViews()
        {
            if (!Spawned || Pair == null || (ViewsVerified && (!Runtime.DetailedDiagnosticsEnabled || RenderReports >= 12))) return;
            foreach (UnitInterfaceController controller in Resources.FindObjectsOfTypeAll(typeof(UnitInterfaceController)))
            {
                if (controller == null || !controller.gameObject.activeInHierarchy) continue;
                Component customView = NativeRead.Call(controller, "GetUnitViewForEntity", Pair[0]) as Component;
                if (customView == null) continue;
                BvmRigDriver.Ensure((UnitView)customView);
                List<Renderer> imported = ImportedRenderers(customView);
                if (imported.Count == 0) continue;
                Renderer[] controlled = NativeRead.Get(customView,"mGeoRenderObjects") as Renderer[];
                int nativeControlled=0,active=0,enabled=0,visible=0,supported=0,nonzeroVisibility=0;
                float maxVisibility=0f,minNLIPS=float.MaxValue,maxNLIPS=float.MinValue;
                Bounds worldBounds=new Bounds();bool boundsReady=false;
                MaterialPropertyBlock block=new MaterialPropertyBlock();
                foreach(Renderer renderer in imported)
                {
                    if(controlled!=null)foreach(Renderer member in controlled)if(member==renderer){nativeControlled++;break;}
                    if(renderer.gameObject.activeInHierarchy)active++;
                    if(renderer.enabled)enabled++;
                    if(renderer.isVisible)visible++;
                    Material material=renderer.sharedMaterial;
                    if(material!=null&&material.shader!=null&&material.shader.isSupported)supported++;
                    renderer.GetPropertyBlock(block);
                    float v=block.GetFloat("_Visibility"),n=block.GetFloat("_NLIPSScale");
                    if(v>0.001f)nonzeroVisibility++;
                    maxVisibility=Math.Max(maxVisibility,v);minNLIPS=Math.Min(minNLIPS,n);maxNLIPS=Math.Max(maxNLIPS,n);
                    if(!boundsReady){worldBounds=renderer.bounds;boundsReady=true;}else worldBounds.Encapsulate(renderer.bounds);
                }
                bool stateReady=nativeControlled==imported.Count&&active>0&&enabled>0&&visible>0&&supported==imported.Count&&nonzeroVisibility>0;
                if(stateReady&&!ViewsVerified)
                {
                    ViewsVerified=true;
                    Runtime.Log("CUSTOM_UNIT_RENDER_STATE_READY importedSkinned="+imported.Count+"; nativeVisibilityBindings="+nativeControlled+
                        "; controlLAVSpawned=False"+"; active="+active+"; enabled="+enabled+"; visibleToAnyCamera="+visible+
                        "; visibilityPropertyNonzero="+nonzeroVisibility+"; actualPixelAcceptancePending=True; BVMArticulatedGeometry=True; originalLAVMeshesBlanked=True");
                }
                if(Time.realtimeSinceStartup>=NextRenderReport&&RenderReports<12)
                {
                    RaycastHit ground=new RaycastHit();Vector3 position=customView.transform.position;
                    int layer=LayerMask.NameToLayer("Terrain");
                    bool groundHit=layer>=0&&Physics.Raycast(new Vector3(position.x,position.y+2000f,position.z),Vector3.down,out ground,4000f,1<<layer);
                    float groundY=groundHit?ground.point.y:float.NaN;
                    Runtime.Log("BVM_RENDER_DIAGNOSTIC sample="+RenderReports+"; root="+position+"; rootScale="+customView.transform.lossyScale+
                        "; imported="+imported.Count+"; nativeControlled="+nativeControlled+"; active="+active+"; enabled="+enabled+"; visibleToAnyCamera="+visible+
                        "; shaderSupported="+supported+"; maxVisibility="+maxVisibility+"; NLIPS="+minNLIPS+".."+maxNLIPS+
                        "; worldBounds="+worldBounds+"; groundHit="+groundHit+"; groundY="+groundY+
                        "; geometryGroundAnchor=0; actualPixelAcceptancePending=True");
                    NextRenderReport=Time.realtimeSinceStartup+.5f;RenderReports++;
                }
                return;
            }
        }

        static List<Renderer> ImportedRenderers(Component view)
        {
            List<Renderer> result=new List<Renderer>();
            foreach(Renderer renderer in view.GetComponentsInChildren<Renderer>(true))
            {
                SkinnedMeshRenderer skinned=renderer as SkinnedMeshRenderer;
                MeshFilter filter=renderer.GetComponent<MeshFilter>();
                Mesh mesh=skinned!=null?skinned.sharedMesh:(filter==null?null:filter.sharedMesh);
                if(mesh!=null&&mesh.name.StartsWith("EF_BVM_Chunk",StringComparison.Ordinal))result.Add(renderer);
            }
            return result;
        }

        static void ConfigureBvmView(UnitViewAttributes target, UnitViewAttributes original)
        {
            // Only the custom view owns these mutable settings. Native LAV and
            // its weapons keep their original definitions and control scripts.
            object lod = Copy(original.LODTunings);
            if (lod == null || object.ReferenceEquals(lod, original.LODTunings))
                throw new Exception("BVM LOD tuning is not independent");
            foreach (string name in new[]{"SuspensionActiveLODs","WheelActuationActiveLODs","BodyMotionActiveLODs"})
            {
                object old = NativeRead.Get(lod,name);
                if (old == null || !old.GetType().IsEnum) throw new Exception("Unexpected native LOD mask: "+name);
                Set(lod,name,Enum.ToObject(old.GetType(),0));
            }
            Set(target,"LODTunings",lod);
            target.NLIPSScale = new AnimationCurve(new Keyframe(0f,1f),new Keyframe(100000f,1f));
            int oldEffects = original.UnitEffectDefs == null ? 0 : original.UnitEffectDefs.Length;
            int oldWheels = original.WheelFXDefinitions == null ? 0 : original.WheelFXDefinitions.Length;
            target.UnitEffectDefs = new EffectDefinition[0];
            target.WheelFXDefinitions = new UnitViewAttributes.WheelAttributes[0];
            // Native weapon FX resolves these names recursively below UnitView.
            // Retain native aiming bones/state so the presentation bridge can
            // read their actual yaw/pitch; only the emission anchors move.
            UnitViewAttributes.TurretAttributes[] turrets=(UnitViewAttributes.TurretAttributes[])Copy(original.TurretDefinitions);
            if(turrets==null||turrets.Length==0||object.ReferenceEquals(turrets,original.TurretDefinitions))
                throw new Exception("BVM emission definitions are not independent");
            for(int i=0;i<turrets.Length;i++)
            {
                UnitViewAttributes.TurretAttributes prior=original.TurretDefinitions[i],current=turrets[i];
                if(prior==null||current==null||object.ReferenceEquals(prior,current))
                    throw new Exception("BVM emission entry aliases native LAV");
                string oldCenter=prior.CenterEmissionBone;string[] oldEmission=prior.FXEmissionBones;
                current.CenterEmissionBone="EF_BVM_Muzzle";current.FXEmissionBones=new string[]{"EF_BVM_Muzzle"};
                if(prior.CenterEmissionBone!=oldCenter||!object.ReferenceEquals(prior.FXEmissionBones,oldEmission)||
                    current.BoneName!=prior.BoneName||current.PitchBoneName!=prior.PitchBoneName||current.SimTurretOrigin!=prior.SimTurretOrigin)
                    throw new Exception("BVM emission mapping altered native aiming data");
            }
            target.TurretDefinitions=turrets;
            Runtime.Log("BVM_NATIVE_EMISSION_MAPPED turrets="+turrets.Length+"; CenterEmissionBone=EF_BVM_Muzzle; FXEmissionBones=EF_BVM_Muzzle; nativeYawPitchBonesRetained=True; nativeDefinitionsUnchanged=True; weaponDamageAndLoadoutUnchanged=True");
            if (object.ReferenceEquals(target.LODTunings,original.LODTunings) ||
                (original.UnitEffectDefs == null ? 0 : original.UnitEffectDefs.Length) != oldEffects ||
                (original.WheelFXDefinitions == null ? 0 : original.WheelFXDefinitions.Length) != oldWheels)
                throw new Exception("Native LAV view changed while preparing BVM");
            Runtime.Log("BVM_PRESENTATION_SETTINGS_READY independentLOD=True; suspension=False; bodyMotion=False; wheelActuation=False; NLIPS=1; inheritedBodyFX=0; inheritedWheelFX=0; nativeBodyFX="+oldEffects+"; nativeWheelFX="+oldWheels+"; nativeWeaponsRetained=True");
        }

        sealed class BvmChunk
        {
            public int Part;
            public int Material;
            public Vector3[] Positions;
            public Vector3[] Normals;
            public Vector2[] UV;
            public int[] Indices;
        }
        sealed class BvmRigData
        {
            public Material[] Materials;
            public string[] Names;
            public int[] Parents,Kinds,Sides,TrackSides;
            public Vector3[] LocalPositions,Axes;
            public Quaternion[] Rotations;
            public float[] Radii,SpinSigns,TrackLoops,TrackRepeats,TrackVSigns;
            public int[] TrackBindings;
            public int Yaw,Pitch,Recoil,MuzzleParent;
            public Vector3 YawAxis,PitchAxis,RecoilAxis,MuzzleLocal;
            public float RestElevation,PitchAngleSign,YawMin,YawMax,PitchMin,PitchMax,YawSpeed,PitchSpeed,TrackGauge;
        }

        static Material BvmMaterial(BinaryReader reader,string root,Material template,Dictionary<string,Texture2D> cache)
        {
            string name=BvmText(reader),albedo=BvmText(reader),normal=BvmText(reader),ao=BvmText(reader);
            float cutoff=BvmFloat(reader);int kind=reader.ReadInt32();
            if(cutoff<0||cutoff>1||kind<0||kind>2)throw new InvalidDataException("Invalid BVM material record");
            Material material=new Material(template);material.name="EF_BVM_"+name;material.hideFlags=HideFlags.HideAndDontSave;
            material.SetTexture("_MainTex",BvmTexture(root,albedo,false,cache));
            if(material.HasProperty("_BumpMap")&&normal.Length>0)material.SetTexture("_BumpMap",BvmTexture(root,normal,true,cache));
            if(material.HasProperty("_OcclusionMap")&&ao.Length>0)material.SetTexture("_OcclusionMap",BvmTexture(root,ao,true,cache));
            if(material.HasProperty("_OverlayMask"))material.SetTexture("_OverlayMask",Solid(Color.clear));
            foreach(string property in new[]{"_Color","_BaseColor","_TrimColor"})if(material.HasProperty(property))material.SetColor(property,Color.white);
            if(material.HasProperty("_Glossiness"))material.SetFloat("_Glossiness",.24f);
            if(material.HasProperty("_Metallic"))material.SetFloat("_Metallic",.18f);
            if(material.HasProperty("_BumpScale"))material.SetFloat("_BumpScale",1f);
            if(kind==1)
            {
                foreach(string property in new[]{"_Cutoff","_AlphaCutoff","_AlphaTestThreshold"})if(material.HasProperty(property))material.SetFloat(property,cutoff);
                material.EnableKeyword("_ALPHATEST_ON");material.renderQueue=2450;
            }
            else if(kind==2)
            {
                if(material.HasProperty("_Mode"))material.SetFloat("_Mode",2f);
                if(material.HasProperty("_SrcBlend"))material.SetFloat("_SrcBlend",5f);
                if(material.HasProperty("_DstBlend"))material.SetFloat("_DstBlend",10f);
                if(material.HasProperty("_ZWrite"))material.SetFloat("_ZWrite",0f);
                material.EnableKeyword("_ALPHABLEND_ON");material.renderQueue=3000;
            }
            Owned.Add(material);
            Runtime.Log("BVM_MATERIAL name="+name+"; albedo="+albedo+"; normalAG="+normal+"; ao="+ao+"; kind="+kind+"; shader="+material.shader.name);
            return material;
        }

        static BvmRigData ReadBvmRig(string file,string textureRoot,Material template,Dictionary<string,Texture2D> cache)
        {
            BvmRigData rig=new BvmRigData();
            using(BinaryReader reader=new BinaryReader(File.OpenRead(file),System.Text.Encoding.UTF8))
            {
                if(System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4))!="BRG2"||reader.ReadInt32()!=2)throw new InvalidDataException("Unsupported BVM rig header");
                int sourceCount=BvmCount(reader,64),bindingCount=BvmCount(reader,64),nodeCount=BvmCount(reader,512),wheelCount=BvmCount(reader,256);
                if(sourceCount!=15||bindingCount!=17||nodeCount!=46||wheelCount!=26)throw new InvalidDataException("BVM rig count mismatch");
                Material[] source=new Material[sourceCount];for(int i=0;i<source.Length;i++)source[i]=BvmMaterial(reader,textureRoot,template,cache);
                rig.Materials=new Material[bindingCount];int[] bindingSides=new int[bindingCount];
                for(int i=0;i<bindingCount;i++)
                {
                    int id=reader.ReadInt32(),side=reader.ReadInt32();
                    if(id<0||id>=sourceCount||side<-1||side>1)throw new InvalidDataException("Invalid BVM material binding");
                    Material material=new Material(source[id]);material.name="EF_BVM_Binding"+i+"_"+source[id].name;material.hideFlags=HideFlags.HideAndDontSave;Owned.Add(material);
                    rig.Materials[i]=material;bindingSides[i]=side;
                }
                rig.Names=new string[nodeCount];rig.Parents=new int[nodeCount];rig.Kinds=new int[nodeCount];rig.Sides=new int[nodeCount];
                rig.LocalPositions=new Vector3[nodeCount];rig.Rotations=new Quaternion[nodeCount];rig.Axes=new Vector3[nodeCount];rig.Radii=new float[nodeCount];rig.SpinSigns=new float[nodeCount];
                for(int i=0;i<nodeCount;i++)
                {
                    rig.Names[i]=BvmText(reader);rig.Parents[i]=reader.ReadInt32();rig.Kinds[i]=reader.ReadInt32();
                    Vector3 pivot=BvmVector(reader);rig.LocalPositions[i]=BvmVector(reader);
                    Quaternion rotation=new Quaternion(BvmFloat(reader),BvmFloat(reader),BvmFloat(reader),BvmFloat(reader));
                    float norm=rotation.x*rotation.x+rotation.y*rotation.y+rotation.z*rotation.z+rotation.w*rotation.w;
                    if(rig.Parents[i]>=i||rig.Parents[i]<-1||rig.Kinds[i]<0||rig.Kinds[i]>7||Math.Abs(norm-1f)>.001f||pivot.sqrMagnitude>1000000f)
                        throw new InvalidDataException("Invalid BVM rig node");
                    rig.Rotations[i]=rotation;rig.SpinSigns[i]=1f;
                }
                bool[] wheels=new bool[nodeCount];
                float leftX=0f,rightX=0f;int leftCount=0,rightCount=0;
                for(int i=0;i<wheelCount;i++)
                {
                    int id=reader.ReadInt32();Vector3 axis=BvmVector(reader);float radius=BvmFloat(reader);int side=reader.ReadInt32();float spin=BvmFloat(reader);
                    if(id<0||id>=nodeCount||rig.Kinds[id]!=BvmRigBinding.Wheel||wheels[id]||axis.sqrMagnitude<.9f||radius<=0f||(side!=-1&&side!=1)||Math.Abs(Math.Abs(spin)-1f)>.001f)
                        throw new InvalidDataException("Invalid BVM wheel rig");
                    wheels[id]=true;rig.Axes[id]=axis.normalized;rig.Radii[id]=radius;rig.Sides[id]=side;rig.SpinSigns[id]=spin;
                    // Both track mesh nodes use a shared origin. Recover gauge
                    // from road-wheel centers in model-root REST coordinates.
                    if(rig.Names[id].StartsWith("bone_wheel_l_",StringComparison.Ordinal)||rig.Names[id].StartsWith("bone_wheel_r_",StringComparison.Ordinal))
                    {
                        Vector3 center=Vector3.zero;int parent=id;
                        while(parent>=0){center+=rig.LocalPositions[parent];parent=rig.Parents[parent];}
                        if(side<0){leftX+=center.x;leftCount++;}else{rightX+=center.x;rightCount++;}
                    }
                }
                // Road-wheel centers define the track contact gauge; track origins coincide.
                int tracks=BvmCount(reader,8);if(tracks!=2)throw new InvalidDataException("BVM needs two independent tracks");
                rig.TrackSides=new int[tracks];rig.TrackBindings=new int[tracks];rig.TrackLoops=new float[tracks];rig.TrackRepeats=new float[tracks];rig.TrackVSigns=new float[tracks];
                Vector3[] trackPivots=new Vector3[tracks];
                for(int i=0;i<tracks;i++)
                {
                    int side=reader.ReadInt32(),binding=reader.ReadInt32(),node=reader.ReadInt32();float loop=BvmFloat(reader),cycles=BvmFloat(reader),perMeter=BvmFloat(reader);
                    if((side!=-1&&side!=1)||binding<0||binding>=bindingCount||node<0||node>=nodeCount||rig.Kinds[node]!=BvmRigBinding.Track||bindingSides[binding]!=side||loop<=0f||Math.Abs(cycles)<.001f||Math.Abs(cycles/loop-perMeter)>.001f)
                        throw new InvalidDataException("Invalid BVM track rig");
                    rig.TrackSides[i]=side;rig.TrackBindings[i]=binding;rig.TrackLoops[i]=loop;rig.TrackRepeats[i]=cycles;rig.TrackVSigns[i]=Math.Sign(cycles);rig.Sides[node]=side;
                    int cursor=node;while(cursor>=0){trackPivots[i]+=rig.LocalPositions[cursor];cursor=rig.Parents[cursor];}
                }
                if(leftCount!=6||rightCount!=6)throw new InvalidDataException("BVM road-wheel gauge anchors missing");
                rig.TrackGauge=Math.Abs(rightX/rightCount-leftX/leftCount);
                if(rig.TrackSides[0]==rig.TrackSides[1]||rig.TrackBindings[0]==rig.TrackBindings[1]||rig.TrackGauge<.1f)throw new InvalidDataException("BVM tracks not independent");
                rig.Yaw=reader.ReadInt32();rig.Pitch=reader.ReadInt32();rig.Recoil=reader.ReadInt32();
                rig.YawAxis=BvmVector(reader);rig.PitchAxis=BvmVector(reader);rig.RecoilAxis=BvmVector(reader);
                rig.RestElevation=BvmFloat(reader);rig.PitchAngleSign=BvmFloat(reader);
                rig.YawMin=BvmFloat(reader);rig.YawMax=BvmFloat(reader);rig.PitchMin=BvmFloat(reader);rig.PitchMax=BvmFloat(reader);rig.YawSpeed=BvmFloat(reader);rig.PitchSpeed=BvmFloat(reader);
                rig.MuzzleParent=reader.ReadInt32();rig.MuzzleLocal=BvmVector(reader);
                if(rig.Yaw<0||rig.Yaw>=nodeCount||rig.Pitch<0||rig.Pitch>=nodeCount||rig.Recoil<0||rig.Recoil>=nodeCount||
                    rig.Kinds[rig.Yaw]!=BvmRigBinding.Yaw||rig.Kinds[rig.Pitch]!=BvmRigBinding.Pitch||rig.Kinds[rig.Recoil]!=BvmRigBinding.Recoil||
                    rig.MuzzleParent<0||rig.MuzzleParent>=nodeCount||rig.YawSpeed<=0||rig.PitchSpeed<=0||Math.Abs(Math.Abs(rig.PitchAngleSign)-1f)>.001f)
                    throw new InvalidDataException("Invalid BVM articulation");
                rig.Axes[rig.Yaw]=rig.YawAxis;rig.Axes[rig.Pitch]=rig.PitchAxis;rig.Axes[rig.Recoil]=rig.RecoilAxis;
                if(reader.BaseStream.Position!=reader.BaseStream.Length)throw new InvalidDataException("BVM rig unexpected tail");
            }
            return rig;
        }

        static int StripInheritedBvmEffects(GameObject model)
        {
            int count=0;
            Type[] types={typeof(Light),typeof(LensFlare),typeof(Projector),typeof(ParticleSystem),typeof(ParticleSystemRenderer),typeof(ParticleEmitter),typeof(ParticleRenderer)};
            foreach(Type type in types)
                foreach(Component component in model.GetComponentsInChildren(type,true))
                    if(component!=null){UnityEngine.Object.DestroyImmediate(component);count++;}
            return count;
        }

        static GameObject CreateBvmPrefab(GameObject original)
        {
            string root=Path.Combine(AssetDirectory,"bvm_animated"),textureRoot=Path.Combine(AssetDirectory,"bvm");
            string file=Path.Combine(root,"bvm_animated.bin"),rigFile=Path.Combine(root,"rig_runtime.bin");
            if(!File.Exists(file)||!File.Exists(rigFile))throw new FileNotFoundException("Prepared BVM articulated package missing",root);
            using(System.Security.Cryptography.SHA256 sha=System.Security.Cryptography.SHA256.Create())
                ObjHash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-","").ToLowerInvariant();
            Templates=new GameObject("MapUnitLab.BVMTemplates");Templates.SetActive(false);Templates.hideFlags=HideFlags.HideAndDontSave;UnityEngine.Object.DontDestroyOnLoad(Templates);
            GameObject model=(GameObject)UnityEngine.Object.Instantiate(original);
            model.name=original.name;model.transform.parent=Templates.transform;model.transform.localPosition=Vector3.zero;
            model.transform.localRotation=original.transform.localRotation;model.transform.localScale=original.transform.localScale;
            LODGroup group=model.GetComponent<LODGroup>();LOD[] oldLODs=group==null?new LOD[0]:group.GetLODs();
            Renderer[] native=oldLODs.Length>0?oldLODs[0].renderers:model.GetComponentsInChildren<Renderer>(true);
            Bounds nativeBounds=ModelBounds(model,native);Material template=null;int layer=model.layer;
            foreach(Renderer renderer in native)if(renderer!=null&&renderer.sharedMaterial!=null){template=renderer.sharedMaterial;layer=renderer.gameObject.layer;break;}
            if(template==null||template.shader==null||!template.shader.isSupported)throw new Exception("Native unit material unavailable for BVM import");
            Dictionary<string,Texture2D> textureCache=new Dictionary<string,Texture2D>(StringComparer.OrdinalIgnoreCase);
            BvmRigData rig=ReadBvmRig(rigFile,textureRoot,template,textureCache);
            List<BvmChunk> chunks=new List<BvmChunk>();Bounds bvmBounds=new Bounds();bool boundsReady=false;
            using(BinaryReader reader=new BinaryReader(File.OpenRead(file),System.Text.Encoding.UTF8))
            {
                if(System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4))!="BVA2"||reader.ReadInt32()!=2)throw new InvalidDataException("Unsupported BVM animated buffer header");
                int materialCount=BvmCount(reader,64),nodeCount=BvmCount(reader,512),chunkCount=BvmCount(reader,512);
                if(materialCount!=rig.Materials.Length||nodeCount!=rig.Names.Length||chunkCount==0)throw new InvalidDataException("BVM geometry/rig counts mismatch");
                for(int i=0;i<chunkCount;i++)
                {
                    BvmChunk chunk=new BvmChunk();chunk.Part=reader.ReadInt32();chunk.Material=reader.ReadInt32();
                    int vertices=BvmCount(reader,65535),indices=BvmCount(reader,1000000),sourceGroups=BvmCount(reader,4096);
                    if(chunk.Part<0||chunk.Part>=nodeCount||chunk.Material<0||chunk.Material>=materialCount||vertices<3||indices<3||indices%3!=0)throw new InvalidDataException("Invalid BVM animated chunk");
                    for(int j=0;j<sourceGroups;j++)if(reader.ReadInt32()<0)throw new InvalidDataException("Invalid BVM source group");
                    chunk.Positions=new Vector3[vertices];chunk.Normals=new Vector3[vertices];chunk.UV=new Vector2[vertices];chunk.Indices=new int[indices];
                    for(int v=0;v<vertices;v++)
                    {
                        Vector3 point=BvmVector(reader),normal=BvmVector(reader);float u=BvmFloat(reader),t=BvmFloat(reader);
                        chunk.Positions[v]=point;chunk.Normals[v]=normal.sqrMagnitude>.000001f?normal.normalized:Vector3.up;chunk.UV[v]=new Vector2(u,t);
                        if(!boundsReady){bvmBounds=new Bounds(point,Vector3.zero);boundsReady=true;}else bvmBounds.Encapsulate(point);
                    }
                    for(int j=0;j<indices;j++){int index=reader.ReadInt32();if(index<0||index>=vertices)throw new InvalidDataException("BVM triangle index out of range");chunk.Indices[j]=index;}
                    ImportedVertices+=vertices;ImportedTriangles+=indices/3;chunks.Add(chunk);
                }
                if(reader.BaseStream.Position!=reader.BaseStream.Length)throw new InvalidDataException("BVM geometry unexpected tail");
            }
            ImportedMeshes=chunks.Count;
            if(!boundsReady||ImportedTriangles!=315238||Math.Abs(bvmBounds.min.y)>.001f)throw new InvalidDataException("BVM faces or model ground anchor mismatch");
            float scale=Math.Min(nativeBounds.size.x/Math.Max(.001f,bvmBounds.size.x),nativeBounds.size.z/Math.Max(.001f,bvmBounds.size.z))*.96f;
            Mesh empty=new Mesh();empty.name="EF_BVM_EmptyNativeGeometry";empty.hideFlags=HideFlags.HideAndDontSave;Owned.Add(empty);int blanked=0;
            foreach(SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)){renderer.sharedMesh=empty;renderer.enabled=false;blanked++;}
            foreach(MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true)){filter.sharedMesh=empty;blanked++;}
            foreach(Renderer renderer in model.GetComponentsInChildren<Renderer>(true))renderer.enabled=false;
            int removedFX=StripInheritedBvmEffects(model);
            GameObject presentation=new GameObject("EF_BVM_PresentationRoot");presentation.layer=layer;presentation.transform.parent=model.transform;
            presentation.transform.localPosition=Vector3.zero;presentation.transform.localRotation=Quaternion.identity;presentation.transform.localScale=Vector3.one;
            Transform[] bones=new Transform[rig.Names.Length];
            for(int i=0;i<bones.Length;i++)
            {
                GameObject node=new GameObject("EF_BVM_Rig_"+i+"_"+rig.Names[i]);node.layer=layer;bones[i]=node.transform;
                bones[i].parent=rig.Parents[i]<0?presentation.transform:bones[rig.Parents[i]];
                bones[i].localPosition=rig.LocalPositions[i]*scale;bones[i].localRotation=rig.Rotations[i];bones[i].localScale=Vector3.one;
            }
            List<Renderer>[] levels={new List<Renderer>(),new List<Renderer>(),new List<Renderer>()};
            for(int i=0;i<chunks.Count;i++)
            {
                BvmChunk chunk=chunks[i];Vector3[] vertices=chunk.Positions;for(int v=0;v<vertices.Length;v++)vertices[v]*=scale;
                Mesh mesh=new Mesh();mesh.name="EF_BVM_Chunk"+i;mesh.hideFlags=HideFlags.HideAndDontSave;mesh.vertices=vertices;mesh.normals=chunk.Normals;
                mesh.uv=chunk.UV;mesh.triangles=chunk.Indices;mesh.tangents=BvmTangents(vertices,chunk.Normals,chunk.UV,chunk.Indices);
                BoneWeight[] weights=new BoneWeight[vertices.Length];for(int v=0;v<weights.Length;v++){weights[v].boneIndex0=0;weights[v].weight0=1f;}
                mesh.boneWeights=weights;mesh.bindposes=new Matrix4x4[]{bones[chunk.Part].worldToLocalMatrix*presentation.transform.localToWorldMatrix};
                Color[] colors=new Color[vertices.Length];for(int v=0;v<colors.Length;v++)colors[v]=Color.white;mesh.colors=colors;mesh.RecalculateBounds();Owned.Add(mesh);
                for(int lod=0;lod<3;lod++)
                {
                    GameObject part=new GameObject("EF_BVM_Part"+chunk.Part+"_Chunk"+i+"_LOD"+lod);part.layer=layer;part.transform.parent=presentation.transform;
                    part.transform.localPosition=Vector3.zero;part.transform.localRotation=Quaternion.identity;part.transform.localScale=Vector3.one;
                    SkinnedMeshRenderer renderer=part.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=mesh;renderer.bones=new Transform[]{bones[chunk.Part]};renderer.rootBone=bones[0];
                    renderer.updateWhenOffscreen=true;Bounds skinBounds=mesh.bounds;skinBounds.Expand(1f*scale);renderer.localBounds=skinBounds;renderer.sharedMaterial=rig.Materials[chunk.Material];levels[lod].Add(renderer);
                }
            }
            BvmRigBinding binding=model.AddComponent<BvmRigBinding>();binding.ModelRoot=presentation.transform;binding.Bones=bones;binding.PartTransforms=bones;
            binding.RigNames=rig.Names;binding.RigKinds=rig.Kinds;binding.BindLocalPositions=new Vector3[bones.Length];binding.BindLocalRotations=new Quaternion[bones.Length];
            binding.RigAxes=rig.Axes;binding.WheelRadii=rig.Radii;binding.WheelSpinSigns=rig.SpinSigns;binding.RigSides=rig.Sides;binding.VisualScale=scale;
            for(int i=0;i<bones.Length;i++){binding.BindLocalPositions[i]=bones[i].localPosition;binding.BindLocalRotations[i]=bones[i].localRotation;binding.WheelRadii[i]*=scale;}
            binding.TrackMaterials=new Material[rig.TrackBindings.Length];binding.TrackMaterialSides=rig.TrackSides;binding.TrackLoopLengths=rig.TrackLoops;
            binding.TrackUVRepeats=rig.TrackRepeats;binding.TrackVSigns=rig.TrackVSigns;binding.TrackGauge=rig.TrackGauge*scale;
            for(int i=0;i<binding.TrackMaterials.Length;i++){binding.TrackMaterials[i]=rig.Materials[rig.TrackBindings[i]];binding.TrackLoopLengths[i]*=scale;}
            binding.YawNode=rig.Yaw;binding.PitchNode=rig.Pitch;binding.RecoilNode=rig.Recoil;binding.YawAxis=rig.YawAxis;binding.PitchAxis=rig.PitchAxis;binding.RecoilAxis=rig.RecoilAxis;
            binding.RestElevation=rig.RestElevation;binding.PitchAngleSign=rig.PitchAngleSign;binding.YawMin=rig.YawMin;binding.YawMax=rig.YawMax;
            binding.PitchMin=rig.PitchMin;binding.PitchMax=rig.PitchMax;binding.YawSpeed=rig.YawSpeed;binding.PitchSpeed=rig.PitchSpeed;
            GameObject muzzle=new GameObject("EF_BVM_Muzzle");muzzle.layer=layer;muzzle.transform.parent=bones[rig.MuzzleParent];muzzle.transform.localPosition=rig.MuzzleLocal*scale;
            muzzle.transform.localRotation=Quaternion.LookRotation(rig.RecoilAxis,Vector3.up);muzzle.transform.localScale=Vector3.one;binding.Muzzle=muzzle.transform;
            if(group==null)group=model.AddComponent<LODGroup>();
            LOD[] lods=new LOD[3];for(int lod=0;lod<3;lod++)lods[lod]=new LOD(lod<oldLODs.Length?oldLODs[lod].screenRelativeTransitionHeight:(lod==0?.6f:(lod==1?.25f:.03f)),levels[lod].ToArray());
            group.SetLODs(lods);group.RecalculateBounds();UnitView cachedView=model.GetComponent<UnitView>();if(cachedView!=null)Set(cachedView,"mGeoRenderObjects",null);
            model.SetActive(true);
            Runtime.Log("BVM_ARTICULATED_IMPORT_PREPARED file="+file+"; SHA256="+ObjHash+"; vertices="+ImportedVertices+"; triangles="+ImportedTriangles+"; meshes="+ImportedMeshes+
                "; bones="+bones.Length+"; wheels=26; independentTrackMaterials=2; uniformVisualScale="+scale+"; gauge="+binding.TrackGauge+"; importedSkinned="+(ImportedMeshes*3)+
                "; nativeRootBoneUsed=False; presentationRoot=EF_BVM_PresentationRoot; nativeGeoRendererCacheCleared="+(cachedView!=null)+"; nativeMeshesBlanked="+blanked+
                "; inheritedLightParticleComponentsRemoved="+removedFX+"; sourceBvmBounds="+bvmBounds+"; nativeWrapperBounds="+nativeBounds+"; ballistic125mmSimulated=False; actualRenderingNotYetVerified=True");
            return model;
        }

        static int BvmCount(BinaryReader reader,int max)
        {
            int count=reader.ReadInt32();if(count<0||count>max)throw new InvalidDataException("Invalid BVM buffer count");return count;
        }
        static string BvmText(BinaryReader reader)
        {
            int count=BvmCount(reader,4096);byte[] b=reader.ReadBytes(count);if(b.Length!=count)throw new EndOfStreamException();return System.Text.Encoding.UTF8.GetString(b);
        }
        static float BvmFloat(BinaryReader reader)
        {
            float value=reader.ReadSingle();if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidDataException("Nonfinite BVM value");return value;
        }
        static Vector3 BvmVector(BinaryReader reader){return new Vector3(BvmFloat(reader),BvmFloat(reader),BvmFloat(reader));}
        static Texture2D BvmTexture(string root,string relative,bool linear,Dictionary<string,Texture2D> cache)
        {
            if(relative.Length==0)throw new InvalidDataException("Required BVM texture path empty");
            string key=(linear?"linear:":"color:")+relative;Texture2D result;if(cache.TryGetValue(key,out result))return result;
            string fullRoot=Path.GetFullPath(root),file=Path.GetFullPath(Path.Combine(fullRoot,relative));
            if(!file.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("BVM texture escapes asset directory");
            result=new Texture2D(2,2,TextureFormat.RGBA32,true,linear);if(!result.LoadImage(File.ReadAllBytes(file)))throw new InvalidDataException("Cannot decode BVM texture "+relative);
            result.name="EF_BVM_"+relative;result.wrapMode=TextureWrapMode.Repeat;result.filterMode=FilterMode.Trilinear;result.hideFlags=HideFlags.HideAndDontSave;
            Owned.Add(result);cache[key]=result;return result;
        }
        static Vector4[] BvmTangents(Vector3[] vertices,Vector3[] normals,Vector2[] uv,int[] indices)
        {
            Vector3[] sdir=new Vector3[vertices.Length],tdir=new Vector3[vertices.Length];
            for(int i=0;i<indices.Length;i+=3)
            {
                int a=indices[i],b=indices[i+1],c=indices[i+2];Vector3 ab=vertices[b]-vertices[a],ac=vertices[c]-vertices[a];
                Vector2 ut=uv[b]-uv[a],vt=uv[c]-uv[a];float determinant=ut.x*vt.y-ut.y*vt.x;if(Math.Abs(determinant)<1e-10f)continue;
                Vector3 s=(ab*vt.y-ac*ut.y)/determinant,t=(ac*ut.x-ab*vt.x)/determinant;
                sdir[a]+=s;sdir[b]+=s;sdir[c]+=s;tdir[a]+=t;tdir[b]+=t;tdir[c]+=t;
            }
            Vector4[] result=new Vector4[vertices.Length];
            for(int i=0;i<vertices.Length;i++)
            {
                Vector3 n=normals[i],s=sdir[i]-n*Vector3.Dot(n,sdir[i]);
                if(s.sqrMagnitude<1e-10f)s=Vector3.Cross(n,Math.Abs(n.y)<.95f?Vector3.up:Vector3.right);s.Normalize();
                result[i]=new Vector4(s.x,s.y,s.z,Vector3.Dot(Vector3.Cross(n,s),tdir[i])<0?-1f:1f);
            }
            return result;
        }

        // The legacy small-car parser is retained only as non-default research.
        // BVM uses a bounded, textured multi-Mesh stream instead of this OBJ path.
        static GameObject CreateLegacyRoverPrefab(GameObject original)
        {
            string objFile = Path.Combine(AssetDirectory, "lab_scout.obj");
            string mtlFile = Path.Combine(AssetDirectory, "lab_scout.mtl");
            if (!File.Exists(objFile) || !File.Exists(mtlFile)) throw new FileNotFoundException("External lab OBJ/MTL missing", objFile);
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                ObjHash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(objFile))).Replace("-", "").ToLowerInvariant();
            Templates = new GameObject("MapUnitLab.ModelTemplates");
            Templates.SetActive(false); Templates.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(Templates);
            GameObject model = (GameObject)UnityEngine.Object.Instantiate(original);
            model.name = original.name;
            model.transform.parent = Templates.transform;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = original.transform.localRotation;
            model.transform.localScale = original.transform.localScale;
            LODGroup group = model.GetComponent<LODGroup>();
            LOD[] oldLODs = group == null ? new LOD[0] : group.GetLODs();
            Renderer[] renderers = oldLODs.Length > 0 ? oldLODs[0].renderers : model.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = ModelBounds(model, renderers);
            Material template = null;
            foreach (Renderer renderer in renderers)
                if (renderer != null && renderer.sharedMaterial != null) { template = renderer.sharedMaterial; break; }
            if (template == null || template.shader == null || !template.shader.isSupported) throw new Exception("Native LAV material/shader not available");
            ObjGeometry geometry = ReadObj(objFile);
            Material[] materials = MaterialsFromMtl(mtlFile, geometry.Materials, template);
            float horizontal = Math.Min(bounds.size.x / Math.Max(.001f,geometry.Bounds.size.x), bounds.size.z / Math.Max(.001f,geometry.Bounds.size.z)) * .96f;
            Vector3[] vertices = geometry.Vertices.ToArray();
            for (int i=0;i<vertices.Length;i++)
            {
                Vector3 p=vertices[i];
                vertices[i] = new Vector3((p.x-geometry.Bounds.center.x)*horizontal+bounds.center.x,
                    (p.y-geometry.Bounds.min.y)*horizontal+bounds.min.y,
                    (p.z-geometry.Bounds.center.z)*horizontal+bounds.center.z);
            }
            Mesh mesh = new Mesh(); mesh.name = "EF_LabScout_ExternalOBJ"; mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.vertices = vertices; mesh.subMeshCount = geometry.Materials.Count;
            Vector2[] uv = new Vector2[vertices.Length]; Vector4[] tangents = new Vector4[vertices.Length]; Color[] colors = new Color[vertices.Length];
            for(int i=0;i<vertices.Length;i++){uv[i]=new Vector2(vertices[i].x*.05f,vertices[i].z*.05f);tangents[i]=new Vector4(1,0,0,1);colors[i]=Color.white;}
            mesh.uv=uv;mesh.tangents=tangents;mesh.colors=colors;
            for(int i=0;i<geometry.Materials.Count;i++)mesh.SetTriangles(geometry.Triangles[i].ToArray(),i);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); Owned.Add(mesh);
            if (group == null) group = model.AddComponent<LODGroup>();
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true)) if (renderer != null) renderer.enabled = false;
            LOD[] lods = new LOD[3];
            for(int i=0;i<3;i++)
            {
                GameObject body = new GameObject("EF_LabScout_External_LOD"+i); body.layer=renderers[0].gameObject.layer;
                body.transform.parent=model.transform;body.transform.localPosition=Vector3.zero;body.transform.localRotation=Quaternion.identity;body.transform.localScale=Vector3.one;
                MeshFilter filter=body.AddComponent<MeshFilter>();filter.sharedMesh=mesh;
                MeshRenderer renderer=body.AddComponent<MeshRenderer>();renderer.sharedMaterials=materials;
                float threshold=i<oldLODs.Length?oldLODs[i].screenRelativeTransitionHeight:(i==0?.6f:(i==1?.25f:.03f));
                lods[i]=new LOD(threshold,new Renderer[]{renderer});
            }
            group.SetLODs(lods);group.RecalculateBounds();
            Runtime.Log("CUSTOM_OBJ_IMPORTED vertices="+mesh.vertexCount+"; triangleCount="+(mesh.triangles.Length/3)+"; materials="+materials.Length+
                "; scale="+horizontal+"; nativeBounds="+bounds+"; file="+objFile+"; SHA256="+ObjHash);
            return model;
        }

        static Bounds ModelBounds(GameObject model, Renderer[] renderers)
        {
            bool initialized=false;Bounds result=new Bounds();
            foreach(Renderer renderer in renderers)
            {
                if(renderer==null)continue;
                Bounds local;SkinnedMeshRenderer skinned=renderer as SkinnedMeshRenderer;
                if(skinned!=null)local=skinned.localBounds;
                else {MeshFilter filter=renderer.GetComponent<MeshFilter>();if(filter==null||filter.sharedMesh==null)continue;local=filter.sharedMesh.bounds;}
                for(int corner=0;corner<8;corner++)
                {
                    Vector3 p=new Vector3((corner&1)==0?local.min.x:local.max.x,(corner&2)==0?local.min.y:local.max.y,(corner&4)==0?local.min.z:local.max.z);
                    p=model.transform.InverseTransformPoint(renderer.transform.TransformPoint(p));
                    if(!initialized){result=new Bounds(p,Vector3.zero);initialized=true;}else result.Encapsulate(p);
                }
            }
            if(!initialized||result.size.x<=0||result.size.z<=0)throw new Exception("Cannot measure original LAV local bounds");
            return result;
        }

        sealed class ObjGeometry
        {
            public readonly List<Vector3> Vertices=new List<Vector3>();
            public readonly List<string> Materials=new List<string>();
            public readonly List<List<int>> Triangles=new List<List<int>>();
            public Bounds Bounds;
        }
        static ObjGeometry ReadObj(string file)
        {
            ObjGeometry data=new ObjGeometry();int material=-1;
            foreach(string raw in File.ReadAllLines(file))
            {
                string line=raw.Trim();if(line.Length==0||line[0]=='#')continue;
                string[] parts=line.Split((char[])null,StringSplitOptions.RemoveEmptyEntries);
                if(parts[0]=="v")
                {
                    if(parts.Length!=4)throw new InvalidDataException("OBJ vertex contract");
                    Vector3 p=new Vector3(float.Parse(parts[1],CultureInfo.InvariantCulture),float.Parse(parts[2],CultureInfo.InvariantCulture),float.Parse(parts[3],CultureInfo.InvariantCulture));
                    if(float.IsNaN(p.x)||float.IsNaN(p.y)||float.IsNaN(p.z)||float.IsInfinity(p.x)||float.IsInfinity(p.y)||float.IsInfinity(p.z))throw new InvalidDataException("OBJ nonfinite vertex");
                    data.Vertices.Add(p);if(data.Vertices.Count==1)data.Bounds=new Bounds(p,Vector3.zero);else data.Bounds.Encapsulate(p);
                }
                else if(parts[0]=="usemtl")
                {
                    if(parts.Length!=2)throw new InvalidDataException("OBJ material contract");
                    material=data.Materials.IndexOf(parts[1]);
                    if(material<0){material=data.Materials.Count;data.Materials.Add(parts[1]);data.Triangles.Add(new List<int>());}
                }
                else if(parts[0]=="f")
                {
                    if(material<0||parts.Length<4||parts.Length>8)throw new InvalidDataException("OBJ face contract");
                    int first=ObjIndex(parts[1],data.Vertices.Count);
                    for(int i=2;i<parts.Length-1;i++){data.Triangles[material].Add(first);data.Triangles[material].Add(ObjIndex(parts[i],data.Vertices.Count));data.Triangles[material].Add(ObjIndex(parts[i+1],data.Vertices.Count));}
                }
                if(data.Vertices.Count>65535||data.Materials.Count>16)throw new InvalidDataException("OBJ exceeds lab limits");
            }
            if(data.Vertices.Count<3||data.Materials.Count==0)throw new InvalidDataException("OBJ has no geometry");
            return data;
        }
        static int ObjIndex(string token,int count)
        {
            int slash=token.IndexOf('/');if(slash>=0)token=token.Substring(0,slash);
            int index=int.Parse(token,CultureInfo.InvariantCulture);index=index>0?index-1:count+index;
            if(index<0||index>=count)throw new InvalidDataException("OBJ index outside vertices");return index;
        }
        static Material[] MaterialsFromMtl(string file,List<string> names,Material template)
        {
            Dictionary<string,Color> palette=new Dictionary<string,Color>(StringComparer.Ordinal);string name=null;
            foreach(string raw in File.ReadAllLines(file))
            {
                string[] p=raw.Trim().Split((char[])null,StringSplitOptions.RemoveEmptyEntries);if(p.Length==0)continue;
                if(p[0]=="newmtl"&&p.Length==2)name=p[1];
                else if(p[0]=="Kd"&&p.Length==4&&name!=null)palette[name]=new Color(float.Parse(p[1],CultureInfo.InvariantCulture),float.Parse(p[2],CultureInfo.InvariantCulture),float.Parse(p[3],CultureInfo.InvariantCulture),1f);
            }
            Material[] result=new Material[names.Count];
            for(int i=0;i<names.Count;i++)
            {
                Color color;if(!palette.TryGetValue(names[i],out color))throw new InvalidDataException("MTL color missing "+names[i]);
                Material material=new Material(template);material.name="EF_Lab_"+names[i];material.hideFlags=HideFlags.HideAndDontSave;
                Texture2D albedo=Solid(color);material.SetTexture("_MainTex",albedo);
                if(material.HasProperty("_OverlayMask"))material.SetTexture("_OverlayMask",Solid(Color.clear));
                if(material.HasProperty("_BumpMap"))material.SetTexture("_BumpMap",Solid(new Color(.5f,.5f,1f,.5f)));
                foreach(string prop in new[]{"_Color","_BaseColor","_TrimColor"})if(material.HasProperty(prop))material.SetColor(prop,Color.white);
                Owned.Add(material);result[i]=material;
            }
            return result;
        }
        static Texture2D Solid(Color color)
        {
            Texture2D texture=new Texture2D(1,1,TextureFormat.RGBA32,false);texture.SetPixel(0,0,color);texture.Apply();texture.hideFlags=HideFlags.HideAndDontSave;Owned.Add(texture);return texture;
        }

        static ScriptableObject CloneAsset(ScriptableObject original,string name,bool enable)
        {
            ScriptableObject clone=RhodesAssetClone59.CreateEmpty(original.GetType());
            foreach(FieldInfo field in Fields(original.GetType()))if(field.Name!="mDataCache")field.SetValue(clone,Copy(field.GetValue(original)));
            clone.name=name;clone.hideFlags=HideFlags.HideAndDontSave;Owned.Add(clone);
            if(enable){MethodInfo onEnable=FindMethod(clone.GetType(),"OnEnable");if(onEnable!=null)onEnable.Invoke(clone,null);}
            return clone;
        }
        static MethodInfo FindMethod(Type type,string name)
        {
            for(Type current=type;current!=null;current=current.BaseType)
            {
                MethodInfo method=current.GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly,null,Type.EmptyTypes,null);
                if(method!=null)return method;
            }
            return null;
        }
        static IEnumerable<FieldInfo> Fields(Type type)
        {
            for(Type current=type;current!=null&&current!=typeof(ScriptableObject)&&current!=typeof(UnityEngine.Object)&&current!=typeof(object);current=current.BaseType)
                foreach(FieldInfo field in current.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))if(!field.IsStatic)yield return field;
        }
        static void Set(object value,string name,object data)
        {
            foreach(FieldInfo field in Fields(value.GetType()))if(field.Name==name){field.SetValue(value,data);return;}
            throw new MissingFieldException(value.GetType().FullName,name);
        }
        sealed class IdentityComparer:IEqualityComparer<object>
        {
            public new bool Equals(object a,object b){return object.ReferenceEquals(a,b);}
            public int GetHashCode(object value){return RuntimeHelpers.GetHashCode(value);}
        }
        static object Copy(object value){return Copy(value,new Dictionary<object,object>(new IdentityComparer()));}
        static object Copy(object value,Dictionary<object,object> seen)
        {
            if(value==null)return null;Type type=value.GetType();
            if(type.IsPrimitive||type.IsEnum||type==typeof(string)||typeof(UnityEngine.Object).IsAssignableFrom(type))return value;
            object previous;if(seen.TryGetValue(value,out previous))return previous;
            Array array=value as Array;if(array!=null){Array result=(Array)array.Clone();seen.Add(value,result);for(int i=0;i<result.Length;i++)result.SetValue(Copy(array.GetValue(i),seen),i);return result;}
            if(type.Namespace==null||!type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))return value;
            object clone=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,null);seen.Add(value,clone);
            foreach(FieldInfo field in Fields(type))field.SetValue(clone,Copy(field.GetValue(value),seen));return clone;
        }
        static void ClearOwned()
        {
            if(Templates!=null){UnityEngine.Object.DestroyImmediate(Templates);Templates=null;}
            for(int i=Owned.Count-1;i>=0;i--)if(Owned[i]!=null)UnityEngine.Object.DestroyImmediate(Owned[i]);
            Owned.Clear();
        }
    }
}
