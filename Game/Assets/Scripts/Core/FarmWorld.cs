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
        readonly List<Field> fields = new List<Field>();
        readonly List<Npc> npcs = new List<Npc>();
        readonly List<WorkLogEntry> log = new List<WorkLogEntry>();
        readonly Dictionary<string, Field> fieldsById = new Dictionary<string, Field>();
        readonly Dictionary<string, Npc> npcsById = new Dictionary<string, Npc>();
        readonly UtilityScorer scorer = new UtilityScorer();

        /// <summary>仓库的位置；随身物品只有在这里才能移交。</summary>
        public float WarehouseX { get; private set; }

        public float WarehouseZ { get; private set; }

        /// <summary>与仓库的移交半径。</summary>
        public const float WarehouseRadius = 1.5f;

        public FarmClock Clock { get; }

        public FarmLedger Ledger { get; }

        public IReadOnlyList<Field> Fields => fields;

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

        public Field AddField(string id, float x, float z, float fertility = 0.3f, float resilience = 0.5f)
        {
            var field = new Field(id, x, z, fertility, resilience);
            if (fieldsById.ContainsKey(id))
            {
                throw new ArgumentException($"地块 id 重复：{id}", nameof(id));
            }

            fields.Add(field);
            fieldsById.Add(id, field);
            return field;
        }

        public Npc AddNpc(string id, string displayName, float x = 0f, float z = 0f)
        {
            var npc = new Npc(id, displayName, x, z);
            if (npcsById.ContainsKey(id))
            {
                throw new ArgumentException($"NPC id 重复：{id}", nameof(id));
            }

            npcs.Add(npc);
            npcsById.Add(id, npc);
            return npc;
        }

        public void SetWarehouse(float x, float z)
        {
            WarehouseX = x;
            WarehouseZ = z;
        }

        /// <summary>往种子库入账。</summary>
        public void AddSeeds(string speciesId, int count)
        {
            Ledger.AddSeeds(speciesId, count);
        }

        public Field GetField(string id)
        {
            if (!fieldsById.TryGetValue(id, out var field))
            {
                throw new KeyNotFoundException($"找不到地块：{id}");
            }

            return field;
        }

        public Npc GetNpc(string id)
        {
            if (!npcsById.TryGetValue(id, out var npc))
            {
                throw new KeyNotFoundException($"找不到 NPC：{id}");
            }

            return npc;
        }

        public bool IsAtWarehouse(Npc npc)
        {
            var dx = npc.X - WarehouseX;
            var dz = npc.Z - WarehouseZ;
            return Math.Sqrt(dx * dx + dz * dz) <= WarehouseRadius;
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

        /// <summary>取效用最高的有效候选并执行；没有可执行工步时返回未执行结果。</summary>
        public StepResult StepOnce(string npcId)
        {
            var candidates = RankCandidates(npcId);
            foreach (var candidate in candidates)
            {
                if (!candidate.IsValid)
                {
                    continue;
                }

                return ExecuteStep(npcId, candidate.FieldId, candidate.Step);
            }

            return StepResult.None(npcId, "现在没有该干的活");
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

            var yield = 0;

            switch (step)
            {
                case WorkStep.Sow:
                    if (!CropCatalog.TryGet(npc.PreferredSpeciesId, out var species))
                    {
                        return StepResult.Rejected(npcId, fieldId, step, "不知道该种什么");
                    }

                    if (!Ledger.TryTakeSeeds(species.Id, species.SeedCost))
                    {
                        return StepResult.Rejected(npcId, fieldId, step, $"种子库里没有{species.DisplayName}种子了");
                    }

                    field.Apply(step, species);
                    break;

                case WorkStep.Harvest:
                    var speciesId = field.Crop?.Id ?? npc.PreferredSpeciesId;
                    yield = field.Apply(step);
                    if (yield > 0)
                    {
                        Ledger.AddProduce(speciesId, yield);
                    }

                    break;

                default:
                    field.Apply(step);
                    break;
            }

            npc.ExertStep();
            log.Add(new WorkLogEntry(Clock.Day, npc.Id, field.Id, step, yield, field.State));
            return StepResult.Success(npcId, fieldId, step, yield);
        }

        /// <summary>NPC 移动；表现层每帧把位置写回领域，保证距离项与移交判定一致。</summary>
        public void MoveNpc(string npcId, float x, float z)
        {
            GetNpc(npcId).MoveTo(x, z);
        }

        /// <summary>NPC 在仓库从产出仓取货到随身。离开仓库则拒绝。</summary>
        public bool TryPickUpProduce(string npcId, string speciesId, int count)
        {
            var npc = GetNpc(npcId);
            return IsAtWarehouse(npc) && Ledger.TryCarry(speciesId, count);
        }

        /// <summary>NPC 回到仓库后把随身交回产出仓。不在仓库则拒绝。</summary>
        public bool TryDepositCarried(string npcId, string speciesId, int count)
        {
            var npc = GetNpc(npcId);
            return IsAtWarehouse(npc) && Ledger.TryDeposit(speciesId, count);
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
