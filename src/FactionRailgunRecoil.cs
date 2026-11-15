using System;
using System.Collections.Generic;
using EndfieldDoK.ModelIntake;
using UnityEngine;
namespace EndfieldDoK.MapUnitLab
{
    public sealed class FactionRailgunRecoil
    {
        readonly RailgunRecoilState State=new RailgunRecoilState();
        Transform[] Nodes;Vector3[] Rest;double LastOffset=-1;
        public static FactionRailgunRecoil Bind(FactionModelBinding binding,int sequence)
        {
            if(binding.UnitKey!="RI_U06_RAILGUN")return null;
            List<Transform> nodes=new List<Transform>();
            for(int i=0;i<binding.Nodes.Length;i++)if(binding.Names[i].EndsWith("::PIVOT_R06_GunRecoil_export",StringComparison.Ordinal))
            {
                Transform t=binding.Nodes[i];if(t==null||!t.IsChildOf(binding.GeometryRoot))throw new InvalidOperationException("Railgun recoil node ownership");nodes.Add(t);
            }
            if(nodes.Count==0)return null;
            if(nodes.Count!=3)throw new InvalidOperationException("Railgun recoil requires all three LOD nodes");
            FactionRailgunRecoil rig=new FactionRailgunRecoil();rig.Nodes=nodes.ToArray();rig.Rest=new Vector3[3];
            for(int i=0;i<3;i++){rig.Rest[i]=Vector3.zero;rig.Nodes[i].localPosition=Vector3.zero;}
            rig.State.Initialize(sequence);return rig;
        }
        public void Step(int sequence,float deltaSeconds,bool shownAlive)
        {
            double offset=State.Step(sequence,deltaSeconds>0?deltaSeconds:0,shownAlive);if(Math.Abs(offset-LastOffset)<1e-8)return;LastOffset=offset;
            // All LOD nodes remain synchronized. Native FX resolves the LOD0
            // muzzle even when another renderer is visible.
            for(int i=0;i<Nodes.Length;i++)if(Nodes[i]!=null)Nodes[i].localPosition=Rest[i]+Vector3.back*(float)offset;
        }
    }
}
