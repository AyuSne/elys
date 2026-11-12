using System;
using System.Collections.Generic;

namespace ElysFarm.Core
{
    /// <summary>
    /// 传闻台账（见 GLOSSARY「传闻」「转述」）：每名 NPC 知道哪些关于第三人的事。
    /// 玩家在闲聊里提到别人时记一条；NPC 之间转述时说法原样带过去，来源与手数更新。
    /// 它只记录「谁知道什么」，不改变地块、时钟或背包；模块内部，不单开测试接缝，
    /// 所有行为通过 <see cref="FarmWorld.TryShareNews"/> 暴露。
    /// </summary>
    internal sealed class NewsBook
    {
        /// <summary>importance 的合法范围，与记忆一致。</summary>
        internal const int MaxImportance = 10;

        /// <summary>说法开头的口头语，写成记忆时去掉，免得转述后越叠越长。</summary>
        static readonly string[] ClaimNoise =
        {
            "我听说", "听说", "据说", "告诉你", "知道吗", "对了", "顺便说一句", "顺便", "其实", "好像", "就是"
        };

        static readonly char[] ClaimTrimChars =
        {
            ' ', '，', ',', '。', '.', '！', '!', '？', '?', '、', '：', ':', '；', ';',
            '“', '”', '"', '「', '」', '…', '\t'
        };

        const string ParticleOnly = "了吧啊呀呢哈嘛哦的";

        readonly Dictionary<string, List<Item>> byNpc = new Dictionary<string, List<Item>>();

        /// <summary>
        /// 从玩家的一句闲聊里认出被谈论的 NPC，并把「他说了他什么」记下来。
        /// 只认第三名 NPC（不讲听者自己的事）；没有说法或只叫了个名字时不算传闻。
        /// </summary>
        public bool LearnFromPlayer(string npcId, string text, int importance, IReadOnlyList<Npc> npcs)
        {
            if (npcs == null || npcs.Count == 0 || string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            Npc subject = null;
            var earliest = -1;
            foreach (var candidate in npcs)
            {
                if (string.Equals(candidate.Id, npcId, StringComparison.Ordinal))
                {
                    continue;
                }

                var at = MentionIndex(text, candidate);
                if (at >= 0 && (earliest < 0 || at < earliest))
                {
                    subject = candidate;
                    earliest = at;
                }
            }

            if (subject == null)
            {
                return false;
            }

            var claim = ClaimOf(text, subject);
            if (claim.Length == 0)
            {
                return false;
            }

            return Learn(npcId, new Item(subject.Id, subject.DisplayName, claim, importance, 0));
        }

        /// <summary>记下一条某人知道的传闻；同一件事（主体 + 说法）只记一次。返回是否新增。</summary>
        public bool Learn(string npcId, Item item)
        {
            if (string.IsNullOrWhiteSpace(npcId)
                || string.IsNullOrWhiteSpace(item.SubjectId)
                || string.IsNullOrWhiteSpace(item.Claim))
            {
                return false;
            }

            if (!byNpc.TryGetValue(npcId, out var items))
            {
                items = new List<Item>();
                byNpc.Add(npcId, items);
            }

            if (IndexOf(items, item.SubjectId, item.Claim) >= 0)
            {
                return false;
            }

            items.Add(item);
            return true;
        }

        /// <summary>挑一条讲给这名听众：听众已经知道的不再讲，重要性高的先讲。</summary>
        public bool TryPickForRetelling(string speakerId, string listenerId, out Item item)
        {
            item = default;
            if (!byNpc.TryGetValue(speakerId, out var items) || items.Count == 0)
            {
                return false;
            }

            var found = false;
            foreach (var candidate in items)
            {
                if (Knows(listenerId, candidate.SubjectId, candidate.Claim))
                {
                    continue;
                }

                if (!found
                    || candidate.Importance > item.Importance
                    || (candidate.Importance == item.Importance && candidate.Hops < item.Hops))
                {
                    item = candidate;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>某人是否已经知道这件事。</summary>
        public bool Knows(string npcId, string subjectId, string claim)
        {
            return byNpc.TryGetValue(npcId, out var items) && IndexOf(items, subjectId, claim) >= 0;
        }

        static int IndexOf(List<Item> items, string subjectId, string claim)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i].SubjectId, subjectId, StringComparison.Ordinal)
                    && string.Equals(items[i].Claim, claim, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        static int MentionIndex(string text, Npc npc)
        {
            var byName = text.IndexOf(npc.DisplayName, StringComparison.Ordinal);
            var byId = text.IndexOf(npc.Id, StringComparison.OrdinalIgnoreCase);
            if (byName < 0)
            {
                return byId;
            }

            return byId < 0 ? byName : Math.Min(byName, byId);
        }

        static string ClaimOf(string text, Npc subject)
        {
            var claim = RemoveAll(text, subject.DisplayName, StringComparison.Ordinal);
            claim = RemoveAll(claim, subject.Id, StringComparison.OrdinalIgnoreCase);
            return TrimClaim(claim);
        }

        static string RemoveAll(string text, string word, StringComparison comparison)
        {
            if (string.IsNullOrEmpty(word))
            {
                return text;
            }

            var index = text.IndexOf(word, comparison);
            while (index >= 0)
            {
                text = text.Remove(index, word.Length);
                index = text.IndexOf(word, index, comparison);
            }

            return text;
        }

        static string TrimClaim(string text)
        {
            var claim = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim(ClaimTrimChars);
            var changed = true;
            while (changed && claim.Length > 0)
            {
                changed = false;
                foreach (var noise in ClaimNoise)
                {
                    if (!claim.StartsWith(noise, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    claim = claim.Substring(noise.Length).Trim(ClaimTrimChars);
                    changed = true;
                    break;
                }
            }

            return IsParticleOnly(claim) ? string.Empty : claim;
        }

        static bool IsParticleOnly(string claim)
        {
            foreach (var ch in claim)
            {
                if (ParticleOnly.IndexOf(ch) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>一条已知的传闻：关于谁、说了什么、听者该记多少、转了几手。</summary>
        internal readonly struct Item
        {
            public Item(string subjectId, string subjectName, string claim, int importance, int hops)
            {
                SubjectId = subjectId ?? string.Empty;
                SubjectName = subjectName ?? string.Empty;
                Claim = claim ?? string.Empty;
                Importance = Math.Max(1, Math.Min(MaxImportance, importance));
                Hops = Math.Max(0, hops);
            }

            public string SubjectId { get; }

            public string SubjectName { get; }

            public string Claim { get; }

            public int Importance { get; }

            public int Hops { get; }
        }
    }
}
