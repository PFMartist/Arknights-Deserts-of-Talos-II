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
            model.PresentationBounds=BoundsFromMeshes(model);
            if(model.LodGroup!=null)model.LodGroup.RecalculateBounds();
        }
        static Bounds BoundsFromMeshes(RhodesLoadedModel model)
        {
            Bounds bounds=new Bounds();bool first=true;
            Matrix4x4 worldToRoot=model.Root.transform.worldToLocalMatrix;
            foreach(Renderer renderer in model.Renderers)
            {
                if(renderer==null)continue;
                MeshFilter filter=renderer.GetComponent<MeshFilter>();
                if(filter==null||filter.sharedMesh==null)throw new InvalidDataException("Static aircraft preview requires uploaded rigid meshes");
                // Bounds remain available after UploadMeshData(true). Reading
                // vertices here would fail and would add an unnecessary copy.
                Bounds local=filter.sharedMesh.bounds;
                Matrix4x4 toRoot=worldToRoot*renderer.transform.localToWorldMatrix;
                for(int corner=0;corner<8;corner++)
                {
                    Vector3 point=new Vector3((corner&1)==0?local.min.x:local.max.x,
                        (corner&2)==0?local.min.y:local.max.y,(corner&4)==0?local.min.z:local.max.z);
                    point=toRoot.MultiplyPoint3x4(point);
                    if(float.IsNaN(point.sqrMagnitude)||float.IsInfinity(point.sqrMagnitude))throw new InvalidDataException("Nonfinite aircraft posed bounds");
                    if(first){bounds=new Bounds(point,Vector3.zero);first=false;}else bounds.Encapsulate(point);
                }
            }
            if(first||bounds.size.x<=0f||bounds.size.y<=0f||bounds.size.z<=0f)throw new InvalidDataException("Aircraft preview has empty posed bounds");
            return bounds;
        }
    }
}
