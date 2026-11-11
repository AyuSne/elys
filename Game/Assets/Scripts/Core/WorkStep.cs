namespace ElysFarm.Core
{
    /// <summary>
    /// 五种工步（见 GLOSSARY：工步是农场层面「该做的农活」，
    /// 与 NPC 层面的「动作」不是同一个概念）。
    /// </summary>
    public enum WorkStep
    {
        /// <summary>播种。</summary>
        Sow,

        /// <summary>浇水。</summary>
        Water,

        /// <summary>施肥。</summary>
        Fertilize,

        /// <summary>除草。</summary>
        Weed,

        /// <summary>收获。</summary>
        Harvest
    }
}
