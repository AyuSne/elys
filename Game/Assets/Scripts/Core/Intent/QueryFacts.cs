using System;
using System.Collections.Generic;

namespace ElysFarm.Core
{
    /// <summary>
    /// 一次追问的事实：地块、工步、体力与命中的记忆全部由本地算好（<see cref="LocalIntentParser.QueryReply"/>），
    /// 模型只负责把它们换成另一种说法。
    /// 事实串同时是回退台词：模型不可用、超时或改掉事实时，直接用这一串回答玩家。
    /// </summary>
    internal sealed class QueryFacts
    {
        readonly IReadOnlyList<string> fieldIds;
        readonly MemoryHit? citedMemory;

        QueryFacts(
            string reply,
            IReadOnlyList<MemoryHit> memories,
            IReadOnlyList<string> fieldIds,
            MemoryHit? citedMemory)
        {
            Reply = reply;
            Memories = memories;
            this.fieldIds = fieldIds;
            this.citedMemory = citedMemory;
        }

        /// <summary>本地生成的事实串；也是模型失败时的回退台词。</summary>
        public string Reply { get; }

        /// <summary>这次回答实际命中的记忆，与决策可视化第 1 项同一次检索。</summary>
        public IReadOnlyList<MemoryHit> Memories { get; }

        /// <summary>
        /// 模型的措辞能不能用：换说法可以，改事实不行。
        /// 事实串里的地块、工步与体力必须一样不少地还在，不能编出事实里没有的地块或游戏日；
        /// 问往事时还必须仍然引用记忆里的地块或游戏日。不合格就作废，改用事实串回答。
        /// </summary>
        public bool Accepts(string reply)
        {
            var text = (reply ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return false;
            }

            return KeepsAnchors(text)
                && NamesOnlyKnownFields(text)
                && NamesOnlyKnownDays(text)
                && (citedMemory == null || CarriesCitedMemory(text));
        }

        public static QueryFacts Build(
            FarmWorld world,
            Npc npc,
            string question,
            IReadOnlyList<MemoryHit> memories)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (npc == null)
            {
                throw new ArgumentNullException(nameof(npc));
            }

            var hits = memories ?? Array.Empty<MemoryHit>();
            var ids = new List<string>(world.Fields.Count);
            foreach (var field in world.Fields)
            {
                ids.Add(field.Id);
            }

            // 问往事时事实串引用的是玩家当时那句话（与 LocalIntentParser.QueryReply 同一条规则），
            // 模型的措辞也必须还引用这一条，不能换成另一条记忆里的地块或游戏日。
            var cited = LocalIntentParser.IsMemoryQuestion(question ?? string.Empty) && hits.Count > 0
                ? LocalIntentParser.BestPlayerMemory(hits)
                : (MemoryHit?)null;

            return new QueryFacts(
                LocalIntentParser.QueryReply(world, npc, question, hits),
                hits,
                ids,
                cited);
        }

        /// <summary>事实串里出现过的地块、工步与体力——换说法时这些词必须还在。</summary>
        bool KeepsAnchors(string reply)
        {
            foreach (var fieldId in fieldIds)
            {
                if (Reply.Contains(fieldId) && !reply.Contains(fieldId))
                {
                    return false;
                }
            }

            foreach (WorkStep step in Enum.GetValues(typeof(WorkStep)))
            {
                var label = WorkStepLabel.Of(step);
                if (Reply.Contains(label) && !reply.Contains(label))
                {
                    return false;
                }
            }

            return !Reply.Contains("体力") || reply.Contains("体力");
        }

        /// <summary>
        /// 台词里提到的地块必须来自事实或命中的记忆：把 A1 说成 A2 不算换说法，
        /// 编出「A9」这种不存在的 id 也不算。
        /// </summary>
        bool NamesOnlyKnownFields(string reply)
        {
            foreach (var fieldId in fieldIds)
            {
                if (NamesField(reply, fieldId)
                    && !NamesField(Reply, fieldId)
                    && !MemoriesMention(fieldId))
                {
                    return false;
                }
            }

            return !MentionsUnknownFieldId(reply);
        }

