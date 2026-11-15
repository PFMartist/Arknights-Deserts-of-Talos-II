using System;
using System.Collections;
using System.Collections.Generic;
using BBI.Game.Data;
using BBI.Core.ComponentModel;
using BBI.Game.Simulation;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Menu-first host. No laboratory terrain, targets, resource grants,
    // automatic selection/orders, quality overrides or demo hotkeys.
    public static class RhodesSkirmishRuntime
    {
        static volatile bool InMatch,Retiring;
        static readonly object SimGate=new object();
        static bool LoggedMenu;
        static bool PresentationFaulted;
        static bool RetirementFaulted;
        static Entity[] Carriers;
        static object AuthoritySim;
        public static bool ReadyForMatch {get{return !InMatch&&!Retiring;}}
        public static void MainTick(object main)
        {
            LevelManager levels=NativeRead.Get(main,"mLevelManager") as LevelManager;
            if(levels==null)return;
            bool running=levels.ManagerState==LevelManager.State.Game && RhodesSkirmishFaction.IsMatchActive;
            if(InMatch&&!running)BeginRetirement();
            // GameExiting still runs the native simulation during the fade.
            // Stop scheduling first, then reset only once native shutdown has
            // completed and the manager has returned to Startup.
            if(Retiring){if(!RetirementFaulted&&levels.ManagerState==LevelManager.State.Startup)FinishRetirementWhenSafe();return;}
            if(!running)
            {
                if(!LoggedMenu&&levels.ManagerState==LevelManager.State.Startup&&levels.CommanderAttributes!=null)
                {
                    LoggedMenu=true;
                    Runtime.Log("RHODES_SKIRMISH_MENU_READY menuFirst=True; automaticMatch=False; qualityOverride=False; channel=skirmish_preview; humanCommanders=prepared_native_slots; nativeAIPreserved=True");
                }
                return;
            }
            bool entering=!InMatch;
            if(entering){InMatch=true;LoggedMenu=false;PresentationFaulted=false;RhodesSkirmishFrameTiming.Reset();}
            if(PresentationFaulted)return;
            try{
                long sectionStart=RhodesSkirmishFrameTiming.Begin();
                Runtime.SetSkirmishPresentation(main);
                if(entering){
                    OptionalPresentationServices.EndMatch();
                    RhodesPowerUI.BeginSession();
                    Runtime.Log("RHODES_SKIRMISH_GAME_READY nativeMap=True; labTerrain=False; labTargets=False; resourceBonus=False; debugHotkeys=False; rhodesOwners="+RhodesResearch.DeclaredOwners().Length);
                }
                if(UnitViewDiscovery.Failure!=null)throw new InvalidOperationException("Native view lifecycle registry: "+UnitViewDiscovery.Failure);
                RhodesSkirmishFrameTiming.End(0,sectionStart);sectionStart=RhodesSkirmishFrameTiming.Begin();
                OptionalPresentationServices.TickHud(main);
                RhodesSkirmishFrameTiming.End(1,sectionStart);sectionStart=RhodesSkirmishFrameTiming.Begin();
                HumanoidModel.DiscoverActiveUnits();OptionalPresentationServices.TickHumanoid();
                RhodesSkirmishFrameTiming.End(2,sectionStart);sectionStart=RhodesSkirmishFrameTiming.Begin();
                FactionUnitModels.MainTick();
                RhodesSkirmishFrameTiming.End(3,sectionStart);sectionStart=RhodesSkirmishFrameTiming.Begin();
                RhodesTiltrotorFlightControl.MainTick();
                LaevatainFlameBridge.MainTick();GilbertaGravityBridge.MainTick();ArdeliaHealingBridge.MainTick();
                RhodesSkirmishFrameTiming.End(4,sectionStart);sectionStart=RhodesSkirmishFrameTiming.Begin();
                RhodesCarrierUnits.ObserveViews();
                RhodesSkirmishFrameTiming.End(5,sectionStart);RhodesSkirmishFrameTiming.Frame();
            }catch(Exception error){
                ReportPresentationFailure(error);
            }
        }
        internal static void ReportPresentationFailure(Exception error)
        {
            if(PresentationFaulted)return;
            PresentationFaulted=true;
            Runtime.Log("RHODES_SKIRMISH_PRESENTATION_DISABLED error="+error+"; nativeSimulationAndMatchRetirementContinue=True; retry=next_match");
        }
        public static void SimTick(object sim)
        {
            lock(SimGate)
            {
            // Rendering/loading progress is local to each peer. Only native
            // simulation calls and prepared commander identities gate authority.
            if(!RhodesSkirmishFaction.IsMatchActive||sim==null)return;
            if(!object.ReferenceEquals(AuthoritySim,sim)){AuthoritySim=sim;Carriers=null;}
            if(Carriers==null)
            {
                // Discover the declared faction owners after the native start
                // fleet appears. No Commander1 assumption or recurring scan on death.
                CommanderID[] owners=RhodesResearch.DeclaredOwners();
                if(owners.Length!=0)
                {
                    HashSet<CommanderID> declared=new HashSet<CommanderID>(owners);
                    List<Entity> found=new List<Entity>();
                    foreach(object unit in (IEnumerable)NativeRead.Get(NativeRead.Get(sim,"UnitManager"),"Units"))
                    {
                        if((string)NativeRead.Get(unit,"TypeName")!=RhodesCarrierUnits.UnitKey)continue;
                        Entity entity=(Entity)NativeRead.Get(unit,"Entity");
                        if(declared.Contains((CommanderID)NativeRead.Call(typeof(Sim),"GetEntityCommanderID",entity))&&
                            !(bool)NativeRead.Call(typeof(Sim),"IsEntityDead",entity))found.Add(entity);
                    }
                    if(found.Count!=0)Carriers=found.ToArray();
                }
            }
            RhodesResearch.SimTick(sim);
            RhodesPowerGate.SimTick(sim,Carriers);
            RhodesDeploymentState.SimTick(sim,Carriers);
            RhodesTiltrotorFlightControl.SimTick(sim);
            RhodesOperatorTransport.SimTick(sim);
            RhodesTiltrotorConcealment.SimTick(sim);
            LaevatainFlameBridge.SimTick(sim);GilbertaGravityBridge.SimTick(sim);ArdeliaHealingBridge.SimTick(sim);
            HumanoidWeaponEventBridge.SimTick(sim);
            }
        }
        static void BeginRetirement()
        {
            InMatch=false;Retiring=true;RetirementFaulted=false;
            RhodesSkirmishFrameTiming.Reset();
            Runtime.ClearSkirmishPresentation();
            Runtime.Log("RHODES_SKIRMISH_SESSION_STOP_REQUESTED waitingForNativeShutdown=True");
        }
        static void ResetSessionServices()
        {
            Carriers=null;AuthoritySim=null;
            MatchRuntimeLifetime.RetireAll();
            Runtime.ClearSkirmishPresentation();
            Runtime.Log("RHODES_SKIRMISH_SESSION_STOPPED nativeShutdownComplete=True; eventsDetached=True");
        }
        static void FinishRetirementWhenSafe()
        {
            if(RetirementFaulted)return;
            foreach(UnityEngine.Object item in UnitViewDiscovery.Get())
            {
                UnitView view=item as UnitView;if(view==null||!view.gameObject.activeInHierarchy)continue;
                UnitState state=view.NonInterpolatedTickState;
                if(state!=null&&(state.TypeID==RhodesCarrierUnits.UnitKey||FactionUnits.IsFactionKey(state.TypeID)||Array.IndexOf(HumanoidUnits.Keys,state.TypeID)>=0))return;
            }
            lock(SimGate)
            {
                if(RetirementFaulted)return;
                try{
                    ResetSessionServices();
                    // RetireAll attempts both lifetime scopes before surfacing a failure.
                    RhodesSkirmishFaction.ResetMatch();
                }catch(Exception error){
                    // A poisoned type initializer cannot recover next frame.
                    // Keep the next mod match blocked after partial cleanup.
                    RetirementFaulted=true;PresentationFaulted=true;
                    Runtime.Log("RHODES_SKIRMISH_RETIREMENT_FAILED error="+error+"; nextMatchAllowed=False; restartRequired=True; recurringRetry=False");
                    return;
                }
            }
            // Keep templates that native entity definitions still reference.
            // Register() releases/rebuilds them on a real registry generation swap.
            Retiring=false;LoggedMenu=false;PresentationFaulted=false;
            Runtime.Log("RHODES_SKIRMISH_SESSION_RESET nextMatchAllowed=True; registeredAssetsPreserved=True");
        }
    }
}
