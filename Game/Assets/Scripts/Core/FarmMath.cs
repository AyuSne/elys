using System;

namespace ElysFarm.Core
{
    /// <summary>领域层共用的小工具；不依赖 UnityEngine，保证领域层可独立测试。</summary>
    internal static class FarmMath
    {
        /// <summary>把 0..1 之外的输入夹回区间。</summary>
        public static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }
    }
}
