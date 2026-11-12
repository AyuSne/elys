using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 场景装饰：地面、草垛、农舍、风车、水塔、树木与杂草，以及光照与后处理。
    /// 每个模型都走 <see cref="QuaterniusArt"/>；缺失时跳过装饰，作物与 NPC 的体块回退在各自的表现类里。
    /// 场景其余部分照常搭起来——素材没拉下来也能演示。
    /// </summary>
    public static class FarmDressing
    {
        static readonly Color GrassColor = new Color(0.32f, 0.46f, 0.26f);
        static readonly Color HayColor = new Color(0.82f, 0.70f, 0.30f);
        static readonly Color BarnFallback = new Color(0.58f, 0.38f, 0.22f);
        static readonly Color[] TreeGreens =
        {
            new Color(0.24f, 0.44f, 0.22f),
            new Color(0.31f, 0.52f, 0.26f),
            new Color(0.20f, 0.38f, 0.20f),
            new Color(0.36f, 0.56f, 0.30f)
        };

        static readonly Color BushGreen = new Color(0.30f, 0.52f, 0.24f);
        static readonly Color RockGrey = new Color(0.55f, 0.54f, 0.50f);
        static readonly Color GrassTuft = new Color(0.42f, 0.60f, 0.28f);

        /// <summary>
        /// 自然包的模型是白模（材质全白，靠使用方染色），不染会得到一片白色树石。
        /// 树按 yaw 取四种绿，灌木/草/石头各一种，保持低多边形风格。
        /// </summary>
        static Color? TintFor(string modelName, float yaw)
        {
            if (modelName.StartsWith("Nature/Tree"))
            {
                var variant = Mathf.Abs(Mathf.RoundToInt(yaw / 10f)) % TreeGreens.Length;
                return TreeGreens[variant];
            }

            if (modelName.StartsWith("Nature/Bush"))
            {
                return BushGreen;
            }

            if (modelName.StartsWith("Nature/Grass"))
            {
                return GrassTuft;
            }

            if (modelName.StartsWith("Nature/Rock"))
            {
                return RockGrey;
            }

            return null;
        }

        public static void Build(Transform parent)
        {
            DemoMaterials.Block("Ground", parent, new Vector3(2f, -0.1f, 0f), new Vector3(38f, 0.2f, 28f), GrassColor);

            // 产出仓与草垛：领域里的标记点，仍用体块表示，NPC 走到这里就算到了。
            DemoMaterials.Block(
                "Produce Store",
                parent,
                new Vector3(FarmDemoBootstrap.ProduceStoreSpot.X, 0.6f, FarmDemoBootstrap.ProduceStoreSpot.Z),
                new Vector3(1.6f, 1.2f, 1.6f),
                BarnFallback);
            DemoMaterials.Block(
                "Haystack",
                parent,
                new Vector3(FarmDemoBootstrap.RestSpot.X, 0.35f, FarmDemoBootstrap.RestSpot.Z),
                new Vector3(3.2f, 0.7f, 1.4f),
                HayColor);

            // 装饰性建筑没有体块回退：缺素材时宁可少一座谷仓，也不要在场地边堆大箱子。
            SpawnOrBlock("Buildings/Barn", parent, new Vector3(-3.4f, 0f, 8.8f), 0.58f, 8f);
            SpawnOrBlock("Buildings/SmallBarn", parent, new Vector3(-7.4f, 0f, -4.8f), 0.5f, 20f);
            SpawnOrBlock("Buildings/Windmill", parent, new Vector3(8.8f, 0f, 9.0f), 0.5f, -24f);
            SpawnOrBlock("Buildings/WaterTower", parent, new Vector3(-10.6f, 0f, -5.4f), 0.55f, 0f);
            SpawnOrBlock("Buildings/ChickenCoop", parent, new Vector3(7.4f, 0f, -5.8f), 0.8f, -35f);
            SpawnOrBlock("Buildings/Well", parent, new Vector3(4.9f, 0f, -4.9f), 0.85f, 0f);

            SpawnScatter(
                parent,
                "Nature/Tree",
                4,
                new[]
                {
                    new PropPlacement(-12.5f, 3.2f, 1.0f, 20f),
                    new PropPlacement(-12.8f, -2.0f, 1.2f, -35f),
                    new PropPlacement(13.2f, 3.6f, 0.95f, 120f),
                    new PropPlacement(13.6f, 8.8f, 1.15f, -60f),
                    new PropPlacement(-7.2f, 14.2f, 1.3f, 0f),
                    new PropPlacement(5.2f, 14.6f, 1.1f, 80f)
                });

            SpawnScatter(
                parent,
                "Nature/Bush",
                3,
                new[]
                {
                    new PropPlacement(-9.4f, 2.2f, 1.1f, 15f),
                    new PropPlacement(-9.0f, -7.8f, 0.9f, 70f),
                    new PropPlacement(10.8f, -1.4f, 1.0f, -20f),
                    new PropPlacement(11.4f, -8.2f, 1.2f, 45f),
                    new PropPlacement(-1.2f, 12.8f, 1.0f, 0f)
                });

            SpawnScatter(
                parent,
                "Nature/Rock",
                3,
                new[]
                {
                    new PropPlacement(-6.4f, 8.6f, 0.9f, 0f),
                    new PropPlacement(6.8f, 9.2f, 0.7f, 40f),
                    new PropPlacement(-2.6f, -7.4f, 0.8f, 95f),
                    new PropPlacement(8.2f, 2.0f, 0.6f, 10f)
                });

            var grass = new PropPlacement[10];
            for (var i = 0; i < grass.Length; i++)
            {
                grass[i] = new PropPlacement(
                    -11f + (i % 5) * 5.4f,
                    i < 5 ? 14.6f : -9.4f,
                    0.9f,
                    i * 36f);
            }

            SpawnScatter(parent, "Nature/Grass", 3, grass);
        }

        readonly struct PropPlacement
        {
            public PropPlacement(float x, float z, float scale, float yaw)
            {
                X = x;
                Z = z;
                Scale = scale;
                Yaw = yaw;
            }

            public float X { get; }

            public float Z { get; }

            public float Scale { get; }

            public float Yaw { get; }
        }

        static void SpawnScatter(Transform parent, string prefix, int variants, PropPlacement[] placements)
        {
            for (var i = 0; i < placements.Length; i++)
            {
                var placement = placements[i];
                SpawnOrBlock(
                    $"{prefix}{(i % variants) + 1}",
                    parent,
                    new Vector3(placement.X, 0f, placement.Z),
                    placement.Scale,
                    placement.Yaw);
            }
        }

        static void SpawnOrBlock(string modelName, Transform parent, Vector3 ground, float scale, float yaw)
        {
            var model = QuaterniusArt.Load(modelName);
            if (model == null)
            {
                return;
            }

            var instance = ModelPlacement.Spawn(model, parent, ground, scale, yaw);
            var tint = TintFor(modelName, yaw);
            if (instance != null && tint.HasValue)
            {
                ModelPlacement.Tint(instance, tint.Value);
            }
        }

        /// <summary>太阳、环境光与雾；场景里已有方向光就复用。</summary>
        public static void ApplyLighting()
        {
            var sun = Object.FindFirstObjectByType<Light>();
            if (sun == null)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
            }

            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            sun.intensity = 1.1f;
            sun.color = new Color(1f, 0.97f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.40f, 0.48f, 0.60f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.47f, 0.40f);
            RenderSettings.ambientGroundColor = new Color(0.20f, 0.19f, 0.16f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.006f;
            RenderSettings.fogColor = new Color(0.62f, 0.72f, 0.82f);
        }

        /// <summary>URP 后处理：ACES 色调映射、轻 Bloom、暗角与一点饱和度。</summary>
        public static void ApplyPostProcessing(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            var data = camera.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }

            if (Object.FindFirstObjectByType<Volume>() != null)
            {
                return;
            }

            var volumeObject = new GameObject("Post FX");
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tonemapping = profile.Add<Tonemapping>();
            tonemapping.mode.Override(TonemappingMode.ACES);

            var bloom = profile.Add<Bloom>();
            bloom.threshold.Override(1.3f);
            bloom.intensity.Override(0.18f);
            bloom.scatter.Override(0.6f);

            var vignette = profile.Add<Vignette>();
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.45f);

            var color = profile.Add<ColorAdjustments>();
            color.postExposure.Override(0f);
            color.contrast.Override(8f);
            color.saturation.Override(6f);

            volume.sharedProfile = profile;
        }
    }
}
