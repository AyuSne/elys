namespace ElysFarm.Core
{
    /// <summary>
    /// 一步农活的结果怎么说。执行了只给地块与工步；没执行则按 <see cref="StepOutcome"/> 分三种说法，
    /// 每种都先说清状态、再说落在哪块地：「等肥力降下来（A1 · 施肥）」「不用施肥（A1 · 已经熟了，先收了吧）」
    /// 「没做成：种子库里没有萝卜种子了（A1 · 播种）」。
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
                    return $"等{ConditionOf(reason)}（{what}）";
                case StepOutcome.Skipped:
                    // 「不用…」已经点出是哪种活，括号里补地块与为什么不用做。
                    return $"不用{WorkStepLabel.Of(step)}（{fieldId} · {reason}）";
                case StepOutcome.Failed:
                    return $"没做成：{reason}（{what}）";
                default:
                    return what;
            }
        }

        /// <summary>
        /// 等的是哪个条件：说「等什么」而不是「现在是什么样」，玩家才知道要等多久。
        /// 这份清单与 <see cref="ActionValidator.IsWaiting"/> 是同一批理由，新增等条件时两处一起加；
        /// 认不出的理由原样接在「等」后面，至少不会把它说成失败。
        /// </summary>
        static string ConditionOf(string reason)
        {
            if (reason == Field.NotMatureReason)
            {
                return "成熟";
            }

            if (reason == Field.NoWeedsReason)
            {
                return "草长出来";
            }

            if (reason == Field.WateredReason)
            {
                return "水分下去";
            }

            if (reason == Field.FertileReason)
            {
                return "肥力降下来";
            }

            return reason == Npc.RestingReason ? "休息完" : reason;
        }
    }
}
