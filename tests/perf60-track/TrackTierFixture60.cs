using System;
using System.Collections.Generic;
using EndfieldDoK.ModelIntake;
using EndfieldDoK.MapUnitLab;
using UnityEngine;

internal static class TrackTierFixture60
{
    static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    static void Equal(double actual,double expected,string message)
    {Require(Math.Abs(actual-expected)<.0001,message+": expected "+expected+", got "+actual);}
    static FactionTrackLiveRig Rig(FactionTrackPackage package)
    {
        FactionTrackLiveRig rig=new FactionTrackLiveRig();rig.Groups=new FactionTrackLiveBelt[package.Belts.Length];
        for(int i=0;i<rig.Groups.Length;i++)
        {
            FactionTrackBelt data=package.Belts[i];FactionTrackLiveBelt g=new FactionTrackLiveBelt();g.Data=data;
            g.Bones=new Transform[data.Phases.Length];for(int b=0;b<g.Bones.Length;b++)g.Bones[b]=new Transform();
            g.Skins=new SkinnedMeshRenderer[3];g.UsedBones=new bool[3][];
            for(int lod=0;lod<3;lod++)
            {
                g.Skins[lod]=new SkinnedMeshRenderer();g.UsedBones[lod]=new bool[g.Bones.Length];
                foreach(int bone in data.Skins[lod].Bones)g.UsedBones[lod][bone]=true;
            }
            g.NativeUsedBones=new bool[g.Bones.Length];g.Wheels=new Transform[data.Wheels.Length];
            g.WheelAngles=new double[data.Wheels.Length];g.WheelRest=new Quaternion[data.Wheels.Length];
            for(int w=0;w<g.Wheels.Length;w++)g.Wheels[w]=new Transform();
            rig.Groups[i]=g;
        }
        return rig;
    }
    static void Visible(FactionTrackLiveRig rig,int mask)
    {foreach(FactionTrackLiveBelt g in rig.Groups)for(int lod=0;lod<3;lod++)g.Skins[lod].isVisible=(mask&(1<<lod))!=0;}
    static void Reset(FactionTrackLiveRig rig)
    {
        foreach(FactionTrackLiveBelt g in rig.Groups)
        {
            foreach(Transform t in g.Bones){t.PositionWrites=0;t.RotationWrites=0;}
            foreach(Transform t in g.Wheels){t.PositionWrites=0;t.RotationWrites=0;}
        }
    }
    static int Count(FactionTrackLiveRig rig,bool bones)
    {
        int writes=0;foreach(FactionTrackLiveBelt g in rig.Groups)
            foreach(Transform t in bones?g.Bones:g.Wheels)writes+=t.PositionWrites+t.RotationWrites;
        return writes;
    }
    static int BoneCount(FactionTrackLiveRig rig,int mask)
    {
        int count=0;foreach(FactionTrackLiveBelt g in rig.Groups)
            for(int bone=0;bone<g.Bones.Length;bone++)
            {bool used=false;for(int lod=0;lod<3;lod++)used|=(mask&(1<<lod))!=0&&g.UsedBones[lod][bone];if(used)count++;}
        return count;
    }
    static int WheelCount(FactionTrackLiveRig rig,int mask)
    {int count=0;foreach(FactionTrackLiveBelt g in rig.Groups)foreach(FactionTrackWheel w in g.Data.Wheels)if((mask&(1<<w.Lod))!=0)count++;return count;}
    static void AssertWrites(FactionTrackLiveRig rig,int boneMask,int wheelMask,string phase)
    {
        Equal(Count(rig,true),2*BoneCount(rig,boneMask),phase+" bone position+rotation writes");
        Equal(Count(rig,false),WheelCount(rig,wheelMask),phase+" wheel rotation writes");
        foreach(FactionTrackLiveBelt g in rig.Groups)
        {
            for(int b=0;b<g.Bones.Length;b++)
            {
                bool used=false;for(int lod=0;lod<3;lod++)used|=(boneMask&(1<<lod))!=0&&g.UsedBones[lod][b];
                Equal(g.Bones[b].PositionWrites,used?1:0,phase+" exact bone position "+b);
                Equal(g.Bones[b].RotationWrites,used?1:0,phase+" exact bone rotation "+b);
            }
            for(int w=0;w<g.Wheels.Length;w++)
                Equal(g.Wheels[w].RotationWrites,(wheelMask&(1<<g.Data.Wheels[w].Lod))!=0?1:0,phase+" exact wheel LOD");
        }
    }
    static void AssertNearPose(FactionTrackLiveRig rig)
    {
        foreach(FactionTrackLiveBelt g in rig.Groups)
        {
            for(int bone=0;bone<g.Bones.Length;bone++)if(g.UsedBones[0][bone])
            {
                double forward,up,angle;FactionTrackMath.Sample(g.Data,g.Data.Phases[bone]+g.Phase,out forward,out up,out angle);
                Equal(g.Bones[bone].localPosition.y,up,"catchup up");Equal(g.Bones[bone].localPosition.z,forward,"catchup forward");
                Equal(g.Bones[bone].localRotation.Angle,angle*180/Math.PI,"catchup bone angle input");
            }
            for(int w=0;w<g.Wheels.Length;w++)if(g.Data.Wheels[w].Lod==0)
                Equal(g.Wheels[w].localRotation.Angle,g.WheelAngles[w]*180/Math.PI,"catchup wheel angle input");
        }
    }
    static void CheckNativeGate(FactionTrackPackage package)
    {
        // Current native RG permits wheel/tread animation at L0/L1; the
        // other three tracked donors permit L0 only. The caller is unchanged.
        int nativeMask=package.UnitKey=="RI_U06_RAILGUN"?3:1;
        for(int nativeLod=0;nativeLod<4;nativeLod++)for(int unityMask=0;unityMask<8;unityMask++)
        {
            FactionTrackLiveRig rig=Rig(package);Visible(rig,unityMask);rig.Advance(.53,.011);
            bool nativeAllowed=(nativeMask&(1<<nativeLod))!=0;
            if(nativeAllowed)rig.ApplyNative(true);
            int oldWrites=nativeAllowed?2*BoneCount(rig,unityMask)+WheelCount(rig,unityMask):0;
            int newWrites=Count(rig,true)+Count(rig,false);
            Require(newWrites<=oldWrites,"work increased versus Hotfix59 native gate at native="+nativeLod+" Unity mask="+unityMask);
            if(!nativeAllowed)Require(newWrites==0,"native-disabled track animation was reenabled");
        }
        Console.WriteLine("PASS NATIVE GATE "+package.UnitKey+" inheritedMask="+nativeMask+"; 32 native/Unity LOD combinations never exceed Hotfix59 transform writes; native-disabled remains disabled");
    }
    static void Test(FactionTrackPackage package)
    {
        FactionTrackLiveRig rig=Rig(package);Visible(rig,1);rig.ApplyNative(true);AssertWrites(rig,1,1,"L0 full");
        Reset(rig);rig.ApplyNative(true);AssertWrites(rig,0,0,"stationary no repeat");
        // All combinations include Unity crossfade/secondary-camera visibility.
        for(int mask=0;mask<8;mask++)
        {
            Visible(rig,mask);rig.Advance(.37,.013);Reset(rig);rig.ApplyNative(true);
            AssertWrites(rig,mask&3,mask&1,"visibility mask "+mask);
            if((mask&3)==0)foreach(FactionTrackLiveBelt g in rig.Groups)Require(g.Dirty,"frozen phase must stay pending");
        }
        // L1 advances only its own actual FTK bones; no near-only bones/wheels.
        Visible(rig,2);rig.Advance(.47,-.015);Reset(rig);rig.ApplyNative(false);AssertWrites(rig,2,0,"L1 track only");
        Reset(rig);rig.ApplyNative(false);AssertWrites(rig,0,0,"L1 stationary no repeat");
        int oldMid=2*BoneCount(rig,2)+WheelCount(rig,2),newMid=2*BoneCount(rig,2);
        int oldFar=2*BoneCount(rig,4)+WheelCount(rig,4);
        Require(newMid<oldMid&&oldFar>0,"real FTK mid/far write reduction not demonstrated");
        // Far/off advancement stays independent of visual writes, including yaw.
        Visible(rig,4);Reset(rig);
        double[] expected=new double[rig.Groups.Length];for(int g=0;g<expected.Length;g++)expected[g]=rig.Groups[g].Phase;
        for(int tick=0;tick<20;tick++)
        {
            rig.Advance(.61,-.019);rig.ApplyNative(false);
            for(int g=0;g<expected.Length;g++)expected[g]=FactionTrackMath.Wrap(expected[g]+FactionTrackMath.Travel(.61,-.019,rig.Groups[g].Data.LocalRight),rig.Groups[g].Data.Perimeter);
        }
        AssertWrites(rig,0,0,"L2 frozen 20 moving ticks");
        for(int g=0;g<expected.Length;g++)Equal(rig.Groups[g].Phase,expected[g],"phase advanced while frozen");
        Visible(rig,1);Reset(rig);rig.ApplyNative(true);AssertWrites(rig,1,1,"near catchup");AssertNearPose(rig);
        // Native wheel permission can return without a Unity mask change.
        rig.Advance(.23,.009);Reset(rig);rig.ApplyNative(false);AssertWrites(rig,1,0,"native wheels disabled");
        Reset(rig);rig.ApplyNative(true);AssertWrites(rig,0,1,"native wheels reenabled same visibility");AssertNearPose(rig);
        Reset(rig);rig.ApplyNative(true);AssertWrites(rig,0,0,"stationary after catchup");
        // A caller with no native track permission simply skips ApplyNative;
        // the existing Advance path still records travel for the next permit.
        Reset(rig);rig.Advance(.44,-.012);AssertWrites(rig,0,0,"native track off no application");
        rig.ApplyNative(true);AssertWrites(rig,1,1,"native track permission catchup");AssertNearPose(rig);
        Console.WriteLine("PASS "+package.UnitKey+" belts="+rig.BeltCount+" L0writes="+(2*BoneCount(rig,1)+WheelCount(rig,1))+" conditionalL1writes="+oldMid+"->"+newMid+" conditionalL2writes="+oldFar+"->0; all visibility masks, stationary, native wheel gate and catchup passed");
        CheckNativeGate(package);
    }
    static int Main(string[] args)
    {
        try
        {
            Require(args.Length==4,"Expected the four current FTK package paths");
            foreach(string path in args)Test(FactionTrackPackage.Read(path));
            Console.WriteLine("PASS ALL actual production Advance/ApplyNative subset, pure policy, real validated FTK data; transform-write instrumentation only, no Unity rendering or game startup");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
