using System.Collections.Generic;
using System.Linq;
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

        static FarmWorld TwoNpcWorld(NpcPersona ahePersona = null)
        {
            var world = new FarmWorld(new FarmClock());
            world.AddField("A1", new FarmPosition(0f, 2f));
            world.AddNpc("ahe", "阿禾", new FarmPosition(0f, -4f), ahePersona ?? NpcPersona.Ahe);
            world.AddNpc("aya", "阿芽", new FarmPosition(2f, -4f), NpcPersona.Aya);
            world.SetProduceStore(new FarmPosition(0f, -4f));
            world.AddSeeds(CropCatalog.RadishId, 10);
            return world;
        }

        [Test]
        public void 接单即占住地块_别人再对这块地下令得到带名字的拒绝()
        {
            var world = TwoNpcWorld();
            var field = world.GetField("A1");

            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", CropCatalog.RadishId, PlantRadish, out _, out var reason),
                reason);

            var claim = world.ClaimOf("A1");
            Assert.IsTrue(claim.IsClaimed, "接单即占住整块地");
            Assert.AreEqual("ahe", claim.NpcId);
            Assert.AreEqual("阿禾", claim.NpcName);
            Assert.AreEqual("阿禾", world.FieldClaims.Single(c => c.FieldId == "A1").NpcName, "抽屉据此显示地块归属");

            Assert.IsFalse(
                world.TryAssignWorkOrder("aya", "A1", CropCatalog.RadishId, PlantRadish, out var order, out var rejected),
                "同一块地不能同时属于两张工单");
            Assert.IsNull(order);
            Assert.AreEqual("阿禾正在弄这块地", rejected, "拒绝要说清是谁占着");
            Assert.IsNull(world.WorkOrderOf("aya"), "被拒的那次不能登记工单");
            Assert.AreEqual(FieldState.Empty, field.State, "被拒的那次不能改动地块");
            Assert.AreEqual(10, world.Ledger.SeedsOf(CropCatalog.RadishId), "被拒的那次不能扣种子");

            var blocked = world.ExecuteStep("aya", "A1", WorkStep.Sow);
            Assert.IsFalse(blocked.Executed, "直接指向被占地块的工步也要过占位校验");
            Assert.AreEqual("阿禾正在弄这块地", blocked.Reason);
            Assert.IsFalse(
                world.RankCandidates("aya").Any(c => c.FieldId == "A1" && c.IsValid),
                "别人占着的地块不再给出有效候选");
        }

        [Test]
        public void 工单完成后地块立刻可以被别人认领()
        {
            var world = TwoNpcWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed, "先种上，才有浇水可做");

            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", null, new[] { WorkStep.Water }, out var order, out var reason),
                reason);
            Assert.IsTrue(world.ClaimOf("A1").IsClaimed, "接单即占住");

            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed);
            Assert.IsTrue(order.IsComplete);
            Assert.IsNull(world.WorkOrderOf("ahe"), "工单完成后应清空");
            Assert.IsFalse(world.ClaimOf("A1").IsClaimed, "工单完成后占位立刻释放");

            Assert.IsTrue(
                world.TryAssignWorkOrder("aya", "A1", null, new[] { WorkStep.Fertilize }, out _, out var next),
                next);
            Assert.AreEqual("aya", world.ClaimOf("A1").NpcId, "释放后别人马上能接手");
        }

        [Test]
        public void 取消工单会释放占位但不回滚已完成的工步()
        {
            var world = TwoNpcWorld();
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", CropCatalog.RadishId, PlantRadish, out _, out var reason),
                reason);
            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed, "先播下种");

            Assert.IsTrue(world.TryCancelWorkOrder("ahe", out var cancelled, out var cancelReason), cancelReason);
            Assert.AreEqual("A1", cancelled.FieldId, "取消的是这张工单");
            Assert.IsNull(world.WorkOrderOf("ahe"), "取消后不再挂工单");
            Assert.IsFalse(world.ClaimOf("A1").IsClaimed, "取消立刻释放占位");
            Assert.AreEqual(FieldState.Sown, world.GetField("A1").State, "已完成的工步不回滚");
            Assert.AreEqual(9, world.Ledger.SeedsOf(CropCatalog.RadishId), "播下去的种子不会退回来");

            Assert.IsFalse(world.TryCancelWorkOrder("ahe", out _, out var empty), "没有工单时取消要给理由");
            Assert.AreEqual("没有接单", empty);

            Assert.IsTrue(
                world.TryAssignWorkOrder("aya", "A1", null, new[] { WorkStep.Water }, out _, out var next),
                next);
            Assert.AreEqual("aya", world.ClaimOf("A1").NpcId, "取消释放后别人马上能接手");
        }

        [Test]
        public void 去休息不释放占位_回来还能接着干()
        {
            // 干两步就累垮、一觉睡过两个游戏日的人设：用来验证休息不算「没进展」。
            var fragile = new NpcPersona(
                "fragile",
                decisionSeconds: 0f,
                exertionPerStep: 0.45f,
                restThreshold: Npc.MinStamina,
                restSeconds: FarmClock.SecondsPerDay * 2f + 50f,
                restRecovery: 0.60f,
                diligence: 1f,
                distanceWeight: 0.15f);

            var world = TwoNpcWorld(fragile);
            world.AddSeeds(CropCatalog.WheatId, 5);

            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", CropCatalog.WheatId, PlantRadish, out _, out var reason),
                reason);
            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed, "第一步播种");
            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed, "第二步浇水后体力见底");
            Assert.IsTrue(world.GetNpc("ahe").IsResting, "体力降到阈值应去休息");

            world.AdvanceDays(2);
            Assert.IsTrue(world.GetNpc("ahe").IsResting, "这一觉还没睡完");
            Assert.AreEqual(
                WorkStep.Fertilize,
                world.WorkOrderOf("ahe")?.NextStep,
                "去休息不释放工单，回来还接着排到施肥");
            Assert.AreEqual("ahe", world.ClaimOf("A1").NpcId, "去休息不释放占位，回来还能接着干");

            world.Tick(fragile.RestSeconds);
            Assert.IsFalse(world.GetNpc("ahe").IsResting, "休息够时长就该起身");
            var resumed = world.AdvanceWorkOrder("ahe");
            Assert.IsTrue(resumed.Executed, "休息结束后接着干原工单");
            Assert.AreEqual(WorkStep.Fertilize, resumed.Step);
            Assert.AreEqual(WorkStep.Weed, world.WorkOrderOf("ahe")?.NextStep, "工单推进到下一位除草");
            Assert.AreEqual("ahe", world.ClaimOf("A1").NpcId, "接着干时占位仍在自己手里");
        }

        [Test]
        public void 对同一块地的工单完成前换地接单会释放原来的占位()
        {
            var world = TwoNpcWorld();
            world.AddField("A2", new FarmPosition(6f, 2f));

            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", CropCatalog.RadishId, PlantRadish, out _, out var reason),
                reason);
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A2", CropCatalog.RadishId, PlantRadish, out _, out var again),
                again);

            Assert.AreEqual("A2", world.WorkOrderOf("ahe")?.FieldId, "接单会替换这名 NPC 原来的工单");
            Assert.IsFalse(world.ClaimOf("A1").IsClaimed, "旧地块立刻释放");
            Assert.AreEqual("ahe", world.ClaimOf("A2").NpcId, "新地块归这名 NPC");
        }

        [Test]
        public void 超过一个游戏日没有进展的工单自动放弃并释放占位()
        {
            var world = TwoNpcWorld();
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", CropCatalog.RadishId, PlantRadish, out _, out var reason),
                reason);
            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed, "播下种；此后没人再推进这张工单");

            world.AdvanceDays(1);
            Assert.IsNotNull(world.WorkOrderOf("ahe"), "才一个游戏日，工单先留着");
            Assert.AreEqual("ahe", world.ClaimOf("A1").NpcId, "留着的工单也留着占位");

            world.AdvanceDays(1);
            Assert.IsNull(world.WorkOrderOf("ahe"), "超过一个游戏日没有进展就自动放弃");
            Assert.IsFalse(world.ClaimOf("A1").IsClaimed, "放弃后释放占位");
            Assert.IsTrue(
                world.TryAssignWorkOrder("aya", "A1", null, new[] { WorkStep.Water }, out _, out var next),
                "释放后别人能接这块地：" + next);
        }

        [Test]
        public void 被拒且无法继续时立刻释放占位()
        {
            var world = TwoNpcWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Water).Executed);
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Fertilize).Executed);
            world.AdvanceDays(2);

            // 第 3 天挂一张「浇水」的工单：等下到地里时萝卜已经熟了，这一步永远做不成。
            Assert.AreEqual(FieldState.Growing, world.GetField("A1").State);
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", null, new[] { WorkStep.Water }, out _, out var reason),
                reason);

            world.AdvanceDays(1);
            Assert.AreEqual(FieldState.Mature, world.GetField("A1").State, "萝卜在第 4 天成熟");

            var blocked = world.AdvanceWorkOrder("ahe");
            Assert.IsFalse(blocked.Executed);
            Assert.AreEqual("已经熟了，先收了吧", blocked.Reason);
            Assert.IsNull(world.WorkOrderOf("ahe"), "做不下去的工单不能一直挂着");
            Assert.IsFalse(world.ClaimOf("A1").IsClaimed, "放弃后释放占位");

            Assert.IsTrue(
                world.TryAssignWorkOrder("aya", "A1", null, new[] { WorkStep.Harvest }, out _, out var next),
                next);
        }

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
