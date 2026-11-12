using System;
using System.Collections.Generic;
using System.Text;
using ElysFarm.Core;

namespace ElysFarm.Demo
{
    /// <summary>
    /// #10 的三条固定剧本。id 同时用于命令行（<c>-elysScenario full-flow</c>）、
    /// 证据文件名与测试报告，所以一旦发布不要再改。
    /// </summary>
    public enum FixedScenario
    {
        /// <summary>全流程：收小麦、种萝卜、五步工单、跨日成熟、背包收支。</summary>
        FullFlow,

        /// <summary>拒绝与抱怨：白名单外品种、没熟的作物、体力见底后的休息理由。</summary>
        Refusal,

        /// <summary>记忆体现：第 1 天的浇水指令，跨到第 2 天仍能对上方位与地块。</summary>
        Memory
    }

    /// <summary>一条剧本的剧本名与命令行 id。</summary>
    public static class ScenarioCatalog
    {
        public static IReadOnlyList<FixedScenario> All { get; } = new[]
        {
            FixedScenario.FullFlow,
            FixedScenario.Refusal,
            FixedScenario.Memory
        };

        public static string IdOf(FixedScenario scenario)
        {
            switch (scenario)
            {
                case FixedScenario.FullFlow:
                    return "full-flow";
                case FixedScenario.Refusal:
                    return "refusal";
                case FixedScenario.Memory:
                    return "memory";
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
            }
        }

        public static string TitleOf(FixedScenario scenario)
        {
            switch (scenario)
            {
                case FixedScenario.FullFlow:
                    return "剧本一 · 全流程";
                case FixedScenario.Refusal:
                    return "剧本二 · 拒绝与抱怨";
                case FixedScenario.Memory:
                    return "剧本三 · 记忆体现";
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
            }
        }

        public static bool TryParse(string id, out FixedScenario scenario)
        {
            foreach (var candidate in All)
            {
                if (string.Equals(IdOf(candidate), id, StringComparison.OrdinalIgnoreCase))
                {
                    scenario = candidate;
                    return true;
                }
            }

            scenario = default;
            return false;
        }
    }

    /// <summary>剧本里的一次玩家输入及其结果。演示层与领域跑共用这一份结构，剧本回放才能逐字对上。</summary>
    public sealed class ScenarioCommand
    {
        /// <summary>在剧本里的序号，从 0 开始。</summary>
        public int Index { get; internal set; }

        /// <summary>这条命令说给谁听。</summary>
        public string NpcId { get; internal set; }

        /// <summary>玩家原话。</summary>
        public string Text { get; internal set; }

        /// <summary>意图出口：农事指令 / 追问 / 闲聊。</summary>
        public IntentOutlet Outlet { get; internal set; }

        /// <summary>农事指令是否被接单；追问与闲聊恒为 false。</summary>
        public bool Accepted { get; internal set; }

        /// <summary>接单地块；未接单为 null。</summary>
        public string FieldId { get; internal set; }

        /// <summary>播种品种；非播种为 null。</summary>
        public string SpeciesId { get; internal set; }

        /// <summary>接单时登记的计划工步；未接单为空。</summary>
        public IReadOnlyList<WorkStep> PlannedSteps { get; internal set; } = Array.Empty<WorkStep>();

        /// <summary>这条命令的计划工步里真正执行过的，按执行顺序；维护工步不算。</summary>
        public IReadOnlyList<WorkStep> Performed { get; internal set; } = Array.Empty<WorkStep>();

        /// <summary>NPC 的回应或拒绝台词。</summary>
        public string Reply { get; internal set; } = string.Empty;

        /// <summary>接单时流水已有的条数；演示层与领域跑都从这里开始数这条命令执行了哪些工步。</summary>
        internal int LogStart { get; set; }
    }

    /// <summary>剧本结束时某块地的可观察状态。生长进度是连续值，只报阶段，避免浮点误差进证据。</summary>
    public sealed class ScenarioField
    {
        /// <summary>地块 id。</summary>
        public string Id { get; internal set; }

