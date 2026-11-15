using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
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
    public static class CustomFlatMap
    {
        public const string MapKey = "EF_FlatLab_01";
        const float HalfExtent = 4000f;
        const int Grid = 513;
        static readonly MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly List<GameObject> SourceRoots = new List<GameObject>();
        static int RemovedRendererCount, DisabledSensorControllerCount;
        static float NextSourceSweepAt;
        static readonly List<Collider> DisabledColliders = new List<Collider>();
        static readonly List<UnityEngine.Object> CreatedAssets = new List<UnityEngine.Object>();
        static LevelDefinition Definition;
        static LevelManager Manager;
        static LevelDefinition[] OriginalEntries;
        static GameObject SpawnRoot, WorldRoot, DynamicRoot;
        static Scene LoadingScene;
        static DependencyContainerBase SessionDependencies;
        static Mesh FloorMesh;
        static MeshCollider FloorCollider;
        static Renderer FloorRenderer, SensorFloorRenderer, SensorGridRenderer;
        static readonly List<Mesh> GroundMeshes = new List<Mesh>();
        static readonly List<MeshCollider> GroundColliders = new List<MeshCollider>();
        static readonly List<Renderer> GroundRenderers = new List<Renderer>(), SensorSurfaces = new List<Renderer>();
        static readonly Dictionary<SetRenderSettings,ParticleOcclusionMapAttributesAsset> AuxiliaryOriginals = new Dictionary<SetRenderSettings,ParticleOcclusionMapAttributesAsset>();
        static DuneData Dunes;
        static bool SensorEventsBound, SensorActive, SourcePresentationVerified;
        static UserCamera ActiveUserCamera;
        static BoxCollider CameraConstraints;
        static volatile bool DependenciesInjected, ColliderVerified, SimulationVerified;
        static volatile string Fault;
        static int PhysicsWaitFrames;

        public static Vector2r Center { get { return Runtime.HubRegion?HubRegionMap.Center:P(0f, 0f); } }
        static Vector2r PlayerStart = P(-2400f, 0f), PlayerGoal = P(-1840f, 0f);
        public static Vector2r Start { get { return Runtime.HubRegion?HubRegionMap.Start:PlayerStart; } }
        public static Vector2r Goal { get { return Runtime.HubRegion?HubRegionMap.Goal:PlayerGoal; } }
        public static void ConfigurePlayerTestArea(Vector2r carrierOrigin)
        {
            if(Runtime.HubRegion){HubRegionMap.ConfigurePlayerTestArea(carrierOrigin);return;}
            // Team spawn sides are randomized by the native SpawnAssigner.
            // Anchor the test pair to the actual player, never to an assumed left side.
            float inward = F(carrierOrigin.X) <= 0f ? 1f : -1f;
            PlayerStart = carrierOrigin + P(inward * 600f, 0f);
            PlayerGoal = PlayerStart + P(inward * 560f, 0f);
            Runtime.Log("FLAT MAP PLAYER AREA: carrier=" + carrierOrigin + "; start=" + PlayerStart + "; goal=" + PlayerGoal + "; anchoredToActualOwner=True");
        }
        public static void ConfigureStandaloneTestArea(Vector2r unitOrigin)
        {
            if(Runtime.HubRegion){HubRegionMap.ConfigureStandaloneTestArea(unitOrigin);return;}
            float inward=F(unitOrigin.X)<=0f?1f:-1f;
            PlayerStart=unitOrigin;PlayerGoal=unitOrigin+P(inward*560f,0f);
            Runtime.Log("FLAT MAP STANDALONE AREA: startingUnit="+unitOrigin+"; start="+PlayerStart+"; goal="+PlayerGoal+"; carrierRequired=False; nativeStartingUnitAdopted=True");
        }
        public static bool Ready { get { return Runtime.HubRegion?HubRegionMap.Ready:(DependenciesInjected && ColliderVerified && SimulationVerified && Fault == null); } }

        public static LevelDefinition RegisterClone(LevelManager levels, LevelDefinition template)
        {
            if(Runtime.HubRegion){return HubRegionMap.RegisterClone(levels,template);}
            if (Definition != null) return Definition;
            if (levels == null || template == null) throw new ArgumentNullException("Flat map requires a native bootstrap definition");
            Dunes=DuneData.LoadOptional();
            string sourceName = template.SceneName;
            string[] sourceSubscenes = template.GetAllSubscenes();
            Definition = (LevelDefinition)CloneMethod.Invoke(template, null);
            NativeRead.Set(Definition, "m_SceneName", MapKey);
            NativeRead.Set(Definition, "m_NameLocId", "罗德岛训练场");
            NativeRead.Set(Definition, "m_MapDescriptionLocId", Dunes==null?"平坦开阔的训练场地，可进行单位操控与武器测试。":"包含沙丘与起伏地形，可进行车辆通行与舰载机操作训练。");
            NativeRead.Set(Definition, "m_AmbientHeatPoints", 0);
            NativeRead.Set(Definition, "m_RandomArtifacts", new string[0]);
            NativeRead.Set(Definition, "m_MaximumSpawnedWreckArtifacts", 0);
            NativeRead.Set(Definition, "m_EnableCampaignPersistence", false);
            NativeRead.Set(Definition, "m_ShowMapBoundaries", true);
            NativeRead.Set(Definition, "m_ShowMapBoundariesSensors", true);

            MultiSceneAsset bootstrap = RhodesAssetClone59.CreateEmpty<MultiSceneAsset>();
            bootstrap.name = MapKey + "_NativeLoaderBootstrap";
            bootstrap.Subscenes = new MultiSceneAsset.SceneInfo[sourceSubscenes.Length];
            for (int i = 0; i < sourceSubscenes.Length; i++)
            {
                bootstrap.Subscenes[i] = new MultiSceneAsset.SceneInfo();
                bootstrap.Subscenes[i].Include = true;
                bootstrap.Subscenes[i].SceneName = sourceSubscenes[i];
                bootstrap.Subscenes[i].SceneAssetPath = string.Empty;
            }
            NativeRead.Set(Definition, "m_MultiScene", bootstrap);
            CreatedAssets.Add(bootstrap);
            BuildSpawnInfo();
            NativeRead.Set(Definition, "m_SceneSpawnInfo", SpawnRoot);

            OriginalEntries = (LevelDefinition[])NativeRead.Get(levels, "m_LevelEntriesMP");
            LevelDefinition[] entries = new LevelDefinition[OriginalEntries.Length + 1];
            Array.Copy(OriginalEntries, entries, OriginalEntries.Length);
            entries[entries.Length - 1] = Definition;
            NativeRead.Set(levels, "m_LevelEntriesMP", entries);
            Manager = levels;
            levels.SceneLoadStart += OnSceneLoadStart;
            if (template.SceneName != sourceName || object.ReferenceEquals(Definition, template))
                throw new InvalidOperationException("Flat map registration mutated the native definition");
            Runtime.Log("FLAT MAP REGISTERED: key=" + MapKey + "; independentDefinition=True; originalEntryUnchanged=True; entriesBefore="
                + OriginalEntries.Length + "; entriesAfter=" + entries.Length + "; bounds=-4000..4000; terrain="+(Dunes==null?"flatY0":"cellCentredDuneHeightfield64mRange")+"; spawnPoints=6; bootstrap="
                + sourceName + "; nativeSubscenes=" + string.Join(",", sourceSubscenes)
                + "; newCompiledUnityScene=False; generatedWorld=True");
            return Definition;
        }

        public static void ConfigureSessionDependencies(DependencyContainerBase dependencies)
        {
            if(Runtime.HubRegion){HubRegionMap.ConfigureSessionDependencies(dependencies);return;}
            SessionDependencies = dependencies;
        }

        public static IEnumerator OnSceneLoadStart(LevelDefinition level, Scene scene, GameMode mode, ReplayMode replay, DependencyContainerBase dependencies)
        {
            if (level != null && level.SceneName == MapKey)
            {
                LoadingScene = scene;
                SessionDependencies = dependencies;
                Runtime.Log("FLAT MAP LOAD START: logicalScene=" + scene.Name + "; sharedBootstrapSubscenes=" + string.Join(",", scene.Subscenes));
            }
            yield break;
        }

        // Hooked at the unique return of native LevelManager.ProcessTerrainInfo.
        // Native data is ignored only for our independently registered map key.
        public static DependencyContainerBase ReplaceTerrainDependencies(DependencyContainerBase original, LevelDefinition level)
        {
            if(Runtime.HubRegion){return HubRegionMap.ReplaceTerrainDependencies(original,level);}
            if (level == null || level.SceneName != MapKey) return original;
            try
            {
                if (DependenciesInjected) throw new InvalidOperationException("Flat map terrain dependencies injected twice");
                if (LoadingScene == null || SessionDependencies == null) throw new InvalidOperationException("Flat map scene/session capture missing");
                SuppressBootstrapWorld();
                ClearSourceMissionEntities();
                BuildFlatWorld();
                ReplaceRenderHeightMetadata();
                if(Runtime.RhodesCarrier)RhodesTestLighting.Install(MapKey,LoadingScene.SceneRootNodes,SensorActive);
                else FlatLabDaylight.Install(MapKey,LoadingScene.SceneRootNodes,SensorActive);

                SimMapDependencies flat = new SimMapDependencies();
                flat.MinPathfindingBounds = P(-HalfExtent, -HalfExtent);
                flat.MaxPathfindingBounds = P(HalfExtent, HalfExtent);
                flat.HeightMapAttributes = new FlatHeightAttributes();
                flat.Obstacles = new MapObstacle[0];
                flat.RidgeLines = new SimRidgeLine[0];
                flat.SerializedSimMapData = null;
                flat.AmbientHeatPoints = 0;
                DependenciesInjected = true;
                Runtime.Log("FLAT MAP DEPENDENCIES: bounds=" + flat.MinPathfindingBounds + ".." + flat.MaxPathfindingBounds
                    + "; heightMode="+(Dunes==null?"constant0":"sharedQuantizedDunes")+"; grid=" + Grid + "x" + Grid
                    + "; oldObstacles=discarded; obstacles=0; ridgelines=0; serializedNativeNavDiscarded=True; nativeFreshBakeRequired=True");
                return new DependencyContainer<SimMapDependencies>(flat);
            }
            catch (Exception error)
            {
                Fault = error.ToString();
                Runtime.Log("FLAT MAP LOAD FAILED: " + error);
                throw;
            }
        }

        public static void MainTick()
        {
            if(Runtime.HubRegion){HubRegionMap.MainTick();return;}
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
            Runtime.Log("FLAT MAP VISIBLE/COLLISION PASS: " + detail + "; floorChunks="+GroundMeshes.Count+"; firstChunkVertices=" + FloorMesh.vertexCount
                + "; firstColliderUsesSameMesh=" + (FloorCollider.sharedMesh == FloorMesh)
                + "; cameraBounds=" + CameraConstraints.bounds + "; oldRendererCount=" + RemovedRendererCount
                + "; oldColliderCount=" + DisabledColliders.Count);
        }

        public static bool Prepare(object sim)
        {
            if(Runtime.HubRegion){return HubRegionMap.Prepare(sim);}
            if (Fault != null) throw new InvalidOperationException("Flat map: " + Fault);
            if (!DependenciesInjected || !ColliderVerified) return false;
            if (SimulationVerified) return true;
            object map = NativeRead.Get(sim, "Map");
            Vector2r min = (Vector2r)NativeRead.Get(map, "Min"), max = (Vector2r)NativeRead.Get(map, "Max");
            if (min != P(-HalfExtent,-HalfExtent) || max != P(HalfExtent,HalfExtent)) throw new InvalidOperationException("SimMap did not receive independent flat bounds");
            object height = NativeRead.Get(map,"HeightMap");
            byte[] bytes = (byte[])NativeRead.Get(height,"mHeights");
            if (bytes.Length != Grid*Grid) throw new InvalidOperationException("Flat simulation height grid size mismatch");
            for(int i=0;i<bytes.Length;i++)if(bytes[i]!=(Dunes==null?0:Dunes.Q[i]))throw new InvalidOperationException("Generated/native terrain bytes differ at "+i);
            int points = 0;
            foreach (float z in ValidationZ())
                foreach (float x in ValidationX())
                {
                    Vector2r point=P(x,z);
                    float expected=Dunes==null?0f:Dunes.Height(x,z);
                    if (Math.Abs(F((Fixed64)NativeRead.Call(height,"GetWorldHeight",point))-expected)>.005f
                        || !(bool)NativeRead.Call(map,"MapPositionValid",point,UnitClass.Ground))
                        throw new InvalidOperationException("Generated simulation point height/passability mismatch: "+point);
                    points++;
                }
            if ((bool)NativeRead.Call(map,"MapPositionValid",P(HalfExtent+10f,0f),UnitClass.Ground))
                throw new InvalidOperationException("Independent flat map boundary did not reject outside point");
            IDictionary caches=(IDictionary)NativeRead.Get(map,"mBakedMeshes");
            if (caches.Count==0) throw new InvalidOperationException("Flat map has no baked native navigation caches");
            int obstacles=0;
            foreach (object ignored in (IEnumerable)NativeRead.Get(NativeRead.Get(map,"mPathfindingObstaclesHash"),"AllObstacles")) obstacles++;
            if (obstacles!=0) throw new InvalidOperationException("Original map navigation obstacles leaked into flat map: "+obstacles);
            int spawns=0;
            Array spawnAttributes=(Array)NativeRead.Get(NativeRead.Get(sim,"Settings"),"SpawnPoints");
            foreach(object spawn in spawnAttributes)
            {
                // PlayerSpawnInfo implements SpawnAttributes.SpawnPoint explicitly.
                Vector2r point=((SpawnAttributes)spawn).SpawnPoint;
                if (Math.Abs(F(point.X))>HalfExtent || Math.Abs(F(point.Y))>HalfExtent)
                    throw new InvalidOperationException("Original spawn point leaked into flat map: "+point);
                Runtime.Log("FLAT MAP SIM SPAWN: index="+spawns+"; position="+point);
                spawns++;
            }
            if (spawns!=6) throw new InvalidOperationException("Flat map did not receive six independent spawn attributes");
            SimulationVerified=true;
            Runtime.Log("FLAT MAP READY: key="+MapKey+"; simulationHeightMode="+(Dunes==null?"allZero":"sharedQuantizedDunes")+"; validatedPoints="+points
                +"; nativeObstacles="+obstacles+"; freshNativeNavCaches="+caches.Count+"; independentSpawns="+spawns
                +"; outsideBoundsRejected=True; renderCollisionAndSimAligned=True");
            return true;
        }

        static void BuildSpawnInfo()
        {
            SpawnRoot=new GameObject(MapKey+"_IndependentSpawnInfo");
            SpawnRoot.SetActive(false);
            SceneSpawnInfo info=SpawnRoot.AddComponent<SceneSpawnInfo>();
            SceneSpawnInfo.TeamSpawnInfo[] teams=new SceneSpawnInfo.TeamSpawnInfo[2];
            SceneSpawnInfo.PlayerSpawnInfo[] ffa=new SceneSpawnInfo.PlayerSpawnInfo[6];
            for(int team=0;team<2;team++)
            {
                teams[team]=new SceneSpawnInfo.TeamSpawnInfo();
                SceneSpawnInfo.PlayerSpawnInfo[] players=new SceneSpawnInfo.PlayerSpawnInfo[3];
                for(int i=0;i<3;i++)
                {
                    GameObject point=new GameObject("Spawn_"+team+"_"+i);
                    point.transform.parent=SpawnRoot.transform;
                    point.transform.position=new Vector3(team==0?-3000f:3000f,0f,(i-1)*700f);
                    point.transform.rotation=Quaternion.Euler(0f,team==0?90f:270f,0f);
                    players[i]=new SceneSpawnInfo.PlayerSpawnInfo();
                    NativeRead.Set(players[i],"m_SpawnPoint",point.transform);
                    players[i].m_DefaultCameraHeading=team==0?90f:270f;
                    ffa[team*3+i]=players[i];
                }
                NativeRead.Set(teams[team],"m_PlayerSpawnInfos",players);
            }
            NativeRead.Set(info,"m_TeamSpawnInfos",teams);
            NativeRead.Set(info,"m_FFASpawnInfos",ffa);
            SpawnRoot.SetActive(true);
            UnityEngine.Object.DontDestroyOnLoad(SpawnRoot);
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
                    NativeRead.Set(bounds,"SizeX",HalfExtent*2f); NativeRead.Set(bounds,"SizeY",HalfExtent*2f);
                    NativeRead.Set(bounds,"CenterX",0f); NativeRead.Set(bounds,"CenterY",0f);
                    NativeRead.Call(bounds,"SetSimCoordinates");
                    oldBounds++;
                }
            }
            if(roots==0 || RemovedRendererCount==0) throw new InvalidOperationException("Native bootstrap world roots could not be identified and suppressed");
            Runtime.Log("FLAT MAP SOURCE WORLD SUPPRESSED: roots="+roots+"; renderers="+RemovedRendererCount
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
                    Runtime.Log("FLAT SOURCE SENSOR TOGGLE REMOVED: object=" + ObjectPath(toggle.transform)
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
            RefreshDynamicRootProtection();
            foreach (Collider collider in DisabledColliders)
                if (collider != null && collider.enabled && IsSourceEnvironment(collider.transform)) collider.enabled = false;
            if (!force && Time.realtimeSinceStartup < NextSourceSweepAt) return;
            int before = RemovedRendererCount;
            foreach (GameObject root in SourceRoots) if (root != null) PruneSourcePresentation(root);
            NextSourceSweepAt = Time.realtimeSinceStartup + 2f;
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
                Runtime.Log("FLAT SOURCE PRESENTATION VERIFIED: remainingSourceRenderers=" + remaining
                    + "; activeSourceSensorControllers=" + activeControllers + "; activeSourceTerrains=" + activeSourceTerrains
                    + "; removedRenderers=" + RemovedRendererCount + "; neutralizedSensorControllers=" + DisabledSensorControllerCount
                    + "; currentSensors=" + SensorActive + "; protectedDynamicOrGeneratedRenderers=" + protectedRenderers
                    + "; dynamicRootProtected=" + (DynamicRoot != null) + "; sceneAssetsOnDiskUnchanged=True; generatedAndDynamicWorldPreserved=True");
            SourcePresentationVerified = true;
        }

        static void BuildFlatSensorPresentation()
        {
            int layer=LayerMask.NameToLayer("Default");
            if(layer<0)throw new InvalidOperationException("Native Default sensor layer is missing");
            Shader shader=Dunes==null?(Shader.Find("Unlit/Color")??Shader.Find("Sprites/Default")??Shader.Find("Diffuse")):(Shader.Find("Sprites/Default")??Shader.Find("Unlit/Color")??Shader.Find("Diffuse"));
            if(shader==null)throw new InvalidOperationException("No sensor terrain shader found");
            bool vertexColours=Dunes!=null&&shader.name=="Sprites/Default";
            Material surface=new Material(shader);surface.name=MapKey+"_SensorHeightSurface";
            Color flat=EndfieldHudTheme.Active?new Color(.075f,.082f,.086f,1f):new Color(.025f,.05f,.075f,1f);
            surface.color=vertexColours?Color.white:flat;surface.renderQueue=1900;CreatedAssets.Add(surface);
            int colouredVertices=0;
            foreach(Mesh sourceMesh in GroundMeshes)
            {
                Mesh mesh=sourceMesh;
                if(vertexColours)
                {
                    // Private render-only copy: physical height, triangle winding,
                    // simulation bytes and the gameplay mesh remain unchanged.
                    mesh=UnityEngine.Object.Instantiate(sourceMesh) as Mesh;
                    if(mesh==null)throw new InvalidOperationException("Sensor mesh clone failed");
                    mesh.name=sourceMesh.name+"_SensorElevationColour";CreatedAssets.Add(mesh);
                    Vector3[] p=mesh.vertices,n=mesh.normals;Color32[] colours=new Color32[p.Length];
                    Vector3 light=new Vector3(-.45f,.84f,-.30f).normalized;
                    for(int i=0;i<p.Length;i++)
                    {
                        float elevation=Mathf.Sqrt(Mathf.Clamp01(p[i].y/56f));
                        float relief=(Vector3.Dot(n[i],light)-light.y)*.75f;
                        float tone=Mathf.Clamp(elevation*.095f+relief,-.025f,.12f);
                        colours[i]=new Color(Mathf.Clamp01(flat.r+tone),Mathf.Clamp01(flat.g+tone*.97f),Mathf.Clamp01(flat.b+tone*.90f),1f);
                    }
                    mesh.colors32=colours;colouredVertices+=p.Length;
                }
                GameObject sensor=new GameObject(MapKey+"_Sensors_Surface_"+SensorSurfaces.Count);
                sensor.transform.parent=WorldRoot.transform;sensor.transform.localPosition=Vector3.zero;sensor.layer=layer;
                sensor.AddComponent<MeshFilter>().sharedMesh=mesh;
                Renderer renderer=sensor.AddComponent<MeshRenderer>();renderer.sharedMaterial=surface;SensorSurfaces.Add(renderer);
                if(SensorFloorRenderer==null)SensorFloorRenderer=renderer;
            }
            List<Vector3> vertices=new List<Vector3>();List<int> indices=new List<int>();
            for(float value=-HalfExtent;value<=HalfExtent;value+=500f)
            {AddSensorGridLine(vertices,indices,value,true);AddSensorGridLine(vertices,indices,value,false);}
            Mesh grid=new Mesh();grid.name=MapKey+"_Sensor500mGrid";grid.vertices=vertices.ToArray();
            grid.SetIndices(indices.ToArray(),MeshTopology.Lines,0);grid.RecalculateBounds();CreatedAssets.Add(grid);
            Material lines=new Material(shader);lines.name=MapKey+"_SensorGridMaterial";
            lines.color=EndfieldHudTheme.Active?(Dunes==null?new Color(.18f,.20f,.19f,1f):new Color(.15f,.165f,.16f,1f)):new Color(.12f,.26f,.32f,1f);lines.renderQueue=1901;CreatedAssets.Add(lines);
            GameObject gridObject=new GameObject(MapKey+"_Sensors_500mGrid");gridObject.transform.parent=WorldRoot.transform;gridObject.layer=layer;
            gridObject.AddComponent<MeshFilter>().sharedMesh=grid;
            SensorGridRenderer=gridObject.AddComponent<MeshRenderer>();SensorGridRenderer.sharedMaterial=lines;
            int contourSegments=Dunes==null?0:BuildSensorContours(shader,layer);
            SetSensorState(false,"generated");BindSensorEvents();
            Runtime.Log("FLAT SENSOR PRESENTATION PREPARED: ownSurface=True; sensorLayer="+layer+"; nativeCameraIncludesDefault=True; terrainLayerExcludedByNativeSensors=True; surfaceUsesSameHeightTriangles=True; sensorPrivateVertexColours="+vertexColours+"; elevationColourVertices="+colouredVertices+"; sparseContourSegments="+contourSegments+"; gridFollowsTerrain=True; gridSpacing=500; gridVertices="+vertices.Count+"; sourceContoursOrBuildingsReused=False; physicalTerrainExaggerated=False; shader="+shader.name);
        }
        static int BuildSensorContours(Shader shader,int layer)
        {
            Material material=new Material(shader);material.name=MapKey+"_SensorHeightContours";
            material.color=EndfieldHudTheme.Active?new Color(.235f,.25f,.235f,.65f):new Color(.17f,.32f,.37f,.65f);
            material.renderQueue=1902;CreatedAssets.Add(material);
            float[] levels={.5f,1f,4f,8f,16f,24f,32f,40f,48f};List<Vector3> points=new List<Vector3>();int count=0,batches=0;
            foreach(Mesh mesh in GroundMeshes)
            {
                Vector3[] v=mesh.vertices;int[] triangles=mesh.triangles;
                for(int i=0;i<triangles.Length;i+=3)
                {
                    Vector3 a=v[triangles[i]],b=v[triangles[i+1]],c=v[triangles[i+2]];
                    float lo=Mathf.Min(a.y,Mathf.Min(b.y,c.y)),hi=Mathf.Max(a.y,Mathf.Max(b.y,c.y));
                    if(hi-lo<.00001f)continue;
                    foreach(float level in levels)
                    {
                        if(level<=lo||level>=hi||(level<4f&&Mathf.Abs((a.x+b.x+c.x)/3f)<2200f))continue;
                        Vector3 first=Vector3.zero,second=Vector3.zero;int crossings=0;
                        AddContourCrossing(a,b,level,ref first,ref second,ref crossings);
                        AddContourCrossing(b,c,level,ref first,ref second,ref crossings);
                        AddContourCrossing(c,a,level,ref first,ref second,ref crossings);
                        if(crossings!=2||(first-second).sqrMagnitude<.000001f)continue;
                        first.y+=.12f;second.y+=.12f;points.Add(first);points.Add(second);count++;
                        if(points.Count>=58000){FlushSensorContours(points,material,layer,batches++);points.Clear();}
                    }
                }
            }
            if(points.Count>0)FlushSensorContours(points,material,layer,batches++);
            return count;
        }
        static void AddContourCrossing(Vector3 a,Vector3 b,float level,ref Vector3 first,ref Vector3 second,ref int count)
        {
            if((a.y<level)==(b.y<level)||Mathf.Abs(a.y-b.y)<.000001f)return;
            Vector3 point=Vector3.Lerp(a,b,(level-a.y)/(b.y-a.y));
            if(count==0)first=point;else if(count==1)second=point;count++;
        }
        static void FlushSensorContours(List<Vector3> points,Material material,int layer,int batch)
        {
            Mesh mesh=new Mesh();mesh.name=MapKey+"_SparseContours_"+batch;mesh.vertices=points.ToArray();
            int[] indices=new int[points.Count];for(int i=0;i<indices.Length;i++)indices[i]=i;
            mesh.SetIndices(indices,MeshTopology.Lines,0);mesh.RecalculateBounds();CreatedAssets.Add(mesh);
            GameObject item=new GameObject(mesh.name);item.transform.parent=WorldRoot.transform;item.layer=layer;
            item.AddComponent<MeshFilter>().sharedMesh=mesh;Renderer renderer=item.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;SensorSurfaces.Add(renderer);
        }

        static void AddSensorGridLine(List<Vector3> vertices,List<int> indices,float fixedAxis,bool alongZ)
        {
            int segments=Dunes==null?1:256;
            for(int i=0;i<segments;i++)
            {
                float a=-HalfExtent+HalfExtent*2f*i/segments,b=-HalfExtent+HalfExtent*2f*(i+1)/segments;
                float ax=alongZ?fixedAxis:a,az=alongZ?a:fixedAxis,bx=alongZ?fixedAxis:b,bz=alongZ?b:fixedAxis;
                int start=vertices.Count;float ya=Dunes==null?0f:Dunes.Height(ax,az)+.08f,yb=Dunes==null?0f:Dunes.Height(bx,bz)+.08f;
                vertices.Add(new Vector3(ax,ya,az));vertices.Add(new Vector3(bx,yb,bz));indices.Add(start);indices.Add(start+1);
            }
        }

        static void BindSensorEvents()
        {
            if (SensorEventsBound || ShipbreakersMain.PresentationEventSystem == null) return;
            ShipbreakersMain.PresentationEventSystem.AddHandler<SensorsManagerEvent>(OnFlatSensorsTransition);
            SensorEventsBound = true;
        }

        static void OnFlatSensorsTransition(SensorsManagerEvent value)
        {
            if (!DependenciesInjected && FloorRenderer == null) return;
            EnforceSourceSuppression(true);
            SetSensorState(value.Active, "native SensorsManagerEvent");
        }

        static void SetSensorState(bool active, string reason)
        {
            SensorActive = active;
            if(Runtime.RhodesCarrier)RhodesTestLighting.SetSensors(active);else FlatLabDaylight.SetSensors(active);
            foreach(Renderer renderer in GroundRenderers)if(renderer!=null)renderer.enabled=!active;
            foreach(Renderer renderer in SensorSurfaces)if(renderer!=null)renderer.enabled=active;
            if (SensorGridRenderer != null) SensorGridRenderer.enabled = active;
            Runtime.Log("FLAT SENSOR STATE: active=" + active + "; reason=" + reason
                + "; generatedSensorSurface=" + (SensorFloorRenderer != null && SensorFloorRenderer.enabled)
                + "; generatedGameplaySurface=" + (FloorRenderer != null && FloorRenderer.enabled)
                + "; gameplayLayer=" + (FloorRenderer == null ? -1 : FloorRenderer.gameObject.layer)
                + "; sensorLayer=" + (SensorFloorRenderer == null ? -1 : SensorFloorRenderer.gameObject.layer)
                + "; gridEnabled=" + (SensorGridRenderer != null && SensorGridRenderer.enabled)
                + "; actualCameraSensors=" + (ActiveUserCamera == null ? "not-bound" : ActiveUserCamera.SensorsManagerActive.ToString()));
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
            Runtime.Log("FLAT MAP ORIGINAL ENTITIES DISCARDED: descriptors="+descriptors+"; groups="+groups+"; newDescriptors=0; newGroups=0; wreckArtifacts=0");
        }

        static void BuildFlatWorld()
        {
            WorldRoot=new GameObject(MapKey+"_GeneratedWorld");
            GameObject dynamicRoot=NativeRead.Get(LoadingScene,"mDynamicRootNode") as GameObject;
            if(dynamicRoot!=null) WorldRoot.transform.parent=dynamicRoot.transform;
            int terrainLayer=LayerMask.NameToLayer("Terrain");
            if(terrainLayer<0) throw new InvalidOperationException("Native Terrain render/physics layer is missing");
            if(Dunes!=null)BuildDuneGround(terrainLayer);
            else
            {
            GameObject floor=new GameObject("EF_FlatGround_8000x8000_Y0");
            floor.transform.parent=WorldRoot.transform; floor.layer=terrainLayer;
            FloorMesh=new Mesh(); FloorMesh.name=MapKey+"_FlatVisibleAndCollisionMesh";
            FloorMesh.vertices=new Vector3[] {new Vector3(-HalfExtent,0f,-HalfExtent),new Vector3(HalfExtent,0f,-HalfExtent),new Vector3(HalfExtent,0f,HalfExtent),new Vector3(-HalfExtent,0f,HalfExtent)};
            FloorMesh.triangles=new int[] {0,2,1,0,3,2};
            FloorMesh.normals=new Vector3[] {Vector3.up,Vector3.up,Vector3.up,Vector3.up};
            FloorMesh.uv=new Vector2[] {new Vector2(0f,0f),new Vector2(1f,0f),new Vector2(1f,1f),new Vector2(0f,1f)};
            FloorMesh.RecalculateBounds(); CreatedAssets.Add(FloorMesh);
            floor.AddComponent<MeshFilter>().sharedMesh=FloorMesh;
            MeshRenderer renderer=floor.AddComponent<MeshRenderer>(); FloorRenderer=renderer;
            renderer.sharedMaterial=FlatSandMaterial.Create(HalfExtent*2f,CreatedAssets);
            FloorCollider=floor.AddComponent<MeshCollider>(); FloorCollider.sharedMesh=FloorMesh;
            GroundMeshes.Add(FloorMesh);GroundColliders.Add(FloorCollider);GroundRenderers.Add(FloorRenderer);
            }
            BuildFlatSensorPresentation();

            GameObject constraints=new GameObject("MapBounds"); constraints.transform.parent=WorldRoot.transform;
            constraints.tag="CAM_CONSTRAINTS"; constraints.layer=LayerMask.NameToLayer("Ignore Raycast");
            CameraConstraints=constraints.AddComponent<BoxCollider>(); CameraConstraints.size=new Vector3(HalfExtent*2f,4000f,HalfExtent*2f); CameraConstraints.isTrigger=true;
            MapBounds bounds=constraints.AddComponent<MapBounds>();
            NativeRead.Set(bounds,"SizeX",HalfExtent*2f); NativeRead.Set(bounds,"SizeY",HalfExtent*2f);
            NativeRead.Set(bounds,"CenterX",0f); NativeRead.Set(bounds,"CenterY",0f); NativeRead.Call(bounds,"SetSimCoordinates");

            // Count actual active scene components rather than prefab assets.
            int activeSpawnCount=0;
            foreach(SceneSpawnInfo spawn in UnityEngine.Object.FindObjectsOfType(typeof(SceneSpawnInfo)))
            { if(spawn.gameObject!=SpawnRoot) throw new InvalidOperationException("An original SceneSpawnInfo remains active: "+spawn.name); activeSpawnCount++; }
            if(activeSpawnCount!=1) throw new InvalidOperationException("Independent flat spawn component is not uniquely active");
            RenderSettings.fog=false;
            int vertices=0,triangles=0;foreach(Mesh mesh in GroundMeshes){vertices+=mesh.vertexCount;triangles+=mesh.triangles.Length/3;}
            Runtime.Log("FLAT MAP GENERATED WORLD: vertices="+vertices+"; triangles="+triangles+"; chunks="+GroundMeshes.Count+"; terrain="+(Dunes==null?"flatY0":"cellCentredQuantizedDunes")+"; extent=8000x8000; layer="+terrainLayer
                +"; ownMeshCollider=True; ownCameraBounds=True; uniqueActiveSpawnInfo=True; originalWorldGeometryDisabled=True; perFrameMeshWrites=0");
        }

        static void BuildDuneGround(int layer)
        {
            Material material=FlatSandMaterial.Create(HalfExtent*2f,CreatedAssets);
            // Native513² byte grid is unchanged. Each cell is a four-triangle
            // fan around its exact native bilinear midpoint; this quarters the
            // diagonal interpolation error with the new64m height range.
            const int cells=Grid+1,patch=128;
            for(int z0=0;z0<cells;z0+=patch)for(int x0=0;x0<cells;x0+=patch)
            {
                int nx=Math.Min(patch,cells-x0),nz=Math.Min(patch,cells-z0),width=nx+1;
                int corners=(nx+1)*(nz+1),total=corners+nx*nz;
                Vector3[] points=new Vector3[total],normals=new Vector3[total];Vector2[] uv=new Vector2[total];
                for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++)
                {
                    float px=Dunes.Knot(x0+x),pz=Dunes.Knot(z0+z);int index=z*width+x;
                    points[index]=new Vector3(px,Dunes.Height(px,pz),pz);normals[index]=Dunes.Normal(px,pz);
                    uv[index]=new Vector2((px+HalfExtent)/(HalfExtent*2f),(pz+HalfExtent)/(HalfExtent*2f));
                }
                int[] triangles=new int[nx*nz*12];int cursor=0;
                for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)
                {
                    int a=z*width+x,b=a+1,c=a+width,d=c+1,m=corners+z*nx+x;
                    float px=(points[a].x+points[d].x)*.5f,pz=(points[a].z+points[d].z)*.5f;
                    points[m]=new Vector3(px,Dunes.Height(px,pz),pz);normals[m]=Dunes.Normal(px,pz);
                    uv[m]=new Vector2((px+HalfExtent)/(HalfExtent*2f),(pz+HalfExtent)/(HalfExtent*2f));
                    triangles[cursor++]=a;triangles[cursor++]=m;triangles[cursor++]=b;
                    triangles[cursor++]=b;triangles[cursor++]=m;triangles[cursor++]=d;
                    triangles[cursor++]=d;triangles[cursor++]=m;triangles[cursor++]=c;
                    triangles[cursor++]=c;triangles[cursor++]=m;triangles[cursor++]=a;
                }
                Mesh mesh=new Mesh();mesh.name=MapKey+"_DuneFanPatch_"+x0+"_"+z0;
                mesh.vertices=points;mesh.normals=normals;mesh.uv=uv;mesh.triangles=triangles;mesh.RecalculateBounds();CreatedAssets.Add(mesh);
                GameObject item=new GameObject(mesh.name);item.transform.parent=WorldRoot.transform;item.layer=layer;
                item.AddComponent<MeshFilter>().sharedMesh=mesh;Renderer renderer=item.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                MeshCollider collider=item.AddComponent<MeshCollider>();collider.sharedMesh=mesh;
                GroundMeshes.Add(mesh);GroundColliders.Add(collider);GroundRenderers.Add(renderer);
                if(FloorMesh==null){FloorMesh=mesh;FloorCollider=collider;FloorRenderer=renderer;}
            }
        }

        static void ReplaceRenderHeightMetadata()
        {
            int count=0;FlatHeightAttributes heights=new FlatHeightAttributes();
            Texture2D occlusion=new Texture2D(Grid,Grid,TextureFormat.RGBA32,false,true);occlusion.name=MapKey+"_SharedHeightOcclusion";occlusion.wrapMode=TextureWrapMode.Clamp;
            Color32[] pixels=new Color32[Grid*Grid];byte[] q=heights.HeightMap;
            for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32(q[i],q[i],q[i],255);
            occlusion.SetPixels32(pixels);occlusion.Apply(false,false);CreatedAssets.Add(occlusion);
            DuneLabHeightAsset newHeight=RhodesAssetClone59.CreateEmpty<DuneLabHeightAsset>();newHeight.name=MapKey+"_IndependentAuxHeight";newHeight.Attributes=heights;CreatedAssets.Add(newHeight);
            foreach(GameObject root in LoadingScene.SceneRootNodes)
                if(root!=null)
                    foreach(SetRenderSettings settings in root.GetComponentsInChildren<SetRenderSettings>(true))
                    {
                        ParticleOcclusionMapAttributesAsset original=settings.ParticleOcclusionSettings;
                        if(original==null) continue;
                        ParticleOcclusionMapAttributesAsset copy=UnityEngine.Object.Instantiate(original) as ParticleOcclusionMapAttributesAsset;
                        object data=CloneMethod.Invoke(original.Data,null);
                        NativeRead.Set(data,"mOcclusionMap",occlusion);
                        NativeRead.Set(data,"mMapBottomLeft",new Vector3(-HalfExtent,0f,-HalfExtent));
                        NativeRead.Set(data,"mMapSize",new Vector3(HalfExtent*2f,F(heights.MaxHeight),HalfExtent*2f));
                        NativeRead.Set(data,"mHeightMap",newHeight);
                        AuxiliaryOriginals.Add(settings,original);
                        NativeRead.Set(copy,"m_Data",data); settings.ParticleOcclusionSettings=copy;
                        NativeRead.Call(settings,"UpdateParticleOcclusionShaderConstants");
                        CreatedAssets.Add(copy);
                        count++;
                    }
            if(count==0) throw new InvalidOperationException("No native auxiliary map metadata was found for flat replacement");
            Runtime.Log("FLAT MAP AUXILIARY MAPS: renderSettings="+count+"; sameQuantizedBytesAsSim=True; independentHeightMapAsset=True; sourceBytesReadOnly=True; shaderMapBounds=-4000..4000; restoredOnReset=True");
        }

        static bool VerifyFlatCollision(out string detail)
        {
            if(FloorCollider==null || FloorMesh==null) { detail="Generated flat floor does not exist"; return false; }
            int hits=0;float maximumError=0f;
            foreach(float z in ValidationZ()) foreach(float x in ValidationX())
            {
                RaycastHit hit;
                bool success=Physics.Raycast(new Vector3(x,10000f,z),Vector3.down,out hit,20000f,1<<FloorCollider.gameObject.layer);
                float error=success?Math.Abs(hit.point.y-(Dunes==null?0f:Dunes.Height(x,z))):float.PositiveInfinity;
                if(!success || !GroundColliders.Contains(hit.collider as MeshCollider) || error>.04f)
                { detail="Generated terrain raycast mismatch at "+x+","+z+": "+(success?hit.collider.name+" y="+hit.point.y+" error="+error:"no-hit"); return false; }
                maximumError=Math.Max(maximumError,error);
                hits++;
            }
            if(Math.Abs(CameraConstraints.bounds.min.x+HalfExtent)>0.01f || Math.Abs(CameraConstraints.bounds.max.x-HalfExtent)>0.01f)
            { detail="Camera constraints do not match flat map extent"; return false; }
            detail="surfaceRaycasts="+hits+"; maximumCollisionVsNativeBilinearError="+maximumError+"; generatedSourceMeshOnly=True";
            return true;
        }

        public static void Reset()
        {
            RhodesTestLighting.Reset();
            FlatLabDaylight.Reset();
            if(Runtime.HubRegion){HubRegionMap.Reset();return;}
            if(Manager!=null)
            {
                Manager.SceneLoadStart-=OnSceneLoadStart;
                if(OriginalEntries!=null) NativeRead.Set(Manager,"m_LevelEntriesMP",OriginalEntries);
            }
            foreach(KeyValuePair<SetRenderSettings,ParticleOcclusionMapAttributesAsset> pair in AuxiliaryOriginals)if(pair.Key!=null)pair.Key.ParticleOcclusionSettings=pair.Value;
            AuxiliaryOriginals.Clear();
            if(WorldRoot!=null) UnityEngine.Object.Destroy(WorldRoot);
            if(SpawnRoot!=null) UnityEngine.Object.Destroy(SpawnRoot);
            foreach(UnityEngine.Object asset in CreatedAssets) if(asset!=null) UnityEngine.Object.Destroy(asset);
            if (SensorEventsBound && ShipbreakersMain.PresentationEventSystem != null)
                ShipbreakersMain.PresentationEventSystem.RemoveHandler<SensorsManagerEvent>(OnFlatSensorsTransition);
            SensorEventsBound=false; SensorActive=false; SourcePresentationVerified=false; ActiveUserCamera=null;
            CreatedAssets.Clear(); SourceRoots.Clear(); DisabledColliders.Clear();
            GroundMeshes.Clear();GroundColliders.Clear();GroundRenderers.Clear();SensorSurfaces.Clear();Dunes=null;
            RemovedRendererCount=DisabledSensorControllerCount=0; NextSourceSweepAt=0f;
            Definition=null; Manager=null; OriginalEntries=null; SpawnRoot=WorldRoot=DynamicRoot=null; LoadingScene=null; SessionDependencies=null;
            FloorMesh=null; FloorCollider=null; CameraConstraints=null;
            FloorRenderer=SensorFloorRenderer=SensorGridRenderer=null;
            DependenciesInjected=ColliderVerified=SimulationVerified=false; Fault=null; PhysicsWaitFrames=0;
            PlayerStart=P(-2400f,0f); PlayerGoal=P(-1840f,0f);
        }

        // Presentation-only queries of this generated collider's actual fan
        // triangles. Never synthesize support for HubRegion/native scenes.
        public static bool TrySampleGeneratedGround(float x,float z,out float y)
        {
            y=0f;
            if(Runtime.HubRegion||!Ready||WorldRoot==null||!WorldRoot.activeInHierarchy||FloorCollider==null||!FloorCollider.enabled||
               !RhodesSuspensionMath.Finite(x)||!RhodesSuspensionMath.Finite(z)||Math.Abs(x)>HalfExtent||Math.Abs(z)>HalfExtent)return false;
            y=Dunes==null?0f:Dunes.SurfaceHeight(x,z);return true;
        }
        public static bool TryFindGeneratedGroundSupport(RhodesGroundPoint[] quad,float slopeX,float slopeZ,
            RhodesGroundPoint[] workA,RhodesGroundPoint[] workB,out RhodesGroundPoint support)
        {
            support=new RhodesGroundPoint();float ignored;
            if(quad==null||quad.Length!=4||!RhodesSuspensionMath.Finite(slopeX)||!RhodesSuspensionMath.Finite(slopeZ))return false;
            for(int i=0;i<4;i++)if(!TrySampleGeneratedGround(quad[i].X,quad[i].Z,out ignored))return false;
            if(Dunes!=null)return Dunes.SurfaceSupport(quad,slopeX,slopeZ,workA,workB,out support);
            double maximum=double.NegativeInfinity;
            for(int i=0;i<4;i++){double value=-(double)slopeX*quad[i].X-(double)slopeZ*quad[i].Z;if(value>maximum){maximum=value;support=new RhodesGroundPoint(quad[i].X,0f,quad[i].Z);}}
            return true;
        }

        public static float SampleLift(Vector2r position) { return 0f; }
        public static float SampleOriginalHeight(Vector2r position) { return Runtime.HubRegion?HubRegionMap.SampleOriginalHeight(position):(Dunes==null?0f:Dunes.Height(F(position.X),F(position.Y))); }
        static float[] ValidationX(){return Dunes==null?new float[]{-3200f,0f,3200f}:new float[]{-3300f,-1900f,0f,1900f,3300f};}
        static float[] ValidationZ(){return Dunes==null?new float[]{-3200f,0f,3200f}:new float[]{-3200f,-1900f,-1350f,-700f,0f,700f,1350f,1900f,3200f};}
        static Vector2r P(float x,float z) { return new Vector2r(Fixed64.UnsafeFromFloat(x),Fixed64.UnsafeFromFloat(z)); }
        static float F(Fixed64 value) { return Fixed64.UnsafeFloatValue(value); }

        sealed class FlatHeightAttributes : HeightMapAttributes
        {
            readonly byte[] bytes=Dunes==null?new byte[Grid*Grid]:Dunes.Q;
            readonly int range=Dunes==null?255:64;
            public byte[] HeightMap { get { return bytes; } }
            public int GridWidth { get { return Grid; } }
            public int GridLength { get { return Grid; } }
            public Vector2r MinBounds { get { return P(-HalfExtent,-HalfExtent); } }
            public Vector2r MaxBounds { get { return P(HalfExtent,HalfExtent); } }
            public Fixed64 MinHeight { get { return Fixed64.Zero; } }
            public Fixed64 MaxHeight { get { return Fixed64.FromInt(range); } }
        }

        sealed class DuneData
        {
            public byte[] Q;
            public const float Range=64f,Cell=HalfExtent*2f/Grid;
            public float Knot(int index){return index==0?-HalfExtent:index==Grid+1?HalfExtent:-HalfExtent+(index-.5f)*Cell;}
            float Decode(int x,int z){return Q[z*Grid+x]*(Range/255f);}
            public float Height(float x,float z)
            {
                // Match native SimHeightMap's cell-centre convention exactly.
                float px=(x+HalfExtent)/Cell-.5f,pz=(z+HalfExtent)/Cell-.5f;
                int ix=(int)Math.Floor(px),iz=(int)Math.Floor(pz);float tx=px-ix,tz=pz-iz;
                int x0=Math.Max(0,Math.Min(Grid-1,ix)),x1=Math.Max(0,Math.Min(Grid-1,ix+1));
                int z0=Math.Max(0,Math.Min(Grid-1,iz)),z1=Math.Max(0,Math.Min(Grid-1,iz+1));
                return (Decode(x0,z0)*(1f-tx)+Decode(x1,z0)*tx)*(1f-tz)+(Decode(x0,z1)*(1f-tx)+Decode(x1,z1)*tx)*tz;
            }
            int CellIndex(float p){return Math.Max(0,Math.Min(Grid,(int)Math.Floor((p+HalfExtent)/Cell+.5f)));}
            public float SurfaceHeight(float x,float z)
            {
                int ix=CellIndex(x),iz=CellIndex(z);float x0=Knot(ix),x1=Knot(ix+1),z0=Knot(iz),z1=Knot(iz+1);
                float a=Height(x0,z0),b=Height(x1,z0),c=Height(x0,z1),d=Height(x1,z1),middle=Height((x0+x1)*.5f,(z0+z1)*.5f);
                return RhodesSuspensionMath.FanHeight((x-x0)/(x1-x0),(z-z0)/(z1-z0),a,b,c,d,middle);
            }
            public bool SurfaceSupport(RhodesGroundPoint[] quad,float slopeX,float slopeZ,RhodesGroundPoint[] workA,RhodesGroundPoint[] workB,out RhodesGroundPoint support)
            {
                float minX=quad[0].X,maxX=minX,minZ=quad[0].Z,maxZ=minZ;
                for(int i=1;i<4;i++){minX=Math.Min(minX,quad[i].X);maxX=Math.Max(maxX,quad[i].X);minZ=Math.Min(minZ,quad[i].Z);maxZ=Math.Max(maxZ,quad[i].Z);}
                int x0=CellIndex(minX),x1=CellIndex(maxX),z0=CellIndex(minZ),z1=CellIndex(maxZ);
                support=new RhodesGroundPoint();if((x1-x0+1)*(z1-z0+1)>128)return false;
                bool known=false;double maximum=double.NegativeInfinity;
                for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
                {
                    float xa=Knot(x),xb=Knot(x+1),za=Knot(z),zb=Knot(z+1),xm=(xa+xb)*.5f,zm=(za+zb)*.5f;
                    RhodesGroundPoint a=new RhodesGroundPoint(xa,Height(xa,za),za),b=new RhodesGroundPoint(xb,Height(xb,za),za),
                        c=new RhodesGroundPoint(xa,Height(xa,zb),zb),d=new RhodesGroundPoint(xb,Height(xb,zb),zb),m=new RhodesGroundPoint(xm,Height(xm,zm),zm);
                    RhodesSuspensionMath.AccumulateTriangleSupport(a,m,b,quad,workA,workB,slopeX,slopeZ,ref known,ref maximum,ref support);
                    RhodesSuspensionMath.AccumulateTriangleSupport(b,m,d,quad,workA,workB,slopeX,slopeZ,ref known,ref maximum,ref support);
                    RhodesSuspensionMath.AccumulateTriangleSupport(d,m,c,quad,workA,workB,slopeX,slopeZ,ref known,ref maximum,ref support);
                    RhodesSuspensionMath.AccumulateTriangleSupport(c,m,a,quad,workA,workB,slopeX,slopeZ,ref known,ref maximum,ref support);
                }
                return known;
            }
            public Vector3 Normal(float x,float z)
            {
                float dx=(Height(x+Cell,z)-Height(x-Cell,z))/(Cell*2f),dz=(Height(x,z+Cell)-Height(x,z-Cell))/(Cell*2f);
                return new Vector3(-dx,1f,-dz).normalized;
            }
            static bool FlatRectangle(float x,float z,float cx,float cz,float rx,float rz)
            {return Math.Abs(x-cx)<=rx&&Math.Abs(z-cz)<=rz;}
            static bool ProtectedFlatSample(float x,float z)
            {
                if(Math.Abs(z)<=900f)return true;
                // Actual current roster footprints plus16m native-sampling margin.
                // No temporary arrays or scene scans in the byte-validation loop.
                for(int side=-1;side<=1;side+=2)for(int slot=-700;slot<=700;slot+=700)
                {
                    float sx=side*3000f;
                    if(FlatRectangle(x,z,sx,slot,366f,156f)
                        ||FlatRectangle(x,z,side*3100f,slot+350f,146f,126f)
                        ||FlatRectangle(x,z,sx,slot+2000f,146f,126f)
                        ||FlatRectangle(x,z,sx,slot-2000f,146f,126f)
                        ||FlatRectangle(x,z,side*3400f,slot,146f,126f))return true;
                }
                return false;
            }
            public static DuneData LoadOptional()
            {
                if(!Runtime.RhodesCarrier)return null;
                string folder=Path.Combine(CustomUnits.AssetDirectory,"flat_lab_v07"),config=Path.Combine(folder,"dune_lab.ini");
                if(!File.Exists(config)){Runtime.Log("RHODES_DUNE_LAB disabled; config absent; original independent flat field retained");return null;}
                Dictionary<string,string> values=new Dictionary<string,string>(StringComparer.Ordinal);
                foreach(string line in File.ReadAllLines(config))
                {
                    string row=line.Trim();if(row.Length==0||row.StartsWith("#",StringComparison.Ordinal)||row.StartsWith(";",StringComparison.Ordinal))continue;
                    int separator=row.IndexOf('=');if(separator<=0)throw new InvalidDataException("Malformed Rhodes dune configuration");
                    string key=row.Substring(0,separator).Trim(),value=row.Substring(separator+1).Trim();
                    if((key!="Enabled"&&key!="HeightFile"&&key!="HeightSHA256")||values.ContainsKey(key))throw new InvalidDataException("Unknown/duplicate Rhodes dune configuration key");
                    values.Add(key,value);
                }
                string enabled;if(!values.TryGetValue("Enabled",out enabled)||enabled!="true")
                {if(enabled=="false"){Runtime.Log("RHODES_DUNE_LAB disabled by explicit preset; original flat field retained");return null;}throw new InvalidDataException("Rhodes dune Enabled must be true/false");}
                string file,expected;
                if(!values.TryGetValue("HeightFile",out file)||file!="dune_lab_v05.bin"||!values.TryGetValue("HeightSHA256",out expected)||expected.Length!=64)
                    throw new InvalidDataException("Rhodes dune payload name/hash missing or invalid");
                string path=Path.Combine(folder,file);FileInfo info=new FileInfo(path);
                if(!info.Exists||info.Length!=Grid*Grid+24L)throw new InvalidDataException("Rhodes dune payload has wrong size");
                byte[] raw=File.ReadAllBytes(path);string actual;
                using(SHA256 hash=SHA256.Create())actual=BitConverter.ToString(hash.ComputeHash(raw)).Replace("-",string.Empty).ToLowerInvariant();
                if(actual!=expected.ToLowerInvariant())throw new InvalidDataException("Rhodes dune payload SHA256 mismatch");
                DuneData result=new DuneData();
                using(BinaryReader reader=new BinaryReader(new MemoryStream(raw,false)))
                {
                    if(reader.ReadInt32()!=0x31444645||reader.ReadInt32()!=1||reader.ReadInt32()!=Grid||reader.ReadInt32()!=Grid||reader.ReadSingle()!=HalfExtent||reader.ReadSingle()!=Range)
                        throw new InvalidDataException("Rhodes dune grid/extent/range contract mismatch");
                    result.Q=reader.ReadBytes(Grid*Grid);
                }
                float maxDelta=0f;
                for(int z=0;z<Grid;z++)for(int x=0;x<Grid;x++)
                {
                    int index=z*Grid+x;float px=-HalfExtent+(x+.5f)*Cell,pz=-HalfExtent+(z+.5f)*Cell;
                    if(ProtectedFlatSample(px,pz)&&result.Q[index]!=0)throw new InvalidDataException("Rhodes dune payload alters protected spawn/production flats");
                    if(x>0)maxDelta=Math.Max(maxDelta,Math.Abs(result.Q[index]-result.Q[index-1])*(Range/255f));
                    if(z>0)maxDelta=Math.Max(maxDelta,Math.Abs(result.Q[index]-result.Q[index-Grid])*(Range/255f));
                }
                if(maxDelta>2f||maxDelta/Cell>.08f)throw new InvalidDataException("Rhodes dune payload exceeds slope/adjacent-step envelope");
                Runtime.Log("RHODES_DUNE_LAB_LOADED grid=513x513; byteSamplesAtCellCentres=True; heightRange=64; sampleSpacing="+Cell+"; maxAdjacentHeightDelta="+maxDelta+
                    "; protectedLocalRosterPads=True; centralFlatHalfWidth900=True; nearbyEndLaneSwells=True; localSuspensionDiagnosticLanes=A_Gentle_B_LimitStress; mapBoundsUnchanged=True; nativeFreshNavigationBake=True; runtimeNavigationAndPhysicsAcceptancePending=True; payloadSHA256="+actual);
                return result;
            }
        }
    }

    public sealed class DuneLabHeightAsset : HeightMapAttributesAsset
    {
        public HeightMapAttributes Attributes;
        public override object Data { get { return Attributes; } }
    }
}
