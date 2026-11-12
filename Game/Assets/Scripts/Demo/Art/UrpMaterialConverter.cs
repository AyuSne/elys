using System;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 把导入的 Standard 材质就地转成 URP/Lit。素材包是给通用管线做的，
    /// 在 URP 工程里直接拖进来会变成粉色；这个转换器是唯一处理该问题的地方，
    /// 也被编辑器导入后处理与菜单「全部转换」共用。
    /// </summary>
    public static class UrpMaterialConverter
    {
        public const string UrpLitShaderName = "Universal Render Pipeline/Lit";

        /// <summary>材质是否已经是 URP 兼容的 shader。</summary>
        public static bool IsUrpCompatible(Material material)
        {
            return material != null
                && material.shader != null
                && material.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal);
        }

        /// <summary>转换一个材质；已兼容或无法转换时返回 false。</summary>
        public static bool Convert(Material material)
        {
            if (material == null || IsUrpCompatible(material))
            {
                return false;
            }

            var urpLit = Shader.Find(UrpLitShaderName);
            if (urpLit == null)
            {
                Debug.LogWarning($"[Elys] 找不到 {UrpLitShaderName}，材质 {material.name} 未转换");
                return false;
            }

            // 先读旧属性：切 shader 之后 Standard 的 _Color / _Glossiness / _MainTex 就没了。
            var color = ReadColor(material, "_Color", new Color(1f, 1f, 1f, 1f));
            var texture = ReadTexture(material, "_MainTex");
            var metallic = ReadFloat(material, "_Metallic", 0f);
            var smoothness = material.HasProperty("_Glossiness")
                ? material.GetFloat("_Glossiness")
                : ReadFloat(material, "_Smoothness", 0.5f);

            material.shader = urpLit;
            material.SetColor("_BaseColor", color);
            if (texture != null)
            {
                material.SetTexture("_BaseMap", texture);
            }

            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            return true;
        }

        static Color ReadColor(Material material, string property, Color fallback)
        {
            if (material.HasProperty(property))
            {
                return material.GetColor(property);
            }

            return material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : fallback;
        }

        static Texture ReadTexture(Material material, string property)
        {
            return material.HasProperty(property) ? material.GetTexture(property) : null;
        }

        static float ReadFloat(Material material, string property, float fallback)
        {
            return material.HasProperty(property) ? material.GetFloat(property) : fallback;
        }
    }
}
