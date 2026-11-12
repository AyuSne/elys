using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>2D 场景的排序：按世界 y 从远到近（y 越小越靠前）。</summary>
    public static class SortingOrder
    {
        public const int Ground = -10000;

        public const int Soil = -9000;

        public static int At(float worldY, int bias = 0)
        {
            // Renderer.sortingOrder 是 16 位有符号整数：基数必须小，否则会回绕到负数并被地面盖住。
            return -Mathf.RoundToInt(worldY * 100f) + bias;
        }
    }
}
