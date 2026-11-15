using System;
using System.Collections.Generic;
using System.IO;
using EndfieldDoK.ModelIntake;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public sealed class RhodesProbeLoadResult:IDisposable
    {
        public Renderer[] Renderers,CommonLodRenderers;
        public UnityEngine.Object[] OwnedAssets;
        public Bounds RootLocalSweptBounds;
        internal GameObject WireObject;
        internal Mesh TemplateMesh;
        public void Dispose()
        {
            if(TemplateMesh!=null)RhodesProbeLoader.Forget(TemplateMesh);
            if(WireObject!=null)UnityEngine.Object.DestroyImmediate(WireObject);
            if(OwnedAssets!=null)foreach(UnityEngine.Object value in OwnedAssets)if(value!=null)UnityEngine.Object.DestroyImmediate(value);
            WireObject=null;TemplateMesh=null;OwnedAssets=null;Renderers=CommonLodRenderers=new Renderer[0];
        }
    }
    // Template publication is immutable data+mesh only; no serialized Mono cache.
    // The host adopts OwnedAssets on success and keeps the same renderer in all LODs.
    public static class RhodesProbeLoader
    {
        public const string PackageFileName="rhodes_probe.rpa";
        static readonly Dictionary<Mesh,RhodesProbePackage> Packages=new Dictionary<Mesh,RhodesProbePackage>();
        public static bool IsCableName(string name)
        {foreach(RhodesProbePackage package in Packages.Values)if(package.CableName==name)return true;return false;}
        public static bool TryGetPackage(Mesh template,out RhodesProbePackage package)
        {package=null;return template!=null&&Packages.TryGetValue(template,out package);}
        public static void Forget(Mesh template){if(!object.ReferenceEquals(template,null))Packages.Remove(template);}
        public static void ReleaseTemplateCache(){Packages.Clear();}
        public static RhodesProbeLoadResult Load(string folder,Transform presentationRoot,Func<RhodesMaterial,Material> materialFactory,int layer)
        {
            if(presentationRoot==null||materialFactory==null)throw new ArgumentNullException("Probe loader host");
            if(layer<0||layer>31)throw new ArgumentOutOfRangeException("layer");
            RhodesProbePackage package=RhodesProbePackage.Read(Path.Combine(Path.GetFullPath(folder),PackageFileName));
            foreach(Transform node in presentationRoot.GetComponentsInChildren<Transform>(true))if(node.name==package.CableName)throw new InvalidDataException("Duplicate probe cable object "+package.CableName);
            RhodesProbeLoadResult result=new RhodesProbeLoadResult();
            try
            {
                Material material=materialFactory(package.Material);if(material==null)throw new InvalidDataException("Probe material factory returned null");
                int count=package.VertexCount;Vector3[] positions=new Vector3[count],normals=new Vector3[count];Vector2[] uv=new Vector2[count];Color32[] colours=new Color32[count];
                float[] values=package.Frames[0].Wire;
                for(int i=0;i<count;i++){int at=i*6;positions[i]=new Vector3(values[at],values[at+1],values[at+2]);normals[i]=new Vector3(values[at+3],values[at+4],values[at+5]).normalized;uv[i]=new Vector2(package.UV[i*2],package.UV[i*2+1]);colours[i]=new Color32(255,255,255,255);}
                Mesh mesh=new Mesh();result.TemplateMesh=mesh;result.OwnedAssets=new UnityEngine.Object[]{mesh};mesh.name="EF_RPA_Template_"+package.CableName;mesh.hideFlags=HideFlags.HideAndDontSave;
                mesh.vertices=positions;mesh.normals=normals;mesh.uv=uv;mesh.uv2=uv;mesh.colors32=colours;mesh.triangles=package.Indices;mesh.bounds=WireBounds(package);
                GameObject wire=new GameObject(package.CableName);result.WireObject=wire;wire.layer=layer;wire.transform.parent=presentationRoot;wire.transform.localPosition=Vector3.zero;wire.transform.localRotation=Quaternion.identity;wire.transform.localScale=Vector3.one;
                wire.AddComponent<MeshFilter>().sharedMesh=mesh;MeshRenderer renderer=wire.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
                result.Renderers=new Renderer[]{renderer};result.CommonLodRenderers=result.Renderers;
                Bounds swept=WireBounds(package);swept.Encapsulate(new Vector3(package.ProbeSweptBounds[0],package.ProbeSweptBounds[1],package.ProbeSweptBounds[2]));swept.Encapsulate(new Vector3(package.ProbeSweptBounds[3],package.ProbeSweptBounds[4],package.ProbeSweptBounds[5]));result.RootLocalSweptBounds=swept;
                Packages.Add(mesh,package);return result;
            }
            catch{result.Dispose();throw;}
        }
        internal static Bounds WireBounds(RhodesProbePackage package)
        {Vector3 lo=new Vector3(package.WireBounds[0],package.WireBounds[1],package.WireBounds[2]),hi=new Vector3(package.WireBounds[3],package.WireBounds[4],package.WireBounds[5]);return new Bounds((lo+hi)*.5f,hi-lo);}
    }
}
