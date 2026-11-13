namespace ElysFarm.Core
{
    /// <summary>
    /// 一次工步尝试的结果（见 GLOSSARY「工单」）。工单的未执行结果分三态：
    /// <see cref="Waiting"/>（等条件，过一阵再试）、<see cref="Skipped"/>（这一步不必做，工单接着做后面的）、
    /// <see cref="Failed"/>（做不下去，工单要释放占位）；<see cref="Executed"/> 表示真的做了。
    /// 「没做成」只说失败：等条件与跳过各有自己的说法，卡片与抽屉按同一份文案展示。
    /// </summary>
    public enum StepOutcome
    {
        /// <summary>真的执行了。</summary>
        Executed,

        /// <summary>等条件：前置状态还没到（没熟、刚浇过、肥力还够），工单保留。</summary>
        Waiting,

        /// <summary>跳过：这一步不必做（作物已经熟了），工单继续做后面的工步。</summary>
        Skipped,

        /// <summary>失败：做不下去（没种子、地块状态变了），工单释放占位。</summary>
        Failed
    }
}
