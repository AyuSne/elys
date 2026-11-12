using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 场景的底：俯视时用运行时 Tilemap 铺满草地，素材关掉或缺失时退回一整块绿色体块。
    /// 本票只做地面与按 Y 的绘制顺序；建筑、土路与树木留给 T2。
    /// </summary>
    public static class FarmDressing
    {
        static readonly Color GrassFallback = new Color(0.28f, 0.42f, 0.22f);

        /// <summary>草地瓦片在室外图集里的格位；只用亮度接近、铺开不显接缝的几个。</summary>
        static readonly Vector2Int[] GrassCells =
        {
            new Vector2Int(4, 13),
            new Vector2Int(7, 13),
            new Vector2Int(7, 15),
            new Vector2Int(5, 15)
        };

        static readonly List<Tile> CreatedTiles = new List<Tile>();

        /// <summary>重建表现层前调用：Tilemap 的 Tile 是 ScriptableObject，不随场景对象销毁。</summary>
        public static void DestroyTiles()
        {
            foreach (var tile in CreatedTiles)
            {
                DemoMaterials.DestroyObject(tile);
            }

            CreatedTiles.Clear();
        }

        public static void Build(Transform parent)
        {
            if (PixelArt.UseArt && BuildGroundTilemap(parent))
            {
                return;
            }

            BuildGroundBlock(parent);
        }

        static bool BuildGroundTilemap(Transform parent)
        {
            var sprites = new Sprite[GrassCells.Length];
            for (var i = 0; i < GrassCells.Length; i++)
            {
                sprites[i] = PixelArt.LoadTile(GrassCells[i].x, GrassCells[i].y);
                if (sprites[i] == null)
                {
                    // 图集缺一张就整块回退：半张 Tilemap 会比整块草地更难看。
                    return false;
                }
            }

            var gridObject = new GameObject("Farm Grid");
            gridObject.transform.SetParent(parent, false);
            var grid = gridObject.AddComponent<Grid>();
            grid.cellSize = new Vector3(1f, 1f, 0f);

            var tilemapObject = new GameObject("Ground");
            tilemapObject.transform.SetParent(gridObject.transform, false);
            var tilemap = tilemapObject.AddComponent<Tilemap>();
            var renderer = tilemapObject.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = SortingOrder.Ground;

            var tiles = new Tile[sprites.Length];
            for (var i = 0; i < sprites.Length; i++)
            {
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sprites[i];
                tile.colliderType = Tile.ColliderType.None;
                tiles[i] = tile;
                CreatedTiles.Add(tile);
            }

            var bounds = ExpandedWalkable();
            for (var y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (var x = bounds.xMin; x < bounds.xMax; x++)
                {
                    // 两个素数相乘再异或：变化不沿某一行/列周期重复，铺开不会出现横向亮带。
                    var hash = unchecked((x * 73856093) ^ (y * 19349663)) & 0x7fffffff;
                    var variant = hash % tiles.Length;
                    tilemap.SetTile(new Vector3Int(x, y, 0), tiles[variant]);
                }
            }

            return true;
        }

        static void BuildGroundBlock(Transform parent)
        {
            var center = FarmLayout.TileCenter(FarmLayout.Walkable);
            var width = FarmLayout.Walkable.width + 4f;
            var height = FarmLayout.Walkable.height + 4f;
            DemoMaterials.Block(
                "Ground",
                parent,
                new Vector3(center.x, center.y, 1f),
                new Vector3(width, height, 0.2f),
                GrassFallback);
        }

        /// <summary>地面比可走范围再向外铺两格，3× 俯视时不会看到地面边缘。</summary>
        static RectInt ExpandedWalkable()
        {
            return new RectInt(
                FarmLayout.Walkable.xMin - 2,
                FarmLayout.Walkable.yMin - 2,
                FarmLayout.Walkable.width + 4,
                FarmLayout.Walkable.height + 4);
        }
    }
}
