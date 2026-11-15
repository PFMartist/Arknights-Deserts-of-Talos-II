using System;
using System.Collections.Generic;
using EndfieldDoK.ModelIntake;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Unity clones this small identity tag; live mutable phases are never stored
    // on the prefab or in its serialization graph.
    public sealed class FactionTrackRigTag:MonoBehaviour
    {
        public string CacheKey,UnitKey;
    }
    public sealed class FactionTrackLoadResult
    {
        public Renderer[] Renderers;
        public Renderer[][] LodRenderers;
    }
    public static class FactionTrackLoader
    {
        sealed class Source
        {
            internal FactionTrackPackage Package;
            internal Mesh[][] Meshes;
            internal Quaternion[][] WheelRest;
            internal GameObject Owner;
        }
        static readonly Dictionary<string,Source> Sources=new Dictionary<string,Source>(StringComparer.Ordinal);
        static int NextTemplate;
        public static void ClearCaches(){Sources.Clear();}
        public static void ReleaseModel(RhodesLoadedModel owner)
        {
            if(owner==null||owner.Root==null)return;
            FactionTrackRigTag tag=owner.Root.GetComponent<FactionTrackRigTag>();if(tag==null)return;
            string key=tag.CacheKey;Source s;
            if(Sources.TryGetValue(key,out s)&&object.ReferenceEquals(s.Owner,owner.Root))Sources.Remove(key);
        }
        internal static string SpaceName(int group){return "EF_FTK_SPACE_"+group;}
        internal static string BoneName(int bone){return "EF_FTK_BONE_"+bone;}
        internal static string SkinName(int group,int lod){return "GEO_FTK_"+group+"_L"+lod;}
        public static FactionTrackLoadResult Load(RhodesLoadedModel owner,Material[] materials,int layer)
        {
            FactionTrackPackage p=owner.Package.Tracks;if(p==null)throw new InvalidOperationException("Validated FTK absent");
            string key=p.UnitKey+"/"+p.ModelSha256+"/"+(++NextTemplate);Source source=new Source{Package=p,Meshes=new Mesh[p.Belts.Length][],WheelRest=new Quaternion[p.Belts.Length][],Owner=owner.Root};
            List<Renderer> all=new List<Renderer>();List<Renderer>[] lods={new List<Renderer>(),new List<Renderer>(),new List<Renderer>()};
            for(int g=0;g<p.Belts.Length;g++)
            {
                FactionTrackBelt b=p.Belts[g];Transform pivot=owner.FindNode(b.Node);
                if(pivot==null||!pivot.IsChildOf(owner.Root.transform))throw new InvalidOperationException("FTK template pivot ownership");
                source.WheelRest[g]=new Quaternion[b.Wheels.Length];
                for(int w=0;w<b.Wheels.Length;w++)source.WheelRest[g][w]=owner.FindNode(b.Wheels[w].Node).localRotation;
                GameObject space=new GameObject(SpaceName(g));space.layer=layer;space.transform.parent=pivot;space.transform.localPosition=Vector3.zero;space.transform.localRotation=Quaternion.identity;space.transform.localScale=Vector3.one;
                Transform[] bones=new Transform[b.Phases.Length];Matrix4x4[] bindposes=new Matrix4x4[bones.Length];
                for(int k=0;k<bones.Length;k++)
                {
                    double f,u,a;FactionTrackMath.Sample(b,b.Phases[k],out f,out u,out a);
                    Vector3 pos=new Vector3(0f,(float)u,(float)f);Quaternion rot=Quaternion.AngleAxis((float)(a*180.0/Math.PI),Vector3.right);
                    GameObject bone=new GameObject(BoneName(k));bone.layer=layer;bone.transform.parent=space.transform;bone.transform.localPosition=pos;bone.transform.localRotation=rot;bone.transform.localScale=Vector3.one;
                    bones[k]=bone.transform;bindposes[k]=Matrix4x4.TRS(pos,rot,Vector3.one).inverse;
                }
                source.Meshes[g]=new Mesh[3];
                foreach(FactionTrackSkin s in b.Skins)
                {
                    int count=s.Bones.Length;Vector3[] verts=new Vector3[count],normals=new Vector3[count];Vector2[] uv=new Vector2[count],uv1=new Vector2[count];BoneWeight[] weights=new BoneWeight[count];Color32[] colours=new Color32[count];
                    for(int i=0;i<count;i++)
                    {
                        int at=i*10;float[] v=s.Vertices;verts[i]=new Vector3(v[at],v[at+1],v[at+2]);normals[i]=new Vector3(v[at+3],v[at+4],v[at+5]);uv[i]=new Vector2(v[at+6],v[at+7]);uv1[i]=new Vector2(v[at+8],v[at+9]);colours[i]=new Color32(255,255,255,255);
                        BoneWeight weight=new BoneWeight();weight.boneIndex0=s.Bones[i];weight.weight0=1f;weights[i]=weight;
                    }
                    Mesh mesh=new Mesh();mesh.name="EF_FTK_"+p.UnitKey+"_"+g+"_L"+s.Lod;mesh.hideFlags=HideFlags.HideAndDontSave;owner.Owned.Add(mesh);
                    mesh.vertices=verts;mesh.normals=normals;mesh.uv=uv;mesh.uv2=uv1;mesh.colors32=colours;mesh.boneWeights=weights;mesh.bindposes=bindposes;mesh.subMeshCount=s.Submeshes.Length;
                    Material[] slots=new Material[s.Submeshes.Length];
                    for(int i=0;i<slots.Length;i++){mesh.SetTriangles(s.Submeshes[i].Indices,i);slots[i]=materials[s.Submeshes[i].Material];}
                    mesh.RecalculateBounds();source.Meshes[g][s.Lod]=mesh;
                    GameObject geometry=new GameObject(SkinName(g,s.Lod));geometry.layer=layer;geometry.transform.parent=space.transform;geometry.transform.localPosition=Vector3.zero;geometry.transform.localRotation=Quaternion.identity;geometry.transform.localScale=Vector3.one;
                    SkinnedMeshRenderer renderer=geometry.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=mesh;renderer.sharedMaterials=slots;renderer.bones=bones;renderer.rootBone=space.transform;renderer.quality=SkinQuality.Bone1;renderer.updateWhenOffscreen=false;renderer.localBounds=Bounds(b.Bounds);
                    all.Add(renderer);lods[s.Lod].Add(renderer);
                    for(int c=0;c<8;c++)
                    {
                        Bounds bounds=renderer.localBounds;Vector3 pt=bounds.center+new Vector3((c&1)==0?-bounds.extents.x:bounds.extents.x,(c&2)==0?-bounds.extents.y:bounds.extents.y,(c&4)==0?-bounds.extents.z:bounds.extents.z);
                        Bounds combined=owner.PresentationBounds;combined.Encapsulate(owner.Root.transform.InverseTransformPoint(space.transform.TransformPoint(pt)));owner.PresentationBounds=combined;
                    }
                }
            }
            FactionTrackRigTag tag=owner.Root.AddComponent<FactionTrackRigTag>();tag.CacheKey=key;tag.UnitKey=p.UnitKey;Sources.Add(key,source);
            return new FactionTrackLoadResult{Renderers=all.ToArray(),LodRenderers=new Renderer[][]{lods[0].ToArray(),lods[1].ToArray(),lods[2].ToArray()}};
        }
        static Bounds Bounds(float[] v){Vector3 lo=new Vector3(v[0],v[1],v[2]),hi=new Vector3(v[3],v[4],v[5]);return new Bounds((lo+hi)*.5f,hi-lo);}
        public static FactionTrackLiveRig RebindLive(Transform root,string unitKey)
        {
            FactionTrackRigTag tag=root.GetComponent<FactionTrackRigTag>();if(tag==null)return null;
            Source source;if(tag.UnitKey!=unitKey||!Sources.TryGetValue(tag.CacheKey,out source))throw new InvalidOperationException("FTK live template identity missing");
            Dictionary<string,Transform> nodes=new Dictionary<string,Transform>(StringComparer.Ordinal);
            foreach(Transform t in root.GetComponentsInChildren<Transform>(true))
                if(t.name.StartsWith("L",StringComparison.Ordinal))
                {if(nodes.ContainsKey(t.name))throw new InvalidOperationException("FTK duplicate live source node");nodes.Add(t.name,t);}
            FactionTrackLiveRig live=new FactionTrackLiveRig();live.Groups=new FactionTrackLiveBelt[source.Package.Belts.Length];
            for(int g=0;g<live.Groups.Length;g++)
            {
                FactionTrackBelt b=source.Package.Belts[g];Transform pivot;
                if(!nodes.TryGetValue(b.Node,out pivot)||!pivot.IsChildOf(root))throw new InvalidOperationException("FTK live pivot missing");
                Transform space=pivot.Find(SpaceName(g));if(space==null||space.parent!=pivot)throw new InvalidOperationException("FTK cloned rig space missing");
                FactionTrackLiveBelt state=new FactionTrackLiveBelt();state.Data=b;state.Bones=new Transform[b.Phases.Length];state.Skins=new SkinnedMeshRenderer[3];state.UsedBones=new bool[3][];
                for(int k=0;k<state.Bones.Length;k++)
                {
                    Transform t=space.Find(BoneName(k));if(t==null||t.parent!=space||!t.IsChildOf(root))throw new InvalidOperationException("FTK live bone ownership");state.Bones[k]=t;
                    double f,u,a;FactionTrackMath.Sample(b,b.Phases[k],out f,out u,out a);t.localPosition=new Vector3(0f,(float)u,(float)f);t.localRotation=Quaternion.AngleAxis((float)(a*180.0/Math.PI),Vector3.right);
                }
                for(int lod=0;lod<3;lod++)
                {
                    Transform t=space.Find(SkinName(g,lod));SkinnedMeshRenderer skin=t==null?null:t.GetComponent<SkinnedMeshRenderer>();
                    if(skin==null||skin.sharedMesh!=source.Meshes[g][lod]||!skin.transform.IsChildOf(root))throw new InvalidOperationException("FTK live skin identity");
                    skin.bones=(Transform[])state.Bones.Clone();skin.rootBone=space;skin.localBounds=Bounds(b.Bounds);state.Skins[lod]=skin;
                    bool[] used=new bool[state.Bones.Length];foreach(int bone in b.Skins[lod].Bones)used[bone]=true;state.UsedBones[lod]=used;
                }
                state.NativeUsedBones=new bool[state.Bones.Length];
                state.Wheels=new Transform[b.Wheels.Length];state.WheelRest=new Quaternion[b.Wheels.Length];state.WheelAngles=new double[b.Wheels.Length];
                for(int k=0;k<b.Wheels.Length;k++)
                {
                    Transform t;if(!nodes.TryGetValue(b.Wheels[k].Node,out t)||!t.IsChildOf(root))throw new InvalidOperationException("FTK live wheel ownership");
                    state.Wheels[k]=t;state.WheelRest[k]=source.WheelRest[g][k];t.localRotation=state.WheelRest[k];live.OwnedWheels.Add(t);
                }
                live.Groups[g]=state;
            }
            return live;
        }
    }
    public sealed class FactionTrackLiveBelt
    {
        internal FactionTrackBelt Data;
        internal Transform[] Bones,Wheels;
        internal Quaternion[] WheelRest;
        internal double[] WheelAngles;
        internal double Phase;
        internal SkinnedMeshRenderer[] Skins;
        internal bool[][] UsedBones;
        internal bool[] NativeUsedBones;
        internal int NativeVisibleMask=-1;
        internal bool Dirty=true;
        internal int LastLod=-1;
    }
    public sealed class FactionTrackLiveRig
    {
        internal FactionTrackLiveBelt[] Groups;
        internal readonly HashSet<Transform> OwnedWheels=new HashSet<Transform>();
        public int BeltCount {get{return Groups.Length;}}
        public bool OwnsWheel(Transform wheel){return OwnedWheels.Contains(wheel);}
        public void Advance(double forwardDistance,double yawRadians)
        {
            foreach(FactionTrackLiveBelt g in Groups)
            {
                double travel=FactionTrackMath.Travel(forwardDistance,yawRadians,g.Data.LocalRight);
                if(Math.Abs(travel)<1e-10)continue;
                g.Phase=FactionTrackMath.Wrap(g.Phase+travel,g.Data.Perimeter);g.Dirty=true;
                for(int i=0;i<g.WheelAngles.Length;i++)g.WheelAngles[i]=FactionTrackMath.Wrap(g.WheelAngles[i]+travel/g.Data.Wheels[i].Radius,Math.PI*2);
            }
        }
        public void Apply(int lod)
        {
            if(lod<0||lod>2)return;
            foreach(FactionTrackLiveBelt g in Groups)
            {
                SkinnedMeshRenderer skin=g.Skins[lod];if(skin==null||!skin.enabled||(!skin.isVisible&&g.LastLod==lod)||(!g.Dirty&&g.LastLod==lod))continue;
                // One shared skeleton per physical belt. A far LOD updates only
                // shoe stations; intermediate pins catch up when a near LOD returns.
                for(int k=0;k<g.Bones.Length;k++)
                {
                    if(!g.UsedBones[lod][k])continue;
                    double f,u,a;FactionTrackMath.Sample(g.Data,g.Data.Phases[k]+g.Phase,out f,out u,out a);
                    g.Bones[k].localPosition=new Vector3(0f,(float)u,(float)f);g.Bones[k].localRotation=Quaternion.AngleAxis((float)(a*180.0/Math.PI),Vector3.right);
                }
                for(int k=0;k<g.Wheels.Length;k++)if(g.Data.Wheels[k].Lod==lod)
                    g.Wheels[k].localRotation=Quaternion.AngleAxis((float)(g.WheelAngles[k]*180.0/Math.PI),Vector3.right)*g.WheelRest[k];
                g.Dirty=false;g.LastLod=lod;
            }
        }
        public void ApplyNative()
        {
            // Called only when the inherited native wheel/tread actuation mask
            // permits it. Unity owns mesh selection; renderer.isVisible is only
            // a work-culling hint (including other cameras/shadows), never a
            // replacement LOD selector. A stable stationary belt has no writes.
            foreach(FactionTrackLiveBelt g in Groups)
            {
                int visibleMask=0;
                for(int lod=0;lod<g.Skins.Length;lod++)
                    if(g.Skins[lod]!=null&&g.Skins[lod].enabled&&g.Skins[lod].isVisible)visibleMask|=1<<lod;
                bool write=NativeUnitLodAdapter.NeedsActuationWrites(g.Dirty,g.NativeVisibleMask,visibleMask);
                if(visibleMask!=g.NativeVisibleMask)
                {
                    Array.Clear(g.NativeUsedBones,0,g.NativeUsedBones.Length);
                    for(int lod=0;lod<g.UsedBones.Length;lod++)if((visibleMask&(1<<lod))!=0)
                        for(int bone=0;bone<g.NativeUsedBones.Length;bone++)
                            if(g.UsedBones[lod][bone])g.NativeUsedBones[bone]=true;
                    g.NativeVisibleMask=visibleMask;
                }
                if(!write)continue;
                for(int k=0;k<g.Bones.Length;k++)
                {
                    if(!g.NativeUsedBones[k])continue;
                    double f,u,a;FactionTrackMath.Sample(g.Data,g.Data.Phases[k]+g.Phase,out f,out u,out a);
                    g.Bones[k].localPosition=new Vector3(0f,(float)u,(float)f);g.Bones[k].localRotation=Quaternion.AngleAxis((float)(a*180.0/Math.PI),Vector3.right);
                }
                for(int k=0;k<g.Wheels.Length;k++)if((visibleMask&(1<<g.Data.Wheels[k].Lod))!=0)
                    g.Wheels[k].localRotation=Quaternion.AngleAxis((float)(g.WheelAngles[k]*180.0/Math.PI),Vector3.right)*g.WheelRest[k];
                g.Dirty=false;
            }
        }
    }
}
