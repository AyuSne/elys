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
            world.AddField("A1", new FarmPosition(0f, 2f));
            world.AddNpc("ahe", "阿禾", new FarmPosition(0f, -4f));
            world.SetProduceStore(new FarmPosition(0f, -4f));
            world.AddSeeds(CropCatalog.RadishId, seeds);
            return world;
        }

        static void PlantRadish(FarmWorld world)
        {
            world.ExecuteStep("ahe", "A1", WorkStep.Sow);
            world.ExecuteStep("ahe", "A1", WorkStep.Water);
            world.ExecuteStep("ahe", "A1", WorkStep.Fertilize);
        }

        static void GrowRadishToMature(FarmWorld world, Field field)
        {
            for (var day = 0; day < 10 && field.State != FieldState.Mature; day++)
            {
                if (field.Can(WorkStep.Water, out _))
                {
                    world.ExecuteStep("ahe", "A1", WorkStep.Water);
                }

                world.AdvanceDays(1);
            }
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
            Assert.AreEqual(1, world.Ledger.SeedsRemoved, "播种消耗 1 颗种子");
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
            Assert.AreEqual(0f, field.Growth, 0.0001f, "没浇过水，一天也不该长");
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
            Assert.AreEqual(18, world.Ledger.SeedTotal, "播种扣 1 颗；3 天里每天补 2 萝卜 + 1 小麦");
            Assert.AreEqual(3, world.Ledger.ProduceTotal);
            Assert.IsTrue(world.Ledger.Verify(out var error), error);

            Assert.IsTrue(world.IsAtProduceStore(world.GetNpc("ahe")), "NPC 初始在产出仓");
            Assert.IsTrue(world.TryPickUpProduce("ahe", CropCatalog.RadishId, 2), "在产出仓才能取货");
            Assert.AreEqual(2, world.Ledger.HeldOf("ahe", CropCatalog.RadishId));
            Assert.AreEqual(1, world.Ledger.ProduceOf(CropCatalog.RadishId));
            Assert.AreEqual(3, world.Ledger.ProduceTotal, "取到随身只是搬家");
            Assert.IsTrue(world.Ledger.Verify(out error), error);

            world.MoveNpc("ahe", new FarmPosition(0f, 4f));
            Assert.IsFalse(world.TryDepositHeld("ahe", CropCatalog.RadishId, 2), "不在产出仓不能移交");
            Assert.AreEqual(2, world.Ledger.HeldOf("ahe", CropCatalog.RadishId), "失败的移交不能改动账本");

            world.MoveNpc("ahe", new FarmPosition(0f, -4f));
            Assert.IsTrue(world.TryDepositHeld("ahe", CropCatalog.RadishId, 2));
            Assert.AreEqual(3, world.Ledger.ProduceOf(CropCatalog.RadishId));
            Assert.AreEqual(0, world.Ledger.HeldOf("ahe", CropCatalog.RadishId));
            Assert.AreEqual(2, world.Ledger.HeldAdded);
            Assert.AreEqual(2, world.Ledger.HeldRemoved);
            Assert.IsTrue(world.Ledger.Verify(out error), error);
        }

        [Test]
        public void 跨日后种子库按固定数量补货()
        {
            var world = NewWorld();
            Assert.AreEqual(10, world.Ledger.SeedTotal, "开局只有 10 颗萝卜种子");

            world.AdvanceDays(1);

            Assert.AreEqual(12, world.Ledger.SeedsOf(CropCatalog.RadishId), "萝卜每天补 2 颗");
            Assert.AreEqual(1, world.Ledger.SeedsOf(CropCatalog.WheatId), "小麦每天补 1 颗");
            Assert.AreEqual(13, world.Ledger.SeedTotal, "补货都要入账");
            Assert.IsTrue(world.Ledger.Verify(out var error), error);

            world.AdvanceDays(2);

            Assert.AreEqual(16, world.Ledger.SeedsOf(CropCatalog.RadishId), "两天再补 4 颗萝卜种子");
            Assert.AreEqual(3, world.Ledger.SeedsOf(CropCatalog.WheatId), "两天再补 2 颗小麦种子");
            Assert.AreEqual(19, world.Ledger.SeedTotal);
        }

        [Test]
        public void 补货仍不够时照旧说种子库里没有种子()
        {
            var world = new FarmWorld(new FarmClock());
            world.AddField("A1", new FarmPosition(0f, 2f));
            world.AddNpc("ahe", "阿禾", new FarmPosition(0f, -4f));
            world.SetProduceStore(new FarmPosition(0f, -4f));

            var empty = world.ExecuteStep("ahe", "A1", WorkStep.Sow, CropCatalog.WheatId);
            Assert.IsFalse(empty.Executed, "一粒小麦种子都没有");
            Assert.AreEqual("种子库里没有小麦种子了", empty.Reason, "拒绝理由沿用动作校验那一句");

            world.AdvanceDays(1);
            Assert.AreEqual(1, world.Ledger.SeedsOf(CropCatalog.WheatId), "一天只补 1 颗");
            var shortOfCost = world.ExecuteStep("ahe", "A1", WorkStep.Sow, CropCatalog.WheatId);
            Assert.IsFalse(shortOfCost.Executed, "小麦一次要 2 颗，补一天还不够");
            Assert.AreEqual("种子库里没有小麦种子了", shortOfCost.Reason);
            Assert.AreEqual(FieldState.Empty, world.GetField("A1").State, "被拒绝的播种不能改动地块");

            world.AdvanceDays(1);
            Assert.IsTrue(
                world.ExecuteStep("ahe", "A1", WorkStep.Sow, CropCatalog.WheatId).Executed,
                "第 3 天攒够 2 颗，农场就不再因为种子用完而空转");
        }

        [Test]
        public void 补货写进种子库流水()
        {
            var world = NewWorld();
            Assert.AreEqual(0, world.SeedRestocks.Count, "还没跨日，种子库流水里没有补货");
            var seedsBefore = world.Ledger.SeedsAdded;

            world.AdvanceDays(2);

            var flow = world.SeedRestocks;
            Assert.AreEqual(4, flow.Count, "两天 × 萝卜与小麦各一条");
            foreach (var day in new[] { 2, 3 })
            {
                var entries = flow.Where(restock => restock.Day == day).ToList();
                Assert.AreEqual(2, entries.Count, $"第 {day} 天应补两种种子");
                Assert.IsTrue(
                    entries.Any(restock => restock.SpeciesId == CropCatalog.RadishId && restock.Count == 2),
                    "萝卜每天补 2 颗");
                Assert.IsTrue(
                    entries.Any(restock => restock.SpeciesId == CropCatalog.WheatId && restock.Count == 1),
                    "小麦每天补 1 颗");
            }

            var restocked = flow.Sum(restock => restock.Count);
            Assert.AreEqual(restocked, world.Ledger.SeedsAdded - seedsBefore, "流水要解释种子库的每一次增加");
            Assert.AreEqual(4, world.RestockedSeedsOf(CropCatalog.RadishId), "两天各补 2 颗萝卜种子");
            Assert.AreEqual(2, world.RestockedSeedsOf(CropCatalog.WheatId), "两天各补 1 颗小麦种子");
            Assert.IsTrue(world.Ledger.Verify(out var error), error);
        }

        [Test]
        public void 种子不够时工单失败释放_补货后能重新接单()
        {
            var world = NewWorld();
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", CropCatalog.RadishId, new[] { WorkStep.Sow }, out _, out var reason),
                reason);
            Assert.IsTrue(world.Ledger.TryTakeSeeds(CropCatalog.RadishId, 10), "接单后种子被别的活吃光");

            var failed = world.AdvanceWorkOrder("ahe");
            Assert.AreEqual(StepOutcome.Failed, failed.Outcome, "种子不够做不下去");
            Assert.AreEqual("种子库里没有萝卜种子了", failed.Reason, "拒绝理由沿用动作校验那一句");
            Assert.IsNull(world.WorkOrderOf("ahe"), "失败即释放占位");
            Assert.IsFalse(world.ClaimOf("A1").IsClaimed);

            world.AdvanceDays(1);

            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", CropCatalog.RadishId, new[] { WorkStep.Sow }, out _, out var retry),
                retry);
            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed, "补货后下一张播种工单种得下去");
        }

        [Test]
        public void 成熟后每天仍结算水分杂草与肥力()
        {
            var world = NewWorld();
            var field = world.GetField("A1");

            PlantRadish(world);
            GrowRadishToMature(world, field);

            Assert.AreEqual(FieldState.Mature, field.State);
            Assert.IsFalse(field.Can(WorkStep.Water, out var matureReason), "成熟后只需求收获");
            Assert.AreEqual("已经熟了，先收了吧", matureReason);

            var growth = field.Growth;
            var water = field.Water;
            var weeds = field.Weeds;
            var fertility = field.Fertility;

            world.AdvanceDays(1);

            Assert.AreEqual(growth, field.Growth, 0.0001f, "成熟后不再生长");
            Assert.Less(field.Water, water, "成熟后仍会失水");
            Assert.Greater(field.Weeds, weeds, "成熟后仍会长草");
            Assert.Less(field.Fertility, fertility, "成熟后仍会掉肥力");
        }

        [Test]
        public void 加速档位改变推进速度但不改变结算结果()
        {
            var slow = NewWorld(speed: 1f);
            var fast = NewWorld(speed: 16f);

            foreach (var world in new[] { slow, fast })
            {
                PlantRadish(world);
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
            world.AddField("A2", new FarmPosition(20f, 20f));

            PlantRadish(world);
            GrowRadishToMature(world, mature);

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

        [Test]
        public void 已经被种上的地块播种候选带一句实话的否决理由()
        {
            var world = NewWorld();
            var field = world.GetField("A1");
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed, "先把地种上");

            var sow = world.RankCandidates("ahe").Single(c => c.FieldId == "A1" && c.Step == WorkStep.Sow);
            Assert.IsFalse(sow.IsValid, "地里还种着东西，不能再播");
            Assert.AreEqual(Field.SownReason, sow.RejectionReason, "候选否决行读领域里的同一个常量");
            StringAssert.DoesNotContain("还没空出来", sow.RejectionReason, "候选否决行不再说「还没空出来」");

            Assert.IsFalse(field.Can(WorkStep.Sow, out var reason));
            Assert.AreEqual(Field.SownReason, reason);
            Assert.AreEqual("那块地还种着东西", Field.SownReason, "理由只有这一个常量来源，文案就是这句");
            Assert.AreEqual(
                StepOutcome.Failed,
                ActionValidator.DispositionOf(Field.SownReason),
                "地被抢种是做不下去，不是等条件");
        }
    }
}
