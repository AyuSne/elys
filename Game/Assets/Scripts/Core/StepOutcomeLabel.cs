namespace ElysFarm.Core
{
    /// <summary>
    /// 一步农活的结果怎么说。执行了只给地块与工步；没执行则按 <see cref="StepOutcome"/> 分三种说法：
    /// 等条件（「等 A1 · 收获（还没熟）」）、跳过（「跳过 A1 · 施肥（已经熟了，先收了吧）」）、
    /// 失败（「没做成：A1 · 收获（种子库里没有萝卜种子了）」）。
    /// 三人卡的当前意图与决策可视化的意图都读这同一句，两边才不会各说一套（见 GLOSSARY「工单」）。
    /// </summary>
    public static class StepOutcomeLabel
    {
        /// <summary>把一步工步的结果写成一句可读的话。</summary>
        public static string Of(string fieldId, WorkStep step, StepOutcome outcome, string reason)
        {
            var what = $"{fieldId} · {WorkStepLabel.Of(step)}";
            switch (outcome)
            {
                case StepOutcome.Waiting:
                    return $"等 {what}（{reason}）";
                case StepOutcome.Skipped:
                    return $"跳过 {what}（{reason}）";
                case StepOutcome.Failed:
                    return $"没做成：{what}（{reason}）";
                default:
                    return what;
            }
        }
    }
}
