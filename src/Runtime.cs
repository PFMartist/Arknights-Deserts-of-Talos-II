// Isolated map / independent-unit experiment; not a released campaign.

// Native startup, quality and reflection helpers adapted from this project's HandOfSajuuk (MIT).

using System;

using System.Collections;

using System.Collections.Generic;

using System.IO;

using System.Reflection;

using BBI.Core;

using BBI.Core.ComponentModel;

using BBI.Core.Data;

using BBI.Core.Network;

using BBI.Core.Utility;

using BBI.Core.Utility.FixedPoint;

using BBI.Game.Data;

using BBI.Game.Network;

using BBI.Game.Replay;

using BBI.Game.Simulation;

using BBI.Game.Utility;

using BBI.Unity.Core.DLC;

using BBI.Unity.Game;

using BBI.Unity.Game.Data;

using BBI.Unity.Game.DLC;

using BBI.Unity.Game.UI;

using BBI.Unity.Game.Utility;

using BBI.Unity.Game.World;

using UnityEngine;



[assembly: AssemblyVersion("1.0.0.0")]

namespace EndfieldDoK.MapUnitLab

{

    public static class Runtime

    {

        static readonly object LogMutex = new object();

        static readonly object HandoffMutex = new object();

        static readonly string LogPath = FindLogPath();

        static readonly CommanderID Player = new CommanderID(1);

        static volatile int Stage;

        static volatile bool Failed, SpawnDone;

        static bool TimeScaleSet, SelectionSent, QualityApplied, QualityResolutionLogged, MapConfigured, RendererWaitWarningSent;

        static float Deadline, FocusDeadline, QualityVerifyAt, FocusVerifyAt;

        static LevelManager Levels;

        static DLCManager DLC;

        static SimController PresentationSim;

        public static float PresentationDeltaTime { get { return PresentationSim==null||PresentationSim.Paused?0f:Time.deltaTime*Mathf.Max(0f,Fixed64.UnsafeFloatValue(PresentationSim.TimeScale)); } }

        static Entity[] FocusRequest, LabEntities;

        static Vector2r LabOrigin;

        static bool FriendlyFleetReported;



        public static readonly bool UnitsOnly = File.Exists(Path.Combine(LabDirectory, "MapUnitLab.unitsonly"));

        public static readonly bool Lifecycle = File.Exists(Path.Combine(LabDirectory,"MapUnitLab.lifecycle"));

        public static readonly bool HubRegion = File.Exists(Path.Combine(LabDirectory,"MapUnitLab.hubregion"));

        public static readonly bool Humanoid = File.Exists(Path.Combine(LabDirectory,"MapUnitLab.humanoid"));

        public static readonly bool SkirmishMode = File.Exists(Path.Combine(LabDirectory,"RhodesMod.skirmish"));

        public static readonly bool RhodesCarrier = SkirmishMode || File.Exists(Path.Combine(LabDirectory,"MapUnitLab.rhodescarrier"));

        public static bool HumanoidServicesEnabled { get { return Humanoid || RhodesCarrier; } }

        public static readonly bool DetailedDiagnosticsEnabled = File.Exists(Path.Combine(LabDirectory, "MapUnitLab.diagnostics"));

        public static readonly bool FrameTimingEnabled = File.Exists(Path.Combine(LabDirectory, "MapUnitLab.frametiming"));

        public static readonly bool MenuPreview = File.Exists(Path.Combine(LabDirectory, "MapUnitLab.menupreview"));



        internal static void SetSkirmishPresentation(object main){PresentationSim=(SimController)NativeRead.Get(main,"mSimController");}
        internal static void ClearSkirmishPresentation(){PresentationSim=null;}

        public static void AfterEntityRegistration()

        {

            try
            {
                // Rhodes DoK: authored/directed by PFMartist; AI collaboration
                // by GPT through Codex. Public skirmish does not load the old
                // 315k-triangle BVM laboratory prototype or its private atlas.
                if(!SkirmishMode)CustomUnits.Register();
                else Log("RHODES_SKIRMISH_LEGACY_LAB_SKIPPED prototypeBVM=False; labAssetsNotRequired=True; nativeDonorsPreserved=True");
                if(HumanoidServicesEnabled)HumanoidUnits.Register();
                if(RhodesCarrier)
                {
                    FactionUnits.Register();
                    RhodesResearch.Register();
                    RhodesCarrierUnits.Register();
                    RhodesResearch.VerifyRegisteredSpecialtyTargets();
                    RhodesUnitIcons.ConfigureRegistered();
                    if(!SkirmishMode)RhodesWeaponRangeTargets.Register();
                }
                if(Humanoid)HumanoidRangeTarget.Register();
                RhodesLocalization59.CaptureRegistered((EntityTypeCollection)NativeRead.Get(typeof(ShipbreakersMain),"sEntityTypes"));
                RhodesNavigation59.CaptureRegisteredProfiles();
            }

            catch (Exception error) { Stop("main stage registration", error); }

        }



