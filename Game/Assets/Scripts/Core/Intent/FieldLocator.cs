using System;

namespace ElysFarm.Core
{
    /// <summary>
    /// 把「东边那块」「最近的地」之类的指称落到具体地块。
    /// 方向按农场平面上的极值选取，多个地块打平时取离 NPC 最近的那个。
    /// </summary>
    internal static class FieldLocator
    {
        public static bool TryResolve(FarmWorld world, Npc npc, string token, out Field field, out string reason)
        {
            field = null;
            reason = null;

            if (world.Fields.Count == 0)
            {
                reason = "农场里还没有地块。";
                return false;
            }

            var text = (token ?? string.Empty).Trim();
            if (text.Length == 0 || IsAny(text))
            {
                field = Nearest(world, npc);
                return true;
            }

            foreach (var candidate in world.Fields)
            {
                if (string.Equals(candidate.Id, text, StringComparison.OrdinalIgnoreCase))
                {
                    field = candidate;
                    return true;
                }
            }

            float dx;
            float dz;
            if (TryDirection(text, out dx, out dz))
            {
                field = Extreme(world, npc, dx, dz);
                return true;
            }

            if (IsNear(text))
            {
                field = Nearest(world, npc);
                return true;
            }

            reason = $"农场里没有「{text}」这块地。";
            return false;
        }

        /// <summary>指称本身是一个方向词时，给出对应的中文方位；用于把玩家原话写进记忆。</summary>
        public static bool TryDirectionLabel(string token, out string label)
        {
            label = null;
            var text = (token ?? string.Empty).Trim();
            if (text.Length == 0 || !TryDirection(text, out var dx, out var dz))
            {
                return false;
            }

            if (dx > 0f)
            {
                label = "东边";
            }
            else if (dx < 0f)
            {
                label = "西边";
            }
            else if (dz > 0f)
            {
                label = "北边";
            }
            else
            {
                label = "南边";
            }

            return true;
        }

        /// <summary>指称没有点名具体地块，可以交给效用打分去挑。</summary>
        public static bool IsImplicit(string token)
        {
            return string.IsNullOrWhiteSpace(token) || IsAny(token ?? string.Empty) || IsNear(token ?? string.Empty);
        }

        static bool IsAny(string text)
        {
            switch (text.ToLowerInvariant())
            {
                case "any":
                case "all":
                case "none":
                case "null":
                    return true;
                default:
                    return false;
            }
        }

        static bool IsNear(string text)
        {
            switch (text.ToLowerInvariant())
            {
                case "near":
                case "nearest":
                case "这里":
                case "那里":
                    return true;
                default:
                    return text.Contains("最近") || text.Contains("附近") || text.Contains("旁边");
            }
        }

        static bool TryDirection(string text, out float dx, out float dz)
        {
            dx = 0f;
            dz = 0f;

            switch (text.ToLowerInvariant())
            {
                case "east":
                case "东":
                case "东边":
                case "东面":
                case "东侧":
                case "东头":
                    dx = 1f;
                    return true;
                case "west":
                case "西":
                case "西边":
                case "西面":
                case "西侧":
                case "西头":
                    dx = -1f;
                    return true;
                case "north":
                case "北":
                case "北边":
                case "北面":
                case "北侧":
                case "北头":
                    dz = 1f;
                    return true;
                case "south":
                case "南":
                case "南边":
                case "南面":
                case "南侧":
                case "南头":
                    dz = -1f;
                    return true;
                default:
                    return false;
            }
        }

        static Field Extreme(FarmWorld world, Npc npc, float dx, float dz)
        {
            Field best = null;
            var bestProjection = float.NegativeInfinity;
            var bestDistance = float.PositiveInfinity;

            foreach (var candidate in world.Fields)
            {
                var projection = candidate.Position.X * dx + candidate.Position.Z * dz;
                var distance = npc.DistanceTo(candidate);
                var better = projection > bestProjection
                    || (Math.Abs(projection - bestProjection) < 0.0001f && distance < bestDistance);

                if (!better)
                {
                    continue;
                }

                best = candidate;
                bestProjection = projection;
                bestDistance = distance;
            }

            return best;
        }

        static Field Nearest(FarmWorld world, Npc npc)
        {
            Field best = null;
            var bestDistance = float.PositiveInfinity;

            foreach (var candidate in world.Fields)
            {
                var distance = npc.DistanceTo(candidate);
                if (best == null || distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            return best;
        }
    }
}
