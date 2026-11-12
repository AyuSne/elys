using System;

namespace ElysFarm.Core
{
    /// <summary>
    /// NPC：农场中的自主角色（见 GLOSSARY）。
    /// 领域层保留会影响决策的状态——位置、体力与人设参数；心情与饥饿只供决策可视化的状态条展示，
    /// 不参与效用打分。台词、表情与背景故事不在这一层。
    /// </summary>
    public sealed class Npc
    {
        /// <summary>体力下限：人设的休息阈值与休息恢复量都不得让它继续下探。</summary>
        public const float MinStamina = 0.15f;

        /// <summary>每个游戏日恢复的体力。</summary>
        public const float StaminaPerDay = 0.35f;

        /// <summary>每执行一个工步增加的饥饿。</summary>
        public const float HungerPerStep = 0.05f;

        /// <summary>跨日进食后回落的饥饿。</summary>
        public const float HungerRecoveryPerDay = 0.30f;

        /// <summary>每执行一个工步下降的心情。</summary>
        public const float MoodPerStep = 0.015f;

        /// <summary>跨日恢复的心情。</summary>
        public const float MoodRecoveryPerDay = 0.06f;

        /// <summary>一次休息结束恢复的心情。</summary>
        public const float MoodRecoveryPerRest = 0.05f;

        /// <summary>饥饿到该值后，每干一个工步额外掉心情。</summary>
        public const float HungerMoodThreshold = 0.70f;

        /// <summary>太饿时每个工步额外掉的心情。</summary>
        public const float HungerMoodPenalty = 0.02f;

        /// <summary>休息中尝试干活时的统一理由。</summary>
        public const string RestingReason = "正在休息，得缓一缓";

        string preferredSpeciesId = CropCatalog.RadishId;

        /// <summary>NPC 的唯一 id。</summary>
        public string Id { get; }

        /// <summary>展示名，例如「阿禾」。</summary>
        public string DisplayName { get; }

        /// <summary>人设参数：决定快慢、体力消耗、休息节奏与效用权重。</summary>
        public NpcPersona Persona { get; }

        /// <summary>在农场平面上的位置。</summary>
        public FarmPosition Position { get; private set; }

        /// <summary>体力，0..1；影响效用打分的体力项，低于阈值时无法执行工步。</summary>
        public float Stamina { get; private set; } = 1f;

        /// <summary>
        /// 心情，0..1。只由工步消耗、休息与跨日结算驱动，供决策可视化的状态条展示；
        /// 效用打分仍按 ADR-0002 只使用体力，不把心情混进决策。
        /// </summary>
        public float Mood { get; private set; } = 0.65f;

        /// <summary>饥饿，0..1（1 = 最饿）；干活上升，跨日进食后回落。</summary>
        public float Hunger { get; private set; } = 0.10f;

        /// <summary>是否在休息；休息期间不接活，体力按人设参数恢复。</summary>
        public bool IsResting { get; private set; }

        /// <summary>距离下一次可做决定还剩多少游戏秒。</summary>
        public float DecisionSecondsRemaining { get; private set; }

        /// <summary>本次休息还剩多少游戏秒；不在休息时为 0。</summary>
        public float RestSecondsRemaining { get; private set; }

        /// <summary>此刻是否已经想好、可以决定下一步。</summary>
        public bool CanDecide => !IsResting && DecisionSecondsRemaining <= 0f;

        /// <summary>当前打算种的品种；播种时从种子库扣该品种的种子。未知品种会被拒绝。</summary>
        public string PreferredSpeciesId
        {
            get => preferredSpeciesId;
            set
            {
                if (string.IsNullOrWhiteSpace(value) || !CropCatalog.TryGet(value, out _))
                {
                    throw new ArgumentException($"未知品种：{value}", nameof(value));
                }

                preferredSpeciesId = value;
            }
        }

        public Npc(string id, string displayName, FarmPosition position = default, NpcPersona persona = null)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("NPC 必须有 id", nameof(id));
            }

            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            Position = position;
            Persona = persona ?? NpcPersona.Default;
            DecisionSecondsRemaining = Persona.DecisionSeconds;
        }

        /// <summary>与某地块的平面距离，供效用打分的距离项使用。</summary>
        public float DistanceTo(Field field)
        {
            return Position.DistanceTo(field.Position);
        }

        /// <summary>与任意一点的平面距离。</summary>
        public float DistanceTo(FarmPosition position)
        {
            return Position.DistanceTo(position);
        }

        internal void MoveTo(FarmPosition position)
        {
            Position = position;
        }

        internal void ExertStep()
        {
            Stamina = FarmMath.Clamp01(Stamina - Persona.ExertionPerStep);
            Hunger = FarmMath.Clamp01(Hunger + HungerPerStep);

            var hungerPenalty = Hunger >= HungerMoodThreshold ? HungerMoodPenalty : 0f;
            Mood = FarmMath.Clamp01(Mood - MoodPerStep - hungerPenalty);

            if (Stamina <= Persona.RestThreshold)
            {
                BeginRest();
            }
        }

        /// <summary>一次成功的决定开始计时；由 <see cref="FarmWorld.TryDecide"/> 调用。</summary>
        internal void MarkDecided()
        {
            DecisionSecondsRemaining = Persona.DecisionSeconds;
        }

        /// <summary>推进人设计时；由 <see cref="FarmWorld"/> 在 Tick / 跨日时统一调用。</summary>
        internal void AdvanceTime(float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }

            DecisionSecondsRemaining = Math.Max(0f, DecisionSecondsRemaining - seconds);

            if (!IsResting)
            {
                return;
            }

            RestSecondsRemaining = Math.Max(0f, RestSecondsRemaining - seconds);
            if (RestSecondsRemaining <= 0f)
            {
                FinishRest();
            }
        }

        /// <summary>进入休息：中断当前农活，按人设时长恢复体力。只由工步消耗触发。</summary>
        void BeginRest()
        {
            if (IsResting)
            {
                return;
            }

            IsResting = true;
            RestSecondsRemaining = Persona.RestSeconds;
            DecisionSecondsRemaining = 0f;
        }

        void FinishRest()
        {
            IsResting = false;
            RestSecondsRemaining = 0f;
            Stamina = FarmMath.Clamp01(Stamina + Persona.RestRecovery);
            Mood = FarmMath.Clamp01(Mood + MoodRecoveryPerRest);
            MarkDecided();
        }

        internal void RecoverDay()
        {
            Stamina = FarmMath.Clamp01(Stamina + StaminaPerDay);
            Hunger = FarmMath.Clamp01(Hunger - HungerRecoveryPerDay);
            Mood = FarmMath.Clamp01(Mood + MoodRecoveryPerDay);
        }
    }
}
