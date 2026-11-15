using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using UnityEngine;

namespace EndfieldDoK.ModelIntake
{
    // Explicitly called adapter only. No BepInEx/registry/Patcher dependencies.
    // Does not create colliders, navigation, weapons, production or animation.
    public sealed class RhodesLoadedModel : IDisposable
    {
        public GameObject Root { get; internal set; }
        public Transform[] Nodes { get; internal set; }
        public Renderer[] Renderers { get; internal set; }
        public LODGroup LodGroup { get; internal set; }
        public RhodesModelPackage Package { get; internal set; }
        public Bounds PresentationBounds { get; internal set; }
        public readonly List<string> Warnings=new List<string>();
        internal readonly List<UnityEngine.Object> Owned=new List<UnityEngine.Object>();
        internal readonly List<IDisposable> Shared=new List<IDisposable>();
        internal readonly Dictionary<string,Transform> ByName=new Dictionary<string,Transform>(StringComparer.Ordinal);
        internal readonly Dictionary<string,Transform> Modules=new Dictionary<string,Transform>(StringComparer.Ordinal);
        internal long AlbedoPixels;
        internal RhodesTeamColourProfile TeamColours;
        internal RhodesPbrProfile PbrMaterials;

        public Transform FindNode(string name) { Transform node;return ByName.TryGetValue(name,out node)?node:null; }
        public bool SetModuleVisible(string id,bool visible)
        {
            Transform module;if(!Modules.TryGetValue(id,out module)||module==null)return false;
            if(visible)
            {
                string branch="common";foreach(RhodesModule item in Package.Modules)if(item.Id==id){branch=item.Branch;break;}
                if(branch!="common")foreach(RhodesModule item in Package.Modules)
                    if(item.Branch!="common"&&item.Branch!=branch&&Nodes[item.Node].gameObject.activeSelf)
                        throw new InvalidOperationException("Aviation and gunnery modules are mutually exclusive");
            }
            module.gameObject.SetActive(visible);return true;
        }
        // Host supplies a full set derived from the actual owner's completed
        // research snapshot. This performs no research/commander inference.
        public void ApplyVisibleModules(string[] visibleIds)
        {
            if(visibleIds==null)throw new ArgumentNullException("visibleIds");
            Dictionary<string,bool> requested=new Dictionary<string,bool>(StringComparer.Ordinal);string branch=null;
            foreach(string id in visibleIds)
            {
                if(!Modules.ContainsKey(id))throw new ArgumentException("Unknown module id: "+id);
                requested[id]=true;
                foreach(RhodesModule item in Package.Modules)if(item.Id==id&&item.Branch!="common")
                {if(branch!=null&&branch!=item.Branch)throw new InvalidOperationException("Mixed aviation/gunnery visible set");branch=item.Branch;}
            }
            foreach(RhodesModule item in Package.Modules)Nodes[item.Node].gameObject.SetActive(requested.ContainsKey(item.Id));
        }
        // The host is the final authority after native interpolation/FOW. Do not
        // also attach the old RhodesCarrierView that enables all LOD renderers.
        public void SetNativeVisibility(bool nativeVisible,bool forceIconOnly,bool hullAlive)
        {
            bool show=nativeVisible&&!forceIconOnly&&hullAlive;
            if(Root!=null && Root.activeSelf!=show)Root.SetActive(show);
        }
        public void AttachInMetreSpace(Transform parent)
        {
            if(Root==null||parent==null)throw new ArgumentNullException("parent");
            Vector3 scale=parent.lossyScale;
            if(scale.x<=0f||scale.y<=0f||scale.z<=0f||Math.Abs(scale.x-scale.y)>.001f||Math.Abs(scale.x-scale.z)>.001f)
                throw new InvalidDataException("Use an unmirrored uniform-scale wrapper; shear requires an authored adapter");
            Root.transform.parent=parent;Root.transform.localPosition=Vector3.zero;Root.transform.localRotation=Quaternion.identity;
            Root.transform.localScale=new Vector3(1f/scale.x,1f/scale.y,1f/scale.z);
        }
        public void Dispose()
        {
            EndfieldDoK.MapUnitLab.FactionTrackLoader.ReleaseModel(this);
            if(Root!=null)UnityEngine.Object.DestroyImmediate(Root);Root=null;
            for(int index=Owned.Count-1;index>=0;index--)if(Owned[index]!=null)
            {Mesh mesh=Owned[index] as Mesh;if(mesh!=null)EndfieldDoK.MapUnitLab.RhodesProbeLoader.Forget(mesh);UnityEngine.Object.DestroyImmediate(Owned[index]);}
            Owned.Clear();RhodesSharedResources.Release(this);ByName.Clear();Modules.Clear();Nodes=new Transform[0];Renderers=new Renderer[0];LodGroup=null;TeamColours=null;PbrMaterials=null;
        }
    }

