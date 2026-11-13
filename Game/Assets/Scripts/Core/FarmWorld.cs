using System;
using System.Collections.Generic;

namespace ElysFarm.Core
{
    /// <summary>
    /// 纯领域层（见 ADR-0001）：持有全部地块、<see cref="FarmClock"/>、<see cref="FarmLedger"/>、
    /// NPC 列表与 <see cref="UtilityScorer"/>，对外暴露「推进一天」「让某 NPC 执行某工步」
    /// 「查询某地块可执行的工步」等能力。UI 与表现层只读它的快照。
    /// </summary>
    public sealed class FarmWorld
    {
        /// <summary>与产出仓的移交半径。</summary>
        public const float ProduceStoreRadius = 1.5f;

        /// <summary>两名 NPC 说得上话的半径；更远就听不见，也就没法转述。</summary>
        public const float ChatRadius = 3.5f;

        /// <summary>两次决定之间、还没拿定主意时的意图文案；演示行与决策可视化共用。</summary>
        public const string ThinkingReason = "琢磨着干点啥…";

        /// <summary>每个游戏日结算时补进种子库的萝卜种子数。</summary>
        const int RadishSeedsPerDay = 2;

        /// <summary>每个游戏日结算时补进种子库的小麦种子数。</summary>
        const int WheatSeedsPerDay = 1;

        readonly List<Field> fields = new List<Field>();
        readonly List<Npc> npcs = new List<Npc>();
        readonly List<WorkLogEntry> log = new List<WorkLogEntry>();
        readonly List<SeedRestock> seedRestocks = new List<SeedRestock>();
        readonly Dictionary<string, Field> fieldsById = new Dictionary<string, Field>();
        readonly Dictionary<string, Npc> npcsById = new Dictionary<string, Npc>();
        readonly Dictionary<string, WorkOrder> workOrdersByNpc = new Dictionary<string, WorkOrder>();
        readonly Dictionary<string, string> claimOfField = new Dictionary<string, string>();
        readonly Dictionary<string, StepCandidate> lastDecisions = new Dictionary<string, StepCandidate>();
        readonly Dictionary<string, IReadOnlyList<MemoryHit>> lastRecalls =
            new Dictionary<string, IReadOnlyList<MemoryHit>>();
        readonly MemoryStore memories = new MemoryStore();
        readonly HearsayBook hearsay = new HearsayBook();
        readonly UtilityScorer scorer = new UtilityScorer();

        /// <summary>产出仓的位置；随身物品只有在这里才能移交。</summary>
        public FarmPosition ProduceStore { get; private set; }

        /// <summary>游戏时钟。</summary>
        public FarmClock Clock { get; }

        /// <summary>背包的三本账：种子库、产出仓、随身。</summary>
        public FarmLedger Ledger { get; }

        /// <summary>全部地块，按加入顺序排列。</summary>
        public IReadOnlyList<Field> Fields => fields;

        /// <summary>全部 NPC，按加入顺序排列。</summary>
        public IReadOnlyList<Npc> Npcs => npcs;

        /// <summary>已执行工步的流水，按发生顺序。</summary>
        public IReadOnlyList<WorkLogEntry> Log => log;

        /// <summary>种子库的补货流水，按发生顺序；每个游戏日结算时补一次。</summary>
        public IReadOnlyList<SeedRestock> SeedRestocks => seedRestocks;

        /// <summary>
        /// 某品种累计补进种子库的数量。种子库流水与账本入账同源，证据与 HUD 都读它，
        /// 不在演示层重算补货政策。
        /// </summary>
        public int RestockedSeedsOf(string speciesId)
        {
            var total = 0;
            foreach (var restock in seedRestocks)
            {
                if (restock.SpeciesId == speciesId)
                {
                    total += restock.Count;
                }
            }

            return total;
        }

        /// <summary>每个游戏日结算完毕后触发，参数为新的游戏日。</summary>
        public event Action<int> DaySettled;

        public FarmWorld(FarmClock clock = null)
        {
            Clock = clock ?? new FarmClock();
            Ledger = new FarmLedger();
            Clock.DayAdvanced += SettleDay;
        }

