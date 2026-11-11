using System;

namespace ElysFarm.Core
{
    /// <summary>
    /// NPC：农场中的自主角色（见 GLOSSARY）。
    /// 领域层只保留会影响决策的状态——位置与体力；表现、台词与人设不在这一层。
    /// </summary>
    public sealed class Npc
    {
        /// <summary>低于该体力时拒绝任何工步，理由是「太累了」。</summary>
        public const float MinStamina = 0.15f;

        /// <summary>每执行一个工步消耗的体力。</summary>
        public const float StaminaPerStep = 0.08f;

        /// <summary>每个游戏日恢复的体力。</summary>
        public const float StaminaPerDay = 0.35f;

        string preferredSpeciesId = CropCatalog.RadishId;

        /// <summary>NPC 的唯一 id。</summary>
        public string Id { get; }

        /// <summary>展示名，例如「阿禾」。</summary>
        public string DisplayName { get; }

        /// <summary>在农场平面上的位置。</summary>
        public FarmPosition Position { get; private set; }

        /// <summary>体力，0..1；影响效用打分的体力项，低于阈值时无法执行工步。</summary>
        public float Stamina { get; private set; } = 1f;

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

        public Npc(string id, string displayName, FarmPosition position = default)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("NPC 必须有 id", nameof(id));
            }

            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            Position = position;
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
            Stamina = FarmMath.Clamp01(Stamina - StaminaPerStep);
        }

        internal void RecoverDay()
        {
            Stamina = FarmMath.Clamp01(Stamina + StaminaPerDay);
        }
    }
}
