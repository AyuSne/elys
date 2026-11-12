using System;
using System.Collections.Generic;
using System.Text;

namespace ElysFarm.Core
{
    /// <summary>
    /// 不依赖模型的本地意图解析。它同时承担两个职责：
    /// 断网/超时时的降级路径，以及给模型结果兜底的确定性规则。
    /// 只识别白名单内的动作与品种；识别不了的品种会被明确拒绝，而不是猜一个。
    /// </summary>
    internal static class LocalIntentParser
    {
        /// <summary>追问与「问过去」共用的标记；只在这里列一次，出口与回答分类都读它。</summary>
        static readonly string[] SharedMemoryMarkers = { "哪块", "哪边", "是不是", "你让我" };

        /// <summary>问「是什么 / 为什么 / 怎么样」的标记：带这些字的句子是追问，不是农活请求。</summary>
        static readonly string[] InformationMarkers =
        {
            "干什么", "干嘛", "做什么", "忙什么", "在种什么", "种什么", "种啥",
            "种的什么", "种的是什么", "种的啥", "种了啥", "在种啥",
            "为什么", "怎么不去", "什么情况", "状态", "累不累", "渴不渴",
            "怎么样", "如何", "记得吗"
        };

        static readonly string[] QueryMarkers = BuildQueryMarkers();

        static string[] BuildQueryMarkers()
        {
            // 问事的标记加上「吗 / 还记得」与回忆型标记，全部在这里汇总，避免两处各维护一份。
            var markers = new List<string>(InformationMarkers) { "吗", "还记得" };
            markers.AddRange(SharedMemoryMarkers);
            return markers.ToArray();
        }

        static readonly char[] CropPrefixNoise = "满上点儿些一亩块片地田都全把给在到去遍次棵株颗粒茬植".ToCharArray();
        static readonly char[] CropSuffixNoise = "吧了呀哦呢哈".ToCharArray();

        /// <summary>白名单动作词；判定「请求」时只认这些，不认「草地」「减肥」这类泛用字。</summary>
        static readonly string[] ActionWords =
        {
            "种", "播", "栽", "浇", "施肥", "肥", "除草", "拔草", "收"
        };

        /// <summary>动作词前的祈使 / 请求标记；出现在这些词后面的动作用户是在提要求。</summary>
        static readonly string[] RequestMarkers =
        {
            "帮我", "帮忙", "替我", "给我", "麻烦", "请", "能不能", "可不可以", "要不要",
            "把", "给", "去", "来", "赶紧", "快", "记得", "别忘了", "该", "再", "想", "要",
            "随便", "马上", "立刻"
        };

        /// <summary>即使整句在讲往事，也足以说明「这是请求」的强标记。</summary>
        static readonly string[] StrongRequestMarkers =
        {
            "帮我", "帮忙", "替我", "给我", "麻烦", "请", "能不能", "可不可以", "要不要"
        };

        /// <summary>讲往事的标记：没有强请求标记时，这类句子按陈述处理。</summary>
        static readonly string[] StatementMarkers =
        {
            "听说", "据说", "昨天", "前天", "上次", "之前", "刚才", "梦见", "梦到", "已经",
            "再也", "本来", "未来", "后来", "原来", "从来"
        };

        /// <summary>回忆型追问的标记；带它们的是问过去，不是提要求。</summary>
        static readonly string[] PastQuestionMarkers = { "哪块", "哪边", "上次", "之前", "记得", "是不是", "你让我" };

        public static IntentDraft Parse(string text, FarmWorld world, Npc npc)
        {
            var normalized = Normalize(text);

            if (!IsFarmRequestNormalized(normalized))
            {
                // 追问优先于闲聊：带问句标记但不像农活请求的，按追问处理。
                return IsQuery(normalized)
                    ? IntentDraft.Read(IntentOutlet.Query, null)
                    : IntentDraft.Read(IntentOutlet.Chat, ChatReply(normalized));
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
                    return IntentDraft.Refuse(ActionValidator.UnknownSpeciesReason(unknown));
                }
            }

            return IntentDraft.Command(goal.Value, DetectField(normalized, world), speciesToken, null);
        }

