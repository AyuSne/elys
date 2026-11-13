namespace ElysFarm.Core
{
    /// <summary>
    /// 种子库的一次补货入账（见 GLOSSARY「种子库」）。跨日结算时按固定数量把种子补进
    /// <see cref="FarmLedger"/>，并留下这条流水，种子数变化因此有据可查。
    /// </summary>
    public readonly struct SeedRestock
    {
        /// <summary>补货发生时的游戏日。</summary>
        public int Day { get; }

        /// <summary>补进的品种。</summary>
        public string SpeciesId { get; }

        /// <summary>补进的数量。</summary>
        public int Count { get; }

        public SeedRestock(int day, string speciesId, int count)
        {
            Day = day;
            SpeciesId = speciesId;
            Count = count;
        }
    }
}
