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

        public string Id { get; }

        public string DisplayName { get; }

        /// <summary>在农场平面上的 X 坐标。</summary>
        public float X { get; private set; }

        /// <summary>在农场平面上的 Z 坐标。</summary>
        public float Z { get; private set; }

        /// <summary>体力，0..1；影响效用打分的体力项，低于阈值时无法执行工步。</summary>
        public float Stamina { get; private set; } = 1f;

        /// <summary>当前打算种的品种；播种时从种子库扣该品种的种子。</summary>
        public string PreferredSpeciesId { get; set; } = CropCatalog.RadishId;

        public Npc(string id, string displayName, float x = 0f, float z = 0f)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("NPC 必须有 id", nameof(id));
            }

            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            X = x;
            Z = z;
        }

        public float DistanceTo(Field field)
        {
            return field.DistanceTo(X, Z);
        }

        internal void MoveTo(float x, float z)
        {
            X = x;
            Z = z;
        }

        internal void ExertStep()
        {
            Stamina = Clamp01(Stamina - StaminaPerStep);
        }

        internal void RecoverDay()
        {
            Stamina = Clamp01(Stamina + StaminaPerDay);
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