        /// <summary>「字母 + 数字」形状的 id 必须在这个世界的地块表里，否则就是编出来的地块。</summary>
        bool MentionsUnknownFieldId(string text)
        {
            var index = 0;
            while (index < text.Length)
            {
                if (!IsAsciiLetter(text[index]))
                {
                    index++;
                    continue;
                }

                var start = index;
                while (index < text.Length && IsAsciiLetter(text[index]))
                {
                    index++;
                }

                var digitsAt = index;
                while (index < text.Length && char.IsDigit(text[index]))
                {
                    index++;
                }

                if (index > digitsAt && !IsKnownFieldId(text.Substring(start, index - start)))
                {
                    return true;
                }
            }

            return false;
        }

        bool IsKnownFieldId(string token)
        {
            foreach (var fieldId in fieldIds)
            {
                if (string.Equals(fieldId, token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>台词里的「第 N 天」必须是事实或命中的记忆里有的那一天。</summary>
        bool NamesOnlyKnownDays(string reply)
        {
            var allowed = new HashSet<int>(DaysIn(Reply));
            foreach (var memory in Memories)
            {
                allowed.Add(memory.Day);
            }

            foreach (var day in DaysIn(reply))
            {
                if (!allowed.Contains(day))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>问往事时，台词还要落到被引用那条记忆的地块或游戏日。</summary>
        bool CarriesCitedMemory(string reply)
        {
            var memory = citedMemory.Value;
            if (DaysIn(reply).Contains(memory.Day))
            {
                return true;
            }

            var text = (memory.Text ?? string.Empty).Trim();
            if (text.Length > 0 && reply.Contains(text))
            {
                return true;
            }

            foreach (var fieldId in fieldIds)
            {
                if (memory.Text.Contains(fieldId) && NamesField(reply, fieldId))
                {
                    return true;
                }
            }

            return false;
        }

        bool MemoriesMention(string fieldId)
        {
            foreach (var memory in Memories)
            {
                if ((memory.Text ?? string.Empty).Contains(fieldId))
                {
                    return true;
                }
            }

            return false;
        }

        static bool IsAsciiLetter(char value)
        {
            return (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');
        }

        /// <summary>「A1」这种地块 id 不能误配到「A10」里，所以还要求后面不是数字。</summary>
        static bool NamesField(string text, string fieldId)
        {
            var from = 0;
            while (from <= text.Length - fieldId.Length)
            {
                var index = text.IndexOf(fieldId, from, StringComparison.Ordinal);
                if (index < 0)
                {
                    return false;
                }

                var after = index + fieldId.Length;
                if (after >= text.Length || !char.IsDigit(text[after]))
                {
                    return true;
                }

                from = after;
            }

            return false;
        }

        /// <summary>台词里所有「第 N 天」的 N；「工单第 1/1 步」不算。</summary>
        static List<int> DaysIn(string text)
        {
            var days = new List<int>();
            var from = 0;
            while (from < text.Length)
            {
                var start = text.IndexOf("第", from, StringComparison.Ordinal);
                if (start < 0)
                {
                    break;
                }

                var index = start + 1;
                while (index < text.Length && text[index] == ' ')
                {
                    index++;
                }

                var digits = index;
                while (index < text.Length && char.IsDigit(text[index]))
                {
                    index++;
                }

                if (index > digits)
                {
                    var afterDigits = index;
                    while (afterDigits < text.Length && text[afterDigits] == ' ')
                    {
                        afterDigits++;
                    }

                    if (afterDigits < text.Length
                        && text[afterDigits] == '天'
                        && int.TryParse(text.Substring(digits, index - digits), out var day))
                    {
                        days.Add(day);
                    }
                }

                from = index > digits ? index : start + 1;
            }

            return days;
        }
    }
}
