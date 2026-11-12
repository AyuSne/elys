using ElysFarm.Demo;
using NUnit.Framework;
using UnityEngine;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #12 双档正交机位：俯视固定 3×（一格 48px），跟拍锁定某名 NPC 并切到 4×。
    /// 测试打 <see cref="CameraRig"/> 这条纯数据接缝，不创建相机、不进 Play Mode。
    /// </summary>
    public class CameraRigTests
    {
        [Test]
        public void 默认是俯视三倍正交()
        {
            var rig = new CameraRig();

            Assert.AreEqual(CameraMode.Overview, rig.Mode, "开局应是俯视");
            Assert.IsNull(rig.FollowNpcId, "俯视时不跟拍任何人");
            Assert.AreEqual(3f, rig.Zoom, 0.0001f, "俯视固定 3×");
            Assert.AreEqual(48f, rig.TileScreenPixels, 0.0001f, "一格 16px × 3 = 48 屏幕像素");
            Assert.AreEqual(FarmLayout.OverviewCenter, rig.Center, "俯视看向农场中心");
            Assert.AreEqual(7.5f, rig.OrthographicSize(720), 0.0001f, "720 高、48px/格时正交半高 7.5 格");
            Assert.AreEqual(11.25f, rig.OrthographicSize(1080), 0.0001f);
        }

        [Test]
        public void 跟拍锁定某名NPC并切到四倍()
        {
            var rig = new CameraRig();

            Assert.IsTrue(rig.Follow(FarmDemoBootstrap.AheId), "已知 NPC 应能跟拍");
            Assert.AreEqual(CameraMode.Follow, rig.Mode);
            Assert.IsTrue(rig.IsFollowing(FarmDemoBootstrap.AheId));
            Assert.IsFalse(rig.IsFollowing(FarmDemoBootstrap.AyaId));
            Assert.AreEqual(4f, rig.Zoom, 0.0001f, "跟拍切到 4×");
            Assert.AreEqual(64f, rig.TileScreenPixels, 0.0001f, "一格 16px × 4 = 64 屏幕像素");
            Assert.AreEqual(5.625f, rig.OrthographicSize(720), 0.0001f, "4× 时正交半高应减半再减");

            Assert.IsTrue(FarmLayout.TryGetNpc(FarmDemoBootstrap.AheId, out var spawn));
            Assert.AreEqual(spawn.World, rig.Center, "切到跟拍时先落在该 NPC 的起点");

            rig.Track(new Vector2(3.25f, -1.5f));
            Assert.AreEqual(new Vector2(3.25f, -1.5f), rig.Center, "跟拍时观察中心随 NPC 移动");
        }

        [Test]
        public void 未知id不会把档位切走()
        {
            var rig = new CameraRig();

            Assert.IsFalse(rig.Follow("nobody"), "未知 NPC 不应进入跟拍");
            Assert.AreEqual(CameraMode.Overview, rig.Mode);
            Assert.AreEqual(3f, rig.Zoom, 0.0001f);

            Assert.IsTrue(rig.Follow(FarmDemoBootstrap.AyaId));
            Assert.IsFalse(rig.Follow("nobody"), "未知 NPC 不应改掉当前跟拍对象");
            Assert.IsTrue(rig.IsFollowing(FarmDemoBootstrap.AyaId));
            Assert.AreEqual(4f, rig.Zoom, 0.0001f);
        }

        [Test]
        public void 可以在三名NPC之间切换并自由切回俯视()
        {
            var rig = new CameraRig();

            Assert.IsTrue(rig.Follow(FarmDemoBootstrap.AheId));
            Assert.IsTrue(rig.Follow(FarmDemoBootstrap.AyaId));
            Assert.IsTrue(rig.IsFollowing(FarmDemoBootstrap.AyaId));
            Assert.IsFalse(rig.IsFollowing(FarmDemoBootstrap.AheId), "跟拍对象应只剩一个");

            rig.Track(new Vector2(9f, 9f));
            rig.Overview();

            Assert.AreEqual(CameraMode.Overview, rig.Mode);
            Assert.IsNull(rig.FollowNpcId);
            Assert.AreEqual(3f, rig.Zoom, 0.0001f, "切回俯视应回到 3×");
            Assert.AreEqual(FarmLayout.OverviewCenter, rig.Center, "切回俯视应回到农场中心");
        }

        [Test]
        public void 正交尺寸按屏幕高度换算且像素吸附到屏幕像素()
        {
            Assert.AreEqual(8f, CameraRig.OrthographicSizeFor(3f, 768), 0.0001f);
            Assert.AreEqual(6f, CameraRig.OrthographicSizeFor(4f, 768), 0.0001f);

            var snapped = CameraRig.SnapToPixel(new Vector2(1.01f, -0.49f), 3f);
            var pixel = 1f / (FarmLayout.PixelsPerTile * 3f);
            Assert.AreEqual(1.01f, snapped.x, pixel, "吸附误差不超过一个屏幕像素");
            Assert.AreEqual(-0.49f, snapped.y, pixel);

            var exact = CameraRig.SnapToPixel(new Vector2(0.5f, -0.25f), 4f);
            Assert.AreEqual(0.5f, exact.x, 0.00001f, "已在像素网格上的中心不应被挪动");
            Assert.AreEqual(-0.25f, exact.y, 0.00001f);
        }
    }
}
