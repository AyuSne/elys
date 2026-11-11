using System;
using System.Collections.Generic;

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
                field = Nearest(world, npc, null);
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
                field = Nearest(world, npc, null);
                return true;
            }

            reason = $"农场里没有「{text}」这块地。";
            return false;
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

        static Field Nearest(FarmWorld world, Npc npc, Func<Field, bool> filter)
        {
            Field best = null;
            var bestDistance = float.PositiveInfinity;

            foreach (var candidate in world.Fields)
            {
                if (filter != null && !filter(candidate))
                {
                    continue;
                }

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

    internal static class SpeciesLocator
    {
        public static bool TryResolve(string token, Npc npc, out CropSpecies species, out string reason)
        {
            var text = string.IsNullOrWhiteSpace(token) ? npc.PreferredSpeciesId : token.Trim();

            foreach (var candidate in CropCatalog.All)
            {
                if (string.Equals(candidate.Id, text, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.DisplayName, text, StringComparison.Ordinal)
                    || MatchesAlias(text, candidate.Id))
                {
                    species = candidate;
                    reason = null;
                    return true;
                }
            }

            species = null;
            reason = $"「{text}」可种不出来，地里只能种萝卜和小麦。";
            return false;
        }

        static bool MatchesAlias(string text, string speciesId)
        {
            if (speciesId == CropCatalog.RadishId)
            {
                return text == "萝卜" || text.Equals("radish", StringComparison.OrdinalIgnoreCase);
            }

            if (speciesId == CropCatalog.WheatId)
            {
                return text == "小麦" || text == "麦子" || text.Equals("wheat", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
    }
}
