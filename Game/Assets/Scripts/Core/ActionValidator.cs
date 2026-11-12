using System;
using System.Collections.Generic;

namespace ElysFarm.Core
{
    /// <summary>
    /// 动作白名单与前置条件校验的唯一实现处（见 ADR-0001）。
    /// 被否决时必须产出一句可读理由，该理由同时用于拒绝台词与决策可视化的否决原因。
    /// 合法目标（Goal）到工步序列的映射也放在这里，避免提示词、模型解析与路由器各存一份。
    /// </summary>
    public static class ActionValidator
    {
        static readonly WorkStep[] PlantSteps =
        {
            WorkStep.Sow,
            WorkStep.Water,
            WorkStep.Fertilize,
            WorkStep.Weed,
            WorkStep.Harvest
        };

        static readonly WorkStep[] WaterSteps = { WorkStep.Water };
        static readonly WorkStep[] FertilizeSteps = { WorkStep.Fertilize };
        static readonly WorkStep[] WeedSteps = { WorkStep.Weed };
        static readonly WorkStep[] HarvestSteps = { WorkStep.Harvest };

        /// <summary>合法目标的 wire token，按提示词 schema 的顺序排列。</summary>
        internal static readonly string[] GoalTokens = { "plant", "water", "fertilize", "weed", "harvest" };

        /// <summary>校验工步此刻是否可执行；不可执行时必须给出可读理由。</summary>
        public static bool Validate(FarmWorld world, Npc npc, Field field, WorkStep step, out string reason)
        {
            return Validate(world, npc, field, step, null, out reason);
        }

        /// <summary>
        /// 校验工步此刻是否可执行，允许为播种指定本次要用的品种（玩家点名品种时）。
        /// 不可执行时必须给出可读理由。
        /// </summary>
        public static bool Validate(
            FarmWorld world,
            Npc npc,
            Field field,
            WorkStep step,
            string sowSpeciesId,
            out string reason)
        {
            if (!field.Can(step, out reason))
            {
                return false;
            }

            if (npc.IsResting)
            {
                reason = Npc.RestingReason;
                return false;
            }

            if (npc.Stamina < Npc.MinStamina)
            {
                reason = Npc.TooTiredReason;
                return false;
            }

            if (step == WorkStep.Sow)
            {
                return TryResolveSow(world, npc, sowSpeciesId, out _, out reason);
            }

            reason = null;
            return true;
        }

        /// <summary>播种所需的品种与种子校验；通过时给出要播的品种。</summary>
        public static bool TryResolveSow(FarmWorld world, Npc npc, out CropSpecies species, out string reason)
        {
            return TryResolveSow(world, npc, null, out species, out reason);
        }

        /// <summary>
        /// 播种所需的品种与种子校验。指定品种优先于 NPC 的当前打算；
        /// 未知品种与种子不足都给出可读理由。
        /// </summary>
        public static bool TryResolveSow(
            FarmWorld world,
            Npc npc,
            string sowSpeciesId,
            out CropSpecies species,
            out string reason)
        {
            if (!TryResolveSpecies(sowSpeciesId, npc.PreferredSpeciesId, out species, out reason))
            {
                return false;
            }

            if (world.Ledger.SeedsOf(species.Id) < species.SeedCost)
            {
                reason = SeedShortageReason(species);
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// 品种白名单的唯一实现处：把 id、展示名或别名解析成品种。
        /// 未知品种给出一句可读理由，所有调用方共用这同一句话。
        /// </summary>
        public static bool TryResolveSpecies(
            string token,
            string defaultSpeciesId,
            out CropSpecies species,
            out string reason)
        {
            var text = string.IsNullOrWhiteSpace(token) ? defaultSpeciesId : token.Trim();

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
            reason = UnknownSpeciesReason(text);
            return false;
        }

        /// <summary>品种不在白名单时的统一可读理由。品种名从 <see cref="CropCatalog"/> 生成，新增品种不会漏。</summary>
        public static string UnknownSpeciesReason(string token)
        {
            var text = string.IsNullOrWhiteSpace(token) ? "这个" : token.Trim();
            var allowed = new System.Text.StringBuilder();
            foreach (var species in CropCatalog.All)
            {
                if (allowed.Length > 0)
                {
                    allowed.Append('、');
                }

                allowed.Append(species.DisplayName);
            }

            return $"「{text}」可种不出来，地里只能种{allowed}。";
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

        /// <summary>把模型返回的 wire token 解析成合法目标；白名单之外返回 false。</summary>
        public static bool TryParseGoal(string token, out FarmGoal goal)
        {
            switch (token)
            {
                case "plant":
                case "sow":
                    goal = FarmGoal.Plant;
                    return true;
                case "water":
                    goal = FarmGoal.Water;
                    return true;
                case "fertilize":
                    goal = FarmGoal.Fertilize;
                    return true;
                case "weed":
                    goal = FarmGoal.Weed;
                    return true;
                case "harvest":
                    goal = FarmGoal.Harvest;
                    return true;
                default:
                    goal = default;
                    return false;
            }
        }

        /// <summary>合法目标对应的工步序列；所有调用方共用这一份计划。</summary>
        public static bool TryGetPlan(FarmGoal goal, out IReadOnlyList<WorkStep> steps)
        {
            switch (goal)
            {
                case FarmGoal.Plant:
                    steps = PlantSteps;
                    return true;
                case FarmGoal.Water:
                    steps = WaterSteps;
                    return true;
                case FarmGoal.Fertilize:
                    steps = FertilizeSteps;
                    return true;
                case FarmGoal.Weed:
                    steps = WeedSteps;
                    return true;
                case FarmGoal.Harvest:
                    steps = HarvestSteps;
                    return true;
                default:
                    steps = null;
                    return false;
            }
        }

        /// <summary>种子不足时的统一可读理由。</summary>
        public static string SeedShortageReason(CropSpecies species)
        {
            return $"种子库里没有{species.DisplayName}种子了";
        }
    }
}
