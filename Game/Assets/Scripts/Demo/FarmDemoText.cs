using System;
using System.Collections.Generic;
using System.Text;
using ElysFarm.Core;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 演示层的中文文案。领域层的意图与记忆也需要工步中文名，因此词表在 Core 的
    /// <see cref="WorkStepLabel"/>；这里转发，保证 HUD 与流水用的是同一套工步名。
    /// </summary>
    public static class FarmDemoText
    {
        public static string StepName(WorkStep step)
        {
            return WorkStepLabel.Of(step);
        }

        public static string SpeciesName(string speciesId)
        {
            return CropCatalog.TryGet(speciesId, out var species) ? species.DisplayName : speciesId;
        }

        /// <summary>
        /// 种子库补货的流水行：同一天的补货合成一行，最多显示最近 maxDays 天，按天数从早到晚。
        /// 数字直接来自领域留下的 <see cref="SeedRestock"/>，HUD 不另算一遍。
        /// </summary>
        public static IReadOnlyList<string> RestockLines(IReadOnlyList<SeedRestock> restocks, int maxDays)
        {
            var lines = new List<string>();
            if (restocks == null || restocks.Count == 0 || maxDays <= 0)
            {
                return lines;
            }

            // 从最近的一天往回收，最多 maxDays 个不同的游戏日；最后翻回时间顺序。
            var days = new List<int>();
            for (var i = restocks.Count - 1; i >= 0 && days.Count < maxDays; i--)
            {
                if (!days.Contains(restocks[i].Day))
                {
                    days.Add(restocks[i].Day);
                }
            }

            days.Reverse();
            foreach (var day in days)
            {
                lines.Add(RestockLineOf(restocks, day));
            }

            return lines;
        }

        static string RestockLineOf(IReadOnlyList<SeedRestock> restocks, int day)
        {
            var builder = new StringBuilder($"第 {day} 天 · 种子库补货");
            foreach (var entry in restocks)
            {
                if (entry.Day == day)
                {
                    builder.Append(" · ").Append(SpeciesName(entry.SpeciesId)).Append(" +").Append(entry.Count);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// 三人卡那一行的当前意图。NPC 手上有工单或在休息时直接用领域给出的意图行
        /// （<see cref="FarmWorld.IntentOf"/>，也就是决策抽屉第 2 项的那句话）——
        /// 「等条件 / 跳过 / 没做成」因此不会在卡片与抽屉上各说一套；其余时候才显示演示层
        /// 自己的临时状态（前往、待命、跟 X 说…）。
        /// </summary>
        public static string CardIntent(FarmWorld world, Npc npc, string demoIntent)
        {
            if (world == null || npc == null)
            {
                return demoIntent;
            }

            return npc.IsResting || world.WorkOrderOf(npc.Id) != null
                ? world.IntentOf(npc.Id)
                : demoIntent;
        }

        /// <summary>
        /// 把 HUD 里的一行动态文案截短，避免面板被长句撑开。
        /// 卡片那一行按像素宽度截（<see cref="Fit"/>），这里给字数固定、不必量字宽的场合；
        /// 取的是「放得下 maxLength 个字」这同一个含义，截短规则只此一份。
        /// </summary>
        public static string Shorten(string text, int maxLength)
        {
            return Fit(text, maxLength, LengthOf);
        }

        static float LengthOf(string text)
        {
            return text?.Length ?? 0;
        }

        /// <summary>
        /// 按可用宽度截短一行文案：放得下就原样返回，放不下就截到最后一个放得下的字再加省略号。
        /// 字宽由调用方量（HUD 用当前字体样式），所以这里不依赖 UnityEngine，可以单测。
        /// 三人卡与决策抽屉是同一句话，卡片只按自己的宽度收尾，不会把字切在边上。
        /// </summary>
        public static string Fit(string text, float maxWidth, Func<string, float> measure)
        {
            if (string.IsNullOrEmpty(text) || measure == null || maxWidth <= 0f || measure(text) <= maxWidth)
            {
                return text;
            }

            for (var length = text.Length - 1; length > 0; length--)
            {
                var candidate = text.Substring(0, length).TrimEnd() + "…";
                if (measure(candidate) <= maxWidth)
                {
                    return candidate;
                }
            }

            return "…";
        }

        /// <summary>把路由器接单的结果摊成一行「理解成」，让玩家确认没听错。</summary>
        public static string Plan(string fieldId, string speciesId, IReadOnlyList<WorkStep> steps)
        {
            var builder = new StringBuilder("理解成：");
            builder.Append(fieldId);

            if (!string.IsNullOrWhiteSpace(speciesId))
            {
                builder.Append(" · ").Append(SpeciesName(speciesId));
            }

            if (steps != null && steps.Count > 0)
            {
                builder.Append(" · ");
                for (var i = 0; i < steps.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append("→");
                    }

                    builder.Append(StepName(steps[i]));
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// 最近一次模型调用的摘要。失败的调用也保留能拿到的耗时与 token；
        /// 超时 / 断网时模型名缺失，用结果词代替。
        /// </summary>
        public static string Call(ModelCallInfo call)
        {
            var tokens = $"{call.PromptTokens}+{call.CompletionTokens} tok";
            switch (call.Outcome)
            {
                case ModelCallOutcome.Ok:
                    return $"{ModelName(call)} {call.LatencySeconds:0.00}s · {tokens}";
                case ModelCallOutcome.Timeout:
                    return $"{ModelName(call)} 超时 {call.LatencySeconds:0.0}s 上限 · token 未返回 · 回退模板";
                case ModelCallOutcome.Error:
                    return $"{ModelName(call)} 失败 {call.LatencySeconds:0.00}s · token 未返回 · 回退模板";
                case ModelCallOutcome.Unusable:
                    return $"{ModelName(call)} {call.LatencySeconds:0.00}s · {tokens} · 回退模板";
                case ModelCallOutcome.Guard:
                    return string.IsNullOrWhiteSpace(call.Detail)
                        ? "本地白名单校验（未调用模型）"
                        : call.Detail;
                case ModelCallOutcome.Correction:
                    return $"{ModelName(call)} {call.LatencySeconds:0.00}s · {tokens} · 本地出口纠正";
                default:
                    return "未调用（本地模式）";
            }
        }

        static string ModelName(ModelCallInfo call)
        {
            return string.IsNullOrWhiteSpace(call.Model) ? "未知模型" : call.Model;
        }
    }
}
