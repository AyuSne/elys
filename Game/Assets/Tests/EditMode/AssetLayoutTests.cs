using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #16 收口：打包目录只留 2D 像素素材。3D 旧素材（FBX/材质）若回到
    /// <c>Assets/Resources</c>，每次打包都会把没人用的模型背进包里。
    /// 这里断言的是交付约束本身，不打开场景、不加载贴图。
    /// </summary>
    public class AssetLayoutTests
    {
        static readonly string ResourcesRoot = Path.Combine(Application.dataPath, "Resources");

        [Test]
        public void 打包目录里不再有3D素材()
        {
            var leftovers = Directory
                .EnumerateFiles(ResourcesRoot, "*", SearchOption.AllDirectories)
                .Where(IsThreeDimensional)
                .Select(path => path.Substring(ResourcesRoot.Length + 1))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.IsEmpty(
                leftovers,
                "Assets/Resources 只应保留 2D 像素素材，以下 3D 素材会被打进包里："
                + string.Join("、", leftovers.Take(5))
                + (leftovers.Length > 5 ? " 等" : string.Empty));
        }

        [Test]
        public void 像素素材目录仍在()
        {
            var pixelArt = Path.Combine(ResourcesRoot, "PixelArt");

            Assert.IsTrue(Directory.Exists(pixelArt), "2D 分支必须保留 Assets/Resources/PixelArt");
            Assert.IsNotEmpty(
                Directory.GetFiles(pixelArt, "*.png", SearchOption.AllDirectories),
                "像素素材目录里至少要有一张 PNG");
        }

        static bool IsThreeDimensional(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".fbx", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".obj", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".blend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".mat", StringComparison.OrdinalIgnoreCase);
        }
    }
}
