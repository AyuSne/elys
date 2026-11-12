using ElysFarm.Core;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #8 传闻与转述的领域接缝测试：只打 <see cref="FarmWorld"/> 这条缝。
    /// 断言的是规格里的一条信息能从 A 传到 B 再传到 C，以及转述只写记忆、
    /// 不改变地块、时钟与背包。传闻的来源、说法与转手次数都通过公开结果核对。
    /// </summary>
    public class SocialTests
    {
        const int SeedCount = 10;

        static FarmWorld NewWorld()
        {
            var world = new FarmWorld(new FarmClock());
            world.AddField("A1", new FarmPosition(0f, 2f));
            world.AddField("A2", new FarmPosition(4f, 2f));
            world.AddNpc("ahe", "阿禾", new FarmPosition(0f, 0f), NpcPersona.Ahe);
            world.AddNpc("aya", "阿芽", new FarmPosition(0.6f, 0f), NpcPersona.Aya);
            world.AddNpc("atu", "阿土", new FarmPosition(1.2f, 0f), NpcPersona.Atu);
            world.AddNpc("amiao", "阿苗", new FarmPosition(1.8f, 0f));
            world.SetProduceStore(new FarmPosition(0f, -1f));
            world.AddSeeds(CropCatalog.RadishId, SeedCount);
            return world;
        }

        [Test]
        public void 玩家说的别人闲话会被听的人转述出去()
        {
            var world = NewWorld();
            world.RememberChat("atu", "阿芽今天偷懒了", 4);

            Assert.IsTrue(world.TryShareNews("atu", "ahe", out var told), "两个人站得近，可以聊两句");
            Assert.AreEqual("aya", told.SubjectId);
            StringAssert.Contains("偷懒", told.Claim);
            Assert.AreEqual(1, told.Hops);

            var heard = world.RetrieveMemories("ahe", "阿芽偷懒");
            Assert.Greater(heard.Count, 0, "听来的事要写进记忆");
            StringAssert.Contains("听阿土说", heard[0].Text, "记忆里要说清是谁讲的");
            StringAssert.Contains("阿芽", heard[0].Text);
            StringAssert.Contains("偷懒", heard[0].Text);
            Assert.AreEqual(3, heard[0].Importance, "转述比原始闲聊低一级");
        }

        [Test]
        public void 一条传闻能从阿土传到阿禾再传到阿苗()
        {
            var world = NewWorld();
            world.RememberChat("atu", "阿芽今天偷懒了", 4);

            Assert.IsTrue(world.TryShareNews("atu", "ahe", out _));
            Assert.IsTrue(world.TryShareNews("ahe", "amiao", out var second), "听来的事还能再讲给第三个人");

            Assert.AreEqual("aya", second.SubjectId, "转述不能把说的是谁弄丢");
            Assert.AreEqual(2, second.Hops, "到阿苗这里是第二手");

            var heard = world.RetrieveMemories("amiao", "阿芽偷懒");
            Assert.Greater(heard.Count, 0, "第三个人也要记得这件事");
            StringAssert.Contains("听阿禾说", heard[0].Text, "来源换成当下讲的人");
            StringAssert.Contains("阿芽", heard[0].Text);
            StringAssert.Contains("偷懒", heard[0].Text);
            Assert.AreEqual(2, heard[0].Importance, "每转一手，重要性递减");
        }

        [Test]
        public void 隔着老远的两名NPC说不上话()
        {
            var world = NewWorld();
            world.RememberChat("atu", "阿芽今天偷懒了", 4);
            world.MoveNpc("ahe", new FarmPosition(60f, 0f));

            Assert.IsFalse(world.TryShareNews("atu", "ahe", out _, out var reason));
            StringAssert.Contains("远", reason);
            Assert.AreEqual(0, world.RetrieveMemories("ahe", "阿芽偷懒").Count, "没说到就没听来");
        }

        [Test]
        public void 已经听过的传闻不会被重复转述()
        {
            var world = NewWorld();
            world.RememberChat("atu", "阿芽今天偷懒了", 4);
            Assert.IsTrue(world.TryShareNews("atu", "ahe", out _));

            Assert.IsFalse(world.TryShareNews("atu", "ahe", out _, out var reason), "同一句话没必要说第二遍");
            Assert.IsFalse(string.IsNullOrWhiteSpace(reason));
            Assert.AreEqual(1, world.RetrieveMemories("ahe", "阿芽偷懒").Count, "记忆里只留一条");

            Assert.IsFalse(world.TryShareNews("ahe", "atu", out _, out _), "听过的人不会再讲回给讲的人");
        }

        [Test]
        public void 没听说过别人的事就没得转述()
        {
            var world = NewWorld();
            world.RememberChat("atu", "今天天气不错", 4);

            Assert.IsFalse(world.TryShareNews("atu", "ahe", out _, out var reason));
            Assert.IsFalse(string.IsNullOrWhiteSpace(reason), "没什么可说时也要给一句可读的理由");
        }

        [Test]
        public void 自己跟自己转述不了()
        {
            var world = NewWorld();
            world.RememberChat("atu", "阿芽今天偷懒了", 4);

            Assert.IsFalse(world.TryShareNews("atu", "atu", out _, out _));
        }

        [Test]
        public void 把传闻讲到当事人自己时记忆改用第二人称()
        {
            var world = NewWorld();
            world.RememberChat("atu", "阿芽今天偷懒了", 4);

            Assert.IsTrue(world.TryShareNews("atu", "aya", out _));

            var heard = world.RetrieveMemories("aya", "阿芽偷懒");
            Assert.Greater(heard.Count, 0);
            StringAssert.Contains("听阿土说，你今天偷懒了", heard[0].Text, "讲到听者自己时应说「你」");
        }

        [Test]
        public void 转述只写记忆不改变地块时钟与背包()
        {
            var world = NewWorld();
            Assert.IsTrue(world.ExecuteStep("atu", "A1", WorkStep.Sow).Executed);
            world.AdvanceDays(1);

            var day = world.Clock.Day;
            var field = world.GetField("A1");
            var water = field.Water;
            var growth = field.Growth;
            var weeds = field.Weeds;
            var seeds = world.Ledger.SeedsOf(CropCatalog.RadishId);
            var logCount = world.Log.Count;

            world.RememberChat("atu", "阿芽今天偷懒了", 4);
            Assert.IsTrue(world.TryShareNews("atu", "ahe", out _));
            Assert.IsTrue(world.TryShareNews("ahe", "amiao", out _));

            Assert.AreEqual(day, world.Clock.Day, "转述不能推进时间");
            Assert.AreEqual(water, field.Water, 0.0001f, "转述不能改地块水分");
            Assert.AreEqual(growth, field.Growth, 0.0001f, "转述不能改生长进度");
            Assert.AreEqual(weeds, field.Weeds, 0.0001f, "转述不能改杂草");
            Assert.AreEqual(seeds, world.Ledger.SeedsOf(CropCatalog.RadishId), "转述不能动种子库");
            Assert.AreEqual(logCount, world.Log.Count, "转述不能留下工步流水");
            Assert.IsNull(world.WorkOrderOf("atu"), "转述不能派工单");
        }
    }
}
