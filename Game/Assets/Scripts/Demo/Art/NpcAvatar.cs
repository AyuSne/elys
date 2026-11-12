using System.Collections.Generic;
using ElysFarm.Core;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 一名 NPC 的替身：优先用 Quaternius 的角色模型与 Idle/Walk 动画，
    /// 素材缺失时退回原来的胶囊体。根节点始终在脚底（y=0），移动与转身只动根节点。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcAvatar : MonoBehaviour
    {
        /// <summary>角色模型约 3.3 个单位高，缩到 0.55 后约 1.8 米。</summary>
        public const float ModelScale = 0.55f;

        static List<AnimationClip> allClips;

        GameObject modelInstance;
        AnimationClip idleClip;
        AnimationClip walkClip;
        AnimationClip playingClip;
        float clipTime;
        bool walking;

        /// <summary>是否用了角色模型（false 表示体块回退）。</summary>
        public bool UsingModel => modelInstance != null;

        /// <summary>给 HUD 显示的名字。</summary>
        public string DisplayName { get; private set; }

        public static NpcAvatar Create(Npc npc, Transform parent, Color fallbackColor)
        {
            var go = new GameObject($"NPC {npc.DisplayName}");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(npc.Position.X, 0f, npc.Position.Z);

            var avatar = go.AddComponent<NpcAvatar>();
            avatar.Build(npc, fallbackColor);
            return avatar;
        }

        void Build(Npc npc, Color fallbackColor)
        {
            DisplayName = npc.DisplayName;

            // 脚下的圆盘让俯视时也能分清三个人，同时标出跟拍对象。
            DemoMaterials.Cylinder(
                "Footprint",
                transform,
                new Vector3(transform.position.x, 0.02f, transform.position.z),
                new Vector3(1.05f, 0.02f, 1.05f),
                fallbackColor);

            var model = QuaterniusArt.LoadCharacter(npc.Id);
            if (model == null)
            {
                BuildFallback(fallbackColor);
                return;
            }

            modelInstance = ModelPlacement.Spawn(model, transform, transform.position, ModelScale);
            if (modelInstance == null)
            {
                BuildFallback(fallbackColor);
                return;
            }

            if (allClips == null)
            {
                allClips = new List<AnimationClip>(Resources.LoadAll<AnimationClip>("Quaternius/Characters"));
            }

            idleClip = FindClip("|Idle");
            walkClip = FindClip("|Walk");
            if (idleClip != null)
            {
                playingClip = idleClip;
                idleClip.SampleAnimation(modelInstance, 0f);
            }
        }

        void BuildFallback(Color color)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(transform, false);
            body.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            body.transform.localScale = new Vector3(0.65f, 0.8f, 0.65f);
            body.GetComponent<Renderer>().sharedMaterial = DemoMaterials.Create($"Npc {DisplayName}", color);

            var nose = DemoMaterials.Block(
                "Nose",
                body.transform,
                Vector3.zero,
                new Vector3(0.22f, 0.22f, 0.4f),
                new Color(0.92f, 0.85f, 0.7f));
            nose.transform.localPosition = new Vector3(0f, 0.45f, 0.5f);
        }

        /// <summary>在走路与待机之间切换；切换时从第一帧开始播，避免跑步循环跳帧。</summary>
        public void SetWalking(bool value)
        {
            if (walking == value)
            {
                return;
            }

            walking = value;
            var next = value ? walkClip ?? idleClip : idleClip;
            if (next != playingClip)
            {
                playingClip = next;
                clipTime = 0f;
            }
        }

        void Update()
        {
            if (modelInstance == null || playingClip == null)
            {
                return;
            }

            clipTime = Mathf.Repeat(clipTime + Time.deltaTime, Mathf.Max(0.0001f, playingClip.length));
            playingClip.SampleAnimation(modelInstance, clipTime);
        }

        static AnimationClip FindClip(string suffix)
        {
            foreach (var clip in allClips)
            {
                if (clip.name.EndsWith(suffix, System.StringComparison.Ordinal))
                {
                    return clip;
                }
            }

            return null;
        }
    }
}
