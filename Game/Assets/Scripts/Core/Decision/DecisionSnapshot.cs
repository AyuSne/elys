using System.Collections.Generic;

namespace ElysFarm.Core
{
    /// <summary>NPC 的状态条：体力、心情、饥饿，均为 0..1。</summary>
    public readonly struct NpcStatus
    {
        public NpcStatus(float stamina, float mood, float hunger)
        {
            Stamina = stamina;
            Mood = mood;
            Hunger = hunger;
        }

        /// <summary>体力，0..1；执行工步时消耗，休息与跨日恢复。</summary>
        public float Stamina { get; }

        /// <summary>心情，0..1；干活下降，休息与跨日回升。</summary>
        public float Mood { get; }

        /// <summary>饥饿，0..1（1 = 最饿）；干活上升，跨日进食后回落。</summary>
        public float Hunger { get; }
    }

    /// <summary>一条被检索命中的记忆及其权重。</summary>
    public readonly struct MemoryHit
    {
        public MemoryHit(string text, int day, float weight)
        {
            Text = text;
            Day = day;
            Weight = weight;
        }

        /// <summary>记忆正文。</summary>
        public string Text { get; }

        /// <summary>记忆发生时的游戏日。</summary>
        public int Day { get; }

        /// <summary>检索权重，0..1；决策可视化用权重条展示。</summary>
        public float Weight { get; }
    }

    /// <summary>
    /// 决策可视化层的一次只读快照，恰好五项：记忆、意图、候选动作、最近一次模型调用、状态条。
    /// 不包含 prompt 原文，也不包含 token 流；UI 只渲染快照，不持有领域状态。
    /// </summary>
    public sealed class DecisionSnapshot
    {
        internal DecisionSnapshot(
            string npcId,
            IReadOnlyList<MemoryHit> memories,
            string intent,
            IReadOnlyList<StepCandidate> candidates,
            ModelCallInfo modelCall,
            NpcStatus status)
        {
            NpcId = npcId;
            Memories = memories;
            Intent = intent;
            Candidates = candidates;
            ModelCall = modelCall;
            Status = status;
        }

        /// <summary>快照对应的 NPC。</summary>
        public string NpcId { get; }

        /// <summary>第 1 项：检索命中的记忆，最多三条，按权重降序。</summary>
        public IReadOnlyList<MemoryHit> Memories { get; }

        /// <summary>第 2 项：该 NPC 此刻的意图。</summary>
        public string Intent { get; }

        /// <summary>第 3 项：候选工步；无效候选自带动作校验给出的否决原因。</summary>
        public IReadOnlyList<StepCandidate> Candidates { get; }

        /// <summary>第 4 项：最近一次模型调用（模型名 / 耗时 / token）。</summary>
        public ModelCallInfo ModelCall { get; }

        /// <summary>第 5 项：状态条（体力 / 心情 / 饥饿）。</summary>
        public NpcStatus Status { get; }
    }
}
