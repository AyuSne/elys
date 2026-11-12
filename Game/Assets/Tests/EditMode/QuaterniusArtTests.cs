using ElysFarm.Core;
using ElysFarm.Demo;
using NUnit.Framework;
using UnityEngine;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #9 素材缺失回退：加载器拿不到模型时只能返回 null，不能抛异常，
    /// 否则演示会在「素材没导入」的机器上直接崩。测试打 <see cref="QuaterniusArt"/> 这条表现层接缝。
    /// </summary>
    public class QuaterniusArtTests
    {
        [SetUp]
        public void SetUp()
        {
            QuaterniusArt.UseArt = true;
        }

        [TearDown]
        public void TearDown()
        {
            QuaterniusArt.UseArt = true;
        }

        [Test]
        public void 素材缺失时返回空而不是抛异常()
        {
            Assert.IsNull(QuaterniusArt.Load("Crops/DoesNotExist"), "不存在的模型应返回 null");
            Assert.IsNull(QuaterniusArt.Load(null), "空路径应返回 null");
            Assert.IsNull(QuaterniusArt.LoadCrop("moon", CropStage.Mature), "未知品种应返回 null");
            Assert.IsNull(QuaterniusArt.Load("Buildings/DoesNotExist"), "不存在的场景模型应返回 null");
        }

        [Test]
        public void 关掉素材开关后即使资源存在也退回体块()
        {
            Assert.IsNotNull(QuaterniusArt.Load("Crops/Carrot_1"), "默认应能加载已导入的素材");

            QuaterniusArt.UseArt = false;

            Assert.IsNull(QuaterniusArt.Load("Crops/Carrot_1"), "关掉素材后必须走体块回退");
            Assert.IsNull(QuaterniusArt.LoadCrop(CropCatalog.RadishId, CropStage.Seedling), "作物同样回退");
        }

        [Test]
        public void 两个品种的五个阶段与场景模型素材都在工程里()
        {
            foreach (var speciesId in new[] { CropCatalog.RadishId, CropCatalog.WheatId })
            {
                for (var stage = CropStage.Seedling; stage <= CropStage.Mature; stage++)
                {
                    Assert.IsNotNull(
                        QuaterniusArt.LoadCrop(speciesId, stage),
                        $"{speciesId} 的第 {(int)stage} 阶段模型应能被 Resources 加载");
                }
            }

            var sceneModels = new[]
            {
                "Buildings/Barn",
                "Buildings/Windmill",
                "Buildings/WaterTower",
                "Buildings/Well",
                "Buildings/ChickenCoop",
                "Buildings/Fence",
                "Nature/Tree1",
                "Nature/Bush1",
                "Nature/Rock1",
                "Nature/Grass1",
                "Characters/Worker_Male",
                "Characters/Casual_Female",
                "Characters/Casual_Male"
            };

            foreach (var model in sceneModels)
            {
                Assert.IsNotNull(QuaterniusArt.Load(model), $"{model} 应能被 Resources 加载");
            }
        }

        [Test]
        public void 三名NPC各有不同的角色模型且都能加载()
        {
            var ids = new[] { FarmDemoBootstrap.AheId, FarmDemoBootstrap.AyaId, FarmDemoBootstrap.AtuId };
            var modelNames = new System.Collections.Generic.HashSet<string>();

            foreach (var npcId in ids)
            {
                var modelName = QuaterniusArt.CharacterModelName(npcId);
                Assert.IsNotNull(modelName, $"{npcId} 应有角色模型");
                Assert.IsTrue(modelNames.Add(modelName), $"{npcId} 的角色模型不应与别人重复");
                Assert.IsNotNull(QuaterniusArt.LoadCharacter(npcId), $"{modelName} 应能被 Resources 加载");
            }

            Assert.IsNull(QuaterniusArt.CharacterModelName("nobody"), "未知 NPC 没有角色模型");
            Assert.IsNull(QuaterniusArt.LoadCharacter("nobody"), "未知 NPC 应回退体块");
        }

        [Test]
        public void 成熟作物用素材包的成品模型而不是平铺的麦捆()
        {
            Assert.AreEqual("Carrot_Crop", QuaterniusArt.CropModelName(CropCatalog.RadishId, CropStage.Mature));
            Assert.AreEqual("Wheat_4", QuaterniusArt.CropModelName(CropCatalog.WheatId, CropStage.Mature));
            Assert.AreEqual("Carrot_1", QuaterniusArt.CropModelName(CropCatalog.RadishId, CropStage.Seedling));
            Assert.AreEqual("Wheat_2", QuaterniusArt.CropModelName(CropCatalog.WheatId, CropStage.Sprout));
        }
    }
}