        internal static bool TryFindUnknownCrop(string text, out string unknown)
        {
            unknown = null;
            var normalized = Normalize(text);

            // 只拦「像农事请求的播种要求」；追问与闲聊里出现「种」不应被当成指令。
            if (!IsFarmRequestNormalized(normalized) || DetectGoal(normalized) != FarmGoal.Plant)
            {
                return false;
            }

            unknown = FindUnknownCrop(normalized);
            return unknown != null;
        }

        /// <summary>
        /// 这句话是不是一句农活请求。出口判定与规划超时都读它，规则只有这一处：
        /// 有白名单动作词、不是在问事或回忆、动作词在句首或前面有祈使 / 请求标记、
        /// 且动作没被「不 / 别 / 没」否定。
        /// 闲聊里的陈述句（「我昨天种下的萝卜长得真好」）因此不会被当成指令。
        /// </summary>
        internal static bool IsFarmRequest(string text)
        {
            return IsFarmRequestNormalized(Normalize(text));
        }

        static bool IsFarmRequestNormalized(string normalized)
        {
            if (normalized.Length == 0
                || ContainsAny(normalized, InformationMarkers)
                || IsPastQuestion(normalized))
            {
                return false;
            }

            var index = FirstActionIndex(normalized);
            if (index < 0)
            {
                return false;
            }

            // 「种地」「浇水」这种动词开头的句子就是祈使句。
            if (index == 0)
            {
                return true;
            }

            // 「别浇」「不种」是拒绝或陈述，不是请求。
            if (IsNegator(normalized[index - 1]))
            {
                return false;
            }

            var prefix = normalized.Substring(0, index);
            if (ContainsAny(prefix, StatementMarkers) && !ContainsAny(prefix, StrongRequestMarkers))
            {
                return false;
            }

            return HasRequestMarker(prefix);
        }

        /// <summary>「种子」「种类」里的「种」是名词，不是动作；找动作词时跳过它们。</summary>
        static readonly string[] ActionNouns = { "种子", "种类" };

        static int FirstActionIndex(string text)
        {
            var first = -1;
            foreach (var word in ActionWords)
            {
                var from = 0;
                while (from <= text.Length - word.Length)
                {
                    var index = text.IndexOf(word, from, StringComparison.Ordinal);
                    if (index < 0)
                    {
                        break;
                    }

                    if (!IsNounAt(text, index))
                    {
                        if (first < 0 || index < first)
                        {
                            first = index;
                        }

                        break;
                    }

                    from = index + 1;
                }
            }

            return first;
        }

