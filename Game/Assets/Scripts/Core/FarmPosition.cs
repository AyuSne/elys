using System;

namespace ElysFarm.Core
{
    /// <summary>
    /// 农场平面上的一个点（X/Z）。领域层不依赖 UnityEngine，因此用这个值类型
    /// 代替 Vector3 来传递位置，避免 (x, z) 两个浮点数在各处成对出现。
    /// </summary>
    public readonly struct FarmPosition
    {
        /// <summary>原点 (0, 0)。</summary>
        public static readonly FarmPosition Origin = new FarmPosition(0f, 0f);

        /// <summary>X 坐标。</summary>
        public float X { get; }

        /// <summary>Z 坐标。</summary>
        public float Z { get; }

        public FarmPosition(float x, float z)
        {
            X = x;
            Z = z;
        }

        /// <summary>与另一点的平面距离。</summary>
        public float DistanceTo(FarmPosition other)
        {
            var dx = X - other.X;
            var dz = Z - other.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
