using System;
using System.IO;
using EndfieldDoK.ModelIntake;

namespace EndfieldDoK.MapUnitLab
{
    // Static wrecks intentionally have no live-unit navigation, selection,
    // articulation, FTK or suspension contract. Validate their own export shape.
    internal static class RhodesWreckContract
    {
        internal static void Require(RhodesModelPackage p)
        {
            if(p==null||p.Version!=2||p.ExternalTrackGeometry||p.Selection.Specified||p.Navigation.Specified||
                p.Modules==null||p.Modules.Length!=0||p.Nodes==null||p.Nodes.Length==0||p.Materials==null||p.Materials.Length!=1||
                p.Meshes==null||p.Meshes.Length==0)
                throw new InvalidDataException("Expected independent static RHS2 wreck with visual bounds and one baked atlas");
            if(p.Visual==null||p.Visual.Min==null||p.Visual.Min.Length!=3||Math.Abs(p.Visual.Min[1])>.05f)
                throw new InvalidDataException("Authored wreck ground datum must be Y=0 within five centimetres");
            RhodesMaterial mat=p.Materials[0];
            if(mat.Albedo!="textures/wreck_albedo.png"||!string.IsNullOrEmpty(mat.Normal)||!string.IsNullOrEmpty(mat.Emission))
                throw new InvalidDataException("Wreck atlas binding differs from authored export");
            int[] triangles=new int[3];
            foreach(RhodesNode node in p.Nodes)
                if(node.Parent!=-1||!Same(node.Position,new float[]{0,0,0})||!Same(node.Rotation,new float[]{0,0,0,1})||!Same(node.Scale,new float[]{1,1,1}))
                    throw new InvalidDataException("Wreck vertices must be baked into independent identity nodes");
            foreach(RhodesMesh mesh in p.Meshes)
            {
                if(mesh.Lod<0||mesh.Lod>2||mesh.Material!=0||mesh.Indices==null||mesh.Indices.Length==0)
                    throw new InvalidDataException("Wreck requires exactly three nonempty static LODs");
                triangles[mesh.Lod]+=mesh.Indices.Length/3;
            }
            if(triangles[0]<1||triangles[1]<1||triangles[2]<1||triangles[1]>triangles[0]||triangles[2]>triangles[1])
                throw new InvalidDataException("Wreck LOD triangle counts are missing or not descending");
        }
        static bool Same(float[] a,float[] b)
        {if(a==null||a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(Math.Abs(a[i]-b[i])>.00001f)return false;return true;}
    }
}
