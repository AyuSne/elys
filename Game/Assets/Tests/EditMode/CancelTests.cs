using System.Threading.Tasks;
using ElysFarm.Core;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #22 取消工单的接缝测试：只打 <see cref="IntentRouter"/> 这一条缝（离线路由与假
    /// <see cref="IModelClient"/> 两种配置都用）。断言规格里的几条边界——「别干了 / 取消 / 停下」
    /// 都能取消、取消由本地规则判定而不等模型、没有工单时只回一句话、问句与否定说法不被误判成取消。
    /// 三人卡的「取消」按钮是演示层，按仓库惯例用 Play Mode 截图验收，不另开场景级接缝。
    /// </summary>
    public class CancelTests
    {
        static readonly WorkStep[] PlantRadish =
        {
            WorkStep.Sow,
            WorkStep.Water,
            WorkStep.Fertilize,
            WorkStep.Weed,
            WorkStep.Harvest
        };

        static FarmWorld NewWorld()
        {
            var world = new FarmWorld(new FarmClock());
            world.AddField("A1", new FarmPosition(0f, 2f));
            world.AddNpc("ahe", "阿禾", new FarmPosition(0f, -4f));
            world.AddNpc("aya", "阿芽", new FarmPosition(2f, -4f));
            world.SetProduceStore(new FarmPosition(0f, -4f));
            world.AddSeeds(CropCatalog.RadishId, 10);
            return world;
        }

        static FarmWorld WorldWithOrder(out WorkOrder order)
        {
            var world = NewWorld();
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A1", CropCatalog.RadishId, PlantRadish, out order, out var reason),
                reason);
            return world;
        }

        [Test]
        public async Task 说别干了就取消工单并释放地块()
        {
            var world = WorldWithOrder(out _);

            var result = await new IntentRouter().RouteAsync("别干了", world, world.GetNpc("ahe"));

            Assert.IsNull(world.WorkOrderOf("ahe"), "说「别干了」就该取消工单");
            Assert.IsFalse(world.ClaimOf("A1").IsClaimed, "取消立刻释放占位");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Reply), "取消也要回一句可读的话");
            Assert.IsFalse(result.Accepted, "取消不是接单");
            Assert.IsFalse(result.Refused, "取消不是拒单");
        }

        [Test]
        public async Task 取消与停下这些说法都能取消工单_结果带出被取消的工单()
        {
            foreach (var phrase in new[] { "取消", "停下", "别干了", "阿禾，取消", "你停下吧" })
            {
                var world = WorldWithOrder(out _);

                var result = await new IntentRouter().RouteAsync(phrase, world, world.GetNpc("ahe"));

                Assert.IsNull(world.WorkOrderOf("ahe"), $"「{phrase}」应能取消工单");
                Assert.IsFalse(world.ClaimOf("A1").IsClaimed, $"「{phrase}」应释放占位");
                Assert.IsTrue(result.CancelRequested, $"「{phrase}」的结果要标成取消请求");
                Assert.IsTrue(result.Cancelled, $"「{phrase}」要撤掉一张工单");
                Assert.AreEqual("A1", result.CancelledWorkOrder?.FieldId, $"「{phrase}」要说明撤的是哪张工单");
                Assert.AreEqual(IntentOutlet.FarmCommand, result.Outlet, "取消仍走农事指令这个出口");
            }
        }
        [Test]
        public async Task 问句与否定说法不取消工单()
        {
            foreach (var phrase in new[] { "别取消", "不要停下", "为什么取消了", "上次你让我别干了？" })
            {
                var world = WorldWithOrder(out _);

                var result = await new IntentRouter().RouteAsync(phrase, world, world.GetNpc("ahe"));

                Assert.IsNotNull(world.WorkOrderOf("ahe"), $"「{phrase}」不是取消，工单要留着");
                Assert.IsTrue(world.ClaimOf("A1").IsClaimed, $"「{phrase}」不该释放占位");
                Assert.IsFalse(result.CancelRequested, $"「{phrase}」不该标成取消请求");
            }
        }

        [Test]
        public async Task 句首叫的是别的NPC时不算取消()
        {
            foreach (var phrase in new[] { "阿芽别干了", "阿芽停下" })
            {
                var world = WorldWithOrder(out _);

                var result = await new IntentRouter().RouteAsync(phrase, world, world.GetNpc("ahe"));

                Assert.IsNotNull(world.WorkOrderOf("ahe"), $"「{phrase}」是在说别人，阿禾的工单要留着");
                Assert.IsFalse(result.CancelRequested, $"「{phrase}」不该标成取消请求");
            }
        }

        [Test]
        public async Task 没有工单时说别干了只回一句话_不造工单也不改地块()
        {
            var world = NewWorld();

            var result = await new IntentRouter().RouteAsync("别干了", world, world.GetNpc("ahe"));

            Assert.IsTrue(result.CancelRequested, "这是取消请求，只是没取消成");
            Assert.IsFalse(result.Cancelled, "本来就没活可撤");
            Assert.IsNull(result.AssignedWorkOrder, "取消不能顺手排一张新工单");
            Assert.IsNull(world.WorkOrderOf("ahe"));
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Reply), "要回一句可读的话");
            Assert.AreEqual(FieldState.Empty, world.GetField("A1").State, "取消不改地块");
            Assert.AreEqual(10, world.Ledger.SeedsOf(CropCatalog.RadishId), "取消不改背包");
            Assert.AreEqual(0, world.Log.Count, "取消不执行工步");
        }

        [Test]
        public async Task 取消由本地规则裁决_不为它调用模型()
        {
            var world = WorldWithOrder(out _);
            var client = new ScriptedModelClient(
                "{\"outlet\":\"farm_command\",\"goal\":\"plant\",\"field\":\"A1\",\"species\":\"radish\",\"reply\":\"\"}");

            var result = await new IntentRouter(client).RouteAsync("别干了", world, world.GetNpc("ahe"));

            Assert.AreEqual(0, client.Calls, "取消没有细节要模型补，不该为它发请求");
            Assert.AreEqual(ModelCallOutcome.Guard, result.Call.Outcome, "HUD 据此说明这句是本地规则裁决的");
            StringAssert.Contains("未调用模型", result.Call.Detail, "抽屉里要说明这一句没问过模型");
            Assert.IsTrue(result.Cancelled);
            Assert.IsNull(world.WorkOrderOf("ahe"), "模型哪怕会把它改写成农活，也轮不到它说话");
        }
    }
}
