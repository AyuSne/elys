using System.Collections.Generic;
using System.Threading.Tasks;
using ElysFarm.Core;
using ElysFarm.Demo;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #20 追问走表达层的接缝测试：与 <see cref="ChatTests"/> 一样只打
    /// <see cref="IntentRouter"/> 与 <see cref="IModelClient"/> 两条缝。
    /// 断言的是规格里的三条边界——回答的事实（地块、工步、体力、命中的记忆）由本地生成并注入、
    /// 模型只换一种说法、模型失败或改掉事实时回退事实串；模型一律用假实现，不联网。
    /// </summary>
    public class QueryTests
    {
        [Test]
        public async Task 追问的回答由表达层另说一次_不用分类调用的reply()
        {
            var world = NewWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", null, new[] { WorkStep.Water }, out _, out var reason),
                reason);

            var requests = new List<ChatRequest>();
            var client = new ScriptedModelClient((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ChatResponse(
                    requests.Count == 1
                        ? "{\"outlet\":\"query\",\"reply\":\"我在 A1 浇水。\"}"
                        : "我这就去 A1 浇水，体力还有八成。",
                    "fake-deepseek",
                    0.9d,
                    100,
                    20));
            });

            var result = await new IntentRouter(client).RouteAsync(
                "你在干什么",
                world,
                world.GetNpc("ahe"),
                dialogue: new DialogueContext(NpcTones.Ahe));

            Assert.AreEqual(IntentOutlet.Query, result.Outlet);
            Assert.AreEqual(2, requests.Count, "分类一次，追问的回答另有表达层一次");
            Assert.AreEqual("我这就去 A1 浇水，体力还有八成。", result.Reply, "回答由表达层说，不借分类调用的 reply");
            Assert.IsFalse(result.UsedFallback, "表达层成功时不算本地降级");
            Assert.AreEqual(ModelCallOutcome.Ok, result.Call.Outcome);
            Assert.AreEqual(20, result.Call.CompletionTokens, "HUD 展示的应是表达层这次调用");
        }

        [Test]
        public async Task 追问的提示词注入本地事实与口吻()
        {
            var world = NewWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", null, new[] { WorkStep.Water }, out _, out var reason),
                reason);

            var requests = new List<ChatRequest>();
            var client = new ScriptedModelClient((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ChatResponse(
                    requests.Count == 1
                        ? "{\"outlet\":\"query\"}"
                        : "我这就去 A1 浇水，体力还有八成。",
                    "fake-deepseek"));
            });
            var history = new[]
            {
                new DialogueTurn("你好呀", "嗯，来了。")
            };

            var result = await new IntentRouter(client).RouteAsync(
                "你在干什么",
                world,
                world.GetNpc("ahe"),
                dialogue: new DialogueContext(NpcTones.Ahe, history));

            Assert.IsFalse(result.UsedFallback);
            Assert.AreEqual(2, requests.Count);
            var expression = requests[1];
            Assert.IsFalse(expression.JsonOutput, "表达层不要求 JSON");
            Assert.IsFalse(expression.Thinking, "追问不开思考模式");
            Assert.AreEqual(0.8f, expression.Temperature, 0.0001f, "规格写死 temperature 0.8");
            Assert.AreEqual(200, expression.MaxTokens, "表达层输出上限 200 token");
            Assert.AreEqual(2, expression.Messages.Count, "system 给角色与口吻，user 给状态、事实与上下文");

            var system = expression.Messages[0].Content;
            var user = expression.Messages[1].Content;
            StringAssert.Contains(NpcTones.Ahe, system, "口吻要进 system");
            StringAssert.Contains("阿禾", system);
            StringAssert.Contains("本地事实", system, "system 要说明本地事实是唯一依据");
            StringAssert.Contains("只能换一种说法", system, "system 要禁止改事实");
            StringAssert.Contains("本地事实", user, "事实段要进 user");
            StringAssert.Contains("我正要去 A1 浇水", user, "本地事实串要原样进 user");
            StringAssert.Contains("体力", user, "状态摘要要带体力");
            StringAssert.Contains("在 A1 播下了萝卜", user, "命中的记忆要进 user");
            StringAssert.Contains("你好呀", user, "上一轮玩家说的话要进 user");
            StringAssert.Contains("嗯，来了。", user, "上一轮 NPC 的回话也要进 user");
            StringAssert.EndsWith("玩家说：你在干什么", user, "玩家这句话要放在提示词末尾");
        }

        [Test]
        public async Task 表达层丢掉体力时回退事实串_回答仍带地块工步体力()
        {
            var world = NewWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", null, new[] { WorkStep.Water }, out _, out var reason),
                reason);

            var requests = new List<ChatRequest>();
            var client = new ScriptedModelClient((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ChatResponse(
                    request.JsonOutput ? "{\"outlet\":\"query\"}" : "我这就去 A1 浇水。",
                    "fake-deepseek"));
            });

            var result = await new IntentRouter(client).RouteAsync(
                "你在干什么",
                world,
                world.GetNpc("ahe"),
                dialogue: new DialogueContext(NpcTones.Ahe));

            StringAssert.Contains("A1", result.Reply, "回答必须点名当前工单的地块");
            StringAssert.Contains("浇水", result.Reply, "回答必须说出下一步工步");
            StringAssert.Contains("体力", result.Reply, "「你在干什么」的回答必须带体力");
            Assert.IsTrue(result.UsedFallback, "台词丢了一条事实，只能回退本地事实串并标「本地模板」");
            Assert.AreEqual(ModelCallOutcome.Unusable, result.Call.Outcome);
            Assert.AreEqual(3, requests.Count, "分类一次，表达层重试一次后回退");
        }

        [Test]
        public async Task 表达层留着地块工步体力的说法就采用()
        {
            var world = NewWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", null, new[] { WorkStep.Water }, out _, out var reason),
                reason);

            var client = new ScriptedModelClient((request, _) => Task.FromResult(new ChatResponse(
                request.JsonOutput
                    ? "{\"outlet\":\"query\"}"
                    : "我这就去 A1 浇水，体力还有八成。",
                "fake-deepseek")));

            var result = await new IntentRouter(client).RouteAsync(
                "你在干什么",
                world,
                world.GetNpc("ahe"),
                dialogue: new DialogueContext(NpcTones.Ahe));

            Assert.AreEqual("我这就去 A1 浇水，体力还有八成。", result.Reply, "事实齐全时用模型换的说法");
            Assert.IsFalse(result.UsedFallback);
        }

        [Test]
        public async Task 表达层编出事实里没有的地块时回退事实串()
        {
            var world = NewWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", null, new[] { WorkStep.Water }, out _, out var reason),
                reason);

            var requests = new List<ChatRequest>();
            var client = new ScriptedModelClient((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ChatResponse(
                    request.JsonOutput
                        ? "{\"outlet\":\"query\"}"
                        : "我这就去 A2 浇水，A1 那块晚点再说，体力还有八成。",
                    "fake-deepseek"));
            });

            var result = await new IntentRouter(client).RouteAsync(
                "你在干什么",
                world,
                world.GetNpc("ahe"),
                dialogue: new DialogueContext(NpcTones.Ahe));

            StringAssert.DoesNotContain("A2", result.Reply, "事实里只有 A1，编出来的地块不能留在回答里");
            StringAssert.Contains("A1", result.Reply);
            Assert.IsTrue(result.UsedFallback, "地块被换掉就作废这份措辞");
            Assert.AreEqual(3, requests.Count);
        }

        [Test]
        public async Task 记忆追问沿用记忆里的地块与游戏日_并与抽屉第1项一致()
        {
            var world = NewWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            world.AdvanceDays(1);

            var router = new IntentRouter(new ScriptedModelClient((request, _) => Task.FromResult(
                new ChatResponse(
                    request.JsonOutput
                        ? "{\"outlet\":\"query\"}"
                        : "对，第 2 天你让我给西边（A1）浇水，体力也够。",
                    "fake-deepseek"))));
            var command = await router.RouteAsync("给西边的地浇水", world, world.GetNpc("ahe"));
            Assert.IsTrue(command.Accepted, command.Reply);
            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed);
            world.AdvanceDays(1);

            var result = await router.RouteAsync(
                "上次你让我浇的是西边那块吗",
                world,
                world.GetNpc("ahe"),
                dialogue: new DialogueContext(NpcTones.Ahe));

            Assert.AreEqual(IntentOutlet.Query, result.Outlet, "带「上次」的问句是追问，不是新指令");
            Assert.AreEqual("对，第 2 天你让我给西边（A1）浇水，体力也够。", result.Reply);
            Assert.IsFalse(result.UsedFallback);
            StringAssert.Contains("西边", result.Reply, "回答必须引用记忆里的方位");
            StringAssert.Contains("A1", result.Reply, "回答必须落到记忆里的地块");

            var drawer = world.Inspect("ahe").Memories[0];
            StringAssert.Contains("玩家让我给西边（A1）浇水", drawer.Text, "抽屉第 1 项应是这次追问实际命中的记忆");
            StringAssert.Contains($"第 {drawer.Day} 天", result.Reply, "回答引用的游戏日要与抽屉第 1 项一致");
        }

        [Test]
        public async Task 记忆追问编出记忆里没有的游戏日时回退事实串()
        {
            var world = NewWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            world.AdvanceDays(1);

            var requests = new List<ChatRequest>();
            var client = new ScriptedModelClient((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ChatResponse(
                    request.JsonOutput
                        ? "{\"outlet\":\"query\"}"
                        : "对，第 5 天你让我给西边（A1）浇水，体力也够。",
                    "fake-deepseek"));
            });
            var router = new IntentRouter(client);
            var command = await router.RouteAsync("给西边的地浇水", world, world.GetNpc("ahe"));
            Assert.IsTrue(command.Accepted, command.Reply);
            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed);
            world.AdvanceDays(1);

            var result = await router.RouteAsync(
                "上次你让我浇的是西边那块吗",
                world,
                world.GetNpc("ahe"),
                dialogue: new DialogueContext(NpcTones.Ahe));

            StringAssert.Contains("第 2 天", result.Reply, "记忆里的游戏日是第 2 天");
            StringAssert.DoesNotContain("第 5 天", result.Reply, "事实里没有第 5 天，编出来就不能用");
            Assert.IsTrue(result.UsedFallback, "编出游戏日的措辞要作废");
            Assert.AreEqual(4, requests.Count, "命令一次分类 + 追问一次分类 + 表达层重试一次");
        }

        [Test]
        public async Task 表达层空响应时回退事实串_回答仍带地块工步体力()
        {
            var world = NewWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", null, new[] { WorkStep.Water }, out _, out var reason),
                reason);

            var requests = new List<ChatRequest>();
            var client = new ScriptedModelClient((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ChatResponse(
                    request.JsonOutput ? "{\"outlet\":\"query\"}" : "   ",
                    "fake-deepseek"));
            });

            var result = await new IntentRouter(client).RouteAsync(
                "你在干什么",
                world,
                world.GetNpc("ahe"),
                dialogue: new DialogueContext(NpcTones.Ahe));

            Assert.AreEqual(3, requests.Count, "分类一次，表达层空响应再试一次");
            StringAssert.Contains("A1", result.Reply, "模型失败时事实串仍可用");
            StringAssert.Contains("浇水", result.Reply);
            StringAssert.Contains("体力", result.Reply);
            Assert.IsTrue(result.UsedFallback, "HUD 要据此标出「本地模板」");
            Assert.AreEqual(ModelCallOutcome.Unusable, result.Call.Outcome);
            Assert.AreEqual("fake-deepseek", result.Call.Model, "失败也要能展示模型名");
            Assert.AreEqual(0, world.WorkOrderOf("ahe").CompletedCount, "追问只读，工单不动");
        }

        [Test]
        public async Task 表达层返回JSON时重试而不是把JSON当回答()
        {
            var world = NewWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", null, new[] { WorkStep.Water }, out _, out var reason),
                reason);

            var requests = new List<ChatRequest>();
            var client = new ScriptedModelClient((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ChatResponse(
                    request.JsonOutput
                        ? "{\"outlet\":\"query\"}"
                        : "{\"reply\":\"我在 A1 浇水，体力八成。\"}",
                    "fake-deepseek"));
            });

            var result = await new IntentRouter(client).RouteAsync(
                "你在干什么",
                world,
                world.GetNpc("ahe"),
                dialogue: new DialogueContext(NpcTones.Ahe));

            Assert.AreEqual(3, requests.Count, "JSON 冒充台词按不可用处理并重试");
            StringAssert.DoesNotContain("outlet", result.Reply, "对话框里绝不能出现 JSON");
            StringAssert.DoesNotContain("{", result.Reply);
            StringAssert.Contains("A1", result.Reply);
            Assert.IsTrue(result.UsedFallback);
        }

        [Test]
        public async Task 模型把追问误判成农活时仍走表达层且保留纠正标记()
        {
            var world = NewWorld();
            var requests = new List<ChatRequest>();
            var client = new ScriptedModelClient((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ChatResponse(
                    request.JsonOutput
                        ? "{\"outlet\":\"farm_command\",\"goal\":\"plant\",\"field\":\"A1\",\"species\":\"radish\",\"reply\":\"\"}"
                        : "我现在待命，体力还是满的。",
                    "fake-deepseek",
                    1.25d,
                    123,
                    45));
            });

            var result = await new IntentRouter(client).RouteAsync(
                "你在干什么",
                world,
                world.GetNpc("ahe"),
                dialogue: new DialogueContext(NpcTones.Ahe));

            Assert.AreEqual(IntentOutlet.Query, result.Outlet, "问句不能因为模型说是农活就变成工单");
            Assert.AreEqual("我现在待命，体力还是满的。", result.Reply, "回话仍由表达层说");
            Assert.IsFalse(result.UsedFallback);
            Assert.IsNull(world.WorkOrderOf("ahe"));
            Assert.AreEqual(FieldState.Empty, world.GetField("A1").State);
            Assert.AreEqual(ModelCallOutcome.Correction, result.Call.Outcome, "ADR-0004：出口被本地纠正过");
            Assert.AreEqual(123, result.Call.PromptTokens, "纠正时仍保留分类调用的 token");
            Assert.AreEqual(45, result.Call.CompletionTokens);
            Assert.AreEqual(2, requests.Count, "分类一次、表达层一次");
        }

        [Test]
        public async Task 分类调用不可用时追问仍试一次表达层()
        {
            var world = NewWorld();
            world.Remember("ahe", "在 A1 播下了萝卜", 6);
            var requests = new List<ChatRequest>();
            var client = new ScriptedModelClient((request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new ChatResponse(
                    request.JsonOutput ? "   " : "我在待命，A1 的萝卜是第 1 天种的，体力满着。",
                    "fake-deepseek"));
            });

            var result = await new IntentRouter(client).RouteAsync(
                "你在干什么",
                world,
                world.GetNpc("ahe"),
                dialogue: new DialogueContext(NpcTones.Ahe));

            Assert.AreEqual(IntentOutlet.Query, result.Outlet, "出口由本地规则定为追问");
            Assert.AreEqual(2, requests.Count, "分类空响应之后仍要问一次表达层");
            Assert.AreEqual("我在待命，A1 的萝卜是第 1 天种的，体力满着。", result.Reply);
            Assert.IsFalse(result.UsedFallback, "台词是模型说的，HUD 不该标「本地模板」");
            Assert.AreEqual(ModelCallOutcome.Ok, result.Call.Outcome);
        }

        static FarmWorld NewWorld()
        {
            var world = new FarmWorld(new FarmClock());
            world.AddField("A1", new FarmPosition(2f, 2f));
            world.AddField("A2", new FarmPosition(10f, 2f));
            world.AddNpc("ahe", "阿禾", new FarmPosition(0f, -4f), NpcPersona.Ahe);
            world.AddNpc("aya", "阿芽", new FarmPosition(0f, -3f), NpcPersona.Aya);
            world.AddNpc("atu", "阿土", new FarmPosition(1f, -3f), NpcPersona.Atu);
            world.SetProduceStore(new FarmPosition(0f, -4f));
            world.AddSeeds(CropCatalog.RadishId, 10);
            return world;
        }
    }
}