    public static class RhodesModelBuilder
    {
        // Reads RHS1 or RHS2 but never chooses this loader for the existing unit.
        // The returned root is inactive until the caller applies module state and
        // native visibility. Native template material itself is never modified.
        public static RhodesLoadedModel Load(string packageFile,Material nativeTemplate,int geometryLayer)
        {return LoadWithTextureBudget(packageFile,nativeTemplate,geometryLayer,16);}

        // Explicit bounded small-unit intake; the established ship budget stays 16.
        public static RhodesLoadedModel LoadSmallUnit(string packageFile,Material nativeTemplate,int geometryLayer)
        {return LoadWithTextureBudget(packageFile,nativeTemplate,geometryLayer,32);}

        static RhodesLoadedModel LoadWithTextureBudget(string packageFile,Material nativeTemplate,int geometryLayer,int textureBudget)
        {
            using(RhodesModelBuildJob job=BeginWithTextureBudget(packageFile,nativeTemplate,geometryLayer,textureBudget,false))
            {while(job.Step()){}return job.TakeResult();}
        }
        public static RhodesModelBuildJob BeginLoad(string packageFile,Material nativeTemplate,int geometryLayer,bool smallUnit)
        {return BeginLoad(packageFile,nativeTemplate,geometryLayer,smallUnit,true);}
        internal static RhodesModelBuildJob BeginLoad(string packageFile,Material nativeTemplate,int geometryLayer,bool smallUnit,bool queuedInput)
        {return BeginWithTextureBudget(packageFile,nativeTemplate,geometryLayer,smallUnit?32:16,queuedInput);}
        // Wreck atlases are immutable baked pixels. Use the existing distinct
        // GPU-only cache key; never discard a live model's readable lease.
        internal static RhodesModelBuildJob BeginLoadWreck(string packageFile,Material nativeTemplate,int geometryLayer)
        {return BeginWithTextureBudget(packageFile,nativeTemplate,geometryLayer,16,true,false);}
        static RhodesModelBuildJob BeginWithTextureBudget(string packageFile,Material nativeTemplate,int geometryLayer,int textureBudget,bool queuedInput)
        {return BeginWithTextureBudget(packageFile,nativeTemplate,geometryLayer,textureBudget,queuedInput,true);}
        static RhodesModelBuildJob BeginWithTextureBudget(string packageFile,Material nativeTemplate,int geometryLayer,int textureBudget,bool queuedInput,bool albedoReadable)
        {
            RhodesModelBuildJob job=new RhodesModelBuildJob();
            job.Initialize(BuildIncrementally(packageFile,nativeTemplate,geometryLayer,textureBudget,queuedInput,albedoReadable,job));return job;
        }
        static IEnumerator BuildIncrementally(string packageFile,Material nativeTemplate,int geometryLayer,int textureBudget,bool queuedInput,bool albedoReadable,RhodesModelBuildJob job)
        {
            if(nativeTemplate==null||nativeTemplate.shader==null||!nativeTemplate.shader.isSupported)throw new ArgumentException("Audited native shader template required");
            if(geometryLayer<0||geometryLayer>31)throw new ArgumentOutOfRangeException("geometryLayer");
            RhodesModelPackage package;
            if(queuedInput)
            {
                using(RhodesPackageDecodeQueue.Request input=RhodesPackageDecodeQueue.Submit(packageFile))
                {while(!input.IsComplete)yield return RhodesBuildSignal.WaitingForInput;package=input.TakeResult();}
            }
            else package=RhodesModelPackage.Read(packageFile);
            foreach(RhodesNode node in package.Nodes)if(node.Name=="PIVOT_V14_MainChassis")
            {EndfieldDoK.MapUnitLab.RhodesSuspensionV14Contract.RequireModelPackage(package,packageFile);break;}
            string folder=Path.GetDirectoryName(Path.GetFullPath(packageFile));RhodesLoadedModel result=new RhodesLoadedModel();result.Package=package;
            Vector3 visualMin=V3(package.Visual.Min),visualMax=V3(package.Visual.Max);
            result.PresentationBounds=new Bounds((visualMin+visualMax)*.5f,visualMax-visualMin);
            bool completed=false;
            try
            {
                result.PbrMaterials=RhodesPbrMaterials.Read(folder,packageFile,package.ContentSha256);
                if(result.PbrMaterials!=null)textureBudget=RhodesPbrMaterials.MaximumTextures;
                result.TeamColours=RhodesTeamColours.Prepare(packageFile,result);
                result.Root=new GameObject("EF_Rhodes_RHS"+package.Version+"_Intake");
                // The importer yields across menu/map scene transitions. Keep
                // its owned, inactive root alive until the host attaches it or
                // Dispose cancels the job; scene lifetime must not retire it.
                result.Root.hideFlags=HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(result.Root);
                result.Root.SetActive(false);result.Root.layer=geometryLayer;
                yield return null;
                result.Nodes=new Transform[package.Nodes.Length];
                for(int index=0;index<package.Nodes.Length;index++)
                {
                    RhodesNode data=package.Nodes[index];GameObject node=new GameObject(data.Name);node.layer=geometryLayer;
                    node.transform.parent=data.Parent<0?result.Root.transform:result.Nodes[data.Parent];
                    node.transform.localPosition=V3(data.Position);node.transform.localRotation=new Quaternion(data.Rotation[0],data.Rotation[1],data.Rotation[2],data.Rotation[3]);node.transform.localScale=V3(data.Scale);
                    result.Nodes[index]=node.transform;result.ByName.Add(data.Name,node.transform);
                    if((index&31)==31)yield return null;
                }
                Dictionary<string,Texture2D> textures=new Dictionary<string,Texture2D>(StringComparer.OrdinalIgnoreCase);
                Material[] materials=new Material[package.Materials.Length];Dictionary<string,Material> privateMaterials=new Dictionary<string,Material>(StringComparer.Ordinal);
                for(int index=0;index<materials.Length;index++)
                {materials[index]=CreateMaterial(package.Materials[index],folder,nativeTemplate,result,textures,textureBudget,albedoReadable);privateMaterials.Add(package.Materials[index].Name,materials[index]);yield return null;}
                List<Renderer> all=new List<Renderer>();List<Renderer>[] lods=new List<Renderer>[4];for(int index=0;index<lods.Length;index++)lods[index]=new List<Renderer>();
                int highest=-1;
                // Explicit FAR aliases opt into reuse/omission. Validate each
                // alias against the exact LOD1 node/material before allocating.
                Dictionary<string,bool> farReplacements=CollectPerformanceFarReplacements(package);
                bool performanceLods=farReplacements.Count>0;
                int meshIndex=0;
                foreach(RhodesMesh data in package.Meshes)
                {
                    bool authoredCarrierMesh=IsSourceCarrierMesh(data.Name);
                    bool tangents=RhodesPbrMaterials.RequiresTangents(package.Materials[data.Material],result.PbrMaterials);
                    string meshKey=package.ContentSha256+"|mesh:"+(meshIndex++)+"|tangent:"+tangents;
                    Mesh mesh=RhodesSharedResources.Mesh(meshKey,result,delegate
                    {
                    int count=data.Vertices.Length/10;Vector3[] positions=new Vector3[count],normals=new Vector3[count];Vector2[] uv=new Vector2[count],uv1=new Vector2[count];
                    for(int index=0;index<count;index++)
                    {
                        int at=index*10;positions[index]=new Vector3(data.Vertices[at],data.Vertices[at+1],data.Vertices[at+2]);
                        normals[index]=new Vector3(data.Vertices[at+3],data.Vertices[at+4],data.Vertices[at+5]).normalized;
                        uv[index]=new Vector2(data.Vertices[at+6],data.Vertices[at+7]);uv1[index]=new Vector2(data.Vertices[at+8],data.Vertices[at+9]);
                    }
                    Mesh created=new Mesh();created.name="EF_RHS2_"+data.Name;created.hideFlags=HideFlags.HideAndDontSave;
                    try
                    {
                    Color32[] colours=new Color32[count];for(int index=0;index<count;index++)colours[index]=new Color32(255,255,255,255);
                    created.vertices=positions;created.normals=normals;created.uv=uv;created.uv2=uv1;created.colors32=colours;created.triangles=data.Indices;
                    if(tangents)created.tangents=RhodesPbrMaterials.Tangents(positions,normals,uv,data.Indices);
                    created.RecalculateBounds();created.UploadMeshData(true);return created;
                    }catch{UnityEngine.Object.DestroyImmediate(created);throw;}
                    });
                    GameObject part=new GameObject("GEO_"+data.Name);part.layer=geometryLayer;part.transform.parent=result.Nodes[data.Node];
                    part.transform.localPosition=Vector3.zero;part.transform.localRotation=Quaternion.identity;part.transform.localScale=Vector3.one;
                    part.AddComponent<MeshFilter>().sharedMesh=mesh;MeshRenderer renderer=part.AddComponent<MeshRenderer>();renderer.sharedMaterial=materials[data.Material];all.Add(renderer);
                    if(data.Lod>=0){lods[data.Lod].Add(renderer);highest=Math.Max(highest,data.Lod);}
                    // Alias the SAME renderer on its original articulated node;
                    // ownership/FOW lists still contain each renderer only once.
                    if(performanceLods&&data.Lod==1&&!authoredCarrierMesh&&!farReplacements.ContainsKey(data.Name)&&!OmitPerformanceFar(data,package))
                        lods[2].Add(renderer);
                    yield return null;
                }
                if(package.ExternalTrackGeometry)
                {
                    if(package.Tracks==null)throw new InvalidDataException("FTK geometry requires a hash-validated sidecar");
                    EndfieldDoK.MapUnitLab.FactionTrackLoadResult tracks=EndfieldDoK.MapUnitLab.FactionTrackLoader.Load(result,RhodesTeamColours.UnpaintedTrackMaterials(materials,result),geometryLayer);
                    all.AddRange(tracks.Renderers);
                    for(int level=0;level<3;level++)lods[level].AddRange(tracks.LodRenderers[level]);
                }
                else if(File.Exists(Path.Combine(folder,FactionTrackPackage.FileName)))
                    throw new InvalidDataException("Unpaired FTK sidecar beside a static-only RHS model");
                string trackFile=Path.Combine(folder,EndfieldDoK.MapUnitLab.RhodesTrackLoader.PackageFileName);
                if(File.Exists(trackFile))
                {
                    EndfieldDoK.MapUnitLab.RhodesTrackLoadResult tracks=EndfieldDoK.MapUnitLab.RhodesTrackLoader.Load(folder,result.Root.transform,result.FindNode,
                        delegate(RhodesMaterial description)
                        {
                            Material value;if(privateMaterials.TryGetValue(description.Name,out value))return RhodesTeamColours.UnpaintedTrackMaterial(value,result);
                            value=CreateMaterial(description,folder,nativeTemplate,result,textures);privateMaterials.Add(description.Name,value);return RhodesTeamColours.UnpaintedTrackMaterial(value,result);
                        },geometryLayer);
                    all.AddRange(tracks.Renderers);result.Owned.AddRange(tracks.OwnedAssets);
                    for(int level=0;level<=highest;level++)lods[level].AddRange(tracks.CommonLodRenderers);
                    Bounds trackSwept=result.PresentationBounds;trackSwept.Encapsulate(tracks.RootLocalSweptBounds);result.PresentationBounds=trackSwept;
                }
                string probeFile=Path.Combine(folder,EndfieldDoK.MapUnitLab.RhodesProbeLoader.PackageFileName);
                if(File.Exists(probeFile))
                {
                    EndfieldDoK.MapUnitLab.RhodesProbeLoadResult probe=EndfieldDoK.MapUnitLab.RhodesProbeLoader.Load(folder,result.Root.transform,
                        delegate(RhodesMaterial description)
                        {
                            Material value;if(privateMaterials.TryGetValue(description.Name,out value))return RhodesTeamColours.UnpaintedTrackMaterial(value,result);
                            value=CreateMaterial(description,folder,nativeTemplate,result,textures);privateMaterials.Add(description.Name,value);return RhodesTeamColours.UnpaintedTrackMaterial(value,result);
                        },geometryLayer);
                    all.AddRange(probe.Renderers);result.Owned.AddRange(probe.OwnedAssets);
                    for(int level=0;level<=highest;level++)lods[level].AddRange(probe.CommonLodRenderers);
                    Bounds swept=result.PresentationBounds;swept.Encapsulate(probe.RootLocalSweptBounds);result.PresentationBounds=swept;
                }
                yield return null;
                RhodesGunneryDeckSeals.AddIfCompatible(result,materials,geometryLayer,all,lods,highest);
                RhodesRepairModuleConnectors51.AddIfCompatible(result,materials,geometryLayer,all,lods,highest);
                result.Renderers=all.ToArray();
                if(highest>=0)
                {
                    if(result.Root==null)throw new InvalidOperationException("Owned model root retired during incremental construction");
                    LODGroup group=result.Root.AddComponent<LODGroup>();LOD[] levels=new LOD[highest+1];float[] thresholds=performanceLods?new float[]{.32f,.10f,.03f,.01f}:new float[]{.6f,.25f,.08f,.01f};
                    for(int index=0;index<levels.Length;index++)levels[index]=new LOD(index==highest?.00001f:thresholds[index],lods[index].ToArray());
                    group.SetLODs(levels);group.RecalculateBounds();result.LodGroup=group;
                }
                foreach(RhodesModule module in package.Modules)
                {
                    Transform node=result.Nodes[module.Node];result.Modules.Add(module.Id,node);
                    // Never show mutually exclusive branch payloads or an installed
                    // upgrade merely because both geometries were received.
                    if(module.Branch!="common"||module.Role=="module")node.gameObject.SetActive(false);
                }
                if(package.Version==1)result.Warnings.Add("RHS1 legacy texture is technical compatibility only, not approved art direction");
                if(!package.Selection.Specified)result.Warnings.Add("Selection bounds unspecified: do not infer from probe/visual extent");
                if(!package.Navigation.Specified)result.Warnings.Add("Navigation footprint unmeasured: no simulation dimensions were assigned");
                result.Warnings.Add("Root inactive; host must initialize commander/research/branch state then call final native visibility gate");
                job.Publish(result);completed=true;
            }
            finally{if(!completed)result.Dispose();}
        }
        static Dictionary<string,bool> CollectPerformanceFarReplacements(RhodesModelPackage package)
        {
            const string suffix="_FAR_LOD2";
            Dictionary<string,RhodesMesh> replacements=new Dictionary<string,RhodesMesh>(StringComparer.Ordinal);
            foreach(RhodesMesh mesh in package.Meshes)
                if(mesh.Lod==2&&mesh.Name.EndsWith(suffix,StringComparison.Ordinal))
                {
                    string name=mesh.Name.Substring(0,mesh.Name.Length-suffix.Length);
                    if(string.IsNullOrEmpty(name)||replacements.ContainsKey(name))
                        throw new InvalidDataException("Duplicate/empty performance FAR alias: "+mesh.Name);
                    replacements.Add(name,mesh);
                }
            Dictionary<string,bool> result=new Dictionary<string,bool>(StringComparer.Ordinal);
            if(replacements.Count==0)return result;
            Dictionary<string,RhodesMesh> sources=new Dictionary<string,RhodesMesh>(StringComparer.Ordinal);
            foreach(RhodesMesh mesh in package.Meshes)if(mesh.Lod==1)
            {
                if(sources.ContainsKey(mesh.Name))throw new InvalidDataException("Ambiguous performance LOD1 mesh name: "+mesh.Name);
                sources.Add(mesh.Name,mesh);
            }
            foreach(KeyValuePair<string,RhodesMesh> pair in replacements)
            {
                RhodesMesh source;
                if(!sources.TryGetValue(pair.Key,out source)||source.Node!=pair.Value.Node||source.Material!=pair.Value.Material)
                    throw new InvalidDataException("Performance FAR alias must preserve exact LOD1 node/material: "+pair.Key);
                result.Add(pair.Key,true);
            }
            return result;
        }
        static bool OmitPerformanceFar(RhodesMesh mesh,RhodesModelPackage package)
        {
            string node=package.Nodes[mesh.Node].Name,material=package.Materials[mesh.Material].Name;
            return (node.StartsWith("CHASSIS_",StringComparison.Ordinal)&&
                (node.IndexOf("_PIVOT_RamBarrel_",StringComparison.Ordinal)>=0||node.IndexOf("_PIVOT_RamRod_",StringComparison.Ordinal)>=0))||
                material.StartsWith("WX03_",StringComparison.Ordinal);
        }
        static Material CreateMaterial(RhodesMaterial data,string folder,Material template,RhodesLoadedModel owner,Dictionary<string,Texture2D> textures)
        {return CreateMaterial(data,folder,template,owner,textures,owner.PbrMaterials==null?16:RhodesPbrMaterials.MaximumTextures);}

