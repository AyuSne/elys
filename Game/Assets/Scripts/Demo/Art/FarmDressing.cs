using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 场景的底：俯视时用运行时 Tilemap 铺满草地，再在上面铺一条土路。
    /// 素材关掉或缺失时退回一整块绿色体块与一条土色带子，布局照样读得出来。
    /// 土路是表现层自己画的贴图——原作图集里没有一条能无缝铺开的土路。
    /// </summary>
    public static class FarmDressing
    {
        static readonly Color GrassFallback = new Color(0.28f, 0.42f, 0.22f);

        static readonly Color RoadFallback = new Color(0.62f, 0.48f, 0.32f);

        /// <summary>土路的两档土色与深色小点，取自目标图的配色。</summary>
        static readonly Color RoadLight = new Color32(166, 130, 88, 255);

        static readonly Color RoadDark = new Color32(158, 123, 82, 255);

        static readonly Color RoadSpeckle = new Color32(142, 110, 72, 255);

        /// <summary>
        /// 草地瓦片在室外图集里的格位：取春季图集里偏亮、铺开接缝不明显的四种，
        /// 深浅各两档，铺满一屏时不会看出一格格的网格。
        /// </summary>
        static readonly Vector2Int[] GrassCells =
        {
            new Vector2Int(0, 11),
            new Vector2Int(2, 16),
            new Vector2Int(0, 13),
            new Vector2Int(2, 13)
        };

        static readonly List<Tile> CreatedTiles = new List<Tile>();

        static readonly List<Sprite> CreatedSprites = new List<Sprite>();

        static readonly List<Texture2D> CreatedTextures = new List<Texture2D>();

        /// <summary>
        /// 重建表现层前调用：Tilemap 的 Tile 是 ScriptableObject，土路的贴图与精灵是运行时创建的，
        /// 都不随场景对象销毁。
        /// </summary>
        public static void DestroyTiles()
        {
            foreach (var tile in CreatedTiles)
            {
                DemoMaterials.DestroyObject(tile);
            }

            foreach (var sprite in CreatedSprites)
            {
                DemoMaterials.DestroyObject(sprite);
            }

            foreach (var texture in CreatedTextures)
            {
                DemoMaterials.DestroyObject(texture);
            }

            CreatedTiles.Clear();
            CreatedSprites.Clear();
            CreatedTextures.Clear();
        }

        public static void Build(Transform parent)
        {
            if (PixelArt.UseArt && BuildTilemaps(parent))
            {
                return;
            }

            BuildGroundBlock(parent);
            BuildRoadBlock(parent);
        }

        /// <summary>铺草地与土路两层 Tilemap；草地素材缺一张就整块回退。</summary>
        static bool BuildTilemaps(Transform parent)
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

            var ground = CreateTilemap(gridObject.transform, "Ground", SortingOrder.Ground);
            var tiles = new Tile[sprites.Length];
            for (var i = 0; i < sprites.Length; i++)
            {
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sprites[i];
                tile.colliderType = Tile.ColliderType.None;
                tiles[i] = tile;
                CreatedTiles.Add(tile);
            }

            var bounds = FarmLayout.Drawn;
            for (var y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (var x = bounds.xMin; x < bounds.xMax; x++)
                {
                    // 两个素数相乘再异或：变化不沿某一行/列周期重复，铺开不会出现横向亮带。
                    var hash = unchecked((x * 73856093) ^ (y * 19349663)) & 0x7fffffff;
                    var variant = hash % tiles.Length;
                    ground.SetTile(new Vector3Int(x, y, 0), tiles[variant]);
                }
            }

            BuildRoadTilemap(gridObject.transform);
            return true;
        }

        /// <summary>土路单独一层：铺在草地上，耕地与所有立起来的东西都在它之上。</summary>
        static void BuildRoadTilemap(Transform gridTransform)
        {
            var road = CreateTilemap(gridTransform, "Road", SortingOrder.Road);

            var light = CreateRoadTile(CreateRoadSprite(true));
            var dark = CreateRoadTile(CreateRoadSprite(false));
            foreach (var tile in FarmLayout.RoadTiles)
            {
                // 明暗交错，铺开才看得出是一块块土，不是一条纯色带。
                var picked = (tile.x + tile.y) % 2 == 0 ? light : dark;
                road.SetTile(new Vector3Int(tile.x, tile.y, 0), picked);
            }
        }

        static Tilemap CreateTilemap(Transform parent, string name, int sortingOrder)
        {
            var tilemapObject = new GameObject(name);
            tilemapObject.transform.SetParent(parent, false);
            var tilemap = tilemapObject.AddComponent<Tilemap>();
            var renderer = tilemapObject.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = sortingOrder;
            return tilemap;
        }

        static Tile CreateRoadTile(Sprite sprite)
        {
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.colliderType = Tile.ColliderType.None;
            CreatedTiles.Add(tile);
            return tile;
        }

        /// <summary>
        /// 画一张 16×16 的土路贴图：底色上一格撒六七个小点，位置由定值散列决定，每次结果都一样。
        /// </summary>
        static Sprite CreateRoadSprite(bool light)
        {
            const int size = FarmLayout.PixelsPerTile;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = light ? "Road Light" : "Road Dark",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var baseColor = light ? RoadLight : RoadDark;
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var hash = unchecked((x * 73856093) ^ (y * 19349663)) & 0x7fffffff;
                    pixels[y * size + x] = hash % 23 == 0 ? RoadSpeckle : baseColor;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            CreatedTextures.Add(texture);

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                FarmLayout.PixelsPerTile,
                0,
                SpriteMeshType.FullRect);
            sprite.name = texture.name;
            CreatedSprites.Add(sprite);
            return sprite;
        }

        static void BuildGroundBlock(Transform parent)
        {
            var center = FarmLayout.TileCenter(FarmLayout.Drawn);
            var width = FarmLayout.Drawn.width + 4f;
            var height = FarmLayout.Drawn.height + 4f;
            DemoMaterials.Block(
                "Ground",
                parent,
                new Vector3(center.x, center.y, 1f),
                new Vector3(width, height, 0.2f),
                GrassFallback);
        }

        /// <summary>体块回退下的土路：主路一条带子，加上几条岔路的小块。</summary>
        static void BuildRoadBlock(Transform parent)
        {
            var hasMainRow = false;
            var minX = int.MaxValue;
            var maxX = int.MinValue;
            foreach (var tile in FarmLayout.RoadTiles)
            {
                if (tile.y != FarmLayout.RoadRow)
                {
                    DemoMaterials.Block(
                        $"Road Spur {tile.x},{tile.y}",
                        parent,
                        new Vector3(tile.x + 0.5f, tile.y + 0.5f, 0.6f),
                        new Vector3(1f, 1f, 0.1f),
                        RoadFallback);
                    continue;
                }

                hasMainRow = true;
                minX = Mathf.Min(minX, tile.x);
                maxX = Mathf.Max(maxX, tile.x);
            }

            if (!hasMainRow)
            {
                return;
            }

            DemoMaterials.Block(
                "Road",
                parent,
                new Vector3((minX + maxX + 1) * 0.5f, FarmLayout.RoadRow + 0.5f, 0.6f),
                new Vector3(maxX - minX + 1, 1f, 0.1f),
                RoadFallback);
        }
    }
}
