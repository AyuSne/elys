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
        public void 取消工单保留已经浇过的水与施过的肥()
        {
            var world = TwoNpcWorld();
            var field = world.GetField("A1");
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", CropCatalog.RadishId, PlantRadish, out _, out var reason),
                reason);
            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed, "第一步播种");
            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed, "第二步浇水");
            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed, "第三步施肥");
            Assert.AreEqual(1f, field.Water, 0.0001f);
            Assert.AreEqual(1f, field.Fertility, 0.0001f);

            Assert.IsTrue(world.TryCancelWorkOrder("ahe", out _, out var cancelReason), cancelReason);

            Assert.AreEqual(1f, field.Water, 0.0001f, "浇过的水不回滚");
            Assert.AreEqual(1f, field.Fertility, 0.0001f, "施过的肥不回滚");
            Assert.AreEqual(FieldState.Sown, field.State, "地块停在做完的那一步上，不退回空地");
            Assert.AreEqual(CropCatalog.RadishId, field.Crop?.Id, "种下去的作物还在");
            Assert.IsFalse(field.Can(WorkStep.Water, out var waterReason), "刚浇过，再浇水仍被拒绝");
            Assert.AreEqual(Field.WateredReason, waterReason);
            Assert.IsFalse(field.Can(WorkStep.Fertilize, out var fertileReason), "肥力还够，再施肥仍被拒绝");
            Assert.AreEqual(Field.FertileReason, fertileReason);
        }

        [Test]
        public void 取消后回待命_不再执行原来的工步()
        {
            var world = TwoNpcWorld();
            var field = world.GetField("A1");
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", CropCatalog.RadishId, PlantRadish, out _, out var reason),
                reason);
            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed, "先播下种");
            var logCount = world.Log.Count;

            Assert.IsTrue(world.TryCancelWorkOrder("ahe", out var cancelled, out var cancelReason), cancelReason);

            Assert.AreEqual("A1", cancelled.FieldId);

            // 决定间隔走完再看意图：取消不会给 NPC 留一条旧工步。
            world.Tick(NpcPersona.Ahe.DecisionSeconds);
            Assert.AreEqual("待命", world.IntentOf("ahe"), "取消后回待命");

            var stale = world.AdvanceWorkOrder("ahe");
            Assert.IsFalse(stale.Executed, "旧工单已经撤掉，不该再执行");
            Assert.AreEqual("没有接单", stale.Reason);
            Assert.AreEqual(logCount, world.Log.Count, "不再产生新的工步流水");
            Assert.AreEqual(FieldState.Sown, field.State, "旧工单的下一步没有被做成自主行为");
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
        public void 只剩一步且它已不必做时跳过算完工并释放占位()
        {
            var world = TwoNpcWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Water).Executed);
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Fertilize).Executed);
            world.AdvanceDays(2);

            // 第 3 天挂一张「浇水」的工单：等下到地里时萝卜已经熟了，这一步不必再做了。
            Assert.AreEqual(FieldState.Growing, world.GetField("A1").State);
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", null, new[] { WorkStep.Water }, out _, out var reason),
                reason);

            world.AdvanceDays(1);
            Assert.AreEqual(FieldState.Mature, world.GetField("A1").State, "萝卜在第 4 天成熟");

            var skipped = world.AdvanceWorkOrder("ahe");
            Assert.IsFalse(skipped.Executed);
            Assert.AreEqual(StepOutcome.Skipped, skipped.Outcome);
            Assert.AreEqual(Field.MatureReason, skipped.Reason);
            Assert.IsNull(world.WorkOrderOf("ahe"), "最后一步跳过，工单就算做完");
            Assert.IsFalse(world.ClaimOf("A1").IsClaimed, "完工后释放占位");

            Assert.IsTrue(
                world.TryAssignWorkOrder("aya", "A1", null, new[] { WorkStep.Harvest }, out _, out var next),
                next);
        }

        [Test]
        public void 肥力还够这类等条件在意图行里显示等而不是没做成()
        {
            var world = TwoNpcWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Fertilize).Executed, "先把肥力加满");
            Assert.IsTrue(
                world.TryAssignWorkOrder(
                    "ahe",
                    "A1",
                    null,
                    new[] { WorkStep.Water, WorkStep.Fertilize },
                    out _,
                    out var reason),
                reason);
            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed, "先浇水，下一步轮到施肥");

            var line = world.IntentOf("ahe");
            StringAssert.StartsWith("等肥力降下来", line, "等条件要说「等什么」，不是「现在是什么样」");
            StringAssert.Contains("A1", line);
            StringAssert.Contains("施肥", line);
            StringAssert.DoesNotContain("没做成", line, "「没做成」只用于失败");

            var waiting = world.AdvanceWorkOrder("ahe");
            Assert.IsFalse(waiting.Executed);
            Assert.IsNotNull(world.WorkOrderOf("ahe"), "等条件的工单要留到条件满足");
            Assert.AreEqual(WorkStep.Fertilize, world.WorkOrderOf("ahe")?.NextStep);
        }

        [Test]
        public void 没什么草可除时意图行也显示等条件()
        {
            var world = TwoNpcWorld();
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", CropCatalog.RadishId, PlantRadish, out _, out var reason),
                reason);
            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed, "第一步播种");
            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed, "第二步浇水");
            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed, "第三步施肥");

            var line = world.IntentOf("ahe");
            StringAssert.StartsWith("等草长出来", line, "没什么草可除也是等条件，要说清等什么");
            StringAssert.Contains("除草", line);
            StringAssert.DoesNotContain("没做成", line);
            Assert.AreEqual(WorkStep.Weed, world.WorkOrderOf("ahe")?.NextStep, "等草长出来，工单保留");
        }

        [Test]
        public void 真做不下去时意图行显示没做成并带理由()
        {
            var world = TwoNpcWorld();
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", CropCatalog.RadishId, new[] { WorkStep.Sow }, out _, out var reason),
                reason);

            // 接单之后种子被别的活吃光：这一步再也做不成，不能显示成「等」。
            Assert.IsTrue(world.Ledger.TryTakeSeeds(CropCatalog.RadishId, 10), "把种子库取空");

            var line = world.IntentOf("ahe");
            StringAssert.StartsWith("没做成：", line, "真正的失败才说没做成");
            StringAssert.Contains("种子库里没有萝卜种子了", line, "失败要带理由");
            StringAssert.Contains("A1", line);
            StringAssert.Contains("播种", line);

            var failed = world.AdvanceWorkOrder("ahe");
            Assert.AreEqual(StepOutcome.Failed, failed.Outcome);
            Assert.IsFalse(failed.Executed);
            Assert.IsNull(world.WorkOrderOf("ahe"), "做不下去的工单不再挂着");
            Assert.IsFalse(world.ClaimOf("A1").IsClaimed, "失败要释放占位");
        }

        [Test]
        public void 作物熟了以后没意义的工步记为跳过_工单继续到收获()
        {
            var world = TwoNpcWorld();
            var field = world.GetField("A1");
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Water).Executed);
            world.AdvanceDays(2);
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Water).Executed, "再浇一次，让它在接到工单后熟");
            world.AdvanceDays(1);

            // 第 4 天接单：作物第二天就熟，施肥这一步到时已经没有意义。
            Assert.AreEqual(FieldState.Growing, field.State);
            Assert.IsTrue(
                world.TryAssignWorkOrder(
                    "ahe",
                    "A1",
                    null,
                    new[] { WorkStep.Fertilize, WorkStep.Harvest },
                    out var order,
                    out var reason),
                reason);
            world.AdvanceDays(1);
            Assert.AreEqual(FieldState.Mature, field.State, "萝卜熟了");

            var line = world.IntentOf("ahe");
            StringAssert.StartsWith("不用施肥", line, "不必做的工步说「不用…」，不是「等」也不是「没做成」");
            StringAssert.Contains("A1", line);
            StringAssert.Contains("已经熟了", line);

            var skipped = world.AdvanceWorkOrder("ahe");
            Assert.AreEqual(StepOutcome.Skipped, skipped.Outcome);
            Assert.IsFalse(skipped.Executed);
            Assert.IsNotNull(world.WorkOrderOf("ahe"), "跳过一步不等于整张工单作废");
            Assert.AreEqual(WorkStep.Harvest, order.NextStep, "跳过之后接着排到收获");
            Assert.AreEqual("ahe", world.ClaimOf("A1").NpcId, "跳过不释放占位");

            var harvested = world.AdvanceWorkOrder("ahe");
            Assert.IsTrue(harvested.Executed, "跳过后照样收得成");
            Assert.AreEqual(WorkStep.Harvest, harvested.Step);
            Assert.AreEqual(3, world.Ledger.ProduceOf(CropCatalog.RadishId));
            Assert.IsNull(world.WorkOrderOf("ahe"), "收获完，工单结束");
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