        static bool FactionCleanupPending;
        static void ReleaseFactionAfterUnload()
        {
            if(!FactionCleanupPending)return;
            foreach(UnitView view in UnitViewDiscovery.Get())
            {
                if(view==null||!view.gameObject.activeInHierarchy)continue;
                UnitState state=view.NonInterpolatedTickState;
                if(state!=null&&FactionUnits.IsFactionKey(state.TypeID))return;
                // A dying native view can clear its state before its FX retires.
                // Both model families share the private feedback templates.
                if(view.GetComponent<FactionModelBinding>()!=null||view.GetComponent<RhodesCarrierBinding>()!=null)return;
            }
            FactionUnits.Reset();FactionUnitModels.Reset();UnitViewDiscovery.Reset();FactionCleanupPending=false;
            Log("Faction private assets released after native views retired");
        }

        public static void Tick(object main)

        {

            if (Failed) return;
            RhodesVehicleWrecks.MainTick(main);
            if(SkirmishMode){try{RhodesSkirmishRuntime.MainTick(main);}catch(Exception error){RhodesSkirmishRuntime.ReportPresentationFailure(error);}return;}
            if(UnitViewDiscovery.Failure!=null){Stop("unit lifecycle registry",new InvalidOperationException(UnitViewDiscovery.Failure));return;}

            try

            {

                if (QualityApplied && !QualityResolutionLogged && Time.realtimeSinceStartup >= QualityVerifyAt)

                {

                    Log("Actual display: " + Screen.width + "x" + Screen.height + "; fullscreen=" + Screen.fullScreen + "; textureLimit=" + QualitySettings.masterTextureLimit);

                    QualityResolutionLogged = true;

                }

                // Opt-in launch diagnostic: leave the native frontend in place.
                // The ordinary launcher has no marker and follows the lab flow.
                if(MenuPreview){ApplyRequestedQuality();return;}

                if (Stage == 6) { ReleaseFactionAfterUnload(); return; }

                if (Stage >= 4 && Levels != null && Levels.ManagerState != LevelManager.State.Game)

                {

                    FactionCleanupPending=true;RhodesHudIcon.Release();RhodesModeVoice.Reset();LabFrameTiming.Reset();EndfieldSensorTheme.Release();SmallUnitCamera.Reset(); if(RhodesCarrier){RhodesTiltrotorFlightControl.Reset();RhodesOperatorTransport.Reset();RhodesTiltrotorConcealment.Reset();LaevatainFlameBridge.Reset();GilbertaGravityBridge.Reset();ArdeliaHealingBridge.Reset();RhodesResearch.Reset();RhodesDeploymentState.Reset();RhodesCarrierUnits.Reset();RhodesCarrierScenario.Reset();RhodesManualDamage.Reset();RhodesDemoRollback.Reset();} if(HumanoidServicesEnabled){HumanoidFireGate.Reset();PerlicaOrbitalBridge.Reset();HumanoidCasualtyProbe.Reset();HumanoidCorpse.ResetAll();HumanoidAttackAudio.ResetAll();HumanoidUnitVoice.ResetAll();HumanoidRifleFireFx.ResetAll();HumanoidSelection.Reset();HumanoidModel.Reset();} BvmRangeTargets.Reset(); BvmUnitTelemetry.Reset(); BvmNativeState.Unbind(); CustomFlatMap.Reset(); PresentationSim=null; Stage = 6; Log("Lab scene unloaded; cloned terrain restored. Restart the application for another lab run."); return;

                }

                if(Stage>=4&&Stage<6)LabFrameTiming.Frame();

                long sectionStart=LabFrameTiming.Begin();

                if (!UnitsOnly) CustomFlatMap.MainTick();

                if(Stage>=4&&Stage<6)OptionalPresentationServices.TickHud(main);

                LabFrameTiming.End(0,sectionStart);sectionStart=LabFrameTiming.Begin();

                // Camera is isolated with the optional HUD/sensor services above.

                LabFrameTiming.End(1,sectionStart);sectionStart=LabFrameTiming.Begin();

                if(HumanoidServicesEnabled){HumanoidModel.DiscoverActiveUnits();OptionalPresentationServices.TickSelection();if(Stage>=4){PerlicaOrbitalBridge.MainTick();OptionalPresentationServices.TickAudio();}}

                if(!Humanoid)BvmRigDriver.DiscoverActiveUnits();

                if(RhodesCarrier){FactionUnitModels.MainTick();if(Stage>=4&&Stage<6){RhodesTiltrotorFlightControl.MainTick();LaevatainFlameBridge.MainTick();GilbertaGravityBridge.MainTick();ArdeliaHealingBridge.MainTick();}}

                LabFrameTiming.End(2,sectionStart);sectionStart=LabFrameTiming.Begin();

                BvmIdentity.MainTick();

                CustomUnits.UpdateViews();

                LabFrameTiming.End(3,sectionStart);sectionStart=LabFrameTiming.Begin();

                if(RhodesCarrier){RhodesCarrierUnits.ObserveViews();if(Stage>=4&&Stage<6){RhodesManualDamage.MainTick();RhodesDemoRollback.MainTick();}}

                LabFrameTiming.End(4,sectionStart);

                if (Stage == 5) return;

                if (Stage == 0)

                {

                    Levels = (LevelManager)NativeRead.Get(main, "mLevelManager");

                    DLC = (DLCManager)NativeRead.Get(typeof(ShipbreakersMain), "sDLCManager");

                    if (Levels == null || DLC == null || Levels.ManagerState != LevelManager.State.Startup) return;

                    if (Levels.LevelEntriesMP == null || Levels.CommanderAttributes == null || !ApplyRequestedQuality()) return;

                    Deadline = Time.realtimeSinceStartup + 120;

                    Stage = 1;

                    Log("MapUnitLab starting: isolated copy; native offline base map; independent flat-map definition and custom unit; 4K Ultra; normal 1x.");

                }

                if (Stage == 1)

                {

                    if (Time.realtimeSinceStartup > Deadline) throw new Exception("Native startup/preview timeout");

                    PlayerCustomizationPanel panel = (PlayerCustomizationPanel)NativeRead.Get(typeof(PlayerCustomizationPanel), "sPanel");

                    if (panel == null) return;

                    object content = NativeRead.Get(panel, "mContentManager");

                    if (content == null) return;

                    object scene = NativeRead.Get(content, "Scene"), dlc = NativeRead.Get(content, "DLC");

                    if (scene == null || dlc == null || !(bool)NativeRead.Get(scene, "IsLoaded") || !(bool)NativeRead.Get(dlc, "IsLoaded") || NativeRead.Get(scene, "mLoadingCoroutine") != null) return;

                    PlayerCustomizationPanel.UnloadPreviewContent(); Stage = 2; return;

                }

                if (Stage == 2)

                {

                    // Hub navigation is prepared by an external offline tool.

                    // Only a validated cache may proceed to the scene loader.

                    if(HubRegion&&HubRegionNavigation.IsPreparing)return;

                    if(!StartMatch())return;

                    Deadline=Time.realtimeSinceStartup+(HubRegion?300:120);Stage=3;return;

                }

                if (Stage == 3)

                {

                    if (Levels.ManagerState != LevelManager.State.Game)

                    {

                        if (Time.realtimeSinceStartup > Deadline) throw new Exception("Native load timeout: " + Levels.ManagerState);

                        return;

                    }

                    if (!TimeScaleSet)

                    {

                        SimController controller = (SimController)NativeRead.Get(main, "mSimController");

                        if (controller == null) return;

                        controller.TimeScale = Fixed64.FromInt(1); PresentationSim=controller; TimeScaleSet = true;

                    }

                    Stage = 4; FocusDeadline = Time.realtimeSinceStartup + 180;

                    Log("Native simulation active; preparing flat map or explicit native-only unit test.");

                }

                if (Stage == 4 && SpawnDone)

                {

                    Entity[] request; lock (HandoffMutex) request = FocusRequest;

                    if (request == null || request.Length == 0) return;

                    foreach (UnitInterfaceController controller in Resources.FindObjectsOfTypeAll(typeof(UnitInterfaceController)))

                    {

                        if (controller == null || !controller.gameObject.activeInHierarchy) continue;

                        UnitView view = (UnitView)NativeRead.Call(controller, "GetUnitViewForEntity", request[0]);

                        if (view == null) continue;

                        if (!SelectionSent)

                        {

                            controller.SetUnitSelection(request[0]); controller.FocusCameraOnUnit(view);

                            SelectionSent = true; FocusVerifyAt = Time.realtimeSinceStartup + 1f;

                            Log("FOCUS READY: native custom-unit view selected and camera focused; waiting for renderer-state checks, not claiming visual acceptance.");

                            return;

                        }

                        if (Time.realtimeSinceStartup < FocusVerifyAt || !(RhodesCarrier?RhodesCarrierUnits.ViewsVerified:(Humanoid?HumanoidModel.ViewsVerified:CustomUnits.ViewsVerified)))

                        {

                            // Readiness is diagnostic: keep discovery/grounding alive for

                            // later production even if presentation validation is delayed.

                            // Stage 4 remains pending so a late valid view can still recover.

                            if (Time.realtimeSinceStartup > FocusDeadline && !RendererWaitWarningSent)

                            {

                                RendererWaitWarningSent = true;

                                Log("WARNING renderer-state validation still pending; presentation discovery and native simulation services continue; manualReadinessVerified=False");

                            }

                            return;

                        }

                        Log("MANUAL READY: unit selected/focused and renderer-state checks completed; actual picture and sensor-mode appearance still require user visual confirmation. No automatic movement or exit.");

                        Stage = 5; if(Lifecycle)LifecycleLab.ArmCombat(); return;

                    }

                    if (Time.realtimeSinceStartup > FocusDeadline) throw new Exception("Spawn succeeded but custom unit native view/selection did not appear");

                }

            }

            catch (Exception error) { Stop("main stage " + Stage, error); }

        }



