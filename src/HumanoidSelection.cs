// Per-instance selection presentation and click-volume adaptation for humanoids.
// Only UnitView.SelectionCollider (native SEL_ BoxCollider) is resized. No BVM
// rig, shared atlas, native prefab, physical body or sensor collider is touched.
using System;
using System.Collections.Generic;
using BBI.Unity.Game.Data;
using BBI.Unity.Game.World;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public static class HumanoidSelection
    {
        static float NextScan;
        public static void MainTick()
        {
            if (Time.realtimeSinceStartup < NextScan) return;
            NextScan = Time.realtimeSinceStartup + .25f;
            foreach (UnitView view in UnitViewDiscovery.Get())
                if (view != null && view.gameObject.activeInHierarchy && view.SimStateInterpolator != null) Ensure(view);
        }
        public static void Ensure(UnitView view)
        {
            if (view == null || !view.gameObject.activeInHierarchy || view.SimStateInterpolator == null) return;
            HumanoidBinding binding = view.GetComponent<HumanoidBinding>();
            if (binding == null || binding.PresentationRoot == null || binding.MemberRoots == null || binding.MemberRoots.Length == 0) return;
            HumanoidSelectionView driver = view.GetComponent<HumanoidSelectionView>();
            if (driver == null) driver = view.gameObject.AddComponent<HumanoidSelectionView>();
            driver.Initialize(view, binding);
        }
        public static void Reset()
        {
            NextScan = 0f;
            foreach (HumanoidSelectionView driver in Resources.FindObjectsOfTypeAll(typeof(HumanoidSelectionView)))
                if (driver != null) UnityEngine.Object.DestroyImmediate(driver);
        }

        // Pure geometry builder: XZ plane, double winding, quadrant gaps.
        // Public so an offline fixture can check buffers without Unity objects.
        public static void BuildRingBuffers(float diameter, float width, out Vector3[] vertices, out int[] indices, out Vector2[] uv)
        {
            if (float.IsNaN(diameter) || float.IsInfinity(diameter) || diameter <= .5f ||
                float.IsNaN(width) || float.IsInfinity(width) || width <= 0f || width >= diameter * .5f)
                throw new ArgumentException("Invalid humanoid ring dimensions");
            List<Vector3> points = new List<Vector3>(); List<int> triangles = new List<int>();
            float outer = diameter * .5f, inner = outer - width;
            for (int quadrant = 0; quadrant < 4; quadrant++)
                for (int segment = 0; segment < 24; segment++)
                {
                    double a = (quadrant * 90.0 + 4.0 + segment * 82.0 / 24.0) * Math.PI / 180.0;
                    double b = (quadrant * 90.0 + 4.0 + (segment + 1) * 82.0 / 24.0) * Math.PI / 180.0;
                    int start = points.Count;
                    points.Add(Polar(outer, a)); points.Add(Polar(outer, b)); points.Add(Polar(inner, b)); points.Add(Polar(inner, a));
                    foreach (int index in new int[] { 0, 1, 2, 0, 2, 3, 2, 1, 0, 3, 2, 0 }) triangles.Add(start + index);
                }
            vertices = points.ToArray(); indices = triangles.ToArray(); uv = new Vector2[vertices.Length];
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(.5f, .5f);
        }
        static Vector3 Polar(float radius, double angle) { return new Vector3((float)Math.Sin(angle) * radius, 0f, (float)Math.Cos(angle) * radius); }

        public sealed class HumanoidSelectionView : MonoBehaviour
        {
            UnitView View;
            HumanoidBinding Binding;
            GameObject NativeRing, OwnRing;
            Renderer[] NativeRenderers = new Renderer[0];
            bool[] NativeEnabled = new bool[0];
            BoxCollider ClickBox;
            Vector3 OriginalCenter, OriginalSize;
            Mesh RingMesh;
            MeshRenderer RingRenderer;
            Material RingMaterial;
            Texture2D WhiteTexture;
            float Diameter, RenderDiameter, RenderBand, MetresPerPixel;
            bool SpriteShader, ColliderReported, RejectionReported, HaveVisibleState, LastVisible;
            int Reports;
            const float GroundLift = .10f;

            public void Initialize(UnitView view, HumanoidBinding binding)
            {
                if (View != null && (View != view || Binding != binding)) { RestoreNative(); RestoreCollider(); DestroyOwn(); }
                View = view; Binding = binding; Diameter = binding.MemberCount > 1 ? 4.8f : 1.4f;
            }
            void LateUpdate()
            {
                if (View == null || Binding == null || Binding.PresentationRoot == null || View.SimStateInterpolator == null)
                { RestoreNative(); RestoreCollider(); if (RingRenderer != null) RingRenderer.enabled = false; return; }
                ResizeSelectionCollider();
                GameObject current = NativeRead.Get(View, "mUnderlayObject") as GameObject;
                if (current != NativeRing)
                {
                    RestoreNative(); NativeRing = current;
                    if (NativeRing != null)
                    {
                        // This must be the spawned underlay instance, never the
                        // HUD prefab. Unity object equality detects accidental
                        // asset references before we hide any Renderer.
                        UnitHUDAttributes hud = View.Settings.HUDAttributes;
                        if (hud != null && current == hud.UnderlayPrefab)
                            throw new InvalidOperationException("Refusing to hide a shared humanoid underlay prefab");
                        NativeRenderers = current.GetComponentsInChildren<Renderer>(true); NativeEnabled = new bool[NativeRenderers.Length];
                        for (int i = 0; i < NativeRenderers.Length; i++) NativeEnabled[i] = NativeRenderers[i] != null && NativeRenderers[i].enabled;
                    }
                }
                if (OwnRing == null) CreateOwn();
                for (int i = 0; i < NativeRenderers.Length; i++) if (NativeRenderers[i] != null) NativeRenderers[i].enabled = false;
                Transform root = Binding.PresentationRoot;
                Vector3 position = root.position; position.y += GroundLift; OwnRing.transform.position = position;
                OwnRing.transform.rotation = Quaternion.AngleAxis(root.eulerAngles.y, Vector3.up);
                Vector3 parentScale = OwnRing.transform.parent.lossyScale;
                OwnRing.transform.localScale = new Vector3(1f / Nonzero(parentScale.x), 1f / Nonzero(parentScale.y), 1f / Nonzero(parentScale.z));
                object fade = NativeRead.Get(View, "mFOWFade");
                float visibility = fade == null ? 0f : Mathf.Clamp01(Convert.ToSingle(NativeRead.Get(fade, "Value")));
                bool nativeActive = NativeRing == null || View.UnderlayActive;
                bool shown = View.Selected && nativeActive && View.gameObject.activeInHierarchy && visibility > .001f && !UserCamera.IsSensorsModeActive;
                FitProjectedBand();
                Color color = SelectionColor(); color.a = (SpriteShader ? .78f : 1f) * visibility; RingMaterial.color = color; RingRenderer.enabled = shown;
                if (!HaveVisibleState || shown != LastVisible || Reports < 2)
                {
                    Runtime.Log("HUMANOID_SELECTION_STATE entity=" + View.EntityID + "; members=" + Binding.MemberCount +
                        "; selected=" + View.Selected + "; shown=" + shown + "; fowFade=" + visibility + "; baseDiameter=" + Diameter +
                        "; renderDiameter=" + RenderDiameter + "; bandMetres=" + RenderBand + "; bandPixels=" + (MetresPerPixel > .000001f ? RenderBand / MetresPerPixel : 0f) +
                        "; nativeInstanceRenderersHidden=" + NativeRenderers.Length + "; clickBoxVerified=" + (ClickBox != null) +
                        "; nativeSharedAssetsUntouched=True; actualMouseSelectionAndPixelAcceptancePending=True");
                    HaveVisibleState = true; LastVisible = shown; Reports++;
                }
            }

            void ResizeSelectionCollider()
            {
                // Verified build 12339551 contract: SelectionCollider returns the
                // mSelectionCollider BoxCollider, built/found with SEL_ prefix.
                // Do not enumerate and resize unrelated physics/sensor colliders.
                BoxCollider current = View.SelectionCollider;
                object cached = NativeRead.Get(View, "mSelectionCollider");
                if (current == null || !object.ReferenceEquals(current, cached) || current.transform == View.transform ||
                    !current.transform.IsChildOf(View.transform) || !current.name.StartsWith("SEL_", StringComparison.Ordinal))
                {
                    if (!RejectionReported) Runtime.Log("HUMANOID_SELECTION_COLLIDER_REJECTED entity=" + View.EntityID + "; nativeDedicatedSELBoxNotConfirmed=True; unrelatedCollidersUntouched=True");
                    RejectionReported = true; return;
                }
                if (current != ClickBox)
                {
                    RestoreCollider(); ClickBox = current; OriginalCenter = current.center; OriginalSize = current.size; ColliderReported = false;
                }
                // Fit each visible member's nominal standing envelope, including
                // all six 3x2 member origins. Convert all world corners to the
                // dedicated box's LOCAL frame; its native transform is retained.
                bool squad = Binding.MemberCount > 1, haveBounds = false;
                Vector3 personSize = squad ? new Vector3(1.1f, 1.85f, 1.1f) : new Vector3(1f, 1.75f, 1f);
                Bounds local = new Bounds();
                for (int memberIndex=0;memberIndex<Binding.MemberRoots.Length;memberIndex++)
                {
                    if(Binding.AliveMembers!=null&&!Binding.AliveMembers[memberIndex])continue;
                    Transform member=Binding.MemberRoots[memberIndex];
                    if (member == null) continue;
                    // Rig scale and formation origin are already authored in
                    // world metres by HumanoidDriver. Do not scale the collider
                    // itself or modify native NLIPS, body or child transforms.
                    Vector3 foot = member.position;
                    Quaternion yaw = Quaternion.AngleAxis(Binding.PresentationRoot.eulerAngles.y, Vector3.up);
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 offset = new Vector3((i & 1) == 0 ? -personSize.x * .5f : personSize.x * .5f,
                            (i & 2) == 0 ? 0f : personSize.y, (i & 4) == 0 ? -personSize.z * .5f : personSize.z * .5f);
                        Vector3 point = current.transform.InverseTransformPoint(foot + yaw * offset);
                        if (!haveBounds) { local = new Bounds(point, Vector3.zero); haveBounds = true; } else local.Encapsulate(point);
                    }
                }
                if (!haveBounds) return;
                if (!Finite(local.center) || !Finite(local.size) || local.size.x <= 0f || local.size.y <= 0f || local.size.z <= 0f)
                    throw new InvalidOperationException("Invalid humanoid selection box transform");
                current.center = local.center; current.size = local.size;
                if (!ColliderReported)
                {
                    Runtime.Log("HUMANOID_SELECTION_COLLIDER_FITTED entity=" + View.EntityID + "; source=UnitView.SelectionCollider/mSelectionCollider" +
                        "; type=BoxCollider; prefix=SEL_; componentID=" + current.GetInstanceID() + "; members=" + Binding.MemberCount +
                        "; originalLocalSize=" + OriginalSize + "; newLocalSize=" + current.size + "; newLocalCenter=" + current.center +
                        "; standingEnvelopeMetres=" + personSize + "; allMemberOriginsIncluded=True; transformLayerAndTriggerUnchanged=True" +
                        "; physicsAndSensorCollidersUntouched=True; actualMouseSelectionPending=True");
                    ColliderReported = true;
                }
            }

            void CreateOwn()
            {
                Shader shader = Shader.Find("Sprites/Default"); SpriteShader = shader != null && shader.isSupported;
                if (!SpriteShader) { shader = Shader.Find("Unlit/Color"); if (shader == null || !shader.isSupported) throw new InvalidOperationException("No supported humanoid selection-ring shader"); }
                RingMesh = new Mesh(); RingMesh.name = "EF_Humanoid_SelectionRing_XZ"; RingMesh.hideFlags = HideFlags.HideAndDontSave;
                UpdateBuffers(Diameter, BaseBand());
                WhiteTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false); WhiteTexture.name = "EF_Humanoid_SelectionWhite";
                WhiteTexture.hideFlags = HideFlags.HideAndDontSave; WhiteTexture.SetPixel(0, 0, Color.white); WhiteTexture.Apply();
                RingMaterial = new Material(shader); RingMaterial.name = "EF_Humanoid_SelectionMaterial_" + View.GetInstanceID(); RingMaterial.hideFlags = HideFlags.HideAndDontSave;
                if (RingMaterial.HasProperty("_MainTex")) RingMaterial.mainTexture = WhiteTexture;
                RingMaterial.renderQueue = 3200; RingMaterial.color = SelectionColor();
                if (RingMaterial.HasProperty("_Cull")) RingMaterial.SetInt("_Cull", 0);
                if (RingMaterial.HasProperty("_ZWrite")) RingMaterial.SetInt("_ZWrite", 0);
                if (RingMaterial.HasProperty("_ZTest")) RingMaterial.SetInt("_ZTest", 4);
                OwnRing = new GameObject("EF_Humanoid_SelectionRing_ExplicitXZ"); OwnRing.layer = LayerMask.NameToLayer("Default");
                OwnRing.transform.parent = View.transform; OwnRing.transform.localPosition = Vector3.zero; OwnRing.transform.localRotation = Quaternion.identity; OwnRing.transform.localScale = Vector3.one;
                OwnRing.AddComponent<MeshFilter>().sharedMesh = RingMesh; RingRenderer = OwnRing.AddComponent<MeshRenderer>();
                RingRenderer.sharedMaterial = RingMaterial; RingRenderer.enabled = false; RingRenderer.receiveShadows = false;
                RingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Runtime.Log("HUMANOID_SELECTION_RING_CREATED entity=" + View.EntityID + "; members=" + Binding.MemberCount + "; diameter=" + Diameter +
                    "; shader=" + shader.name + "; explicitXZ=True; doubleWindings=True; viewInstanceOnly=True; adaptiveMinDiameterPixels=8; adaptiveMinBandPixels=1.15");
            }
            void FitProjectedBand()
            {
                object hud = View.Settings.HUDSystem, camera = hud == null ? null : NativeRead.Get(hud, "ActiveUserCamera");
                MetresPerPixel = camera == null ? 0f : Convert.ToSingle(NativeRead.Get(camera, "MetersPerPixel"));
                if (float.IsNaN(MetresPerPixel) || float.IsInfinity(MetresPerPixel) || MetresPerPixel < 0f) MetresPerPixel = 0f;
                float diameter = Math.Max(Diameter, MetresPerPixel * 8f), band = Math.Max(BaseBand(), MetresPerPixel * 1.15f);
                if (Math.Abs(diameter - RenderDiameter) > Math.Max(.001f, RenderDiameter * .06f) || Math.Abs(band - RenderBand) > Math.Max(.001f, RenderBand * .06f)) UpdateBuffers(diameter, band);
            }
            float BaseBand() { return Binding.MemberCount > 1 ? .13f : .075f; }
            void UpdateBuffers(float diameter, float band)
            {
                Vector3[] vertices; int[] indices; Vector2[] uv; BuildRingBuffers(diameter, band, out vertices, out indices, out uv);
                RingMesh.vertices = vertices; RingMesh.triangles = indices; RingMesh.uv = uv;
                Vector3[] normals = new Vector3[vertices.Length]; Color[] colors = new Color[vertices.Length];
                for (int i = 0; i < vertices.Length; i++) { normals[i] = Vector3.up; colors[i] = Color.white; }
                RingMesh.normals = normals; RingMesh.colors = colors; RingMesh.RecalculateBounds(); Bounds bounds = RingMesh.bounds; bounds.Expand(.03f); RingMesh.bounds = bounds;
                RenderDiameter = diameter; RenderBand = band;
            }
            Color SelectionColor()
            {
                UnitHUDInteractionAttributes.IconColourings colors = NativeRead.Get(View, "mIconColours") as UnitHUDInteractionAttributes.IconColourings;
                if (colors != null) { Color value = colors.SelectedColour; if (Math.Max(value.r, Math.Max(value.g, value.b)) > .05f) return value; }
                return new Color(.3f, 1f, .48f, 1f);
            }
            static float Nonzero(float value) { return Math.Max(.00001f, Math.Abs(value)); }
            static bool Finite(Vector3 value) { return !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z)); }
            void RestoreCollider()
            { if (ClickBox != null) { ClickBox.center = OriginalCenter; ClickBox.size = OriginalSize; } ClickBox = null; ColliderReported = false; }
            void RestoreNative()
            { for (int i = 0; i < NativeRenderers.Length; i++) if (NativeRenderers[i] != null) NativeRenderers[i].enabled = NativeEnabled[i]; NativeRing = null; NativeRenderers = new Renderer[0]; NativeEnabled = new bool[0]; HaveVisibleState = false; }
            void DestroyOwn()
            {
                if (OwnRing != null) UnityEngine.Object.DestroyImmediate(OwnRing); OwnRing = null; RingRenderer = null;
                if (RingMesh != null) UnityEngine.Object.DestroyImmediate(RingMesh); RingMesh = null;
                if (RingMaterial != null) UnityEngine.Object.DestroyImmediate(RingMaterial); RingMaterial = null;
                if (WhiteTexture != null) UnityEngine.Object.DestroyImmediate(WhiteTexture); WhiteTexture = null;
            }
            void OnDisable() { RestoreNative(); RestoreCollider(); if (RingRenderer != null) RingRenderer.enabled = false; }
            void OnDestroy() { RestoreNative(); RestoreCollider(); DestroyOwn(); View = null; Binding = null; }
        }
    }
}
