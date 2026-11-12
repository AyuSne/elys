using UnityEditor;
using UnityEngine;

namespace ElysFarm.EditorTools
{
    /// <summary>
    /// 像素素材的导入规则：点采样、不压缩、不生成 mipmap、不缩放 NPOT。
    /// 切图在 <see cref="ElysFarm.Demo.PixelArt"/> 里按 16px 格位运行时完成，
    /// 所以这里按普通贴图导入，而不是 Sprite 资产——格位与尺寸都由代码决定，测试才断言得到。
    /// </summary>
    public sealed class PixelArtImportPostprocessor : AssetPostprocessor
    {
        public const string Root = "Assets/Resources/PixelArt/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root, System.StringComparison.Ordinal))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.maxTextureSize = 2048;
        }
    }
}