        static bool ApplyRequestedQuality()

        {

            if (QualityApplied) return true;

            UserSettings settings = ShipbreakersMain.UserSettings;

            if (settings == null || settings.Video == null || settings.ScreenSettings == null) return false;

            QualitySettingsAsset preset = VideoSettings.GetPresetQualitySettingForLOD(HardwareRequirements.VideoQualityLOD.Ultra);

            if (preset == null || preset.Data == null) return false;

            // Verify the native save target before calling the game's settings writer.

            // Module 9 is the UserSettings directory in this inspected build.

            Type modulePath = typeof(Sim).Assembly.GetType("BBI.Game.IO.ModulePath", true);

            Type module = modulePath.GetNestedType("Module", BindingFlags.Public | BindingFlags.NonPublic);

            if (module == null) throw new Exception("Native settings Module enum missing");

            string settingsPath = Path.GetFullPath((string)NativeRead.Call(modulePath, "GetFullPath", Enum.ToObject(module, 9), "settings.json"));

            string sandboxRoot = Path.GetFullPath(Path.GetDirectoryName(Path.GetDirectoryName(LogPath)));

            string sandboxPrefix = sandboxRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!settingsPath.StartsWith(sandboxPrefix, StringComparison.OrdinalIgnoreCase)) throw new Exception("Native settings path is outside this sandbox: " + settingsPath);

