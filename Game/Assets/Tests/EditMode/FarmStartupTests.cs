using System.Collections.Generic;
using ElysFarm.Core;
using ElysFarm.Demo;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #13 开局：三块地并排，各自停在一个不同的生长阶段——西边刚播下的萝卜、中间熟了的小麦、东边快熟的萝卜。
    /// 测试打在 <see cref="FarmWorld"/> 这条接缝上：只用公开的工步与日结算推进，不打开场景，也不联网。
    /// </summary>
    public class FarmStartupTests
    {
        [Test]
        public void 开局三块地处在三个不同阶段()
        {
            var world = NewWorld();
            FarmStartup.Seed(world);

            var stages = new HashSet<CropStage>();
            foreach (var field in world.Fields)
            {
                Assert.IsTrue(field.IsSown, $"{field.Id} 开局就该种上，空着看不出阶段");
                var stage = CropStages.Of(field.State, field.Growth);
                Assert.IsTrue(stages.Add(stage), $"{field.Id} 的阶段与别人重了：{stage}");
            }

            Assert.AreEqual(world.Fields.Count, stages.Count, "三块地要一眼分辨得开");
        }

        [Test]
        public void 开局仍是第一天且没浇过水的地不长()
        {
            var world = NewWorld();
            FarmStartup.Seed(world);

            Assert.AreEqual(1, world.Clock.Day, "开局不能被催到第 N 天");
            Assert.AreEqual(
                0f,
                world.GetField(FarmDemoBootstrap.FieldA1).Growth,
                0.0001f,
                "没浇过水的地一毫米都不该长");
        }

        [Test]
        public void 中间那块小麦熟了东边那块萝卜快熟()
        {
            var world = NewWorld();
            FarmStartup.Seed(world);

            var wheat = world.GetField(FarmDemoBootstrap.FieldA2);
            Assert.AreEqual(CropCatalog.WheatId, wheat.Crop.Id, "中间那块是小麦");
            Assert.AreEqual(FieldState.Mature, wheat.State, "中间那块要能直接收");

            var radish = world.GetField(FarmDemoBootstrap.FieldA3);
            Assert.AreEqual(CropCatalog.RadishId, radish.Crop.Id, "东边那块是萝卜");
            Assert.AreEqual(
                CropStage.Ripening,
                CropStages.Of(radish.State, radish.Growth),
                "东边那块快熟，但还没到能收");
        }

        [Test]
        public void 开局播种要从种子库扣账()
        {
            var world = NewWorld();
            var radishBefore = world.Ledger.SeedsOf(CropCatalog.RadishId);
            var wheatBefore = world.Ledger.SeedsOf(CropCatalog.WheatId);

            FarmStartup.Seed(world);

            // 两块萝卜各一颗种、一块小麦两颗种。
            Assert.AreEqual(
                radishBefore - 2,
                world.Ledger.SeedsOf(CropCatalog.RadishId),
                "播下两块萝卜，种子库要少两颗");
            Assert.AreEqual(
                wheatBefore - 2,
                world.Ledger.SeedsOf(CropCatalog.WheatId),
                "播下一块小麦，种子库要少两颗种");
            Assert.IsTrue(world.Ledger.Verify(out var error), $"开局之后三本账要不守恒了：{error}");
        }

        [Test]
        public void 种子库不够时播不下去()
        {
            var world = NewWorld();
            while (world.Ledger.SeedsOf(CropCatalog.RadishId) > 0)
            {
                world.Ledger.TryTakeSeeds(CropCatalog.RadishId, 1);
            }

            FarmStartup.Seed(world);

            Assert.AreEqual(FieldState.Empty, world.GetField(FarmDemoBootstrap.FieldA1).State, "没种子就不该凭空种上");
            Assert.AreEqual(FieldState.Empty, world.GetField(FarmDemoBootstrap.FieldA3).State, "没种子就不该凭空种上");
            Assert.AreEqual(CropStage.Mature, CropStages.Of(
                world.GetField(FarmDemoBootstrap.FieldA2).State,
                world.GetField(FarmDemoBootstrap.FieldA2).Growth), "小麦种子还在，中间那块照常种");
            Assert.IsTrue(world.Ledger.Verify(out var error), error);
        }

        /// <summary>与演示引导器相同的开局世界：三块地，肥力与种子都取自同一条常量。</summary>
        static FarmWorld NewWorld()
        {
            var world = new FarmWorld(new FarmClock());
            foreach (var footprint in FarmLayout.Fields)
            {
                world.AddField(
                    footprint.Id,
                    FarmLayout.ToFarmPosition(footprint.Center),
                    FarmStartup.FieldFertility);
            }

            world.AddSeeds(CropCatalog.RadishId, 12);
            world.AddSeeds(CropCatalog.WheatId, 4);
            return world;
        }
    }
}
