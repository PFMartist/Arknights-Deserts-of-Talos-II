// Native research data and read-only ResearchEvent bridge. Build 12339551.

// Register before StartMatch; BindCommander on the already private scenario copy.

using System;

using System.Collections;

using System.Collections.Generic;

using System.Reflection;

using System.Runtime.CompilerServices;

using BBI.Core;

using BBI.Core.Data;
using BBI.Core.ComponentModel;

using BBI.Core.Events;

using BBI.Game.Data;

using BBI.Game.Events;

using BBI.Game.Simulation;

using BBI.Unity.Game;

using BBI.Unity.Game.Data;

using BBI.Unity.Game.World;

using UnityEngine;



namespace EndfieldDoK.MapUnitLab

{

    public static partial class RhodesResearch

    {

        public const string TreeKey="EF_Rhodes_TechTree";

        public const string LightAssembly="Research_RI_C01_LightAssembly";
        public const string SuppressionAssembly="Research_RI_C02_SuppressionAssembly";
        public const string RailgunAssembly="Research_RI_C03_RailgunAssembly";
        public const string AirDefenceAssembly="Research_RI_C04_AirDefenceAssembly";
        public const string TiltAssembly="Research_RI_C05_TiltAssembly";
        public const string ArtilleryAssembly="Research_RI_C06_ArtilleryAssembly";

        public const string CIWS2="Research_EF_Rhodes_CIWS2";

        public const string Repair2="Research_EF_Rhodes_Repair2";

        public const string VTOL2="Research_EF_Rhodes_VTOL2";

        public const string Aviation1="Research_EF_Rhodes_Aviation1";

        public const string Gunnery1="Research_EF_Rhodes_Gunnery1";

        public const string Aviation2="Research_EF_Rhodes_Aviation2";

        public const string Gunnery2="Research_EF_Rhodes_Gunnery2";

        public const string Gunnery3="Research_EF_Rhodes_Gunnery3";

        public const string RepairWeaponID="EF_RHODES_REPAIR";
        public const string RepairStarboardWeaponID="EF_RHODES_REPAIR_STARBOARD";

        static readonly string[] Keys=WithSpecialtyResearchKeys(WithFactionUpgradeKeys(WithProductionResearchKey(BuildResearchKeys())));

        static readonly BindingFlags Declared=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;

        static readonly MethodInfo Memberwise=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);

        static EntityTypeCollection Registry;

        static readonly List<ScriptableObject> Owned=new List<ScriptableObject>();

        // A startup registry is replaced while GameStartSettings still refers

        // to its private commander -> faction -> tree/buff asset generation.

        // Keep that complete generation alive until no owner/view can use it.

        static readonly List<ScriptableObject> RetainedCommanderAssets=new List<ScriptableObject>();

        static readonly Dictionary<string,ScriptableObject> Items=new Dictionary<string,ScriptableObject>();

        static ScriptableObject Tree;

        sealed class CommanderResearchState
        {
            internal readonly CommanderID Owner;
            internal readonly HashSet<string> Completed=new HashSet<string>();
            internal readonly HashSet<string> Scratch=new HashSet<string>();
            internal readonly HashSet<string> GateScratch=new HashSet<string>();
            internal readonly Dictionary<string,int> CompletionTicks=new Dictionary<string,int>(StringComparer.Ordinal);
            internal bool Initialized;
            internal string Active=string.Empty;
            internal Sim ClockSim;
            internal Commander ClockCommander;
            internal int GunneryCompletedTick=-1,Revision;
            internal CommanderResearchState(CommanderID owner){Owner=owner;}
        }
        static readonly Dictionary<CommanderID,CommanderResearchState> CommanderStates=new Dictionary<CommanderID,CommanderResearchState>();
        static CommanderID[] CommanderOrder=new CommanderID[0];
        static IEventReceiver Receiver;
        static readonly BBI.Core.Events.EventHandler<ResearchEvent> Handler=OnResearch;
        static readonly object Gate=new object();
        static int Revision;
        static bool Bound{get{return CommanderStates.Count!=0;}}
        public static bool IsDeclaredOwner(CommanderID owner){lock(Gate)return CommanderStates.ContainsKey(owner);}
        internal static CommanderID[] DeclaredOwners(){lock(Gate)return CommanderOrder;}

        public static bool Ready {get;private set;}

        public static int StateRevision {get{lock(Gate)return Revision;}}