            settings.Video.CopyFrom(preset.Data);

            Resolution resolution = new Resolution(); resolution.width = 3840; resolution.height = 2160;

            settings.ScreenSettings.ScreenResolution = resolution;

            settings.ScreenSettings.Fullscreen = true;

            // This native method calls Video.Apply, ApplyScreenSettings (including

            // Screen.SetResolution(3840,2160,true)), and ApplyQualitySettings.

            settings.ApplyVideoSettings();

            bool saved = settings.SaveToFile();

            if (!saved) throw new Exception("Native Ultra/4K settings save failed: " + settingsPath);

            QualityApplied = true;

            QualityVerifyAt = Time.realtimeSinceStartup + 2;

            Log("Native graphics preset=" + preset.name + "; TextureQuality=" + settings.Video.TextureQuality + "; UnitLODBias=" + settings.Video.UnitLODBias + "; FXLODBias=" + settings.Video.FXLODBias + "; AnisotropicFiltering=" + settings.Video.AnisotropicFiltering + "; requested=3840x2160 fullscreen; saved=" + settingsPath);

            return true;

        }

        static bool StartMatch()

        {

            if(RhodesCarrier&&(Humanoid||HubRegion||UnitsOnly||Lifecycle||File.Exists(Path.Combine(LabDirectory,"MapUnitLab.autoprobe"))))throw new InvalidOperationException("Rhodes carrier trial requires its own flat map and manual controls");

            OptionalPresentationServices.EndMatch();
            RhodesTiltrotorFlightControl.Reset();RhodesOperatorTransport.Reset();RhodesTiltrotorConcealment.Reset();LaevatainFlameBridge.Reset();GilbertaGravityBridge.Reset();ArdeliaHealingBridge.Reset();RhodesResearch.Reset();RhodesDeploymentState.Reset();RhodesCarrierScenario.Reset();RhodesManualDamage.Reset();RhodesDemoRollback.Reset();

            if(HubRegion&&(UnitsOnly||Lifecycle||File.Exists(Path.Combine(LabDirectory,"MapUnitLab.autoprobe"))))throw new InvalidOperationException("HubRegion cannot combine with UnitsOnly, Lifecycle or movement probe");

            if(Humanoid&&(HubRegion||UnitsOnly||Lifecycle||File.Exists(Path.Combine(LabDirectory,"MapUnitLab.autoprobe"))))throw new InvalidOperationException("Humanoid trial requires the independent flat map and manual controls");

            BvmRangeTargets.Reset(); LifecycleLab.Reset(); HumanoidScenario.Reset(); HumanoidFireGate.Reset(); PerlicaOrbitalBridge.Reset(); HumanoidCasualtyProbe.Reset(); HumanoidCorpse.ResetAll(); HumanoidAttackAudio.ResetAll();HumanoidUnitVoice.ResetAll();HumanoidRifleFireFx.ResetAll();

            NullSession session = new NullSession();

            List<CommanderDescription> descriptions = new List<CommanderDescription>();

            descriptions.Add(new CommanderDescription("罗德岛指挥官", Player, session.LocalPlayerID, 0, 0, new TeamID(1), PlayerType.Human, UnitColors.kDefault, Difficulty.None, DLCPackID.kInvalidID, false));

            descriptions.Add(new CommanderDescription("Gaalsien", new CommanderID(2), NetworkPlayerID.kInvalidID, 1, 1, new TeamID(2), PlayerType.AI, UnitColors.kDefault, (Difficulty)Enum.Parse(typeof(Difficulty), "Easy"), DLCPackID.kInvalidID, false));

            LevelDefinition map = null;

            int best = -1;

            foreach (LevelDefinition candidate in Levels.LevelEntriesMP)

            {

                if (candidate == null || candidate.MaxPlayers < descriptions.Count) continue;

                string name = candidate.SceneName == null ? "" : candidate.SceneName.ToLowerInvariant();

                int score = candidate.MaxPlayers;

                if (name.IndexOf("firebase") >= 0) score += 100;

                else if (name.IndexOf("dune") >= 0) score += 60;

                if (score > best) { map = candidate; best = score; }

            }

            if (map == null) throw new Exception("No original multiplayer bootstrap is available");

            if (!UnitsOnly) map = CustomFlatMap.RegisterClone(Levels, map);

            if(HubRegion&&!HubRegionNavigation.EnsurePrepared())return false;

            Dictionary<CommanderID, TeamID> teams = new Dictionary<CommanderID, TeamID>();

            foreach (CommanderDescription description in descriptions) teams.Add(description.CommanderID, description.TeamID);

            List<CommanderSpawnInfo> spawns;

            if (!SpawnAssigner.GenerateSpawnPoints(map, teams, TeamSetting.Team, out spawns)) throw new Exception("Native team spawn assignment failed");

            Dictionary<CommanderID, PlayerSelection> selections = SpawnAssigner.GeneratePlayerSelections(Levels.CommanderAttributes, descriptions, spawns, DLC);

            if(RhodesCarrier)

            {

                foreach(CommanderID id in new CommanderID[]{Player,new CommanderID(2)})

                {

                    PlayerSelection prior=selections[id];CommanderAttributes source=prior.Attributes;

                    CommanderAttributes copy=(CommanderAttributes)typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(source,null);

                    copy.StartingUnits=id==Player?RhodesCarrierScenario.CreateFriendlyFleet():new StartingUnit[0];

                    copy.StartingResource1=id==Player?20000:0;copy.StartingResource2=id==Player?5000:0;

                    if(id==Player)

                    {

                        int priorStartingPop=source.StartingPopCap,priorMaximumPop=source.MaximumPopCap;

                        Fixed64 priorRefund=source.RefundPercentage;

                        NativeRead.Set(copy,"m_StartingPopCap",100);NativeRead.Set(copy,"m_MaximumPopCap",100);NativeRead.Set(copy,"m_RefundPercentage",1f);

                        if(copy.StartingPopCap!=100||copy.MaximumPopCap!=100||copy.RefundPercentage!=Fixed64.FromInt(1)||

                            source.StartingPopCap!=priorStartingPop||source.MaximumPopCap!=priorMaximumPop||source.RefundPercentage!=priorRefund)

                            throw new InvalidOperationException("Rhodes production commander values or source isolation failed");

                        RhodesResearch.BindCommander(copy,id);

                    }

                    selections[id]=new PlayerSelection(copy,prior.Desc);

                    if(object.ReferenceEquals(copy,source))throw new InvalidOperationException("Rhodes starting fleet aliases native commander");

                }

                Log("RHODES START CONFIG: initialFriendly=1_custom_carrier; scaleReferenceAfterSpawn=1_BVM; hostileUnits=0; nativeCarrierAndSupport=0; flatMap=True; scriptedMovement=False; productionEnabled=True; initialResourcesCU=20000; initialResourcesRU=5000; populationCap=100; nativeCancelRefundFraction=1; privateNativeResearchTree=True; resourcesGrantedOnce=True");

            }

            else if(Humanoid)

            {

                foreach(CommanderID id in new CommanderID[]{Player,new CommanderID(2)})

                {

                    PlayerSelection prior=selections[id];CommanderAttributes source=prior.Attributes;

                    CommanderAttributes copy=(CommanderAttributes)typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(source,null);

                    copy.StartingUnits=id==Player?HumanoidScenario.CreateFriendlyFleet():new StartingUnit[0];

                    copy.StartingResource1=0;copy.StartingResource2=0;selections[id]=new PlayerSelection(copy,prior.Desc);

                    if(object.ReferenceEquals(copy,source))throw new InvalidOperationException("Humanoid starting fleet aliases native commander");

                }

                Log("HUMANOID START CONFIG: perlica=1; squadsAfterSpawn=2; membersPerSquad=6; carrier=0; hostileAfterSpawn=1_stationary_range_target; flatMap=True; scriptedMovement=False");

            }

            else if(HubRegion)

            {

                foreach(CommanderID id in new CommanderID[]{Player,new CommanderID(2)})

                {

                    PlayerSelection prior=selections[id];CommanderAttributes source=prior.Attributes;

                    CommanderAttributes copy=(CommanderAttributes)typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(source,null);

                    StartingUnit observer=new StartingUnit();observer.Unit=CustomUnits.UnitKey;observer.Tag="EF_HUB_MANUAL_WALK_TEST";

                    copy.StartingUnits=id==Player?new StartingUnit[]{observer}:new StartingUnit[0];

                    copy.StartingResource1=0;copy.StartingResource2=0;selections[id]=new PlayerSelection(copy,prior.Desc);

                    if(object.ReferenceEquals(copy,source))throw new InvalidOperationException("Hub preview fleet must be independent");

                }

                Log("HUB REGION START CONFIG: initialFriendly=1_BVM; initialHostiles=0; recordingFinalRoster="+(HubRecordingScenario.Enabled?"2_friendly_1_hostile":"1_friendly_0_hostile")+"; nativeCarrierAndSupport=0; playerOrdersScripted=False; fullRegionPathAcceptancePending=True");

            }

            else if(Lifecycle)

            {

                foreach(CommanderID id in new CommanderID[]{Player,new CommanderID(2)})

                {

                    PlayerSelection prior=selections[id];CommanderAttributes source=prior.Attributes;

                    CommanderAttributes copy=(CommanderAttributes)typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(source,null);

                    copy.StartingUnits=id==Player?LifecycleLab.CreateFriendlyFleet():LifecycleLab.CreateEnemyFleet();

                    copy.StartingResource1=0;copy.StartingResource2=0;selections[id]=new PlayerSelection(copy,prior.Desc);

                    if(object.ReferenceEquals(copy,source)||copy.StartingUnits.Length==0)throw new Exception("Lifecycle needs independent nonempty native starting fleets");

                }

                Log("LIFECYCLE START CONFIG: friendly=2_BVM; hostileInitial=native_mining_unit; carrierCount=0; nativeAnnihilationOnly=True; fullUnitCountIncludesAnyHiddenSupport=True; ordinaryTargetRangeDisabled=True");

            }

            else if (!UnitsOnly)

            {

                PlayerSelection originalFriendly=selections[Player];

                CommanderAttributes friendlySource=originalFriendly.Attributes;

                StartingUnit[] sourceFleet=friendlySource.StartingUnits;

                List<StartingUnit> retained=new List<StartingUnit>();int removedLAV=0;

                foreach(StartingUnit entry in sourceFleet)

                    if(string.Equals(entry.Unit,CustomUnits.ControlKey,StringComparison.Ordinal))removedLAV++;

                    else retained.Add(entry);

                CommanderAttributes friendly=(CommanderAttributes)typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(friendlySource,null);

                friendly.StartingUnits=retained.ToArray();selections[Player]=new PlayerSelection(friendly,originalFriendly.Desc);

                if(!object.ReferenceEquals(friendlySource.StartingUnits,sourceFleet)||object.ReferenceEquals(friendly,friendlySource))

                    throw new Exception("Friendly lab starting fleet is not independent");

                Log("LAB_PLAYER_FLEET_FILTER nativeLAVRemovedFromStartingFleet="+removedLAV+"; retainedNativeStartingEntries="+retained.Count+"; carrierAndSupportPreserved=True; originalCommanderAttributesUnchanged=True; manualComparatorLAVSpawned=False");

                CommanderID opponentID = new CommanderID(2);

                PlayerSelection originalSelection = selections[opponentID];

                CommanderAttributes originalAttributes = originalSelection.Attributes;

                int originalCount = originalAttributes.StartingUnits.Length;

                CommanderAttributes emptyAttributes = (CommanderAttributes)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(originalAttributes, null);

                emptyAttributes.StartingUnits = new StartingUnit[0];

                emptyAttributes.StartingResource1 = 0;

                emptyAttributes.StartingResource2 = 0;

                selections[opponentID] = new PlayerSelection(emptyAttributes, originalSelection.Desc);

                if (originalAttributes.StartingUnits.Length != originalCount || object.ReferenceEquals(emptyAttributes, originalAttributes))

                    throw new Exception("Safe lab opponent attributes were not independently cloned");

                Log("SAFE LAB OPPONENT: startingUnits=0; startingResources=0/0; hostileCommanderRetained=True; originalCommanderAttributesUnchanged=True; noAutomaticVictoryFlags=True");

            }

            // Native None=1 is automatically rewritten to CarrierAnnihilation.

            // Zero is an explicitly empty condition mask, retaining two active commanders.

            VictoryConditions conditions = Lifecycle ? VictoryConditions.Annihilation : (UnitsOnly ? VictoryConditions.CarrierAnnihilation : (VictoryConditions)0);

            GameStartSettings settings = new GameStartSettings(selections, conditions, TeamSetting.Team, GameModeSettings.kDefaultSettings, 3650, true, false, AutomatchSizeType.ThreeVSThree);

            DependencyContainerBase dependencies = new DependencyContainer<GameStartSettings, SessionBase>(settings, session);

            if (!UnitsOnly) CustomFlatMap.ConfigureSessionDependencies(dependencies);

            Levels.StartLoadLevel(GameMode.AISkirmish, ReplayMode.ReplaysDisabled, map, dependencies);

            Log("Loading map definition=" + map.SceneName + "; player=Commander1/Coalition/Team1; otherCommander=Commander2/Gaalsien/Team2; flatLabOpponentFleet=empty; offline NullSession; replay disabled.");

            return true;

        }





