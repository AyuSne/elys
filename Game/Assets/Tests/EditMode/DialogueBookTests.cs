using ElysFarm.Core;
using ElysFarm.Demo;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #18 对话簿：演示层给每名 NPC 记一份对话，切换指挥对象时恢复各自那一份，
    /// 每人最多保留最近 6 轮。纯逻辑，不打开场景，也不碰 <see cref="FarmWorld"/>。
    /// </summary>
    public class DialogueBookTests
    {
        [Test]
        public void 没聊过的人没有上一轮_切换后取回各自那一份()
        {
            var book = new DialogueBook();
            Assert.IsNull(book.CurrentRound, "刚打开对话簿时没有可显示的一轮");
            Assert.IsEmpty(book.RoundsOf(FarmDemoBootstrap.AheId), "没聊过的阿禾没有历史");

            book.SwitchTo(FarmDemoBootstrap.AyaId);
            Assert.IsEmpty(book.RoundsOf(FarmDemoBootstrap.AyaId), "切到阿芽也还没有历史");
            book.Record(FarmDemoBootstrap.AyaId, Round("今天天气不错", "是挺好，适合晒谷子。"));
            book.Record(
                FarmDemoBootstrap.AyaId,
                Round("那你今天做什么", "去东边那块地除草。", IntentOutlet.FarmCommand, true, "理解成：A1 · 除草"));

            book.SwitchTo(FarmDemoBootstrap.AheId);
            Assert.IsNull(book.CurrentRound, "阿禾还没聊过，不能显示别人的上一轮");
            Assert.IsEmpty(book.RoundsOf(FarmDemoBootstrap.AheId), "阿芽的对话不能记到阿禾名下");
            book.Record(FarmDemoBootstrap.AheId, Round("阿禾你呢", "待会儿把水浇了。"));

            book.SwitchTo(FarmDemoBootstrap.AyaId);
            var rounds = book.RoundsOf(FarmDemoBootstrap.AyaId);
            Assert.AreEqual(2, rounds.Count, "切回阿芽时两轮都要在");

            var current = book.CurrentRound;
            Assert.IsNotNull(current, "切回聊过的人要能看到上一轮");
            Assert.AreEqual("那你今天做什么", current.Value.PlayerText, "看到的是最后一句");
            Assert.AreEqual("去东边那块地除草。", current.Value.Reply);
            Assert.AreEqual(IntentOutlet.FarmCommand, current.Value.Outlet, "出口要跟着这一轮恢复");
            Assert.IsTrue(current.Value.Accepted, "接单与否要跟着这一轮恢复");
            Assert.AreEqual("理解成：A1 · 除草", current.Value.Understood, "「理解成」一行也要恢复");
        }

        [Test]
        public void 每名NPC最多保留最近六轮()
        {
            var book = new DialogueBook();
            book.SwitchTo(FarmDemoBootstrap.AheId);
            for (var i = 1; i <= 7; i++)
            {
                book.Record(FarmDemoBootstrap.AheId, Round($"第 {i} 句", $"第 {i} 答"));
            }

            var rounds = book.RoundsOf(FarmDemoBootstrap.AheId);
            Assert.AreEqual(DialogueBook.MaxRounds, rounds.Count, "每名 NPC 只保留 6 轮");
            Assert.AreEqual(6, rounds.Count, "规格写死的是 6 轮");
            Assert.AreEqual("第 2 句", rounds[0].PlayerText, "第 7 轮挤掉最旧的第 1 轮");
            Assert.AreEqual("第 7 句", rounds[rounds.Count - 1].PlayerText, "最新一轮还在末尾");
            Assert.AreEqual("第 7 句", book.CurrentRound?.PlayerText, "当前轮也是最新那一轮");

            book.SwitchTo(FarmDemoBootstrap.AtuId);
            book.Record(FarmDemoBootstrap.AtuId, Round("阿土，听说了吗", "听说了。"));
            Assert.AreEqual(1, book.RoundsOf(FarmDemoBootstrap.AtuId).Count, "封顶只作用于聊满的人");
            Assert.AreEqual(6, book.RoundsOf(FarmDemoBootstrap.AheId).Count, "别人的 6 轮不受影响");
        }

        static DialogueRound Round(
            string playerText,
            string reply,
            IntentOutlet outlet = IntentOutlet.Chat,
            bool accepted = false,
            string understood = "闲聊")
        {
            return new DialogueRound(playerText, reply, understood, outlet, accepted);
        }
    }
}