        public static bool IsCompleted(CommanderID id,string key)
        {lock(Gate){CommanderResearchState state;return CommanderStates.TryGetValue(id,out state)&&state.Completed.Contains(key);}}

        public static string ActiveResearch(CommanderID id)
        {lock(Gate){CommanderResearchState state;return CommanderStates.TryGetValue(id,out state)?state.Active:string.Empty;}}



        public struct Snapshot

        {

            public int Revision,CIWSLevel,RepairLevel,AviationLevel,GunneryLevel,GunneryCompletedSimTick;

            public int AviationCompletedSimTick,Aviation2CompletedSimTick,Gunnery2CompletedSimTick,Gunnery3CompletedSimTick,CIWS2CompletedSimTick,Repair2CompletedSimTick,TiltCompletedSimTick;
            public string ActiveKey;

        }

        public static bool TrySnapshot(CommanderID id,out Snapshot snapshot)
        {
            lock(Gate)
            {
                snapshot=new Snapshot();snapshot.GunneryCompletedSimTick=-1;
                CommanderResearchState state;if(!CommanderStates.TryGetValue(id,out state)||!state.Initialized)return false;
                if(state.Completed.Contains(Gunnery1)&&state.GunneryCompletedTick<0)return false;
                snapshot.Revision=state.Revision;snapshot.ActiveKey=state.Active;
                snapshot.CIWSLevel=1;snapshot.RepairLevel=1;
                snapshot.AviationLevel=state.Completed.Contains(Aviation1)?1:0;
                snapshot.GunneryLevel=GunneryStage(state.Completed.Contains(Gunnery1),state.Completed.Contains(Gunnery2),state.Completed.Contains(Gunnery3));
                if(snapshot.GunneryLevel<0)return false;
                snapshot.GunneryCompletedSimTick=state.GunneryCompletedTick;
                snapshot.AviationCompletedSimTick=CompletionTick(state,Aviation1);snapshot.Aviation2CompletedSimTick=CompletionTick(state,Aviation2);
                snapshot.Gunnery2CompletedSimTick=CompletionTick(state,Gunnery2);snapshot.CIWS2CompletedSimTick=CompletionTick(state,CIWS2);snapshot.Repair2CompletedSimTick=CompletionTick(state,Repair2);
                snapshot.Gunnery3CompletedSimTick=CompletionTick(state,Gunnery3);
                snapshot.TiltCompletedSimTick=CompletionTick(state,TiltAssembly);return true;
            }
        }



        // The native route-choice tier is exclusive and permanent for this match.
        // Per-item formal prices/times are defined in Research.Balance.
        static readonly string[] ExclusiveRouteKeys={Aviation1,Gunnery1};

        public static void Register()

