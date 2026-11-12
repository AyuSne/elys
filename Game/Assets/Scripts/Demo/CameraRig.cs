using System;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>相机模式：默认俯视 45°，可切到任一 NPC 的第三人称跟拍。</summary>
    public enum CameraMode
    {
        Overhead,
        Follow
    }

    /// <summary>一次机位的完整描述；场景生成器与运行时共用。</summary>
    public readonly struct CameraPose
    {
        public CameraPose(Vector3 position, Quaternion rotation, float fieldOfView)
        {
            Position = position;
            Rotation = rotation;
            FieldOfView = fieldOfView;
        }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public float FieldOfView { get; }
    }

    /// <summary>
    /// 双机位的纯逻辑：当前模式、跟拍对象与两种机位的姿态。
    /// 不碰 <see cref="Camera"/>，由 <see cref="DemoCamera"/> 把姿态应用到场景里的相机；
    /// 测试打这条缝，不需要打开场景或进入 Play Mode。
    /// </summary>
    public sealed class CameraRig
    {
        /// <summary>俯视机位的俯角（验收要求 45°）。</summary>
        public const float OverheadPitch = 45f;

        public const float OverheadFieldOfView = 45f;

        public const float FollowDistance = 4.2f;

        public const float FollowHeight = 2.0f;

        public const float FollowLookHeight = 1.45f;

        public const float FollowFieldOfView = 50f;

        /// <summary>默认俯视机位，看向农场中心（NPC 一线），让田间与人都落在 HUD 中间的空档里。</summary>
        public static CameraPose OverheadPose =>
            new CameraPose(new Vector3(0f, 12.5f, -15.5f), Quaternion.Euler(OverheadPitch, 0f, 0f), OverheadFieldOfView);

        public CameraMode Mode { get; private set; } = CameraMode.Overhead;

        /// <summary>正在跟拍的 NPC；俯视时为 null。</summary>
        public string FollowNpcId { get; private set; }

        public bool IsFollowing(string npcId)
        {
            return Mode == CameraMode.Follow && string.Equals(FollowNpcId, npcId, StringComparison.Ordinal);
        }

        public void Overhead()
        {
            Mode = CameraMode.Overhead;
            FollowNpcId = null;
        }

        public bool Follow(string npcId)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                return false;
            }

            FollowNpcId = npcId;
            Mode = CameraMode.Follow;
            return true;
        }

        /// <summary>某名 NPC 站在原地、朝某方向时的跟拍机位。</summary>
        public static CameraPose FollowPose(Vector3 npcPosition, Quaternion npcRotation)
        {
            var forward = npcRotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            var position = npcPosition - forward * FollowDistance + Vector3.up * FollowHeight;
            var target = npcPosition + Vector3.up * FollowLookHeight;
            var rotation = Quaternion.LookRotation(target - position, Vector3.up);
            return new CameraPose(position, rotation, FollowFieldOfView);
        }
    }
}