        /// <summary>地块状态（空地 / 已播种 / 生长中 / 成熟 / 已收获）。</summary>
        public FieldState State { get; internal set; }

        /// <summary>表现层的五个生长阶段之一。</summary>
        public CropStage Stage { get; internal set; }

        /// <summary>地块上的品种；空地或已收获为空。</summary>
        public string CropId { get; internal set; } = string.Empty;
    }

    /// <summary>一条剧本跑完后的可观察结局；证据文件与测试都读它。</summary>
    public sealed class ScenarioFinal
    {
        /// <summary>结束时的游戏日。</summary>
        public int Day { get; internal set; }

        /// <summary>三块地的最终状态，按世界里的顺序。</summary>
        public IReadOnlyList<ScenarioField> Fields { get; internal set; } = Array.Empty<ScenarioField>();

        /// <summary>种子库里剩余的萝卜种子。</summary>
        public int RadishSeeds { get; internal set; }

        /// <summary>种子库里剩余的小麦种子。</summary>
        public int WheatSeeds { get; internal set; }

        /// <summary>产出仓里的萝卜数量。</summary>
        public int RadishProduce { get; internal set; }

        /// <summary>产出仓里的小麦数量。</summary>
        public int WheatProduce { get; internal set; }

        /// <summary>累计入账的种子数。</summary>
        public int SeedsAdded { get; internal set; }

        /// <summary>累计被播种消耗的种子数。</summary>
        public int SeedsRemoved { get; internal set; }

        /// <summary>累计入库的产出数。</summary>
        public int ProduceAdded { get; internal set; }

        /// <summary>三本账是否收支相抵。</summary>
        public bool LedgerVerified { get; internal set; }

        /// <summary>不守恒时的可读原因；守恒为空。</summary>
        public string LedgerError { get; internal set; } = string.Empty;

        /// <summary>所有命令里实际执行过的计划工步，按发生顺序。</summary>
        public IReadOnlyList<WorkStep> Performed { get; internal set; } = Array.Empty<WorkStep>();

        /// <summary>结束时在休息的 NPC id。</summary>
        public IReadOnlyList<string> RestingNpcIds { get; internal set; } = Array.Empty<string>();

        /// <summary>还挂着的工单，格式 <c>npc:地块/工步</c>；没有为空。</summary>
        public IReadOnlyList<string> ActiveWorkOrders { get; internal set; } = Array.Empty<string>();
    }

    /// <summary>
    /// 从流水里挑出「这条命令的计划工步」，按计划顺序匹配、忽略维护工步。
    /// 领域跑与演示回放共用同一段匹配，证据才对得上。
    /// </summary>
    internal static class ScenarioLog
    {
        public static IReadOnlyList<WorkStep> PerformedIn(
            FarmWorld world,
            int logStart,
            IReadOnlyList<WorkStep> plan)
        {
            var performed = new List<WorkStep>();
            if (world == null || plan == null || plan.Count == 0)
            {
                return performed;
            }

            var index = 0;
            for (var i = Math.Max(0, logStart); i < world.Log.Count && index < plan.Count; i++)
            {
                if (world.Log[i].Step == plan[index])
                {
                    performed.Add(plan[index]);
                    index++;
                }
            }

            return performed;
        }
    }

    /// <summary>一条剧本的完整结果：谁说了什么、结局如何、以及可逐字比较的证据文本。</summary>
    public sealed class ScenarioReport
    {
        /// <summary>剧本 id。</summary>
        public FixedScenario Scenario { get; internal set; }

        /// <summary>剧本标题，供 HUD 与报告使用。</summary>
        public string Title { get; internal set; } = string.Empty;

        readonly List<ScenarioCommand> commands = new List<ScenarioCommand>();

        /// <summary>按顺序记录的玩家输入。</summary>
        public IReadOnlyList<ScenarioCommand> Commands => commands;

        /// <summary>剧本结束时的可观察结局。</summary>
        public ScenarioFinal Final { get; internal set; } = new ScenarioFinal();

