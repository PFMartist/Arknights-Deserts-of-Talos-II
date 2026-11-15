using System;
using System.Collections.Generic;
using System.IO;
using EndfieldDoK.ModelIntake;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    [Serializable]
    public sealed class RhodesTrackSegmentState
    {
        public int Kind;
        public Vector2 A,B;
        public float Radius,Angle,Length;
    }
    [Serializable]
    public sealed class RhodesTrackGroupState
    {
        public string BogieName;
        public float LocalRight,Perimeter;
        public float DistanceScale=1f;
        public RhodesTrackSegmentState[] Path;
        public Transform[] Bones;
        public int[] Kinds;
        public float[] BaseDistances,Radii,InitialAngles;
        public Vector3[] InitialPositions;
        public SkinnedMeshRenderer Renderer;
    }
    // Public serializable transform arrays are remapped by Unity.Instantiate.
    // Mutable phase/caches belong only to the live driver, never this template.
    public sealed class RhodesTrackRigBinding:MonoBehaviour
    {
        public Transform PresentationRoot;
        public RhodesTrackGroupState[] Groups;
    }
    // Rebuilt after Instantiate from native skin/bone references. This object
    // is owned by one live driver and is never a Unity serialization contract.
    public sealed class RhodesTrackLiveRig
    {
        public Transform PresentationRoot;
        public RhodesTrackGroupState[] Groups;
        public bool LegacyBindingPresent,LegacyGroupsPresent;
        public int NativeBoneArrays,RepairedBoneArrays,BogieLookupFallbacks;
    }
    public sealed class RhodesTrackLoadResult:IDisposable
    {
        public Renderer[] Renderers;
        // These are COMMON renderers: append these same references to each
        // hull LOD's list. There is one mesh/renderer per bogie, never duplicate
        // LOD geometry. Native FOW must receive the complete Renderers array.
        public Renderer[] CommonLodRenderers;
        public RhodesTrackRigBinding Binding;
        public Bounds RootLocalSweptBounds;
        public UnityEngine.Object[] OwnedAssets;
        internal GameObject[] CreatedObjects;
        public void Dispose()
        {
            if(CreatedObjects!=null)for(int i=CreatedObjects.Length-1;i>=0;i--)if(CreatedObjects[i]!=null)UnityEngine.Object.DestroyImmediate(CreatedObjects[i]);
            if(Binding!=null)UnityEngine.Object.DestroyImmediate(Binding);
            if(OwnedAssets!=null)foreach(UnityEngine.Object asset in OwnedAssets)if(asset!=null)UnityEngine.Object.DestroyImmediate(asset);
            CreatedObjects=null;OwnedAssets=null;Binding=null;Renderers=new Renderer[0];CommonLodRenderers=Renderers;
        }
    }
    public static class RhodesTrackLoader
    {
        public const string PackageFileName="rhodes_tracks.tkr";
        sealed class SourceCache
        {
            public RhodesTrackPackage Package;
            public Mesh[] Meshes;
        }
        // Only immutable package/mesh data survives template-to-live cloning.
        // Registering the next template replaces this cache before live views.
        static SourceCache CurrentSource;
        public static void ReleaseTemplateCache(){CurrentSource=null;}
        // Front-end previews build the same immutable skin without becoming the
        // live battle template. Call Capture/Restore in a main-thread try/finally
        // around that isolated load; neither operation owns or disposes meshes.
        public static object CaptureTemplate(){return CurrentSource;}
        public static void RestoreTemplate(object snapshot)
        {
            if(snapshot!=null && !(snapshot is SourceCache))throw new ArgumentException("Invalid Rhodes track template snapshot","snapshot");
            CurrentSource=(SourceCache)snapshot;
        }
        // The supplied factory is the hull builder's existing private material
        // cache. TKR contains full descriptions, never historical material IDs.
        // On success the host owns result.OwnedAssets and template objects;
        // do not dispose shared meshes from a cloned live rig's OnDestroy.
        public static RhodesTrackLoadResult Load(string folder,Transform presentationRoot,
            Func<string,Transform> findNode,Func<RhodesMaterial,Material> materialFactory,int layer)
        {
            if(presentationRoot==null||findNode==null||materialFactory==null)throw new ArgumentNullException("Track loader host interface");
            if(layer<0||layer>31)throw new ArgumentOutOfRangeException("layer");
            string file=Path.Combine(Path.GetFullPath(folder),PackageFileName);
            RhodesTrackPackage package=RhodesTrackPackage.Read(file);
            if((findNode("PIVOT_V14_MainChassis")!=null)!=package.IndependentBeltPitch)
                throw new InvalidDataException("V14 model and independent-belt TKR2 must be supplied together");
            if(package.IndependentBeltPitch)
            {
                RhodesSuspensionV14Contract.RequireTrackPackage(file);
                if(package.ModuleCount!=RhodesSuspensionV14Contract.ModuleCount||package.Groups.Length!=RhodesSuspensionV14Contract.TrackCount||findNode("PIVOT_V14_MainChassis")==null)
                    throw new InvalidDataException("TKR2 module/belt layout does not match its verified V14 model");
            }
            if(presentationRoot.GetComponent<RhodesTrackRigBinding>()!=null)throw new InvalidOperationException("Rhodes track skin already loaded");
            RhodesTrackLoadResult result=new RhodesTrackLoadResult();
            List<GameObject> created=new List<GameObject>();List<UnityEngine.Object> owned=new List<UnityEngine.Object>();List<Renderer> renderers=new List<Renderer>();
            try
            {
                Material[] materials=new Material[package.Materials.Length];
                for(int i=0;i<materials.Length;i++)
                {materials[i]=materialFactory(package.Materials[i]);if(materials[i]==null)throw new InvalidDataException("Host material factory returned null for "+package.Materials[i].Name);}
                RhodesTrackRigBinding rig=presentationRoot.gameObject.AddComponent<RhodesTrackRigBinding>();result.Binding=rig;rig.PresentationRoot=presentationRoot;rig.Groups=new RhodesTrackGroupState[package.Groups.Length];
                bool haveBounds=false;Bounds combined=new Bounds();
                for(int i=0;i<package.Groups.Length;i++)
                {
                    RhodesTrackGroup source=package.Groups[i];Transform bogie=findNode(source.BogieName);
                    if(bogie==null||!bogie.IsChildOf(presentationRoot))throw new InvalidDataException("TKR bogie absent from the current static model: "+source.BogieName);
                    Transform rigSpace=bogie;
                    if(package.IndependentBeltPitch)
                    {
                        GameObject space=new GameObject("EF_TrackRigSpace_"+source.BogieName);space.layer=layer;space.transform.parent=bogie;
                        space.transform.localPosition=new Vector3(source.RigOffset[0],source.RigOffset[1],source.RigOffset[2]);space.transform.localRotation=Quaternion.identity;
                        space.transform.localScale=new Vector3(source.RigScale[0],source.RigScale[1],source.RigScale[2]);created.Add(space);rigSpace=space.transform;
                    }
                    RhodesTrackGroupState group=new RhodesTrackGroupState();group.BogieName=source.BogieName;group.LocalRight=source.LocalRight;group.Perimeter=source.Perimeter;
                    group.DistanceScale=source.RigScale[2];
                    group.Path=new RhodesTrackSegmentState[source.Path.Length];
                    for(int k=0;k<group.Path.Length;k++)
                    {
                        RhodesTrackPath p=source.Path[k];RhodesTrackSegmentState segment=new RhodesTrackSegmentState();segment.Kind=p.Kind;segment.A=new Vector2(p.AY,p.AZ);segment.B=new Vector2(p.BY,p.BZ);segment.Radius=p.Radius;segment.Angle=p.Angle;segment.Length=p.Length;group.Path[k]=segment;
                    }
                    int boneCount=source.Bones.Length;group.Bones=new Transform[boneCount];group.Kinds=new int[boneCount];group.BaseDistances=new float[boneCount];group.Radii=new float[boneCount];group.InitialAngles=new float[boneCount];group.InitialPositions=new Vector3[boneCount];Matrix4x4[] bindposes=new Matrix4x4[boneCount];
                    for(int k=0;k<boneCount;k++)
                    {
                        RhodesTrackBone data=source.Bones[k];Vector3 p=new Vector3(data.InitialPosition[0],data.InitialPosition[1],data.InitialPosition[2]);Quaternion q=Quaternion.AngleAxis(data.InitialAngle*Mathf.Rad2Deg,Vector3.right);
                        GameObject bone=new GameObject("EF_TrackBone_"+data.Name);bone.layer=layer;bone.transform.parent=rigSpace;bone.transform.localPosition=p;bone.transform.localRotation=q;bone.transform.localScale=Vector3.one;created.Add(bone);
                        group.Bones[k]=bone.transform;group.Kinds[k]=data.Kind;group.BaseDistances[k]=data.BaseDistance;group.Radii[k]=data.Radius;group.InitialPositions[k]=p;group.InitialAngles[k]=data.InitialAngle;
                        // Vertices are in this bogie's own local bind space.
                        bindposes[k]=Matrix4x4.TRS(p,q,Vector3.one).inverse;
                    }
                    int count=source.VertexBones.Length;Vector3[] positions=new Vector3[count],normals=new Vector3[count];Vector2[] uv=new Vector2[count],uv1=new Vector2[count];Color32[] colours=new Color32[count];BoneWeight[] weights=new BoneWeight[count];
                    for(int k=0;k<count;k++)
                    {
                        int at=k*10;float[] v=source.Vertices;positions[k]=new Vector3(v[at],v[at+1],v[at+2]);normals[k]=new Vector3(v[at+3],v[at+4],v[at+5]);uv[k]=new Vector2(v[at+6],v[at+7]);uv1[k]=new Vector2(v[at+8],v[at+9]);
                        colours[k]=new Color32(255,255,255,255);
                        BoneWeight weight=new BoneWeight();weight.boneIndex0=source.VertexBones[k];weight.weight0=1f;weights[k]=weight;
                    }
                    Mesh mesh=new Mesh();mesh.name="EF_TKR_"+source.BogieName;mesh.hideFlags=HideFlags.HideAndDontSave;owned.Add(mesh);mesh.vertices=positions;mesh.normals=normals;mesh.uv=uv;mesh.uv2=uv1;mesh.colors32=colours;mesh.bindposes=bindposes;mesh.boneWeights=weights;mesh.subMeshCount=source.Submeshes.Length;
                    Material[] groupMaterials=new Material[source.Submeshes.Length];
                    for(int k=0;k<source.Submeshes.Length;k++){mesh.SetTriangles(source.Submeshes[k].Indices,k);groupMaterials[k]=materials[source.Submeshes[k].Material];}
                    mesh.RecalculateBounds();
                    // Keep native CPU mesh buffers. Unity 5.2 PlayerSettings
                    // enables GPU skinning, but backend acceptance is runtime.
                    GameObject geometry=new GameObject("GEO_EF_TKR_"+source.BogieName);geometry.layer=layer;geometry.transform.parent=rigSpace;geometry.transform.localPosition=Vector3.zero;geometry.transform.localRotation=Quaternion.identity;geometry.transform.localScale=Vector3.one;created.Add(geometry);
                    SkinnedMeshRenderer renderer=geometry.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh=mesh;renderer.sharedMaterials=groupMaterials;renderer.bones=group.Bones;renderer.rootBone=rigSpace;renderer.quality=SkinQuality.Bone1;renderer.updateWhenOffscreen=false;
                    Vector3 lo=new Vector3(source.Bounds[0],source.Bounds[1],source.Bounds[2]),hi=new Vector3(source.Bounds[3],source.Bounds[4],source.Bounds[5]);Bounds bounds=new Bounds((lo+hi)*.5f,hi-lo);renderer.localBounds=bounds;group.Renderer=renderer;rig.Groups[i]=group;renderers.Add(renderer);
                    for(int corner=0;corner<8;corner++)
                    {
                        Vector3 p=bounds.center+new Vector3((corner&1)==0?-bounds.extents.x:bounds.extents.x,(corner&2)==0?-bounds.extents.y:bounds.extents.y,(corner&4)==0?-bounds.extents.z:bounds.extents.z);
                        p=presentationRoot.InverseTransformPoint(rigSpace.TransformPoint(p));if(!haveBounds){combined=new Bounds(p,Vector3.zero);haveBounds=true;}else combined.Encapsulate(p);
                    }
                }
                result.Renderers=renderers.ToArray();result.CommonLodRenderers=result.Renderers;result.RootLocalSweptBounds=combined;result.OwnedAssets=owned.ToArray();result.CreatedObjects=created.ToArray();
                Mesh[] meshes=new Mesh[renderers.Count];for(int i=0;i<meshes.Length;i++)meshes[i]=((SkinnedMeshRenderer)renderers[i]).sharedMesh;
                CurrentSource=new SourceCache{Package=package,Meshes=meshes};return result;
            }
            catch
            {result.CreatedObjects=created.ToArray();result.OwnedAssets=owned.ToArray();result.Dispose();throw;}
        }
        public static RhodesTrackLiveRig RebindLive(Transform presentationRoot,Transform ownerRoot,Func<string,Transform> findNode)
        {
            if(presentationRoot==null||ownerRoot==null||presentationRoot==ownerRoot||!presentationRoot.IsChildOf(ownerRoot)||findNode==null)
                throw new InvalidOperationException("Track live root is not inside the current native owner");
            SourceCache source=CurrentSource;
            if(source==null||source.Package==null||source.Meshes==null||source.Meshes.Length!=source.Package.Groups.Length)
                throw new InvalidOperationException("Track immutable template cache is absent");
            RhodesTrackLiveRig live=new RhodesTrackLiveRig();live.PresentationRoot=presentationRoot;
            RhodesTrackRigBinding legacy=presentationRoot.GetComponent<RhodesTrackRigBinding>();
            live.LegacyBindingPresent=legacy!=null;live.LegacyGroupsPresent=legacy!=null&&legacy.Groups!=null;
            live.Groups=new RhodesTrackGroupState[source.Package.Groups.Length];
            Dictionary<string,Transform> fallbackBogies=null;
            for(int i=0;i<live.Groups.Length;i++)
            {
                RhodesTrackGroup data=source.Package.Groups[i];Transform bogie=findNode(data.BogieName);
                if(bogie==null||bogie.name!=data.BogieName||!bogie.IsChildOf(presentationRoot))
                {
                    if(fallbackBogies==null)
                    {
                        fallbackBogies=new Dictionary<string,Transform>(StringComparer.Ordinal);
                        HashSet<string> required=new HashSet<string>(StringComparer.Ordinal);
                        foreach(RhodesTrackGroup group in source.Package.Groups)required.Add(group.BogieName);
                        foreach(Transform node in presentationRoot.GetComponentsInChildren<Transform>(true))
                            if(required.Contains(node.name))
                            {if(fallbackBogies.ContainsKey(node.name))throw new InvalidOperationException("Duplicate live bogie "+node.name);fallbackBogies.Add(node.name,node);}
                    }
                    if(!fallbackBogies.TryGetValue(data.BogieName,out bogie))throw new InvalidOperationException("Live bogie absent: "+data.BogieName);
                    live.BogieLookupFallbacks++;
                }
                Transform rigSpace=bogie;
                if(source.Package.IndependentBeltPitch)
                {
                    rigSpace=bogie.Find("EF_TrackRigSpace_"+data.BogieName);
                    if(rigSpace==null||rigSpace.parent!=bogie||Quaternion.Angle(rigSpace.localRotation,Quaternion.identity)>.01f||
                        (rigSpace.localPosition-new Vector3(data.RigOffset[0],data.RigOffset[1],data.RigOffset[2])).sqrMagnitude>.000001f||
                        (rigSpace.localScale-new Vector3(data.RigScale[0],data.RigScale[1],data.RigScale[2])).sqrMagnitude>.000001f)
                        throw new InvalidOperationException("Live independent-belt rig-space affine mismatch: "+data.BogieName);
                }
                SkinnedMeshRenderer renderer=null;string rendererName="GEO_EF_TKR_"+data.BogieName;
                foreach(SkinnedMeshRenderer candidate in bogie.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if(candidate!=null&&candidate.name==rendererName)
                    {if(renderer!=null)throw new InvalidOperationException("Duplicate live track skin "+rendererName);renderer=candidate;}
                if(renderer==null||!renderer.transform.IsChildOf(presentationRoot)||source.Meshes[i]==null||renderer.sharedMesh!=source.Meshes[i])
                    throw new InvalidOperationException("Live track skin/current immutable mesh identity mismatch: "+data.BogieName);
                if(renderer.sharedMesh.vertexCount!=data.VertexBones.Length||renderer.sharedMesh.subMeshCount!=data.Submeshes.Length||renderer.sharedMesh.bindposes.Length!=data.Bones.Length)
                    throw new InvalidOperationException("Live track skin layout mismatch: "+data.BogieName);
                Transform[] bones=renderer.bones;bool nativeBones=bones!=null&&bones.Length==data.Bones.Length;
                if(nativeBones)
                {
                    HashSet<Transform> unique=new HashSet<Transform>();
                    for(int k=0;k<bones.Length;k++)
                        if(bones[k]==null||bones[k].parent!=rigSpace||bones[k].name!="EF_TrackBone_"+data.Bones[k].Name||!bones[k].IsChildOf(presentationRoot)||!unique.Add(bones[k])){nativeBones=false;break;}
                }
                if(nativeBones){bones=(Transform[])bones.Clone();live.NativeBoneArrays++;}
                else
                {
                    // Fallback is limited to this bogie's direct existing bones.
                    // No source/template transforms and no new geometry are used.
                    Dictionary<string,Transform> byName=new Dictionary<string,Transform>(StringComparer.Ordinal);
                    foreach(Transform node in rigSpace)
                        if(node.name.StartsWith("EF_TrackBone_",StringComparison.Ordinal))
                        {if(byName.ContainsKey(node.name))throw new InvalidOperationException("Duplicate live track bone "+node.name);byName.Add(node.name,node);}
                    if(byName.Count!=data.Bones.Length)throw new InvalidOperationException("Live track bone count mismatch: "+data.BogieName);
                    bones=new Transform[data.Bones.Length];
                    for(int k=0;k<bones.Length;k++)if(!byName.TryGetValue("EF_TrackBone_"+data.Bones[k].Name,out bones[k]))throw new InvalidOperationException("Live track bone absent: "+data.Bones[k].Name);
                    renderer.bones=bones;live.RepairedBoneArrays++;
                }
                if(renderer.rootBone!=rigSpace)renderer.rootBone=rigSpace;
                Vector3 lo=new Vector3(data.Bounds[0],data.Bounds[1],data.Bounds[2]),hi=new Vector3(data.Bounds[3],data.Bounds[4],data.Bounds[5]);
                renderer.localBounds=new Bounds((lo+hi)*.5f,hi-lo);
                RhodesTrackGroupState state=new RhodesTrackGroupState();state.BogieName=data.BogieName;state.LocalRight=data.LocalRight;state.Perimeter=data.Perimeter;
                state.DistanceScale=data.RigScale[2];
                state.Bones=bones;state.Renderer=renderer;state.Path=new RhodesTrackSegmentState[data.Path.Length];
                for(int k=0;k<state.Path.Length;k++)
                {RhodesTrackPath p=data.Path[k];state.Path[k]=new RhodesTrackSegmentState{Kind=p.Kind,A=new Vector2(p.AY,p.AZ),B=new Vector2(p.BY,p.BZ),Radius=p.Radius,Angle=p.Angle,Length=p.Length};}
                int count=data.Bones.Length;state.Kinds=new int[count];state.BaseDistances=new float[count];state.Radii=new float[count];state.InitialAngles=new float[count];state.InitialPositions=new Vector3[count];
                for(int k=0;k<count;k++)
                {RhodesTrackBone bone=data.Bones[k];state.Kinds[k]=bone.Kind;state.BaseDistances[k]=bone.BaseDistance;state.Radii[k]=bone.Radius;state.InitialAngles[k]=bone.InitialAngle;state.InitialPositions[k]=new Vector3(bone.InitialPosition[0],bone.InitialPosition[1],bone.InitialPosition[2]);}
                live.Groups[i]=state;
            }
            return live;
        }
    }
}
