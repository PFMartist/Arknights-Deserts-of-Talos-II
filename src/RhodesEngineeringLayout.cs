using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BBI.Game.Data;
using BBI.Unity.Game.UI;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Adapt the live private Engineering tree, not the serialized settings or
    // the research data. Native RebuildTreeView leaves dependency-locked and
    // unused grid slots active. ParentGrid.hideInactive can compact those slots
    // once their visibility follows the authoritative native button list.
    internal static class RhodesEngineeringLayout
    {
        static readonly List<RhodesEngineeringLayoutDriver> Drivers=new List<RhodesEngineeringLayoutDriver>();
        static int HudPasses;
        static float RetryAt;
        static bool Warned;

        internal static void PanelObserved(object panel)
        {
            ResearchPanelsController owner=panel as ResearchPanelsController;
            if(owner==null)return;
            try
            {
                Prune();
                RhodesEngineeringLayoutDriver driver=owner.GetComponent<RhodesEngineeringLayoutDriver>();
                if(driver==null)driver=owner.gameObject.AddComponent<RhodesEngineeringLayoutDriver>();
                if(!Drivers.Contains(driver))Drivers.Add(driver);
                driver.Observe(owner);
            }
            catch(Exception error){Warn(error);}
        }

        // Only the existing UISystem's explicit panel list, in two finite HUD
        // passes. This also covers a loaded session with zero research buttons.
        internal static void BindGameHud(object main)
        {
            if(HudPasses>=2 || (HudPasses==1 && Time.realtimeSinceStartup<RetryAt))return;
            HudPasses++;RetryAt=Time.realtimeSinceStartup+1f;
            try
            {
                object ui=null;
                if(main!=null)NativeRead.TryGet(main,"mUISystem",out ui);
                if(ui==null)NativeRead.TryGet(typeof(UISystem),"mUISystemInstance",out ui);
                object value;
                if(ui==null || !NativeRead.TryGet(ui,"mBBIPanels",out value))return;
                IEnumerable panels=value as IEnumerable;
                if(panels!=null)foreach(object panel in panels)PanelObserved(panel);
            }
            catch(Exception error){Warn(error);}
        }

        internal static void ResearchItemObserved(object item)
        {
            ResearchButtonController button=item as ResearchButtonController;
            if(button==null)return;
            try
            {
                Prune();
                foreach(RhodesEngineeringLayoutDriver driver in Drivers)
                    if(driver.Contains(button)){driver.Queue();break;}
            }
            catch(Exception error){Warn(error);}
        }

        static void Prune()
        {
            for(int i=Drivers.Count-1;i>=0;i--)if(Drivers[i]==null)Drivers.RemoveAt(i);
        }

        internal static void EndSession()
        {
            foreach(RhodesEngineeringLayoutDriver driver in Drivers)if(driver!=null)driver.Release();
            Drivers.Clear();HudPasses=0;RetryAt=0f;Warned=false;
        }

        internal static void Warn(Exception error)
        {
            if(Warned)return;Warned=true;
            Runtime.Log("WARNING RHODES_ENGINEERING_LAYOUT "+error.GetType().Name+": "+error.Message+"; nativeFallback=True; recurringSceneScan=False");
        }
    }

    // Disabled while idle. Native grid rebuild/open/close notifications arm at
    // most two LateUpdates, after native UIGrid.Update and button anchoring.
    public sealed class RhodesEngineeringLayoutDriver : MonoBehaviour
    {
        ResearchPanelsController Owner;
        object Engineering;
        EngineeringPanelController.EngineeringPanelSettings Settings;
        UIGrid Parent;
        readonly Dictionary<UIGrid,GridLayoutState> OriginalGrids=new Dictionary<UIGrid,GridLayoutState>();
        readonly Dictionary<ResearchButtonController,ButtonLayoutState> OriginalButtons=new Dictionary<ResearchButtonController,ButtonLayoutState>();
        bool OriginalHideInactive,Applying;
        int Remaining,Reports;

        internal void Observe(ResearchPanelsController owner)
        {
            Owner=owner;Resolve();Queue();
        }

        internal bool Contains(ResearchButtonController button)
        {
            return Resolve() && Parent!=null && button.transform.IsChildOf(Parent.transform);
        }

        internal void Queue()
        {
            if(Applying || Owner==null)return;
            Remaining=2;enabled=true;
        }

        void OnEnable(){if(Owner!=null && !Applying)Remaining=2;}
        void OnDestroy(){Release();}

        void OnNativeReposition()
        {
            // Includes rebuilds with no eligible buttons, and both fold/unfold
            // directions. Ignore only the Reposition calls made by this driver.
            Queue();
        }

        bool Resolve()
        {
            if(Owner==null)return false;
            object next;NativeRead.TryGet(Owner,"mEngineeringController",out next);
            if(!object.ReferenceEquals(Engineering,next))
            {
                Detach(true);Engineering=next;
                if(next!=null)
                    Settings=NativeRead.Get(next,"mSettings") as EngineeringPanelController.EngineeringPanelSettings;
            }
            if(Settings==null || Settings.ParentGrid==null)return false;
            if(Parent!=Settings.ParentGrid && IsRhodes())
            {
                DetachGrid();Parent=Settings.ParentGrid;
                OriginalHideInactive=Parent.hideInactive;
                Parent.onReposition+=OnNativeReposition;
            }
            return true;
        }

        bool IsRhodes()
        {
            object value;
            return Engineering!=null && NativeRead.TryGet(Engineering,"mCommanderTechTree",out value)
                && value is TechTreeAttributes && ((TechTreeAttributes)value).Name==RhodesResearch.TreeKey;
        }

        void LateUpdate()
        {
            if(Remaining<=0){enabled=false;return;}
            Remaining--;
            try
            {
                if(!Resolve() || !IsRhodes())
                {
                    // A new native controller owns active flags from now on.
                    DetachGrid(true);Remaining=0;return;
                }
                if(Settings.ResearchPanel==null || !Settings.ResearchPanel.activeInHierarchy)
                {Remaining=0;return;}
                Applying=true;
                Apply();
            }
            catch(Exception error){Remaining=0;RhodesEngineeringLayout.Warn(error);}
            finally{Applying=false;if(Remaining<=0)enabled=false;}
        }

        void Apply()
        {
            UIGrid[] grids=Settings.TechTreeGrids;
            if(grids==null)return;
            List<ResearchButtonController> buttons=NativeRead.Get(Engineering,"mResearchButtons") as List<ResearchButtonController>;
            if(buttons==null)return;
            List<ResearchButtonController> expired=new List<ResearchButtonController>();
            foreach(KeyValuePair<ResearchButtonController,ButtonLayoutState> pair in OriginalButtons)if(pair.Key==null)expired.Add(pair.Key);
            foreach(ResearchButtonController button in expired)OriginalButtons.Remove(button);
            int[] counts=new int[grids.Length];
            foreach(ResearchButtonController button in buttons)
            {
                if(button==null || !button.gameObject.activeSelf)continue;
                for(int i=0;i<grids.Length;i++)
                    if(grids[i]!=null && button.transform.parent==grids[i].transform){counts[i]++;break;}
            }
            TechTreeAttributes tree=(TechTreeAttributes)NativeRead.Get(Engineering,"mCommanderTechTree");
            int visible=0;List<string> visibleSlots=new List<string>();List<UIGrid> orderedRows=new List<UIGrid>();
            Parent.hideInactive=true;
            for(int i=0;i<grids.Length;i++)
            {
                UIGrid grid=grids[i];if(grid==null)continue;
                bool populated=counts[i]>0;
                if(grid.gameObject.activeSelf!=populated)grid.gameObject.SetActive(populated);
                GridLayoutState original;
                if(OriginalGrids.TryGetValue(grid,out original))original.RestoreConfiguration(false);
                if(!populated)continue;
                if(original==null){original=new GridLayoutState(grid);OriginalGrids.Add(grid,original);}
                grid.hideInactive=true;
                visible++;visibleSlots.Add(i.ToString(CultureInfo.InvariantCulture));orderedRows.Add(grid);
                if(counts[i]>1)
                {
                    grid.arrangement=UIGrid.Arrangement.Vertical;grid.pivot=UIWidget.Pivot.TopLeft;
                    grid.maxPerLine=0;grid.cellHeight=Parent.cellHeight;grid.cellWidth=Settings.TableWidth;
                }
                foreach(ResearchButtonController button in buttons)
                {
                    if(button==null||!button.gameObject.activeSelf||button.transform.parent!=grid.transform)continue;
                    ButtonLayoutState saved;
                    if(!OriginalButtons.TryGetValue(button,out saved)){saved=new ButtonLayoutState(button);OriginalButtons.Add(button,saved);}
                    saved.ApplyFullWidth(Settings.TableWidth);
                }
                grid.Reposition();
            }
            // Native UIGrid gives each branch one base row. Expand only rows
            // with multiple visible choices, ordered by their actual native Y.
            // Reposition first on every pass: offsets never accumulate.
            Parent.Reposition();
            orderedRows.Sort(delegate(UIGrid a,UIGrid b){return b.transform.localPosition.y.CompareTo(a.transform.localPosition.y);});
            float extra=0f;
            foreach(UIGrid grid in orderedRows)
            {
                Vector3 position=grid.transform.localPosition;position.y-=extra;grid.transform.localPosition=position;
                int count=0;foreach(ResearchButtonController button in buttons)if(button!=null&&button.gameObject.activeSelf&&button.transform.parent==grid.transform)count++;
                extra+=RhodesEngineeringLayoutPolicy.ExtraHeight(count,Parent.cellHeight);
            }
            NativeRead.Call(Engineering,"RefreshBackgroundSize");
            if(Remaining==0 && Reports++<12)
            {
                Bounds content=NGUIMath.CalculateRelativeWidgetBounds(Parent.transform,false);
                Runtime.Log("RHODES_ENGINEERING_LAYOUT tree="+tree.Name+"; visibleSlots="+string.Join(",",visibleSlots.ToArray())+
                    "; visibleRows="+visible+"; nativePitch="+Parent.cellHeight.ToString("0.##",CultureInfo.InvariantCulture)+
                    "; nativeButtonHeight="+(Settings.ButtonPrefab==null?0f:Settings.ButtonPrefab.Size.y).ToString("0.##",CultureInfo.InvariantCulture)+
                    "; contentHeight="+content.size.y.ToString("0.##",CultureInfo.InvariantCulture)+
                    "; backgroundHeight="+(Settings.BackgroundSprite==null?0:Settings.BackgroundSprite.height)+
                    "; upgradesY="+(Settings.UpgradesPanel==null?0f:Settings.UpgradesPanel.transform.localPosition.y).ToString("0.##",CultureInfo.InvariantCulture)+
                    "; fullWidthVerticalChoices=True; nativeCancelHandlersRetained=True; extraRowsHeight="+extra.ToString("0.##",CultureInfo.InvariantCulture)+"; nativeBackgroundRefresh=True; finiteLatePasses=2; recurringSceneScan=False; sharedSettingsAndResearchDataUnchanged=True");
            }
        }

        sealed class GridLayoutState
        {
            readonly UIGrid Grid;readonly UIGrid.Arrangement Arrangement;readonly UIWidget.Pivot Pivot;
            readonly float Width,Height;readonly int MaxPerLine;readonly bool HideInactive;readonly Vector3 Position;
            internal GridLayoutState(UIGrid grid)
            {Grid=grid;Arrangement=grid.arrangement;Pivot=grid.pivot;Width=grid.cellWidth;Height=grid.cellHeight;MaxPerLine=grid.maxPerLine;HideInactive=grid.hideInactive;Position=grid.transform.localPosition;}
            internal void RestoreConfiguration(bool position)
            {
                if(Grid==null)return;Grid.arrangement=Arrangement;Grid.pivot=Pivot;Grid.cellWidth=Width;Grid.cellHeight=Height;Grid.maxPerLine=MaxPerLine;Grid.hideInactive=HideInactive;
                if(position)Grid.transform.localPosition=Position;
            }
        }
        struct AnchorState
        {
            readonly Transform Target;readonly float Relative;readonly int Absolute;
            internal AnchorState(UIRect.AnchorPoint anchor){Target=anchor.target;Relative=anchor.relative;Absolute=anchor.absolute;}
            internal void Restore(UIRect.AnchorPoint anchor){anchor.target=Target;anchor.relative=Relative;anchor.absolute=Absolute;}
        }
        sealed class WidgetState
        {
            internal readonly UIWidget Widget;readonly int Width,Height;readonly Vector3 Position;
            readonly AnchorState Left,Right,Bottom,Top;readonly UIRect.AnchorUpdate UpdateMode;
            internal WidgetState(UIWidget widget)
            {
                Widget=widget;if(widget==null)return;Width=widget.width;Height=widget.height;Position=widget.transform.localPosition;
                Left=new AnchorState(widget.leftAnchor);Right=new AnchorState(widget.rightAnchor);Bottom=new AnchorState(widget.bottomAnchor);Top=new AnchorState(widget.topAnchor);UpdateMode=widget.updateAnchors;
            }
            internal void Restore()
            {
                if(Widget==null)return;Left.Restore(Widget.leftAnchor);Right.Restore(Widget.rightAnchor);Bottom.Restore(Widget.bottomAnchor);Top.Restore(Widget.topAnchor);
                Widget.updateAnchors=UpdateMode;Widget.width=Width;Widget.height=Height;Widget.ResetAnchors();Widget.transform.localPosition=Position;
            }
        }
        sealed class ButtonLayoutState
        {
            readonly ResearchButtonController Button;readonly Vector2 Size;readonly Vector3 Position;
            readonly UIWidget Main;readonly WidgetState Cancel,Timer,Name;readonly BoxCollider CancelCollider;
            readonly Vector3 CancelColliderSize,CancelColliderCenter;
            internal ButtonLayoutState(ResearchButtonController button)
            {
                Button=button;Size=button.Size;Position=button.transform.localPosition;
                Component main=NativeRead.Get(button,"m_ButtonHandler") as Component,cancel=NativeRead.Get(button,"m_CancelButtonHandler") as Component;
                Main=main==null?null:main.GetComponent<UIWidget>();
                Cancel=new WidgetState(cancel==null?null:cancel.GetComponent<UIWidget>());
                Timer=new WidgetState(NativeRead.Get(button,"m_TimeRemainingLabel") as UIWidget);
                Name=new WidgetState(NativeRead.Get(button,"m_ResearchLabel") as UIWidget);
                CancelCollider=cancel==null?null:cancel.GetComponent<BoxCollider>();
                if(CancelCollider!=null){CancelColliderSize=CancelCollider.size;CancelColliderCenter=CancelCollider.center;}
            }
            internal void ApplyFullWidth(float width)
            {
                Button.Size=new Vector2(width,Size.y);
                // Direct field references include inactive progress subtrees.
                // Keep the same native cancel object, collider and ClickAction.
                if(Main==null||Cancel.Widget==null)throw new InvalidOperationException("Native research cancel anchor widgets are missing");
                UIWidget cancel=Cancel.Widget;cancel.width=RhodesEngineeringLayoutPolicy.CancelWidth;cancel.height=RhodesEngineeringLayoutPolicy.CancelHeight;
                cancel.leftAnchor.target=null;cancel.rightAnchor.Set(Main.transform,1f,-RhodesEngineeringLayoutPolicy.RightPadding);
                cancel.ResetAndUpdateAnchors();
                if(CancelCollider!=null){CancelCollider.size=new Vector3(RhodesEngineeringLayoutPolicy.CancelWidth,RhodesEngineeringLayoutPolicy.CancelHeight,CancelColliderSize.z);CancelCollider.center=Vector3.zero;}
                if(Timer.Widget!=null)
                {
                    Timer.Widget.leftAnchor.target=null;Timer.Widget.rightAnchor.Set(cancel.transform,0f,-RhodesEngineeringLayoutPolicy.TimerGap);Timer.Widget.ResetAndUpdateAnchors();
                }
                if(Name.Widget!=null)
                {
                    int reserved=RhodesEngineeringLayoutPolicy.NameReservedWidth(Timer.Widget==null?42:Timer.Widget.width);
                    Name.Widget.rightAnchor.Set(Main.transform,1f,-reserved);Name.Widget.ResetAndUpdateAnchors();
                }
            }
            internal void Restore()
            {
                if(Button==null)return;Button.Size=Size;Button.transform.localPosition=Position;Cancel.Restore();Timer.Restore();Name.Restore();
                if(CancelCollider!=null){CancelCollider.size=CancelColliderSize;CancelCollider.center=CancelColliderCenter;}
            }
        }
        bool RestoreGeometry(bool reposition)
        {
            bool changed=OriginalGrids.Count>0||OriginalButtons.Count>0;
            foreach(KeyValuePair<ResearchButtonController,ButtonLayoutState> pair in OriginalButtons)pair.Value.Restore();
            OriginalButtons.Clear();
            foreach(KeyValuePair<UIGrid,GridLayoutState> pair in OriginalGrids)
            {pair.Value.RestoreConfiguration(true);if(reposition&&pair.Key!=null)pair.Key.Reposition();}
            OriginalGrids.Clear();return changed;
        }

        void DetachGrid(){DetachGrid(false);}

        void DetachGrid(bool reflow)
        {
            bool wasApplying=Applying;Applying=true;
            try
            {
                if(Parent!=null)Parent.onReposition-=OnNativeReposition;
                bool changed=RestoreGeometry(reflow);
                if(Parent!=null)
                {
                    changed|=Parent.hideInactive!=OriginalHideInactive;
                    Parent.hideInactive=OriginalHideInactive;
                    if(reflow && changed)
                    {
                        Parent.Reposition();
                        if(Engineering!=null)NativeRead.Call(Engineering,"RefreshBackgroundSize");
                    }
                }
            }
            finally{Applying=wasApplying;Parent=null;}
            // Never restore saved active flags: native RebuildTreeView has
            // already assigned the next faction/session's visible rows.
        }

        void Detach(bool reflow){DetachGrid(reflow);Settings=null;Engineering=null;}

        internal void Release()
        {
            Detach(false);Owner=null;Remaining=0;enabled=false;Reports=0;
        }
    }
}
