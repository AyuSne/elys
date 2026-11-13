using System;
using System.Collections.Generic;

namespace ElysFarm.Demo
{
    /// <summary>剧本里的一拍。领域跑按顺序执行，演示层按同一份顺序回放，避免两处剧本漂移。</summary>
    public enum ScenarioBeatKind
    {
        Caption,
        Select,
        Say,
        WaitPlan,
        WaitOrderDone,
        WaitDay,
        WaitRest,
        SetSpeed,
        OpenInspector
    }

    /// <summary>
    /// 一拍剧本。除了文本与等待条件之外不带任何状态：
    /// 「说一句」只登记玩家输入，真正执行仍由 <see cref="ElysFarm.Core.FarmWorld"/> 的动作校验决定。
    /// </summary>
    public sealed class ScenarioBeat
    {
        ScenarioBeat(ScenarioBeatKind kind)
        {
            Kind = kind;
        }

        public ScenarioBeatKind Kind { get; }

        /// <summary>说的话或字幕标题。</summary>
        public string Text { get; private set; } = string.Empty;

        /// <summary>字幕的第二行。</summary>
        public string Detail { get; private set; } = string.Empty;

        public string NpcId { get; private set; } = string.Empty;

        /// <summary>等待的目标：<see cref="ScenarioBeatKind.WaitPlan"/> 是计划工步数，<see cref="ScenarioBeatKind.WaitDay"/> 是游戏日。</summary>
        public int Target { get; private set; }

        public float Speed { get; private set; } = 1f;

        /// <summary>开关类动作的目标值，目前只有 <see cref="ScenarioBeatKind.OpenInspector"/> 使用。</summary>
        public bool Open { get; private set; }

        /// <summary>这一拍在画面里停留多久（秒，真实时间）；只影响演示，不影响证据。</summary>
        public float Hold { get; private set; }

        public static ScenarioBeat Caption(string text, string detail, float hold = 4f)
        {
            return new ScenarioBeat(ScenarioBeatKind.Caption)
            {
                Text = text,
                Detail = detail,
                Hold = hold
            };
        }

        public static ScenarioBeat Select(string npcId)
        {
            return new ScenarioBeat(ScenarioBeatKind.Select) { NpcId = npcId };
        }

        public static ScenarioBeat Say(string text, float hold = 2.5f)
        {
            return new ScenarioBeat(ScenarioBeatKind.Say) { Text = text, Hold = hold };
        }

        public static ScenarioBeat WaitPlan(int count)
        {
            return new ScenarioBeat(ScenarioBeatKind.WaitPlan) { Target = count };
        }

        public static ScenarioBeat WaitOrderDone()
        {
            return new ScenarioBeat(ScenarioBeatKind.WaitOrderDone);
        }

        public static ScenarioBeat WaitDay(int day)
        {
            return new ScenarioBeat(ScenarioBeatKind.WaitDay) { Target = day };
        }

        public static ScenarioBeat WaitRest()
        {
            return new ScenarioBeat(ScenarioBeatKind.WaitRest);
        }

        public static ScenarioBeat SetSpeed(float speed)
        {
            return new ScenarioBeat(ScenarioBeatKind.SetSpeed) { Speed = speed };
        }

        public static ScenarioBeat OpenInspector(bool open)
        {
            return new ScenarioBeat(ScenarioBeatKind.OpenInspector) { Open = open };
        }
    }

    /// <summary>
    /// 三条固定剧本的台本（#10）。台词刻意用本地解析器认得的说法：
    /// 「中间那块」不靠字段名，而靠效用打分在唯一空着的地里挑；
    /// 「西边那块」由 <see cref="ElysFarm.Core.FieldLocator"/> 落到 A1。
    /// </summary>
    public static class FixedScreenplays
    {
        public const string FullFlowHint =
            "五步工单：播种 → 浇水 → 施肥 → 除草 → 收获；跨到第 4 天，种子库每天补货、产出仓入库。";

        public const string RefusalHint =
            "拒绝理由来自动作校验：白名单外的品种与还没熟的作物；阿芽干到体力见底，自己说要歇会儿。";

        public const string MemoryHint =
            "第 1 天让阿禾浇水，跨到第 2 天再问「上次是哪块」：回答要落到记忆里的方位与地块。";

