using System;

namespace ElysFarm.Core
{
    /// <summary>
    /// NPC 的人设参数。只有会影响决策的部分放在领域层：决定快慢、每个工步的体力消耗、
    /// 何时去休息、效用打分的人设与距离权重。台词、表情、背景故事与行走速度留在表现层。
    /// 三名 NPC 的可观察差异（抢着干 / 摸鱼 / 磨蹭）必须来自这些参数，而不是台词（见 #5）。
    /// </summary>
    public sealed class NpcPersona
    {
        /// <summary>人设 id，与 NPC 的 id 区分；HUD 用它挑展示文案。</summary>
        public string Id { get; }

        /// <summary>一次决定消耗的间隔（游戏秒），由 <see cref="FarmWorld.TryDecide"/> 成功时扣除。越小越「快」。</summary>
        public float DecisionSeconds { get; }

        /// <summary>每执行一个工步消耗的体力。</summary>
        public float ExertionPerStep { get; }

        /// <summary>体力降到该值时去休息；不得低于 <see cref="Npc.MinStamina"/>。</summary>
        public float RestThreshold { get; }

        /// <summary>一次休息的时长（游戏秒）。</summary>
        public float RestSeconds { get; }

        /// <summary>休息结束时恢复的体力。</summary>
        public float RestRecovery { get; }

        /// <summary>效用打分的人设乘数：越勤勉，同一件农活的分越高。</summary>
        public float Diligence { get; }

        /// <summary>距离扣分的权重：越怕走路，越偏向近处的地块。</summary>
        public float DistanceWeight { get; }

        public NpcPersona(
            string id,
            float decisionSeconds,
            float exertionPerStep,
            float restThreshold,
            float restSeconds,
            float restRecovery,
            float diligence,
            float distanceWeight)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("人设必须有 id", nameof(id));
            }

            RequireNonNegative(decisionSeconds, nameof(decisionSeconds));
            RequireNonNegative(exertionPerStep, nameof(exertionPerStep));
            RequireNonNegative(restSeconds, nameof(restSeconds));
            RequireNonNegative(distanceWeight, nameof(distanceWeight));

            if (restRecovery < Npc.MinStamina)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(restRecovery),
                    restRecovery,
                    "休息恢复量不得低于体力下限");
            }

            if (restThreshold < Npc.MinStamina || restThreshold > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(restThreshold),
                    restThreshold,
                    "休息阈值必须在体力下限到 1 之间");
            }

            if (diligence < 0f || diligence > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(diligence), diligence, "勤勉系数必须在 0..1");
            }

            Id = id;
            DecisionSeconds = decisionSeconds;
            ExertionPerStep = exertionPerStep;
            RestThreshold = restThreshold;
            RestSeconds = restSeconds;
            RestRecovery = restRecovery;
            Diligence = diligence;
            DistanceWeight = distanceWeight;
        }

        /// <summary>阿禾：稳、勤、话少。决定不慢，体力消耗低，几乎不提前休息。</summary>
        public static readonly NpcPersona Ahe = new NpcPersona(
            "ahe",
            decisionSeconds: 0.70f,
            exertionPerStep: 0.06f,
            restThreshold: Npc.MinStamina,
            restSeconds: 12f,
            restRecovery: 0.55f,
            diligence: 1.00f,
            distanceWeight: 0.15f);

        /// <summary>阿芽：快、爱偷懒、爱甩锅。决定最快，但累得快、歇得也勤。</summary>
        public static readonly NpcPersona Aya = new NpcPersona(
            "aya",
            decisionSeconds: 0.25f,
            exertionPerStep: 0.12f,
            restThreshold: 0.55f,
            restSeconds: 10f,
            restRecovery: 0.45f,
            diligence: 0.72f,
            distanceWeight: 0.32f);

        /// <summary>阿土：慢、懂多、爱八卦。决定最慢，但对远近不那么计较。</summary>
        public static readonly NpcPersona Atu = new NpcPersona(
            "atu",
            decisionSeconds: 1.80f,
            exertionPerStep: 0.07f,
            restThreshold: 0.25f,
            restSeconds: 8f,
            restRecovery: 0.50f,
            diligence: 0.90f,
            distanceWeight: 0.06f);

        /// <summary>未指定人设时的兼容基线：决定间隔 0、每步 0.08 体力，沿用单 NPC 版本的手感。</summary>
        public static readonly NpcPersona Default = new NpcPersona(
            "default",
            decisionSeconds: 0f,
            exertionPerStep: 0.08f,
            restThreshold: Npc.MinStamina,
            restSeconds: 8f,
            restRecovery: 0.50f,
            diligence: 1.00f,
            distanceWeight: UtilityScorer.DistanceWeight);

        static void RequireNonNegative(float value, string name)
        {
            if (value < 0f)
            {
                throw new ArgumentOutOfRangeException(name, value, "人设参数不能为负");
            }
        }
    }
}
