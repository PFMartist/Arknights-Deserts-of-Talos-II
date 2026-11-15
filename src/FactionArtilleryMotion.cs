using System;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Cached three-bone presentation. No mesh/material creation, searches,
    // damage, firing schedule or independent gameplay clock in Step.
    public sealed class FactionArtilleryMotion
    {
        Transform Recoil,Wedge,Rammer;Vector3 RecoilRest,WedgeRest,RammerRest;
        int Sequence;float Age=100f,StartOffset,LastOffset=-1f,LastWedge=-1f,LastRammer=-1f;
        const float Stroke=.62f,Kick=1f/12f,HoldUntil=.125f,Return=.5f;
        public static FactionArtilleryMotion Bind(FactionModelBinding binding,int sequence)
        {
            if(binding.UnitKey!=FactionUnits.ArtilleryKey)return null;
            FactionArtilleryMotion rig=new FactionArtilleryMotion();rig.Sequence=sequence;
            rig.Recoil=Marker(binding,"PIVOT_U09_GunRecoil");rig.Wedge=Marker(binding,"PIVOT_U09_BreechWedge");rig.Rammer=Marker(binding,"PIVOT_U09_LoadRammer");
            rig.RecoilRest=rig.Recoil.localPosition;rig.WedgeRest=rig.Wedge.localPosition;rig.RammerRest=rig.Rammer.localPosition;return rig;
        }
        static Transform Marker(FactionModelBinding binding,string name)
        {
            Transform found=null;for(int i=0;i<binding.Nodes.Length;i++)if(binding.Names[i]==name)
            {if(found!=null)throw new InvalidOperationException("Duplicate U09 motion node "+name);found=binding.Nodes[i];}
            if(found==null||!found.IsChildOf(binding.GeometryRoot))throw new InvalidOperationException("U09 motion node ownership "+name);return found;
        }
        static float Smooth(float t){t=Mathf.Clamp01(t);return t*t*(3f-2f*t);}
        float Offset()
        {
            if(Age<Kick){float t=Mathf.Clamp01(Age/Kick);return StartOffset+(Stroke-StartOffset)*(1f-(1f-t)*(1f-t)*(1f-t));}
            if(Age<HoldUntil)return Stroke;return Stroke*(1f-Smooth((Age-HoldUntil)/Return));
        }
        public void Step(int sequence,float deltaSeconds,bool shownAlive)
        {
            int change=unchecked(sequence-Sequence);Sequence=sequence;
            if(!shownAlive||change<0){Age=100f;StartOffset=0f;}
            else
            {
                if(change>0){StartOffset=Offset();Age=0f;}
                if(deltaSeconds>0f)Age=Mathf.Min(100f,Age+deltaSeconds);
            }
            float offset=Offset();float wedge=Age<.625f?Smooth((Age-.125f)/.25f):1f-Smooth((Age-.625f)/.5f);
            float rammer=Age<.75f?Smooth((Age-.375f)/.375f):1f-Smooth((Age-.75f)/.375f);
            if(!shownAlive){wedge=rammer=0f;}
            if(Mathf.Abs(offset-LastOffset)>1e-7f){LastOffset=offset;if(Recoil!=null)Recoil.localPosition=RecoilRest+Vector3.back*offset;}
            if(Mathf.Abs(wedge-LastWedge)>1e-7f){LastWedge=wedge;if(Wedge!=null)Wedge.localPosition=WedgeRest+Vector3.down*(.65f*wedge);}
            if(Mathf.Abs(rammer-LastRammer)>1e-7f){LastRammer=rammer;if(Rammer!=null)Rammer.localPosition=RammerRest+Vector3.forward*(.82f*rammer);}
        }
        public void Reset(){Step(Sequence,0f,false);}
    }
}
