namespace ElysFarm.Core
{
    /// <summary>
    /// 一块地的占位归属（见 GLOSSARY「占位」）：这块地此刻由谁占着，或无人认领。
    /// 归属只由 <see cref="FarmWorld"/> 的工单登记决定；决策可视化直接展示这张表，
    /// 因此它不能由 HUD 事后推断，也不参与效用打分之外的任何状态改动。
    /// </summary>
    public readonly struct FieldClaim
    {
        internal FieldClaim(string fieldId, string npcId, string npcName)
        {
            FieldId = fieldId;
            NpcId = npcId;
            NpcName = npcName;
        }

        /// <summary>地块 id，例如 A1。</summary>
        public string FieldId { get; }

        /// <summary>占着这块地的 NPC id；无人认领时为 null。</summary>
        public string NpcId { get; }

        /// <summary>占着这块地的 NPC 展示名；无人认领时为 null。</summary>
        public string NpcName { get; }

        /// <summary>是否有人占着。</summary>
        public bool IsClaimed => !string.IsNullOrEmpty(NpcId);

        /// <summary>无人认领的归属条目。</summary>
        internal static FieldClaim Free(string fieldId)
        {
            return new FieldClaim(fieldId, null, null);
        }
    }
}
