using System.Collections.Generic;

namespace ElysFarm.Core
{
    /// <summary>
    /// 作物品种（见 GLOSSARY：品种决定生长时长与产出物）。
    /// 纯数据，不含行为。
    /// </summary>
    public sealed class CropSpecies
    {
        public string Id { get; set; }

        public string DisplayName { get; set; }

        /// <summary>从播种到成熟需要的游戏日数。</summary>
        public int DaysToMature { get; set; }

        /// <summary>每个游戏日自然流失的水分。</summary>
        public float WaterDrainPerDay { get; set; }

        /// <summary>每个游戏日自然长出的杂草。</summary>
        public float WeedPerDay { get; set; }

        /// <summary>一次收获的产出数量。</summary>
        public int YieldPerHarvest { get; set; }

        /// <summary>播种一次消耗的种子数量。</summary>
        public int SeedCost { get; set; }
    }

    /// <summary>
    /// 品种表。两种作物的节奏差异是观察价值的来源：
    /// 萝卜快熟用于演示闭环，小麦慢熟用于制造「有人在等、有人在忙」的错位。
    /// </summary>
    public static class CropCatalog
    {
        public const string RadishId = "radish";
        public const string WheatId = "wheat";

        /// <summary>萝卜：3 个游戏日，快，演示用。</summary>
        public static readonly CropSpecies Radish = new CropSpecies
        {
            Id = RadishId,
            DisplayName = "萝卜",
            DaysToMature = 3,
            WaterDrainPerDay = 0.30f,
            WeedPerDay = 0.15f,
            YieldPerHarvest = 3,
            SeedCost = 1
        };

        /// <summary>小麦：7 个游戏日，慢，对比用。</summary>
        public static readonly CropSpecies Wheat = new CropSpecies
        {
            Id = WheatId,
            DisplayName = "小麦",
            DaysToMature = 7,
            WaterDrainPerDay = 0.18f,
            WeedPerDay = 0.10f,
            YieldPerHarvest = 5,
            SeedCost = 2
        };

        static readonly Dictionary<string, CropSpecies> ById = new Dictionary<string, CropSpecies>
        {
            { Radish.Id, Radish },
            { Wheat.Id, Wheat }
        };

        public static IReadOnlyCollection<CropSpecies> All => ById.Values;

        public static bool TryGet(string id, out CropSpecies species)
        {
            return ById.TryGetValue(id, out species);
        }
    }
}
