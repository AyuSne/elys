using System;

namespace ElysFarm.Core
{
    /// <summary>
    /// 一条传闻（见 GLOSSARY「传闻」）：关于第三名 NPC 的一件事。
    /// 说法本身不随转述改变，<see cref="Hops"/> 记录转了几手，<see cref="Importance"/> 随转手递减。
    /// <see cref="SourceName"/> 是「我从谁那儿听来的」——转述时说话的人要引述它（「听阿土说，…」）；
    /// 空表示直接从玩家那儿听来，只说「听说」。
    /// </summary>
    public readonly struct Hearsay
    {
        public Hearsay(
            string subjectId,
            string subjectName,
            string claim,
            int importance,
            int hops,
            string sourceName = null)
        {
            SubjectId = subjectId ?? string.Empty;
            SubjectName = subjectName ?? string.Empty;
            Claim = claim ?? string.Empty;
            Importance = MemoryStore.ClampImportance(importance);
            Hops = Math.Max(0, hops);
            SourceName = sourceName ?? string.Empty;
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

        /// <summary>这事是听谁说的（展示名）；空表示直接听玩家说的，转述时只说「听说」。</summary>
        public string SourceName { get; }

        /// <summary>说到别人嘴边的那句话：「阿芽今天偷懒了」。</summary>
        public string Line => $"{SubjectName}{Claim}";

        /// <summary>
        /// 再转述一手：重要性减一，手数加一，来源换成当下讲的人。
        /// 返回的是听者记下的那一条，不是讲的人说的那句话。
        /// </summary>
        public Hearsay Retold(string speakerName)
        {
            return new Hearsay(
                SubjectId,
                SubjectName,
                Claim,
                Math.Max(1, Importance - 1),
                Hops + 1,
                speakerName);
        }

        /// <summary>
        /// 转述者当面对听者说的那句话：「听阿土说，阿芽今天偷懒了」。
        /// 事情讲到讲的人自己时说「我」，讲到听者自己时说「你」；直接听玩家说的只说「听说」。
        /// </summary>
        public string SaidBy(string speakerId, string listenerId)
        {
            return SourcePrefix + WhoFor(speakerId, listenerId) + Claim;
        }

        /// <summary>
        /// 某名听者记下的那句话：「听阿土说，阿芽今天偷懒了」；
        /// 事情正讲到听者自己时改用第二人称（「听阿禾说，你今天偷懒了」）。
        /// </summary>
        public string TextFor(string listenerId)
        {
            return SourcePrefix + WhoFor(null, listenerId) + Claim;
        }

        /// <summary>听来的引述来源；直接听玩家说的只说「听说」。</summary>
        string SourcePrefix => string.IsNullOrEmpty(SourceName) ? "听说" : $"听{SourceName}说，";

        /// <summary>当事人怎么称呼：讲的人自己说「我」，听者自己说「你」，其余点名。</summary>
        string WhoFor(string speakerId, string listenerId)
        {
            if (!string.IsNullOrEmpty(speakerId) && string.Equals(speakerId, SubjectId, StringComparison.Ordinal))
            {
                return "我";
            }

            if (!string.IsNullOrEmpty(listenerId) && string.Equals(listenerId, SubjectId, StringComparison.Ordinal))
            {
                return "你";
            }

            return SubjectName;
        }
    }
}
