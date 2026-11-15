using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Reflection;
using BBI.Core.Data;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Replay;
using BBI.Game.Simulation;
using BBI.Unity.Core.Data;
using BBI.Unity.Core.Rendering;
using BBI.Unity.Core.Utility;
using BBI.Unity.Game;
using BBI.Unity.Game.Events;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Independent logical map and generated world. The Unity scene loader and
    // non-geometric bootstrap services are borrowed from a native level.
    // No new compiled Unity scene/AssetBundle is claimed by this experiment.
    public static class HubRegionMap
    {
        public const string MapKey = "EF_HubRegion_01";
        static HubData Data;
        static HubHeightAttributes Heights;
        static readonly List<MeshCollider> WalkColliders=new List<MeshCollider>();
        static readonly List<Renderer> GameplayRenderers=new List<Renderer>(), SensorRenderers=new List<Renderer>();
        static readonly List<ImportedSensorBinding> ImportedSensorBindings=new List<ImportedSensorBinding>();
        static readonly Color[] SensorColors=new Color[6];
        static readonly Material[] SemanticSensorMaterials=new Material[6];
        static readonly Dictionary<string,Texture2D> Textures=new Dictionary<string,Texture2D>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<SetRenderSettings,ParticleOcclusionMapAttributesAsset> AuxiliaryOriginals=new Dictionary<SetRenderSettings,ParticleOcclusionMapAttributesAsset>();
        static int ExpectedObstacleCount;
        public static HeightMapAttributes HeightAttributes { get { return Heights; } }
        static string AssetRoot { get { return Path.Combine(CustomUnits.AssetDirectory,"hub_region_v01"); } }
        static readonly MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly List<GameObject> SourceRoots = new List<GameObject>();
        static int RemovedRendererCount, DisabledSensorControllerCount;
        const float SourceSweepInterval = 5f;
        static float NextSourceSweepAt, LastSourceSweepMs, PeakSourceSweepMs;
        static float NextFrameReportAt, FrameIntervalTotalMs, FrameIntervalMaxMs;
        static int FrameSamples, FramesAbove33Ms, FramesAbove50Ms, LastProfileFrame = -1;
        static readonly List<Collider> DisabledColliders = new List<Collider>();
        static readonly List<UnityEngine.Object> CreatedAssets = new List<UnityEngine.Object>();
        static LevelDefinition Definition;
        static LevelManager Manager;
        static LevelDefinition[] OriginalEntries;
        static GameObject SpawnRoot, WorldRoot, DynamicRoot;
        static Scene LoadingScene;
        static DependencyContainerBase SessionDependencies;
        static bool SensorEventsBound, SensorActive, SensorStateApplied, SourcePresentationVerified;
        static UserCamera ActiveUserCamera;
        static BoxCollider CameraConstraints;
        static volatile bool DependenciesInjected, ColliderVerified, SimulationVerified;
        static volatile string Fault;
        static int PhysicsWaitFrames;

        public static Vector2r Center { get { return Data==null?P(0,0):P((Data.MinX+Data.MaxX)*.5f,(Data.MinZ+Data.MaxZ)*.5f); } }
        public static Vector2r Start { get { return HubRecordingScenario.Enabled?HubRecordingScenario.PlayerStart:(Data==null?P(0,0):P(Data.StartX,Data.StartZ)); } }
        public static Vector2r Goal { get { return Data==null?P(0,0):P(Data.GoalX,Data.GoalZ); } }
        public static void ConfigurePlayerTestArea(Vector2r carrierOrigin)
        { Runtime.Log("HUB TEST AREA: nativeOrigin="+carrierOrigin+"; manifestStart="+Start+"; goal="+Goal+"; campaignSetup=False; recordingLayout="+HubRecordingScenario.Enabled+""); }
        public static void ConfigureStandaloneTestArea(Vector2r unitOrigin)
        { ConfigurePlayerTestArea(unitOrigin); }

        public static bool Ready { get { return DependenciesInjected && ColliderVerified && SimulationVerified && Fault == null; } }

        public static LevelDefinition RegisterClone(LevelManager levels,LevelDefinition template)
        {
            if(Definition!=null)return Definition;
            if(levels==null||template==null)throw new ArgumentNullException("Hub import requires native bootstrap services");
            System.Diagnostics.Stopwatch dataClock=System.Diagnostics.Stopwatch.StartNew();
            Data=HubData.Load(Path.Combine(AssetRoot,"hub_region.bin"));Heights=new HubHeightAttributes(Data);
            LogLoadPhase(dataClock,"mapMetadataAndMasks");dataClock.Stop();
            EntityTypeCollection registry=(EntityTypeCollection)NativeRead.Get(typeof(ShipbreakersMain),"sEntityTypes");
            if(registry==null||!CustomUnits.Ready)throw new InvalidOperationException("Hub navigation needs the registered BVM first");
            UnitAttributes bvm=registry.GetEntityType(CustomUnits.UnitKey).Get<UnitAttributes>();
            HubRegionNavigation.Configure(bvm.NavMeshAttributes,Heights,Data.Valid,BuildObstacles(),Data.Path);
            Definition=(LevelDefinition)CloneMethod.Invoke(template,null);
            NativeRead.Set(Definition,"m_SceneName",MapKey);NativeRead.Set(Definition,"m_NameLocId","四号谷地·枢纽区");
            NativeRead.Set(Definition,"m_MapDescriptionLocId","枢纽区周边的谷地场景，包含建筑、山坡与谷地通道。");
            NativeRead.Set(Definition,"m_AmbientHeatPoints",0);NativeRead.Set(Definition,"m_RandomArtifacts",new string[0]);
            NativeRead.Set(Definition,"m_MaximumSpawnedWreckArtifacts",0);NativeRead.Set(Definition,"m_EnableCampaignPersistence",false);
            NativeRead.Set(Definition,"m_ShowMapBoundaries",true);NativeRead.Set(Definition,"m_ShowMapBoundariesSensors",true);
            string[] subscenes=template.GetAllSubscenes();MultiSceneAsset bootstrap=RhodesAssetClone59.CreateEmpty<MultiSceneAsset>();
            bootstrap.name=MapKey+"_NativeServiceBootstrap";bootstrap.Subscenes=new MultiSceneAsset.SceneInfo[subscenes.Length];
            for(int i=0;i<subscenes.Length;i++){bootstrap.Subscenes[i]=new MultiSceneAsset.SceneInfo();bootstrap.Subscenes[i].Include=true;bootstrap.Subscenes[i].SceneName=subscenes[i];bootstrap.Subscenes[i].SceneAssetPath=string.Empty;}
            NativeRead.Set(Definition,"m_MultiScene",bootstrap);CreatedAssets.Add(bootstrap);BuildSpawnInfo();NativeRead.Set(Definition,"m_SceneSpawnInfo",SpawnRoot);
            OriginalEntries=(LevelDefinition[])NativeRead.Get(levels,"m_LevelEntriesMP");LevelDefinition[] entries=new LevelDefinition[OriginalEntries.Length+1];
            Array.Copy(OriginalEntries,entries,OriginalEntries.Length);entries[entries.Length-1]=Definition;NativeRead.Set(levels,"m_LevelEntriesMP",entries);
            Manager=levels;levels.SceneLoadStart+=OnSceneLoadStart;
            Runtime.Log("HUB IMPORT REGISTERED: key="+MapKey+"; source="+Data.Path+"; bounds="+Data.MinX+","+Data.MinZ+".."+Data.MaxX+","+Data.MaxZ+
                "; heightGrid="+Data.W+"x"+Data.L+"; heightRange="+Data.Range+"; byteQuantum="+(Data.Range/255f)+"; meshChunks="+Data.Meshes.Count+
                "; independentDefinition=True; nativeTemplateUnchanged=True; compiledScene=False; originalSourceHolesPreserved=True; fullNavigationAcceptance=False");
            return Definition;
        }

        public static void ConfigureSessionDependencies(DependencyContainerBase dependencies)
        {
            SessionDependencies = dependencies;
        }

        public static IEnumerator OnSceneLoadStart(LevelDefinition level, Scene scene, GameMode mode, ReplayMode replay, DependencyContainerBase dependencies)
        {
            if (level != null && level.SceneName == MapKey)
            {
                LoadingScene = scene;
                SessionDependencies = dependencies;
                Runtime.Log("HUB MAP LOAD START: logicalScene=" + scene.Name + "; sharedBootstrapSubscenes=" + string.Join(",", scene.Subscenes));
            }
            yield break;
        }

        // Hooked at the unique return of native LevelManager.ProcessTerrainInfo.
        // Native data is ignored only for our independently registered map key.
        public static DependencyContainerBase ReplaceTerrainDependencies(DependencyContainerBase original,LevelDefinition level)
        {
            if(level==null||level.SceneName!=MapKey)return original;
            try
            {
                if(DependenciesInjected)throw new InvalidOperationException("Hub terrain dependencies injected twice");
                if(Data==null||LoadingScene==null||SessionDependencies==null)throw new InvalidOperationException("Hub bootstrap capture missing");
                System.Diagnostics.Stopwatch bootstrapClock=System.Diagnostics.Stopwatch.StartNew();
                CaptureNativeSensorPalette();LogLoadPhase(bootstrapClock,"captureNativeSensorPalette");
                SuppressBootstrapWorld();LogLoadPhase(bootstrapClock,"suppressBootstrapWorld");
                ClearSourceMissionEntities();LogLoadPhase(bootstrapClock,"clearSourceMissionEntities");
                BuildFlatWorld();LogLoadPhase(bootstrapClock,"buildGeneratedWorldTotal");
                ReplaceRenderHeightMetadata();LogLoadPhase(bootstrapClock,"replaceRenderHeightMetadata");bootstrapClock.Stop();
                SimMapDependencies result=new SimMapDependencies();result.MinPathfindingBounds=P(Data.MinX,Data.MinZ);result.MaxPathfindingBounds=P(Data.MaxX,Data.MaxZ);
                result.HeightMapAttributes=Heights;result.Obstacles=BuildObstacles();
                result.RidgeLines=new SimRidgeLine[0];result.SerializedSimMapData=HubRegionNavigation.GetPreparedSimMapData();result.AmbientHeatPoints=0;DependenciesInjected=true;
                Runtime.Log("HUB DEPENDENCIES: min="+result.MinPathfindingBounds+"; max="+result.MaxPathfindingBounds+"; heightDataIndependent=True; obstacles="+ExpectedObstacleCount+"; totalMapObstacleRecordsIncludingLOFOnly="+result.Obstacles.Length+
                    "; nativeSourceNavigationDiscarded=True; nativeFreshNavigationBake=offlineSeparateProcess; preparedSerializedSimMapInjected=True; inGameWorkers=0; mainThreadBakeRequired=False; singleValuedHeight=True; multilevelTraversal=False; campaignOrEnemiesAdded=False");
                return new DependencyContainer<SimMapDependencies>(result);
            }
            catch(Exception error){Fault=error.ToString();Runtime.Log("HUB LOAD FAILED: "+error);throw;}
        }

        public static void MainTick()
        {
            if (Fault != null) throw new InvalidOperationException("Flat map: " + Fault);
            if (!DependenciesInjected) return;
            EnforceSourceSuppression(false);
            BindSensorEvents();
            // Event subscription is the primary path; polling the actual camera is
            // a fallback for a transition published before this generated world existed.
            if (ActiveUserCamera == null)
                foreach (UserCamera camera in UnityEngine.Object.FindObjectsOfType(typeof(UserCamera)))
                    if (camera != null && camera.gameObject.activeInHierarchy) { ActiveUserCamera = camera; break; }
            if (ActiveUserCamera != null && ActiveUserCamera.SensorsManagerActive != SensorActive)
                SetSensorState(ActiveUserCamera.SensorsManagerActive, "camera-state fallback");
            if (Ready) RecordFrameTiming();
            if (ColliderVerified) return;
            string detail;
            if (!VerifyFlatCollision(out detail))
            {
                if (++PhysicsWaitFrames > 180)
                {
                    Fault = "Flat collision validation timeout: " + detail;
                    throw new InvalidOperationException(Fault);
                }
                return;
            }
            ColliderVerified = true;
            Runtime.Log("HUB COLLISION PASS: " + detail + "; importedWalkColliderChunks="+WalkColliders.Count
                + "; cameraBounds=" + CameraConstraints.bounds + "; oldRendererCount=" + RemovedRendererCount
                + "; oldColliderCount=" + DisabledColliders.Count);
        }

        public static bool Prepare(object sim)
        {
            if(Fault!=null)throw new InvalidOperationException("Hub region: "+Fault);
            if(!DependenciesInjected||!ColliderVerified)return false;if(SimulationVerified)return true;
            object map=NativeRead.Get(sim,"Map");
            if((Vector2r)NativeRead.Get(map,"Min")!=P(Data.MinX,Data.MinZ)||(Vector2r)NativeRead.Get(map,"Max")!=P(Data.MaxX,Data.MaxZ))throw new InvalidOperationException("Hub sim bounds differ from import manifest");
            object height=NativeRead.Get(map,"HeightMap");byte[] raw=(byte[])NativeRead.Get(height,"mHeights");
            if(raw.Length!=Data.Q.Length)throw new InvalidOperationException("Hub sim height length mismatch");
            for(int i=0;i<raw.Length;i++)if(raw[i]!=Data.Q[i])throw new InvalidOperationException("Hub sim height differs at "+i);
            int samples=0;foreach(Vector2r point in ValidationPoints())
            {
                float expected=Data.Height(F(point.X),F(point.Y));float actual=F((Fixed64)NativeRead.Call(height,"GetWorldHeight",point));
                if(Math.Abs(expected-actual)>.025f)throw new InvalidOperationException("Hub native bilinear height mismatch: "+point+" expected="+expected+" actual="+actual);
                samples++;
            }
            if(!(bool)NativeRead.Call(map,"MapPositionValid",Start,UnitClass.Ground)||!(bool)NativeRead.Call(map,"MapPositionValid",Goal,UnitClass.Ground))throw new InvalidOperationException("Hub test start/goal is blocked by imported obstacles");
            if((bool)NativeRead.Call(map,"MapPositionValid",P(Data.MaxX+10,Data.MaxZ+10),UnitClass.Ground))throw new InvalidOperationException("Hub outside map is accepted");
            int count=0;foreach(object ignored in (IEnumerable)NativeRead.Get(NativeRead.Get(map,"mPathfindingObstaclesHash"),"AllObstacles"))count++;
            if(count!=ExpectedObstacleCount)throw new InvalidOperationException("Hub navigation obstacle count mismatch");
            IDictionary caches=(IDictionary)NativeRead.Get(map,"mBakedMeshes");if(caches.Count==0)throw new InvalidOperationException("Hub native navigation cache missing");
            HubRegionNavigation.VerifyLiveMap(map);
            SimulationVerified=true;
            Runtime.Log("HUB IMPORT READY: simHeightBytesVerified="+raw.Length+"; bilinearSamples="+samples+"; nativeObstacles="+count+"; freshNativeNavCaches="+caches.Count+
                "; start="+Start+"; goal="+Goal+"; startAndGoalPointValidity=True; entireRouteOrRegionProvenPassable=False; actualGameplayAcceptancePending=True");return true;
        }

        static void BuildSpawnInfo()
        {
            SpawnRoot=new GameObject(MapKey+"_IndependentSpawnInfo");SpawnRoot.SetActive(false);SceneSpawnInfo info=SpawnRoot.AddComponent<SceneSpawnInfo>();
            SceneSpawnInfo.TeamSpawnInfo[] teams=new SceneSpawnInfo.TeamSpawnInfo[2];SceneSpawnInfo.PlayerSpawnInfo[] ffa=new SceneSpawnInfo.PlayerSpawnInfo[6];
            for(int team=0;team<2;team++)
            {
                teams[team]=new SceneSpawnInfo.TeamSpawnInfo();SceneSpawnInfo.PlayerSpawnInfo[] players=new SceneSpawnInfo.PlayerSpawnInfo[3];
                for(int i=0;i<3;i++)
                {
                    GameObject point=new GameObject("HubSpawn_"+team+"_"+i);point.transform.parent=SpawnRoot.transform;
                    Vector2r spawn=Start;float sx=F(spawn.X),sz=F(spawn.Y);
                    point.transform.position=new Vector3(sx,Data.Height(sx,sz),sz);point.transform.rotation=Quaternion.Euler(0f,90f,0f);
                    players[i]=new SceneSpawnInfo.PlayerSpawnInfo();NativeRead.Set(players[i],"m_SpawnPoint",point.transform);players[i].m_DefaultCameraHeading=90f;ffa[team*3+i]=players[i];
                }
                NativeRead.Set(teams[team],"m_PlayerSpawnInfos",players);
            }
            NativeRead.Set(info,"m_TeamSpawnInfos",teams);NativeRead.Set(info,"m_FFASpawnInfos",ffa);SpawnRoot.SetActive(true);UnityEngine.Object.DontDestroyOnLoad(SpawnRoot);
        }

        static void SuppressBootstrapWorld()
        {
            int roots=0, oldSpawns=0, oldBounds=0;
            RefreshDynamicRootProtection();
            foreach(GameObject root in LoadingScene.SceneRootNodes)
            {
                if(root==null || IsProtectedSubtree(root.transform)) continue;
                roots++;
                SourceRoots.Add(root);
                PruneSourcePresentation(root);
                foreach(Collider collider in root.GetComponentsInChildren<Collider>(true))
                {
                    if (!IsSourceEnvironment(collider.transform)) continue;
                    collider.enabled=false; DisabledColliders.Add(collider);
                    if(collider.gameObject.tag=="CAM_CONSTRAINTS") collider.gameObject.tag="Untagged";
                }
                foreach(Terrain terrain in root.GetComponentsInChildren<Terrain>(true)) if (IsSourceEnvironment(terrain.transform)) terrain.enabled=false;
                foreach(AudioSource audio in root.GetComponentsInChildren<AudioSource>(true)) if (IsSourceEnvironment(audio.transform)) { audio.Stop(); audio.enabled=false; }
                foreach(ParticleSystem particles in root.GetComponentsInChildren<ParticleSystem>(true)) if (IsSourceEnvironment(particles.transform)) { particles.Stop(); particles.Clear(); }
                foreach(SceneSpawnInfo source in root.GetComponentsInChildren<SceneSpawnInfo>(true))
                {
                    if (!IsSourceEnvironment(source.transform)) continue;
                    // Remove only the scene-instance spawn component. It can
                    // share a root with required rendering/bootstrap services.
                    UnityEngine.Object.DestroyImmediate(source); oldSpawns++;
                }
                foreach(MapBounds bounds in root.GetComponentsInChildren<MapBounds>(true))
                {
                    if (!IsSourceEnvironment(bounds.transform)) continue;
                    NativeRead.Set(bounds,"SizeX",Data.WidthM); NativeRead.Set(bounds,"SizeY",Data.LengthM);
                    NativeRead.Set(bounds,"CenterX",(Data.MinX+Data.MaxX)*.5f); NativeRead.Set(bounds,"CenterY",(Data.MinZ+Data.MaxZ)*.5f);
                    NativeRead.Call(bounds,"SetSimCoordinates");
                    oldBounds++;
                }
            }
            if(roots==0 || RemovedRendererCount==0) throw new InvalidOperationException("Native bootstrap world roots could not be identified and suppressed");
            Runtime.Log("HUB MAP SOURCE WORLD SUPPRESSED: roots="+roots+"; renderers="+RemovedRendererCount
                +"; colliders="+DisabledColliders.Count+"; oldSpawnComponentsRemoved="+oldSpawns+"; oldMapBoundsOverridden="+oldBounds);
        }


        // The native sensor camera excludes Terrain and renders only Default,
        // Models and CollidingModels. It has a distinct environmental mesh set.
        // Clear those source scene-instance renderers as well as gameplay renderers;
        // never destroy the shared mesh/material assets or the bootstrap services.

        static void RefreshDynamicRootProtection()
        {
            if (DynamicRoot != null || LoadingScene == null) return;
            object value;
            if (NativeRead.TryGet(LoadingScene, "mDynamicRootNode", out value)) DynamicRoot = value as GameObject;
        }

        static bool IsProtectedSubtree(Transform value)
        {
            if (value == null) return true;
            if (WorldRoot != null && value.IsChildOf(WorldRoot.transform)) return true;
            if (SpawnRoot != null && value.IsChildOf(SpawnRoot.transform)) return true;
            if (DynamicRoot != null && value.IsChildOf(DynamicRoot.transform)) return true;
            for (Transform current = value; current != null; current = current.parent)
                if (current.name == "~Dynamic") return true;
            return value.GetComponentInParent<UnitView>() != null;
        }

        static bool IsSourceEnvironment(Transform value)
        {
            if (IsProtectedSubtree(value)) return false;
            foreach (GameObject root in SourceRoots)
                if (root != null && value.IsChildOf(root.transform)) return true;
            return false;
        }

        static void PruneSourcePresentation(GameObject root)
        {
            if (root == null) return;
            foreach (SensorsViewObjectToggle toggle in root.GetComponentsInChildren<SensorsViewObjectToggle>(true))
            {
                if (!IsSourceEnvironment(toggle.transform)) continue;
                GameObject[] sensors = NativeRead.Get(toggle, "m_OnInSensors") as GameObject[];
                GameObject[] gameplay = NativeRead.Get(toggle, "m_OnInGameplay") as GameObject[];
                int count = (sensors == null ? 0 : sensors.Length) + (gameplay == null ? 0 : gameplay.Length);
                if (toggle.enabled || count != 0)
                {
                    Runtime.Log("HUB SOURCE SENSOR TOGGLE REMOVED: object=" + ObjectPath(toggle.transform)
                        + "; sensorTargets=" + (sensors == null ? 0 : sensors.Length)
                        + "; gameplayTargets=" + (gameplay == null ? 0 : gameplay.Length));
                    // Clearing the arrays also neutralizes an already queued callback.
                    NativeRead.Set(toggle, "m_OnInSensors", new GameObject[0]);
                    NativeRead.Set(toggle, "m_OnInGameplay", new GameObject[0]);
                    toggle.enabled = false; DisabledSensorControllerCount++;
                }
            }
            foreach (SensorsViewSwapBase swap in root.GetComponentsInChildren<SensorsViewSwapBase>(true))
            {
                if (!IsSourceEnvironment(swap.transform)) continue;
                object value;
                if (NativeRead.TryGet(swap, "mSwapData", out value) && value is Array)
                {
                    Array data = (Array)value;
                    NativeRead.Set(swap, "mSwapData", Array.CreateInstance(data.GetType().GetElementType(), 0));
                }
                if (swap.enabled) { swap.enabled = false; DisabledSensorControllerCount++; }
            }
            foreach (LODGroup lod in root.GetComponentsInChildren<LODGroup>(true)) if (IsSourceEnvironment(lod.transform)) lod.enabled = false;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !IsSourceEnvironment(renderer.transform)) continue;
                renderer.enabled = false;
                // This is a component on an already loaded bootstrap scene instance,
                // not the source prefab, mesh asset, scene on disk, or dynamic units.
                UnityEngine.Object.DestroyImmediate(renderer);
                RemovedRendererCount++;
            }
        }

        static void EnforceSourceSuppression(bool force)
        {
            // Loaded source geometry is static and its sensor callbacks are already
            // neutralized. Recheck on transitions plus a slow real-time guard,
            // rather than recursively walking inactive bootstrap trees each second.
            if (!force && Time.realtimeSinceStartup < NextSourceSweepAt) return;
            System.Diagnostics.Stopwatch sweepClock = System.Diagnostics.Stopwatch.StartNew();
            RefreshDynamicRootProtection();
            foreach (Collider collider in DisabledColliders)
                if (collider != null && collider.enabled && IsSourceEnvironment(collider.transform)) collider.enabled = false;
            int before = RemovedRendererCount;
            foreach (GameObject root in SourceRoots) if (root != null) PruneSourcePresentation(root);
            NextSourceSweepAt = Time.realtimeSinceStartup + SourceSweepInterval;
            int remaining = 0, activeControllers = 0, activeSourceTerrains = 0, protectedRenderers = 0;
            foreach (GameObject root in SourceRoots)
                if (root != null)
                {
                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                        if (IsSourceEnvironment(renderer.transform)) remaining++; else protectedRenderers++;
                    foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
                    {
                        if (!IsSourceEnvironment(terrain.transform)) continue;
                        if (terrain.enabled) terrain.enabled = false;
                        if (terrain.enabled) activeSourceTerrains++;
                    }
                    foreach (SensorsViewObjectToggle toggle in root.GetComponentsInChildren<SensorsViewObjectToggle>(true))
                        if (IsSourceEnvironment(toggle.transform) && toggle.enabled) activeControllers++;
                    foreach (SensorsViewSwapBase swap in root.GetComponentsInChildren<SensorsViewSwapBase>(true))
                        if (IsSourceEnvironment(swap.transform) && swap.enabled) activeControllers++;
                }
            if (remaining != 0 || activeControllers != 0 || activeSourceTerrains != 0)
                throw new InvalidOperationException("Original map presentation remains: renderers=" + remaining + "; activeSensorControllers=" + activeControllers + "; activeTerrains=" + activeSourceTerrains);
            if (force || RemovedRendererCount != before || !SourcePresentationVerified)
                Runtime.Log("HUB SOURCE PRESENTATION VERIFIED: remainingSourceRenderers=" + remaining
                    + "; activeSourceSensorControllers=" + activeControllers + "; activeSourceTerrains=" + activeSourceTerrains
                    + "; removedRenderers=" + RemovedRendererCount + "; neutralizedSensorControllers=" + DisabledSensorControllerCount
                    + "; currentSensors=" + SensorActive + "; protectedDynamicOrGeneratedRenderers=" + protectedRenderers
                    + "; dynamicRootProtected=" + (DynamicRoot != null) + "; sceneAssetsOnDiskUnchanged=True; generatedAndDynamicWorldPreserved=True");
            SourcePresentationVerified = true;
            sweepClock.Stop(); LastSourceSweepMs = (float)sweepClock.Elapsed.TotalMilliseconds;
            PeakSourceSweepMs = Mathf.Max(PeakSourceSweepMs, LastSourceSweepMs);
        }

        static void BuildFlatSensorPresentation()
        {
            int layer=LayerMask.NameToLayer("Default");Material surface=NewSensorMaterial("Ground",SensorColors[0]);
            foreach(MeshCollider collider in WalkColliders)
            {
                GameObject item=new GameObject("HubSensorSurface");item.transform.parent=WorldRoot.transform;item.layer=layer;
                item.AddComponent<MeshFilter>().sharedMesh=collider.sharedMesh;Renderer r=item.AddComponent<MeshRenderer>();r.sharedMaterial=surface;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;SensorRenderers.Add(r);
            }
            BuildContours();SetSensorState(false,"generated");BindSensorEvents();
            Runtime.Log("HUB SENSOR PREPARED: heightDerivedSurface=True; sourceHolesPreserved=True; nativeSensorMaskUnchanged=True; nativeFOWUnchanged=True; nativeBootstrapSensorGeometryRemoved=True");
        }

        static void BindSensorEvents()
        {
            if (SensorEventsBound || ShipbreakersMain.PresentationEventSystem == null) return;
            ShipbreakersMain.PresentationEventSystem.AddHandler<SensorsManagerEvent>(OnFlatSensorsTransition);
            SensorEventsBound = true;
        }

        static void OnFlatSensorsTransition(SensorsManagerEvent value)
        {
            if (!DependenciesInjected && WorldRoot == null) return;
            EnforceSourceSuppression(true);
            SetSensorState(value.Active, "native SensorsManagerEvent");
        }

        static void SetSensorState(bool active,string reason)
        {
            if(SensorStateApplied&&SensorActive==active)return;
            SensorStateApplied=true;ResetFrameTiming();
            SensorActive=active;foreach(ImportedSensorBinding binding in ImportedSensorBindings)binding.Apply(active);
            foreach(Renderer r in SensorRenderers)if(r!=null)r.enabled=active;
            Runtime.Log("HUB SENSOR STATE: active="+active+"; reason="+reason+"; sharedImportedRenderers="+ImportedSensorBindings.Count+"; independentTerrainAndContourRenderers="+SensorRenderers.Count+
                "; semanticSolidGeometry=True; materialAndLayerRestoredOnExit=True; normalMaterialContentsChanged=False; sharedSimulationHeight=True");
        }

        static void ResetFrameTiming()
        {
            NextFrameReportAt=FrameIntervalTotalMs=FrameIntervalMaxMs=0f;
            FrameSamples=FramesAbove33Ms=FramesAbove50Ms=0;LastProfileFrame=-1;
        }
        static void RecordFrameTiming()
        {
            if(LastProfileFrame==Time.frameCount)return;LastProfileFrame=Time.frameCount;
            float now=Time.realtimeSinceStartup;
            if(NextFrameReportAt==0f){NextFrameReportAt=now+15f;return;}
            float milliseconds=Time.unscaledDeltaTime*1000f;
            if(milliseconds>0f&&!float.IsNaN(milliseconds)&&!float.IsInfinity(milliseconds))
            {
                FrameSamples++;FrameIntervalTotalMs+=milliseconds;FrameIntervalMaxMs=Mathf.Max(FrameIntervalMaxMs,milliseconds);
                if(milliseconds>33.333f)FramesAbove33Ms++;if(milliseconds>50f)FramesAbove50Ms++;
            }
            if(now<NextFrameReportAt)return;
            Runtime.Log("HUB FRAME INTERVAL: sensors="+SensorActive+"; samples="+FrameSamples+"; averageMs="+(FrameSamples==0?0f:FrameIntervalTotalMs/FrameSamples).ToString("F2",CultureInfo.InvariantCulture)
                +"; approximateFPS="+(FrameIntervalTotalMs<=0f?0f:1000f*FrameSamples/FrameIntervalTotalMs).ToString("F1",CultureInfo.InvariantCulture)+"; maximumMs="+FrameIntervalMaxMs.ToString("F2",CultureInfo.InvariantCulture)
                +"; framesAbove33Ms="+FramesAbove33Ms+"; framesAbove50Ms="+FramesAbove50Ms+"; lastSourceGuardMs="+LastSourceSweepMs.ToString("F2",CultureInfo.InvariantCulture)+"; peakSourceGuardMs="+PeakSourceSweepMs.ToString("F2",CultureInfo.InvariantCulture)
                +"; managedHeapBytes="+GC.GetTotalMemory(false)+"; intervalIncludesNativeGameAndRendering=True; cpuGpuBottleneckNotSeparated=True; normalAndSensorWindowsSeparate=True");
            ResetFrameTiming();NextFrameReportAt=now+15f;
        }

        static string ObjectPath(Transform value)
        {
            string path = value.name;
            for (Transform parent = value.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
            return path;
        }

        static void ClearSourceMissionEntities()
        {
            MissionDependencies original;
            if(!SessionDependencies.Get<MissionDependencies>(out original) || original==null)
                throw new InvalidOperationException("Original mission dependencies were not present at terrain hook");
            int descriptors=0, groups=0;
            IEnumerable oldDescriptors=NativeRead.Get(original,"EntityDescriptors") as IEnumerable;
            IEnumerable oldGroups=NativeRead.Get(original,"EntityGroupDescriptors") as IEnumerable;
            if(oldDescriptors!=null) foreach(object item in oldDescriptors) descriptors++;
            if(oldGroups!=null) foreach(object item in oldGroups) groups++;
            MissionDependencies clean=new MissionDependencies(new SceneEntityGroupDescriptor[0],new SceneEntityDescriptor[0],
                original.MissionCommanders,new string[0],0);
            SessionDependencies.Combine(new DependencyContainer<MissionDependencies>(clean));
            Runtime.Log("HUB MAP ORIGINAL ENTITIES DISCARDED: descriptors="+descriptors+"; groups="+groups+"; newDescriptors=0; newGroups=0; wreckArtifacts=0");
        }

        static void LogLoadPhase(System.Diagnostics.Stopwatch timer,string phase)
        {
            timer.Stop();
            Runtime.Log("HUB LOAD PHASE: phase="+phase+"; elapsedMs="+timer.Elapsed.TotalMilliseconds.ToString("F2",CultureInfo.InvariantCulture)
                +"; managedHeapBytes="+GC.GetTotalMemory(false)+"; forcedGC=False; cpuGpuFrameBreakdown=False");
            timer.Reset();timer.Start();
        }

        static void BuildFlatWorld()
        {
            System.Diagnostics.Stopwatch phaseClock=System.Diagnostics.Stopwatch.StartNew();
            System.Diagnostics.Stopwatch uploadClock=new System.Diagnostics.Stopwatch();
            WorldRoot=new GameObject(MapKey+"_GeneratedWorld");GameObject dynamicRoot=NativeRead.Get(LoadingScene,"mDynamicRootNode") as GameObject;if(dynamicRoot!=null)WorldRoot.transform.parent=dynamicRoot.transform;
            Matrix4x4 rootMatrix=WorldRoot.transform.localToWorldMatrix;for(int r=0;r<4;r++)for(int c=0;c<4;c++)if(Math.Abs(rootMatrix[r,c]-(r==c?1f:0f))>.0001f)throw new InvalidOperationException("Hub dynamic bootstrap root is not world identity");
            int terrain=LayerMask.NameToLayer("Terrain");if(terrain<0)throw new InvalidOperationException("Native Terrain layer missing");BuildWalkCollision(terrain);LogLoadPhase(phaseClock,"buildWalkCollision");
            Shader shader=Shader.Find("Standard")??Shader.Find("Diffuse");if(shader==null)throw new InvalidOperationException("Hub gameplay shader unavailable");
            for(int kind=1;kind<SemanticSensorMaterials.Length;kind++)SemanticSensorMaterials[kind]=NewSensorMaterial("Kind"+kind,SensorColors[kind]);
            Mesh[] sharedMeshes=new Mesh[Data.Meshes.Count];Material[] sharedMaterials=new Material[Data.Meshes.Count];
            Dictionary<string,Material> materialPool=new Dictionary<string,Material>(StringComparer.Ordinal);
            long importedSourceVertexBytes=0L,importedSourceIndexBytes=0L;
            using(BinaryReader reader=new BinaryReader(File.OpenRead(Data.Path)))for(int recordIndex=0;recordIndex<Data.Meshes.Count;recordIndex++)
            {
                MeshRecord record=Data.Meshes[recordIndex];reader.BaseStream.Position=record.Offset;Vector3[] vertices=new Vector3[record.V],normals=new Vector3[record.V];Vector2[] uv=new Vector2[record.V];
                for(int i=0;i<record.V;i++){vertices[i]=ReadV3(reader);normals[i]=ReadV3(reader);uv[i]=new Vector2(Finite(reader.ReadSingle()),Finite(reader.ReadSingle()));}
                int[] indices=new int[record.I];for(int i=0;i<indices.Length;i++){indices[i]=reader.ReadInt32();if(indices[i]<0||indices[i]>=record.V)throw new InvalidDataException("Hub mesh index outside vertices");}
                Mesh mesh=new Mesh();mesh.name="Hub_"+record.Name;mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.triangles=indices;mesh.RecalculateBounds();
                // Imported source meshes are immutable render assets. Sensor
                // transitions only replace renderer materials/layers, and the
                // V05 walk colliders use separately generated HubBilinearWalk meshes.
                // Release this mesh's CPU copy after its final attribute write;
                // bounds remain available for native culling and shadow rendering.
                uploadClock.Start();mesh.UploadMeshData(true);uploadClock.Stop();
                if(mesh.isReadable)throw new InvalidOperationException("Hub immutable render mesh remained CPU-readable: "+mesh.name);
                importedSourceVertexBytes+=(long)record.V*32L;importedSourceIndexBytes+=(long)record.I*4L;
                CreatedAssets.Add(mesh);sharedMeshes[recordIndex]=mesh;
                // Identical proxy surfaces share one material; textured terrain
                // remains separated by its exact source texture and tint.
                string materialKey=record.Texture+"|"+record.Color.r.ToString("R",CultureInfo.InvariantCulture)+"|"+record.Color.g.ToString("R",CultureInfo.InvariantCulture)+"|"+record.Color.b.ToString("R",CultureInfo.InvariantCulture)+"|"+record.Color.a.ToString("R",CultureInfo.InvariantCulture);
                Material material;
                if(!materialPool.TryGetValue(materialKey,out material))
                {
                    material=new Material(shader);material.name="HubSharedProxy_"+materialPool.Count;material.color=record.Color;
                    if(record.Texture.Length!=0){Texture2D texture;string file=SafeAssetPath(record.Texture);if(!Textures.TryGetValue(file,out texture)){texture=new Texture2D(2,2,TextureFormat.RGBA32,false);if(!texture.LoadImage(File.ReadAllBytes(file)))throw new InvalidDataException("Hub PNG decode failed: "+file);texture.name=record.Texture;Textures.Add(file,texture);CreatedAssets.Add(texture);}material.mainTexture=texture;}
                    if(material.HasProperty("_Glossiness"))material.SetFloat("_Glossiness",.15f);
                    CreatedAssets.Add(material);materialPool.Add(materialKey,material);
                }
                sharedMaterials[recordIndex]=material;
            }
            LogLoadPhase(phaseClock,"decodeMeshesCreateMaterialsAndUpload");
            Runtime.Log("HUB RENDER RESOURCES: meshCount="+sharedMeshes.Length+"; uniqueNormalMaterials="+materialPool.Count+"; previousPerMeshMaterials="+sharedMeshes.Length+"; normalViewGeometryAndTexturesUnchanged=True; sensorShadowsDisabled=True; sourceGuardIntervalSeconds="+SourceSweepInterval+"; nativeQualityPresetUnchanged=True; performanceRequiresRuntimeComparison=True");
            Runtime.Log("HUB IMMUTABLE RENDER MESHES: cpuReadable=False; meshCount="+sharedMeshes.Length
                +"; importedSourceVertexBytes="+importedSourceVertexBytes+"; importedSourceInt32IndexBytes="+importedSourceIndexBytes
                +"; uploadElapsedMs="+uploadClock.Elapsed.TotalMilliseconds.ToString("F2",CultureInfo.InvariantCulture)
                +"; sourcePayloadIsNotMeasuredProcessMemory=True; walkColliderMeshesUnchanged=True; frameRateBenefitUnmeasured=True");
            int[] kinds=new int[6];
            foreach(InstanceRecord instance in Data.Instances)
            {
                MeshRecord record=Data.Meshes[instance.Index];Mesh mesh=sharedMeshes[instance.Index];GameObject item=new GameObject(mesh.name);item.transform.parent=WorldRoot.transform;item.layer=terrain;ApplyInstance(item.transform,instance);item.AddComponent<MeshFilter>().sharedMesh=mesh;
                Renderer renderer=item.AddComponent<MeshRenderer>();renderer.sharedMaterial=sharedMaterials[instance.Index];GameplayRenderers.Add(renderer);
                ImportedSensorBindings.Add(new ImportedSensorBinding{Renderer=renderer,NormalMaterial=renderer.sharedMaterial,NormalLayer=item.layer,NormalEnabled=renderer.enabled,NormalShadowCasting=renderer.shadowCastingMode,NormalReceiveShadows=renderer.receiveShadows,Kind=record.Kind});kinds[record.Kind]++;
            }
            LogLoadPhase(phaseClock,"createImportedRenderers");
            Runtime.Log("HUB SENSOR SEMANTIC GEOMETRY: terrain="+kinds[0]+"; architecture="+kinds[1]+"; roadAndFloor="+kinds[2]+"; naturalMacroRock="+kinds[3]+"; originium="+kinds[4]+"; industrialLandmark="+kinds[5]+
                "; sourceLowLODGeometryReused=True; perItemSensorRendererCopies=0; aabbBoxesAsMainVisual=False; normalViewMaterialsPreserved=True; actualDrawCallsAndFrameRateUnverified=True");
            BuildFlatSensorPresentation();LogLoadPhase(phaseClock,"buildSensorSurfaceAndContours");GameObject constraints=new GameObject("HubMapBounds");constraints.transform.parent=WorldRoot.transform;constraints.tag="CAM_CONSTRAINTS";constraints.layer=LayerMask.NameToLayer("Ignore Raycast");
            constraints.transform.position=new Vector3((Data.MinX+Data.MaxX)*.5f,Data.Range*.5f,(Data.MinZ+Data.MaxZ)*.5f);
            CameraConstraints=constraints.AddComponent<BoxCollider>();CameraConstraints.size=new Vector3(Data.WidthM,Math.Max(2000f,Data.Range+1000f),Data.LengthM);CameraConstraints.isTrigger=true;
            MapBounds bounds=constraints.AddComponent<MapBounds>();NativeRead.Set(bounds,"SizeX",Data.WidthM);NativeRead.Set(bounds,"SizeY",Data.LengthM);
            NativeRead.Set(bounds,"CenterX",(Data.MinX+Data.MaxX)*.5f);NativeRead.Set(bounds,"CenterY",(Data.MinZ+Data.MaxZ)*.5f);NativeRead.Call(bounds,"SetSimCoordinates");
            int spawns=0;foreach(SceneSpawnInfo spawn in UnityEngine.Object.FindObjectsOfType(typeof(SceneSpawnInfo))){if(spawn.gameObject!=SpawnRoot)throw new InvalidOperationException("Native bootstrap spawn remains active");spawns++;}
            if(spawns!=1)throw new InvalidOperationException("Hub spawn component not uniquely active");
            LogLoadPhase(phaseClock,"cameraConstraintsAndSpawnValidation");phaseClock.Stop();
            Runtime.Log("HUB WORLD BUILT: uniqueSourceMeshChunks="+Data.Meshes.Count+"; sharedMeshInstances="+Data.Instances.Count+"; walkColliders="+WalkColliders.Count+"; collisionBilinearToleranceMeters=0.125; sourceRenderHeightByteErrorBound="+(Data.Range/510f)+
                "; originalTRSValidated=True; shearSilentlyDiscarded=False; meshColliderDrivesNativeSimulation=False; originalSourceGeometryUsed=True; fullNavigationProven=False");
        }

        static void ReplaceRenderHeightMetadata()
        {
            int count=0;Texture2D occlusion=new Texture2D(Data.W,Data.L,TextureFormat.RGBA32,false);occlusion.name=MapKey+"_HeightOcclusion";occlusion.wrapMode=TextureWrapMode.Clamp;
            Color32[] pixels=new Color32[Data.Q.Length];for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32(Data.Q[i],Data.Q[i],Data.Q[i],255);occlusion.SetPixels32(pixels);occlusion.Apply(false,false);CreatedAssets.Add(occlusion);
            HubRegionHeightAsset height=RhodesAssetClone59.CreateEmpty<HubRegionHeightAsset>();height.name=MapKey+"_IndependentAuxHeight";height.Attributes=Heights;CreatedAssets.Add(height);
            foreach(GameObject root in LoadingScene.SceneRootNodes)if(root!=null)foreach(SetRenderSettings settings in root.GetComponentsInChildren<SetRenderSettings>(true))
            {
                ParticleOcclusionMapAttributesAsset original=settings.ParticleOcclusionSettings;if(original==null)continue;
                ParticleOcclusionMapAttributesAsset copy=UnityEngine.Object.Instantiate(original) as ParticleOcclusionMapAttributesAsset;object data=CloneMethod.Invoke(original.Data,null);
                NativeRead.Set(data,"mOcclusionMap",occlusion);NativeRead.Set(data,"mMapBottomLeft",new Vector3(Data.MinX,0f,Data.MinZ));NativeRead.Set(data,"mMapSize",new Vector3(Data.WidthM,Data.Range,Data.LengthM));
                NativeRead.Set(data,"mHeightMap",height);NativeRead.Set(copy,"m_Data",data);AuxiliaryOriginals.Add(settings,original);settings.ParticleOcclusionSettings=copy;NativeRead.Call(settings,"UpdateParticleOcclusionShaderConstants");CreatedAssets.Add(copy);count++;
            }
            if(count==0)throw new InvalidOperationException("Native render metadata service missing");
            Runtime.Log("HUB AUXILIARY MAPS: settings="+count+"; independentHeightMapAsset=True; sharedQuantizedHeightBytes=True; sourceTextAssetRetained=False; proceduralOcclusionApproximation=True; actualParticlesNeedVisualAcceptance=True");
        }

        static bool VerifyFlatCollision(out string detail)
        {
            if(WalkColliders.Count==0){detail="No imported walk collision";return false;}int hits=0;float maxError=0f;
            foreach(Vector2r point in ValidationPoints())
            {
                RaycastHit hit;float expected=Data.Height(F(point.X),F(point.Y));
                if(!Physics.Raycast(new Vector3(F(point.X),Data.Range+1000f,F(point.Y)),Vector3.down,out hit,Data.Range+2000f,1<<LayerMask.NameToLayer("Terrain"))||!WalkColliders.Contains(hit.collider as MeshCollider))
                {detail="Hub imported collision missing at "+point;return false;}
                float error=Math.Abs(hit.point.y-expected);maxError=Math.Max(maxError,error);
                if(error>WalkHeightTolerance+.025f){detail="Hub collider/height mismatch "+point+" error="+error;return false;}hits++;
            }
            detail="importedWalkRaycasts="+hits+"; maxTriangleVsNativeBilinearError="+maxError+"; previousCoarsePatchBound="+Data.TriangleErrorBound+"; enforcedAdaptiveErrorMetres="+WalkHeightTolerance+"; cellInteriorSamplesIncluded=True; sourceVoidsNotFilled=True";return hits>=2;
        }

        public static void Reset()
        {
            foreach(ImportedSensorBinding binding in ImportedSensorBindings)binding.Apply(false);ImportedSensorBindings.Clear();
            for(int kind=0;kind<SemanticSensorMaterials.Length;kind++)SemanticSensorMaterials[kind]=null;
            if(Manager!=null)
            {
                Manager.SceneLoadStart-=OnSceneLoadStart;
                if(OriginalEntries!=null) NativeRead.Set(Manager,"m_LevelEntriesMP",OriginalEntries);
            }
            foreach(KeyValuePair<SetRenderSettings,ParticleOcclusionMapAttributesAsset> pair in AuxiliaryOriginals)if(pair.Key!=null)pair.Key.ParticleOcclusionSettings=pair.Value;
            AuxiliaryOriginals.Clear();WalkColliders.Clear();GameplayRenderers.Clear();SensorRenderers.Clear();Textures.Clear();Data=null;Heights=null;ExpectedObstacleCount=0;
            if(WorldRoot!=null) UnityEngine.Object.Destroy(WorldRoot);
            if(SpawnRoot!=null) UnityEngine.Object.Destroy(SpawnRoot);
            foreach(UnityEngine.Object asset in CreatedAssets) if(asset!=null) UnityEngine.Object.Destroy(asset);
            if (SensorEventsBound && ShipbreakersMain.PresentationEventSystem != null)
                ShipbreakersMain.PresentationEventSystem.RemoveHandler<SensorsManagerEvent>(OnFlatSensorsTransition);
            SensorEventsBound=false; SensorActive=false; SensorStateApplied=false; SourcePresentationVerified=false; ActiveUserCamera=null;
            CreatedAssets.Clear(); SourceRoots.Clear(); DisabledColliders.Clear();
            RemovedRendererCount=DisabledSensorControllerCount=0; NextSourceSweepAt=0f; LastSourceSweepMs=PeakSourceSweepMs=0f;
            ResetFrameTiming();
            Definition=null; Manager=null; OriginalEntries=null; SpawnRoot=WorldRoot=DynamicRoot=null; LoadingScene=null; SessionDependencies=null;
            CameraConstraints=null; HeightValidationPoints=null;
            DependenciesInjected=ColliderVerified=SimulationVerified=false; Fault=null; PhysicsWaitFrames=0;

        }

        public static float SampleLift(Vector2r position){return 0f;}
        public static float SampleOriginalHeight(Vector2r position){return Data==null?0f:Data.Height(F(position.X),F(position.Y));}

        static Vector2r P(float x,float z) { return new Vector2r(Fixed64.UnsafeFromFloat(x),Fixed64.UnsafeFromFloat(z)); }
        static float F(Fixed64 value) { return Fixed64.UnsafeFloatValue(value); }

        sealed class HubHeightAttributes : HeightMapAttributes
        {
            readonly HubData Value;public HubHeightAttributes(HubData value){Value=value;}
            public byte[] HeightMap{get{return Value.Q;}}public int GridWidth{get{return Value.W;}}public int GridLength{get{return Value.L;}}
            public Vector2r MinBounds{get{return P(Value.MinX,Value.MinZ);}}public Vector2r MaxBounds{get{return P(Value.MaxX,Value.MaxZ);}}
            public Fixed64 MinHeight{get{return Fixed64.Zero;}}public Fixed64 MaxHeight{get{return Fixed64.UnsafeFromFloat(Value.Range);}}
        }

        static float Finite(float value)
        {if(float.IsNaN(value)||float.IsInfinity(value))throw new InvalidDataException("Hub contains non-finite geometry");return value;}
        static Vector3 ReadV3(BinaryReader reader){return new Vector3(Finite(reader.ReadSingle()),Finite(reader.ReadSingle()),Finite(reader.ReadSingle()));}
        static string ReadText(BinaryReader reader)
        {int count=reader.ReadInt32();if(count<0||count>4096)throw new InvalidDataException("Hub string size invalid");byte[] b=reader.ReadBytes(count);if(b.Length!=count)throw new EndOfStreamException();return Encoding.UTF8.GetString(b);}
        static string SafeAssetPath(string relative)
        {
            if(Path.IsPathRooted(relative))throw new InvalidDataException("Hub texture must be relative");string root=Path.GetFullPath(AssetRoot).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            string path=Path.GetFullPath(Path.Combine(root,relative));if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!File.Exists(path))throw new InvalidDataException("Hub texture outside asset root or missing: "+relative);return path;
        }
        static Material NewSensorMaterial(string name,Color color)
        {
            Shader shader=Shader.Find("Unlit/Color")??Shader.Find("Sprites/Default")??Shader.Find("Diffuse");if(shader==null)throw new InvalidOperationException("Hub sensor shader unavailable");
            Material material=new Material(shader);material.name="HubSensor_"+name;material.color=color;CreatedAssets.Add(material);return material;
        }
        static MapObstacle[] BuildObstacles()
        {
            List<MapObstacle> result=new List<MapObstacle>();int lofOnly=0,suppliedNavigation=0;ExpectedObstacleCount=Data.VoidRectangles.Count;
            foreach(ObstacleRecord item in Data.Obstacles)
            {
                bool only=(item.Flags&4)!=0;UnitClass restricted=only?(UnitClass)0:UnitClass.Ground|UnitClass.Hover|UnitClass.Carrier;
                result.Add(new MapObstacle(only||(item.Flags&1)!=0,(item.Flags&2)!=0,restricted,ConvexPolygon.FromPointCloud(new List<Vector2r>(item.Points))));
                if(only)lofOnly++;else{suppliedNavigation++;ExpectedObstacleCount++;}
            }
            foreach(MaskRect rect in Data.VoidRectangles)
            {
                float x0=Data.MinX+rect.X0*Data.DX,x1=Data.MinX+rect.X1*Data.DX,z0=Data.MinZ+rect.Z0*Data.DZ,z1=Data.MinZ+rect.Z1*Data.DZ;
                result.Add(new MapObstacle(false,false,UnitClass.Ground|UnitClass.Hover|UnitClass.Carrier,ConvexPolygon.FromPointCloud(new List<Vector2r>{P(x0,z0),P(x1,z0),P(x1,z1),P(x0,z1)})));
            }
            Runtime.Log("HUB OBSTACLES: supplied="+Data.Obstacles.Count+"; suppliedNavigation="+suppliedNavigation+"; suppliedLOFOnly="+lofOnly+"; conservativeNavigationRectangles="+Data.VoidRectangles.Count+
                "; expectedPathfindingShapes="+ExpectedObstacleCount+"; navigationMaskSource="+Data.NavigationMaskSource+"; voidMaskPaddingCells=1; polygonsNotUnityColliders=True; runtimeSlopeInference=False; offlineNavOnlyBarriersSupported=True");
            return result.ToArray();
        }
        const float WalkHeightTolerance = .1f;
        static List<Vector2r> HeightValidationPoints;
        static List<Vector2r> ValidationPoints()
        {
            if(HeightValidationPoints!=null)return HeightValidationPoints;
            List<Vector2r> result=new List<Vector2r>();result.Add(Start);if(Goal!=Start)result.Add(Goal);
            for(int j=1;j<4;j++)for(int i=1;i<4;i++)
            {
                int x=Math.Min(Data.W-2,i*Data.W/4),z=Math.Min(Data.L-2,j*Data.L/4);
                if(!Data.Blocked[z*Data.W+x])result.Add(P(Data.MinX+(x+.5f)*Data.DX,Data.MinZ+(z+.5f)*Data.DZ));
                if(ValidPatch(x,z))
                {
                    result.Add(P(Data.MinX+(x+1f)*Data.DX,Data.MinZ+(z+1f)*Data.DZ));
                    result.Add(P(Data.MinX+(x+.75f)*Data.DX,Data.MinZ+(z+1.25f)*Data.DZ));
                }
            }
            // Include a deliberately difficult patch interior. Grid centers alone
            // cannot expose the old triangular-vs-bilinear sampling mismatch.
            float maximum=-1f;int worstX=0,worstZ=0;
            for(int z=0;z<Data.L-1;z++)for(int x=0;x<Data.W-1;x++)if(ValidPatch(x,z))
            {
                int k=z*Data.W+x;float error=Math.Abs(Data.Decode(k)-Data.Decode(k+1)-Data.Decode(k+Data.W)+Data.Decode(k+Data.W+1))*.25f;
                if(error>maximum){maximum=error;worstX=x;worstZ=z;}
            }
            if(maximum>=0f)
            {
                result.Add(P(Data.MinX+(worstX+1f)*Data.DX,Data.MinZ+(worstZ+1f)*Data.DZ));
                result.Add(P(Data.MinX+(worstX+.75f)*Data.DX,Data.MinZ+(worstZ+.75f)*Data.DZ));
                result.Add(P(Data.MinX+(worstX+1.25f)*Data.DX,Data.MinZ+(worstZ+.75f)*Data.DZ));
                int refinedN=Math.Max(1,(int)Math.Ceiling(Math.Sqrt(maximum/WalkHeightTolerance)));
                float subcellFraction=(refinedN/2+.5f)/refinedN;
                result.Add(P(Data.MinX+(worstX+.5f+subcellFraction)*Data.DX,Data.MinZ+(worstZ+.5f+subcellFraction)*Data.DZ));
            }
            HeightValidationPoints=result;return result;
        }
        static bool ValidPatch(int x,int z)
        {
            int k=z*Data.W+x;
            return Data.Valid[k]!=0&&Data.Valid[k+1]!=0&&Data.Valid[k+Data.W]!=0&&Data.Valid[k+Data.W+1]!=0;
        }
        sealed class WalkMeshChunk
        {
            public readonly List<Vector3> Vertices=new List<Vector3>();
            public readonly List<int> Triangles=new List<int>();
            readonly Dictionary<Vector3,int> Indices=new Dictionary<Vector3,int>();
            public int Vertex(Vector3 value)
            {
                int index;if(Indices.TryGetValue(value,out index))return index;
                index=Vertices.Count;Vertices.Add(value);Indices.Add(value,index);return index;
            }
            public void Quad(int a,int b,int c,int d)
            {Triangles.Add(a);Triangles.Add(d);Triangles.Add(b);Triangles.Add(a);Triangles.Add(c);Triangles.Add(d);}
        }
        static Vector3 WalkVertex(float x,float z){return new Vector3(x,Data.Height(x,z),z);}
        static void CommitWalkChunk(WalkMeshChunk chunk,int layer)
        {
            if(chunk.Triangles.Count==0)return;
            if(chunk.Vertices.Count>=60000)throw new InvalidDataException("Adaptive walk mesh exceeds old Unity vertex contract");
            Mesh mesh=new Mesh();mesh.name="HubBilinearWalk_"+WalkColliders.Count;mesh.vertices=chunk.Vertices.ToArray();
            Vector2[] uv=new Vector2[chunk.Vertices.Count];for(int i=0;i<uv.Length;i++)uv[i]=new Vector2((chunk.Vertices[i].x-Data.MinX)/Data.WidthM,(chunk.Vertices[i].z-Data.MinZ)/Data.LengthM);
            mesh.uv=uv;mesh.triangles=chunk.Triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();CreatedAssets.Add(mesh);
            GameObject floor=new GameObject(mesh.name);floor.transform.parent=WorldRoot.transform;floor.layer=layer;
            MeshCollider collider=floor.AddComponent<MeshCollider>();collider.sharedMesh=mesh;WalkColliders.Add(collider);
        }
        static void BuildWalkCollision(int layer)
        {
            int width=Data.W+2,length=Data.L+2;float[] xs=new float[width],zs=new float[length];
            xs[0]=Data.MinX;xs[width-1]=Data.MaxX;zs[0]=Data.MinZ;zs[length-1]=Data.MaxZ;
            for(int i=1;i<width-1;i++)xs[i]=Data.MinX+(i-.5f)*Data.DX;
            for(int j=1;j<length-1;j++)zs[j]=Data.MinZ+(j-.5f)*Data.DZ;
            int patches=0,refined=0,maximumSubdivisions=1,totalTriangles=0;
            // Spatial chunks retain useful culling. Adaptive subdivisions only
            // refine curved bilinear patches; no source void is made solid.
            for(int firstZ=0;firstZ<length-1;firstZ+=32)for(int firstX=0;firstX<width-1;firstX+=32)
            {
                WalkMeshChunk chunk=new WalkMeshChunk();
                for(int z=firstZ;z<Math.Min(firstZ+32,length-1);z++)for(int x=firstX;x<Math.Min(firstX+32,width-1);x++)
                {
                    int x0=Math.Max(0,Math.Min(Data.W-1,x-1)),x1=Math.Max(0,Math.Min(Data.W-1,x));
                    int z0=Math.Max(0,Math.Min(Data.L-1,z-1)),z1=Math.Max(0,Math.Min(Data.L-1,z));
                    if(Data.Valid[z0*Data.W+x0]==0||Data.Valid[z0*Data.W+x1]==0||Data.Valid[z1*Data.W+x0]==0||Data.Valid[z1*Data.W+x1]==0)continue;
                    Vector3 a=WalkVertex(xs[x],zs[z]),b=WalkVertex(xs[x+1],zs[z]),c=WalkVertex(xs[x],zs[z+1]),d=WalkVertex(xs[x+1],zs[z+1]);
                    float error=Math.Abs(a.y-b.y-c.y+d.y)*.25f;
                    int n=Math.Max(1,(int)Math.Ceiling(Math.Sqrt(error/WalkHeightTolerance)));
                    if(n>64)throw new InvalidDataException("Adaptive walk patch subdivision is unexpectedly large");
                    if(chunk.Vertices.Count+(n+1)*(n+1)>=59000){CommitWalkChunk(chunk,layer);chunk=new WalkMeshChunk();}
                    int[] indices=new int[(n+1)*(n+1)];
                    for(int j=0;j<=n;j++)for(int i=0;i<=n;i++)
                    {
                        float px=i==0?a.x:(i==n?b.x:a.x+(b.x-a.x)*(i/(float)n));
                        float pz=j==0?a.z:(j==n?c.z:a.z+(c.z-a.z)*(j/(float)n));
                        indices[j*(n+1)+i]=chunk.Vertex(WalkVertex(px,pz));
                    }
                    for(int j=0;j<n;j++)for(int i=0;i<n;i++){int k=j*(n+1)+i;chunk.Quad(indices[k],indices[k+1],indices[k+n+1],indices[k+n+2]);}
                    patches++;if(n>1)refined++;maximumSubdivisions=Math.Max(maximumSubdivisions,n);totalTriangles+=2*n*n;
                }
                CommitWalkChunk(chunk,layer);
            }
            if(WalkColliders.Count==0)throw new InvalidDataException("Hub valid mask has no collidable terrain cells");
            Runtime.Log("HUB ADAPTIVE WALK SURFACE: sourcePatches="+patches+"; refinedPatches="+refined+"; triangles="+totalTriangles+"; chunks="+WalkColliders.Count+"; maxSubdivisions="+maximumSubdivisions
                +"; bilinearErrorTargetMetres="+WalkHeightTolerance+"; colliderAndSensorShareMesh=True; sourceVoidsPreserved=True; nativeSimulationBilinearUnchanged=True; actualGameplayAcceptancePending=True");
        }
        static void BuildContours()
        {
            float interval=Math.Max(1f,Data.Range/48f);List<Vector3> lines=new List<Vector3>();int total=0;
            Material material=NewSensorMaterial("Contours",new Color(.16f,.29f,.35f,1f));
            for(int j=0;j<Data.L-1;j++)for(int i=0;i<Data.W-1;i++)
            {
                int k=j*Data.W+i;if(Data.Valid[k]==0||Data.Valid[k+1]==0||Data.Valid[k+Data.W]==0||Data.Valid[k+Data.W+1]==0)continue;
                float x=Data.MinX+(i+.5f)*Data.DX,z=Data.MinZ+(j+.5f)*Data.DZ;
                Vector3[] p=new Vector3[]{new Vector3(x,Data.Decode(k),z),new Vector3(x+Data.DX,Data.Decode(k+1),z),new Vector3(x+Data.DX,Data.Decode(k+Data.W+1),z+Data.DZ),new Vector3(x,Data.Decode(k+Data.W),z+Data.DZ)};
                float low=Math.Min(Math.Min(p[0].y,p[1].y),Math.Min(p[2].y,p[3].y)),high=Math.Max(Math.Max(p[0].y,p[1].y),Math.Max(p[2].y,p[3].y));
                for(float level=(float)Math.Ceiling(low/interval)*interval;level<high;level+=interval)
                {
                    List<Vector3> crossings=new List<Vector3>();for(int e=0;e<4;e++){Vector3 a=p[e],b=p[(e+1)%4];if((a.y<=level&&b.y>level)||(b.y<=level&&a.y>level)){Vector3 point=a+(b-a)*((level-a.y)/(b.y-a.y));point.y+=.06f;crossings.Add(point);}}
                    for(int c=0;c+1<crossings.Count;c+=2){lines.Add(crossings[c]);lines.Add(crossings[c+1]);total++;}
                    if(lines.Count>=59000){CreateContourChunk(lines,material);lines.Clear();}
                }
            }
            if(lines.Count>0)CreateContourChunk(lines,material);Runtime.Log("HUB CONTOURS: heightDerivedSegments="+total+"; intervalM="+interval+"; sourceHoleCellsSkipped=True");
        }
        static void CreateContourChunk(List<Vector3> vertices,Material material)
        {
            Mesh mesh=new Mesh();mesh.name="HubContourChunk";mesh.vertices=vertices.ToArray();int[] indices=new int[vertices.Count];for(int i=0;i<indices.Length;i++)indices[i]=i;mesh.SetIndices(indices,MeshTopology.Lines,0);mesh.RecalculateBounds();CreatedAssets.Add(mesh);
            GameObject item=new GameObject(mesh.name);item.transform.parent=WorldRoot.transform;item.layer=LayerMask.NameToLayer("Default");item.AddComponent<MeshFilter>().sharedMesh=mesh;Renderer renderer=item.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;SensorRenderers.Add(renderer);
        }
        static void ApplyInstance(Transform target,InstanceRecord value){target.localPosition=value.Position;target.localRotation=value.Rotation;target.localScale=value.Scale;}
        sealed class ImportedSensorBinding
        {
            public Renderer Renderer;public Material NormalMaterial;public int NormalLayer,Kind;public bool NormalEnabled,NormalReceiveShadows;
            public UnityEngine.Rendering.ShadowCastingMode NormalShadowCasting;
            public void Apply(bool sensors)
            {
                if(Renderer==null)return;
                // Imported terrain is replaced by the independent height-derived
                // surface. Every other semantic role reuses its actual solid mesh.
                if(sensors&&Kind==0){Renderer.enabled=false;return;}
                Renderer.sharedMaterial=sensors?SemanticSensorMaterials[Kind]:NormalMaterial;
                Renderer.shadowCastingMode=sensors?UnityEngine.Rendering.ShadowCastingMode.Off:NormalShadowCasting;
                Renderer.receiveShadows=sensors?false:NormalReceiveShadows;
                Renderer.gameObject.layer=sensors?LayerMask.NameToLayer("Default"):NormalLayer;
                Renderer.enabled=NormalEnabled;
            }
        }
        static string ColorText(Color c)
        {return string.Format(CultureInfo.InvariantCulture,"({0:F4},{1:F4},{2:F4},{3:F4})",c.r,c.g,c.b,c.a);}
        static int NativeRole(string value)
        {
            string s=value.ToLowerInvariant();
            if(s.Contains("crystal")||s.Contains("_ore")||s.Contains("ore_")||s.Contains("/ore")||s.Contains(" ore")||s.Contains("resource")||s.Contains("salvage"))return 4;
            if(s.Contains("refinery")||s.Contains("industrial")||s.Contains("reactor")||s.Contains("factory")||s.Contains("silo")||s.Contains("tower"))return 5;
            if(s.Contains("road")||s.Contains("floor")||s.Contains("platform")||s.Contains("runway")||s.Contains("bridge")||s.Contains("ramp"))return 2;
            if(s.Contains("rock")||s.Contains("cliff")||s.Contains("boulder")||s.Contains("dune")||s.Contains("mountain"))return 3;
            if(s.Contains("terrain")||s.Contains("ground")||s.Contains("landscape"))return 0;
            return 1;
        }
        static bool PaletteColorUsable(Color c,int role)
        {
            if(float.IsNaN(c.r)||float.IsNaN(c.g)||float.IsNaN(c.b)||float.IsNaN(c.a)||float.IsInfinity(c.r)||float.IsInfinity(c.g)||float.IsInfinity(c.b)||float.IsInfinity(c.a))return false;
            float high=Math.Max(c.r,Math.Max(c.g,c.b)),low=Math.Min(c.r,Math.Min(c.g,c.b));
            if(c.a<.02f||high<.035f||high>4f)return false;
            // White/black defaults and red masks do not establish an environment
            // palette. Preserve red as the hostile-unit identification channel.
            if(high>.92f&&low>.88f)return false;
            if(c.r>c.g*1.8f&&c.r>c.b*1.6f&&role!=4)return false;
            if(role==4&&c.r>c.g*3f)return false;
            return true;
        }
        static bool VertexPalette(Renderer renderer,out Color average,out string summary)
        {
            average=Color.white;summary="meshOrVertexColorsAbsent";
            try
            {
                SkinnedMeshRenderer skin=renderer as SkinnedMeshRenderer;MeshFilter filter=renderer.GetComponent<MeshFilter>();
                Mesh mesh=skin!=null?skin.sharedMesh:(filter==null?null:filter.sharedMesh);if(mesh==null)return false;
                Color32[] colors=mesh.colors32;if(colors==null||colors.Length==0){summary="mesh="+mesh.name+"; vertexColors=0";return false;}
                int samples=Math.Min(64,colors.Length);float r=0,g=0,b=0,a=0,min=1f,max=0f;
                for(int i=0;i<samples;i++)
                {
                    Color32 c=colors[samples==1?0:i*(colors.Length-1)/(samples-1)];r+=c.r;g+=c.g;b+=c.b;a+=c.a;
                    min=Math.Min(min,Math.Min(c.r,Math.Min(c.g,c.b))/255f);max=Math.Max(max,Math.Max(c.r,Math.Max(c.g,c.b))/255f);
                }
                average=new Color(r/(255f*samples),g/(255f*samples),b/(255f*samples),a/(255f*samples));
                summary="mesh="+mesh.name+"; vertexColorCount="+colors.Length+"; sampled="+samples+"; mean="+ColorText(average)+"; sampleRgbRange="+min.ToString("F4",CultureInfo.InvariantCulture)+".."+max.ToString("F4",CultureInfo.InvariantCulture);
                return true;
            }
            catch(UnityException error){summary="vertexColorsUnreadable="+error.GetType().Name;return false;}
        }
        static void CaptureNativeSensorPalette()
        {
            Color[] defaults=new Color[]{new Color(.025f,.04f,.055f,1f),new Color(.34f,.56f,.66f,1f),new Color(.075f,.105f,.125f,1f),new Color(.14f,.19f,.22f,1f),new Color(.34f,.24f,.10f,1f),new Color(.42f,.66f,.64f,1f)};
            for(int i=0;i<defaults.Length;i++)SensorColors[i]=defaults[i];
            RefreshDynamicRootProtection();Dictionary<Renderer,bool> candidates=new Dictionary<Renderer,bool>();
            int modelLayer=LayerMask.NameToLayer("Models"),collidingLayer=LayerMask.NameToLayer("CollidingModels");
            foreach(GameObject root in LoadingScene.SceneRootNodes)if(root!=null)
            {
                foreach(SensorsViewObjectToggle toggle in root.GetComponentsInChildren<SensorsViewObjectToggle>(true))
                {
                    if(IsProtectedSubtree(toggle.transform))continue;
                    GameObject[] targets=NativeRead.Get(toggle,"m_OnInSensors") as GameObject[];if(targets==null)continue;
                    foreach(GameObject target in targets)if(target!=null)foreach(Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
                        if(renderer!=null&&!IsProtectedSubtree(renderer.transform))candidates[renderer]=true;
                }
                foreach(Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if(renderer==null||IsProtectedSubtree(renderer.transform))continue;
                    int layer=renderer.gameObject.layer;if((layer==modelLayer||layer==collidingLayer)&&!candidates.ContainsKey(renderer))candidates.Add(renderer,false);
                }
            }
            int[] scores=new int[6];string[] sources=new string[6];int materials=0,vertexSets=0,logged=0,readFailures=0;
            foreach(KeyValuePair<Renderer,bool> pair in candidates)
            {
                Renderer renderer=pair.Key;Color vertexMean;string vertexSummary;bool vertexAvailable=VertexPalette(renderer,out vertexMean,out vertexSummary);if(vertexAvailable)vertexSets++;
                Material[] native;
                try{native=renderer.sharedMaterials;}catch(UnityException){readFailures++;continue;}
                foreach(Material material in native)
                {
                    if(material==null)continue;materials++;string path=ObjectPath(renderer.transform),shader=material.shader==null?"missing":material.shader.name;
                    Color color=Color.white,tint=Color.white;bool hasColor=false,hasTint=false;
                    try{hasColor=material.HasProperty("_Color");hasTint=material.HasProperty("_TintColor");if(hasColor)color=material.GetColor("_Color");if(hasTint)tint=material.GetColor("_TintColor");}
                    catch(UnityException){readFailures++;}
                    int role=NativeRole(path+" "+material.name);Color selected=Color.white;string channel="none";int quality=0;
                    if(hasColor&&PaletteColorUsable(color,role)){selected=color;channel="_Color";quality=30;}
                    if(hasTint&&PaletteColorUsable(tint,role)){selected=tint;channel="_TintColor";quality=35;}
                    if(vertexAvailable&&pair.Value&&PaletteColorUsable(vertexMean,role)&&quality==0){selected=vertexMean;channel="sampledVertexMean";quality=20;}
                    int score=(pair.Value?100:0)+quality;
                    if(quality>0&&score>scores[role])
                    {
                        selected.a=1f;SensorColors[role]=selected;scores[role]=score;
                        sources[role]=path+" | material="+material.name+" | shader="+shader+" | channel="+channel+" | origin="+(pair.Value?"m_OnInSensors":"ModelsOrCollidingModelsLayer");
                    }
                    if(logged<48)
                    {
                        Runtime.Log("HUB NATIVE SENSOR PALETTE SAMPLE: object="+path+"; origin="+(pair.Value?"m_OnInSensors":"ModelsOrCollidingModelsLayer")+
                            "; layer="+renderer.gameObject.layer+"; material="+material.name+"; shader="+shader+"; _Color="+(hasColor?ColorText(color):"absent")+
                            "; _TintColor="+(hasTint?ColorText(tint):"absent")+"; "+vertexSummary+"; inferredRole="+role+"; selectedChannel="+channel);logged++;
                    }
                }
            }
            // Some native materials carry brightness through vertex masks or a
            // proprietary shader. Such defaults are recorded above, not guessed
            // to be literal material colors. Unresolved roles use stated colors.
            for(int role=0;role<6;role++)Runtime.Log("HUB SENSOR ROLE PALETTE: kind="+role+"; color="+ColorText(SensorColors[role])+
                "; source="+(sources[role]??"explicitFallback_noUnambiguousNativeRoleColor")+"; solidUnlitApproximation=True; normalMaterialsModified=False");
            Runtime.Log("HUB NATIVE SENSOR PALETTE CAPTURED: candidateRenderers="+candidates.Count+"; materials="+materials+"; vertexColorSets="+vertexSets+"; readFailures="+readFailures+
                "; detailedSamplesLogged="+logged+"; captureBeforeSourceDestruction=True; nativeShaderAndVertexColorCompositionRecreated=False; runtimeVisualAcceptancePending=True");
        }

        static InstanceRecord DecomposeInstance(int index,Matrix4x4 m)
        {
            if(Math.Abs(m[3,0])+Math.Abs(m[3,1])+Math.Abs(m[3,2])+Math.Abs(m[3,3]-1f)>.0001f)throw new InvalidDataException("Hub instance is not affine");
            Vector3 a=new Vector3(m[0,0],m[1,0],m[2,0]),b=new Vector3(m[0,1],m[1,1],m[2,1]),c=new Vector3(m[0,2],m[1,2],m[2,2]);
            float x=a.magnitude,y=b.magnitude,z=c.magnitude;if(x<.000001f||y<.000001f||z<.000001f)throw new InvalidDataException("Hub degenerate instance scale");
            if(Math.Abs(Vector3.Dot(a,b)/(x*y))>.00025f||Math.Abs(Vector3.Dot(a,c)/(x*z))>.00025f||Math.Abs(Vector3.Dot(b,c)/(y*z))>.00025f)throw new InvalidDataException("Hub instance contains shear; preserve offline instead of changing geometry");
            if(Vector3.Dot(Vector3.Cross(a,b),c)<0f)x=-x;Quaternion rotation=Quaternion.LookRotation(c/z,b/y);Vector3 scale=new Vector3(x,y,z),position=new Vector3(m[0,3],m[1,3],m[2,3]);Matrix4x4 reconstructed=Matrix4x4.TRS(position,rotation,scale);
            float tolerance=Math.Max(1f,Math.Max(Math.Abs(x),Math.Max(y,z)))*.0005f;
            for(int r=0;r<3;r++)for(int col=0;col<4;col++)if(Math.Abs(reconstructed[r,col]-m[r,col])>tolerance)throw new InvalidDataException("Hub instance TRS reconstruction differs from source matrix");
            return new InstanceRecord{Index=index,Position=position,Rotation=rotation,Scale=scale};
        }
        sealed class InstanceRecord{public int Index;public Vector3 Position,Scale;public Quaternion Rotation;}
        sealed class MeshRecord{public int Kind,V,I;public string Name,Texture;public Color Color;public long Offset;}
        sealed class ObstacleRecord{public int Flags;public Vector2r[] Points;}
        sealed class MaskRect{public int X0,X1,Z0,Z1;}
        sealed class HubData
        {
            public string Path;public float MinX,MinZ,MaxX,MaxZ,Range,StartX,StartZ,GoalX,GoalZ,TriangleErrorBound;public int W,L;public byte[] Q,Valid,NavigationValid;public bool[] Blocked;
            public string NavigationMaskSource="sourceValid_noNavigationProxy";
            public readonly List<InstanceRecord> Instances=new List<InstanceRecord>();public readonly List<MeshRecord> Meshes=new List<MeshRecord>();public readonly List<ObstacleRecord> Obstacles=new List<ObstacleRecord>();public readonly List<MaskRect> VoidRectangles=new List<MaskRect>();
            public float WidthM{get{return MaxX-MinX;}}public float LengthM{get{return MaxZ-MinZ;}}public float DX{get{return WidthM/W;}}public float DZ{get{return LengthM/L;}}
            public float Decode(int index){return Q[index]*(Range/255f);}
            public float Height(float x,float z)
            {
                float px=(x-MinX)/DX-.5f,pz=(z-MinZ)/DZ-.5f;int ix=(int)Math.Floor(px),iz=(int)Math.Floor(pz);float tx=px-ix,tz=pz-iz;
                int x0=Math.Max(0,Math.Min(W-1,ix)),x1=Math.Max(0,Math.Min(W-1,ix+1)),z0=Math.Max(0,Math.Min(L-1,iz)),z1=Math.Max(0,Math.Min(L-1,iz+1));
                return (Decode(z0*W+x0)*(1-tx)+Decode(z0*W+x1)*tx)*(1-tz)+(Decode(z1*W+x0)*(1-tx)+Decode(z1*W+x1)*tx)*tz;
            }
            public bool Safe(float x,float z)
            {int i=(int)Math.Floor((x-MinX)/DX),j=(int)Math.Floor((z-MinZ)/DZ);return i>=0&&i<W&&j>=0&&j<L&&!Blocked[j*W+i];}
            public static HubData Load(string file)
            {
                HubData d=new HubData();d.Path=System.IO.Path.GetFullPath(file);
                using(BinaryReader r=new BinaryReader(File.OpenRead(file)))
                {
                    int magic=r.ReadInt32();if(magic!=0x31425548&&magic!=0x32425548)throw new InvalidDataException("Expected HUB1/HUB2 magic");int version=r.ReadInt32();if(version!=1&&version!=2)throw new InvalidDataException("Expected HUB version 1 or 2");
                    d.MinX=Finite(r.ReadSingle());d.MinZ=Finite(r.ReadSingle());d.MaxX=Finite(r.ReadSingle());d.MaxZ=Finite(r.ReadSingle());d.Range=Finite(r.ReadSingle());d.W=r.ReadInt32();d.L=r.ReadInt32();
                    if(d.W<2||d.W>4096||d.L<2||d.L>4096||d.WidthM<=0||d.LengthM<=0||d.Range<=0||d.Range>10000)throw new InvalidDataException("Hub metadata limits invalid");
                    int count=checked(d.W*d.L);d.Q=r.ReadBytes(count);d.Valid=r.ReadBytes(count);if(d.Q.Length!=count||d.Valid.Length!=count)throw new EndOfStreamException();
                    for(int k=0;k<count;k++)if(d.Valid[k]>1)throw new InvalidDataException("Hub mask must use only 0/1");
                    d.StartX=Finite(r.ReadSingle());d.StartZ=Finite(r.ReadSingle());d.GoalX=Finite(r.ReadSingle());d.GoalZ=Finite(r.ReadSingle());
                    int obstacleCount=r.ReadInt32();if(obstacleCount<0||obstacleCount>100000)throw new InvalidDataException("Hub obstacle count invalid");
                    for(int o=0;o<obstacleCount;o++){ObstacleRecord item=new ObstacleRecord();item.Flags=r.ReadInt32();int n=r.ReadInt32();if(item.Flags<0||item.Flags>7||n<3||n>128)throw new InvalidDataException("Hub convex obstacle contract invalid");item.Points=new Vector2r[n];for(int i=0;i<n;i++)item.Points[i]=P(Finite(r.ReadSingle()),Finite(r.ReadSingle()));d.Obstacles.Add(item);}
                    int meshes=r.ReadInt32();if(meshes<1||meshes>100000)throw new InvalidDataException("Hub mesh count invalid");
                    for(int m=0;m<meshes;m++)
                    {
                        MeshRecord item=new MeshRecord();item.Kind=r.ReadInt32();item.Name=ReadText(r);item.Texture=ReadText(r);item.Color=new Color(Finite(r.ReadSingle()),Finite(r.ReadSingle()),Finite(r.ReadSingle()),Finite(r.ReadSingle()));item.V=r.ReadInt32();item.I=r.ReadInt32();
                        if(item.Kind<0||item.Kind>5||item.V<3||item.V>60000||item.I<3||item.I%3!=0||item.I>18000000)throw new InvalidDataException("Hub mesh chunk contract invalid");
                        item.Offset=r.BaseStream.Position;long bytes=checked((long)item.V*32L+(long)item.I*4L);if(bytes>r.BaseStream.Length-r.BaseStream.Position)throw new EndOfStreamException();r.BaseStream.Seek(bytes,SeekOrigin.Current);d.Meshes.Add(item);
                    }
                    if(version==1){for(int i=0;i<d.Meshes.Count;i++)d.Instances.Add(new InstanceRecord{Index=i,Position=Vector3.zero,Rotation=Quaternion.identity,Scale=Vector3.one});}
                    else
                    {
                        int instanceCount=r.ReadInt32();if(instanceCount<1||instanceCount>200000)throw new InvalidDataException("Hub instance count invalid");
                        for(int i=0;i<instanceCount;i++){int index=r.ReadInt32();if(index<0||index>=d.Meshes.Count)throw new InvalidDataException("Hub instance mesh index invalid");Matrix4x4 matrix=new Matrix4x4();for(int c=0;c<4;c++)for(int row=0;row<4;row++)matrix[row,c]=Finite(r.ReadSingle());d.Instances.Add(DecomposeInstance(index,matrix));}
                    }
                    if(r.BaseStream.Position!=r.BaseStream.Length)throw new InvalidDataException("Unexpected Hub binary tail");
                }
                d.LoadNavigationMask();d.BuildVoidMask();if(!d.Safe(d.StartX,d.StartZ)||!d.Safe(d.GoalX,d.GoalZ))throw new InvalidDataException("Hub start/goal intersects the conservative navigation proxy or its one-cell safety border");
                for(int j=0;j<d.L-1;j++)for(int i=0;i<d.W-1;i++){int k=j*d.W+i;if(d.Valid[k]!=0&&d.Valid[k+1]!=0&&d.Valid[k+d.W]!=0&&d.Valid[k+d.W+1]!=0)d.TriangleErrorBound=Math.Max(d.TriangleErrorBound,Math.Abs(d.Decode(k)-d.Decode(k+1)-d.Decode(k+d.W)+d.Decode(k+d.W+1))*.25f);}
                return d;
            }
            void LoadNavigationMask()
            {
                NavigationValid=Valid;string file=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path),"hub_navigation_mask.bin");
                if(!File.Exists(file)){Runtime.Log("HUB NAVIGATION MASK: optionalProxy=False; sourceValidUsedUnchanged=True; additionalPaddingCells=1");return;}
                byte[] actualHash;using(System.Security.Cryptography.SHA256 sha=System.Security.Cryptography.SHA256.Create())actualHash=sha.ComputeHash(Valid);
                using(BinaryReader reader=new BinaryReader(File.OpenRead(file)))
                {
                    if(reader.BaseStream.Length!=48L+Valid.Length)throw new InvalidDataException("Hub NVM1 navigation mask length mismatch");
                    if(reader.ReadInt32()!=0x314D564E||reader.ReadInt32()!=1||reader.ReadInt32()!=W||reader.ReadInt32()!=L)throw new InvalidDataException("Hub NVM1 navigation mask header/dimensions mismatch");
                    byte[] expectedHash=reader.ReadBytes(32);for(int i=0;i<actualHash.Length;i++)if(expectedHash[i]!=actualHash[i])throw new InvalidDataException("Hub NVM1 source Valid SHA256 mismatch");
                    byte[] proxy=reader.ReadBytes(Valid.Length);if(proxy.Length!=Valid.Length||reader.BaseStream.Position!=reader.BaseStream.Length)throw new EndOfStreamException("Hub NVM1 mask data incomplete");
                    int extraBlocked=0;for(int i=0;i<proxy.Length;i++){if(proxy[i]>1||proxy[i]>Valid[i])throw new InvalidDataException("Hub NVM1 navigation validity must be 0/1 and cannot restore a source void");if(proxy[i]<Valid[i])extraBlocked++;}
                    NavigationValid=proxy;NavigationMaskSource="optionalConservativeNVM1Proxy";
                    Runtime.Log("HUB NAVIGATION MASK: optionalProxy=True; file="+file+"; sourceValidSHA256="+BitConverter.ToString(actualHash).Replace("-",string.Empty).ToLowerInvariant()+
                        "; proxyAdditionallyBlockedSamples="+extraBlocked+"; navigationValidSubsetOfSourceValid=True; QAndSourceValidUnmodified=True; additionalPaddingCells=1; sourceCollisionAndSensorGeometryStillUsesSourceValid=True");
                }
            }
            void BuildVoidMask()
            {
                Blocked=new bool[Valid.Length];for(int j=0;j<L;j++)for(int i=0;i<W;i++)for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
                {int x=i+dx,z=j+dz;if(x<0||x>=W||z<0||z>=L||NavigationValid[z*W+x]==0)Blocked[j*W+i]=true;}
                Dictionary<string,MaskRect> active=new Dictionary<string,MaskRect>();
                for(int j=0;j<L;j++)
                {
                    Dictionary<string,MaskRect> next=new Dictionary<string,MaskRect>();int i=0;
                    while(i<W){if(!Blocked[j*W+i]){i++;continue;}int first=i;while(i<W&&Blocked[j*W+i])i++;string key=first+":"+i;MaskRect rect;
                        if(active.TryGetValue(key,out rect))rect.Z1=j+1;else{rect=new MaskRect{X0=first,X1=i,Z0=j,Z1=j+1};VoidRectangles.Add(rect);}next.Add(key,rect);}
                    active=next;
                }
                if(VoidRectangles.Count>50000)throw new InvalidDataException("Hub void mask yields too many exact rectangle blockers; simplify mask conservatively offline");
            }
        }


    }
    public sealed class HubRegionHeightAsset : HeightMapAttributesAsset
    {
        public HeightMapAttributes Attributes;
        public override object Data { get { return Attributes; } }
    }
}
