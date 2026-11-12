namespace ElysFarm.Core
{
    /// <summary>
    /// 一个候选工步及其效用打分的分项。决策可视化层直接展示这些分项，
    /// 因此「为什么选这个」不依赖任何日志文本。
    /// </summary>
    public readonly struct StepCandidate
    {
        /// <summary>候选地块的 id。</summary>
        public string FieldId { get; }

        /// <summary>候选工步。</summary>
        public WorkStep Step { get; }

        /// <summary>总效用；无效候选恒为 0。</summary>
        public float Score { get; }

        /// <summary>需求项：干湿度、杂草、成熟度等合成出的「现在有多该做」。</summary>
        public float Need { get; }

        /// <summary>该 NPC 的人设权重（勤勉系数），0..1。</summary>
        public float PersonaWeight { get; }

        /// <summary>距离扣分。</summary>
        public float DistancePenalty { get; }

        /// <summary>体力系数，0.6..1。</summary>
        public float StaminaFactor { get; }

        /// <summary>此刻是否满足前置条件与资源约束。</summary>
        public bool IsValid { get; }

        /// <summary>被否决的可读理由；有效候选为 null。</summary>
        public string RejectionReason { get; }

        public StepCandidate(
            string fieldId,
            WorkStep step,
            float score,
            float need,
            float personaWeight,
            float distancePenalty,
            float staminaFactor,
            bool isValid,
            string rejectionReason)
        {
            FieldId = fieldId;
            Step = step;
            Score = score;
            Need = need;
            PersonaWeight = personaWeight;
            DistancePenalty = distancePenalty;
            StaminaFactor = staminaFactor;
            IsValid = isValid;
            RejectionReason = rejectionReason;
        }
    }
}
