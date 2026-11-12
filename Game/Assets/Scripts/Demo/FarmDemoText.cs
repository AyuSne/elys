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

        public static string StateName(FieldState state)
        {
            switch (state)
            {
                case FieldState.Empty:
                    return "空地";
                case FieldState.Sown:
                    return "已播种";
                case FieldState.Growing:
                    return "生长中";
                case FieldState.Mature:
                    return "成熟";
                case FieldState.Harvested:
                    return "已收获";
                default:
                    return state.ToString();
            }
        }

        public static string SpeciesName(string speciesId)
        {
            return CropCatalog.TryGet(speciesId, out var species) ? species.DisplayName : speciesId;
        }

        /// <summary>人设的一句话注脚；人设参数在领域层，这里只放展示用词。</summary>
        public static string PersonaNote(string personaId)
        {
            switch (personaId)
            {
                case "ahe":
                    return "稳、勤、话少";
                case "aya":
                    return "快、爱偷懒、爱甩锅";
                case "atu":
                    return "慢、懂多、爱八卦";
                default:
                    return "自定义人设";
            }
        }

        /// <summary>把 HUD 里的一行动态文案截短，避免面板被长句撑开。</summary>
        public static string Shorten(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text;
            }

            return text.Substring(0, maxLength) + "…";
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
                    return "本地白名单校验（未调用模型）";
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
