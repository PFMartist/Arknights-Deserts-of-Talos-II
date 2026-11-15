using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BBI.Core.ComponentModel;
using BBI.Game.Simulation;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.FX;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Native destruction and FX pooling remain authoritative. This module only
    // provides an independent, recognizable BVM visual carcass, not a resource.
    public static class BvmWreck
    {
        static readonly List<UnityEngine.Object> Owned=new List<UnityEngine.Object>();
        static readonly Dictionary<UnitAssetContainer,GameObject> Prepared=new Dictionary<UnitAssetContainer,GameObject>();
        static GameObject Templates;
        static BvmTurretEjectionSettings VisualOptions;
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;

        internal static BvmTurretEjectionSettings Settings
        {
            get
            {
                if(VisualOptions==null)
                {
                    try{VisualOptions=BvmTurretEjectionSettings.Load(Path.Combine(CustomUnits.AssetDirectory,"bvm_wreck.ini"));}
                    catch(Exception error)
                    {
                        // A bad optional visual config must not prevent native death.
                        VisualOptions=new BvmTurretEjectionSettings();VisualOptions.Enabled=false;VisualOptions.DemoOnCombatDeath=false;
                        Runtime.Log("BVM_WRECK_CONFIG_WARNING "+error.Message+"; turretEjectionDisabled=True; staticCarcassRetained=True");
                    }
                    Runtime.Log("BVM_WRECK_VISUAL_OPTIONS enabled="+VisualOptions.Enabled+"; demoOnCombatDeath="+VisualOptions.DemoOnCombatDeath+
                        "; source="+VisualOptions.Source+"; realCookoffDamageModel=False");
                }
                return VisualOptions;
            }
        }

        public static GameObject Apply(UnitAssetContainer custom,UnitAssetContainer original,UnitViewAttributes preparedView)
        {
            if(custom==null||original==null||object.ReferenceEquals(custom,original)||preparedView==null||preparedView.ModelPrefab==null)
                throw new InvalidOperationException("BVM wreck needs an independent prepared custom unit");
            GameObject existing;if(Prepared.TryGetValue(custom,out existing))return existing;
            BvmRigBinding[] rigs=preparedView.ModelPrefab.GetComponentsInChildren<BvmRigBinding>(true);
            if(rigs.Length!=1||rigs[0].ModelRoot==null)throw new InvalidOperationException("BVM wreck requires exactly one prepared own rig");
            ScriptableObject nativeAsset=NativeRead.Get(original,"m_Death") as ScriptableObject;
            if(nativeAsset==null)throw new InvalidOperationException("Native death asset is absent; refusing to invent destruction settings");
            UnitDeathAttributes native=NativeRead.Get(nativeAsset,"m_Attributes") as UnitDeathAttributes;
            if(native==null||native.DeathAttributes==null)throw new InvalidOperationException("Native destruction settings are absent");
            object originalCustomDeath=NativeRead.Get(custom,"m_Death");
            GameObject originalCarcass=native.DeathAttributes.CarcassPrefab,originalBurst=native.DeathAttributes.InitialBurstPrefab;
            object originalGround=native.GroundUnitDestruction;
            ScriptableObject independent=RhodesAssetClone59.CreateEmpty(nativeAsset.GetType());
            Dictionary<object,object> copied=new Dictionary<object,object>(new IdentityComparer());
            foreach(FieldInfo field in Fields(nativeAsset.GetType()))if(field.Name!="mDataCache")field.SetValue(independent,Copy(field.GetValue(nativeAsset),copied));
            independent.name=custom.name+"_Death_BVM";independent.hideFlags=HideFlags.HideAndDontSave;Owned.Add(independent);
            UnitDeathAttributes data=NativeRead.Get(independent,"m_Attributes") as UnitDeathAttributes;
            if(data==null||object.ReferenceEquals(data,native)||object.ReferenceEquals(data.DeathAttributes,native.DeathAttributes)||
                (data.GroundUnitDestruction!=null&&object.ReferenceEquals(data.GroundUnitDestruction,native.GroundUnitDestruction)))
                throw new InvalidOperationException("BVM death settings are not deeply independent");
            GameObject carcass=BuildPrefab(custom.name,rigs[0],originalCarcass);
            data.DeathAttributes.CarcassPrefab=carcass;
            if(data.GroundUnitDestruction!=null)
            {
                data.GroundUnitDestruction.TripOnDeath=false;
                data.GroundUnitDestruction.MapWheelBones=false;
                data.GroundUnitDestruction.SteeringWheels=new string[0];
            }
            // Preserve native burst, audio/ping, skip/death delay and all sim data.
            if(native.DeathAttributes.CarcassPrefab!=originalCarcass||native.DeathAttributes.InitialBurstPrefab!=originalBurst||
                !object.ReferenceEquals(native.GroundUnitDestruction,originalGround)||
                data.DeathAttributes.InitialBurstPrefab!=originalBurst||data.DeathAttributes.DeathPingPrefab!=native.DeathAttributes.DeathPingPrefab||
                data.DeathAttributes.ModelSwapDelayTime!=native.DeathAttributes.ModelSwapDelayTime||
                data.DeathAttributes.ModelSwapAdditionalRandomDelay!=native.DeathAttributes.ModelSwapAdditionalRandomDelay||
                data.DeathAttributes.CannotSkipDeathSequence!=native.DeathAttributes.CannotSkipDeathSequence)
                throw new InvalidOperationException("Native death settings changed during BVM carcass preparation");
            OptionalOnEnable(independent);Set(custom,"m_Death",independent);Set(custom,"mDataCache",null);Prepared.Add(custom,carcass);
            Runtime.Log("BVM_WRECK_PREPARED custom="+custom.name+"; originalDeathReferenceWas="+(object.ReferenceEquals(originalCustomDeath,nativeAsset)?"native":"otherCustom")+
                "; independentDeathAsset=True; visualCarcass="+carcass.name+"; nativeInitialBurstRetained=True; nativeModelSwapDelay="+data.DeathAttributes.ModelSwapDelayTime+
                "; nativeGroundTripDisabledOnlyOnCustom=True; nativeDamageUnchanged=True; nativeFXCarcassQueue=True; nativePoolDespawn=True; collectibleResource=False; actualDeathAcceptancePending=True");
            return carcass;
        }

        static GameObject BuildPrefab(string key,BvmRigBinding rig,GameObject nativeCarcass)
        {
            if(Templates==null){Templates=new GameObject("EF_BVM_WreckTemplates");Templates.SetActive(false);Templates.hideFlags=HideFlags.HideAndDontSave;UnityEngine.Object.DontDestroyOnLoad(Templates);}
            Shader shader=Shader.Find("Standard");if(shader==null||!shader.isSupported)shader=Shader.Find("Diffuse");
            if(shader==null||!shader.isSupported)throw new InvalidOperationException("No supported static BVM carcass shader");
            GameObject root=new GameObject(key+"_StaticCarcass");root.transform.parent=Templates.transform;root.hideFlags=HideFlags.HideAndDontSave;
            root.transform.localPosition=Vector3.zero;root.transform.localRotation=Quaternion.identity;root.transform.localScale=Vector3.one;
            if(rig.Bones==null||rig.YawNode<0||rig.YawNode>=rig.Bones.Length||rig.Bones[rig.YawNode]==null)
                throw new InvalidOperationException("BVM carcass has no real turret yaw pivot");
            Vector3 turretPivot=rig.ModelRoot.InverseTransformPoint(rig.Bones[rig.YawNode].position);
            GameObject turret=new GameObject("EF_BVM_CarcassTurretPivot");turret.transform.parent=root.transform;
            turret.transform.localPosition=turretPivot;turret.transform.localRotation=Quaternion.identity;turret.transform.localScale=Vector3.one;
            List<MeshFilter> filters=new List<MeshFilter>();List<Mesh> originals=new List<Mesh>();List<string> names=new List<string>();List<bool> turretParts=new List<bool>();
            int turretTriangles=0,turretChunkCount=0;
            HashSet<Mesh> imported=new HashSet<Mesh>();Dictionary<Material,Material> materials=new Dictionary<Material,Material>();int triangles=0;
            Texture2D white=new Texture2D(1,1,TextureFormat.ARGB32,false);white.name="EF_BVM_CarcassNeutralAlbedo";white.SetPixel(0,0,Color.white);white.Apply();white.hideFlags=HideFlags.HideAndDontSave;Owned.Add(white);
            foreach(SkinnedMeshRenderer source in rig.ModelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh=source.sharedMesh;
                if(mesh==null||!mesh.name.StartsWith("EF_BVM_Chunk",StringComparison.Ordinal)||!imported.Add(mesh))continue;
                Transform owner=source.bones!=null&&source.bones.Length==1?source.bones[0]:null;
                if(owner==null)throw new InvalidOperationException("BVM carcass lost its single-owner bone contract");
                bool isTurret=owner==rig.Bones[rig.YawNode]||owner.IsChildOf(rig.Bones[rig.YawNode]);
                Mesh templateMesh=mesh;
                if(isTurret)
                {
                    templateMesh=UnityEngine.Object.Instantiate(mesh) as Mesh;
                    templateMesh.name=mesh.name+"_TurretPivotTemplate";templateMesh.hideFlags=HideFlags.HideAndDontSave;
                    Vector3[] vertices=templateMesh.vertices;for(int v=0;v<vertices.Length;v++)vertices[v]-=turretPivot;
                    templateMesh.vertices=vertices;templateMesh.RecalculateBounds();Owned.Add(templateMesh);
                    turretTriangles+=mesh.triangles.Length/3;turretChunkCount++;
                }
                GameObject part=new GameObject(mesh.name+"_Wreck");part.layer=source.gameObject.layer;part.transform.parent=isTurret?turret.transform:root.transform;
                part.transform.localPosition=Vector3.zero;part.transform.localRotation=Quaternion.identity;part.transform.localScale=Vector3.one;
                MeshFilter filter=part.AddComponent<MeshFilter>();filter.sharedMesh=templateMesh;
                MeshRenderer renderer=part.AddComponent<MeshRenderer>();renderer.receiveShadows=true;
                Material[] assigned=source.sharedMaterials;
                for(int i=0;i<assigned.Length;i++)
                {
                    Material original=assigned[i],wreck;
                    if(original==null)throw new InvalidOperationException("BVM source material missing");
                    if(!materials.TryGetValue(original,out wreck))
                    {
                        wreck=CreateMaterial(original,shader,white);materials.Add(original,wreck);Owned.Add(wreck);
                    }
                    assigned[i]=wreck;
                }
                renderer.sharedMaterials=assigned;filters.Add(filter);originals.Add(templateMesh);names.Add(mesh.name);turretParts.Add(isTurret);triangles+=mesh.triangles.Length/3;
            }
            if(filters.Count==0||triangles!=315238||turretChunkCount==0||turretChunkCount==filters.Count)
                throw new InvalidOperationException("BVM carcass hull/turret partition did not preserve the prepared geometry");
            root.layer=filters[0].gameObject.layer;turret.layer=root.layer;
            // Components are added under an inactive template parent only AFTER
            // the renderer hierarchy exists, so native Awake caches valid parts.
            FXElement element=root.AddComponent<FXElement>();element.FXType=FXElement.FXCategory.CarcassSmall;
            element.OnParentDespawn=FXElement.ParentDespawnType.Ungroup;
            BvmTurretDebris flight=turret.AddComponent<BvmTurretDebris>();flight.TemplatePivot=turretPivot;
            BvmStaticCarcass corpse=root.AddComponent<BvmStaticCarcass>();corpse.Filters=filters.ToArray();corpse.TemplateMeshes=originals.ToArray();corpse.ChunkNames=names.ToArray();
            corpse.TurretParts=turretParts.ToArray();corpse.Turret=flight;
            if(nativeCarcass!=null)
            {
                FXCarcassBase[] native=nativeCarcass.GetComponentsInChildren<FXCarcassBase>(true);
                if(native.Length>0){corpse.LifeTime=native[0].LifeTime;corpse.FadeDuration=native[0].FadeDuration;}
            }
            Runtime.Log("BVM_WRECK_PREFAB_READY parts="+filters.Count+"; triangles="+triangles+"; nativeFXCategory=CarcassSmall; nativeLifeTime="+corpse.LifeTime+
                "; nativeFadeDuration="+corpse.FadeDuration+"; shader="+shader.name+"; turretChunks="+turretChunkCount+"; turretTriangles="+turretTriangles+
                "; hullTriangles="+(triangles-turretTriangles)+"; yawPivot="+turretPivot+"; visualEjectionEnabled="+Settings.Enabled+"; subtreePartitionDisjoint=True; sourceMeshesUnmodified=True; pauseUsesSimulationClock=True; sourceAlbedoUnmodified=True; unitVisibilityShaderNotUsed=True; RigidbodyCount=0; ColliderCount=0; resourceEntity=False");
            return root;
        }

        static Material CreateMaterial(Material source,Shader shader,Texture2D white)
        {
            Material material=new Material(shader);material.name="EF_BVM_Burnt_"+source.name;material.hideFlags=HideFlags.HideAndDontSave;
            bool track=source.name.IndexOf("track",StringComparison.OrdinalIgnoreCase)>=0;
            bool cutout=source.IsKeywordEnabled("_ALPHATEST_ON")||source.renderQueue==2450;
            bool glass=source.renderQueue>=3000;
            if(material.HasProperty("_Color"))material.SetColor("_Color",track?new Color(.12f,.13f,.12f,1f):(glass?new Color(.09f,.10f,.09f,1f):new Color(.27f,.28f,.26f,1f)));
            // Opaque surfaces become neutral burnt material. Preserve the alpha
            // texture only for actual cutout geometry so grilles keep their holes.
            if(material.HasProperty("_MainTex"))material.SetTexture("_MainTex",cutout&&source.HasProperty("_MainTex")?source.GetTexture("_MainTex"):white);
            if(material.HasProperty("_BumpMap")&&source.HasProperty("_BumpMap")&&source.GetTexture("_BumpMap")!=null)
            {material.SetTexture("_BumpMap",source.GetTexture("_BumpMap"));material.EnableKeyword("_NORMALMAP");}
            if(material.HasProperty("_OcclusionMap")&&source.HasProperty("_OcclusionMap")&&source.GetTexture("_OcclusionMap")!=null)
            {material.SetTexture("_OcclusionMap",source.GetTexture("_OcclusionMap"));material.EnableKeyword("_OCCLUSIONMAP");}
            if(material.HasProperty("_Metallic"))material.SetFloat("_Metallic",track?.12f:.30f);
            if(material.HasProperty("_Glossiness"))material.SetFloat("_Glossiness",glass?.25f:.12f);
            if(cutout)
            {
                if(material.HasProperty("_Mode"))material.SetFloat("_Mode",1f);if(material.HasProperty("_Cutoff"))material.SetFloat("_Cutoff",.5f);
                material.EnableKeyword("_ALPHATEST_ON");material.renderQueue=2450;
            }
            return material;
        }

        // Call only after the old map/FX scene has unloaded, before destroying
        // the old imported source meshes. Not needed during a live test match.
        public static void ResetAfterSceneUnload()
        {
            foreach(BvmStaticCarcass corpse in Resources.FindObjectsOfTypeAll(typeof(BvmStaticCarcass)))
                if(corpse!=null&&corpse.gameObject.activeInHierarchy)throw new InvalidOperationException("Cannot release BVM carcass assets used by an active FX instance");
            if(Templates!=null)UnityEngine.Object.DestroyImmediate(Templates);Templates=null;
            for(int i=Owned.Count-1;i>=0;i--)if(Owned[i]!=null)UnityEngine.Object.DestroyImmediate(Owned[i]);Owned.Clear();Prepared.Clear();VisualOptions=null;
        }
        sealed class IdentityComparer:IEqualityComparer<object>
        {
            public new bool Equals(object a,object b){return object.ReferenceEquals(a,b);}
            public int GetHashCode(object a){return RuntimeHelpers.GetHashCode(a);}
        }
        static object Copy(object value,Dictionary<object,object> seen)
        {
            if(value==null)return null;Type type=value.GetType();
            if(type.IsPrimitive||type.IsEnum||type==typeof(string)||typeof(UnityEngine.Object).IsAssignableFrom(type))return value;
            object previous;if(seen.TryGetValue(value,out previous))return previous;
            Array source=value as Array;
            if(source!=null){Array clone=(Array)source.Clone();seen.Add(value,clone);for(int i=0;i<clone.Length;i++)clone.SetValue(Copy(source.GetValue(i),seen),i);return clone;}
            if(type.Namespace==null||!type.Namespace.StartsWith("BBI.",StringComparison.Ordinal))return value;
            object result=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,null);seen.Add(value,result);
            foreach(FieldInfo field in Fields(type))field.SetValue(result,Copy(field.GetValue(value),seen));return result;
        }
        static IEnumerable<FieldInfo> Fields(Type type)
        {
            for(Type current=type;current!=null&&current!=typeof(object)&&current!=typeof(UnityEngine.Object)&&current!=typeof(ScriptableObject);current=current.BaseType)
                foreach(FieldInfo field in current.GetFields(Flags))if(!field.IsStatic)yield return field;
        }
        static void Set(object target,string name,object value)
        {
            foreach(FieldInfo field in Fields(target.GetType()))if(field.Name==name){field.SetValue(target,value);return;}
            throw new MissingFieldException(target.GetType().FullName,name);
        }
        static void OptionalOnEnable(object target)
        {
            for(Type type=target.GetType();type!=null;type=type.BaseType)
            {MethodInfo method=type.GetMethod("OnEnable",Flags,null,Type.EmptyTypes,null);if(method!=null){method.Invoke(target,null);return;}}
        }
    }

    // Freeze a real, skinned BVM pose into separate MeshRenderer geometry, then
    // retain native FXCarcassBase queue/lifetime/fading/pool-despawn behaviour.
    public sealed class BvmStaticCarcass : FXCarcassBase
    {
        // Native spawn disables components whose update contract is false.
        protected override bool RequiresUpdates { get { return true; } }
        public MeshFilter[] Filters;
        public Mesh[] TemplateMeshes;
        public string[] ChunkNames;
        public bool[] TurretParts;
        public BvmTurretDebris Turret;
        readonly List<Mesh> Baked=new List<Mesh>();
        Vector3 SavedPosition;Quaternion SavedRotation;Vector3 SavedScale;
        bool PendingPose,Spawned,HadSnapshot;
        int Reports;float NextReport;

        protected override void DoSpawnOps(FXSpawnDependencies dependencies)
        {
            ClearBaked();base.DoSpawnOps(dependencies);Reports=0;NextReport=0f;Spawned=true;HadSnapshot=false;
            FXCarcassBase.CarcassSpawnInfo info;
            if(dependencies==null||!dependencies.Get<FXCarcassBase.CarcassSpawnInfo>(out info)||info==null||info.SpawningUnit==null)
            {Runtime.Log("BVM_WRECK_SNAPSHOT_WARNING nativeCarcassSpawnInfoMissing=True; templateGeometryRetained=True");return;}
            BvmRigBinding[] source=info.SpawningUnit.GetComponentsInChildren<BvmRigBinding>(true);
            if(source.Length!=1||source[0].ModelRoot==null)
            {Runtime.Log("BVM_WRECK_SNAPSHOT_WARNING sourceBVMRigMissing=True; templateGeometryRetained=True");return;}
            BvmRigBinding rig=source[0];SavedPosition=rig.ModelRoot.position;SavedRotation=rig.ModelRoot.rotation;SavedScale=rig.ModelRoot.lossyScale;
            // The old wrapper's destruction Rigidbody can settle below the tank
            // visual anchor. Place this static corpse on the actual terrain.
            int terrain=LayerMask.NameToLayer("Terrain");RaycastHit ground;
            if(terrain>=0&&Physics.Raycast(new Vector3(SavedPosition.x,2000f,SavedPosition.z),Vector3.down,out ground,4000f,1<<terrain))SavedPosition.y=ground.point.y+.025f;
            PendingPose=true;
            try
            {
                Dictionary<string,SkinnedMeshRenderer> renderers=new Dictionary<string,SkinnedMeshRenderer>(StringComparer.Ordinal);
                foreach(SkinnedMeshRenderer renderer in rig.ModelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if(renderer.sharedMesh!=null&&renderer.sharedMesh.name.StartsWith("EF_BVM_Chunk",StringComparison.Ordinal)&&!renderers.ContainsKey(renderer.sharedMesh.name))
                        renderers.Add(renderer.sharedMesh.name,renderer);
                if(Filters==null||ChunkNames==null||TurretParts==null||Filters.Length!=ChunkNames.Length||Filters.Length!=TurretParts.Length)
                    throw new InvalidOperationException("Carcass target chunk contract changed");
                Vector3 pivot=rig.ModelRoot.InverseTransformPoint(rig.Bones[rig.YawNode].position);
                List<Mesh> turretMeshes=new List<Mesh>();
                for(int i=0;i<Filters.Length;i++)
                {
                    SkinnedMeshRenderer renderer;if(Filters[i]==null||!renderers.TryGetValue(ChunkNames[i],out renderer))throw new InvalidOperationException("Source carcass chunk not found: "+ChunkNames[i]);
                    Mesh mesh=new Mesh();mesh.name=ChunkNames[i]+"_Frozen_"+GetInstanceID();mesh.hideFlags=HideFlags.HideAndDontSave;Baked.Add(mesh);
                    renderer.BakeMesh(mesh);if(mesh.vertexCount!=renderer.sharedMesh.vertexCount)throw new InvalidOperationException("Baked BVM vertex count changed");
                    // Explicitly retain topology and UV on this Unity 5.2 path;
                    // the source mesh remains immutable and the baked vertices
                    // carry the real turret/weapon/wheel pose at destruction.
                    mesh.uv=renderer.sharedMesh.uv;mesh.triangles=renderer.sharedMesh.triangles;
                    if(TurretParts[i])
                    {
                        Vector3[] vertices=mesh.vertices;for(int v=0;v<vertices.Length;v++)vertices[v]-=pivot;mesh.vertices=vertices;
                        turretMeshes.Add(mesh);
                    }
                    if(mesh.normals.Length!=mesh.vertexCount)mesh.RecalculateNormals();
                    if(mesh.tangents.Length!=mesh.vertexCount)mesh.tangents=renderer.sharedMesh.tangents;
                    mesh.RecalculateBounds();
                    Filters[i].sharedMesh=mesh;
                }
                HadSnapshot=true;
                ApplySavedPose();
                if(Turret!=null)
                {
                    Turret.transform.localPosition=pivot;
                    UnitView view=info.SpawningUnit.GetComponent<UnitView>();
                    UnitState state=view==null?null:view.NonInterpolatedTickState;
                    BvmTurretEjectionSettings options=BvmWreck.Settings;
                    bool combatDeath=state!=null&&(state.UnitIsDead||state.Hitpoints<=0)&&state.LastAttackedByEntity!=Entity.None;
                    if(options.Enabled&&options.DemoOnCombatDeath&&combatDeath)
                        Turret.Begin(transform,transform.TransformPoint(pivot),SavedRotation,SavedScale,BvmTurretDebris.BuildSupport(turretMeshes),options,view.EntityID.GetHashCode());
                    else Runtime.Log("BVM_TURRET_EJECTION_NOT_SELECTED enabled="+options.Enabled+"; demoOnCombatDeath="+options.DemoOnCombatDeath+
                        "; confirmedCombatDeath="+combatDeath+"; verifiedAmmoCookoffSignal=False; completeStaticCarcassRetained=True");
                }
            }
            catch(Exception error)
            {
                ClearBaked();HadSnapshot=false;Runtime.Log("BVM_WRECK_SNAPSHOT_WARNING "+error.GetType().Name+": "+error.Message+"; restPoseTemplateRetained=True; nativeDeathContinues=True");
            }
            ApplySavedPose();
            Runtime.Log("BVM_WRECK_SPAWNED entity="+(info.SpawningUnit.GetComponent<UnitView>()==null?"none":info.SpawningUnit.GetComponent<UnitView>().EntityID.ToString())+
                "; frozenPose="+HadSnapshot+"; parts="+(Filters==null?0:Filters.Length)+"; sourceWorldPose="+SavedPosition+"; independentMeshes="+Baked.Count+
                "; nativeFXCarcassQueue=True; nativeInitialBurstAndDeathEvent=True; noRigidbody=True; noCollider=True; noUnitView=True; collectibleResource=False; actualPixelAcceptancePending=True");
        }
        void LateUpdate()
        {
            // FXElement resets only original GO/rigidbody/light/animation state,
            // not MeshFilter meshes. Reapply the captured world pose once after
            // the spawn broadcasts, then let native sinking/fading move it.
            if(PendingPose){ApplySavedPose();if(Turret!=null)Turret.Tick(0f);PendingPose=false;}
        }
        void ApplySavedPose()
        {
            transform.position=SavedPosition;transform.rotation=SavedRotation;
            Vector3 parent=transform.parent==null?Vector3.one:transform.parent.lossyScale;
            transform.localScale=new Vector3(SavedScale.x/Math.Max(.00001f,Math.Abs(parent.x)),SavedScale.y/Math.Max(.00001f,Math.Abs(parent.y)),SavedScale.z/Math.Max(.00001f,Math.Abs(parent.z)));
            bool ready=false;Bounds bounds=new Bounds();
            if(mRenderers!=null)foreach(Renderer renderer in mRenderers)if(renderer!=null){if(!ready){bounds=renderer.bounds;ready=true;}else bounds.Encapsulate(renderer.bounds);}
            mHeight=ready?Math.Max(.5f,bounds.size.y+.15f):3f;
        }
        protected override void FreezeBody(){ }
        // FXCarcassBase.Update is a private Unity message which consumes wall
        // deltaTime. This own message follows its inspected native state machine
        // with the simulation clock, retaining the native queue/transitions/pool.
        void Update()
        {
            if(!Spawned)return;
            float dt=Runtime.PresentationDeltaTime;
            if(dt<=0f||float.IsNaN(dt)||float.IsInfinity(dt))return;
            mTimer-=dt;
            switch(mState)
            {
                case CarcassState.Alive:
                    if(mTimer<=0f){if(mIsFlaggedForRemoval){RefreshFadeHeight();TransitionToFading();}else TransitionToFrozen();}
                    break;
                case CarcassState.Frozen:
                    if(mTimer<=0f&&mIsFlaggedForRemoval){RefreshFadeHeight();TransitionToFading();}
                    break;
                case CarcassState.Fading:
                    if(mTimer<=0f)
                    {
                        FXManager.Manager.CarcassQueue.Remove(this);DespawnCarcass();return;
                    }
                    transform.position-=Vector3.up*(mHeight/Math.Max(.001f,FadeDuration)*dt);
                    break;
            }
            if(Turret!=null)Turret.Tick(dt);
            UpdateCarcass();
        }
        void RefreshFadeHeight()
        {
            bool ready=false;Bounds bounds=new Bounds();
            if(mRenderers!=null)foreach(Renderer renderer in mRenderers)if(renderer!=null)
            {if(!ready){bounds=renderer.bounds;ready=true;}else bounds.Encapsulate(renderer.bounds);}
            if(ready)mHeight=Math.Max(.5f,bounds.size.y+.20f);
        }
        protected override void UpdateCarcass()
        {
            if(!Spawned||Reports>=6||Time.realtimeSinceStartup<NextReport)return;
            int enabled=0,visible=0,supported=0;Bounds bounds=new Bounds();bool ready=false;
            if(mRenderers!=null)foreach(Renderer renderer in mRenderers)
            {
                if(renderer==null)continue;if(renderer.enabled)enabled++;if(renderer.isVisible)visible++;
                if(renderer.sharedMaterial!=null&&renderer.sharedMaterial.shader!=null&&renderer.sharedMaterial.shader.isSupported)supported++;
                if(!ready){bounds=renderer.bounds;ready=true;}else bounds.Encapsulate(renderer.bounds);
            }
            Runtime.Log("BVM_WRECK_RENDER_STATE active="+gameObject.activeInHierarchy+"; frozenPose="+HadSnapshot+"; enabled="+enabled+"; visibleToAnyCamera="+visible+"; supportedMaterials="+supported+
                "; worldBounds="+bounds+"; root="+transform.position+"; nativeCarcassState="+mState+"; nativeTimer="+mTimer+"; nativeFadeHeight="+mHeight+"; collectibleResource=False; pixelAcceptancePending=True");
            Reports++;NextReport=Time.realtimeSinceStartup+.5f;
        }
        protected override void DoDespawnOps(object arg)
        {
            Runtime.Log("BVM_WRECK_DESPAWNED nativeFXPool=True; ownedMeshCount="+Baked.Count+"; resourceRemovedByModule=False");
            ClearBaked();PendingPose=Spawned=false;base.DoDespawnOps(arg);
        }
        void ClearBaked()
        {
            if(Turret!=null)Turret.ResetFlight();
            if(Filters!=null&&TemplateMeshes!=null)for(int i=0;i<Math.Min(Filters.Length,TemplateMeshes.Length);i++)if(Filters[i]!=null)Filters[i].sharedMesh=TemplateMeshes[i];
            for(int i=Baked.Count-1;i>=0;i--)if(Baked[i]!=null)UnityEngine.Object.DestroyImmediate(Baked[i]);Baked.Clear();
        }
        void OnDestroy(){ClearBaked();PendingPose=Spawned=false;}
    }
}
