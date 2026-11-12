using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>相机档位：俯视看全局，跟拍看一名 NPC 的细节。</summary>
    public enum CameraMode
    {
        Overhead,
        Follow
    }

    /// <summary>
    /// 双档正交机位的纯数据：当前档位、跟拍对象、缩放倍数与观察中心。
    /// 3× 俯视固定看农场中心；4× 跟拍锁定某名 NPC，未知 id 不改档位。
    /// 不碰 <see cref="Camera"/>，由 <see cref="DemoCamera"/> 把中心与缩放应用到场景相机；
    /// 测试打这条缝，不需要打开场景或进入 Play Mode。
    /// </summary>
    public sealed class CameraRig
    {
        /// <summary>俯视倍率：一格 16px 显示为 48 屏幕像素。</summary>
        public const float OverheadZoom = 3f;

        /// <summary>跟拍倍率：一格 16px 显示为 64 屏幕像素。</summary>
        public const float FollowZoom = 4f;

        public CameraMode Mode { get; private set; } = CameraMode.Overhead;

        /// <summary>正在跟拍的 NPC；俯视时为 null。</summary>
        public string FollowNpcId { get; private set; }

        /// <summary>当前倍率：俯视 3×、跟拍 4×。</summary>
        public float Zoom => Mode == CameraMode.Follow ? FollowZoom : OverheadZoom;

        /// <summary>一格在当前倍率下的屏幕像素数。</summary>
        public float TileScreenPixels => FarmLayout.PixelsPerTile * Zoom;

        /// <summary>相机在世界里的观察中心（x 右、y 上）。</summary>
        public Vector2 Center { get; private set; } = FarmLayout.OverheadCenter;

        public bool IsFollowing(string npcId)
        {
            return Mode == CameraMode.Follow
                && string.Equals(FollowNpcId, npcId, System.StringComparison.Ordinal);
        }

        public void Overhead()
        {
            Mode = CameraMode.Overhead;
            FollowNpcId = null;
            Center = FarmLayout.OverheadCenter;
        }

        /// <summary>锁定一名已知 NPC 并切到 4×；未知 id 返回 false 且不改动当前档位。</summary>
        public bool Follow(string npcId)
        {
            if (!FarmLayout.TryGetNpc(npcId, out var spawn))
            {
                return false;
            }

            Mode = CameraMode.Follow;
            FollowNpcId = npcId;
            Center = spawn.World;
            return true;
        }

        /// <summary>跟拍时把观察中心移到 NPC 当前位置；俯视时忽略。</summary>
        public void Track(Vector2 center)
        {
            if (Mode == CameraMode.Follow)
            {
                Center = center;
            }
        }

        /// <summary>
        /// 正交半高（世界单位）：屏幕半高除以每单位屏幕像素数，保证一格正好
        /// <see cref="TileScreenPixels"/> 像素——这就是 3× / 4× 的定义。
        /// </summary>
        public float OrthographicSize(int screenHeight)
        {
            return OrthographicSizeFor(Zoom, screenHeight);
        }

        public static float OrthographicSizeFor(float zoom, int screenHeight)
        {
            return screenHeight / (2f * FarmLayout.PixelsPerTile * zoom);
        }

        /// <summary>把观察中心吸附到屏幕像素网格，避免像素素材在亚像素位置抖动。</summary>
        public static Vector2 SnapToPixel(Vector2 center, float zoom)
        {
            var pixel = 1f / (FarmLayout.PixelsPerTile * zoom);
            return new Vector2(
                Mathf.Round(center.x / pixel) * pixel,
                Mathf.Round(center.y / pixel) * pixel);
        }
    }
}
