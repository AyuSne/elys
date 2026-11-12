using System.Collections.Generic;
using System.Text;
using ElysFarm.Core;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 演示层的中文文案。领域层不关心展示用词，把它们集中放在这里，
    /// 也保证 HUD 与流水用的是同一套工步名。
    /// </summary>
    public static class FarmDemoText
    {
        public static string StepName(WorkStep step)
        {
            switch (step)
            {
                case WorkStep.Sow:
                    return "播种";
                case WorkStep.Water:
                    return "浇水";
                case WorkStep.Fertilize:
                    return "施肥";
                case WorkStep.Weed:
                    return "除草";
                case WorkStep.Harvest:
                    return "收获";
                default:
                    return step.ToString();
            }
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

        /// <summary>最近一次模型调用的摘要，供临时 HUD 展示（决策可视化层另有其五项）。</summary>
        public static string Call(ModelCallInfo call)
        {
            switch (call.Outcome)
            {
                case ModelCallOutcome.Ok:
                    return $"{call.Model} · {call.LatencySeconds:0.00}s · {call.PromptTokens}+{call.CompletionTokens} tok";
                case ModelCallOutcome.Timeout:
                    return "超时，已回退本地模板";
                case ModelCallOutcome.Error:
                    return "调用失败，已回退本地模板";
                case ModelCallOutcome.Unusable:
                    return "返回不可用，已回退本地模板";
                case ModelCallOutcome.Guard:
                    return "本地白名单校验（未调用模型）";
                default:
                    return "未调用（本地模式）";
            }
        }
    }
}
