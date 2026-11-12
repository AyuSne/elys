using ElysFarm.Core;
using ElysFarm.Demo;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #12 五个生长阶段：领域只给生长进度与地块状态，表现层把它映射成五个阶段与图集格位。
    /// 测试打 <see cref="CropStages"/> 这条表现层接缝，不打开场景，也不检查渲染结果。
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
        public void 五个阶段对应五个互不相同的图集格位()
        {
            foreach (var speciesId in new[] { CropCatalog.RadishId, CropCatalog.WheatId })
            {
                var stages = new[]
                {
                    CropStage.Seedling, CropStage.Sprout, CropStage.Growing,
                    CropStage.Ripening, CropStage.Mature
                };

                var previousColumn = -1;
                foreach (var stage in stages)
                {
                    var cell = CropStages.CellOf(speciesId, stage);
                    Assert.IsTrue(cell.IsValid, $"{speciesId} 的 {stage} 需要图集格位");
                    Assert.AreEqual(16, cell.Width, "作物格宽 16px");
                    Assert.AreEqual(32, cell.Height, "作物格高 32px");
                    Assert.Greater(cell.Column, previousColumn, "阶段越靠后，格位应越靠右（不会回退）");
                    previousColumn = cell.Column;
                }

                var mature = CropStages.CellOf(speciesId, CropStage.Mature);
                for (var stage = CropStage.Seedling; stage < CropStage.Mature; stage++)
                {
                    Assert.AreNotEqual(
                        mature,
                        CropStages.CellOf(speciesId, stage),
                        $"{speciesId} 的成熟格位不能与 {stage} 共用");
                }
            }

            Assert.AreNotEqual(
                CropStages.CellOf(CropCatalog.RadishId, CropStage.Mature),
                CropStages.CellOf(CropCatalog.WheatId, CropStage.Mature),
                "两个品种的成熟外观不能共用同一格");
        }

        [Test]
        public void 空阶段与未知品种没有格位()
        {
            Assert.IsFalse(CropStages.CellOf(CropCatalog.RadishId, CropStage.None).IsValid);
            Assert.IsFalse(CropStages.CellOf("moon", CropStage.Mature).IsValid);
            Assert.IsFalse(CropStages.CellOf(null, CropStage.Mature).IsValid);
        }

        [Test]
        public void 体块回退仍有颜色与尺寸()
        {
            foreach (var stage in new[]
                     {
                         CropStage.Seedling, CropStage.Sprout, CropStage.Growing,
                         CropStage.Ripening, CropStage.Mature
                     })
            {
                Assert.IsTrue(CropStages.ColorOf(stage) != default, $"{stage} 要有回退颜色");
                Assert.Greater(CropStages.ScaleOf(stage), 0f, $"{stage} 要有正的回退尺寸");
            }
        }
    }
}
