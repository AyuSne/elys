using ElysFarm.Demo;
using NUnit.Framework;
using UnityEngine;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #9 无粉色材质：导入的 Standard 材质必须能就地转成 URP/Lit，
    /// 颜色、贴图、金属度与光滑度都要带过去，且重复转换不破坏材质。
    /// </summary>
    public class UrpMaterialTests
    {
        [Test]
        public void 非URP材质转成URP后保留颜色与贴图()
        {
            // 本机通常有 Standard；万一某个 Unity 版本移除了它，用 Sprites/Default 也能验证
            // 「非 URP → URP」这条转换路径，而不是静默跳过。
            var usesStandard = Shader.Find("Standard") != null;
            var shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            Assert.IsNotNull(shader, "工程里至少要有一个非 URP shader 用来验证转换");

            var texture = Texture2D.whiteTexture;
            var material = new Material(shader) { name = "TestNonUrp" };
            material.SetColor("_Color", new Color(0.8f, 0.2f, 0.1f, 1f));
            material.SetTexture("_MainTex", texture);
            if (usesStandard)
            {
                material.SetFloat("_Metallic", 0.4f);
                material.SetFloat("_Glossiness", 0.3f);
            }

            Assert.IsFalse(UrpMaterialConverter.IsUrpCompatible(material), "非 URP shader 不算 URP 兼容");
            Assert.IsTrue(UrpMaterialConverter.Convert(material), "非 URP 材质应被转换");
            Assert.IsTrue(UrpMaterialConverter.IsUrpCompatible(material), "转换后必须是 URP 材质");
            Assert.AreEqual(UrpMaterialConverter.UrpLitShaderName, material.shader.name);
            AssertColor(new Color(0.8f, 0.2f, 0.1f, 1f), material.GetColor("_BaseColor"), "颜色应带过去");
            Assert.AreEqual(texture, material.GetTexture("_BaseMap"));
            Assert.AreEqual(usesStandard ? 0.4f : 0f, material.GetFloat("_Metallic"), 0.0001f);
            Assert.AreEqual(usesStandard ? 0.3f : 0.5f, material.GetFloat("_Smoothness"), 0.0001f);

            Object.DestroyImmediate(material);
        }

        static void AssertColor(Color expected, Color actual, string message)
        {
            Assert.AreEqual(expected.r, actual.r, 0.001f, message);
            Assert.AreEqual(expected.g, actual.g, 0.001f, message);
            Assert.AreEqual(expected.b, actual.b, 0.001f, message);
            Assert.AreEqual(expected.a, actual.a, 0.001f, message);
        }

        [Test]
        public void 已经是URP的材质不会被重复转换()
        {
            var material = new Material(Shader.Find(UrpMaterialConverter.UrpLitShaderName));
            material.SetColor("_BaseColor", new Color(0.2f, 0.7f, 0.3f, 1f));

            Assert.IsTrue(UrpMaterialConverter.IsUrpCompatible(material));
            Assert.IsFalse(UrpMaterialConverter.Convert(material), "已兼容的材质不需要再次转换");
            AssertColor(new Color(0.2f, 0.7f, 0.3f, 1f), material.GetColor("_BaseColor"), "颜色不应被改写");

            Object.DestroyImmediate(material);
        }

        [Test]
        public void 空材质不转换也不抛异常()
        {
            Assert.IsFalse(UrpMaterialConverter.Convert(null));
            Assert.IsFalse(UrpMaterialConverter.IsUrpCompatible(null));
        }
    }
}
