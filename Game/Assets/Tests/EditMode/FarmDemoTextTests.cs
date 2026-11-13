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
            StringAssert.StartsWith("等肥力降下来", card, "等条件在卡片上也说「等…」");
            StringAssert.DoesNotContain("没做成", card);
        }

        [Test]
        public void 休息时三人卡与抽屉也显示同一句()
        {
            // 干两步就累垮的人设：第二刀之后进休息，意图行照旧只认领域那一句。
            var fragile = new NpcPersona(
                "fragile",
                decisionSeconds: 0f,
                exertionPerStep: 0.45f,
                restThreshold: Npc.MinStamina,
                restSeconds: 60f,
                restRecovery: 0.60f,
                diligence: 1f,
                distanceWeight: 0.15f);
            var world = new FarmWorld(new FarmClock());
            world.AddField("A1", new FarmPosition(0f, 2f));
            world.AddNpc("ahe", "阿禾", new FarmPosition(0f, -4f), fragile);
            world.AddSeeds(CropCatalog.RadishId, 10);
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Water).Executed);
            var npc = world.GetNpc("ahe");
            Assert.IsTrue(npc.IsResting, "体力降到阈值以下应去休息");

            var card = FarmDemoText.CardIntent(world, npc, "歇着（体力 10%，还剩 60 秒）");

            StringAssert.StartsWith("休息中", card, "休息也读领域那一句");
            Assert.AreEqual(world.Inspect("ahe").Intent, card, "卡片与抽屉必须是同一句话");
        }

        [Test]
        public void 没有工单时三人卡显示演示层的临时状态()
        {
            var world = NewWorld();

            Assert.AreEqual(
                FarmDemoText.WaitingIntent,
                FarmDemoText.CardIntent(world, world.GetNpc("ahe"), FarmDemoText.WaitingIntent),
                "没有工单时卡片仍是演示层自己的说法");
        }

        [Test]
        public void 自主开着又没活时卡片与抽屉都从待命说起()
        {
            // 空农场：一块地也没有，阿禾拿主意时确实没活干（默认人设决定间隔为 0）。
            var world = new FarmWorld(new FarmClock());
            world.AddNpc("ahe", "阿禾", new FarmPosition(0f, -4f));
            Assert.IsFalse(world.TryDecide("ahe", out _, out var reason), "没有地块，确实没活干");

            var card = FarmDemoText.NoWorkIntent(reason);

            Assert.AreEqual("待命（现在没有该干的活）", card, "卡片先说「待命」，再接领域给的理由");
            Assert.AreEqual("待命", world.IntentOf("ahe"), "抽屉第 ② 项说的仍是领域的「待命」");
            StringAssert.DoesNotContain("没活干", card, "不再一边说「没活干」一边说「待命」");
            Assert.AreEqual("待命", FarmDemoText.NoWorkIntent(null), "拿不到理由时只说「待命」，不画一对空括号");
        }

        [Test]
        public void 自主关着时卡片与抽屉也同词根()
        {
            var world = new FarmWorld(new FarmClock());
            world.AddNpc("ahe", "阿禾", new FarmPosition(0f, -4f));

            Assert.AreEqual(
                "待命",
                world.IntentOf("ahe", includeAutonomousIntent: false),
                "暂停时抽屉只给「待命」");
            StringAssert.StartsWith("待命", FarmDemoText.WaitingIntent, "卡片也从「待命」说起");
        }

        [Test]
        public void 点取消的提示行记点了取消_不伪造玩家台词()
        {
            // #30：三人卡的「取消」按钮不是玩家说的那句话。提示行记的是痕迹「（点了取消）」，
            // 所以对话里不会再冒出一句玩家没打过的「别干了」；路由仍用取消语句（见 FarmDemoBootstrap）。
            float Measure(string text)
            {
                return text.Length * 12f;
            }

            const string understood = "取消：A1 的工单作废（做过的工步不回滚）";
            var line = FarmDemoText.HintLine(FarmDemoText.CancelTrace, understood, 2000f, Measure);

            // 「你：… · 理解成…」的分隔符沿用 #25 的格式（两侧各一个空格），这里换的是前半句。
            Assert.AreEqual("你：（点了取消） · 取消：A1 的工单作废（做过的工步不回滚）", line);
            StringAssert.DoesNotContain("别干了", line, "按钮不能伪造玩家台词");
        }

        [Test]
        public void 打字取消记的还是玩家原话()
        {
            // 同一行在打字取消时完全不变：记的就是玩家自己打的那句话（#30 的回归护栏）。
            float Measure(string text)
            {
                return text.Length * 12f;
            }

            const string understood = "取消：A1 的工单作废（做过的工步不回滚）";
            var line = FarmDemoText.HintLine("别干了", understood, 2000f, Measure);

            Assert.AreEqual("你：别干了 · 取消：A1 的工单作废（做过的工步不回滚）", line);
            StringAssert.DoesNotContain("（点了取消）", line);
        }

        [Test]
        public void 种子库补货流水按游戏日合成一行()
        {
            var restocks = new[]
            {
                new SeedRestock(2, CropCatalog.RadishId, 2),
                new SeedRestock(2, CropCatalog.WheatId, 1),
                new SeedRestock(3, CropCatalog.RadishId, 2),
                new SeedRestock(3, CropCatalog.WheatId, 1)
            };

            var latest = FarmDemoText.RestockLines(restocks, 1);
            Assert.AreEqual(1, latest.Count, "只显示最近一个游戏日");
            Assert.AreEqual("第 3 天 · 种子库补货 · 萝卜 +2 · 小麦 +1", latest[0]);

            CollectionAssert.AreEqual(
                new[]
                {
                    "第 2 天 · 种子库补货 · 萝卜 +2 · 小麦 +1",
                    "第 3 天 · 种子库补货 · 萝卜 +2 · 小麦 +1"
                },
                FarmDemoText.RestockLines(restocks, 2),
                "同一游戏日的多条补货合成一行，游戏日从早到晚");

            Assert.IsEmpty(FarmDemoText.RestockLines(null, 2));
            Assert.IsEmpty(FarmDemoText.RestockLines(new SeedRestock[0], 2), "还没跨日时没有补货流水");
        }

        [Test]
        public void 种子库补货累计行用来对账()
        {
            var restocks = new[]
            {
                new SeedRestock(2, CropCatalog.RadishId, 2),
                new SeedRestock(2, CropCatalog.WheatId, 1),
                new SeedRestock(3, CropCatalog.RadishId, 2),
                new SeedRestock(3, CropCatalog.WheatId, 1)
            };

            Assert.AreEqual(
                "种子库补货累计 · 萝卜 +4 · 小麦 +2",
                FarmDemoText.RestockTotalLine(restocks),
                "累计行要把几天、几种补货合成一句，结余才对得上");

            Assert.AreEqual(string.Empty, FarmDemoText.RestockTotalLine(null));
            Assert.AreEqual(string.Empty, FarmDemoText.RestockTotalLine(new SeedRestock[0]));
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

        [Test]
        public void 对话框回复最多一百六十字_一百六十字内不截断()
        {
            var exact = new string('字', 160);

            Assert.AreEqual(exact, FarmDemoText.DialogueReply(exact), "160 字要原样显示");
            Assert.AreEqual(string.Empty, FarmDemoText.DialogueReply(null));

            var over = FarmDemoText.DialogueReply(new string('字', 161));
            Assert.AreEqual(160, over.Length, "超过 160 字才截，截完仍不超过 160");
            StringAssert.EndsWith("…", over, "截短要留省略号");
        }

        [Test]
        public void 长句只执行一个工步时说明按哪个工步理解()
        {
            const string longSentence = "麻烦你把中间那块地里的萝卜都收了吧谢谢你";
            const string shortSentence = "把中间那块地收了";
            var singleStep = new[] { WorkStep.Harvest };
            var fiveSteps = new[]
            {
                WorkStep.Sow, WorkStep.Water, WorkStep.Fertilize, WorkStep.Weed, WorkStep.Harvest
            };

            Assert.IsTrue(FarmDemoText.IsLongSentence(longSentence), "20 字以上算长句");
            Assert.IsFalse(FarmDemoText.IsLongSentence(shortSentence));
            Assert.AreEqual(
                "理解成：A2 · 收获 · 我按收获理解",
                FarmDemoText.Plan("A2", null, singleStep, FarmDemoText.IsLongSentence(longSentence)),
                "长句只排一个工步时，要说明实际理解成哪个工步");
            Assert.AreEqual(
                "理解成：A2 · 收获",
                FarmDemoText.Plan("A2", null, singleStep, FarmDemoText.IsLongSentence(shortSentence)),
                "短句不加大白话注脚");
            Assert.AreEqual(
                "理解成：A2 · 萝卜 · 播种→浇水→施肥→除草→收获",
                FarmDemoText.Plan(
                    "A2",
                    CropCatalog.RadishId,
                    fiveSteps,
                    FarmDemoText.IsLongSentence(longSentence)),
                "整块地的多步计划不是「只执行一个工步」，不加注脚");
        }

        [Test]
        public void 理解成一行先缩短玩家原话_不截掉理解成()
        {
            float Measure(string text)
            {
                return text.Length * 12f;
            }

            const string understood = "理解成：A2 · 萝卜 · 播种→浇水→施肥→除草→收获";
            var line = FarmDemoText.HintLine(
                "麻烦你把中间那块地里的萝卜都收了再给东边那块地浇水谢谢你",
                understood,
                600f,
                Measure);

            Assert.LessOrEqual(Measure(line), 600f, "整行要放得下");
            StringAssert.StartsWith("你：", line);
            StringAssert.Contains(understood, line, "「理解成」部分不能被截掉");
            Assert.LessOrEqual(line.Length, 50, "被截短的是玩家原话，不是理解成");
        }

        [Test]
        public void 空输入回车给一行轻提示()
        {
            Assert.AreEqual(
                "先说点什么吧，比如「去东边那块地除草」。",
                FarmDemoText.EmptyInputHintFor(string.Empty));
            Assert.AreEqual(
                "先说点什么吧，比如「去东边那块地除草」。",
                FarmDemoText.EmptyInputHintFor("   "),
                "只有空格也算空输入");
            Assert.IsNull(FarmDemoText.EmptyInputHintFor("去东边那块地除草"), "有话要说时不该顶掉正常提示");
        }
    }
}
