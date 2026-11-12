using System;
using System.Collections.Generic;

namespace ElysFarm.Core
{
    /// <summary>
    /// 记忆中台（见规格）：按 NPC 记录带游戏日与 importance（1–10）的记忆。
    /// 两段式检索——先按相似度召回 10 条，再按
    /// <c>0.5×相似度 + 0.3×重要性 + 0.2×时间衰减</c> 取前几条。
    /// 相似度在有向量时用余弦，否则退回本地关键词 bigram；断网也照常检索。
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

        /// <summary>第一段检索召回的条数；加权重排只在这十条里进行。</summary>
        public const int RecallCount = 10;

        readonly Dictionary<string, List<MemoryEntry>> byNpc = new Dictionary<string, List<MemoryEntry>>();
        readonly Dictionary<string, Dictionary<string, float[]>> vectors =
            new Dictionary<string, Dictionary<string, float[]>>();

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

        /// <summary>还没有向量的记忆文本，新的在前；供路由器一次批量取向量。</summary>
        public IReadOnlyList<string> TextsNeedingEmbedding(string npcId, int count)
        {
            if (count <= 0 || !byNpc.TryGetValue(npcId, out var entries) || entries.Count == 0)
            {
                return Array.Empty<string>();
            }

            vectors.TryGetValue(npcId, out var byText);
            var texts = new List<string>();
            for (var i = entries.Count - 1; i >= 0 && texts.Count < count; i--)
            {
                var text = entries[i].Text;
                if ((byText != null && byText.ContainsKey(text)) || texts.Contains(text))
                {
                    continue;
                }

                texts.Add(text);
            }

            return texts;
        }

        /// <summary>缓存一条记忆的 embedding；同一文本重复缓存时后写入的覆盖先写入的。</summary>
        public void CacheEmbedding(string npcId, string text, float[] vector)
        {
            if (string.IsNullOrWhiteSpace(npcId)
                || string.IsNullOrWhiteSpace(text)
                || vector == null
                || vector.Length == 0)
            {
                return;
            }

            if (!vectors.TryGetValue(npcId, out var byText))
            {
                byText = new Dictionary<string, float[]>();
                vectors.Add(npcId, byText);
            }

            byText[text.Trim()] = (float[])vector.Clone();
        }

        /// <summary>
        /// 两段式检索：先按相似度召回 <see cref="RecallCount"/> 条，再按
        /// <c>0.5×相似度 + 0.3×重要性 + 0.2×时间衰减</c> 取前 count 条。
        /// 第二段只在召回的十条里重排，因此低相似度的记忆不会靠重要性直接空降。
        /// 余弦与关键词不是同一把尺子：只要有一条记忆缺向量或维度对不上，
        /// 这次检索就整体退回关键词，避免两种分数混排。
        /// </summary>
        public IReadOnlyList<MemoryHit> Retrieve(
            string npcId,
            string cue,
            int currentDay,
            int count,
            float[] cueEmbedding = null)
        {
            if (count <= 0 || !byNpc.TryGetValue(npcId, out var entries) || entries.Count == 0)
            {
                return Array.Empty<MemoryHit>();
            }

            // 余弦与关键词是两种尺度，不能逐条混排：只要有一条记忆缺向量或维度对不上，
            // 这次检索就整体退回关键词，保证召回阶段仍在同一把尺子上。
            var useVectors = HasCompleteEmbeddings(npcId, cueEmbedding, entries);
            var recalled = new List<Recalled>(entries.Count);
            foreach (var entry in entries)
            {
                recalled.Add(new Recalled(
                    entry,
                    useVectors
                        ? Cosine(cueEmbedding, VectorsOf(npcId)[entry.Text])
                        : MemorySimilarity.Between(cue, entry.Text)));
            }

            recalled.Sort(CompareSimilarity);
            if (recalled.Count > RecallCount)
            {
                recalled.RemoveRange(RecallCount, recalled.Count - RecallCount);
            }

            var scored = new List<MemoryHit>(recalled.Count);
            foreach (var candidate in recalled)
            {
                var importance = candidate.Entry.Importance / (float)MaxImportance;
                var ageDays = Math.Max(0, currentDay - candidate.Entry.Day);
                var recency = 1f / (1f + ageDays);
                var weight = SimilarityWeight * candidate.Similarity
                    + ImportanceWeight * importance
                    + RecencyWeight * recency;

                scored.Add(new MemoryHit(
                    candidate.Entry.Text,
                    candidate.Entry.Day,
                    weight,
                    candidate.Entry.Importance));
            }

            scored.Sort(Compare);
            if (scored.Count > count)
            {
                scored.RemoveRange(count, scored.Count - count);
            }

            return scored;
        }

        /// <summary>本次检索能否整段用余弦：线索和每条记忆都得有同维度的向量。</summary>
        bool HasCompleteEmbeddings(string npcId, float[] cueEmbedding, List<MemoryEntry> entries)
        {
            if (cueEmbedding == null || cueEmbedding.Length == 0 || !vectors.TryGetValue(npcId, out var byText))
            {
                return false;
            }

            foreach (var entry in entries)
            {
                if (!byText.TryGetValue(entry.Text, out var vector)
                    || vector == null
                    || vector.Length != cueEmbedding.Length)
                {
                    return false;
                }
            }

            return true;
        }

        Dictionary<string, float[]> VectorsOf(string npcId)
        {
            return vectors[npcId];
        }

        /// <summary>余弦相似度，负相关按 0 计；维度不一致或零向量一律按 0。</summary>
        static float Cosine(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length == 0 || a.Length != b.Length)
            {
                return 0f;
            }

            double dot = 0d;
            double lengthA = 0d;
            double lengthB = 0d;
            for (var i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                lengthA += a[i] * a[i];
                lengthB += b[i] * b[i];
            }

            if (lengthA <= 0d || lengthB <= 0d)
            {
                return 0f;
            }

            var cosine = dot / (Math.Sqrt(lengthA) * Math.Sqrt(lengthB));
            return (float)Math.Max(0d, Math.Min(1d, cosine));
        }

        static int CompareSimilarity(Recalled a, Recalled b)
        {
            var bySimilarity = b.Similarity.CompareTo(a.Similarity);
            if (bySimilarity != 0)
            {
                return bySimilarity;
            }

            var byDay = b.Entry.Day.CompareTo(a.Entry.Day);
            return byDay != 0 ? byDay : string.CompareOrdinal(a.Entry.Text, b.Entry.Text);
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

        readonly struct Recalled
        {
            public Recalled(MemoryEntry entry, float similarity)
            {
                Entry = entry;
                Similarity = similarity;
            }

            public MemoryEntry Entry { get; }

            public float Similarity { get; }
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
        /// <summary>当前情境（cue）与一条记忆的相似度，0..1。</summary>
        public static float Between(string cue, string memory)
        {
            if (string.IsNullOrWhiteSpace(cue) || string.IsNullOrWhiteSpace(memory))
            {
                return 0f;
            }

            var cueGrams = Bigrams(cue);
            var memoryGrams = Bigrams(memory);
            if (cueGrams.Count == 0 || memoryGrams.Count == 0)
            {
                return 0f;
            }

            var shared = 0;
            foreach (var gram in cueGrams)
            {
                if (memoryGrams.Contains(gram))
                {
                    shared++;
                }
            }

            var union = cueGrams.Count + memoryGrams.Count - shared;
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
