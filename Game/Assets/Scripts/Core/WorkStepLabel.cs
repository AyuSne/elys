namespace ElysFarm.Core
{
    /// <summary>
    /// 工步的中文名。意图、记忆与拒绝理由都共用这一份词表，避免领域层与演示层各写一套
    /// （见 GLOSSARY：播种 / 浇水 / 施肥 / 除草 / 收获）。
    /// </summary>
    public static class WorkStepLabel
    {
        /// <summary>取工步的中文名。</summary>
        public static string Of(WorkStep step)
        {
            switch (step)
            {
                case WorkStep.Sow:
                    return "播种";
                case WorkStep.Water:
                    return "浇水";
                case WorkStep.Fertilize:
                    return "施肥";
                case WorkStep.Weed:
                    return "除草";
                case WorkStep.Harvest:
                    return "收获";
                default:
                    return $"未知工步（{step}）";
            }
        }
    }
}
