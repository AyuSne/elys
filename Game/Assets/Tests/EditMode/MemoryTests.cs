using System.Linq;
using ElysFarm.Core;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #7 记忆的领域接缝：记忆的写入、两段式检索与权重排序都只通过
    /// <see cref="FarmWorld"/> 的公开行为断言。embedding 向量由路由器提供，
    /// 这里用预置向量验证「余弦替换关键词」这一条，不联网。
    /// </summary>
    public class MemoryTests
    {
        static FarmWorld NewWorld()
        {
            var world = new FarmWorld(new FarmClock());
            world.AddField("A1", new FarmPosition(-4f, 4f));
            world.AddField("A2", new FarmPosition(0f, 4f));
            world.AddField("A3", new FarmPosition(4f, 4f));
            world.AddNpc("ahe", "阿禾", new FarmPosition(0f, -3f));
            world.AddNpc("aya", "阿芽", new FarmPosition(1f, -3f));
            world.AddSeeds(CropCatalog.RadishId, 20);
            return world;
        }

        [Test]
        public void 记忆先召回十条再按加权取前三条()
        {
            var crowded = NewWorld();
            for (var i = 0; i < 10; i++)
            {
                crowded.Remember("ahe", $"给 A1 浇了水（第 {i + 1} 次）", 5);
            }

            crowded.Remember("ahe", "今天天气不错", 10);

            var crowdedHits = crowded.RetrieveMemories("ahe", "给 A2 浇水");

            Assert.AreEqual(3, crowdedHits.Count, "检索固定取前三条");
            Assert.IsFalse(
                crowdedHits.Any(hit => hit.Text.Contains("天气")),
                "相似度排在第十名之外的记忆不进入加权重排，即使重要性最高");

            var roomy = NewWorld();
            for (var i = 0; i < 9; i++)
            {
                roomy.Remember("ahe", $"给 A1 浇了水（第 {i + 1} 次）", 5);
            }

            roomy.Remember("ahe", "今天天气不错", 10);

            var roomyHits = roomy.RetrieveMemories("ahe", "给 A2 浇水");

            Assert.AreEqual(
                "今天天气不错",
                roomyHits[0].Text,
                "进入召回后，重要性更高的记忆可以在加权重排中反超相似度");
        }

        [Test]
        public void 有向量时用余弦相似度而不是关键词()
        {
            var world = NewWorld();
            world.Remember("ahe", "给 A1 浇了水", 3);
            world.Remember("ahe", "收 A2 的萝卜", 3);
            world.CacheMemoryEmbedding("ahe", "给 A1 浇了水", new[] { 1f, 0f });
            world.CacheMemoryEmbedding("ahe", "收 A2 的萝卜", new[] { 0f, 1f });

            var hits = world.RetrieveMemories("ahe", "随便说点什么", new[] { 0f, 1f });

            Assert.AreEqual(2, hits.Count);
            Assert.AreEqual("收 A2 的萝卜", hits[0].Text, "余弦应让与检索线索同向的记忆排第一");
            Assert.AreEqual("给 A1 浇了水", hits[1].Text);
        }

        [Test]
        public void 向量不齐全时整次检索退回关键词()
        {
            var world = NewWorld();
            world.Remember("ahe", "给 A1 浇了水", 5);
            world.Remember("ahe", "今天天气不错", 5);
            world.CacheMemoryEmbedding("ahe", "今天天气不错", new[] { 1f, 0f });

            var hits = world.RetrieveMemories("ahe", "给 A1 浇水", new[] { 1f, 0f });

            Assert.AreEqual("给 A1 浇了水", hits[0].Text, "缺向量的记忆不能和余弦分数混排");
        }

        [Test]
        public void 向量维度不一致时整次检索退回关键词()
        {
            var world = NewWorld();
            world.Remember("ahe", "给 A1 浇了水", 5);
            world.Remember("ahe", "今天天气不错", 5);
            world.CacheMemoryEmbedding("ahe", "给 A1 浇了水", new[] { 1f, 0f });
            world.CacheMemoryEmbedding("ahe", "今天天气不错", new[] { 0f, 1f });

            var hits = world.RetrieveMemories("ahe", "给 A1 浇水", new[] { 1f, 0f, 0f });

            Assert.AreEqual("给 A1 浇了水", hits[0].Text, "维度对不上时退回关键词");
        }

        [Test]
        public void 没有向量时退回关键词检索()
        {
            var world = NewWorld();
            world.Remember("ahe", "玩家让我给西边（A1）浇水", 7);
            world.Remember("ahe", "在 A2 播下了萝卜", 6);

            var hits = world.RetrieveMemories("ahe", "上次你让我浇的是西边那块");

            Assert.Greater(hits.Count, 0);
            StringAssert.Contains("西边", hits[0].Text, "断网时关键词仍要能命中跨日指代");
            Assert.AreEqual(0, world.RetrieveMemories("aya", "浇的是西边那块").Count, "记忆只属于对话的那名 NPC");
        }

        [Test]
        public void 决策面板显示最近一次追问命中的记忆()
        {
            var world = NewWorld();
            Assert.IsTrue(world.ExecuteStep("ahe", "A1", WorkStep.Sow).Executed);
            Assert.IsTrue(world.ExecuteStep("ahe", "A2", WorkStep.Sow).Executed);
            world.Remember("ahe", "在 A2 浇了水", 8);
            world.Remember("ahe", "玩家让我给西边（A1）浇水", 7);
            Assert.IsTrue(
                world.TryAssignWorkOrder("ahe", "A2", null, new[] { WorkStep.Water }, out _, out var reason),
                reason);

            var intentHits = world.RetrieveMemories("ahe", world.IntentOf("ahe"));
            StringAssert.Contains("A2", intentHits[0].Text, "按当前意图检索会先命中 A2");

            var answerHits = world.RecallForAnswer("ahe", "上次你让我浇的是西边那块");
            StringAssert.Contains("西边", answerHits[0].Text);

            var snapshot = world.Inspect("ahe");

            Assert.AreEqual(
                answerHits[0].Text,
                snapshot.Memories[0].Text,
                "面板第 1 项必须展示这次追问实际命中的记忆，而不是另取一次");
        }
    }
}
