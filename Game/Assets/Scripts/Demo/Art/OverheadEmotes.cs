using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>四种会冒头顶表情的事件（见 #15）：接单、被拒、去休息、转述。</summary>
    public enum EmoteEvent
    {
        Accepted,
        Rejected,
        Resting,
        Retelling
    }

    /// <summary>一条正在冒的表情：谁、因为哪件事、从什么时候开始。</summary>
    public readonly struct ActiveEmote
    {
        public ActiveEmote(string npcId, EmoteEvent emote, float startedAt)
        {
            NpcId = npcId;
            Event = emote;
            StartedAt = startedAt;
        }

        public string NpcId { get; }

        public EmoteEvent Event { get; }

        public float StartedAt { get; }

        /// <summary>从冒出来到 now 经过了多久；防负数。</summary>
        public float ElapsedAt(float now)
        {
            return Mathf.Max(0f, now - StartedAt);
        }

        /// <summary>是否已经满 2.5 秒；到点即消失。</summary>
        public bool IsExpiredAt(float now)
        {
            return now - StartedAt >= OverheadEmotes.Duration;
        }
    }

    /// <summary>
    /// 头顶表情板：只记录「谁在什么时候因为什么冒了表情」，不碰 <see cref="ElysFarm.Core.FarmWorld"/>。
    /// 同时最多三个（农场也正好三名 NPC）、同一个人再冒会替换上一张，2.5 秒后自动过期。
    /// 纯数据，测试直接喂时间，不需要打开场景。
    /// </summary>
    public sealed class EmoteBoard
    {
        readonly List<ActiveEmote> active = new List<ActiveEmote>();

        /// <summary>现在还在冒的表情，按冒出来的先后排列。</summary>
        public IReadOnlyList<ActiveEmote> ActiveAt(float now)
        {
            Prune(now);
            return active;
        }

        /// <summary>让某名 NPC 因为一件事冒一个表情；同一人的旧表情会被替换。</summary>
        public void Post(string npcId, EmoteEvent emote, float now)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                return;
            }

            Prune(now);
            for (var i = 0; i < active.Count; i++)
            {
                if (string.Equals(active[i].NpcId, npcId, StringComparison.Ordinal))
                {
                    active.RemoveAt(i);
                    break;
                }
            }

            active.Add(new ActiveEmote(npcId, emote, now));

            // 列表按时间先后追加；超出上限时从最早的那张开始挤掉。
            while (active.Count > OverheadEmotes.MaxConcurrent)
            {
                active.RemoveAt(0);
            }
        }

        /// <summary>取某名 NPC 此刻的表情；没有或已过期返回 false。</summary>
        public bool TryGet(string npcId, float now, out ActiveEmote emote)
        {
            Prune(now);
            foreach (var candidate in active)
            {
                if (string.Equals(candidate.NpcId, npcId, StringComparison.Ordinal))
                {
                    emote = candidate;
                    return true;
                }
            }

            emote = default;
            return false;
        }

        void Prune(float now)
        {
            active.RemoveAll(candidate => candidate.IsExpiredAt(now));
        }
    }

    /// <summary>
    /// 头顶表情的规则：四种事件各用表情图集里的哪一行、每行四帧怎么切、
    /// 2.5 秒怎么分配到四帧。全部是纯函数，测试不打开场景、不加载贴图。
    /// </summary>
    public static class OverheadEmotes
    {
        /// <summary>一张表情在头顶停留的时间。</summary>
        public const float Duration = 2.5f;

        /// <summary>同时最多冒几张；农场正好三名 NPC。</summary>
        public const int MaxConcurrent = 3;

        /// <summary>图集里一格 16×16；按 2× 放大到 32。</summary>
        public const int FrameSize = 16;

        /// <summary>每种表情四帧。</summary>
        public const int FrameCount = 4;

        /// <summary>弹出动画每帧的秒数；四帧播完停在最后一帧。</summary>
        public const float SecondsPerFrame = 0.1f;

        /// <summary>表情图集在 Resources/PixelArt 下的名字。</summary>
        public const string AtlasPath = "Emotes/emotes";

        /// <summary>
        /// 事件对应表情图集的行号，沿用原作图集的语义：
        /// 第 4 行感叹号（接单）、第 3 行恼火（被拒）、第 6 行睡觉（去休息）、第 10 行省略号（转述）。
        /// </summary>
        public static int RowOf(EmoteEvent emote)
        {
            switch (emote)
            {
                case EmoteEvent.Accepted:
                    return 4;
                case EmoteEvent.Rejected:
                    return 3;
                case EmoteEvent.Resting:
                    return 6;
                case EmoteEvent.Retelling:
                    return 10;
                default:
                    return -1;
            }
        }

        /// <summary>某事件某帧在图集里的格位；帧号或事件越界返回无效格位。</summary>
        public static SpriteCell CellOf(EmoteEvent emote, int frame)
        {
            var row = RowOf(emote);
            return row < 0 || frame < 0 || frame >= FrameCount
                ? default
                : new SpriteCell(frame, row, FrameSize, FrameSize);
        }

        /// <summary>表情冒出来 elapsed 秒时该播第几帧：四帧弹出，之后定格在最后一帧。</summary>
        public static int FrameAt(float elapsed)
        {
            if (elapsed <= 0f)
            {
                return 0;
            }

            return Mathf.Min(FrameCount - 1, Mathf.FloorToInt(elapsed / SecondsPerFrame));
        }

        /// <summary>
        /// 世界坐标是否在正交相机的可见范围里。相机背后（z 不大于相机 z）一律不画；
        /// 视野四边算可见，免得 NPC 刚走到边上表情就闪掉。
        /// </summary>
        public static bool IsVisible(
            Vector3 world,
            Vector2 cameraCenter,
            float halfWidth,
            float halfHeight,
            float cameraZ)
        {
            if (world.z <= cameraZ)
            {
                return false;
            }

            return world.x >= cameraCenter.x - halfWidth
                && world.x <= cameraCenter.x + halfWidth
                && world.y >= cameraCenter.y - halfHeight
                && world.y <= cameraCenter.y + halfHeight;
        }
    }
}