        public static bool ShouldProcessAirSortie(Entity entity)
        {return !RhodesCarrier||RhodesTiltrotorFlightControl.ShouldProcessAirSortie(entity);}

        public static void SimTick(object sim)

        {

            if(SkirmishMode){
                if(Failed){
                    if(RhodesSkirmishFaction.IsNetworkMatch)throw new InvalidOperationException("Rhodes initialization failed; cannot continue a network simulation");
                    return;
                }
                try{RhodesSkirmishRuntime.SimTick(sim);}catch(Exception error){
                    // A peer must never silently skip authoritative mod writes
                    // while the native lockstep simulation continues.
                    if(RhodesSkirmishFaction.IsNetworkMatch){Log("ERROR RHODES_NETWORK_SIMULATION "+error+"; propagatingToNativeScheduler=True");throw;}
                    Stop("skirmish simulation",error);
                }
                return;
            }

            if (Failed || (Stage != 4 && Stage != 5)) return;

            try

            {

                uint frame = Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim, "GlobalFrameCount"), "FrameNumber"));

                if (frame < 5) return;

                if (!UnitsOnly&&!Lifecycle&&!HubRegion&&!Humanoid&&!RhodesCarrier) BvmRangeTargets.AssertSafeOpponent(sim, frame);

                if (!MapConfigured)

                {

                    object originUnit;Entity leadEntity;

                    if(RhodesCarrier)

                    {

                        originUnit=FindOwnedUnit(sim,RhodesCarrierUnits.UnitKey);

                        if(originUnit==null){if(frame>160)throw new Exception("Rhodes initial carrier did not appear");return;}

                        leadEntity=(Entity)NativeRead.Get(originUnit,"Entity");

                    }

                    else if(Humanoid)

                    {

                        originUnit=FindOwnedUnit(sim,HumanoidUnits.PerlicaKey);

                        if(originUnit==null){if(frame>160)throw new Exception("Humanoid initial Perlica did not appear");return;}

                        leadEntity=(Entity)NativeRead.Get(originUnit,"Entity");

                    }

                    else if(HubRegion)

                    {

                        originUnit=FindOwnedUnit(sim,CustomUnits.UnitKey);

                        if(originUnit==null){if(frame>160)throw new Exception("Hub manual BVM did not appear");return;}

                        leadEntity=(Entity)NativeRead.Get(originUnit,"Entity");

                    }

                    else if(Lifecycle)

                    {

                        if(!LifecycleLab.TryGetLeadBvm(sim,out originUnit,out leadEntity))

                        {if(frame>160)throw new Exception("Lifecycle initial native BVM did not appear");return;}

                    }

                    else

                    {

                        originUnit=FindOwnedUnit(sim,"C_Carrier_MP");

                        if(originUnit==null){if(frame>160)throw new Exception("Coalition carrier not available");return;}

                    }

                    object state=NativeRead.Call(originUnit,"GetState");

                    Vector2r origin=(Vector2r)NativeRead.Get(NativeRead.Get(state,"StartOfFrameDynamicState"),"Position");LabOrigin=origin;

                    if(Lifecycle||HubRegion||Humanoid||RhodesCarrier)CustomFlatMap.ConfigureStandaloneTestArea(origin);

                    else if(!UnitsOnly)CustomFlatMap.ConfigurePlayerTestArea(origin);

                    if(UnitsOnly)Log("UNITS_ONLY: native terrain is left unchanged; validating independent model/unit behavior separately.");

                    MapConfigured=true;

                }                if (!UnitsOnly && !CustomFlatMap.Prepare(sim)) return;

                if (!SpawnDone)

                {

                    if(RhodesCarrier)LabEntities=RhodesCarrierScenario.EnsureRoster(sim);

                    else if(Humanoid)LabEntities=HumanoidScenario.EnsureRoster(sim);

                    else if(HubRegion)

                    {

                        object initial=FindOwnedUnit(sim,CustomUnits.UnitKey);

                        if(initial==null)return;

                        LabEntities=HubRecordingScenario.EnsureRoster(sim,(Entity)NativeRead.Get(initial,"Entity"));

                    }

                    else if(Lifecycle)

                    {

                        object initial;Entity lead;

                        if(!LifecycleLab.TryGetLeadBvm(sim,out initial,out lead))return;

                        LabEntities=CustomUnits.AdoptStartingBvm(sim,lead);

                    }

                    else LabEntities=UnitsOnly?CustomUnits.EnsureSpawnOnNativeMap(sim,LabOrigin):CustomUnits.EnsureSpawn(sim);

                    if (LabEntities == null || LabEntities.Length == 0) return;

                    lock (HandoffMutex) FocusRequest = LabEntities;

                    SpawnDone = true;

                }

                if(RhodesCarrier){RhodesResearch.SimTick(sim);RhodesDemoRollback.SimTick(sim);RhodesDeploymentState.SimTick(sim,LabEntities);RhodesManualDamage.SimTick(sim);RhodesCarrierScenario.Tick(sim);RhodesTiltrotorFlightControl.SimTick(sim);RhodesOperatorTransport.SimTick(sim);RhodesTiltrotorConcealment.SimTick(sim);LaevatainFlameBridge.SimTick(sim);GilbertaGravityBridge.SimTick(sim);ArdeliaHealingBridge.SimTick(sim);HumanoidCasualtyProbe.SimTick(sim);HumanoidWeaponEventBridge.SimTick(sim);PerlicaOrbitalBridge.SimTick(sim);}

                else if(Humanoid){HumanoidCasualtyProbe.SimTick(sim);HumanoidScenario.Tick(sim);HumanoidWeaponEventBridge.SimTick(sim);PerlicaOrbitalBridge.SimTick(sim);}

                else if(Lifecycle)LifecycleLab.Tick(sim);

                else if(!UnitsOnly&&!HubRegion)BvmRangeTargets.Tick(sim,LabEntities[0]);

                if(!FriendlyFleetReported&&frame>=85)

                {

                    Dictionary<string,int> roster=new Dictionary<string,int>();int lavCount=0;

                    foreach(object value in (IEnumerable)NativeRead.Get(NativeRead.Get(sim,"UnitManager"),"Units"))

                    {

                        Entity id=(Entity)NativeRead.Get(value,"Entity");

                        if((CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",id)!=Player)continue;

                        string key=(string)NativeRead.Get(value,"TypeName");int count;

                        roster.TryGetValue(key,out count);roster[key]=count+1;

                        if(key==CustomUnits.ControlKey)lavCount++;

                    }

                    List<string> names=new List<string>(roster.Keys);names.Sort(StringComparer.Ordinal);List<string> parts=new List<string>();

                    foreach(string name in names)parts.Add(name+":"+roster[name]);

                    Log("LAB_FRIENDLY_FLEET_VERIFIED units="+string.Join(",",parts.ToArray())+"; nativeLAVCount="+lavCount+"; explicitControlLAVSpawned=False; nativeDefinitionsUnchanged=True");

                    FriendlyFleetReported=true;

                }

                if(DetailedDiagnosticsEnabled)BvmUnitTelemetry.Tick(sim);

                LabProbe.Tick(sim, LabEntities[0]);

            }

            catch (Exception error) { Stop("simulation", error); }

        }

        internal static object FindOwnedUnit(object sim, string key)

        {

            foreach (object unit in (IEnumerable)NativeRead.Get(NativeRead.Get(sim, "UnitManager"), "Units"))

                if ((string)NativeRead.Get(unit, "TypeName") == key && (CommanderID)NativeRead.Call(typeof(Sim), "GetEntityCommanderID", (Entity)NativeRead.Get(unit, "Entity")) == Player) return unit;

            return null;

        }

        static void Stop(string where, Exception error)

        {

            Failed = true;RhodesDeploymentState.Suspend(); Log("ERROR " + where + ": " + error + "; experiment driver stopped; game remains open.");

        }

        static string FindLogPath()

        {

            DirectoryInfo folder = new DirectoryInfo(Path.GetDirectoryName(typeof(Runtime).Assembly.Location));

            for (DirectoryInfo current = folder; current != null; current = current.Parent)

                if (string.Equals(current.Name, "BepInEx", StringComparison.OrdinalIgnoreCase)) return Path.Combine(current.FullName, "MapUnitLab.Runtime.log");

            return Path.Combine(folder.FullName, "MapUnitLab.Runtime.log");

        }

        public static string LabDirectory { get { return Path.GetDirectoryName(LogPath); } }

        public static void Log(string message)

        {

            lock (LogMutex) try { File.AppendAllText(LogPath, DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine); } catch { }

        }

    }



    // Compatibility surface; metadata caching belongs to the reusable core.
    internal static class NativeRead
    {
        public static object Get(object value,string name)
        {return ModCore.NativeAccessCache.Get(value,name);}
        public static bool TryGet(object value,string name,out object result)
        {return ModCore.NativeAccessCache.TryGet(value,name,out result);}
        public static void Set(object value,string name,object result)
        {ModCore.NativeAccessCache.Set(value,name,result);}
        public static object Call(object value,string name,params object[] args)
        {return ModCore.NativeAccessCache.Call(value,name,args);}
    }

}

