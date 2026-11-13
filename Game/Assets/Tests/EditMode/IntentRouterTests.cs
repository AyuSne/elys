using System;
using System.Collections.Generic;
using System.Linq;
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
        public async Task 拒绝台词带地块与理由()
        {
            var world = NewWorld();
            var router = new IntentRouter();

            var result = await router.RouteAsync("把东边那块地收了", world, world.GetNpc("ahe"));

            Assert.IsFalse(result.Accepted);
            StringAssert.StartsWith("A1 ", result.Reply, "拒绝台词要带地块 id");
            StringAssert.Contains("还没熟", result.Reply, "拒绝台词要带理由");
            Assert.IsNull(world.WorkOrderOf("ahe"));
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
            Assert.AreEqual("A1 还没空出来", second.Reply, "拒绝台词应来自 ActionValidator 的真实返回值");
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
            Assert.AreEqual(15, world.Ledger.SeedsOf(CropCatalog.RadishId), "播种扣 1 颗；等成熟的 3 天每天补 2 颗");
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
        public async Task 离线追问回答当前状态与记忆依据()
        {
            var world = NewWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            world.AdvanceDays(1);
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", null, new[] { WorkStep.Water }, out _, out var reason),
                reason);

            var result = await new IntentRouter().RouteAsync("你在干什么", world, world.GetNpc("ahe"));

            Assert.AreEqual(IntentOutlet.Query, result.Outlet);
            StringAssert.Contains("A1", result.Reply, "回答必须点名具体地块，不能只说套话");
            StringAssert.Contains("浇水", result.Reply, "回答必须说出下一步工步");
            StringAssert.Contains("体力", result.Reply, "回答必须给出真实体力");
            StringAssert.Contains("第 1 天", result.Reply, "回答必须引用检索到的记忆及其游戏日");
            var order = world.WorkOrderOf("ahe");
            Assert.IsNotNull(order, "追问只读，不能丢掉工单");
            Assert.AreEqual(WorkStep.Water, order.NextStep.Value, "追问不能推进工单");
            Assert.AreEqual(1, world.Log.Count, "追问不能执行工步");
        }

        [Test]
        public async Task 跨日追问能对上玩家上次让我浇的地块()
        {
            var world = NewWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            world.AdvanceDays(1);

            var router = new IntentRouter();
            var command = await router.RouteAsync("给西边的地浇水", world, world.GetNpc("ahe"));
            Assert.IsTrue(command.Accepted, command.Reply);
            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed);
            world.AdvanceDays(1);

            var question = await router.RouteAsync("上次你让我浇的是西边那块吗", world, world.GetNpc("ahe"));

            Assert.AreEqual(IntentOutlet.Query, question.Outlet, "带「上次」的追问不能被当成新的浇水指令");
            StringAssert.Contains("第 2 天", question.Reply, "必须说出玩家上次下指令的游戏日");
            StringAssert.Contains("西边", question.Reply, "必须沿用当时的方位指称");
            StringAssert.Contains("A1", question.Reply, "必须落到真正浇过的地块");
            Assert.IsNull(world.WorkOrderOf("ahe"), "追问不能产生新工单");
        }

        [Test]
        public async Task 模型追问的提示词带上检索命中与当前意图()
        {
            var world = NewWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);

            var prompt = string.Empty;
            var client = new ScriptedModelClient((request, _) =>
            {
                if (request.JsonOutput)
                {
                    prompt = request.Messages[request.Messages.Count - 1].Content;
                    return Task.FromResult(
                        new ChatResponse("{\"outlet\":\"query\"}", "fake-deepseek"));
                }

                return Task.FromResult(
                    new ChatResponse("我在 A1 播下了萝卜，体力还满着。", "fake-deepseek"));
            });

            var result = await new IntentRouter(client).RouteAsync("你在干什么", world, world.GetNpc("ahe"));

            Assert.AreEqual(IntentOutlet.Query, result.Outlet);
            StringAssert.Contains("在 A1 播下了萝卜", prompt, "检索命中的记忆必须注入分类提示词");
            StringAssert.Contains("当前意图", prompt, "提示词要带上当前意图");
            StringAssert.Contains("待命", prompt);
            Assert.AreEqual("我在 A1 播下了萝卜，体力还满着。", result.Reply, "回答由表达层换一种说法");
            Assert.IsFalse(result.UsedFallback);
        }

        [Test]
        public async Task 在线时用向量检索替换关键词检索()
        {
            var world = NewWorld();
            world.Remember("ahe", "给 A1 浇了水", 5);
            world.Remember("ahe", "今天天气不错", 5);

            var prompt = string.Empty;
            var client = new ScriptedModelClient((request, _) =>
            {
                if (request.JsonOutput)
                {
                    prompt = request.Messages[request.Messages.Count - 1].Content;
                    return Task.FromResult(
                        new ChatResponse("{\"outlet\":\"query\"}", "fake-deepseek"));
                }

                return Task.FromResult(
                    new ChatResponse("天气不错，我还记得。体力也够。", "fake-deepseek"));
            })
            {
                SupportsEmbeddings = true,
                OnEmbed = inputs => Task.FromResult<IReadOnlyList<float[]>>(
                    inputs.Select(text => text.Contains("A2") || text == "今天天气不错"
                        ? new[] { 1f, 0f }
                        : new[] { 0f, 1f }).ToList())
            };

            var result = await new IntentRouter(client).RouteAsync(
                "你还记得给 A2 浇水吗",
                world,
                world.GetNpc("ahe"));

            Assert.AreEqual(IntentOutlet.Query, result.Outlet);
            Assert.AreEqual(1, client.EmbedCalls, "一次追问只批量取一次向量");
            var weather = prompt.IndexOf("今天天气不错", StringComparison.Ordinal);
            var water = prompt.IndexOf("给 A1 浇了水", StringComparison.Ordinal);
            Assert.GreaterOrEqual(weather, 0, "向量命中的记忆必须注入提示词");
            Assert.GreaterOrEqual(water, 0);
            Assert.Less(weather, water, "向量相近的记忆应排在关键词相近的记忆之前");
            StringAssert.Contains("天气不错", result.Reply, "回答仍要落在命中的那条记忆上");
        }

        [Test]
        public async Task 向量调用失败时退回关键词检索并照常回答()
        {
            var world = NewWorld();
            world.Remember("ahe", "给 A1 浇了水", 5);
            world.Remember("ahe", "今天天气不错", 5);

            var prompt = string.Empty;
            var client = new ScriptedModelClient((request, _) =>
            {
                if (request.JsonOutput)
                {
                    prompt = request.Messages[request.Messages.Count - 1].Content;
                    return Task.FromResult(
                        new ChatResponse("{\"outlet\":\"query\"}", "fake-deepseek"));
                }

                return Task.FromResult(
                    new ChatResponse("我浇过 A1，第 1 天的事我还记得，体力也够。", "fake-deepseek"));
            })
            {
                SupportsEmbeddings = true,
                OnEmbed = _ => Task.FromException<IReadOnlyList<float[]>>(
                    new InvalidOperationException("模拟向量服务断网"))
            };

            var result = await new IntentRouter(client).RouteAsync(
                "你还记得给 A2 浇水吗",
                world,
                world.GetNpc("ahe"));

            Assert.AreEqual(IntentOutlet.Query, result.Outlet);
            Assert.AreEqual(1, client.EmbedCalls);
            var water = prompt.IndexOf("给 A1 浇了水", StringComparison.Ordinal);
            var weather = prompt.IndexOf("今天天气不错", StringComparison.Ordinal);
            Assert.GreaterOrEqual(water, 0);
            Assert.Less(water, weather, "向量不可用时必须退回关键词排序");
            StringAssert.Contains("我浇过 A1", result.Reply);
            StringAssert.Contains("第 1 天", result.Reply, "问「记得」时要落到记忆里的游戏日");
        }

        [Test]
        public async Task 向量调用超时不会拖住追问()
        {
            var world = NewWorld();
            world.Remember("ahe", "在 A1 播下了萝卜", 6);
            var never = new TaskCompletionSource<IReadOnlyList<float[]>>();
            var client = new ScriptedModelClient((request, _) => Task.FromResult(
                new ChatResponse(
                    request.JsonOutput ? "{\"outlet\":\"query\"}" : "我在待命，体力满着，A1 的萝卜是第 1 天种的。",
                    "fake")))
            {
                SupportsEmbeddings = true,
                OnEmbed = _ => never.Task
            };

            var result = await new IntentRouter(client).RouteAsync(
                "你在干什么",
                world,
                world.GetNpc("ahe"),
                embeddingDeadlineSeconds: 0.05f);

            Assert.AreEqual(IntentOutlet.Query, result.Outlet);
            Assert.AreEqual(1, client.EmbedCalls);
            Assert.AreEqual(
                "我在待命，体力满着，A1 的萝卜是第 1 天种的。",
                result.Reply,
                "向量超时不能挡住对话");
        }

        [Test]
        public async Task 记忆追问的措辞丢掉记忆依据时回退事实串()
        {
            var world = NewWorld();
            world.Remember("ahe", "玩家让我给西边（A1）浇水", 7);
            const string json = "{\"outlet\":\"query\",\"reply\":\"我在 A1。\"}";

            var result = await new IntentRouter(new ScriptedModelClient(json)).RouteAsync(
                "上次你让我浇的是哪块",
                world,
                world.GetNpc("ahe"));

            Assert.AreEqual(IntentOutlet.Query, result.Outlet);
            Assert.IsTrue(result.UsedFallback, "JSON 冒充台词时回退本地事实串");
            StringAssert.Contains("第 1 天", result.Reply, "跨日追问必须落到记忆里的游戏日");
            StringAssert.Contains("西边", result.Reply, "跨日追问必须落到记忆里的方位");
        }

        [Test]
        public async Task 记忆追问优先引用玩家的话而不是自己的动作()
        {
            var world = NewWorld();
            world.Remember("ahe", "给 A1 浇了水", 10);
            world.Remember("ahe", "玩家让我给西边（A1）浇水", 4);

            var result = await new IntentRouter().RouteAsync(
                "上次你浇的是哪块",
                world,
                world.GetNpc("ahe"));

            Assert.AreEqual(IntentOutlet.Query, result.Outlet);
            StringAssert.Contains("你让我给西边", result.Reply, "回答玩家的问题时应引用玩家当时的交代");

            // 回答与决策可视化展示的是同一次检索；#7 的「优先玩家原话」规则压过「必须引用第 1 项」，
            // 所以这里第 1 项仍是权重最高的自己的动作，回答引用第 2 项。验收场景里玩家原话排第 1，
            // 由 QueryTests.记忆追问沿用记忆里的地块与游戏日_并与决策可视化第1项一致 盯住。
            var panelHits = world.Inspect("ahe").Memories;
            StringAssert.Contains("给 A1 浇了水", panelHits[0].Text, "第 1 项是权重最高的那条");
            StringAssert.Contains("玩家让我给西边（A1）浇水", panelHits[1].Text, "玩家原话排在第 2 项");
        }

        [Test]
        public async Task 追问只读而闲聊写回玩家原话()
        {
            var world = NewWorld();
            var router = new IntentRouter();
            Assert.AreEqual(0, world.Inspect("ahe").Memories.Count, "开局没有记忆");

            await router.RouteAsync("你在干什么", world, world.GetNpc("ahe"));

            Assert.AreEqual(0, world.Inspect("ahe").Memories.Count, "追问只读，不能留下记忆");

            var chat = await router.RouteAsync("你好呀", world, world.GetNpc("ahe"));

            Assert.AreEqual(IntentOutlet.Chat, chat.Outlet);
            var memories = world.Inspect("ahe").Memories;
            Assert.Greater(memories.Count, 0, "闲聊要留下记忆");
            StringAssert.Contains("你好呀", memories[0].Text, "记下的应该是玩家的原话");
        }

        [Test]
        public async Task 闲聊记忆的重要性由模型打分()
        {
            var world = NewWorld();
            world.Remember("ahe", "玩家说「随便聊聊」", 8);
            var calls = 0;
            var client = new ScriptedModelClient((_, __) =>
            {
                calls++;
                return Task.FromResult(new ChatResponse(
                    calls == 1
                        ? "{\"outlet\":\"chat\",\"importance\":9}"
                        : "我记下了，你路上当心。",
                    "fake-deepseek"));
            });

            var result = await new IntentRouter(client).RouteAsync(
                "我明天要走了",
                world,
                world.GetNpc("ahe"));

            Assert.AreEqual(IntentOutlet.Chat, result.Outlet);
            Assert.AreEqual("我记下了，你路上当心。", result.Reply, "回话走表达层");
            Assert.AreEqual(2, calls, "importance 来自分类调用，回话来自表达层调用");
            var hits = world.RetrieveMemories("ahe", "待命");
            StringAssert.Contains("我明天要走了", hits[0].Text, "模型打出的高重要性应压过默认值");
        }

        [Test]
        public async Task 带昨天的闲聊仍然写记忆而不是被当成追问()
        {
            var world = NewWorld();

            var result = await new IntentRouter().RouteAsync("昨天天气不错", world, world.GetNpc("ahe"));

            Assert.AreEqual(IntentOutlet.Chat, result.Outlet, "陈述句不能因为带「昨天」就变成追问");
            StringAssert.Contains("昨天天气不错", world.Inspect("ahe").Memories[0].Text);
        }

        [Test]
        public async Task 离线解释为什么没去干活时给出休息原因()
        {
            var world = new FarmWorld(new FarmClock());
            world.AddField("A1", new FarmPosition(0f, 2f));
            world.AddField("A2", new FarmPosition(4f, 2f));
            world.AddNpc("aya", "阿芽", new FarmPosition(0f, -4f), NpcPersona.Aya);
            world.AddSeeds(CropCatalog.RadishId, 10);

            Assert.IsTrue(world.ExecuteStep("aya", "A1", WorkStep.Sow).Executed);
            Assert.IsTrue(world.ExecuteStep("aya", "A1", WorkStep.Water).Executed);
            Assert.IsTrue(world.ExecuteStep("aya", "A1", WorkStep.Fertilize).Executed);
            Assert.IsTrue(world.ExecuteStep("aya", "A2", WorkStep.Sow).Executed);

            var npc = world.GetNpc("aya");
            Assert.IsTrue(npc.IsResting, "阿芽连做四个工步后应按人设去休息");
            var stamina = npc.Stamina;
            var logCount = world.Log.Count;

            var result = await new IntentRouter().RouteAsync("你怎么不去干活", world, npc);

            Assert.AreEqual(IntentOutlet.Query, result.Outlet);
            StringAssert.Contains("歇", result.Reply, "必须承认自己在休息");
            StringAssert.Contains("体力", result.Reply, "必须给出具体体力");
            StringAssert.Contains("秒", result.Reply, "必须给出还要缓多久");
            Assert.AreEqual(stamina, npc.Stamina, 0.0001f, "追问不能恢复体力");
            Assert.AreEqual(logCount, world.Log.Count, "追问不能执行工步");
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
