using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BBI.Unity.Game.HUD;
using BBI.Unity.Game.UI;
using EndfieldDoK.MapUnitLab;
using UnityEngine;

[Serializable] public sealed class SkinGraph61
{
    public Color Colour;public SkinGraph61 Next;public List<object> Items=new List<object>();
}
public sealed class SkinHost61
{
    public Color Foreground;public Color32 Accent;public SkinGraph61 Graph;
    public object Dynamic;public object[] Array;public int Count;
}
public sealed class IconContainer61 {public string m_Name="Icon";public UISprite m_Sprite;}
internal static class HudSkinFixture61
{
    static int FieldPlans;
    static void Require(bool value,string why){if(!value)throw new InvalidOperationException(why);}
    static FieldInfo[] Fields(Type type){FieldPlans++;return type.GetFields(BindingFlags.Public|BindingFlags.Instance);}
    static bool Opaque(object value){return value is BBI.Unity.Game.Data.UnitHUDInteractionAttributes.UnitHangarStateColours;}
    static bool Excluded(FieldInfo field){return field.FieldType.IsPrimitive||field.FieldType.IsEnum||field.FieldType==typeof(string);}
    static Color Blue(){return new Color(.1f,.3f,.9f,1f);}
    static Color White(){return new Color(1f,1f,1f,1f);}
    static HudSerializedSkinSnapshot61 Capture(SkinHost61 host){return HudSerializedSkinSnapshot61.Capture(host,Fields,Opaque,Excluded);}
    static IDictionary Owners(){return (IDictionary)typeof(RhodesHangarStateColours).GetField("Owners",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);}
    static object SlotData(){return Activator.CreateInstance(typeof(UnitHangarPanelController).GetNestedType("HangarSlotObjectData",BindingFlags.NonPublic),true);}

    static void SnapshotTests()
    {
        SkinHost61 host=new SkinHost61{Foreground=White(),Accent=new Color32{r=255,g=255,b=255,a=255},Graph=new SkinGraph61{Colour=White()},Dynamic=1,Array=new object[]{White()}};
        host.Graph.Next=host.Graph;host.Graph.Items.Add(new SkinGraph61{Colour=White()});
        HudSerializedSkinSnapshot61 snapshot=Capture(host);int plans=FieldPlans;
        for(int i=0;i<10000;i++){host.Count=i;Require(snapshot.Matches(host),"stable pooled colour graph incorrectly invalidated");}
        Require(FieldPlans==plans,"stable reuse rebuilt reflection field plans");
        host.Foreground=Blue();Require(!snapshot.Matches(host),"native widget/palette reset not detected");host.Foreground=White();
        host.Accent=new Color32{r=0,g=0,b=255,a=255};Require(!snapshot.Matches(host),"Color32 reset not detected");host.Accent=new Color32{r=255,g=255,b=255,a=255};
        host.Graph.Colour=Blue();Require(!snapshot.Matches(host),"in-place nested colour reset not detected");host.Graph.Colour=White();
        ((SkinGraph61)host.Graph.Items[0]).Colour=Blue();Require(!snapshot.Matches(host),"nested list colour reset not detected");((SkinGraph61)host.Graph.Items[0]).Colour=White();
        host.Graph.Items.Add(White());Require(!snapshot.Matches(host),"new list element missed");host.Graph.Items.RemoveAt(1);
        object old=host.Array;host.Array=new object[]{White()};Require(!snapshot.Matches(host),"replaced equivalent array should rebuild ownership snapshot");host.Array=(object[])old;
        host.Array[0]=Blue();Require(!snapshot.Matches(host),"mutable array colour leaf reset missed");host.Array[0]=White();
        host.Dynamic=Blue();Require(!snapshot.Matches(host),"polymorphic primitive-to-colour replacement missed");host.Dynamic=1;
        SkinGraph61 oldCycle=host.Graph.Next;host.Graph.Next=new SkinGraph61{Colour=Blue()};Require(!snapshot.Matches(host),"cycle edge replaced with new colour graph missed");host.Graph.Next=oldCycle;
        Require(snapshot.Matches(host),"restored graph should match");
        SkinHost61 second=new SkinHost61{Foreground=Blue(),Accent=host.Accent,Graph=host.Graph,Dynamic=1,Array=host.Array};
        Require(!snapshot.Matches(second),"new component data must not inherit prior colour acceptance");
        host.Dynamic=new BBI.Unity.Game.Data.UnitHUDInteractionAttributes.UnitHangarStateColours();snapshot=Capture(host);Require(snapshot.Matches(host),"native palette treated as mutable theme graph");
        host.Graph.Items.Clear();for(int i=0;i<2048;i++)host.Graph.Items.Add(White());
        snapshot=Capture(host);Require(!snapshot.Matches(host),"uninspected oversized list incorrectly accepted");
        host.Graph.Items.Clear();host.Graph.Items.Add(Blue());Require(!snapshot.Matches(host),"oversized list shrinking in place hid a newly mappable colour");
        host.Graph.Items=new List<object>{Blue()};Require(!snapshot.Matches(host),"oversized list replaced with small list was missed");
        host.Array=new object[2048];snapshot=Capture(host);Require(!snapshot.Matches(host),"uninspected oversized array incorrectly accepted");
        host.Array=new object[]{Blue()};Require(!snapshot.Matches(host),"oversized array replaced with small array was missed");
        host.Graph.Items=new List<object>();for(int i=0;i<1000;i++)host.Graph.Items.Add(new SkinGraph61{Colour=White()});
        snapshot=Capture(host);host.Graph.Items.Clear();host.Foreground=Blue();Require(!snapshot.Matches(host),"budget-tail snapshot hid changes after graph shrinkage");
        Console.WriteLine("PASS SNAPSHOT 10000 stable reuses without field-plan rebuild; direct/nested/list/array colours, new references/elements, polymorphic fields and cycle replacement invalidate; counters do not");
        Console.WriteLine("PASS BUDGET oversized list/array never accepted blindly; in-place shrink, small replacement and truncated-tail changes conservatively invalidate");
    }

