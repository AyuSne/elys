using System;
using System.Collections.Generic;

namespace ElysFarm.Core
{
    /// <summary>
    /// 玩家一句话被拆成的一串待执行工步，挂在一名 NPC 名下。
    /// 派单只登记顺序，不改变地块或背包；每个工步真正执行前仍要过 <see cref="ActionValidator"/>。
    /// 因此「玩家说的话」最多只能排出一个候选序列，不能直接改游戏状态。
    /// </summary>
    public sealed class WorkOrder
    {
        /// <summary>超过这么多游戏日没有进展就自动放弃；规格定的是「超过一个游戏日」。</summary>
        public const int AbandonAfterIdleDays = 1;

        readonly List<WorkStep> steps;

        /// <summary>接单的 NPC。</summary>
        public string NpcId { get; }

        /// <summary>工步要落在哪块地。</summary>
        public string FieldId { get; }

        /// <summary>播种用的品种；其它工步不用它。</summary>
        public string SpeciesId { get; }

        /// <summary>按执行顺序排列的工步。</summary>
        public IReadOnlyList<WorkStep> Steps => steps;

        /// <summary>已经执行完的工步数。</summary>
        public int CompletedCount { get; private set; }

        /// <summary>最近一次有进展的游戏日——接单那天也算。超过一个游戏日没进展就会被自动放弃。</summary>
        public int LastProgressDay { get; private set; }

        /// <summary>所有工步都已执行。</summary>
        public bool IsComplete => CompletedCount >= steps.Count;

        /// <summary>下一个待执行的工步；完成后为 null。</summary>
        public WorkStep? NextStep => IsComplete ? (WorkStep?)null : steps[CompletedCount];

        internal WorkOrder(string npcId, string fieldId, string speciesId, IEnumerable<WorkStep> plannedSteps, int day)
        {
            if (string.IsNullOrWhiteSpace(npcId))
            {
                throw new ArgumentException("工单必须指定 NPC", nameof(npcId));
            }

            if (string.IsNullOrWhiteSpace(fieldId))
            {
                throw new ArgumentException("工单必须指定地块", nameof(fieldId));
            }

            if (plannedSteps == null)
            {
                throw new ArgumentNullException(nameof(plannedSteps));
            }

            NpcId = npcId;
            FieldId = fieldId;
            SpeciesId = speciesId;
            steps = new List<WorkStep>(plannedSteps);
            LastProgressDay = day;
            if (steps.Count == 0)
            {
                throw new ArgumentException("工单至少需要一个工步", nameof(plannedSteps));
            }
        }

        /// <summary>把一个工步记为已完成；已完成的工单不能再推进。</summary>
        internal bool Advance()
        {
            if (IsComplete)
            {
                return false;
            }

            CompletedCount++;
            return true;
        }

        /// <summary>记下这天有进展：执行了计划工步、插做了维护，或者 NPC 在休息。</summary>
        internal void MarkProgress(int day)
        {
            LastProgressDay = day;
        }

        /// <summary>到 day 为止已经连续多少个游戏日没有进展；刚有进展时为 0。</summary>
        public int DaysWithoutProgress(int day)
        {
            return day - LastProgressDay;
        }
    }
}
