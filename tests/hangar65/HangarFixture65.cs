using System;
using System.Collections.Generic;
using System.Reflection;
using BBI.Unity.Game.HUD;
using BBI.Unity.Game.UI;
using EndfieldDoK.MapUnitLab;
using UnityEngine;

public sealed class HangarSettings65
{
    public UIGrid m_HangarInterceptorGrid,m_HangarGunShipGrid,m_HangarBomberGrid;
    public NGUIIconController m_HangarInterceptorIconPrefab,m_HangarGunShipIconPrefab,m_HangarBomberIconPrefab;
    public GameObject m_HangarParentObject;
}
public sealed class CommandSettings65 {public CommandGrid65 m_HangarDeployGrid,m_HangarRecallGrid;}
public sealed class CommandGrid65 {public UIGrid m_Grid;}
public sealed class IconGraphic65 {public string m_Name="Icon";public UISprite m_Sprite;}
public sealed class AbilityButtonState {public string AbilityTypeName;}

public static class HangarFixture65
{
    static int Checks;
    static void Check(bool condition,string message){Checks++;if(!condition)throw new InvalidOperationException(message);}
    static void Near(float actual,float expected,string message){Check(Math.Abs(actual-expected)<.00001f,message+" actual="+actual+" expected="+expected);}
    static object Read(object target,string name){return NativeRead.Get(target,name);}
    static void Invoke(object target,string name){target.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(target,null);}
    static T Component<T>(Transform parent) where T:Component,new()
    {GameObject node=new GameObject();if(parent!=null)parent.Attach(node.transform);return node.AddComponent<T>();}
    static NGUIIconController Prefab(int size)
    {
        NGUIIconController icon=Component<NGUIIconController>(null);
        UISprite sprite=Component<UISprite>(icon.transform);sprite.width=size;sprite.height=size;
        icon.m_IconObjects=new object[]{new IconGraphic65{m_Sprite=sprite}};return icon;
    }
    static HangarSettings65 Settings(int size)
    {
        GameObject root=new GameObject();
        return new HangarSettings65{
            m_HangarParentObject=root,m_HangarInterceptorGrid=Component<UIGrid>(root.transform),
            m_HangarGunShipGrid=Component<UIGrid>(root.transform),m_HangarBomberGrid=Component<UIGrid>(root.transform),
            m_HangarInterceptorIconPrefab=Prefab(size),m_HangarGunShipIconPrefab=Prefab(size),m_HangarBomberIconPrefab=Prefab(size)};
    }
    static CommandSettings65 Commands(Transform parent)
    {
        CommandSettings65 settings=new CommandSettings65{m_HangarDeployGrid=new CommandGrid65{m_Grid=Component<UIGrid>(parent)},m_HangarRecallGrid=new CommandGrid65{m_Grid=Component<UIGrid>(parent)}};
        string[] keys={RhodesCarrierUnits.AircraftLaunchKey,RhodesCarrierUnits.TiltLaunchKey,RhodesCarrierUnits.HeavyBomberLaunchKey};
        for(int i=0;i<keys.Length;i++)
        {
            NGUIIconController icon=Component<NGUIIconController>(settings.m_HangarDeployGrid.m_Grid.transform);
            icon.Data=new AbilityButtonState{AbilityTypeName=keys[i]};icon.transform.localPosition=new Vector3(i*100,0,0);
        }
        return settings;
    }
    static UnitHangarPanelController Controller(UnitInterfaceController owner,HangarSettings65 settings)
    {
        return new UnitHangarPanelController{mInterfaceController=owner,mSettings=settings,mHangarSlotIcons=new Dictionary<string,List<NGUIIconController>>{
            {RhodesCarrierUnits.AircraftKey,new List<NGUIIconController>(new NGUIIconController[15])},
            {FactionUnits.TiltStrikeKey,new List<NGUIIconController>(new NGUIIconController[2])},
            {RhodesCarrierUnits.HeavyBomberKey,new List<NGUIIconController>(new NGUIIconController[4])}}};
    }
    static void Layout(RhodesHangarLayout driver){driver.Tick();driver.Tick();Check(driver.PendingCoroutines==0,"deferred layout completes");}
    static void Listeners(UnitInterfaceController owner,UnitHangarPanelController controller)
    {Check(owner.SelectionListeners==1,"one selection listener");Check(owner.LeadListeners==1,"one lead listener");Check(controller.VisibleListeners==1,"one visibility listener");}