        {

            EntityTypeCollection current=(EntityTypeCollection)NativeRead.Get(typeof(ShipbreakersMain),"sEntityTypes");

            if(current==null)throw new InvalidOperationException("Rhodes research needs the current native registry");

            if(object.ReferenceEquals(Registry,current))

            {if(!Ready)throw new InvalidOperationException("Rhodes research registration failed; restart required");Verify();return;}

            // A registry swap is restart-only while any private unit view is live.

            foreach(UnitView view in Resources.FindObjectsOfTypeAll(typeof(UnitView)))

            {

                if(view==null||!view.gameObject.activeInHierarchy)continue;

                if(view.GetComponent<RhodesCarrierBinding>()!=null)throw new InvalidOperationException("Cannot replace private research assets while a Rhodes view is active");

                UnitState state=view.NonInterpolatedTickState;

                string key=state==null?string.Empty:state.TypeID;

                if(key==RhodesCarrierUnits.UnitKey||key==RhodesCarrierUnits.AircraftKey)throw new InvalidOperationException("Cannot replace private research assets while a Rhodes/aircraft view is active");

            }

            CommanderID[] declaredOwners=DeclaredOwners();bool retainOwner=declaredOwners.Length!=0;

            Reset();

            if(retainOwner)RetainedCommanderAssets.AddRange(Owned);

            else

            {

                for(int i=Owned.Count-1;i>=0;i--)if(Owned[i]!=null)UnityEngine.Object.DestroyImmediate(Owned[i]);

                for(int i=RetainedCommanderAssets.Count-1;i>=0;i--)if(RetainedCommanderAssets[i]!=null)UnityEngine.Object.DestroyImmediate(RetainedCommanderAssets[i]);

                RetainedCommanderAssets.Clear();

            }

            Owned.Clear();Items.Clear();Tree=null;Registry=current;Ready=false;

            foreach(string key in Keys)if(Registry.GetEntityType(key)!=null)throw new InvalidOperationException("Private Rhodes research collision: "+key);

            if(Registry.GetEntityType(TreeKey)!=null)throw new InvalidOperationException("Private Rhodes tree collision");

            AddNativeUnlock(LightAssembly,"轻型突击车装配","Tier_1_C_Escorts",new string[0]);
            AddNativeUnlock(SuppressionAssembly,"装甲突击车装配","Tier_2_C_ArmouredAssault",new string[]{LightAssembly});
            AddNativeUnlock(RailgunAssembly,"轨道炮车装配","Tier_2_C_RailGun",new string[]{LightAssembly});
            AddNativeUnlock(AirDefenceAssembly,"防空导弹车装配","Tier_2_C_HAC_AA",new string[]{LightAssembly});
            AddNativeUnlock(TiltAssembly,"倾转旋翼机机库","Tier_3_C_Interceptor_Bomber",new string[]{LightAssembly});
            AddNativeUnlock(ArtilleryAssembly,"自行火炮装配","Tier_3_C_Artillery",new string[]{SuppressionAssembly});

            // VTOL2 key is reserved; no no-op research item is registered or shown.

            Add(Aviation1,"航空改装",ResearchType.Engineering,new string[]{TiltAssembly});

            Add(Gunnery1,"炮战改装 I：前主炮",ResearchType.Engineering,new string[]{SuppressionAssembly});
            RegisterGunneryStaging();

            RegisterPopulationResearch();
            RegisterProductionResearch();
            RegisterReactorResearch();
            RegisterFactionUpgrades();
            RegisterAdvancedSuppressant();
            RegisterSpecialtyResearch();
            RegisterEscortBoost();

            // The four former performance upgrades are replaced by temporary
            // native power-level effects. Construction research grants no
            // duplicate permanent weapon, repair or hangar-efficiency buffs.

            Tree=Create(NativeType("TechTreeAttributesAsset"),TreeKey);

            object data=NewGameData("TechTreeAttributesData");((NamedObjectBase)data).InitializeName(TreeKey);

            object branch=NewGameData("TechTreeData");Set(branch,"m_TechTreeName","罗德岛本舰改装");

            Set(branch,"m_IconSpriteName",CarrierResearchIcon);

            object choice=NewGameData("TechTreeTierData");Set(choice,"m_TierName","改装构型（二选一）");

            Set(choice,"m_ResearchItems",new string[]{Aviation1,Gunnery1});

            // Route selection remains one exclusive tier. Only the gunnery
            // route can satisfy the following independent aft-station unlock.
            Set(branch,"m_TechTiers",BuildCarrierRouteTiers(choice));
            Set(branch,"m_Upgrades",BuildGunneryUpgradeEntries());
            Set(data,"m_TechTrees",BuildFactionUpgradeBranches(branch));Set(Tree,"m_Attributes",data);

            Enable(Tree);AddDefinition(TreeKey,data);Ready=true;Verify();

            // Restore only a scenario owner that BindCommander declared before

            // native scene registration. SimTick still validates the actual

            // commander's private tree before observing research or publishing.

            if(retainOwner)

            {

                lock(Gate){foreach(CommanderID id in declaredOwners)CommanderStates.Add(id,new CommanderResearchState(id));CommanderOrder=(CommanderID[])declaredOwners.Clone();Revision++;}
                Runtime.Log("RHODES_RESEARCH_OWNERS_RESTORED count="+declaredOwners.Length+"; retainedCommanderAssets="+RetainedCommanderAssets.Count+"; nativeCommanderTreeValidationPending=True; nativeResearchWrites=False");

            }

            Runtime.Log("RHODES_RESEARCH_REGISTERED privateTree="+TreeKey+"; researchItemCount="+Keys.Length+"; researchTiming=PerItemFormal; engineeringTreeCount="+(CapabilityEngineeringChains().Length+1)+"; capabilityStudiesEngineeringOnly=True; propertyStudiesUpgradeOnly=True; privateNativeResearchTime=True; engineeringChoiceSameTier=True; nativeCancelRefund=True; nativeSameTypeQueue=True; mutuallyExclusiveAtSimAndFrameValidation=True; originalFactionTreesUnchanged=True; runtimeAcceptancePending=True");

        }

