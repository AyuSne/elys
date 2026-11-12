using ElysFarm.Demo;
using NUnit.Framework;
using UnityEngine;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #9 双机位：俯视 45° 是默认，可切到任一 NPC 的第三人称跟拍，也能切回俯视。
    /// 测试打 <see cref="CameraRig"/> 这条纯逻辑接缝，不创建相机、不进 Play Mode。
    /// </summary>
    public class CameraRigTests
    {
        [Test]
        public void 默认机位是俯视四十五度()
        {
            var rig = new CameraRig();

            Assert.AreEqual(CameraMode.Overhead, rig.Mode, "开局应是俯视");
            Assert.IsNull(rig.FollowNpcId, "俯视时不跟拍任何人");
            Assert.AreEqual(
                45f,
                Mathf.Abs(Mathf.DeltaAngle(0f, CameraRig.OverheadPose.Rotation.eulerAngles.x)),
                0.01f,
                "验收要求俯视 45°");
            Assert.Greater(CameraRig.OverheadPose.Position.y, 0f, "俯视相机应在农场上方");
            Assert.Less(CameraRig.OverheadPose.Position.z, 0f, "俯视相机应在农场南侧，向 +Z 看");
        }

        [Test]
        public void 切到跟拍后相机落在NPC身后上方()
        {
            var rig = new CameraRig();
            var npcPosition = new Vector3(1f, 0f, 2f);

            Assert.IsTrue(rig.Follow("ahe"));
            Assert.AreEqual(CameraMode.Follow, rig.Mode);
            Assert.IsTrue(rig.IsFollowing("ahe"));

            var pose = CameraRig.FollowPose(npcPosition, Quaternion.identity);

            Assert.AreEqual(npcPosition.x, pose.Position.x, 0.001f, "NPC 朝 +Z 时相机应在正后方");
            Assert.AreEqual(npcPosition.z - CameraRig.FollowDistance, pose.Position.z, 0.001f);
            Assert.Greater(pose.Position.y, npcPosition.y + 1.5f, "跟拍相机要高于 NPC 的肩");

            var forward = pose.Rotation * Vector3.forward;
            var toHead = (npcPosition + Vector3.up * CameraRig.FollowLookHeight - pose.Position).normalized;
            Assert.Greater(Vector3.Dot(forward, toHead), 0.999f, "相机应看向 NPC 的头");
        }

        [Test]
        public void 跟拍姿态随NPC朝向绕到背后()
        {
            var npcPosition = new Vector3(1f, 0f, 2f);
            var pose = CameraRig.FollowPose(npcPosition, Quaternion.Euler(0f, 90f, 0f));

            Assert.AreEqual(npcPosition.x - CameraRig.FollowDistance, pose.Position.x, 0.001f, "NPC 朝 +X 时相机在 -X");
            Assert.AreEqual(npcPosition.z, pose.Position.z, 0.001f);

            var forward = pose.Rotation * Vector3.forward;
            var toHead = (npcPosition + Vector3.up * CameraRig.FollowLookHeight - pose.Position).normalized;
            Assert.Greater(Vector3.Dot(forward, toHead), 0.999f, "绕着转之后仍要看向 NPC 的头");
        }

        [Test]
        public void 可以在三名NPC之间切换并自由切回俯视()
        {
            var rig = new CameraRig();

            Assert.IsTrue(rig.Follow("ahe"));
            Assert.IsTrue(rig.Follow("aya"));
            Assert.IsTrue(rig.IsFollowing("aya"));
            Assert.IsFalse(rig.IsFollowing("ahe"), "跟拍对象应只剩一个");

            rig.Overhead();

            Assert.AreEqual(CameraMode.Overhead, rig.Mode);
            Assert.IsNull(rig.FollowNpcId);
            Assert.AreEqual(new Vector3(0f, 12.5f, -15.5f), CameraRig.OverheadPose.Position, "切回俯视应回到默认机位");
        }

        [Test]
        public void 空id不会把机位切走()
        {
            var rig = new CameraRig();

            Assert.IsFalse(rig.Follow(null));
            Assert.IsFalse(rig.Follow(string.Empty));
            Assert.AreEqual(CameraMode.Overhead, rig.Mode, "空 id 不应切到跟拍");
        }
    }
}
