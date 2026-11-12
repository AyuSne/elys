using ElysFarm.Demo;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #9 无粉色材质：工程里所有 Quaternius 素材材质（含 FBX 内嵌材质）必须是 URP 兼容的。
    /// 这是唯一一条直接扫资产的验收用例，防的是「某个模型重新导入后悄悄变回 Standard」。
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

                Assert.IsTrue(
                    UrpMaterialConverter.IsUrpCompatible(material),
                    $"{path} 的 shader 是 {material.shader.name}，在 URP 下会变粉");
                checkedCount++;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { ArtRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx"))
                {
                    continue;
                }

                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is not Material material)
                    {
                        continue;
                    }

                    Assert.IsTrue(
                        UrpMaterialConverter.IsUrpCompatible(material),
                        $"{path} 内嵌材质 {material.name} 的 shader 是 {material.shader.name}，在 URP 下会变粉");
                    checkedCount++;
                }
            }

            Assert.Greater(checkedCount, 0, "没有扫到任何美术材质，素材目录可能没导入");
        }
    }
}
