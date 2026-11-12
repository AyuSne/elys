using ElysFarm.Core;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 一块地的表现：3×3 的耕地、九株作物。像素素材下按阶段换图集格位；
    /// 素材关掉或缺失时，同一套格位改放土块与体块作物，状态照旧由 <see cref="Field"/> 决定。
    /// </summary>
    public sealed class CropFieldView
    {
        /// <summary>水分高于此值就换成湿土图；两边用同一个阈值，避免土色和作物阶段打架。</summary>
        const float WetWaterThreshold = 0.05f;

        readonly Field field;
        Transform root;
        Transform cropRoot;
        RectInt tiles;
        CropStage stage = CropStage.None;
        bool built;
        bool watered;
        SpriteRenderer[] soilTiles;
        GameObject soilBlock;
        Material soilBlockMaterial;

        public CropFieldView(Field field)
        {
            this.field = field;
        }

        public void Build(Transform parent)
        {
            root = new GameObject($"Field {field.Id}").transform;
            root.SetParent(parent, false);

            if (FarmLayout.TryGetField(field.Id, out var footprint))
            {
                tiles = footprint.Tiles;
            }
            else
            {
                // 布局里没有的地块（测试或临时场景）就围着领域坐标铺 3×3，不让表现层崩。
                var x = Mathf.RoundToInt(field.Position.X) - 1;
                var y = Mathf.RoundToInt(field.Position.Z) - 1;
                tiles = new RectInt(x, y, FarmLayout.FieldTiles, FarmLayout.FieldTiles);
            }

            BuildSoil();

            cropRoot = new GameObject("Crops").transform;
            cropRoot.SetParent(root, false);

            built = true;
            stage = CropStage.None;
            Refresh();
        }

        /// <summary>每帧同步干湿度与作物阶段；只有阶段变化时才重建作物。</summary>
        public void Refresh()
        {
            if (!built)
            {
                return;
            }

            var wetNow = field.Water >= WetWaterThreshold;
            if (wetNow != watered)
            {
                watered = wetNow;
                UpdateSoilLook();
            }
            else if (soilBlockMaterial != null)
            {
                // 体块回退时土色跟水分连续变化，和旧版一样能看出在变干。
                DemoMaterials.SetColor(soilBlockMaterial, DemoMaterials.SoilColor(field.Water));
            }

            var next = CropStages.Of(field.State, field.Growth);
            if (next != stage)
            {
                stage = next;
                RebuildCrop();
            }
        }

        void BuildSoil()
        {
            if (PixelArt.UseArt && PixelArt.LoadHoeDirt(false) != null)
            {
                soilTiles = new SpriteRenderer[tiles.width * tiles.height];
                var index = 0;
                for (var y = 0; y < tiles.height; y++)
                {
                    for (var x = 0; x < tiles.width; x++)
                    {
                        var position = FarmLayout.TileCenter(new Vector2Int(tiles.x + x, tiles.y + y));
                        var soil = SpriteObject.Create(
                            $"Soil {tiles.x + x},{tiles.y + y}",
                            root,
                            PixelArt.LoadHoeDirt(field.Water >= WetWaterThreshold),
                            SortingOrder.Soil);
                        soil.transform.position = new Vector3(position.x, position.y, 0f);
                        soilTiles[index++] = soil;
                    }
                }

                return;
            }

            var center = FarmLayout.TileCenter(tiles);
            var block = DemoMaterials.Block(
                $"Soil {field.Id}",
                root,
                new Vector3(center.x, center.y, 0.4f),
                new Vector3(tiles.width, tiles.height, 0.15f),
                DemoMaterials.SoilColor(field.Water));
            soilBlock = block;
            soilBlockMaterial = block.GetComponent<Renderer>().sharedMaterial;
        }

        void UpdateSoilLook()
        {
            var sprite = PixelArt.LoadHoeDirt(watered);
            if (soilTiles != null)
            {
                foreach (var soil in soilTiles)
                {
                    if (soil != null && sprite != null)
                    {
                        soil.sprite = sprite;
                    }
                }

                return;
            }

            DemoMaterials.SetColor(soilBlockMaterial, DemoMaterials.SoilColor(field.Water));
        }

        void RebuildCrop()
        {
            ClearChildren(cropRoot);

            if (stage == CropStage.None || field.Crop == null)
            {
                return;
            }

            var speciesId = field.Crop.Id;
            var sprite = PixelArt.LoadCrop(speciesId, stage);
            if (sprite != null)
            {
                for (var y = 0; y < tiles.height; y++)
                {
                    for (var x = 0; x < tiles.width; x++)
                    {
                        var position = FarmLayout.TileCenter(new Vector2Int(tiles.x + x, tiles.y + y));
                        var crop = SpriteObject.Create(
                            $"Crop {field.Id} {tiles.x + x},{tiles.y + y}",
                            cropRoot,
                            sprite,
                            SortingOrder.At(position.y, 1));
                        crop.transform.position = new Vector3(position.x, position.y, 0f);
                    }
                }

                return;
            }

            // 体块回退：一株一个方块，高度随阶段长高，成熟变金黄。
            var height = 0.28f + CropStages.ScaleOf(stage) * 1.05f;
            var color = CropStages.ColorOf(stage);
            for (var y = 0; y < tiles.height; y++)
            {
                for (var x = 0; x < tiles.width; x++)
                {
                    var position = FarmLayout.TileCenter(new Vector2Int(tiles.x + x, tiles.y + y));
                    DemoMaterials.Block(
                        $"Crop {field.Id} {tiles.x + x},{tiles.y + y}",
                        cropRoot,
                        new Vector3(position.x, position.y + height * 0.5f, 0f),
                        new Vector3(0.55f, height, 0.4f),
                        color);
                }
            }
        }

        static void ClearChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                DemoMaterials.DestroyObject(parent.GetChild(i).gameObject);
            }
        }
    }
}
