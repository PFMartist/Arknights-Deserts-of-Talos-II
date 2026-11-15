using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BBI.Unity.Game.UI;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Lifecycle-scoped skin adapter. A game-stage bind follows the native HUD
    // roots and explicit cross-tree references. No recurring scene/UI scans,
    // layout, input, shared atlas, replacement chain or simulation changes.
    public static class EndfieldHudTheme
    {
        const string Prefix = "EF_HUD_";
        const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        static readonly Color Yellow = new Color(238f/255f,229f/255f,46f/255f,1f);
        static readonly Color White = new Color(236f/255f,237f/255f,231f/255f,1f);
        static readonly string[] TextureNames = { "Atlas-ControlStrip", "Atlas-HUD", "Atlas-WorldHUD", "Atlas-FrontEnd" };
        static readonly Dictionary<UIAtlas,UIAtlas> Atlases = new Dictionary<UIAtlas,UIAtlas>();
        static readonly HashSet<UIAtlas> FailedAtlases = new HashSet<UIAtlas>();
        static readonly Dictionary<UISprite,AtlasRoute> SpriteAtlases = new Dictionary<UISprite,AtlasRoute>();
        static readonly Dictionary<UIWidget,Color> WidgetColours = new Dictionary<UIWidget,Color>();
        static readonly Dictionary<FieldKey,FieldSnapshot> FieldChanges = new Dictionary<FieldKey,FieldSnapshot>();
        static readonly Dictionary<Type,FieldInfo[]> SerializedFields = new Dictionary<Type,FieldInfo[]>();
        static readonly Dictionary<string,UIAtlas> MenuAtlases = new Dictionary<string,UIAtlas>(StringComparer.Ordinal);
        static readonly HashSet<string> Warnings = new HashSet<string>(StringComparer.Ordinal);
        static readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();
        static readonly HashSet<GameObject> BoundHudRoots = new HashSet<GameObject>();
        static readonly Dictionary<UISprite,List<UIButton>> LinkedMenuButtons = new Dictionary<UISprite,List<UIButton>>();
        static readonly HashSet<UISprite> MenuSprites = new HashSet<UISprite>();
        static readonly Dictionary<string,UIWidget> ResourceLabels = new Dictionary<string,UIWidget>(StringComparer.Ordinal);
        static readonly HashSet<string> ReferenceGroups = new HashSet<string>(StringComparer.Ordinal);
        static readonly Dictionary<UIPopupList,UIAtlas> PopupOriginalAtlases = new Dictionary<UIPopupList,UIAtlas>();
        static readonly MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance | BindingFlags.NonPublic);
        static GameObject Templates;
        static bool ConfigLoaded,Enabled,Restoring,GameHudBound,GameHudFallbackScanned,GameHudPassCompleted,GameHudLateRetryUsed;
        static float GameHudRetryAt;
        static string DirectoryPath;
        static int PanelReports,IconReports,AtlasReports,ReferenceReports,ResourceReports,ReferenceRoots,MenuMatches,ResearchReports,OpenReports,ProductionReports;
        public static bool Active { get { return IsEnabled(); } }

        struct FieldKey : IEquatable<FieldKey>
        {
            public object Target;
            public FieldInfo Field;
            public bool Equals(FieldKey other) { return object.ReferenceEquals(Target,other.Target) && Field.Equals(other.Field); }
            public override bool Equals(object other) { return other is FieldKey && Equals((FieldKey)other); }
            public override int GetHashCode() { return RuntimeHelpers.GetHashCode(Target) ^ Field.GetHashCode(); }
        }
        sealed class FieldSnapshot { public object Target,Original; public FieldInfo Field; }
        sealed class AtlasRoute { public UIAtlas Original,Applied; }
        sealed class IdentityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object left,object right) { return object.ReferenceEquals(left,right); }
            public int GetHashCode(object value) { return RuntimeHelpers.GetHashCode(value); }
        }
        sealed class MenuSprite
        {
            public string Name;
            public int Width,Height;
            public bool Active;
        }

        public static void PanelInitialized(object panel)
        {
            RhodesNetworkCompatibility.PanelInitialized(panel);
            RhodesLoadingScreen.PanelInitialized(panel);
            RhodesOperatorReticleLifetime.ObservePanel(panel);
            EndfieldMenuBackground.PanelInitialized(panel);
            RhodesCustomizationPreview.PreparePanel(panel);
            RhodesPowerUI.Bind(panel);
            RhodesEngineeringLayout.PanelObserved(panel);
            if(!IsEnabled())return;
            try
            {
                Component root=panel as Component;
                if(root==null)return;
                ApplySubtree(root);
                ApplyControllerReferences(root);
                if(PanelReports++<8)Runtime.Log("ENDFIELD_HUD_PANEL themed="+root.GetType().Name+"; eventRootAndKnownUIReferences=True; nativeLayoutAndInputUnchanged=True");
            }
            catch(Exception error){Warn("panel",error);}
        }

        // Call on the main thread in Stage 4. Repeated calls after success are
        // constant-time; an absent UISystem only retries these finite references.
        public static void BindGameHud(object main)
        {
            // Functional layout must also bind when the optional skin is off.
            RhodesEngineeringLayout.BindGameHud(main);
            if(!IsEnabled() || GameHudBound)return;
            bool latePass=GameHudPassCompleted;
            if(latePass)
            {
                if(GameHudLateRetryUsed || Time.realtimeSinceStartup<GameHudRetryAt)return;
                GameHudLateRetryUsed=true;
            }
            else
            {
                // Consume the first pass even when no HUD exists yet. There is
                // always at most one delayed completion pass, never a frame loop.
                GameHudPassCompleted=true;GameHudRetryAt=Time.realtimeSinceStartup+1f;
            }
            try
            {
                object ui=null;
                if(main!=null)NativeRead.TryGet(main,"mUISystem",out ui);
                if(ui==null)NativeRead.TryGet(typeof(UISystem),"mUISystemInstance",out ui);
                Component system=ui as Component;
                if(system==null)
                {
                    Runtime.Log("ENDFIELD_HUD_BOUND complete=False; nativeSystemMissing=True; lateRetryUsed="+GameHudLateRetryUsed+"; lateRetryScheduled="+(!GameHudLateRetryUsed)+"; recurringSceneScan=False");return;
                }
                List<Component> roots=new List<Component>();
                AddRootList(roots,ReadOptional(ui,"HUDPanels"));
                AddRootList(roots,ReadOptional(ui,"ListOfUIComponentsToDisableIfControlStripIsOff"));
                if(roots.Count==0)
                {
                    AddRootList(roots,ReadOptional(ui,"m_HUDPanels"));
                    AddRootList(roots,ReadOptional(ui,"m_ListOfUIComponentsToDisableIfControlStripIsOff"));
                }
                int nativeRootCount=roots.Count,controllers=0,beforeReferences=ReferenceRoots;
                IEnumerable panels=ReadOptional(ui,"mBBIPanels") as IEnumerable;
                if(panels!=null)foreach(object item in panels)
                {
                    Component controller=item as Component;if(controller==null)continue;
                    controllers++;RhodesPowerUI.Bind(controller);ApplyControllerReferences(controller);
                }
                // Active scene panels only: never Resources assets or prefabs.
                // The fallback is allowed once and only when native HUD roots
                // and explicit controller references are unavailable.
                int fallbackRoots=0;
                if(roots.Count==0 && ReferenceRoots==beforeReferences && !GameHudFallbackScanned)
                {
                    GameHudFallbackScanned=true;
                    foreach(UIPanel panel in UnityEngine.Object.FindObjectsOfType(typeof(UIPanel)))
                        if(panel!=null){AddRoot(roots,panel);fallbackRoots++;}
                }
                int applied=0;
                foreach(Component root in roots)
                {
                    if(root==null)continue;
                    bool covered=false;
                    foreach(Component other in roots)
                        if(other!=null && other!=root && root.transform!=other.transform && root.transform.IsChildOf(other.transform)){covered=true;break;}
                    if(covered)continue;
                    bool added=BoundHudRoots.Add(root.gameObject);if(!added && !latePass)continue;
                    ApplySubtree(root);applied++;
                }
                bool anyRoots=applied>0 || ReferenceRoots>beforeReferences;
                int liveMenus=0,menuAtlasReferences=0,liveResources=0;
                foreach(UISprite menu in MenuSprites)if(menu!=null)
                {
                    liveMenus++;if(menu.atlas!=null && menu.atlas.name.StartsWith(Prefix+"Menu_",StringComparison.Ordinal))menuAtlasReferences++;
                }
                foreach(UIWidget label in ResourceLabels.Values)if(label!=null)liveResources++;
                bool unitGroups=ReferenceGroups.Contains("Information") && ReferenceGroups.Contains("Command") && ReferenceGroups.Contains("ControlGroup") && ReferenceGroups.Contains("Fleet");
                bool anchorsReady=anyRoots && liveMenus>0 && menuAtlasReferences>0 && liveResources>=2 && unitGroups;
                // Even ready first-pass anchors receive one finite late pass,
                // covering controls finalized after native panel initialization.
                GameHudBound=latePass && anchorsReady;
                Runtime.Log("ENDFIELD_HUD_BOUND complete="+GameHudBound+"; anchorsReady="+anchorsReady+"; nativeRoots="+nativeRootCount+"; appliedRoots="+applied+"; cachedRoots="+BoundHudRoots.Count+
                    "; controllers="+controllers+"; crossTreeRoots="+(ReferenceRoots-beforeReferences)+"; widgets="+WidgetColours.Count+
                    "; atlasRoutes="+SpriteAtlases.Count+"; menuSpriteInstances="+liveMenus+"; menuRoutedSpriteInstances="+menuAtlasReferences+"; menuMatches="+MenuMatches+"; menuAtlases="+MenuAtlases.Count+
                    "; resourceLabelRefs="+liveResources+"; unitPanelGroupsReady="+unitGroups+"; informationReady="+ReferenceGroups.Contains("Information")+
                    "; commandReady="+ReferenceGroups.Contains("Command")+"; controlGroupReady="+ReferenceGroups.Contains("ControlGroup")+"; fleetReady="+ReferenceGroups.Contains("Fleet")+
                    "; fallbackSceneRoots="+fallbackRoots+"; lateRetryUsed="+GameHudLateRetryUsed+
                    "; lateRetryScheduled="+(!GameHudLateRetryUsed)+"; recurringSceneScan=False; nativeLayoutAndInputUnchanged=True");
                int samples=0;foreach(KeyValuePair<string,UIWidget> label in ResourceLabels)
                    if(label.Value!=null && samples++<4)Runtime.Log("ENDFIELD_HUD_RESOURCE_CURRENT field="+label.Key+"; color="+ColourText(label.Value.color)+"; actualSceneLabel=True");
            }
            catch(Exception error){Warn("bind-game-hud",error);}
        }
        static object ReadOptional(object target,string name)
        {
            object result;return target!=null && NativeRead.TryGet(target,name,out result)?result:null;
        }
        static void AddRootList(List<Component> roots,object value)
        {
            IEnumerable list=value as IEnumerable;if(list==null)return;
            foreach(object item in list)AddRoot(roots,item);
        }
        static void AddRoot(List<Component> roots,object value)
        {
            Component component=value as Component;GameObject node=value as GameObject;
            if(component==null && node!=null)component=node.transform;
            if(component==null)return;
            foreach(Component existing in roots)if(existing!=null && existing.gameObject==component.gameObject)return;
            roots.Add(component);
        }
        static void ApplyControllerReferences(Component controller)
        { ApplyControllerReferences(controller,new HashSet<Component>()); }
        static void ApplyControllerReferences(Component controller,HashSet<Component> visiting)
        {
            if(controller==null || !visiting.Add(controller))return;
            string name=controller.GetType().Name;string[] paths=null;
            if(name=="MenuBarController")paths=new string[]{"m_MenuButton","m_GameOverContinueButton","m_MenuPanel"};
            else if(name=="PlayerSlotController")paths=new string[]{"m_FactionPopup","m_TeamPopup","m_AIDifficultyPopup","m_PlayerNameLabel","m_RemovePlayerButton","m_HostPlayerSprite","m_PendingFriendName","m_CancelInviteButton"};
            else if(name=="MessageBoxPanel")paths=new string[]{"m_PanelContainer","m_YesButton","m_NoButton","m_OKButton","m_MessageLoc","m_CountdownLabel","m_YesLoc","m_NoLoc","m_OKLoc"};
            else if(name=="InGameMenuPanel")paths=new string[]{"m_ReturnButton","m_RestartButton","m_SaveButton","m_LoadButton","m_LoadLastCheckpointButton","m_OptionsButton","m_QuitButton","m_QuitLabel","m_ButtonGrid","m_OptionsPanel"};
            else if(name=="OptionsPanel")paths=new string[]{"m_CancelButton","m_AcceptButton","m_OptionsPanels","m_OptionsTabToggles"};
            else if(name=="OptionsAudioPanel")paths=new string[]{"m_MasterVolumeSlider","m_MusicVolumeSlider","m_EffectsVolumeSlider","m_SpeechVolumeSlider","m_MasterVolumeLabel","m_MusicVolumeLabel","m_EffectsVolumeLabel","m_SpeechVolumeLabel","m_SubtitlesToggle","m_SoundQualityPopup","m_VoiceCountPopup","m_ResetDefaultsButton"};
            else if(name=="OptionsGameplayPanel")paths=new string[]{"m_LanguagePopupList","m_InvertMouseScroll","m_CameraEdgePanSpeedSlider","m_CameraOrbitHorizSpeedSlider","m_CameraOrbitVertSpeedSlider","m_CameraZoomAutoPitchToggle","m_CameraResetOnExitSensorsToggle","m_CameraLockCursorOnOrbitToggle","m_GlobalChatEnabledToggle","m_TeamChatEnabledToggle","m_AutoSaveReplaysToggle","m_MultiplayerTacticalViewToggle","m_UnitFocusOnSelectToggle","m_UnitStickySelectionToggle","m_ResetDefaultsButton","m_ClearUserData"};
            else if(name=="OptionsVideoPanel")paths=new string[]{"m_PresetsPopup","m_SetToDefaultsButton",
                "m_GraphicsOptionsBindings.FullscreenToggle","m_GraphicsOptionsBindings.VSyncToggle","m_GraphicsOptionsBindings.ResolutionPopupList","m_GraphicsOptionsBindings.TextureQualityPopup","m_GraphicsOptionsBindings.EnableFogToggle","m_GraphicsOptionsBindings.EnabledBloomToggle","m_GraphicsOptionsBindings.EnableAAToggle","m_GraphicsOptionsBindings.AnisotropicFilteringSlider","m_GraphicsOptionsBindings.ShadowQualityPopup","m_GraphicsOptionsBindings.DecalDrawDistancePopup",
                "m_FXOptionsBindings.WheelFXToggle","m_FXOptionsBindings.ImpactFXToggle","m_FXOptionsBindings.DamageFXToggle","m_FXOptionsBindings.DynamicDecalBudgetPopup","m_FXOptionsBindings.FXMaximumLODPopup","m_FXOptionsBindings.FXLODBiasPopup",
                "m_UnitOptionsBindings.NLIPSToggle","m_UnitOptionsBindings.UnitLODBiasPopup","m_UnitOptionsBindings.UnitMaxLODPopup","m_UnitOptionsBindings.CarcassPersistencePopup","m_UnitOptionsBindings.TracksPersistencesPopup"};
            else if(name=="ResearchButtonController")paths=new string[]{"m_ResizeWidgets","m_IconSprite","m_ResearchLabel","m_Resourece1CostLabel","m_Resourece2CostLabel","m_ProgressBar","m_TimeRemainingLabel","m_ProgressContainer","m_CostContainer","m_ButtonHandler","m_BackgroundHandler","m_CancelButtonHandler","mStartResearchButton"};
            else if(name=="ProductionQueueController")paths=new string[]{"m_ConstructionProgressBar","m_CancelCurrentButton","m_CurrentProductionIcon","m_QueueGrid"};
            else if(name=="UnitInterfaceController")paths=new string[]{
                "mInformationPanelSettings.mInformationPanelContainer","mInformationPanelSettings.mInformationPanelView",
                "mCommandPanelSettings.m_CommandPanelContainer","mCommandPanelSettings.m_ProductionGridContainer","mCommandPanelSettings.m_HangarAbilityGridContainer","mCommandPanelSettings.m_AlwaysActiveGridContainer",
                "mCommandPanelSettings.m_ProductionGrid.m_Grid","mCommandPanelSettings.m_CommonGrid.m_Grid","mCommandPanelSettings.m_AbilitiesGrid.m_Grid",
                "mCommandPanelSettings.m_HangarDeployGrid.m_Grid","mCommandPanelSettings.m_HangarRecallGrid.m_Grid","mCommandPanelSettings.m_AlwaysActiveGrid.m_Grid",
                "mControlGroupSettings.m_GridCenter","mFleetControllerSettings.mFleetPanelView","mFleetControllerSettings.mFleetTable","mFleetControllerSettings.m_FleetTableBackground",
                "mProductionQueueSettings.m_QueueDisplayGrid","mUnitHangarPanelSettings.m_HangarParentObject",
                "mPowerShuntPanelSettings.m_PowerShuntPanel","mPowerShuntPanelSettings.mTogglePanelUIGameObjectReference",
                "mPowerShuntPanelSettings.m_TemperatureGaugeAnchor","mPowerShuntPanelSettings.m_PowerSystemControllerTopLeftAnchor",
                "mPowerShuntPanelSettings.m_PowerSystemControllerTopRightAnchor","mPowerShuntPanelSettings.m_PowerSystemControllerBottomLeftAnchor",
                "mPowerShuntPanelSettings.m_PowerSystemControllerBottomRightAnchor","mPowerShuntPanelSettings.m_ReservePowerPoolEventHandler",
                "mPowerShuntPanelSettings.m_ReservePowerPoolLabel","mPowerShuntPanelSettings.m_OverheatWarning","mPowerShuntPanelSettings.m_CoolingLabel",
                "mPowerShuntPanelSettings.m_TooltipAnchor","mPowerShuntPanelSettings.m_HighlightPanelGameObject",
                "mPowerShuntPanelSettings.m_HighlightTopLeftGameObject","mPowerShuntPanelSettings.m_HighlightTopRightGameObject",
                "mPowerShuntPanelSettings.m_HighlightBottomLeftGameObject","mPowerShuntPanelSettings.m_HighlightBottomRightGameObject"};
            else if(name=="ResourcePanelController")paths=new string[]{"m_Resource1Label","m_Resource2Label","m_HarvestersResource1Label","m_HarvestersResource2Label","m_CurrentPopCapLabel","m_MaxPopCapLabel","mTogglePanelUIGameObjectReference","m_HighlightResourcePanelGameObject","m_HighlightPopCapPanelGameObject"};
            else if(name=="PortraitController")paths=new string[]{"m_PortraitSprite","m_TextLabel","m_Panel"};
            else if(name=="SensorManagerToggleController")paths=new string[]{"m_SensorsButton"};
            if(paths==null)return;
            foreach(string path in paths)
            {
                try
                {
                    object value=controller;foreach(string part in path.Split('.')){value=ReadOptional(value,part);if(value==null)break;}
                    IEnumerable list=value as IEnumerable;
                    if(list!=null && !(value is string)){foreach(object item in list)ApplyKnownReference(name,path,item,visiting);}
                    else ApplyKnownReference(name,path,value,visiting);
                }
                catch(Exception error){Warn("reference:"+name+"."+path,error);}
            }
        }
        static void ApplyKnownReference(string owner,string path,object value,HashSet<Component> visiting)
        {
            Component component=value as Component;GameObject node=value as GameObject;if(component==null && node!=null)component=node.transform;
            if(component==null)return;
            UIWidget widget=component as UIWidget;Color before=widget==null?Color.clear:widget.color;
            // Only finite, audited scene fields are followed, never prefabs.
            ApplySubtree(component);BoundHudRoots.Add(component.gameObject);ReferenceRoots++;
            if(path.StartsWith("mInformationPanelSettings.",StringComparison.Ordinal))ReferenceGroups.Add("Information");
            else if(path.StartsWith("mCommandPanelSettings.",StringComparison.Ordinal))ReferenceGroups.Add("Command");
            else if(path.StartsWith("mControlGroupSettings.",StringComparison.Ordinal))ReferenceGroups.Add("ControlGroup");
            else if(path.StartsWith("mFleetControllerSettings.",StringComparison.Ordinal))ReferenceGroups.Add("Fleet");
            if(owner=="ResourcePanelController" && widget!=null)ResourceLabels[path]=widget;
            if(owner=="ResourcePanelController" && widget!=null && ResourceReports++<4)
                Runtime.Log("ENDFIELD_HUD_RESOURCE field="+path+"; before="+ColourText(before)+"; after="+ColourText(widget.color)+"; sceneLabelReference=True");
            if(ReferenceReports++<12)Runtime.Log("ENDFIELD_HUD_REFERENCE owner="+owner+"; field="+path+"; target="+component.gameObject.name+"; crossTreeReference=True");
            ApplyControllerReferences(component,visiting);
        }
        static string ColourText(Color colour)
        {
            return colour.r.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture)+","+colour.g.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture)+","+
                colour.b.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture)+","+colour.a.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture);
        }

        public static void IconSpawned(object icon)
        {
            RhodesHangarStateColours.RegisterIcon(icon);
            if(!IsEnabled())return;
            try
            {
                Component root=icon as Component;
                if(root==null)return;
                ApplyIconStateCaches(root);
                ApplySubtree(root);
                if(IconReports++<4)Runtime.Log("ENDFIELD_HUD_ICON themed="+root.GetType().Name+"; pooledSpawnOnly=True; nativeIconNamesRetained=True");
            }
            catch(Exception error){Warn("icon",error);}
        }

        public static void ResearchItemInitialized(object item)
        {
            RhodesEngineeringLayout.ResearchItemObserved(item);
            if(!IsEnabled())return;
            try
            {
                Component root=item as Component;if(root==null)return;
                ApplySubtree(root);ApplyControllerReferences(root);
                if(ResearchReports++<12)Runtime.Log("ENDFIELD_RESEARCH_ITEM themed="+root.gameObject.name+"; nativeInitializeComplete=True; dynamicEntryOnly=True; simulationAndCostsUnchanged=True");
            }
            catch(Exception error){Warn("research-item",error);}
        }
        // Production queues are instantiated after the HUD roots are bound.
        // Their native Awake creates the fixed queued-slot children first;
        // theme only that live instance, once, after its native setup completes.
        public static void ProductionQueueInitialized(object item)
        {
            if(!IsEnabled())return;
            try
            {
                Component root=item as Component;if(root==null)return;
                ApplySubtree(root);ApplyControllerReferences(root);
                if(ProductionReports++<8)Runtime.Log("ENDFIELD_PRODUCTION_QUEUE themed="+root.gameObject.name+"; nativeAwakeComplete=True; slotsAlreadyCreated=True; sceneScan=False; productionAndInputUnchanged=True");
            }
            catch(Exception error){Warn("production-queue",error);}
        }
        public static void PanelOpened(object panel)
        {
            if(!IsEnabled())return;
            try
            {
                Component root=panel as Component;if(root==null)return;
                ApplySubtree(root);ApplyControllerReferences(root);
                if(OpenReports++<12)Runtime.Log("ENDFIELD_HUD_OPEN themed="+root.GetType().FullName+"; nativeOpenComplete=True; optionsValuesAndBindingsUnchanged=True; atlasRoutes="+SpriteAtlases.Count);
            }
            catch(Exception error){Warn("panel-open",error);}
        }
        // Lobby rows are instantiated after their parent panel is initialized.
        // Follow the known controls only: the native team colour and player
        // banner are commander data, so never theme the entire row subtree.
        public static void PlayerSlotInitialized(object slot)
        {
            if(!IsEnabled())return;
            try
            {
                Component root=slot as Component;
                if(root==null || root.GetType().FullName!="BBI.Unity.Game.UI.PlayerSlotController")return;
                ApplyControllerReferences(root);
                if(OpenReports++<12)Runtime.Log("ENDFIELD_PLAYER_SLOT themed=True; nativeInitializeComplete=True; controlReferencesOnly=True; teamAndPlayerColoursUnchanged=True; sceneScan=False");
            }
            catch(Exception error){Warn("player-slot",error);}
        }
        static void ApplyIconStateCaches(Component icon)
        {
            if(!HasBaseType(icon.GetType(),"BBI.Unity.Game.HUD.NGUIIconController"))return;
            HashSet<object> seen=new HashSet<object>(new IdentityComparer());
            IEnumerable objects=ReadOptional(icon,"m_IconObjects") as IEnumerable;
            if(objects!=null)foreach(object value in objects)ApplyIconContainer(value,seen);
            IDictionary variables=ReadOptional(icon,"mVisualizationVariables") as IDictionary;
            if(variables!=null)foreach(object value in variables.Values)ApplyIconContainer(value,seen);
        }
        static bool HasBaseType(Type type,string name)
        {for(Type current=type;current!=null;current=current.BaseType)if(current.FullName==name)return true;return false;}
        static void ApplyIconContainer(object container,HashSet<object> seen)
        {
            if(container==null || container.GetType().FullName!="BBI.Unity.Game.HUD.NGUIIconController+VisualizationContainer" || !seen.Add(container))return;
            // Runtime caches owned by this icon; preserve container identity.
            foreach(string field in new string[]{"mInitialColour","mInitialBackgroundColour","mOverridenColour","mOverridenBackgroundColour"})MapNamedColourField(container,field);
            MapAtlasField(container,"mIconAtlas");
            HashSet<GameObject> walking=new HashSet<GameObject>();
            foreach(string field in new string[]{"m_Sprite","m_Label","m_Button","m_ProgressBar"})
            {
                Component widget=ReadOptional(container,field) as Component;if(widget!=null)ApplySubtree(widget,walking);
            }
        }

        // Pure numerical work after the one-time configuration read. Mapping is
        // idempotent, so overlapping panel and pooled-icon hooks do not dim it.
        public static Color MapColour(Color input)
        {
            if(!IsEnabled())return input;
            return MapValue(input);
        }
        public static Color MapIconColour(object icon,string variable,Color input)
        {
            if(RhodesHangarStateColours.IsForeground(icon,variable))return input;
            return MapColour(input);
        }
        public static Color MapContainerColour(object container,Color input)
        {
            if(RhodesHangarStateColours.IsForegroundContainer(container))return input;
            return MapColour(input);
        }
        static Color MapValue(Color input)
        {
            float r=input.r,g=input.g,b=input.b;
            if(input.a<=0f || float.IsNaN(r) || float.IsNaN(g) || float.IsNaN(b) ||
                r<0f || g<0f || b<0f || r>1f || g>1f || b>1f)return input;
            float high=Math.Max(r,Math.Max(g,b)),low=Math.Min(r,Math.Min(g,b)),span=high-low;
            if(high<.001f || span<.035f || span/high<.10f)return input;
            float hue;
            if(high==r)hue=(g-b)/span;
            else if(high==g)hue=2f+(b-r)/span;
            else hue=4f+(r-g)/span;
            hue/=6f;if(hue<0f)hue+=1f;
            // Keep threat red and health/friendly green, including dim states.
            if(hue<.065f || hue>.94f || (hue>=.24f && hue<.46f))return input;
            if(hue>=.065f && hue<.24f)
            {
                float value=Math.Min(high,Yellow.r),scale=value/Yellow.r;
                return new Color(Yellow.r*scale,Yellow.g*scale,Yellow.b*scale,input.a);
            }
            if(hue>=.46f && hue<=.80f)
            {
                float value=Math.Min(high,White.g),scale=value/White.g;
                return new Color(White.r*scale,White.g*scale,White.b*scale,input.a);
            }
            return input;
        }

        // Only the four exact native texture names qualify. In particular,
        // EF_BVM atlases with appended sprites must retain their own texture.
        public static UIAtlas ThemeAtlas(UIAtlas native)
        { return RhodesResearch.MapResearchAtlas(OperatorSkillIcons.MapAtlas(OperatorPortraitIcons.MapAtlas(RhodesHudIcon.MapAtlas(ThemeAtlasCore(native))))); }

        static UIAtlas ThemeAtlasCore(UIAtlas native)
        {
            if(native==null || !IsEnabled())return native;
            try
            {
                if(IsPrivate(native) || FailedAtlases.Contains(native))return native;
                UIAtlas found;if(Atlases.TryGetValue(native,out found))return found==null?native:found;
                UIAtlas source=ResolveAtlas(native);
                if(source==null || IsPrivate(source) || source.texture==null)return native;
                string textureName=source.texture.name;
                bool known=false;foreach(string name in TextureNames)if(string.Equals(textureName,name,StringComparison.Ordinal)){known=true;break;}
                if(!known)return native;
                if(Atlases.TryGetValue(source,out found)){Atlases[native]=found;return found;}
                Texture2D texture=null;Material material=null;GameObject node=null;
                try
                {
                    texture=LoadTexture(textureName+".png");
                    if(texture.width!=source.texture.width || texture.height!=source.texture.height)
                        throw new InvalidDataException("Themed atlas size differs from native: "+textureName);
                    texture.name=Prefix+textureName;texture.filterMode=source.texture.filterMode;texture.wrapMode=source.texture.wrapMode;
                    material=new Material(source.spriteMaterial);material.name=Prefix+"Material_"+textureName;material.hideFlags=HideFlags.HideAndDontSave;material.mainTexture=texture;
                    EnsureTemplates();node=new GameObject(Prefix+textureName);node.transform.parent=Templates.transform;node.hideFlags=HideFlags.HideAndDontSave;
                    UIAtlas result=node.AddComponent<UIAtlas>();result.spriteMaterial=material;result.pixelSize=source.pixelSize;
                    List<UISpriteData> sprites=new List<UISpriteData>();foreach(UISpriteData item in source.spriteList)sprites.Add(CopySprite(item));
                    result.spriteList=sprites;result.MarkSpriteListAsChanged();
                    Owned.Add(texture);Owned.Add(material);Atlases[source]=result;Atlases[native]=result;
                    if(AtlasReports++<6)Runtime.Log("ENDFIELD_HUD_ATLAS texture="+textureName+"; size="+texture.width+"x"+texture.height+"; spriteCount="+sprites.Count+"; nativeAtlasUntouched=True; nativeShaderAndPMA=True");
                    return result;
                }
                catch
                {
                    if(node!=null)UnityEngine.Object.DestroyImmediate(node);
                    if(material!=null)UnityEngine.Object.DestroyImmediate(material);
                    if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);
                    throw;
                }
            }
            catch(Exception error){FailedAtlases.Add(native);Warn("atlas:"+native.name,error);return native;}
        }

        static void ApplySubtree(Component root)
        { ApplySubtree(root,new HashSet<GameObject>()); }
        static void ApplySubtree(Component root,HashSet<GameObject> walking)
        {
            if(root==null || !walking.Add(root.gameObject))return;
            Component[] components=root.GetComponentsInChildren<Component>(true);
            // Snapshot all current widget colours before a button setter can
            // initialize its native cached colours or start a native tween.
            foreach(Component component in components)
            {
                UIWidget widget=component as UIWidget;
                if(widget!=null && !WidgetColours.ContainsKey(widget))WidgetColours.Add(widget,widget.color);
            }
            foreach(Component component in components)
            {
                if(component==null || component is Transform || component is UIAtlas)continue;
                try
                {
                    UIButtonColor button=component as UIButtonColor;
                    if(button!=null){ApplyButton(button);ApplyButtonTargets(button,walking);}
                    ApplySerializedColours(component);
                    UIPopupList popup=component as UIPopupList;
                    if(popup!=null)
                    {
                        UIAtlas originalPopup=UnthemedAtlas(popup.atlas);
                        MapAtlasField(popup,"atlas");
                        if(popup.atlas!=originalPopup && !PopupOriginalAtlases.ContainsKey(popup))PopupOriginalAtlases.Add(popup,originalPopup);
                        GameObject child=ReadOptional(popup,"mChild") as GameObject;if(child!=null)ApplySubtree(child.transform,walking);
                    }
                    UISprite sprite=component as UISprite;
                    if(sprite!=null)
                    {
                        if(IsMenuName(sprite.spriteName)){MenuMatches++;MenuSprites.Add(sprite);}
                        UIAtlas original=sprite.atlas;
                        UIAtlas nativeOriginal=UnthemedAtlas(original);
                        if(nativeOriginal!=original && !SpriteAtlases.ContainsKey(sprite))SpriteAtlases.Add(sprite,new AtlasRoute{Original=nativeOriginal,Applied=original});
                        UIAtlas themed=IsMenuName(sprite.spriteName)?MenuAtlas(sprite,root.transform):ThemeAtlas(original);
                        if(themed!=null && themed!=original)
                        {
                            AtlasRoute route;
                            if(!SpriteAtlases.TryGetValue(sprite,out route)){route=new AtlasRoute{Original=original};SpriteAtlases.Add(sprite,route);}
                            route.Applied=themed;
                            sprite.atlas=themed;
                        }
                    }
                    UIWidget widget=component as UIWidget;
                    if(widget!=null){if(!IsNativeResourceAccent(widget)&&!RhodesHangarStateColours.IsForegroundVisual(widget))widget.color=MapValue(widget.color);widget.MarkAsChanged();}
                }
                catch(Exception error){Warn("component:"+component.GetType().FullName,error);}
            }
        }
        static bool IsNativeResourceAccent(UIWidget widget)
        {
            // Native RU blue is semantic information, not a panel theme.
            // The resource glyph colours are baked in four exact atlas sprites;
            // preserve their white tint and only the audited RU counter accent.
            UISprite sprite=widget as UISprite;
            if(sprite==null)return false;
            if(sprite.spriteName!=null && sprite.spriteName.StartsWith("MapMarker-ResourceRU-",StringComparison.Ordinal))return true;
            Transform node=sprite.transform;
            return node.name=="RU_topbar" && node.parent!=null && node.parent.name=="2-Counter-Resource2" &&
                node.parent.parent!=null && node.parent.parent.name=="Resources";
        }
        static void ApplyButtonTargets(UIButtonColor colourButton,HashSet<GameObject> walking)
        {
            // UIButton's visual target and cached widget can be in a different
            // hierarchy from the input component. Follow only those known links.
            List<Component> targets=new List<Component>();
            if(colourButton.tweenTarget!=null)targets.Add(colourButton.tweenTarget.transform);
            object value;if(NativeRead.TryGet(colourButton,"mSprite",out value))AddRoot(targets,value);
            if(NativeRead.TryGet(colourButton,"mWidget",out value))AddRoot(targets,value);
            UIButton button=colourButton as UIButton;
            foreach(Component target in targets)
            {
                if(target==null)continue;
                if(button!=null)foreach(UISprite sprite in target.GetComponentsInChildren<UISprite>(true))
                    if(sprite!=null && IsMenuName(sprite.spriteName))
                    {
                        List<UIButton> linked;if(!LinkedMenuButtons.TryGetValue(sprite,out linked)){linked=new List<UIButton>();LinkedMenuButtons.Add(sprite,linked);}
                        if(!linked.Contains(button))linked.Add(button);
                    }
                ApplySubtree(target,walking);
            }
        }

        static void ApplyButton(UIButtonColor button)
        {
            if(RhodesHangarStateColours.IsForegroundVisual(button))return;
            // Read the public getter to finish native OnInit first. Its setter
            // would start/restart a TweenColor through SetState(false), so map
            // the verified cached fields and update the widget separately.
            Color initialized=button.defaultColor;
            if(!Same(initialized,MapValue(initialized)))MapNamedColourField(button,"mDefaultColor");
            MapNamedColourField(button,"mStartingColor");
            MapNamedColourField(button,"hover");MapNamedColourField(button,"pressed");MapNamedColourField(button,"disabledColor");
        }
        static void MapNamedColourField(object target,string name)
        {
            if(RhodesHangarStateColours.IsForegroundField(target,name))return;
            for(Type type=target.GetType();type!=null && type!=typeof(object);type=type.BaseType)
            {
                FieldInfo field=type.GetField(name,InstanceFields);
                if(field==null)continue;
                if(field.FieldType==typeof(Color) && !field.IsInitOnly)
                {
                    Color original=(Color)field.GetValue(target),mapped=MapValue(original);
                    if(!Same(original,mapped))WriteField(target,field,mapped);
                }
                return;
            }
        }
        static void ApplySerializedColours(Component component)
        {
            int budget=1024;
            Dictionary<object,object> copies=new Dictionary<object,object>(new IdentityComparer());
            HashSet<object> visiting=new HashSet<object>(new IdentityComparer());
            foreach(FieldInfo field in GetSerializedFields(component.GetType()))
            {
                // UIWidget.color is changed through its public setter so NGUI
                // can invalidate the vertex colour cache normally.
                if(field.DeclaringType==typeof(UIWidget) && field.Name=="mColor")continue;
                object original=field.GetValue(component);bool changed;
                object mapped=MapSerializedValue(original,0,ref budget,copies,visiting,out changed);
                if(changed)WriteField(component,field,mapped);
            }
        }
        static object MapSerializedValue(object value,int depth,ref int budget,Dictionary<object,object> copies,HashSet<object> visiting,out bool changed)
        {
            changed=false;if(value==null || depth>8 || --budget<0)return value;
            if(RhodesHangarStateColours.IsNativePalette(value))return value;
            Type type=value.GetType();
            if(type==typeof(Color))
            {
                Color original=(Color)value,mapped=MapValue(original);changed=!Same(original,mapped);return changed?(object)mapped:value;
            }
            if(type==typeof(Color32))
            {
                Color32 original=(Color32)value;Color mapped=MapValue(new Color(original.r/255f,original.g/255f,original.b/255f,original.a/255f));
                Color32 result=new Color32(Byte(mapped.r),Byte(mapped.g),Byte(mapped.b),original.a);
                changed=original.r!=result.r || original.g!=result.g || original.b!=result.b;return changed?(object)result:value;
            }
            // Never walk another component, material, ScriptableObject, font,
            // texture, atlas or Unity native-backed serializable wrapper.
            if(type.IsPrimitive || type.IsEnum || type==typeof(string) || value is UnityEngine.Object || value is Delegate)return value;
            object previous;if(copies.TryGetValue(value,out previous)){changed=!object.ReferenceEquals(value,previous);return previous;}
            if(!type.IsValueType && !visiting.Add(value))return value;
            try
            {
                Array array=value as Array;
                if(array!=null)
                {
                    if(array.Rank!=1 || array.Length>budget)return value;
                    Array clone=null;
                    for(int index=0;index<array.Length;index++)
                    {
                        bool itemChanged;object item=MapSerializedValue(array.GetValue(index),depth+1,ref budget,copies,visiting,out itemChanged);
                        if(itemChanged){if(clone==null)clone=(Array)array.Clone();clone.SetValue(item,index);}
                    }
                    if(clone!=null){copies[value]=clone;changed=true;return clone;}return value;
                }
                if(type.IsGenericType && type.GetGenericTypeDefinition()==typeof(List<>))
                {
                    IList list=(IList)value;if(list.Count>budget)return value;
                    IList clone=null;
                    for(int index=0;index<list.Count;index++)
                    {
                        bool itemChanged;object item=MapSerializedValue(list[index],depth+1,ref budget,copies,visiting,out itemChanged);
                        if(itemChanged && clone==null){clone=(IList)Activator.CreateInstance(type);foreach(object entry in list)clone.Add(entry);}
                        if(itemChanged)clone[index]=item;
                    }
                    if(clone!=null){copies[value]=clone;changed=true;return clone;}return value;
                }
                string space=type.Namespace??string.Empty;
                if(!type.IsSerializable || (!space.StartsWith("BBI.",StringComparison.Ordinal) && space.Length!=0))return value;
                object result=null;
                foreach(FieldInfo field in GetSerializedFields(type))
                {
                    if(RhodesHangarStateColours.IsForegroundField(value,field.Name))continue;
                    bool itemChanged;object item=MapSerializedValue(field.GetValue(value),depth+1,ref budget,copies,visiting,out itemChanged);
                    if(itemChanged){if(result==null)result=CloneMethod.Invoke(value,null);field.SetValue(result,item);}
                }
                if(result!=null){copies[value]=result;changed=true;return result;}return value;
            }
            finally{if(!type.IsValueType)visiting.Remove(value);}
        }
        static FieldInfo[] GetSerializedFields(Type type)
        {
            FieldInfo[] cached;if(SerializedFields.TryGetValue(type,out cached))return cached;
            List<FieldInfo> fields=new List<FieldInfo>();
            for(Type current=type;current!=null && current!=typeof(object) && current!=typeof(MonoBehaviour) && current!=typeof(Behaviour) && current!=typeof(Component) && current!=typeof(UnityEngine.Object);current=current.BaseType)
                foreach(FieldInfo field in current.GetFields(InstanceFields))
                    if(!field.IsStatic && !field.IsInitOnly && !field.IsNotSerialized &&
                        (field.IsPublic || field.IsDefined(typeof(SerializeField),true)))fields.Add(field);
            cached=fields.ToArray();SerializedFields.Add(type,cached);return cached;
        }
        static void WriteField(object target,FieldInfo field,object mapped)
        {
            FieldKey key=new FieldKey{Target=target,Field=field};
            if(!FieldChanges.ContainsKey(key))FieldChanges.Add(key,new FieldSnapshot{Target=target,Field=field,Original=field.GetValue(target)});
            field.SetValue(target,mapped);
        }
        static void MapAtlasField(object target,string name)
        {
            for(Type type=target.GetType();type!=null && type!=typeof(object);type=type.BaseType)
            {
                FieldInfo field=type.GetField(name,InstanceFields);if(field==null)continue;
                if(field.FieldType!=typeof(UIAtlas) || field.IsInitOnly)return;
                UIAtlas current=field.GetValue(target) as UIAtlas,native=UnthemedAtlas(current),mapped=ThemeAtlas(current);
                if(current==mapped && current==native)return;
                FieldKey key=new FieldKey{Target=target,Field=field};
                if(!FieldChanges.ContainsKey(key))FieldChanges.Add(key,new FieldSnapshot{Target=target,Field=field,Original=native});
                if(current!=mapped)field.SetValue(target,mapped);return;
            }
        }
        static UIAtlas UnthemedAtlas(UIAtlas atlas)
        {
            if(atlas==null)return atlas;
            foreach(KeyValuePair<UIAtlas,UIAtlas> pair in Atlases)if(pair.Value==atlas)return pair.Key;
            return atlas;
        }

        static UIAtlas MenuAtlas(UISprite sprite,Transform scope)
        {
            UIAtlas original=sprite.atlas;
            if(original==null)return original;
            UIAtlas source=original;
            if(original.name.StartsWith(Prefix+"Menu_",StringComparison.Ordinal))
            {
                AtlasRoute route;if(!SpriteAtlases.TryGetValue(sprite,out route))return original;
                source=route.Original;
            }
            source=ResolveAtlas(source);if(source==null || source.spriteMaterial==null)return original;
            List<MenuSprite> names=new List<MenuSprite>();
            AddMenuSprite(names,source,"GameMenu_HWlogo_default",false);
            AddMenuSprite(names,source,"GameMenu_HWlogo_on",true);
            AddMenuSprite(names,source,sprite.spriteName,IsActiveMenuName(sprite.spriteName));
            List<UIButton> linkedButtons;
            if(LinkedMenuButtons.TryGetValue(sprite,out linkedButtons))foreach(UIButton linked in linkedButtons)if(linked!=null)
            {
                AddMenuSprite(names,source,linked.normalSprite,false);AddMenuSprite(names,source,linked.hoverSprite,true);
                AddMenuSprite(names,source,linked.pressedSprite,true);AddMenuSprite(names,source,linked.disabledSprite,false);
            }
            // Keep every string in the existing UIButton state machine. Only
            // supply replacement pixels behind those unchanged state names.
            for(Transform current=sprite.transform;current!=null;current=current.parent)
            {
                UIButton[] buttons=current.GetComponents<UIButton>();
                foreach(UIButton button in buttons)
                {
                    AddMenuSprite(names,source,button.normalSprite,false);
                    AddMenuSprite(names,source,button.hoverSprite,true);
                    AddMenuSprite(names,source,button.pressedSprite,true);
                    AddMenuSprite(names,source,button.disabledSprite,false);
                }
                if(buttons.Length>0 || current==scope)break;
            }
            names.Sort(delegate(MenuSprite a,MenuSprite b){return string.CompareOrdinal(a.Name,b.Name);});
            string key=source.GetInstanceID().ToString();foreach(MenuSprite item in names)key+="|"+item.Name+":"+item.Width+":"+item.Height+":"+item.Active;
            UIAtlas cached;if(MenuAtlases.TryGetValue(key,out cached))return cached;
            Texture2D normal=null,active=null,texture=null;Material material=null;GameObject node=null;
            try
            {
                normal=LoadTexture("menu_logo_white.png");active=LoadTexture("menu_logo_yellow.png");
                Color32[] white=normal.GetPixels32(),yellow=active.GetPixels32();
                int width=4,height=2;foreach(MenuSprite item in names){width=Math.Max(width,item.Width+4);height+=item.Height+4;}
                if(width>SystemInfo.maxTextureSize || height>SystemInfo.maxTextureSize)throw new InvalidDataException("Menu icon atlas exceeds native texture limit");
                Color32[] pixels=new Color32[width*height];List<UISpriteData> sprites=new List<UISpriteData>();int top=2;
                bool pma=source.premultipliedAlpha;
                foreach(MenuSprite item in names)
                {
                    UISpriteData reference=source.GetSprite(item.Name),data=reference==null?new UISpriteData():CopySprite(reference);
                    data.name=item.Name;data.x=2;data.y=top;data.width=item.Width;data.height=item.Height;
                    FillMenuIcon(pixels,width,height,data,item.Active?yellow:white,item.Active?active.width:normal.width,item.Active?active.height:normal.height,pma);
                    sprites.Add(data);top+=item.Height+4;
                }
                texture=new Texture2D(width,height,TextureFormat.RGBA32,false);texture.name=Prefix+"MenuTexture";texture.hideFlags=HideFlags.HideAndDontSave;
                texture.SetPixels32(pixels);texture.Apply(false,false);texture.filterMode=FilterMode.Bilinear;texture.wrapMode=TextureWrapMode.Clamp;
                material=new Material(source.spriteMaterial);material.name=Prefix+"MenuMaterial";material.hideFlags=HideFlags.HideAndDontSave;material.mainTexture=texture;
                EnsureTemplates();node=new GameObject(Prefix+"Menu_"+MenuAtlases.Count);node.transform.parent=Templates.transform;node.hideFlags=HideFlags.HideAndDontSave;
                UIAtlas result=node.AddComponent<UIAtlas>();result.spriteMaterial=material;result.pixelSize=source.pixelSize;result.spriteList=sprites;result.MarkSpriteListAsChanged();
                Owned.Add(texture);Owned.Add(material);MenuAtlases.Add(key,result);
                Runtime.Log("ENDFIELD_MENU_LOGO_READY sameNativeStateNames=True; states="+names.Count+"; colliderAndButtonUntouched=True; privateAtlas=True; PMA="+pma);
                return result;
            }
            catch
            {
                if(node!=null)UnityEngine.Object.DestroyImmediate(node);
                if(material!=null)UnityEngine.Object.DestroyImmediate(material);
                if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
            finally
            {
                if(normal!=null)UnityEngine.Object.DestroyImmediate(normal);
                if(active!=null)UnityEngine.Object.DestroyImmediate(active);
            }
        }
        static void AddMenuSprite(List<MenuSprite> names,UIAtlas source,string name,bool active)
        {
            if(string.IsNullOrEmpty(name))return;
            foreach(MenuSprite existing in names)if(existing.Name==name)return;
            UISpriteData reference=source.GetSprite(name);
            int width=reference==null?64:reference.width,height=reference==null?64:reference.height;
            if(width<=0 || height<=0 || width>2048 || height>2048)throw new InvalidDataException("Menu state has invalid native sprite dimensions");
            names.Add(new MenuSprite{Name=name,Width=width,Height=height,Active=active});
        }
        static void FillMenuIcon(Color32[] pixels,int width,int height,UISpriteData sprite,Color32[] source,int sourceWidth,int sourceHeight,bool pma)
        {
            float scale=Math.Min(sprite.width/(float)sourceWidth,sprite.height/(float)sourceHeight);
            float imageWidth=sourceWidth*scale,imageHeight=sourceHeight*scale,left=(sprite.width-imageWidth)*.5f,bottom=(sprite.height-imageHeight)*.5f;
            for(int y=0;y<sprite.height;y++)for(int x=0;x<sprite.width;x++)
            {
                float u=(x+.5f-left)/imageWidth,v=(y+.5f-bottom)/imageHeight;
                if(u<0f || u>=1f || v<0f || v>=1f)continue;
                int sx=Math.Min(sourceWidth-1,(int)(u*sourceWidth)),sy=Math.Min(sourceHeight-1,(int)(v*sourceHeight));
                Color32 pixel=source[sy*sourceWidth+sx];
                if(pma){pixel.r=(byte)((pixel.r*pixel.a+127)/255);pixel.g=(byte)((pixel.g*pixel.a+127)/255);pixel.b=(byte)((pixel.b*pixel.a+127)/255);}
                pixels[(height-sprite.y-sprite.height+y)*width+sprite.x+x]=pixel;
            }
        }
        static bool IsMenuName(string name) { return name!=null && name.StartsWith("GameMenu_HWlogo_",StringComparison.Ordinal); }
        static bool IsActiveMenuName(string name) { return name!=null && name!="GameMenu_HWlogo_default"; }

        static bool IsPrivate(UIAtlas atlas)
        {
            if(atlas.name.StartsWith("EF_",StringComparison.Ordinal))return true;
            Texture texture=atlas.texture;return texture!=null && texture.name.StartsWith("EF_",StringComparison.Ordinal);
        }
        static UIAtlas ResolveAtlas(UIAtlas atlas)
        {
            UIAtlas current=atlas;
            for(int depth=0;current!=null && current.replacement!=null;depth++)
            {
                if(depth>=16)throw new InvalidOperationException("Unexpected UIAtlas replacement cycle");
                current=current.replacement;
            }
            return current;
        }
        static Texture2D LoadTexture(string filename)
        {
            if(filename!=Path.GetFileName(filename))throw new InvalidDataException("Theme filename must remain inside ui_endfield");
            string file=Path.Combine(DirectoryPath,filename);Texture2D texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            texture.hideFlags=HideFlags.HideAndDontSave;
            try{if(!texture.LoadImage(File.ReadAllBytes(file)))throw new InvalidDataException("Cannot decode theme PNG: "+filename);return texture;}
            catch{UnityEngine.Object.DestroyImmediate(texture);throw;}
        }
        static void EnsureTemplates()
        {
            if(Templates!=null)return;
            Templates=new GameObject(Prefix+"Templates");Templates.hideFlags=HideFlags.HideAndDontSave;Templates.SetActive(false);UnityEngine.Object.DontDestroyOnLoad(Templates);
        }
        static UISpriteData CopySprite(UISpriteData source)
        {
            return new UISpriteData{name=source.name,x=source.x,y=source.y,width=source.width,height=source.height,
                borderLeft=source.borderLeft,borderRight=source.borderRight,borderTop=source.borderTop,borderBottom=source.borderBottom,
                paddingLeft=source.paddingLeft,paddingRight=source.paddingRight,paddingTop=source.paddingTop,paddingBottom=source.paddingBottom};
        }
        static bool IsEnabled()
        {
            if(Restoring)return false;
            if(ConfigLoaded)return Enabled;
            ConfigLoaded=true;Enabled=false;
            try
            {
                DirectoryPath=Path.Combine(CustomUnits.AssetDirectory,"ui_endfield");string path=Path.Combine(DirectoryPath,"theme.ini");
                if(!File.Exists(path))return false;
                foreach(string raw in File.ReadAllLines(path))
                {
                    string line=raw.Trim();if(line.Length==0 || line[0]=='#' || line[0]==';' || line[0]=='[')continue;
                    int equals=line.IndexOf('=');if(equals<0)continue;
                    if(string.Equals(line.Substring(0,equals).Trim(),"Enabled",StringComparison.OrdinalIgnoreCase))
                    {
                        bool value;if(!bool.TryParse(line.Substring(equals+1).Trim(),out value))throw new InvalidDataException("Invalid Enabled in ui_endfield/theme.ini");
                        Enabled=value;
                    }
                }
                Runtime.Log("ENDFIELD_HUD_CONFIG enabled="+Enabled+"; finiteStageBindAndLifecycleHooks=True; nativeAtlasAssetsUnchanged=True");
            }
            catch(Exception error){Enabled=false;Warn("configuration",error);}
            return Enabled;
        }
        static bool Same(Color first,Color second) { return first.r==second.r && first.g==second.g && first.b==second.b && first.a==second.a; }
        static byte Byte(float value) { return (byte)Math.Max(0,Math.Min(255,(int)(value*255f+.5f))); }
        static void Warn(string key,Exception error)
        {
            if(Warnings.Add(key))Runtime.Log("WARNING ENDFIELD_HUD "+key+": "+error.GetType().Name+": "+error.Message+"; nativeFallback=True; gameplayContinues=True");
        }

        // Native UnitInterface and its authored widgets outlive a match, while
        // the native session controllers and some pooled entries are rebuilt.
        // Retain the UI-lifetime atlas routes, but permit the same two finite
        // HUD passes for the next match. Never destroy a still-referenced atlas.
        public static void EndSession()
        {
            RhodesHangarStateColours.ClearContexts();
            RhodesEngineeringLayout.EndSession();
            GameHudBound=GameHudFallbackScanned=GameHudPassCompleted=GameHudLateRetryUsed=false;GameHudRetryAt=0f;
            BoundHudRoots.Clear();ResourceLabels.Clear();ReferenceGroups.Clear();ReferenceRoots=0;
            List<UISprite> sprites=new List<UISprite>();
            foreach(UISprite sprite in SpriteAtlases.Keys)if(sprite==null)sprites.Add(sprite);
            foreach(UISprite sprite in sprites)SpriteAtlases.Remove(sprite);
            List<UIWidget> widgets=new List<UIWidget>();
            foreach(UIWidget widget in WidgetColours.Keys)if(widget==null)widgets.Add(widget);
            foreach(UIWidget widget in widgets)WidgetColours.Remove(widget);
            List<FieldKey> fields=new List<FieldKey>();
            foreach(FieldKey field in FieldChanges.Keys)
            {UnityEngine.Object target=field.Target as UnityEngine.Object;if(!object.ReferenceEquals(target,null)&&target==null)fields.Add(field);}
            foreach(FieldKey field in fields)FieldChanges.Remove(field);
            sprites.Clear();foreach(UISprite sprite in LinkedMenuButtons.Keys)if(sprite==null)sprites.Add(sprite);
            foreach(UISprite sprite in sprites)LinkedMenuButtons.Remove(sprite);
            MenuSprites.RemoveWhere(delegate(UISprite sprite){return sprite==null;});
            List<UIPopupList> popups=new List<UIPopupList>();foreach(UIPopupList popup in PopupOriginalAtlases.Keys)if(popup==null)popups.Add(popup);
            foreach(UIPopupList popup in popups)PopupOriginalAtlases.Remove(popup);
            Runtime.Log("ENDFIELD_HUD_SESSION_ENDED uiLifetimeAtlasesPreserved=True; nextMatchFiniteBindRearmed=True; recurringSceneScan=False");
        }

        // Disable hooks first, restore the exact instance snapshots, then release
        // only resources created here. Restart with Enabled=false for a complete
        // native startup, including BVM atlases that copied themed pixels.
        public static void Restore()
        {
            Restoring=true;Enabled=false;ConfigLoaded=true;
            try
            {
                foreach(FieldSnapshot record in FieldChanges.Values)
                    try{UnityEngine.Object unity=record.Target as UnityEngine.Object;if(object.ReferenceEquals(unity,null) || unity!=null)record.Field.SetValue(record.Target,record.Original);}catch(Exception error){Warn("restore-field",error);}
                foreach(KeyValuePair<UIWidget,Color> pair in WidgetColours)
                    try{if(pair.Key!=null){pair.Key.color=pair.Value;pair.Key.MarkAsChanged();}}catch(Exception error){Warn("restore-widget",error);}
                foreach(KeyValuePair<UISprite,AtlasRoute> pair in SpriteAtlases)
                    try
                    {
                        // A later BVM route owns its extended atlas. Do not
                        // strip its custom sprite names during theme rollback.
                        if(pair.Key!=null && (pair.Key.atlas==pair.Value.Applied || IsOwnedAtlas(pair.Key.atlas)))pair.Key.atlas=pair.Value.Original;
                    }
                    catch(Exception error){Warn("restore-atlas",error);}
                // A dropdown opened after the panel bind inherits the private
                // atlas without an icon spawn event. Restore its known local
                // generated root before releasing our textures.
                foreach(KeyValuePair<UIPopupList,UIAtlas> pair in PopupOriginalAtlases)
                    try
                    {
                        if(pair.Key==null)continue;GameObject child=ReadOptional(pair.Key,"mChild") as GameObject;if(child==null)continue;
                        foreach(UISprite sprite in child.GetComponentsInChildren<UISprite>(true))
                            if(sprite!=null && IsOwnedAtlas(sprite.atlas))sprite.atlas=UnthemedAtlas(sprite.atlas);
                    }
                    catch(Exception error){Warn("restore-popup",error);}
                try{if(Templates!=null)UnityEngine.Object.DestroyImmediate(Templates);}catch(Exception error){Warn("restore-templates",error);}Templates=null;
                for(int index=Owned.Count-1;index>=0;index--)
                    try{if(Owned[index]!=null)UnityEngine.Object.DestroyImmediate(Owned[index]);}catch(Exception error){Warn("restore-owned",error);}
            }
            catch(Exception error){Warn("restore",error);}
            finally
            {
                RhodesHangarStateColours.ClearContexts();
                Owned.Clear();Atlases.Clear();FailedAtlases.Clear();MenuAtlases.Clear();SpriteAtlases.Clear();WidgetColours.Clear();FieldChanges.Clear();
                SerializedFields.Clear();BoundHudRoots.Clear();LinkedMenuButtons.Clear();MenuSprites.Clear();ResourceLabels.Clear();ReferenceGroups.Clear();PopupOriginalAtlases.Clear();
                GameHudBound=false;GameHudFallbackScanned=false;GameHudPassCompleted=false;GameHudLateRetryUsed=false;GameHudRetryAt=0f;Restoring=false;
            }
        }
        static bool IsOwnedAtlas(UIAtlas atlas)
        {
            if(atlas==null)return false;
            foreach(UIAtlas owned in Atlases.Values)if(atlas==owned)return true;
            foreach(UIAtlas owned in MenuAtlases.Values)if(atlas==owned)return true;
            return false;
        }
    }
}
