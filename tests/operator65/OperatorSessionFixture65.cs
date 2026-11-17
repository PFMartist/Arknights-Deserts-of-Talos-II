using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public static void Destroy(GameObject target){target.Destroyed=true;}
    }
}

public sealed class GameObject:UnityEngine.Object
{
    public bool activeInHierarchy=true,activeSelf=true,Destroyed;
    public readonly Dictionary<Type,object> Components=new Dictionary<Type,object>();
    public T GetComponent<T>() where T:class
    {object value;return Components.TryGetValue(typeof(T),out value)?value as T:null;}
    public T AddComponent<T>() where T:MonoBehaviour,new()
    {T component=new T();component.gameObject=this;Components.Add(typeof(T),component);return component;}
    public void SetActive(bool active){activeSelf=active;activeInHierarchy=active;}
}

public class MonoBehaviour
{
    public GameObject gameObject=new GameObject();
    public int CoroutineStarts,CoroutineStops;
    readonly List<IEnumerator> Pending=new List<IEnumerator>();
    public int PendingCount{get{return Pending.Count;}}
    protected void StartCoroutine(IEnumerator routine){CoroutineStarts++;Pending.Add(routine);}
    protected void StopAllCoroutines(){CoroutineStops++;Pending.Clear();}
    public void Tick()
    {for(int i=Pending.Count-1;i>=0;i--)if(!Pending[i].MoveNext())Pending.RemoveAt(i);}
}

public sealed class UnitInterfaceController:MonoBehaviour
{
    public event Action NewSelection,NewLeadUnitSelected,NewFrameFromSim;
    public T GetComponent<T>() where T:class{return gameObject.GetComponent<T>();}
    public int SelectionListeners{get{return Count(NewSelection);}}
    public int LeadListeners{get{return Count(NewLeadUnitSelected);}}
    public int FrameListeners{get{return Count(NewFrameFromSim);}}
    static int Count(Delegate callback){return callback==null?0:callback.GetInvocationList().Length;}
    public void RaiseSelection(){if(NewSelection!=null)NewSelection();}
    public void EndNativeSession(){NewSelection=NewLeadUnitSelected=NewFrameFromSim=null;}
}

public sealed class UnitHangarPanelController
{
    public readonly UnitInterfaceController mInterfaceController;
    public event Action<bool> UnitHangarPanelVisibleChanged;
    public UnitHangarPanelController(UnitInterfaceController owner){mInterfaceController=owner;}
    public int VisibleListeners{get{return UnitHangarPanelVisibleChanged==null?0:UnitHangarPanelVisibleChanged.GetInvocationList().Length;}}
    public void RaiseVisible(){if(UnitHangarPanelVisibleChanged!=null)UnitHangarPanelVisibleChanged(true);}
}

public static class UICamera
{
    public static event Action onScreenResize;
    public static int Listeners{get{return onScreenResize==null?0:onScreenResize.GetInvocationList().Length;}}
    public static void RaiseResize(){if(onScreenResize!=null)onScreenResize();}
}

public static class NativeRead
{
    public static int Despawns;
    public static object Get(object target,string name){return target.GetType().GetField(name).GetValue(target);}
    public static void Call(object target,string name,object dependency){if(name=="OnDespawned")Despawns++;}
}
public static class Runtime{public static void Log(string message){Console.WriteLine(message);}}
public struct Entity
{
    public int Id;
    public static readonly Entity None=new Entity();
}
public struct RhodesOperatorSlotStatus{public int Marker;}
public static class RhodesOperatorTransport{public const int Capacity=4;}
public sealed class NGUIEventHandler{public Action ClickAction,DoubleClickAction;}
public sealed class NGUIIconController:MonoBehaviour{public Action ClickAction,DoubleClickAction;}

