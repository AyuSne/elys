using System;
using System.Collections.Generic;

namespace ElysFarm.Core
{
    /// <summary>
    /// 背包的三本账（见 GLOSSARY）：种子库、产出仓、随身。
    /// 「背包功能正常」真正要证明的是收支守恒 —— 没有凭空出现的种子，
    /// 也没有凭空中饱私囊的收成，所以三本账都记录累计进出并暴露 <see cref="Verify"/>。
    /// </summary>
    public sealed class FarmLedger
    {
        readonly Dictionary<string, int> seeds = new Dictionary<string, int>();
        readonly Dictionary<string, int> produce = new Dictionary<string, int>();
        readonly Dictionary<string, int> held = new Dictionary<string, int>();

        /// <summary>累计入账的种子数。</summary>
        public int SeedsAdded { get; private set; }

        /// <summary>累计被播种消耗的种子数。</summary>
        public int SeedsRemoved { get; private set; }

        /// <summary>累计入库的产出数。</summary>
        public int ProduceAdded { get; private set; }

        /// <summary>累计被取走的产出数。</summary>
        public int ProduceRemoved { get; private set; }

        /// <summary>累计取到随身的产出数。</summary>
        public int HeldAdded { get; private set; }

        /// <summary>累计从随身交回产出仓的产出数。</summary>
        public int HeldRemoved { get; private set; }

        /// <summary>种子库当前总数。</summary>
        public int SeedTotal => Sum(seeds);

        /// <summary>产出总量（产出仓 + NPC 随身）。</summary>
        public int ProduceTotal => Sum(produce) + Sum(held);

        /// <summary>产出仓当前总数（不含随身）。</summary>
        public int StoredProduce => Sum(produce);

        /// <summary>NPC 随身当前总数。</summary>
        public int HeldTotal => Sum(held);

        /// <summary>某种子库中某品种的当前数量。</summary>
        public int SeedsOf(string speciesId) => Get(seeds, speciesId);

        /// <summary>产出仓中某品种的当前数量。</summary>
        public int ProduceOf(string speciesId) => Get(produce, speciesId);

        /// <summary>随身中某品种的当前数量。</summary>
        public int HeldOf(string speciesId) => Get(held, speciesId);

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

        /// <summary>NPC 从产出仓取货到随身（总量不变）。</summary>
        public bool TryPickUp(string speciesId, int count)
        {
            Require(speciesId, count);

            if (Get(produce, speciesId) < count)
            {
                return false;
            }

            produce[speciesId] = Get(produce, speciesId) - count;
            held[speciesId] = Get(held, speciesId) + count;
            HeldAdded += count;
            return true;
        }

        /// <summary>NPC 把随身物品交回产出仓（总量不变）。</summary>
        public bool TryDeposit(string speciesId, int count)
        {
            Require(speciesId, count);

            if (Get(held, speciesId) < count)
            {
                return false;
            }

            held[speciesId] = Get(held, speciesId) - count;
            produce[speciesId] = Get(produce, speciesId) + count;
            HeldRemoved += count;
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
        /// 校验三本账各自的收支守恒，并保证任何一本账都不出现负数。
        /// 随身是产出仓与 NPC 之间的搬运，因此产出仓的期望值要减去当前随身。
        /// </summary>
        public bool Verify(out string error)
        {
            if (!VerifyNoNegatives(seeds, "种子库", out error)
                || !VerifyNoNegatives(produce, "产出仓", out error)
                || !VerifyNoNegatives(held, "随身", out error))
            {
                return false;
            }

            var expectedSeeds = SeedsAdded - SeedsRemoved;
            if (SeedTotal != expectedSeeds)
            {
                error = $"种子库不守恒：入账 {SeedsAdded} - 消耗 {SeedsRemoved} = {expectedSeeds}，实际持有 {SeedTotal}";
                return false;
            }

            var expectedHeld = HeldAdded - HeldRemoved;
            if (HeldTotal != expectedHeld)
            {
                error = $"随身不守恒：取入 {HeldAdded} - 交回 {HeldRemoved} = {expectedHeld}，实际持有 {HeldTotal}";
                return false;
            }

            var expectedStored = ProduceAdded - ProduceRemoved - HeldTotal;
            if (StoredProduce != expectedStored)
            {
                error = $"产出仓不守恒：入库 {ProduceAdded} - 取走 {ProduceRemoved} - 随身 {HeldTotal} = {expectedStored}，实际持有 {StoredProduce}";
                return false;
            }

            var expectedProduce = ProduceAdded - ProduceRemoved;
            if (ProduceTotal != expectedProduce)
            {
                error = $"产出总量不守恒：入库 {ProduceAdded} - 取走 {ProduceRemoved} = {expectedProduce}，实际持有 {ProduceTotal}（产出仓 {StoredProduce} + 随身 {HeldTotal}）";
                return false;
            }

            error = null;
            return true;
        }

        static bool VerifyNoNegatives(Dictionary<string, int> ledger, string name, out string error)
        {
            foreach (var pair in ledger)
            {
                if (pair.Value < 0)
                {
                    error = $"{name}出现负数：{pair.Key} = {pair.Value}";
                    return false;
                }
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
