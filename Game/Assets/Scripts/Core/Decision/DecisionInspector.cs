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

        /// <summary>
        /// 为某名 NPC 取一次五项快照。lastCall 由调用方从 <see cref="IntentRouter"/> 带来。
        /// <paramref name="includeAutonomousIntent"/> 为 false 时（演示层暂停了自主），第 2 项写「待命」；
        /// 检索线索仍按真实意图取，暂停不会把 ① 的记忆也换掉（#29）。
        /// </summary>
        public static DecisionSnapshot Inspect(
            FarmWorld world,
            Npc npc,
            ModelCallInfo lastCall,
            bool includeAutonomousIntent = true)
        {
            var candidates = world.RankCandidates(npc.Id);
            var cueIntent = world.IntentOf(npc.Id);
            var shownIntent = includeAutonomousIntent ? cueIntent : world.IntentOf(npc.Id, false);

            // 玩家追问后展示回答实际依据的那三条；没有追问过才按当前意图检索。
            var memories = world.LastRecallOf(npc.Id) ?? world.RetrieveMemories(npc.Id, cueIntent);

            return new DecisionSnapshot(
                npc.Id,
                npc.DisplayName,
                memories,
                shownIntent,
                candidates,
                world.FieldClaims,
                lastCall,
                new NpcStatus(npc.Stamina, npc.Mood, npc.Hunger));
        }
    }
}
