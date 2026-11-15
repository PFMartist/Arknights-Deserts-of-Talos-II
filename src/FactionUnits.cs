using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using BBI.Core;
using BBI.Core.Data;
using BBI.Game.Data;
using BBI.Unity.Game;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Private definitions. Native simulation continues to own movement,
    // harvesting, resource deposits, projectiles, repair and aircraft sorties.
    // This is an explicitly adjustable technical-trial balance, not final lore.
    public static partial class FactionUnits
    {
        public const string HarvesterKey="RI_U01_HARVESTER",SurveyKey="RI_U02_FIELD_SURVEY";
        public const string EngineeringKey="RI_U03_ENGINEERING",EscortKey="RI_U04_LIGHT_ESCORT";
        public const string SuppressionKey="RI_U05_SUPPRESSION",RailgunKey="RI_U06_RAILGUN";
        public const string AirDefenceKey="RI_U07_AIR_DEFENCE",TiltStrikeKey="RI_U08_TILT_STRIKE";
        public const string ArtilleryKey="RI_U09_ARTILLERY";
        public const string RepairWeaponID="REPAIR";
        public sealed class UnitSpec
        {
            public readonly string Key,Donor,Title,Description,Research,ProductionKey;
            public readonly int Resource1,Resource2,Population;public readonly float Seconds;public readonly KeyCode Hotkey;
            internal UnitSpec(string key,string donor,string title,string description,string research,int r1,int r2,int pop,float seconds,KeyCode hotkey)
            {Key=key;Donor=donor;Title=title;Description=description;Research=research;Resource1=r1;Resource2=r2;Population=pop;Seconds=seconds;Hotkey=hotkey;ProductionKey="Ability_"+key+"_Produce";}
        }
        static readonly UnitSpec[] Roster={
            new UnitSpec(HarvesterKey,"C_Harvester_MP","资源采集车","采集通用物资与精制资源，并运回本舰或工程支援车。",null,100,0,1,12f,KeyCode.J),
            new UnitSpec(SurveyKey,"C_Baserunner_MP","外勤勘探车","部署探针开展侦察，并将战场遗物运往回收区。",null,250,0,2,16f,KeyCode.K),
            new UnitSpec(EngineeringKey,"C_Baserunner_MP","工程支援车","为前线提供资源接收与维修支援，可自动修复附近受损的友军载具。",null,400,0,4,35f,KeyCode.N),
            new UnitSpec(EscortKey,"C_Escort_MP","轻型突击车","机动灵活的轻型战斗车辆，适合侦察、袭扰与侧翼护卫。",RhodesResearch.LightAssembly,100,0,2,8f,KeyCode.J),
            new UnitSpec(SuppressionKey,"C_HAC_MP","装甲突击车","搭载连射等离子炮，以范围爆炸打击成群地面目标；保留全向炮塔和烟幕弹，射程较短。",RhodesResearch.SuppressionAssembly,220,40,3,18f,KeyCode.K),
            new UnitSpec(RailgunKey,"C_Railgun_MP","轨道炮车","远距离直射反装甲载具，适合在侦察掩护下攻击装甲目标；近距离作战需要友军保护。",RhodesResearch.RailgunAssembly,200,60,3,14f,KeyCode.J),
            new UnitSpec(AirDefenceKey,"C_HAC_Upgrade01_MP","防空导弹车","以防空导弹保护编队，并可使用曲射榴弹支援对地作战。",RhodesResearch.AirDefenceAssembly,350,80,3,21f,KeyCode.K),
            new UnitSpec(TiltStrikeKey,"C_Interceptor_MP","武装倾转旋翼机","搭载干员实施隐蔽部署，可在空中收放干员，并由舱内就绪干员施放技能，与地面施法共享冷却。出击后按令作战，弹药耗尽后留场；靠近敌军或开火施法时暴露。",RhodesResearch.TiltAssembly,150,100,2,15f,KeyCode.J),
            new UnitSpec(ArtilleryKey,"C_ArtilleryCruiser_MP","自行火炮","搭载于中型底盘的单炮火力支援载具，以远程曲射打击地面目标。可切换射击模式；存在近距离射击盲区，需要友军掩护。",RhodesResearch.ArtilleryAssembly,ArtilleryCostCU,ArtilleryCostRU,ArtilleryPopulation,ArtilleryProductionSeconds,KeyCode.N)
        };
        sealed class Context
        {
            public UnitSpec Spec;public UnitAssetContainer Source,Custom;public ScriptableObject Core,View,Hud;
            public EntityTypeAttributes Definition;
            public readonly Dictionary<object,object> Map=new Dictionary<object,object>(new IdentityComparer());
            public readonly List<ScriptableObject> Owned=new List<ScriptableObject>();
            public readonly List<GameObject> OwnedPresentation=new List<GameObject>();
            public readonly List<SourceRecord> Sources=new List<SourceRecord>();
        }
        sealed class SourceRecord {public ScriptableObject Asset;public string Fingerprint;}
        sealed class IdentityComparer:IEqualityComparer<object>
        {public new bool Equals(object a,object b){return object.ReferenceEquals(a,b);}public int GetHashCode(object value){return RuntimeHelpers.GetHashCode(value);}}
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
        static readonly MethodInfo CloneMethod=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        static EntityTypeCollection Registry;
        static readonly Dictionary<string,Context> Contexts=new Dictionary<string,Context>(StringComparer.Ordinal);
        public static bool Ready {get;private set;}
        public static UnitSpec[] Specs {get{return (UnitSpec[])Roster.Clone();}}
        public static bool IsFactionKey(string key){return key!=null&&FindSpec(key)!=null;}
        internal static void ConfigureStartingHangarPolicy(object core,string key)
        {
            if(core==null||FindSpec(key)==null)throw new ArgumentException("Private faction unit required for hangar policy");
            if(key!=TiltStrikeKey)Write(core,"m_StartsInHangar",true);
        }
        public static UnitSpec FindSpec(string key){foreach(UnitSpec item in Roster)if(item.Key==key)return item;return null;}
        public static EntityTypeAttributes GetDefinition(string key){Context c;return Contexts.TryGetValue(key,out c)?c.Definition:null;}
        public static UnitAssetContainer GetContainer(string key){Context c;return Contexts.TryGetValue(key,out c)?c.Custom:null;}
        public static UnitAssetContainer GetSource(string key){Context c;return Contexts.TryGetValue(key,out c)?c.Source:null;}
        // Level-unload owner calls after faction views are no longer active.
        // The same guard used for registry replacement prevents destroying a
        // live view's ScriptableObject generation during an accidental reset.
        public static void Reset(){ReleaseStale();Registry=null;Ready=false;}
        public static void Register()
        {
            EntityTypeCollection current=(EntityTypeCollection)NativeRead.Get(typeof(ShipbreakersMain),"sEntityTypes");
            if(current==null)throw new InvalidOperationException("Faction registration needs the current native registry");
            if(object.ReferenceEquals(current,Registry)){if(!Ready)throw new InvalidOperationException("Faction registration previously failed");VerifyRegistered();return;}
            ReleaseStale();Registry=current;Ready=false;
            RegisterSurveyProbe();
            foreach(UnitSpec spec in Roster)RegisterOne(spec);
            RhodesSurveyArtifacts.Register(current);
            Ready=true;VerifyRegistered();
            Runtime.Log("RHODES_SURVEY_ARTIFACT_READY unit=RI_U02_FIELD_SURVEY; salvage=NativeCollectible; relicCapacity=1; destination=OwnedExtractionZone; nativeCollectorsPreserved=8; nativeScoringUnchanged=True; privateQuery=True");
            Runtime.Log("RHODES_FACTION_REGISTERED units="+Roster.Length+"; nativeDefinitionsImmutable=True; economics=NativeBuild12339551ExceptU03AndU09PrivateTrials; U03=BaserunnerMovementWithSupportStorageAndSingleRepair; U07=NativeAAMortar; U08=LargeTiltrotorNativeSortie; U09=MediumLightSingleShotArtillery; noScriptedUnitSpawns=True; gameAcceptancePending=True");
        }
        static UnitAssetContainer FindSource(string key)
        {
            EntityTypeAttributes definition=Registry.GetEntityType(key);UnitAttributes expected=definition==null?null:definition.Get<UnitAttributes>();
            if(expected==null)throw new InvalidOperationException("Native faction donor missing "+key);
            UnitAssetContainer found=null;
            foreach(UnitAssetContainer item in Resources.FindObjectsOfTypeAll(typeof(UnitAssetContainer)))
                if(item!=null&&item.name==key&&object.ReferenceEquals(Read(Read(item,"m_Attributes"),"m_Attributes"),expected))
                {if(found!=null)throw new InvalidOperationException("Multiple current donor containers "+key);found=item;}
            if(found==null)throw new InvalidOperationException("Loaded native donor container missing "+key);return found;
        }
        static void RegisterOne(UnitSpec spec)
        {
            if(Registry.GetEntityType(spec.Key)!=null)throw new InvalidOperationException("Faction key collision "+spec.Key);
            Context c=new Context();c.Spec=spec;c.Source=FindSource(spec.Donor);Contexts.Add(spec.Key,c);
            UnitAttributes baseline=Registry.GetEntityType(spec.Donor).Get<UnitAttributes>();
            if(spec.Key==ArtilleryKey)VerifyArtilleryDonor(baseline);
            if(spec.Key==SuppressionKey)
            {EntityTypeAttributes av=Registry.GetEntityType(SuppressionDonor);VerifySuppressionDonors(baseline,av==null?null:av.Get<UnitAttributes>());}
            if(spec.Key!=EngineeringKey&&spec.Key!=ArtilleryKey&&spec.Key!=SuppressionKey&&(baseline.Resource1Cost!=spec.Resource1||baseline.Resource2Cost!=spec.Resource2||baseline.PopCapCost!=spec.Population||
                Math.Abs(BBI.Core.Utility.FixedPoint.Fixed64.UnsafeFloatValue(baseline.ProductionTime)-spec.Seconds)>.001f))
                throw new InvalidOperationException("Native build trial economy changed: "+spec.Donor);
            c.Custom=(UnitAssetContainer)CloneAsset(c,c.Source,spec.Key);
            CloneClosure(c);
            foreach(ScriptableObject asset in c.Owned)RemapAsset(c,asset);
            c.Core=(ScriptableObject)Read(c.Custom,"m_Attributes");c.View=(ScriptableObject)Read(c.Custom,"m_View");c.Hud=(ScriptableObject)Read(c.Custom,"m_HUD");
            if(spec.Key==EngineeringKey)PrepareEngineering(c);
            if(spec.Key==ArtilleryKey){PrepareArtillery(c);PrepareArtilleryVoiceAndTactical(c);}
            if(spec.Key==SuppressionKey)PrepareSuppression(c);
            if(spec.Key==SurveyKey)PrepareSurveyProbeProduction(c);
            if(spec.Key==TiltStrikeKey)EnsureTiltrotorStop(c);
            FilterAbilities(c);CloneClosure(c);
            foreach(ScriptableObject asset in c.Owned)RemapAsset(c,asset);
            if(!object.ReferenceEquals(Read(c.Custom,"m_Attributes"),c.Core)||!object.ReferenceEquals(Read(c.Custom,"m_View"),c.View)||!object.ReferenceEquals(Read(c.Custom,"m_HUD"),c.Hud))
                throw new InvalidOperationException("Faction private asset generation drifted during closure "+spec.Key);
            object core=Read(c.Core,"m_Attributes");
            Write(core,"m_Resource1Cost",spec.Resource1);Write(core,"m_Resource2Cost",spec.Resource2);
            Write(core,"m_ProductionTime",spec.Seconds);Write(core,"m_PopCapCost",spec.Population);
            Write(core,"m_WorldHeightOffset",0f);
            if(spec.Key==TiltStrikeKey)Write(core,"m_Class",UnitClass.Air|UnitClass.Large|UnitClass.Strikecraft);
            // Native opening fleets deploy through their carrier hangar.
            // Normal production retains the untracked ground bay routing.
            ConfigureStartingHangarPolicy(core,spec.Key);
            ConfigureFootprint(c);
            if(spec.Key==TiltStrikeKey){RhodesTiltrotorFlightConfig.Prepare(c.Custom,c.Source);PrepareOperatorTransport(c);}
            object hud=Read(c.Hud,"m_Attributes");Write(hud,"mUnitLocalizedNameStringID",spec.Title);
            Write(hud,"mUnitLocalizedShortDescriptionStringID",spec.Description);
            Write(hud,"mUnitLocalizedLongDescriptionStringID",spec.Description);
            NormalizePrivateVeterancy(c.Custom,spec.Key);
            RhodesUnitVoiceAliases.ConfigureUnitAudio(c.Custom,spec.Key);
            PrepareFactionAbilityIdentities(c);
            foreach(ScriptableObject asset in c.Owned)if(!object.ReferenceEquals(asset,c.Custom))CallIfPresent(asset,"OnEnable");
            if(spec.Key==SurveyKey)RegisterSurveyProbeAbilities(c);
            if(spec.Key==TiltStrikeKey)RegisterOperatorTransportAbilities(c);
            if(spec.Key==TiltStrikeKey)RhodesTiltrotorFlightConfig.VerifyConfigured(c.Custom);
            if(spec.Key==ArtilleryKey)RegisterArtilleryCommands(c);
            RegisterFactionAbilityCommands(c);
            UnitViewAttributes view=(UnitViewAttributes)Read(c.View,"Data"),sourceView=(UnitViewAttributes)Read(Read(c.Source,"m_View"),"Data");
            if(object.ReferenceEquals(view,sourceView))throw new InvalidOperationException("Faction view aliases native source");
            ConfigureTurretContracts(c,view);
            FactionUnitModels.ConfigureView(spec.Key,view,sourceView);
            if(spec.Key!=TiltStrikeKey)FactionVehicleLights.Append(view,Registry.GetEntityType("C_Escort_MP").Get<UnitViewAttributes>());
            Write(c.Custom,"mDataCache",null);CallRequired(c.Custom,"OnEnable");
            EntityTypeAttributes definition=new EntityTypeAttributes(spec.Key);
            foreach(object datum in (IEnumerable)c.Custom)if(datum!=null&&!definition.Add(datum))throw new InvalidOperationException("Duplicate faction datum "+spec.Key+" "+datum.GetType().Name);
            Registry.Add(definition);c.Definition=definition;VerifyContext(c);
            Runtime.Log("RHODES_FACTION_UNIT key="+spec.Key+"; donor="+spec.Donor+"; class="+definition.Get<UnitAttributes>().Class+"; CU="+spec.Resource1+"; RU="+spec.Resource2+"; seconds="+spec.Seconds+"; population="+spec.Population+"; nativeSimulationAuthority=True");
        }
        static void ConfigureFootprint(Context c)
        {
            Bounds bounds=FactionUnitModels.BoundsFor(c.Spec.Key);
            float width=Math.Max(.5f,bounds.size.x),length=Math.Max(1f,bounds.size.z);
            object sim=Read(Read(c.Custom,"m_Movement"),"m_Simulation"),dynamics=Read(sim,"m_Dynamics");
            Write(dynamics,"m_Width",width);Write(dynamics,"m_Length",length);
            if(c.Spec.Key==TiltStrikeKey)return;
            float clearance=Math.Max(width,length)*.5f+.25f;
            object donorSim=Read(Read(c.Source,"m_Movement"),"m_Simulation"),donorDynamics=Read(donorSim,"m_Dynamics");
            object donorManeuvers=Read(donorSim,"m_Maneuvers"),donorAvoidance=Read(donorSim,"m_Avoidance");
            NativeFormationSpacing spacing=FormationSpacingPolicy.Evaluate(width,length,
                (float)Read(donorDynamics,"m_Width"),(float)Read(donorDynamics,"m_Length"),
                (float)Read(donorManeuvers,"m_FormationSpacingDistance"),(float)Read(donorManeuvers,"m_FormationSectionGapAhead"));
            object nav=Read(c.Core,"m_NavMesh");
            if(nav==null)throw new InvalidOperationException("Ground faction unit has no private nav mesh "+c.Spec.Key);
            Write(Read(nav,"m_NavMeshConfig"),"m_DistanceFromObstacles",clearance);
            Write(dynamics,"m_MaxSpeedTurnRadius",length);
            object maneuvers=Read(sim,"m_Maneuvers");
            Write(maneuvers,"m_ObstacleAvoidanceDistance",FormationSpacingPolicy.PreserveDynamicAvoidance(clearance,(float)Read(donorManeuvers,"m_ObstacleAvoidanceDistance")));
            Write(maneuvers,"m_FormationSpacingDistance",spacing.MemberGap);
            Write(maneuvers,"m_FormationSectionGapAhead",spacing.SectionGapAhead);
            Write(maneuvers,"m_ArrivalDistance",Math.Max(1f,length*.15f));
            Write(Read(sim,"m_Avoidance"),"m_AvoidanceDistance",FormationSpacingPolicy.PreserveDynamicAvoidance(clearance,(float)Read(donorAvoidance,"m_AvoidanceDistance")));
        }
        static void EnsureTiltrotorStop(Context c)
        {
            Array previous=(Array)Read(c.Custom,"m_Abilities");
            ScriptableObject stop=null;
            foreach(ScriptableObject ability in previous)
            {
                AbilityAttributes data=ability==null?null:Read(ability,"m_Attributes") as AbilityAttributes;
                if(data!=null&&data.AbilityType==AbilityClass.Stop){stop=ability;break;}
            }
            List<ScriptableObject> abilities=new List<ScriptableObject>();foreach(ScriptableObject ability in previous)abilities.Add(ability);
            if(stop==null)
            {
                ScriptableObject source=null;
                foreach(ScriptableObject ability in (Array)Read(FindSource("C_Escort_MP"),"m_Abilities"))
                {
                    AbilityAttributes data=ability==null?null:Read(ability,"m_Attributes") as AbilityAttributes;
                    if(data!=null&&data.AbilityType==AbilityClass.Stop){source=ability;break;}
                }
                if(source==null)throw new InvalidOperationException("Native ground Stop template unavailable for U08 hover");
                stop=CloneAsset(c,source,source.name);abilities.Add(stop);
            }
            // Field transfers use airborne hover. Only the ordinary native
            // Stop remains; there is no ground-landing ability or L hotkey.
            SetAbilities(c,abilities);
            Runtime.Log("RHODES_U08_NATIVE_STOP_ADDED privateClone=True; fieldLandingAbility=False; airborneOperatorTransfer=True; nativeMotherShipLaunchRecoveryRetained=True");
        }
        static void ConfigureTurretContracts(Context c,UnitViewAttributes view)
        {
            if(c.Spec.Key==ArtilleryKey)ConfigureArtilleryTurret(c,view);
            else if(c.Spec.Key==EngineeringKey)
            {
                UnitViewAttributes.TurretAttributes repair=new UnitViewAttributes.TurretAttributes();
                repair.BoneName="EF_Faction_RepairYaw";repair.PitchBoneName=string.Empty;repair.SimTurretOrigin=repair.BoneName;
                repair.CenterEmissionBone="EF_Faction_Muzzle";repair.FXEmissionBones=new string[]{repair.CenterEmissionBone};
                repair.PitchMin=0f;repair.PitchMax=0f;repair.MaxPitchDegreesPerSecond=0f;repair.MaxRollDegreesPerSecond=0f;
                repair.StateAnimationTriggers=new UnitViewAttributes.TurretOperatingAnimationTrigger[0];
                view.TurretDefinitions=new UnitViewAttributes.TurretAttributes[]{repair};
            }
            else if(c.Spec.Key==AirDefenceKey)
            {
                if(view.TurretDefinitions==null||view.TurretDefinitions.Length!=1)throw new InvalidOperationException("Native AA/Mortar source must have one shared turret");
                UnitViewAttributes.TurretAttributes mortar=(UnitViewAttributes.TurretAttributes)CopyData(view.TurretDefinitions[0],new Dictionary<object,object>(new IdentityComparer()));
                mortar.BoneName="EF_Faction_MortarYaw";mortar.PitchBoneName="EF_Faction_MortarPitch";mortar.SimTurretOrigin=mortar.BoneName;
                mortar.CenterEmissionBone="EF_Faction_Mortar";mortar.FXEmissionBones=new string[]{mortar.CenterEmissionBone};
                mortar.PitchMin=0f;mortar.PitchMax=80f;mortar.MaxPitchDegreesPerSecond=90f;mortar.MaxRollDegreesPerSecond=0f;
                mortar.StateAnimationTriggers=new UnitViewAttributes.TurretOperatingAnimationTrigger[0];
                view.TurretDefinitions=new UnitViewAttributes.TurretAttributes[]{view.TurretDefinitions[0],mortar};
                bool found=false;Array bindings=(Array)Read(c.Core,"m_WeaponLoadout");
                for(int i=0;i<bindings.Length;i++)
                {object binding=bindings.GetValue(i);if((string)Read(binding,"m_WeaponID")=="GRENADE"){Write(binding,"m_TurretIndex",1);bindings.SetValue(binding,i);found=true;}}
                if(!found)throw new InvalidOperationException("Native Mortar binding absent");
            }
        }
        static void PrepareEngineering(Context c)
        {
            UnitAssetContainer support=FindSource("C_SupportCruiser_MP");
            ScriptableObject storage=(ScriptableObject)Read(support,"m_Storage");
            Write(c.Custom,"m_Storage",CloneAsset(c,storage,EngineeringKey+"_Storage"));
            Write(c.Custom,"m_Harvester",null);
            ScriptableObject repair=null,disable=null,weapon=null;object binding=null;
            foreach(ScriptableObject a in (Array)Read(support,"m_Abilities"))
            {if(a!=null&&a.name=="Ability_C_Repair_MP")repair=a;if(a!=null&&a.name=="Ability_Common_DisableResourceController")disable=a;}
            foreach(object item in (Array)Read(Read(support,"m_Attributes"),"m_WeaponLoadout"))if((string)Read(item,"m_WeaponID")==RepairWeaponID){binding=item;weapon=(ScriptableObject)Read(item,"m_Weapon");}
            if(repair==null||disable==null||weapon==null||binding==null)throw new InvalidOperationException("Native U03 support interfaces missing");
            ScriptableObject privateWeapon=CloneAsset(c,weapon,EngineeringKey+"_RepairWeapon");
            object sim=Read(privateWeapon,"m_Simulation"),turret=Read(sim,"m_Turret");
            Write(turret,"m_FieldOfView",186f);Write(turret,"m_FieldOfFire",6f);
            object copiedBinding=CopyData(binding,new Dictionary<object,object>(new IdentityComparer()));Write(copiedBinding,"m_Weapon",privateWeapon);
            Write(copiedBinding,"m_TurretIndex",0);Write(copiedBinding,"m_TurretDefaultAngleOffsetDegrees",0f);
            Array loadout=Array.CreateInstance(copiedBinding.GetType(),1);loadout.SetValue(copiedBinding,0);Write(c.Core,"m_WeaponLoadout",loadout);
            Array oldWeapons=(Array)Read(c.Custom,"m_Weapons");Array weapons=Array.CreateInstance(oldWeapons.GetType().GetElementType(),1);weapons.SetValue(privateWeapon,0);Write(c.Custom,"m_Weapons",weapons);
            List<ScriptableObject> abilities=new List<ScriptableObject>();
            foreach(ScriptableObject a in (Array)Read(c.Custom,"m_Abilities"))if(a!=null&&(a.name.StartsWith("Ability_Common_",StringComparison.Ordinal)||a.name=="Ability_Veterancy"))abilities.Add(a);
            abilities.Add(CloneAsset(c,repair,"Ability_RI_U03_Repair"));abilities.Add(CloneAsset(c,disable,disable.name));
            SetAbilities(c,abilities);
            Write(Read(c.Core,"m_Attributes"),"m_RepairPickupRange",400);
            object ownStorage=Read(Read(c.Custom,"m_Storage"),"m_Attributes");
            if(!(bool)Read(ownStorage,"m_LinkToPlayerBank")||!(bool)Read(ownStorage,"m_IsResourceController"))throw new InvalidOperationException("U03 is not a native resource controller");
        }
        static void FilterAbilities(Context c)
        {
            List<ScriptableObject> abilities=new List<ScriptableObject>();
            foreach(ScriptableObject a in (Array)Read(c.Custom,"m_Abilities"))
            {
                if(a==null)continue;object data=Read(a,"m_Attributes");AbilityAttributes attributes=data as AbilityAttributes;
                if(c.Spec.Key==HarvesterKey&&!KeepHarvesterAbility(a.name,data,Read(a,"m_View")))continue;
                if(c.Spec.Key==SuppressionKey&&!KeepSuppressionAbility(a.name))continue;
                bool probe=c.Spec.Key==SurveyKey&&a.name.IndexOf("Probe",StringComparison.OrdinalIgnoreCase)>=0;
                if(attributes!=null&&attributes.AbilityType==AbilityClass.ProduceUnit&&!probe)continue;
                if(c.Spec.Key==SurveyKey&&!(a.name.StartsWith("Ability_Common_",StringComparison.Ordinal)||a.name=="Ability_Veterancy"||probe||a.name=="Ability_C_DeployScanner"||a.name=="Ability_C_Baserunner_ContextMenu_Default"||a.name==RhodesSurveyArtifacts.SalvageAbility))continue;
                if(a.name.IndexOf("UpgradeTo",StringComparison.OrdinalIgnoreCase)>=0)continue;
                // The private eight-unit research tree owns capabilities; old
                // Coalition prerequisite IDs must not strand valid commands.
                ConfigureFactionAbilityResearch(c.Spec.Key,a.name,data);
                if(c.Spec.Key==EngineeringKey&&attributes!=null&&attributes.AbilityType==AbilityClass.RepairTarget)
                {Write(Read(data,"m_Autocast"),"m_AutocastEnabled",true);Write(Read(data,"m_Autocast"),"m_AutocastEnabledOnSpawn",true);Write(Read(a,"m_View"),"LocalizedTitleStringID","维修光束");}
                if(c.Spec.Key==AirDefenceKey&&a.name=="Ability_C_Grenade_MP")Write(Read(a,"m_View"),"LocalizedTitleStringID","曲射榴弹");
                if(c.Spec.Key==SurveyKey&&a.name==RhodesSurveyArtifacts.SalvageAbility)
                {Write(Read(a,"m_View"),"LocalizedShortDescriptionStringID","部署并回收战场遗物");Write(Read(a,"m_View"),"LocalizedLongDescriptionStringID","命令外勤勘探车前往目标遗物并部署回收。装载后将车辆驶入己方遗物回收区，完成遗物运出。");}
                abilities.Add(a);
            }
            SetAbilities(c,abilities);
            object hud=Read(c.Hud,"m_Attributes");foreach(object context in (Array)Read(hud,"m_HUDContexts"))
            {List<string> order=new List<string>();foreach(ScriptableObject a in abilities)order.Add(a.name);Write(context,"m_AbilityOrder",order.ToArray());ClearArray(context,"m_AdditionalAbilities");}
            if(c.Spec.Key==EngineeringKey){Write(Read(c.Core,"m_Attributes"),"m_NumProductionQueues",0);Write(Read(c.Core,"m_Attributes"),"m_ShowProductionQueues",false);}
        }
        static void SetAbilities(Context c,List<ScriptableObject> abilities)
        {Array prior=(Array)Read(c.Custom,"m_Abilities");Array value=Array.CreateInstance(prior.GetType().GetElementType(),abilities.Count);for(int i=0;i<abilities.Count;i++)value.SetValue(abilities[i],i);Write(c.Custom,"m_Abilities",value);}
        public static void VerifyRegistered(){foreach(UnitSpec spec in Roster){Context c;if(!Contexts.TryGetValue(spec.Key,out c)||!object.ReferenceEquals(Registry.GetEntityType(spec.Key),c.Definition))throw new InvalidOperationException("Faction definition missing "+spec.Key);VerifyContext(c);}VerifySurveyProbe();RhodesSurveyArtifacts.Verify(Registry);}
        static void VerifyContext(Context c)
        {
            VerifyPrivateVeterancy(c);
            if(c.Spec.Key==HarvesterKey)VerifyHarvesterCleanup(c);
            if(c.Spec.Key==ArtilleryKey)VerifyArtillery(c);
            if(c.Spec.Key==SuppressionKey)VerifySuppression(c);
            if(c.Spec.Key==TiltStrikeKey)VerifyOperatorTransport(c);
            if(c.Spec.Key==SurveyKey)VerifySurveyArtifacts(c);
            VerifyFactionAbilityResearch(c);
            UnitAttributes unit=c.Definition.Get<UnitAttributes>();if(unit==null||object.ReferenceEquals(unit,Registry.GetEntityType(c.Spec.Donor).Get<UnitAttributes>()))throw new InvalidOperationException("Faction core not private "+c.Spec.Key);
            if(unit.Resource1Cost!=c.Spec.Resource1||unit.Resource2Cost!=c.Spec.Resource2||unit.PopCapCost!=c.Spec.Population)throw new InvalidOperationException("Faction trial economy drift "+c.Spec.Key);
            if(!object.ReferenceEquals(Read(c.Custom,"m_Attributes"),c.Core)||!object.ReferenceEquals(Read(c.Custom,"m_View"),c.View)||!object.ReferenceEquals(Read(c.Custom,"m_HUD"),c.Hud))
                throw new InvalidOperationException("Faction cached asset generation differs from container "+c.Spec.Key);
            UnitViewAttributes view=(UnitViewAttributes)Read(c.View,"Data");
            if(c.Spec.Key==EngineeringKey&&(unit.WeaponLoadout.Length!=1||unit.WeaponLoadout[0].WeaponID!=RepairWeaponID||unit.WeaponLoadout[0].TurretIndex!=0||view.TurretDefinitions.Length!=1))
                throw new InvalidOperationException("U03 single repair turret mapping failed");
            if(c.Spec.Key==AirDefenceKey)
            {bool mortar=false;foreach(WeaponBinding binding in unit.WeaponLoadout)if(binding.WeaponID=="GRENADE"&&binding.TurretIndex==1)mortar=true;if(!mortar||view.TurretDefinitions.Length!=2)throw new InvalidOperationException("U07 distinct mortar turret mapping failed");}
            VerifySourceIsolation(c);
        }
        static void VerifySourceIsolation(Context c)
        {
            foreach(SourceRecord record in c.Sources)if(Fingerprint(record.Asset)!=record.Fingerprint)throw new InvalidOperationException("Native source mutated "+record.Asset.name);
            HashSet<object> native=new HashSet<object>(new IdentityComparer()),seen=new HashSet<object>(new IdentityComparer());
            foreach(SourceRecord record in c.Sources)foreach(FieldInfo f in Fields(record.Asset.GetType()))if(f.Name!="mDataCache")Gather(f.GetValue(record.Asset),native);
            foreach(ScriptableObject asset in c.Owned)foreach(FieldInfo f in Fields(asset.GetType()))if(f.Name!="mDataCache")Check(f.GetValue(asset),native,seen,asset.name+"."+f.Name);
        }
        static void ReleaseStale()
        {
            if(Contexts.Count==0&&SurveyProbeContext==null){RhodesSurveyArtifacts.Reset();return;}
            foreach(UnitView view in Resources.FindObjectsOfTypeAll(typeof(UnitView)))
            {if(view==null||!view.gameObject.activeInHierarchy)continue;BBI.Game.Simulation.UnitState state=view.NonInterpolatedTickState;if(state!=null&&(IsFactionKey(state.TypeID)||state.TypeID==SurveyProbeKey))throw new InvalidOperationException("Cannot replace active faction assets");}
            RhodesSurveyArtifacts.Reset();
            foreach(Context c in Contexts.Values)
            {
                for(int i=c.OwnedPresentation.Count-1;i>=0;i--)if(c.OwnedPresentation[i]!=null)UnityEngine.Object.DestroyImmediate(c.OwnedPresentation[i]);
                c.OwnedPresentation.Clear();
                for(int i=c.Owned.Count-1;i>=0;i--)if(c.Owned[i]!=null)UnityEngine.Object.DestroyImmediate(c.Owned[i]);
            }
            Contexts.Clear();
            ReleaseSurveyProbe();
        }
        static void VerifySurveyArtifacts(Context c)
        {
            VerifySurveyProbeProduction(c);
            int count=0;
            foreach(ScriptableObject ability in (Array)Read(c.Custom,"m_Abilities"))
            {
                string salvageKey=FactionAbilityPrivateKey(SurveyKey,RhodesSurveyArtifacts.SalvageAbility);
                if(ability==null||ability.name!=salvageKey)continue;
                AbilityAttributes data=Read(ability,"m_Attributes") as AbilityAttributes;
                if(data==null||data.AbilityType!=AbilityClass.Salvage||data.Salvage==null||data.Salvage.TargetType!=SalvageTargetType.Collectible||
                    (int)data.StartsRemovedInGameMode!=0||((Array)Read(Read(data,"m_ResearchDeps"),"m_DependentResearch")).Length!=0||
                    !object.ReferenceEquals(c.Definition.Get<AbilityAttributes>(salvageKey),data))
                    throw new InvalidOperationException("U02 native collectible salvage command is unavailable");
                count++;
            }
            if(count!=1||c.Definition.Get<HarvesterAttributes>()==null)throw new InvalidOperationException("U02 native collectible harvesting interface is missing");
            UnitHangarAttributes hangar=c.Definition.Get<UnitHangarAttributes>();int relicBays=0;
            if(hangar!=null&&hangar.HangarBays!=null)foreach(HangarBay bay in hangar.HangarBays)
                if(bay!=null&&bay.Name=="RelicType"&&bay.EntityType=="RelicType"&&bay.MaxCount==1)relicBays++;
            if(relicBays!=1)throw new InvalidOperationException("U02 must preserve the native single-relic hangar bay");
        }
        static void CloneClosure(Context c)
        {for(int i=0;i<c.Owned.Count;i++){if(c.Owned.Count>768)throw new InvalidOperationException("Faction source closure exceeded bound");HashSet<object> seen=new HashSet<object>(new IdentityComparer());foreach(FieldInfo field in Fields(c.Owned[i].GetType()))if(field.Name!="mDataCache")Collect(c,field.GetValue(c.Owned[i]),seen);}}
        static void Collect(Context c,object value,HashSet<object> seen)
        {
            if(value==null)return;ScriptableObject asset=value as ScriptableObject;if(asset!=null){if(c.Map.ContainsKey(asset)||IsOwned(c,asset))return;string ns=asset.GetType().Namespace;if(asset is UnitAssetContainer)throw new InvalidOperationException("Unexpected referenced unit container");if(ns!=null&&ns.StartsWith("BBI.",StringComparison.Ordinal))CloneAsset(c,asset,asset.name);return;}
            Type type=value.GetType();if(type.IsPrimitive||type.IsEnum||type==typeof(string)||typeof(UnityEngine.Object).IsAssignableFrom(type)||!seen.Add(value))return;Array array=value as Array;if(array!=null){foreach(object item in array)Collect(c,item,seen);return;}if(type.Namespace!=null&&type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))foreach(FieldInfo field in Fields(type))Collect(c,field.GetValue(value),seen);
        }
        static bool IsOwned(Context c,ScriptableObject value){foreach(ScriptableObject asset in c.Owned)if(object.ReferenceEquals(asset,value))return true;return false;}
        static ScriptableObject CloneAsset(Context c,ScriptableObject source,string name)
        {if(source==null)throw new InvalidOperationException("Missing source "+name);object prior;if(c.Map.TryGetValue(source,out prior))return (ScriptableObject)prior;if(IsOwned(c,source))return source;c.Sources.Add(new SourceRecord{Asset=source,Fingerprint=Fingerprint(source)});ScriptableObject clone=RhodesAssetClone59.CreateEmpty(source.GetType());c.Map.Add(source,clone);Dictionary<object,object> copied=new Dictionary<object,object>(new IdentityComparer());foreach(FieldInfo f in Fields(source.GetType()))if(f.Name!="mDataCache")f.SetValue(clone,CopyData(f.GetValue(source),copied));clone.name=name;clone.hideFlags=HideFlags.HideAndDontSave;c.Owned.Add(clone);return clone;}
        static object CopyData(object value,Dictionary<object,object> seen)
        {if(value==null)return null;Type type=value.GetType();if(type.IsPrimitive||type.IsEnum||type==typeof(string)||typeof(UnityEngine.Object).IsAssignableFrom(type))return value;object prior;if(seen.TryGetValue(value,out prior))return prior;Array a=value as Array;if(a!=null){if(a.Rank!=1)throw new InvalidOperationException("Unexpected multidimensional data");Array copy=(Array)a.Clone();seen.Add(value,copy);for(int i=0;i<a.Length;i++)copy.SetValue(CopyData(a.GetValue(i),seen),i);return copy;}if(type.Namespace==null||!type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))return value;object result=CloneMethod.Invoke(value,null);seen.Add(value,result);foreach(FieldInfo f in Fields(type))f.SetValue(result,CopyData(f.GetValue(value),seen));return result;}
        static void RemapAsset(Context c,ScriptableObject asset){HashSet<object> seen=new HashSet<object>(new IdentityComparer());foreach(FieldInfo f in Fields(asset.GetType()))if(f.Name!="mDataCache")f.SetValue(asset,Remap(c,f.GetValue(asset),seen));}
        static object Remap(Context c,object value,HashSet<object> seen)
        {if(value==null)return null;object mapped;if(c.Map.TryGetValue(value,out mapped))return mapped;Type t=value.GetType();if(t.IsPrimitive||t.IsEnum||t==typeof(string)||typeof(UnityEngine.Object).IsAssignableFrom(t)||!seen.Add(value))return value;Array a=value as Array;if(a!=null){for(int i=0;i<a.Length;i++)a.SetValue(Remap(c,a.GetValue(i),seen),i);return a;}if(t.Namespace!=null&&t.Namespace.StartsWith("BBI.",StringComparison.Ordinal))foreach(FieldInfo f in Fields(t))f.SetValue(value,Remap(c,f.GetValue(value),seen));return value;}
        static bool Mutable(object value){if(value==null)return false;Type t=value.GetType();return !t.IsValueType&&t!=typeof(string)&&!typeof(UnityEngine.Object).IsAssignableFrom(t)&&(value is Array||(t.Namespace!=null&&t.Namespace.StartsWith("BBI.",StringComparison.Ordinal)));}
        static void Gather(object value,HashSet<object> seen){if(!Mutable(value)||!seen.Add(value))return;Array a=value as Array;if(a!=null){foreach(object item in a)Gather(item,seen);return;}foreach(FieldInfo f in Fields(value.GetType()))Gather(f.GetValue(value),seen);}
        static void Check(object value,HashSet<object> native,HashSet<object> seen,string path){if(!Mutable(value)||!seen.Add(value))return;if(native.Contains(value))throw new InvalidOperationException("Mutable faction datum aliases native "+path);Array a=value as Array;if(a!=null){for(int i=0;i<a.Length;i++)Check(a.GetValue(i),native,seen,path+"["+i+"]");return;}foreach(FieldInfo f in Fields(value.GetType()))Check(f.GetValue(value),native,seen,path+"."+f.Name);}
        static string Fingerprint(ScriptableObject asset){StringBuilder b=new StringBuilder();HashSet<object> seen=new HashSet<object>(new IdentityComparer());foreach(FieldInfo f in Fields(asset.GetType())){b.Append(f.Name).Append('=');Append(b,f.GetValue(asset),seen);}using(SHA256 hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(b.ToString()))).Replace("-","");}
        static void Append(StringBuilder b,object value,HashSet<object> seen){if(value==null){b.Append("null;");return;}Type t=value.GetType();b.Append(t.FullName).Append(':');UnityEngine.Object u=value as UnityEngine.Object;if(!object.ReferenceEquals(u,null)){b.Append(u.GetInstanceID()).Append('/').Append(u.name).Append(';');return;}if(t.IsPrimitive||t.IsEnum||t==typeof(string)){b.Append(Convert.ToString(value,CultureInfo.InvariantCulture)).Append(';');return;}if(t.IsValueType){foreach(FieldInfo f in Fields(t))Append(b,f.GetValue(value),seen);return;}if(!seen.Add(value)){b.Append("seen;");return;}Array a=value as Array;if(a!=null){b.Append(a.Length).Append('[');foreach(object item in a)Append(b,item,seen);b.Append(']');return;}if(t.Namespace!=null&&t.Namespace.StartsWith("BBI.",StringComparison.Ordinal))foreach(FieldInfo f in Fields(t))Append(b,f.GetValue(value),seen);else b.Append(RuntimeHelpers.GetHashCode(value));}
        static List<FieldInfo> Fields(Type type){List<FieldInfo> result=new List<FieldInfo>();for(Type t=type;t!=null&&t!=typeof(object)&&t!=typeof(UnityEngine.Object)&&t!=typeof(ScriptableObject);t=t.BaseType)foreach(FieldInfo f in t.GetFields(Flags))if(!f.IsStatic)result.Add(f);result.Sort(delegate(FieldInfo a,FieldInfo b){return string.CompareOrdinal(a.DeclaringType.FullName+"."+a.Name,b.DeclaringType.FullName+"."+b.Name);});return result;}
        static object Read(object owner,string name){if(owner==null)throw new ArgumentNullException(name);for(Type t=owner.GetType();t!=null;t=t.BaseType){FieldInfo f=t.GetField(name,Flags);if(f!=null)return f.GetValue(owner);PropertyInfo p=t.GetProperty(name,Flags);if(p!=null)return p.GetValue(owner,null);}throw new MissingMemberException(owner.GetType().FullName,name);}
        static void Write(object owner,string name,object value){for(Type t=owner.GetType();t!=null;t=t.BaseType){FieldInfo f=t.GetField(name,Flags);if(f!=null){if(value!=null&&!f.FieldType.IsInstanceOfType(value)&&f.FieldType.IsPrimitive)value=Convert.ChangeType(value,f.FieldType,CultureInfo.InvariantCulture);f.SetValue(owner,value);return;}}throw new MissingFieldException(owner.GetType().FullName,name);}
        static void ClearArray(object owner,string name){object old=Read(owner,name);Array a=old as Array;if(a==null)throw new InvalidOperationException("Expected array "+name);Write(owner,name,Array.CreateInstance(a.GetType().GetElementType(),0));}
        static bool CallIfPresent(object owner,string name){for(Type t=owner.GetType();t!=null;t=t.BaseType){MethodInfo m=t.GetMethod(name,Flags,null,Type.EmptyTypes,null);if(m!=null){m.Invoke(owner,null);return true;}}return false;}
        static void CallRequired(object owner,string name){if(!CallIfPresent(owner,name))throw new MissingMethodException(owner.GetType().FullName,name);}
    }
}
