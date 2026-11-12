using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 立在农场上的东西：农舍、鸡舍、水井、产出仓，以及外圈的树、树桩与草丛。
    /// 每一件都以占位底边的中点为落点，按这条边的 y 排序——所以人走到树或房子背后会被挡住，
    /// 走到前面就盖住它。素材关掉或缺失时整件跳过（这是验收项：体块版本照样能跑），
    /// 缺一张图只少一件东西，不影响别的。
    /// </summary>
    public static class FarmProps
    {
        public static void Build(Transform parent)
        {
            if (!PixelArt.UseArt)
            {
                return;
            }

            foreach (var landmark in FarmLayout.Landmarks)
            {
                var sprite = PixelArt.LoadLandmark(landmark.Kind);
                if (sprite == null)
                {
                    continue;
                }

                Place($"Landmark {landmark.Kind}", parent, sprite, landmark.Anchor, landmark.SortBase);
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
    }
}
