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

        static readonly char[] CropPrefixNoise =
            "满上点儿些一亩块片地田都全把给在到去遍次棵株颗粒茬植下进出起好完掉往".ToCharArray();

        static readonly char[] CropSuffixNoise = "吧了呀哦呢哈".ToCharArray();

        /// <summary>动作词开头的这些名词不是动作：「种子」「种类」「肥力」。</summary>
        static readonly string[] ActionNouns = { "种子", "种类", "肥力" };

        /// <summary>「减肥」「肥皂」里的「肥」不是施肥。</summary>
        static readonly char[] FertilizerBlockers = { '减', '皂' };

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

        /// <summary>第一 / 第二人称开头时，后面的弱标记（想、要、给…）也算请求。</summary>
        static readonly string[] DirectAddressPrefixes = { "我", "你", "您", "俺", "咱" };

        static readonly char[] VocativeTrimChars = { ' ', '，', ',', '：', ':', '！', '!', '、', '\t' };

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
            var spoken = SpokenText(text, world, npc, out var aboutSomeoneElse);
            var normalized = Normalize(spoken);

            if (aboutSomeoneElse || !IsFarmRequestNormalized(normalized))
            {
                // 追问优先于闲聊：带问句标记但不像农活请求的，按追问处理。
                // 闲聊不带台词：这句话怎么说是表达层（ExpressionPrompt）的事，本地只分类。
                return IsQuery(normalized)
                    ? IntentDraft.Read(IntentOutlet.Query, null)
                    : IntentDraft.Read(IntentOutlet.Chat, string.Empty);
            }

            var goal = DetectGoal(normalized);
            if (!goal.HasValue)
            {
                return IntentDraft.Refuse("这个活我还不会干。");
            }

            var speciesToken = DetectSpecies(normalized);
            if (goal.Value == FarmGoal.Plant && speciesToken == null)
            {
                var unknown = FindUnknownCrop(normalized, world);
                if (unknown != null)
                {
                    return IntentDraft.Refuse(ActionValidator.UnknownSpeciesReason(unknown));
                }
            }

            return IntentDraft.Command(goal.Value, DetectField(normalized, world), speciesToken, null);
        }

        /// <summary>
        /// 纯英文（可以带数字与标点）的输入只回这一句固定话术（#25）。它由本地规则直接判定：
        /// 不经过模型、不写记忆、不碰农场，也不会被模型编成一句闲聊。
        /// </summary>
        public const string ChineseOnlyReply = "我只听得懂中文。";

        /// <summary>抽屉第 4 项展示的调用摘要：这不是白名单校验，只是语言边界。</summary>
        public const string ChineseOnlyCallDetail = "非中文输入（未调用模型）";

        /// <summary>
        /// 这句话是不是「一个汉字都没有、但至少有一个字母」的非中文输入。
        /// 拉丁字母、假名、谚文等都算；中英混写（「把 A1 浇水」）仍按中文解析：
        /// 只要出现汉字就走中文规则，不能因为夹着地块 id 就被拦成听不懂。
        /// </summary>
        public static bool LooksNonChinese(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var hasLetter = false;
            foreach (var c in text)
            {
                if (IsChinese(c))
                {
                    return false;
                }

                if (char.IsLetter(c))
                {
                    hasLetter = true;
                }
            }

            return hasLetter;
        }

        static bool IsChinese(char c)
        {
            return (c >= '\u4e00' && c <= '\u9fff') || (c >= '\u3400' && c <= '\u4dbf');
        }

        internal static bool TryFindUnknownCrop(
            string text,
            FarmWorld world,
            Npc npc,
            out string unknown)
        {
            unknown = null;
            var spoken = SpokenText(text, world, npc, out var aboutSomeoneElse);
            var normalized = Normalize(spoken);

            // 只拦「像农事请求的播种要求」；追问与闲聊里出现「种」不应被当成指令。
            if (aboutSomeoneElse
                || !IsFarmRequestNormalized(normalized)
                || DetectGoal(normalized) != FarmGoal.Plant)
            {
                return false;
            }

            unknown = FindUnknownCrop(normalized, world);
            return unknown != null;
        }

        /// <summary>
        /// 这句话是不是一句农活请求。出口判定与规划超时都读它，规则只有这一处：
        /// 有白名单动作词、不是在问事或回忆、动作没被「不 / 别 / 没」否定，
        /// 并且动作前有祈使 / 请求标记（强标记可在任意位置，弱标记要在句首，
        /// 第一 / 第二人称开头的句子例外）。句首的称呼先去掉：叫的是被指挥的人
        /// （「阿禾，把地浇了」）算请求，叫的是别的 NPC（「阿土把活干完了」）算在说别人。
        /// 闲聊里的陈述句（「我昨天种下的萝卜长得真好」）因此不会被当成指令。
        /// </summary>
        internal static bool IsFarmRequest(string text, FarmWorld world, Npc addressed)
        {
            var spoken = SpokenText(text, world, addressed, out var aboutSomeoneElse);
            return !aboutSomeoneElse && IsFarmRequestNormalized(Normalize(spoken));
        }

        /// <summary>
        /// 取消工单的固定说法（#22）。它由本地规则直接判定、先于模型生效：
        /// 取消没有细节要模型补，而模型可能把「别干了」改写成一条合法农活。
        /// </summary>
        static readonly string[] CancelMarkers = { "别干", "取消", "停下" };

        /// <summary>
        /// 这句话是不是要取消他们自己的工单。问句与回忆句（「为什么取消了」「上次你让我别干了」）
        /// 不是取消；句首叫的是别的 NPC 时（「阿土别干了」）也不是。
        /// </summary>
        internal static bool IsCancelRequest(string text, FarmWorld world, Npc addressed)
        {
            var spoken = SpokenText(text, world, addressed, out var aboutSomeoneElse);
            return !aboutSomeoneElse && IsCancelRequestNormalized(Normalize(spoken));
        }

        static bool IsCancelRequestNormalized(string text)
        {
            if (text.Length == 0)
            {
                return false;
            }

            if (IsQuery(text) || IsPastQuestion(text))
            {
                return false;
            }

            foreach (var marker in CancelMarkers)
            {
                var from = 0;
                while (from <= text.Length - marker.Length)
                {
                    var index = text.IndexOf(marker, from, StringComparison.Ordinal);
                    if (index < 0)
                    {
                        break;
                    }

                    if (!IsNegatedAt(text, index))
                    {
                        return true;
                    }

                    from = index + 1;
                }
            }

            return false;
        }

        /// <summary>标记前面紧挨着否定词时，这句话是「别取消 / 不要停下」，不是取消。</summary>
        static bool IsNegatedAt(string text, int index)
        {
            if (index <= 0)
            {
                return false;
            }

            if (IsNegator(text[index - 1]))
            {
                return true;
            }

            if (index < 2)
            {
                return false;
            }

            var pair = text.Substring(index - 2, 2);
            return pair == "不要" || pair == "不用" || pair == "不必" || pair == "不许" || pair == "不能";
        }

        /// <summary>句首称呼处理后的原话；aboutSomeoneElse 表示这句话在说另一名 NPC 的事。</summary>
        static string SpokenText(string text, FarmWorld world, Npc addressed, out bool aboutSomeoneElse)
        {
            aboutSomeoneElse = false;
            var spoken = (text ?? string.Empty).TrimStart();
            var withoutVocative = StripAddressed(spoken, addressed);
            if (withoutVocative != null)
            {
                return withoutVocative;
            }

            aboutSomeoneElse = StartsWithOtherNpc(spoken, world, addressed);
            return spoken;
        }

        /// <summary>句首是被指挥 NPC 的称呼时，去掉称呼；否则返回 null。</summary>
        static string StripAddressed(string text, Npc addressed)
        {
            if (addressed == null)
            {
                return null;
            }

            foreach (var name in new[] { addressed.DisplayName, addressed.Id })
            {
                if (string.IsNullOrEmpty(name)
                    || !text.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return text.Substring(name.Length).TrimStart(VocativeTrimChars);
            }

            return null;
        }

        /// <summary>句首点名的是另一名 NPC，说明这句话在讲他的事，不是派活。</summary>
        static bool StartsWithOtherNpc(string text, FarmWorld world, Npc addressed)
        {
            if (world == null)
            {
                return false;
            }

            foreach (var npc in world.Npcs)
            {
                if (addressed != null && string.Equals(npc.Id, addressed.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (text.StartsWith(npc.DisplayName, StringComparison.Ordinal)
                    || text.StartsWith(npc.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        static bool IsFarmRequestNormalized(string normalized)
        {
            if (normalized.Length == 0)
            {
                return false;
            }

            // 问过去的句子永远是追问，哪怕带「帮我」这类强标记。
            if (IsPastQuestion(normalized))
            {
                return false;
            }

            var index = FirstActionIndex(normalized);
            if (index < 0)
            {
                return false;
            }

            var hasStrong = ContainsAny(normalized, StrongRequestMarkers);

            // 问事的话只有在强请求标记下才可能是请求（「状态不好，帮我浇点水」）。
            if (ContainsAny(normalized, InformationMarkers) && !hasStrong)
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
            if (ContainsAny(prefix, StatementMarkers) && !hasStrong)
            {
                return false;
            }

            return HasRequestMarker(prefix, hasStrong);
        }

        static int FirstActionIndex(string text)
        {
            var first = -1;
            foreach (var word in ActionWords)
            {
                var index = FindActionIndex(text, word);
                if (index >= 0 && (first < 0 || index < first))
                {
                    first = index;
                }
            }

            return first;
        }

        /// <summary>某个动作词第一次真正当动词用的位置；名词里的同形字不算。</summary>
        static int FindActionIndex(string text, string word)
        {
            var from = 0;
            while (from <= text.Length - word.Length)
            {
                var index = text.IndexOf(word, from, StringComparison.Ordinal);
                if (index < 0)
                {
                    return -1;
                }

                if (!IsActionNoun(text, index, word))
                {
                    return index;
                }

                from = index + 1;
            }

            return -1;
        }

        static bool IsActionNoun(string text, int index, string word)
        {
            foreach (var noun in ActionNouns)
            {
                if (index + noun.Length <= text.Length
                    && string.CompareOrdinal(text, index, noun, 0, noun.Length) == 0)
                {
                    return true;
                }
            }

            return word == "肥"
                && index > 0
                && Array.IndexOf(FertilizerBlockers, text[index - 1]) >= 0;
        }

        /// <summary>
        /// 前缀里有没有可用的请求标记。强标记出现在哪都算；弱标记只有在句首
        /// （「把 / 给 / 去 / 再…」）或第一、第二人称开头（「我想…」「你要…」）时才算，
        /// 否则「阿土把活干完了」这种说别人的句子会被误当成请求。
        /// </summary>
        static bool HasRequestMarker(string prefix, bool hasStrong)
        {
            if (hasStrong)
            {
                return true;
            }

            if (StartsWithAny(prefix, DirectAddressPrefixes))
            {
                return HasAnyActiveMarker(prefix);
            }

            foreach (var marker in RequestMarkers)
            {
                if (prefix.StartsWith(marker, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        static bool HasAnyActiveMarker(string prefix)
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

        static bool StartsWithAny(string text, IReadOnlyList<string> markers)
        {
            foreach (var marker in markers)
            {
                if (text.StartsWith(marker, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        static bool IsNegator(char value)
        {
            return value == '不' || value == '别' || value == '没' || value == '甭';
        }

        /// <summary>
        /// 问过去的句子（「上次你浇的是哪块」「你帮我浇水了吗」）是追问，不是新请求。
        /// 「了吗 / 了没 / 过吗 / 没有」把一句听起来像请求的话变成问结果，不能当成要活干。
        /// </summary>
        static bool IsPastQuestion(string text)
        {
            if (ContainsAny(text, PastQuestionMarkers))
            {
                return true;
            }

            if (text.Contains("了吗") || text.Contains("了没") || text.Contains("过吗") || text.Contains("没有"))
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
        /// 已知品种、泛称、地块指称与语气词都不算，剩下的就是 NPC 要明确拒绝的对象。
        /// 「种子」里的同形字不算动作，地块 id（「种在 A1」）也不是品种。
        /// </summary>
        static string FindUnknownCrop(string text, FarmWorld world)
        {
            var index = FindActionIndex(text, "种");
            if (index < 0)
            {
                index = FindActionIndex(text, "栽");
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

            if (world != null)
            {
                foreach (var field in world.Fields)
                {
                    if (rest.StartsWith(field.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }
                }
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
        /// 追问的事实串。按问题分成三类：上次那件事、为什么没去干活、现在在干什么；
        /// 每条都落到具体地块 / 工步 / 体力，并附上检索到的记忆作为依据。
        /// 离线时它直接就是回答；在线时它进表达层提示词，模型给不出合格台词时也回退到它。
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
                builder.Append($"（工单第 {order.CompletedCount + 1}/{order.Steps.Count} 步），体力 {npc.Stamina:P0}");
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

        /// <summary>
        /// 问往事时回答哪一条：优先玩家当时的那句话，没有再取权重最高的那条。
        /// 追问事实串与措辞校验都读这里，两处不能各挑各的。
        /// </summary>
        internal static MemoryHit BestPlayerMemory(IReadOnlyList<MemoryHit> memories)
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
    }
}
