using System;
using System.Collections;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Core.Utility;
using BBI.Core.Utility.FixedPoint;
using BBI.Game.Data;
using BBI.Game.Simulation;

namespace EndfieldDoK.MapUnitLab
{
    // Manual functional range, never an automatic demonstration. One-time
    // native spawns only; no initial injury, move/attack/reveal orders, healing,
    // respawning, scripted hitpoints or scripted weapon activations.
    public static class RhodesCarrierScenario
    {
        static readonly CommanderID Player=new CommanderID(1),Enemy=new CommanderID(2);
        static bool Complete,HavePosition;
        static Entity[] Fleet;
        static uint LastReport,LastHealthSample;
        static Vector2r LastPosition;static float Travel;
        static readonly Dictionary<Entity,int> LastHealth=new Dictionary<Entity,int>();
        public static void Reset()
        {Complete=HavePosition=false;Fleet=null;LastReport=LastHealthSample=0;Travel=0f;LastHealth.Clear();RhodesProductionTelemetry.Reset();RhodesLabResources.Reset();}
        public static StartingUnit[] CreateFriendlyFleet()
        {StartingUnit carrier=new StartingUnit();carrier.Unit=RhodesCarrierUnits.UnitKey;carrier.Tag="EF_RHODES_MANUAL_CARRIER";return new StartingUnit[]{carrier};}
        public static Entity[] EnsureRoster(object sim)
        {
            if(Complete)return Fleet;
            object carrier=Runtime.FindOwnedUnit(sim,RhodesCarrierUnits.UnitKey);if(carrier==null)return null;
            if(!RhodesWeaponRangeTargets.Ready)throw new InvalidOperationException("Register private Rhodes range targets before starting the simulation");
            RhodesWeaponRangeTargets.VerifyRegistered();
            UnitState state=State(carrier);Vector2r origin=state.StartOfFrameDynamicState.Position;
            Orientation2 orientation=state.StartOfFrameDynamicState.Orientation;
            Vector2r forward=orientation.Forward,right=orientation.Right,left=new Vector2r(-right.X,-right.Y);
            List<Entity> friendly=new List<Entity>();friendly.Add((Entity)NativeRead.Get(carrier,"Entity"));
            Spawn(sim,Enemy,RhodesWeaponRangeTargets.ArmourKey,Point(origin,forward,0,left,2000),right,"EF_RHODES_SUPPORT_CRUISER_TARGET");
            // Beyond initial CIWS 1600m and carrier sensors. Manual approach
            // makes them visible through normal native sensor/FOW rules.
            Spawn(sim,Enemy,RhodesWeaponRangeTargets.AirKey,Point(origin,forward,350,right,1850),left,"EF_RHODES_AIR_TARGET_NEAR");
            Spawn(sim,Enemy,RhodesWeaponRangeTargets.AirKey,Point(origin,forward,650,right,2450),left,"EF_RHODES_AIR_TARGET_FAR");
            // Actual unmodified faction harvester health/armour gives the
            // operator a meaningful raid target, alongside the old gun range.
            Spawn(sim,Enemy,"RI_U01_HARVESTER",Point(origin,forward,600,left,1100),right,"EF_OPERATOR_HARVESTER_TARGET_1");
            Spawn(sim,Enemy,"RI_U01_HARVESTER",Point(origin,forward,720,left,1100),right,"EF_OPERATOR_HARVESTER_TARGET_2");
            Spawn(sim,Enemy,"RI_U01_HARVESTER",Point(origin,forward,840,left,1100),right,"EF_OPERATOR_HARVESTER_TARGET_3");
            // Actual model-derived U01 radius is15.27m: retain these heavy
            // vehicles as an immunity comparison. Three small U04 targets
            // (actual radius4.56m) fit a25m triangle inside the30m skill field.
            // A separate native range-only weapon lease makes unloading safe;
            // it does not lock movement or change any shared unit definition.
            Entity gravityA=Spawn(sim,Enemy,"RI_U04_LIGHT_ESCORT",Point(origin,forward,1020,left,1085),right,"EF_GILBERTA_MOBILE_TARGET_1");
            Entity gravityB=Spawn(sim,Enemy,"RI_U04_LIGHT_ESCORT",Point(origin,forward,1060,left,1085),right,"EF_GILBERTA_MOBILE_TARGET_2");
            Entity gravityC=Spawn(sim,Enemy,"RI_U04_LIGHT_ESCORT",Point(origin,forward,1040,left,1125),right,"EF_GILBERTA_MOBILE_TARGET_3");
            if(!GilbertaGravityBridge.PrepareRangeTarget(sim,gravityA)||!GilbertaGravityBridge.PrepareRangeTarget(sim,gravityB)||!GilbertaGravityBridge.PrepareRangeTarget(sim,gravityC))
                throw new InvalidOperationException("Gilberta mobile range targets require the scoped native weapon lease");
            RhodesLabResources.Ensure(sim,origin,orientation);
            Fleet=friendly.ToArray();
            int friendlyCount=0,hostileCount=0,other=0,carriers=0,support=0;
            foreach(object unit in Units(sim)){Entity id=(Entity)NativeRead.Get(unit,"Entity");CommanderID owner=(CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",id);
                if(owner==Player){friendlyCount++;string key=(string)NativeRead.Get(unit,"TypeName");if(key==RhodesCarrierUnits.UnitKey)carriers++;else if(key==RhodesManualDamage.SupportKey)support++;}
                else if(owner==Enemy)hostileCount++;else other++;}
            if(friendlyCount!=1||carriers!=1||support!=0||hostileCount!=9||other!=0)throw new InvalidOperationException("Rhodes range initial roster is not 1 base Rhodes / 1 support-cruiser + 2 air + 3 harvester + 3 mobile gravity targets");
            Complete=true;
            Runtime.Log("RHODES_SCENARIO_READY customCarriers=1; friendlyNativeSupportCruiser=0; initialFriendlyBVM=0; friendlyOwner=1; nativeMiningPatches=2; nativeCU_nodes=8; nativeRU_nodes=4; researchInitiallyCompleted=0; otherBranchRequiresFreshMatch=True; enemySupportCruiserTargets=1; supportCruiserDonor=C_SupportCruiser_MP; enemyAirTargets=2; enemyOperatorHarvesterTargets=3; enemyMobileGravityTargets=3; gravityFieldTestCenterForward=1040; gravityFieldTestCenterLeft=1100; gravityTargetsMoveAndHealthUnchanged=True; rangeOnlyNativeWeaponLease=True; harvesterStatsUnmodified=True; nativeCarrierEnemies=0; groundTargetHP=120000; airTargetHP=24000; mainRangePortMetres=2000; manualDamageHotkey=F8; manualDamageSelectedFriendlyTypes=RhodesOrNativeSupportCruiser; manualDamagePercentOfCurrentMaxHP=25; manualDamageMinimumHP=1; nativeInitialDamage=0; scriptedMoveOrders=0; scriptedAttackOrders=0; revealCommands=0; autoRespawn=False; nativeProductionRetained=True; gameVisualAcceptance=False");
            return Fleet;
        }
        public static void Tick(object sim)
        {
            if(!Complete)return;if(Runtime.DetailedDiagnosticsEnabled)RhodesProductionTelemetry.Tick(sim);uint frame=Frame(sim);
            if(!Runtime.DetailedDiagnosticsEnabled)return;
            if(LastHealthSample==0||frame-LastHealthSample>=40){LastHealthSample=frame;
                foreach(object unit in Units(sim)){
                    Entity id=(Entity)NativeRead.Get(unit,"Entity");UnitState state=State(unit);int old;
                    if(!LastHealth.TryGetValue(id,out old)||old!=state.Hitpoints){Runtime.Log("RHODES_RANGE_HEALTH entity="+id+"; key="+NativeRead.Get(unit,"TypeName")+"; previous="+(LastHealth.ContainsKey(id)?old:-1)+"; current="+state.Hitpoints+"; dead="+state.UnitIsDead+"; automaticHPWrites=0; nativeDamageAndRepairOnly=True");LastHealth[id]=state.Hitpoints;}
                }
            }
            object carrier=Runtime.FindOwnedUnit(sim,RhodesCarrierUnits.UnitKey);if(carrier==null)return;
            UnitState carrierState=State(carrier);Vector2r position=carrierState.StartOfFrameDynamicState.Position;
            if(HavePosition)Travel+=Fixed64.UnsafeFloatValue((position-LastPosition).Length);LastPosition=position;HavePosition=true;
            if(LastReport!=0&&frame-LastReport<160)return;LastReport=frame;
            Runtime.Log("RHODES_NATIVE_STATE frame="+frame+"; position="+position+"; speedMetresPerSecond="+Fixed64.UnsafeFloatValue(carrierState.StartOfFrameDynamicState.Velocity.Length)+"; travelMetres="+Travel+"; hp="+carrierState.Hitpoints+"; commandsIssuedByObserver=0; nativeSimulationOwnsMovement=True");
        }
        static Vector2r Point(Vector2r origin,Vector2r a,int alongA,Vector2r b,int alongB)
        {Vector2r p=origin+a*Fixed64.FromInt(alongA)+b*Fixed64.FromInt(alongB);if(Math.Abs(Fixed64.UnsafeFloatValue(p.X))>3700f||Math.Abs(Fixed64.UnsafeFloatValue(p.Y))>3700f)throw new InvalidOperationException("Rhodes manual range placement outside 8000m flat-map safety margin: "+p);return p;}
        static Entity Spawn(object sim,CommanderID owner,string key,Vector2r point,Vector2r direction,string tag)
        {
            HashSet<Entity> prior=new HashSet<Entity>();foreach(object unit in Units(sim))prior.Add((Entity)NativeRead.Get(unit,"Entity"));
            Type factory=typeof(Sim).Assembly.GetType("BBI.Game.Commands.SimCommandFactory",true);
            object command=NativeRead.Call(factory,"CreateSpawnUnitsCommand",owner,new string[]{key},new string[]{tag},point,Orientation2.FromDirection(direction));
            if(!Run(sim,command))throw new InvalidOperationException("Rhodes manual range spawn rejected "+tag);
            Entity result=Entity.None;int count=0;
            foreach(object unit in Units(sim)){
                Entity id=(Entity)NativeRead.Get(unit,"Entity");if(prior.Contains(id))continue;
                if((string)NativeRead.Get(unit,"TypeName")!=key||(CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",id)!=owner)throw new InvalidOperationException("Rhodes range spawn identity mismatch "+tag);
                if((State(unit).StartOfFrameDynamicState.Position-point).Length>Fixed64.FromInt(15))throw new InvalidOperationException("Rhodes range spawn displaced excessively "+tag);
                result=id;count++;}
            if(count!=1)throw new InvalidOperationException("Rhodes range spawn did not produce exactly one native entity "+tag);return result;
        }
        static bool Run(object sim,object command){return (bool)NativeRead.Call(command,"RunCommand",sim,NativeRead.Get(sim,"InteractionProvider"),NativeRead.Get(sim,"GlobalFrameCount"));}
        static IEnumerable Units(object sim){return (IEnumerable)NativeRead.Get(NativeRead.Get(sim,"UnitManager"),"Units");}
        static UnitState State(object unit){return (UnitState)NativeRead.Call(unit,"GetState");}
        static uint Frame(object sim){return Convert.ToUInt32(NativeRead.Get(NativeRead.Get(sim,"GlobalFrameCount"),"FrameNumber"));}
    }
}
