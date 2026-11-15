// Passive diagnostic helper. No simulation commands, Time writes or bone writes.
// Glue, after HumanoidDriver's member-pose loop:
// HumanoidAnimationAudit.Observe(this,View,Binding,Data,Clock,Moving,dt);
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BBI.Game.Simulation;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    internal static class HumanoidAnimationAudit
    {
        sealed class Sample
        {
            public float LastReport=-100f,LastClock,ClockTravel,DtTotal,AngleTravel,PositionTravel;
            public Quaternion LastRotation;
            public Vector3 LastPosition;
            public int Bone,Calls;
            public bool PreviousReady;
            public float RawAngleSpan,RawPositionSpan;
            public HumanoidClip Clip;
            public int BindingReports;
        }
        static readonly Dictionary<int,Sample> Samples=new Dictionary<int,Sample>();
        sealed class ClipSelection
        {
            public HumanoidData Data;
            public int Bone;
            public float AngleSpan,PositionSpan;
        }
        // Clip tracks/masks and the active-node table are immutable after load.
        // All squad instances can reuse the exact same diagnostic selection.
        static readonly Dictionary<HumanoidClip,ClipSelection> ClipSelections=new Dictionary<HumanoidClip,ClipSelection>();
        static readonly FieldInfo PresentationSimField=typeof(Runtime).GetField("PresentationSim",BindingFlags.Static|BindingFlags.NonPublic);

        internal static void Reset() { Samples.Clear();ClipSelections.Clear(); }

        internal static void Observe(HumanoidDriver driver,UnitView view,HumanoidBinding binding,HumanoidData data,float[] clocks,bool[] moving,float effectiveDt)
        {
            if(!Runtime.DetailedDiagnosticsEnabled)return;
            // The caller runs only after ApplyPose. Observe the first member to
            // keep overhead bounded; all members share the same clock source.
            if(driver==null||view==null||binding==null||data==null||clocks==null||clocks.Length==0||data.ActiveNodes.Length==0)return;
            Sample s;int key=driver.GetInstanceID();
            if(!Samples.TryGetValue(key,out s)){s=new Sample();Samples.Add(key,s);}
            HumanoidClip clip=driver.CurrentSourceClip??(moving[0]?data.Walk:data.Idle);
            if(s.Clip!=clip)
            {
                s.Clip=clip;s.Bone=FindMostAnimatedBone(data,clip,out s.RawAngleSpan,out s.RawPositionSpan);
                s.PreviousReady=false;
                if(data.Actor=="chr_0004_pelica"&&s.BindingReports++<16)VerifySkinBinding(view,binding,data,clip);
            }
            Transform bone=binding.Bones[s.Bone];
            if(s.PreviousReady)
            {
                float change=clocks[0]-s.LastClock;
                if(change<0f)change+=clip.Duration;
                s.ClockTravel+=Mathf.Max(0f,change);
                s.AngleTravel+=Quaternion.Angle(s.LastRotation,bone.localRotation);
                s.PositionTravel+=(bone.localPosition-s.LastPosition).magnitude;
            }
            s.LastClock=clocks[0];s.LastRotation=bone.localRotation;s.LastPosition=bone.localPosition;
            s.PreviousReady=true;s.DtTotal+=Mathf.Max(0f,effectiveDt);s.Calls++;
            float now=Time.realtimeSinceStartup;if(now-s.LastReport<5f)return;
            SimController sim=PresentationSimField==null?null:PresentationSimField.GetValue(null) as SimController;
            string simScale=sim==null?"missing":F(BBI.Core.Utility.FixedPoint.Fixed64.UnsafeFloatValue(sim.TimeScale));
            Runtime.Log("HUMANOID_ANIMATION_CLOCK entity="+view.EntityID+"; actor="+data.Actor+
                "; clip="+clip.Name+"; moving="+moving[0]+"; driverPaused="+driver.Paused+
                "; simPaused="+(sim==null?"missing":sim.Paused.ToString())+"; simTimeScale="+simScale+
                "; unityTimeScale="+F(Time.timeScale)+"; unityDelta="+F(Time.deltaTime)+
                "; unityUnscaledDelta="+F(Time.unscaledDeltaTime)+"; effectiveDt="+F(effectiveDt)+
                "; observeCalls="+s.Calls+"; effectiveDtAccum="+F(s.DtTotal)+
                "; clipClock="+F(clocks[0])+"; clockTravelAccum="+F(s.ClockTravel)+
                "; clipDuration="+F(clip.Duration)+"; rate="+F(clip.Rate)+"; samples="+clip.Samples+
                "; activeNodes="+data.ActiveNodes.Length+"; sampledBone="+data.Nodes[s.Bone].Name+
                "; rawClipAngleSpanDegrees="+F(s.RawAngleSpan)+"; rawClipPositionSpanMetres="+F(s.RawPositionSpan)+
                "; actualLocalAngleTravelDegrees="+F(s.AngleTravel)+"; actualLocalPositionTravelMetres="+F(s.PositionTravel)+
                "; passiveOnly=True; boneAndClockWrites=False; actualPixelAcceptancePending=True");
            s.LastReport=now;s.Calls=0;s.DtTotal=s.ClockTravel=s.AngleTravel=s.PositionTravel=0f;
        }

        static int FindMostAnimatedBone(HumanoidData data,HumanoidClip clip,out float angleSpan,out float positionSpan)
        {
            ClipSelection selection;
            if(ClipSelections.TryGetValue(clip,out selection)&&object.ReferenceEquals(selection.Data,data))
            {angleSpan=selection.AngleSpan;positionSpan=selection.PositionSpan;return selection.Bone;}
            int bone=CalculateMostAnimatedBone(data,clip,out angleSpan,out positionSpan);
            ClipSelections[clip]=new ClipSelection{Data=data,Bone=bone,AngleSpan=angleSpan,PositionSpan=positionSpan};
            return bone;
        }
        static int CalculateMostAnimatedBone(HumanoidData data,HumanoidClip clip,out float angleSpan,out float positionSpan)
        {
            int best=data.ActiveNodes[0];float bestScore=-1f;angleSpan=positionSpan=0f;
            foreach(int node in data.ActiveNodes)
            {
                // Measure the visible body instead of selecting a moving
                // funnel or invisible IK target as proof of body animation.
                if(data.Actor=="chr_0004_pelica"&&!BodyBone(data.Nodes[node].Name))continue;
                int track=clip.NodeTracks[node];if(track<0)continue;
                int bit=0x80>>(track&7),block=track>>3;
                bool rotation=(clip.Masks[clip.Stride+block]&bit)!=0,position=(clip.Masks[block]&bit)!=0;
                int first=track*10;Quaternion q=rotation?Q(clip.Qvv,first):Quaternion.identity;
                Vector3 p=position?P(clip.Qvv,first+4):Vector3.zero;float a=0f,d=0f;
                for(int sample=1;sample<clip.Samples;sample++)
                {
                    int index=(sample*clip.Tracks+track)*10;
                    if(rotation)a=Mathf.Max(a,Quaternion.Angle(q,Q(clip.Qvv,index)));
                    if(position)d=Mathf.Max(d,(P(clip.Qvv,index+4)-p).magnitude);
                }
                float score=a+d*180f;
                if(score>bestScore){bestScore=score;best=node;angleSpan=a;positionSpan=d;}
            }
            return best;
        }
        static bool BodyBone(string name)
        {return name.EndsWith("/Bip001_Pelvis",StringComparison.Ordinal)||name.EndsWith("/Bip001_Spine2",StringComparison.Ordinal)||name.EndsWith("/Bip001_Head",StringComparison.Ordinal)||name.EndsWith("/Bip001_L_UpperArm",StringComparison.Ordinal)||name.EndsWith("/Bip001_R_UpperArm",StringComparison.Ordinal)||name.EndsWith("/Bip001_L_Hand",StringComparison.Ordinal)||name.EndsWith("/Bip001_R_Hand",StringComparison.Ordinal);}
        static void VerifySkinBinding(UnitView view,HumanoidBinding binding,HumanoidData data,HumanoidClip clip)
        {
            int checkedBones=0,mismatches=0;
            for(int part=0;part<data.Parts.Length;part++)
            {
                SkinnedMeshRenderer renderer=binding.Renderers[part];if(renderer==null){mismatches++;continue;}
                Transform[] actual=renderer.bones;HumanoidPart p=data.Parts[part];
                if(actual.Length!=p.PaletteCount){mismatches++;continue;}
                for(int j=0;j<actual.Length;j++)
                {checkedBones++;if(actual[j]!=binding.Bones[data.PaletteNodes[p.PaletteStart+j]])mismatches++;}
            }
            Runtime.Log("PERLICA_SKIN_BINDING_READBACK entity="+view.EntityID+"; clip="+clip.Name+"; checkedBones="+checkedBones+"; mismatches="+mismatches+"; passiveOnly=True; rendererAndPoseBonesAreSame="+(mismatches==0));
        }
        static Quaternion Q(float[] v,int i)
        {
            float length=Mathf.Sqrt(v[i]*v[i]+v[i+1]*v[i+1]+v[i+2]*v[i+2]+v[i+3]*v[i+3]);
            if(length<1e-8f)return Quaternion.identity;
            return new Quaternion(v[i]/length,v[i+1]/length,v[i+2]/length,v[i+3]/length);
        }
        static Vector3 P(float[] v,int i){return new Vector3(v[i],v[i+1],v[i+2]);}
        static string F(float value){return value.ToString("G9",CultureInfo.InvariantCulture);}
    }
}
