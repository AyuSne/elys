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

        readonly List<Field> fields = new List<Field>();
        readonly List<Npc> npcs = new List<Npc>();
        readonly List<WorkLogEntry> log = new List<WorkLogEntry>();
        readonly Dictionary<string, Field> fieldsById = new Dictionary<string, Field>();
        readonly Dictionary<string, Npc> npcsById = new Dictionary<string, Npc>();
        readonly Dictionary<string, WorkOrder> workOrdersByNpc = new Dictionary<string, WorkOrder>();
        readonly Dictionary<string, StepCandidate> lastDecisions = new Dictionary<string, StepCandidate>();
        readonly MemoryStore memories = new MemoryStore();
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
        /// 该 NPC 此刻的意图：有工单时给出下一步与等待原因，否则给出自主决定；休息中给出剩余时间。
        /// 决策可视化层直接展示这句话，因此它只能来自领域状态，不能由 HUD 事后编造。
        /// </summary>
        public string IntentOf(string npcId)
        {
            var npc = GetNpc(npcId);
            var order = WorkOrderOf(npcId);
            if (order != null && order.NextStep.HasValue)
            {
                var next = order.NextStep.Value;
                var field = GetField(order.FieldId);
                var waiting = ActionValidator.Validate(this, npc, field, next, order.SpeciesId, out var reason)
                    ? string.Empty
                    : $"（{reason}）";

                return $"工单：{order.FieldId} · {WorkStepLabel.Of(next)}（{order.CompletedCount}/{order.Steps.Count}）{waiting}";
            }

            if (npc.IsResting)
            {
                return $"休息中（还剩 {npc.RestSecondsRemaining:0} 秒）";
            }

            if (lastDecisions.TryGetValue(npcId, out var decision))
            {
                return $"自主：{decision.FieldId} · {WorkStepLabel.Of(decision.Step)}";
            }

            return "待命";
        }

        /// <summary>决策可视化层的五项只读快照；lastCall 由演示层从 <see cref="IntentRouter"/> 带来。</summary>
        public DecisionSnapshot Inspect(string npcId, ModelCallInfo lastCall = default)
        {
            return DecisionInspector.Inspect(this, GetNpc(npcId), lastCall);
        }

        /// <summary>检索某名 NPC 的记忆；最多返回 count 条，按权重降序。</summary>
        public IReadOnlyList<MemoryHit> RetrieveMemories(string npcId, string cue, int count = DecisionInspector.MemoryCount)
        {
            GetNpc(npcId);
            return memories.Retrieve(npcId, cue, Clock.Day, count);
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

            if (!ActionValidator.Validate(this, npc, field, step, sowSpeciesId, out var reason))
            {
                return StepResult.Rejected(npcId, fieldId, step, reason);
            }

            var produced = 0;
            string memoryText;

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
                    memoryText = $"在 {field.Id} 播下了{species.DisplayName}";
                    break;

                case WorkStep.Harvest:
                    var speciesId = field.Crop?.Id ?? npc.PreferredSpeciesId;
                    var cropName = field.Crop?.DisplayName ?? "作物";
                    produced = field.Apply(step);
                    if (produced > 0)
                    {
                        Ledger.AddProduce(speciesId, produced);
                    }

                    memoryText = produced > 0
                        ? $"在 {field.Id} 收了 {produced} 个{cropName}"
                        : $"在 {field.Id} 收了{cropName}";
                    break;

                default:
                    field.Apply(step);
                    memoryText = MemoryTextOf(field.Id, step);
                    break;
            }

            npc.ExertStep();
            memories.Remember(npc.Id, memoryText, Clock.Day, MemoryImportanceOf(step));
            log.Add(new WorkLogEntry(Clock.Day, npc.Id, field.Id, step, produced));
            return StepResult.Success(npcId, fieldId, step, produced);
        }

        /// <summary>工步记成记忆时的自然语句；决策可视化直接展示它。</summary>
        static string MemoryTextOf(string fieldId, WorkStep step)
        {
            switch (step)
            {
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

        /// <summary>工步的记忆重要性：收获最值得记住，日常维护最轻。</summary>
        static int MemoryImportanceOf(WorkStep step)
        {
            switch (step)
            {
                case WorkStep.Harvest:
                    return 8;
                case WorkStep.Sow:
                    return 6;
                case WorkStep.Weed:
                    return 5;
                case WorkStep.Water:
                case WorkStep.Fertilize:
                    return 4;
                default:
                    return 3;
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

            order = new WorkOrder(npcId, fieldId, resolvedSpeciesId, steps);
            workOrdersByNpc[npcId] = order;
            lastDecisions.Remove(npcId);
            return true;
        }

        /// <summary>某名 NPC 当前挂着的工单；没有接单时为 null。</summary>
        public WorkOrder WorkOrderOf(string npcId)
        {
            return workOrdersByNpc.TryGetValue(npcId, out var order) ? order : null;
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
                workOrdersByNpc.Remove(npcId);
                return StepResult.None(npcId, "工单已完成");
            }

            var result = ExecuteStep(npcId, order.FieldId, step.Value, order.SpeciesId);
            if (result.Executed)
            {
                order.Advance();
                if (order.IsComplete)
                {
                    workOrdersByNpc.Remove(npcId);
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
            foreach (var field in fields)
            {
                field.AdvanceDay();
            }

            foreach (var npc in npcs)
            {
                npc.RecoverDay();
            }

            DaySettled?.Invoke(day);
        }
    }
}
