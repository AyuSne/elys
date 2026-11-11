using System.Collections.Generic;
using ElysFarm.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ElysFarm.EditorTools
{
    /// <summary>
    /// 生成体块演示场景。场景里只放一个引导器与 HUD 组件，方块与材质由引导器在运行时创建，
    /// 因此改布局只需要改代码，不需要手改 YAML。
    /// </summary>
    public static class FarmSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/FarmDemo.unity";

        [MenuItem("Elys/构建农场演示场景")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var camera = Camera.main;
            if (camera != null)
            {
                camera.transform.SetPositionAndRotation(new Vector3(0f, 12f, -10f), Quaternion.Euler(42f, 0f, 0f));
            }

            var root = new GameObject("Farm Demo");
            root.AddComponent<FarmDemoBootstrap>();
            root.AddComponent<FarmDemoHud>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureInBuildSettings();
            AssetDatabase.SaveAssets();

            Debug.Log($"[Elys] 体块演示场景已生成：{ScenePath}");
        }

        static void EnsureInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (var i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path != ScenePath)
                {
                    continue;
                }

                scenes[i].enabled = true;
                EditorBuildSettings.scenes = scenes.ToArray();
                return;
            }

            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
