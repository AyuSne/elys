using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>2D 场景的排序：按世界 y 从远到近（y 越小越靠前）。</summary>
    public static class SortingOrder
    {
        public const int Ground = -10000;

        /// <summary>土路铺在草地上，但仍在耕地与所有立起来的东西之下。</summary>
        public const int Road = -9500;

        public const int Soil = -9000;

        /// <summary>
        /// 头顶表情是反馈层：不用 y 排序，压在农场上所有东西之上，免得被树或房子挡住。
        /// 仍要落在 SpriteRenderer 的 16 位有符号范围内。
        /// </summary>
        public const int Emote = 30000;

        public static int At(float worldY, int bias = 0)
        {
            // Renderer.sortingOrder 是 16 位有符号整数：基数必须小，否则会回绕到负数并被地面盖住。
            return -Mathf.RoundToInt(worldY * 100f) + bias;
        }
    }
}
