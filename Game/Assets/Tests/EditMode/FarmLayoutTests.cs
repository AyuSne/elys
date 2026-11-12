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

        /// <summary>#13：农舍在东、鸡舍在南，水井与产出仓各一处。</summary>
        [Test]
        public void 农场有四座地标且农舍在东鸡舍在南()
        {
            var kinds = new HashSet<LandmarkKind>();
            foreach (var landmark in FarmLayout.Landmarks)
            {
                Assert.IsTrue(kinds.Add(landmark.Kind), $"地标重复：{landmark.Kind}");
                Assert.Greater(landmark.Tiles.width, 0, $"{landmark.Kind} 占位不能为空");
                Assert.Greater(landmark.Tiles.height, 0, $"{landmark.Kind} 占位不能为空");
            }

            CollectionAssert.AreEquivalent(
                new[]
                {
                    LandmarkKind.Farmhouse,
                    LandmarkKind.Coop,
                    LandmarkKind.Well,
                    LandmarkKind.ProduceStore
                },
                kinds,
                "农舍、鸡舍、水井、产出仓各一座");

            Assert.IsTrue(FarmLayout.TryGetLandmark(LandmarkKind.Farmhouse, out var farmhouse));
            Assert.IsTrue(FarmLayout.TryGetLandmark(LandmarkKind.Coop, out var coop));

            var fieldEast = FieldOf(FarmDemoBootstrap.FieldA3).Tiles.xMax;
            Assert.GreaterOrEqual(farmhouse.Tiles.xMin, fieldEast, "农舍在地块以东");
            Assert.LessOrEqual(coop.Tiles.yMin, FieldOf(FarmDemoBootstrap.FieldA1).Tiles.yMin, "鸡舍在地块以南");

            foreach (var landmark in FarmLayout.Landmarks)
            {
                foreach (var field in FarmLayout.Fields)
                {
                    Assert.IsFalse(
                        field.Tiles.Overlaps(landmark.Tiles),
                        $"{landmark.Kind} 不该压在地块 {field.Id} 上");
                }
            }
        }

        static FieldFootprint FieldOf(string fieldId)
        {
            Assert.IsTrue(FarmLayout.TryGetField(fieldId, out var footprint), $"找不到地块 {fieldId}");
            return footprint;
        }

        /// <summary>#13：一条土路把农舍、三块地、产出仓连成一条动线。</summary>
        [Test]
        public void 土路把农舍三块地与产出仓连成一条动线()
        {
            Assert.GreaterOrEqual(FarmLayout.RoadTiles.Count, 20, "土路要横穿一屏，不能只有几格");

            var reached = RoadReachableFrom(FarmLayout.RoadTiles[0]);
            Assert.AreEqual(
                FarmLayout.RoadTiles.Count,
                reached.Count,
                "土路必须连成一条，不能断成几截");

            foreach (var field in FarmLayout.Fields)
            {
                Assert.IsTrue(TouchesRoad(field.Tiles), $"{field.Id} 没有路通到地块边");
            }

            Assert.IsTrue(
                TouchesRoad(LandmarkOf(LandmarkKind.Farmhouse).Tiles),
                "农舍门口没有路");
            Assert.IsTrue(
                TouchesRoad(LandmarkOf(LandmarkKind.ProduceStore).Tiles),
                "产出仓没有路");
        }

        /// <summary>#13：地标不压在土路上了——路要整条看得见，建筑站在路两边。</summary>
        [Test]
        public void 地标不压在土路上()
        {
            foreach (var landmark in FarmLayout.Landmarks)
            {
                for (var y = landmark.Tiles.yMin; y < landmark.Tiles.yMax; y++)
                {
                    for (var x = landmark.Tiles.xMin; x < landmark.Tiles.xMax; x++)
                    {
                        var tile = new Vector2Int(x, y);
                        Assert.IsFalse(
                            FarmLayout.IsRoad(tile),
                            $"{landmark.Kind} 压在土路的 {tile} 上");
                    }
                }
            }
        }

        static Landmark LandmarkOf(LandmarkKind kind)
        {
            Assert.IsTrue(FarmLayout.TryGetLandmark(kind, out var landmark), $"找不到地标 {kind}");
            return landmark;
        }

        /// <summary>#13：树与草丛沿画面外圈落位，把农场围在中间。</summary>
        [Test]
        public void 树和草丛压在画面边缘()
        {
            Assert.GreaterOrEqual(FarmLayout.Decorations.Count, 8, "外圈要有足够的树与草丛");

            foreach (var decoration in FarmLayout.Decorations)
            {
                Assert.That(
                    decoration.Variant,
                    Is.InRange(0, FarmLayout.VariantCountOf(decoration.Kind) - 1),
                    $"{decoration.Kind} 的变体号越界");
                Assert.IsTrue(
                    IsInside(FarmLayout.GroundBounds, decoration.Tiles),
                    $"{decoration.Kind} 落在没铺地面的地方：{decoration.Tiles}");
            }

            var fields = FieldsBounds();
            Assert.IsTrue(HasTree(test => test.Tiles.xMax <= fields.xMin), "西侧要有树");
            Assert.IsTrue(HasDecoration(test => test.Tiles.yMin >= fields.yMax), "北侧要有树或草丛");
            Assert.IsTrue(HasDecoration(test => test.Tiles.yMax <= fields.yMin), "南侧要有树或草丛");

            var eastLandmark = false;
            foreach (var landmark in FarmLayout.Landmarks)
            {
                if (landmark.Tiles.xMin >= fields.xMax)
                {
                    eastLandmark = true;
                }
            }

            Assert.IsTrue(eastLandmark, "东侧要有农舍或鸡舍");
        }

        [Test]
        public void 装饰不压在地块与地标上()
        {
            foreach (var decoration in FarmLayout.Decorations)
            {
                foreach (var field in FarmLayout.Fields)
                {
                    Assert.IsFalse(
                        decoration.Tiles.Overlaps(field.Tiles),
                        $"{decoration.Kind} 压在 {field.Id} 上");
                }

                foreach (var landmark in FarmLayout.Landmarks)
                {
                    Assert.IsFalse(
                        decoration.Tiles.Overlaps(landmark.Tiles),
                        $"{decoration.Kind} 压在 {landmark.Kind} 上");
                }
            }
        }

        static bool HasDecoration(System.Predicate<Decoration> test)
        {
            foreach (var decoration in FarmLayout.Decorations)
            {
                if (test(decoration))
                {
                    return true;
                }
            }

            return false;
        }

        static bool HasTree(System.Predicate<Decoration> test)
        {
            foreach (var decoration in FarmLayout.Decorations)
            {
                if (decoration.Kind == DecorationKind.Tree && test(decoration))
                {
                    return true;
                }
            }

            return false;
        }

        static RectInt FieldsBounds()
        {
            var xMin = int.MaxValue;
            var yMin = int.MaxValue;
            var xMax = int.MinValue;
            var yMax = int.MinValue;
            foreach (var field in FarmLayout.Fields)
            {
                xMin = Mathf.Min(xMin, field.Tiles.xMin);
                yMin = Mathf.Min(yMin, field.Tiles.yMin);
                xMax = Mathf.Max(xMax, field.Tiles.xMax);
                yMax = Mathf.Max(yMax, field.Tiles.yMax);
            }

            return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        static HashSet<Vector2Int> RoadReachableFrom(Vector2Int start)
        {
            var reached = new HashSet<Vector2Int>();
            if (!FarmLayout.IsRoad(start))
            {
                return reached;
            }

            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            reached.Add(start);

            var steps = new[]
            {
                new Vector2Int(1, 0),
                new Vector2Int(-1, 0),
                new Vector2Int(0, 1),
                new Vector2Int(0, -1)
            };

            while (queue.Count > 0)
            {
                var tile = queue.Dequeue();
                foreach (var step in steps)
                {
                    var next = tile + step;
                    if (FarmLayout.IsRoad(next) && reached.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            return reached;
        }

        /// <summary>路面有没有碰到这块占位：压在上面或贴着边都算通。</summary>
        static bool TouchesRoad(RectInt tiles)
        {
            for (var y = tiles.yMin - 1; y <= tiles.yMax; y++)
            {
                for (var x = tiles.xMin - 1; x <= tiles.xMax; x++)
                {
                    if (FarmLayout.IsRoad(new Vector2Int(x, y)))
                    {
                        return true;
                    }
                }
            }

            return false;
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
