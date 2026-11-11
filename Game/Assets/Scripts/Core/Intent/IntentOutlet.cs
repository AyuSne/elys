namespace ElysFarm.Core
{
    /// <summary>
    /// 意图的三个出口（见规格）。只有 <see cref="FarmCommand"/> 可能改变游戏状态；
    /// 追问与闲聊只读，必须原样留下世界状态。
    /// </summary>
    public enum IntentOutlet
    {
        /// <summary>农事指令：可以排成工单，但仍要过动作校验。</summary>
        FarmCommand,

        /// <summary>追问：只读回答，不改变任何状态。</summary>
        Query,

        /// <summary>闲聊：只读回答，不改变任何状态。</summary>
        Chat
    }

    /// <summary>农事指令的目标，决定要排哪几个工步。</summary>
    public enum FarmGoal
    {
        /// <summary>种满一块地：播种 → 浇水 → 施肥 → 除草 → 收获。</summary>
        Plant,

        /// <summary>只浇水。</summary>
        Water,

        /// <summary>只施肥。</summary>
        Fertilize,

        /// <summary>只除草。</summary>
        Weed,

        /// <summary>只收获。</summary>
        Harvest
    }
}
