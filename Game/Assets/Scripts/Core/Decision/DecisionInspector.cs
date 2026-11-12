namespace ElysFarm.Core
{
    /// <summary>
    /// 决策可视化层的读数器：把 <see cref="FarmWorld"/> 的只读状态整理成恰好五项的快照
    /// （见 ALIGNMENT §四）。它是 <see cref="FarmWorld.Inspect"/> 的实现，测试与 UI 都只打
    /// FarmWorld 这条缝；本类不改变任何游戏状态。
    /// </summary>
    public static class DecisionInspector
    {
        /// <summary>面板固定展示的检索命中条数。</summary>
        public const int MemoryCount = 3;

        /// <summary>为某名 NPC 取一次五项快照。lastCall 由调用方从 <see cref="IntentRouter"/> 带来。</summary>
        public static DecisionSnapshot Inspect(FarmWorld world, Npc npc, ModelCallInfo lastCall)
        {
            var candidates = world.RankCandidates(npc.Id);
            var intent = world.IntentOf(npc.Id);

            // 玩家追问后展示回答实际依据的那三条；没有追问过才按当前意图检索。
            var memories = world.LastRecallOf(npc.Id) ?? world.RetrieveMemories(npc.Id, intent);

            return new DecisionSnapshot(
                npc.Id,
                npc.DisplayName,
                memories,
                intent,
                candidates,
                lastCall,
                new NpcStatus(npc.Stamina, npc.Mood, npc.Hunger));
        }
    }
}