    public static int Main()
    {
        try{return Run();}
        catch(Exception error){Console.Error.WriteLine("FAIL Hangar65 checks="+Checks+"; "+error.Message);return 1;}
    }
    static int Run()
    {
        UnitInterfaceController owner=Component<UnitInterfaceController>(null);
        HangarSettings65 first=Settings(200);owner.mCommandPanelSettings=Commands(first.m_HangarParentObject.transform);
        UnitHangarPanelController oldController=Controller(owner,first);
        RhodesHangarLayout.Bind(oldController);RhodesHangarLayout driver=owner.GetComponent<RhodesHangarLayout>();
        Check(driver!=null,"driver is attached once");Listeners(owner,oldController);
        Check(driver.PendingCoroutines==1,"first bind schedules one layout");
        RhodesHangarLayout.Bind(oldController);Listeners(owner,oldController);
        Check(driver.PendingCoroutines==1,"duplicate bind replaces pending layout");Layout(driver);
        float scale=20f*.86f/200f;
        Near(first.m_HangarInterceptorGrid.transform.localScale.x,scale,"first actual reflow size");
        Near(first.m_HangarGunShipGrid.transform.localScale.x,scale,"rotor actual reflow size");
        Near(first.m_HangarBomberGrid.transform.localScale.x,scale,"bomber actual reflow size");
        Check(first.m_HangarInterceptorGrid.maxPerLine==3,"fighter has three columns");

        // Actual native session teardown clears owner delegates and creates a
        // fresh controller on the same persistent UnitInterfaceController.
        owner.ResetSessionEvents();UnitHangarPanelController nextController=Controller(owner,first);
        RhodesHangarLayout.Bind(nextController);Check(oldController.VisibleListeners==0,"previous controller is detached");
        Check(object.ReferenceEquals(Read(driver,"Controller"),nextController),"new session controller is stored");
        Listeners(owner,nextController);Layout(driver);
        Near(first.m_HangarInterceptorGrid.transform.localScale.x,scale,"second session scale stays stable");
        owner.Select();owner.Lead();nextController.Visible();Check(driver.PendingCoroutines==1,"current session events coalesce");Layout(driver);
        oldController.Visible();Check(driver.PendingCoroutines==0,"disposed controller no longer queues layout");

        RhodesHangarLayout.Bind(nextController);
        Near(first.m_HangarInterceptorGrid.transform.localScale.x,1,"rebind restores original scale before recapture");
        Check(first.m_HangarInterceptorGrid.arrangement==UIGrid.Arrangement.Vertical,"rebind restores arrangement");
        Check(first.m_HangarInterceptorGrid.pivot==UIWidget.Pivot.Center,"rebind restores pivot");
        Layout(driver);Near(first.m_HangarInterceptorGrid.transform.localScale.x,scale,"repeat bind does not compound scale");

        // Native references can be rebuilt while retaining controller identity.
        HangarSettings65 replacement=Settings(400);nextController.mSettings=replacement;
        owner.mCommandPanelSettings=Commands(replacement.m_HangarParentObject.transform);
        RhodesHangarLayout.Bind(nextController);Listeners(owner,nextController);
        Near(first.m_HangarInterceptorGrid.transform.localScale.x,1,"replacing settings restores previous grid");
        Check(object.ReferenceEquals(Read(driver,"Fighter"),replacement.m_HangarInterceptorGrid),"settings grid replacement is adopted");
        Layout(driver);Near(replacement.m_HangarInterceptorGrid.transform.localScale.x,scale*.5f,"replacement prefab graphic size is adopted");

        // A queued layout can outlive a destroyed native root. It is guarded,
        // and the next explicit bind restores surviving old grids safely.
        owner.Select();replacement.m_HangarParentObject.transform.Destroyed=true;
        replacement.m_HangarInterceptorGrid.Destroyed=true;driver.Tick();driver.Tick();
        HangarSettings65 third=Settings(200);nextController.mSettings=third;
        owner.mCommandPanelSettings=Commands(third.m_HangarParentObject.transform);
        RhodesHangarLayout.Bind(nextController);Layout(driver);
        Near(replacement.m_HangarGunShipGrid.transform.localScale.x,1,"surviving old grid restores after other grid destruction");
        Near(third.m_HangarInterceptorGrid.transform.localScale.x,scale,"new root recovers after old root destruction");

        owner.gameObject.activeInHierarchy=false;Invoke(driver,"OnDisable");
        RhodesHangarLayout.Bind(nextController);Check(driver.PendingCoroutines==0,"disabled interface defers layout");
        owner.gameObject.activeInHierarchy=true;Invoke(driver,"OnEnable");Check(driver.PendingCoroutines==1,"reenable schedules layout");Layout(driver);
        Near(third.m_HangarInterceptorGrid.transform.localScale.x,scale,"reenable preserves correct scale");
        owner.ResetSessionEvents();RhodesHangarLayout.Bind(nextController);Listeners(owner,nextController);Layout(driver);
        Check(object.ReferenceEquals(owner.GetComponent<RhodesHangarLayout>(),driver),"persistent component is reused");

        Invoke(driver,"OnDestroy");Check(owner.SelectionListeners==0&&owner.LeadListeners==0,"destroy detaches owner events");
        Check(nextController.VisibleListeners==0,"destroy detaches controller event");
        Check(driver.PendingCoroutines==0&&!(bool)Read(driver,"Ready"),"destroy clears deferred binding state");
        Near(third.m_HangarInterceptorGrid.transform.localScale.x,1,"destroy restores native scale");
        Invoke(driver,"OnDestroy");Check(driver.PendingCoroutines==0,"repeated cleanup is safe");
        Console.WriteLine("PASS Hangar65 checks="+Checks+"; productionClass=RhodesHangarLayout; UnityAndNativeUI=managedStubs; actualReflow=True; runtimeVisualAcceptance=False");
        return 0;
    }
}
