using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ElysFarm.Core;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #5 三个 NPC 与效用打分：人设只通过决策参数进入领域层。
    /// 测试仍然只打 <see cref="FarmWorld"/> 这条缝——候选分项、选择速度、休息与出勤，
    /// 都通过它暴露的公开状态（候选、NPC 的体力与休息、流水）断言；不直接测
    /// <see cref="UtilityScorer"/> 的排序实现。
    /// </summary>
    public class NpcDecisionTests
    {
        /// <summary>
        /// 两块同品种、不同日期状态的地：A1 远且仍缺水，A2 近且刚浇满。
        /// 这样距离敏感度与需求权重都会影响排序，而不是只比较同一块地。
        /// </summary>
        static FarmWorld ScoringWorld()
        {
            var world = new FarmWorld(new FarmClock());
            world.AddField("A1", new FarmPosition(0f, 100f));
            world.AddField("A2", new FarmPosition(0f, 2f));
            world.AddNpc("ahe", "阿禾", new FarmPosition(0f, -5f), NpcPersona.Ahe);
            world.AddNpc("aya", "阿芽", new FarmPosition(0f, -5f), NpcPersona.Aya);
            world.AddNpc("atu", "阿土", new FarmPosition(0f, -5f), NpcPersona.Atu);
            world.SetProduceStore(new FarmPosition(0f, -5f));
            world.AddSeeds(CropCatalog.RadishId, 30);

            foreach (var fieldId in new[] { "A1", "A2" })
            {
                world.ExecuteStep("ahe", fieldId, WorkStep.Sow);
                world.ExecuteStep("ahe", fieldId, WorkStep.Water);
            }

            // 两个游戏日后两块地都长草、掉肥力；再单独把 A2 浇满，只留 A1 的缺水候选。
            world.AdvanceDays(2);
            world.ExecuteStep("ahe", "A2", WorkStep.Water);

            return world;
        }

        [Test]
        public void 候选工步带需求人设体力与距离的分项权重()
        {
            var world = ScoringWorld();

            var candidate = world.RankCandidates("aya")
                .Single(c => c.FieldId == "A2" && c.Step == WorkStep.Fertilize);

            Assert.IsTrue(candidate.IsValid, "A2 刚浇过水，施肥应是有效候选");
            Assert.AreEqual(NpcPersona.Aya.Diligence, candidate.PersonaWeight, 0.0001f, "分项要带该 NPC 的人设权重");
            Assert.Greater(candidate.Need, 0f, "需求项应可单独看到");
            Assert.Greater(candidate.StaminaFactor, 0f, "体力系数应可单独看到");
            Assert.Greater(candidate.DistancePenalty, 0f, "距离扣分应可单独看到");

            Assert.AreEqual(
                candidate.Need * candidate.PersonaWeight * candidate.StaminaFactor - candidate.DistancePenalty,
                candidate.Score,
                0.0001f,
                "总分应等于 需求 × 人设 × 体力 − 距离扣分，评审才能逐项解释");
        }

        [Test]
        public void 同一批地块上怕走路的阿芽与阿土排出不同首选()
        {
            var world = ScoringWorld();

            var aya = world.NextStep("aya");
            var atu = world.NextStep("atu");

            Assert.IsTrue(aya.HasValue, "阿芽应有可干的活");
            Assert.IsTrue(atu.HasValue, "阿土应有可干的活");
            Assert.AreEqual("A2", aya.Value.FieldId, "阿芽怕走路，应选近处的 A2");
            Assert.AreEqual(WorkStep.Fertilize, aya.Value.Step, "A2 刚浇过，最急的是施肥");
            Assert.AreEqual("A1", atu.Value.FieldId, "阿土不怕走路，应选需求更高的远处 A1");
            Assert.AreEqual(WorkStep.Water, atu.Value.Step, "A1 缺水，浇水应排第一");
        }

        [Test]
        public void 决定速度按人设拉开_阿芽先动阿土最后()
        {
            var world = new FarmWorld(new FarmClock());
            world.AddField("A1", new FarmPosition(0f, 2f));
            world.AddNpc("ahe", "阿禾", new FarmPosition(0f, -5f), NpcPersona.Ahe);
            world.AddNpc("aya", "阿芽", new FarmPosition(0f, -5f), NpcPersona.Aya);
            world.AddNpc("atu", "阿土", new FarmPosition(0f, -5f), NpcPersona.Atu);
            world.AddSeeds(CropCatalog.RadishId, 10);

            Assert.IsFalse(world.TryDecide("ahe", out _, out var aheReason), "开局还没到阿禾的决定时刻");
            Assert.AreEqual("还没拿定主意", aheReason);
            Assert.IsFalse(world.TryDecide("atu", out _, out _), "阿土最慢");

            world.Tick(NpcPersona.Aya.DecisionSeconds + 0.01f);
            Assert.IsTrue(world.TryDecide("aya", out var ayaCandidate, out _), "阿芽决定最快");
            Assert.IsTrue(ayaCandidate.IsValid);
            Assert.IsFalse(world.TryDecide("aya", out _, out var decidedReason), "一次决定要消耗决定间隔");
            Assert.AreEqual("还没拿定主意", decidedReason);

            Assert.IsTrue(world.ExecuteStep("aya", ayaCandidate.FieldId, ayaCandidate.Step).Executed);
            Assert.IsFalse(world.TryDecide("aya", out _, out var cooldownReason), "执行不会重置已经开始的间隔");
            Assert.AreEqual("还没拿定主意", cooldownReason);
            Assert.IsFalse(world.TryDecide("ahe", out _, out _), "阿禾还没轮到");

            world.Tick(NpcPersona.Ahe.DecisionSeconds);
            Assert.IsTrue(world.TryDecide("ahe", out _, out _), "阿禾随后拿定主意");
            Assert.IsFalse(world.TryDecide("atu", out _, out _), "阿土仍最慢");

            world.Tick(NpcPersona.Atu.DecisionSeconds);
            Assert.IsTrue(world.TryDecide("atu", out _, out _), "阿土最后也拿定主意");
            Assert.IsTrue(world.TryDecide("aya", out _, out _), "等够决定间隔后又能决定");
        }

        [Test]
        public void 体力耗尽会中断当前工步并去休息()
        {
            var fragile = new NpcPersona(
                "fragile",
                decisionSeconds: 0f,
                exertionPerStep: 0.45f,
                restThreshold: Npc.MinStamina,
                restSeconds: 6f,
                restRecovery: 0.60f,
                diligence: 1f,
                distanceWeight: 0.15f);

            var world = new FarmWorld(new FarmClock());
            world.AddField("A1", new FarmPosition(0f, 2f));
            world.AddNpc("ahe", "阿禾", new FarmPosition(0f, -4f), fragile);
            world.SetProduceStore(new FarmPosition(0f, -4f));
            world.AddSeeds(CropCatalog.RadishId, 5);

            var plan = new[]
            {
                WorkStep.Sow,
                WorkStep.Water,
                WorkStep.Fertilize,
                WorkStep.Weed,
                WorkStep.Harvest
            };
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", CropCatalog.RadishId, plan, out var order, out var reason),
                reason);

            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed, "第一步播种");
            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed, "第二步浇水后体力见底");

            var npc = world.GetNpc("ahe");
            Assert.IsTrue(npc.IsResting, "体力降到阈值应中断农活去休息");
            var completed = order.CompletedCount;
            Assert.AreEqual(2, completed);

            var blocked = world.AdvanceWorkOrder("ahe");
            Assert.IsFalse(blocked.Executed, "休息中不能继续工单");
            Assert.AreEqual("正在休息，得缓一缓", blocked.Reason);
            Assert.AreEqual(completed, order.CompletedCount, "休息不能偷偷推进工单");
            Assert.AreEqual(FieldState.Sown, world.GetField("A1").State, "休息时不能改动地块");

            world.Tick(fragile.RestSeconds);

            Assert.IsFalse(npc.IsResting, "休息够时长就该起身");
            Assert.Greater(npc.Stamina, fragile.RestThreshold, "休息应恢复体力");

            var resumed = world.AdvanceWorkOrder("ahe");
            Assert.IsTrue(resumed.Executed, "休息后接着干原工单");
            Assert.AreEqual(WorkStep.Fertilize, resumed.Step);
            Assert.AreEqual(3, order.CompletedCount);
        }

        [Test]
        public void 同一批地块上三名NPC抢活摸鱼与磨蹭可区分()
        {
            var world = new FarmWorld(new FarmClock());

            // 同一批地块、同一批种子，没人有工单：三人都靠效用打分自己找活。
            for (var i = 0; i < 20; i++)
            {
                var x = -9.5f + (i % 5) * 4f;
                var z = 2f + (i / 5) * 3f;
                world.AddField($"F{i:00}", new FarmPosition(x, z));
            }

            world.AddNpc("ahe", "阿禾", new FarmPosition(0f, -5f), NpcPersona.Ahe);
            world.AddNpc("aya", "阿芽", new FarmPosition(0f, -5f), NpcPersona.Aya);
            world.AddNpc("atu", "阿土", new FarmPosition(0f, -5f), NpcPersona.Atu);
            world.SetProduceStore(new FarmPosition(0f, -5f));
            world.AddSeeds(CropCatalog.RadishId, 60);

            var firstActionAt = new Dictionary<string, float>();
            var actions = new Dictionary<string, int>();
            var restSeconds = new Dictionary<string, float>();
            foreach (var npc in world.Npcs)
            {
                firstActionAt[npc.Id] = float.NaN;
                actions[npc.Id] = 0;
                restSeconds[npc.Id] = 0f;
            }

            const float StepSeconds = 0.01f;
            const float WindowSeconds = 30f;

            for (var elapsed = 0f; elapsed < WindowSeconds; elapsed += StepSeconds)
            {
                foreach (var npc in world.Npcs)
                {
                    if (world.TryDecide(npc.Id, out var candidate, out _))
                    {
                        var result = world.ExecuteStep(npc.Id, candidate.FieldId, candidate.Step);
                        if (result.Executed)
                        {
                            actions[npc.Id]++;
                            if (float.IsNaN(firstActionAt[npc.Id]))
                            {
                                firstActionAt[npc.Id] = elapsed;
                            }
                        }
                    }

                    if (npc.IsResting)
                    {
                        restSeconds[npc.Id] += StepSeconds;
                    }
                }

                world.Tick(StepSeconds);
            }

            Assert.IsTrue(actions.Values.All(count => count > 0), "三名 NPC 都该干过活");
            Assert.Less(firstActionAt["aya"], firstActionAt["ahe"], "阿芽决定最快，先动");
            Assert.Less(firstActionAt["ahe"], firstActionAt["atu"], "阿土最慢，最后动");
            Assert.Greater(actions["ahe"], actions["aya"], "阿禾最勤，干的活最多");
            Assert.Greater(actions["ahe"], actions["atu"], "阿禾比磨蹭的阿土干得多");
            Assert.Greater(restSeconds["aya"], restSeconds["ahe"], "阿芽摸鱼，休息时间最长");
            Assert.Greater(restSeconds["aya"], restSeconds["atu"], "阿芽比阿土更常歇着");
            Assert.Greater(restSeconds["aya"], WindowSeconds / 2f, "阿芽有一半以上时间在摸鱼");
        }

        [Test]
        public async Task 不接模型也能把一句话变成三名NPC的农活()
        {
            var world = new FarmWorld(new FarmClock());
            world.AddField("A1", new FarmPosition(0f, 3f));
            world.AddField("A2", new FarmPosition(4f, 3f));
            world.AddField("A3", new FarmPosition(-4f, 3f));
            world.AddNpc("ahe", "阿禾", new FarmPosition(-1.5f, -3f), NpcPersona.Ahe);
            world.AddNpc("aya", "阿芽", new FarmPosition(0f, -3f), NpcPersona.Aya);
            world.AddNpc("atu", "阿土", new FarmPosition(1.5f, -3f), NpcPersona.Atu);
            world.SetProduceStore(new FarmPosition(-2.4f, -3f));
            world.AddSeeds(CropCatalog.RadishId, 10);

            var router = new IntentRouter();
            var result = await router.RouteAsync("把地都种上萝卜", world, world.GetNpc("ahe"));

            Assert.IsFalse(router.HasModel, "这条链路不接模型");
            Assert.IsTrue(result.UsedFallback, "应当走本地模板");
            Assert.IsTrue(result.Accepted, result.Reply);
            Assert.AreEqual("ahe", result.AssignedWorkOrder.NpcId);

            var actions = new Dictionary<string, int>();
            foreach (var npc in world.Npcs)
            {
                actions[npc.Id] = 0;
            }

            for (var elapsed = 0f; elapsed < 8f; elapsed += 0.1f)
            {
                foreach (var npc in world.Npcs)
                {
                    if (world.WorkOrderOf(npc.Id) != null)
                    {
                        if (world.AdvanceWorkOrder(npc.Id).Executed)
                        {
                            actions[npc.Id]++;
                        }

                        continue;
                    }

                    if (world.TryDecide(npc.Id, out var candidate, out _)
                        && world.ExecuteStep(npc.Id, candidate.FieldId, candidate.Step).Executed)
                    {
                        actions[npc.Id]++;
                    }
                }

                world.Tick(0.1f);
            }

            Assert.IsTrue(actions.Values.All(count => count > 0), "三名 NPC 都应在这条无模型链路里干过活");
            Assert.Greater(world.WorkOrderOf("ahe")?.CompletedCount ?? 0, 0, "接单的 NPC 应推进工单");
        }
    }
}
