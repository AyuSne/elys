using System.Collections.Generic;
using System.Linq;
using ElysFarm.Core;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// 农场闭环的领域测试。按 ADR-0001 与规格，测试只打 <see cref="FarmWorld"/> 这条缝：
    /// 五个工步的迁移、跨日结算、效用打分与三本账都通过它的外部行为断言。
    /// </summary>
    public class FarmWorldTests
    {
        static FarmWorld NewWorld(int seeds = 10, float speed = 1f)
        {
            var world = new FarmWorld(new FarmClock { Speed = speed });
            world.AddField("A1", 0f, 2f);
            world.AddNpc("ahe", "阿禾", 0f, -4f);
            world.SetWarehouse(0f, -4f);
            world.AddSeeds(CropCatalog.RadishId, seeds);
            return world;
        }

        [Test]
        public void 一名NPC仅靠效用打分把一块地种到收获()
        {
            var world = NewWorld();
            var field = world.GetField("A1");
            var steps = new List<WorkStep>();

            for (var guard = 0; guard < 200 && field.State != FieldState.Harvested; guard++)
            {
                var result = world.StepOnce("ahe");
                if (result.Executed)
                {
                    steps.Add(result.Step.Value);
                    continue;
                }

                world.AdvanceDays(1);
            }

            Assert.AreEqual(FieldState.Harvested, field.State, "NPC 应当自行完成收获");
            Assert.Contains(WorkStep.Sow, steps, "应当播过种");
            Assert.Contains(WorkStep.Water, steps, "应当浇过水");
            Assert.Contains(WorkStep.Fertilize, steps, "应当施过肥");
            Assert.Contains(WorkStep.Weed, steps, "应当除过草");
            Assert.Contains(WorkStep.Harvest, steps, "应当收获");
            Assert.AreEqual(3, world.Ledger.ProduceOf(CropCatalog.RadishId), "萝卜每次收获 3 个");
            Assert.AreEqual(9, world.Ledger.SeedsOf(CropCatalog.RadishId), "播种消耗 1 颗种子");
            Assert.IsTrue(world.Ledger.Verify(out var error), error);
        }

        [Test]
        public void 不浇水的地块十个游戏日后仍未成熟()
        {
            var world = NewWorld();
            var field = world.GetField("A1");
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);

            world.AdvanceDays(10);

            Assert.AreNotEqual(FieldState.Mature, field.State, "不浇水不该成熟");
            Assert.Less(field.Growth, 0.5f, "只有播种当天那一点水分，长不了多少");
        }

        [Test]
        public void 五个工步各自可独立触发()
        {
            var world = NewWorld();
            var field = world.GetField("A1");

            Assert.AreEqual(FieldState.Empty, field.State, "初始应为空地");

            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            Assert.AreEqual(FieldState.Sown, field.State);
            Assert.AreEqual(9, world.Ledger.SeedsOf(CropCatalog.RadishId), "播种扣 1 颗种子");

            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Water).Executed);
            Assert.AreEqual(1f, field.Water, 0.001f);

            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Fertilize).Executed);
            Assert.AreEqual(1f, field.Fertility, 0.001f);

            world.AdvanceDays(1);
            Assert.Greater(field.Weeds, 0f, "过一天应该长出杂草");
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Weed).Executed);
            Assert.AreEqual(0f, field.Weeds, 0.001f);

            for (var day = 0; day < 10 && field.State != FieldState.Mature; day++)
            {
                if (field.Can(WorkStep.Water, out _))
                {
                    world.ExecuteStep("ahe", "A1", WorkStep.Water);
                }

                world.AdvanceDays(1);
            }

            Assert.AreEqual(FieldState.Mature, field.State, "萝卜应在 3 个游戏日内成熟");
            var harvest = world.ExecuteStep("ahe", "A1", WorkStep.Harvest);
            Assert.IsTrue(harvest.Executed);
            Assert.AreEqual(3, harvest.Yield);
            Assert.AreEqual(FieldState.Harvested, field.State);
            Assert.AreEqual(3, world.Ledger.ProduceOf(CropCatalog.RadishId));
            Assert.IsTrue(world.Ledger.Verify(out var error), error);
        }

        [Test]
        public void 三本账满足入账减出账等于持有()
        {
            var world = NewWorld();
            var field = world.GetField("A1");

            for (var guard = 0; guard < 200 && field.State != FieldState.Harvested; guard++)
            {
                if (!world.StepOnce("ahe").Executed)
                {
                    world.AdvanceDays(1);
                }
            }

            Assert.AreEqual(FieldState.Harvested, field.State);
            Assert.AreEqual(1, world.Ledger.SeedsRemoved);
            Assert.AreEqual(9, world.Ledger.SeedTotal);
            Assert.AreEqual(3, world.Ledger.ProduceTotal);
            Assert.IsTrue(world.Ledger.Verify(out var error), error);

            Assert.IsTrue(world.IsAtWarehouse(world.GetNpc("ahe")), "NPC 初始在仓库");
            Assert.IsTrue(world.TryPickUpProduce("ahe", CropCatalog.RadishId, 2), "在仓库才能取货");
            Assert.AreEqual(2, world.Ledger.CarriedOf(CropCatalog.RadishId));
            Assert.AreEqual(1, world.Ledger.ProduceOf(CropCatalog.RadishId));
            Assert.AreEqual(3, world.Ledger.ProduceTotal, "取到随身只是搬家");
            Assert.IsTrue(world.Ledger.Verify(out error), error);

            world.MoveNpc("ahe", 0f, 4f);
            Assert.IsFalse(world.TryDepositCarried("ahe", CropCatalog.RadishId, 2), "不在仓库不能移交");
            Assert.AreEqual(2, world.Ledger.CarriedOf(CropCatalog.RadishId), "失败的移交不能改动账本");

            world.MoveNpc("ahe", 0f, -4f);
            Assert.IsTrue(world.TryDepositCarried("ahe", CropCatalog.RadishId, 2));
            Assert.AreEqual(3, world.Ledger.ProduceOf(CropCatalog.RadishId));
            Assert.AreEqual(0, world.Ledger.CarriedOf(CropCatalog.RadishId));
            Assert.IsTrue(world.Ledger.Verify(out error), error);
        }

        [Test]
        public void 加速档位改变推进速度但不改变结算结果()
        {
            var slow = NewWorld(speed: 1f);
            var fast = NewWorld(speed: 16f);

            foreach (var world in new[] { slow, fast })
            {
                world.ExecuteStep("ahe", "A1", WorkStep.Sow);
                world.ExecuteStep("ahe", "A1", WorkStep.Water);
                world.ExecuteStep("ahe", "A1", WorkStep.Fertilize);
                world.AdvanceDays(2);
            }

            var slowField = slow.GetField("A1");
            var fastField = fast.GetField("A1");

            Assert.AreEqual(slow.Clock.Day, fast.Clock.Day, "同样推进两个游戏日");
            Assert.AreEqual(slowField.Growth, fastField.Growth, 0.0001f, "生长结算与档位无关");
            Assert.AreEqual(slowField.Water, fastField.Water, 0.0001f, "水分结算与档位无关");
            Assert.AreEqual(slowField.Weeds, fastField.Weeds, 0.0001f, "杂草结算与档位无关");
            Assert.AreEqual(slowField.Fertility, fastField.Fertility, 0.0001f, "肥力结算与档位无关");

            var slowRate = new FarmWorld(new FarmClock { Speed = 1f });
            var fastRate = new FarmWorld(new FarmClock { Speed = 16f });
            slowRate.Tick(90f);
            fastRate.Tick(90f);

            Assert.AreEqual(1, slowRate.Clock.Day, "1x 下 90 秒还没跨日");
            Assert.AreEqual(5, fastRate.Clock.Day, "16x 下 90 秒跨过 4 个游戏日");
        }

        [Test]
        public void 效用打分把成熟的收获排在缺水的浇水之前_且无效候选带理由()
        {
            var world = NewWorld(seeds: 5);
            var mature = world.GetField("A1");
            world.AddField("A2", 20f, 20f);

            world.ExecuteStep("ahe", "A1", WorkStep.Sow);
            world.ExecuteStep("ahe", "A1", WorkStep.Water);
            world.ExecuteStep("ahe", "A1", WorkStep.Fertilize);
            for (var day = 0; day < 5 && mature.State != FieldState.Mature; day++)
            {
                if (mature.Can(WorkStep.Water, out _))
                {
                    world.ExecuteStep("ahe", "A1", WorkStep.Water);
                }

                world.AdvanceDays(1);
            }

            Assert.AreEqual(FieldState.Mature, mature.State);

            world.ExecuteStep("ahe", "A2", WorkStep.Sow);
            world.ExecuteStep("ahe", "A2", WorkStep.Water);

            var candidates = world.RankCandidates("ahe");
            var top = candidates[0];

            Assert.IsTrue(top.IsValid, "排第一的应该是有效候选");
            Assert.AreEqual(WorkStep.Harvest, top.Step, "收获的效用高于浇水");
            Assert.AreEqual("A1", top.FieldId);

            var immatureHarvest = candidates.Single(c => c.FieldId == "A2" && c.Step == WorkStep.Harvest);
            Assert.IsFalse(immatureHarvest.IsValid);
            Assert.AreEqual("还没熟", immatureHarvest.RejectionReason);
            Assert.Greater(top.Score, 0f);
        }
    }
}
