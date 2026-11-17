using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object left,Object right)
        {
            bool leftNull=object.ReferenceEquals(left,null)||left.Destroyed;
            bool rightNull=object.ReferenceEquals(right,null)||right.Destroyed;
            return leftNull||rightNull?leftNull==rightNull:object.ReferenceEquals(left,right);
        }
        public static bool operator !=(Object left,Object right){return !(left==right);}
        public override bool Equals(object other){return object.ReferenceEquals(this,other);}
        public override int GetHashCode(){return base.GetHashCode();}
    }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 right {get{return new Vector3(1,0,0);}}
        public static Vector3 up {get{return new Vector3(0,1,0);}}
        public float magnitude {get{return (float)Math.Sqrt(x*x+y*y+z*z);}}
        public static Vector3 operator +(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
        public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
        public static Vector3 operator *(Vector3 a,float value){return new Vector3(a.x*value,a.y*value,a.z*value);}
    }
    public struct Bounds {public Vector3 min;}
    public static class Mathf
    {
        public static float Min(float a,float b){return Math.Min(a,b);}
        public static float Max(float a,float b){return Math.Max(a,b);}
        public static float Abs(float value){return Math.Abs(value);}
    }
    public class Component:Object
    {
        public GameObject gameObject;
        public Transform transform {get{return gameObject.transform;}}
        public T GetComponent<T>() where T:Component {return gameObject.GetComponent<T>();}
    }
    public class GameObject:Object
    {
        public bool activeInHierarchy=true;
        public bool activeSelf=true;
        public readonly Transform transform;
        readonly List<Component> Components=new List<Component>();
        public GameObject(){transform=new Transform();transform.gameObject=this;Components.Add(transform);}
        public T AddComponent<T>() where T:Component,new(){T value=new T();value.gameObject=this;Components.Add(value);return value;}
        public T GetComponent<T>() where T:Component
        {foreach(Component component in Components)if(component is T&&!component.Destroyed)return (T)component;return null;}
    }
    public class Transform:Component,IEnumerable
    {
        public Transform parent;
        public Vector3 localPosition;
        public Vector3 localScale=new Vector3(1,1,1);
        public readonly List<Transform> Children=new List<Transform>();
        public Vector3 position
        {
            get{return parent==null?localPosition:parent.TransformPoint(localPosition);}
            set{localPosition=parent==null?value:parent.InverseTransformPoint(value);}
        }
        public Vector3 TransformVector(Vector3 value)
        {
            Vector3 own=new Vector3(value.x*localScale.x,value.y*localScale.y,value.z*localScale.z);
            return parent==null?own:parent.TransformVector(own);
        }
        public Vector3 InverseTransformVector(Vector3 value)
        {
            Vector3 own=parent==null?value:parent.InverseTransformVector(value);
            return new Vector3(own.x/localScale.x,own.y/localScale.y,own.z/localScale.z);
        }
        public Vector3 TransformPoint(Vector3 value){return position+TransformVector(value);}
        public Vector3 InverseTransformPoint(Vector3 value){return InverseTransformVector(value-position);}
        public IEnumerator GetEnumerator(){return Children.GetEnumerator();}
        public void Attach(Transform child){child.parent=this;Children.Add(child);}
    }
    public class MonoBehaviour:Component
    {
        readonly List<IEnumerator> Routines=new List<IEnumerator>();
        public int CoroutineStarts,CoroutineStops;
        protected void StartCoroutine(IEnumerator routine){CoroutineStarts++;Routines.Add(routine);}
        protected void StopAllCoroutines(){CoroutineStops++;Routines.Clear();}
        public int PendingCoroutines {get{return Routines.Count;}}
        public void Tick()
        {for(int i=Routines.Count-1;i>=0;i--)if(!Routines[i].MoveNext())Routines.RemoveAt(i);}
    }
}
public class UIWidget:UnityEngine.Component {public enum Pivot {TopLeft,Center}}
public class UISprite:UIWidget {public int width=200,height=200;}
public class UIGrid:UnityEngine.Component
{
    public enum Arrangement {Horizontal,Vertical}
    public Arrangement arrangement=Arrangement.Vertical;
    public UIWidget.Pivot pivot=UIWidget.Pivot.Center;
    public int maxPerLine=1,Repositions;
    public float cellWidth=20,cellHeight=20;
    public void Reposition(){Repositions++;}
}
public static class NGUIMath
{public static UnityEngine.Bounds CalculateRelativeWidgetBounds(UnityEngine.Transform root,UnityEngine.Transform child,bool active){return new UnityEngine.Bounds();}}
namespace BBI.Core.ComponentModel {public struct Entity {}}
namespace BBI.Game.Commands {public class SimStateFrame {}}
namespace BBI.Unity.Game.HUD
{
    public class NGUIIconController:UnityEngine.Component
    {public object Data;public object[] m_IconObjects=new object[0];}
}
namespace BBI.Unity.Game.UI
{
    public class UnitInterfaceController:UnityEngine.Component
    {
        public object mCommandPanelSettings;
        public event Action<IList<BBI.Core.ComponentModel.Entity>,BBI.Game.Commands.SimStateFrame> NewSelection;
        public event Action<BBI.Core.ComponentModel.Entity> NewLeadUnitSelected;
        public int SelectionListeners {get{return NewSelection==null?0:NewSelection.GetInvocationList().Length;}}
        public int LeadListeners {get{return NewLeadUnitSelected==null?0:NewLeadUnitSelected.GetInvocationList().Length;}}
        public void ResetSessionEvents(){NewSelection=null;NewLeadUnitSelected=null;}
        public void Select(){if(NewSelection!=null)NewSelection(new List<BBI.Core.ComponentModel.Entity>(),new BBI.Game.Commands.SimStateFrame());}
        public void Lead(){if(NewLeadUnitSelected!=null)NewLeadUnitSelected(new BBI.Core.ComponentModel.Entity());}
    }
    public class UnitHangarPanelController
    {
        public object mInterfaceController,mSettings;
        public Dictionary<string,List<BBI.Unity.Game.HUD.NGUIIconController>> mHangarSlotIcons;
        public event Action<bool> UnitHangarPanelVisibleChanged;
        public int VisibleListeners {get{return UnitHangarPanelVisibleChanged==null?0:UnitHangarPanelVisibleChanged.GetInvocationList().Length;}}
        public void Visible(){if(UnitHangarPanelVisibleChanged!=null)UnitHangarPanelVisibleChanged(true);}
    }
}
namespace EndfieldDoK.MapUnitLab
{
    public static class NativeRead
    {
        public static object Get(object target,string name)
        {
            if(target==null)return null;
            for(Type type=target.GetType();type!=null;type=type.BaseType)
            {
                FieldInfo field=type.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);
                if(field!=null)return field.GetValue(target);
            }
            throw new MissingFieldException(target.GetType().FullName,name);
        }
    }
    public static class Runtime {public static void Log(string text){}}
    public static class RhodesHangarStateColours {public static void ObserveNative(object controller){}}
    public static class RhodesCarrierUnits
    {
        public const string AircraftKey="fighter",HeavyBomberKey="bomber";
        public const string AircraftLaunchKey="launch_fighter",TiltLaunchKey="launch_rotor",HeavyBomberLaunchKey="launch_bomber";
    }
    public static class FactionUnits {public const string TiltStrikeKey="rotor";}
}