        static void Add(string key,string title,ResearchType type,string[] dependencies)

        {

            ScriptableObject asset=Create(NativeType("ResearchItemAttributesAsset"),key);

            object data=NewData("ResearchItemAttributesData");((NamedObjectBase)data).InitializeName(key);

            Set(data,"m_ResearchType",type);ApplyResearchIcon(data,key);

            Set(data,"m_LocalizedResearchTitleStringID",title);Set(data,"m_LocalizedShortDescriptionStringID",title);

            string detail=key==Aviation1?"改装为航空构型，解锁舰载截击机与舰载重型轰炸机的生产和出击。与炮战改装二选一，完成后本局固定为航空构型。":"改装为炮战构型，部署一号主炮，基础单发伤害300。基础最大射程1600。二号主炮与主炮全装药可分别研究。与航空改装二选一，完成后本局固定为炮战构型。";

            if(key==Gunnery2)detail="部署二号主炮，基础单发伤害300，并继承已完成的主炮全装药强化。两座主炮的基础射程各增加200，接敌距离同步增加；近距盲区350不变。";
            else if(key==Gunnery3)detail="一号、二号主炮的基础单发伤害由300提高至600。已部署及后续接入的主炮均生效。两座主炮基础射程各增加200；与后主炮研究均完成时最大射程为2000，探测火控5档为2400。";

            if(key==LightAssembly)detail="解锁轻型突击车生产，并开放后续地面车辆装配。";
            else if(key==SuppressionAssembly)detail="解锁装甲突击车生产，并开放自行火炮装配与炮战改装。";
            else if(key==RailgunAssembly)detail="解锁轨道炮车生产，提供远程直射反装甲火力。";
            else if(key==AirDefenceAssembly)detail="解锁防空导弹车生产，提供防空导弹与手动曲射榴弹。";
            else if(key==TiltAssembly)detail="解锁武装倾转旋翼机与独立机库，支持干员搭载。使用舰侧跑道出击、小矩形升降机回收。各舰体构型均可使用。";
            else if(key==ArtilleryAssembly)detail="解锁自行火炮生产，提供远程间接炮击与手动单发射击。存在近距射击盲区，需要侦察与护卫。";
            Set(data,"m_LocalizedLongDescriptionStringID",detail);

            ApplyResearchBalance(data,key);

            Set(data,"m_ResearchDependencies",dependencies);Set(data,"m_ResearchVOCode",ResearchVoiceCode(key));

            Empty(data,"m_UnitTypeBuffs");Empty(data,"m_CommandsToRun");

            Set(asset,"m_Attributes",data);Enable(asset);Items.Add(key,asset);AddDefinition(key,data);

        }

        static void AddNativeUnlock(string key,string title,string nativeKey,string[] dependencies)
        {
            EntityTypeAttributes definition=Registry.GetEntityType(nativeKey);
            ResearchItemAttributes original=definition==null?null:definition.Get<ResearchItemAttributes>();
            if(original==null)throw new InvalidOperationException("Native trial research baseline missing: "+nativeKey);
            int cu=Convert.ToInt32(NativeRead.Get(original,"m_Resource1Cost"));
            int ru=Convert.ToInt32(NativeRead.Get(original,"m_Resource2Cost"));
            float seconds=Convert.ToSingle(NativeRead.Get(original,"m_ResearchTime"),System.Globalization.CultureInfo.InvariantCulture);
            if(cu<0||ru<0||seconds<=0f||float.IsNaN(seconds)||float.IsInfinity(seconds))throw new InvalidOperationException("Invalid native research baseline "+nativeKey);
            if(key==ArtilleryAssembly)
            {
                if(cu!=500||ru!=300||Math.Abs(seconds-100f)>.001f)throw new InvalidOperationException("Native C06 research donor differs from audited build12339551");
            }
            Add(key,title,ResearchType.Engineering,dependencies);
            Runtime.Log("RHODES_FACTION_RESEARCH_BASELINE key="+key+"; donor="+nativeKey+"; nativeCU="+cu+"; nativeRU="+ru+"; nativeSeconds="+seconds+"; CU="+ResearchCU(key)+"; RU="+ResearchRU(key)+"; seconds="+DurationSeconds(key)+"; perItemFormalBalance=True; dependencyOverride="+string.Join(",",dependencies));
        }