        static bool IsNounAt(string text, int index)
        {
            foreach (var noun in ActionNouns)
            {
                if (index + noun.Length <= text.Length
                    && string.CompareOrdinal(text, index, noun, 0, noun.Length) == 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>前缀里有没有「没被否定」的请求标记；「不想」「不要」不算。</summary>
        static bool HasRequestMarker(string prefix)
        {
            foreach (var marker in RequestMarkers)
            {
                var from = 0;
                while (from <= prefix.Length - marker.Length)
                {
                    var index = prefix.IndexOf(marker, from, StringComparison.Ordinal);
                    if (index < 0)
                    {
                        break;
                    }

                    if (index == 0 || !IsNegator(prefix[index - 1]))
                    {
                        return true;
                    }

                    from = index + marker.Length;
                }
            }

            return false;
        }

        static bool IsNegator(char value)
        {
            return value == '不' || value == '别' || value == '没' || value == '甭';
        }

        /// <summary>问过去的句子（「上次你浇的是哪块」「还记得浇水吗」）是追问，不是新请求。</summary>
        static bool IsPastQuestion(string text)
        {
            if (ContainsAny(text, PastQuestionMarkers))
            {
                return true;
            }

            var mentionsPastDay = text.Contains("昨天") || text.Contains("前天");
            return mentionsPastDay && (text.Contains('吗') || text.Contains('哪'));
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
            return ContainsAny(text, QueryMarkers);
        }

        static bool ContainsAny(string text, IReadOnlyList<string> markers)
        {
            foreach (var marker in markers)
            {
                if (text.Contains(marker))
                {
                    return true;
                }
            }

            return false;
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

            var rest = text.Substring(index + 1);

            // 「种植萝卜」「种上 10 棵萝卜」这类合法说法里，已知品种可能不在开头。
            if (ContainsKnownSpecies(rest))
            {
                return null;
            }

            while (rest.Length > 0
                && (char.IsDigit(rest[0]) || Array.IndexOf(CropPrefixNoise, rest[0]) >= 0))
            {
                rest = rest.Substring(1);
            }

            rest = rest.TrimEnd(CropSuffixNoise);
            if (rest.Length == 0)
            {
                return null;
            }

            if (rest.StartsWith("什么") || rest.StartsWith("啥") || rest.StartsWith("地") || rest.StartsWith("田"))
            {
                return null;
            }

            return rest.Length > 6 ? rest.Substring(0, 6) : rest;
        }

        static bool ContainsKnownSpecies(string text)
        {
            foreach (var species in CropCatalog.All)
            {
                if (text.Contains(species.DisplayName)
                    || text.IndexOf(species.Id, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return text.Contains("麦子");
        }

        /// <summary>
        /// 离线追问的回答。按问题分成三类：上次那件事、为什么没去干活、现在在干什么；
        /// 每条回答都落到具体地块 / 工步 / 体力，并附上检索到的记忆作为依据。
        /// </summary>
        internal static string QueryReply(
            FarmWorld world,
            Npc npc,
            string question,
            IReadOnlyList<MemoryHit> memories)
        {
            var normalized = Normalize(question ?? string.Empty);
            var builder = new StringBuilder();

            if (IsMemoryQuestion(normalized) && memories != null && memories.Count > 0)
            {
                AppendMemoryAnswer(builder, world, npc, BestPlayerMemory(memories));
                return builder.ToString();
            }

            if (IsWhyNotWorking(normalized))
            {
                AppendWhyNotWorking(builder, world, npc);
            }
            else if (IsStatusQuestion(normalized))
            {
                AppendStatus(builder, npc);
            }
            else
            {
                AppendWhatDoing(builder, world, npc);
            }

            AppendMemoryBasis(builder, memories);
            return builder.ToString();
        }

        internal static bool IsMemoryQuestion(string text)
        {
            return ContainsAny(text, SharedMemoryMarkers)
                || text.Contains("上次")
                || text.Contains("之前")
                || text.Contains("昨天")
                || text.Contains("前天")
                || text.Contains("记得");
        }

        static bool IsWhyNotWorking(string text)
        {
            return text.Contains("怎么不去")
                || text.Contains("为什么不")
                || text.Contains("为什么没")
                || text.Contains("怎么没")
                || text.Contains("不干活")
                || text.Contains("偷懒")
                || text.Contains("闲着");
        }

        static bool IsStatusQuestion(string text)
        {
            return text.Contains("累不累")
                || text.Contains("渴不渴")
                || text.Contains("状态")
                || text.Contains("体力")
                || text.Contains("心情")
                || text.Contains("饿");
        }

        static bool TryValidateOrderStep(
            FarmWorld world,
            Npc npc,
            WorkOrder order,
            out string reason)
        {
            return ActionValidator.Validate(
                world,
                npc,
                world.GetField(order.FieldId),
                order.NextStep.Value,
                order.SpeciesId,
                out reason);
        }

        static void AppendWhatDoing(StringBuilder builder, FarmWorld world, Npc npc)
        {
            var order = world.WorkOrderOf(npc.Id);
            if (order != null && order.NextStep.HasValue)
            {
                var step = order.NextStep.Value;
                builder.Append($"我正要去 {order.FieldId} {WorkStepLabel.Of(step)}");
                builder.Append($"（工单第 {order.CompletedCount + 1}/{order.Steps.Count} 步）");
                if (!TryValidateOrderStep(world, npc, order, out var reason))
                {
                    builder.Append($"，不过{TrimEnd(reason)}");
                }

                builder.Append('。');
                return;
            }

            if (npc.IsResting)
            {
                builder.Append($"我在歇着：体力只剩 {npc.Stamina:P0}，还要 {npc.RestSecondsRemaining:0} 秒。");
                return;
            }

            var intent = world.IntentOf(npc.Id);
            if (string.Equals(intent, FarmWorld.ThinkingReason, StringComparison.Ordinal))
            {
                builder.Append($"我刚干完一趟，还在琢磨下一步（还要 {npc.DecisionSecondsRemaining:0} 秒）。");
                return;
            }

            const string autonomousPrefix = "自主：";
            if (intent.StartsWith(autonomousPrefix, StringComparison.Ordinal))
            {
                var plan = intent.Substring(autonomousPrefix.Length).Replace(" · ", " ");
                builder.Append($"我正打算去 {plan}，体力 {npc.Stamina:P0}。");
                return;
            }

            builder.Append($"我现在{intent}，体力 {npc.Stamina:P0}。");
        }

        static void AppendWhyNotWorking(StringBuilder builder, FarmWorld world, Npc npc)
        {
            if (npc.IsResting)
            {
                builder.Append(
                    $"我累了在歇着：体力只剩 {npc.Stamina:P0}，还要 {npc.RestSecondsRemaining:0} 秒才缓过来。");
                return;
            }

            var order = world.WorkOrderOf(npc.Id);
            if (order != null && order.NextStep.HasValue)
            {
                var step = order.NextStep.Value;
                builder.Append($"我手里有你派的活：去 {order.FieldId} {WorkStepLabel.Of(step)}。");
                if (!TryValidateOrderStep(world, npc, order, out var reason))
                {
                    builder.Append($"现在{TrimEnd(reason)}，得等一等。");
                }

                return;
            }

            if (!npc.CanDecide)
            {
                builder.Append($"我刚干完一趟，还得 {npc.DecisionSecondsRemaining:0} 秒才拿主意。");
                return;
            }

            var next = world.NextStep(npc.Id);
            if (!next.HasValue)
            {
                builder.Append("地里现在没有我能干的活了。");
                return;
            }

            builder.Append($"我正准备去 {next.Value.FieldId} {WorkStepLabel.Of(next.Value.Step)}，只是还没动身。");
        }

        static void AppendStatus(StringBuilder builder, Npc npc)
        {
            builder.Append(
                $"体力 {npc.Stamina:P0}，心情 {npc.Mood:P0}，饥饿 {npc.Hunger:P0}。");
        }

        static MemoryHit BestPlayerMemory(IReadOnlyList<MemoryHit> memories)
        {
            foreach (var memory in memories)
            {
                if (memory.Text.StartsWith("玩家", StringComparison.Ordinal))
                {
                    return memory;
                }
            }

            return memories[0];
        }

        static void AppendMemoryAnswer(StringBuilder builder, FarmWorld world, Npc npc, MemoryHit memory)
        {
            var text = TrimEnd(memory.Text);
            builder.Append(text.StartsWith("玩家", StringComparison.Ordinal)
                ? $"对，第 {memory.Day} 天{ToSecondPerson(text)}。"
                : $"我记得，第 {memory.Day} 天{text}。");
            builder.Append($"现在{world.IntentOf(npc.Id)}，体力 {npc.Stamina:P0}。");
        }

        static void AppendMemoryBasis(StringBuilder builder, IReadOnlyList<MemoryHit> memories)
        {
            if (memories == null || memories.Count == 0)
            {
                return;
            }

            var top = memories[0];
            builder.Append($"我记得第 {top.Day} 天，{TrimEnd(top.Text)}。");
        }

        static string ToSecondPerson(string text)
        {
            return text.StartsWith("玩家", StringComparison.Ordinal) ? "你" + text.Substring(2) : text;
        }

        static string TrimEnd(string text)
        {
            return (text ?? string.Empty).Trim().TrimEnd('。', '！', '？', '，', '、');
        }

        internal static string ChatReply(string text)
        {
            var replies = new[] { "嗯，我记着呢。", "天不早了，地里的活我盯着。", "你说了算，我听着。" };
            return replies[text.Length % replies.Length];
        }
    }
}
