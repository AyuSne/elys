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
        public void 关掉素材开关后即使图存在也退回体块()
        {
            Assert.IsNotNull(PixelArt.LoadTexture("Characters/Robin"), "默认应能按名字加载已导入的图");
            Assert.IsNotNull(PixelArt.LoadCharacter(FarmDemoBootstrap.AheId, Facing.Down, 0));

            PixelArt.UseArt = false;

            Assert.IsNull(PixelArt.LoadTexture("Characters/Robin"), "关掉素材后必须走体块回退");
            Assert.IsNull(PixelArt.LoadCharacter(FarmDemoBootstrap.AheId, Facing.Down, 0), "角色同样回退");
            Assert.IsNull(PixelArt.LoadCrop(CropCatalog.RadishId, CropStage.Seedling), "作物同样回退");
            Assert.IsNull(PixelArt.LoadTile(4, 13), "瓦片同样回退");
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
        public void 能按格位切出精灵且尺寸与格位一致()
        {
            var character = PixelArt.LoadCharacter(FarmDemoBootstrap.AheId, Facing.Up, 2);
            Assert.IsNotNull(character, "角色精灵应能切出来");
            Assert.AreEqual(16f, character.rect.width, 0.001f);
            Assert.AreEqual(32f, character.rect.height, 0.001f);
            Assert.IsNotNull(character.texture);

            var expected = PixelArt.CharacterCell(Facing.Up, 2).RectIn(character.texture.height);
            Assert.AreEqual(expected.x, character.rect.x, 0.001f);
            Assert.AreEqual(expected.y, character.rect.y, 0.001f);
        }

        [Test]
        public void 两个品种的五个阶段都能切出图且格位不重复()
        {
            var cells = new System.Collections.Generic.HashSet<SpriteCell>();
            foreach (var speciesId in new[] { CropCatalog.RadishId, CropCatalog.WheatId })
            {
                for (var stage = CropStage.Seedling; stage <= CropStage.Mature; stage++)
                {
                    var sprite = PixelArt.LoadCrop(speciesId, stage);
                    Assert.IsNotNull(sprite, $"{speciesId} 的 {stage} 阶段应能切出图");
                    Assert.AreEqual(16f, sprite.rect.width, 0.001f);
                    Assert.AreEqual(32f, sprite.rect.height, 0.001f);
                    Assert.IsTrue(cells.Add(CropStages.CellOf(speciesId, stage)), $"{speciesId}/{stage} 格位重复");
                }
            }

            Assert.AreEqual(10, cells.Count, "两个品种五个阶段的格位应两两不同");
        }

        [Test]
        public void 瓦片与耕地按16像素切格()
        {
            var grass = PixelArt.LoadTile(4, 13);
            Assert.IsNotNull(grass, "草地瓦片应能切出来");
            Assert.AreEqual(16f, grass.rect.width, 0.001f);
            Assert.AreEqual(16f, grass.rect.height, 0.001f);

            var dry = PixelArt.LoadHoeDirt(false);
            var wet = PixelArt.LoadHoeDirt(true);
            Assert.IsNotNull(dry);
            Assert.IsNotNull(wet);
            Assert.AreNotEqual(dry.rect.xMin, wet.rect.xMin, "干土与湿土取不同的格位");
        }
    }
}
