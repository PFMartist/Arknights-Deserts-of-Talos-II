using System;
using System.Collections.Generic;
using System.Reflection;
namespace UnityEngine
{
    public class GameObject {}
    public struct Color {public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}}
    public struct Color32 {public byte r,g,b,a;}
    public class Component
    {
        public Transform transform;
        public T GetComponent<T>() where T:class{return transform==null?null:transform.FindComponent<T>();}
    }
    public class Transform:Component
    {
        public Transform parent;readonly List<Component> Components=new List<Component>();
        public Transform(){transform=this;}
        public bool IsChildOf(Transform root){for(Transform node=this;node!=null;node=node.parent)if(object.ReferenceEquals(node,root))return true;return false;}
        public void Attach(Component component){component.transform=this;Components.Add(component);}
        public T FindComponent<T>() where T:class{foreach(Component c in Components)if(c is T)return c as T;return null;}
    }
}
public class UIWidget:UnityEngine.Component {}
public class UISprite:UIWidget {}
public class UIProgressBar:UnityEngine.Component {public UIWidget foregroundWidget;}
namespace BBI.Unity.Game.Data
{
    public class UnitHUDInteractionAttributes
    {public class UnitHangarStateColours {} public UnitHangarStateColours UnitHangarIconColours;}
}
namespace BBI.Unity.Game.HUD
{
    public class NGUIIconController:UnityEngine.Component
    {
        public sealed class VisualizationContainer
        {
            public UnityEngine.Color mInitialColour,mInitialBackgroundColour,mOverridenColour,mOverridenBackgroundColour;
            public object mIconAtlas;
            public UISprite m_Sprite;
            public UIWidget m_Label,m_Button;
            public UIProgressBar m_ProgressBar;
        }
        public object Data;
        public object[] m_IconObjects=new object[0];
        public Dictionary<string,object> mVisualizationVariables=new Dictionary<string,object>();
        public NGUIIconController(){new UnityEngine.Transform().Attach(this);}
    }
}
namespace BBI.Unity.Game.UI
{
    public class UnitHangarPanelController:UnityEngine.Component
    {sealed class HangarSlotObjectData {}}
}
namespace EndfieldDoK.MapUnitLab
{
    public static class NativeRead
    {
        public static int Reads;
        public static bool TryGet(object target,string name,out object value)
        {
            Reads++;value=null;if(target==null)return false;
            for(Type type=target.GetType();type!=null;type=type.BaseType)
            {
                FieldInfo field=type.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
                if(field!=null){value=field.GetValue(target);return true;}
                PropertyInfo property=type.GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
                if(property!=null){value=property.GetValue(target,null);return true;}
            }
            return false;
        }
    }
    public static class Runtime {public static void Log(string text){}}
}
