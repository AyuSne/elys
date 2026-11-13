namespace ElysFarm.Core
{
    /// <summary>
    /// 一次转述（见 GLOSSARY「转述」）：谁讲给谁听、讲的是哪件事说的话，以及听者记下了什么。
    /// <see cref="Line"/> 是转述者说的那句话，头像与名字跟着 <see cref="SpeakerId"/>，
    /// 所以对话框要展示的是「转述者 + 听 X 说，……」，而不是被议论的人。
    /// </summary>
    public readonly struct Retelling
    {
        internal Retelling(string speakerId, string speakerName, string listenerId, Hearsay heard, Hearsay learned)
        {
            SpeakerId = speakerId;
            SpeakerName = speakerName;
            ListenerId = listenerId;
            Heard = heard;
            Learned = learned;
        }

        /// <summary>讲的人。</summary>
        public string SpeakerId { get; }

        /// <summary>讲的人的展示名。</summary>
        public string SpeakerName { get; }

        /// <summary>听的人。</summary>
        public string ListenerId { get; }

        /// <summary>讲的人手里那条传闻，含他自己是从谁那儿听来的。</summary>
        public Hearsay Heard { get; }

        /// <summary>听者记下的那一条：来源换成当下讲的人，重要性减一，手数加一。</summary>
        public Hearsay Learned { get; }

        /// <summary>转述者说的那句话：「听阿土说，阿芽今天偷懒了」。</summary>
        public string Line => Heard.SaidBy(SpeakerId, ListenerId);

        /// <summary>被谈论的 NPC。</summary>
        public string SubjectId => Learned.SubjectId;

        /// <summary>被谈论的 NPC 的展示名。</summary>
        public string SubjectName => Learned.SubjectName;

        /// <summary>关于这个人的说法。</summary>
        public string Claim => Learned.Claim;

        /// <summary>听者记下的重要性：每转一手减一。</summary>
        public int Importance => Learned.Importance;

        /// <summary>到听者这里是第几手。</summary>
        public int Hops => Learned.Hops;
    }
}
