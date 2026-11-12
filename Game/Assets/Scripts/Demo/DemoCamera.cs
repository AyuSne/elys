using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 把 <see cref="CameraRig"/> 的档位与观察中心应用到场景相机。
    /// 相机始终是正交的：俯视 3× 固定看农场中心，跟拍 4× 追 NPC 并把中心吸附到像素网格。
    /// 跟拍在 LateUpdate 里追（NPC 在 Update 里移动），使用指数平滑避免抖动。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DemoCamera : MonoBehaviour
    {
        const float FollowSharpness = 9f;

        /// <summary>跟拍时把 NPC 的脚底往上抬一点，让身体落在画面正中。</summary>
        public const float FollowLookHeight = 0.75f;

        /// <summary>相机没有地面可画时的背景色；与草地接近，漏边也不刺眼。</summary>
        public static readonly Color BackgroundColor = new Color(0.18f, 0.27f, 0.15f);

        Camera targetCamera;
        Transform followTarget;
        Vector2 center;
        bool snapNextPose = true;

        /// <summary>机位状态（档位、跟拍对象与缩放），HUD 只读。</summary>
        public CameraRig Rig { get; } = new CameraRig();

        public CameraMode Mode => Rig.Mode;

        public string FollowNpcId => Rig.FollowNpcId;

        /// <summary>场景生成器与运行时共用：把相机摆成俯视 3× 的初始状态。</summary>
        public static void ApplyOverviewTo(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            camera.orthographic = true;
            camera.orthographicSize = CameraRig.OrthographicSizeFor(CameraRig.OverviewZoom, 720);
            camera.transform.SetPositionAndRotation(
                new Vector3(FarmLayout.OverviewCenter.x, FarmLayout.OverviewCenter.y, -10f),
                Quaternion.identity);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColor;
        }

        public void Configure(Camera camera)
        {
            targetCamera = camera;
            if (targetCamera == null)
            {
                return;
            }

            Rig.Overview();
            center = FarmLayout.OverviewCenter;
            snapNextPose = true;
            ApplyCamera();
        }

        public void SetOverhead()
        {
            Rig.Overview();
            followTarget = null;
            center = FarmLayout.OverviewCenter;
            snapNextPose = true;
            ApplyCamera();
        }

        public bool Follow(string npcId, Transform target)
        {
            if (target == null || !Rig.Follow(npcId))
            {
                return false;
            }

            followTarget = target;
            center = DesiredCenter();
            snapNextPose = true;
            return true;
        }

        void LateUpdate()
        {
            if (targetCamera == null)
            {
                return;
            }

            if (Rig.Mode == CameraMode.Follow && followTarget == null)
            {
                SetOverhead();
                return;
            }

            var desired = DesiredCenter();
            if (Rig.Mode == CameraMode.Follow)
            {
                Rig.Track(desired);
            }

            if (snapNextPose)
            {
                center = desired;
                snapNextPose = false;
            }
            else
            {
                var t = 1f - Mathf.Exp(-FollowSharpness * Time.deltaTime);
                center = Vector2.Lerp(center, desired, t);
            }

            center = CameraRig.SnapToPixel(center, Rig.Zoom);
            ApplyCamera();
        }

        Vector2 DesiredCenter()
        {
            if (Rig.Mode == CameraMode.Follow && followTarget != null)
            {
                var position = followTarget.position;
                return new Vector2(position.x, position.y + FollowLookHeight);
            }

            return FarmLayout.OverviewCenter;
        }

        void ApplyCamera()
        {
            if (targetCamera == null)
            {
                return;
            }

            targetCamera.orthographic = true;
            targetCamera.orthographicSize = Rig.OrthographicSize(Screen.height);
            targetCamera.transform.SetPositionAndRotation(
                new Vector3(center.x, center.y, -10f),
                Quaternion.identity);
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = BackgroundColor;
        }
    }
}
