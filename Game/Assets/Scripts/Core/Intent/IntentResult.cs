using System.Collections.Generic;

namespace ElysFarm.Core
{
    /// <summary>
    /// 一次玩家输入经过意图出口后的结果。指挥官线的唯一可见产物：
    /// 要么是三个出口之一（其中农事指令可能附带一张工单），要么是一句拒绝。
    /// 地块、品种与计划工步都从工单读取，不另存一份，避免两处状态漂移。
    /// </summary>
    public sealed class IntentResult
    {
        IntentResult(
            IntentOutlet outlet,
            bool accepted,
            string reply,
            WorkOrder assignedWorkOrder,
            bool usedFallback,
            ModelCallInfo call)
        {
            Outlet = outlet;
            Accepted = accepted;
            Reply = reply ?? string.Empty;
            AssignedWorkOrder = assignedWorkOrder;
            UsedFallback = usedFallback;
            Call = call;
        }

        /// <summary>三个出口之一。</summary>
        public IntentOutlet Outlet { get; }

        /// <summary>农事指令是否被接单；其它出口恒为 false。</summary>
        public bool Accepted { get; }

        /// <summary>NPC 的回应或拒绝台词；必须可读。</summary>
        public string Reply { get; }

        /// <summary>已登记的工单；未接单为 null。</summary>
        public WorkOrder AssignedWorkOrder { get; }

        /// <summary>接单的地块；未接单为 null。</summary>
        public string FieldId => AssignedWorkOrder?.FieldId;

        /// <summary>播种品种；未接单或非播种为 null。</summary>
        public string SpeciesId => AssignedWorkOrder?.SpeciesId;

        /// <summary>本单计划执行的工步；未接单为空。</summary>
        public IReadOnlyList<WorkStep> PlannedSteps => AssignedWorkOrder?.Steps;

        /// <summary>本次结果是否走了本地降级（模型不可用、超时或输出不可用）。</summary>
        public bool UsedFallback { get; }

        /// <summary>最近一次模型调用的摘要。</summary>
        public ModelCallInfo Call { get; }

        /// <summary>农事指令被拒绝。</summary>
        public bool Refused => Outlet == IntentOutlet.FarmCommand && !Accepted;

        internal static IntentResult AcceptedCommand(
            string reply,
            WorkOrder workOrder,
            bool usedFallback,
            ModelCallInfo call)
        {
            return new IntentResult(
                IntentOutlet.FarmCommand,
                true,
                reply,
                workOrder,
                usedFallback,
                call);
        }

        internal static IntentResult Refuse(string reply, bool usedFallback, ModelCallInfo call)
        {
            return new IntentResult(IntentOutlet.FarmCommand, false, reply, null, usedFallback, call);
        }

        internal static IntentResult ReadOnly(
            IntentOutlet outlet,
            string reply,
            bool usedFallback,
            ModelCallInfo call)
        {
            return new IntentResult(outlet, false, reply, null, usedFallback, call);
        }
    }
}
