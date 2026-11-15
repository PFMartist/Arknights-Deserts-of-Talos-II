// Human-controlled private faction for native local/custom multiplayer matches.
// Native teams, maps, AI and spawn assignments remain authoritative.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BBI.Core;
using BBI.Core.Network;
using BBI.Game.Data;
using BBI.Game.Simulation;
using BBI.Unity.Game;
using BBI.Unity.Game.UI;
using BBI.Unity.Game.Utility;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesSkirmishFaction
    {
        static readonly BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        static readonly MethodInfo Memberwise = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly List<ScriptableObject> MenuAssets = new List<ScriptableObject>();
        static LevelManager Manager;
        static CommanderAttributes Template;
        static readonly Dictionary<CommanderID,CommanderAttributes> PreparedCommanders=new Dictionary<CommanderID,CommanderAttributes>();
        static readonly Dictionary<CommanderID,CommanderDescription> PreparedDescriptions=new Dictionary<CommanderID,CommanderDescription>();
        static object SetupPanel;
        static int EntryIndex = -1;
        static bool MatchActive,NetworkMatch;
        static readonly HashSet<CommanderID> ColoursReported=new HashSet<CommanderID>();

        public static bool Enabled { get { return Runtime.SkirmishMode; } }
        public static bool IsMatchActive { get { return Enabled && MatchActive; } }
        // Legacy informational accessor. Authoritative services must use the
        // declared owner set or each unit's OwnerCommander, never an arbitrary
        // first player when several humans chose Rhodes.
        public static CommanderID Owner { get { foreach(CommanderID owner in PreparedCommanders.Keys)if(PreparedCommanders.Count==1)return owner;return CommanderID.None; } }
        public static bool IsNetworkMatch { get { return MatchActive&&NetworkMatch; } }
        public static int FactionEntryIndex { get { return EntryIndex; } }
        public static bool IsRhodesIndex(int index) { return Enabled && EntryIndex >= 0 && index == EntryIndex; }

        internal static bool IsRhodesCustomization(object customizationPanel)
        {
            Component setup=SetupPanel as Component;
            if(!Enabled||setup==null||!setup.gameObject.activeInHierarchy||
                !ReferenceEquals(NativeRead.Get(SetupPanel,"m_PlayerCustomizationPanel"),customizationPanel))return false;
            if(SetupPanel is MultiplayerMissionPanel)
            {
                object shared=NativeRead.Get(SetupPanel,"mSharedLobbyData");
                return shared!=null&&IsRhodesIndex((int)NativeRead.Get(shared,"LocalPlayerFactionIndex"));
            }
            List<CommanderDescription> descriptions=NativeRead.Get(SetupPanel,"mCommanderDescriptions") as List<CommanderDescription>;
            if(descriptions==null)return false;
            foreach(CommanderDescription description in descriptions)
                if(description.NetworkPlayerID.IsLocalPlayer())return IsRhodesIndex(description.FactionIndex);
            return false;
        }

        // LevelManager.Initialize postfix. This only creates menu data. It does
        // not bind research owners, nor require an entity registry.
        public static void AfterLevelInitialize(object manager)
        {
            if (!Enabled) return;
            LevelManager levelManager = manager as LevelManager;
            if (levelManager == null || levelManager.CommanderAttributes == null) return;
            EnsureTemplate(levelManager);
        }

        public static void EnterSetup(object panel)
        {
            if (!Enabled || panel == null) return;
            if (!(panel is SkirmishSetupPanel)&&!(panel is MultiplayerMissionPanel))
                throw new InvalidOperationException("Rhodes requires a local skirmish or custom multiplayer lobby");
            LevelManager manager = NativeRead.Get(panel, "mLevelManager") as LevelManager;
            if (manager != null && manager.ManagerState == LevelManager.State.Startup && PreparedCommanders.Count!=0&&RhodesSkirmishRuntime.ReadyForMatch)
            {
                // A cancelled/failed load may never reach Runtime's Game exit
                // branch. Startup guarantees no live native match owns this bind.
                RhodesResearch.Reset();
                ResetMatch();
                Runtime.Log("RHODES_SKIRMISH_ABANDONED_PREPARATION_RESET nativeState=Startup; menuAndRegistryAssetsRetained=True");
            }
            SetupPanel = panel;
            if (manager != null) EnsureTemplate(manager);
        }

        public static void LeaveSetup(object panel)
        {
            if (ReferenceEquals(SetupPanel, panel)) SetupPanel = null;
            // A start click can hide the panel while native level loading begins.
            // Do not clear the prepared commander set here.
        }

        public static Dictionary<string, PlayerFactionSelection> FilterFactionOptions(
            Dictionary<string, PlayerFactionSelection> original, object manager, PlayerType playerType,bool localChoice)
        {
            if (!Enabled || original == null) return original;
            LevelManager levelManager = manager as LevelManager;
            if (levelManager != null) EnsureTemplate(levelManager);
            Component setup=SetupPanel as Component;
            bool expose = setup!=null&&setup.gameObject.activeInHierarchy&&playerType==PlayerType.Human&&
                (!(SetupPanel is MultiplayerMissionPanel)||!localChoice||RhodesNetworkCompatibility.AllowRhodesSelection(SetupPanel));
            Dictionary<string, PlayerFactionSelection> result = new Dictionary<string, PlayerFactionSelection>();
            foreach (KeyValuePair<string, PlayerFactionSelection> item in original)
            {
                bool ours = IsRhodesIndex((int)item.Value.Faction) && item.Value.DLCPackID == DLCPackID.kInvalidID;
                if (!ours || expose) result.Add(item.Key, item.Value);
            }
            // Covers a lazy append after the native option loop already ran.
            if (expose && Template != null)
            {
                bool present = false;
                foreach (PlayerFactionSelection choice in result.Values)
                    if (IsRhodesIndex((int)choice.Faction) && choice.DLCPackID == DLCPackID.kInvalidID) { present = true; break; }
                if (!present) result.Add(Template.Name, new PlayerFactionSelection((CustomizationFactionSetting)EntryIndex, DLCPackID.kInvalidID, false));
            }
            return result;
        }

        // Input filter for GameLobbyView.RollForRandomFaction. A human's random
        // choice must also exclude RI even though RI appears in the explicit list.
        public static Dictionary<string, PlayerFactionSelection> FilterRandomOptions(Dictionary<string, PlayerFactionSelection> original)
        {
            if (!Enabled || original == null || EntryIndex < 0) return original;
            Dictionary<string, PlayerFactionSelection> result = new Dictionary<string, PlayerFactionSelection>();
            foreach (KeyValuePair<string, PlayerFactionSelection> item in original)
                if (!(IsRhodesIndex((int)item.Value.Faction) && item.Value.DLCPackID == DLCPackID.kInvalidID)) result.Add(item.Key, item.Value);
            return result;
        }

        // Native custom-lobby slots populate their faction popup only once.
        // A room fingerprint can become ready after that initial population.
        // Edit the existing backing lists without changing popup.value, calling
        // PopulateFactionOptions/Clear, or firing a faction-selection event.
        internal static void RefreshNetworkChoices(object panel,bool ready)
        {
            if(!Enabled||!(panel is MultiplayerMissionPanel)||Template==null||EntryIndex<0)return;
            GameLobbyView view=NativeRead.Get(panel,"m_LobbyViewPanel") as GameLobbyView;
            if(view==null)return;
            IDictionary<CommanderID,PlayerSlotController> slots=NativeRead.Get(view,"mPlayerSlotLookup") as IDictionary<CommanderID,PlayerSlotController>;
            if(slots==null)return;
            foreach(PlayerSlotController slot in slots.Values)
            {
                if(slot==null||slot.PlayerType!=PlayerType.Human)continue;
                PlayerFactionSelection current;
                bool selectedRhodes=slot.GetFactionSelection(out current)&&IsRhodesIndex((int)current.Faction);
                RefreshNetworkSlotChoices(slot.PlayerType,slot.FactionOptionNames,slot.FactionOptionData,Template.Name,EntryIndex,slot.PlayerID.IsLocalPlayer(),ready,selectedRhodes);
            }
        }

        // Kept managed and separate so delayed-ready, a joining player and a
        // pending room can be audited without a Unity scene or popup callbacks.
        internal static void RefreshNetworkSlotChoices(PlayerType playerType,List<string> names,List<object> data,
            string label,int factionIndex,bool localChoice,bool ready,bool selectedRhodes)
        {
            if(playerType!=PlayerType.Human||names==null||data==null||names.Count!=data.Count||factionIndex<0)return;
            // Native read-only remote slots need every faction as display data,
            // including when joining while that peer's fingerprint is pending.
            ready=ready||!localChoice;
            bool present=false;
            for(int i=data.Count-1;i>=0;i--)
            {
                if(!(data[i] is PlayerFactionSelection))continue;
                PlayerFactionSelection selection=(PlayerFactionSelection)data[i];
                if((int)selection.Faction!=factionIndex||selection.DLCPackID!=DLCPackID.kInvalidID)continue;
                present=true;
                // Keep the selected identity representable if another peer is
                // still verifying. Compatibility still blocks new selections
                // and match start; no native shared metadata is rewritten.
                if(!ready&&!selectedRhodes){data.RemoveAt(i);names.RemoveAt(i);}
            }
            if(ready&&!present){names.Add(label);data.Add(new PlayerFactionSelection((CustomizationFactionSetting)factionIndex,DLCPackID.kInvalidID,false));}
        }

        // The native setter must not save our appended faction into the fixed
        // two-entry customization skin array. Multiplayer metadata still gets
        // the selected integer through the original native setter.
        public static bool PersistLocalFactionSelection(bool requested,PlayerFactionSelection selection)
        {return requested&&!IsRhodesIndex((int)selection.Faction);}

        public static PlayerFactionSelection FilterNetworkLocalFactionSelection(PlayerFactionSelection incoming,object panel,bool remember)
        {
            if(!Enabled||!(panel is MultiplayerMissionPanel))return incoming;
            bool selectedRhodes=IsRhodesIndex((int)incoming.Faction);
            object shared=NativeRead.Get(panel,"mSharedLobbyData");
            // A native customization dialog offers only base factions. Keep a
            // selected Rhodes role while applying its colours, but let an
            // explicit popup selection (remember=true) switch back normally.
            bool retain=!remember&&shared!=null&&IsRhodesIndex((int)NativeRead.Get(shared,"LocalPlayerFactionIndex"));
            if(!selectedRhodes&&!retain)return incoming;
            RhodesNetworkCompatibility.RequireCompatibleRoom(panel);
            return new PlayerFactionSelection((CustomizationFactionSetting)EntryIndex,DLCPackID.kInvalidID,false);
        }

        // GeneratePlayerSelections postfix. Keep every original description,
        // including native team, spawn, network owner, colour and AI difficulty.
        public static Dictionary<CommanderID, PlayerSelection> PrepareSelections(Dictionary<CommanderID, PlayerSelection> original)
        {
            if (!Enabled || original == null) return original;
            CommanderID[] owners=RhodesMatchSelectionPolicy.Validate(original,EntryIndex);
            if(owners.Length==0)return original;
            if (!RhodesSkirmishRuntime.ReadyForMatch)
                throw new InvalidOperationException("Previous match is still shutting down; wait for the main menu before starting Rhodes again");
            bool network=SetupPanel is MultiplayerMissionPanel;
            if(!(SetupPanel is SkirmishSetupPanel)&&!network)
                throw new InvalidOperationException("Rhodes preparation requires a supported match setup");
            if(network)RhodesNetworkCompatibility.RequireCompatibleRoom(SetupPanel);
            foreach(CommanderID owner in owners)
                if(Template==null||!ReferenceEquals(original[owner].Attributes,Template))
                    throw new InvalidOperationException("Rhodes menu template mapping changed before match start");
            if (!RhodesResearch.Ready) throw new InvalidOperationException("Rhodes registry/research must be registered before native match preparation");

            if(PreparedCommanders.Count==0)
            {
                try
                {
                    // A fixed owner order makes registration/bind order equal
                    // on every peer, independent of Dictionary enumeration.
                    foreach(CommanderID owner in owners)
                    {
                        CommanderAttributes commander=CloneCommander(Template);
                        RhodesResearch.BindCommander(commander,owner);
                        PreparedCommanders.Add(owner,commander);
                    }
                    foreach(KeyValuePair<CommanderID,PlayerSelection> item in original)PreparedDescriptions.Add(item.Key,item.Value.Desc);
                    NetworkMatch=network;MatchActive=true;
                    Runtime.Log("RHODES_MATCH_PREPARED rhodesHumans="+owners.Length+"; totalCommanders="+original.Count+"; network="+network+"; nativeTeamsAndSpawns=True; nativeAIPreserved=True; privatePopulationPerOwner=True");
                }
                catch
                {
                    // No simulation has started. Release the whole partial
                    // owner bind instead of leaving only some peers prepared.
                    RhodesResearch.Reset();ResetMatch();throw;
                }
            }
            else if(!MatchActive||NetworkMatch!=network||!RhodesMatchSelectionPolicy.MatchesPrepared(original,owners,PreparedCommanders,PreparedDescriptions))
                throw new InvalidOperationException("Prepared match selections changed; return to setup before preparing again");

            Dictionary<CommanderID, PlayerSelection> result = new Dictionary<CommanderID, PlayerSelection>(original);
            foreach(CommanderID owner in owners)
            {
                CommanderDescription description=original[owner].Desc;
                // Network descriptions already contain synchronized colours.
                // Reading a peer's local UserSettings here would fork state.
                if(!network&&description.NetworkPlayerID.IsLocalPlayer())
                    description.UnitColors=RhodesDefaultColours.ResolveSkirmish(description.UnitColors,ShipbreakersMain.UserSettings.Customization);
                if(ColoursReported.Add(owner))Runtime.Log("RHODES_MATCH_COLOURS owner="+owner+"; resolved="+RhodesDefaultColours.Describe(description.UnitColors)+"; networkDescriptionAuthoritative="+network);
                result[owner]=new PlayerSelection(PreparedCommanders[owner],description);
            }
            return result;
        }

        // Runtime calls after simulation teardown. Registration generations and
        // menu assets intentionally survive; the runtime owns service resets.
        public static void ResetMatch()
        {
            MatchActive = false;
            NetworkMatch=false;
            PreparedCommanders.Clear();PreparedDescriptions.Clear();ColoursReported.Clear();
            RhodesDefaultColours.ResetSpawnReports();
        }

        static void EnsureTemplate(LevelManager manager)
        {
            CommanderAttributes[] original = manager.CommanderAttributes;
            if (original == null || original.Length == 0) return;
            if (ReferenceEquals(Manager, manager) && Template != null)
            {
                for (int i = 0; i < original.Length; i++)
                    if (ReferenceEquals(original[i], Template)) { EntryIndex = i; return; }
            }
            if (MatchActive) throw new InvalidOperationException("Cannot replace Rhodes menu mapping while a Rhodes match is active");
            CommanderAttributes donor = null;
            foreach (CommanderAttributes candidate in original)
                if (candidate != null && candidate.Faction != null && candidate.Faction.ID == FactionID.Coalition)
                { donor = candidate; break; }
            if (donor == null) throw new InvalidOperationException("Native Coalition skirmish commander is unavailable");
            CommanderAttributes template = CloneCommander(donor);
            int removedPowerOverrides = StripInheritedMultiplayerPower(template, donor);
            Runtime.Log("RHODES_SKIRMISH_STARTING_POWER_ISOLATED removedNativeMPPackets=" + removedPowerOverrides +
                "; removedSetEntries=6; nativeSource=MultiplayerStartingBuffSet; sourceAssetPathID=40811; privateCommanderOnly=True; nativeFactionsUnchanged=True; postSpawnPowerWrites=False");
            NativeRead.Set(template, "m_Name", "Rhodes Island / 罗德岛");
            NativeRead.Set(template, "m_NameLocID", string.Empty);
            ScriptableObject donorFaction = NativeRead.Get(donor, "m_FactionAttributes") as ScriptableObject;
            if (donorFaction == null) throw new InvalidOperationException("Native Coalition faction asset is unavailable");
            ScriptableObject faction = CloneFaction(donorFaction);
            NativeRead.Set(template, "m_FactionAttributes", faction);
            template.StartingUnits = ReplaceFleet(donor.StartingUnits);
            // Native deployment retains the opening fleet and formation goals.
            ConfigureOpeningDeployment(template);
            RhodesResearch.ConfigureSkirmishPopulationOpening(template);
            EntryIndex = original.Length;
            CommanderAttributes[] appended = new CommanderAttributes[original.Length + 1];
            Array.Copy(original, appended, original.Length);
            appended[EntryIndex] = template;
            NativeRead.Set(manager, "<CommanderAttributes>k__BackingField", appended);
            Manager = manager; Template = template;
            Runtime.Log("RHODES_SKIRMISH_MENU_REGISTERED index=" + EntryIndex + "; privateCommander=True; privateFaction=True; nativeFactionIDCompatibility=1; existingEntriesUnchanged=True; privatePopulationResearch=True");
        }

        internal static void ConfigureOpeningDeployment(CommanderAttributes commander)
        {
            if(commander==null)throw new ArgumentNullException("commander");
            NativeRead.Set(commander,"m_BeginDeployed",false);
            NativeRead.Set(commander,"m_WaitForDeployCommand",false);
        }
        static CommanderAttributes CloneCommander(CommanderAttributes source)
        {
            // BBI commander arrays are immutable template inputs here. Clone all
            // fields recursively while retaining Unity assets by reference.
            return (CommanderAttributes)Copy(source, new Dictionary<object, object>(new Identity()));
        }

        static StartingUnit[] ReplaceFleet(StartingUnit[] original)
        {
            if (original == null) throw new InvalidOperationException("Native starting fleet is missing");
            // StartingUnit is a reference type in this build. Cloning only the
            // array would modify the native Coalition entries in place.
            StartingUnit[] result = (StartingUnit[])Copy(original, new Dictionary<object, object>(new Identity()));
            int carriers = 0, harvesters = 0, surveyors = 0;
            for (int i = 0; i < result.Length; i++)
            {
                StartingUnit entry = result[i];
                if (entry.Unit == "C_Carrier_MP") { entry.Unit = RhodesCarrierUnits.UnitKey; carriers++; }
                else if (entry.Unit == "C_Harvester_MP") { entry.Unit = FactionUnits.HarvesterKey; harvesters++; }
                else if (entry.Unit == "C_Baserunner_MP") { entry.Unit = FactionUnits.SurveyKey; surveyors++; }
                result[i] = entry;
            }
            if (carriers != 1 || harvesters != 4 || surveyors != 1)
                throw new InvalidOperationException("Unexpected native starting fleet; expected one carrier, four harvesters and one baserunner");
            // Population setup separately removes only the legacy population
            // scanner from this private copy, leaving ordinary survey scanners.
            return result;
        }

        static ScriptableObject CloneFaction(ScriptableObject source)
        {
            ScriptableObject clone = RhodesAssetClone59.CreateEmpty(source.GetType());
            clone.name = "EF_Rhodes_MenuFaction";
            clone.hideFlags = HideFlags.HideAndDontSave;
            Dictionary<object, object> seen = new Dictionary<object, object>(new Identity());
            foreach (FieldInfo field in Fields(source.GetType()))
                if (field.Name != "mDataCache") field.SetValue(clone, Copy(field.GetValue(source), seen));
            object data = NativeRead.Get(clone, "m_FactionAttributes");
            // Native loading rows read Faction.FactionName, not Commander.Name.
            // This data is our deep copy; keep Coalition's VO/ID compatibility.
            NativeRead.Set(data, "m_FactionName", "Rhodes Island / 罗德岛");
            ((NamedObjectBase)data).InitializeName(clone.name);
            for (Type type = clone.GetType(); type != null; type = type.BaseType)
            {
                MethodInfo enable = type.GetMethod("OnEnable", Declared, null, Type.EmptyTypes, null);
                if (enable != null) { enable.Invoke(clone, null); break; }
            }
            MenuAssets.Add(clone);
            return clone;
        }

        static List<FieldInfo> Fields(Type type)
        {
            List<FieldInfo> fields = new List<FieldInfo>();
            for (Type cursor = type; cursor != null && cursor != typeof(object) && cursor != typeof(UnityEngine.Object) && cursor != typeof(ScriptableObject); cursor = cursor.BaseType)
                foreach (FieldInfo field in cursor.GetFields(Declared)) if (!field.IsStatic) fields.Add(field);
            return fields;
        }

        static object Copy(object value, Dictionary<object, object> seen)
        {
            if (value == null) return null;
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || typeof(UnityEngine.Object).IsAssignableFrom(type)) return value;
            object prior;
            if (seen.TryGetValue(value, out prior)) return prior;
            Array array = value as Array;
            if (array != null)
            {
                Array result = (Array)array.Clone(); seen.Add(value, result);
                for (int i = 0; i < array.Length; i++) result.SetValue(Copy(array.GetValue(i), seen), i);
                return result;
            }
            if (type.Namespace == null || !type.Namespace.StartsWith("BBI.", StringComparison.Ordinal)) return value;
            object clone = Memberwise.Invoke(value, null); seen.Add(value, clone);
            foreach (FieldInfo field in Fields(type)) field.SetValue(clone, Copy(field.GetValue(value), seen));
            return clone;
        }

        sealed class Identity : IEqualityComparer<object>
        {
            public new bool Equals(object left, object right) { return ReferenceEquals(left, right); }
            public int GetHashCode(object value) { return RuntimeHelpers.GetHashCode(value); }
        }
    }
}