        // Call during registration only, before simulation creation. The native buff

        // name is a loadout WeaponID, not the weapon asset name.

        public static void AddWeaponBuff(string researchKey,string weaponID,int percent)

        {AddAttributeBuff(researchKey,weaponID,262148,percent);}

        static void AddAttributeBuff(string researchKey,string weaponID,int attributeID,int percent)

        {

            if(Bound)throw new InvalidOperationException("Research buffs must be finalized before commander binding");

            ScriptableObject research;

            if(!Items.TryGetValue(researchKey,out research)||string.IsNullOrEmpty(weaponID))throw new InvalidOperationException("Unknown private research/weapon ID");

            ScriptableObject buff=Create(NativeType("BuffSetAttributesAsset"),researchKey+"_"+weaponID+"_"+attributeID+"_Buff");

            object set=NewData("AttributeBuffSetData"),entry=NewData("AttributeBuffData");

            Set(entry,"m_Name",weaponID);SetEnum(entry,"m_AttributeID",attributeID);SetEnum(entry,"m_Mode",1);Set(entry,"m_Value",percent);

            Array entries=Array.CreateInstance(entry.GetType(),1);entries.SetValue(entry,0);Set(set,"m_Buffs",entries);

            Set(buff,"m_Attributes",set);Enable(buff);

            object target=NewData("UnitTypeBuffData");Set(target,"m_UnitTypePrefix",RhodesCarrierUnits.UnitKey);Set(target,"m_UseAsPrefix",false);

            SetEnum(target,"m_UnitClass",0);SetEnum(target,"m_ClassOperator",1);Set(target,"m_BuffSet",buff);

            object data=NativeRead.Get(research,"m_Attributes");Array prior=(Array)NativeRead.Get(data,"m_UnitTypeBuffs");

            Array all=Array.CreateInstance(target.GetType(),prior.Length+1);prior.CopyTo(all,0);all.SetValue(target,prior.Length);Set(data,"m_UnitTypeBuffs",all);Enable(research);

        }

        public static void BindCommander(CommanderAttributes privateCopy,CommanderID id)

        {

            if(!Ready||privateCopy==null)throw new InvalidOperationException("Register Rhodes research before binding the private commander");

            VerifySkirmishPopulationOpening(privateCopy);

            lock(Gate){if(id==CommanderID.None||CommanderStates.ContainsKey(id))throw new InvalidOperationException("Rhodes commander is invalid or already bound");}

            ScriptableObject source=NativeRead.Get(privateCopy,"m_FactionAttributes") as ScriptableObject;

            if(source==null)throw new InvalidOperationException("Commander faction asset is missing");

            ScriptableObject faction=Create(source.GetType(),"EF_Rhodes_Faction_"+id);

            Dictionary<object,object> seen=new Dictionary<object,object>(new Identity());

            foreach(FieldInfo field in Fields(source.GetType()))if(field.Name!="mDataCache")field.SetValue(faction,Copy(field.GetValue(source),seen));

            object factionData=NativeRead.Get(faction,"m_FactionAttributes");

            ((NamedObjectBase)factionData).InitializeName(faction.name);Set(factionData,"m_TechTreeAttributes",Tree);Enable(faction);

            Set(privateCopy,"m_FactionAttributes",faction);

            // Do not import old Coalition StartingTech into this independent tree.

            privateCopy.StartingTech=new string[0];privateCopy.LockedTech=new string[0];

            if(privateCopy.Faction.TechTree==null||privateCopy.Faction.TechTree.Name!=TreeKey||object.ReferenceEquals(privateCopy.Faction,NativeRead.Get(source,"m_FactionAttributes")))

                throw new InvalidOperationException("Private commander research isolation failed");

            lock(Gate){CommanderStates.Add(id,new CommanderResearchState(id));CommanderOrder=RhodesCommanderScopePolicy.OrderedOwners(CommanderStates.Keys);Revision++;}

            Runtime.Log("RHODES_RESEARCH_OWNER_DECLARED commander="+id+"; privateFaction="+faction.name+"; ownedAssets="+Owned.Count+"; nativeCommanderTreeValidationPending=True");

        }

        public static void Reset()
        {
            if(Receiver!=null)Receiver.RemoveHandler<ResearchEvent>(Handler);Receiver=null;
            lock(Gate){CommanderStates.Clear();CommanderOrder=new CommanderID[0];Revision++;}
        }

