using ElysFarm.Core;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 一名 NPC 的替身：像素素材下按朝向与行走帧换四方向行走图，停下回到该方向第一帧；
    /// 素材缺失或关掉时退回「胶囊体 + 朝向鼻子」的体块。根节点始终在脚底，移动只动根节点。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcAvatar : MonoBehaviour
    {
        const float BlockZ = -0.2f;
        const float BlockHeight = 1.4f;

        Npc npc;
        SpriteRenderer body;
        SpriteRenderer shadow;
        NpcEmote emote;
        bool art;
        Facing facing = Facing.Down;
        bool walking;
        float walkTime;
        int lastFrame = -1;
        Transform nose;

        /// <summary>脚底的世界坐标；走路与相机跟拍都读它。</summary>
        public Vector2 Feet => new Vector2(transform.position.x, transform.position.y);

        public static NpcAvatar Create(Npc npc, Transform parent, Color fallbackColor)
        {
            var gameObject = new GameObject($"NPC {npc.DisplayName}");
            gameObject.transform.SetParent(parent, false);

            var avatar = gameObject.AddComponent<NpcAvatar>();
            avatar.Build(npc, fallbackColor);
            return avatar;
        }

        void Build(Npc npc, Color fallbackColor)
        {
            this.npc = npc;

            var sprite = PixelArt.LoadCharacter(npc.Id, facing, 0);
            if (sprite == null)
            {
                BuildBlocks(fallbackColor);
                return;
            }

            art = true;

            var shadowSprite = PixelArt.LoadShadow();
            if (shadowSprite != null)
            {
                shadow = SpriteObject.Create("Shadow", transform, shadowSprite, SortingOrder.At(0f, 1));
                shadow.transform.localPosition = new Vector3(0f, 0.06f, 0f);
                shadow.color = new Color(1f, 1f, 1f, 0.45f);
            }

            body = SpriteObject.Create("Body", transform, sprite, SortingOrder.At(0f, 5));
            emote = NpcEmote.Create(transform);
        }

        void BuildBlocks(Color fallbackColor)
        {
            var root = new GameObject("Body");
            root.transform.SetParent(transform, false);

            var bodyBlock = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            bodyBlock.name = "Torso";
            bodyBlock.transform.SetParent(root.transform, false);
            bodyBlock.transform.localPosition = new Vector3(0f, BlockHeight * 0.5f, BlockZ);
            bodyBlock.transform.localScale = new Vector3(0.55f, BlockHeight * 0.5f, 0.4f);
            bodyBlock.GetComponent<Renderer>().sharedMaterial = DemoMaterials.Create($"Npc {npc.Id}", fallbackColor);
            var collider = bodyBlock.GetComponent<Collider>();
            if (collider != null)
            {
                DemoMaterials.DestroyObject(collider);
            }

            var block = DemoMaterials.Block(
                "Nose",
                root.transform,
                Vector3.zero,
                new Vector3(0.2f, 0.2f, 0.2f),
                new Color(0.92f, 0.85f, 0.7f));
            nose = block.transform;
            nose.localPosition = NoseOffset(facing);
        }

        /// <summary>把脚底放到世界坐标；排序随 y 更新。</summary>
        public void SetFeet(Vector2 feet)
        {
            transform.position = new Vector3(feet.x, feet.y, 0f);
            UpdateSorting();
        }

        /// <summary>行走状态；停下时锁回该方向第一帧。</summary>
        public void SetWalking(bool value)
        {
            if (walking == value)
            {
                return;
            }

            walking = value;
            if (!walking)
            {
                walkTime = 0f;
                lastFrame = -1;
            }

            UpdateArt();
        }

        /// <summary>显示头顶表情；体块回退或素材缺失时静默跳过。</summary>
        public void ShowEmote(EmoteEvent emoteEvent, float elapsed)
        {
            emote?.Show(emoteEvent, elapsed);
        }

        /// <summary>收起头顶表情。</summary>
        public void HideEmote()
        {
            emote?.Hide();
        }

        /// <summary>用速度方向更新朝向；主分量决定四方向之一。</summary>
        public void SetVelocity(Vector2 velocity)
        {
            var next = PixelArt.FacingOf(velocity, facing);
            if (next == facing)
            {
                return;
            }

            facing = next;
            lastFrame = -1;
            if (nose != null)
            {
                nose.localPosition = NoseOffset(facing);
            }

            UpdateArt();
        }

        void Update()
        {
            if (!art)
            {
                return;
            }

            if (walking)
            {
                walkTime += Time.deltaTime;
            }

            UpdateArt();
        }

        void UpdateArt()
        {
            if (!art || body == null)
            {
                return;
            }

            var frame = PixelArt.WalkFrame(walking, walkTime);
            if (frame != lastFrame)
            {
                var sprite = PixelArt.LoadCharacter(npc.Id, facing, frame);
                if (sprite != null)
                {
                    body.sprite = sprite;
                    lastFrame = frame;
                }
            }

            UpdateSorting();
        }

        void UpdateSorting()
        {
            if (!art)
            {
                return;
            }

            var feetY = transform.position.y;
            if (body != null)
            {
                body.sortingOrder = SortingOrder.At(feetY, 5);
            }

            if (shadow != null)
            {
                shadow.sortingOrder = SortingOrder.At(feetY, 1);
            }
        }

        static Vector3 NoseOffset(Facing facing)
        {
            switch (facing)
            {
                case Facing.Right:
                    return new Vector3(0.34f, BlockHeight * 0.62f, BlockZ - 0.25f);
                case Facing.Left:
                    return new Vector3(-0.34f, BlockHeight * 0.62f, BlockZ - 0.25f);
                case Facing.Up:
                    return new Vector3(0f, BlockHeight * 0.8f, BlockZ + 0.12f);
                default:
                    return new Vector3(0f, BlockHeight * 0.55f, BlockZ - 0.25f);
            }
        }
    }
}
