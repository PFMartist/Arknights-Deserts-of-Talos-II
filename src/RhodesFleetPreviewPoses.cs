using System;
using System.IO;
using EndfieldDoK.ModelIntake;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // One-shot menu geometry only. Reads a local AFM directly; never touches the
    // battle motion dictionary, native UnitView, registry or simulation drivers.
    internal static class RhodesFleetPreviewPoses
    {
        internal const float DeckReadyFrame=45f,SourceFramesPerSecond=30f;
        internal static bool IsAircraft(string key)
        {return key=="EF_Rhodes_Interceptor_MP"||key=="EF_Rhodes_HeavyBomber_MP";}
        internal static void ApplyDeckReady(RhodesLoadedModel model,string modelFile)
        {
            if(model==null||model.Root==null)throw new ArgumentNullException("model");
            string key=new DirectoryInfo(Path.GetDirectoryName(modelFile)).Name;
            if(!IsAircraft(key))throw new ArgumentException("Only authored F01/B01 have this static preview pose");
            AirWingMotion motion=AirWingMotion.Read(Path.Combine(Path.GetDirectoryName(modelFile),"motion.afm"),modelFile);
            foreach(AirWingCurve curve in motion.Curves)
            {
                Quaternion pose=curve.Sample(DeckReadyFrame/SourceFramesPerSecond);int matches=0,lods=0;
                for(int i=0;i<model.Package.Nodes.Length;i++)
                {
                    string name=model.Package.Nodes[i].Name;
                    if(!name.EndsWith("::"+curve.Node,StringComparison.Ordinal))continue;
                    int lod=name.Length>1?name[1]-'0':-1;
                    if(lod<0||lod>2||!name.StartsWith("L"+lod+"::",StringComparison.Ordinal)||(lods&(1<<lod))!=0||
                        model.Nodes[i]==null||!model.Nodes[i].IsChildOf(model.Root.transform))
                        throw new InvalidDataException("Aircraft menu curve must have one owned target in each LOD: "+curve.Node);
                    model.Nodes[i].localRotation=pose;matches++;lods|=1<<lod;
                }
                if(matches!=3||lods!=7)throw new InvalidDataException("Aircraft menu pose is missing an authored LOD target: "+curve.Node);
            }
            model.PresentationBounds=BoundsFromPackage(model);
            if(model.LodGroup!=null)
            {
                // Skin culling spheres include animation sweep; the static
                // menu pose needs its own tight reference envelope instead.
                model.LodGroup.localReferencePoint=model.PresentationBounds.center;
                Vector3 size=model.PresentationBounds.size;
                model.LodGroup.size=Math.Max(size.x,Math.Max(size.y,size.z));
            }
        }
        static Bounds BoundsFromPackage(RhodesLoadedModel model)
        {
            if(model.Package==null||model.Package.Meshes==null||model.Nodes==null)
                throw new InvalidDataException("Aircraft preview source geometry is missing");
            Bounds bounds=new Bounds();bool first=true;
            Matrix4x4 worldToRoot=model.Root.transform.worldToLocalMatrix;
            foreach(RhodesMesh mesh in model.Package.Meshes)
            {
                if(mesh.Node<0||mesh.Node>=model.Nodes.Length||model.Nodes[mesh.Node]==null||
                    !model.Nodes[mesh.Node].IsChildOf(model.Root.transform)||mesh.Vertices==null||
                    mesh.Vertices.Length==0||mesh.Vertices.Length%10!=0)
                    throw new InvalidDataException("Aircraft preview source mesh ownership/layout is invalid");
                // Rigid renderers and one-bone batches share the same authored
                // node transforms. Read retained source positions once instead
                // of GPU-only MeshFilter data or the skin's conservative sphere.
                // No BakeMesh, shared-mesh mutation or battle-cache changes.
                Matrix4x4 toRoot=worldToRoot*model.Nodes[mesh.Node].localToWorldMatrix;
                for(int at=0;at<mesh.Vertices.Length;at+=10)
                {
                    Vector3 point=toRoot.MultiplyPoint3x4(new Vector3(mesh.Vertices[at],mesh.Vertices[at+1],mesh.Vertices[at+2]));
                    if(float.IsNaN(point.sqrMagnitude)||float.IsInfinity(point.sqrMagnitude))throw new InvalidDataException("Nonfinite aircraft posed bounds");
                    if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);
                }
            }
            if(first||bounds.size.x<=0f||bounds.size.y<=0f||bounds.size.z<=0f)throw new InvalidDataException("Aircraft preview has empty posed bounds");
            return bounds;
        }
    }
}