        // Bind before the first native Tick so a research event emitted inside
        // that tick already has its owner clock. Do not inspect or advance
        // native research here; the postfix snapshot remains authoritative.
        public static void PrepareSimulation(object simObject)
        {
            if(!Runtime.RhodesCarrier||!Ready||!Bound||simObject==null)return;
            Sim sim=simObject as Sim;if(sim==null)return;
            IEventReceiver current=(IEventReceiver)NativeRead.Get(sim,"SimEventSystem");
            if(!object.ReferenceEquals(current,Receiver))
            {
                if(Receiver!=null)Receiver.RemoveHandler<ResearchEvent>(Handler);Receiver=current;
                if(Receiver!=null)Receiver.AddHandler<ResearchEvent>(Handler);
                lock(Gate)foreach(CommanderResearchState state in CommanderStates.Values){state.Initialized=false;state.ClockCommander=null;}
            }
            CommanderID[] owners=DeclaredOwners();foreach(CommanderID owner in owners)PrepareClock(sim,owner);
        }
        public static void SimTick(object simObject)
        {
            PrepareSimulation(simObject);
            if(!Runtime.RhodesCarrier||!Ready||!Bound||simObject==null)return;
            Sim sim=simObject as Sim;if(sim==null)return;
            CommanderID[] owners=DeclaredOwners();foreach(CommanderID owner in owners)UpdateCommander(sim,owner);
        }
        static CommanderResearchState PrepareClock(Sim sim,CommanderID owner)
        {
            CommanderResearchState state;lock(Gate)
            {
                if(!CommanderStates.TryGetValue(owner,out state))return null;
                if(!object.ReferenceEquals(state.ClockSim,sim))
                {state.Initialized=false;state.ClockCommander=null;state.Completed.Clear();state.CompletionTicks.Clear();state.Active=string.Empty;state.GunneryCompletedTick=-1;state.Revision++;Revision++;}
                state.ClockSim=sim;
            }
            return state;
        }
        static void UpdateCommander(Sim sim,CommanderID owner)
        {
            CommanderResearchState state=PrepareClock(sim,owner);if(state==null)return;
            object manager=NativeRead.Get(sim,"CommanderManager");Commander commander=(Commander)NativeRead.Call(manager,"GetCommanderFromID",owner);
            if(commander==null||(string)NativeRead.Get(commander,"TechTreeTypeName")!=TreeKey)
            {lock(Gate){state.Initialized=false;state.ClockCommander=null;}return;}
            HashSet<string> done=state.Scratch;done.Clear();foreach(string key in (IEnumerable)NativeRead.Get(commander,"CompletedResearch"))if(IsOurs(key))done.Add(key);
            string active=string.Empty;foreach(ActiveResearchItem item in commander.GetAllActiveResearchItems())
                if(item!=null&&item.ResearchItem!=null&&IsOurs(item.ResearchItem.Name)){active=item.ResearchItem.Name;break;}
            bool changed;int revision,tick=ReadNativeFrame(sim);
            lock(Gate)
            {
                state.ClockCommander=commander;int priorTick=state.GunneryCompletedTick;bool ticksChanged=false;
                foreach(string key in done)if(RecordCompletion(state,key,tick))ticksChanged=true;
                foreach(string key in Keys)if(!done.Contains(key))state.CompletionTicks.Remove(key);
                state.GunneryCompletedTick=CompletionTick(state,Gunnery1);
                changed=!state.Initialized||state.Active!=active||state.Completed.Count!=done.Count||priorTick!=state.GunneryCompletedTick||ticksChanged;
                if(!changed)foreach(string key in done)if(!state.Completed.Contains(key)){changed=true;break;}
                if(changed){state.Completed.Clear();foreach(string key in done)state.Completed.Add(key);state.Active=active;state.Revision++;Revision++;}
                state.Initialized=true;revision=state.Revision;
            }
            if(changed){List<string> keys=new List<string>(done);keys.Sort(StringComparer.Ordinal);Runtime.Log("RHODES_RESEARCH_SNAPSHOT commander="+owner+"; revision="+revision+"; active="+active+"; completed="+string.Join(",",keys.ToArray())+"; authoritativeCommanderTree="+TreeKey+"; nativeResearchWrites=False");}
            if(done.Contains(Aviation1)&&done.Contains(Gunnery1))throw new InvalidOperationException("Native Rhodes route exclusivity invariant failed");
        }

