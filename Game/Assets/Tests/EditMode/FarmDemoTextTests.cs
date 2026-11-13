using ElysFarm.Core;
using ElysFarm.Demo;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// 演示层文案的纯逻辑接缝：三人卡那一行的「当前意图」在有工单时必须与决策抽屉同一句话，
    /// 这样「等条件 / 跳过 / 没做成」不会在两张界面上各说一套（#23）。
    /// 不打开场景、不测渲染，和 <see cref="PixelArtTests"/>、<see cref="FarmLayoutTests"/> 一样只调纯函数。
    /// </summary>
    public class FarmDemoTextTests
    {
        static FarmWorld NewWorld()
        {
            var world = new FarmWorld(new FarmClock());
            world.AddField("A1", new FarmPosition(0f, 2f));
            world.AddNpc("ahe", "阿禾", new FarmPosition(0f, -4f));
            world.AddSeeds(CropCatalog.RadishId, 10);
            return world;
        }

        [Test]
        public void 有工单时三人卡与抽屉显示同一句()
        {
            var world = NewWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Fertilize).Executed, "先把肥力加满，制造等条件");
            Assert.IsTrue(
                world.TryAssignWorkOrder(
                    "ahe",
                    "A1",
                    null,
                    new[] { WorkStep.Water, WorkStep.Fertilize },
                    out _,
                    out var reason),
                reason);
            Assert.IsTrue(world.AdvanceWorkOrder("ahe").Executed, "先浇水，下一步轮到施肥");

            var card = FarmDemoText.CardIntent(world, world.GetNpc("ahe"), "前往 A1 · 施肥");

            Assert.AreEqual(world.Inspect("ahe").Intent, card, "卡片与抽屉必须是同一句话");
            StringAssert.StartsWith("等 ", card, "等条件在卡片上也说「等…」");
            StringAssert.DoesNotContain("没做成", card);
        }

        [Test]
        public void 没有工单时三人卡显示演示层的临时状态()
        {
            var world = NewWorld();

            Assert.AreEqual(
                "待命，等玩家开口",
                FarmDemoText.CardIntent(world, world.GetNpc("ahe"), "待命，等玩家开口"),
                "没有工单时卡片仍是演示层自己的说法");
        }

        [Test]
        public void 三人卡文案按可用宽度截短而不是按字数()
        {
            // 假字体：每个字符 12 像素宽。宽度够就原样显示，不够就截到放得下的地方再加省略号。
            float Measure(string text)
            {
                return text.Length * 12f;
            }

            Assert.AreEqual("等 A1 · 除草（没什么草可除）", FarmDemoText.Fit("等 A1 · 除草（没什么草可除）", 12f * 17f, Measure), "放得下就原样显示");
            Assert.AreEqual("等 A1 · 除…", FarmDemoText.Fit("等 A1 · 除草（没什么草可除）", 12f * 9f, Measure), "截短要留省略号");
            Assert.AreEqual(string.Empty, FarmDemoText.Fit(string.Empty, 100f, Measure));
        }
    }
}
