using System.Linq;
using ElysFarm.Core;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #6 决策可视化的领域接缝：测试只打 <see cref="FarmWorld.Inspect"/>，
    /// 断言观察者能在面板上看到的五项——记忆、意图、候选与否决原因、模型调用、状态条。
    /// 面板与渲染不参与测试。
    /// </summary>
    public class DecisionInspectorTests
    {
        static FarmWorld NewWorld()
        {
            var world = new FarmWorld(new FarmClock());
            world.AddField("A1", new FarmPosition(0f, 2f));
            world.AddNpc("ahe", "阿禾", new FarmPosition(0f, -4f));
            world.AddNpc("aya", "阿芽", new FarmPosition(4f, -4f), NpcPersona.Aya);
            world.SetProduceStore(new FarmPosition(0f, -4f));
            world.AddSeeds(CropCatalog.RadishId, 10);
            return world;
        }

        static readonly WorkStep[] PlantPlan =
        {
            WorkStep.Sow,
            WorkStep.Water,
            WorkStep.Fertilize,
            WorkStep.Weed,
            WorkStep.Harvest
        };

        [Test]
        public void 决策快照包含五项且状态条随工步更新()
        {
            var world = NewWorld();
            var call = new ModelCallInfo("deepseek-flash", 1.25d, 321, 45, ModelCallOutcome.Ok);

            var before = world.Inspect("ahe", call);

            Assert.AreEqual("ahe", before.NpcId);
            Assert.AreEqual("阿禾", before.NpcName);
            Assert.IsNotNull(before.Memories, "第 1 项：检索命中的记忆");
            Assert.AreEqual(0, before.Memories.Count, "还没干过活，没有记忆");
            Assert.AreEqual("待命", before.Intent, "第 2 项：当前意图");
            Assert.Greater(before.Candidates.Count, 0, "第 3 项：候选动作");
            Assert.AreEqual("deepseek-flash", before.ModelCall.Model, "第 4 项：模型名");
            Assert.AreEqual(1.25d, before.ModelCall.LatencySeconds, 0.001d, "第 4 项：耗时");
            Assert.AreEqual(321, before.ModelCall.PromptTokens, "第 4 项：输入 token");
            Assert.AreEqual(45, before.ModelCall.CompletionTokens, "第 4 项：输出 token");
            Assert.AreEqual(1f, before.Status.Stamina, 0.001f, "第 5 项：体力条");
            Assert.That(before.Status.Mood, Is.InRange(0f, 1f), "第 5 项：心情条");
            Assert.That(before.Status.Hunger, Is.InRange(0f, 1f), "第 5 项：饥饿条");

            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            var after = world.Inspect("ahe");

            Assert.Less(after.Status.Stamina, before.Status.Stamina, "体力应随工步消耗");
            Assert.Greater(after.Status.Hunger, before.Status.Hunger, "干完活更饿");
            Assert.Less(after.Status.Mood, before.Status.Mood, "干活让心情下降");

            world.AdvanceDays(1);
            var settled = world.Inspect("ahe").Status;
            Assert.Less(settled.Hunger, after.Status.Hunger, "跨日进食后饥饿回落");
            Assert.Greater(settled.Mood, after.Status.Mood, "跨日休息后心情回升");
        }

        [Test]
        public void 当前意图跟随工单与自主决定()
        {
            var world = NewWorld();
            Assert.AreEqual("待命", world.Inspect("ahe").Intent, "开局没有工单也没有决定");

            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", CropCatalog.RadishId, PlantPlan, out _, out var reason),
                reason);
            var orderIntent = world.Inspect("ahe").Intent;
            StringAssert.Contains("A1", orderIntent);
            StringAssert.Contains("播种", orderIntent);

            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed);
            orderIntent = world.Inspect("ahe").Intent;
            StringAssert.Contains("浇水", orderIntent, "工单推进后意图应变成下一步");

            var autonomous = NewWorld();
            Assert.IsTrue(autonomous.TryDecide("ahe", out var candidate, out var decideReason), decideReason);
            Assert.AreEqual(WorkStep.Sow, candidate.Step);

            var autonomousIntent = autonomous.Inspect("ahe").Intent;
            StringAssert.Contains(candidate.FieldId, autonomousIntent);
            StringAssert.Contains("播种", autonomousIntent);

            var coolingDown = new FarmWorld(new FarmClock());
            coolingDown.AddField("A1", new FarmPosition(0f, 2f));
            coolingDown.AddNpc("ahe", "阿禾", new FarmPosition(0f, -4f), NpcPersona.Ahe);
            coolingDown.AddSeeds(CropCatalog.RadishId, 5);
            coolingDown.Tick(NpcPersona.Ahe.DecisionSeconds);
            Assert.IsTrue(coolingDown.TryDecide("ahe", out var pending, out var decisionReason), decisionReason);
            Assert.IsTrue(coolingDown.ExecuteStep("ahe", pending.FieldId, pending.Step).Executed);
            StringAssert.Contains(
                "琢磨",
                coolingDown.Inspect("ahe").Intent,
                "两次决定之间要说明还在琢磨，不能显示成待命");
        }

        [Test]
        public void 候选动作的否决原因直接来自动作校验()
        {
            var world = NewWorld();
            var snapshot = world.Inspect("ahe");

            var rejected = snapshot.Candidates.First(c => !c.IsValid);

            var attempt = world.ExecuteStep("ahe", rejected.FieldId, rejected.Step);
            Assert.IsFalse(attempt.Executed, "被否决的候选不能再执行");
            Assert.AreEqual(attempt.Reason, rejected.RejectionReason, "快照里的否决原因必须就是执行时动作校验给出的理由");
        }

        [Test]
        public void 记忆最多三条且只属于该NPC()
        {
            var world = NewWorld();
            world.AddField("A2", new FarmPosition(6f, 2f));

            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Water).Executed);
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Fertilize).Executed);
            Assert.IsTrue(world.ExecuteStep("aya", "A2", WorkStep.Sow).Executed);

            var ahe = world.Inspect("ahe");
            Assert.AreEqual(3, ahe.Memories.Count, "面板固定展示三条检索命中的记忆");
            Assert.IsTrue(ahe.Memories.All(m => m.Text.Contains("A1")), "阿禾只看得到自己的记忆");
            Assert.IsTrue(ahe.Memories.All(m => m.Weight > 0f && m.Weight <= 1f), "权重应落在 0..1");
            StringAssert.Contains("播下了", ahe.Memories[0].Text, "同样新旧时，重要性更高的播种应排第一");
            for (var i = 1; i < ahe.Memories.Count; i++)
            {
                Assert.GreaterOrEqual(ahe.Memories[i - 1].Weight, ahe.Memories[i].Weight, "按权重降序");
            }

            var aya = world.Inspect("aya");
            Assert.AreEqual(1, aya.Memories.Count, "阿芽只看得到自己做过的事");
            StringAssert.Contains("A2", aya.Memories[0].Text);

            world.AdvanceDays(1);
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Weed).Executed);
            Assert.AreEqual(3, world.Inspect("ahe").Memories.Count, "多于三条时只返回权重最高的三条");
        }

        [Test]
        public void 记忆检索优先命中当前意图指向的地块()
        {
            var world = NewWorld();
            world.AddField("A2", new FarmPosition(6f, 2f));

            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Water).Executed);
            Assert.IsTrue(world.ExecuteStep("ahe", "A2", WorkStep.Sow).Executed);
            Assert.IsTrue(world.ExecuteStep("ahe", "A2", WorkStep.Water).Executed);

            world.AdvanceDays(1);
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A2", CropCatalog.RadishId, new[] { WorkStep.Water }, out _, out var reason),
                reason);

            var snapshot = world.Inspect("ahe");
            StringAssert.Contains("A2", snapshot.Intent);
            StringAssert.Contains("A2", snapshot.Memories[0].Text, "当前意图指向 A2，检索应先命中 A2 的记忆");
            StringAssert.Contains("水", snapshot.Memories[0].Text, "A2 的记忆里与「浇水」最相关");
        }

        [Test]
        public void 同一件事新的记忆权重更高()
        {
            var world = NewWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Water).Executed);
            world.AdvanceDays(1);
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Water).Executed);

            var waters = world.Inspect("ahe").Memories
                .Where(m => m.Text.Contains("浇了水"))
                .ToList();

            Assert.AreEqual(2, waters.Count, "两条浇水记忆都应被检索到");
            Assert.AreEqual(2, waters[0].Day, "新记忆应排在旧记忆之前");
            Assert.Greater(waters[0].Weight, waters[1].Weight, "时间衰减让旧记忆权重更低");
        }

        [Test]
        public void 决策快照带出地块归属_被占的地块在别人那里只有否决原因()
        {
            var world = NewWorld();
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", CropCatalog.RadishId, PlantPlan, out _, out var reason),
                reason);

            var ahe = world.Inspect("ahe");
            Assert.AreEqual("阿禾", ahe.FieldClaims.Single(c => c.FieldId == "A1").NpcName, "抽屉据此显示地块归属");

            var aya = world.Inspect("aya");
            Assert.IsFalse(aya.Candidates.Any(c => c.FieldId == "A1" && c.IsValid), "别人占着的地块不给有效候选");
            Assert.IsTrue(
                aya.Candidates.Any(c => c.FieldId == "A1" && c.RejectionReason == "阿禾正在弄这块地"),
                "被占的候选要带着占位者的名字被否决");
        }

        [Test]
        public void 别人正要去的地块在候选里给出同一句否决理由()
        {
            var world = NewWorld();
            Assert.IsTrue(world.TryDecide("ahe", out var pick, out var reason), reason);
            Assert.AreEqual("A1", pick.FieldId);
            StringAssert.Contains("自主：A1", world.Inspect("ahe").Intent, "意图行与自主目标登记同源");

            var aya = world.Inspect("aya");
            Assert.IsFalse(aya.Candidates.Any(c => c.IsValid), "唯一的 A1 已经有人正要去");
            Assert.IsTrue(
                aya.Candidates.Where(c => c.FieldId == "A1").All(c => c.RejectionReason == "阿禾正在弄这块地"),
                "被自主目标占住的候选，理由与工单占位同一句说法");
        }

        [Test]
        public void 读取决策快照不改变任何游戏状态()
        {
            var world = NewWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);

            var npc = world.GetNpc("ahe");
            var day = world.Clock.Day;
            var daySeconds = world.Clock.DaySeconds;
            var seeds = world.Ledger.SeedsOf(CropCatalog.RadishId);
            var logCount = world.Log.Count;
            var stamina = npc.Stamina;
            var mood = npc.Mood;
            var hunger = npc.Hunger;

            world.Inspect("ahe", new ModelCallInfo("fake", 1d, 1, 1, ModelCallOutcome.Ok));
            world.Inspect("ahe");

            Assert.AreEqual(day, world.Clock.Day);
            Assert.AreEqual(daySeconds, world.Clock.DaySeconds, 0.0001f);
            Assert.AreEqual(seeds, world.Ledger.SeedsOf(CropCatalog.RadishId));
            Assert.AreEqual(logCount, world.Log.Count);
            Assert.AreEqual(stamina, npc.Stamina, 0.0001f);
            Assert.AreEqual(mood, npc.Mood, 0.0001f);
            Assert.AreEqual(hunger, npc.Hunger, 0.0001f);
        }
    }
}
