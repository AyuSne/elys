using System;
using System.Collections.Generic;

namespace ElysFarm.Core
{
    /// <summary>
    /// 背包的三本账（见 GLOSSARY）：种子库、产出仓、随身。
    /// 「背包功能正常」真正要证明的是收支守恒 —— 没有凭空出现的种子，
    /// 也没有凭空中饱私囊的收成，所以这里同时记录累计进出并暴露 <see cref="Verify"/>。
    /// </summary>
    public sealed class FarmLedger
    {
        readonly Dictionary<string, int> seeds = new Dictionary<string, int>();
        readonly Dictionary<string, int> produce = new Dictionary<string, int>();
        readonly Dictionary<string, int> carried = new Dictionary<string, int>();

        /// <summary>累计入账的种子数。</summary>
        public int SeedsAdded { get; private set; }

        /// <summary>累计被播种消耗的种子数。</summary>
        public int SeedsRemoved { get; private set; }

        /// <summary>累计入库的产出数。</summary>
        public int ProduceAdded { get; private set; }

        /// <summary>累计被取走的产出数。</summary>
        public int ProduceRemoved { get; private set; }

        /// <summary>种子库当前总数。</summary>
        public int SeedTotal => Sum(seeds);

        /// <summary>产出总量（产出仓 + NPC 随身）。</summary>
        public int ProduceTotal => Sum(produce) + Sum(carried);

        /// <summary>产出仓当前总数（不含随身）。</summary>
        public int StoredProduce => Sum(produce);

        /// <summary>NPC 随身当前总数。</summary>
        public int CarriedTotal => Sum(carried);

        public int SeedsOf(string speciesId) => Get(seeds, speciesId);

        public int ProduceOf(string speciesId) => Get(produce, speciesId);

        public int CarriedOf(string speciesId) => Get(carried, speciesId);

        /// <summary>往种子库入账。</summary>
        public void AddSeeds(string speciesId, int count)
        {
            Require(speciesId, count);
            seeds[speciesId] = Get(seeds, speciesId) + count;
            SeedsAdded += count;
        }

        /// <summary>从种子库扣除；不足则返回 false 且不做任何改动。</summary>
        public bool TryTakeSeeds(string speciesId, int count)
        {
            Require(speciesId, count);

            if (Get(seeds, speciesId) < count)
            {
                return false;
            }

            seeds[speciesId] = Get(seeds, speciesId) - count;
            SeedsRemoved += count;
            return true;
        }

        /// <summary>把收获物放入产出仓。</summary>
        public void AddProduce(string speciesId, int count)
        {
            Require(speciesId, count);
            produce[speciesId] = Get(produce, speciesId) + count;
            ProduceAdded += count;
        }

        /// <summary>NPC 从产出仓拿走，转为随身（总数不变）。</summary>
        public bool TryCarry(string speciesId, int count)
        {
            Require(speciesId, count);

            if (Get(produce, speciesId) < count)
            {
                return false;
            }

            produce[speciesId] = Get(produce, speciesId) - count;
            carried[speciesId] = Get(carried, speciesId) + count;
            return true;
        }

        /// <summary>NPC 把随身物品交回产出仓（总数不变）。</summary>
        public bool TryDeposit(string speciesId, int count)
        {
            Require(speciesId, count);

            if (Get(carried, speciesId) < count)
            {
                return false;
            }

            carried[speciesId] = Get(carried, speciesId) - count;
            produce[speciesId] = Get(produce, speciesId) + count;
            return true;
        }

        /// <summary>从产出仓取走（例如玩家收走）。</summary>
        public bool TryTakeProduce(string speciesId, int count)
        {
            Require(speciesId, count);

            if (Get(produce, speciesId) < count)
            {
                return false;
            }

            produce[speciesId] = Get(produce, speciesId) - count;
            ProduceRemoved += count;
            return true;
        }

        /// <summary>
        /// 校验收支守恒。种子与产出各自满足「入账 - 出账 = 当前持有」，
        /// 且任何一本账都不允许出现负数。
        /// </summary>
        public bool Verify(out string error)
        {
            foreach (var pair in seeds)
            {
                if (pair.Value < 0)
                {
                    error = $"种子库出现负数：{pair.Key} = {pair.Value}";
                    return false;
                }
            }

            foreach (var pair in produce)
            {
                if (pair.Value < 0)
                {
                    error = $"产出仓出现负数：{pair.Key} = {pair.Value}";
                    return false;
                }
            }

            foreach (var pair in carried)
            {
                if (pair.Value < 0)
                {
                    error = $"随身出现负数：{pair.Key} = {pair.Value}";
                    return false;
                }
            }

            var expectedSeeds = SeedsAdded - SeedsRemoved;
            if (SeedTotal != expectedSeeds)
            {
                error = $"种子不守恒：入账 {SeedsAdded} - 消耗 {SeedsRemoved} = {expectedSeeds}，实际持有 {SeedTotal}";
                return false;
            }

            var expectedProduce = ProduceAdded - ProduceRemoved;
            if (ProduceTotal != expectedProduce)
            {
                error = $"产出不守恒：入库 {ProduceAdded} - 取走 {ProduceRemoved} = {expectedProduce}，实际持有 {ProduceTotal}";
                return false;
            }

            error = null;
            return true;
        }

        static void Require(string speciesId, int count)
        {
            if (string.IsNullOrWhiteSpace(speciesId))
            {
                throw new ArgumentException("必须指定品种 id", nameof(speciesId));
            }

            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "数量必须为正");
            }
        }

        static int Get(Dictionary<string, int> ledger, string speciesId)
        {
            return ledger.TryGetValue(speciesId, out var value) ? value : 0;
        }

        static int Sum(Dictionary<string, int> ledger)
        {
            var total = 0;
            foreach (var pair in ledger)
            {
                total += pair.Value;
            }

            return total;
        }
    }
}
