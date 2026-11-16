using System;
using System.Collections.Generic;
using EndfieldDoK.MapUnitLab;
using UnityEngine;
using BBI.Unity.Game.World;
using BBI.Unity.Core.World;

static class NativeLodBoundsFixture
{
    static int Checks,Cases;
    static void Check(bool condition,string message){Checks++;if(!condition)throw new Exception(message);}
    static void Close(float a,float b,string message){Check(Math.Abs(a-b)<.002f,message+" actual="+a+" expected="+b);}
    static void Close(Vector3 a,Vector3 b,string message){Close(a.x,b.x,message+" X");Close(a.y,b.y,message+" Y");Close(a.z,b.z,message+" Z");}
    static Quaternion Yaw(float degrees){double half=degrees*Math.PI/360;return new Quaternion(0,(float)Math.Sin(half),0,(float)Math.Cos(half));}
    sealed class Context
    {
        internal UnitView View;
        internal LODGroup Native,Authored;
        internal Renderer[] Renderers;
    }
    sealed class Snapshot
    {
        internal LOD[] Levels;
        internal Vector3 Reference;
        internal float Size;
        internal bool Enabled;
        internal int Force;
        internal Snapshot(LODGroup group){Levels=group.GetLODs();Reference=group.localReferencePoint;Size=group.size;Enabled=group.enabled;Force=group.ForcedMode;}
    }
    static Context Setup(float parentScale,float nativeScale,float authoredScale,float yaw)
    {
        Transform scene=new Transform{localPosition=new Vector3(420,-37,880),localRotation=Yaw(yaw),localScale=Vector3.one*parentScale};
        Transform wrapper=new Transform{parent=scene,localPosition=new Vector3(5,2,-7),localRotation=Yaw(-32),localScale=Vector3.one*nativeScale};
        Transform geometry=new Transform{parent=wrapper,localPosition=new Vector3(4,3,-2),localRotation=Yaw(47),localScale=Vector3.one*authoredScale};
        LODGroup native=new LODGroup(wrapper),authored=new LODGroup(geometry);
        Renderer[] renderers=new Renderer[3];LOD[] authoredLevels=new LOD[3],nativeLevels=new LOD[3];
        float[] thresholds={.7f,.3f,.025f};
        for(int i=0;i<3;i++)
        {
            renderers[i]=new SkinnedMeshRenderer{transform=new Transform{parent=geometry,localPosition=new Vector3(i,-i,i*2)},LocalBounds=new Bounds(new Vector3(40,50,60),Vector3.one*2000000)};
            authoredLevels[i]=new LOD(.8f-i*.2f,new Renderer[]{renderers[i]});
            nativeLevels[i]=new LOD(thresholds[i],new Renderer[]{new Renderer{transform=new Transform{parent=wrapper}}});
            nativeLevels[i].fadeTransitionWidth=.13f+i*.07f;
        }
        native.SetLODs(nativeLevels);native.localReferencePoint=new Vector3(-5,7,3);native.size=24;native.enabled=false;native.ForceLOD(2);
        authored.SetLODs(authoredLevels);authored.localReferencePoint=new Vector3(1.25f,-2.5f,3.75f);authored.size=60;authored.enabled=true;authored.ForceLOD(1);
        native.ResetCounters();authored.ResetCounters();
        return new Context{Native=native,Authored=authored,View=new UnitView{transform=wrapper,Group=native,Mode=LODGroupController.ModelLOD.Medium},Renderers=renderers};
    }
    static void SameLevels(LOD[] actual,LOD[] expected,string message)
    {
        Check(actual.Length==expected.Length,message+" count");
        for(int i=0;i<actual.Length;i++)
        {
            Check(actual[i].screenRelativeTransitionHeight.Equals(expected[i].screenRelativeTransitionHeight),message+" threshold");
            Check(actual[i].fadeTransitionWidth.Equals(expected[i].fadeTransitionWidth),message+" fade");
            if(actual[i].renderers==null||expected[i].renderers==null){Check(actual[i].renderers==expected[i].renderers,message+" null renderer list");continue;}
            Check(actual[i].renderers.Length==expected[i].renderers.Length,message+" renderer count");
            for(int j=0;j<actual[i].renderers.Length;j++)Check(object.ReferenceEquals(actual[i].renderers[j],expected[i].renderers[j]),message+" renderer identity");
        }
    }
    static void Restored(LODGroup group,Snapshot saved,string message)
    {
        SameLevels(group.GetLODs(),saved.Levels,message);
        Vector3 actual=group.localReferencePoint;
        Check(actual.x.Equals(saved.Reference.x)&&actual.y.Equals(saved.Reference.y)&&actual.z.Equals(saved.Reference.z),message+" reference");
        Check(group.size.Equals(saved.Size),message+" size");Check(group.enabled==saved.Enabled,message+" enabled");Check(group.ForcedMode==saved.Force,message+" force");
    }
    static void RejectBeforeMutation(string name,Action<Context> alter)
    {
        Cases++;Context c=Setup(1,1,1,0);alter(c);Snapshot n=new Snapshot(c.Native),a=new Snapshot(c.Authored);
        c.Native.ResetCounters();c.Authored.ResetCounters();bool rejected=false;
        try{NativeUnitLodAdapter.AdoptGeometry(c.View,c.Authored);}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"Invalid contract accepted "+name);Check(c.Native.SetCalls==0&&c.Authored.SetCalls==0,"Invalid contract mutated LOD lists "+name);
        Check(c.Native.PropertyWrites==0&&c.Authored.PropertyWrites==0,"Invalid contract mutated LOD properties "+name);
        Restored(c.Native,n,"Invalid native "+name);Restored(c.Authored,a,"Invalid authored "+name);
    }
    static void Successes()
    {
        foreach(float parentScale in new float[]{.2f,1,7})foreach(float nativeScale in new float[]{.5f,1,2})foreach(float authoredScale in new float[]{.2f,1,3})foreach(float yaw in new float[]{0,73,180})
        {
            Cases++;Context c=Setup(parentScale,nativeScale,authoredScale,yaw);Snapshot n=new Snapshot(c.Native),a=new Snapshot(c.Authored);
            // The geometry is a direct native child. Its known local yaw/scale
            // yields an independent closed-form reference after all world
            // parent translations, rotations and scales cancel.
            double sine=Math.Sin(47*Math.PI/180),cosine=Math.Cos(47*Math.PI/180);
            Vector3 expected=new Vector3((float)(4+authoredScale*(a.Reference.x*cosine+a.Reference.z*sine)),
                3+authoredScale*a.Reference.y,(float)(-2+authoredScale*(-a.Reference.x*sine+a.Reference.z*cosine)));
            float expectedSize=a.Size*authoredScale;
            LODGroup adopted=NativeUnitLodAdapter.AdoptGeometry(c.View,c.Authored);
            Check(object.ReferenceEquals(adopted,c.Native),"Native root LOD identity changed");
            Close(c.Native.localReferencePoint,expected,"Transferred source reference");Close(c.Native.size,expectedSize,"Transferred source distance size");
            Check(c.Native.Recalculations==0&&c.Authored.Recalculations==0,"Skin swept sphere recalculation occurred");
            Check(c.Native.enabled&&!c.Authored.enabled,"Single active LOD owner");Check(c.Authored.GetLODs().Length==0,"Authored lists still registered");
            Check(c.Native.ForcedMode==n.Force&&c.Authored.ForcedMode==a.Force,"Forced mode changed");
            LOD[] levels=c.Native.GetLODs();
            for(int i=0;i<3;i++)
            {
                Check(levels[i].screenRelativeTransitionHeight==n.Levels[i].screenRelativeTransitionHeight,"Native threshold not preserved");
                Check(levels[i].fadeTransitionWidth==n.Levels[i].fadeTransitionWidth,"Native fade not preserved");
                Check(levels[i].renderers.Length==1&&object.ReferenceEquals(levels[i].renderers[0],c.Renderers[i]),"Authored renderer mapping changed");
            }
            for(int mode=0;mode<=3;mode++){c.View.Mode=(LODGroupController.ModelLOD)mode;Check(NativeUnitLodAdapter.CurrentLod(c.View)==mode,"Native private getter delegate result");}
        }
        foreach(int count in new int[]{1,2,3,4,5,6,7,8})
        {
            Cases++;Context c=Setup(1,1,1,19);LOD[] nativeLevels=new LOD[count],authoredLevels=new LOD[count];
            for(int i=0;i<count;i++)
            {
                nativeLevels[i]=new LOD(1f-(i+1f)/(count+1),new Renderer[0]);nativeLevels[i].fadeTransitionWidth=.03f*i;
                authoredLevels[i]=new LOD(.1f,new Renderer[]{c.Renderers[0]});
            }
            c.Native.SetLODs(nativeLevels);c.Authored.SetLODs(authoredLevels);NativeUnitLodAdapter.AdoptGeometry(c.View,c.Authored);
            LOD[] result=c.Native.GetLODs();Check(result.Length==count,"Supported LOD level count changed");
            for(int i=0;i<count;i++){Check(result[i].screenRelativeTransitionHeight==nativeLevels[i].screenRelativeTransitionHeight,"Supported level native threshold");Check(result[i].fadeTransitionWidth==nativeLevels[i].fadeTransitionWidth,"Supported level native fade");}
        }
        foreach(int mode in new int[]{-1,0,1,2})
        {
            Cases++;Context c=Setup(1,1,1,0);c.Native.ForceLOD(mode);c.Authored.ForceLOD(mode);NativeUnitLodAdapter.AdoptGeometry(c.View,c.Authored);
            Check(c.Native.ForcedMode==mode&&c.Authored.ForcedMode==mode,"Automatic/forced mode changed");
        }
        Check(NativeUnitLodAdapter.CurrentLod(null)==3,"Null native view LOD result");
    }
    static void Rejections()
    {
        RejectBeforeMutation("ownership",delegate(Context c){c.Authored.transform.parent=new Transform();});
        RejectBeforeMutation("native missing",delegate(Context c){c.View.Group=null;});
        RejectBeforeMutation("same group",delegate(Context c){c.View.Group=c.Authored;});
        RejectBeforeMutation("empty native levels",delegate(Context c){c.Native.SetLODs(new LOD[0]);});
        RejectBeforeMutation("count mismatch",delegate(Context c){c.Authored.SetLODs(new LOD[]{c.Authored.GetLODs()[0]});});
        RejectBeforeMutation("nine levels",delegate(Context c){LOD[] levels=new LOD[9];c.Native.SetLODs(levels);c.Authored.SetLODs(levels);});
        foreach(float threshold in new float[]{-.01f,1.01f,float.NaN,float.PositiveInfinity})
        {float bad=threshold;RejectBeforeMutation("threshold "+bad,delegate(Context c){LOD[] levels=c.Native.GetLODs();levels[0].screenRelativeTransitionHeight=bad;c.Native.SetLODs(levels);});}
        RejectBeforeMutation("unordered thresholds",delegate(Context c){LOD[] levels=c.Native.GetLODs();levels[1].screenRelativeTransitionHeight=.8f;c.Native.SetLODs(levels);});
        RejectBeforeMutation("duplicate thresholds",delegate(Context c){LOD[] levels=c.Native.GetLODs();levels[1].screenRelativeTransitionHeight=levels[0].screenRelativeTransitionHeight;c.Native.SetLODs(levels);});
        RejectBeforeMutation("empty authored renderers",delegate(Context c){LOD[] levels=c.Authored.GetLODs();levels[1].renderers=new Renderer[0];c.Authored.SetLODs(levels);});
        RejectBeforeMutation("null authored renderers",delegate(Context c){LOD[] levels=c.Authored.GetLODs();levels[1].renderers=null;c.Authored.SetLODs(levels);});
        RejectBeforeMutation("null renderer",delegate(Context c){LOD[] levels=c.Authored.GetLODs();levels[1].renderers=new Renderer[]{null};c.Authored.SetLODs(levels);});
        RejectBeforeMutation("foreign renderer",delegate(Context c){c.Renderers[1].transform.parent=new Transform();});
        foreach(Vector3 scale in new Vector3[]{new Vector3(1,2,1),new Vector3(-1,-1,-1),Vector3.zero,new Vector3(float.NaN,1,1),new Vector3(float.PositiveInfinity,1,1)})
        {Vector3 bad=scale;RejectBeforeMutation("invalid source world scale",delegate(Context c){c.Authored.transform.localScale=bad;});}
        RejectBeforeMutation("native nonuniform compensated source",delegate(Context c){c.Native.transform.localScale=new Vector3(2,3,2);c.Authored.transform.localScale=new Vector3(.5f,1f/3f,.5f);});
        RejectBeforeMutation("native negative compensated source",delegate(Context c){c.Native.transform.localScale=Vector3.one*-1;c.Authored.transform.localScale=Vector3.one*-1;});
        foreach(float size in new float[]{0,-1,float.NaN,float.PositiveInfinity})
        {float bad=size;RejectBeforeMutation("invalid source LOD size",delegate(Context c){c.Authored.size=bad;});}
        RejectBeforeMutation("nonfinite source reference",delegate(Context c){c.Authored.localReferencePoint=new Vector3(float.NaN,0,0);});
        RejectBeforeMutation("nonfinite source position",delegate(Context c){c.Authored.transform.localPosition=new Vector3(float.PositiveInfinity,0,0);});
    }
    static void Rollbacks()
    {
        foreach(bool nativeEnabled in new bool[]{false,true})foreach(bool authoredEnabled in new bool[]{false,true})foreach(bool failNative in new bool[]{false,true})foreach(bool afterMutation in new bool[]{false,true})
        {
            Cases++;Context c=Setup(2,.5f,3,37);c.Native.enabled=nativeEnabled;c.Authored.enabled=authoredEnabled;
            Snapshot n=new Snapshot(c.Native),a=new Snapshot(c.Authored);LODGroup target=failNative?c.Native:c.Authored;
            if(afterMutation)target.ThrowOnceAfterSet=true;else target.ThrowOnceBeforeSet=true;
            bool threw=false;try{NativeUnitLodAdapter.AdoptGeometry(c.View,c.Authored);}catch(ApplicationException){threw=true;}
            Check(threw,"Injected SetLODs failure was swallowed");Restored(c.Native,n,"Rollback native");Restored(c.Authored,a,"Rollback authored");
            NativeUnitLodAdapter.AdoptGeometry(c.View,c.Authored);Close(c.Native.size,180,"Successful retry after rollback");
            Check(c.Native.enabled&&!c.Authored.enabled&&c.Authored.GetLODs().Length==0,"Retry did not adopt restored groups");
        }
    }
    static int Main()
    {
        try
        {
            Context old=Setup(1,1,1,0);float trueSize=old.Authored.size;
            BaselineNativeUnitLodAdapter.AdoptGeometry(old.View,old.Authored);
            Check(old.Native.Recalculations==1,"Old baseline did not execute renderer-bound recalculation");
            Check(old.Native.size>trueSize*1000,"Old baseline did not reproduce swept-sphere LOD inflation");
            Console.WriteLine("NEGATIVE_CONTROL oldSize="+old.Native.size+" authoredDistanceSize="+trueSize);
            Successes();Rejections();Rollbacks();
            Console.WriteLine("PASS cases="+Cases+" checks="+Checks+" productionSource=True hugeSkinSphereExcluded=True preMutationValidation=True rollbackSetLodsBeforeAndAfter=True unityGpuValidation=False");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
