using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ElysFarm.Core;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 三条固定剧本的领域执行器（#10）。只打 <see cref="FarmWorld"/> 与
    /// <see cref="IntentRouter"/> 两条既有接缝：不开场景、不联网、不看墙钟，
    /// 因此同一份台本连续跑三次必须得到逐字相同的证据文本。
    /// 演示层用同一份 <see cref="FixedScreenplays"/> 与同一张 <see cref="ScenarioTrace"/>，
    /// 这样录屏里发生的事就是这里断言过的事。
    /// </summary>
    public static class FixedScenarios
    {
        /// <summary>单条命令里推进工步的保险丝；正常剧本几十步内就结束。</summary>
        const int MaxDriveSteps = 800;

        public static async Task<ScenarioReport> RunAsync(FixedScenario scenario)
        {
            var world = FarmDemoWorld.Create();
            var router = new IntentRouter();
            var report = new ScenarioReport
            {
                Scenario = scenario,
                Title = ScenarioCatalog.TitleOf(scenario)
            };
            var trace = new ScenarioTrace();
            trace.Header(scenario, report.Title);

            try
            {
                var runner = new DomainRun(world, router, report, trace);
                await runner.RunAsync(FixedScreenplays.Of(scenario));
            }
            catch (Exception ex)
            {
                report.Fail($"剧本执行失败：{ex.GetType().Name}：{ex.Message}");
            }

            report.Final = Snapshot(world, report.Commands);
            trace.Final(report.Final);
            report.Trace = trace.Text;
            return report;
        }

        /// <summary>三条剧本按顺序各跑一次；不并行，避免共享的静态状态影响证据。</summary>
        public static async Task<IReadOnlyList<ScenarioReport>> RunAllAsync()
        {
            var reports = new List<ScenarioReport>(ScenarioCatalog.All.Count);
            foreach (var scenario in ScenarioCatalog.All)
            {
                reports.Add(await RunAsync(scenario));
            }

            return reports;
        }

        /// <summary>把世界收敛成可比较、可写进证据的结局；演示层与领域跑共用。</summary>
        internal static ScenarioFinal Snapshot(FarmWorld world, IReadOnlyList<ScenarioCommand> commands)
        {
            var fields = new List<ScenarioField>(world.Fields.Count);
            foreach (var field in world.Fields)
            {
                fields.Add(new ScenarioField
                {
                    Id = field.Id,
                    State = field.State,
                    Stage = CropStages.Of(field.State, field.Growth),
                    CropId = field.Crop?.Id ?? string.Empty
                });
            }

            var performed = new List<WorkStep>();
            foreach (var command in commands)
            {
                if (command.Performed == null)
                {
                    continue;
                }

                performed.AddRange(command.Performed);
            }

            var resting = new List<string>();
            var orders = new List<string>();
            foreach (var npc in world.Npcs)
            {
                if (npc.IsResting)
                {
                    resting.Add(npc.Id);
                }

                var order = world.WorkOrderOf(npc.Id);
                if (order != null)
                {
                    var next = order.NextStep;
                    orders.Add($"{npc.Id}:{order.FieldId}/{WorkStepLabel.Of(next.Value)}");
                }
            }

            var verified = world.Ledger.Verify(out var ledgerError);

            // 补货流水既进 HUD 也进证据：种子数为什么涨，三份 trace 里读得到。
            var radishRestocked = 0;
            var wheatRestocked = 0;
            foreach (var restock in world.SeedRestocks)
            {
                if (restock.SpeciesId == CropCatalog.RadishId)
                {
                    radishRestocked += restock.Count;
                }
                else if (restock.SpeciesId == CropCatalog.WheatId)
                {
                    wheatRestocked += restock.Count;
                }
            }

            return new ScenarioFinal
            {
                Day = world.Clock.Day,
                Fields = fields,
                RadishSeeds = world.Ledger.SeedsOf(CropCatalog.RadishId),
                WheatSeeds = world.Ledger.SeedsOf(CropCatalog.WheatId),
                RadishProduce = world.Ledger.ProduceOf(CropCatalog.RadishId),
                WheatProduce = world.Ledger.ProduceOf(CropCatalog.WheatId),
                SeedsAdded = world.Ledger.SeedsAdded,
                SeedsRemoved = world.Ledger.SeedsRemoved,
                RadishRestocked = radishRestocked,
                WheatRestocked = wheatRestocked,
                ProduceAdded = world.Ledger.ProduceAdded,
                LedgerVerified = verified,
                LedgerError = ledgerError ?? string.Empty,
                Performed = performed,
                RestingNpcIds = resting,
                ActiveWorkOrders = orders
            };
        }

        sealed class DomainRun
        {
            readonly FarmWorld world;
            readonly IntentRouter router;
            readonly ScenarioReport report;
            readonly ScenarioTrace trace;
            string selectedNpcId = FarmDemoBootstrap.DefaultNpcId;
            ScenarioCommand currentCommand;

            public DomainRun(FarmWorld world, IntentRouter router, ScenarioReport report, ScenarioTrace trace)
            {
                this.world = world;
                this.router = router;
                this.report = report;
                this.trace = trace;
            }

            public async Task RunAsync(IReadOnlyList<ScenarioBeat> beats)
            {
                foreach (var beat in beats)
                {
                    if (report.Failure != null)
                    {
                        return;
                    }

                    switch (beat.Kind)
                    {
                        case ScenarioBeatKind.Caption:
                            trace.Caption(beat.Text, beat.Detail);
                            break;
                        case ScenarioBeatKind.Select:
                            if (ViewOf(beat.NpcId) == null)
                            {
                                report.Fail($"剧本点名了不存在的 NPC：{beat.NpcId}");
                                return;
                            }

                            selectedNpcId = beat.NpcId;
                            break;
                        case ScenarioBeatKind.Say:
                            await SayAsync(beat.Text);
                            break;
                        case ScenarioBeatKind.WaitPlan:
                            DrivePlan(beat.Target);
                            break;
                        case ScenarioBeatKind.WaitOrderDone:
                            DriveOrderDone();
                            break;
                        case ScenarioBeatKind.WaitDay:
                            DriveDay(beat.Target);
                            break;
                        case ScenarioBeatKind.WaitRest:
                            DriveRest();
                            break;
                        case ScenarioBeatKind.SetSpeed:
                            // 领域跑按游戏日推进，不模拟倍率；倍率只影响演示的等待时长。
                            break;
                        case ScenarioBeatKind.OpenInspector:
                            // 决策抽屉只读，不影响领域状态。
                            break;
                        default:
                            report.Fail($"未知剧本拍：{beat.Kind}");
                            return;
                    }
                }
            }

            async Task SayAsync(string text)
            {
                var npc = ViewOf(selectedNpcId);
                var logStart = world.Log.Count;
                IntentResult result;
                try
                {
                    result = await router.RouteAsync(text, world, npc);
                }
                catch (Exception ex)
                {
                    report.Fail($"「{text}」路由失败：{ex.Message}");
                    return;
                }

                currentCommand = report.AddCommand(result, npc.Id, text);
                currentCommand.LogStart = logStart;
                trace.Command(currentCommand);
            }

            void DrivePlan(int count)
            {
                var npc = ViewOf(selectedNpcId);
                if (currentCommand == null)
                {
                    report.Fail("还没接单就开始等工步");
                    return;
                }

                var order = world.WorkOrderOf(npc.Id);
                if (order == null)
                {
                    report.Fail($"「{currentCommand.Text}」没有登记工单");
                    return;
                }

                for (var guard = 0; guard < MaxDriveSteps; guard++)
                {
                    if (world.WorkOrderOf(npc.Id) == null || order.CompletedCount >= count)
                    {
                        break;
                    }

                    var result = world.AdvanceWorkOrder(npc.Id);
                    if (!result.Executed && !WaitForNpc(world, npc, result.Reason, report))
                    {
                        return;
                    }
                }

                UpdatePerformed(currentCommand);
                trace.PlanSteps(count, currentCommand.Performed);
                if (currentCommand.Performed.Count < count)
                {
                    report.Fail(
                        $"「{currentCommand.Text}」只执行了 {currentCommand.Performed.Count}/{count} 个计划工步");
                }
            }

            void DriveOrderDone()
            {
                var npc = ViewOf(selectedNpcId);
                if (currentCommand == null)
                {
                    report.Fail("还没接单就开始等收工");
                    return;
                }

                for (var guard = 0; guard < MaxDriveSteps; guard++)
                {
                    if (world.WorkOrderOf(npc.Id) == null)
                    {
                        break;
                    }

                    var result = world.AdvanceWorkOrder(npc.Id);
                    if (!result.Executed && !WaitForNpc(world, npc, result.Reason, report))
                    {
                        return;
                    }
                }

                UpdatePerformed(currentCommand);
                trace.PlanSteps(currentCommand.PlannedSteps.Count, currentCommand.Performed);
                if (currentCommand.Performed.Count < currentCommand.PlannedSteps.Count)
                {
                    report.Fail(
                        $"「{currentCommand.Text}」没跑完：{currentCommand.Performed.Count}/{currentCommand.PlannedSteps.Count}");
                }
            }

            /// <summary>
            /// 工步没执行时，只有「等作物 / 等杂草 / 等水分下降」与「NPC 在休息」算自然等待；
            /// 种子不足、地块被占、工单不存在这类错误必须让剧本失败，不能靠过一天掩盖。
            /// 自然等待分别用游戏日与休息秒数推进，证据里的 day 因此来自时间推进本身。
            /// </summary>
            static bool WaitForNpc(FarmWorld world, Npc npc, string reason, ScenarioReport report)
            {
                if (npc.IsResting)
                {
                    world.Tick(npc.RestSecondsRemaining + 0.01f);
                    return true;
                }

                if (IsNaturalFieldWait(reason))
                {
                    world.AdvanceDays(1);
                    return true;
                }

                report.Fail($"{npc.DisplayName}被挡住了：{reason ?? "没有可执行工步"}");
                return false;
            }

            static bool IsNaturalFieldWait(string reason)
            {
                // 剧本自己的等待清单：只认「等作物 / 等杂草 / 等水分下降」这类自然等待。
                // 领域层的工单释放规则（ActionValidator.IsWaiting）不参与判定，两边各管各的。
                return reason == "还没熟"
                    || reason == "没什么草可除"
                    || reason == "刚浇过，不渴"
                    || reason == "肥力还够";
            }

            void DriveDay(int day)
            {
                for (var guard = 0; guard < MaxDriveSteps && world.Clock.Day < day; guard++)
                {
                    world.AdvanceDays(1);
                }

                trace.Day(world.Clock.Day);
                if (world.Clock.Day < day)
                {
                    report.Fail($"时间没推进到第 {day} 天（现在第 {world.Clock.Day} 天）");
                }
            }

            void DriveRest()
            {
                var npc = ViewOf(selectedNpcId);
                trace.Resting(npc.Id);
                if (!npc.IsResting)
                {
                    report.Fail($"{npc.DisplayName}没有进入休息（体力 {npc.Stamina:P0}）");
                }
            }

            void UpdatePerformed(ScenarioCommand command)
            {
                command.Performed = ScenarioLog.PerformedIn(world, command.LogStart, command.PlannedSteps);
            }

            Npc ViewOf(string npcId)
            {
                foreach (var npc in world.Npcs)
                {
                    if (npc.Id == npcId)
                    {
                        return npc;
                    }
                }

                return null;
            }
        }
    }
}
