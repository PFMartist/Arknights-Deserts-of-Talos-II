using System;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Game.Commands;
using BBI.Game.Data;
using BBI.Game.Simulation;
using BBI.Unity.Game;
using BBI.Unity.Game.HUD;
using BBI.Unity.Game.UI;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Native power widgets retain all allocation, charging and click behavior.
    // This event-driven extension displays cumulative costs in all four columns.
    // Native controls own allocation, charging, refunds and click scheduling.
    public sealed class RhodesPowerUI:MonoBehaviour
    {
        static readonly Color Yellow=new Color(238f/255f,229f/255f,46f/255f,1f);
        static readonly Color White=new Color(236f/255f,237f/255f,231f/255f,1f);
        static readonly Color Dim=new Color(.48f,.49f,.47f,1f);
        static readonly string[] Names={"武备","损管","机动","探测"};
        static readonly List<RhodesPowerUI> KnownDrivers=new List<RhodesPowerUI>();
        UnitInterfaceController Owner;
        ICommandScheduler Scheduler;
        ICommanderInteractionProvider Interaction;
        GameObject Root;
        UILabel Feedback;
        readonly UILabel[] Levels=new UILabel[RhodesPowerProfile.SystemCount];
        readonly SimpleTooltip[] CostTips=new SimpleTooltip[RhodesPowerProfile.SystemCount];
        readonly PowerShuntState.PowerSystemState[] SystemCache=new PowerShuntState.PowerSystemState[RhodesPowerProfile.SystemCount];
        readonly int[] LastCurrent={-1,-1,-1,-1},LastTarget={-1,-1,-1,-1};
        bool Bound,Ready;
        Entity Selected=Entity.None;
        SimStateFrame Latest;
        bool WaitingFirstFrame;
        string WaitReason;
        int WaitReports;

        public static void Bind(object panel)
        {
            RhodesPowerVoice.ObservePanel(panel);
            UnitInterfaceController owner=panel as UnitInterfaceController;if(owner==null)return;
            RhodesPowerUI driver=owner.GetComponent<RhodesPowerUI>();
            if(driver==null)driver=owner.gameObject.AddComponent<RhodesPowerUI>();
            driver.Owner=owner;if(!KnownDrivers.Contains(driver))KnownDrivers.Add(driver);
            try
            {
                if(driver.Ready&&driver.Root!=null){driver.RefreshSession(owner);return;}
                driver.DetachOwnEvents();driver.DestroyWidgets();driver.Initialize(owner);
            }
            catch(Exception error){driver.DetachOwnEvents();driver.DestroyWidgets();Runtime.Log("WARNING RHODES_POWER_UI_INIT "+error.GetType().Name+": "+error.Message+"; nativePowerUIUntouched=True; visualAcceptancePending=True");}
        }

        // InitializePanel runs before native OnSessionStarted resolves the
        // scheduler/provider. Retry only known owners, plus at most one native
        // frame event per new match; never add an Update or scene search.
        public static void BeginSession()
        {
            RhodesPowerVoice.BeginSession();
            for(int i=KnownDrivers.Count-1;i>=0;i--)
            {
                RhodesPowerUI driver=KnownDrivers[i];if(driver==null){KnownDrivers.RemoveAt(i);continue;}
                driver.ResetSession();driver.WaitReason=null;driver.WaitReports=0;
                Bind(driver.Owner);
                if(!driver.Bound&&driver.Owner!=null)
                {driver.Owner.NewFrameFromSim-=driver.OnFirstSessionFrame;driver.Owner.NewFrameFromSim+=driver.OnFirstSessionFrame;driver.WaitingFirstFrame=true;}
            }
        }
        public static void EndSession()
        {RhodesPowerVoice.EndSession();for(int i=KnownDrivers.Count-1;i>=0;i--){RhodesPowerUI driver=KnownDrivers[i];if(driver==null)KnownDrivers.RemoveAt(i);else driver.ResetSession();}}
        void OnFirstSessionFrame(SimStateFrame frame){CancelFirstFrame();Bind(Owner);}
        void CancelFirstFrame()
        {if(WaitingFirstFrame&&Owner!=null)Owner.NewFrameFromSim-=OnFirstSessionFrame;WaitingFirstFrame=false;}
        void DetachOwnEvents()
        {
            if(Bound&&Owner!=null){Owner.NewSelection-=OnSelection;Owner.NewLeadUnitSelected-=OnLead;Owner.NewFrameFromSim-=OnFrame;}
            Bound=false;
        }
        void ResetSession()
        {
            CancelFirstFrame();DetachOwnEvents();Selected=Entity.None;Latest=null;
            Scheduler=null;Interaction=null;
            for(int i=0;i<RhodesPowerProfile.SystemCount;i++){LastCurrent[i]=-1;LastTarget[i]=-1;}
            if(Root!=null)Root.SetActive(false);
        }
        bool Wait(string reason)
        {
            if(WaitReason!=reason&&WaitReports++<4)Runtime.Log("RHODES_POWER_UI_WAIT reason="+reason+"; finiteSessionRetry=True; nativePowerUIUntouched=True");
            WaitReason=reason;return false;
        }
        bool ReadSessionServices(UnitInterfaceController owner,out ICommandScheduler scheduler,out ICommanderInteractionProvider interaction)
        {
            scheduler=NativeRead.Get(owner,"mCommandScheduler") as ICommandScheduler;
            interaction=NativeRead.Get(owner,"mInteractionProvider") as ICommanderInteractionProvider;
            return scheduler!=null&&interaction!=null||Wait(scheduler==null?"scheduler_not_resolved":"interaction_not_resolved");
        }
        void AttachOwnEvents(UnitInterfaceController owner,ICommandScheduler scheduler,ICommanderInteractionProvider interaction)
        {
            bool changed=!Bound||!ReferenceEquals(Scheduler,scheduler)||!ReferenceEquals(Interaction,interaction);
            DetachOwnEvents();Owner=owner;Scheduler=scheduler;Interaction=interaction;
            // Native OnSessionEnded clears these delegate fields. Ready widgets
            // therefore do not imply that subscriptions survived a new match.
            owner.NewSelection-=OnSelection;owner.NewLeadUnitSelected-=OnLead;owner.NewFrameFromSim-=OnFrame;
            owner.NewSelection+=OnSelection;owner.NewLeadUnitSelected+=OnLead;owner.NewFrameFromSim+=OnFrame;Bound=true;CancelFirstFrame();
            if(changed){for(int i=0;i<RhodesPowerProfile.SystemCount;i++){LastCurrent[i]=-1;LastTarget[i]=-1;}}
            Selected=owner.LeadUnit;Latest=ShipbreakersMain.CurrentSimFrame;Render(Latest);
        }
        void RefreshSession(UnitInterfaceController owner)
        {
            ICommandScheduler scheduler;ICommanderInteractionProvider interaction;
            if(ReadSessionServices(owner,out scheduler,out interaction))AttachOwnEvents(owner,scheduler,interaction);
        }
        void Initialize(UnitInterfaceController owner)
        {
            object settings=NativeRead.Get(owner,"mPowerShuntPanelSettings");
            GameObject nativePanel=NativeRead.Get(settings,"m_PowerShuntPanel") as GameObject;
            UILabel font=NativeRead.Get(settings,"m_ReservePowerPoolLabel") as UILabel;
            GameObject fourth=NativeRead.Get(settings,"m_PowerSystemControllerBottomRightAnchor") as GameObject;
            if(nativePanel==null){Wait("power_panel_missing");return;}
            if(font==null||(font.bitmapFont==null&&font.trueTypeFont==null)){Wait("native_font_not_ready");return;}
            if(fourth==null){Wait("fourth_anchor_missing");return;}
            ICommandScheduler scheduler;ICommanderInteractionProvider interaction;
            if(!ReadSessionServices(owner,out scheduler,out interaction))return;
            Array prefabs=NativeRead.Get(settings,"m_PowerLevelPrefabs") as Array;
            if(prefabs==null||prefabs.Length<RhodesPowerProfile.MaximumLevel){Wait("native_five_power_levels_missing");return;}
            Owner=owner;
            Root=new GameObject("EF_RhodesPowerCosts");Root.hideFlags=HideFlags.HideAndDontSave;Root.layer=font.gameObject.layer;
            Root.transform.parent=nativePanel.transform;Root.transform.localPosition=Vector3.zero;Root.transform.localRotation=Quaternion.identity;Root.transform.localScale=Vector3.one;
            string[] anchors={"m_PowerSystemControllerTopLeftAnchor","m_PowerSystemControllerTopRightAnchor","m_PowerSystemControllerBottomLeftAnchor","m_PowerSystemControllerBottomRightAnchor"};
            for(int i=0;i<RhodesPowerProfile.SystemCount;i++)
            {
                GameObject anchor=NativeRead.Get(settings,anchors[i]) as GameObject;if(anchor==null)throw new InvalidOperationException("Native power anchor missing");
                Vector3 point=nativePanel.transform.InverseTransformPoint(anchor.transform.position);
                Levels[i]=Label("EF_PowerState_"+i,font,point.x,230f,38,27,10,White);Levels[i].text=Names[i]+"\n0→0";
                CostTips[i]=Levels[i].gameObject.AddComponent<SimpleTooltip>();
                NativeRead.Set(CostTips[i],"m_Anchor",anchor.transform);
                NativeRead.Set(CostTips[i],"m_TitleStringId",Names[i]+"供能");
                NativeRead.Set(CostTips[i],"m_LongDescriptionStringId",string.Empty);
                BoxCollider hit=Levels[i].gameObject.AddComponent<BoxCollider>();hit.isTrigger=true;
                hit.center=new Vector3(0,-13.5f,0);hit.size=new Vector3(38,27,1);
            }
            Feedback=Label("EF_PowerChargeHelp",font,-119f,15f,210,26,9,Dim);
            Ready=true;Root.SetActive(false);AttachOwnEvents(owner,scheduler,interaction);
            Runtime.Log("RHODES_POWER_UI_BOUND nativePanel=True; nativeFourthColumn=True; systems=4; maximumLevel=5; cumulativeCosts=True; nativeFontAndAtlas=True; nativePowerLevelPrefabs="+(prefabs==null?0:prefabs.Length)+"; immutableFrameEvents=True; perFrameSceneScan=False; updateLoop=False; nativeVisibilityToggleUnchanged=True; currentAndTargetDisplayed=True; visualAcceptancePending=True");
        }
        GameObject Node(string name,float x,float y)
        {
            GameObject node=new GameObject(name);node.hideFlags=HideFlags.HideAndDontSave;node.layer=Root.layer;node.transform.parent=Root.transform;
            node.transform.localRotation=Quaternion.identity;node.transform.localScale=Vector3.one;node.transform.localPosition=new Vector3(x,y,0);return node;
        }
        UILabel Label(string name,UILabel font,float x,float y,int width,int height,int size,Color color)
        {
            UILabel label=Node(name,x,y).AddComponent<UILabel>();if(font.bitmapFont!=null)label.bitmapFont=font.bitmapFont;else label.trueTypeFont=font.trueTypeFont;
            label.fontStyle=FontStyle.Normal;label.fontSize=size;label.pivot=UIWidget.Pivot.Top;label.alignment=NGUIText.Alignment.Center;
            label.width=width;label.height=height;label.maxLineCount=2;label.overflowMethod=UILabel.Overflow.ClampContent;label.supportEncoding=false;
            label.applyGradient=false;label.effectStyle=UILabel.Effect.None;label.color=color;label.depth=28;return label;
        }
        void OnSelection(IList<Entity> entities,SimStateFrame frame){SetSelected(Owner.LeadUnit);Latest=frame;Render(frame);}
        void OnLead(Entity entity){SetSelected(entity);Latest=ShipbreakersMain.CurrentSimFrame;Render(Latest);}
        void SetSelected(Entity entity)
        {
            if(entity!=Selected)Selected=entity;
        }
        void OnFrame(SimStateFrame frame)
        {
            RhodesPowerVoice.FirstFrame(Frame(frame));
            Latest=frame;Render(frame);
        }
        static uint Frame(SimStateFrame frame){return frame==null?0:frame.FrameNumber.FrameNumber;}
        bool TryState(SimStateFrame frame,Entity entity,out PowerShuntState state)
        {
            state=null;if(frame==null||entity==Entity.None)return false;
            UnitState unit=frame.FindObject<UnitState>(entity);
            if(unit==null||unit.TypeID!=RhodesCarrierUnits.UnitKey||!Interaction.LocalCommanderCanControl(unit.OwnerCommander))return false;
            state=frame.FindObject<PowerShuntState>(entity);return state!=null&&state.PowerSystems!=null&&state.ReservePowerPool!=null;
        }
        bool GetSystems(PowerShuntState state,out PowerShuntState.PowerSystemState[] systems)
        {
            systems=SystemCache;Array.Clear(systems,0,systems.Length);
            foreach(PowerShuntState.PowerSystemState system in state.PowerSystems)
            {
                if(system==null)continue;int index=(int)system.PowerSystemType;
                if(index>=0&&index<RhodesPowerProfile.SystemCount)systems[index]=system;
            }
            foreach(PowerShuntState.PowerSystemState system in systems)if(system==null)return false;
            return true;
        }
        void Render(SimStateFrame frame)
        {
            if(!Ready||Root==null)return;
            PowerShuntState state;PowerShuntState.PowerSystemState[] systems;
            bool visible=TryState(frame,Selected,out state);
            if(Root.activeSelf!=visible)Root.SetActive(visible);
            if(!visible)return;
            if(!GetSystems(state,out systems)){Feedback.text="供能状态同步中";return;}
            int actualCost=0,targetCost=0;
            for(int i=0;i<RhodesPowerProfile.SystemCount;i++)
            {
                int current=systems[i].CurrentPowerLevelInEffectIndex,target=systems[i].CurrentTargetPowerLevelIndex;
                if(current<0||current>RhodesPowerProfile.MaximumLevel||target<0||target>RhodesPowerProfile.MaximumLevel)
                {Feedback.text="供能档位同步中";return;}
                actualCost+=RhodesPowerProfile.CumulativePowerCost(current);
                targetCost+=RhodesPowerProfile.CumulativePowerCost(target);
                if(LastCurrent[i]==current&&LastTarget[i]==target)continue;
                LastCurrent[i]=current;LastTarget[i]=target;
                Levels[i].text=Names[i]+"\n"+current+"→"+target;
                Levels[i].color=current==target?White:Yellow;
                NativeRead.Set(CostTips[i],"m_ShortDescriptionStringId",SystemCostText(current,target));
            }
            int budget;
            bool budgetReady=RhodesPowerGate.TryGetBudget(Selected,out budget)&&budget>=RhodesPowerProfile.InitialPower&&budget<=RhodesPowerProfile.MaximumPower;
            string help=budgetReady?ThermalBudgetText(actualCost,targetCost,state.ReservePowerPool.CurrentAmount,budget,
                state.TotalHeat,state.HeatThreshold,state.IsCooling):"供能总量同步中";
            if(Feedback.text!=help)Feedback.text=help;
        }
        internal static string SystemCostText(int current,int target)
        {
            RhodesPowerProfile.CheckLevel(current);RhodesPowerProfile.CheckLevel(target);
            int actual=RhodesPowerProfile.CumulativePowerCost(current),desired=RhodesPowerProfile.CumulativePowerCost(target);
            string next=target<RhodesPowerProfile.MaximumLevel?"下一档再占用"+(RhodesPowerProfile.CumulativePowerCost(target+1)-desired)+"点":"已达最高档";
            return "当前"+current+"档，占用及热负荷"+actual+"点；目标"+target+"档，占用"+desired+"点。"+next+"。每升一档充能3秒；全舰散热极限19。";
        }
        internal static string BudgetText(int actualCost,int targetCost,int reserve,int total)
        {
            return "占用 "+actualCost+" → "+targetCost+" / "+total+"  储备 "+reserve+"\n每档充能3秒 · 累计占用0/1/3/6/10/15";
        }
        internal static string ThermalBudgetText(int actualCost,int targetCost,int reserve,int total,int heat,int threshold,bool cooling)
        {
            if(threshold<=0)return "热控状态同步中";
            int excess=heat-threshold;
            string state=excess>0?"过载+"+excess:cooling?"散热中":threshold-heat<=RhodesThermalProfile.NearOverheatWarningMargin?"临近极限":"稳定";
            return "占用 "+actualCost+"→"+targetCost+" /"+total+"  储备 "+reserve+"\n热负荷 "+heat+"/"+threshold+" · "+state;
        }
        void OnDestroy()
        {
            ResetSession();KnownDrivers.Remove(this);DestroyWidgets();
        }
        void DestroyWidgets(){if(Root!=null)UnityEngine.Object.Destroy(Root);Root=null;Ready=false;}
    }
}
