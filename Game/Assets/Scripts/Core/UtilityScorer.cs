using System;
using System.Collections.Generic;

namespace ElysFarm.Core
{
    /// <summary>
    /// 手写效用打分（见 ADR-0002）：把「现在有多该做」合成一个需求项，
    /// 再乘体力系数、减距离扣分，得到候选工步的排序。
    /// 只做排序，不执行；执行必须再过 <see cref="ActionValidator"/>。
    /// </summary>
    public sealed class UtilityScorer
    {
        /// <summary>距离归一化尺度：超过该距离的扣分封顶。</summary>
        public const float DistanceScale = 20f;

        /// <summary>距离扣分的最大权重。</summary>
        public const float DistanceWeight = 0.15f;

        /// <summary>体力再低，动作也保留这个比例的效用——累了会变慢，不会直接罢工。</summary>
        public const float StaminaFloor = 0.6f;

        static readonly WorkStep[] AllSteps =
        {
            WorkStep.Sow,
            WorkStep.Water,
            WorkStep.Fertilize,
            WorkStep.Weed,
            WorkStep.Harvest
        };

        /// <summary>给一个 NPC 排出一批候选工步：有效的按分数降序在前，无效的带理由在后。</summary>
        public IReadOnlyList<StepCandidate> Rank(FarmWorld world, Npc npc)
        {
            var candidates = new List<StepCandidate>(world.Fields.Count * AllSteps.Length);

            foreach (var field in world.Fields)
            {
                foreach (var step in AllSteps)
                {
                    candidates.Add(Score(world, npc, field, step));
                }
            }

            candidates.Sort(Compare);
            return candidates;
        }

        /// <summary>给单个候选工步打分；无效候选保留否决理由，分数记 0。</summary>
        public StepCandidate Score(FarmWorld world, Npc npc, Field field, WorkStep step)
        {
            var isValid = ActionValidator.Validate(world, npc, field, step, out var reason);
            var need = Need(field, step);
            var distancePenalty = DistanceWeight * Clamp01(npc.DistanceTo(field) / DistanceScale);
            var staminaFactor = StaminaFloor + (1f - StaminaFloor) * npc.Stamina;
            var score = isValid ? need * staminaFactor - distancePenalty : 0f;

            return new StepCandidate(
                field.Id,
                step,
                score,
                need,
                distancePenalty,
                staminaFactor,
                isValid,
                reason);
        }

        static float Need(Field field, WorkStep step)
        {
            switch (step)
            {
                case WorkStep.Sow:
                    return field.State == FieldState.Empty || field.State == FieldState.Harvested ? 0.95f : 0f;

                case WorkStep.Water:
                    return IsGrowing(field) ? Pow(1f - field.Water, 1.2f) * 1.25f : 0f;

                case WorkStep.Fertilize:
                    return IsGrowing(field) ? (1f - field.Fertility) * 0.55f : 0f;

                case WorkStep.Weed:
                    return IsGrowing(field) ? field.Weeds * 0.9f : 0f;

                case WorkStep.Harvest:
                    return field.State == FieldState.Mature ? 1.5f : 0f;

                default:
                    return 0f;
            }
        }

        static bool IsGrowing(Field field)
        {
            return field.State == FieldState.Sown || field.State == FieldState.Growing;
        }

        static int Compare(StepCandidate a, StepCandidate b)
        {
            if (a.IsValid != b.IsValid)
            {
                return a.IsValid ? -1 : 1;
            }

            var byScore = b.Score.CompareTo(a.Score);
            if (byScore != 0)
            {
                return byScore;
            }

            var byField = string.CompareOrdinal(a.FieldId, b.FieldId);
            return byField != 0 ? byField : a.Step.CompareTo(b.Step);
        }

        static float Pow(float value, float exponent)
        {
            return (float)Math.Pow(value, exponent);
        }

        static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }
    }
}