        /// <summary>加入一块地；id 必须唯一。</summary>
        public Field AddField(string id, FarmPosition position, float fertility = 0.3f, float resilience = 0.5f)
        {
            var field = new Field(id, position, fertility, resilience);
            if (fieldsById.ContainsKey(id))
            {
                throw new ArgumentException($"地块 id 重复：{id}", nameof(id));
            }

            fields.Add(field);
            fieldsById.Add(id, field);
            return field;
        }

        /// <summary>加入一名 NPC；id 必须唯一。未指定人设时使用 <see cref="NpcPersona.Default"/>。</summary>
        public Npc AddNpc(string id, string displayName, FarmPosition position = default, NpcPersona persona = null)
        {
            var npc = new Npc(id, displayName, position, persona);
            if (npcsById.ContainsKey(id))
            {
                throw new ArgumentException($"NPC id 重复：{id}", nameof(id));
            }

            npcs.Add(npc);
            npcsById.Add(id, npc);
            return npc;
        }

        /// <summary>指定产出仓的位置，随身物品只在这里移交。</summary>
        public void SetProduceStore(FarmPosition position)
        {
            ProduceStore = position;
        }

        /// <summary>往种子库入账。</summary>
        public void AddSeeds(string speciesId, int count)
        {
            Ledger.AddSeeds(speciesId, count);
        }

        /// <summary>按 id 取地块。</summary>
        public Field GetField(string id)
        {
            if (!fieldsById.TryGetValue(id, out var field))
            {
                throw new KeyNotFoundException($"找不到地块：{id}");
            }

            return field;
        }

        /// <summary>按 id 取 NPC。</summary>
        public Npc GetNpc(string id)
        {
            if (!npcsById.TryGetValue(id, out var npc))
            {
                throw new KeyNotFoundException($"找不到 NPC：{id}");
            }

            return npc;
        }

        /// <summary>NPC 是否在产出仓的移交半径内。</summary>
        public bool IsAtProduceStore(Npc npc)
        {
            return npc.DistanceTo(ProduceStore) <= ProduceStoreRadius;
        }

        /// <summary>推进真实时间；跨日时由 <see cref="FarmClock"/> 触发结算，NPC 计时按加速倍率同步推进。</summary>
        public int Tick(float deltaSeconds)
        {
            var advanced = Clock.Tick(deltaSeconds);
            AdvanceNpcTime(deltaSeconds * Clock.Speed);
            return advanced;
        }

        /// <summary>推进指定数量的游戏日，返回实际跨过的天数。</summary>
        public int AdvanceDays(int days)
        {
            if (days < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(days), "天数不能为负");
            }

            var advanced = 0;
            var daySeconds = FarmClock.SecondsPerDay / Clock.Speed;
            for (var i = 0; i < days; i++)
            {
                advanced += Clock.Tick(daySeconds);
                AdvanceNpcTime(FarmClock.SecondsPerDay);
            }

            return advanced;
        }

        /// <summary>NPC 的决定间隔与休息都按游戏秒推进，跨日与实时 tick 共用这一个入口。</summary>
        void AdvanceNpcTime(float gameSeconds)
        {
            foreach (var npc in npcs)
            {
                npc.AdvanceTime(gameSeconds);
            }
        }

        /// <summary>NPC 的候选工步排序；无效候选带否决理由，供决策可视化层展示。</summary>
        public IReadOnlyList<StepCandidate> RankCandidates(string npcId)
        {
            return scorer.Rank(this, GetNpc(npcId));
        }

