using System;

namespace ElysFarm.Core
{
    /// <summary>
    /// 地块：农场的可耕作单元，持有自身状态与其中的作物。
    /// 它是「智能在环境里」这一设计的载体 —— 地块知道自己缺什么，
    /// NPC 只是能对某状态施加某动作的执行器。
    /// </summary>
    public sealed class Field
    {
        /// <summary>生长进度达到该值即视为成熟。</summary>
        public const float MatureThreshold = 1f;

        public string Id { get; }

        /// <summary>地块在农场平面上的位置，用于效用打分的距离项。</summary>
        public FarmPosition Position { get; }

        public FieldState State { get; private set; } = FieldState.Empty;

        /// <summary>当前作物；空地或已收获时为 null。</summary>
        public CropSpecies Crop { get; private set; }

        // —— 作物五属性，均为 0..1 连续值 ——

        /// <summary>生长进度。</summary>
        public float Growth { get; private set; }

        /// <summary>水分。</summary>
        public float Water { get; private set; }

        /// <summary>肥力。</summary>
        public float Fertility { get; private set; }

        /// <summary>杂草。</summary>
        public float Weeds { get; private set; }

        /// <summary>抗性（预留：极端天气时减少损失）。</summary>
        public float Resilience { get; private set; }

        /// <summary>地块上是否还留着作物（已成熟的地块也算，因为作物还没收）。</summary>
        public bool IsSown =>
            State == FieldState.Sown ||
            State == FieldState.Growing ||
            State == FieldState.Mature;

        /// <summary>作物是否还在生长；成熟后不再变化。</summary>
        public bool IsGrowing =>
            State == FieldState.Sown ||
            State == FieldState.Growing;

        /// <summary>是否可以播种：空地或已收获。</summary>
        public bool IsFree =>
            State == FieldState.Empty ||
            State == FieldState.Harvested;

        const string NotPlantedReason = "地里什么都没种";

        /// <summary>
        /// 地里还种着东西（已播种 / 生长中 / 成熟），此刻不能再播种。这是失败而不是等条件：
        /// 地块不会自己空出来，工单只能释放占位、让玩家换一块地，所以它不在
        /// <see cref="ActionValidator.IsWaiting"/> 的清单里。判定与文案都读这一个常量。
        /// </summary>
        public const string SownReason = "那块地还种着东西";

        /// <summary>作物已经熟了，浇水、施肥、除草都没有意义——工单跳过这些工步（见 ADR-0006）。</summary>
        public const string MatureReason = "已经熟了，先收了吧";

        /// <summary>作物还没熟，收获、浇水这类工步只能等——工单因此保留。</summary>
        public const string NotMatureReason = "还没熟";

        /// <summary>没有草可除，只能等草长出来。</summary>
        public const string NoWeedsReason = "没什么草可除";

        /// <summary>刚浇过水，不渴；等水分掉下去再说。</summary>
        public const string WateredReason = "刚浇过，不渴";

        /// <summary>肥力还够，等它掉下去再说。</summary>
        public const string FertileReason = "肥力还够";

        public Field(string id, FarmPosition position, float fertility = 0.5f, float resilience = 0.5f)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("地块必须有 id", nameof(id));
            }

