using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DwarfSimulator.Editor
{
    /// <summary>
    /// Converts the legacy Built-in materials shipped with Low Poly Modular Terrain Pack to URP/Lit.
    /// The conversion is intentionally limited to this asset pack.
    /// </summary>
    public static class LowPolyModularTerrainUrpConverter
    {
        private const string PackRoot = "Assets/Packages/Low Poly Modular Terrain Pack";
        private const string UrpLitShaderName = "Universal Render Pipeline/Lit";

        [MenuItem("Tools/Dwarf Simulator/Convert Low Poly Modular Terrain Pack to URP")]
        public static void ConvertMaterials()
        {
            Shader urpLit = Shader.Find(UrpLitShaderName);
            if (urpLit == null)
            {
                throw new InvalidOperationException(
                    $"Shader '{UrpLitShaderName}' is unavailable. Make sure URP is installed before converting the pack.");
            }

            string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { PackRoot });
            int convertedCount = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (string guid in materialGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

                    if (material == null || material.shader == urpLit)
                    {
                        continue;
                    }

                    // Capture the legacy Standard values before swapping shaders.
                    Texture baseMap = GetTexture(material, "_MainTex");
                    Vector2 textureScale = GetTextureScale(material, "_MainTex");
                    Vector2 textureOffset = GetTextureOffset(material, "_MainTex");
                    Color baseColor = GetColor(material, "_Color", Color.white);
                    float metallic = GetFloat(material, "_Metallic", 0f);
                    float smoothness = GetFloat(material, "_Glossiness", 0.5f);
                    bool isTransparent = GetFloat(material, "_Mode", 0f) >= 2f;

                    material.shader = urpLit;

                    material.SetTexture("_BaseMap", baseMap);
                    material.SetTextureScale("_BaseMap", textureScale);
                    material.SetTextureOffset("_BaseMap", textureOffset);
                    material.SetColor("_BaseColor", baseColor);
                    material.SetColor("_Color", baseColor);
                    material.SetFloat("_Metallic", metallic);
                    material.SetFloat("_Smoothness", smoothness);
                    material.SetFloat("_WorkflowMode", 1f);
                    material.SetFloat("_AlphaClip", 0f);
                    material.SetFloat("_Cull", (float)CullMode.Back);
                    material.SetFloat("_QueueOffset", 0f);

                    ConfigureSurface(material, isTransparent);

                    EditorUtility.SetDirty(material);
                    convertedCount++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Converted {convertedCount} Low Poly Modular Terrain Pack material(s) to URP/Lit.");
        }

        private static void ConfigureSurface(Material material, bool isTransparent)
        {
            material.DisableKeyword("_ALPHATEST_ON");

            if (isTransparent)
            {
                // The legacy materials use Standard's Transparent (premultiplied) mode.
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 1f);
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
                material.SetShaderPassEnabled("DepthOnly", false);
            }
            else
            {
                material.SetFloat("_Surface", 0f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)BlendMode.Zero);
                material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                material.SetFloat("_DstBlendAlpha", (float)BlendMode.Zero);
                material.SetFloat("_ZWrite", 1f);
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Opaque");
                material.renderQueue = -1;
                material.SetShaderPassEnabled("DepthOnly", true);
            }
        }

        private static Texture GetTexture(Material material, string propertyName)
        {
            return material.HasProperty(propertyName) ? material.GetTexture(propertyName) : null;
        }

        private static Vector2 GetTextureScale(Material material, string propertyName)
        {
            return material.HasProperty(propertyName) ? material.GetTextureScale(propertyName) : Vector2.one;
        }

        private static Vector2 GetTextureOffset(Material material, string propertyName)
        {
            return material.HasProperty(propertyName) ? material.GetTextureOffset(propertyName) : Vector2.zero;
        }

        private static Color GetColor(Material material, string propertyName, Color fallback)
        {
            return material.HasProperty(propertyName) ? material.GetColor(propertyName) : fallback;
        }

        private static float GetFloat(Material material, string propertyName, float fallback)
        {
            return material.HasProperty(propertyName) ? material.GetFloat(propertyName) : fallback;
        }
    }
}
