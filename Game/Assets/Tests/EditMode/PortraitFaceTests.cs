using System;
using System.Collections.Generic;
using ElysFarm.Core;
using ElysFarm.Demo;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #14 对话框头像的六档表情：出口（农事 / 追问 / 闲聊）加上那一刻的状态
    /// （平常 / 等模型 / 休息）决定用哪张脸，纯函数，不打开场景。
    /// </summary>
    public class PortraitFaceTests
    {
        [Test]
        public void 追问显示平常_闲聊显示笑()
        {
            Assert.AreEqual(
                FaceEmotion.Neutral,
                PortraitFace.Of(IntentOutlet.Query, false, FaceCondition.Idle),
                "追问只是照实回答，不该陪笑");

            Assert.AreEqual(
                FaceEmotion.Smile,
                PortraitFace.Of(IntentOutlet.Chat, false, FaceCondition.Idle),
                "闲聊是放松的出口");
        }

        [Test]
        public void 农事接单得意_拒绝不耐烦()
        {
            Assert.AreEqual(
                FaceEmotion.Smug,
                PortraitFace.Of(IntentOutlet.FarmCommand, true, FaceCondition.Idle),
                "接下一单活是得意的");

            Assert.AreEqual(
                FaceEmotion.Impatient,
                PortraitFace.Of(IntentOutlet.FarmCommand, false, FaceCondition.Idle),
                "拒绝一单活是不耐烦的");
        }

        [Test]
        public void 等模型显示懵_休息显示累_状态优先于出口()
        {
            Assert.AreEqual(
                FaceEmotion.Confused,
                PortraitFace.Of(IntentOutlet.FarmCommand, true, FaceCondition.Thinking),
                "还在理解玩家的话时，接没接单都先懵着");

            Assert.AreEqual(
                FaceEmotion.Tired,
                PortraitFace.Of(IntentOutlet.Chat, false, FaceCondition.Resting),
                "休息时不管刚才是什么出口，脸上都是累");
        }

        [Test]
        public void 出口与状态的全部组合都有明确的脸()
        {
            // 平常状态下三种出口各一张脸；等模型与休息是状态优先，盖过出口。
            var idleFaces = new Dictionary<(IntentOutlet Outlet, bool Accepted), FaceEmotion>
            {
                { (IntentOutlet.FarmCommand, true), FaceEmotion.Smug },
                { (IntentOutlet.FarmCommand, false), FaceEmotion.Impatient },
                { (IntentOutlet.Query, true), FaceEmotion.Neutral },
                { (IntentOutlet.Query, false), FaceEmotion.Neutral },
                { (IntentOutlet.Chat, true), FaceEmotion.Smile },
                { (IntentOutlet.Chat, false), FaceEmotion.Smile }
            };

            var reached = new HashSet<FaceEmotion>();
            var combinations = 0;
            foreach (IntentOutlet outlet in Enum.GetValues(typeof(IntentOutlet)))
            {
                foreach (var accepted in new[] { true, false })
                {
                    foreach (FaceCondition condition in Enum.GetValues(typeof(FaceCondition)))
                    {
                        combinations++;
                        var expected = condition == FaceCondition.Resting
                            ? FaceEmotion.Tired
                            : condition == FaceCondition.Thinking
                                ? FaceEmotion.Confused
                                : idleFaces[(outlet, accepted)];
                        Assert.AreEqual(
                            expected,
                            PortraitFace.Of(outlet, accepted, condition),
                            $"{outlet} + accepted={accepted} + {condition} 的脸不对");
                        reached.Add(expected);
                    }
                }
            }

            Assert.AreEqual(18, combinations, "出口 3 × 接单 2 × 状态 3 = 18 种组合");
            Assert.AreEqual(
                Enum.GetValues(typeof(FaceEmotion)).Length,
                reached.Count,
                "六档表情都要有组合能走到，不能有死档");
        }

        [Test]
        public void 头像格位按两列排布()
        {
            Assert.AreEqual(new SpriteCell(0, 0, 64, 64), PortraitFace.CellOfFrame(0));
            Assert.AreEqual(new SpriteCell(1, 0, 64, 64), PortraitFace.CellOfFrame(1));
            Assert.AreEqual(new SpriteCell(0, 2, 64, 64), PortraitFace.CellOfFrame(4));
            Assert.AreEqual(new SpriteCell(1, 5, 64, 64), PortraitFace.CellOfFrame(11));
            Assert.IsFalse(PortraitFace.CellOfFrame(-1).IsValid, "格位不能为负");
        }

        [Test]
        public void 阿禾与阿芽的六档表情各有自己的格位()
        {
            // 阿土的头像集只有四张脸，六档里有两对就近共用；这里只对脸够用的两人断言互不相同。
            foreach (var npcId in new[] { FarmDemoBootstrap.AheId, FarmDemoBootstrap.AyaId })
            {
                var cells = new System.Collections.Generic.HashSet<SpriteCell>();
                foreach (FaceEmotion emotion in System.Enum.GetValues(typeof(FaceEmotion)))
                {
                    var cell = PortraitFace.CellOf(npcId, emotion);
                    Assert.IsTrue(cell.IsValid, $"{npcId} 的 {emotion} 应有格位");
                    Assert.AreEqual(64, cell.Width, "头像按 64px 切格");
                    Assert.AreEqual(64, cell.Height, "头像按 64px 切格");
                    Assert.IsTrue(cells.Add(cell), $"{npcId} 的 {emotion} 与别的表情撞了格位");
                }
            }
        }

        [Test]
        public void 三名NPC各有头像_表情格位不越界()
        {
            var atlases = new System.Collections.Generic.HashSet<string>();
            foreach (var npcId in new[]
                     {
                         FarmDemoBootstrap.AheId,
                         FarmDemoBootstrap.AyaId,
                         FarmDemoBootstrap.AtuId
                     })
            {
                var atlas = PortraitFace.AtlasOf(npcId);
                Assert.IsNotNull(atlas, $"{npcId} 应有头像");
                Assert.IsTrue(atlases.Add(atlas), $"{npcId} 的头像不该与别人共用");

                var frameCount = PortraitFace.FrameCountOf(npcId);
                Assert.GreaterOrEqual(frameCount, 4, "头像集至少要有四张脸");
                foreach (FaceEmotion emotion in System.Enum.GetValues(typeof(FaceEmotion)))
                {
                    var frame = PortraitFace.FrameOf(npcId, emotion);
                    Assert.GreaterOrEqual(frame, 0, $"{npcId} 的 {emotion} 没有格位");
                    Assert.Less(frame, frameCount, $"{npcId} 的 {emotion} 越过了头像集");
                }
            }

            Assert.IsNull(PortraitFace.AtlasOf("nobody"), "未知 NPC 没有头像");
            Assert.IsFalse(PortraitFace.CellOf("nobody", FaceEmotion.Neutral).IsValid, "未知 NPC 回退到无图");
        }
    }
}
