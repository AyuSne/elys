using ElysFarm.Core;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 演示层的中文文案。领域层不关心展示用词，把它们集中放在这里，
    /// 也保证 HUD 与流水用的是同一套工步名。
    /// </summary>
    public static class FarmDemoText
    {
        public static string StepName(WorkStep step)
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
                    return step.ToString();
            }
        }

        public static string StateName(FieldState state)
        {
            switch (state)
            {
                case FieldState.Empty:
                    return "空地";
                case FieldState.Sown:
                    return "已播种";
                case FieldState.Growing:
                    return "生长中";
                case FieldState.Mature:
                    return "成熟";
                case FieldState.Harvested:
                    return "已收获";
                default:
                    return state.ToString();
            }
        }
    }
}
