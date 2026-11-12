using ElysFarm.Core;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 演示开局的农事状态：三块地并排，各自停在一个不同的生长阶段，一眼就能看出「谁刚种下、谁能收、谁快熟了」。
    /// 只用 <see cref="Field"/> 的公开工步与日结算往前推——不走时钟，所以开局仍是第 1 天；
    /// 也不写流水、不动 NPC，那些是 <see cref="FarmWorld"/> 自己的事。
    /// </summary>
    public static class FarmStartup
    {
        /// <summary>地块的初始肥力。种植天数按它算，所以放在这里，免得场景与开局各改各的。</summary>
        public const float FieldFertility = 0.3f;

        /// <summary>往前最多结算多少天；改坏公式时宁可停在原地，也不死循环。</summary>
        const int MaxSeedingDays = 30;

        /// <summary>
        /// 给三块地安排三个不同的阶段：西边刚播下的萝卜（幼苗）、中间熟了的小麦、东边快熟的萝卜。
        /// 已经种了的地块会跳过，重复调用不会叠作物。
        /// </summary>
        public static void Seed(FarmWorld world)
        {
            if (world == null)
            {
                return;
            }

            // 西边：播下就不管了，一瓢水都没浇——不浇水不长，所以停在幼苗。
            Sow(world, FarmDemoBootstrap.FieldA1, CropCatalog.RadishId);

            // 中间：小麦一天一瓢水浇到熟，开局就能收。
            Sow(world, FarmDemoBootstrap.FieldA2, CropCatalog.WheatId);
            GrowTo(world, FarmDemoBootstrap.FieldA2, CropStage.Mature);

            // 东边：萝卜浇到快熟就停手，留一档给观察者看。
            Sow(world, FarmDemoBootstrap.FieldA3, CropCatalog.RadishId);
            GrowTo(world, FarmDemoBootstrap.FieldA3, CropStage.Ripening);
        }

        static void Sow(FarmWorld world, string fieldId, string speciesId)
        {
            var field = FindField(world, fieldId);
            if (field == null || !CropCatalog.TryGet(speciesId, out var species))
            {
                return;
            }

            if (!field.Can(WorkStep.Sow, out _))
            {
                return;
            }

            field.Apply(WorkStep.Sow, species);
        }

        /// <summary>一天一瓢水地往前结算，直到地块长到目标阶段。</summary>
        static void GrowTo(FarmWorld world, string fieldId, CropStage target)
        {
            var field = FindField(world, fieldId);
            if (field == null)
            {
                return;
            }

            for (var day = 0; day < MaxSeedingDays; day++)
            {
                if (CropStages.Of(field.State, field.Growth) >= target)
                {
                    return;
                }

                if (field.Can(WorkStep.Water, out _))
                {
                    field.Apply(WorkStep.Water);
                }

                field.AdvanceDay();
            }
        }

        /// <summary>缺地块（临时场景或测试的精简世界）时不抛异常，跳过即可。</summary>
        static Field FindField(FarmWorld world, string fieldId)
        {
            foreach (var field in world.Fields)
            {
                if (field.Id == fieldId)
                {
                    return field;
                }
            }

            return null;
        }
    }
}
