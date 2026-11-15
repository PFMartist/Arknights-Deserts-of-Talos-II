using System;
using BBI.Core.ComponentModel;
using BBI.Unity.Game.HUD;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // One stable, presentation-only target per private U08 instance. The native
    // camera and icon controllers retain projection, focus and input ownership.
    // This component adds no Update loop, registry or scene discovery.
    public sealed class RhodesTiltrotorPresentationAnchor:MonoBehaviour
    {
        UnitView View;Entity Entity=Entity.None;
        Transform Body,OriginalCamera,OriginalIconTrack;
        NGUIIconController Icon;
        bool Ready;

        internal static RhodesTiltrotorPresentationAnchor Ensure(UnitView view,FactionModelBinding binding)
        {
            if(view==null||binding==null||binding.UnitKey!=FactionUnits.TiltStrikeKey||binding.GeometryRoot==null)return null;
            RhodesTiltrotorPresentationAnchor anchor=view.GetComponent<RhodesTiltrotorPresentationAnchor>();
            if(anchor==null)anchor=view.gameObject.AddComponent<RhodesTiltrotorPresentationAnchor>();
            anchor.Bind(view,binding);return anchor;
        }
        void Bind(UnitView view,FactionModelBinding binding)
        {
            if(Ready&&View==view&&Entity.Equals(view.EntityID))return;
            Release();View=view;Entity=view.EntityID;
            if(Body==null)
            {
                GameObject node=new GameObject("EF_U08_PresentationAnchor");node.layer=view.gameObject.layer;
                node.hideFlags=HideFlags.HideAndDontSave;UnityEngine.Object.DontDestroyOnLoad(node);
                // A root child inherits the native hover pose before our late
                // geometry correction. Camera LateUpdate could then sample an
                // intermediate position. Keep a stable world-space snapshot;
                // the existing body driver remains its only position writer.
                Body=node.transform;Body.localRotation=Quaternion.identity;Body.localScale=Vector3.one;
            }
            OriginalCamera=view.CameraTarget;
            Body.position=binding.GeometryRoot.TransformPoint(binding.ModelBounds.center);
            Body.gameObject.SetActive(true);
            NativeRead.Set(view,"CameraTarget",Body);Ready=true;
        }
        internal void UpdateBody(FactionModelBinding binding)
        {
            if(!Ready||View==null||Body==null||!Entity.Equals(View.EntityID)||binding==null||binding.GeometryRoot==null)return;
            Body.position=binding.GeometryRoot.TransformPoint(binding.ModelBounds.center);
        }
        void ForgetIcon()
        {
            // Native icons are pooled. A recycled icon belonging to another
            // view must never be changed by its previous view's cleanup.
            if(Icon!=null&&object.ReferenceEquals(Icon.Data,View)&&Icon.ObjectToTrack==Body)Icon.ObjectToTrack=OriginalIconTrack;
            Icon=null;OriginalIconTrack=null;
        }
        void BindIcon(NGUIIconController icon)
        {
            if(icon==null||!object.ReferenceEquals(icon.Data,View))return;
            if(Icon!=icon){ForgetIcon();Icon=icon;OriginalIconTrack=icon.ObjectToTrack;}
            icon.ObjectToTrack=Body;
        }
        public static void IconSpawned(object controller)
        {
            try
            {
                if(controller==null)return;
                UnitView view=NativeRead.Get(controller,"mAssociatedView") as UnitView;
                NGUIIconController icon=NativeRead.Get(controller,"mIconController") as NGUIIconController;
                if(view==null||icon==null||!object.ReferenceEquals(icon.Data,view)||view.EntityID.Equals(Entity.None)||
                    view.NonInterpolatedTickState==null||view.NonInterpolatedTickState.UnitIsDead||
                    view.NonInterpolatedTickState.TypeID!=FactionUnits.TiltStrikeKey)return;
                RhodesTiltrotorPresentationAnchor anchor=Ensure(view,view.GetComponent<FactionModelBinding>());
                if(anchor!=null)anchor.BindIcon(icon);
            }
            catch(Exception error){Runtime.Log("RHODES_U08_ANCHOR_WARNING "+error.GetType().Name+": "+error.Message+"; nativeIconFallback=True");}
        }
        internal void Release()
        {
            ForgetIcon();
            if(Ready&&View!=null&&View.CameraTarget==Body)NativeRead.Set(View,"CameraTarget",OriginalCamera);
            // This node is independent of the view hierarchy. Preserve the
            // native camera's inactive-target test when the view is pooled.
            if(Body!=null)Body.gameObject.SetActive(false);
            Ready=false;OriginalCamera=null;
        }
        void OnDisable(){Release();}
        void OnDestroy(){Release();if(Body!=null)UnityEngine.Object.Destroy(Body.gameObject);View=null;Body=null;Entity=Entity.None;}
    }
}
