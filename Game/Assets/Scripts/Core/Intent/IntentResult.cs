using System.Collections.Generic;

namespace ElysFarm.Core
{
    /// <summary>
    /// 一次玩家输入经过意图出口后的结果。指挥官线的唯一可见产物：
    /// 要么是三个出口之一（其中农事指令可能附带一张工单，也可能取消掉当前那张），要么是一句拒绝。
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
            ModelCallInfo call,
            bool cancelRequested = false,
            WorkOrder cancelledWorkOrder = null)
        {
            Outlet = outlet;
            Accepted = accepted;
            Reply = reply ?? string.Empty;
            AssignedWorkOrder = assignedWorkOrder;
            UsedFallback = usedFallback;
            Call = call;
            CancelRequested = cancelRequested;
            CancelledWorkOrder = cancelledWorkOrder;
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

        /// <summary>
        /// 这句话是要取消工单——取消成没成都算。取消由本地规则裁决、不经过模型，
        /// HUD 据此把这一轮说成「取消」而不是「拒单」。
        /// </summary>
        public bool CancelRequested { get; }

        /// <summary>被取消的那张工单；不是取消请求或没取消成时为 null。</summary>
        public WorkOrder CancelledWorkOrder { get; }

        /// <summary>取消成功：确实撤掉了一张工单。</summary>
        public bool Cancelled => CancelledWorkOrder != null;

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

        /// <summary>
        /// 取消某名 NPC 当前工单的结果。<paramref name="cancelled"/> 为 null 表示这次没取消成
        /// （手上本来就没活）；两种情况都标 <see cref="CancelRequested"/>。
        /// 台词由本地规则给，不算本地降级：模型根本不需要被问到这件事。
        /// </summary>
        internal static IntentResult Cancel(string reply, WorkOrder cancelled, ModelCallInfo call)
        {
            return new IntentResult(
                IntentOutlet.FarmCommand,
                false,
                reply,
                null,
                false,
                call,
                cancelRequested: true,
                cancelledWorkOrder: cancelled);
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