        /// <summary>效用最高的有效候选；没有该干的活时为 null。</summary>
        public StepCandidate? NextStep(string npcId)
        {
            foreach (var candidate in RankCandidates(npcId))
            {
                if (candidate.IsValid)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// 该 NPC 此刻的意图：有工单时给出下一步与三态说法（等条件 / 跳过 / 没做成），
        /// 否则给出自主决定；休息中给出剩余时间。
        /// 决策可视化层与三人卡直接展示这句话，因此它只能来自领域状态，不能由 HUD 事后编造。
        /// </summary>
        public string IntentOf(string npcId)
        {
            var npc = GetNpc(npcId);
            var order = WorkOrderOf(npcId);
            if (order != null && order.NextStep.HasValue)
            {
                var next = order.NextStep.Value;
                var field = GetField(order.FieldId);
                if (ActionValidator.Validate(this, npc, field, next, order.SpeciesId, out var reason))
                {
                    return StepOutcomeLabel.Of(order.FieldId, next, StepOutcome.Executed, null);
                }

                return StepOutcomeLabel.Of(
                    order.FieldId,
                    next,
                    ActionValidator.DispositionOf(reason),
                    reason);
            }

            if (npc.IsResting)
            {
                return $"休息中（还剩 {npc.RestSecondsRemaining:0} 秒）";
            }

            if (lastDecisions.TryGetValue(npcId, out var decision))
            {
                return $"自主：{decision.FieldId} · {WorkStepLabel.Of(decision.Step)}";
            }

            if (!npc.CanDecide)
            {
                return ThinkingReason;
            }

            return "待命";
        }

        /// <summary>决策可视化层的五项只读快照；lastCall 由演示层从 <see cref="IntentRouter"/> 带来。</summary>
        public DecisionSnapshot Inspect(string npcId, ModelCallInfo lastCall = default)
        {
            return DecisionInspector.Inspect(this, GetNpc(npcId), lastCall);
        }

        /// <summary>记下一条属于某名 NPC 的记忆；importance 会被夹到 1..10，时间记为当前游戏日。</summary>
        public void Remember(string npcId, string text, int importance)
        {
            _ = GetNpc(npcId);
            memories.Remember(npcId, text, Clock.Day, importance);

            // 记忆变了，上一次回答命中的排序不再可信；面板回到按当前意图检索。
            lastRecalls.Remove(npcId);
        }

        /// <summary>
        /// 记下玩家对某名 NPC 说的一句闲聊：只写记忆，不改地块、时钟与背包；
        /// 话里提到另一名 NPC 时，顺带记成一条可转述的传闻。
        /// </summary>
        public void RememberChat(string npcId, string text, int importance)
        {
            Remember(npcId, MemoryContext.PlayerChatMemory(text), importance);
            hearsay.LearnFromPlayer(npcId, text, importance, npcs);
        }

        /// <summary>
        /// 让某名 NPC 把听来的事讲给另一名 NPC（见 GLOSSARY「转述」）。
        /// 两人要站得够近，讲的人手里要有对方还不知道的事；成功时只写听者的记忆，
        /// 说法原样带过去，来源换成当下讲的人，重要性随转手次数递减。
        /// 返回的 <see cref="Retelling"/> 同时带出「转述者说的是哪句话」与「听者记下了什么」。
        /// </summary>
        public bool TryRetell(string speakerId, string listenerId, out Retelling retelling)
        {
            return TryRetell(speakerId, listenerId, out retelling, out _);
        }

        /// <summary>带失败理由的转述入口；失败时不改动任何状态。</summary>
        public bool TryRetell(string speakerId, string listenerId, out Retelling retelling, out string reason)
        {
            retelling = default;

            var speaker = GetNpc(speakerId);
            var listener = GetNpc(listenerId);
            if (speaker == listener)
            {
                reason = "自己跟自己没什么好说的";
                return false;
            }

            if (speaker.DistanceTo(listener.Position) > ChatRadius)
            {
                reason = $"{listener.DisplayName}离得太远，说不上话";
                return false;
            }

            if (!hearsay.TryPickForRetelling(speaker.Id, listener.Id, out var heard))
            {
                reason = "我这儿也没什么听来的事";
                return false;
            }

            var learned = heard.Retold(speaker.DisplayName);
            hearsay.Learn(listener.Id, learned);
            Remember(listener.Id, learned.TextFor(listener.Id), learned.Importance);
            retelling = new Retelling(speaker.Id, speaker.DisplayName, listener.Id, heard, learned);
            reason = null;
            return true;
        }

        /// <summary>
        /// 地块的可读指称：处在某个方向极值上时带上方位（如「西边（A1）」），
        /// 否则只给 id。记忆与追问都用它，保证玩家说「西边那块」时能对上同一块地。
        /// </summary>
        public string DescribeField(string fieldId)
        {
            var field = GetField(fieldId);
            if (fields.Count <= 1)
            {
                return field.Id;
            }

            var minX = fields[0].Position.X;
            var maxX = minX;
            var minZ = fields[0].Position.Z;
            var maxZ = minZ;
            foreach (var each in fields)
            {
                minX = Math.Min(minX, each.Position.X);
                maxX = Math.Max(maxX, each.Position.X);
                minZ = Math.Min(minZ, each.Position.Z);
                maxZ = Math.Max(maxZ, each.Position.Z);
            }

            const float epsilon = 0.001f;
            if (field.Position.X <= minX + epsilon && maxX - minX > epsilon)
            {
                return $"{FarmDirection.LabelOf(-1f, 0f)}（{field.Id}）";
            }

            if (field.Position.X >= maxX - epsilon && maxX - minX > epsilon)
            {
                return $"{FarmDirection.LabelOf(1f, 0f)}（{field.Id}）";
            }

            if (field.Position.Z <= minZ + epsilon && maxZ - minZ > epsilon)
            {
                return $"{FarmDirection.LabelOf(0f, -1f)}（{field.Id}）";
            }

            if (field.Position.Z >= maxZ - epsilon && maxZ - minZ > epsilon)
            {
                return $"{FarmDirection.LabelOf(0f, 1f)}（{field.Id}）";
            }

            return field.Id;
        }

        /// <summary>
        /// 缓存一条记忆的 embedding 向量，供后续检索用余弦相似度；向量只是检索元数据，不改变游戏状态。
        /// 没有向量（断网、未配置 embedding）时检索自动退回关键词相似度。
        /// </summary>
        public void CacheMemoryEmbedding(string npcId, string text, float[] embedding)
        {
            _ = GetNpc(npcId);
            memories.CacheEmbedding(npcId, text, embedding);
        }

        /// <summary>还没有向量的记忆文本，新的在前；路由器据此一次批量取向量。</summary>
        public IReadOnlyList<string> MemoriesNeedingEmbedding(string npcId, int count)
        {
            _ = GetNpc(npcId);
            return memories.TextsNeedingEmbedding(npcId, count);
        }

        /// <summary>
        /// 检索某名 NPC 的记忆；最多返回三条，按权重降序。
        /// 给出检索线索的 embedding 时，有向量的记忆用余弦相似度，没有向量的仍用关键词。
        /// 这是纯查询，不会记下本次命中；要回答玩家时用 <see cref="RecallForAnswer"/>。
        /// </summary>
        public IReadOnlyList<MemoryHit> RetrieveMemories(string npcId, string cue, float[] cueEmbedding = null)
        {
            _ = GetNpc(npcId);
            return memories.Retrieve(npcId, cue, Clock.Day, DecisionInspector.MemoryCount, cueEmbedding);
        }

        /// <summary>
        /// 为一次回答检索记忆，并记下这次命中：决策可视化层展示的正是回答所依据的三条，
        /// 而不是按当前意图再检索一次。回答本身只读，不改地块、时钟或背包。
        /// </summary>
        public IReadOnlyList<MemoryHit> RecallForAnswer(string npcId, string cue, float[] cueEmbedding = null)
        {
            var hits = RetrieveMemories(npcId, cue, cueEmbedding);
            lastRecalls[npcId] = hits;
            return hits;
        }

        /// <summary>最近一次回答命中的记忆；没有回答过时为 null。</summary>
        internal IReadOnlyList<MemoryHit> LastRecallOf(string npcId)
        {
            return lastRecalls.TryGetValue(npcId, out var hits) ? hits : null;
        }

        /// <summary>
        /// 带人设计时的决策入口：休息中或还没到决定时刻时返回可读理由；
        /// 成功的决定会消耗本次决定间隔，决定只给出候选，真正执行仍要过 <see cref="ExecuteStep"/> 的动作校验。
        /// </summary>
        public bool TryDecide(string npcId, out StepCandidate candidate, out string reason)
        {
            candidate = default;
            var npc = GetNpc(npcId);

            if (npc.IsResting)
            {
                reason = Npc.RestingReason;
                return false;
            }

            if (!npc.CanDecide)
            {
                reason = "还没拿定主意";
                return false;
            }

            var best = NextStep(npcId);
            if (!best.HasValue)
            {
                reason = "现在没有该干的活";
                return false;
            }

            candidate = best.Value;
            lastDecisions[npcId] = candidate;
            lastRecalls.Remove(npcId);
            npc.MarkDecided();
            reason = null;
            return true;
        }

        /// <summary>取效用最高的有效候选并执行；没有可执行工步时返回未执行结果。</summary>
        public StepResult StepOnce(string npcId)
        {
            if (!TryDecide(npcId, out var candidate, out var reason))
            {
                return StepResult.None(npcId, reason);
            }

            return ExecuteStep(npcId, candidate.FieldId, candidate.Step);
        }

        /// <summary>
        /// 让 NPC 执行一个工步。即使调用方指定了工步，也必须先过
        /// <see cref="ActionValidator"/>；校验失败时不改动任何状态。
        /// </summary>
        public StepResult ExecuteStep(string npcId, string fieldId, WorkStep step)
        {
            return ExecuteStep(npcId, fieldId, step, null);
        }

        /// <summary>
        /// 让 NPC 执行一个工步，并可为播种指定本次要用的品种。
        /// 校验失败时不改动任何状态。
        /// </summary>
        public StepResult ExecuteStep(string npcId, string fieldId, WorkStep step, string sowSpeciesId)
        {
            var npc = GetNpc(npcId);
            var field = GetField(fieldId);

            // 一次执行尝试会消耗掉上一次自主决定：无论成败，NPC 都不该继续声称自己「正要去」那件事。
            lastDecisions.Remove(npcId);
            lastRecalls.Remove(npcId);

            if (!ActionValidator.Validate(this, npc, field, step, sowSpeciesId, out var reason))
            {
                return StepResult.Rejected(npcId, fieldId, step, reason);
            }

            var produced = 0;
            CropSpecies memorySpecies = null;

            switch (step)
            {
                case WorkStep.Sow:
                    if (!ActionValidator.TryResolveSow(this, npc, sowSpeciesId, out var species, out var sowReason))
                    {
                        return StepResult.Rejected(npcId, fieldId, step, sowReason);
                    }

                    if (!Ledger.TryTakeSeeds(species.Id, species.SeedCost))
                    {
                        return StepResult.Rejected(npcId, fieldId, step, ActionValidator.SeedShortageReason(species));
                    }

                    field.Apply(step, species);
                    memorySpecies = species;
                    break;

                case WorkStep.Harvest:
                    var speciesId = field.Crop?.Id ?? npc.PreferredSpeciesId;
                    memorySpecies = field.Crop;
                    produced = field.Apply(step);
                    if (produced > 0)
                    {
                        Ledger.AddProduce(speciesId, produced);
                    }

                    break;

                default:
                    field.Apply(step);
                    break;
            }

            npc.ExertStep();
            memories.Remember(
                npc.Id,
                MemoryTextOf(field.Id, step, memorySpecies, produced),
                Clock.Day,
                MemoryImportanceOf(step));
            log.Add(new WorkLogEntry(Clock.Day, npc.Id, field.Id, step, produced));

            // 在自己的地块上做成任何一件事都算进展：计划工步、插做的维护都算，
            // 否则等成熟的长周期工单会被「没进展」的兜底规则误伤。
            var progress = WorkOrderOf(npcId);
            if (progress != null && progress.FieldId == fieldId)
            {
                progress.MarkProgress(Clock.Day);
            }

            return StepResult.Success(npcId, fieldId, step, produced);
        }

        /// <summary>工步记成记忆时的自然语句；决策可视化直接展示它。</summary>
        static string MemoryTextOf(string fieldId, WorkStep step, CropSpecies species, int produced)
        {
            switch (step)
            {
                case WorkStep.Sow:
                    return $"在 {fieldId} 播下了{species.DisplayName}";
                case WorkStep.Harvest:
                    var cropName = species?.DisplayName ?? "作物";
                    return produced > 0
                        ? $"在 {fieldId} 收了 {produced} 个{cropName}"
                        : $"在 {fieldId} 收了{cropName}";
                case WorkStep.Water:
                    return $"给 {fieldId} 浇了水";
                case WorkStep.Fertilize:
                    return $"给 {fieldId} 施了肥";
                case WorkStep.Weed:
                    return $"除了 {fieldId} 的草";
                default:
                    return $"在 {fieldId} 做了{WorkStepLabel.Of(step)}";
            }
        }

        const int HarvestMemoryImportance = 8;
        const int SowMemoryImportance = 6;
        const int WeedMemoryImportance = 5;
        const int UpkeepMemoryImportance = 4;
        const int RoutineMemoryImportance = 3;

        /// <summary>工步的记忆重要性：收获最值得记住，日常维护最轻。</summary>
        static int MemoryImportanceOf(WorkStep step)
        {
            switch (step)
            {
                case WorkStep.Harvest:
                    return HarvestMemoryImportance;
                case WorkStep.Sow:
                    return SowMemoryImportance;
                case WorkStep.Weed:
                    return WeedMemoryImportance;
                case WorkStep.Water:
                case WorkStep.Fertilize:
                    return UpkeepMemoryImportance;
                default:
                    return RoutineMemoryImportance;
            }
        }

        /// <summary>
        /// 把一串工步派给某名 NPC。第一个工步必须先通过 <see cref="ActionValidator"/>；
        /// 校验失败时返回 false 且不登记工单，也不改动任何游戏状态。
        /// </summary>
        public bool TryAssignWorkOrder(
            string npcId,
            string fieldId,
            string speciesId,
            IReadOnlyList<WorkStep> steps,
            out WorkOrder order,
            out string reason)
        {
            order = null;

            var npc = GetNpc(npcId);
            var field = GetField(fieldId);

            if (steps == null || steps.Count == 0)
            {
                reason = "没有要干的活";
                return false;
            }

            var resolvedSpeciesId = steps[0] == WorkStep.Sow
                ? (string.IsNullOrWhiteSpace(speciesId) ? npc.PreferredSpeciesId : speciesId)
                : null;
            if (!ActionValidator.Validate(this, npc, field, steps[0], resolvedSpeciesId, out reason))
            {
                return false;
            }

            // 接单即占住整块地：一块地同一时刻只属于一张工单。
            if (!TryClaimField(npcId, fieldId, out reason))
            {
                return false;
            }

            ReleaseWorkOrderOf(npcId);
            order = new WorkOrder(npcId, fieldId, resolvedSpeciesId, steps, Clock.Day);
            workOrdersByNpc[npcId] = order;
            lastDecisions.Remove(npcId);
            return true;
        }

        /// <summary>某名 NPC 当前挂着的工单；没有接单时为 null。</summary>
        public WorkOrder WorkOrderOf(string npcId)
        {
            return workOrdersByNpc.TryGetValue(npcId, out var order) ? order : null;
        }

        /// <summary>
        /// 取消某名 NPC 当前的工单（见 GLOSSARY「占位」的释放时机）：释放占位，
        /// 已经做过的工步不回滚——浇过的水还在，播下去的种子不会退回来。
        /// 没有接单时不改动任何状态，并给出可读理由。
        /// </summary>
        public bool TryCancelWorkOrder(string npcId, out WorkOrder cancelled, out string reason)
        {
            _ = GetNpc(npcId);
            var order = WorkOrderOf(npcId);
            if (order == null)
            {
                cancelled = null;
                reason = "没有接单";
                return false;
            }

            ReleaseWorkOrderOf(npcId);
            cancelled = order;
            reason = null;
            return true;
        }

        /// <summary>
        /// 某块地此刻的占位归属（见 GLOSSARY「占位」）；无人认领时 <see cref="FieldClaim.IsClaimed"/> 为 false。
        /// </summary>
        public FieldClaim ClaimOf(string fieldId)
        {
            var field = GetField(fieldId);
            return claimOfField.TryGetValue(field.Id, out var npcId)
                ? new FieldClaim(field.Id, npcId, GetNpc(npcId).DisplayName)
                : FieldClaim.Free(field.Id);
        }

        /// <summary>
        /// 全部地块的占位归属，按地块加入顺序；只读查询，与「谁占着这块地」同源。
        /// </summary>
        public IReadOnlyList<FieldClaim> FieldClaims
        {
            get
            {
                var claims = new List<FieldClaim>(fields.Count);
                foreach (var field in fields)
                {
                    claims.Add(ClaimOf(field.Id));
                }

                return claims;
            }
        }

        /// <summary>谁占着这块地；无人认领时为 null。动作校验用它挡下别人的工步。</summary>
        internal Npc ClaimOwnerOf(string fieldId)
        {
            return claimOfField.TryGetValue(fieldId, out var npcId) ? GetNpc(npcId) : null;
        }

        /// <summary>
        /// 把地块记到这名 NPC 名下。校验里已经挡过一次，这里再挡一次是因为登记才是
        /// 「一块地只属于一张工单、一名 NPC 只占一块地」这条不变量的落点：将来多一个登记入口
        /// 也不会把别人的地抢走。被拒时不改动任何状态。
        /// </summary>
        bool TryClaimField(string npcId, string fieldId, out string reason)
        {
            if (claimOfField.TryGetValue(fieldId, out var holder) && holder != npcId)
            {
                reason = ActionValidator.ClaimedReason(GetNpc(holder));
                return false;
            }

            UnclaimFieldOf(npcId);
            claimOfField[fieldId] = npcId;
            reason = null;
            return true;
        }

        /// <summary>释放这名 NPC 名下的占位（如果还有）；工单由 <see cref="ReleaseWorkOrderOf"/> 一并清掉。</summary>
        void UnclaimFieldOf(string npcId)
        {
            string claimedField = null;
            foreach (var pair in claimOfField)
            {
                if (pair.Value == npcId)
                {
                    claimedField = pair.Key;
                    break;
                }
            }

            if (claimedField != null)
            {
                claimOfField.Remove(claimedField);
            }
        }

        /// <summary>清掉这名 NPC 的工单并释放它占着的地块；已完成的工步不回滚。</summary>
        void ReleaseWorkOrderOf(string npcId)
        {
            if (!workOrdersByNpc.TryGetValue(npcId, out var order))
            {
                return;
            }

            workOrdersByNpc.Remove(npcId);
            if (claimOfField.TryGetValue(order.FieldId, out var ownerId) && ownerId == npcId)
            {
                claimOfField.Remove(order.FieldId);
            }
        }

        /// <summary>种植类工单在等待成熟时，允许插做的维护工步。</summary>
        static readonly WorkStep[] MaintenanceSteps =
        {
            WorkStep.Water,
            WorkStep.Weed,
            WorkStep.Fertilize
        };

        /// <summary>维护工步的最低需求，避免每天无谓地浇水。</summary>
        const float MaintenanceNeed = 0.35f;

        /// <summary>
        /// 执行工单的下一个工步。前置条件不满足时返回带理由的未执行结果，工单保持原样等待；
        /// 种植类工单在等待期间会先做最急需的维护（浇水 / 除草 / 施肥），
        /// 否则小麦这类长周期作物会因为缺水永远等不到收获。
        /// 执行成功后推进工单，全部完成即自动清空。
        /// 返回结果可能是计划工步，也可能是维护工步；进度以 <see cref="WorkOrder.CompletedCount"/> 为准。
        /// </summary>
        public StepResult AdvanceWorkOrder(string npcId)
        {
            var order = WorkOrderOf(npcId);
            if (order == null)
            {
                return StepResult.None(npcId, "没有接单");
            }

            var step = order.NextStep;
            if (!step.HasValue)
            {
                ReleaseWorkOrderOf(npcId);
                return StepResult.None(npcId, "工单已完成");
            }

            var result = ExecuteStep(npcId, order.FieldId, step.Value, order.SpeciesId);
            if (result.Executed)
            {
                order.Advance();
                if (order.IsComplete)
                {
                    ReleaseWorkOrderOf(npcId);
                }

                return result;
            }

            // 跳过：这一步不必做了（作物已经熟了，浇水/施肥/除草都没有意义）。
            // 工单接着做后面的工步，占位留着；跳过本身算进展，否则兜底规则会把正在等的工单放掉。
            if (result.Outcome == StepOutcome.Skipped)
            {
                order.Advance();
                order.MarkProgress(Clock.Day);
                if (order.IsComplete)
                {
                    ReleaseWorkOrderOf(npcId);
                }

                return result;
            }

            var maintenance = NextMaintenanceStep(npcId, order);
            if (maintenance.HasValue)
            {
                var maintained = ExecuteStep(npcId, order.FieldId, maintenance.Value);
                if (maintained.Executed)
                {
                    return maintained;
                }
            }

            // 等条件（还没熟、刚浇过）只说明还没到时候；别的（失败）意味着这单活做不下去了，
            // 释放占位，别让一张永远推进不了的工单一直占着这块地。分态判据只有 StepOutcome 一份。
            if (result.Outcome != StepOutcome.Waiting)
            {
                ReleaseWorkOrderOf(npcId);
            }

            return result;
        }

        WorkStep? NextMaintenanceStep(string npcId, WorkOrder order)
        {
            // 第一个工步（播种）还没做成时不能插维护：否则地块被占死会无限浇水而不报错。
            if (order.CompletedCount == 0 || order.Steps[0] != WorkStep.Sow)
            {
                return null;
            }

            var npc = GetNpc(npcId);
            var field = GetField(order.FieldId);
            foreach (var step in MaintenanceSteps)
            {
                if (field.Need(step) < MaintenanceNeed)
                {
                    continue;
                }

                if (ActionValidator.Validate(this, npc, field, step, null, out _))
                {
                    return step;
                }
            }

            return null;
        }

        /// <summary>NPC 移动；表现层每帧把位置写回领域，保证距离项与移交判定一致。</summary>
        public void MoveNpc(string npcId, FarmPosition position)
        {
            GetNpc(npcId).MoveTo(position);
        }

        /// <summary>NPC 在产出仓取货到随身；不在产出仓则拒绝。</summary>
        public bool TryPickUpProduce(string npcId, string speciesId, int count)
        {
            var npc = GetNpc(npcId);
            return IsAtProduceStore(npc) && Ledger.TryPickUp(npc.Id, speciesId, count);
        }

        /// <summary>NPC 回到产出仓后把随身交回；不在产出仓则拒绝。</summary>
        public bool TryDepositHeld(string npcId, string speciesId, int count)
        {
            var npc = GetNpc(npcId);
            return IsAtProduceStore(npc) && Ledger.TryDeposit(npc.Id, speciesId, count);
        }

        void SettleDay(int day)
        {
            // 跨日后时间衰减变了，上一次回答命中的顺序不再可信，回到按当前意图检索。
            lastRecalls.Clear();

            RestockSeeds(day);

            foreach (var field in fields)
            {
                field.AdvanceDay();
            }

            foreach (var npc in npcs)
            {
                npc.RecoverDay();
            }

            AbandonStaleWorkOrders(day);
            DaySettled?.Invoke(day);
        }

        /// <summary>
        /// 每个游戏日结算时给种子库补货：萝卜 +2、小麦 +1。种子会耗尽，补货让农场
        /// 不会因为种子用完而空转；补货后仍然不够时，播种照旧被
        /// <see cref="ActionValidator.TryResolveSow"/> 以「种子库里没有…种子了」拒绝。
        /// 数量固定，这是演示规则，不做经济平衡。
        /// </summary>
        void RestockSeeds(int day)
        {
            RestockSeed(CropCatalog.RadishId, RadishSeedsPerDay, day);
            RestockSeed(CropCatalog.WheatId, WheatSeedsPerDay, day);
        }

        /// <summary>一次补货既入账，也留下流水——种子数涨了要能说出来是怎么涨的。</summary>
        void RestockSeed(string speciesId, int count, int day)
        {
            Ledger.AddSeeds(speciesId, count);
            seedRestocks.Add(new SeedRestock(day, speciesId, count));
        }

        /// <summary>
        /// 兜底规则：超过一个游戏日没有进展的工单自动放弃，占位随之释放，农场不会一直占着
        /// 没人推进的地块。NPC 去休息不释放——休息时长由人设固定，睡醒就回来接着干。
        /// </summary>
        void AbandonStaleWorkOrders(int day)
        {
            if (workOrdersByNpc.Count == 0)
            {
                return;
            }

            var stale = new List<string>();
            foreach (var order in workOrdersByNpc.Values)
            {
                if (GetNpc(order.NpcId).IsResting)
                {
                    // 休息不是停滞：占位留着，停滞计时也跟着清零。
                    order.MarkProgress(day);
                    continue;
                }

                if (order.DaysWithoutProgress(day) > WorkOrder.AbandonAfterIdleDays)
                {
                    stale.Add(order.NpcId);
                }
            }

            foreach (var npcId in stale)
            {
                ReleaseWorkOrderOf(npcId);
            }
        }
    }
}
