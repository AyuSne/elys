using UnityEditor;
using UnityEngine;

namespace ElysFarm.EditorTools
{
    /// <summary>
    /// 导入后的材质兜底：Quaternius 的模型是给通用管线做的，个别模型（或换 Unity 版本后）
    /// 会以 Standard 材质导入，在 URP 里就是粉色。这里在每次模型导入后把材质
    /// 就地转成 URP/Lit，并提供菜单把所有美术材质扫一遍。
    /// 转换逻辑本身在 <see cref="ElysFarm.Demo.UrpMaterialConverter"/>，有 EditMode 测试。
    /// </summary>
    public sealed class QuaterniusMaterialPostprocessor : AssetPostprocessor
    {
        public const string ArtRoot = "Assets/Resources/Quaternius";

        /// <summary>
        /// Quaternius 的 FBX 里带着 Blender 场景的相机与灯；导入后每实例化一个模型
        /// 就会多一个 depth=0、clear=Skybox 的相机，把主相机画面刷成背景色（Game 视图一片纯色）。
        /// 导入阶段就关掉，运行时再兜底删一次。
        /// </summary>
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ArtRoot, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;
            importer.importCameras = false;
            importer.importLights = false;
        }

        void OnPostprocessModel(GameObject model)
        {
            if (!assetPath.StartsWith(ArtRoot, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            FixMaterials(model);
        }

        static void FixMaterials(GameObject model)
        {
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null)
                    {
                        continue;
                    }

                    if (ElysFarm.Demo.UrpMaterialConverter.Convert(material))
                    {
                        Debug.Log($"[Elys] 材质 {material.name} 已转为 URP/Lit");
                    }
                }
            }
        }

        [MenuItem("Elys/资源/把 Quaternius 材质全部转为 URP/Lit")]
        public static void ConvertAllArtMaterials()
        {
            var converted = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { ArtRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null && ElysFarm.Demo.UrpMaterialConverter.Convert(material))
                {
                    EditorUtility.SetDirty(material);
                    converted++;
                }
            }

            foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { ArtRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is Material material && ElysFarm.Demo.UrpMaterialConverter.Convert(material))
                    {
                        EditorUtility.SetDirty(material);
                        converted++;
                    }
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Elys] 已转换 {converted} 个材质为 URP/Lit");
        }
    }
}