        static void OnResearch(ResearchEvent evt)
        {
            if(evt==null||evt.ResearchItem==null||!IsOurs(evt.ResearchItem.Name))return;
            string key=evt.ResearchItem.Name;
            lock(Gate)
            {
                CommanderResearchState state;if(!CommanderStates.TryGetValue(evt.Commander,out state))return;
                if(evt.Reason==ResearchEvent.EventReason.Completed)
                {state.Completed.Add(key);if(state.Active==key)state.Active=string.Empty;if(state.ClockSim!=null)RecordCompletion(state,key,ReadNativeFrame(state.ClockSim));}
                else if(evt.Reason==ResearchEvent.EventReason.Started)state.Active=key;
                else if(evt.Reason==ResearchEvent.EventReason.Cancelled&&state.Active==key)state.Active=string.Empty;
                state.Revision++;Revision++;
            }
            Runtime.Log("RHODES_NATIVE_RESEARCH commander="+evt.Commander+"; item="+key+"; reason="+evt.Reason+"; stateReadOnly=True; unlockOwnedByNativeResearchDependencies=True");
        }

        static int CompletionTick(CommanderResearchState state,string key)
        {int tick;return state.CompletionTicks.TryGetValue(key,out tick)?tick:-1;}
        static bool RecordCompletion(CommanderResearchState state,string key,int tick)
        {
            if(state.CompletionTicks.ContainsKey(key))return false;
            state.CompletionTicks.Add(key,tick);if(key==Gunnery1)state.GunneryCompletedTick=tick;return true;
        }
        // A native capability check can precede the end-of-tick observer. Read
        // only this commander's tiny completed set so no one-tick early fire
        // or launch slips through. Reuse a commander-owned set, without a bit-width
        // limit, a per-call collection allocation, scene scan, or Unity access.
        public static void RefreshCompletedAtNativeGate()
        {CommanderID[] owners=DeclaredOwners();foreach(CommanderID owner in owners)RefreshCompletedAtNativeGate(owner);}
        public static void RefreshCompletedAtNativeGate(CommanderID owner)
        {
            lock(Gate)
            {
                CommanderResearchState state;if(!CommanderStates.TryGetValue(owner,out state)||state.ClockSim==null||state.ClockCommander==null)return;
                RefreshCompletedState(state,(IEnumerable)NativeRead.Get(state.ClockCommander,"CompletedResearch"),ReadNativeFrame(state.ClockSim));
            }
        }
        // Caller owns Gate. Separate the deterministic set reconciliation from
        // native state reads so owner isolation and entries past bit31 can be tested.
        static void RefreshCompletedState(CommanderResearchState state,IEnumerable nativeCompleted,int tick)
        {
            HashSet<string> completed=state.GateScratch;completed.Clear();
            foreach(string key in nativeCompleted)if(IsOurs(key))completed.Add(key);
            bool changed=false;
            foreach(string key in Keys)
            {
                if(completed.Contains(key)){if(state.Completed.Add(key))changed=true;if(RecordCompletion(state,key,tick))changed=true;}
                else{if(state.Completed.Remove(key))changed=true;state.CompletionTicks.Remove(key);}
            }
            state.GunneryCompletedTick=CompletionTick(state,Gunnery1);
            if(changed){state.Revision++;Revision++;}
        }
        static int ReadNativeFrame(Sim sim)

