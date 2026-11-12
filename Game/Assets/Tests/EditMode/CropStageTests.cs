using ElysFarm.Core;
using ElysFarm.Demo;
using NUnit.Framework;
using UnityEngine;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #9 五个生长阶段：领域只给生长进度与地块状态，表现层把它映射成五个可区分的阶段。
    /// 测试打 <see cref="CropStages"/> 这条表现层接缝，不打开场景，也不检查网格。
    /// </summary>
    public class CropStageTests
    {
        [Test]
        public void 空地或已收获的地块没有作物阶段()
        {
            Assert.AreEqual(CropStage.None, CropStages.Of(FieldState.Empty, 0f), "空地没有作物");
            Assert.AreEqual(CropStage.None, CropStages.Of(FieldState.Harvested, 0f), "收获后也没有作物");
        }

        [Test]
        public void 生长进度把作物依次推进到五个阶段()
        {
            Assert.AreEqual(CropStage.Seedling, CropStages.Of(FieldState.Sown, 0f), "刚播种是第一阶段");
            Assert.AreEqual(CropStage.Sprout, CropStages.Of(FieldState.Growing, 0.30f), "过四分之一是第二阶段");
            Assert.AreEqual(CropStage.Growing, CropStages.Of(FieldState.Growing, 0.55f), "过半是第三阶段");
            Assert.AreEqual(CropStage.Ripening, CropStages.Of(FieldState.Growing, 0.80f), "过四分之三是第四阶段");
            Assert.AreEqual(CropStage.Mature, CropStages.Of(FieldState.Mature, 1f), "成熟是第五阶段");
        }

        [Test]
        public void 阶段边界落在四分之一处且满进度也算成熟()
        {
            Assert.AreEqual(CropStage.Seedling, CropStages.Of(FieldState.Growing, 0.24f));
            Assert.AreEqual(CropStage.Sprout, CropStages.Of(FieldState.Growing, 0.25f));
            Assert.AreEqual(CropStage.Growing, CropStages.Of(FieldState.Growing, 0.50f));
            Assert.AreEqual(CropStage.Ripening, CropStages.Of(FieldState.Growing, 0.75f));
            Assert.AreEqual(CropStage.Mature, CropStages.Of(FieldState.Growing, 1f), "进度满格等同于成熟");
        }

        [Test]
        public void 五个阶段的颜色与尺寸两两可区分()
        {
            var stages = new[]
            {
                CropStage.Seedling, CropStage.Sprout, CropStage.Growing, CropStage.Ripening, CropStage.Mature
            };

            for (var i = 0; i < stages.Length; i++)
            {
                Assert.IsTrue(CropStages.ColorOf(stages[i]) != default, $"{stages[i]} 要有可用的回退颜色");
                Assert.Greater(CropStages.ScaleOf(stages[i]), 0f, $"{stages[i]} 要有正的尺寸");

                for (var j = i + 1; j < stages.Length; j++)
                {
                    var colorGap = Vector4.Distance(
                        (Vector4)CropStages.ColorOf(stages[i]),
                        (Vector4)CropStages.ColorOf(stages[j]));
                    Assert.Greater(colorGap, 0.04f, $"{stages[i]} 与 {stages[j]} 的颜色要拉得开");

                    var scaleGap = Mathf.Abs(CropStages.ScaleOf(stages[i]) - CropStages.ScaleOf(stages[j]));
                    Assert.Greater(scaleGap, 0.02f, $"{stages[i]} 与 {stages[j]} 的尺寸要拉得开");
                }
            }
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