// Bind, Cleanup, DestroyWidgets and bootstrap scheduling are appended verbatim
// from production by the runner. Widget construction and event bodies below
// only expose the lifecycle effects exercised by these tests.
public sealed partial class RhodesOperatorStatusUI:MonoBehaviour
{
    public sealed class SlotWidgets{public NGUIIconController Icon;public NGUIEventHandler HitInput;}
    public UnitInterfaceController Interface;
    public object HudDependencies;
    public GameObject Root;
    public object NativeHangar,RegionHint,NativeSpace,HangarObject,NativeFrame,NativeFontReference;
    public object Background,Accent,HeaderRule,Header,Phase,Detail,Feedback,UICameraReference,Layout;
    public bool AlignmentPending,BuildPending,EventsBound,RegionDumped,Ready,Dirty;
    public object PendingHangarController;
    public UnitHangarPanelController NativeHangarController;
    public readonly SlotWidgets[] Cards=new SlotWidgets[RhodesOperatorTransport.Capacity];
    public readonly object[] Actions=new object[3];
    public object AutoAction;
    public RhodesOperatorSlotStatus[] Slots=new RhodesOperatorSlotStatus[RhodesOperatorTransport.Capacity];
    public Entity Selected=Entity.None;
    public int ActiveSlot,LastCargoRevision=-1,LastManifestRevision=-1,LastSkillSeconds=-1;
    public string LocalFeedback,LastPhase,LastSkillType,LastSkillReason,LastGeometry,RegionReference;
    public bool LastSkillKnown,LastSkillReady,LastAutoResearched,LastAutoEnabled;
    public int GeometryReports,BuildAttempts,BuildWarnings,BuildCalls,SelectionCallbacks,VisibleCallbacks,ResizeCallbacks,RenderCalls,AlignmentCalls;
    public UnitHangarPanelController LastBuiltController;
    void OnSelection(){SelectionCallbacks++;}
    void OnLead(){}
    void OnFrame(){}
    void OnScreenResize(){ResizeCallbacks++;}
    void OnNativeHangarVisible(bool visible){if(visible)VisibleCallbacks++;}
    void Render(){RenderCalls++;}
    void ScheduleAlignment(){AlignmentCalls++;}
    bool Build(UnitInterfaceController owner,object controller)
    {
        BuildCalls++;LastBuiltController=(UnitHangarPanelController)controller;Root=new GameObject();
        HudDependencies=new object();NativeHangar=RegionHint=NativeSpace=HangarObject=NativeFrame=NativeFontReference=new object();
        Background=Accent=HeaderRule=Header=Phase=Detail=Feedback=UICameraReference=Layout=new object();
        for(int i=0;i<Cards.Length;i++)Cards[i]=new SlotWidgets{Icon=new NGUIIconController(),HitInput=new NGUIEventHandler()};
        for(int i=0;i<Actions.Length;i++)Actions[i]=new object();AutoAction=new object();Ready=true;return true;
    }
    public void Enable(){OnEnable();}
    public void Disable(){OnDisable();}
    public void Release(){Cleanup();}
}