        static Material CreateMaterial(RhodesMaterial data,string folder,Material template,RhodesLoadedModel owner,Dictionary<string,Texture2D> textures,int textureBudget)
        {return CreateMaterial(data,folder,template,owner,textures,textureBudget,true);}
        static Material CreateMaterial(RhodesMaterial data,string folder,Material template,RhodesLoadedModel owner,Dictionary<string,Texture2D> textures,int textureBudget,bool albedoReadable)
        {
            string authorName=data.Name.StartsWith("V17SRC_",StringComparison.Ordinal)?data.Name.Substring(7):data.Name;
            bool aircraftLogo=IsAircraftLogoDecal(data.Name);
            bool decal=IsAuthoredWearDecal(authorName)||aircraftLogo;
            bool glazing=authorName.StartsWith("V06_",StringComparison.Ordinal)&&authorName.IndexOf("Glazing",StringComparison.OrdinalIgnoreCase)>=0;
            Color authoredEmission;bool emission=TryAuthoredEmission(authorName,out authoredEmission);
            Material material;
            if(decal)
            {
                Shader shader=Shader.Find("Standard");
                if(shader==null||!shader.isSupported)shader=Shader.Find("Legacy Shaders/Transparent/Diffuse");
                if(shader==null||!shader.isSupported)throw new InvalidDataException("No supported transparent shader for authored decal: "+data.Name);
                material=new Material(shader);
                if(material.HasProperty("_Mode"))material.SetFloat("_Mode",aircraftLogo?2f:3f);
                if(material.HasProperty("_SrcBlend"))material.SetInt("_SrcBlend",5);
                if(material.HasProperty("_DstBlend"))material.SetInt("_DstBlend",10);
                if(material.HasProperty("_ZWrite"))material.SetInt("_ZWrite",0);
                material.SetOverrideTag("RenderType","Transparent");material.DisableKeyword("_ALPHATEST_ON");material.EnableKeyword("_ALPHABLEND_ON");material.DisableKeyword("_ALPHAPREMULTIPLY_ON");material.renderQueue=3000;
            }
            else
            {
                if(glazing||emission)
                {
                    Shader glassShader=Shader.Find("Standard");
                    if(glassShader==null||!glassShader.isSupported)throw new InvalidDataException("Authored opaque glazing/emission requires a supported Standard shader: "+data.Name);
                    material=new Material(glassShader);
                }
                else material=new Material(template);
                if(material.HasProperty("_Mode"))material.SetFloat("_Mode",0f);
                if(material.HasProperty("_SrcBlend"))material.SetInt("_SrcBlend",1);
                if(material.HasProperty("_DstBlend"))material.SetInt("_DstBlend",0);
                if(material.HasProperty("_ZWrite"))material.SetInt("_ZWrite",1);
                material.SetOverrideTag("RenderType","Opaque");material.DisableKeyword("_ALPHATEST_ON");material.DisableKeyword("_ALPHABLEND_ON");material.DisableKeyword("_ALPHAPREMULTIPLY_ON");material.renderQueue=-1;
            }
            material.name=data.Name;material.hideFlags=HideFlags.HideAndDontSave;owner.Owned.Add(material);
            Texture2D albedo=null;
            if(!string.IsNullOrEmpty(data.Albedo))
            {
                string path=RhodesModelPackage.SafeAssetPath(folder,data.Albedo);
                if(!textures.TryGetValue(path,out albedo))
                {
                    FileInfo info=new FileInfo(path);if(!info.Exists||info.Length<24||info.Length>33554432L)throw new InvalidDataException("Albedo file size budget exceeded");
                    RhodesResourceContent content=RhodesResourceContent.Read(path,33554432L);int width,height;ReadPngSize(content.Bytes,out width,out height);
                    long pixelBudget=owner.PbrMaterials==null?33554432L:RhodesPbrMaterials.MaximumPixels;
                    if(width<1||height<1||width>4096||height>4096||textures.Count>=textureBudget||owner.AlbedoPixels+(long)width*height>pixelBudget)
                        throw new InvalidDataException("Albedo dimensions/aggregate pixel budget exceeded before decode");
                    albedo=RhodesSharedResources.Png(content,"albedo",false,albedoReadable,width,height,"EF_RHS2_Albedo_"+textures.Count,owner);
                    owner.AlbedoPixels+=(long)width*height;
                    textures.Add(path,albedo);
                }
            }
            material.mainTexture=albedo;material.mainTextureScale=Vector2.one;material.mainTextureOffset=Vector2.zero;
            foreach(string property in new string[]{"_MainTex","_BaseMap","_Diffuse"})if(material.HasProperty(property))material.SetTexture(property,albedo);
            // Blender's Principled values are linear. Native material colour
            // properties are authored in sRGB (this game's player colour space
            // is Gamma); passing the linear numbers directly made the hull black.
            Color colour=new Color(LinearToSRGB(data.BaseColour[0]),LinearToSRGB(data.BaseColour[1]),LinearToSRGB(data.BaseColour[2]),data.BaseColour[3]);
            if(material.HasProperty("_Color"))material.SetColor("_Color",colour);
            if(material.HasProperty("_BaseColor"))material.SetColor("_BaseColor",material.HasProperty("_Color")?Color.white:colour);
            if(material.HasProperty("_TrimColor"))material.SetColor("_TrimColor",Color.white);
            foreach(string property in new string[]{"_BumpMap","_OcclusionMap","_MetallicGlossMap","_SpecGlossMap","_EmissionMap","_DetailAlbedoMap","_DetailNormalMap","_ParallaxMap"})if(material.HasProperty(property))material.SetTexture(property,null);
            if(material.HasProperty("_BumpScale"))material.SetFloat("_BumpScale",0f);
            if(material.HasProperty("_Parallax"))material.SetFloat("_Parallax",0f);
            if(material.HasProperty("_DamageToggle"))material.SetFloat("_DamageToggle",1f);
            if(material.HasProperty("_SpecColor"))material.SetColor("_SpecColor",new Color(.16f,.16f,.16f,1f));
            // The donor's Surface/Detail maps describe its own UV atlas. They
            // must not produce speckles, glow or old-hull damage on this mesh.
            if(material.HasProperty("_Surface"))material.SetTexture("_Surface",SolidTexture("__neutral_surface",Color.black,owner,textures));
            if(material.HasProperty("_DetailMask"))material.SetTexture("_DetailMask",SolidTexture("__neutral_detail",Color.clear,owner,textures));
            if(material.HasProperty("_OverlayMask"))
            {
                Texture2D clear;
                if(!textures.TryGetValue("__empty_overlay",out clear))
                {
                    clear=new Texture2D(1,1,TextureFormat.RGBA32,false);clear.SetPixel(0,0,Color.clear);clear.Apply();
                    clear.name="EF_RHS2_ClearOverlay";clear.hideFlags=HideFlags.HideAndDontSave;owner.Owned.Add(clear);textures.Add("__empty_overlay",clear);
                }
                material.SetTexture("_OverlayMask",clear);
            }
            material.DisableKeyword("_NORMALMAP");material.DisableKeyword("_METALLICGLOSSMAP");material.DisableKeyword("_SPECGLOSSMAP");material.DisableKeyword("_DETAIL_MULX2");material.DisableKeyword("_PARALLAXMAP");material.DisableKeyword("_EMISSION");
            if(material.HasProperty("_Glossiness"))material.SetFloat("_Glossiness",1f-data.Roughness);
            if(material.HasProperty("_Metallic"))material.SetFloat("_Metallic",data.Metallic);
            if(emission)
            {
                if(!material.HasProperty("_EmissionColor"))throw new InvalidDataException("Authored emission requires Standard _EmissionColor: "+data.Name);
                material.SetColor("_EmissionColor",authoredEmission);material.EnableKeyword("_EMISSION");
                // Uniform name-profile emission only; _EmissionMap stays null.
                // No new PNG, GI, bloom or native scene-lighting changes.
            }
            RhodesPbrMaterials.Bind(data,material,folder,owner,textures);
            EndfieldDoK.MapUnitLab.RhodesThermalVisuals.ConfigureMaterial(data.Name,material);
            if(owner.PbrMaterials==null&&(!string.IsNullOrEmpty(data.Normal)||!string.IsNullOrEmpty(data.Emission)))owner.Warnings.Add(data.Name+": normal/emission retained in package, not bound before native shader/UV audit");
            RhodesTeamColours.BindMaterial(data,material,owner);
            return material;
        }
        static float LinearToSRGB(float value)
        {value=Mathf.Max(0f,value);return value<=.0031308f?value*12.92f:1.055f*Mathf.Pow(value,1f/2.4f)-.055f;}
        static Texture2D SolidTexture(string key,Color colour,RhodesLoadedModel owner,Dictionary<string,Texture2D> textures)
        {
            Texture2D texture;if(textures.TryGetValue(key,out texture))return texture;
            texture=new Texture2D(1,1,TextureFormat.RGBA32,false);texture.name="EF_RHS2"+key;texture.hideFlags=HideFlags.HideAndDontSave;
            texture.SetPixel(0,0,colour);texture.Apply();owner.Owned.Add(texture);textures.Add(key,texture);return texture;
        }
        static bool IsSourceCarrierMesh(string name)
        {return name.StartsWith("V17_",StringComparison.Ordinal)||name.StartsWith("V18_",StringComparison.Ordinal);}
        internal static bool IsAircraftLogoDecal(string name)
        {
            return name=="EF_Rhodes_Interceptor_MP_10_RI_Official_SVG_Painted_Decal"||
                name=="EF_Rhodes_HeavyBomber_MP_10_RI_Official_SVG_Painted_Decal";
        }
        static bool IsAuthoredWearDecal(string name)
        {return name=="WX03_runoff"||name=="WX03_chip"||name=="WX03_soot"||name=="WX03_scuff"||name=="WX03_oil";}
        static bool TryAuthoredEmission(string name,out Color colour)
        {
            colour=Color.black;
            if(name=="V07_Emission_CoreAmber")colour=new Color(2.1f,.72f,.12f,1f);
            else if(name=="V07_Emission_WorkWhite")colour=new Color(1.6f,1.8f,2f,1f);
            else if(name=="V07_Emission_NavRed")colour=new Color(1.65f,.035f,.025f,1f);
            else if(name=="V07_Emission_NavGreen")colour=new Color(.035f,1.4f,.17f,1f);
            // V18's fixed bow lamps carry emissive lenses only. Preserve the
            // authored Principled colour * strength without adding scene lights.
            else if(name=="V18_Headlight_ColdWhite")colour=new Color(1.21f,1.716f,2.2f,1f);
            else if(name=="V18_Headlight_AmberMarker")colour=new Color(1.53f,.629f,.0935f,1f);
            else if(name=="V18_Side_WorkLight")colour=new Color(.56f,.344f,.096f,1f);
            else if(name=="V18_Lamp_Ivory")colour=new Color(.287f,.2695f,.1995f,1f);
            else return false;
            return true;
        }
        static void ReadPngSize(byte[] bytes,out int width,out int height)
        {
            byte[] signature={137,80,78,71,13,10,26,10};for(int index=0;index<signature.Length;index++)if(bytes[index]!=signature[index])throw new InvalidDataException("V01 runtime textures must be PNG");
            if(bytes[12]!=73||bytes[13]!=72||bytes[14]!=68||bytes[15]!=82)throw new InvalidDataException("PNG IHDR missing");
            width=(bytes[16]<<24)|(bytes[17]<<16)|(bytes[18]<<8)|bytes[19];height=(bytes[20]<<24)|(bytes[21]<<16)|(bytes[22]<<8)|bytes[23];
        }
        static Vector3 V3(float[] values) { return new Vector3(values[0],values[1],values[2]); }
    }
}
