using System.Collections.Generic;
using ElysFarm.Demo;
using NUnit.Framework;
using UnityEngine;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #12 农场布局：地块、NPC 起点、产出仓与休息点都落在瓦片网格上。
    /// 测试打 <see cref="FarmLayout"/> 这条纯数据接缝：断言数量、两两不重叠、都在可走范围内，
    /// 不创建场景对象，也不检查渲染结果。
    /// </summary>
    public class FarmLayoutTests
    {
        [Test]
        public void 农场有三块地三名NPC起点一个产出仓与一个休息点()
        {
            Assert.AreEqual(3, FarmLayout.Fields.Count, "地块数量是玩法决定的");
            Assert.AreEqual(3, FarmLayout.NpcSpawns.Count, "三名 NPC 各有一个起点");

            var ids = new HashSet<string>();
            foreach (var field in FarmLayout.Fields)
            {
                Assert.IsTrue(ids.Add(field.Id), $"地块 id 不应重复：{field.Id}");
                Assert.AreEqual(FarmLayout.FieldTiles, field.Tiles.width, $"{field.Id} 应是 {FarmLayout.FieldTiles}×{FarmLayout.FieldTiles}");
                Assert.AreEqual(FarmLayout.FieldTiles, field.Tiles.height, $"{field.Id} 应是 {FarmLayout.FieldTiles}×{FarmLayout.FieldTiles}");
            }

            var npcIds = new HashSet<string>();
            foreach (var spawn in FarmLayout.NpcSpawns)
            {
                Assert.IsTrue(npcIds.Add(spawn.Id), $"NPC 起点 id 不应重复：{spawn.Id}");
            }

            CollectionAssert.AreEquivalent(
                new[] { FarmDemoBootstrap.AheId, FarmDemoBootstrap.AyaId, FarmDemoBootstrap.AtuId },
                npcIds,
                "三名 NPC 的 id 要与演示引导器一致");

            Assert.AreNotEqual(FarmLayout.ProduceStoreTile, FarmLayout.RestSpotTile, "产出仓与休息点是两个地方");
        }

        [Test]
        public void 三块地在同一行且两两不重叠()
        {
            var fields = FarmLayout.Fields;
            for (var i = 0; i < fields.Count; i++)
            {
                Assert.AreEqual(fields[0].Tiles.y, fields[i].Tiles.y, "三块地应排成一行");

                for (var j = i + 1; j < fields.Count; j++)
                {
                    Assert.IsFalse(
                        fields[i].Tiles.Overlaps(fields[j].Tiles),
                        $"{fields[i].Id} 与 {fields[j].Id} 不应重叠");
                }
            }

            Assert.Less(fields[0].Tiles.x, fields[1].Tiles.x, "地块按从左到右排列，便于叙述东/中/西");
            Assert.Less(fields[1].Tiles.x, fields[2].Tiles.x, "地块按从左到右排列，便于叙述东/中/西");
        }

        [Test]
        public void 所有地标都在可走范围内()
        {
            var walkable = FarmLayout.Walkable;
            foreach (var field in FarmLayout.Fields)
            {
                Assert.IsTrue(IsInside(walkable, field.Tiles), $"{field.Id} 超出可走范围");
            }

            foreach (var spawn in FarmLayout.NpcSpawns)
            {
                Assert.IsTrue(walkable.Contains(spawn.Tile), $"{spawn.Id} 的起点超出可走范围");
            }

            Assert.IsTrue(walkable.Contains(FarmLayout.ProduceStoreTile), "产出仓超出可走范围");
            Assert.IsTrue(walkable.Contains(FarmLayout.RestSpotTile), "休息点超出可走范围");
        }

        [Test]
        public void 地标之间两两不重叠()
        {
            foreach (var field in FarmLayout.Fields)
            {
                Assert.IsFalse(field.Tiles.Contains(FarmLayout.ProduceStoreTile), "产出仓不应在地块里");
                Assert.IsFalse(field.Tiles.Contains(FarmLayout.RestSpotTile), "休息点不应在地块里");

                foreach (var spawn in FarmLayout.NpcSpawns)
                {
                    Assert.IsFalse(field.Tiles.Contains(spawn.Tile), $"{spawn.Id} 的起点不应落在地块里");
                    Assert.AreNotEqual(spawn.Tile, FarmLayout.ProduceStoreTile);
                    Assert.AreNotEqual(spawn.Tile, FarmLayout.RestSpotTile);
                }
            }

            Assert.AreNotEqual(FarmLayout.ProduceStoreTile, FarmLayout.RestSpotTile);
        }

        [Test]
        public void 瓦片中心与农场坐标一致()
        {
            Assert.AreEqual(new Vector2(0.5f, 0.5f), FarmLayout.TileCenter(Vector2Int.zero));
            Assert.AreEqual(new Vector2(-2.5f, 4.5f), FarmLayout.TileCenter(new Vector2Int(-3, 4)));

            var field = FarmLayout.Fields[0];
            var center = FarmLayout.TileCenter(field.Tiles);
            Assert.AreEqual(field.Tiles.x + field.Tiles.width * 0.5f, center.x, 0.0001f);
            Assert.AreEqual(field.Tiles.y + field.Tiles.height * 0.5f, center.y, 0.0001f);

            var farmPosition = FarmLayout.ToFarmPosition(center);
            Assert.AreEqual(center.x, farmPosition.X, 0.0001f);
            Assert.AreEqual(center.y, farmPosition.Z, 0.0001f);
        }

        [Test]
        public void 未知编号查不到地块与NPC()
        {
            Assert.IsFalse(FarmLayout.TryGetField("nope", out _), "未知地块不该返回布局");
            Assert.IsFalse(FarmLayout.TryGetNpc("nobody", out _), "未知 NPC 不该返回起点");
            Assert.IsTrue(FarmLayout.TryGetField(FarmDemoBootstrap.FieldA1, out var field));
            Assert.AreEqual(FarmDemoBootstrap.FieldA1, field.Id);
        }

        static bool IsInside(RectInt area, RectInt rect)
        {
            return rect.xMin >= area.xMin
                && rect.yMin >= area.yMin
                && rect.xMax <= area.xMax
                && rect.yMax <= area.yMax;
        }
    }
}
