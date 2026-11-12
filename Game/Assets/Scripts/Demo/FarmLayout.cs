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

        /// <summary>俯视机位的观察中心。</summary>
        public static readonly Vector2 OverheadCenter = Vector2.zero;

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

        static readonly ReadOnlyCollection<FieldFootprint> FieldFootprintList =
            new ReadOnlyCollection<FieldFootprint>(FieldFootprintArray);

        static readonly ReadOnlyCollection<NpcSpawn> NpcSpawnList =
            new ReadOnlyCollection<NpcSpawn>(NpcSpawnArray);

        public static IReadOnlyList<FieldFootprint> Fields => FieldFootprintList;

        public static IReadOnlyList<NpcSpawn> NpcSpawns => NpcSpawnList;

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
    }
}
