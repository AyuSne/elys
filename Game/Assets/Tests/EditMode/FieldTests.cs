using ElysFarm.Core;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// 覆盖 ALIGNMENT.md 里「步骤完整」这条要求：
    /// 五个工步各自可独立触发、可独立验收。
    /// </summary>
    public class FieldTests
    {
        static Field NewField(float fertility = 0.3f, float resilience = 0.5f)
        {
            return new Field("A1", fertility, resilience);
        }

        [Test]
        public void 五个工步依次执行_地块状态按序迁移()
        {
            var field = NewField();
            var ledger = new FarmLedger();
            ledger.AddSeeds(CropCatalog.RadishId, 5);

            Assert.AreEqual(FieldState.Empty, field.State, "初始应为空地");

            // 1. 播种
            Assert.IsTrue(field.Can(WorkStep.Sow, out _));
            Assert.IsTrue(ledger.TryTakeSeeds(CropCatalog.RadishId, CropCatalog.Radish.SeedCost));
            field.Apply(WorkStep.Sow, CropCatalog.Radish);
            Assert.AreEqual(FieldState.Sown, field.State);

            // 2. 浇水
            Assert.IsTrue(field.Can(WorkStep.Water, out _));
            field.Apply(WorkStep.Water);
            Assert.AreEqual(1f, field.Water, 0.001f);

            // 3. 施肥
            Assert.IsTrue(field.Can(WorkStep.Fertilize, out _));
            field.Apply(WorkStep.Fertilize);
            Assert.AreEqual(1f, field.Fertility, 0.001f);

            // 4. 除草
            field.AdvanceDay();
            Assert.Greater(field.Weeds, 0f, "过一天应该长出杂草");
            Assert.IsTrue(field.Can(WorkStep.Weed, out _));
            field.Apply(WorkStep.Weed);
            Assert.AreEqual(0f, field.Weeds, 0.001f);

            // 5. 收获（先推进到成熟）
            for (var i = 0; i < CropCatalog.Radish.DaysToMature + 1; i++)
            {
                if (field.Can(WorkStep.Water, out _))
                {
                    field.Apply(WorkStep.Water);
                }

                field.AdvanceDay();
            }

            Assert.AreEqual(FieldState.Mature, field.State, "萝卜应在 3 天内成熟");
            Assert.IsTrue(field.Can(WorkStep.Harvest, out _));

            var harvested = field.Apply(WorkStep.Harvest);
            ledger.AddProduce(CropCatalog.RadishId, harvested);

            Assert.AreEqual(FieldState.Harvested, field.State);
            Assert.Greater(harvested, 0);
            Assert.IsTrue(ledger.Verify(out var error), error);
        }

        [Test]
        public void 不浇水时作物不会成熟()
        {
            var field = NewField();
            field.Apply(WorkStep.Sow, CropCatalog.Radish);

            for (var i = 0; i < 10; i++)
            {
                field.AdvanceDay();
            }

            Assert.AreNotEqual(FieldState.Mature, field.State, "不浇水不该成熟");
        }

        [Test]
        public void 已播种的地块不能再播种_并给出理由()
        {
            var field = NewField();
            field.Apply(WorkStep.Sow, CropCatalog.Radish);

            Assert.IsFalse(field.Can(WorkStep.Sow, out var reason));
            Assert.IsFalse(string.IsNullOrEmpty(reason), "被否决时必须给出可读理由");
        }

        [Test]
        public void 空地不能浇水_并给出理由()
        {
            var field = NewField();

            Assert.IsFalse(field.Can(WorkStep.Water, out var reason));
            Assert.AreEqual("地里什么都没种", reason);
        }

        [Test]
        public void 未成熟不能收获()
        {
            var field = NewField();
            field.Apply(WorkStep.Sow, CropCatalog.Radish);

            Assert.IsFalse(field.Can(WorkStep.Harvest, out var reason));
            Assert.AreEqual("还没熟", reason);
        }

        [Test]
        public void 未通过校验时强行执行会抛异常()
        {
            var field = NewField();
            Assert.Throws<System.InvalidOperationException>(() => field.Apply(WorkStep.Harvest));
        }
    }
}