internal static class OperatorSessionFixture65
{
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static void Drain(RhodesOperatorStatusUI driver){driver.Tick();driver.Tick();driver.Tick();}
    static void CheckListeners(UnitInterfaceController owner,UnitHangarPanelController controller)
    {
        Require(owner.SelectionListeners==1&&owner.LeadListeners==1&&owner.FrameListeners==1,"native owner events were missing or duplicated");
        Require(controller.VisibleListeners==1&&UICamera.Listeners==1,"hangar/resize events were missing or duplicated");
    }
    static void CheckReset(RhodesOperatorStatusUI driver)
    {
        Require(driver.Selected.Id==0&&driver.ActiveSlot==0&&driver.LastCargoRevision==-1&&driver.LastManifestRevision==-1,"cargo selection/revision cache survived rebind");
        Require(driver.Slots[0].Marker==0&&driver.LocalFeedback==null&&driver.LastPhase==null&&driver.LastSkillType==null&&driver.LastSkillReason==null,"cargo manifest/feedback cache survived rebind");
        Require(driver.LastSkillSeconds==-1&&!driver.LastSkillKnown&&!driver.LastSkillReady&&!driver.LastAutoResearched&&!driver.LastAutoEnabled,"skill cache survived rebind");
        Require(!driver.RegionDumped&&driver.GeometryReports==0&&driver.BuildAttempts==0&&driver.BuildWarnings==0&&driver.LastGeometry==null&&driver.RegionReference==null,"per-session bootstrap/geometry state survived rebind");
        Require(driver.Root==null&&driver.Cards[0]==null&&driver.Actions[0]==null&&driver.AutoAction==null&&driver.NativeHangar==null&&driver.NativeFrame==null&&driver.NativeFontReference==null&&driver.HudDependencies==null,"old widget/native references survived rebind");
    }
    static int Main()
    {
        try
        {
            UnitInterfaceController owner=new UnitInterfaceController();UnitHangarPanelController first=new UnitHangarPanelController(owner);
            RhodesOperatorStatusUI.Bind(first);RhodesOperatorStatusUI driver=owner.GetComponent<RhodesOperatorStatusUI>();
            Require(driver!=null&&!driver.Ready&&driver.BuildPending&&driver.PendingCount==1,"initial binding did not defer one build");
            CheckListeners(owner,first);Drain(driver);Require(driver.Ready&&driver.LastBuiltController==first,"initial deferred build used the wrong controller");
            GameObject oldRoot=driver.Root;int oldBuilds=driver.BuildCalls;
            driver.Selected=new Entity{Id=42};driver.ActiveSlot=3;driver.Slots[0]=new RhodesOperatorSlotStatus{Marker=9};
            driver.LastCargoRevision=driver.LastManifestRevision=7;driver.LocalFeedback=driver.LastPhase=driver.LastSkillType=driver.LastSkillReason="old";
            driver.LastSkillSeconds=9;driver.LastSkillKnown=driver.LastSkillReady=driver.LastAutoResearched=driver.LastAutoEnabled=true;
            driver.RegionDumped=true;driver.GeometryReports=driver.BuildAttempts=driver.BuildWarnings=4;driver.LastGeometry=driver.RegionReference="old";
            owner.EndNativeSession();UnitHangarPanelController second=new UnitHangarPanelController(owner);
            RhodesOperatorStatusUI.Bind(second);
            Require(!driver.Ready&&driver.BuildPending,"persistent Ready host skipped second-match bootstrap");
            Require(!oldRoot.activeSelf&&oldRoot.Destroyed&&NativeRead.Despawns==4,"old cargo widgets remained active or native slots skipped despawn");
            Require(first.VisibleListeners==0,"disposed hangar retained the operator callback");CheckReset(driver);CheckListeners(owner,second);
            owner.RaiseSelection();second.RaiseVisible();UICamera.RaiseResize();
            Require(driver.SelectionCallbacks==1&&driver.VisibleCallbacks==1&&driver.ResizeCallbacks==1,"new-session events did not reach the reused driver");
            Drain(driver);Require(driver.BuildCalls==oldBuilds+1&&driver.LastBuiltController==second&&driver.Root!=oldRoot,"second-match build reused old controller/widgets");

            // Constructor hook re-entry must refresh even if native delegate
            // fields were cleared while the controller object was retained.
            owner.EndNativeSession();oldRoot=driver.Root;RhodesOperatorStatusUI.Bind(second);CheckListeners(owner,second);
            Require(oldRoot.Destroyed&&driver.PendingCount==1,"same-controller rebind retained widgets or duplicate builds");Drain(driver);
            driver.Root=null;RhodesOperatorStatusUI.Bind(second);Require(!driver.Ready&&driver.BuildPending,"missing root left stale Ready set");Drain(driver);

            // Rebind before the two native-layout yields finish cancels the
            // old pending build and leaves a single current-controller build.
            RhodesOperatorStatusUI.Bind(first);driver.Tick();int before=driver.BuildCalls;
            RhodesOperatorStatusUI.Bind(second);Require(driver.PendingCount==1,"old coroutine survived a pending rebind");
            Drain(driver);Require(driver.BuildCalls==before+1&&driver.LastBuiltController==second,"old pending coroutine built against a previous session");

            owner.gameObject.activeInHierarchy=false;RhodesOperatorStatusUI.Bind(second);
            Require(!driver.BuildPending&&driver.PendingCount==0&&!driver.Ready,"inactive native host queued bootstrap");
            owner.gameObject.activeInHierarchy=true;driver.Enable();Require(driver.BuildPending,"reenabled host did not resume current-session bootstrap");Drain(driver);
            RhodesOperatorStatusUI.Bind(second);driver.Disable();Require(!driver.BuildPending&&driver.PendingCount==0,"disable left a stale pending flag/coroutine");driver.Enable();Drain(driver);
            driver.Release();Require(owner.SelectionListeners==0&&owner.LeadListeners==0&&owner.FrameListeners==0&&second.VisibleListeners==0&&UICamera.Listeners==0,"release left own subscriptions attached");
            Require(driver.Interface==null&&driver.PendingHangarController==null&&driver.NativeHangarController==null,"release retained the old native session");
            Console.WriteLine("PASS production operator Bind/Cleanup/DestroyWidgets/bootstrap scheduling across persistent owner, second match, same-controller rebind, missing root, pending rebind, inactive/reenabled host and disable/enable");
            Console.WriteLine("PASS offline lifecycle fixture with native/Unity stubs; native widget construction, geometry and actual game interaction require runtime validation");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
