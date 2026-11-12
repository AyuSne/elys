using ElysFarm.Core;
using ElysFarm.Demo;
using NUnit.Framework;
using UnityEngine;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #12 像素图集：按名字取图、按格位切图，缺失或关掉素材时返回 null。
    /// 测试打 <see cref="PixelArt"/> 这条表现层接缝，不打开场景；
    /// 切格结果（行、列、尺寸）与四方向选择都是纯函数，可以直接断言。
    /// </summary>
    public class PixelArtTests
    {
        [SetUp]
        public void SetUp()
        {
            PixelArt.UseArt = true;
            PixelArt.ClearCache();
        }

        [TearDown]
        public void TearDown()
        {
            PixelArt.UseArt = true;
            PixelArt.ClearCache();
        }

        [Test]
        public void 素材缺失时返回空而不是抛异常()
        {
            Assert.IsNull(PixelArt.LoadTexture(null), "空名字应返回 null");
            Assert.IsNull(PixelArt.LoadTexture("Characters/DoesNotExist"), "不存在的图应返回 null");
            Assert.IsNull(
                PixelArt.LoadSprite("Characters/DoesNotExist", new SpriteCell(0, 0, 16, 32), Vector2.zero),
                "切格失败应返回 null");
            Assert.IsNull(PixelArt.LoadCharacter("nobody", Facing.Down, 0), "未知 NPC 应回退体块");
            Assert.IsNull(PixelArt.LoadCrop("moon", CropStage.Mature), "未知品种应回退体块");
        }

        [Test]
        public void 关掉素材开关后取图一律为空()
        {
            PixelArt.UseArt = false;

            Assert.IsNull(PixelArt.LoadTexture("Characters/Robin"), "关掉素材后必须走体块回退");
            Assert.IsNull(PixelArt.LoadCharacter(FarmDemoBootstrap.AheId, Facing.Down, 0), "角色同样回退");
            Assert.IsNull(PixelArt.LoadCrop(CropCatalog.RadishId, CropStage.Seedling), "作物同样回退");
            Assert.IsNull(PixelArt.LoadTile(4, 13), "瓦片同样回退");
            Assert.IsNull(PixelArt.LoadHoeDirt(false), "耕地同样回退");
        }

        [Test]
        public void 角色图按四方向四帧切格()
        {
            Assert.AreEqual(new SpriteCell(0, 0, 16, 32), PixelArt.CharacterCell(Facing.Down, 0));
            Assert.AreEqual(new SpriteCell(3, 1, 16, 32), PixelArt.CharacterCell(Facing.Right, 3));
            Assert.AreEqual(new SpriteCell(2, 2, 16, 32), PixelArt.CharacterCell(Facing.Up, 2));
            Assert.AreEqual(new SpriteCell(1, 3, 16, 32), PixelArt.CharacterCell(Facing.Left, 1));

            Assert.IsFalse(PixelArt.CharacterCell(Facing.Down, 4).IsValid, "每个方向只有四帧");
            Assert.IsFalse(PixelArt.CharacterCell(Facing.Down, -1).IsValid, "帧号不能为负");
        }

        [Test]
        public void 三名NPC各有自己的行走图()
        {
            var atlases = new System.Collections.Generic.HashSet<string>();
            foreach (var npcId in new[] { FarmDemoBootstrap.AheId, FarmDemoBootstrap.AyaId, FarmDemoBootstrap.AtuId })
            {
                var atlas = PixelArt.CharacterAtlasOf(npcId);
                Assert.IsNotNull(atlas, $"{npcId} 应有行走图");
                Assert.IsTrue(atlases.Add(atlas), $"{npcId} 的行走图不应与别人重复");
            }

            Assert.IsNull(PixelArt.CharacterAtlasOf("nobody"), "未知 NPC 没有行走图");
        }

        [Test]
        public void 四方向由速度的主分量决定()
        {
            Assert.AreEqual(Facing.Right, PixelArt.FacingOf(new Vector2(1f, 0.2f), Facing.Down));
            Assert.AreEqual(Facing.Left, PixelArt.FacingOf(new Vector2(-1f, 0.2f), Facing.Down));
            Assert.AreEqual(Facing.Up, PixelArt.FacingOf(new Vector2(0.2f, 1f), Facing.Down));
            Assert.AreEqual(Facing.Down, PixelArt.FacingOf(new Vector2(0.2f, -1f), Facing.Up));
            Assert.AreEqual(Facing.Left, PixelArt.FacingOf(new Vector2(-2f, 1.9f), Facing.Down), "横向占主导");
        }

        [Test]
        public void 停下回到该方向第一帧()
        {
            Assert.AreEqual(0, PixelArt.WalkFrame(false, 0f));
            Assert.AreEqual(0, PixelArt.WalkFrame(false, 99f), "站着不动时锁在第一帧");

            Assert.AreEqual(0, PixelArt.WalkFrame(true, 0f));
            Assert.AreEqual(1, PixelArt.WalkFrame(true, PixelArt.WalkSecondsPerFrame + 0.001f));
            Assert.AreEqual(0, PixelArt.WalkFrame(true, PixelArt.WalkSecondsPerFrame * 4f), "四帧循环");

            Assert.AreEqual(
                Facing.Right,
                PixelArt.FacingOf(Vector2.zero, Facing.Right),
                "停下时朝向保持上一次的方向");
        }

        [Test]
        public void 格位在贴图里翻成Unity的左下角矩形()
        {
            var rect = new SpriteCell(2, 2, 16, 32).RectIn(288);

            Assert.AreEqual(32f, rect.x, 0.001f, "列号 × 格宽");
            Assert.AreEqual(192f, rect.y, 0.001f, "贴图高度 −（行号 + 1）× 格高");
            Assert.AreEqual(16f, rect.width, 0.001f);
            Assert.AreEqual(32f, rect.height, 0.001f);

            Assert.AreEqual(Rect.zero, default(SpriteCell).RectIn(288), "无效格位不产生矩形");
        }
    }
}
