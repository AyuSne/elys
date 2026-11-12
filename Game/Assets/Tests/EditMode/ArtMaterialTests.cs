using System;
using ElysFarm.Demo;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #9 无粉色材质：工程里所有 Quaternius 素材材质（含 FBX 内嵌材质）必须用 URP/Lit，
    /// 并且带 <c>_BaseColor</c>。这是唯一一条直接扫资产的验收用例，防的是
    /// 「某个模型重新导入后悄悄变回 Standard」或「换个 URP shader 但属性对不上」。
    /// </summary>
    public class ArtMaterialTests
    {
        const string ArtRoot = "Assets/Resources/Quaternius";

        [Test]
        public void 工程里的美术材质全部URP兼容()
        {
            Assert.IsNotNull(
                Shader.Find(UrpMaterialConverter.UrpLitShaderName),
                "工程里必须有 URP/Lit，否则转换器无从谈起");

            var checkedCount = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { ArtRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    continue;
                }

                AssertMaterial(path, material);
                checkedCount++;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { ArtRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is not Material material)
                    {
                        continue;
                    }

                    AssertMaterial(path, material);
                    checkedCount++;
                }
            }

            Assert.Greater(checkedCount, 20, "扫到的美术材质太少，素材目录可能没导入或没有内嵌材质");
        }

        static void AssertMaterial(string path, Material material)
        {
            Assert.AreEqual(
                UrpMaterialConverter.UrpLitShaderName,
                material.shader.name,
                $"{path} 的材质 {material.name} 不是 URP/Lit，在 URP 下会变粉");
            Assert.IsTrue(
                material.HasProperty("_BaseColor"),
                $"{path} 的材质 {material.name} 缺少 _BaseColor，URP/Lit 的属性对不上");
        }
    }
}
