using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ElysFarm.Core;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>一块地在瓦片网格上的占位与中心点。</summary>
    public readonly struct FieldFootprint
    {
        public FieldFootprint(string id, RectInt tiles)
        {
            Id = id;
            Tiles = tiles;
        }

        public string Id { get; }

        public RectInt Tiles { get; }

        /// <summary>地块中心的世界坐标（1 世界单位 = 1 瓦片）。</summary>
        public Vector2 Center => FarmLayout.TileCenter(Tiles);
    }

    /// <summary>一名 NPC 的起始瓦片。</summary>
    public readonly struct NpcSpawn
    {
        public NpcSpawn(string id, Vector2Int tile)
        {
            Id = id;
            Tile = tile;
        }

        public string Id { get; }

        public Vector2Int Tile { get; }

        public Vector2 World => FarmLayout.TileCenter(Tile);
    }

    /// <summary>地面上的地标：农舍、鸡舍、水井与产出仓。每座只有一个，位置由布局决定。</summary>
    public enum LandmarkKind
    {
        Farmhouse,
        Coop,
        Well,
        ProduceStore
    }

    /// <summary>
    /// 「站在地上」这件事的规矩：落点是占位底边的中点，排序基准也是这条边。
    /// 地标与装饰共用这一处，免得两边各写一份、各自漂移。
    /// </summary>
    public static class GroundSpot
    {
        /// <summary>精灵的落点：占位底边的中点（1 世界单位 = 1 瓦片）。</summary>
        public static Vector2 AnchorOf(RectInt tiles)
        {
            return new Vector2(tiles.xMin + tiles.width * 0.5f, tiles.yMin);
        }

        /// <summary>按 Y 排序的基准：地面接触边。</summary>
        public static float SortBaseOf(RectInt tiles)
        {
            return tiles.yMin;
        }
    }

    /// <summary>
    /// 一座地标在瓦片网格上的占位。精灵以占位底边的中点为落点，
    /// 排序也以这条底边为准——所以人站在它北边会被它挡住，站在南边就画在它前面。
    /// </summary>
    public readonly struct Landmark
    {
        public Landmark(LandmarkKind kind, RectInt tiles)
        {
            Kind = kind;
            Tiles = tiles;
        }

        public LandmarkKind Kind { get; }

        public RectInt Tiles { get; }

        /// <summary>精灵的落点：占位底边的中点（1 世界单位 = 1 瓦片）。</summary>
        public Vector2 Anchor => GroundSpot.AnchorOf(Tiles);

        /// <summary>按 Y 排序的基准：地面接触边。</summary>
        public float SortBase => GroundSpot.SortBaseOf(Tiles);
    }

    /// <summary>外圈的装饰：只压画面边缘，不参与玩法。</summary>
    public enum DecorationKind
    {
        Tree,
        Stump,
        GrassTuft
    }

    /// <summary>
    /// 一棵树、一段树桩或一丛草在瓦片网格上的占位。
    /// 和地标一样，精灵以占位底边的中点为落点，排序按这条底边算。
    /// </summary>
    public readonly struct Decoration
    {
        public Decoration(DecorationKind kind, int variant, RectInt tiles)
        {
            Kind = kind;
            Variant = variant;
            Tiles = tiles;
        }

        public DecorationKind Kind { get; }

        /// <summary>同种装饰的换图序号，决定用哪张图。</summary>
        public int Variant { get; }

        public RectInt Tiles { get; }

        /// <summary>精灵的落点：占位底边的中点。</summary>
        public Vector2 Anchor => GroundSpot.AnchorOf(Tiles);

        /// <summary>按 Y 排序的基准：地面接触边。</summary>
        public float SortBase => GroundSpot.SortBaseOf(Tiles);
    }

    /// <summary>
    /// 农场的瓦片布局：三块地、三名 NPC 起点、产出仓与休息点。
    /// 这是表现层的唯一布局来源，领域只拿到换算好的 <see cref="FarmPosition"/>。
    /// 1 世界单位 = 1 瓦片 = 16px；俯视 3× 时一瓦片 48 屏幕像素。
    /// </summary>
    public static class FarmLayout
    {
        public const int PixelsPerTile = 16;

        /// <summary>一块地是 3×3 瓦片，正好放下九株作物。</summary>
        public const int FieldTiles = 3;

        /// <summary>可走范围：比俯视 3× 的可见范围略大，地面与最外侧一格都有余量。</summary>
        public static readonly RectInt Walkable = new RectInt(-13, -7, 27, 15);

        /// <summary>铺地面的范围：比可走范围再向外两格，3× 俯视时看不到地面边缘。</summary>
        public static readonly RectInt GroundBounds = new RectInt(
            Walkable.xMin - 2,
            Walkable.yMin - 2,
            Walkable.width + 4,
            Walkable.height + 4);

        /// <summary>俯视机位的观察中心。</summary>
        public static readonly Vector2 OverheadCenter = Vector2.zero;

        /// <summary>土路的主路所在的瓦片行；NPC 的起点就站在这条路上。</summary>
        public const int RoadRow = -1;

        public static readonly Vector2Int ProduceStoreTile = new Vector2Int(-4, -3);

        public static readonly Vector2Int RestSpotTile = new Vector2Int(2, -3);

        static readonly FieldFootprint[] FieldFootprintArray =
        {
            new FieldFootprint(FarmDemoBootstrap.FieldA1, new RectInt(-6, 1, FieldTiles, FieldTiles)),
            new FieldFootprint(FarmDemoBootstrap.FieldA2, new RectInt(-2, 1, FieldTiles, FieldTiles)),
            new FieldFootprint(FarmDemoBootstrap.FieldA3, new RectInt(2, 1, FieldTiles, FieldTiles))
        };

        static readonly NpcSpawn[] NpcSpawnArray =
        {
            new NpcSpawn(FarmDemoBootstrap.AheId, new Vector2Int(-2, -1)),
            new NpcSpawn(FarmDemoBootstrap.AyaId, new Vector2Int(0, -1)),
            new NpcSpawn(FarmDemoBootstrap.AtuId, new Vector2Int(2, -1))
        };

        /// <summary>
        /// 农舍在东、鸡舍在南、水井与产出仓在路的南侧。
        /// 占位按精灵的实际像素尺寸换算：农舍 118×150、鸡舍 96×128、水井 48×80、产出仓 16×16。
        /// 建筑全部退到路的南/北侧，土路整条看得见——树冠可以压路，房子不压。
        /// </summary>
        static readonly Landmark[] LandmarkArray =
        {
            new Landmark(LandmarkKind.Farmhouse, new RectInt(6, 0, 7, 9)),
            new Landmark(LandmarkKind.Coop, new RectInt(6, -9, 6, 8)),
            new Landmark(LandmarkKind.Well, new RectInt(-7, -6, 3, 5)),
            new Landmark(LandmarkKind.ProduceStore, new RectInt(-4, -3, 1, 1))
        };

        static readonly ReadOnlyCollection<FieldFootprint> FieldFootprintList =
            new ReadOnlyCollection<FieldFootprint>(FieldFootprintArray);

        static readonly ReadOnlyCollection<NpcSpawn> NpcSpawnList =
            new ReadOnlyCollection<NpcSpawn>(NpcSpawnArray);

        static readonly ReadOnlyCollection<Landmark> LandmarkList =
            new ReadOnlyCollection<Landmark>(LandmarkArray);

        /// <summary>
        /// 外圈的树、树桩与草丛。全部落在地块与地标之外：树冠可以压到路上，
        /// 但树干（占位的底边）不站在路中间。
        /// </summary>
        static readonly Decoration[] DecorationArray =
        {
            DecorationOf(DecorationKind.Tree, 0, -11, 2),
            DecorationOf(DecorationKind.Tree, 1, -11, -3),
            DecorationOf(DecorationKind.Tree, 0, -11, -8),
            DecorationOf(DecorationKind.Stump, 0, -8, 1),
            DecorationOf(DecorationKind.GrassTuft, 0, -7, 4),
            DecorationOf(DecorationKind.GrassTuft, 1, -3, 4),
            DecorationOf(DecorationKind.GrassTuft, 2, 1, 4),
            DecorationOf(DecorationKind.GrassTuft, 0, 5, 4),
            DecorationOf(DecorationKind.GrassTuft, 1, -8, -3),
            DecorationOf(DecorationKind.GrassTuft, 2, -2, -3),
            DecorationOf(DecorationKind.GrassTuft, 0, 5, -2),
            DecorationOf(DecorationKind.GrassTuft, 1, -3, 2),
            DecorationOf(DecorationKind.GrassTuft, 2, 1, 1)
        };

        static readonly ReadOnlyCollection<Decoration> DecorationList =
            new ReadOnlyCollection<Decoration>(DecorationArray);

        /// <summary>土路的全部瓦片：一条横穿农场的主路，加上通到三块地与产出仓的岔路。</summary>
        static readonly Vector2Int[] RoadTileArray = BuildRoadTiles();

        static readonly HashSet<Vector2Int> RoadTileSet = new HashSet<Vector2Int>(RoadTileArray);

        static readonly ReadOnlyCollection<Vector2Int> RoadTileList =
            new ReadOnlyCollection<Vector2Int>(RoadTileArray);

        public static IReadOnlyList<FieldFootprint> Fields => FieldFootprintList;

        public static IReadOnlyList<NpcSpawn> NpcSpawns => NpcSpawnList;

        public static IReadOnlyList<Landmark> Landmarks => LandmarkList;

        public static IReadOnlyList<Decoration> Decorations => DecorationList;

        public static IReadOnlyList<Vector2Int> RoadTiles => RoadTileList;

        /// <summary>这一格是不是土路。地面以上的东西都不该落在路上。</summary>
        public static bool IsRoad(Vector2Int tile)
        {
            return RoadTileSet.Contains(tile);
        }

        /// <summary>一种装饰占几格：树是 48×80（3×5 格）、树桩 16×26、草丛 15×20。</summary>
        public static Vector2Int TileSizeOf(DecorationKind kind)
        {
            return ShapeOf(kind).Size;
        }

        /// <summary>一种装饰有几种换图；变体号必须小于这个数。</summary>
        public static int VariantCountOf(DecorationKind kind)
        {
            return ShapeOf(kind).Variants;
        }

        /// <summary>装饰的尺寸与换图数量写在一处：加一种装饰只改这里和素材表。</summary>
        static DecorationShape ShapeOf(DecorationKind kind)
        {
            switch (kind)
            {
                case DecorationKind.Tree:
                    return new DecorationShape(new Vector2Int(3, 5), 2);
                case DecorationKind.Stump:
                    return new DecorationShape(new Vector2Int(1, 2), 1);
                default:
                    return new DecorationShape(Vector2Int.one, 3);
            }
        }

        /// <summary>一种装饰的占位尺寸与换图数量。</summary>
        readonly struct DecorationShape
        {
            public DecorationShape(Vector2Int size, int variants)
            {
                Size = size;
                Variants = variants;
            }

            public Vector2Int Size { get; }

            public int Variants { get; }
        }

        static Decoration DecorationOf(DecorationKind kind, int variant, int x, int y)
        {
            var size = TileSizeOf(kind);
            return new Decoration(kind, variant, new RectInt(x, y, size.x, size.y));
        }

        public static bool TryGetField(string fieldId, out FieldFootprint footprint)
        {
            if (!string.IsNullOrEmpty(fieldId))
            {
                foreach (var candidate in FieldFootprintArray)
                {
                    if (string.Equals(candidate.Id, fieldId, StringComparison.Ordinal))
                    {
                        footprint = candidate;
                        return true;
                    }
                }
            }

            footprint = default;
            return false;
        }

        public static bool TryGetNpc(string npcId, out NpcSpawn spawn)
        {
            if (!string.IsNullOrEmpty(npcId))
            {
                foreach (var candidate in NpcSpawnArray)
                {
                    if (string.Equals(candidate.Id, npcId, StringComparison.Ordinal))
                    {
                        spawn = candidate;
                        return true;
                    }
                }
            }

            spawn = default;
            return false;
        }

        /// <summary>按种类取地标；每种地标只有一座。</summary>
        public static bool TryGetLandmark(LandmarkKind kind, out Landmark landmark)
        {
            foreach (var candidate in LandmarkArray)
            {
                if (candidate.Kind == kind)
                {
                    landmark = candidate;
                    return true;
                }
            }

            landmark = default;
            return false;
        }

        /// <summary>瓦片正中：精灵的落点都从这里换算。</summary>
        public static Vector2 TileCenter(Vector2Int tile)
        {
            return new Vector2(tile.x + 0.5f, tile.y + 0.5f);
        }

        /// <summary>一个矩形区域的正中。</summary>
        public static Vector2 TileCenter(RectInt tiles)
        {
            return new Vector2(tiles.x + tiles.width * 0.5f, tiles.y + tiles.height * 0.5f);
        }

        /// <summary>世界坐标转领域坐标（FarmPosition 的 Z 就是世界的 y）。</summary>
        public static FarmPosition ToFarmPosition(Vector2 world)
        {
            return new FarmPosition(world.x, world.y);
        }

        /// <summary>领域坐标转世界坐标。</summary>
        public static Vector2 ToWorld(FarmPosition position)
        {
            return new Vector2(position.X, position.Z);
        }

        /// <summary>
        /// 土路：一条横穿农场的主路，加上通到每块地南沿的岔路与通到产出仓的一条。
        /// 主路正对农舍的门口，所以「农舍 → 地 → 产出仓」是同一条动线。
        /// </summary>
        static Vector2Int[] BuildRoadTiles()
        {
            var tiles = new List<Vector2Int>();

            // 主路铺满可走范围的宽度：镜头怎么推都看得到路，不会在边上断掉。
            for (var x = Walkable.xMin; x < Walkable.xMax; x++)
            {
                tiles.Add(new Vector2Int(x, RoadRow));
            }

            foreach (var field in FieldFootprintArray)
            {
                // 地块正中偏左的那一列：岔路往北一格就贴住地块南沿，人从路上拐进去。
                tiles.Add(new Vector2Int(field.Tiles.xMin + field.Tiles.width / 2, RoadRow + 1));
            }

            tiles.Add(new Vector2Int(ProduceStoreTile.x, RoadRow - 1));
            return tiles.ToArray();
        }
    }
}
