using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 把 <see cref="CameraRig"/> 的姿态应用到场景相机。俯视是固定机位；
    /// 跟拍在 LateUpdate 里追 NPC（NPC 在 Update 里移动），使用指数平滑避免抖动。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DemoCamera : MonoBehaviour
    {
        const float FollowSharpness = 9f;

        /// <summary>演示场景的天空/背景色；场景生成器与运行时共用。</summary>
        public static readonly Color BackgroundColor = new Color(0.55f, 0.66f, 0.78f);

        Camera targetCamera;
        Transform followTarget;
        bool snapNextPose = true;

        /// <summary>把一组机位数据应用到相机；场景生成器与运行时共用同一实现。</summary>
        public static void ApplyPoseTo(Camera camera, CameraPose pose)
        {
            if (camera == null)
            {
                return;
            }

            camera.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            camera.fieldOfView = pose.FieldOfView;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColor;
        }

        /// <summary>机位状态（模式与跟拍对象），HUD 只读。</summary>
        public CameraRig Rig { get; } = new CameraRig();

        public CameraMode Mode => Rig.Mode;

        public string FollowNpcId => Rig.FollowNpcId;

        public void Configure(Camera camera)
        {
            targetCamera = camera;
            if (targetCamera == null)
            {
                return;
            }

            Apply(CameraRig.OverheadPose);
        }

        public void SetOverhead()
        {
            Rig.Overhead();
            followTarget = null;
            snapNextPose = true;
            if (targetCamera != null)
            {
                Apply(CameraRig.OverheadPose);
            }
        }

        public bool Follow(string npcId, Transform target)
        {
            if (target == null || !Rig.Follow(npcId))
            {
                return false;
            }

            followTarget = target;
            snapNextPose = true;
            return true;
        }

        void LateUpdate()
        {
            if (targetCamera == null)
            {
                return;
            }

            if (Rig.Mode != CameraMode.Follow)
            {
                Apply(CameraRig.OverheadPose);
                return;
            }

            if (followTarget == null)
            {
                SetOverhead();
                return;
            }

            var target = CameraRig.FollowPose(followTarget.position, followTarget.rotation);
            if (snapNextPose)
            {
                Apply(target);
                snapNextPose = false;
                return;
            }

            var t = 1f - Mathf.Exp(-FollowSharpness * Time.deltaTime);
            var position = Vector3.Lerp(targetCamera.transform.position, target.Position, t);
            var rotation = Quaternion.Slerp(targetCamera.transform.rotation, target.Rotation, t);
            Apply(new CameraPose(position, rotation, target.FieldOfView));
        }

        void Apply(CameraPose pose)
        {
            ApplyPoseTo(targetCamera, pose);
        }
    }
}
