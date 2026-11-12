using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 一名 NPC 头顶的表情：一个挂在角色根节点上的世界空间 <see cref="SpriteRenderer"/>，
    /// 16px 的图按 2× 放大到 32px，底边贴在头顶。显示哪一帧由 <see cref="FarmDemoBootstrap"/>
    /// 按 <see cref="EmoteBoard"/> 的时间决定；出屏或位于相机背后时在 LateUpdate 里不画。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcEmote : MonoBehaviour
    {
        /// <summary>角色一帧 16×32 = 2 世界单位高；表情底边贴在头顶上方一点。</summary>
        public const float HeadHeight = 1.95f;

        /// <summary>16px 的表情按 2× 放大到 32px（2 世界单位）。</summary>
        public const float Scale = 2f;

        SpriteRenderer bubble;
        EmoteEvent current;
        int currentFrame = -1;
        bool showing;

        /// <summary>在角色根节点下建一张头顶表情；角色根节点在脚底，所以位置直接取头顶高度。</summary>
        public static NpcEmote Create(Transform avatarRoot)
        {
            var gameObject = new GameObject("Emote");
            gameObject.transform.SetParent(avatarRoot, false);
            gameObject.transform.localPosition = new Vector3(0f, HeadHeight, 0f);
            gameObject.transform.localScale = new Vector3(Scale, Scale, 1f);

            var image = gameObject.AddComponent<SpriteRenderer>();
            image.sortingOrder = SortingOrder.Emote;
            image.enabled = false;

            var emote = gameObject.AddComponent<NpcEmote>();
            emote.bubble = image;
            return emote;
        }

        /// <summary>显示某件事的表情，并按 elapsed 播到对应帧；素材缺失时静默不画。</summary>
        public void Show(EmoteEvent emote, float elapsed)
        {
            var frame = OverheadEmotes.FrameAt(elapsed);
            if (showing && current == emote && currentFrame == frame)
            {
                return;
            }

            var sprite = PixelArt.LoadEmote(emote, frame);
            current = emote;
            currentFrame = frame;
            showing = sprite != null;
            bubble.sprite = sprite;
            bubble.enabled = showing;
        }

        /// <summary>收起表情；下一次 Show 会重新取图。</summary>
        public void Hide()
        {
            if (!showing && bubble.sprite == null)
            {
                return;
            }

            showing = false;
            currentFrame = -1;
            bubble.sprite = null;
            bubble.enabled = false;
        }

        /// <summary>
        /// 相机在 Update 里移动、NPC 在 Update 里移动，所以可见性放到 LateUpdate 再判。
        /// 出屏或跑到相机背后时只关渲染，信号仍由表情板按 2.5 秒计时。
        /// </summary>
        void LateUpdate()
        {
            if (!showing || bubble.sprite == null)
            {
                return;
            }

            var camera = Camera.main;
            if (camera == null)
            {
                bubble.enabled = false;
                return;
            }

            var origin = camera.transform.position;
            bubble.enabled = OverheadEmotes.IsVisible(
                transform.position,
                new Vector2(origin.x, origin.y),
                camera.orthographicSize * camera.aspect,
                camera.orthographicSize,
                origin.z);
        }
    }
}
