using System;
using System.Collections.Generic;

namespace ElysFarm.Core
{
    /// <summary>
    /// 手写效用打分（见 ADR-0002）：把地块的原始需求（<see cref="Field.Need"/>）按工步加权，
    /// 再乘体力系数、减距离扣分，得到候选工步的排序。
    /// 只做排序，不执行；执行必须再过 <see cref="ActionValidator"/>。
    /// 排序时另外避开「别人正要去」的地块（自主目标，#27）——那是只读的候选排除，
    /// 不进动作校验，所以玩家点名的指令照样能派到同一块地。
    /// </summary>
    public sealed class UtilityScorer
    {
        /// <summary>距离归一化尺度：超过该距离的扣分封顶。</summary>
        public const float DistanceScale = 20f;

        /// <summary>距离扣分的默认权重；每名 NPC 的人设可以覆盖它。</summary>
        public const float DistanceWeight = 0.15f;

        /// <summary>体力再低，动作也保留这个比例的效用——累了会变慢，不会直接罢工。</summary>
        public const float StaminaFloor = 0.6f;

        // 各工步的需求权重：收获最急，施肥最缓。
        const float SowWeight = 0.95f;
        const float WaterWeight = 1.25f;
        const float WaterUrgencyExponent = 1.2f;
        const float FertilizeWeight = 0.55f;
        const float WeedWeight = 0.9f;
        const float HarvestWeight = 1.5f;

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

        static StepCandidate Score(FarmWorld world, Npc npc, Field field, WorkStep step)
        {
            string reason;
            bool isValid;

            // 自主目标只在这一条读路径上生效：别人正要去的地块，在候选排序里与工单占位同等对待，
            // 理由也用同一句「<名字>正在弄这块地」。动作校验不读它，玩家点名的指令不受影响（#27）。
            var goalOwner = world.AutonomousOwnerOf(field.Id);
            if (goalOwner != null && goalOwner != npc)
            {
                isValid = false;
                reason = ActionValidator.ClaimedReason(goalOwner);
            }
            else
            {
                isValid = ActionValidator.Validate(world, npc, field, step, out reason);
            }

            var need = WeightedNeed(field, step);
            var personaWeight = npc.Persona.Diligence;
            var distance = npc.DistanceTo(field);
            var distancePenalty = npc.Persona.DistanceWeight * FarmMath.Clamp01(distance / DistanceScale);
            var staminaFactor = StaminaFloor + (1f - StaminaFloor) * npc.Stamina;
            var score = isValid ? need * personaWeight * staminaFactor - distancePenalty : 0f;

            return new StepCandidate(
                field.Id,
                step,
                score,
                need,
                personaWeight,
                distancePenalty,
                staminaFactor,
                isValid,
                reason);
        }

        static float WeightedNeed(Field field, WorkStep step)
        {
            var need = field.Need(step);

            switch (step)
            {
                case WorkStep.Sow:
                    return need * SowWeight;
                case WorkStep.Water:
                    return Pow(need, WaterUrgencyExponent) * WaterWeight;
                case WorkStep.Fertilize:
                    return need * FertilizeWeight;
                case WorkStep.Weed:
                    return need * WeedWeight;
                case WorkStep.Harvest:
                    return need * HarvestWeight;
                default:
                    return 0f;
            }
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
    }
}
