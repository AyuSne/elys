using System;

namespace ElysFarm.Core
{
    /// <summary>
    /// NPC 的人设参数。只有会影响决策的部分放在领域层：决定快慢、每个工步的体力消耗、
    /// 何时去休息、效用打分的人设与距离权重。台词、表情与背景故事留在表现层。
    /// 三名 NPC 的可观察差异（抢着干 / 摸鱼 / 磨蹭）必须来自这些参数，而不是台词（见 #5）。
    /// </summary>
    public sealed class NpcPersona
    {
        /// <summary>人设 id，与 NPC 的 id 区分；HUD 用它挑展示文案。</summary>
        public string Id { get; }

        /// <summary>两次决定之间的间隔（游戏秒）。越小越「快」。</summary>
        public float DecisionSeconds { get; }

        /// <summary>每执行一个工步消耗的体力。</summary>
        public float StaminaPerStep { get; }

        /// <summary>体力降到该值时去休息；通常不低于 <see cref="Npc.MinStamina"/>。</summary>
        public float RestThreshold { get; }

        /// <summary>一次休息的时长（游戏秒）。</summary>
        public float RestSeconds { get; }

        /// <summary>休息结束时恢复的体力。</summary>
        public float RestRecovery { get; }

        /// <summary>效用打分的人设乘数：越勤勉，同一件农活的分越高。</summary>
        public float Diligence { get; }

        /// <summary>距离扣分的权重：越怕走路，越偏向近处的地块。</summary>
        public float DistanceWeight { get; }

        /// <summary>表现层的行走速度；领域层不推进位置，只把它当人设的一部分。</summary>
        public float WalkSpeed { get; }

        public NpcPersona(
            string id,
            float decisionSeconds,
            float staminaPerStep,
            float restThreshold,
            float restSeconds,
            float restRecovery,
            float diligence,
            float distanceWeight,
            float walkSpeed)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("人设必须有 id", nameof(id));
            }

            RequireNonNegative(decisionSeconds, nameof(decisionSeconds));
            RequireNonNegative(staminaPerStep, nameof(staminaPerStep));
            RequireNonNegative(restSeconds, nameof(restSeconds));
            RequireNonNegative(restRecovery, nameof(restRecovery));
            RequireNonNegative(distanceWeight, nameof(distanceWeight));

            if (restThreshold < 0f || restThreshold > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(restThreshold), restThreshold, "休息阈值必须在 0..1");
            }

            if (diligence < 0f || diligence > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(diligence), diligence, "勤勉系数必须在 0..1");
            }

            if (walkSpeed <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(walkSpeed), walkSpeed, "行走速度必须为正");
            }

            Id = id;
            DecisionSeconds = decisionSeconds;
            StaminaPerStep = staminaPerStep;
            RestThreshold = restThreshold;
            RestSeconds = restSeconds;
            RestRecovery = restRecovery;
            Diligence = diligence;
            DistanceWeight = distanceWeight;
            WalkSpeed = walkSpeed;
        }

        /// <summary>阿禾：稳、勤、话少。决定不慢，体力消耗低，几乎不提前休息。</summary>
        public static readonly NpcPersona Ahe = new NpcPersona(
            "ahe",
            decisionSeconds: 0.70f,
            staminaPerStep: 0.06f,
            restThreshold: Npc.MinStamina,
            restSeconds: 12f,
            restRecovery: 0.55f,
            diligence: 1.00f,
            distanceWeight: 0.15f,
            walkSpeed: 2.6f);

        /// <summary>阿芽：快、爱偷懒、爱甩锅。决定最快，但累得快、歇得也勤。</summary>
        public static readonly NpcPersona Aya = new NpcPersona(
            "aya",
            decisionSeconds: 0.25f,
            staminaPerStep: 0.12f,
            restThreshold: 0.55f,
            restSeconds: 10f,
            restRecovery: 0.45f,
            diligence: 0.72f,
            distanceWeight: 0.32f,
            walkSpeed: 3.4f);

        /// <summary>阿土：慢、懂多、爱八卦。决定最慢，走路也慢，但对远近不那么计较。</summary>
        public static readonly NpcPersona Atu = new NpcPersona(
            "atu",
            decisionSeconds: 1.80f,
            staminaPerStep: 0.07f,
            restThreshold: 0.25f,
            restSeconds: 8f,
            restRecovery: 0.50f,
            diligence: 0.90f,
            distanceWeight: 0.06f,
            walkSpeed: 1.6f);

        /// <summary>未指定人设时的默认值：不额外拖延，体力与休息沿用原来的领域常量。</summary>
        public static readonly NpcPersona Default = new NpcPersona(
            "default",
            decisionSeconds: 0f,
            staminaPerStep: Npc.StaminaPerStep,
            restThreshold: Npc.MinStamina,
            restSeconds: 8f,
            restRecovery: 0.50f,
            diligence: 1.00f,
            distanceWeight: UtilityScorer.DistanceWeight,
            walkSpeed: 2.6f);

        static void RequireNonNegative(float value, string name)
        {
            if (value < 0f)
            {
                throw new ArgumentOutOfRangeException(name, value, "人设参数不能为负");
            }
        }
    }
}
