namespace ElysFarm.Core
{
    /// <summary>
    /// 动作白名单与前置条件校验的唯一实现处（见 ADR-0001）。
    /// 被否决时必须产出一句可读理由，该理由同时用于拒绝台词与决策可视化的否决原因。
    /// </summary>
    public static class ActionValidator
    {
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

            if (npc.Stamina < Npc.MinStamina)
            {
                reason = "太累了，得歇会儿";
                return false;
            }

            if (npc.DistanceTo(field) > Npc.MaxWorkDistance)
            {
                reason = "那块地太远了，我不去";
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
            var targetId = string.IsNullOrWhiteSpace(sowSpeciesId) ? npc.PreferredSpeciesId : sowSpeciesId;
            if (!CropCatalog.TryGet(targetId, out species))
            {
                reason = "不知道该种什么";
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

        /// <summary>种子不足时的统一可读理由。</summary>
        public static string SeedShortageReason(CropSpecies species)
        {
            return $"种子库里没有{species.DisplayName}种子了";
        }
    }
}
