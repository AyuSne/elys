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

        static readonly string[] QueryMarkers = BuildQueryMarkers();

        static string[] BuildQueryMarkers()
        {
            var markers = new List<string>
            {
                "干什么", "干嘛", "做什么", "忙什么", "在种什么", "种什么", "种啥",
                "种的什么", "种的是什么", "种的啥", "种了啥", "在种啥",
                "为什么", "怎么不去", "什么情况", "状态", "累不累", "渴不渴",
                "吗", "怎么样", "如何", "还记得", "记得吗"
            };
            markers.AddRange(SharedMemoryMarkers);
            return markers.ToArray();
        }

        static readonly char[] CropPrefixNoise = "满上点儿些一亩块片地田都全把给在到去遍次棵株颗粒茬植".ToCharArray();
        static readonly char[] CropSuffixNoise = "吧了呀哦呢哈".ToCharArray();

        public static IntentDraft Parse(string text, FarmWorld world, Npc npc)
        {
            var normalized = Normalize(text);

            if (IsQuery(normalized))
            {
                // 追问的台词要带当前状态与检索到的记忆，由路由器在拿到记忆后生成。
                return IntentDraft.Read(IntentOutlet.Query, null);
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
                    return IntentDraft.Refuse(ActionValidator.UnknownSpeciesReason(unknown));
                }
            }

            return IntentDraft.Command(goal.Value, DetectField(normalized, world), speciesToken, null);
        }

        internal static bool TryFindUnknownCrop(string text, out string unknown)
        {
            unknown = null;
            var normalized = Normalize(text);

            // 只拦「像农事指令的播种要求」；追问与闲聊里出现「种」不应被当成指令。
            if (!LooksLikeFarmCommand(normalized) || DetectGoal(normalized) != FarmGoal.Plant)
            {
                return false;
            }

            unknown = FindUnknownCrop(normalized);
            return unknown != null;
        }

        /// <summary>文本是否像一句农活要求；用于选择规划级还是对话级的模型超时。</summary>
        internal static bool LooksLikeFarmCommand(string text)
        {
            var normalized = Normalize(text);
            return !IsQuery(normalized) && HasFarmKeyword(normalized);
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
