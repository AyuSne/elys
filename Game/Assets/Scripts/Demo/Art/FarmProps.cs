using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 立在农场上的东西：农舍、鸡舍、水井、产出仓，以及外圈的树、树桩与草丛。
    /// 每一件都以占位底边的中点为落点、按这条边排序——所以人走到树或房子背后会被挡住，
    /// 走到前面就盖住它。
    /// 素材关掉或缺失时，地标退回同尺寸的体块（房子、井、木桶的位置与体量还在，动线照样读得出来），
    /// 装饰整件跳过（它们只压画面边缘，不参与玩法）。
    /// </summary>
    public static class FarmProps
    {
        public static void Build(Transform parent)
        {
            foreach (var landmark in FarmLayout.Landmarks)
            {
                var sprite = PixelArt.LoadLandmark(landmark.Kind);
                if (sprite == null)
                {
                    PlaceBlock(landmark, parent);
                    continue;
                }

                Place($"Landmark {landmark.Kind}", parent, sprite, landmark.Anchor, landmark.SortBase);
            }

            if (!PixelArt.UseArt)
            {
                return;
            }

            foreach (var decoration in FarmLayout.Decorations)
            {
                var sprite = PixelArt.LoadDecoration(decoration.Kind, decoration.Variant);
                if (sprite == null)
                {
                    continue;
                }

                Place(
                    $"Decoration {decoration.Kind} {decoration.Variant}",
                    parent,
                    sprite,
                    decoration.Anchor,
                    decoration.SortBase);
            }
        }

        /// <summary>落点即脚底；排序值由脚底那条线决定。</summary>
        static void Place(string name, Transform parent, Sprite sprite, Vector2 anchor, float sortBase)
        {
            var renderer = SpriteObject.Create(name, parent, sprite, SortingOrder.At(sortBase));
            renderer.transform.position = new Vector3(anchor.x, anchor.y, 0f);
        }

        /// <summary>
        /// 素材缺失时的体块：按占位大小立一个方块，颜色按种类区分。
        /// z 取 0.2——在地面与耕地之上、在作物与角色之下。
        /// </summary>
        static void PlaceBlock(Landmark landmark, Transform parent)
        {
            var tiles = landmark.Tiles;
            var center = FarmLayout.TileCenter(tiles);
            DemoMaterials.Block(
                $"Landmark {landmark.Kind}",
                parent,
                new Vector3(center.x, center.y, 0.2f),
                new Vector3(tiles.width, tiles.height, 0.2f),
                BlockColorOf(landmark.Kind));
        }

        /// <summary>
        /// 体块的颜色：四种要互相分得开，也不能和草地、耕地混在一起——
        /// 体块版本里没有贴图，认位置全靠色块。
        /// </summary>
        static Color BlockColorOf(LandmarkKind kind)
        {
            switch (kind)
            {
                case LandmarkKind.Farmhouse:
                    return new Color(0.55f, 0.32f, 0.20f);
                case LandmarkKind.Coop:
                    return new Color(0.24f, 0.42f, 0.38f);
                case LandmarkKind.Well:
                    return new Color(0.55f, 0.55f, 0.52f);
                default:
                    return new Color(0.72f, 0.52f, 0.18f);
            }
        }
    }
}
