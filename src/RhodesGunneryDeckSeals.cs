using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndfieldDoK.ModelIntake
{
    // A presentation-only repair for one frozen R23 intake. The author meshes,
    // animation curves, selection/collision bounds and source UVs stay intact.
    internal static class RhodesGunneryDeckSeals
    {
        internal const string ModelHash="b1be0ef36e372c2b4415e625084d933911541e4b9f84be3915bcff83225c74ba";
        internal const string MaterialName="V17SRC_V06_03_Mechanical_Graphite";
        internal const float SampleU=.75830078125f,SampleV=.74169921875f;
        internal sealed class Plan {internal readonly RhodesMesh[] Pieces;internal Plan(RhodesMesh[] pieces){Pieces=pieces;}}

        // Pure managed planning: no Unity objects are allocated until every
        // node, material and original platform/LOD identity has been checked.
        internal static Plan CreatePlan(RhodesModelPackage package)
        {
            if(package==null||package.ContentSha256!=ModelHash)return null;
            if(package.Version!=2||package.Nodes==null||package.Nodes.Length!=823||package.Materials==null||package.Materials.Length<=12)
                throw new InvalidOperationException("R23 deck seal package identity mismatch");
            RequireNode(package,453,"PIVOT_GN_ModuleLift",437);
            RequireNode(package,454,"PIVOT_GN_AFT_Lift",453);
            RequireNode(package,464,"PIVOT_GN_FORE_Lift",453);
            RhodesMaterial material=package.Materials[12];
            if(material.Name!=MaterialName||material.Albedo!="textures/runtime_albedo_63861c07cfe99ce818c1.png"||
                material.Normal!=""||material.Emission!=""||Math.Abs(material.Metallic-.62f)>.00001f||Math.Abs(material.Roughness-.48f)>.00001f)
                throw new InvalidOperationException("R23 deck seal original graphite material mismatch");
            foreach(int node in new[]{454,464})foreach(int lod in new[]{0,1,2})
            {
                string name="V18_R23_GN_L"+lod+"_N"+node+"_M12_C0";int found=0;
                foreach(RhodesMesh mesh in package.Meshes)
                    if(mesh.Name==name){if(mesh.Node!=node||mesh.Material!=12||mesh.Lod!=lod)throw new InvalidOperationException("R23 deck seal platform mesh mismatch");found++;}
                if(found!=1)throw new InvalidOperationException("R23 deck seal exact platform LOD missing");
            }
            RhodesMesh[] pieces=new RhodesMesh[2];
            for(int i=0;i<2;i++)
            {
                int node=i==0?464:454;double[,] world=BindWorld(package,node);
                // Current bind has only this translation. Still use its actual
                // inverse below, rather than treating source DU as world metres.
                for(int row=0;row<3;row++)for(int col=0;col<3;col++)
                    if(Math.Abs(world[row,col]-(row==col?1:0))>.00001)throw new InvalidOperationException("R23 deck seal bind basis changed");
                if(Math.Abs(world[0,3]-15.119667053222656)>.00001||Math.Abs(world[1,3]-101.60415649414062)>.00001||Math.Abs(world[2,3])>.00001)
                    throw new InvalidOperationException("R23 deck seal bind origin changed");
                double oz0=i==0?15.0197:-77.7143,oz1=i==0?77.7143:-15.0197;
                double iz0=i==0?16.8115:-75.9225,iz1=i==0?75.9225:-16.8115;
                List<float> vertices=new List<float>();List<int> indices=new List<int>();
                Box(vertices,indices,world,-17.0340,-14.4358,oz0,oz1);
                Box(vertices,indices,world,78.1400,80.7382,oz0,oz1);
                Box(vertices,indices,world,-14.4358,78.1400,oz0,iz0);
                Box(vertices,indices,world,-14.4358,78.1400,iz1,oz1);
                pieces[i]=new RhodesMesh{Name="R23_DeckSeal49_"+(i==0?"FORE":"AFT"),Node=node,Material=12,Lod=-1,Vertices=vertices.ToArray(),Indices=indices.ToArray()};
            }
            return new Plan(pieces);
        }
        static void RequireNode(RhodesModelPackage p,int index,string name,int parent)
        {if(p.Nodes[index].Name!=name||p.Nodes[index].Parent!=parent)throw new InvalidOperationException("R23 deck seal node identity mismatch");}
        static double[,] BindWorld(RhodesModelPackage p,int index)
        {
            RhodesNode n=p.Nodes[index];double x=n.Rotation[0],y=n.Rotation[1],z=n.Rotation[2],w=n.Rotation[3];
            double[,] local={{(1-2*(y*y+z*z))*n.Scale[0],2*(x*y-z*w)*n.Scale[1],2*(x*z+y*w)*n.Scale[2],n.Position[0]},
                {2*(x*y+z*w)*n.Scale[0],(1-2*(x*x+z*z))*n.Scale[1],2*(y*z-x*w)*n.Scale[2],n.Position[1]},
                {2*(x*z-y*w)*n.Scale[0],2*(y*z+x*w)*n.Scale[1],(1-2*(x*x+y*y))*n.Scale[2],n.Position[2]},{0,0,0,1}};
            if(n.Parent<0)return local;
            double[,] parent=BindWorld(p,n.Parent),result=new double[4,4];
            for(int row=0;row<4;row++)for(int col=0;col<4;col++)for(int k=0;k<4;k++)result[row,col]+=parent[row,k]*local[k,col];
            return result;
        }
        static void Box(List<float> vertices,List<int> indices,double[,] world,double x0,double x1,double z0,double z1)
        {
            double y0=106.6256,y1=106.8656;
            double[][] p={new[]{x0,y0,z0},new[]{x0,y0,z1},new[]{x0,y1,z0},new[]{x0,y1,z1},new[]{x1,y0,z0},new[]{x1,y0,z1},new[]{x1,y1,z0},new[]{x1,y1,z1}};
            Face(vertices,indices,world,p,0,1,3,2,-1,0,0);Face(vertices,indices,world,p,4,6,7,5,1,0,0);
            Face(vertices,indices,world,p,0,4,5,1,0,-1,0);Face(vertices,indices,world,p,2,3,7,6,0,1,0);
            Face(vertices,indices,world,p,0,2,6,4,0,0,-1);Face(vertices,indices,world,p,1,5,7,3,0,0,1);
        }
        static void Face(List<float> v,List<int> indices,double[,] world,double[][] points,int a,int b,int c,int d,float nx,float ny,float nz)
        {
            int start=v.Count/10;
            foreach(int index in new[]{a,b,c,d})
            {
                double[] p=points[index];double dx=p[0]-world[0,3],dy=p[1]-world[1,3],dz=p[2]-world[2,3];
                // The checked bind basis is orthonormal; transpose is its exact
                // inverse, preserving both platform-local placement and normals.
                for(int axis=0;axis<3;axis++)v.Add((float)(world[0,axis]*dx+world[1,axis]*dy+world[2,axis]*dz));
                v.Add(nx);v.Add(ny);v.Add(nz);v.Add(SampleU);v.Add(SampleV);v.Add(SampleU);v.Add(SampleV);
            }
            indices.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
        }
        internal static void AddIfCompatible(RhodesLoadedModel owner,Material[] materials,int layer,List<Renderer> all,List<Renderer>[] lods,int highest)
        {
            if(owner.Package.ContentSha256!=ModelHash)return;
            List<GameObject> created=new List<GameObject>();List<Renderer> added=new List<Renderer>();int sharedStart=owner.Shared.Count;
            try
            {
                Plan plan=CreatePlan(owner.Package);
                if(plan==null||highest!=2||materials.Length<=12||materials[12]==null||materials[12].name!=MaterialName)
                    throw new InvalidOperationException("R23 deck seal runtime material/LOD mismatch");
                foreach(RhodesMesh data in plan.Pieces)
                {
                    Mesh mesh=RhodesSharedResources.Mesh(ModelHash+"|deckseal49:v1|"+data.Node,owner,delegate{return BuildMesh(data);});
                    GameObject part=new GameObject(data.Name);created.Add(part);part.layer=layer;part.transform.parent=owner.Nodes[data.Node];
                    part.transform.localPosition=Vector3.zero;part.transform.localRotation=Quaternion.identity;part.transform.localScale=Vector3.one;
                    part.AddComponent<MeshFilter>().sharedMesh=mesh;MeshRenderer renderer=part.AddComponent<MeshRenderer>();renderer.sharedMaterial=materials[12];added.Add(renderer);
                }
                // Same two renderers in every existing LOD. Module ancestry,
                // root FOW/native visibility and owner disposal remain native.
                foreach(Renderer renderer in added){all.Add(renderer);for(int level=0;level<=highest;level++)lods[level].Add(renderer);}
            }
            catch(Exception error)
            {
                foreach(Renderer renderer in added){all.Remove(renderer);foreach(List<Renderer> level in lods)level.Remove(renderer);}
                for(int i=created.Count-1;i>=0;i--)if(created[i]!=null)UnityEngine.Object.DestroyImmediate(created[i]);
                for(int i=owner.Shared.Count-1;i>=sharedStart;i--){owner.Shared[i].Dispose();owner.Shared.RemoveAt(i);}
                owner.Warnings.Add("Optional R23 gunnery deck seals skipped: "+error.Message);
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
