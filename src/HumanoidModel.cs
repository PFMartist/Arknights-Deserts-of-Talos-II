using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Immutable meshes/materials and QVV arrays are cached independently of BVM.
    // Only native UnitView simulation moves the actor; there is no command,
    // targeting, fire, damage, selection or UI implementation here.
    public static class HumanoidModel
    {
        public const string NativeEmissionAlias="EF_Humanoid_Emission";
        public const string NativeSimOriginAlias="EF_Humanoid_SimOrigin";
        static string DirectoryOverride;
        static GameObject Templates;
        static readonly Dictionary<string, HumanoidData> Cache = new Dictionary<string, HumanoidData>(StringComparer.OrdinalIgnoreCase);
        static readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();
        static readonly HashSet<string> VerifiedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly ModCore.DeferredBindingQueue<BBI.Core.ComponentModel.Entity,UnitView>.BindAttempt BindSpawned=TryBindSpawned;
        public static bool ViewsVerified { get { return VerifiedFolders.Count >= 2; } }
        public static string AssetDirectory
        {
            get { return DirectoryOverride ?? CustomUnits.AssetDirectory; }
            set { DirectoryOverride = value == null ? null : Path.GetFullPath(value); }
        }

        public static GameObject CreateModel(GameObject nativeWrapper, string relativeAssetFolder, bool squad, int memberCount)
        {
            if (nativeWrapper == null) throw new ArgumentNullException("nativeWrapper");
            int members = squad ? memberCount : 1;
            if (members < 1 || members > 12) throw new ArgumentOutOfRangeException("memberCount");
            string folder = SafePath(AssetDirectory, relativeAssetFolder);
            HumanoidData data;
            if (!Cache.TryGetValue(folder, out data))
            {
                Material template = null;
                LODGroup sourceGroup=nativeWrapper.GetComponent<LODGroup>();
                if(sourceGroup!=null)foreach(LOD lod in sourceGroup.GetLODs())foreach(Renderer r in lod.renderers)
                    if(template==null&&r!=null&&r.sharedMaterial!=null&&r.sharedMaterial.shader!=null&&r.sharedMaterial.shader.isSupported)template=r.sharedMaterial;
                foreach (Renderer r in nativeWrapper.GetComponentsInChildren<Renderer>(true))
                    if (r != null && r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.isSupported)
                    { if(template==null&&!ProtectedName(r.transform))template = r.sharedMaterial; }
                if (template == null) throw new InvalidOperationException("Native unit shader template missing for humanoid");
                data = Read(folder, template); Cache.Add(folder, data);
            }
            if (Templates == null)
            {
                Templates = new GameObject("MapUnitLab.HumanoidTemplates"); Templates.SetActive(false);
                Templates.hideFlags = HideFlags.HideAndDontSave; UnityEngine.Object.DontDestroyOnLoad(Templates);
            }
            GameObject model = (GameObject)UnityEngine.Object.Instantiate(nativeWrapper);
            model.name = nativeWrapper.name; model.transform.parent = Templates.transform;
            model.transform.localPosition = Vector3.zero; model.transform.localRotation = nativeWrapper.transform.localRotation;
            model.transform.localScale = nativeWrapper.transform.localScale;
            // Preserve transforms, native aim/weapon hardpoints, UnitView, selection
            // colliders and sensor components. Empty only inherited unit geometry.
            LODGroup group = model.GetComponent<LODGroup>();
            List<Renderer> geometry = new List<Renderer>();
            if (group != null) foreach (LOD lod in group.GetLODs()) foreach (Renderer r in lod.renderers) if (r != null && !geometry.Contains(r)) geometry.Add(r);
            foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (!geometry.Contains(r)) geometry.Add(r);
            foreach (MeshRenderer r in model.GetComponentsInChildren<MeshRenderer>(true))
                if (!ProtectedName(r.transform)&&!geometry.Contains(r)) geometry.Add(r);
            Mesh empty = new Mesh(); empty.name = "EF_Humanoid_EmptyInheritedGeometry"; empty.hideFlags = HideFlags.HideAndDontSave; Owned.Add(empty);
            int layer = model.layer;
            foreach (Renderer r in geometry)
            {
                layer = r.gameObject.layer; r.enabled = false;
                SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
                if (smr != null) smr.sharedMesh = empty;
                MeshFilter filter = r.GetComponent<MeshFilter>(); if (filter != null) filter.sharedMesh = empty;
            }
            int removed = StripVehicleEffects(model);
            Transform presentation = NewTransform("EF_Humanoid_PresentationRoot", model.transform, layer);
            HumanoidBinding binding = model.AddComponent<HumanoidBinding>();
            binding.AssetFolder = relativeAssetFolder; binding.MemberCount = members; binding.NodeCount = data.Nodes.Length;
            binding.PresentationRoot = presentation; binding.Bones = new Transform[members * data.Nodes.Length];
            binding.MemberRoots = new Transform[members]; binding.Renderers = new SkinnedMeshRenderer[members * data.Parts.Length];
            binding.PartVisibility=HumanoidRaceLayout.Load(folder,data.Parts.Length,members,out binding.MemberRaces);
            binding.AliveMembers=new bool[members];for(int member=0;member<members;member++)binding.AliveMembers[member]=true;
            binding.MemberEmission=new Transform[members];
            binding.NativeEmission=NewTransform(NativeEmissionAlias,model.transform,layer);
            NewTransform(NativeSimOriginAlias,model.transform,layer);
            int emissionNode=-1;string suffix=squad?"/EF_Guard_SourceMuzzle":"/Bip001_R_Hand";
            for(int node=0;node<data.Nodes.Length;node++)if(data.Nodes[node].Name.EndsWith(suffix,StringComparison.Ordinal))
            {if(emissionNode!=-1)throw new InvalidDataException("Ambiguous humanoid emission source node");emissionNode=node;}
            if(emissionNode<0)throw new InvalidDataException("Humanoid emission source node is missing: "+suffix);
            List<Renderer> rendered = new List<Renderer>();
            for (int member = 0; member < members; member++)
            {
                Transform origin = NewTransform("EF_Humanoid_Member" + member, presentation, layer); binding.MemberRoots[member] = origin;
                // One native entity owns this formation; all six are presentation
                // members, with independent bones and animation clocks.
                origin.localPosition = FormationOffset(member, members);
                for (int i = 0; i < data.Nodes.Length; i++)
                {
                    HumanoidNode n = data.Nodes[i]; int index = member * data.Nodes.Length + i;
                    Transform bone = NewTransform("EF_Humanoid_Bone" + i + "_" + n.Name,
                        n.Parent < 0 ? origin : binding.Bones[member * data.Nodes.Length + n.Parent], layer);
                    bone.localPosition = n.Position; bone.localRotation = n.Rotation; bone.localScale = n.Scale;
                    binding.Bones[index] = bone;
                }
                for (int i = 0; i < data.Parts.Length; i++)
                {
                    HumanoidPart part = data.Parts[i]; Transform item = NewTransform("EF_Humanoid_" + part.Name + "_LOD0", origin, layer);
                    SkinnedMeshRenderer r = item.gameObject.AddComponent<SkinnedMeshRenderer>(); r.sharedMesh = part.Mesh; r.sharedMaterial = part.Material;
                    Transform[] bones = new Transform[part.PaletteCount];
                    for (int b = 0; b < bones.Length; b++) bones[b] = binding.Bones[member * data.Nodes.Length + data.PaletteNodes[part.PaletteStart+b]];
                    r.bones = bones; r.rootBone = binding.Bones[member*data.Nodes.Length]; r.quality = SkinQuality.Bone4;
                    r.localBounds = part.Bounds; r.updateWhenOffscreen = false;
                    r.enabled=binding.PartVisibility[member*data.Parts.Length+i];
                    binding.Renderers[member * data.Parts.Length+i] = r; rendered.Add(r);
                }
                binding.MemberEmission[member]=binding.Bones[member*data.Nodes.Length+emissionNode];
                ApplyPose(binding, data, member, data.Idle, 0f, null, 1f);
            }
            binding.NativeEmission.position=binding.MemberEmission[0].position;
            binding.NativeEmission.rotation=binding.MemberEmission[0].rotation;
            if (group == null) group = model.AddComponent<LODGroup>();
            group.SetLODs(new LOD[] { new LOD(.0001f, rendered.ToArray()) });
            group.RecalculateBounds();
            UnitView view = model.GetComponent<UnitView>();
            if (view == null) throw new InvalidDataException("Humanoid native wrapper has no UnitView");
            // Native renderer cache receives only new geometry. Native property
            // blocks continue to carry FOW/team/visibility values to the same list.
            NativeRead.Set(view, "mGeoRenderObjects", rendered.ToArray());
            model.SetActive(true);
            Runtime.Log("HUMANOID_MODEL_PREPARED actor="+data.Actor+"; folder="+relativeAssetFolder+"; members="+members+
                "; nodesPerMember="+data.Nodes.Length+"; animatedNodesPerMember="+data.ActiveNodes.Length+"; skinnedPartsPerMember="+data.Parts.Length+"; nativeGeometryBlanked="+geometry.Count+
                "; inheritedVehicleFxRemoved="+removed+"; sourceCoordinatesRetained=True; nativeHardpointsPreserved=True; actualPixelAcceptancePending=True");
            return model;
        }

        static bool ProtectedName(Transform t)
        {
            for (; t != null; t = t.parent)
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("sensor") || n.Contains("fow") || n.Contains("underlay") || n.Contains("select") || n.Contains("hud") || n.Contains("blip")) return true;
            }
            return false;
        }
        static int StripVehicleEffects(GameObject model)
        {
            int count = 0;
            Type[] lights = { typeof(Light), typeof(LensFlare), typeof(Projector) };
            foreach (Type type in lights) foreach (Component c in model.GetComponentsInChildren(type, true)) { UnityEngine.Object.DestroyImmediate(c); count++; }
            Type[] particles = { typeof(ParticleSystem), typeof(ParticleSystemRenderer), typeof(ParticleEmitter), typeof(ParticleRenderer), typeof(TrailRenderer) };
            foreach (Type type in particles) foreach (Component c in model.GetComponentsInChildren(type, true))
            {
                bool weapon = false;
                for (Transform t = c.transform; t != null && t != model.transform; t=t.parent)
                { string n = t.name.ToLowerInvariant(); if (n.Contains("muzzle") || n.Contains("weapon") || n.Contains("gun") || n.Contains("projectile") || n.Contains("impact")) weapon = true; }
                if (!weapon) { UnityEngine.Object.DestroyImmediate(c); count++; }
            }
            return count;
        }
        static Transform NewTransform(string name, Transform parent, int layer)
        {
            GameObject g = new GameObject(name); g.layer=layer; g.transform.parent=parent;
            g.transform.localPosition=Vector3.zero; g.transform.localRotation=Quaternion.identity; g.transform.localScale=Vector3.one; return g.transform;
        }
        static Vector3 FormationOffset(int index, int count)
        {
            if (count == 1) return Vector3.zero;
            int columns = count <= 3 ? count : 3, rows=(count+columns-1)/columns;
            return new Vector3((index%columns-(columns-1)*.5f)*1.1f, 0f, ((rows-1)*.5f-index/columns)*1.2f);
        }

        public static void DiscoverActiveUnits(){UnitBindingRouter.BindHumanoid(BindSpawned);}
        static bool TryBindSpawned(BBI.Core.ComponentModel.Entity entity,UnitView view)
        {
            if(view==null||!entity.Equals(view.EntityID))return true;
            if(!view.gameObject.activeInHierarchy||view.SimStateInterpolator==null)return false;
            HumanoidBinding binding=view.GetComponent<HumanoidBinding>();
            if(binding==null||binding.PresentationRoot==null)return false;
            Ensure(view);return true;
        }
        public static void Ensure(UnitView view)
        {
            if (view == null || !view.gameObject.activeInHierarchy || view.SimStateInterpolator == null) return;
            HumanoidBinding binding = view.GetComponent<HumanoidBinding>();
            if (binding == null || binding.PresentationRoot == null) return;
            HumanoidDriver driver = view.GetComponent<HumanoidDriver>(); if(driver!=null&&driver.PresentationFailed)return; if (driver==null) driver=view.gameObject.AddComponent<HumanoidDriver>();
            try{driver.Initialize(view,binding,GetData(binding.AssetFolder));}
            catch(Exception error){driver.QuarantineBinding(view,"humanoid_bind",error);}
        }
        public static void Reset()
        {
            VerifiedFolders.Clear();HumanoidAnimationAudit.Reset();
            // Definitions retain template references across matches. Shared meshes,
            // textures and their owning prototype must survive this session reset.
            foreach (HumanoidDriver driver in Resources.FindObjectsOfTypeAll(typeof(HumanoidDriver))) if(driver != null) driver.ResetPlayback();
        }
        // Call only after the entire old native type registry has been discarded
        // and all active humanoid instances have been destroyed.
        public static void ReleaseTemplates()
        {
            Reset(); if (Templates != null) UnityEngine.Object.DestroyImmediate(Templates); Templates=null;
            foreach(UnityEngine.Object item in Owned) if(item!=null) UnityEngine.Object.DestroyImmediate(item);
            Owned.Clear(); Cache.Clear();
        }
        internal static void Verified(HumanoidBinding binding) { VerifiedFolders.Add(binding.AssetFolder); }
        static HumanoidData GetData(string folder)
        {
            HumanoidData data; if(!Cache.TryGetValue(SafePath(AssetDirectory,folder),out data)) throw new InvalidOperationException("Humanoid immutable package cache absent: "+folder); return data;
        }
        internal static void ApplyPose(HumanoidBinding binding, HumanoidData data, int member, HumanoidClip clip, float time, HumanoidPose[] from, float blend)
        {
            int n=data.Nodes.Length; float frame=Mathf.Clamp(time*clip.Rate,0,clip.Samples-1);
            int a=(int)Math.Floor(frame),b=Math.Min(a+1,clip.Samples-1); float u=frame-a;
            foreach(int i in data.ActiveNodes)
            {
                HumanoidNode node=data.Nodes[i]; HumanoidPose pose=new HumanoidPose(node.Position,node.Rotation,node.Scale);
                int track=clip.NodeTracks[i];
                if(track>=0)
                {
                    int ia=(a*clip.Tracks+track)*10, ib=(b*clip.Tracks+track)*10;
                    int bit=0x80>>(track&7), block=track>>3;
                    if((clip.Masks[block]&bit)!=0)pose.Position=Lerp3(clip.Qvv,ia+4,ib+4,u);
                    if((clip.Masks[clip.Stride+block]&bit)!=0)pose.Rotation=ShortestSlerp(ReadQ(clip.Qvv,ia),ReadQ(clip.Qvv,ib),u);
                    if((clip.Masks[clip.Stride*2+block]&bit)!=0)pose.Scale=Lerp3(clip.Qvv,ia+7,ib+7,u);
                }
                if(from!=null && blend<1f)
                { pose.Position=Vector3.Lerp(from[i].Position,pose.Position,blend); pose.Rotation=ShortestSlerp(from[i].Rotation,pose.Rotation,blend); pose.Scale=Vector3.Lerp(from[i].Scale,pose.Scale,blend); }
                Transform bone=binding.Bones[member*n+i]; bone.localPosition=pose.Position; bone.localRotation=pose.Rotation; bone.localScale=pose.Scale;
            }
        }
        static Vector3 Lerp3(float[] values,int a,int b,float u) { return new Vector3(values[a]+(values[b]-values[a])*u,values[a+1]+(values[b+1]-values[a+1])*u,values[a+2]+(values[b+2]-values[a+2])*u); }
        static Quaternion ReadQ(float[] values,int i) { return new Quaternion(values[i],values[i+1],values[i+2],values[i+3]); }
        public static Quaternion ShortestSlerp(Quaternion a,Quaternion b,float u)
        {
            a=Normalize(a); b=Normalize(b); float dot=a.x*b.x+a.y*b.y+a.z*b.z+a.w*b.w;
            if(dot<0f){b=new Quaternion(-b.x,-b.y,-b.z,-b.w);dot=-dot;}
            float ka=1f-u,kb=u;
            if(dot<.9995f){float theta=(float)Math.Acos(Math.Min(1f,dot)),sin=(float)Math.Sin(theta);ka=(float)Math.Sin((1f-u)*theta)/sin;kb=(float)Math.Sin(u*theta)/sin;}
            return Normalize(new Quaternion(a.x*ka+b.x*kb,a.y*ka+b.y*kb,a.z*ka+b.z*kb,a.w*ka+b.w*kb));
        }
        static Quaternion Normalize(Quaternion q)
        { float l=(float)Math.Sqrt(q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w); if(l<1e-8f)throw new InvalidDataException("Zero source quaternion"); return new Quaternion(q.x/l,q.y/l,q.z/l,q.w/l); }

        static HumanoidData Read(string folder,Material template)
        {return Read(folder,template,true);}
        // Shares the production decoder and policy validation without creating
        // Unity mesh/material/texture objects. Offline reader checks are not a
        // substitute for an in-game material or animation acceptance test.
        internal static HumanoidData ReadMetadata(string folder)
        {return Read(folder,null,false);}
        static HumanoidData Read(string folder,Material template,bool createUnityObjects)
        {
            HumanoidData d=new HumanoidData();
            using(BinaryReader r=new BinaryReader(File.OpenRead(Path.Combine(folder,"humanoid.bin")),Encoding.UTF8))
            {
                if(Encoding.ASCII.GetString(r.ReadBytes(4))!="HMN1")throw new InvalidDataException("Humanoid binary magic");
                int version=r.ReadInt32();if(version!=1&&version!=2)throw new InvalidDataException("Humanoid binary version");
                d.Actor=Text(r); d.Scale=Number(r); d.Heading=Number(r); if(d.Scale<=0f||d.Scale>10f)throw new InvalidDataException("Humanoid scale");
                int nodes=Count(r,4096),palette=Count(r,8192),parts=Count(r,256),clips=Count(r,16);
                if(nodes==0||palette==0||parts==0||clips<2)throw new InvalidDataException("Humanoid empty package");
                d.Nodes=new HumanoidNode[nodes]; d.PaletteNodes=new int[palette]; Matrix4x4[] binds=new Matrix4x4[palette]; d.Parts=new HumanoidPart[parts];
                for(int i=0;i<nodes;i++)
                {
                    HumanoidNode n=new HumanoidNode();n.Name=Text(r);n.Parent=r.ReadInt32();if(n.Parent< -1||n.Parent>=i)throw new InvalidDataException("Humanoid hierarchy");
                    n.Rotation=Normalize(new Quaternion(Number(r),Number(r),Number(r),Number(r)));n.Position=Vector(r);n.Scale=Vector(r);d.Nodes[i]=n;
                }
                for(int i=0;i<palette;i++)
                {
                    d.PaletteNodes[i]=r.ReadInt32();if(d.PaletteNodes[i]<0||d.PaletteNodes[i]>=nodes)throw new InvalidDataException("Humanoid palette");
                    Matrix4x4 matrix=new Matrix4x4();for(int c=0;c<4;c++)for(int row=0;row<4;row++)matrix[row,c]=Number(r);binds[i]=matrix;
                }
                Dictionary<string,Texture2D> textures=new Dictionary<string,Texture2D>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string,HumanoidMaterialPolicy> materialPolicies=HumanoidMaterialSettings.Load(folder);
                for(int i=0;i<parts;i++)
                {
                    HumanoidPart p=new HumanoidPart();p.Name=Text(r);string materialName=Text(r),texture=Text(r);
                    Color tint=version>=2?new Color(Number(r),Number(r),Number(r),Number(r)):(texture.Length>0?Color.white:new Color(.58f,.61f,.60f,1));
                    HumanoidMaterialPolicy policy;materialPolicies.TryGetValue(p.Name,out policy);
                    p.PaletteStart=r.ReadInt32();p.PaletteCount=Count(r,255);int vertices=Count(r,65535),indices=Count(r,3000000);
                    if(vertices<3||indices<3||indices%3!=0||p.PaletteStart<0||p.PaletteCount==0||p.PaletteStart+p.PaletteCount>palette)throw new InvalidDataException("Humanoid part range");
                    p.Bounds=new Bounds(Vector(r),Vector(r));if(p.Bounds.size.x<=0||p.Bounds.size.y<=0||p.Bounds.size.z<=0)throw new InvalidDataException("Humanoid bounds");
                    Vector3[] pos=new Vector3[vertices],normal=new Vector3[vertices];Vector2[] uv=new Vector2[vertices];BoneWeight[] weights=new BoneWeight[vertices];
                    for(int v=0;v<vertices;v++)pos[v]=Vector(r);for(int v=0;v<vertices;v++)normal[v]=Vector(r);
                    for(int v=0;v<vertices;v++)uv[v]=new Vector2(Number(r),Number(r));
                    for(int v=0;v<vertices;v++){BoneWeight w=new BoneWeight();w.weight0=Number(r);w.weight1=Number(r);w.weight2=Number(r);w.weight3=Number(r);if(w.weight0<0||w.weight1<0||w.weight2<0||w.weight3<0||Math.Abs(w.weight0+w.weight1+w.weight2+w.weight3-1f)>.001f)throw new InvalidDataException("Humanoid weights");weights[v]=w;}
                    for(int v=0;v<vertices;v++){BoneWeight w=weights[v];w.boneIndex0=Index(r,p.PaletteCount);w.boneIndex1=Index(r,p.PaletteCount);w.boneIndex2=Index(r,p.PaletteCount);w.boneIndex3=Index(r,p.PaletteCount);weights[v]=w;}
                    int[] triangles=new int[indices];for(int t=0;t<indices;t++)triangles[t]=Index(r,vertices);
                    Matrix4x4[] localBinds=new Matrix4x4[p.PaletteCount];Array.Copy(binds,p.PaletteStart,localBinds,0,p.PaletteCount);
                    if(createUnityObjects)BuildPartGraphics(p,pos,normal,uv,weights,localBinds,triangles,template,materialName,texture,tint,policy,textures,folder);
                    d.Parts[i]=p;
                }
                List<HumanoidClip> allClips=new List<HumanoidClip>();
                HashSet<string> roles=new HashSet<string>(StringComparer.Ordinal);
                for(int i=0;i<clips;i++)
                {
                    string role=Text(r);if(!roles.Add(role))throw new InvalidDataException("Duplicate humanoid clip role: "+role);HumanoidClip c=new HumanoidClip();c.Role=role;c.Name=Text(r);c.Duration=Number(r);c.Rate=Number(r);c.Samples=Count(r,100000);c.Tracks=Count(r,4096);c.Stride=Count(r,512);
                    if(c.Duration<=0||c.Rate<=0||c.Samples<2||c.Tracks==0||c.Stride<(c.Tracks+7)/8)throw new InvalidDataException("Humanoid clip dimensions");
                    c.NodeTracks=new int[nodes];for(int j=0;j<nodes;j++)c.NodeTracks[j]=-1;
                    for(int tr=0;tr<c.Tracks;tr++){int ni=r.ReadInt32();if(ni< -1||ni>=nodes)throw new InvalidDataException("Humanoid track mapping");if(ni>=0){if(c.NodeTracks[ni]>=0)throw new InvalidDataException("Duplicate humanoid track");c.NodeTracks[ni]=tr;}}
                    c.Masks=r.ReadBytes(c.Stride*3);if(c.Masks.Length!=c.Stride*3)throw new EndOfStreamException();
                    long values=(long)c.Samples*c.Tracks*10;if(values>50000000)throw new InvalidDataException("Humanoid clip too large");c.Qvv=new float[(int)values];for(int j=0;j<c.Qvv.Length;j++)c.Qvv[j]=Number(r);
                    if(role=="idle")d.Idle=c;else if(role=="walk")d.Walk=c;else if(role=="run")d.Run=c;else if(role=="attack")d.Attack=c;else if(role=="death")d.Death=c;else if(role=="skill")d.Skill=c;else if(role=="skill_start")d.SkillStart=c;else if(role=="skill_end")d.SkillEnd=c;else if(role=="sprint")d.Sprint=c;else if(!role.StartsWith("attack_",StringComparison.Ordinal))throw new InvalidDataException("Unsupported humanoid clip role: "+role);
                    allClips.Add(c);
                }
                if(d.Idle==null||d.Walk==null||r.BaseStream.Position!=r.BaseStream.Length)throw new InvalidDataException("Humanoid roles or unexpected tail");
                d.Clips=allClips.ToArray();
                List<HumanoidClip> attacks=new List<HumanoidClip>();if(d.Attack!=null)attacks.Add(d.Attack);
                foreach(HumanoidClip clip in allClips)if(clip.Role.StartsWith("attack_",StringComparison.Ordinal))attacks.Add(clip);
                d.AttackVariants=attacks.ToArray();
                d.Actions=HumanoidActionSettings.Load(folder,d);
                d.SoloCombo=HumanoidComboSettings.Load(folder,d);
                List<int> active=new List<int>();for(int i=0;i<nodes;i++)foreach(HumanoidClip clip in d.Clips)if(AffectsDefault(clip,d.Nodes[i],i)){active.Add(i);break;}d.ActiveNodes=active.ToArray();
            }
            return d;
        }
        static void BuildPartGraphics(HumanoidPart p,Vector3[] pos,Vector3[] normal,Vector2[] uv,BoneWeight[] weights,Matrix4x4[] localBinds,int[] triangles,Material template,string materialName,string texture,Color tint,HumanoidMaterialPolicy policy,Dictionary<string,Texture2D> textures,string folder)
        {
            int vertices=pos.Length;
            Mesh mesh=new Mesh();mesh.name="EF_Humanoid_"+p.Name;mesh.hideFlags=HideFlags.HideAndDontSave;mesh.vertices=pos;mesh.normals=normal;mesh.uv=uv;mesh.boneWeights=weights;mesh.bindposes=localBinds;mesh.triangles=triangles;
            Color[] colors=new Color[vertices];for(int v=0;v<vertices;v++)colors[v]=Color.white;mesh.colors=colors;mesh.RecalculateBounds();Owned.Add(mesh);p.Mesh=mesh;
            Material mat=new Material(template);mat.name="EF_Humanoid_"+materialName;mat.hideFlags=HideFlags.HideAndDontSave;
            foreach(string colorName in new string[]{"_Color","_BaseColor","_TrimColor"})
                if(mat.HasProperty(colorName))mat.SetColor(colorName,Color.white);
            // Apply one tint factor only: some native shaders multiply
            // _Color and _BaseColor, which would otherwise square it.
            if(mat.HasProperty("_Color"))mat.SetColor("_Color",tint);else if(mat.HasProperty("_BaseColor"))mat.SetColor("_BaseColor",tint);
            if(texture.Length>0)
            {
                bool linear=policy!=null&&policy.Mode=="multiply";string textureKey=texture+(linear?"|linear":"|srgb");
                Texture2D tex;if(!textures.TryGetValue(textureKey,out tex))
                {tex=new Texture2D(2,2,TextureFormat.RGBA32,true,linear);if(!tex.LoadImage(File.ReadAllBytes(SafePath(folder,texture))))throw new InvalidDataException("Humanoid texture decode "+texture);tex.name="EF_Humanoid_"+texture;tex.wrapMode=TextureWrapMode.Repeat;tex.filterMode=FilterMode.Trilinear;tex.hideFlags=HideFlags.HideAndDontSave;textures.Add(textureKey,tex);Owned.Add(tex);}
                mat.mainTexture=tex; if(mat.HasProperty("_MainTex"))mat.SetTexture("_MainTex",tex);if(mat.HasProperty("_BaseMap"))mat.SetTexture("_BaseMap",tex);
                mat.mainTextureScale=Vector2.one;mat.mainTextureOffset=Vector2.zero;
            }
            else
            {
                Texture2D neutral=new Texture2D(1,1,TextureFormat.RGBA32,false);neutral.SetPixel(0,0,Color.white);neutral.Apply();neutral.hideFlags=HideFlags.HideAndDontSave;Owned.Add(neutral);mat.mainTexture=neutral;
            }
            // Native normal/occlusion/metal masks belong to the inherited
            // vehicle. Do not retain those, or misread source normals as
            // Unity 5's packed normal convention. Base color is explicit.
            if(mat.HasProperty("_BumpMap"))mat.SetTexture("_BumpMap",null);if(mat.HasProperty("_OcclusionMap"))mat.SetTexture("_OcclusionMap",null);
            if(mat.HasProperty("_MetallicGlossMap"))mat.SetTexture("_MetallicGlossMap",null);if(mat.HasProperty("_EmissionMap"))mat.SetTexture("_EmissionMap",null);
            mat.DisableKeyword("_NORMALMAP");mat.DisableKeyword("_METALLICGLOSSMAP");mat.DisableKeyword("_EMISSION");
            if(mat.HasProperty("_OverlayMask")){Texture2D mask=new Texture2D(1,1,TextureFormat.RGBA32,false);mask.SetPixel(0,0,Color.clear);mask.Apply();mask.hideFlags=HideFlags.HideAndDontSave;Owned.Add(mask);mat.SetTexture("_OverlayMask",mask);}
            if(mat.HasProperty("_Metallic"))mat.SetFloat("_Metallic",0f);if(mat.HasProperty("_Glossiness"))mat.SetFloat("_Glossiness",.15f);
            HumanoidMaterialSettings.Apply(mat,policy,tint);
            Owned.Add(mat);p.Material=mat;
        }
        static bool AffectsDefault(HumanoidClip c,HumanoidNode n,int node)
        {
            int track=c.NodeTracks[node];if(track<0)return false;int bit=0x80>>(track&7),block=track>>3;
            for(int sample=0;sample<c.Samples;sample++)
            {
                int index=(sample*c.Tracks+track)*10;float[] v=c.Qvv;
                if((c.Masks[block]&bit)!=0&&(Math.Abs(v[index+4]-n.Position.x)>1e-8f||Math.Abs(v[index+5]-n.Position.y)>1e-8f||Math.Abs(v[index+6]-n.Position.z)>1e-8f))return true;
                if((c.Masks[c.Stride*2+block]&bit)!=0&&(Math.Abs(v[index+7]-n.Scale.x)>1e-8f||Math.Abs(v[index+8]-n.Scale.y)>1e-8f||Math.Abs(v[index+9]-n.Scale.z)>1e-8f))return true;
                if((c.Masks[c.Stride+block]&bit)!=0)
                {
                    double x=v[index],y=v[index+1],z=v[index+2],w=v[index+3],length=Math.Sqrt(x*x+y*y+z*z+w*w);if(length<1e-8)throw new InvalidDataException("Zero clip quaternion");x/=length;y/=length;z/=length;w/=length;
                    double dx=x-n.Rotation.x,dy=y-n.Rotation.y,dz=z-n.Rotation.z,dw=w-n.Rotation.w,minus=dx*dx+dy*dy+dz*dz+dw*dw;
                    dx=x+n.Rotation.x;dy=y+n.Rotation.y;dz=z+n.Rotation.z;dw=w+n.Rotation.w;
                    if(Math.Min(minus,dx*dx+dy*dy+dz*dz+dw*dw)>1e-14)return true;
                }
            }
            return false;
        }
        internal static string SafePath(string root,string relative)
        {
            if(string.IsNullOrEmpty(relative)||Path.IsPathRooted(relative))throw new InvalidDataException("Humanoid relative path");
            string full=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),path=Path.GetFullPath(Path.Combine(full,relative));
            if(!path.StartsWith(full+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Humanoid path escapes asset root");return path;
        }
        static int Count(BinaryReader r,int max){int n=r.ReadInt32();if(n<0||n>max)throw new InvalidDataException("Humanoid count");return n;}
        static int Index(BinaryReader r,int count){int i=r.ReadInt32();if(i<0||i>=count)throw new InvalidDataException("Humanoid index");return i;}
        static float Number(BinaryReader r){float v=r.ReadSingle();if(float.IsNaN(v)||float.IsInfinity(v))throw new InvalidDataException("Humanoid nonfinite number");return v;}
        static Vector3 Vector(BinaryReader r){return new Vector3(Number(r),Number(r),Number(r));}
        static string Text(BinaryReader r){int n=Count(r,4096);byte[] b=r.ReadBytes(n);if(b.Length!=n)throw new EndOfStreamException();return Encoding.UTF8.GetString(b);}
    }

    public sealed class HumanoidBinding : MonoBehaviour
    {
        public string AssetFolder;
        public int MemberCount,NodeCount;
        public Transform PresentationRoot;
        public Transform[] Bones,MemberRoots,MemberEmission;
        public Transform NativeEmission;
        public SkinnedMeshRenderer[] Renderers;
        public bool[] PartVisibility,AliveMembers;
        public string[] MemberRaces;
    }
    internal sealed class HumanoidData
    { public string Actor;public float Scale,Heading;public HumanoidNode[] Nodes;public int[] PaletteNodes,ActiveNodes;public HumanoidPart[] Parts;public HumanoidClip Idle,Walk,Run,Sprint,Attack,Death,Skill,SkillStart,SkillEnd;public HumanoidClip[] Clips,AttackVariants;public HumanoidAnimationProfile Actions;public HumanoidComboSettings SoloCombo; }
    internal sealed class HumanoidNode
    { public string Name;public int Parent;public Vector3 Position,Scale;public Quaternion Rotation; }
    internal sealed class HumanoidPart
    { public string Name;public int PaletteStart,PaletteCount;public Mesh Mesh;public Material Material;public Bounds Bounds; }
    internal sealed class HumanoidClip
    { public string Name,Role;public float Duration,Rate;public int Samples,Tracks,Stride;public int[] NodeTracks;public byte[] Masks;public float[] Qvv; }
    internal struct HumanoidPose
    {
        public Vector3 Position,Scale;public Quaternion Rotation;
        public HumanoidPose(Vector3 p,Quaternion q,Vector3 s){Position=p;Rotation=q;Scale=s;}
    }

    // Pure managed pursuit core. World positions are owned by this array, never
    // read back after the parent moved. Delays and bounded acceleration/heading
    // let a squad stretch through turns and collect into slots after stopping.
    // Members remain visual instances of one native entity, with no own navmesh,
    // simulation collision, target, hitpoints or damage event.
    public sealed class HumanoidFormation
    {
        struct Leader { public float Time,Yaw;public Vector3 Position,Velocity; }
        readonly int Count;
        readonly List<Leader> History=new List<Leader>();
        public readonly Vector3[] Positions,Velocities;
        public readonly float[] Headings,ActualSpeeds;
        public bool Initialized { get; private set; }
        public int TeleportResets { get; private set; }
        Vector3 PreviousCenter;float Clock;
        public HumanoidFormation(int count)
        {
            if(count<1||count>12)throw new ArgumentOutOfRangeException("count");Count=count;
            Positions=new Vector3[count];Velocities=new Vector3[count];Headings=new float[count];ActualSpeeds=new float[count];
        }
        static Vector3 Slot(int i,int count)
        { if(count==1)return Vector3.zero;int columns=count<=3?count:3,rows=(count+columns-1)/columns;return new Vector3((i%columns-(columns-1)*.5f)*1.1f,0,((rows-1)*.5f-i/columns)*1.2f); }
        static Vector3 Rotate(Vector3 p,float yaw)
        {float c=(float)Math.Cos(yaw),s=(float)Math.Sin(yaw);return new Vector3(c*p.x+s*p.z,p.y,-s*p.x+c*p.z);}
        static float Delta(float a,float b)
        {float d=b-a;while(d>(float)Math.PI)d-=(float)(Math.PI*2);while(d<-(float)Math.PI)d+=(float)(Math.PI*2);return d;}
        static Vector3 Clamp(Vector3 v,float max)
        {float length=v.magnitude;return length>max&&length>.000001f?v*(max/length):v;}
        void Snap(Vector3 center,float yaw,Vector3 velocity)
        {
            Clock=0;History.Clear();History.Add(new Leader{Time=0,Position=center,Yaw=yaw,Velocity=velocity});PreviousCenter=center;
            for(int i=0;i<Count;i++){Positions[i]=center+Rotate(Slot(i,Count),yaw);Velocities[i]=Vector3.zero;Headings[i]=yaw;ActualSpeeds[i]=0;}
            Initialized=true;
        }
        Leader Delayed(float time)
        {
            if(time<=History[0].Time)return History[0];
            for(int i=1;i<History.Count;i++)if(History[i].Time>=time)
            {
                Leader a=History[i-1],b=History[i];float u=(time-a.Time)/Math.Max(.000001f,b.Time-a.Time);
                return new Leader{Time=time,Position=Vector3.Lerp(a.Position,b.Position,u),Velocity=Vector3.Lerp(a.Velocity,b.Velocity,u),Yaw=a.Yaw+Delta(a.Yaw,b.Yaw)*u};
            }
            return History[History.Count-1];
        }
        public void Step(Vector3 center,Vector3 forward,Vector3 nativeVelocity,float dt)
        {
            float yaw=(float)Math.Atan2(forward.x,forward.z);nativeVelocity.y=0;
            if(!Initialized){Snap(center,yaw,nativeVelocity);return;}
            if(dt<=0)return;
            if(dt>1f||(center-PreviousCenter).magnitude>Math.Max(12f,nativeVelocity.magnitude*dt*4f+3f))
            {TeleportResets++;Snap(center,yaw,nativeVelocity);return;}
            PreviousCenter=center;Clock+=dt;History.Add(new Leader{Time=Clock,Position=center,Velocity=nativeVelocity,Yaw=yaw});
            while(History.Count>2&&History[1].Time<Clock-.75f)History.RemoveAt(0);
            if(Count==1)
            {
                // Sim position may be held between 8 Hz native samples. Its
                // per-render difference then alternates zero/spikes and would
                // repeatedly reset idle/walk blending. The velocity snapshot is
                // continuous for the native movement state, including reverse.
                ActualSpeeds[0]=nativeVelocity.magnitude;Positions[0]=center;Velocities[0]=nativeVelocity;
                if(ActualSpeeds[0]>.04f){float angle=(float)Math.Atan2(nativeVelocity.x,nativeVelocity.z),turn=Delta(Headings[0],angle),limit=(float)Math.PI*dt;Headings[0]+=Math.Max(-limit,Math.Min(limit,turn));}
                return;
            }
            for(int i=0;i<Count;i++)
            {
                float delay=.035f+(i/3)*.13f+(i%3)*.022f;
                Leader target=Delayed(Clock-delay);Vector3 desired=target.Position+Rotate(Slot(i,Count),target.Yaw),error=desired-Positions[i];error.y=0;
                float maxSpeed=Math.Min(12f,Math.Max(2.2f,nativeVelocity.magnitude*1.45f+1f));
                Vector3 wanted=Clamp(target.Velocity+error*2.6f,maxSpeed),change=Clamp(wanted-Velocities[i],(8f+(i%3)*.4f)*dt);
                Velocities[i]+=change;Vector3 previous=Positions[i];Positions[i]+=Velocities[i]*dt;Positions[i].y=center.y;
                ActualSpeeds[i]=(Positions[i]-previous).magnitude/dt;
                float angle=ActualSpeeds[i]>.04f?(float)Math.Atan2(Velocities[i].x,Velocities[i].z):target.Yaw;
                float turn=Delta(Headings[i],angle),maxTurn=(float)Math.PI*(.92f+i*.03f)*dt;
                Headings[i]+=Math.Max(-maxTurn,Math.Min(maxTurn,turn));
            }
        }
    }

    public sealed class HumanoidDriver : IsolatedUnitPresentation
    {
        UnitView View;HumanoidBinding Binding;HumanoidData Data;
        HumanoidPose[][] From;float[] Clock,BlendAge,Heading;bool[] Moving;
        HumanoidClip[] Current;HumanoidAnimationCore[] Motion;
        HumanoidFormation Formation;BBI.Core.ComponentModel.Entity BoundEntity;
        bool Initialized,Verified,Bound,GroundReported;int ActionReports,EmissionMember;
        Vector3 LastAim;bool HaveAim;long LastNativeRound;
        HumanoidFireSession FireSession;
        HumanoidSoloAttackCombo SoloCombo;
        int ComboReports;
        HumanoidSquadCasualties Casualties;
        HumanoidAttackAudio AttackAudio;
        HumanoidUnitVoice UnitVoice;
        HumanoidPerlicaAttackFx PerlicaAttackFx;
        HumanoidRifleFireFx RifleFireFx;
        LaevatainAccessoryDamping AccessoryDamping;
        bool NewFireSession;
        bool LastSensed;
        const float BlendSeconds=.22f;
        public bool Paused;
        public string ActiveClip { get { return Current==null||Current.Length==0||Current[0]==null?"":Current[0].Name; } }
        internal HumanoidClip CurrentSourceClip { get { return Current==null||Current.Length==0?null:Current[0]; } }
        internal void Initialize(UnitView view,HumanoidBinding binding,HumanoidData data)
        {
            if(Initialized&&View==view&&Binding==binding&&BoundEntity.Equals(view.EntityID))return;
            ReleaseBinding();View=view;Binding=binding;Data=data;
            if(binding.NodeCount!=data.Nodes.Length||binding.Bones==null||binding.Bones.Length!=binding.MemberCount*data.Nodes.Length||binding.Renderers==null||binding.Renderers.Length!=binding.MemberCount*data.Parts.Length)throw new InvalidDataException("Humanoid cloned binding incomplete");
            HashSet<Transform> unique=new HashSet<Transform>();foreach(Transform bone in binding.Bones)if(bone==null||!unique.Add(bone))throw new InvalidDataException("Humanoid members share or miss bones");
            foreach(SkinnedMeshRenderer r in binding.Renderers)
                if(r==null||r.sharedMesh==null||r.sharedMesh.vertexCount==0||r.bones==null||r.bones.Length==0||r.sharedMesh.boneWeights.Length!=r.sharedMesh.vertexCount)throw new InvalidDataException("Humanoid skin renderer incomplete");
            int count=binding.MemberCount;
            From=new HumanoidPose[count][];Clock=new float[count];BlendAge=new float[count];Heading=new float[count];Moving=new bool[count];Current=new HumanoidClip[count];Motion=new HumanoidAnimationCore[count];Formation=new HumanoidFormation(count);
            for(int member=0;member<count;member++){From[member]=new HumanoidPose[data.Nodes.Length];BlendAge[member]=1f;Current[member]=data.Idle;Motion[member]=new HumanoidAnimationCore(data.Actions,member);}
            AccessoryDamping=LaevatainAccessoryDamping.Bind(binding,data);
            BoundEntity=view.EntityID;HumanoidWeaponEventBridge.Bind(view,0);Bound=true;Initialized=true;GroundReported=false;ActionReports=0;EmissionMember=0;HaveAim=false;LastNativeRound=0;
            uint seed=unchecked((uint)view.EntityID.GetHashCode()+0x9E3779B9u);
            FireSession=count>1&&data.AttackVariants.Length>0?new HumanoidFireSession(count,data.AttackVariants.Length,seed):null;
            SoloCombo=count==1&&data.SoloCombo!=null?new HumanoidSoloAttackCombo(data.AttackVariants.Length,data.SoloCombo.ResetGapSeconds):null;ComboReports=0;
            // Single elite actors need the same one-time death handoff as squads.
            Casualties=new HumanoidSquadCasualties(count,seed^0xA511E9B3u);
            binding.AliveMembers=new bool[count];for(int member=0;member<count;member++)binding.AliveMembers[member]=true;
            if(binding.PartVisibility==null||binding.PartVisibility.Length!=count*data.Parts.Length)throw new InvalidDataException("Humanoid race mask shape differs");
            NewFireSession=false;LastSensed=Convert.ToInt32(view.Visibility)==2;
            if(binding.AssetFolder=="humanoid_trial/laevatain")LaevatainFlameBridge.Bind(view);
            if(binding.AssetFolder=="humanoid_trial/gilberta")GilbertaGravityBridge.Bind(view);
            if(binding.AssetFolder=="ardelia_v01")ArdeliaHealingBridge.Bind(view);
            AttackAudio=view.GetComponent<HumanoidAttackAudio>();if(AttackAudio==null)AttackAudio=view.gameObject.AddComponent<HumanoidAttackAudio>();
            AttackAudio.Initialize(view,HumanoidModel.SafePath(HumanoidModel.AssetDirectory,binding.AssetFolder));
            if(count==1&&(binding.AssetFolder=="humanoid_trial/perlica"||binding.AssetFolder=="humanoid_trial/laevatain"||binding.AssetFolder=="humanoid_trial/gilberta"||binding.AssetFolder=="ardelia_v01"))
            {
                UnitVoice=view.GetComponent<HumanoidUnitVoice>();if(UnitVoice==null)UnitVoice=view.gameObject.AddComponent<HumanoidUnitVoice>();
                UnitVoice.Initialize(view,HumanoidModel.SafePath(HumanoidModel.AssetDirectory,binding.AssetFolder));
            }
            if(count==1&&binding.AssetFolder=="humanoid_trial/perlica")
            {
                PerlicaAttackFx=view.GetComponent<HumanoidPerlicaAttackFx>();if(PerlicaAttackFx==null)PerlicaAttackFx=view.gameObject.AddComponent<HumanoidPerlicaAttackFx>();
                PerlicaAttackFx.Initialize(view,HumanoidModel.SafePath(HumanoidModel.AssetDirectory,binding.AssetFolder));
            }
            if(count>1&&binding.AssetFolder=="humanoid_trial/guard")
            {
                UnitVoice=view.GetComponent<HumanoidUnitVoice>();if(UnitVoice==null)UnitVoice=view.gameObject.AddComponent<HumanoidUnitVoice>();
                UnitVoice.Initialize(view,HumanoidModel.SafePath(HumanoidModel.AssetDirectory,binding.AssetFolder));
                RifleFireFx=view.GetComponent<HumanoidRifleFireFx>();if(RifleFireFx==null)RifleFireFx=view.gameObject.AddComponent<HumanoidRifleFireFx>();
                RifleFireFx.Initialize(view,HumanoidModel.SafePath(HumanoidModel.AssetDirectory,binding.AssetFolder+"/rifle_vfx"));
            }
            if(count>1)Runtime.Log("HUMANOID_SQUAD_RACE_LAYOUT entity="+view.EntityID+"; races="+string.Join(",",binding.MemberRaces)+"; partsPerMember="+data.Parts.Length+"; wholeSquadHP="+view.UnitAttrs.MaxHealth+"; nativeEntityCount=1");
        }
        public void ResetPlayback(){ReleaseBinding();Initialized=false;Verified=false;View=null;Binding=null;Data=null;}
        void ReleaseBinding(){if(AccessoryDamping!=null){AccessoryDamping.Release();AccessoryDamping=null;}if(PerlicaAttackFx!=null)PerlicaAttackFx.ResetAll();if(Bound){HumanoidWeaponEventBridge.Unbind(BoundEntity);if(Binding!=null&&Binding.AssetFolder=="humanoid_trial/laevatain")LaevatainFlameBridge.Unbind(BoundEntity);if(Binding!=null&&Binding.AssetFolder=="humanoid_trial/gilberta")GilbertaGravityBridge.Unbind(BoundEntity);if(Binding!=null&&Binding.AssetFolder=="ardelia_v01")ArdeliaHealingBridge.Unbind(BoundEntity);Bound=false;}}
        void OnDisable(){if(Initialized&&Casualties!=null&&View!=null&&View.NonInterpolatedTickState!=null&&View.NonInterpolatedTickState.UnitIsDead)UpdateCasualties(false);ReleaseBinding();Initialized=false;}
        void OnDestroy(){ReleaseBinding();}
        void LateUpdate()
        {
            if(PresentationFailed||!enabled)return;
            try{ApplyPresentationFrame();}
            catch(Exception error){DisablePresentation(View,"humanoid_rig",error);}
        }
        void ApplyPresentationFrame()
        {
            if(!Initialized||View==null||Binding==null||Binding.PresentationRoot==null)return;
            if(View.NonInterpolatedTickState!=null&&!View.NonInterpolatedTickState.UnitIsDead&&View.NonInterpolatedTickState.Hitpoints>0)LastSensed=Convert.ToInt32(View.Visibility)==2;
            UnitSimStateInterpolator sim=View.SimStateInterpolator;if(sim==null)return;
            Vector3 center=sim.CurrentSimPosition,forward=sim.CurrentSimForward,velocity=sim.CurrentSimVelocity;forward.y=0;
            if(forward.sqrMagnitude<.000001f)forward=View.ForwardHorizontal;if(forward.sqrMagnitude<.000001f)forward=Vector3.forward;forward.Normalize();
            RaycastHit groundHit=View.MostRecentGroundHit;Collider groundCollider=groundHit.collider;
            HumanoidGroundSurface surface=HumanoidGroundPose.Capture(GroundVector(center),GroundVector(View.Position),View.SuspensionSettleYOffset,
                groundCollider!=null&&groundCollider.gameObject.layer==BBI.Unity.Core.Rendering.RenderLayer.TerrainLayer,
                GroundVector(groundHit.point),GroundVector(groundHit.normal));
            Vector3 groundedCenter=center;groundedCenter.y=GroundHeight(surface,center);
            Transform root=Binding.PresentationRoot;root.position=groundedCenter;root.rotation=Quaternion.LookRotation(forward,Vector3.up);
            Vector3 parentScale=root.parent.lossyScale;root.localScale=new Vector3(Data.Scale/Nonzero(parentScale.x),Data.Scale/Nonzero(parentScale.y),Data.Scale/Nonzero(parentScale.z));
            float dt=Paused?0f:Mathf.Max(0f,Runtime.PresentationDeltaTime);
            if(AccessoryDamping!=null)AccessoryDamping.RemoveAppliedPose();
            UpdateCasualties(dt==0f);
            bool first=!Formation.Initialized;Formation.Step(center,forward,velocity,dt);
            if(first)for(int i=0;i<Heading.Length;i++)Heading[i]=Formation.Headings[i]*Mathf.Rad2Deg;
            // This explicit test key plays source motion only. It never invents
            // native round events, commands, projectiles, hit effects or damage.
            bool preview=View.Selected&&Input.GetKeyDown(KeyCode.P)&&(Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl))&&(Input.GetKey(KeyCode.LeftAlt)||Input.GetKey(KeyCode.RightAlt));
            if(preview&&Data.Attack!=null&&velocity.sqrMagnitude<.0144f)
            {
                for(int member=0;member<Motion.Length;member++)if(Casualties==null||Casualties.IsAlive(member))Motion[member].RequestAttackPreview();
                Runtime.Log("HUMANOID_ATTACK_PREVIEW_REQUESTED entity="+View.EntityID+"; sourceClip="+Data.Attack.Name+"; selectedByUser=True; nativeRounds=0; damageApplied=False");
            }
            if(dt>0f&&View.Selected&&(Binding.MemberCount>1||Data.Death!=null)&&Input.GetKeyDown(KeyCode.K)&&(Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl))&&(Input.GetKey(KeyCode.LeftAlt)||Input.GetKey(KeyCode.RightAlt)))HumanoidCasualtyProbe.Request(View.EntityID);
            if(AttackAudio!=null)AttackAudio.Tick(dt);
            if(PerlicaAttackFx!=null)PerlicaAttackFx.Tick(dt);
            if(RifleFireFx!=null)RifleFireFx.Tick(dt);
            if(SoloCombo!=null){SoloCombo.Step(dt,new Vector3(velocity.x,0f,velocity.z).magnitude);if(Casualties.AliveCount==0)SoloCombo.End();}
            if(UnitVoice!=null)
            {
                UnitVoice.Tick(dt,View.Selected,new Vector3(velocity.x,0f,velocity.z).magnitude);
                if(dt>0f)
                {
                    long accepted;bool hasVoice=Binding.AssetFolder=="humanoid_trial/laevatain"?
                        LaevatainFlameBridge.TryTakeVoiceAcceptance(BoundEntity,out accepted):Binding.AssetFolder=="humanoid_trial/gilberta"?
                        GilbertaGravityBridge.TryTakeVoiceAcceptance(BoundEntity,out accepted):Binding.AssetFolder=="ardelia_v01"?ArdeliaHealingBridge.TryTakeVoiceAcceptance(BoundEntity,out accepted):PerlicaOrbitalBridge.TryTakeVoiceAcceptance(BoundEntity,out accepted);
                    if(hasVoice)UnitVoice.PlayAbility(accepted);
                }
            }
            NewFireSession=false;if(dt>0f)ReadNativeEvents();
            if(FireSession!=null){FireSession.Step(dt,new Vector3(velocity.x,0,velocity.z).magnitude);if(Casualties!=null&&Casualties.AliveCount==0)FireSession.End();}
            Vector3 supportAim=Vector3.zero; float supportAge=0f;
            bool supportPose=Binding.MemberCount==1&&Data.Attack!=null&&velocity.sqrMagnitude<.0144f&&
                PerlicaOrbitalBridge.TryGetChannel(View.EntityID,out supportAim,out supportAge);
            bool flamePose=Binding.MemberCount==1&&Data.Skill!=null&&Binding.AssetFolder=="humanoid_trial/laevatain"&&velocity.sqrMagnitude<.0144f&&LaevatainFlameBridge.TryGetChannel(View.EntityID,out supportAim,out supportAge);
            bool gravityPose=Binding.MemberCount==1&&Data.Skill!=null&&Binding.AssetFolder=="humanoid_trial/gilberta"&&velocity.sqrMagnitude<.0144f&&GilbertaGravityBridge.TryGetChannel(View.EntityID,out supportAim,out supportAge);
            bool healingEnding=false;bool healingPose=Binding.MemberCount==1&&Data.Skill!=null&&Binding.AssetFolder=="ardelia_v01"&&velocity.sqrMagnitude<.0144f&&ArdeliaHealingBridge.TryGetChannel(View.EntityID,out supportAim,out supportAge,out healingEnding);
            HumanoidClip healingClip=Data.Skill;float healingClock=0f;
            if(healingPose){if(healingEnding&&Data.SkillEnd!=null){healingClip=Data.SkillEnd;healingClock=Math.Min(supportAge,healingClip.Duration);}else if(!healingEnding&&Data.SkillStart!=null&&supportAge<Data.SkillStart.Duration){healingClip=Data.SkillStart;healingClock=supportAge;}else healingClock=(supportAge-(Data.SkillStart==null?0f:Data.SkillStart.Duration))%Data.Skill.Duration;}
            if(flamePose||gravityPose||healingPose)supportPose=true;
            if(supportPose&&SoloCombo!=null)SoloCombo.End();
            for(int member=0;member<Binding.MemberCount;member++)
            {
                if(Casualties!=null&&!Casualties.IsAlive(member))continue;
                // Keep the delayed formation in simulation XZ space. Terrain
                // height must not inflate animation speed or trigger teleports.
                Vector3 memberPosition=Formation.Positions[member];memberPosition.y=GroundHeight(surface,memberPosition);
                Binding.MemberRoots[member].position=memberPosition;
                float desiredHeading=Formation.Headings[member]*Mathf.Rad2Deg;
                if(supportPose||((Motion[member].AttackActive||FireSession!=null&&FireSession.Active)&&HaveAim))
                {
                    Vector3 aim=(supportPose?supportAim:LastAim)-Formation.Positions[member];aim.y=0;
                    if(aim.sqrMagnitude>.0001f)desiredHeading=Mathf.Atan2(aim.x,aim.z)*Mathf.Rad2Deg;
                }
                Heading[member]=Mathf.MoveTowardsAngle(Heading[member],desiredHeading,360f*dt);
                Binding.MemberRoots[member].rotation=Quaternion.Euler(0,Heading[member]+Data.Heading,0);
                HumanoidAnimationCore motion=Motion[member];motion.Step(Formation.ActualSpeeds[member],dt);
                Moving[member]=motion.LocomotionRole!=HumanoidLocomotionRole.Idle;
                bool sustained=FireSession!=null&&FireSession.Active&&Formation.ActualSpeeds[member]<=Data.Actions.MoveExitSpeed;
                HumanoidClip attackClip=SoloCombo!=null&&SoloCombo.HasSelection?Data.AttackVariants[SoloCombo.CurrentVariant]:Data.Attack;
                HumanoidClip clip=healingPose?healingClip:(flamePose||gravityPose)?Data.Skill:supportPose?Data.Attack:sustained?Data.AttackVariants[FireSession.VariantFor(member)]:motion.AttackActive&&attackClip!=null?attackClip:motion.LocomotionRole==HumanoidLocomotionRole.Sprint&&Data.Sprint!=null?Data.Sprint:motion.LocomotionRole==HumanoidLocomotionRole.Run&&Data.Run!=null?Data.Run:Moving[member]?Data.Walk:Data.Idle;
                if(dt>0f&&(Current[member]!=clip||(sustained?NewFireSession:motion.AttackStarted)))
                {
                    for(int i=0;i<Data.Nodes.Length;i++){Transform bone=Binding.Bones[member*Data.Nodes.Length+i];From[member][i]=new HumanoidPose(bone.localPosition,bone.localRotation,bone.localScale);}
                    Current[member]=clip;BlendAge[member]=0;
                    if(member==0&&ActionReports<40){ActionReports++;Runtime.Log("HUMANOID_ACTION_TRANSITION entity="+View.EntityID+"; role="+clip.Role+"; clip="+clip.Name+"; speed="+Formation.ActualSpeeds[member]+"; playbackRate="+motion.LocomotionPlaybackRate+"; nativeDamageOwnedBySimulation=True; visualAcceptancePending=True");}
                }
                float poseBlendSeconds=SoloCombo!=null&&motion.AttackActive&&!supportPose?.08f:BlendSeconds;
                BlendAge[member]=Mathf.Min(poseBlendSeconds,BlendAge[member]+dt);float blend=BlendAge[member]/poseBlendSeconds;blend=blend*blend*(3f-2f*blend);
                Clock[member]=healingPose?healingClock:supportPose?Mathf.Min(supportAge,clip.Duration):sustained?FireSession.ClipClockFor(member,clip.Duration):motion.AttackActive&&attackClip!=null?motion.AttackClock:motion.LocomotionClock;
                HumanoidModel.ApplyPose(Binding,Data,member,clip,Clock[member],BlendAge[member]<poseBlendSeconds?From[member]:null,blend);
            }
            if(AccessoryDamping!=null)AccessoryDamping.Step(View,CurrentSourceClip,dt,velocity,Casualties==null||Casualties.IsAlive(0));
            if(Casualties!=null&&!Casualties.IsAlive(EmissionMember))EmissionMember=Casualties.MemberForNativeRound(LastNativeRound+1);
            if(EmissionMember>=0&&Binding.NativeEmission!=null&&Binding.MemberEmission!=null&&Binding.MemberEmission.Length>EmissionMember)
            {
                Transform emitter=Binding.MemberEmission[EmissionMember];
                Binding.NativeEmission.position=emitter.position;Binding.NativeEmission.rotation=emitter.rotation;
            }
            if(Binding.AliveMembers==null||Binding.AliveMembers[0])HumanoidAnimationAudit.Observe(this,View,Binding,Data,Clock,Moving,dt);
            bool visible=Convert.ToInt32(View.Visibility)==2&&!UserCamera.IsSensorsModeActive;
            for(int member=0;member<Binding.MemberCount;member++)
                if(Casualties==null||Casualties.IsAlive(member))for(int part=0;part<Data.Parts.Length;part++)
                {int index=member*Data.Parts.Length+part;Binding.Renderers[index].enabled=visible&&Binding.PartVisibility[index];}
            if(!Verified)
            {
                Verified=true;HumanoidModel.Verified(Binding);
                Runtime.Log("HUMANOID_NATIVE_VIEW_VERIFIED actor="+Data.Actor+"; entity="+View.EntityID+"; members="+Binding.MemberCount+"; independentBones="+Binding.Bones.Length+
                    "; skinnedRenderers="+Binding.Renderers.Length+"; nativeStateValid=True; idleClip="+Data.Idle.Name+"; walkClip="+Data.Walk.Name+
                    "; runClip="+(Data.Run==null?"not-supplied":Data.Run.Name)+"; attackClip="+(Data.Attack==null?"not-supplied":Data.Attack.Name)+
                    "; sourceMetres=True; delayedSquadFollow=True; ownMemberWorldPositions=True; independentMemberTravelAnimation=True; fowVisibilityGate=True; sensorsUseNativeIcon=True; simulationOwnsCommandsAndDamage=True; actualPixelAcceptancePending=True");
            }
            if(!GroundReported)
            {
                GroundReported=true;
                Runtime.Log("HUMANOID_GROUND_ALIGNED actor="+Data.Actor+"; entity="+View.EntityID+"; simY="+center.y+
                    "; nativeViewY="+View.Position.y+"; actorY="+groundedCenter.y+"; nativeGroundSample="+surface.NativeSample+
                    "; customTerrain="+CustomFlatMap.Ready+"; cachedNativeHit=True; extraRaycasts=0; formationXZUnchanged=True; visualAcceptancePending=True");
            }
        }
        static FactionPoseVector GroundVector(Vector3 value){return new FactionPoseVector(value.x,value.y,value.z);}
        static float GroundHeight(HumanoidGroundSurface surface,Vector3 position)
        {
            if(CustomFlatMap.Ready)return CustomFlatMap.SampleOriginalHeight(new BBI.Core.Utility.Vector2r(
                BBI.Core.Utility.FixedPoint.Fixed64.UnsafeFromFloat(position.x),BBI.Core.Utility.FixedPoint.Fixed64.UnsafeFromFloat(position.z)));
            return (float)HumanoidGroundPose.HeightAt(surface,position.x,position.z);
        }
        void ReadNativeEvents()
        {
            HumanoidWeaponVisualEvent message;
            while(HumanoidWeaponEventBridge.TryTake(BoundEntity,out message))
            {
                if(message.Kind==HumanoidWeaponVisualEventKind.IdleOrWinddown){if(SoloCombo!=null)SoloCombo.ObserveIdleOrWinddown(message.EventSequence);if(AttackAudio!=null)AttackAudio.CancelWindup();if(PerlicaAttackFx!=null)PerlicaAttackFx.CancelWindup();foreach(HumanoidAnimationCore motion in Motion)motion.ObserveNativeIdleOrWinddown();continue;}
                long sequence=message.Kind==HumanoidWeaponVisualEventKind.Round?message.RoundSequence:message.RoundSequence+1;
                int member=Casualties==null?HumanoidShotDispatch.MemberForNativeRound(sequence,Binding.MemberCount):Casualties.MemberForNativeRound(sequence);
                if(member<0)continue;
                if(SoloCombo!=null)
                {
                    int variant;int token=message.Target.Equals(BBI.Core.ComponentModel.Entity.None)?0:message.Target.GetHashCode();
                    bool accepted=message.Kind==HumanoidWeaponVisualEventKind.Windup?SoloCombo.ObserveWindup(message.EventSequence,token,out variant):SoloCombo.ObserveRound(message.EventSequence,token,out variant);
                    if(accepted)
                    {
                        HumanoidClip selected=Data.AttackVariants[variant];
                        Motion[member].SetAttackTiming(selected.Duration,Data.SoloCombo.Releases[variant],Data.SoloCombo.ReleaseKnown,Data.SoloCombo.NativeWindupSeconds);
                        if(ComboReports++<32)Runtime.Log("PERLICA_NATIVE_COMBO event="+message.Kind+"; sequence="+message.EventSequence+"; variant="+(variant+1)+"; clip="+selected.Name+"; releaseSeconds="+Data.SoloCombo.Releases[variant]+"; nativeWindupSeconds="+Data.SoloCombo.NativeWindupSeconds+"; sourceReleaseVerified="+Data.SoloCombo.ReleaseKnown+"; nativeShotsAdded=0; simulationDamageUnchanged=True");
                    }
                }
                float delay=HumanoidShotDispatch.MemberVisualDelay(member);
                if(UnitVoice!=null)UnitVoice.ObserveAttackSession(message.EventSequence,message.Target.Equals(BBI.Core.ComponentModel.Entity.None)?0:message.Target.GetHashCode());
                if(message.HasAim){LastAim=new Vector3(message.AimX,0,message.AimZ);HaveAim=true;}
                EmissionMember=member;
                if(FireSession!=null)
                {
                    int before=FireSession.Episode;
                    FireSession.ObserveNativeActivity(message.EventSequence,message.Target.Equals(BBI.Core.ComponentModel.Entity.None)?0:message.Target.GetHashCode());
                    if(before!=FireSession.Episode){NewFireSession=true;Runtime.Log("HUMANOID_FIRE_SESSION entity="+View.EntityID+"; episode="+FireSession.Episode+"; variants="+Data.AttackVariants.Length+"; stableUntilMoveTargetOrGap=True");}
                }
                if(message.Kind==HumanoidWeaponVisualEventKind.Round)
                {
                    LastNativeRound=message.RoundSequence;
                    Runtime.Log("HUMANOID_NATIVE_RANGE_ROUND entity="+View.EntityID+"; sequence="+LastNativeRound+"; visualMember="+member+"; testDamageByNativeSimulation=True; originalSpellRestored=False");
                    if(AttackAudio!=null)
                    {
                        AttackAudio.PlayRound(message.EventSequence,Binding.MemberEmission[member].position);
                        if(message.Hit&&message.HasAim)AttackAudio.PlayImpact(message.EventSequence,new Vector3(message.AimX,0,message.AimZ));
                    }
                    Vector3 visualAim;bool visibleAim=TryVisualAim(message,Binding.MemberEmission[member].position,out visualAim);
                    if(PerlicaAttackFx!=null)PerlicaAttackFx.PlayRound(message.EventSequence,Binding.MemberEmission[member].position,visualAim,visibleAim,message.Hit&&visibleAim);
                    if(RifleFireFx!=null)RifleFireFx.PlayRound(message.EventSequence,member,visualAim,visibleAim);
                    EmissionMember=Casualties==null?HumanoidShotDispatch.MemberForNativeRound(message.RoundSequence+1,Binding.MemberCount):Casualties.MemberForNativeRound(message.RoundSequence+1);
                }
                if(message.Kind==HumanoidWeaponVisualEventKind.Windup)
                {if(AttackAudio!=null)AttackAudio.PlayWindup(message.EventSequence,Binding.MemberEmission[member].position);if(PerlicaAttackFx!=null)PerlicaAttackFx.PlayWindup(message.EventSequence,Binding.MemberEmission[member].position);Motion[member].ObserveWindup(message.WindupSequence,delay);}
                else Motion[member].ObserveFire(message.RoundSequence,delay);
            }
        }
        static bool TryVisualAim(HumanoidWeaponVisualEvent message,Vector3 muzzle,out Vector3 aim)
        {
            aim=new Vector3(message.AimX,muzzle.y,message.AimZ);
            if(!message.HasAim)return false;
            if(message.Target.Equals(BBI.Core.ComponentModel.Entity.None))return true;
            // Show a hit anchor only for a target that the native view exposes.
            // Keep the authoritative XZ rather than retargeting the attack.
            try
            {
                UnitView target=BBI.Unity.Game.EntityComponentExtensions.GetComponent<UnitView>(message.Target,47);
                if(target==null||Convert.ToInt32(target.Visibility)!=2)return false;
                Collider shape=target.GetComponent<Collider>();
                aim.y=shape!=null?shape.bounds.center.y:target.transform.position.y+.8f;
                return !float.IsNaN(aim.y)&&!float.IsInfinity(aim.y);
            }
            // An impact may destroy/remove its target before presentation runs.
            // Optional hit decoration must not break the actor driver.
            catch(Exception){return false;}
        }
        void UpdateCasualties(bool paused)
        {
            if(Casualties==null||View==null||Binding==null||View.NonInterpolatedTickState==null)return;
            int effectiveHP=View.NonInterpolatedTickState.UnitIsDead?0:View.NonInterpolatedTickState.Hitpoints;
            if(!Casualties.ObserveHealth(effectiveHP,View.UnitAttrs.MaxHealth,paused))return;
            for(int member=0;member<Binding.MemberCount;member++)
                if(Binding.AliveMembers[member]&&!Casualties.IsAlive(member))
                {HumanoidCorpse.Spawn(View,Binding,Data,member,LastSensed);Binding.AliveMembers[member]=false;}
            Runtime.Log("HUMANOID_SQUAD_CASUALTIES entity="+View.EntityID+"; hp="+View.NonInterpolatedTickState.Hitpoints+"; survivors="+Casualties.AliveCount+"; total="+Casualties.TotalMembers+"; wholeSquadHPMapping=True; individualDamageAndNav=False; DPSUnchanged=True");
        }
        static float Nonzero(float v){return Math.Abs(v)<.00001f?1f:v;}
    }
}
