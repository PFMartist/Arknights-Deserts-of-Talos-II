using System;
using System.Collections.Generic;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // A detached original member visual. No entity, collider, pathfinding,
    // physics or inventory; the surviving squad keeps its original member IDs.
    public sealed class HumanoidCorpse : MonoBehaviour
    {
        static readonly List<HumanoidCorpse> Corpses=new List<HumanoidCorpse>();
        const int MaximumCorpses=64;
        HumanoidBinding PoseBinding;
        HumanoidData Data;
        Transform Member;
        Renderer[] Renderers;
        bool[] Parts;
        Quaternion Standing;
        Vector3 GroundPosition;
        HumanoidPose[] From;
        HumanoidDeathPlayback Playback;
        bool KnownAtDeath,FinalPoseApplied;
        internal static void Spawn(UnitView owner,HumanoidBinding source,HumanoidData data,int member,bool knownAtDeath)
        {
            Transform root=source.MemberRoots[member];if(root==null)return;
            while(Corpses.Count>=MaximumCorpses)
            {HumanoidCorpse oldest=Corpses[0];Corpses.RemoveAt(0);if(oldest!=null)Destroy(oldest.gameObject);}
            GameObject host=new GameObject("EF_Downed_"+data.Actor+"_"+member);host.hideFlags=HideFlags.DontSave;
            host.transform.position=root.position;host.transform.rotation=root.rotation;
            root.parent=host.transform;root.localPosition=Vector3.zero;root.localRotation=Quaternion.identity;
            HumanoidCorpse corpse=host.AddComponent<HumanoidCorpse>();corpse.Member=root;corpse.Data=data;corpse.Standing=host.transform.rotation;corpse.GroundPosition=host.transform.position;
            corpse.PoseBinding=host.AddComponent<HumanoidBinding>();corpse.PoseBinding.NodeCount=data.Nodes.Length;
            corpse.PoseBinding.MemberCount=1;corpse.PoseBinding.Bones=new Transform[data.Nodes.Length];
            Array.Copy(source.Bones,member*data.Nodes.Length,corpse.PoseBinding.Bones,0,data.Nodes.Length);
            corpse.From=new HumanoidPose[data.Nodes.Length];
            for(int i=0;i<data.Nodes.Length;i++)
            {Transform bone=corpse.PoseBinding.Bones[i];corpse.From[i]=new HumanoidPose(bone.localPosition,bone.localRotation,bone.localScale);}
            corpse.Playback=new HumanoidDeathPlayback(data.Death==null?0f:data.Death.Duration);
            // The dead owner may be removed immediately. A known, fixed corpse
            // must not follow its invalidated Visibility or reveal an unseen death.
            corpse.KnownAtDeath=knownAtDeath;
            corpse.Renderers=new Renderer[data.Parts.Length];corpse.Parts=new bool[data.Parts.Length];
            for(int part=0;part<data.Parts.Length;part++)
            {int index=member*data.Parts.Length+part;corpse.Renderers[part]=source.Renderers[index];corpse.Parts[part]=source.PartVisibility==null||source.PartVisibility[index];}
            DetachNativeRendererCaches(owner,corpse.Renderers);
            foreach(Renderer renderer in corpse.Renderers)if(renderer!=null)
            {
                renderer.gameObject.SetActive(true);
                SkinnedMeshRenderer skin=renderer as SkinnedMeshRenderer;
                if(skin!=null){Bounds bounds=skin.localBounds;bounds.Expand(4f);skin.localBounds=bounds;}
            }
            for(int i=0;i<corpse.Renderers.Length;i++)if(corpse.Renderers[i]!=null)corpse.Renderers[i].enabled=corpse.KnownAtDeath&&!UserCamera.IsSensorsModeActive&&corpse.Parts[i];
            Corpses.Add(corpse);
            Runtime.Log("HUMANOID_DEATH_PRESENTATION member="+member+"; actor="+data.Actor+"; sourceClip="+(data.Death==null?"fallback_tilt":data.Death.Name)+"; clipSeconds="+corpse.Playback.ClipSeconds+"; lifetimeSeconds="+corpse.Playback.LifetimeSeconds+"; blendFromActualPose=.12; fixedWorldAnchor=True; inheritedAppearanceRetained=True; knownAtDeath="+corpse.KnownAtDeath+"; simulatedEntityCreated=False; actualPixelsPending=True");
        }
        static void DetachNativeRendererCaches(UnitView owner,Renderer[] detached)
        {
            HashSet<Renderer> removed=new HashSet<Renderer>(detached);
            Renderer[] original=NativeRead.Get(owner,"mGeoRenderObjects") as Renderer[];
            // Despawn may already have released this cache on a whole-squad
            // death. Continue detaching its existing visuals in that case.
            List<Renderer> remaining=new List<Renderer>();if(original!=null)
            {foreach(Renderer renderer in original)if(!removed.Contains(renderer))remaining.Add(renderer);NativeRead.Set(owner,"mGeoRenderObjects",remaining.ToArray());}
            LODGroup group=owner.GetComponent<LODGroup>();
            if(group!=null)
            {
                LOD[] lods=group.GetLODs();
                for(int lod=0;lod<lods.Length;lod++)
                {
                    List<Renderer> live=new List<Renderer>();foreach(Renderer renderer in lods[lod].renderers)if(!removed.Contains(renderer))live.Add(renderer);
                    lods[lod].renderers=live.ToArray();
                }
                group.SetLODs(lods);if(remaining.Count>0)group.RecalculateBounds();
            }
        }
        void LateUpdate()
        {
            if(Member==null||Data==null){Destroy(gameObject);return;}
            Playback.Step(Runtime.PresentationDeltaTime);
            if(Data.Death!=null)
            {
                if(!FinalPoseApplied)
                {
                    HumanoidModel.ApplyPose(PoseBinding,Data,0,Data.Death,Playback.PoseTime,From,Playback.Blend);
                    if(Playback.PoseTime>=Data.Death.Duration){FinalPoseApplied=true;From=null;}
                }
            }
            else
            {
                // Explicit fallback pose for an asset without a recovered death
                // clip. This is a display transition, not a claimed source anim.
                float t=Mathf.Clamp01(Playback.Elapsed/HumanoidDeathPlayback.FallbackFallSeconds);t=t*t*(3f-2f*t);
                transform.rotation=Standing*Quaternion.AngleAxis(88f*t,Vector3.right);
                transform.position=GroundPosition+Vector3.up*(.16f*t);
            }
            bool visible=KnownAtDeath&&!UserCamera.IsSensorsModeActive;
            for(int i=0;i<Renderers.Length;i++)if(Renderers[i]!=null)Renderers[i].enabled=visible&&Parts[i];
            if(Playback.Finished)Destroy(gameObject);
        }
        void OnDestroy(){Corpses.Remove(this);}
        public static void ResetAll(){foreach(HumanoidCorpse item in Corpses.ToArray())if(item!=null)DestroyImmediate(item.gameObject);Corpses.Clear();}
    }
}
