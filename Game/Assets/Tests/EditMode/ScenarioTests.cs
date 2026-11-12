using System.Linq;
using System.Threading.Tasks;
using ElysFarm.Core;
using ElysFarm.Demo;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #10 的三条固定剧本。测试打在 <see cref="FixedScenarios"/> 这条交付缝上：
    /// 每条剧本都从与演示相同的开局世界跑起，模型保持离线，所以同一句话的结局可重复。
    /// 预期值来自题目与规格（五个工步、萝卜 3 个游戏日、小麦收 5 个），
    /// 不从实现里反算。
    /// </summary>
    public class ScenarioTests
    {
        [Test]
        public async Task 全流程剧本_五个工步按序执行且背包收支相抵()
        {
            var report = await FixedScenarios.RunAsync(FixedScenario.FullFlow);

            Assert.IsNull(report.Failure, report.Failure);
            Assert.AreEqual(2, report.Commands.Count, "剧本一先收小麦，再种一季萝卜");

            var harvest = report.Commands[0];
            Assert.IsTrue(harvest.Accepted, harvest.Reply);
            Assert.AreEqual("A2", harvest.FieldId, "中间那块小麦可以直接收");
            CollectionAssert.AreEqual(new[] { WorkStep.Harvest }, harvest.PlannedSteps);
            CollectionAssert.AreEqual(new[] { WorkStep.Harvest }, harvest.Performed);

            var plant = report.Commands[1];
            Assert.IsTrue(plant.Accepted, plant.Reply);
            Assert.AreEqual("A2", plant.FieldId, "刚收完的地是唯一空着的地，交给效用打分挑");
            Assert.AreEqual(CropCatalog.RadishId, plant.SpeciesId);
            CollectionAssert.AreEqual(
                new[]
                {
                    WorkStep.Sow,
                    WorkStep.Water,
                    WorkStep.Fertilize,
                    WorkStep.Weed,
                    WorkStep.Harvest
                },
                plant.Performed,
                "五个工步应按顺序全部执行");

            var final = report.Final;
            Assert.AreEqual(4, final.Day, "第 1 天播种，萝卜 3 个游戏日后成熟，第 4 天收获");
            Assert.AreEqual(FieldState.Harvested, FieldById(final, "A2").State, "最后一步应是收获");
            Assert.AreEqual(
                CropStage.Seedling,
                FieldById(final, "A1").Stage,
                "剧本一不碰西边那块；没浇过水，四天后仍是幼苗\n" + report.Trace);
            Assert.AreEqual(CropStage.Mature, FieldById(final, "A3").Stage, "东边那块在这几天里自己熟了");

            Assert.IsTrue(final.LedgerVerified, final.LedgerError);
            Assert.AreEqual(9, final.RadishSeeds, "萝卜种子 12 颗，开局两块地用掉 2 颗、这季再用 1 颗");
            Assert.AreEqual(2, final.WheatSeeds, "小麦种子 4 颗，开局那块地用掉 2 颗");
            Assert.AreEqual(3, final.RadishProduce, "萝卜每次收获 3 个");
            Assert.AreEqual(5, final.WheatProduce, "小麦每次收获 5 个");
            Assert.AreEqual(16, final.SeedsAdded, "开局入账 12 萝卜 + 4 小麦");
            Assert.AreEqual(5, final.SeedsRemoved, "开局 2 萝卜 + 2 小麦，这季再 1 萝卜");
            Assert.AreEqual(8, final.ProduceAdded, "小麦 5 个 + 萝卜 3 个");

            CollectionAssert.AreEqual(
                new[]
                {
                    WorkStep.Harvest,
                    WorkStep.Sow,
                    WorkStep.Water,
                    WorkStep.Fertilize,
                    WorkStep.Weed,
                    WorkStep.Harvest
                },
                final.Performed,
                "跨两条命令的执行顺序也要对得上");
            StringAssert.Contains("scenario|full-flow", report.Trace);
        }

        [Test]
        public async Task 记忆体现剧本_跨日追问仍能对上第1天的浇水指令()
        {
            var report = await FixedScenarios.RunAsync(FixedScenario.Memory);

            Assert.IsNull(report.Failure, report.Failure);
            Assert.AreEqual(2, report.Commands.Count);

            var water = report.Commands[0];
            Assert.IsTrue(water.Accepted, water.Reply);
            Assert.AreEqual("A1", water.FieldId, "「西边」应落到 A1");
            CollectionAssert.AreEqual(new[] { WorkStep.Water }, water.Performed);

            var question = report.Commands[1];
            Assert.AreEqual(IntentOutlet.Query, question.Outlet, "带「上次」的问句是追问，不是新指令");
            StringAssert.Contains("第 1 天", question.Reply, "回答必须说出玩家下指令的游戏日");
            StringAssert.Contains("西边", question.Reply, "回答必须沿用当时的方位");
            StringAssert.Contains("A1", question.Reply, "回答必须落到真正浇过的地块");
            StringAssert.Contains("浇水", question.Reply);

            Assert.AreEqual(2, report.Final.Day, "跨日之后才问");
            Assert.AreEqual(
                CropStage.Sprout,
                FieldById(report.Final, "A1").Stage,
                "第 1 天浇过一次水，萝卜跨日长了一档");
            Assert.IsEmpty(report.Final.ActiveWorkOrders, "追问不能产生新工单");
            CollectionAssert.AreEqual(new[] { WorkStep.Water }, report.Final.Performed);
            Assert.IsTrue(report.Final.LedgerVerified, report.Final.LedgerError);
            Assert.AreEqual(10, report.Final.RadishSeeds, "剧本三不播种");
            Assert.AreEqual(0, report.Final.ProduceAdded, "剧本三不收获");
            StringAssert.Contains("scenario|memory", report.Trace);
        }

        [Test]
        public async Task 拒绝与抱怨剧本_两次拒绝给理由且体力见底会抱怨()
        {
            var report = await FixedScenarios.RunAsync(FixedScenario.Refusal);

            Assert.IsNull(report.Failure, report.Failure);
            Assert.AreEqual(7, report.Commands.Count, "两次拒绝 + 四次干活 + 一次抱怨");

            var unknownCrop = report.Commands[0];
            Assert.IsFalse(unknownCrop.Accepted);
            Assert.IsTrue(unknownCrop.Outlet == IntentOutlet.FarmCommand, "白名单外的品种仍走农事指令的拒绝");
            StringAssert.Contains("月亮", unknownCrop.Reply, "拒绝要说清楚种不出来的是什么");
            Assert.IsNull(unknownCrop.FieldId, "拒绝不产生工单");

            var notMature = report.Commands[1];
            Assert.IsFalse(notMature.Accepted);
            StringAssert.Contains("还没熟", notMature.Reply, "没熟的作物要给出可读理由");
            Assert.IsNull(notMature.FieldId);

            CollectionAssert.AreEqual(
                new[] { WorkStep.Water, WorkStep.Fertilize, WorkStep.Harvest, WorkStep.Sow },
                report.Final.Performed,
                "拒绝不该插进执行流水；四次单步活各执行一次");
            Assert.AreEqual(1, report.Final.Day, "剧本二不推进游戏日");

            var complaint = report.Commands[6];
            Assert.AreEqual(IntentOutlet.Query, complaint.Outlet, "「你怎么不去干活」是追问，不是新指令");
            StringAssert.Contains("累了", complaint.Reply, "NPC 要自己说出为什么没干活");
            StringAssert.Contains("52%", complaint.Reply, "抱怨要带真实体力，不能是套话");
            CollectionAssert.Contains(report.Final.RestingNpcIds, FarmDemoBootstrap.AyaId);

            Assert.IsTrue(report.Final.LedgerVerified, report.Final.LedgerError);
            Assert.AreEqual(9, report.Final.RadishSeeds);
            Assert.AreEqual(2, report.Final.WheatSeeds);
            Assert.AreEqual(0, report.Final.RadishProduce, "萝卜还没种出来");
            Assert.AreEqual(5, report.Final.WheatProduce, "中间那块小麦在剧本二里被收了");
            Assert.AreEqual(5, report.Final.SeedsRemoved, "开局两块萝卜 + 一块小麦，再加这次播种");
            Assert.AreEqual(5, report.Final.ProduceAdded);
        }

        [Test]
        public async Task 三条剧本连续跑三次_证据逐字一致()
        {
            var first = await FixedScenarios.RunAllAsync();
            var second = await FixedScenarios.RunAllAsync();
            var third = await FixedScenarios.RunAllAsync();

            Assert.AreEqual(3, first.Count);
            for (var i = 0; i < first.Count; i++)
            {
                var scenario = first[i].Scenario;
                Assert.IsNull(first[i].Failure, $"{ScenarioCatalog.TitleOf(scenario)}：{first[i].Failure}");
                Assert.AreEqual(
                    first[i].Trace,
                    second[i].Trace,
                    $"{ScenarioCatalog.TitleOf(scenario)} 第二次运行与第一次不一致");
                Assert.AreEqual(
                    first[i].Trace,
                    third[i].Trace,
                    $"{ScenarioCatalog.TitleOf(scenario)} 第三次运行与第一次不一致");
                StringAssert.Contains("scenario|" + ScenarioCatalog.IdOf(scenario), first[i].Trace);
            }

            CollectionAssert.AllItemsAreUnique(first.Select(report => report.Title).ToList());
        }

        static ScenarioField FieldById(ScenarioFinal final, string id)
        {
            return final.Fields.Single(field => field.Id == id);
        }
    }
}
