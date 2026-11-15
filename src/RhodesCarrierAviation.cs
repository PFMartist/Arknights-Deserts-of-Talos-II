using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BBI.Core;
using BBI.Core.Data;
using BBI.Game.Data;
using BBI.Unity.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Candidate only: compiled offline. No game, native OnEnable or fixture has
    // been executed by this research. See AVIATION_INTEGRATION.txt for hooks.
    public static partial class RhodesCarrierUnits
    {
        public const string AircraftKey="EF_Rhodes_Interceptor_MP";
        public const string AircraftDonorKey="C_Interceptor_MP";
        public const string AircraftProductionKey="Ability_EF_Rhodes_ProduceInterceptor";
        public const string TiltLaunchKey="Ability_EF_Rhodes_LaunchTiltrotor";
        public const string AircraftLaunchKey="Ability_EF_Rhodes_LaunchInterceptor";
        public const string AircraftRecallKey="Ability_EF_Rhodes_DockAll";
        // Native C carrier stores fifteen interceptors, served by three
        // simultaneous docking/launch lanes. Capacity is not a guide index.
        const int AircraftHangarCapacity=15,AircraftRunwaySlots=3;
        static Context AviationContext;

        sealed class AviationRunway
        {
            // Native simulation XY coordinates, not raw GLB XYZ. Root must
            // resolve the V05 model-to-UnitView transform before providing them.
            public float Right=0f,Back=0f,Separation=0f,Degrees=0f;
            public float ReceivingRight=0f,ReceivingBack=0f;
            public string UndockBone="V03_HP_AV_0$Slot$_Undock";
            public string DockBone="V03_HP_AV_0$Slot$_Dock";
        }

        // Call after PrepareProduction, before outer leaf-remap/OnEnable/cache.
        // Native production, sortie, hangar occupancy and cooldown code owns
        // all simulation state. No script spawn, teleport or ammo refill exists.
        static void PrepareAviation(Context carrier,string researchKey,AviationRunway runway)
        {
            if(runway==null)throw new ArgumentNullException("runway");
            if(AviationContext!=null)throw new InvalidOperationException("Private aircraft already prepared");
            RegisterPrivateAircraft();
            AddAviationCommand(carrier,"Ability_C_ProduceInterceptor_MP",AircraftProductionKey,AircraftKey,researchKey,"生产：舰载截击机",KeyCode.K);
            AddAviationCommand(carrier,"Ability_C_LaunchGunShip_MP",TiltLaunchKey,FactionUnits.TiltStrikeKey,RhodesResearch.TiltAssembly,"旋翼机单架出击",KeyCode.Z);
            AddAviationCommand(carrier,"Ability_C_LaunchInterceptor_MP",AircraftLaunchKey,AircraftKey,string.Empty,"截击机出击",KeyCode.W);
            AddAviationCommand(carrier,"Ability_C_DockAll",AircraftRecallKey,null,string.Empty,"召回作战飞机",KeyCode.U);
            object sourceData=Read(Read(carrier.Source,"m_UnitHangar"),"m_Attributes");
            object bay=null;
            foreach(object candidate in (Array)Read(sourceData,"m_StorableEntities"))
                if((string)Read(candidate,"m_EntityType")==AircraftDonorKey)bay=CopyData(candidate,new Dictionary<object,object>(new IdentityComparer()));
            if(bay==null)throw new InvalidOperationException("Native tracked C interceptor bay is absent");
            Write(bay,"m_Name","EF_Rhodes_Interceptor");Write(bay,"m_EntityType",AircraftKey);
            Write(bay,"m_MaxCount",AircraftHangarCapacity);Write(bay,"m_SlotCount",AircraftRunwaySlots);
            // Synchronize the private native lift, per-slot queue and whole-bay
            // clocks with the real57.388m hull well. Native350m/s release speed,
            // plane ownership, ammo, production, costs and aircraft clips stay
            // unchanged. Full-span wings can open safely inside this shaft.
            Write(bay,"m_UndockTotalSecondsPerSlot",RhodesAviationLaunchProfile.NativeSequenceSeconds);
            Write(bay,"m_UndockLiftTime",RhodesAviationLaunchProfile.LiftSeconds);
            Write(bay,"m_UndockTotalSeconds",RhodesAviationLaunchProfile.WholeBayCycleSeconds);
            Write(bay,"m_DockBringInAnimationSeconds",RhodesAviationLaunchProfile.BringInSeconds);
            Write(bay,"m_UndockPresCtrlBone",runway.UndockBone);Write(bay,"m_DockPresCtrlBone",runway.DockBone);
            Write(bay,"m_UndockXOffsetPos",runway.Right);Write(bay,"m_UndockYOffsetPos",runway.Back);
            Write(bay,"m_UndockSlotXSeperationOffset",runway.Separation);Write(bay,"m_UndockDegreesOffsetAngle",runway.Degrees);
            Write(bay,"m_DockReceivingXOffset",runway.ReceivingRight);Write(bay,"m_DockReceivingYOffset",runway.ReceivingBack);
            object hangar=Read(Read(carrier.Custom,"m_UnitHangar"),"m_Attributes");
            Array ground=(Array)Read(hangar,"m_StorableEntities");Array all=Array.CreateInstance(ground.GetType().GetElementType(),ground.Length+1);
            for(int i=0;i<ground.Length;i++)all.SetValue(ground.GetValue(i),i);all.SetValue(bay,ground.Length);Write(hangar,"m_StorableEntities",all);
            // These are AIRCRAFT animation triggers from C; carrier-side launch
            // rig animation must be rebound to V05, so no original hull trigger
            // is reattached here. Root adds its explicit V05 hull trigger table.
            Array sourceUnitTriggers=(Array)Read(sourceData,"m_UnitDockingTriggers");
            List<object> unitTriggers=new List<object>();
            foreach(object trigger in sourceUnitTriggers)
                if(Convert.ToInt32(Read(trigger,"m_UnitClass"))==1179649)unitTriggers.Add(CopyData(trigger,new Dictionary<object,object>(new IdentityComparer())));
            Array triggers=Array.CreateInstance(sourceUnitTriggers.GetType().GetElementType(),unitTriggers.Count);
            for(int i=0;i<unitTriggers.Count;i++)triggers.SetValue(unitTriggers[i],i);Write(hangar,"m_UnitDockingTriggers",triggers);
            Write(hangar,"m_AlignmentTime",5f);Write(hangar,"m_ApproachTime",6f);
            PrepareTiltrotorBay(carrier,sourceData,hangar);
            AppendAviationCommandsAndHUD(carrier);
        }

        static void RegisterPrivateAircraft()
        {
            if(Registry.GetEntityType(AircraftKey)!=null)throw new InvalidOperationException("Private aircraft key collision");
            UnitAssetContainer donor=null;
            foreach(UnitAssetContainer candidate in Resources.FindObjectsOfTypeAll(typeof(UnitAssetContainer)))
                if(candidate!=null&&candidate.name==AircraftDonorKey&&object.ReferenceEquals(Read(Read(candidate,"m_Attributes"),"m_Attributes"),Registry.GetEntityType(AircraftDonorKey).Get<UnitAttributes>()))donor=candidate;
            if(donor==null)throw new InvalidOperationException("Loaded native C interceptor container is absent");
            Context plane=new Context();AviationContext=plane;plane.Source=donor;plane.Custom=(UnitAssetContainer)CloneAsset(plane,donor,AircraftKey);
            CloneAviationAssetClosure(plane);
            foreach(ScriptableObject asset in plane.Owned)RemapAsset(plane,asset);
            object aircraftHud=Read(Read(plane.Custom,"m_HUD"),"m_Attributes");
            Write(aircraftHud,"mUnitLocalizedNameStringID","舰载截击机");
            Write(aircraftHud,"mUnitLocalizedShortDescriptionStringID","快速出击的舰载战斗机");
            Write(aircraftHud,"mUnitLocalizedLongDescriptionStringID","从本舰出击，对指定区域实施空袭。完成任务或耗尽弹药后返航，补充弹药并整备。");
            FactionUnits.NormalizePrivateVeterancy(plane.Custom,AircraftKey);
            foreach(ScriptableObject asset in plane.Owned)if(!object.ReferenceEquals(asset,plane.Custom))CallIfPresent(asset,"OnEnable");
            ConfigurePrivateAircraftView(plane);
            Write(plane.Custom,"mDataCache",null);CallRequired(plane.Custom,"OnEnable");
            EntityTypeAttributes definition=new EntityTypeAttributes(AircraftKey);
            foreach(object datum in (IEnumerable)plane.Custom)if(datum!=null&&!definition.Add(datum))throw new InvalidOperationException("Duplicate private aircraft datum");
            UnitAttributes core=definition.Get<UnitAttributes>();
            if(core==null||core.Resource1Cost!=150||core.Resource2Cost!=100||core.PopCapCost!=2||core.Class!=Registry.GetEntityType(AircraftDonorKey).Get<UnitAttributes>().Class)
                throw new InvalidOperationException("Native aircraft costs/class were not preserved privately");
            Registry.Add(definition);VerifyPrivateAircraft();
        }

        static void ConfigurePrivateAircraftView(Context plane)
        {
            UnitViewAttributes own=(UnitViewAttributes)Read(Read(plane.Custom,"m_View"),"Data");
            UnitViewAttributes source=(UnitViewAttributes)Read(Read(plane.Source,"m_View"),"Data");
            if(object.ReferenceEquals(own,source)||object.ReferenceEquals(own.LODTunings,source.LODTunings))
                throw new InvalidOperationException("Private aircraft view/LOD data aliases native C interceptor");
            // Remove only camera-dependent visual exaggeration for AircraftKey.
            // Native geometry/root scale, flight capsule, weapons, LOD/icon data
            // and the source C interceptor remain untouched.
            own.NLIPSScale=new AnimationCurve(new Keyframe(0f,1f),new Keyframe(1f,1f));
            RhodesAirWingModels.ConfigureView(AircraftKey,own,source);
        }

        static void CloneAviationAssetClosure(Context plane)
        {
            // Native OnEnable rehydrates attributes from nested ScriptableObject
            // assets. Copy the BBI data closure before any of those callbacks.
            for(int i=0;i<plane.Owned.Count;i++)
            {
                if(plane.Owned.Count>512)throw new InvalidOperationException("Aircraft data closure exceeded bound");
                HashSet<object> seen=new HashSet<object>(new IdentityComparer());
                foreach(FieldInfo field in Fields(plane.Owned[i].GetType()))if(field.Name!="mDataCache")CollectAviationAssets(plane,field.GetValue(plane.Owned[i]),seen);
            }
        }
        static void CollectAviationAssets(Context plane,object value,HashSet<object> seen)
        {
            if(value==null)return;ScriptableObject asset=value as ScriptableObject;
            if(asset!=null)
            {
                if(plane.Map.ContainsKey(asset))return;
                string ns=asset.GetType().Namespace;
                if(asset is UnitAssetContainer)throw new InvalidOperationException("Aircraft data unexpectedly references another unit container");
                if(ns!=null&&ns.StartsWith("BBI.",StringComparison.Ordinal))CloneAsset(plane,asset,asset.name);
                return;
            }
            Type type=value.GetType();if(type.IsPrimitive||type.IsEnum||type==typeof(string)||typeof(UnityEngine.Object).IsAssignableFrom(type)||!seen.Add(value))return;
            Array array=value as Array;if(array!=null){foreach(object item in array)CollectAviationAssets(plane,item,seen);return;}
            if(type.Namespace!=null&&type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))foreach(FieldInfo field in Fields(type))CollectAviationAssets(plane,field.GetValue(value),seen);
        }
        static void AddAviationCommand(Context carrier,string sourceName,string key,string target,string researchKey,string title,KeyCode hotkey)
        {
            if(Registry.GetEntityType(key)!=null)throw new InvalidOperationException("Private aviation ability key collision: "+key);
            ScriptableObject source=null;foreach(ScriptableObject ability in (Array)Read(carrier.Source,"m_Abilities"))if(ability!=null&&ability.name==sourceName)source=ability;
            if(source==null)throw new InvalidOperationException("Native carrier aviation ability absent: "+sourceName);
            // Two distinct launch commands intentionally share one native
            // template. The one-to-one asset map would return the first clone
            // again and overwrite its cached registry/view identity.
            bool recorded=false;foreach(SourceRecord entry in carrier.Sources)if(object.ReferenceEquals(entry.Asset,source))recorded=true;
            if(!recorded)carrier.Sources.Add(new SourceRecord{Asset=source,Fingerprint=Fingerprint(source),Definition=Registry.GetEntityType(source.name)});
            ScriptableObject clone=RhodesAssetClone59.CreateEmpty(source.GetType());
            Dictionary<object,object> copied=new Dictionary<object,object>(new IdentityComparer());
            foreach(FieldInfo field in Fields(source.GetType()))if(field.Name!="mDataCache")field.SetValue(clone,CopyData(field.GetValue(source),copied));
            clone.name=key;clone.hideFlags=HideFlags.HideAndDontSave;carrier.Owned.Add(clone);
            RemapAsset(carrier,clone);
            object data=Read(clone,"m_Attributes"),view=Read(clone,"m_View");
            ((NamedObjectBase)data).InitializeName(key);((NamedObjectBase)view).InitializeName(key);
            if(target!=null)Write(Read(data,"m_ProduceUnit"),"m_UnitTypeToProduce",target);
            Write(Read(data,"m_ProduceUnit"),"m_BypassQueue",false);
            Write(Read(data,"m_ResearchDeps"),"m_DependentResearch",string.IsNullOrEmpty(researchKey)?new string[0]:new string[]{researchKey});
            SetProductionEnum(data,"m_StartsRemovedIn",0);
            Write(view,"LocalizedTitleStringID",title);Write(view,"Hotkey",hotkey);Write(view,"BindingIsExclusive",false);
            if(key==TiltLaunchKey)
            {
                // Native gunship launches one aircraft. U08 is deployed as a
                // manually controlled unit rather than a targeted air strike.
                Write(Read(data,"m_AirSortie"),"m_SortieSquadSize",1);
                Write(Read(data,"m_AirSortie"),"m_SetAttackTargetsWithinRadius",0f);
                Write(Read(data,"m_AirSortie"),"m_LoiterLength",3600f);
                // Native LaunchDocked validation accepts Ground or SingleTarget
                // only. None rejects launch before OnPerform. Keep a deployment
                // point confirmation while excluding target units/strike areas.
                Write(data,"m_TargetingType",AbilityTargetingType.Ground);
                object reticle=Read(view,"TargetingReticle");
                Write(reticle,"m_ReticleEnabled",false);Write(reticle,"m_MouseCursorEnabled",false);
                foreach(string state in new string[]{"Default","Hover","OutOfRange","InsideMinRange","InvalidMapPosition","Confirmed","Cancelled","Deployed"})
                {Write(reticle,"m_"+state+"Prefab",null);Write(reticle,"m_"+state+"IconName",string.Empty);}
                Write(reticle,"m_IconWidth",0);Write(reticle,"m_IconHeight",0);
                Write(view,"TargetingReticle",reticle);
                Write(view,"LocalizedShortDescriptionStringID","单架出击，前往指定位置待命");
                Write(view,"LocalizedLongDescriptionStringID","指定部署位置，单架旋翼机出击并在抵达后悬停。可独立下达移动、攻击与返航命令；无命令时不主动攻击，弹药耗尽后留场待命。干员可在空中部署与召回。");
            }
            if(key==AircraftRecallKey)
            {
                Write(view,"LocalizedShortDescriptionStringID","召回在外的截击机与重型轰炸机");
                Write(view,"LocalizedLongDescriptionStringID","命令截击机与重型轰炸机返回本舰。旋翼机保留当前任务，需单独下达返航命令。");
            }
            foreach(ScriptableObject previous in carrier.Commands)
                if(object.ReferenceEquals(previous,clone)||previous.name==key)throw new InvalidOperationException("Aviation commands share clone or identity: "+key);
            carrier.Commands.Add(clone);CallIfPresent(clone,"OnEnable");
            if(clone.name!=key||((AbilityAttributes)Read(clone,"m_Attributes")).Name!=key)
                throw new InvalidOperationException("Aviation command failed independent identity initialization: "+key);
            Registry.Add(ProductionAbilityDefinition(clone));
        }
        static void AppendAviationCommandsAndHUD(Context carrier)
        {
            Array previous=(Array)Read(carrier.Custom,"m_Abilities");Array all=Array.CreateInstance(previous.GetType().GetElementType(),carrier.Commands.Count+carrier.Products.Count);int i=0;
            foreach(ScriptableObject command in carrier.Commands)all.SetValue(command,i++);
            foreach(ProductionItem product in carrier.Products)all.SetValue(product.Ability,i++);Write(carrier.Custom,"m_Abilities",all);
            RebuildProductionPages(carrier);
        }
        // Inject a call at the end of UnitHangarPanelController's constructor.
        // Native UI classifies by a settings string array, not aircraft class.
        // Clone controller settings so only this private type is added locally.
        public static void PrepareAviationHangarUI(object controller)
        {
            if(!Runtime.RhodesCarrier||controller==null)return;
            ConfigureSeparateAirRows(controller);
            RhodesHangarLayout.Bind(controller);
        }
        static void VerifyPrivateAircraft()
        {
            Context plane=AviationContext;if(plane==null)throw new InvalidOperationException("Private aircraft context absent");
            UnitViewAttributes ownView=(UnitViewAttributes)Read(Read(plane.Custom,"m_View"),"Data");
            UnitViewAttributes sourceView=(UnitViewAttributes)Read(Read(plane.Source,"m_View"),"Data");
            if(ownView.NLIPSScale==null||object.ReferenceEquals(ownView.NLIPSScale,sourceView.NLIPSScale)||object.ReferenceEquals(ownView.LODTunings,sourceView.LODTunings))
                throw new InvalidOperationException("Private aircraft nominal visual scale is not independently owned");
            Keyframe[] curve=ownView.NLIPSScale.keys;
            if(curve.Length!=2||curve[0].time!=0f||curve[1].time!=1f||curve[0].value!=1f||curve[1].value!=1f||curve[0].inTangent!=0f||curve[0].outTangent!=0f||curve[1].inTangent!=0f||curve[1].outTangent!=0f)
                throw new InvalidOperationException("Private aircraft NLIPS curve is not nominal flat1");
            foreach(SourceRecord source in plane.Sources)if(Fingerprint(source.Asset)!=source.Fingerprint)throw new InvalidOperationException("Native aircraft source mutated: "+source.Asset.name);
            HashSet<object> native=new HashSet<object>(new IdentityComparer()),seen=new HashSet<object>(new IdentityComparer());
            foreach(SourceRecord source in plane.Sources)foreach(FieldInfo field in Fields(source.Asset.GetType()))if(field.Name!="mDataCache")Gather(field.GetValue(source.Asset),native);
            foreach(ScriptableObject asset in plane.Owned)foreach(FieldInfo field in Fields(asset.GetType()))if(field.Name!="mDataCache")Check(field.GetValue(asset),native,seen,"aircraft."+asset.name+"."+field.Name);
        }
        static void VerifyAviation(Context carrier)
        {
            VerifyPrivateAircraft();
            Array bays=(Array)Read(Read(Read(carrier.Custom,"m_UnitHangar"),"m_Attributes"),"m_StorableEntities");
            int tracked=0,tiltTracked=0,bomberTracked=0;object nativeBay=null;
            foreach(object bay in bays)if((string)Read(bay,"m_EntityType")!=string.Empty)
            {string key=(string)Read(bay,"m_EntityType");if(key==HeavyBomberKey){VerifyHeavyBomberBay(bay);bomberTracked++;continue;}if(key==FactionUnits.TiltStrikeKey){VerifyTiltrotorBay(bay);tiltTracked++;continue;}if(key!=AircraftKey)throw new InvalidOperationException("Unexpected tracked Rhodes hangar type");tracked++;nativeBay=bay;}
            if(tracked!=1||tiltTracked!=1||bomberTracked!=1||(int)Read(nativeBay,"m_MaxCount")!=AircraftHangarCapacity||(int)Read(nativeBay,"m_SlotCount")!=AircraftRunwaySlots||(string)Read(nativeBay,"m_Name")!="EF_Rhodes_Interceptor")
                throw new InvalidOperationException("Private native aircraft bay count/capacity/name failed");
            if(Math.Abs((float)Read(nativeBay,"m_UndockTotalSecondsPerSlot")-RhodesAviationLaunchProfile.NativeSequenceSeconds)>.0001f||
                Math.Abs((float)Read(nativeBay,"m_UndockLiftTime")-RhodesAviationLaunchProfile.LiftSeconds)>.0001f||
                Math.Abs((float)Read(nativeBay,"m_UndockSpeed")-RhodesAviationLaunchProfile.ReleaseSpeed)>.0001f||
                Math.Abs((float)Read(nativeBay,"m_UndockTotalSeconds")-RhodesAviationLaunchProfile.WholeBayCycleSeconds)>.0001f||
                Math.Abs((float)Read(nativeBay,"m_DockBringInAnimationSeconds")-RhodesAviationLaunchProfile.BringInSeconds)>.0001f)
                throw new InvalidOperationException("Private aircraft/native guide launch timing or speed mismatch");
            foreach(string key in new string[]{AircraftProductionKey,AircraftLaunchKey,TiltLaunchKey,AircraftRecallKey})
            {
                EntityTypeAttributes registered=Registry.GetEntityType(key);AbilityAttributes ability=registered==null?null:registered.Get<AbilityAttributes>();
                if(ability==null||!object.ReferenceEquals(registered.Get<AbilityViewAttributes>(),Read(FindAviationCommand(carrier,key),"m_View")))throw new InvalidOperationException("Private aviation registry missing "+key);
                string[] deps=ability.ResearchDependencies.RequiredResearch;
                if(key==AircraftProductionKey&&(deps.Length!=1||deps[0]!=RhodesResearch.Aviation1||ability.ProduceUnit.UnitTypeToProduce!=AircraftKey||ability.ProduceUnit.BypassQueue))
                    throw new InvalidOperationException("Private aircraft production dependency/target/queue mismatch");
                if(key==TiltLaunchKey)
                {
                    if(ability.TargetingType!=AbilityTargetingType.Ground||ability.AirSortie.SortieSquadSize!=1||
                        ability.AirSortie.SetAttackTargetsWithinRadius!=BBI.Core.Utility.FixedPoint.Fixed64.Zero||ability.ProduceUnit.UnitTypeToProduce!=FactionUnits.TiltStrikeKey)
                        throw new InvalidOperationException("U08 must launch one manually controlled aircraft without an airstrike area");
                    object reticle=Read(Read(FindAviationCommand(carrier,key),"m_View"),"TargetingReticle");
                    if((bool)Read(reticle,"m_ReticleEnabled"))throw new InvalidOperationException("U08 inherited a fixed-wing airstrike reticle");
                }
            }
        }
        static ScriptableObject FindAviationCommand(Context carrier,string key)
        {foreach(ScriptableObject command in carrier.Commands)if(command.name==key)return command;foreach(ProductionItem product in carrier.Products)if(product.AbilityKey==key)return product.Ability;throw new InvalidOperationException("Private aircraft command absent "+key);}
        // Add to ReleaseStale AFTER checking no active private aircraft UnitView.
        static void ReleaseAviationAssets()
        {
            ReleaseAirWingAssets();
            if(AviationContext==null)return;
            for(int i=AviationContext.Owned.Count-1;i>=0;i--)if(AviationContext.Owned[i]!=null)UnityEngine.Object.DestroyImmediate(AviationContext.Owned[i]);
            AviationContext=null;
        }
    }
}


