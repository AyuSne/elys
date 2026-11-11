namespace ElysFarm.Core
{
    /// <summary>
    /// 一次已执行工步的流水，供 HUD、决策可视化与测试断言「某工步发生过」。
    /// </summary>
    public readonly struct WorkLogEntry
    {
        /// <summary>发生时的游戏日。</summary>
        public int Day { get; }

        /// <summary>执行者。</summary>
        public string NpcId { get; }

        /// <summary>目标地块。</summary>
        public string FieldId { get; }

        /// <summary>执行的工步。</summary>
        public WorkStep Step { get; }

        /// <summary>本次收获的产出数量；非收获为 0。</summary>
        public int Yield { get; }

        /// <summary>执行后地块的状态。</summary>
        public FieldState FieldStateAfter { get; }

        public WorkLogEntry(
            int day,
            string npcId,
            string fieldId,
            WorkStep step,
            int yield,
            FieldState fieldStateAfter)
        {
            Day = day;
            NpcId = npcId;
            FieldId = fieldId;
            Step = step;
            Yield = yield;
            FieldStateAfter = fieldStateAfter;
        }
    }
}
