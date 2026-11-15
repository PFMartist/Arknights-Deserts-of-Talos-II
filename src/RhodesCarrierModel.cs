using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    // Static authored hull, carried exclusively by the native UnitView/mover.
    // Source geometry is never stretched to force a historical dimension ratio.
    public sealed class RhodesCarrierBinding : MonoBehaviour
    {
        public Transform GeometryRoot;        // Private presentation anchors are cloned with the model wrapper.        public Transform SignalProbeMarker;        public Transform[] ModuleWarningMarkers;
        public Renderer[] Geometry;
        public Bounds GeometryBounds;
        public float TargetLength;
        public bool IsV05,HasSelectionBounds,HasNavigationBounds;
        public bool NativeLodConfigured,NativeAlwaysHighLOD;
        public int NativeWheelActiveLODs,NativeSuspensionActiveLODs;
        public Bounds VisualBounds,SelectionBounds,NavigationBounds;
        public Transform NativeRoot;
        public string[] NodeNames,ModuleIds,ModuleSlots,ModuleRoles,ModuleBranches,ModuleResearch;
        public Transform[] Nodes,ModuleRoots;
        public Vector3[] AuthoredLocalPositions;
        public Quaternion[] AuthoredLocalRotations;
        public int[] ModuleLevels;
        public float[] ModuleRiseDepths;
        // Nonserialized dictionary is reconstructed per cloned native wrapper.
        Dictionary<string,Transform> NodeMap;
        public Transform FindNode(string name)
        {
            if(NodeMap==null){NodeMap=new Dictionary<string,Transform>(StringComparer.Ordinal);if(NodeNames!=null&&Nodes!=null)for(int i=0;i<NodeNames.Length;i++)NodeMap.Add(NodeNames[i],Nodes[i]);}
            Transform value;return name!=null&&NodeMap.TryGetValue(name,out value)?value:null;
        }
        public void RegisterNode(string name,Transform node)
        {
            if(string.IsNullOrEmpty(name)||node==null||FindNode(name)!=null)throw new InvalidOperationException("Duplicate/invalid Rhodes node registration");
            string[] names=new string[NodeNames.Length+1];Transform[] nodes=new Transform[Nodes.Length+1];
            Array.Copy(NodeNames,names,NodeNames.Length);Array.Copy(Nodes,nodes,Nodes.Length);names[names.Length-1]=name;nodes[nodes.Length-1]=node;
            NodeNames=names;Nodes=nodes;NodeMap=null;
        }
        // Called once while the private template has its authored canonical pose,
        // before native UnitView actuation. Unity clones these value arrays.
        public void CaptureAuthoredLocalPose()
        {
            AuthoredLocalPositions=new Vector3[Nodes.Length];AuthoredLocalRotations=new Quaternion[Nodes.Length];
            for(int i=0;i<Nodes.Length;i++){AuthoredLocalPositions[i]=Nodes[i].localPosition;AuthoredLocalRotations[i]=Nodes[i].localRotation;}
        }
        public Bounds PickBounds { get { return HasSelectionBounds?SelectionBounds:GeometryBounds; } }
    }

    public static class RhodesCarrierModel
    {
        static readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();
        static GameObject Templates;
        static UserCamera BoundCamera;
        static UnitView BoundView;
        static Transform OriginalCameraTarget, CameraAnchor;

        public static GameObject Create(GameObject nativeWrapper, float targetLength)
        {
            if (nativeWrapper == null) throw new ArgumentNullException("nativeWrapper");
            if (targetLength < 100f || targetLength > 1000f) throw new ArgumentOutOfRangeException("targetLength");
            string v05 = Path.Combine(Path.Combine(CustomUnits.AssetDirectory, "rhodes_v05"), "rhodes_v05.bin");
            if(File.Exists(v05))return RhodesCarrierModelV05.Create(nativeWrapper,v05);
            string folder = Path.Combine(CustomUnits.AssetDirectory, "rhodes_carrier");
            string file = Path.Combine(folder, "rhodes_static.bin");
            Vector3[] positions, normals; Vector2[] primary, secondary; int[] indices;
            Bounds sourceBounds;
            Read(file, out positions, out normals, out primary, out secondary, out indices, out sourceBounds);
            float scale = targetLength / sourceBounds.size.z;
            for (int index = 0; index < positions.Length; index++) positions[index] *= scale;
            Mesh mesh = new Mesh(); mesh.name = "EF_RhodesCarrier_StaticLOD0"; mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.vertices = positions; mesh.normals = normals; mesh.uv = primary; mesh.uv2 = secondary; mesh.triangles = indices;
            Color[] colors = new Color[positions.Length]; for (int index = 0; index < colors.Length; index++) colors[index] = Color.white;
            mesh.colors = colors; mesh.RecalculateBounds(); Owned.Add(mesh);
            Material template = null;
            LODGroup nativeGroup = nativeWrapper.GetComponent<LODGroup>();
            if (nativeGroup != null) foreach (LOD lod in nativeGroup.GetLODs()) foreach (Renderer renderer in lod.renderers)
                if (template == null && renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null && renderer.sharedMaterial.shader.isSupported)
                    template = renderer.sharedMaterial;
            if (template == null) foreach (Renderer renderer in nativeWrapper.GetComponentsInChildren<Renderer>(true))
                if (template == null && renderer != null && !ProtectedName(renderer.transform) && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null && renderer.sharedMaterial.shader.isSupported)
                    template = renderer.sharedMaterial;
            if (template == null) throw new InvalidOperationException("Native carrier geometry shader template is unavailable");
            Material material = MaterialFor(folder, template);
            if (Templates == null)
            {
                Templates = new GameObject("MapUnitLab.RhodesCarrierTemplates"); Templates.SetActive(false);
                Templates.hideFlags = HideFlags.HideAndDontSave; UnityEngine.Object.DontDestroyOnLoad(Templates);
            }
            GameObject model = (GameObject)UnityEngine.Object.Instantiate(nativeWrapper);
            model.name = nativeWrapper.name; model.transform.parent = Templates.transform;
            model.transform.localPosition = Vector3.zero; model.transform.localRotation = nativeWrapper.transform.localRotation;
            model.transform.localScale = nativeWrapper.transform.localScale;
            LODGroup group = model.GetComponent<LODGroup>();
            if (group == null) group = model.AddComponent<LODGroup>();
            List<Renderer> inherited = new List<Renderer>();
            foreach (LOD lod in group.GetLODs()) foreach (Renderer renderer in lod.renderers)
                if (renderer != null && !inherited.Contains(renderer)) inherited.Add(renderer);
            foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (!inherited.Contains(renderer)) inherited.Add(renderer);
            foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>(true))
                if (!ProtectedName(renderer.transform) && !inherited.Contains(renderer)) inherited.Add(renderer);
            Mesh empty = new Mesh(); empty.name = "EF_RhodesCarrier_EmptyDonorGeometry"; empty.hideFlags = HideFlags.HideAndDontSave; Owned.Add(empty);
            int layer = model.layer;
            foreach (Renderer renderer in inherited)
            {
                layer = renderer.gameObject.layer; renderer.enabled = false;
                SkinnedMeshRenderer skin = renderer as SkinnedMeshRenderer; if (skin != null) skin.sharedMesh = empty;
                MeshFilter filter = renderer.GetComponent<MeshFilter>(); if (filter != null) filter.sharedMesh = empty;
            }
            int effectsRemoved = StripInheritedEffects(model);
            GameObject presentation = new GameObject("EF_RhodesCarrier_PresentationRoot"); presentation.layer = layer;
            presentation.transform.parent = model.transform; presentation.transform.localPosition = Vector3.zero;
            presentation.transform.localRotation = Quaternion.identity;
            Vector3 rootScale = model.transform.localScale;
            if (rootScale.x <= 0f || rootScale.y <= 0f || rootScale.z <= 0f) throw new InvalidDataException("Carrier wrapper has an invalid root scale");
            // Preserve the donor root's contract while cancelling its display scale
            // in our direct child. New vertices remain world-metre geometry.
            presentation.transform.localScale = new Vector3(1f/rootScale.x, 1f/rootScale.y, 1f/rootScale.z);
            presentation.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer geometry = presentation.AddComponent<MeshRenderer>(); geometry.sharedMaterial = material;
            group.SetLODs(new LOD[] { new LOD(.00001f, new Renderer[] { geometry }) }); group.RecalculateBounds();
            RhodesCarrierBinding binding = model.AddComponent<RhodesCarrierBinding>(); binding.GeometryRoot = presentation.transform;
            binding.Geometry = new Renderer[] { geometry }; binding.GeometryBounds = mesh.bounds; binding.VisualBounds=mesh.bounds; binding.SelectionBounds=mesh.bounds; binding.HasSelectionBounds=true; binding.NativeRoot=model.transform; binding.TargetLength = targetLength;
            UnitView view = model.GetComponent<UnitView>(); if (view == null) throw new InvalidDataException("Native carrier wrapper lacks UnitView");
            NativeRead.Set(view, "mGeoRenderObjects", binding.Geometry);
            model.SetActive(true);
            Runtime.Log("RHODES_MODEL_PREPARED source=EndfieldNativeLOD0; sourceTriangles=4800; sourceVertices=14400; nativeWrapper="+nativeWrapper.name+
                "; uniformScale="+scale+"; modelMetres="+mesh.bounds.size+"; minY="+mesh.bounds.min.y+"; bow=PositiveZ; primaryUV=SourceUV1; retainedSecondaryUV=SourceUV0"+
                "; bakedOriginalColour=True; sourceNormalMapNotMisbound=True; inheritedGeometryBlanked="+inherited.Count+"; inheritedEffectsRemoved="+effectsRemoved+
                "; nativeViewFowPropertyBlocksRetained=True; modelStaticNoAuthoredTrackAnimation=True; actualPixelAcceptancePending=True");
            return model;
        }

        static Material MaterialFor(string folder, Material template)
        {
            Material material = new Material(template); material.name = "EF_RhodesCarrier_OriginalColourApproximation"; material.hideFlags = HideFlags.HideAndDontSave;
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, false); texture.name = "EF_RhodesCarrier_Albedo";
            texture.hideFlags = HideFlags.HideAndDontSave;
            if (!texture.LoadImage(File.ReadAllBytes(Path.Combine(folder, "albedo.png"))) || texture.width != 2048 || texture.height != 2048)
                throw new InvalidDataException("Rhodes albedo is not the prepared 2048-square image");
            texture.wrapMode = TextureWrapMode.Repeat; texture.filterMode = FilterMode.Trilinear; Owned.Add(texture);
            material.mainTexture = texture; material.mainTextureScale = Vector2.one; material.mainTextureOffset = Vector2.zero;
            foreach (string property in new string[] { "_MainTex", "_BaseMap", "_Diffuse" }) if (material.HasProperty(property)) material.SetTexture(property, texture);
            foreach (string property in new string[] { "_Color", "_BaseColor", "_TrimColor" }) if (material.HasProperty(property)) material.SetColor(property, Color.white);
            foreach (string property in new string[] { "_BumpMap", "_OcclusionMap", "_MetallicGlossMap", "_EmissionMap" }) if (material.HasProperty(property)) material.SetTexture(property, null);
            material.DisableKeyword("_NORMALMAP"); material.DisableKeyword("_METALLICGLOSSMAP"); material.DisableKeyword("_EMISSION");
            if (material.HasProperty("_BumpScale")) material.SetFloat("_BumpScale", 0f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .2f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", .15f);
            if (material.HasProperty("_OverlayMask"))
            {
                Texture2D clear = new Texture2D(1, 1, TextureFormat.RGBA32, false); clear.SetPixel(0, 0, Color.clear); clear.Apply();
                clear.hideFlags = HideFlags.HideAndDontSave; Owned.Add(clear); material.SetTexture("_OverlayMask", clear);
            }
            Owned.Add(material); return material;
        }

        static bool ProtectedName(Transform transform)
        {
            for (; transform != null; transform = transform.parent)
            {
                string name = transform.name.ToLowerInvariant();
                if (name.Contains("sensor") || name.Contains("fow") || name.Contains("underlay") || name.Contains("select") || name.Contains("hud") || name.Contains("blip")) return true;
            }
            return false;
        }
        static int StripInheritedEffects(GameObject model)
        {
            int count = 0;
            Type[] types = { typeof(Light), typeof(LensFlare), typeof(Projector), typeof(ParticleSystem), typeof(ParticleSystemRenderer), typeof(ParticleEmitter), typeof(ParticleRenderer), typeof(TrailRenderer) };
            foreach (Type type in types) foreach (Component component in model.GetComponentsInChildren(type, true))
                if (component != null) { UnityEngine.Object.DestroyImmediate(component); count++; }
            return count;
        }
        static float Number(BinaryReader reader)
        { float value = reader.ReadSingle(); if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Nonfinite Rhodes geometry"); return value; }
        static Vector3 Vector(BinaryReader reader) { return new Vector3(Number(reader), Number(reader), Number(reader)); }
        static void Read(string file, out Vector3[] positions, out Vector3[] normals, out Vector2[] primary, out Vector2[] secondary, out int[] indices, out Bounds bounds)
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(file), Encoding.UTF8))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RHS1" || reader.ReadInt32() != 1) throw new InvalidDataException("Unsupported Rhodes static mesh format");
                int vertices = reader.ReadInt32(), count = reader.ReadInt32();
                if (vertices != 14400 || count != 14400 || reader.BaseStream.Length != 16L + vertices * 40L + count * 4L)
                    throw new InvalidDataException("Rhodes original LOD0 counts or file size changed");
                positions = new Vector3[vertices]; normals = new Vector3[vertices]; primary = new Vector2[vertices]; secondary = new Vector2[vertices]; indices = new int[count];
                bounds = new Bounds();
                for (int index = 0; index < vertices; index++)
                {
                    positions[index] = Vector(reader); Vector3 normal = Vector(reader);
                    if (normal.sqrMagnitude < .1f) throw new InvalidDataException("Rhodes source normal is degenerate"); normals[index] = normal.normalized;
                    primary[index] = new Vector2(Number(reader), Number(reader)); secondary[index] = new Vector2(Number(reader), Number(reader));
                    if (index == 0) bounds = new Bounds(positions[index], Vector3.zero); else bounds.Encapsulate(positions[index]);
                }
                for (int index = 0; index < count; index++) { int value = reader.ReadInt32(); if (value < 0 || value >= vertices) throw new InvalidDataException("Rhodes index outside vertex array"); indices[index] = value; }
                if (Math.Abs(bounds.min.y) > .001f || bounds.size.z < 29f || bounds.size.z > 31f || bounds.size.x <= 0f || bounds.size.y <= 0f || reader.BaseStream.Position != reader.BaseStream.Length)
                    throw new InvalidDataException("Rhodes ground/length/source buffer validation failed");
            }
        }

        public static void ObserveCamera(UnitView view, RhodesCarrierBinding binding)
        {
            // Native F/double-click owns targeting and zoom when preservation is on.
            if(SmallUnitCamera.NativeFocusPreserved) { RestoreCamera(); return; }
            foreach (UserCamera camera in UnityEngine.Object.FindObjectsOfType(typeof(UserCamera)))
            {
                if (camera == null || !camera.gameObject.activeInHierarchy || camera.ActiveCamera == null || camera.IsCinematicMode || camera.SensorsManagerActive) continue;
                Transform target = NativeRead.Get(camera, "m_BaseCamTarget") as Transform;
                if (target != view.transform) { if (camera == BoundCamera) RestoreCamera(); continue; }
                if (BoundCamera != camera || BoundView != view)
                {
                    RestoreCamera(); BoundCamera = camera; BoundView = view;
                    OriginalCameraTarget = NativeRead.Get(camera, "m_CamTarget") as Transform;
                    GameObject anchor = new GameObject("EF_RhodesCarrier_CameraAnchor"); anchor.hideFlags = HideFlags.HideAndDontSave;
                    anchor.transform.parent = binding.GeometryRoot; anchor.transform.localPosition = binding.PickBounds.center;
                    anchor.transform.localRotation = Quaternion.identity; anchor.transform.localScale = Vector3.one; CameraAnchor = anchor.transform;
                    float radius = binding.PickBounds.extents.magnitude;
                    float vertical = camera.ActiveCamera.fieldOfView * Mathf.Deg2Rad * .5f;
                    float horizontal = (float)Math.Atan(Math.Tan(vertical) * camera.ActiveCamera.aspect);
                    float distance = radius * 1.15f / (float)Math.Sin(Math.Min(vertical, horizontal));
                    distance = Mathf.Clamp(distance, camera.ClosestCameraDistanceToTarget, camera.CurrentMaxDistance);
                    AnimationCurve curve = camera.MotionValues.ScrollTickLERPCurveGameplay;
                    if (curve != null)
                    {
                        float low = 0f, high = 1f, minimum = camera.ClosestCameraDistanceToTarget, maximum = camera.CurrentMaxDistance;
                        for (int step = 0; step < 40; step++) { float middle = (low + high) * .5f; if (minimum + (maximum-minimum) * curve.Evaluate(middle) < distance) low = middle; else high = middle; }
                        NativeRead.Set(camera, "mDesiredDistanceLERPValue", (low+high)*.5f);
                    }
                    Runtime.Log("RHODES_CAMERA_FOCUS_ADAPTED length="+binding.TargetLength+"; geometryRadius="+radius+"; minimumFitDistance="+distance+
                        "; followsGeometryCentre=True; cameraTransformNotWritten=True; nativeSmoothZoomRetained=True; smallUnitProfileUnchanged=True");
                }
                Transform actual = NativeRead.Get(camera, "m_CamTarget") as Transform;
                if (CameraAnchor != null && (actual == OriginalCameraTarget || actual == CameraAnchor)) NativeRead.Set(camera, "m_CamTarget", CameraAnchor);
            }
        }
        static void RestoreCamera()
        {
            if (BoundCamera != null && CameraAnchor != null && NativeRead.Get(BoundCamera, "m_CamTarget") as Transform == CameraAnchor)
                NativeRead.Set(BoundCamera, "m_CamTarget", OriginalCameraTarget);
            if (CameraAnchor != null) UnityEngine.Object.DestroyImmediate(CameraAnchor.gameObject);
            BoundCamera = null; BoundView = null; OriginalCameraTarget = null; CameraAnchor = null;
        }
        public static void ResetViews()
        {
            RestoreCamera();

            foreach(RhodesSuspensionDriver suspension in Resources.FindObjectsOfTypeAll(typeof(RhodesSuspensionDriver)))if(suspension!=null)UnityEngine.Object.DestroyImmediate(suspension);
            foreach(RhodesProbeMotionDriver probeDriver in Resources.FindObjectsOfTypeAll(typeof(RhodesProbeMotionDriver)))if(probeDriver!=null)UnityEngine.Object.DestroyImmediate(probeDriver);
            foreach(RhodesTrackMotionDriver trackDriver in Resources.FindObjectsOfTypeAll(typeof(RhodesTrackMotionDriver)))if(trackDriver!=null)UnityEngine.Object.DestroyImmediate(trackDriver);
            foreach(RhodesAviationGuideDriver guideDriver in Resources.FindObjectsOfTypeAll(typeof(RhodesAviationGuideDriver)))if(guideDriver!=null)UnityEngine.Object.DestroyImmediate(guideDriver);            foreach(RhodesV17AviationGuideDriver guideDriver in Resources.FindObjectsOfTypeAll(typeof(RhodesV17AviationGuideDriver)))if(guideDriver!=null)UnityEngine.Object.DestroyImmediate(guideDriver);            RhodesV17AviationData.Reset();
            foreach(RhodesV05ModuleDriver moduleDriver in Resources.FindObjectsOfTypeAll(typeof(RhodesV05ModuleDriver)))if(moduleDriver!=null)UnityEngine.Object.DestroyImmediate(moduleDriver);
            foreach (RhodesCarrierView driver in Resources.FindObjectsOfTypeAll(typeof(RhodesCarrierView))) if (driver != null) UnityEngine.Object.DestroyImmediate(driver);
        }
        public static void ReleaseTemplates()
        {
            ResetViews(); RhodesThermalVisuals.ReleaseTemplates(); RhodesCarrierModelV05.ReleaseTemplates(); if (Templates != null) UnityEngine.Object.DestroyImmediate(Templates); Templates = null;
            foreach (UnityEngine.Object item in Owned) if (item != null) UnityEngine.Object.DestroyImmediate(item); Owned.Clear();
        }
    }

    public sealed class RhodesCarrierView : IsolatedUnitPresentation
    {
        UnitView View;
        BBI.Core.ComponentModel.Entity BoundEntity=BBI.Core.ComponentModel.Entity.None;
        RhodesCarrierBinding Binding;
        BoxCollider ClickBox;
        Vector3 OriginalClickCentre, OriginalClickSize;
        GameObject Ring;
        Vector3 OriginalRingScale;
        float RingFactor;
        bool HaveSphere;
        float OriginalSphere,DesiredSphere,NextCameraObservation;
        Bounds LastPickBounds,LastGeometryBounds,LastSelectionBounds;Matrix4x4 LastPickMatrix;
        bool HavePickState,HaveGeometryState;
        RhodesWeaponPresentation Weapons;
        RhodesV05ModuleDriver Modules;
        NativeGroundActivation56 GroundFX;
        RhodesTrackMotionDriver Tracks;
        RhodesProbeMotionDriver Probe;
        RhodesSuspensionDriver Suspension;        RhodesThermalVisuals Thermal;        bool ThermalAttempted,ThermalWarning;

        public bool Validated { get; private set; }

        public void Initialize(UnitView view, RhodesCarrierBinding binding)
        {
            if (View != null && (View != view || Binding != binding)) Restore();
            bool newGroundBinding=View!=view||Binding!=binding||!BoundEntity.Equals(view.EntityID);
            View = view; Binding = binding; BoundEntity=view.EntityID;
            if(newGroundBinding)GroundFX=NativeGroundActivation56.BindCarrier(view,binding);
            if(binding!=null&&binding.IsV05)
            {
                Modules=view.GetComponent<RhodesV05ModuleDriver>();if(Modules==null)Modules=view.gameObject.AddComponent<RhodesV05ModuleDriver>();
                Modules.Initialize(view,binding);
                RhodesAviationGuideDriver guides=view.GetComponent<RhodesAviationGuideDriver>();if(guides==null)guides=view.gameObject.AddComponent<RhodesAviationGuideDriver>();
                guides.Initialize(view,binding);
                Tracks=view.GetComponent<RhodesTrackMotionDriver>();if(Tracks==null)Tracks=view.gameObject.AddComponent<RhodesTrackMotionDriver>();
                Tracks.Initialize(view,binding.GeometryRoot,binding.FindNode);
                Probe=view.GetComponent<RhodesProbeMotionDriver>();if(Probe==null)Probe=view.gameObject.AddComponent<RhodesProbeMotionDriver>();
                Probe.Initialize(view,binding);
                Suspension=view.GetComponent<RhodesSuspensionDriver>();if(Suspension==null)Suspension=view.gameObject.AddComponent<RhodesSuspensionDriver>();
                Suspension.Initialize(view,binding);                AttachThermalVisuals();


            }
        }
        void LateUpdate()
        {
            if(PresentationFailed||!enabled)return;
            try{ApplyPresentationFrame();}
            catch(Exception error){DisablePresentation(View,"carrier_rig",error);}
        }
        void ApplyPresentationFrame()
        {
            if (View == null || Binding == null || Binding.GeometryRoot == null || !BoundEntity.Equals(View.EntityID))
            {return;}
            if(GroundFX!=null&&GroundFX.Observe(View))GroundFX=null;
            bool hasState=View.NonInterpolatedTickState!=null,nativeDying=View.IsDriverDead;
            bool alive=hasState&&!View.NonInterpolatedTickState.UnitIsDead&&!nativeDying;
            bool shown=(hasState||nativeDying)&&Convert.ToInt32(View.Visibility)==2&&!View.ForceIconOnlyMode;
            if(!alive)
            {
                // Keep the last selected modules, suspension and weapon pose
                // until native carcass spawn -> ShowModel(false) -> despawn.
                // Thermal FX/material cleanup remains in native OnDisable.
                if(Binding.GeometryRoot.gameObject.activeSelf!=shown)Binding.GeometryRoot.gameObject.SetActive(shown);
                return;
            }
            if(View.SimStateInterpolator==null)return;
            if(Binding.IsV05)
            {
                // Gate the whole presentation root; each authored LODGroup owns
                // renderer choice. Never enable every V05 LOD renderer here.
                if(Modules!=null)Modules.Step(shown);
                else if(Binding.GeometryRoot.gameObject.activeSelf!=shown)Binding.GeometryRoot.gameObject.SetActive(shown);
            }
            else foreach (Renderer renderer in Binding.Geometry) if (renderer != null) renderer.enabled = shown;
            if(Suspension!=null)Suspension.Step(View,Binding,shown);

            if(Tracks!=null)Tracks.Step(View,shown,!Binding.NativeLodConfigured||NativeUnitLodAdapter.AnimationActive(NativeUnitLodAdapter.CurrentLod(View),Binding.NativeWheelActiveLODs,Binding.NativeAlwaysHighLOD));
            if(Probe!=null)Probe.Step(View,shown);            ApplyThermalVisuals(shown);
            // One host commits the main-gun/recoil/transit overlay only
            // after modules and contact correction have reached their final pose.
            if(Weapons==null)Weapons=View.GetComponent<RhodesWeaponPresentation>();
            if(Weapons!=null)Weapons.ApplyFrame(Time.frameCount);
            FitCollider(); FitRing();FitBoundingSphere();
            if(Time.realtimeSinceStartup>=NextCameraObservation)
            {NextCameraObservation=Time.realtimeSinceStartup+.25f;RhodesCarrierModel.ObserveCamera(View,Binding);}
            bool ready = false;
            if(!Validated)
            {
                ready = true;
                foreach (Renderer renderer in Binding.Geometry)
                    if (renderer == null || renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null || !renderer.sharedMaterial.shader.isSupported) ready = false;
            }
            if (!Validated && ready && ClickBox != null)
            {
                Validated = true;
                Runtime.Log("RHODES_VIEW_VERIFIED entity="+View.EntityID+"; modelMetres="+Binding.GeometryBounds.size+"; dedicatedSelectionBox="+ClickBox.size+
                    "; sourceCarrierRenderersVisible=False; nativeSimulationControlsUnit=True; fowAndSensorsVisibilityGate=True; rhs2V05="+Binding.IsV05+"; visualOnlyModuleTransitions="+Binding.IsV05+"; actualPixelAcceptancePending=True");
            }
        }
        void FitCollider()
        {
            BoxCollider current = View.SelectionCollider;
            if (current == null || current.transform == View.transform || !current.transform.IsChildOf(View.transform) || !current.name.StartsWith("SEL_", StringComparison.Ordinal)) return;
            if (current != ClickBox)
            {
                RestoreCollider(); ClickBox = current; OriginalClickCentre = current.center; OriginalClickSize = current.size;
            }
            Bounds bounds = Binding.PickBounds, local = new Bounds();
            Matrix4x4 matrix=current.transform.worldToLocalMatrix*Binding.GeometryRoot.localToWorldMatrix;
            if(HavePickState&&SameBounds(bounds,LastPickBounds)&&SameMatrix(matrix,LastPickMatrix))
            {
                if((ClickBox.center-LastSelectionBounds.center).sqrMagnitude>.00000001f)ClickBox.center=LastSelectionBounds.center;
                if((ClickBox.size-LastSelectionBounds.size).sqrMagnitude>.00000001f)ClickBox.size=LastSelectionBounds.size;
                return;
            }
            LastPickBounds=bounds;LastPickMatrix=matrix;HavePickState=true;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = bounds.center + new Vector3((corner&1)==0?-bounds.extents.x:bounds.extents.x, (corner&2)==0?-bounds.extents.y:bounds.extents.y, (corner&4)==0?-bounds.extents.z:bounds.extents.z);
                point = matrix.MultiplyPoint3x4(point);
                if (corner == 0) local = new Bounds(point, Vector3.zero); else local.Encapsulate(point);
            }
            LastSelectionBounds=local;
            if((ClickBox.center-local.center).sqrMagnitude>.00000001f)ClickBox.center=local.center;
            if((ClickBox.size-local.size).sqrMagnitude>.00000001f)ClickBox.size=local.size;
        }
        void FitRing()
        {
            GameObject current = NativeRead.Get(View, "mUnderlayObject") as GameObject;
            if (current != Ring)
            {
                RestoreRing(); Ring = current; RingFactor = 0f;
                if (Ring != null)
                {
                    if (View.Settings != null && View.Settings.HUDAttributes != null && Ring == View.Settings.HUDAttributes.UnderlayPrefab)
                        throw new InvalidOperationException("Refusing to scale the shared native underlay prefab");
                    OriginalRingScale = Ring.transform.localScale;
                    bool haveBounds = false; Bounds world = new Bounds();
                    foreach (Renderer renderer in Ring.GetComponentsInChildren<Renderer>(true))
                    { if (!haveBounds) { world=renderer.bounds; haveBounds=true; } else world.Encapsulate(renderer.bounds); }
                    float diameter = Math.Max(world.size.x, world.size.z);
                    if (haveBounds && diameter > .001f) RingFactor = Math.Max(Binding.PickBounds.size.x,Binding.PickBounds.size.z)*1.08f/diameter;
                }
            }
            if(Ring!=null&&RingFactor>0f)
            {Vector3 scale=OriginalRingScale*RingFactor;if((Ring.transform.localScale-scale).sqrMagnitude>.00000001f)Ring.transform.localScale=scale;}
        }
        static bool SameBounds(Bounds left,Bounds right)
        {return (left.center-right.center).sqrMagnitude<.00000001f&&(left.extents-right.extents).sqrMagnitude<.00000001f;}
        static bool SameMatrix(Matrix4x4 left,Matrix4x4 right)
        {for(int index=0;index<16;index++)if(Math.Abs(left[index]-right[index])>.00001f)return false;return true;}
        void FitBoundingSphere()
        {
            if(!HaveSphere){OriginalSphere=View.BoundingSphereRadius;HaveSphere=true;}
            Bounds bounds=Binding.GeometryBounds;
            if(!HaveGeometryState||!SameBounds(bounds,LastGeometryBounds))
            {
                LastGeometryBounds=bounds;HaveGeometryState=true;
                Vector3 min=bounds.min,max=bounds.max;
                DesiredSphere=new Vector3(Math.Max(Math.Abs(min.x),Math.Abs(max.x)),Math.Max(Math.Abs(min.y),Math.Abs(max.y)),Math.Max(Math.Abs(min.z),Math.Abs(max.z))).magnitude;
            }
            if(Math.Abs(View.BoundingSphereRadius-DesiredSphere)>.0001f)NativeRead.Set(View,"<BoundingSphereRadius>k__BackingField",DesiredSphere);
        }
        void RestoreCollider()
        { if (ClickBox != null) { ClickBox.center=OriginalClickCentre; ClickBox.size=OriginalClickSize; } ClickBox=null;HavePickState=false; }
        void RestoreRing()
        { if (Ring != null) Ring.transform.localScale=OriginalRingScale; Ring=null; RingFactor=0f; }
        void AttachThermalVisuals()        {            if(ThermalAttempted||View==null||Binding==null||!Binding.IsV05)return;            ThermalAttempted=true;            try{Thermal=RhodesThermalVisuals.Attach(View,Binding);}            catch(Exception error){ThermalFailure(error);}        }        void ApplyThermalVisuals(bool shown)        {            AttachThermalVisuals();            if(Thermal==null)return;            try{Thermal.Step(shown);}            catch(Exception error){ThermalFailure(error);}        }        void ThermalFailure(Exception error)        {            if(Thermal!=null){try{Thermal.Restore();}catch{}Thermal=null;}            if(ThermalWarning)return;ThermalWarning=true;            Runtime.Log("RHODES_THERMAL_VISUAL_OPTIONAL_DISABLED "+error.GetType().Name+": "+error.Message+"; carrierGeometryRetained=True; nativeThermalSimulationUnchanged=True");        }        void Restore()
        {
            GroundFX=null;
            RestoreCollider(); RestoreRing();            if(Thermal!=null){try{Thermal.Restore();}catch{}Thermal=null;}            ThermalAttempted=ThermalWarning=false;
            if (HaveSphere && View != null) NativeRead.Set(View,"<BoundingSphereRadius>k__BackingField",OriginalSphere);
            BoundEntity=BBI.Core.ComponentModel.Entity.None;
            HaveSphere=false;HaveGeometryState=false;HavePickState=false;NextCameraObservation=0f;Weapons=null;Validated=false;
        }
        void OnDisable() { Restore(); }
        void OnDestroy() { Restore(); }
    }
}
