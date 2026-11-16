using System;
using System.Collections;
using System.Collections.Generic;
using BBI.Core.ComponentModel;
using BBI.Game.Commands;
using BBI.Game.Simulation;
using BBI.Unity.Game.HUD;
using BBI.Unity.Game.UI;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Carrier-style cargo management for an aircraft. Native slot widgets and
    // lifecycle are reused without adding Carrier to the simulation. UI reads
    // immutable snapshots only, through native frame and selection events.
    public sealed class RhodesOperatorStatusUI:MonoBehaviour
    {
        static readonly Color Yellow=new Color(238f/255f,229f/255f,46f/255f,1f),White=new Color(236f/255f,237f/255f,231f/255f,1f);
        static readonly Color Dim=new Color(.47f,.49f,.48f,1f),ButtonFill=new Color(.16f,.17f,.17f,.98f);
        const float Padding=4f;
        internal sealed class PageLayout
        {
            internal float Width,Height,HeaderHeight,RowHeight,PortraitSize,DetailTop,DetailHeight,ActionTop,ActionWidth,ActionHeight,AutoTop,AutoHeight,FeedbackTop,FeedbackHeight;
            internal int TitleFont,BodyFont;
            internal float SlotX(int index){return Width*(index%2==0?.25f:.75f);}
            internal float SlotY(int index){return HeaderHeight+4f+RowHeight*(index/2+.5f);}
            internal float FitNativeVisual(float primarySide,float visualWidth,float visualHeight)
            {
                float hitWidth=(float)Math.Round(Width*.5f-8f),hitHeight=(float)Math.Round(RowHeight-4f);
                return Math.Min(PortraitSize/primarySide,Math.Min(hitWidth/visualWidth,hitHeight/visualHeight));
            }
        }
        internal static PageLayout CalculateLayout(float frameWidth,float frameHeight,float bodyUnits)
        {
            int maximumFont=Math.Max(10,(int)Math.Ceiling(bodyUnits));
            // The real native frame can be 151 x 232 parent-space units. Keep
            // that frame and its readable card minima; choose the largest font
            // that fits rather than scaling the whole page or dropping it.
            for(int font=maximumFont;font>=10;font--)
            {
                PageLayout fitted=CalculateLayoutForFont(frameWidth,frameHeight,font);
                if(fitted.Width>=100f&&fitted.RowHeight>=28f&&fitted.PortraitSize>=24f)return fitted;
            }
            throw new InvalidOperationException("Native hangar frame is too small for readable cargo controls; frame="+frameWidth+"x"+frameHeight+"; bodyUnits="+bodyUnits+"; minimumBodyFont=10; minimumRowHeight=28; minimumPortrait=24");
        }
        static PageLayout CalculateLayoutForFont(float frameWidth,float frameHeight,int font)
        {
            PageLayout layout=new PageLayout();
            layout.Width=frameWidth-2f*Padding;layout.Height=frameHeight-2f*Padding;
            layout.BodyFont=font;layout.TitleFont=(int)Math.Ceiling(layout.BodyFont*1.1f);
            layout.HeaderHeight=layout.TitleFont+layout.BodyFont+14f;
            layout.DetailHeight=layout.BodyFont+6f;layout.ActionHeight=Math.Max(24f,layout.BodyFont+10f);layout.AutoHeight=layout.ActionHeight;layout.FeedbackHeight=layout.BodyFont+8f;
            layout.RowHeight=(layout.Height-layout.HeaderHeight-layout.DetailHeight-layout.ActionHeight-layout.AutoHeight-layout.FeedbackHeight-30f)*.5f;
            layout.PortraitSize=Math.Min(layout.Width*.5f-16f,layout.RowHeight*.86f);
            layout.DetailTop=layout.HeaderHeight+4f+layout.RowHeight*2f+6f;
            layout.ActionTop=layout.DetailTop+layout.DetailHeight+6f;
            layout.ActionWidth=(layout.Width-8f)/3f;
            layout.AutoTop=layout.ActionTop+layout.ActionHeight+6f;
            layout.FeedbackTop=layout.AutoTop+layout.AutoHeight+8f;
            return layout;
        }
        sealed class SlotWidgets
        {
            internal NGUIIconController Icon;
            internal UISprite Portrait,Cooldown;
            internal int Width,Height;
            internal BoxCollider[] Colliders;
            internal UITexture Selected,Background;
            internal UILabel Empty;
            internal Transform Anchor;
            internal BoxCollider HitBox;
            internal NGUIEventHandler HitInput;
            internal Vector3 GraphicPosition,GraphicScale,GraphicCenter;
            internal float VisualWidth,VisualHeight;
            internal Quaternion GraphicRotation;
            internal UIWidget.Pivot GraphicPivot;
            internal string PortraitName;
        }
        sealed class ActionWidgets {internal BoxCollider Collider;internal UITexture Fill;internal UILabel Label;internal bool Enabled;}
        enum ActionKind { Deploy, Recall, Select, Auto }
        UnitInterfaceController Interface;
        object HudDependencies;
        GameObject Root;
        Transform NativeHangar;
        Bounds NativeRegion;
        bool AlignmentPending,BuildPending,EventsBound,RegionDumped;
        object PendingHangarController;
        UnitHangarPanelController NativeHangarController;
        Transform RegionHint,NativeSpace,HangarObject;
        UIWidget NativeFrame;
        UILabel NativeFontReference;
        UITexture Background,Accent,HeaderRule;
        Camera UICameraReference;
        PageLayout Layout;
        int GeometryReports;
        string LastGeometry;
        string RegionReference;
        int BuildAttempts,BuildWarnings;
        UILabel Header,Phase,Detail,Feedback;
        readonly SlotWidgets[] Cards=new SlotWidgets[RhodesOperatorTransport.Capacity];
        readonly ActionWidgets[] Actions=new ActionWidgets[3];
        ActionWidgets AutoAction;
        RhodesOperatorSlotStatus[] Slots=new RhodesOperatorSlotStatus[RhodesOperatorTransport.Capacity];
        Entity Selected=Entity.None;
        int ActiveSlot,LastCargoRevision=-1,LastManifestRevision=-1;
        string LastPhase,LocalFeedback;
        string LastSkillType;
        int LastSkillSeconds=-1;
        bool LastSkillKnown,LastSkillReady,LastAutoResearched,LastAutoEnabled;
        string LastSkillReason;
        bool Ready,Dirty;

        public static void Bind(object hangarController)
        {
            UnitInterfaceController owner=NativeRead.Get(hangarController,"mInterfaceController") as UnitInterfaceController;
            if(owner==null)return;
            RhodesOperatorStatusUI driver=owner.GetComponent<RhodesOperatorStatusUI>();
            if(driver==null)driver=owner.gameObject.AddComponent<RhodesOperatorStatusUI>();
            if(driver.Ready)return;
            if(!driver.EventsBound)
            {
                driver.Interface=owner;driver.PendingHangarController=hangarController;
                driver.NativeHangarController=hangarController as UnitHangarPanelController;
                owner.NewSelection+=driver.OnSelection;owner.NewLeadUnitSelected+=driver.OnLead;owner.NewFrameFromSim+=driver.OnFrame;
                UICamera.onScreenResize+=driver.OnScreenResize;
                if(driver.NativeHangarController!=null)driver.NativeHangarController.UnitHangarPanelVisibleChanged+=driver.OnNativeHangarVisible;
                driver.EventsBound=true;
            }
            driver.ScheduleBuild();
        }
        void ScheduleBuild()
        {
            if(Ready||BuildPending||Interface==null||!gameObject.activeInHierarchy)return;
            BuildPending=true;StartCoroutine(DeferredBuild());
        }
        IEnumerator DeferredBuild()
        {
            // At most two attempts for each explicit selection/layout event.
            // No frame event schedules bootstrap work and no Update is added.
            for(int attempt=0;attempt<2&&!Ready;attempt++)
            {
                yield return null;yield return null;BuildAttempts++;
                try{if(Build(Interface,PendingHangarController))break;}
                catch(Exception error)
                {
                    DestroyWidgets();
                    if(BuildWarnings++<2)Runtime.Log("WARNING RHODES_OPERATOR_MANAGER_UI_FIX09R1 "+error.GetType().Name+": "+error.Message+"; retryOnNextSelectionOrNativeLayout=True");
                }
            }
            BuildPending=false;
        }
        void OnNativeHangarVisible(bool visible){if(visible){if(Ready)ScheduleAlignment();else ScheduleBuild();}}
        bool Build(UnitInterfaceController owner,object hangarController)
        {
            object info=NativeRead.Get(owner,"mInformationPanelSettings"),hangar=NativeRead.Get(hangarController,"mSettings");
            object commands=NativeRead.Get(owner,"mCommandPanelSettings");
            GameObject hangarRegion=NativeRead.Get(commands,"m_HangarAbilityGridContainer") as GameObject;
            GameObject commandRoot=NativeRead.Get(commands,"m_CommandPanelContainer") as GameObject;
            GameObject hangarObject=NativeRead.Get(hangar,"m_HangarParentObject") as GameObject;
            UILabel reference=NativeRead.Get(info,"mUnitNameLabel") as UILabel;
            NGUIIconController prefab=NativeRead.Get(hangar,"m_HangarGunShipIconPrefab") as NGUIIconController;
            HudDependencies=NativeRead.Get(NativeRead.Get(hangarController,"mHUDSystem"),"mDependencyContainer");
            if(hangarRegion==null||commandRoot==null||hangarObject==null||reference==null||prefab==null||HudDependencies==null||(reference.bitmapFont==null&&reference.trueTypeFont==null))
                throw new InvalidOperationException("Native operator panel font, slot or lifecycle dependency missing");
            Interface=owner;RegionHint=hangarRegion.transform;NativeSpace=commandRoot.transform;HangarObject=hangarObject.transform;
            NativeFontReference=reference;
            if(!ResolveNativeRegion())return false;
            Root=new GameObject("EF_OperatorCargoManager");Root.layer=reference.gameObject.layer;Root.hideFlags=HideFlags.HideAndDontSave;
            Root.transform.parent=NativeHangar;Root.transform.localRotation=Quaternion.identity;Root.transform.localScale=Vector3.one;
            NativeHangar=Root.transform.parent;UICameraReference=NGUITools.FindCameraForLayer(Root.layer);
            int depth=reference.depth+8;
            Background=Fill("EF_CargoBackground",new Color(.067f,.071f,.075f,.94f),0,0,1,1,depth);
            Accent=Fill("EF_CargoAccent",Yellow,0,0,1,1,depth+1);
            HeaderRule=Fill("EF_CargoHeaderRule",new Color(.75f,.76f,.73f,.20f),0,0,1,1,depth+1);
            Header=Label("EF_CargoHeader",reference,0,0,1,1,12,White,depth+2,NGUIText.Alignment.Left);
            Phase=Label("EF_CargoPhase",reference,0,0,1,1,10,Dim,depth+2,NGUIText.Alignment.Left);
            for(int i=0;i<Cards.Length;i++)
            {
                SlotWidgets card=new SlotWidgets();Cards[i]=card;
                card.Background=Fill("EF_OperatorSlotBackground_"+i,new Color(.12f,.13f,.13f,.96f),0,0,1,1,depth+2);
                card.HitBox=card.Background.gameObject.AddComponent<BoxCollider>();card.HitBox.isTrigger=true;
                card.HitInput=card.Background.gameObject.AddComponent<NGUIEventHandler>();card.HitInput.Data=i;card.HitInput.ClickAction=OnSlotClicked;card.HitInput.DoubleClickAction=OnSlotDoubleClicked;
                card.Empty=Label("EF_OperatorEmpty_"+i,reference,0,0,1,1,10,Dim,depth+3,NGUIText.Alignment.Center);card.Empty.text=(i+1).ToString("00");
                card.Anchor=Node("EF_OperatorSlot_"+i,0,0).transform;
                NGUIIconController icon=UnityEngine.Object.Instantiate(prefab) as NGUIIconController;
                card.Icon=icon;icon.name="EF_OperatorPortraitSlot_"+i;icon.transform.parent=card.Anchor;icon.transform.localPosition=Vector3.zero;icon.transform.localRotation=Quaternion.identity;icon.transform.localScale=Vector3.one;
                NativeRead.Call(icon,"OnSpawned",HudDependencies);NativeRead.Set(icon,"TrackPosition",false);
                foreach(UISprite visual in icon.GetComponentsInChildren<UISprite>(true))visual.depth=depth+3;
                foreach(UILabel hotkey in icon.GetComponentsInChildren<UILabel>(true))hotkey.enabled=false;
                icon.SetShowing("PassiveIcon",false);icon.SetShowing("DisabledPassiveIcon",false);
                foreach(object item in (IEnumerable)NativeRead.Get(icon,"m_IconObjects"))if((string)NativeRead.Get(item,"m_Name")=="Icon")card.Portrait=NativeRead.Get(item,"m_Sprite") as UISprite;
                if(card.Portrait==null)throw new InvalidOperationException("Native carrier slot portrait visual missing");
                card.Width=card.Height=Math.Max(card.Portrait.width,card.Portrait.height);card.GraphicPivot=UIWidget.Pivot.Center;
                card.GraphicScale=card.Portrait.transform.localScale;card.GraphicRotation=card.Portrait.transform.localRotation;
                Vector3 center=Vector3.zero;foreach(Vector3 corner in card.Portrait.worldCorners)center+=corner*.25f;
                card.GraphicCenter=icon.transform.InverseTransformPoint(center);card.GraphicPosition=card.Portrait.transform.parent.InverseTransformPoint(center);
                card.Colliders=icon.GetComponentsInChildren<BoxCollider>(true);foreach(BoxCollider collider in card.Colliders)collider.enabled=false;
                UIAtlas atlas=OperatorPortraitIcons.CargoAtlas(card.Portrait.atlas);icon.SetAtlas("Icon",atlas);RestoreGraphic(card);card.Portrait.atlas=atlas;
                SetPortrait(card,OperatorPortraitIcons.CargoName);
                List<UIWidget> visuals=new List<UIWidget>();
                foreach(object item in (IEnumerable)NativeRead.Get(icon,"m_IconObjects"))
                {
                    string key=(string)NativeRead.Get(item,"m_Name");
                    if(key!="Icon"&&key!="Background"&&key!="Border"&&key!="Cooldown")continue;
                    UISprite sprite=NativeRead.Get(item,"m_Sprite") as UISprite;
                    if(key=="Cooldown"&&sprite!=null)
                    {
                        card.Cooldown=sprite;ClearPrivateAnchors(sprite);sprite.pivot=UIWidget.Pivot.Center;sprite.width=card.Width;sprite.height=card.Height;
                        sprite.transform.localPosition=sprite.transform.parent.InverseTransformPoint(card.Portrait.transform.position);
                    }
                    if(sprite!=null&&sprite.transform.IsChildOf(icon.transform))visuals.Add(sprite);
                }
                Bounds envelope=WidgetBounds(icon.transform,visuals);
                card.VisualWidth=2f*Math.Max(Math.Abs(envelope.min.x-card.GraphicCenter.x),Math.Abs(envelope.max.x-card.GraphicCenter.x));
                card.VisualHeight=2f*Math.Max(Math.Abs(envelope.min.y-card.GraphicCenter.y),Math.Abs(envelope.max.y-card.GraphicCenter.y));
                if(card.VisualWidth<=0f||card.VisualHeight<=0f)throw new InvalidOperationException("Native cargo visual envelope missing");
                card.Selected=Fill("EF_OperatorSelected_"+i,Dim,0,0,1,1,depth+3);
                NGUITools.MarkParentAsChanged(icon.gameObject);
            }
            Detail=Label("EF_CargoDetail",reference,0,0,1,1,10,White,depth+3,NGUIText.Alignment.Left);
            Actions[0]=Button("部署",ActionKind.Deploy,reference,0,0,1,depth+3);
            Actions[1]=Button("召回",ActionKind.Recall,reference,0,0,1,depth+3);
            Actions[2]=Button("选择",ActionKind.Select,reference,0,0,1,depth+3);
            AutoAction=Button("自动：同步中",ActionKind.Auto,reference,0,0,1,depth+3);
            Feedback=Label("EF_CargoFeedback",reference,0,0,1,1,10,White,depth+3,NGUIText.Alignment.Left);
            AlignToNativeHangar();
            Root.SetActive(false);Ready=true;Dirty=true;if(Selected==Entity.None)Selected=owner.LeadUnit;
            Runtime.Log("RHODES_OPERATOR_MANAGER_UI_READY slots=4; nativeCarrierSlotPrefab=True; nativeSlotLifecycle=True; sharedHUDPoolUntouched=True; portrait=RegisteredNativeRGBA; exactPassengerActions=True; nativeHangarRegion=True; nativePrimaryIconOnly=True; nativeWidgetRectangleLocked=True; fullNativeFrame=True; twoByTwoSlots=True; rootScaleOne=True; cachedManifestOnly=True; noCarrierClassMutation=True; runtimeVisualAcceptancePending=True");
            Render();ScheduleAlignment();return true;
        }
        static Bounds WidgetBounds(Transform space,IList<UIWidget> widgets)
        {
            Bounds result=new Bounds();bool found=false;
            foreach(UIWidget widget in widgets)
            {
                if(widget==null)continue;
                foreach(Vector3 corner in widget.worldCorners)
                {Vector3 point=space.InverseTransformPoint(corner);if(!found){result=new Bounds(point,Vector3.zero);found=true;}else result.Encapsulate(point);}
            }
            return result;
        }
        static void CollectFrameWidgets(Transform node,int depth,List<UIWidget> widgets,ref int left)
        {
            if(node==null||left--<=0||node.name=="EF_OperatorCargoManager")return;
            // Stop at icon roots inside this bounded subtree. An icon above
            // the search root must not disqualify unrelated native frames.
            if(node.GetComponent<NGUIIconController>()!=null)return;
            UIWidget widget=node.GetComponent<UIWidget>();
            if(widget!=null&&widget.enabled&&widget.color.a>.01f&&node.name.IndexOf("collider",StringComparison.OrdinalIgnoreCase)<0)widgets.Add(widget);
            if(depth<=0)return;
            for(int i=0;i<node.childCount&&left>0;i++)CollectFrameWidgets(node.GetChild(i),depth-1,widgets,ref left);
        }
        bool ResolveNativeRegion()
        {
            List<Transform> candidates=new List<Transform>();
            foreach(Transform seed in new Transform[]{RegionHint,HangarObject})
            {
                Transform parent=seed;
                for(int level=0;parent!=null&&level<4;level++,parent=parent.parent)
                {
                    if(candidates.Count<48&&!candidates.Contains(parent))candidates.Add(parent);
                    for(int i=0;i<parent.childCount&&i<16&&candidates.Count<48;i++)if(!candidates.Contains(parent.GetChild(i)))candidates.Add(parent.GetChild(i));
                }
            }
            foreach(Transform candidate in candidates)
            {
                // This exact native frame and parent were identified in the
                // Fix08r1 runtime. Do not substitute a dynamic icon/grid rect.
                if(candidate.name!="HangarPanel-Container"||candidate.parent==null||candidate.parent.name!="UI-Anchor-BottomRight")continue;
                Transform background=candidate.Find("Hangar/Sprite-BG");
                UIWidget frame=background==null?null:background.GetComponent<UIWidget>();
                if(frame==null)continue;
                NativeFrame=frame;NativeHangar=candidate.parent;RegionReference=candidate.name+"/Hangar/Sprite-BG <- "+NativeHangar.name;
                NativeRegion=WidgetBounds(NativeHangar,new UIWidget[]{frame});
                if(NativeRegion.size.x<100f||NativeRegion.size.y<160f)continue;
                Runtime.Log("RHODES_OPERATOR_UI_REGION_FIX09R1 reference="+RegionReference+"; parentSpace=True; rootLocalScaleOne=True; localBounds="+NativeRegion+"; bootstrapAttempts="+BuildAttempts+"; runtimeVisualAcceptancePending=True");
                return true;
            }
            DumpNativeRegionOnce(candidates);return false;
        }
        void DumpNativeRegionOnce(IList<Transform> candidates)
        {
            if(RegionDumped)return;RegionDumped=true;int rows=0;
            foreach(Transform candidate in candidates)
            {
                if(rows>=24)break;
                List<UIWidget> widgets=new List<UIWidget>();int budget=24;CollectFrameWidgets(candidate,1,widgets,ref budget);
                Bounds bounds=WidgetBounds(NativeSpace,widgets);
                string samples=string.Empty;
                for(int i=0;i<widgets.Count&&i<3;i++)samples+=" ["+widgets[i].name+" "+widgets[i].GetType().Name+" "+widgets[i].width+"x"+widgets[i].height+" depth="+widgets[i].depth+"]";
                Runtime.Log("RHODES_OPERATOR_UI_REGION_PROBE_FIX09R1 row="+rows+"; node="+candidate.name+"; parent="+(candidate.parent==null?"none":candidate.parent.name)+"; active="+candidate.gameObject.activeInHierarchy+"; widgets="+widgets.Count+"; localBounds="+bounds+"; widgetSamples="+samples);rows++;
            }
        }
        static void ClearPrivateAnchors(UIRect widget)
        {
            foreach(UIRect.AnchorPoint anchor in new UIRect.AnchorPoint[]{widget.leftAnchor,widget.rightAnchor,widget.bottomAnchor,widget.topAnchor})
            {anchor.target=null;anchor.relative=0f;anchor.absolute=0;anchor.rect=null;anchor.targetCam=null;}
            widget.ResetAnchors();
        }
        static void RestoreGraphic(SlotWidgets card)
        {
            UISprite graphic=card.Portrait;
            // This private slot uses explicit page-local geometry. No cloned
            // anchors may write it back into another HUD branch later.
            ClearPrivateAnchors(graphic);graphic.pivot=card.GraphicPivot;graphic.width=card.Width;graphic.height=card.Height;
            graphic.transform.localRotation=card.GraphicRotation;graphic.transform.localScale=card.GraphicScale;graphic.transform.localPosition=card.GraphicPosition;
            card.Icon.transform.localPosition=-card.GraphicCenter;
            foreach(BoxCollider collider in card.Colliders)collider.enabled=false;
        }
        static void SetPortrait(SlotWidgets card,string name)
        {card.Icon.SetIcon("Icon",name);RestoreGraphic(card);card.PortraitName=name;}
        static void Place(UIWidget widget,float x,float y,float width,float height)
        {
            widget.pivot=UIWidget.Pivot.TopLeft;widget.width=Math.Max(1,Mathf.RoundToInt(width));widget.height=Math.Max(1,Mathf.RoundToInt(height));
            // UIWidget.pivot preserves the old world corner by moving its
            // transform. Position is therefore always assigned LAST.
            widget.transform.localPosition=new Vector3(x,-y,0f);
        }
        void AlignToNativeHangar()
        {
            if(Root==null||NativeFrame==null||NativeFontReference==null||NativeHangar==null)return;
            // The initial build needs dimensions before Ready. A pending
            // selection/resize layout can expire while a built page is hidden.
            if(Ready&&!Root.activeInHierarchy)return;
            NativeRegion=WidgetBounds(NativeHangar,new UIWidget[]{NativeFrame});
            Root.transform.parent=NativeHangar;Root.transform.localRotation=Quaternion.identity;Root.transform.localScale=Vector3.one;
            Root.transform.localPosition=new Vector3(NativeRegion.min.x+Padding,NativeRegion.max.y-Padding,NativeRegion.center.z);
            float nativeLine=NativeFontReference.fontSize;
            float referenceWorld=NativeFontReference.transform.TransformVector(new Vector3(0f,nativeLine,0f)).magnitude;
            float pageUnit=Root.transform.TransformVector(Vector3.up).magnitude;
            Layout=CalculateLayout(NativeRegion.size.x,NativeRegion.size.y,referenceWorld/Math.Max(.0001f,pageUnit));
            Place(Background,0,0,Layout.Width,Layout.Height);Place(Accent,0,0,1,Layout.Height);
            Header.fontSize=Layout.TitleFont;Phase.fontSize=Layout.BodyFont;Detail.fontSize=Layout.BodyFont;Feedback.fontSize=Layout.BodyFont;
            Place(Header,6,3,Layout.Width-12,Layout.TitleFont+3);Place(Phase,6,Layout.TitleFont+6,Layout.Width-12,Layout.BodyFont+3);
            Place(HeaderRule,6,Layout.HeaderHeight-2,Layout.Width-12,1);
            for(int i=0;i<Cards.Length;i++)
            {
                SlotWidgets card=Cards[i];float x=Layout.SlotX(i),y=Layout.SlotY(i),column=Layout.Width*.5f;
                Place(card.Background,column*(i%2)+4,Layout.HeaderHeight+4+Layout.RowHeight*(i/2)+2,column-8,Layout.RowHeight-4);
                card.HitBox.center=new Vector3(card.Background.width*.5f,-card.Background.height*.5f,0);card.HitBox.size=new Vector3(card.Background.width,card.Background.height,1);
                card.Empty.fontSize=Layout.BodyFont;Place(card.Empty,column*(i%2)+4,y-Layout.BodyFont*.5f,column-8,Layout.BodyFont+3);
                card.Anchor.localPosition=new Vector3(x,-y,0);card.Anchor.localRotation=Quaternion.identity;card.Anchor.localScale=Vector3.one;
                RestoreGraphic(card);card.Anchor.localScale=Vector3.one*Layout.FitNativeVisual(card.Width,card.VisualWidth,card.VisualHeight);
                Place(card.Selected,x-Layout.PortraitSize*.5f,y+Layout.PortraitSize*.5f+2,Layout.PortraitSize,1);
            }
            Place(Detail,6,Layout.DetailTop,Layout.Width-12,Layout.DetailHeight);
            for(int i=0;i<Actions.Length;i++)
            {
                ActionWidgets action=Actions[i];float x=i*(Layout.ActionWidth+4);
                Place(action.Fill,x,Layout.ActionTop,Layout.ActionWidth,Layout.ActionHeight);action.Collider.center=new Vector3(action.Fill.width*.5f,-action.Fill.height*.5f,0);action.Collider.size=new Vector3(action.Fill.width,action.Fill.height,1);
                action.Label.fontSize=Layout.BodyFont;Place(action.Label,x,Layout.ActionTop+(Layout.ActionHeight-Layout.BodyFont)*.5f-1,Layout.ActionWidth,Layout.BodyFont+3);
            }
            Place(AutoAction.Fill,0,Layout.AutoTop,Layout.Width,Layout.AutoHeight);
            AutoAction.Collider.center=new Vector3(AutoAction.Fill.width*.5f,-AutoAction.Fill.height*.5f,0);AutoAction.Collider.size=new Vector3(AutoAction.Fill.width,AutoAction.Fill.height,1);
            AutoAction.Label.fontSize=Layout.BodyFont;Place(AutoAction.Label,0,Layout.AutoTop+(Layout.AutoHeight-Layout.BodyFont)*.5f-1,Layout.Width,Layout.BodyFont+3);
            Place(Feedback,6,Layout.FeedbackTop,Layout.Width-12,Layout.FeedbackHeight);
            RecordLayout();
        }
        Rect ScreenRect(Vector3[] corners)
        {
            float left=float.MaxValue,right=float.MinValue,bottom=float.MaxValue,top=float.MinValue;
            foreach(Vector3 corner in corners){Vector3 point=UICameraReference.WorldToScreenPoint(corner);left=Math.Min(left,point.x);right=Math.Max(right,point.x);bottom=Math.Min(bottom,point.y);top=Math.Max(top,point.y);}
            return new Rect(left,Screen.height-top,right-left,top-bottom);
        }
        Rect ColliderRect(BoxCollider collider)
        {
            Vector3[] points=new Vector3[8];for(int i=0;i<8;i++)points[i]=collider.transform.TransformPoint(collider.center+Vector3.Scale(collider.size,new Vector3((i&1)==0?-.5f:.5f,(i&2)==0?-.5f:.5f,(i&4)==0?-.5f:.5f)));
            return ScreenRect(points);
        }
        float LabelPixels(UILabel label,float localHeight)
        {
            Vector3 first=UICameraReference.WorldToScreenPoint(label.transform.position),second=UICameraReference.WorldToScreenPoint(label.transform.TransformPoint(new Vector3(0,localHeight,0)));
            return Math.Abs(second.y-first.y);
        }
        float TextPixels(UILabel label){return LabelPixels(label,label.printedSize.y);}
        float EmPixels(UILabel label){return LabelPixels(label,label.fontSize);}
        void RecordLayout()
        {
            if(!Ready||Root==null||!Root.activeInHierarchy||UICameraReference==null||GeometryReports>=6)return;
            Rect frame=ScreenRect(NativeFrame.worldCorners),page=ScreenRect(Background.worldCorners);
            string signature=Screen.width+"x"+Screen.height+" "+frame+" "+page+" "+Header.fontSize+"/"+Detail.fontSize;
            if(signature==LastGeometry)return;LastGeometry=signature;GeometryReports++;
            float reference=TextPixels(NativeFontReference),title=TextPixels(Header),body=TextPixels(Detail),nativeEm=EmPixels(NativeFontReference);
            Runtime.Log("RHODES_OPERATOR_UI_LAYOUT_FIX09R1 report="+GeometryReports+"; pixelOrigin=TopLeft; frame="+frame+"; page="+page+"; rootParent="+Root.transform.parent.name+"; rootScale="+Root.transform.localScale+"; nativeTextPixels="+reference+"; titlePixels="+title+"; bodyPixels="+body+"; titleRatio="+(title/Math.Max(.001f,reference))+"; bodyRatio="+(body/Math.Max(.001f,reference))+"; phasePixels="+TextPixels(Phase)+"; feedbackPixels="+TextPixels(Feedback)+"; nativeFontSize="+NativeFontReference.fontSize+"; titleFontSize="+Header.fontSize+"; bodyFontSize="+Detail.fontSize+"; nativeEmPixels="+nativeEm+"; titleEmPixels="+EmPixels(Header)+"; bodyEmPixels="+EmPixels(Detail)+"; titleEmRatio="+(EmPixels(Header)/Math.Max(.001f,nativeEm))+"; bodyEmRatio="+(EmPixels(Detail)/Math.Max(.001f,nativeEm)));
            for(int i=0;i<Cards.Length;i++)Runtime.Log("RHODES_OPERATOR_UI_SLOT_FIX09R1 report="+GeometryReports+"; slot="+i+"; graphic="+ScreenRect(Cards[i].Portrait.worldCorners)+"; hit="+ColliderRect(Cards[i].HitBox)+"; enabled="+Cards[i].HitBox.enabled+"; cooldown="+(Cards[i].Cooldown==null?"none":ScreenRect(Cards[i].Cooldown.worldCorners).ToString()));
            for(int i=0;i<Actions.Length;i++)Runtime.Log("RHODES_OPERATOR_UI_ACTION_FIX09R1 report="+GeometryReports+"; action="+i+"; hit="+ColliderRect(Actions[i].Collider)+"; enabled="+Actions[i].Collider.enabled+"; textPixels="+TextPixels(Actions[i].Label));
            Runtime.Log("RHODES_OPERATOR_UI_AUTO_FIX11 report="+GeometryReports+"; hit="+ColliderRect(AutoAction.Collider)+"; enabled="+AutoAction.Collider.enabled+"; textPixels="+TextPixels(AutoAction.Label)+"; separateFromPassengerActions=True; runtimeVisualAcceptancePending=True");
        }
        void ScheduleAlignment()
        {
            // Other unit selections keep this host enabled. A hidden cargo
            // page needs no delayed layout; Render schedules it on next show.
            if(!Ready||Root==null||!Root.activeInHierarchy||AlignmentPending||!gameObject.activeInHierarchy)return;
            AlignmentPending=true;StartCoroutine(AfterNativeLayout());
        }
        IEnumerator AfterNativeLayout()
        {
            // Native selection handlers and RhodesHangarLayout may reposition
            // their pooled grids on the next frame before this panel aligns.
            yield return null;yield return null;
            AlignmentPending=false;AlignToNativeHangar();
        }
        void OnScreenResize(){if(Ready)ScheduleAlignment();else ScheduleBuild();}
        void OnDisable(){StopAllCoroutines();AlignmentPending=BuildPending=false;}
        GameObject Node(string name,float x,float y)
        {GameObject node=new GameObject(name);node.layer=Root.layer;node.hideFlags=HideFlags.HideAndDontSave;node.transform.parent=Root.transform;node.transform.localScale=Vector3.one;node.transform.localRotation=Quaternion.identity;node.transform.localPosition=new Vector3(x,y,0);return node;}
        UITexture Fill(string name,Color colour,float x,float y,int width,int height,int depth)
        {UITexture fill=Node(name,x,y).AddComponent<UITexture>();fill.mainTexture=Texture2D.whiteTexture;fill.pivot=UIWidget.Pivot.TopLeft;fill.width=width;fill.height=height;fill.color=colour;fill.depth=depth;fill.transform.localPosition=new Vector3(x,y,0);return fill;}
        UILabel Label(string name,UILabel reference,float x,float y,int width,int height,int size,Color colour,int depth,NGUIText.Alignment align)
        {
            UILabel label=Node(name,x,y).AddComponent<UILabel>();if(reference.bitmapFont!=null)label.bitmapFont=reference.bitmapFont;else label.trueTypeFont=reference.trueTypeFont;
            label.fontSize=size;label.fontStyle=FontStyle.Normal;label.alignment=align;label.pivot=UIWidget.Pivot.TopLeft;label.width=width;label.height=height;
            label.overflowMethod=UILabel.Overflow.ClampContent;label.maxLineCount=1;label.supportEncoding=false;label.applyGradient=false;label.effectStyle=UILabel.Effect.None;label.spacingX=0;label.spacingY=0;label.color=colour;label.depth=depth;label.transform.localPosition=new Vector3(x,y,0);return label;
        }
        ActionWidgets Button(string text,ActionKind kind,UILabel reference,float x,float y,int width,int depth)
        {
            ActionWidgets button=new ActionWidgets();button.Fill=Fill("EF_CargoAction_"+kind,ButtonFill,x,y,width,13,depth);
            GameObject node=button.Fill.gameObject;button.Collider=node.AddComponent<BoxCollider>();button.Collider.center=new Vector3(width*.5f,-6.5f,0);button.Collider.size=new Vector3(width,13,1);button.Collider.isTrigger=true;
            NGUIEventHandler input=node.AddComponent<NGUIEventHandler>();input.Data=kind;input.ClickAction=OnAction;
            SimpleTooltip tooltip=node.AddComponent<SimpleTooltip>();NativeRead.Set(tooltip,"m_Anchor",node.transform);
            NativeRead.Set(tooltip,"m_TitleStringId",ActionTitle(kind));NativeRead.Set(tooltip,"m_ShortDescriptionStringId",ActionDescription(kind));NativeRead.Set(tooltip,"m_LongDescriptionStringId",string.Empty);
            button.Label=Label("EF_CargoActionLabel_"+kind,reference,x,y-2,width,10,7,White,depth+1,NGUIText.Alignment.Center);button.Label.text=text;
            input.HoverAction=delegate(NGUIEventHandler sender,bool hover){if(button.Enabled)button.Fill.color=hover?new Color(.28f,.29f,.22f,.98f):ButtonFill;};return button;
        }
        static string ActionTitle(ActionKind kind)
        {return kind==ActionKind.Deploy?"部署全部干员":kind==ActionKind.Recall?"召回全部干员":kind==ActionKind.Select?"选中当前干员":"切换干员自动施法";}
        static string ActionDescription(ActionKind kind)
        {
            if(kind==ActionKind.Deploy)return "部署本机全部整备就绪的干员。刚回舱的干员仍需完成原生整备，才能再次离机；整备期间可正常使用机载技能。";
            if(kind==ActionKind.Recall)return "召回本机派出的全部干员，旋翼机将主动接回。回舱后进行原生回血和补弹整备；机载技能只受其自身冷却与施法条件限制。";
            if(kind==ActionKind.Select)return "选中当前舱位已部署在地面的干员。";
            return "切换当前干员的自动机载施法。需要先进源石抑制剂科技；完整回舱后可在整备期间使用，技能冷却与原干员技能保持同步。";
        }
        static string Identity(RhodesOperatorSlotStatus slot){return slot.Occupied?OperatorPortraitIcons.DisplayName(slot.TypeKey):"空舱";}
        static string StateName(RhodesOperatorSlotStatus slot)
        {if(slot.Producing)return "编入中";if(!slot.Occupied)return "—";if(slot.Transferring)return "转运中";if(slot.Deployed)return "已部署";if(slot.Cooling)return "整备"+Mathf.CeilToInt(slot.CooldownRemainingSeconds)+"秒";if(slot.Ready)return "就绪";return "已预留";}
        static string SkillHotkey(string type)
        {return type==HumanoidUnits.LaevatainKey?"Q":type==HumanoidUnits.GilbertaKey?"E":type==HumanoidUnits.ArdeliaKey?"T":null;}
        internal static string AutocastAbilityKey(string type)
        {return type==HumanoidUnits.LaevatainKey?RhodesEmbarkedOperatorSkills.AutoToggleAbilityKey(0):type==HumanoidUnits.GilbertaKey?RhodesEmbarkedOperatorSkills.AutoToggleAbilityKey(1):type==HumanoidUnits.ArdeliaKey?RhodesEmbarkedOperatorSkills.AutoToggleAbilityKey(2):null;}
        internal static bool CanToggleAutocast(RhodesOperatorSlotStatus slot,bool known,RhodesEmbarkedSkillStatus skill)
        {return slot.Occupied&&(slot.Ready||slot.Cooling)&&!slot.Producing&&!slot.Deployed&&!slot.Transferring&&known&&skill.AutoResearched&&AutocastAbilityKey(slot.TypeKey)!=null&&AutocastBlockedSkillReason(skill.Reason)==null;}
        static string AutocastBlockedSkillReason(string reason)
        {return reason=="起降或返航中"||reason=="接驳中"||reason=="归属变更中"||reason=="整备中"||reason=="未搭载"||reason=="状态同步中"?reason:null;}
        internal static string AutocastLabel(bool known,RhodesEmbarkedSkillStatus skill,int seconds)
        {
            if(!known)return "自动：同步中";
            if(!skill.AutoResearched)return "自动：锁定";
            return "自动 "+(skill.AutoEnabled?"开":"关")+" · "+(seconds>0?"冷却 "+seconds+"秒":skill.Ready?"就绪":skill.Reason??"未就绪");
        }
        internal static string SkillStatusLabel(string hotkey,bool known,RhodesEmbarkedSkillStatus skill,int seconds)
        {return hotkey+" "+(!known?"技能同步中":seconds>0?"技能冷却 "+seconds+"秒":skill.Ready?"技能就绪":skill.Reason??"技能未就绪");}
        internal static string AutocastUnavailableReason(RhodesOperatorSlotStatus slot,bool known,RhodesEmbarkedSkillStatus skill)
        {
            if(!known)return "技能状态同步中";
            if(!skill.AutoResearched)return "需研发先进源石抑制剂，解锁机载自动施法";
            if(slot.Deployed)return "干员在地面；回舱后可切换自动施法";
            if(slot.Transferring)return "干员转运中；完成接驳后可切换";
            if(slot.Producing)return "干员编入中";
            if(!slot.Cooling&&!slot.Ready)return "干员尚未完整回舱";
            return AutocastBlockedSkillReason(skill.Reason);
        }
        void OnSelection(IList<Entity> entities,SimStateFrame frame)
        {
            Entity selected=Interface.LeadUnit;RhodesOperatorCargoStatus ignored;
            if(!RhodesOperatorTransport.TryGetStatus(selected,out ignored)&&entities!=null)foreach(Entity entity in entities)if(RhodesOperatorTransport.TryGetStatus(entity,out ignored)){selected=entity;break;}
            SelectCarrier(selected);
        }
        void OnLead(Entity entity){SelectCarrier(entity);}
        void SelectCarrier(Entity entity)
        {if(Selected!=entity){Selected=entity;ActiveSlot=0;LocalFeedback=null;LastManifestRevision=-1;}Dirty=true;if(!Ready){ScheduleBuild();return;}Render();ScheduleAlignment();}
        void OnFrame(SimStateFrame frame){Render();}
        void OnEnable(){if(Ready){Dirty=true;Render();ScheduleAlignment();}else ScheduleBuild();}
        void OnSlotClicked(NGUIEventHandler sender)
        {
            if(sender==null||!(sender.Data is int))return;int index=(int)sender.Data;if(index<0||index>=Slots.Length||!Slots[index].Occupied)return;
            ActiveSlot=index;LocalFeedback=Identity(Slots[index])+" / "+StateName(Slots[index]);Dirty=true;Render();
        }
        void OnSlotDoubleClicked(NGUIEventHandler sender)
        {
            if(sender==null||!(sender.Data is int))return;int index=(int)sender.Data;
            if(index<0||index>=Slots.Length||!Slots[index].Occupied||!Slots[index].Deployed)return;
            OnSlotClicked(sender);SelectPassenger(Slots[index].Passenger);
        }
        void SelectPassenger(Entity passenger)
        {if(passenger!=Entity.None)NativeRead.Call(Interface,"SendUnitSelectionCommand",passenger,SelectedUnitManager.AddMode.NewSet,false);}
        void OnAction(NGUIEventHandler sender)
        {
            if(sender==null||!(sender.Data is ActionKind)||ActiveSlot<0||ActiveSlot>=Slots.Length)return;
            ActionKind kind=(ActionKind)sender.Data;RhodesOperatorSlotStatus slot=Slots[ActiveSlot];bool accepted=false;
            if(kind==ActionKind.Select){if(slot.Deployed)SelectPassenger(slot.Passenger);return;}
            if(kind==ActionKind.Auto)
            {
                RhodesEmbarkedSkillStatus status;bool known=RhodesEmbarkedOperatorSkills.TryGetStatus(Selected,slot.TypeKey,out status);
                if(!CanToggleAutocast(slot,known,status)){LocalFeedback=AutocastUnavailableReason(slot,known,status);Dirty=true;Render();return;}
                accepted=NativeUnitBindings.ScheduleOperatorAbility(Interface,Selected,AutocastAbilityKey(slot.TypeKey));
                // The next immutable simulation snapshot owns the displayed
                // switch value. A successful queue is not an optimistic toggle.
                LocalFeedback=accepted?"已下达自动施法切换命令":"暂时无法切换自动施法，请重试";Dirty=true;Render();return;
            }
            if(kind==ActionKind.Deploy)accepted=NativeUnitBindings.ScheduleOperatorAbility(Interface,Selected,RhodesOperatorTransport.UnloadAbilityKey);
            else if(kind==ActionKind.Recall)accepted=NativeUnitBindings.ScheduleOperatorAbility(Interface,Selected,RhodesOperatorTransport.RecallAbilityKey);else return;
            LocalFeedback=accepted?kind==ActionKind.Deploy?"已下达全部部署命令":"已下达全部召回命令":"暂时无法下达命令，请重试";Dirty=true;Render();
        }
        void Render()
        {
            if(!Ready||Root==null)return;RhodesOperatorCargoStatus cargo;RhodesOperatorManifest manifest;
            if(!RhodesOperatorTransport.TryGetStatus(Selected,out cargo)||!RhodesOperatorTransport.TryGetManifest(Selected,out manifest))
            {if(Root.activeSelf)Root.SetActive(false);LastCargoRevision=-1;LastManifestRevision=-1;return;}
            RhodesTiltrotorFlightStatus flight;bool haveFlight=RhodesTiltrotorFlightControl.TryGetFlightStatus(Selected,out flight);
            string phase=haveFlight?flight.PhaseLabel:cargo.LandingReady?"空中接驳就绪":"待命";if(cargo.LandingReady&&haveFlight&&!flight.HasVisualSample)phase="可部署干员";
            if(LastManifestRevision!=manifest.Revision)Slots=manifest.Slots;
            if(ActiveSlot<0||ActiveSlot>=Slots.Length||!Slots[ActiveSlot].Occupied)for(int i=0;i<Slots.Length;i++)if(Slots[i].Occupied){ActiveSlot=i;break;}
            RhodesOperatorSlotStatus current=Slots[Mathf.Clamp(ActiveSlot,0,Slots.Length-1)];
            string skillHotkey=SkillHotkey(current.TypeKey);RhodesEmbarkedSkillStatus skill=new RhodesEmbarkedSkillStatus();
            bool skillKnown=current.Occupied&&skillHotkey!=null&&RhodesEmbarkedOperatorSkills.TryGetStatus(Selected,current.TypeKey,out skill);
            int skillSeconds=skillKnown&&!float.IsNaN(skill.RemainingSeconds)&&!float.IsInfinity(skill.RemainingSeconds)?Mathf.Max(0,Mathf.CeilToInt(skill.RemainingSeconds)):0;
            bool skillReady=skillKnown&&skill.Ready;
            if(Root.activeSelf&&!Dirty&&LastCargoRevision==cargo.Revision&&LastManifestRevision==manifest.Revision&&LastPhase==phase&&
                LastSkillType==current.TypeKey&&LastSkillKnown==skillKnown&&LastSkillReady==skillReady&&LastSkillSeconds==skillSeconds&&
                LastAutoResearched==skill.AutoResearched&&LastAutoEnabled==skill.AutoEnabled&&LastSkillReason==skill.Reason)return;
            if((LastCargoRevision>=0&&LastCargoRevision!=cargo.Revision)||(LastSkillKnown&&skillKnown&&LastAutoEnabled!=skill.AutoEnabled))LocalFeedback=null;
            LastCargoRevision=cargo.Revision;LastManifestRevision=manifest.Revision;LastPhase=phase;Dirty=false;
            LastSkillType=current.TypeKey;LastSkillKnown=skillKnown;LastSkillReady=skillReady;LastSkillSeconds=skillSeconds;
            LastAutoResearched=skill.AutoResearched;LastAutoEnabled=skill.AutoEnabled;LastSkillReason=skill.Reason;
            Header.text="干员舱位  "+cargo.Occupied+" / "+RhodesOperatorTransport.Capacity;
            Phase.text=current.Occupied&&skillHotkey!=null?SkillStatusLabel(skillHotkey,skillKnown,skill,skillSeconds):phase;
            for(int i=0;i<Cards.Length;i++)
            {
                SlotWidgets card=Cards[i];RhodesOperatorSlotStatus slot=Slots[i];
                // Native dead-state tint is already baked into the RGBA.
                // Keep occupied portraits white; state dimming belongs to
                // the text and frame, not a second multiplication of the face.
                Color portraitColour=Color.white;
                string portraitName=OperatorPortraitIcons.CargoSpriteName(slot.TypeKey);bool hasPortrait=slot.Occupied&&!string.IsNullOrEmpty(portraitName);
                if(hasPortrait&&card.PortraitName!=portraitName)
                SetPortrait(card,portraitName);
                card.Icon.SetColour("Icon",portraitColour);card.Icon.SetEnabled("Button",false);card.Icon.SetShowing("Icon",hasPortrait);card.HitBox.enabled=slot.Occupied;card.Empty.gameObject.SetActive(!slot.Occupied);
                card.Icon.SetValueFloat("Cooldown",slot.Cooling?Mathf.Clamp01(slot.CooldownProgress):1f);card.Icon.SetShowing("Cooldown",slot.Cooling);
                card.Portrait.color=portraitColour;card.Portrait.gameObject.SetActive(hasPortrait);foreach(BoxCollider collider in card.Colliders)collider.enabled=false;
                card.Selected.color=i==ActiveSlot&&slot.Occupied?Yellow:new Color(.5f,.52f,.5f,.15f);
            }
            Detail.text=current.Occupied?Identity(current)+" "+StateName(current):"选择干员";Detail.color=current.Occupied?White:Dim;
            SetButton(Actions[0],cargo.Ready>0);SetButton(Actions[1],cargo.Deployed>0);SetButton(Actions[2],current.Deployed);
            bool showAuto=current.Occupied&&skillHotkey!=null;
            AutoAction.Fill.gameObject.SetActive(showAuto);AutoAction.Label.gameObject.SetActive(showAuto);
            AutoAction.Label.text=AutocastLabel(skillKnown,skill,skillSeconds);SetButton(AutoAction,showAuto&&CanToggleAutocast(current,skillKnown,skill));
            string autoReason=showAuto?AutocastUnavailableReason(current,skillKnown,skill):null;
            if(autoReason!=null){Feedback.text=autoReason;Feedback.color=Yellow;}
            else if(!cargo.LandingReady&&(cargo.Ready>0||cargo.Deployed>0)&&LocalFeedback==null)
            {Feedback.text=cargo.Deployed>0?"召回时旋翼机主动接回干员":"部署时自动悬停；下方需有安全地面";Feedback.color=Yellow;}
            else{Feedback.text=LocalFeedback??cargo.LastAction;Feedback.color=LocalFeedback==null&&cargo.Warning?Yellow:White;}
            if(!Root.activeSelf){Root.SetActive(true);ScheduleAlignment();}
        }
        static void SetButton(ActionWidgets button,bool enabled)
        {button.Enabled=enabled;button.Collider.enabled=enabled;button.Label.color=enabled?White:Dim;button.Fill.color=enabled?ButtonFill:new Color(.11f,.12f,.12f,.8f);}
        void OnDestroy()
        {Cleanup();}
        void Cleanup()
        {
            UICamera.onScreenResize-=OnScreenResize;StopAllCoroutines();AlignmentPending=BuildPending=false;
            if(Interface!=null){Interface.NewSelection-=OnSelection;Interface.NewLeadUnitSelected-=OnLead;Interface.NewFrameFromSim-=OnFrame;}
            if(NativeHangarController!=null)NativeHangarController.UnitHangarPanelVisibleChanged-=OnNativeHangarVisible;
            EventsBound=false;DestroyWidgets();
        }
        void DestroyWidgets()
        {
            foreach(SlotWidgets card in Cards)if(card!=null&&card.Icon!=null)
            {if(card.HitInput!=null){card.HitInput.ClickAction=null;card.HitInput.DoubleClickAction=null;}card.Icon.ClickAction=null;card.Icon.DoubleClickAction=null;try{NativeRead.Call(card.Icon,"OnDespawned",HudDependencies);}catch(Exception){}UnityEngine.Object.Destroy(card.Icon.gameObject);card.Icon=null;}
            if(Root!=null)UnityEngine.Object.Destroy(Root);Root=null;Ready=false;
        }
    }
}
