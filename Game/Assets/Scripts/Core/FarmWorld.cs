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

        /// <summary>加入一名 NPC；id 必须唯一。</summary>
        public Npc AddNpc(string id, string displayName, FarmPosition position = default)
        {
            var npc = new Npc(id, displayName, position);
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

        /// <summary>推进真实时间；跨日时由 <see cref="FarmClock"/> 触发结算。</summary>
        public int Tick(float deltaSeconds)
        {
            return Clock.Tick(deltaSeconds);
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
            }

            return advanced;
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

        /// <summary>取效用最高的有效候选并执行；没有可执行工步时返回未执行结果。</summary>
        public StepResult StepOnce(string npcId)
        {
            var candidate = NextStep(npcId);
            if (!candidate.HasValue)
            {
                return StepResult.None(npcId, "现在没有该干的活");
            }

            return ExecuteStep(npcId, candidate.Value.FieldId, candidate.Value.Step);
        }

        /// <summary>
        /// 让 NPC 执行一个工步。即使调用方指定了工步，也必须先过
        /// <see cref="ActionValidator"/>；校验失败时不改动任何状态。
        /// </summary>
        public StepResult ExecuteStep(string npcId, string fieldId, WorkStep step)
        {
            var npc = GetNpc(npcId);
            var field = GetField(fieldId);

            if (!ActionValidator.Validate(this, npc, field, step, out var reason))
            {
                return StepResult.Rejected(npcId, fieldId, step, reason);
            }

            var produced = 0;

            switch (step)
            {
                case WorkStep.Sow:
                    if (!ActionValidator.TryResolveSow(this, npc, out var species, out var sowReason))
                    {
                        return StepResult.Rejected(npcId, fieldId, step, sowReason);
                    }

                    if (!Ledger.TryTakeSeeds(species.Id, species.SeedCost))
                    {
                        return StepResult.Rejected(npcId, fieldId, step, ActionValidator.SeedShortageReason(species));
                    }

                    field.Apply(step, species);
                    break;

                case WorkStep.Harvest:
                    var speciesId = field.Crop?.Id ?? npc.PreferredSpeciesId;
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
            log.Add(new WorkLogEntry(Clock.Day, npc.Id, field.Id, step, produced));
            return StepResult.Success(npcId, fieldId, step, produced);
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
