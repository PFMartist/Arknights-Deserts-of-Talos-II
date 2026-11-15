// Presentation only. Caller supplies the authoritative target and simulation
// age. This component never reads enemies, schedules damage or advances time.
using System;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    public sealed class PerlicaOrbitalEffect : MonoBehaviour
    {
        const int RingSegments = 96;
        const float AfterglowSeconds = .8f;
        static readonly Color Yellow = new Color(1f, .91f, .24f, 1f);
        static readonly Color White = new Color(.93f, .98f, 1f, 1f);
        static readonly Color Blue = new Color(.25f, .68f, 1f, 1f);
        GameObject EffectsRoot;
        Mesh RingMesh, CrossMesh, BeamCoreMesh, BeamGlowMesh, FlashMesh;
        MeshRenderer ExpectedRing, SolvingRing, ImpactRing, Cross, BeamCore, BeamGlow, Flash;
        Material ExpectedMaterial, SolvingMaterial, ImpactMaterial, CrossMaterial, CoreMaterial, GlowMaterial, FlashMaterial;
        Texture2D WhiteTexture, RadialTexture;
        Color[] SolvingColours;
        bool Initialised, Failed;

        // World-ground Y is supplied by the caller. Flat-map prototype: this
        // does not raycast terrain, deform it, or reposition the chosen target.
        public void Render(Vector3 groundTarget, float elapsedSeconds, float chargeSeconds, float fireSeconds, float radius, bool active)
        {
            if (!active || !Finite(groundTarget) || !Finite(elapsedSeconds) || !Finite(chargeSeconds) ||
                !Finite(fireSeconds) || !Finite(radius) || elapsedSeconds < 0f || chargeSeconds <= 0f || fireSeconds <= 0f || radius <= 0f)
            { Hide(); return; }
            if (elapsedSeconds > chargeSeconds + fireSeconds + AfterglowSeconds) { Hide(); return; }
            if (!Initialised && !Failed) Initialise();
            if (!Initialised) return;
            EffectsRoot.SetActive(true);
            EffectsRoot.transform.position = groundTarget;
            EffectsRoot.transform.rotation = Quaternion.identity;
            EffectsRoot.transform.localScale = Vector3.one;
            float phase = Clamp01(elapsedSeconds / chargeSeconds);
            bool charging = elapsedSeconds < chargeSeconds;
            bool firing = !charging && elapsedSeconds < chargeSeconds + fireSeconds;
            float fireAge = Math.Max(0f, elapsedSeconds - chargeSeconds);
            float afterAge = Math.Max(0f, elapsedSeconds - chargeSeconds - fireSeconds);
            float fade = firing || charging ? 1f : 1f - Clamp01(afterAge / AfterglowSeconds);
            float pulse = .5f + .5f * (float)Math.Sin(elapsedSeconds * 13.0);
            float flash = firing ? (float)Math.Exp(-fireAge * 7.5f) : 0f;

            ExpectedRing.transform.localScale = new Vector3(radius, 1f, radius);
            ExpectedRing.enabled = charging;
            Tint(ExpectedMaterial, Yellow, .12f + phase * .08f);
            SolvingRing.transform.localScale = new Vector3(radius, 1f, radius);
            SolvingRing.enabled = charging || firing;
            Tint(SolvingMaterial, charging ? Yellow : White, charging ? .50f + pulse * .18f : .32f);
            UpdateSolvingColours(charging ? phase : 1f);
            Cross.enabled = charging || firing;
            Cross.transform.localRotation = Quaternion.AngleAxis(charging ? -18f + 18f * phase : 0f, Vector3.up);
            Tint(CrossMaterial, charging ? Yellow : White, charging ? .58f + pulse * .22f : .55f);

            BeamCore.enabled = firing; BeamGlow.enabled = firing;
            float beamPulse = .86f + .14f * (float)Math.Sin(fireAge * 49.0);
            Tint(CoreMaterial, White, beamPulse);
            Tint(GlowMaterial, Blue, .28f * beamPulse);
            Flash.enabled = firing || fade > 0f && !charging;
            float flareSize = firing ? 1.8f + flash * radius * .80f : radius * (.24f + (1f - fade) * .12f);
            Flash.transform.localScale = new Vector3(flareSize, 1f, flareSize);
            Tint(FlashMaterial, firing ? White : Blue, firing ? .23f + flash * .47f : .17f * fade * fade);
            ImpactRing.enabled = !charging && fireAge < .65f;
            float expansion = Clamp01(fireAge / .65f);
            float shockRadius = radius * (.16f + expansion * .92f);
            ImpactRing.transform.localScale = new Vector3(shockRadius, 1f, shockRadius);
            Tint(ImpactMaterial, White, .72f * (1f - expansion) * (1f - expansion));
        }

        void Initialise()
        {
            // An opaque Unlit/Color fallback would turn the impact glow into a
            // solid rectangle and ignore the progressive arc's alpha mask.
            Shader shader = Supported("Particles/Additive") ?? Supported("Sprites/Default") ?? Supported("Unlit/Transparent");
            if (shader == null)
            {
                Failed = true;
                Debug.LogWarning("EF_PerlicaOrbitalEffect: no supported built-in shader; skill simulation is unaffected.");
                return;
            }
            WhiteTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            WhiteTexture.name = "EF_Orbital_White"; WhiteTexture.hideFlags = HideFlags.HideAndDontSave;
            WhiteTexture.SetPixel(0, 0, Color.white); WhiteTexture.Apply();
            RadialTexture = MakeRadialTexture();
            RingMesh = BuildRing(); CrossMesh = BuildCross();
            BeamCoreMesh = BuildBeam(.35f); BeamGlowMesh = BuildBeam(1.2f); FlashMesh = BuildFlash();
            EffectsRoot = new GameObject("EF_Perlica_OrbitalPresentation_World");
            EffectsRoot.hideFlags = HideFlags.DontSave;
            // Keep independent world metres if this host is parented to a model.
            EffectsRoot.transform.parent = null;
            int layer = LayerMask.NameToLayer("Default"); EffectsRoot.layer = layer < 0 ? 0 : layer;
            ExpectedMaterial = MaterialFor(shader, WhiteTexture, "EstimatedRadius");
            SolvingMaterial = MaterialFor(shader, WhiteTexture, "SolvingRadius");
            ImpactMaterial = MaterialFor(shader, WhiteTexture, "ImpactRing");
            CrossMaterial = MaterialFor(shader, WhiteTexture, "GroundTarget");
            CoreMaterial = MaterialFor(shader, WhiteTexture, "BeamCore");
            GlowMaterial = MaterialFor(shader, WhiteTexture, "BeamGlow");
            FlashMaterial = MaterialFor(shader, RadialTexture, "ImpactGlow");
            ExpectedRing = Child("EstimatedRadius", RingMesh, ExpectedMaterial, .12f);
            // Separate mesh colours reveal the calculation arc without editing
            // the faint complete radius used by the other two ring renderers.
            Mesh solving = BuildRing(); SolvingColours = solving.colors;
            SolvingRing = Child("SolvingRadius", solving, SolvingMaterial, .14f);
            ImpactRing = Child("ImpactWave", RingMesh, ImpactMaterial, .16f);
            Cross = Child("GroundTarget", CrossMesh, CrossMaterial, .18f);
            BeamGlow = Child("BeamGlow", BeamGlowMesh, GlowMaterial, .14f);
            BeamCore = Child("BeamCore", BeamCoreMesh, CoreMaterial, .14f);
            Flash = Child("ImpactGlow", FlashMesh, FlashMaterial, .20f);
            Initialised = true; Hide();
        }
        static Shader Supported(string name) { Shader result = Shader.Find(name); return result != null && result.isSupported ? result : null; }
        MeshRenderer Child(string name, Mesh mesh, Material material, float groundLift)
        {
            GameObject child = new GameObject("EF_Orbital_" + name); child.layer = EffectsRoot.layer;
            child.transform.parent = EffectsRoot.transform;
            child.transform.localPosition = new Vector3(0f, groundLift, 0f);
            child.transform.localRotation = Quaternion.identity; child.transform.localScale = Vector3.one;
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.receiveShadows = false; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.enabled = false; return renderer;
        }
        static Material MaterialFor(Shader shader, Texture2D texture, string label)
        {
            Material material = new Material(shader); material.name = "EF_Orbital_" + label;
            material.hideFlags = HideFlags.HideAndDontSave;
            if (material.HasProperty("_MainTex")) material.mainTexture = texture;
            material.renderQueue = 3110;
            if (material.HasProperty("_Cull")) material.SetInt("_Cull", 0);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
            if (material.HasProperty("_ZTest")) material.SetInt("_ZTest", 4);
            return material;
        }
        static void Tint(Material material, Color colour, float alpha)
        {
            colour.a = Clamp01(alpha);
            // Legacy Additive uses _TintColor, Sprites/Unlit use _Color.
            if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", colour);
            if (material.HasProperty("_Color")) material.SetColor("_Color", colour);
        }
        void UpdateSolvingColours(float progress)
        {
            int revealed = (int)Math.Ceiling(Clamp01(progress) * RingSegments);
            for (int segment = 0; segment < RingSegments; segment++)
            {
                // Four short gaps keep a measurement / rangefinder character.
                float alpha = segment < revealed && segment % 24 < 21 ? 1f : 0f;
                for (int corner = 0; corner < 4; corner++) SolvingColours[segment * 4 + corner] = new Color(1f, 1f, 1f, alpha);
            }
            SolvingRing.GetComponent<MeshFilter>().sharedMesh.colors = SolvingColours;
        }
        static Mesh BuildRing()
        {
            Vector3[] points; int[] triangles; Vector2[] uv; Color[] colours;
            RingBuffers(out points, out triangles, out uv, out colours);
            return MeshFor("EF_Orbital_UnitRingXZ", points, triangles, uv, colours);
        }
        // Managed-only geometry helpers can be checked without Unity objects.
        static void RingBuffers(out Vector3[] points, out int[] triangles, out Vector2[] uv, out Color[] colours)
        {
            points = new Vector3[RingSegments * 4]; triangles = new int[RingSegments * 12];
            uv = new Vector2[points.Length]; colours = new Color[points.Length];
            for (int i = 0; i < RingSegments; i++)
            {
                double a = i * Math.PI * 2 / RingSegments, b = (i + 1) * Math.PI * 2 / RingSegments;
                points[i * 4] = Polar(1f, a); points[i * 4 + 1] = Polar(1f, b);
                points[i * 4 + 2] = Polar(.986f, b); points[i * 4 + 3] = Polar(.986f, a);
                QuadIndices(triangles, i * 12, i * 4);
            }
            for (int i = 0; i < points.Length; i++) { uv[i] = new Vector2(.5f, .5f); colours[i] = Color.white; }
        }
        static Mesh BuildCross()
        {
            Vector3[] vertices = new Vector3[16]; int[] indices = new int[48];
            for (int i = 0; i < 4; i++)
            {
                double a = (i * 90.0 + 45.0) * Math.PI / 180.0;
                Vector3 along = Polar(1f, a), across = Polar(.10f, a + Math.PI * .5);
                vertices[i * 4] = along * .35f - across; vertices[i * 4 + 1] = along * 1.35f - across;
                vertices[i * 4 + 2] = along * 1.35f + across; vertices[i * 4 + 3] = along * .35f + across;
                QuadIndices(indices, i * 12, i * 4);
            }
            return MeshFor("EF_Orbital_ImpactReticleXZ", vertices, indices, FlatUV(vertices.Length), WhiteColours(vertices.Length));
        }
        static Mesh BuildBeam(float width)
        {
            Vector3[] vertices; int[] indices; Vector2[] uv; Color[] colours;
            BeamBuffers(width, out vertices, out indices, out uv, out colours);
            return MeshFor("EF_Orbital_Beam_" + width, vertices, indices, uv, colours);
        }
        static void BeamBuffers(float width, out Vector3[] vertices, out int[] indices, out Vector2[] uv, out Color[] colours)
        {
            if (!Finite(width) || width <= 0f) throw new ArgumentException("Invalid orbital beam width");
            Vector3 top = new Vector3(28f, 160f, -14f);
            Vector3 direction = Normal(top), a = Normal(new Vector3(direction.y, -direction.x, 0f)) * width * .5f;
            Vector3 b = Normal(CrossProduct(direction, a)) * width * .5f;
            vertices = new Vector3[] { -a, a, top + a, top - a, -b, b, top + b, top - b };
            indices = new int[24]; QuadIndices(indices, 0, 0); QuadIndices(indices, 12, 4);
            uv = FlatUV(vertices.Length); colours = WhiteColours(vertices.Length);
        }
        static Mesh BuildFlash()
        {
            Vector3[] vertices = new Vector3[] { new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f), new Vector3(1f, 0f, 1f), new Vector3(-1f, 0f, 1f) };
            int[] indices = new int[12]; QuadIndices(indices, 0, 0);
            Vector2[] uv = new Vector2[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            return MeshFor("EF_Orbital_ImpactGlowXZ", vertices, indices, uv, WhiteColours(4));
        }
        static Mesh MeshFor(string label, Vector3[] vertices, int[] indices, Vector2[] uv, Color[] colours)
        {
            Mesh mesh = new Mesh(); mesh.name = label; mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.vertices = vertices; mesh.triangles = indices; mesh.uv = uv; mesh.colors = colours;
            mesh.RecalculateBounds(); return mesh;
        }
        static Texture2D MakeRadialTexture()
        {
            const int size = 32;
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            texture.name = "EF_Orbital_Radial"; texture.hideFlags = HideFlags.HideAndDontSave; texture.wrapMode = TextureWrapMode.Clamp;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float dx = (x + .5f) / size * 2f - 1f, dy = (y + .5f) / size * 2f - 1f;
                float a = Clamp01(1f - dx * dx - dy * dy); pixels[y * size + x] = new Color(1f, 1f, 1f, a * a);
            }
            texture.SetPixels(pixels); texture.Apply(false, true); return texture;
        }
        static Vector3 Polar(float radius, double angle) { return new Vector3((float)Math.Sin(angle) * radius, 0f, (float)Math.Cos(angle) * radius); }
        static Vector3 Normal(Vector3 v) { float length = (float)Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z); return v / length; }
        static Vector3 CrossProduct(Vector3 a, Vector3 b) { return new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
        static void QuadIndices(int[] destination, int offset, int vertex)
        { int[] pattern = new int[] { 0, 1, 2, 0, 2, 3, 2, 1, 0, 3, 2, 0 }; for (int i = 0; i < pattern.Length; i++) destination[offset + i] = vertex + pattern[i]; }
        static Vector2[] FlatUV(int length) { Vector2[] result = new Vector2[length]; for (int i = 0; i < length; i++) result[i] = new Vector2(.5f, .5f); return result; }
        static Color[] WhiteColours(int length) { Color[] result = new Color[length]; for (int i = 0; i < length; i++) result[i] = Color.white; return result; }
        static float Clamp01(float value) { return Math.Max(0f, Math.Min(1f, value)); }
        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        static bool Finite(Vector3 value) { return Finite(value.x) && Finite(value.y) && Finite(value.z); }
        void Hide() { if (EffectsRoot != null) EffectsRoot.SetActive(false); }
        void OnDisable() { Hide(); }
        void OnDestroy()
        {
            if (SolvingRing != null) Destroy(SolvingRing.GetComponent<MeshFilter>().sharedMesh);
            if (EffectsRoot != null) { EffectsRoot.SetActive(false); Destroy(EffectsRoot); }
            foreach (Mesh mesh in new Mesh[] { RingMesh, CrossMesh, BeamCoreMesh, BeamGlowMesh, FlashMesh }) if (mesh != null) Destroy(mesh);
            foreach (Material material in new Material[] { ExpectedMaterial, SolvingMaterial, ImpactMaterial, CrossMaterial, CoreMaterial, GlowMaterial, FlashMaterial }) if (material != null) Destroy(material);
            if (WhiteTexture != null) Destroy(WhiteTexture); if (RadialTexture != null) Destroy(RadialTexture);
            Initialised = false; EffectsRoot = null;
        }
    }
}
