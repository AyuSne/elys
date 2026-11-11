using ElysFarm.Core;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// 覆盖 ALIGNMENT.md 里「背包功能正常」这条要求。
    /// 判据不是「有背包 UI」，而是收支守恒：没有凭空出现的种子，也没有凭空消失的收成。
    /// </summary>
    public class FarmLedgerTests
    {
        [Test]
        public void 初始为空且守恒()
        {
            var ledger = new FarmLedger();
            Assert.AreEqual(0, ledger.SeedTotal);
            Assert.AreEqual(0, ledger.ProduceTotal);
            Assert.IsTrue(ledger.Verify(out var error), error);
        }

        [Test]
        public void 种子入账与消耗守恒()
        {
            var ledger = new FarmLedger();
            ledger.AddSeeds(CropCatalog.RadishId, 10);

            Assert.IsTrue(ledger.TryTakeSeeds(CropCatalog.RadishId, 3));
            Assert.AreEqual(7, ledger.SeedsOf(CropCatalog.RadishId));
            Assert.IsTrue(ledger.Verify(out var error), error);
        }

        [Test]
        public void 种子不足时拒绝且不改动账本()
        {
            var ledger = new FarmLedger();
            ledger.AddSeeds(CropCatalog.RadishId, 1);

            Assert.IsFalse(ledger.TryTakeSeeds(CropCatalog.RadishId, 5));
            Assert.AreEqual(1, ledger.SeedsOf(CropCatalog.RadishId), "失败的扣除不能改动账本");
            Assert.IsTrue(ledger.Verify(out var error), error);
        }

        [Test]
        public void 收获入库与随身移动都不破坏总量()
        {
            var ledger = new FarmLedger();
            ledger.AddProduce(CropCatalog.RadishId, 6);

            var before = ledger.ProduceTotal;

            Assert.IsTrue(ledger.TryPickUp(CropCatalog.RadishId, 4));
            Assert.AreEqual(4, ledger.HeldOf(CropCatalog.RadishId));
            Assert.AreEqual(4, ledger.HeldAdded, "取货要留下累计取入");
            Assert.AreEqual(2, ledger.ProduceOf(CropCatalog.RadishId));
            Assert.AreEqual(before, ledger.ProduceTotal, "随身只是搬家，总量不变");

            Assert.IsTrue(ledger.TryDeposit(CropCatalog.RadishId, 4));
            Assert.AreEqual(0, ledger.HeldOf(CropCatalog.RadishId));
            Assert.AreEqual(4, ledger.HeldRemoved, "交回要留下累计交回");
            Assert.AreEqual(6, ledger.ProduceOf(CropCatalog.RadishId));
            Assert.IsTrue(ledger.Verify(out var error), error);
        }

        [Test]
        public void 取走产出后仍守恒()
        {
            var ledger = new FarmLedger();
            ledger.AddProduce(CropCatalog.WheatId, 5);

            Assert.IsTrue(ledger.TryTakeProduce(CropCatalog.WheatId, 2));
            Assert.AreEqual(3, ledger.ProduceTotal);
            Assert.IsTrue(ledger.Verify(out var error), error);
        }

        [Test]
        public void 负数量与空品种被拒绝()
        {
            var ledger = new FarmLedger();
            Assert.Throws<System.ArgumentOutOfRangeException>(() => ledger.AddSeeds(CropCatalog.RadishId, 0));
            Assert.Throws<System.ArgumentException>(() => ledger.AddSeeds("  ", 1));
        }
    }
}
