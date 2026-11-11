namespace ElysFarm.Core
{
    /// <summary>
    /// 动作白名单与前置条件校验的唯一实现处（见 ADR-0001）。
    /// 被否决时必须产出一句可读理由，该理由同时用于拒绝台词与决策可视化的否决原因。
    /// </summary>
    public static class ActionValidator
    {
        public static bool Validate(FarmWorld world, Npc npc, Field field, WorkStep step, out string reason)
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

            if (step == WorkStep.Sow)
            {
                return TryResolveSow(world, npc, out _, out reason);
            }

            reason = null;
            return true;
        }

        /// <summary>播种所需的品种与种子校验；通过时给出要播的品种。</summary>
        public static bool TryResolveSow(FarmWorld world, Npc npc, out CropSpecies species, out string reason)
        {
            if (!CropCatalog.TryGet(npc.PreferredSpeciesId, out species))
            {
                reason = "不知道该种什么";
                return false;
            }

            if (world.Ledger.SeedsOf(species.Id) < species.SeedCost)
            {
                reason = $"种子库里没有{species.DisplayName}种子了";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