            Id = id;
            Position = position;
            Fertility = FarmMath.Clamp01(fertility);
            Resilience = FarmMath.Clamp01(resilience);
        }

        /// <summary>
        /// 此刻对某工步的原始需求，0..1。地块自己回答「缺什么」，
        /// 效用打分只负责乘权重与排序。已成熟的地块只需求收获。
        /// </summary>
        public float Need(WorkStep step)
        {
            switch (step)
            {
                case WorkStep.Sow:
                    return IsFree ? 1f : 0f;
                case WorkStep.Water:
                    return IsGrowing ? 1f - Water : 0f;
                case WorkStep.Fertilize:
                    return IsGrowing ? 1f - Fertility : 0f;
                case WorkStep.Weed:
                    return IsGrowing ? Weeds : 0f;
                case WorkStep.Harvest:
                    return State == FieldState.Mature ? 1f : 0f;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// 检查某工步此刻能否执行。不能执行时通过 reason 给出可读理由，
        /// 该理由会直接显示在决策可视化层里（对应「候选动作与被否决的原因」）。
        /// </summary>
        public bool Can(WorkStep step, out string reason)
        {
            switch (step)
            {
                case WorkStep.Sow:
                    if (!IsFree)
                    {
                        reason = SownReason;
                        return false;
                    }

                    reason = null;
                    return true;

                case WorkStep.Water:
                    if (!IsSown)
                    {
                        reason = NotPlantedReason;
                        return false;
                    }

                    if (State == FieldState.Mature)
                    {
                        reason = MatureReason;
                        return false;
                    }

                    if (Water >= 0.99f)
                    {
                        reason = WateredReason;
                        return false;
                    }

                    reason = null;
                    return true;

                case WorkStep.Fertilize:
                    if (!IsSown)
                    {
                        reason = NotPlantedReason;
                        return false;
                    }

                    if (State == FieldState.Mature)
                    {
                        reason = MatureReason;
                        return false;
                    }

                    if (Fertility >= 0.99f)
                    {
                        reason = FertileReason;
                        return false;
                    }

                    reason = null;
                    return true;

                case WorkStep.Weed:
                    if (!IsSown)
                    {
                        reason = NotPlantedReason;
                        return false;
                    }

                    if (State == FieldState.Mature)
                    {
                        reason = MatureReason;
                        return false;
                    }

                    if (Weeds <= 0.01f)
                    {
                        reason = NoWeedsReason;
                        return false;
                    }

                    reason = null;
                    return true;

                case WorkStep.Harvest:
                    if (State != FieldState.Mature)
                    {
                        reason = NotMatureReason;
                        return false;
                    }

                    reason = null;
                    return true;

                default:
                    reason = "未知工步";
                    return false;
            }
        }

        /// <summary>
        /// 执行一个工步。调用方必须先通过 <see cref="Can"/> 校验。
        /// 返回本次产生的产出数量（只有收获会产生产出）。
        /// </summary>
        public int Apply(WorkStep step, CropSpecies species = null)
        {
            if (!Can(step, out var reason))
            {
                throw new InvalidOperationException($"地块 {Id} 无法执行 {step}：{reason}");
            }

            switch (step)
            {
                case WorkStep.Sow:
                    Crop = species ?? throw new ArgumentNullException(nameof(species), "播种必须指定品种");
                    State = FieldState.Sown;
                    Growth = 0f;
                    Water = 0f;
                    Weeds = 0f;
                    return 0;

                case WorkStep.Water:
                    Water = 1f;
                    return 0;

                case WorkStep.Fertilize:
                    Fertility = 1f;
                    return 0;

                case WorkStep.Weed:
                    Weeds = 0f;
                    return 0;

                case WorkStep.Harvest:
                    var yield = Crop?.YieldPerHarvest ?? 0;
                    State = FieldState.Harvested;
                    Crop = null;
                    Growth = 0f;
                    Water = 0f;
                    Weeds = 0f;
                    return yield;

                default:
                    throw new ArgumentOutOfRangeException(nameof(step), step, null);
            }
        }

        /// <summary>
        /// 结算一个游戏日：生长推进、水分流失、杂草生长、肥力下降。
        /// 水分耗尽时生长停滞 —— 这是「不浇水就长不大」的唯一实现点。
        /// 成熟后不再生长，但水分、杂草与肥力仍会随日结算。
        /// </summary>
        public void AdvanceDay()
        {
            if (!IsSown)
            {
                return;
            }

            var species = Crop;

            if (IsGrowing && Water > 0f)
            {
                var perDay = 1f / Math.Max(1, species.DaysToMature);
                var weedPenalty = Weeds * 0.5f;
                var fertilityBonus = Fertility * 0.25f;
                Growth += perDay * (1f + fertilityBonus) * (1f - weedPenalty);
            }

            Water = FarmMath.Clamp01(Water - species.WaterDrainPerDay);
            Weeds = FarmMath.Clamp01(Weeds + species.WeedPerDay);
            Fertility = FarmMath.Clamp01(Fertility - 0.15f);

            if (IsGrowing)
            {
                Growth = Math.Min(Growth, MatureThreshold);
                State = Growth >= MatureThreshold ? FieldState.Mature : FieldState.Growing;
            }
        }
    }
}
