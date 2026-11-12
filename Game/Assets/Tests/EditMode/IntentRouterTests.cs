using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElysFarm.Core;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// 「一句话指挥」的入口测试：只打 <see cref="IntentRouter"/> 与 <see cref="IModelClient"/> 两条缝。
    /// 模型永远用假实现，测试不联网；断言只看路由器公开的出口、台词与工单，
    /// 以及玩家输入有没有绕过意图出口改动 <see cref="FarmWorld"/>。
    /// </summary>
    public class IntentRouterTests
    {
        const string PlantRadishJson =
            "{\"outlet\":\"farm_command\",\"goal\":\"plant\",\"field\":\"east\",\"species\":\"radish\",\"reply\":\"\"}";

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

        sealed class ScriptedModelClient : IModelClient
        {
            readonly Func<ChatRequest, CancellationToken, Task<ChatResponse>> onChat;

            public ScriptedModelClient(string json)
                : this((_, __) => Task.FromResult(new ChatResponse(json, "fake-deepseek")))
            {
            }

            public ScriptedModelClient(Func<ChatRequest, CancellationToken, Task<ChatResponse>> onChat)
            {
                this.onChat = onChat;
            }

            public string ModelName => "fake-deepseek";

            public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
            {
                Calls++;
                return onChat(request, cancellationToken);
            }

            public int Calls { get; private set; }
        }

        [Test]
        public async Task 模型解析出农事指令时只排工单不改状态()
        {
            var world = NewWorld();
            var field = world.GetField("A1");
            var router = new IntentRouter(new ScriptedModelClient(PlantRadishJson));

            var result = await router.RouteAsync("把东边那块地种满萝卜", world, world.GetNpc("ahe"));

            Assert.AreEqual(IntentOutlet.FarmCommand, result.Outlet);
            Assert.IsTrue(result.Accepted, result.Reply);
            Assert.AreEqual("A1", result.FieldId, "「东边」应解析成东边的地块");
            Assert.AreEqual(CropCatalog.RadishId, result.SpeciesId);
            CollectionAssert.AreEqual(
                new[] { WorkStep.Sow, WorkStep.Water, WorkStep.Fertilize, WorkStep.Weed, WorkStep.Harvest },
                result.PlannedSteps,
                "种满萝卜应排出五个工步");
            Assert.IsNotNull(result.AssignedWorkOrder);
            Assert.IsFalse(result.UsedFallback);
            Assert.AreEqual(FieldState.Empty, field.State, "玩家输入不能直接改地块状态");
            Assert.AreEqual(10, world.Ledger.SeedsOf(CropCatalog.RadishId), "玩家输入不能直接扣种子");
            Assert.AreEqual(0, world.Log.Count, "路由本身不执行工步");
        }

        [Test]
        public async Task 模型超时后回退本地解析且照样能接单()
        {
            var world = NewWorld();
            var neverCompletes = new TaskCompletionSource<ChatResponse>();
            var router = new IntentRouter(new ScriptedModelClient((_, __) => neverCompletes.Task));

            var result = await router.RouteAsync(
                "把东边那块地种满萝卜",
                world,
                world.GetNpc("ahe"),
                deadlineSeconds: 0.05f);

            Assert.IsTrue(result.UsedFallback, "超时后应标记为本地降级");
            Assert.AreEqual(ModelCallOutcome.Timeout, result.Call.Outcome);
            Assert.AreEqual(IntentOutlet.FarmCommand, result.Outlet);
            Assert.IsTrue(result.Accepted, result.Reply);
            Assert.AreEqual("A1", result.FieldId);
            CollectionAssert.AreEqual(
                new[] { WorkStep.Sow, WorkStep.Water, WorkStep.Fertilize, WorkStep.Weed, WorkStep.Harvest },
                result.PlannedSteps);
            Assert.AreEqual(FieldState.Empty, world.GetField("A1").State, "路由不执行工步");
            Assert.AreEqual(10, world.Ledger.SeedsOf(CropCatalog.RadishId));
        }

        [Test]
        public async Task 去种月亮被拒绝并给出可读理由且不改变状态()
        {
            var world = NewWorld();
            var router = new IntentRouter();

            var result = await router.RouteAsync("去种月亮", world, world.GetNpc("ahe"));

            Assert.AreEqual(IntentOutlet.FarmCommand, result.Outlet);
            Assert.IsFalse(result.Accepted);
            StringAssert.Contains("月亮", result.Reply, "拒绝台词应点名种不出来的东西");
            Assert.IsNull(result.AssignedWorkOrder);
            Assert.IsNull(world.WorkOrderOf("ahe"));
            Assert.AreEqual(FieldState.Empty, world.GetField("A1").State);
            Assert.AreEqual(10, world.Ledger.SeedsOf(CropCatalog.RadishId));
            Assert.AreEqual(0, world.Log.Count);
        }

        [Test]
        public async Task 模型返回白名单外的动作时不改变任何状态()
        {
            var world = NewWorld();
            const string json =
                "{\"outlet\":\"farm_command\",\"goal\":\"dance\",\"field\":\"A1\",\"species\":\"radish\",\"reply\":\"\"}";
            var router = new IntentRouter(new ScriptedModelClient(json));

            var result = await router.RouteAsync("把东边那块地种满萝卜", world, world.GetNpc("ahe"));

            Assert.IsFalse(result.Accepted, "白名单外的动作不能接单");
            StringAssert.Contains("dance", result.Reply);
            Assert.IsFalse(result.UsedFallback, "这是模型语义拒绝，不是离线降级");
            Assert.IsNull(world.WorkOrderOf("ahe"));
            Assert.AreEqual(FieldState.Empty, world.GetField("A1").State);
            Assert.AreEqual(10, world.Ledger.SeedsOf(CropCatalog.RadishId));
            Assert.AreEqual(0, world.Log.Count);
        }

        [Test]
        public async Task 模型返回白名单外的品种时不改变任何状态()
        {
            var world = NewWorld();
            const string json =
                "{\"outlet\":\"farm_command\",\"goal\":\"plant\",\"field\":\"A1\",\"species\":\"moon\",\"reply\":\"\"}";
            var router = new IntentRouter(new ScriptedModelClient(json));

            var result = await router.RouteAsync("随便种点什么", world, world.GetNpc("ahe"));

            Assert.IsFalse(result.Accepted);
            StringAssert.Contains("moon", result.Reply);
            Assert.IsNull(world.WorkOrderOf("ahe"));
            Assert.AreEqual(FieldState.Empty, world.GetField("A1").State);
            Assert.AreEqual(10, world.Ledger.SeedsOf(CropCatalog.RadishId));
        }

        [Test]
        public async Task 模型返回空白内容时回退本地解析()
        {
            var world = NewWorld();
            var router = new IntentRouter(new ScriptedModelClient("   "));

            var result = await router.RouteAsync("把东边那块地种满萝卜", world, world.GetNpc("ahe"));

            Assert.IsTrue(result.UsedFallback, "空格子应按不可用处理并降级");
            Assert.AreEqual(ModelCallOutcome.Unusable, result.Call.Outcome);
            Assert.IsTrue(result.Accepted, result.Reply);
            Assert.AreEqual(FieldState.Empty, world.GetField("A1").State);
        }

        [Test]
        public async Task 闲聊与追问只读不改变世界状态()
        {
            var world = NewWorld();
            var router = new IntentRouter();

            var query = await router.RouteAsync("你在干什么", world, world.GetNpc("ahe"));
            var chat = await router.RouteAsync("你好呀", world, world.GetNpc("ahe"));

            Assert.AreEqual(IntentOutlet.Query, query.Outlet);
            Assert.IsFalse(string.IsNullOrWhiteSpace(query.Reply));
            Assert.AreEqual(IntentOutlet.Chat, chat.Outlet);
            Assert.IsFalse(string.IsNullOrWhiteSpace(chat.Reply));
            Assert.IsNull(world.WorkOrderOf("ahe"), "只读出口不能产生工单");
            Assert.AreEqual(FieldState.Empty, world.GetField("A1").State);
            Assert.AreEqual(10, world.Ledger.SeedsOf(CropCatalog.RadishId));
            Assert.AreEqual(0, world.Log.Count);
        }

        [Test]
        public async Task 拒绝台词直接使用动作校验给出的理由()
        {
            var world = NewWorld();
            var router = new IntentRouter(new ScriptedModelClient(PlantRadishJson));

            var first = await router.RouteAsync("把东边那块地种满萝卜", world, world.GetNpc("ahe"));
            Assert.IsTrue(first.Accepted, first.Reply);
            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed, "先播下种，让地块不再空着");

            var seedsAfterSow = world.Ledger.SeedsOf(CropCatalog.RadishId);
            var second = await router.RouteAsync("再种一遍萝卜", world, world.GetNpc("ahe"));

            Assert.IsFalse(second.Accepted);
            Assert.AreEqual("这块地还没空出来", second.Reply, "拒绝台词应来自 ActionValidator 的真实返回值");
            Assert.AreEqual(seedsAfterSow, world.Ledger.SeedsOf(CropCatalog.RadishId), "被拒绝的指令不能扣种子");
            Assert.AreEqual(1, world.Log.Count, "被拒绝的指令不能执行工步");
        }

        [Test]
        public async Task 模型调用同步抛错时也回退本地解析()
        {
            var world = NewWorld();
            var router = new IntentRouter(new ScriptedModelClient((_, __) =>
                throw new InvalidOperationException("模拟断网")));

            var result = await router.RouteAsync("把东边那块地种满萝卜", world, world.GetNpc("ahe"));

            Assert.IsTrue(result.UsedFallback);
            Assert.AreEqual(ModelCallOutcome.Error, result.Call.Outcome);
            Assert.IsTrue(result.Accepted, result.Reply);
        }

        [Test]
        public async Task 模型返回不存在的地块时拒绝且不改变状态()
        {
            var world = NewWorld();
            const string json =
                "{\"outlet\":\"farm_command\",\"goal\":\"plant\",\"field\":\"moonbase\",\"species\":\"radish\",\"reply\":\"\"}";
            var router = new IntentRouter(new ScriptedModelClient(json));

            var result = await router.RouteAsync("去月亮基地种萝卜", world, world.GetNpc("ahe"));

            Assert.IsFalse(result.Accepted);
            StringAssert.Contains("moonbase", result.Reply);
            Assert.IsNull(world.WorkOrderOf("ahe"));
            Assert.AreEqual(FieldState.Empty, world.GetField("A1").State);
            Assert.AreEqual(10, world.Ledger.SeedsOf(CropCatalog.RadishId));
        }

        [Test]
        public async Task 模型把去种月亮误判成合法指令时仍按白名单拒绝()
        {
            var world = NewWorld();
            var client = new ScriptedModelClient(PlantRadishJson);
            var router = new IntentRouter(client);

            var result = await router.RouteAsync("去种月亮", world, world.GetNpc("ahe"));

            Assert.IsFalse(result.Accepted, "白名单外的品种不能被模型的改写绕过去");
            StringAssert.Contains("月亮", result.Reply);
            Assert.AreEqual(0, client.Calls, "白名单拒绝不应把明显非法的品种送去模型");
            Assert.IsNull(world.WorkOrderOf("ahe"));
            Assert.AreEqual(FieldState.Empty, world.GetField("A1").State);
            Assert.AreEqual(10, world.Ledger.SeedsOf(CropCatalog.RadishId));
        }

        [Test]
        public async Task 问种的是什么时是追问而不是被当成播种指令()
        {
            var world = NewWorld();
            var router = new IntentRouter();

            var result = await router.RouteAsync("你种的是什么", world, world.GetNpc("ahe"));

            Assert.AreEqual(IntentOutlet.Query, result.Outlet, "追问不能被白名单拦截误判成农事指令");
            Assert.IsFalse(result.Accepted);
            Assert.IsNull(world.WorkOrderOf("ahe"));
            Assert.AreEqual(FieldState.Empty, world.GetField("A1").State);
        }

        [Test]
        public async Task 东边那块地按坐标解析成最东边的地块()
        {
            var world = NewWorld();
            world.AddField("A2", new FarmPosition(10f, 2f));
            var router = new IntentRouter(new ScriptedModelClient(PlantRadishJson));

            var result = await router.RouteAsync("把东边那块地种满萝卜", world, world.GetNpc("ahe"));

            Assert.IsTrue(result.Accepted, result.Reply);
            Assert.AreEqual("A2", result.FieldId, "「东边」应选 x 最大的地块");
            Assert.AreEqual("A2", result.AssignedWorkOrder.FieldId);
            Assert.AreEqual(FieldState.Empty, world.GetField("A2").State, "派单不改变地块");
        }

        [Test]
        public async Task 常见播种说法不会被白名单误伤()
        {
            foreach (var text in new[] { "种植萝卜", "给我种上10棵萝卜" })
            {
                var world = NewWorld();
                var router = new IntentRouter();

                var result = await router.RouteAsync(text, world, world.GetNpc("ahe"));

                Assert.IsTrue(result.Accepted, $"{text} 被误拒：{result.Reply}");
                Assert.AreEqual("A1", result.FieldId);
                Assert.AreEqual(CropCatalog.RadishId, result.SpeciesId);
            }
        }

        [Test]
        public async Task 不点名地块时由效用打分挑最该浇的那块()
        {
            var world = new FarmWorld(new FarmClock());
            world.AddField("A1", new FarmPosition(10f, 0f));
            world.AddField("A2", new FarmPosition(1f, 0f));
            world.AddNpc("ahe", "阿禾", new FarmPosition(0f, 0f));
            world.AddSeeds(CropCatalog.RadishId, 10);

            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            Assert.IsTrue(world.ExecuteStep("ahe", "A2", WorkStep.Sow).Executed);
            Assert.IsTrue(world.ExecuteStep("ahe", "A2", WorkStep.Water).Executed);
            world.AdvanceDays(1);

            var router = new IntentRouter();
            var result = await router.RouteAsync("给地浇点水", world, world.GetNpc("ahe"));

            Assert.IsTrue(result.Accepted, result.Reply);
            Assert.AreEqual("A1", result.FieldId, "A1 更缺水，效用应高于更近但刚浇过的 A2");
            Assert.AreEqual(WorkStep.Water, result.AssignedWorkOrder.NextStep.Value);
        }

        [Test]
        public async Task 农事指令用规划级十五秒_闲聊用对话级六秒()
        {
            var world = NewWorld();
            var commandTimeout = 0;
            var commandClient = new ScriptedModelClient((request, _) =>
            {
                commandTimeout = request.TimeoutSeconds;
                return Task.FromResult(new ChatResponse(PlantRadishJson, "fake-deepseek"));
            });

            await new IntentRouter(commandClient).RouteAsync("把东边那块地种满萝卜", world, world.GetNpc("ahe"));
            Assert.AreEqual(15, commandTimeout, "农事指令按规划级 15 秒硬超时");

            var chatTimeout = 0;
            var chatClient = new ScriptedModelClient((request, _) =>
            {
                chatTimeout = request.TimeoutSeconds;
                return Task.FromResult(
                    new ChatResponse("{\"outlet\":\"chat\",\"reply\":\"嗯。\"}", "fake-deepseek"));
            });

            await new IntentRouter(chatClient).RouteAsync("你好呀", world, world.GetNpc("ahe"));
            Assert.AreEqual(6, chatTimeout, "闲聊按对话级 6 秒硬超时");
        }

        [Test]
        public async Task 接单后的工单能自行跑完五个工步并入库()
        {
            var world = NewWorld();
            var field = world.GetField("A1");
            var router = new IntentRouter(new ScriptedModelClient(PlantRadishJson));

            var result = await router.RouteAsync("把东边那块地种满萝卜", world, world.GetNpc("ahe"));
            Assert.IsTrue(result.Accepted, result.Reply);

            var executed = new List<WorkStep>();
            for (var guard = 0; guard < 400 && result.AssignedWorkOrder != null && !result.AssignedWorkOrder.IsComplete; guard++)
            {
                var before = result.AssignedWorkOrder.CompletedCount;
                var step = world.AdvanceWorkOrder("ahe");
                if (result.AssignedWorkOrder.CompletedCount > before)
                {
                    executed.Add(result.AssignedWorkOrder.Steps[before]);
                }

                if (!step.Executed)
                {
                    world.AdvanceDays(1);
                }
            }

            CollectionAssert.AreEqual(
                new[] { WorkStep.Sow, WorkStep.Water, WorkStep.Fertilize, WorkStep.Weed, WorkStep.Harvest },
                executed,
                "一张工单应把五个工步按顺序跑完");
            Assert.AreEqual(FieldState.Harvested, field.State);
            Assert.AreEqual(9, world.Ledger.SeedsOf(CropCatalog.RadishId));
            Assert.AreEqual(3, world.Ledger.ProduceOf(CropCatalog.RadishId));
            Assert.IsTrue(world.Ledger.Verify(out var error), error);
            Assert.IsNull(world.WorkOrderOf("ahe"));
        }

        [Test]
        public async Task 模型失败与超时也记录模型名与耗时()
        {
            var world = NewWorld();
            var failed = new IntentRouter(new ScriptedModelClient((_, __) =>
                throw new InvalidOperationException("模拟断网")));

            var error = await failed.RouteAsync("把东边那块地种满萝卜", world, world.GetNpc("ahe"));

            Assert.AreEqual(ModelCallOutcome.Error, error.Call.Outcome);
            Assert.AreEqual("fake-deepseek", error.Call.Model, "失败也要能展示模型名");
            Assert.GreaterOrEqual(error.Call.LatencySeconds, 0d, "失败也要记录耗时");

            var never = new TaskCompletionSource<ChatResponse>();
            var slow = new IntentRouter(new ScriptedModelClient((_, __) => never.Task));
            var timeout = await slow.RouteAsync(
                "把东边那块地种满萝卜",
                world,
                world.GetNpc("ahe"),
                deadlineSeconds: 0.05f);

            Assert.AreEqual(ModelCallOutcome.Timeout, timeout.Call.Outcome);
            Assert.AreEqual("fake-deepseek", timeout.Call.Model, "超时也要能展示模型名");
            Assert.AreEqual(0.05d, timeout.Call.LatencySeconds, 0.01d, "超时应记录等待上限");
        }

        [Test]
        public async Task 不指定地块和品种时用最近的地块与NPC的默认品种()
        {
            var world = NewWorld();
            var router = new IntentRouter();

            var result = await router.RouteAsync("把地都种上", world, world.GetNpc("ahe"));

            Assert.IsTrue(result.Accepted, result.Reply);
            Assert.AreEqual("A1", result.FieldId);
            Assert.AreEqual(CropCatalog.RadishId, result.SpeciesId);
            Assert.AreEqual(5, result.PlannedSteps.Count);
        }
    }
}
