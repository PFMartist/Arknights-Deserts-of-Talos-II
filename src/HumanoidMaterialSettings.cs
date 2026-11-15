using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace EndfieldDoK.MapUnitLab
{
    internal sealed class HumanoidMaterialPolicy
    {
        internal string Mode = "native";
        internal float Cutoff = .35f;
    }
    internal static class HumanoidMaterialSettings
    {
        internal static Dictionary<string, HumanoidMaterialPolicy> Load(string folder)
        {
            Dictionary<string, HumanoidMaterialPolicy> result = new Dictionary<string, HumanoidMaterialPolicy>(StringComparer.Ordinal);
            string file = Path.Combine(folder, "humanoid_materials.ini");
            if (!File.Exists(file)) return result;
            foreach (string source in File.ReadAllLines(file))
            {
                string line = source.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("[")) continue;
                int split = line.IndexOf('='); if (split <= 0) throw new InvalidDataException("Malformed humanoid material setting");
                string key = line.Substring(0, split).Trim(), value = line.Substring(split + 1).Trim();
                bool cutoff = key.EndsWith("|cutoff", StringComparison.Ordinal);
                if (cutoff) key = key.Substring(0, key.Length - 7);
                HumanoidMaterialPolicy policy;
                if (!result.TryGetValue(key, out policy)) { policy = new HumanoidMaterialPolicy(); result.Add(key, policy); }
                if (cutoff)
                {
                    policy.Cutoff = float.Parse(value, CultureInfo.InvariantCulture);
                    if (float.IsNaN(policy.Cutoff) || float.IsInfinity(policy.Cutoff) || policy.Cutoff < 0f || policy.Cutoff > 1f)
                        throw new InvalidDataException("Invalid humanoid alpha cutoff");
                }
                else
                {
                    if (value != "native" && value != "opaque" && value != "cutout" && value != "alpha" && value != "multiply")
                        throw new InvalidDataException("Invalid humanoid material mode");
                    policy.Mode = value;
                }
            }
            return result;
        }

        internal static void Apply(Material material, HumanoidMaterialPolicy policy, Color tint)
        {
            if (policy == null || policy.Mode == "native" || policy.Mode == "opaque") return;
            if (policy.Mode == "multiply")
            {
                Texture map = material.mainTexture;
                Shader multiply = Shader.Find("Particles/Multiply") ?? Shader.Find("Legacy Shaders/Particles/Multiply");
                if (multiply != null && multiply.isSupported)
                {
                    material.shader = multiply; material.mainTexture = map;
                    if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
                    if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", tint);
                }
                else
                {
                    Shader fallback = Shader.Find("Standard");
                    if (fallback == null || !fallback.isSupported) throw new InvalidDataException("No supported humanoid overlay fallback shader");
                    material.shader = fallback; material.mainTexture = Texture2D.blackTexture; material.color = Color.black;
                    material.SetTexture("_EmissionMap", map); material.SetColor("_EmissionColor", tint); material.EnableKeyword("_EMISSION");
                    material.SetFloat("_Metallic", 0f); material.SetFloat("_Glossiness", 0f);
                    material.SetFloat("_Mode", 2f); material.EnableKeyword("_ALPHABLEND_ON");
                    material.SetInt("_SrcBlend", 0); material.SetInt("_DstBlend", 3); material.SetInt("_ZWrite", 0);
                    Runtime.Log("HUMANOID_OVERLAY_SHADER_FALLBACK material=" + material.name + "; StandardEmissionMultiplyApproximation=True; sourceBlend=Zero_SrcColor; actualPixelAcceptancePending=True");
                }
                material.renderQueue = 3100; return;
            }
            Shader standard = Shader.Find("Standard");
            if (standard == null || !standard.isSupported) throw new InvalidDataException("Humanoid alpha material requires native Standard shader");
            Texture baseMap = material.mainTexture;
            material.shader = standard; material.mainTexture = baseMap; material.color = tint;
            material.mainTextureScale = Vector2.one; material.mainTextureOffset = Vector2.zero;
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .15f);
            material.DisableKeyword("_ALPHATEST_ON"); material.DisableKeyword("_ALPHABLEND_ON"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            if (policy.Mode == "cutout")
            {
                material.SetFloat("_Mode", 1f); material.SetFloat("_Cutoff", policy.Cutoff);
                material.SetInt("_SrcBlend", 1); material.SetInt("_DstBlend", 0); material.SetInt("_ZWrite", 1);
                material.EnableKeyword("_ALPHATEST_ON"); material.renderQueue = 2450;
            }
            else
            {
                material.SetFloat("_Mode", 2f); material.SetInt("_SrcBlend", 5); material.SetInt("_DstBlend", 10); material.SetInt("_ZWrite", 0);
                material.EnableKeyword("_ALPHABLEND_ON"); material.renderQueue = 3000;
            }
        }
    }
}