        /// <summary>规范化证据文本：同样的剧本必须逐字相同，不带墙钟时间。</summary>
        public string Trace { get; internal set; } = string.Empty;

        /// <summary>剧本没跑通时的第一条可读原因；跑通为 null。</summary>
        public string Failure { get; internal set; }

        internal ScenarioCommand AddCommand(IntentResult result, string npcId, string text)
        {
            var command = new ScenarioCommand
            {
                Index = commands.Count,
                NpcId = npcId,
                Text = text,
                Outlet = result.Outlet,
                Accepted = result.Accepted,
                FieldId = result.FieldId,
                SpeciesId = result.SpeciesId,
                PlannedSteps = result.PlannedSteps ?? Array.Empty<WorkStep>(),
                Reply = result.Reply
            };
            commands.Add(command);
            return command;
        }

        internal void Fail(string reason)
        {
            if (Failure == null)
            {
                Failure = reason;
            }
        }
    }

    /// <summary>
    /// 剧本证据的唯一格式。字段用 <c>|</c> 分隔、换行用空格、值里的分隔符转义，
    /// 这样三份文件可以直接 <c>fc</c>/<c>diff</c>，读起来也不需要解析器。
    /// </summary>
    internal sealed class ScenarioTrace
    {
        readonly StringBuilder builder = new StringBuilder();

        public void Header(FixedScenario scenario, string title)
        {
            Line("scenario", ScenarioCatalog.IdOf(scenario), title);
        }

        public void Caption(string title, string detail)
        {
            Line("caption", title, detail);
        }

        public void Command(ScenarioCommand command)
        {
            Line(
                "command",
                command.Index.ToString(),
                command.NpcId,
                command.Text,
                command.Outlet.ToString(),
                command.Accepted ? "accepted" : command.Outlet == IntentOutlet.FarmCommand ? "refused" : "readonly",
                Value(command.FieldId),
                Value(command.SpeciesId),
                Steps(command.PlannedSteps),
                command.Reply);
        }

        public void PlanSteps(int expected, IReadOnlyList<WorkStep> performed)
        {
            Line("steps", expected.ToString(), Steps(performed));
        }

        public void Day(int day)
        {
            Line("day", day.ToString());
        }

        public void Resting(string npcId)
        {
            Line("resting", npcId);
        }

        public void Final(ScenarioFinal final)
        {
            var fields = new StringBuilder();
            foreach (var field in final.Fields)
            {
                if (fields.Length > 0)
                {
                    fields.Append(';');
                }

                fields.Append(field.Id)
                    .Append(':')
                    .Append(field.State)
                    .Append('/')
                    .Append(field.Stage)
                    .Append('/')
                    .Append(Value(field.CropId));
            }

            Line(
                "final",
                $"day={final.Day}",
                $"fields={fields}",
                $"ledger=radish:{final.RadishSeeds},{final.RadishProduce};wheat:{final.WheatSeeds},{final.WheatProduce}",
                $"totals={final.SeedsAdded},{final.SeedsRemoved},{final.ProduceAdded}",
                $"performed={Steps(final.Performed)}",
                $"resting={Join(final.RestingNpcIds)}",
                $"orders={Join(final.ActiveWorkOrders)}",
                $"ledger_ok={final.LedgerVerified}");
        }

        public string Text => builder.ToString().TrimEnd();

        void Line(params string[] fields)
        {
            for (var i = 0; i < fields.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append('|');
                }

                builder.Append(Escape(fields[i]));
            }

            builder.Append('\n');
        }

        static string Steps(IReadOnlyList<WorkStep> steps)
        {
            if (steps == null || steps.Count == 0)
            {
                return "-";
            }

            var text = new StringBuilder();
            for (var i = 0; i < steps.Count; i++)
            {
                if (i > 0)
                {
                    text.Append('→');
                }

                text.Append(WorkStepLabel.Of(steps[i]));
            }

            return text.ToString();
        }

        static string Join(IReadOnlyList<string> values)
        {
            return values == null || values.Count == 0 ? "-" : string.Join(",", values);
        }

        static string Value(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("|", "¦")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }
    }
}
