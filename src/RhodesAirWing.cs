using System;
using System.Collections;
using System.Collections.Generic;
using BBI.Core;
using BBI.Core.Data;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Unity.Game.Data;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static partial class RhodesCarrierUnits
    {
        public const string HeavyBomberKey="EF_Rhodes_HeavyBomber_MP";
        public const string HeavyBomberDonorKey="C_Bomber_MP";
        public const string HeavyBomberProductionKey="Ability_EF_Rhodes_ProduceHeavyBomber";
        public const string HeavyBomberLaunchKey="Ability_EF_Rhodes_LaunchHeavyBomber";
        public const string HeavyBomberBayName="EF_Rhodes_HeavyBomber";
        public const string HeavyBomberLaunchBone="EF_B01_Launch";
        public const string HeavyBomberDockBone="EF_B01_Dock";
        public const int HeavyBomberCapacity=4;
        static Context HeavyBomberContext;

        // Root calls after PrepareAviation, before final private remap/cache.
        // The physical B01/U08 lane broker must be installed by the hull adapter;
        // these remain separate native logical bays with independent capacities.
        static void PrepareAirWing(Context carrier)
        {
            RegisterPrivateHeavyBomber();
            AddAviationCommand(carrier,"Ability_C_ProduceBomber_MP",HeavyBomberProductionKey,HeavyBomberKey,
                RhodesResearch.Aviation1,"生产：重型轰炸机",KeyCode.N);
            AddAviationCommand(carrier,"Ability_C_LaunchBomber_MP",HeavyBomberLaunchKey,HeavyBomberKey,
                RhodesResearch.Aviation1,"重型轰炸机出击",KeyCode.X);
            object sourceData=Read(Read(carrier.Source,"m_UnitHangar"),"m_Attributes"),sourceBay=null;
            foreach(object item in (Array)Read(sourceData,"m_StorableEntities"))
                if((string)Read(item,"m_EntityType")==HeavyBomberDonorKey)sourceBay=item;
            if(sourceBay==null)throw new InvalidOperationException("Native Coalition bomber bay is absent");
            object bay=CopyData(sourceBay,new Dictionary<object,object>(new IdentityComparer()));
            Write(bay,"m_Name",HeavyBomberBayName);Write(bay,"m_EntityType",HeavyBomberKey);
            Write(bay,"m_MaxCount",HeavyBomberCapacity);Write(bay,"m_SlotCount",1);
            Write(bay,"m_UndockPresCtrlBone",HeavyBomberLaunchBone);Write(bay,"m_DockPresCtrlBone",HeavyBomberDockBone);
            // Native bomber timing/speed/cooling remain the baseline here.
            // ConfigureV17AirBay applies the faster bring-in only to V17.
            object hangar=Read(Read(carrier.Custom,"m_UnitHangar"),"m_Attributes");
            Array old=(Array)Read(hangar,"m_StorableEntities"),all=Array.CreateInstance(old.GetType().GetElementType(),old.Length+1);
            Array.Copy(old,all,old.Length);all.SetValue(bay,old.Length);Write(hangar,"m_StorableEntities",all);
            // Only the bomber class receives its own native docking events.
            // U08 keeps its distinct Large class and existing rotor controller.
            Array existing=(Array)Read(hangar,"m_UnitDockingTriggers"),native=(Array)Read(sourceData,"m_UnitDockingTriggers");
            List<object> triggers=new List<object>();foreach(object item in existing)triggers.Add(item);
            int bomberClass=(int)Registry.GetEntityType(HeavyBomberKey).Get<UnitAttributes>().Class;
            foreach(object item in native)if(Convert.ToInt32(Read(item,"m_UnitClass"))==bomberClass)
                triggers.Add(CopyData(item,new Dictionary<object,object>(new IdentityComparer())));
            Array combined=Array.CreateInstance(existing.GetType().GetElementType(),triggers.Count);
            for(int i=0;i<triggers.Count;i++)combined.SetValue(triggers[i],i);Write(hangar,"m_UnitDockingTriggers",combined);
            AppendAviationCommandsAndHUD(carrier);
            Runtime.Log("RHODES_AIR_WING_PREPARED fighter="+AircraftKey+"; bomber="+HeavyBomberKey+
                "; bomberDonor="+HeavyBomberDonorKey+"; bomberCapacity=4; bomberSlots=1; bomberResearch="+RhodesResearch.Aviation1+
                "; C05UnlockGranted=False; nativeBomberDamageAmmoEconomics=True; physicalSharedLaneBrokerRequired=True; runtimeAcceptancePending=True");
        }

        static void RegisterPrivateHeavyBomber()
        {
            if(HeavyBomberContext!=null||Registry.GetEntityType(HeavyBomberKey)!=null)
                throw new InvalidOperationException("Private heavy bomber already registered");
            EntityTypeAttributes nativeDefinition=Registry.GetEntityType(HeavyBomberDonorKey);
            UnitAttributes baseline=nativeDefinition==null?null:nativeDefinition.Get<UnitAttributes>();
            if(baseline==null||baseline.Resource1Cost!=550||baseline.Resource2Cost!=250||baseline.PopCapCost!=2||
                Math.Abs(Fixed64.UnsafeFloatValue(baseline.ProductionTime)-26f)>.001f)
                throw new InvalidOperationException("Native build12339551 bomber economy contract differs");
            UnitAssetContainer donor=null;
            foreach(UnitAssetContainer candidate in Resources.FindObjectsOfTypeAll(typeof(UnitAssetContainer)))
                if(candidate!=null&&candidate.name==HeavyBomberDonorKey&&
                    object.ReferenceEquals(Read(Read(candidate,"m_Attributes"),"m_Attributes"),baseline))
                {if(donor!=null)throw new InvalidOperationException("Multiple loaded bomber donors");donor=candidate;}
            if(donor==null)throw new InvalidOperationException("Current loaded C_Bomber_MP container is absent");
            Context plane=new Context();HeavyBomberContext=plane;plane.Source=donor;
            plane.Custom=(UnitAssetContainer)CloneAsset(plane,donor,HeavyBomberKey);
            CloneAviationAssetClosure(plane);
            foreach(ScriptableObject asset in plane.Owned)RemapAsset(plane,asset);
            object hud=Read(Read(plane.Custom,"m_HUD"),"m_Attributes");
            Write(hud,"mUnitLocalizedNameStringID","舰载重型轰炸机");
            Write(hud,"mUnitLocalizedShortDescriptionStringID","舰载区域轰炸单位，擅长打击地面集群。");
            Write(hud,"mUnitLocalizedLongDescriptionStringID","航空改装后可生产的四发舰载轰炸机。对指定区域实施轰炸，返航后补充弹药并整备；机库最多容纳4架。");
            FactionUnits.NormalizePrivateVeterancy(plane.Custom,HeavyBomberKey);
            foreach(ScriptableObject asset in plane.Owned)if(!object.ReferenceEquals(asset,plane.Custom))CallIfPresent(asset,"OnEnable");
            UnitViewAttributes own=(UnitViewAttributes)Read(Read(plane.Custom,"m_View"),"Data");
            UnitViewAttributes source=(UnitViewAttributes)Read(Read(plane.Source,"m_View"),"Data");
            RhodesAirWingModels.ConfigureView(HeavyBomberKey,own,source);
            Write(plane.Custom,"mDataCache",null);CallRequired(plane.Custom,"OnEnable");
            EntityTypeAttributes definition=new EntityTypeAttributes(HeavyBomberKey);
            foreach(object datum in (IEnumerable)plane.Custom)if(datum!=null&&!definition.Add(datum))
                throw new InvalidOperationException("Duplicate private heavy bomber datum");
            UnitAttributes core=definition.Get<UnitAttributes>();
            if(core==null||core.Resource1Cost!=baseline.Resource1Cost||core.Resource2Cost!=baseline.Resource2Cost||
                core.ProductionTime!=baseline.ProductionTime||core.PopCapCost!=baseline.PopCapCost||core.Class!=baseline.Class)
                throw new InvalidOperationException("Private heavy bomber changed native economy/class");
            Registry.Add(definition);VerifyPrivateHeavyBomber();
            Runtime.Log("RHODES_HEAVY_BOMBER_REGISTERED key="+HeavyBomberKey+"; donor="+HeavyBomberDonorKey+
                "; CU=550; RU=250; seconds=26; population=2; nativeCombatMovementAmmoAndSortie=True; sourceUnchanged=True; runtimeAcceptancePending=True");
        }
        static void VerifyPrivateHeavyBomber()
        {
            Context plane=HeavyBomberContext;if(plane==null)throw new InvalidOperationException("Private bomber context absent");
            foreach(SourceRecord source in plane.Sources)if(Fingerprint(source.Asset)!=source.Fingerprint)
                throw new InvalidOperationException("Native bomber source mutated: "+source.Asset.name);
            HashSet<object> native=new HashSet<object>(new IdentityComparer()),seen=new HashSet<object>(new IdentityComparer());
            foreach(SourceRecord source in plane.Sources)foreach(System.Reflection.FieldInfo field in Fields(source.Asset.GetType()))
                if(field.Name!="mDataCache")Gather(field.GetValue(source.Asset),native);
            foreach(ScriptableObject asset in plane.Owned)foreach(System.Reflection.FieldInfo field in Fields(asset.GetType()))
                if(field.Name!="mDataCache")Check(field.GetValue(asset),native,seen,"bomber."+asset.name+"."+field.Name);
        }
        static void VerifyHeavyBomberBay(object bay)
        {
            if((string)Read(bay,"m_Name")!=HeavyBomberBayName||(string)Read(bay,"m_EntityType")!=HeavyBomberKey||
                (int)Read(bay,"m_MaxCount")!=HeavyBomberCapacity||(int)Read(bay,"m_SlotCount")!=1)
                throw new InvalidOperationException("Private B01 native bay capacity/slot contract changed");
            float bringIn=HasV17Hull()?RhodesBomberRecoveryProfile.BomberBringInSeconds:RhodesBomberRecoveryProfile.StandardBringInSeconds;
            if(Math.Abs((float)Read(bay,"m_DockBringInAnimationSeconds")-bringIn)>.0001f)
                throw new InvalidOperationException("Private B01 bring-in duration differs from its guide/lane contract");
        }
        static void ReleaseAirWingAssets()
        {
            V17AviationConfigured=false;
            if(HeavyBomberContext!=null)
            {
                for(int i=HeavyBomberContext.Owned.Count-1;i>=0;i--)
                    if(HeavyBomberContext.Owned[i]!=null)UnityEngine.Object.DestroyImmediate(HeavyBomberContext.Owned[i]);
                HeavyBomberContext=null;
            }
            RhodesAirWingModels.Reset();
        }
    }
}
