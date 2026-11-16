using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace EndfieldDoK.ModelIntake
{
    public sealed class RhodesCarrierBatchTag:MonoBehaviour
    {
        public string ScopeName;
        public string[] BoneNames;
        public Vector3 BoundsCenter,BoundsSize;
    }

    // Exact-source, opt-in carrier presentation only. Original modules and pose
    // writers remain the authority. No geometry, material or animation edits.
    internal static class RhodesCarrierBatches
    {
        internal const string FileName="carrier_batches.rcb";
        internal const string ModelHash="b1be0ef36e372c2b4415e625084d933911541e4b9f84be3915bcff83225c74ba";
        internal sealed class Batch
        {
            internal int Scope,Material,Mask,Vertices;
            internal float Radius;
            internal readonly List<int> Meshes=new List<int>();
            internal readonly List<int> Bones=new List<int>();
        }
        internal sealed class Plan
        {
            internal readonly List<Batch> Batches=new List<Batch>();
            internal bool[] Covered;
            internal int[] Membership;
        }
        internal static Plan ReadPlan(string folder,RhodesModelPackage p,int[] membership)
        {
            string path=Path.Combine(folder,FileName);if(!File.Exists(path))return null;
            if(p.ContentSha256!=ModelHash||p.Nodes.Length!=823||p.Modules.Length!=54||p.Materials.Length!=107||
                membership==null||membership.Length!=p.Meshes.Length)throw new InvalidDataException("Carrier batch source contract");
            if(new FileInfo(path).Length>131072)throw new InvalidDataException("Carrier batch metadata budget");
            string[] lines=File.ReadAllLines(path);
            if(lines.Length!=p.Nodes.Length+3||lines[0]!="RHCB1"||lines[1]!=p.ContentSha256||lines[2]!="POSITION_NORM_ENVELOPES_1")
                throw new InvalidDataException("Carrier batch exact model/envelope contract");
            float[] limits=new float[p.Nodes.Length];
            for(int i=0;i<limits.Length;i++)
            {
                string[] row=lines[i+3].Split('|');float radius;
                if(row.Length!=2||row[0]!=p.Nodes[i].Name||!float.TryParse(row[1],NumberStyles.Float,CultureInfo.InvariantCulture,out radius)||
                    float.IsNaN(radius)||float.IsInfinity(radius)||radius<0f||radius>10000f||radius+.001f<Norm(p.Nodes[i].Position))
                    throw new InvalidDataException("Carrier batch motion envelope: "+p.Nodes[i].Name);
                limits[i]=radius;
            }
            return CreatePlan(p,membership,limits);
        }
        internal static Plan CreatePlan(RhodesModelPackage p,int[] membership,float[] positionLimits)
        {
            Plan plan=new Plan{Covered=new bool[p.Meshes.Length],Membership=(int[])membership.Clone()};
            Dictionary<string,List<int>> grouped=new Dictionary<string,List<int>>(StringComparer.Ordinal);
            List<string> order=new List<string>();
            for(int i=0;i<p.Meshes.Length;i++)
            {
                RhodesMesh m=p.Meshes[i];if(!Eligible(p,m))continue;
                string key=Gate(p,m.Node)+"|"+m.Material+"|"+membership[i];List<int> list;
                if(!grouped.TryGetValue(key,out list)){list=new List<int>();grouped.Add(key,list);order.Add(key);}list.Add(i);
            }
            foreach(string key in order)
            {
                Batch current=null;
                foreach(int index in grouped[key])
                {
                    RhodesMesh m=p.Meshes[index];int count=m.Vertices.Length/10;
                    if(count>60000)continue;
                    if(current==null||current.Vertices+count>60000||(!current.Bones.Contains(m.Node)&&current.Bones.Count==64))
                    {Finish(p,plan,current,positionLimits);current=new Batch{Material=m.Material,Mask=membership[index],Scope=m.Node};}
                    current.Meshes.Add(index);current.Vertices+=count;
                    if(!current.Bones.Contains(m.Node))current.Bones.Add(m.Node);
                    current.Scope=CommonAncestor(p,current.Scope,m.Node);
                }
                Finish(p,plan,current,positionLimits);
            }
            return plan;
        }
        static void Finish(RhodesModelPackage p,Plan plan,Batch batch,float[] limits)
        {
            // A singleton keeps the established rigid renderer at its node.
            if(batch==null||batch.Meshes.Count<2)return;
            if(batch.Scope<0||batch.Bones.Count>64||batch.Vertices>60000||batch.Mask<=0||batch.Mask>15)
                throw new InvalidDataException("Carrier batch scope/LOD/budget");
            int gate=Gate(p,batch.Scope);
            float radius=0f;
            foreach(int index in batch.Meshes)
            {
                RhodesMesh m=p.Meshes[index];
                if(plan.Covered[index]||m.Material!=batch.Material||plan.Membership[index]!=batch.Mask||Gate(p,m.Node)!=gate||!Eligible(p,m))
                    throw new InvalidDataException("Carrier batch crosses material, LOD or module visibility boundary");
                float local=0f;
                for(int at=0;at<m.Vertices.Length;at+=10)
                    local=Math.Max(local,(float)Math.Sqrt((double)m.Vertices[at]*m.Vertices[at]+(double)m.Vertices[at+1]*m.Vertices[at+1]+(double)m.Vertices[at+2]*m.Vertices[at+2]));
                for(int node=m.Node;node!=batch.Scope;node=p.Nodes[node].Parent)
                {
                    if(node<0)throw new InvalidDataException("Carrier batch bone escaped scope");
                    float[] s=p.Nodes[node].Scale;local=local*Math.Max(s[0],Math.Max(s[1],s[2]))+limits[node];
                }
                radius=Math.Max(radius,local);plan.Covered[index]=true;
            }
            batch.Radius=radius+2f;plan.Batches.Add(batch);
        }
        internal static int Gate(RhodesModelPackage p,int node)
        {
            for(int i=node;i>=0;i=p.Nodes[i].Parent)
                foreach(RhodesModule module in p.Modules)if(module.Node==i)return i;
            return -1;
        }
        internal static bool Eligible(RhodesModelPackage p,RhodesMesh m)
        {
            string material=p.Materials[m.Material].Name;
            // Keep independently mutated heat surfaces, transparent overlays,
            // glazing and authored lamps on their established renderer path.
            string lower=material.ToLowerInvariant();
            if(lower.Contains("wx03_")||lower.Contains("decal")||lower.Contains("glazing")||lower.Contains("emission")||
                lower.Contains("ri44_heat")||lower.Contains("headlight")||lower.Contains("worklight")||lower.Contains("lamp_"))return false;
            for(int node=m.Node;node>=0;node=p.Nodes[node].Parent)
            {
                RhodesNode n=p.Nodes[node];string name=n.Name;float[] scale=n.Scale;
                // These owners inspect MeshFilters or child renderer bounds.
                // They also include hydraulic pieces and stretchable hoses.
                if(name=="PIVOT_V14_MainChassis"||name.StartsWith("CHASSIS_",StringComparison.Ordinal)||name.StartsWith("TRACK_",StringComparison.Ordinal)||
                    name.StartsWith("STUDY46_",StringComparison.Ordinal)||name=="ASSEMBLY_SensorProbe"||name=="PIVOT_ProbeSway")return false;
                if(scale[0]<=0f||scale[1]<=0f||scale[2]<=0f||Math.Abs(scale[0]-scale[1])>.00001f||Math.Abs(scale[0]-scale[2])>.00001f)return false;
            }
            return m.Lod>=0&&m.Lod<4;
        }
        static int CommonAncestor(RhodesModelPackage p,int a,int b)
        {
            for(int i=a;i>=0;i=p.Nodes[i].Parent)
                for(int j=b;j>=0;j=p.Nodes[j].Parent)if(i==j)return i;
            return -1;
        }
        static float Norm(float[] p){return (float)Math.Sqrt((double)p[0]*p[0]+(double)p[1]*p[1]+(double)p[2]*p[2]);}

        internal static Renderer Build(RhodesLoadedModel owner,Material[] materials,Batch batch,int index,int layer)
        {
            Transform scope=owner.Nodes[batch.Scope];Transform[] bones=new Transform[batch.Bones.Count];
            Matrix4x4[] bindposes=new Matrix4x4[bones.Length];string[] names=new string[bones.Length];
            for(int i=0;i<bones.Length;i++)
            {bones[i]=owner.Nodes[batch.Bones[i]];bindposes[i]=bones[i].worldToLocalMatrix*scope.localToWorldMatrix;names[i]=bones[i].name;}
            Bounds swept=new Bounds(Vector3.zero,Vector3.one*(batch.Radius*2f));
            string key=owner.Package.ContentSha256+"|carrier-batch-v1:"+index+"|"+batch.Scope+"|"+batch.Material+"|"+batch.Mask;
            Mesh mesh=RhodesSharedResources.Mesh(key,owner,delegate
            {
                Vector3[] positions=new Vector3[batch.Vertices],normals=new Vector3[batch.Vertices];
                Vector2[] uv=new Vector2[batch.Vertices],uv1=new Vector2[batch.Vertices];
                Color32[] colours=new Color32[batch.Vertices];BoneWeight[] weights=new BoneWeight[batch.Vertices];
                bool tangents=RhodesPbrMaterials.RequiresTangents(owner.Package.Materials[batch.Material],owner.PbrMaterials);
                Vector4[] tangentValues=tangents?new Vector4[batch.Vertices]:null;
                List<int> indices=new List<int>();int offset=0;
                foreach(int source in batch.Meshes)
                {
                    RhodesMesh data=owner.Package.Meshes[source];int bone=batch.Bones.IndexOf(data.Node),count=data.Vertices.Length/10;
                    Vector3[] localPositions=new Vector3[count],localNormals=new Vector3[count];Vector2[] localUV=new Vector2[count];
                    Matrix4x4 toScope=scope.worldToLocalMatrix*bones[bone].localToWorldMatrix,normalMatrix=toScope.inverse.transpose;
                    for(int i=0;i<count;i++)
                    {
                        int a=i*10;float[] v=data.Vertices;
                        localPositions[i]=new Vector3(v[a],v[a+1],v[a+2]);localNormals[i]=new Vector3(v[a+3],v[a+4],v[a+5]).normalized;
                        localUV[i]=new Vector2(v[a+6],v[a+7]);
                    }
                    // Preserve each source mesh's tangent calculation, including
                    // UV seams. No welding or cross-piece tangent averaging.
                    Vector4[] localTangents=tangents?RhodesPbrMaterials.Tangents(localPositions,localNormals,localUV,data.Indices):null;
                    for(int i=0;i<count;i++)
                    {
                        int v=offset+i,a=i*10;
                        positions[v]=toScope.MultiplyPoint3x4(localPositions[i]);normals[v]=normalMatrix.MultiplyVector(localNormals[i]).normalized;
                        uv[v]=localUV[i];uv1[v]=new Vector2(data.Vertices[a+8],data.Vertices[a+9]);colours[v]=new Color32(255,255,255,255);
                        BoneWeight weight=new BoneWeight();weight.boneIndex0=bone;weight.weight0=1f;weights[v]=weight;
                        if(tangents){Vector4 t=localTangents[i];Vector3 d=toScope.MultiplyVector(new Vector3(t.x,t.y,t.z)).normalized;tangentValues[v]=new Vector4(d.x,d.y,d.z,t.w);}
                    }
                    foreach(int i in data.Indices)indices.Add(offset+i);offset+=count;
                }
                Mesh created=new Mesh();created.name="EF_CARRIER_BATCH_"+index;created.hideFlags=HideFlags.HideAndDontSave;
                try
                {
                    created.vertices=positions;created.normals=normals;created.uv=uv;created.uv2=uv1;created.colors32=colours;
                    created.triangles=indices.ToArray();if(tangents)created.tangents=tangentValues;
                    created.boneWeights=weights;created.bindposes=bindposes;created.RecalculateBounds();
                    created.UploadMeshData(false);return created;
                }catch{UnityEngine.Object.DestroyImmediate(created);throw;}
            });
            GameObject go=new GameObject("GEO_CARRIER_BATCH_"+index);go.layer=layer;go.transform.parent=scope;
            go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.identity;go.transform.localScale=Vector3.one;
            SkinnedMeshRenderer skin=go.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;skin.sharedMaterial=materials[batch.Material];
            skin.bones=(Transform[])bones.Clone();skin.rootBone=scope;skin.quality=SkinQuality.Bone1;skin.localBounds=swept;skin.updateWhenOffscreen=false;
            RhodesCarrierBatchTag tag=go.AddComponent<RhodesCarrierBatchTag>();tag.ScopeName=scope.name;tag.BoneNames=names;
            tag.BoundsCenter=swept.center;tag.BoundsSize=swept.size;return skin;
        }
        internal static void RebindLive(Transform root,Func<string,Transform> find)
        {
            if(root==null)return;
            foreach(RhodesCarrierBatchTag tag in root.GetComponentsInChildren<RhodesCarrierBatchTag>(true))
            {
                Transform scope=find(tag.ScopeName);SkinnedMeshRenderer skin=tag.GetComponent<SkinnedMeshRenderer>();
                if(scope==null||!scope.IsChildOf(root)||scope!=tag.transform.parent||skin==null||tag.BoneNames==null)
                    throw new InvalidOperationException("Carrier batch clone scope ownership");
                Transform[] bones=new Transform[tag.BoneNames.Length];
                for(int i=0;i<bones.Length;i++)
                {
                    bones[i]=find(tag.BoneNames[i]);
                    if(bones[i]==null||(bones[i]!=scope&&!bones[i].IsChildOf(scope)))throw new InvalidOperationException("Carrier batch clone bone ownership");
                }
                skin.bones=bones;skin.rootBone=scope;skin.localBounds=new Bounds(tag.BoundsCenter,tag.BoundsSize);
            }
        }
    }
}
