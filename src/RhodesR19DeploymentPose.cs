using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Uses only existing carrier presentation host. R19 parents carry the real
    // authored lift/transport motion; native aim/recoil ancestors remain native.
    internal sealed class RhodesR19DeploymentPose
    {
        sealed class Node {internal Transform Bone;internal V17NodeCurve Curve;internal Vector3 Position;internal Quaternion Rotation;internal bool SharedDeck,BoundInitialPose,FinalInitialPose;}
        readonly Node[] Initial,Aft,Aviation;
        int LastBranch=-1;bool InitialFinal,AftFinal;float LastInitial=float.NaN,LastAft=float.NaN,LastAviation=float.NaN;
        internal RhodesR19DeploymentPose(RhodesCarrierBinding binding)
        {
            RhodesR19DeploymentData data=RhodesR19DeploymentData.Load();
            Transform module=binding.FindNode("PIVOT_GN_ModuleLift"),cover=binding.FindNode("PIVOT_GN_AFT_CoverLift"),aft=binding.FindNode("PIVOT_GN_AFT_Lift");
            if(module==null||cover==null||cover.parent!=module||aft==null||aft.parent!=module)
                throw new InvalidOperationException("R19 aft cover must inherit the main module lift directly");
            Initial=Bind(binding,data.Clips["GN_INIT"]);Aft=Bind(binding,data.Clips["GN_AFT"]);Aviation=Bind(binding,data.Clips["AV_DEPLOY"]);
        }
        static Node[] Bind(RhodesCarrierBinding binding,Dictionary<string,V17NodeCurve> curves)
        {
            List<Node> nodes=new List<Node>();
            foreach(KeyValuePair<string,V17NodeCurve> item in curves)
            {
                Transform bone=binding.FindNode(item.Key);if(bone==null||!bone.IsChildOf(binding.GeometryRoot))throw new InvalidOperationException("R19 motion owned bone missing: "+item.Key);
                nodes.Add(new Node{Bone=bone,Curve=item.Value,Position=bone.localPosition,Rotation=bone.localRotation,
                    SharedDeck=InitialTrackIsSharedDeck(item.Key),BoundInitialPose=InitialTrackHoldsBoundPose(item.Key),FinalInitialPose=InitialTrackHoldsFinalStowedPose(item.Key)});
            }
            return nodes.ToArray();
        }
        static void Set(Node[] nodes,float seconds){foreach(Node node in nodes)node.Curve.Apply(node.Bone,seconds);}
        internal static bool InitialTrackIsSharedDeck(string name)
        {return name.StartsWith("V08_PIVOT_SHUTTER_DECK_",StringComparison.Ordinal);}
        internal static bool WriteInitialTrackInBranch(int branch,bool sharedDeck)
        {return branch==2||!sharedDeck;}
        internal static bool InitialTrackHoldsBoundPose(string name)
        {return name=="PIVOT_GN_AFT_CoverLift";}
        internal static bool InitialTrackHoldsFinalStowedPose(string name)
        {return name=="PIVOT_GN_AFT_Lift";}
        void SetInitial(float seconds,int branch)
        {
            foreach(Node node in Initial)
            {
                if(!WriteInitialTrackInBranch(branch,node.SharedDeck))continue;
                // GN_INIT's authored local counter-motion cancels most of the
                // parent's rise. Keep only this cover at its bound local pose
                // so the complete cover follows the first-stage module lift.
                if(node.BoundInitialPose){node.Bone.localPosition=node.Position;node.Bone.localRotation=node.Rotation;}
                // The rear gun must start fully stowed beneath that moving
                // cover. Its terminal GN_INIT pose equals GN_AFT's first pose;
                // no extra drop, hidden renderer or altered second-stage curve.
                else node.Curve.Apply(node.Bone,node.FinalInitialPose?21.5f:seconds);
            }
        }
        static float Seconds(int start,int tick,float sub)
        {return RhodesDeploymentProfile.AgeTicks(start,tick,sub)*RhodesDeploymentProfile.NativeTickSeconds;}
        internal void Apply(RhodesDeploymentSnapshot state,int tick,float sub)
        {
            if(LastBranch!=state.Branch)
            {
                InitialFinal=AftFinal=false;LastInitial=LastAft=LastAviation=float.NaN;
                // Hidden opposing hardware stays at its author-tested stowed pose.
                // Base/aviation shared deck stays exclusively with the r07 LUT,
                // including settled spawns where that LUT no longer rewrites.
                if(state.Branch!=2)SetInitial(0f,state.Branch);if(state.Branch!=1)Set(Aviation,0f);
                LastBranch=state.Branch;
            }
            if(state.Branch==2)
            {
                float initial=state.ForeReady?21.5f:Mathf.Min(21.5f,Seconds(state.StartTick,tick,sub));
                if(!InitialFinal&&initial!=LastInitial){SetInitial(initial,state.Branch);LastInitial=initial;InitialFinal=initial>=21.5f;}
                // Aft clip does not own any FORE or module/platform bone. Its
                // cover curve starts at the bound local pose used above.
                if(state.ForeReady&&state.TargetLevel>=2)
                {
                    float aft=state.AftReady?15.5f:Mathf.Min(15.5f,Seconds(state.AftStartTick,tick,sub));
                    if(!AftFinal&&aft!=LastAft){Set(Aft,aft);LastAft=aft;AftFinal=aft>=15.5f;}
                }
            }
            else if(state.Branch==1)
            {
                float aviation=state.Kind==1?Mathf.Min(24f,Seconds(state.StartTick,tick,sub)):24f;
                if(aviation!=LastAviation){Set(Aviation,aviation);LastAviation=aviation;}
            }
        }
        internal void Restore()
        {
            Restore(Initial);Restore(Aft);Restore(Aviation);LastBranch=-1;InitialFinal=AftFinal=false;LastInitial=LastAft=LastAviation=float.NaN;
        }
        static void Restore(Node[] nodes)
        {foreach(Node node in nodes)if(node.Bone!=null){node.Bone.localPosition=node.Position;node.Bone.localRotation=node.Rotation;}}
    }
}
