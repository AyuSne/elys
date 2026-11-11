namespace ElysFarm.Core
{
    /// <summary>
    /// 一次工步执行的结果。被否决时 <see cref="Reason"/> 必须可读，
    /// 该理由同时用于 NPC 的拒绝台词与决策可视化的否决原因。
    /// </summary>
    public readonly struct StepResult
    {
        public bool Executed { get; }

        public string NpcId { get; }

        public string FieldId { get; }

        /// <summary>被否决或无事可做时无工步可报。</summary>
        public WorkStep? Step { get; }

        /// <summary>本次收获的产出数量；非收获为 0。</summary>
        public int Yield { get; }

        /// <summary>未执行时的可读理由。</summary>
        public string Reason { get; }

        StepResult(bool executed, string npcId, string fieldId, WorkStep? step, int yield, string reason)
        {
            Executed = executed;
            NpcId = npcId;
            FieldId = fieldId;
            Step = step;
            Yield = yield;
            Reason = reason;
        }

        public static StepResult Success(string npcId, string fieldId, WorkStep step, int yield)
        {
            return new StepResult(true, npcId, fieldId, step, yield, null);
        }

        public static StepResult Rejected(string npcId, string fieldId, WorkStep step, string reason)
        {
            return new StepResult(false, npcId, fieldId, step, 0, reason);
        }

        public static StepResult None(string npcId, string reason)
        {
            return new StepResult(false, npcId, null, null, 0, reason);
        }
    }
}
