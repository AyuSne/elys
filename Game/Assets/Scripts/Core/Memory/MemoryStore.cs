using System;
using System.Collections.Generic;

namespace ElysFarm.Core
{
    /// <summary>
    /// 记忆中台（见规格）：按 NPC 记录带游戏日与 importance（1–10）的记忆，
    /// 按 <c>0.5×相似度 + 0.3×重要性 + 0.2×时间衰减</c> 取前几条。
    /// 相似度先用本地关键词 bigram，断网也照常检索；#7 接 embedding 时只替换相似度这一项，
    /// 排序公式与决策可视化的展示接口保持不变。
    /// 模块内部，不单开测试接缝；所有行为通过 <see cref="FarmWorld"/> 暴露。
    /// </summary>
    internal sealed class MemoryStore
    {
        /// <summary>相似度项的权重。</summary>
        public const float SimilarityWeight = 0.5f;

        /// <summary>重要性项的权重。</summary>
        public const float ImportanceWeight = 0.3f;

        /// <summary>时间衰减项的权重。</summary>
        public const float RecencyWeight = 0.2f;

        /// <summary>importance 的合法范围。</summary>
        public const int MaxImportance = 10;

        readonly Dictionary<string, List<MemoryEntry>> byNpc = new Dictionary<string, List<MemoryEntry>>();

        /// <summary>记下一条属于某 NPC 的记忆；importance 会被夹到 1..10。</summary>
        public void Remember(string npcId, string text, int day, int importance)
        {
            if (string.IsNullOrWhiteSpace(npcId))
            {
                throw new ArgumentException("记忆必须属于某个 NPC", nameof(npcId));
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (!byNpc.TryGetValue(npcId, out var entries))
            {
                entries = new List<MemoryEntry>();
                byNpc.Add(npcId, entries);
            }

            var clamped = Math.Max(1, Math.Min(MaxImportance, importance));
            entries.Add(new MemoryEntry(text.Trim(), day, clamped));
        }

        /// <summary>检索某名 NPC 的前 count 条记忆，按权重降序；权重相同时新记忆优先。</summary>
        public IReadOnlyList<MemoryHit> Retrieve(string npcId, string cue, int currentDay, int count)
        {
            if (count <= 0 || !byNpc.TryGetValue(npcId, out var entries) || entries.Count == 0)
            {
                return Array.Empty<MemoryHit>();
            }

            var scored = new List<MemoryHit>(entries.Count);
            foreach (var entry in entries)
            {
                var similarity = MemorySimilarity.Between(cue, entry.Text);
                var importance = entry.Importance / (float)MaxImportance;
                var ageDays = Math.Max(0, currentDay - entry.Day);
                var recency = 1f / (1f + ageDays);
                var weight = SimilarityWeight * similarity
                    + ImportanceWeight * importance
                    + RecencyWeight * recency;

                scored.Add(new MemoryHit(entry.Text, entry.Day, weight));
            }

            scored.Sort(Compare);
            if (scored.Count > count)
            {
                scored.RemoveRange(count, scored.Count - count);
            }

            return scored;
        }

        static int Compare(MemoryHit a, MemoryHit b)
        {
            var byWeight = b.Weight.CompareTo(a.Weight);
            if (byWeight != 0)
            {
                return byWeight;
            }

            var byDay = b.Day.CompareTo(a.Day);
            return byDay != 0 ? byDay : string.CompareOrdinal(a.Text, b.Text);
        }

        readonly struct MemoryEntry
        {
            public MemoryEntry(string text, int day, int importance)
            {
                Text = text;
                Day = day;
                Importance = importance;
            }

            public string Text { get; }

            public int Day { get; }

            public int Importance { get; }
        }
    }

    /// <summary>
    /// 记忆与当前情境的关键词相似度：把两边切成字符 bigram，取 Jaccard 系数。
    /// 中文没有空格分词，bigram 在短句上足够稳定，且完全离线、可重复。
    /// </summary>
    internal static class MemorySimilarity
    {
        /// <summary>两段文本的相似度，0..1。</summary>
        public static float Between(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return 0f;
            }

            var self = Bigrams(left);
            var other = Bigrams(right);
            if (self.Count == 0 || other.Count == 0)
            {
                return 0f;
            }

            var shared = 0;
            foreach (var gram in self)
            {
                if (other.Contains(gram))
                {
                    shared++;
                }
            }

            var union = self.Count + other.Count - shared;
            return union <= 0 ? 0f : shared / (float)union;
        }

        static HashSet<string> Bigrams(string text)
        {
            var chars = new List<char>(text.Length);
            foreach (var raw in text)
            {
                if (char.IsWhiteSpace(raw) || char.IsPunctuation(raw))
                {
                    continue;
                }

                chars.Add(char.ToLowerInvariant(raw));
            }

            var grams = new HashSet<string>();
            if (chars.Count == 1)
            {
                grams.Add(chars[0].ToString());
                return grams;
            }

            for (var i = 0; i + 1 < chars.Count; i++)
            {
                grams.Add(new string(new[] { chars[i], chars[i + 1] }));
            }

            return grams;
        }
    }
}
