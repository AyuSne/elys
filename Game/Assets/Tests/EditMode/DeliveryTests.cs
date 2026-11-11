using System.Linq;
using ElysFarm.Demo;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ElysFarm.Tests
{
    /// <summary>
    /// 交付物本身的回归（#4）：打出的 Windows 包必须从农场演示场景启动、场景里真的挂着引导器与 HUD，
    /// 且不再带着模板默认的产品名。测试打的是构建配置与场景装配这条「交付缝」。
    /// </summary>
    public class DeliveryTests
    {
        const string DemoScenePath = "Assets/Scenes/FarmDemo.unity";

        [Test]
        public void 构建首场景是农场演示()
        {
            var scenes = EditorBuildSettings.scenes;

            Assert.IsNotEmpty(scenes, "Build Settings 里没有场景，打出的包只会是空场景");
            Assert.IsTrue(scenes[0].enabled, "第一个场景必须启用");
            Assert.AreEqual(DemoScenePath, scenes[0].path, "打出的包必须直接从演示场景启动");
            Assert.IsNotNull(
                AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath),
                "演示场景不在工程里，构建会失败");
        }

        [Test]
        public void 演示场景挂载了引导器与HUD()
        {
            var scene = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Additive);
            try
            {
                Assert.IsNotNull(
                    Object.FindFirstObjectByType<FarmDemoBootstrap>(),
                    "演示场景缺少引导器，包里跑不出种田闭环");
                Assert.IsNotNull(
                    Object.FindFirstObjectByType<FarmDemoHud>(),
                    "演示场景缺少 HUD，观察者看不到决策依据");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void 产品名换成Elys而不是模板默认值()
        {
            Assert.AreEqual("Elys", Application.productName, "交付包不应带着模板的 Game 字样");
        }
    }
}