        public static IReadOnlyList<ScenarioBeat> Of(FixedScenario scenario)
        {
            switch (scenario)
            {
                case FixedScenario.FullFlow:
                    return FullFlow();
                case FixedScenario.Refusal:
                    return Refusal();
                case FixedScenario.Memory:
                    return Memory();
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
            }
        }

        static IReadOnlyList<ScenarioBeat> FullFlow()
        {
            return new[]
            {
                ScenarioBeat.Caption(ScenarioCatalog.TitleOf(FixedScenario.FullFlow), FullFlowHint, 5f),
                ScenarioBeat.Select(FarmDemoBootstrap.AheId),

                // 中间那块是熟了的小麦：先收一茬，让地空出来。
                ScenarioBeat.Say("把中间那块地收了", 3f),
                ScenarioBeat.WaitPlan(1),

                // 不点名地块，交给效用打分挑唯一空着的那块。
                ScenarioBeat.Say("把中间那块地种满萝卜", 3f),
                ScenarioBeat.WaitPlan(1),

                ScenarioBeat.SetSpeed(16f),
                ScenarioBeat.Caption("时间推进 · 第 2 天", "浇水、施肥、除草按工单执行；16x 等杂草长出来。", 3f),
                ScenarioBeat.WaitPlan(4),

                ScenarioBeat.Caption("时间推进 · 第 4 天", "萝卜 3 个游戏日成熟，NPC 自己走回去收。", 3f),
                ScenarioBeat.WaitOrderDone(),
                ScenarioBeat.SetSpeed(1f),
                ScenarioBeat.Caption(
                    "剧本一完成",
                    "第 4 天：萝卜 3 个进产出仓；每天补萝卜 2、小麦 1，种子库 9 → 15 / 2 → 5，产出仓 0 → 8。",
                    5f)
            };
        }

        static IReadOnlyList<ScenarioBeat> Refusal()
        {
            return new[]
            {
                ScenarioBeat.Caption(ScenarioCatalog.TitleOf(FixedScenario.Refusal), RefusalHint, 5f),
                ScenarioBeat.Select(FarmDemoBootstrap.AyaId),

                ScenarioBeat.Say("去种月亮", 3.5f),
                ScenarioBeat.Say("把西边那块地收了", 3.5f),

                // 四次可执行的单步活，把阿芽的体力从 100% 打到 52%（人设阈值 55%）。
                ScenarioBeat.Say("给西边那块地浇水"),
                ScenarioBeat.WaitPlan(1),
                ScenarioBeat.Say("给西边那块地施肥"),
                ScenarioBeat.WaitPlan(1),
                ScenarioBeat.Say("把中间那块地收了"),
                ScenarioBeat.WaitPlan(1),
                ScenarioBeat.Say("把中间那块地种上萝卜"),
                ScenarioBeat.WaitPlan(1),

                ScenarioBeat.WaitRest(),
                ScenarioBeat.Say("你怎么不去干活", 5f),
                ScenarioBeat.Caption(
                    "剧本二完成",
                    "两次拒绝都没改状态；阿芽体力只剩 52%，在草垛边歇着。",
                    5f)
            };
        }

        static IReadOnlyList<ScenarioBeat> Memory()
        {
            return new[]
            {
                ScenarioBeat.Caption(ScenarioCatalog.TitleOf(FixedScenario.Memory), MemoryHint, 5f),
                ScenarioBeat.Select(FarmDemoBootstrap.AheId),

                ScenarioBeat.Say("给西边的地浇水", 3f),
                ScenarioBeat.WaitPlan(1),

                ScenarioBeat.SetSpeed(16f),
                ScenarioBeat.Caption("跨到第 2 天", "时钟与作物都由游戏日推进；昨天的指令留在记忆里。", 3f),
                ScenarioBeat.WaitDay(2),
                ScenarioBeat.SetSpeed(1f),

                ScenarioBeat.OpenInspector(true),
                ScenarioBeat.Say("上次你让我浇的是西边那块吗", 6f),
                ScenarioBeat.Caption(
                    "剧本三完成",
                    "回答引用第 1 天的记忆「西边（A1）」；右侧抽屉展示命中的记忆与权重。",
                    6f)
            };
        }
    }
}
