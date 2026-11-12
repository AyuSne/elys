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

        Camera targetCamera;
        Transform followTarget;
        bool snapNextPose = true;

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
            targetCamera.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            targetCamera.fieldOfView = pose.FieldOfView;
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = new Color(0.55f, 0.66f, 0.78f);
        }
    }
}
