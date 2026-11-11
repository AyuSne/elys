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

            public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
            {
                return onChat(request, cancellationToken);
            }

            public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Array.Empty<float>());
            }
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
            Assert.IsNotNull(result.Order);
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
            Assert.AreEqual("timeout", result.Call.Outcome);
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
            Assert.IsNull(result.Order);
            Assert.IsNull(world.OrderOf("ahe"));
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
            Assert.IsNull(world.OrderOf("ahe"));
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
            Assert.IsNull(world.OrderOf("ahe"));
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
            Assert.AreEqual("unusable", result.Call.Outcome);
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
            Assert.IsNull(world.OrderOf("ahe"), "只读出口不能产生工单");
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
            Assert.IsTrue(world.AdvanceOrder("ahe").Executed, "先播下种，让地块不再空着");

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
            Assert.AreEqual("error", result.Call.Outcome);
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
            Assert.IsNull(world.OrderOf("ahe"));
            Assert.AreEqual(FieldState.Empty, world.GetField("A1").State);
            Assert.AreEqual(10, world.Ledger.SeedsOf(CropCatalog.RadishId));
        }

        [Test]
        public async Task 模型把去种月亮误判成合法指令时仍按白名单拒绝()
        {
            var world = NewWorld();
            var router = new IntentRouter(new ScriptedModelClient(PlantRadishJson));

            var result = await router.RouteAsync("去种月亮", world, world.GetNpc("ahe"));

            Assert.IsFalse(result.Accepted, "白名单外的品种不能被模型的改写绕过去");
            StringAssert.Contains("月亮", result.Reply);
            Assert.IsNull(world.OrderOf("ahe"));
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
            Assert.IsNull(world.OrderOf("ahe"));
            Assert.AreEqual(FieldState.Empty, world.GetField("A1").State);
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
            for (var guard = 0; guard < 400 && result.Order != null && !result.Order.IsComplete; guard++)
            {
                var step = world.AdvanceOrder("ahe");
                if (step.Executed)
                {
                    executed.Add(step.Step.Value);
                    continue;
                }

                world.AdvanceDays(1);
            }

            CollectionAssert.AreEqual(
                new[] { WorkStep.Sow, WorkStep.Water, WorkStep.Fertilize, WorkStep.Weed, WorkStep.Harvest },
                executed,
                "一张工单应把五个工步按顺序跑完");
            Assert.AreEqual(FieldState.Harvested, field.State);
            Assert.AreEqual(9, world.Ledger.SeedsOf(CropCatalog.RadishId));
            Assert.AreEqual(3, world.Ledger.ProduceOf(CropCatalog.RadishId));
            Assert.IsTrue(world.Ledger.Verify(out var error), error);
            Assert.IsNull(world.OrderOf("ahe"));
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
