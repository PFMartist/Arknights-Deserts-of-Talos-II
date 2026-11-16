using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.HUD;
using BBI.Unity.Game.UI;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // The native controller assigns this exact private Data type BEFORE its
    // first state colour. No RGB, object name, or command-area ancestry grants
    // an exception. Container ownership is checked again on every use so pool
    // reuse and serialized container clones cannot inherit a slot permission.
    internal static class RhodesHangarStateColours
    {
        static readonly Type SlotData=typeof(UnitHangarPanelController).GetNestedType("HangarSlotObjectData",BindingFlags.NonPublic);
        static readonly Dictionary<object,WeakReference> Owners=new Dictionary<object,WeakReference>(new IdentityComparer());
        sealed class IdentityComparer:IEqualityComparer<object>
        {
            public new bool Equals(object left,object right){return object.ReferenceEquals(left,right);}
            public int GetHashCode(object value){return RuntimeHelpers.GetHashCode(value);}
        }
        static object Read(object target,string name)
        {object result;return target!=null&&NativeRead.TryGet(target,name,out result)?result:null;}
        static bool IsSlot(NGUIIconController icon)
        {return !object.ReferenceEquals(icon,null)&&icon.Data!=null&&SlotData!=null&&icon.Data.GetType()==SlotData;}
        static IEnumerable Containers(NGUIIconController icon)
        {
            IEnumerable objects=Read(icon,"m_IconObjects") as IEnumerable;
            if(objects!=null)foreach(object item in objects)yield return item;
            IDictionary variables=Read(icon,"mVisualizationVariables") as IDictionary;
            if(variables!=null)foreach(object item in variables.Values)yield return item;
        }
        static bool Contains(NGUIIconController icon,object container)
        {
            foreach(object item in Containers(icon))if(object.ReferenceEquals(item,container))return true;
            return false;
        }
        internal static void RegisterIcon(object value)
        {
            NGUIIconController icon=value as NGUIIconController;if(object.ReferenceEquals(icon,null))return;
            WeakReference owner=null;
            foreach(object item in Containers(icon))if(item!=null)
            {
                WeakReference existing;
                if(Owners.TryGetValue(item,out existing)&&object.ReferenceEquals(existing.Target,icon))continue;
                if(owner==null)owner=new WeakReference(icon);
                Owners[item]=owner;
            }
        }
        internal static void ClearContexts(){Owners.Clear();}
        static NGUIIconController Owner(object container)
        {
            if(container==null)return null;
            WeakReference cached;NGUIIconController icon;
            if(Owners.TryGetValue(container,out cached))
            {
                icon=cached.Target as NGUIIconController;
                if(!object.ReferenceEquals(icon,null)&&Contains(icon,container))return icon;
                Owners.Remove(container);
            }
            // Direct container.Reset calls need the same owner as SetColour.
            // Follow only its explicit visual references to the nearest icon,
            // then require exact native container membership. No scene search.
            foreach(string field in new string[]{"m_Sprite","m_Label","m_Button","m_ProgressBar"})
            {
                Component widget=Read(container,field) as Component;if(object.ReferenceEquals(widget,null))continue;
                for(Transform node=widget.transform;node!=null;node=node.parent)
                {
                    icon=node.GetComponent<NGUIIconController>();if(icon==null)continue;
                    if(!Contains(icon,container))break;
                    Owners[container]=new WeakReference(icon);return icon;
                }
            }
            return null;
        }
        internal static bool IsForeground(object value,string variable)
        {
            return variable=="Icon"&&IsSlot(value as NGUIIconController);
        }
        internal static bool IsForegroundContainer(object container)
        {return (Read(container,"m_Name") as string)=="Icon"&&IsSlot(Owner(container));}
        internal static bool IsForegroundField(object target,string name)
        {return (name=="mInitialColour"||name=="mOverridenColour")&&IsForegroundContainer(target);}
        internal static bool IsForegroundVisual(Component visual)
        {
            if(object.ReferenceEquals(visual,null))return false;
            for(Transform node=visual.transform;node!=null;node=node.parent)
            {
                NGUIIconController icon=node.GetComponent<NGUIIconController>();if(icon==null)continue;
                if(!IsSlot(icon))return false;
                RegisterIcon(icon);
                foreach(object container in Containers(icon))
                {
                    if((Read(container,"m_Name") as string)!="Icon")continue;
                    foreach(string field in new string[]{"m_Sprite","m_Label","m_Button"})
                        if(object.ReferenceEquals(Read(container,field),visual))return true;
                    UIProgressBar progress=Read(container,"m_ProgressBar") as UIProgressBar;
                    if(progress!=null&&(object.ReferenceEquals(progress,visual)||object.ReferenceEquals(progress.foregroundWidget,visual)))return true;
                }
                return false;
            }
            return false;
        }
        internal static bool IsNativePalette(object value)
        {return value is UnitHUDInteractionAttributes.UnitHangarStateColours;}
        internal static void ObserveNative(object controller)
        {
            // The five-state source remains native. The legend's colours are
            // NGUI rich-text tags in the native localized tooltip, not Color
            // fields; normal text and atlas handling retain them unchanged.
            UnitHUDInteractionAttributes attributes=Read(controller,"mUnitHUDInterfaceAttributes") as UnitHUDInteractionAttributes;
            if(attributes==null||attributes.UnitHangarIconColours==null)return;
            Runtime.Log("RHODES_HANGAR_STATE_COLOURS_FIX14 scope=NativeHangarSlotObjectData.IconForeground; numericExceptions=False; nativeLegendRichTextUnchanged=True; backgroundAndGlobalTheme=True; perFrameWork=False; runtimeVisualAcceptancePending=True");
        }
    }
}
