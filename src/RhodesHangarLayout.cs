using System;
using System.Collections;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Game.Commands;
using BBI.Unity.Game.HUD;
using BBI.Unity.Game.UI;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // The native fighter/gunship grids have independent anchors. Classifying
    // U08 does not align those anchors with the two private launch buttons.
    // Reflow on selection/visibility changes only, after native pooled icons
    // have been built. No Update, scene scan or per-simulation-frame polling.
    public sealed class RhodesHangarLayout:MonoBehaviour
    {
        sealed class Snapshot
        {
            internal UIGrid Grid;
            internal Vector3 Position,Scale;
            internal UIGrid.Arrangement Arrangement;
            internal UIWidget.Pivot Pivot;
            internal int Columns;
            internal float Width,Height;
            internal Snapshot(UIGrid grid)
            {Grid=grid;Position=grid.transform.localPosition;Scale=grid.transform.localScale;Arrangement=grid.arrangement;Pivot=grid.pivot;Columns=grid.maxPerLine;Width=grid.cellWidth;Height=grid.cellHeight;}
            internal void Restore()
            {if(Grid==null)return;Grid.transform.localPosition=Position;Grid.transform.localScale=Scale;Grid.arrangement=Arrangement;Grid.pivot=Pivot;Grid.maxPerLine=Columns;Grid.cellWidth=Width;Grid.cellHeight=Height;Grid.Reposition();}
        }
        UnitHangarPanelController Controller;
        UnitInterfaceController Interface;
        UIGrid Fighter,Rotor,Bomber,Deploy,Recall;
        Transform Root;
        Snapshot FighterOriginal,RotorOriginal,BomberOriginal,RecallOriginal;
        float FighterGraphicSize,RotorGraphicSize,BomberGraphicSize;
        bool Ready,Pending,Applied;
        int Reports;
        public static void Bind(object controller)
        {
            UnitHangarPanelController hangar=controller as UnitHangarPanelController;
            UnitInterfaceController unitInterface=NativeRead.Get(controller,"mInterfaceController") as UnitInterfaceController;
            if(hangar==null||unitInterface==null)return;
            RhodesHangarStateColours.ObserveNative(controller);
            RhodesHangarLayout driver=unitInterface.GetComponent<RhodesHangarLayout>();
            if(driver==null)driver=unitInterface.gameObject.AddComponent<RhodesHangarLayout>();
            if(driver.Ready)return;
            object settings=NativeRead.Get(controller,"mSettings"),commands=NativeRead.Get(unitInterface,"mCommandPanelSettings");
            driver.Fighter=NativeRead.Get(settings,"m_HangarInterceptorGrid") as UIGrid;
            driver.Rotor=NativeRead.Get(settings,"m_HangarGunShipGrid") as UIGrid;
            driver.Bomber=NativeRead.Get(settings,"m_HangarBomberGrid") as UIGrid;
            driver.Deploy=NativeRead.Get(NativeRead.Get(commands,"m_HangarDeployGrid"),"m_Grid") as UIGrid;
            driver.Recall=NativeRead.Get(NativeRead.Get(commands,"m_HangarRecallGrid"),"m_Grid") as UIGrid;
            driver.FighterGraphicSize=GraphicSize(NativeRead.Get(settings,"m_HangarInterceptorIconPrefab") as NGUIIconController);
            driver.RotorGraphicSize=GraphicSize(NativeRead.Get(settings,"m_HangarGunShipIconPrefab") as NGUIIconController);
            driver.BomberGraphicSize=GraphicSize(NativeRead.Get(settings,"m_HangarBomberIconPrefab") as NGUIIconController);
            GameObject root=NativeRead.Get(settings,"m_HangarParentObject") as GameObject;
            if(driver.Fighter==null||driver.Rotor==null||driver.Deploy==null||driver.Recall==null||root==null)
                throw new InvalidOperationException("Native Rhodes hangar layout references missing");
            driver.Controller=hangar;driver.Interface=unitInterface;driver.Root=root.transform;driver.Ready=true;
            unitInterface.NewSelection+=driver.OnSelection;
            unitInterface.NewLeadUnitSelected+=driver.OnLead;
            hangar.UnitHangarPanelVisibleChanged+=driver.OnVisible;
            driver.Schedule();
        }
        void OnSelection(IList<Entity> entities,SimStateFrame frame){Schedule();}
        void OnLead(Entity entity){Schedule();}
        void OnVisible(bool visible){if(visible)Schedule();}
        void OnEnable(){if(Ready)Schedule();}
        void OnDisable(){StopAllCoroutines();Pending=false;}
        void Schedule(){if(!Ready||Pending||!gameObject.activeInHierarchy)return;Pending=true;StartCoroutine(DeferredLayout());}
        IEnumerator DeferredLayout()
        {
            yield return null;Pending=false;
            if(Ready&&Interface!=null)Reflow();
        }
        void Restore()
        {
            if(!Applied)return;
            FighterOriginal.Restore();RotorOriginal.Restore();if(BomberOriginal!=null)BomberOriginal.Restore();RecallOriginal.Restore();Applied=false;
        }
        static float GraphicSize(NGUIIconController prefab)
        {
            if(prefab!=null)foreach(object item in (IEnumerable)NativeRead.Get(prefab,"m_IconObjects"))
            {
                if((string)NativeRead.Get(item,"m_Name")!="Icon")continue;
                UISprite sprite=NativeRead.Get(item,"m_Sprite") as UISprite;if(sprite==null)break;
                float width=prefab.transform.InverseTransformVector(sprite.transform.TransformVector(new Vector3(sprite.width,0f,0f))).magnitude;
                float height=prefab.transform.InverseTransformVector(sprite.transform.TransformVector(new Vector3(0f,sprite.height,0f))).magnitude;
                float size=Mathf.Max(width,height);if(size>.0001f)return size;
            }
            throw new InvalidOperationException("Native hangar Icon graphic dimensions missing");
        }
        static void Buttons(UIGrid grid,out Transform fighter,out Transform rotor,out Transform bomber)
        {
            fighter=null;rotor=null;bomber=null;
            foreach(Transform child in grid.transform)
            {
                NGUIIconController icon=child.GetComponent<NGUIIconController>();
                if(icon==null||icon.Data==null||icon.Data.GetType().Name!="AbilityButtonState")continue;
                string key=NativeRead.Get(icon.Data,"AbilityTypeName") as string;
                if(key==RhodesCarrierUnits.AircraftLaunchKey&&(fighter==null||child.gameObject.activeSelf))fighter=child;
                if(key==RhodesCarrierUnits.TiltLaunchKey&&(rotor==null||child.gameObject.activeSelf))rotor=child;
                if(key==RhodesCarrierUnits.HeavyBomberLaunchKey&&(bomber==null||child.gameObject.activeSelf))bomber=child;
            }
        }
        Vector3 Local(Transform value){return Root.InverseTransformPoint(value.position);}
        float LocalStep(UIGrid grid)
        {return Root.InverseTransformVector(grid.transform.TransformVector(new Vector3(grid.cellWidth,0f,0f))).magnitude;}
        void Position(UIGrid grid,float x,float y,float step,int columns)
        {
            float width=Root.InverseTransformVector(grid.transform.TransformVector(Vector3.right)).magnitude;
            float height=Root.InverseTransformVector(grid.transform.TransformVector(Vector3.up)).magnitude;
            grid.arrangement=UIGrid.Arrangement.Horizontal;grid.pivot=UIWidget.Pivot.TopLeft;grid.maxPerLine=columns;
            grid.cellWidth=step/Mathf.Max(.0001f,width);grid.cellHeight=step/Mathf.Max(.0001f,height);
            Vector3 local=Local(grid.transform);local.x=x;local.y=y;grid.transform.position=Root.TransformPoint(local);grid.Reposition();
        }
        float FitGrid(UIGrid grid,Snapshot original,float graphicSize,float step)
        {
            // Native pool spawns restore EACH ICON root to scale one. Keep
            // that contract and resize its persistent row parent instead.
            // Derive from a fixed native graphic, not current occupancy,
            // selection state or the invisible DisabledCollider widget.
            grid.transform.localScale=original.Scale;
            float axis=Root.InverseTransformVector(grid.transform.TransformVector(Vector3.right)).magnitude;
            float factor=step*.86f/(graphicSize*Mathf.Max(.0001f,axis));
            grid.transform.localScale=original.Scale*factor;return factor;
        }
        void Reflow()
        {
            Dictionary<string,List<NGUIIconController>> slots=NativeRead.Get(Controller,"mHangarSlotIcons") as Dictionary<string,List<NGUIIconController>>;
            List<NGUIIconController> fighters,rotors,bombers=null;
            if(slots==null||!slots.TryGetValue(RhodesCarrierUnits.AircraftKey,out fighters)||!slots.TryGetValue(FactionUnits.TiltStrikeKey,out rotors))
            {Restore();return;}
            Transform fighterButton,rotorButton,bomberButton;Buttons(Deploy,out fighterButton,out rotorButton,out bomberButton);
            if(fighterButton==null||rotorButton==null)return;
            if(FighterOriginal==null)
            {FighterOriginal=new Snapshot(Fighter);RotorOriginal=new Snapshot(Rotor);RecallOriginal=new Snapshot(Recall);if(Bomber!=null)BomberOriginal=new Snapshot(Bomber);}
            Vector3 left=Local(fighterButton),right=Local(rotorButton);float separation=Mathf.Abs(right.x-left.x);
            bool three=Bomber!=null&&BomberOriginal!=null&&bomberButton!=null&&slots.TryGetValue(RhodesCarrierUnits.HeavyBomberKey,out bombers);
            if(!three)bombers=null;
            Vector3 third=three?Local(bomberButton):right;
            if(three)separation=Mathf.Min(separation,Mathf.Min(Mathf.Abs(third.x-left.x),Mathf.Abs(third.x-right.x)));
            if(separation<8f){Restore();return;}
            Fighter.transform.localScale=FighterOriginal.Scale;
            Fighter.cellWidth=FighterOriginal.Width;
            float step=Mathf.Min(LocalStep(Fighter),separation*.5f);
            if(step<8f)return;
            Bounds header=NGUIMath.CalculateRelativeWidgetBounds(Root,Deploy.transform,true);
            float top=header.min.y-step*.8f;
            float fighterScale=FitGrid(Fighter,FighterOriginal,FighterGraphicSize,step);
            float rotorScale=FitGrid(Rotor,RotorOriginal,RotorGraphicSize,step);
            Position(Fighter,left.x-step,top,step,3);
            Position(Rotor,right.x,top,step,1);
            if(three){FitGrid(Bomber,BomberOriginal,BomberGraphicSize,step);Position(Bomber,third.x,top,step,1);}
            int rows=Math.Max((fighters.Count+2)/3,rotors.Count);
            if(three)rows=Math.Max(rows,bombers.Count);
            Vector3 recall=Local(Recall.transform);recall.x=three?(Mathf.Min(left.x,Mathf.Min(right.x,third.x))+Mathf.Max(left.x,Mathf.Max(right.x,third.x)))*.5f:(left.x+right.x)*.5f;recall.y=top-(Math.Max(1,rows)-1)*step-step*1.8f;
            Recall.transform.position=Root.TransformPoint(recall);Applied=true;
            if(Reports++<4)Runtime.Log("RHODES_HANGAR_LAYOUT_APPLIED fighterColumns=3; tiltColumns=1; topAligned=True; launchButtonCentersUsed=True; recallBelowSlots=True; slotStep="+step+"; fighterParentScale="+fighterScale+"; tiltParentScale="+rotorScale+"; pooledIconRootScaleWrites=False; invisibleColliderExcludedFromSize=True; nativePrefabsAndClickHandlersRetained=True; eventDrivenOnly=True; runtimeVisualAcceptancePending=True");
        }
        void OnDestroy()
        {
            if(Interface!=null){Interface.NewSelection-=OnSelection;Interface.NewLeadUnitSelected-=OnLead;}
            if(Controller!=null)Controller.UnitHangarPanelVisibleChanged-=OnVisible;
            Restore();Ready=false;
        }
    }
}
