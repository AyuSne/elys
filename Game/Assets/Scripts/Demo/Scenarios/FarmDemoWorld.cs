using ElysFarm.Core;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 演示与固定剧本共用的开局世界。两条路径必须从这里取同一套地块、NPC、种子与开局作物，
    /// 否则「录下来的剧本」和「测试跑过的剧本」就不是同一个农场。
    /// </summary>
    public static class FarmDemoWorld
    {
        /// <summary>种子库的初始萝卜种子数。</summary>
        public const int RadishSeeds = 12;

        /// <summary>种子库的初始小麦种子数。</summary>
        public const int WheatSeeds = 4;

        public static FarmWorld Create()
        {
            var world = new FarmWorld(new FarmClock());

            foreach (var footprint in FarmLayout.Fields)
            {
                world.AddField(
                    footprint.Id,
                    FarmLayout.ToFarmPosition(footprint.Center),
                    FarmStartup.FieldFertility);
            }

            foreach (var spawn in FarmLayout.NpcSpawns)
            {
                world.AddNpc(
                    spawn.Id,
                    FarmDemoBootstrap.DisplayNameOf(spawn.Id),
                    FarmLayout.ToFarmPosition(spawn.World),
                    FarmDemoBootstrap.PersonaOf(spawn.Id));
            }

            world.SetProduceStore(FarmDemoBootstrap.ProduceStoreSpot);
            world.AddSeeds(CropCatalog.RadishId, RadishSeeds);
            world.AddSeeds(CropCatalog.WheatId, WheatSeeds);

            // 开局三块地各停在一个阶段：幼苗的萝卜、熟了的小麦、快熟的萝卜。
            // 种子库先入账——播种照实数扣种，不能凭空长出三块地。
            FarmStartup.Seed(world);
            return world;
        }
    }
}