    static void ClassificationTests()
    {
        RhodesHangarStateColours.ClearContexts();NGUIIconController ordinary=new NGUIIconController{Data=new object()};
        NativeRead.Reads=0;
        for(int i=0;i<10000;i++)
        {Require(!RhodesHangarStateColours.IsForeground(ordinary,"Cooldown"),"non-Icon granted exception");Require(!RhodesHangarStateColours.IsForeground(ordinary,"Icon"),"ordinary unit granted slot exception");}
        Require(NativeRead.Reads==0&&Owners().Count==0,"ordinary SetColour still enumerates/registers container ownership");
        NGUIIconController slot=new NGUIIconController{Data=SlotData()};
        IconContainer61 first=new IconContainer61(),second=new IconContainer61();slot.m_IconObjects=new object[]{first,second};slot.mVisualizationVariables.Add("duplicate",first);
        Require(RhodesHangarStateColours.IsForeground(slot,"Icon"),"native slot foreground lost");Require(!RhodesHangarStateColours.IsForeground(slot,"Cooldown"),"native slot cooldown incorrectly exempt");
        RhodesHangarStateColours.RegisterIcon(slot);WeakReference firstOwner=(WeakReference)Owners()[first],secondOwner=(WeakReference)Owners()[second];
        Require(object.ReferenceEquals(firstOwner,secondOwner),"registration did not share owner WeakReference");
        for(int i=0;i<1000;i++)RhodesHangarStateColours.RegisterIcon(slot);
        Require(object.ReferenceEquals(firstOwner,Owners()[first])&&object.ReferenceEquals(secondOwner,Owners()[second]),"pooled registration replaces identical WeakReferences");
        Require(RhodesHangarStateColours.IsForegroundContainer(first),"registered slot container lost colour preservation");
        slot.Data=new object();Require(!RhodesHangarStateColours.IsForegroundContainer(first),"pooled slot permission leaked after Data reuse");slot.Data=SlotData();
        NGUIIconController next=new NGUIIconController{Data=SlotData(),m_IconObjects=new object[]{first}};
        RhodesHangarStateColours.RegisterIcon(next);Require(object.ReferenceEquals(((WeakReference)Owners()[first]).Target,next),"container owner transfer not tracked");
        Require(!object.ReferenceEquals(firstOwner,Owners()[first]),"owner transfer reused stale WeakReference");
        next.m_IconObjects=new object[0];Require(!RhodesHangarStateColours.IsForegroundContainer(first),"removed container retained cached owner permission");
        RhodesHangarStateColours.ClearContexts();Require(Owners().Count==0,"second-match context reset failed");
        RhodesHangarStateColours.RegisterIcon(slot);Require(RhodesHangarStateColours.IsForegroundContainer(second),"second-match registration failed");
        Console.WriteLine("PASS CLASSIFICATION 20000 ordinary SetColour classifications: zero NativeRead and owner entries; slot palette preserved, WeakReference identity reused, Data/owner/membership reuse and second-match reset verified");
    }
    static void ProductionIconPathTests()
    {
        NGUIIconController icon=new NGUIIconController{Data=SlotData()};NativeRead.Reads=0;ThemeIconProbe61.Enabled=false;
        Require(ThemeIconProbe61.MapIconColour(icon,"Icon",Blue()).Equals(Blue()),"disabled theme changed input");
        Require(ThemeIconProbe61.MapContainerColour(new object(),Blue()).Equals(Blue())&&NativeRead.Reads==0,"disabled theme still classified containers");
        ThemeIconProbe61.Enabled=true;
        Require(ThemeIconProbe61.MapIconColour(icon,"Icon",Blue()).Equals(Blue()),"native hangar foreground palette changed");
        Require(ThemeIconProbe61.MapIconColour(icon,"Cooldown",Blue()).Equals(White()),"ordinary slot decoration mapping bypassed");
        NGUIIconController.VisualizationContainer visual=new NGUIIconController.VisualizationContainer{mInitialColour=Blue(),mInitialBackgroundColour=Blue(),mOverridenColour=Blue(),mOverridenBackgroundColour=Blue(),mIconAtlas="NativeAtlas",m_Sprite=new UISprite()};
        new Transform().Attach(visual.m_Sprite);visual.m_Sprite.transform.parent=icon.transform;
        ThemeIconProbe61.ExternalWalks=0;ThemeIconProbe61.Apply(visual,icon);
        Require(visual.mInitialColour.Equals(White())&&(string)visual.mIconAtlas=="ThemedAtlas","pooled native colour/atlas state not refreshed");
        Require(ThemeIconProbe61.ExternalWalks==0,"ordinary child visual received duplicate subtree walk");
        visual.mInitialColour=Blue();visual.mIconAtlas="NativeAtlas";ThemeIconProbe61.Apply(visual,icon);
        Require(visual.mInitialColour.Equals(White())&&(string)visual.mIconAtlas=="ThemedAtlas","pool respawn reset skipped by cache");
        UISprite first=new UISprite(),second=new UISprite();new Transform().Attach(first);new Transform().Attach(second);
        visual.m_Sprite=first;ThemeIconProbe61.Apply(visual,icon);Require(object.ReferenceEquals(ThemeIconProbe61.LastExternal,first),"external visual reference ignored");
        visual.m_Sprite=second;ThemeIconProbe61.Apply(visual,icon);Require(object.ReferenceEquals(ThemeIconProbe61.LastExternal,second),"replaced external visual reference reused stale subtree");
        int walks=ThemeIconProbe61.ExternalWalks;visual.m_Sprite=null;ThemeIconProbe61.Apply(visual,icon);Require(ThemeIconProbe61.ExternalWalks==walks,"missing external reference still walked");
        Console.WriteLine("PASS PRODUCTION ICON PATH actual MapIcon/ContainerColour and ApplyIconContainer method bodies: disabled short-circuit, native palette, repeated colour/atlas reset, no child double walk, replaced/missing external visual references");
    }
    static int Main()
    {
        try{SnapshotTests();ClassificationTests();ProductionIconPathTests();Console.WriteLine("PASS ALL production managed skin snapshot and complete production hangar-colour class; offline Unity contracts only, no rendering or game startup");return 0;}
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
