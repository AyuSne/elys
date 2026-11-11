using System.Collections.Generic;
using ElysFarm.Core;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// 一句话指挥的领域侧：玩家意图被拆成一张工单，工单按顺序执行五个工步。
    /// 测试只打 <see cref="FarmWorld"/> 这条缝——派单是否改动状态、执行顺序、账本守恒，
    /// 都通过它的外部行为断言。
    /// </summary>
    public class WorkOrderTests
    {
        static FarmWorld NewWorld(int seeds = 10)
        {
            var world = new FarmWorld(new FarmClock());
            world.AddField("A1", new FarmPosition(0f, 2f));
            world.AddNpc("ahe", "阿禾", new FarmPosition(0f, -4f));
            world.SetProduceStore(new FarmPosition(0f, -4f));
            if (seeds > 0)
            {
                world.AddSeeds(CropCatalog.RadishId, seeds);
            }

            return world;
        }

        static readonly WorkStep[] PlantRadish =
        {
            WorkStep.Sow,
            WorkStep.Water,
            WorkStep.Fertilize,
            WorkStep.Weed,
            WorkStep.Harvest
        };

        [Test]
        public void 一句话派出的工单按五个工步顺序执行到收获()
        {
            var world = NewWorld();
            var field = world.GetField("A1");

            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", CropCatalog.RadishId, PlantRadish, out var order, out var reason),
                reason);
            CollectionAssert.AreEqual(PlantRadish, order.Steps, "工单应记录完整的五个工步");
            Assert.AreEqual(FieldState.Empty, field.State, "派单本身不改变地块状态");
            Assert.AreEqual(10, world.Ledger.SeedsOf(CropCatalog.RadishId), "派单本身不扣种子");
            Assert.AreEqual(0, world.Log.Count, "派单本身不执行工步");

            var executed = new List<WorkStep>();
            for (var guard = 0; guard < 400 && !order.IsComplete; guard++)
            {
                var before = order.CompletedCount;
                var result = world.AdvanceWorkOrder("ahe");
                if (order.CompletedCount > before)
                {
                    executed.Add(order.Steps[before]);
                }

                if (!result.Executed)
                {
                    world.AdvanceDays(1);
                }
            }

            CollectionAssert.AreEqual(PlantRadish, executed, "应按工单顺序执行五个工步");
            Assert.AreEqual(FieldState.Harvested, field.State);
            Assert.AreEqual(9, world.Ledger.SeedsOf(CropCatalog.RadishId), "播种扣 1 颗种子");
            Assert.AreEqual(3, world.Ledger.ProduceOf(CropCatalog.RadishId), "萝卜每次收获 3 个");
            Assert.IsTrue(world.Ledger.Verify(out var error), error);
            Assert.IsNull(world.WorkOrderOf("ahe"), "工单完成后应清空");
        }

        [Test]
        public void 小麦工单会自己补水直到收获()
        {
            var world = NewWorld(seeds: 5);
            world.AddSeeds(CropCatalog.WheatId, 5);
            var field = world.GetField("A1");
            var orderSteps = new[]
            {
                WorkStep.Sow,
                WorkStep.Water,
                WorkStep.Fertilize,
                WorkStep.Weed,
                WorkStep.Harvest
            };

            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", CropCatalog.WheatId, orderSteps, out var order, out var reason),
                reason);

            var executed = new List<WorkStep>();
            for (var guard = 0; guard < 800 && !order.IsComplete; guard++)
            {
                var before = order.CompletedCount;
                var result = world.AdvanceWorkOrder("ahe");
                if (order.CompletedCount > before)
                {
                    executed.Add(order.Steps[before]);
                }

                if (!result.Executed)
                {
                    world.AdvanceDays(1);
                }
            }

            CollectionAssert.AreEqual(orderSteps, executed, "等待成熟期间可以插做维护，但五个工步仍按序完成");
            Assert.AreEqual(FieldState.Harvested, field.State, "长周期作物也要能自己等到收获");
            Assert.AreEqual(5, world.Ledger.ProduceOf(CropCatalog.WheatId));
            Assert.AreEqual(3, world.Ledger.SeedsOf(CropCatalog.WheatId), "小麦每次播种消耗 2 颗种子");
            Assert.IsTrue(world.Ledger.Verify(out var error), error);
        }

        [Test]
        public void 拿不到种子的工单被拒绝且不改变任何状态()
        {
            var world = NewWorld(seeds: 0);
            var field = world.GetField("A1");

            Assert.IsFalse(
                world.TryAssignWorkOrder("ahe", "A1", CropCatalog.RadishId, PlantRadish, out var order, out var reason),
                "没有种子不该接单");
            Assert.IsNull(order);
            Assert.AreEqual("种子库里没有萝卜种子了", reason, "拒绝理由应来自动作校验");
            Assert.IsNull(world.WorkOrderOf("ahe"));
            Assert.AreEqual(FieldState.Empty, field.State, "被拒绝的工单不能改动地块");
            Assert.AreEqual(0, world.Log.Count, "被拒绝的工单不能执行工步");
        }

        [Test]
        public void 空地上浇水的工单被拒绝并给出可读理由()
        {
            var world = NewWorld();
            var field = world.GetField("A1");

            Assert.IsFalse(
                world.TryAssignWorkOrder("ahe", "A1", null, new[] { WorkStep.Water }, out _, out var reason),
                "空地上不该接浇水的活");
            Assert.AreEqual("地里什么都没种", reason);
            Assert.AreEqual(FieldState.Empty, field.State);
            Assert.AreEqual(0, world.Log.Count);
        }
    }
}
