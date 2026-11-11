using System.Collections.Generic;
using ElysFarm.Core;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #4 可交付基线的验收剧本：一次完整种田流程同时断言题目要求的三件事——
    /// 步骤完整、背包守恒、时间推进。测试只打 <see cref="FarmWorld"/> 这条缝，
    /// 预期值来自题目与规格（五个工步、萝卜 3 个游戏日成熟、每次收获 3 个），
    /// 与模块级测试互补：这里核对的是观察者能在 HUD 的流水上看到的那条闭环。
    /// </summary>
    public class AcceptanceTests
    {
        static readonly WorkStep[] LoopSteps =
        {
            WorkStep.Sow,
            WorkStep.Water,
            WorkStep.Fertilize,
            WorkStep.Weed,
            WorkStep.Harvest
        };

        const int SeedCount = 10;

        static FarmWorld NewWorld()
        {
            var world = new FarmWorld(new FarmClock());
            world.AddField("A1", new FarmPosition(0f, 2f));
            world.AddNpc("ahe", "阿禾", new FarmPosition(0f, -4f));
            world.SetProduceStore(new FarmPosition(0f, -4f));
            world.AddSeeds(CropCatalog.RadishId, SeedCount);
            return world;
        }

        static void AssignLoop(FarmWorld world, out WorkOrder order)
        {
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", CropCatalog.RadishId, LoopSteps, out order, out var reason),
                reason);
        }

        static void RunLoop(FarmWorld world, out WorkOrder order)
        {
            AssignLoop(world, out order);
            for (var guard = 0; guard < 400 && !order.IsComplete; guard++)
            {
                if (!world.AdvanceWorkOrder("ahe").Executed)
                {
                    world.AdvanceDays(1);
                }
            }
        }

        [Test]
        public void 步骤完整_五个工步都按序执行并留下流水()
        {
            var world = NewWorld();
            RunLoop(world, out var order);

            Assert.IsTrue(order.IsComplete, "工单应跑完五个工步");

            // 等成熟期间的维护浇水也会进流水，因此按工单顺序在流水里逐项匹配计划工步。
            var matched = 0;
            foreach (var entry in world.Log)
            {
                if (entry.FieldId != "A1")
                {
                    continue;
                }

                if (matched < LoopSteps.Length && entry.Step == LoopSteps[matched])
                {
                    matched++;
                }
            }

            Assert.AreEqual(LoopSteps.Length, matched, "五个工步都应按顺序执行，各留下一条流水");
            Assert.AreEqual(FieldState.Harvested, world.GetField("A1").State, "最后一步应是收获");
        }

        [Test]
        public void 背包守恒_全流程结束后三本账收支相抵()
        {
            var world = NewWorld();
            RunLoop(world, out _);

            Assert.IsTrue(world.Ledger.Verify(out var error), error);
            Assert.AreEqual(SeedCount - 1, world.Ledger.SeedsOf(CropCatalog.RadishId), "播种消耗 1 颗种子");
            Assert.AreEqual(1, world.Ledger.SeedsRemoved, "只播种过一次");
            Assert.AreEqual(3, world.Ledger.ProduceOf(CropCatalog.RadishId), "萝卜每次收获 3 个");
            Assert.AreEqual(3, world.Ledger.ProduceAdded, "产出仓的入账应等于收获总量");
            Assert.AreEqual(0, world.Ledger.HeldOf("ahe", CropCatalog.RadishId), "演示流程里随身为 0");
        }

        [Test]
        public void 时间推进_不跨日不会成熟且萝卜第4天收获()
        {
            var idle = NewWorld();
            AssignLoop(idle, out _);
            for (var i = 0; i < 50; i++)
            {
                idle.AdvanceWorkOrder("ahe");
            }

            Assert.AreEqual(1, idle.Clock.Day, "没有推进时间");
            Assert.AreNotEqual(FieldState.Mature, idle.GetField("A1").State, "不跨日不该成熟");
            Assert.AreEqual(0f, idle.GetField("A1").Growth, 0.0001f, "不跨日不该生长");

            var world = NewWorld();
            RunLoop(world, out _);

            var harvest = world.Log[world.Log.Count - 1];
            Assert.AreEqual(WorkStep.Harvest, harvest.Step);
            Assert.AreEqual(4, harvest.Day, "第 1 天播种，萝卜 3 个游戏日后成熟，第 4 天收获");
            Assert.AreEqual(4, world.Clock.Day, "等待成熟只能靠跨日结算推进");
        }
    }
}