        {

            uint frame=Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim,"GlobalFrameCount"),"FrameNumber"));

            if(frame>int.MaxValue)throw new InvalidOperationException("Rhodes completion frame exceeds presentation clock range");

            return (int)frame;

        }

        public static int CompletedSimTick(CommanderID owner,string key)
        {
            if(string.IsNullOrEmpty(key))return -1;
            lock(Gate)
            {
                CommanderResearchState state;
                if(!CommanderStates.TryGetValue(owner,out state)||!state.Initialized||!state.Completed.Contains(key))return -1;
                return CompletionTick(state,key);
            }
        }

        static bool IsOurs(string key){return Array.IndexOf(Keys,key)>=0;}

        static void Verify()

        {

            if(Tree==null||Registry.GetEntityType(TreeKey)==null)throw new InvalidOperationException("Private Rhodes tech tree missing");

            TechTreeAttributes data=(TechTreeAttributes)NativeRead.Get(Tree,"m_Attributes");

            VerifyCapabilityEngineeringLayout(data);
            VerifyResearchIcons(data);
            VerifyResearchBalanceCatalog();

            foreach(string key in Keys)

            {
                ResearchItemAttributes item=Registry.GetEntityType(key).Get<ResearchItemAttributes>();
                if(item==null||item.Name!=key)throw new InvalidOperationException("Private research registration failed: "+key);
                bool property=key==PredictiveFireControl||key==Gunnery3||IsReactorResearch(key)||IsSpecialtyResearch(key);foreach(FleetUpgrade spec in FleetUpgrades())if(spec.Key==key)property=true;
                if(item.TypeOfResearch!=(property?ResearchType.Upgrade:ResearchType.Engineering))
                    throw new InvalidOperationException("Private research has the wrong native panel/queue category: "+key);
                VerifyResearchBalance(item,key);
            }

            VerifyPopulationResearch();
            VerifyProductionResearch();
            VerifyReactorResearch();
            VerifyFactionUpgrades();
            VerifyAdvancedSuppressant();
            VerifyGunneryStaging(data);
            VerifySpecialtyResearch(data);
            VerifyEscortBoost(data);
            VerifyResearchDependencyGraph();

        }

        static void AddDefinition(string key,object data)

        {EntityTypeAttributes definition=new EntityTypeAttributes(key);if(!definition.Add(data))throw new InvalidOperationException("Cannot register private research datum");Registry.Add(definition);}

        static Type NativeType(string name){return typeof(UnitAssetContainer).Assembly.GetType("BBI.Unity.Game.Data."+name,true);}

        static object NewGameData(string name){return Activator.CreateInstance(typeof(ResearchItemAttributes).Assembly.GetType("BBI.Game.Data."+name,true),true);}

        static Array ArrayOf(object item){Array array=Array.CreateInstance(item.GetType(),1);array.SetValue(item,0);return array;}

        static object NewData(string name){return Activator.CreateInstance(NativeType(name),true);}

        static void Empty(object value,string field){FieldInfo member=null;foreach(FieldInfo item in Fields(value.GetType()))if(item.Name==field)member=item;if(member==null)throw new MissingFieldException(field);Set(value,field,Array.CreateInstance(member.FieldType.GetElementType(),0));}

        static void SetEnum(object value,string field,int data){object old=NativeRead.Get(value,field);Set(value,field,Enum.ToObject(old.GetType(),data));}

        static ScriptableObject Create(Type type,string name)

        {ScriptableObject asset=RhodesAssetClone59.CreateEmpty(type);asset.name=name;asset.hideFlags=HideFlags.HideAndDontSave;Owned.Add(asset);return asset;}

        static void Enable(object asset)

        {for(Type type=asset.GetType();type!=null;type=type.BaseType){MethodInfo method=type.GetMethod("OnEnable",Declared,null,Type.EmptyTypes,null);if(method!=null){method.Invoke(asset,null);return;}}}

        static void Set(object value,string name,object data){NativeRead.Set(value,name,data);}

        static List<FieldInfo> Fields(Type type)

        {List<FieldInfo> result=new List<FieldInfo>();for(Type t=type;t!=null&&t!=typeof(object)&&t!=typeof(UnityEngine.Object)&&t!=typeof(ScriptableObject);t=t.BaseType)foreach(FieldInfo f in t.GetFields(Declared))if(!f.IsStatic)result.Add(f);return result;}

        static object Copy(object value,Dictionary<object,object> seen)

        {

            if(value==null)return null;Type type=value.GetType();if(type.IsPrimitive||type.IsEnum||type==typeof(string)||typeof(UnityEngine.Object).IsAssignableFrom(type))return value;

            object prior;if(seen.TryGetValue(value,out prior))return prior;Array array=value as Array;

            if(array!=null){Array copied=(Array)array.Clone();seen.Add(value,copied);for(int i=0;i<array.Length;i++)copied.SetValue(Copy(array.GetValue(i),seen),i);return copied;}

            if(type.Namespace==null||!type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))return value;

            object clone=Memberwise.Invoke(value,null);seen.Add(value,clone);foreach(FieldInfo f in Fields(type))f.SetValue(clone,Copy(f.GetValue(value),seen));return clone;

        }

        sealed class Identity:IEqualityComparer<object>

        {public new bool Equals(object a,object b){return object.ReferenceEquals(a,b);}public int GetHashCode(object value){return RuntimeHelpers.GetHashCode(value);}}

    }

}

