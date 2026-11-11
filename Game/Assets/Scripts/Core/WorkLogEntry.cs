namespace ElysFarm.Core
{
    /// <summary>
    /// 一次已执行工步的流水，供 HUD、决策可视化与测试断言「某工步发生过」。
    /// </summary>
    public readonly struct WorkLogEntry
    {
        public int Day { get; }

        public string NpcId { get; }

        public string FieldId { get; }

        public WorkStep Step { get; }

        public int Yield { get; }

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
