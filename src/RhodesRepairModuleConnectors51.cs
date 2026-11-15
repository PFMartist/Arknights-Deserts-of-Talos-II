using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndfieldDoK.ModelIntake
{
    // Two short fixed collars join the existing COMMON socket backplates to
    // the real side-wall triangles. Neither collar follows repair yaw/pitch.
    internal static class RhodesRepairModuleConnectors51
    {
        internal const string ModelHash="b1be0ef36e372c2b4415e625084d933911541e4b9f84be3915bcff83225c74ba";
        internal const string MaterialName="V17SRC_V06_03_Mechanical_Graphite";
        internal static RhodesMesh CreatePlan(RhodesModelPackage package)
        {
            if(package==null||package.ContentSha256!=ModelHash)return null;
            if(package.Version!=2||package.Nodes.Length!=823||package.Materials.Length<=12||
                package.Nodes[0].Name!="ROOT_Rhodes"||package.Nodes[0].Parent!=-1||
                package.Materials[12].Name!=MaterialName||package.Materials[12].Albedo!="textures/runtime_albedo_63861c07cfe99ce818c1.png")
                throw new InvalidOperationException("Repair51 exact model/root/material mismatch");
            RhodesNode root=package.Nodes[0];
            for(int i=0;i<3;i++)if(root.Position[i]!=0f||root.Scale[i]!=1f||root.Rotation[i]!=0f)
                throw new InvalidOperationException("Repair51 fixed COMMON root basis changed");
            if(root.Rotation[3]!=1f)throw new InvalidOperationException("Repair51 fixed root quaternion changed");
            RequireMount(package,474,"V03_MOUNT_Repair_STARBOARD",126.602012634f);
            RequireMount(package,478,"V03_MOUNT_Repair_PORT",-98.177032471f);
            List<float> vertices=new List<float>();List<int> indices=new List<int>();
            // Raw native RHS units. Each end embeds0.30 into the existing wall
            // or socket; the collar is0.22 inside the socket's bevel perimeter.
            Box(vertices,indices,122.789416504f,124.482868958f);
            Box(vertices,indices,-96.057888794f,-92.715055935f);
            return new RhodesMesh{Name="R23_RepairFixedCollars51",Node=0,Material=12,Lod=-1,Vertices=vertices.ToArray(),Indices=indices.ToArray()};
        }
        static void RequireMount(RhodesModelPackage package,int index,string name,float x)
        {
            RhodesNode node=package.Nodes[index];
            if(node.Name!=name||node.Parent!=0||Math.Abs(node.Position[0]-x)>.0001f||
                Math.Abs(node.Position[1]-73.582382202f)>.0001f||Math.Abs(node.Position[2]+124.989242554f)>.0001f)
                throw new InvalidOperationException("Repair51 socket mount identity changed");
        }
        static void Box(List<float> vertices,List<int> indices,float x0,float x1)
        {
            const float y0=67.351324768f,y1=79.813439636f,z0=-131.220307617f,z1=-118.758185120f;
            float[][] p={new[]{x0,y0,z0},new[]{x0,y0,z1},new[]{x0,y1,z0},new[]{x0,y1,z1},new[]{x1,y0,z0},new[]{x1,y0,z1},new[]{x1,y1,z0},new[]{x1,y1,z1}};
            Face(vertices,indices,p,0,1,3,2,-1,0,0);Face(vertices,indices,p,4,6,7,5,1,0,0);
            Face(vertices,indices,p,0,4,5,1,0,-1,0);Face(vertices,indices,p,2,3,7,6,0,1,0);
            Face(vertices,indices,p,0,2,6,4,0,0,-1);Face(vertices,indices,p,1,5,7,3,0,0,1);
        }
        static void Face(List<float> vertices,List<int> indices,float[][] points,int a,int b,int c,int d,float nx,float ny,float nz)
        {
            int start=vertices.Count/10;
            foreach(int index in new[]{a,b,c,d})
            {
                vertices.AddRange(points[index]);vertices.Add(nx);vertices.Add(ny);vertices.Add(nz);
                vertices.Add(RhodesGunneryDeckSeals.SampleU);vertices.Add(RhodesGunneryDeckSeals.SampleV);
                vertices.Add(RhodesGunneryDeckSeals.SampleU);vertices.Add(RhodesGunneryDeckSeals.SampleV);
            }
            indices.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
        }
        internal static void AddIfCompatible(RhodesLoadedModel owner,Material[] materials,int layer,List<Renderer> all,List<Renderer>[] lods,int highest)
        {
            if(owner.Package.ContentSha256!=ModelHash)return;
            GameObject part=null;Renderer renderer=null;int sharedStart=owner.Shared.Count;
            try
            {
                RhodesMesh plan=CreatePlan(owner.Package);
                if(plan==null||highest!=2||materials.Length<=12||materials[12]==null||materials[12].name!=MaterialName)
                    throw new InvalidOperationException("Repair51 runtime material/LOD mismatch");
                Mesh mesh=RhodesSharedResources.Mesh(ModelHash+"|repaircollars51:v1",owner,delegate{return BuildMesh(plan);});
                part=new GameObject(plan.Name);part.layer=layer;part.transform.parent=owner.Nodes[0];
                part.transform.localPosition=Vector3.zero;part.transform.localRotation=Quaternion.identity;part.transform.localScale=Vector3.one;
                part.AddComponent<MeshFilter>().sharedMesh=mesh;MeshRenderer geometry=part.AddComponent<MeshRenderer>();geometry.sharedMaterial=materials[12];renderer=geometry;
                all.Add(renderer);for(int level=0;level<=highest;level++)lods[level].Add(renderer);
            }
            catch(Exception error)
            {
                if(renderer!=null){all.Remove(renderer);foreach(List<Renderer> level in lods)level.Remove(renderer);}
                if(part!=null)UnityEngine.Object.DestroyImmediate(part);
                for(int i=owner.Shared.Count-1;i>=sharedStart;i--){owner.Shared[i].Dispose();owner.Shared.RemoveAt(i);}
                owner.Warnings.Add("Optional fixed repair collars51 skipped: "+error.Message);
            }
        }
        static Mesh BuildMesh(RhodesMesh data)
        {
            int count=data.Vertices.Length/10;Vector3[] positions=new Vector3[count],normals=new Vector3[count];Vector2[] uv=new Vector2[count];Color32[] colors=new Color32[count];
            for(int i=0;i<count;i++){int at=i*10;positions[i]=new Vector3(data.Vertices[at],data.Vertices[at+1],data.Vertices[at+2]);normals[i]=new Vector3(data.Vertices[at+3],data.Vertices[at+4],data.Vertices[at+5]);uv[i]=new Vector2(data.Vertices[at+6],data.Vertices[at+7]);colors[i]=new Color32(255,255,255,255);}
            Mesh mesh=new Mesh();mesh.name=data.Name;mesh.hideFlags=HideFlags.HideAndDontSave;
            try{mesh.vertices=positions;mesh.normals=normals;mesh.uv=uv;mesh.uv2=uv;mesh.colors32=colors;mesh.triangles=data.Indices;
                mesh.tangents=RhodesPbrMaterials.Tangents(positions,normals,uv,data.Indices);mesh.RecalculateBounds();mesh.UploadMeshData(true);return mesh;}
            catch{UnityEngine.Object.DestroyImmediate(mesh);throw;}
        }
    }
}
