using System;
using System.Collections.Generic;

namespace ElysFarm.Core
{
    /// <summary>
    /// 不依赖模型的本地意图解析。它同时承担两个职责：
    /// 断网/超时时的降级路径，以及给模型结果兜底的确定性规则。
    /// 只识别白名单内的动作与品种；识别不了的品种会被明确拒绝，而不是猜一个。
    /// </summary>
    internal static class LocalIntentParser
    {
        static readonly string[] QueryMarkers =
        {
            "干什么", "干嘛", "做什么", "忙什么", "在种什么", "种什么", "种啥",
            "种的什么", "种的是什么", "种的啥", "种了啥", "在种啥",
            "为什么", "怎么不去", "什么情况", "状态", "累不累", "渴不渴"
        };

        static readonly char[] CropPrefixNoise = "满上点儿些一亩块片地田都全把给在到去遍次棵株颗粒茬".ToCharArray();
        static readonly char[] CropSuffixNoise = "吧了呀哦呢哈".ToCharArray();

        public static IntentDraft Parse(string text, FarmWorld world, Npc npc)
        {
            var normalized = Normalize(text);

            if (IsQuery(normalized))
            {
                return IntentDraft.Read(IntentOutlet.Query, QueryReply(world, npc));
            }

            if (!HasFarmKeyword(normalized))
            {
                return IntentDraft.Read(IntentOutlet.Chat, ChatReply(normalized));
            }

            var goal = DetectGoal(normalized);
            if (!goal.HasValue)
            {
                return IntentDraft.Refuse("这个活我还不会干。");
            }

            var speciesToken = DetectSpecies(normalized);
            if (goal.Value == FarmGoal.Plant && speciesToken == null)
            {
                var unknown = FindUnknownCrop(normalized);
                if (unknown != null)
                {
                    return IntentDraft.Refuse($"「{unknown}」可种不出来，地里只能种萝卜和小麦。");
                }
            }

            return IntentDraft.Command(goal.Value, DetectField(normalized, world), speciesToken, null);
        }

        internal static bool TryFindUnknownCrop(string text, out string unknown)
        {
            unknown = null;
            var normalized = Normalize(text);

            // 只拦「像农事指令的播种要求」；追问与闲聊里出现「种」不应被当成指令。
            if (IsQuery(normalized) || !HasFarmKeyword(normalized) || DetectGoal(normalized) != FarmGoal.Plant)
            {
                return false;
            }

            unknown = FindUnknownCrop(normalized);
            return unknown != null;
        }

        static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var buffer = new System.Text.StringBuilder(text.Length);
            foreach (var ch in text.Trim())
            {
                if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch))
                {
                    continue;
                }

                buffer.Append(ch);
            }

            return buffer.ToString();
        }

        static bool IsQuery(string text)
        {
            foreach (var marker in QueryMarkers)
            {
                if (text.Contains(marker))
                {
                    return true;
                }
            }

            return false;
        }

        static bool HasFarmKeyword(string text)
        {
            return text.Contains("种")
                || text.Contains("播")
                || text.Contains("栽")
                || text.Contains("浇")
                || text.Contains("施肥")
                || text.Contains("肥")
                || text.Contains("除草")
                || text.Contains("拔草")
                || text.Contains("收");
        }

        static FarmGoal? DetectGoal(string text)
        {
            if (text.Contains("收"))
            {
                return FarmGoal.Harvest;
            }

            if (text.Contains("肥"))
            {
                return FarmGoal.Fertilize;
            }

            if (text.Contains("草"))
            {
                return FarmGoal.Weed;
            }

            if (text.Contains("浇") || text.Contains("洒水") || text.Contains("补水"))
            {
                return FarmGoal.Water;
            }

            if (text.Contains("种") || text.Contains("播") || text.Contains("栽"))
            {
                return FarmGoal.Plant;
            }

            return null;
        }

        static string DetectSpecies(string text)
        {
            foreach (var species in CropCatalog.All)
            {
                if (text.Contains(species.DisplayName)
                    || text.IndexOf(species.Id, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return species.Id;
                }
            }

            if (text.Contains("麦子"))
            {
                return CropCatalog.WheatId;
            }

            return null;
        }

        static string DetectField(string text, FarmWorld world)
        {
            foreach (var field in world.Fields)
            {
                if (text.IndexOf(field.Id, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return field.Id;
                }
            }

            if (text.Contains("东"))
            {
                return "east";
            }

            if (text.Contains("西"))
            {
                return "west";
            }

            if (text.Contains("北"))
            {
                return "north";
            }

            if (text.Contains("南"))
            {
                return "south";
            }

            if (text.Contains("最近") || text.Contains("附近") || text.Contains("旁边"))
            {
                return "near";
            }

            return null;
        }

        /// <summary>
        /// 「去种月亮」这类要求里，品种不在白名单。找出「种」后面的名词，
        /// 已知品种、泛称与语气词都不算，剩下的就是 NPC 要明确拒绝的对象。
        /// </summary>
        static string FindUnknownCrop(string text)
        {
            var index = text.IndexOf("种", StringComparison.Ordinal);
            if (index < 0)
            {
                index = text.IndexOf("栽", StringComparison.Ordinal);
            }

            if (index < 0 || index + 1 >= text.Length)
            {
                return null;
            }

            var rest = text.Substring(index + 1).TrimStart(CropPrefixNoise).TrimEnd(CropSuffixNoise);
            if (rest.Length == 0)
            {
                return null;
            }

            foreach (var species in CropCatalog.All)
            {
                if (rest.StartsWith(species.DisplayName, StringComparison.Ordinal)
                    || rest.StartsWith(species.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            if (rest.StartsWith("什么") || rest.StartsWith("啥") || rest.StartsWith("地") || rest.StartsWith("田"))
            {
                return null;
            }

            return rest.Length > 6 ? rest.Substring(0, 6) : rest;
        }

        internal static string QueryReply(FarmWorld world, Npc npc)
        {
            var order = world.OrderOf(npc.Id);
            if (order == null)
            {
                return "我在等下一件该干的活。";
            }

            return order.NextStep.HasValue
                ? $"我正要去{order.FieldId}{StepName(order.NextStep.Value)}。"
                : "我手里的活刚干完。";
        }

        internal static string ChatReply(string text)
        {
            var replies = new[] { "嗯，我记着呢。", "天不早了，地里的活我盯着。", "你说了算，我听着。" };
            return replies[text.Length % replies.Length];
        }

        static string StepName(WorkStep step)
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
    }
}
