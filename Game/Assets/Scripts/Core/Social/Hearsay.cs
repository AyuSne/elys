using System;

namespace ElysFarm.Core
{
    /// <summary>
    /// 一条传闻（见 GLOSSARY「传闻」）：关于第三名 NPC 的一件事。
    /// 说法本身不随转述改变，<see cref="Hops"/> 记录转了几手，<see cref="Importance"/> 随转手递减。
    /// </summary>
    public readonly struct Hearsay
    {
        public Hearsay(string subjectId, string subjectName, string claim, int importance, int hops)
        {
            SubjectId = subjectId ?? string.Empty;
            SubjectName = subjectName ?? string.Empty;
            Claim = claim ?? string.Empty;
            Importance = MemoryStore.ClampImportance(importance);
            Hops = Math.Max(0, hops);
        }

        /// <summary>被谈论的 NPC。</summary>
        public string SubjectId { get; }

        /// <summary>被谈论的 NPC 的展示名。</summary>
        public string SubjectName { get; }

        /// <summary>关于这个人的说法，例如「今天偷懒了」；转述时原样带走。</summary>
        public string Claim { get; }

        /// <summary>知道这件事的 NPC 该记多少重要性（1–10）。</summary>
        public int Importance { get; }

        /// <summary>转了几手：直接听玩家说记 0，第一次转述记 1。</summary>
        public int Hops { get; }

        /// <summary>说到别人嘴边的那句话：「阿芽今天偷懒了」。</summary>
        public string Line => $"{SubjectName}{Claim}";

        /// <summary>再转述一手：重要性减一，手数加一。</summary>
        public Hearsay Retold()
        {
            return new Hearsay(
                SubjectId,
                SubjectName,
                Claim,
                Math.Max(1, Importance - 1),
                Hops + 1);
        }

        /// <summary>
        /// 某名听者记下的那句话：「听阿土说，阿芽今天偷懒了」；
        /// 事情正讲到听者自己时改用第二人称（「听阿禾说，你今天偷懒了」）。
        /// </summary>
        public string TextFor(string speakerName, string listenerId)
        {
            var who = string.Equals(listenerId, SubjectId, StringComparison.Ordinal) ? "你" : SubjectName;
            return $"听{speakerName}说，{who}{Claim}";
        }
    }
}
