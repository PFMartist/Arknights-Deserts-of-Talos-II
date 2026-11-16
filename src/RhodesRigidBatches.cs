using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace EndfieldDoK.ModelIntake
{
    // Clone-safe identity only. Each instance receives its own bones array.
    public sealed class RhodesRigidBatchTag:MonoBehaviour
    {
        public string[] NodeNames;
        public Vector3 BoundsCenter,BoundsSize;
    }
    internal static class RhodesRigidBatches
    {
        internal const string FileName="rigid_batches.rhb";
        internal sealed class Batch
        {
            internal int Lod,Vertices;
            internal readonly List<int> Meshes=new List<int>();
            internal readonly List<int> Nodes=new List<int>();
            internal readonly List<int> Materials=new List<int>();
        }
        // Opt-in binds the exact RHS bytes. Carrier modules are never accepted.
        internal static List<Batch> ReadPlan(string folder,RhodesModelPackage p,out float padding)
        {
            padding=0f;string path=Path.Combine(folder,FileName);if(!File.Exists(path))return null;
            if(new FileInfo(path).Length>256)throw new InvalidDataException("Rigid batch metadata budget");
            string[] lines=File.ReadAllLines(path);
            if(lines.Length!=3||lines[0]!="RHBR1"||lines[1]!=p.ContentSha256||p.Modules.Length!=0||
                !float.TryParse(lines[2],NumberStyles.Float,CultureInfo.InvariantCulture,out padding)||
                float.IsNaN(padding)||float.IsInfinity(padding)||padding<0f||padding>10f)
                throw new InvalidDataException("Rigid batch model/hash/bounds contract");
            return Plan(p);
        }
        internal static List<Batch> Plan(RhodesModelPackage p)
        {
            List<Batch> batches=new List<Batch>();Batch current=null;
            for(int lod=0;lod<4;lod++)for(int i=0;i<p.Meshes.Length;i++)
            {
                RhodesMesh m=p.Meshes[i];if(m.Lod!=lod)continue;int count=m.Vertices.Length/10;
                if(RequiresRigidRenderer(p,m.Node))continue;
                if(count<3||count>60000)throw new InvalidDataException("Rigid source mesh vertex budget");
                if(current==null||current.Lod!=lod||current.Vertices+count>60000||
                    (!current.Nodes.Contains(m.Node)&&current.Nodes.Count==64))
                {current=new Batch();current.Lod=lod;batches.Add(current);}
                current.Meshes.Add(i);current.Vertices+=count;
                if(!current.Nodes.Contains(m.Node))current.Nodes.Add(m.Node);
                if(!current.Materials.Contains(m.Material))current.Materials.Add(m.Material);
            }
            foreach(RhodesMesh m in p.Meshes)if(m.Lod<0)throw new InvalidDataException("Rigid batch requires explicit LODs");
            return batches;
        }
        internal static bool RequiresRigidRenderer(RhodesModelPackage p,int node)
        {
            // Unity's one-weight normal skinning is not inverse-transpose
            // deformation for nonuniform scaling. Keep hydraulic rods and
            // their descendants on the original rigid renderer path.
            for(int i=node;i>=0;i=p.Nodes[i].Parent)
            {
                float[] s=p.Nodes[i].Scale;
                if(s[0]<=0f||s[1]<=0f||s[2]<=0f||Math.Abs(s[0]-s[1])>.00001f||Math.Abs(s[0]-s[2])>.00001f)return true;
            }
            return false;
        }
        internal static Renderer Build(RhodesLoadedModel owner,Material[] materials,Batch batch,int batchIndex,int layer,float padding)
        {
            Transform root=owner.Root.transform;Transform[] bones=new Transform[batch.Nodes.Count];Matrix4x4[] poses=new Matrix4x4[bones.Length];
            string[] names=new string[bones.Length];
            for(int i=0;i<bones.Length;i++)
            {bones[i]=owner.Nodes[batch.Nodes[i]];poses[i]=bones[i].worldToLocalMatrix*root.localToWorldMatrix;names[i]=bones[i].name;}
            Bounds bounds=ConservativeBounds(owner.Package,batch,padding);
            string key=owner.Package.ContentSha256+"|rigid-batch-v1:"+batchIndex;
            Mesh mesh=RhodesSharedResources.Mesh(key,owner,delegate
            {
                int count=batch.Vertices,offset=0;Vector3[] positions=new Vector3[count],normals=new Vector3[count];
                Vector2[] uv=new Vector2[count],uv1=new Vector2[count];Color32[] colours=new Color32[count];BoneWeight[] weights=new BoneWeight[count];
                List<int>[] indices=new List<int>[batch.Materials.Count];for(int i=0;i<indices.Length;i++)indices[i]=new List<int>();
                List<int> all=new List<int>();
                foreach(int source in batch.Meshes)
                {
                    RhodesMesh data=owner.Package.Meshes[source];int bone=batch.Nodes.IndexOf(data.Node),slot=batch.Materials.IndexOf(data.Material);
                    Matrix4x4 matrix=root.worldToLocalMatrix*bones[bone].localToWorldMatrix,normalMatrix=matrix.inverse.transpose;
                    for(int i=0;i<data.Vertices.Length/10;i++)
                    {
                        int a=i*10,v=offset+i;float[] d=data.Vertices;
                        positions[v]=matrix.MultiplyPoint3x4(new Vector3(d[a],d[a+1],d[a+2]));
                        normals[v]=normalMatrix.MultiplyVector(new Vector3(d[a+3],d[a+4],d[a+5])).normalized;
                        uv[v]=new Vector2(d[a+6],d[a+7]);uv1[v]=new Vector2(d[a+8],d[a+9]);colours[v]=new Color32(255,255,255,255);
                        BoneWeight weight=new BoneWeight();weight.boneIndex0=bone;weight.weight0=1f;weights[v]=weight;
                    }
                    foreach(int i in data.Indices){indices[slot].Add(offset+i);all.Add(offset+i);}offset+=data.Vertices.Length/10;
                }
                Mesh created=new Mesh();created.name="EF_RIGID_BATCH_"+batchIndex;created.hideFlags=HideFlags.HideAndDontSave;
                try
                {
                    created.vertices=positions;created.normals=normals;created.uv=uv;created.uv2=uv1;created.colors32=colours;
                    created.boneWeights=weights;created.bindposes=poses;created.subMeshCount=indices.Length;
                    for(int i=0;i<indices.Length;i++)created.SetTriangles(indices[i].ToArray(),i);
                    created.tangents=RhodesPbrMaterials.Tangents(positions,normals,uv,all.ToArray());created.bounds=bounds;
                    // Preserve CPU skinning compatibility on Unity 5.2 hosts.
                    created.UploadMeshData(false);return created;
                }catch{UnityEngine.Object.DestroyImmediate(created);throw;}
            });
            GameObject go=new GameObject("GEO_RIGID_BATCH_"+batchIndex);go.layer=layer;go.transform.parent=root;
            go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.identity;go.transform.localScale=Vector3.one;
            SkinnedMeshRenderer skin=go.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;
            Material[] slots=new Material[batch.Materials.Count];for(int i=0;i<slots.Length;i++)slots[i]=materials[batch.Materials[i]];
            skin.sharedMaterials=slots;skin.bones=(Transform[])bones.Clone();skin.rootBone=root;skin.quality=SkinQuality.Bone1;
            skin.localBounds=bounds;skin.updateWhenOffscreen=false;
            RhodesRigidBatchTag tag=go.AddComponent<RhodesRigidBatchTag>();tag.NodeNames=names;tag.BoundsCenter=bounds.center;tag.BoundsSize=bounds.size;
            return skin;
        }
        static Bounds ConservativeBounds(RhodesModelPackage p,Batch batch,float padding)
        {
            // Triangle inequality for any joint rotation. Padding covers the
            // reviewed finite suspension/recoil translations, not just rest AABB.
            float radius=0f;
            foreach(int index in batch.Meshes)
            {
                RhodesMesh mesh=p.Meshes[index];float local=0f;
                for(int a=0;a<mesh.Vertices.Length;a+=10)local=Math.Max(local,new Vector3(mesh.Vertices[a],mesh.Vertices[a+1],mesh.Vertices[a+2]).magnitude);
                for(int node=mesh.Node;node>=0;node=p.Nodes[node].Parent)
                {
                    RhodesNode n=p.Nodes[node];float scale=Math.Max(Math.Abs(n.Scale[0]),Math.Max(Math.Abs(n.Scale[1]),Math.Abs(n.Scale[2])));
                    local=local*scale+new Vector3(n.Position[0],n.Position[1],n.Position[2]).magnitude;
                }
                radius=Math.Max(radius,local);
            }
            radius+=padding;return new Bounds(Vector3.zero,Vector3.one*(radius*2f));
        }
        internal static void RebindLive(Transform root)
        {
            if(root==null)return;RhodesRigidBatchTag[] tags=root.GetComponentsInChildren<RhodesRigidBatchTag>(true);if(tags.Length==0)return;
            Dictionary<string,Transform> nodes=new Dictionary<string,Transform>(StringComparer.Ordinal);
            foreach(Transform t in root.GetComponentsInChildren<Transform>(true))
                if(!nodes.ContainsKey(t.name))nodes.Add(t.name,t);
            foreach(RhodesRigidBatchTag tag in tags)
            {
                SkinnedMeshRenderer skin=tag.GetComponent<SkinnedMeshRenderer>();if(skin==null||tag.NodeNames==null)throw new InvalidOperationException("Rigid clone identity missing");
                Transform[] bones=new Transform[tag.NodeNames.Length];
                for(int i=0;i<bones.Length;i++)if(!nodes.TryGetValue(tag.NodeNames[i],out bones[i])||!bones[i].IsChildOf(root))throw new InvalidOperationException("Rigid clone bone ownership");
                skin.bones=bones;skin.rootBone=root;skin.localBounds=new Bounds(tag.BoundsCenter,tag.BoundsSize);
            }
        }
    }
}
