using System;
using ElysFarm.Core;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// Quaternius 素材的入口：模型路径、回退开关与加载。
    /// 素材是 CC0（来源与许可见 README）；缺素材或显式关掉时返回 null，
    /// 由调用方退回体块，绝不抛异常——第 3 天的纯体块版本必须一直能跑。
    /// </summary>
    public static class QuaterniusArt
    {
        /// <summary>Resources 下的素材根目录。</summary>
        public const string Root = "Quaternius/";

        /// <summary>是否使用素材；演示启动时会读环境变量 <c>ELYS_FORCE_BLOCKS=1</c> 强制退回体块。</summary>
        public static bool UseArt { get; set; } = true;

        /// <summary>给演示启动时调用：环境变量要求体块时关掉素材。测试不调用，保持确定性。</summary>
        public static void ApplyEnvironmentOverride()
        {
            var forced = Environment.GetEnvironmentVariable("ELYS_FORCE_BLOCKS");
            if (string.Equals(forced, "1", StringComparison.OrdinalIgnoreCase))
            {
                UseArt = false;
            }
        }

        /// <summary>加载一棵作物或场景模型；找不到或出错时返回 null。</summary>
        public static GameObject Load(string modelPath)
        {
            if (!UseArt || string.IsNullOrEmpty(modelPath))
            {
                return null;
            }

            try
            {
                return Resources.Load<GameObject>(Root + modelPath);
            }
            catch (Exception ex)
            {
                // 素材缺失是常规路径（第 3 天的版本只有体块），不能把演示带崩。
                Debug.LogWarning($"[Elys] 素材加载失败：{modelPath} · {ex.Message}");
                return null;
            }
        }

        /// <summary>按品种与阶段加载作物模型；未知品种或素材缺失时返回 null。</summary>
        public static GameObject LoadCrop(string speciesId, CropStage stage)
        {
            var modelName = CropModelName(speciesId, stage);
            return string.IsNullOrEmpty(modelName) ? null : Load("Crops/" + modelName);
        }

        /// <summary>加载场景固定模型，例如 <c>Buildings/Barn</c>、<c>Characters/Worker_Male</c>。</summary>
        public static GameObject LoadModel(string modelPath)
        {
            return Load(modelPath);
        }

        /// <summary>NPC 的角色模型。三个人三种体型/装扮，俯视时也能一眼分开。</summary>
        public static string CharacterModelName(string npcId)
        {
            switch (npcId)
            {
                case FarmDemoBootstrap.AheId:
                    return "Worker_Male";
                case FarmDemoBootstrap.AyaId:
                    return "Casual_Female";
                case FarmDemoBootstrap.AtuId:
                    return "Casual_Male";
                default:
                    return null;
            }
        }

        /// <summary>按 NPC 加载角色模型；未知 NPC 或素材缺失时返回 null。</summary>
        public static GameObject LoadCharacter(string npcId)
        {
            var modelName = CharacterModelName(npcId);
            return string.IsNullOrEmpty(modelName) ? null : Load("Characters/" + modelName);
        }

        /// <summary>
        /// 按品种取某阶段在素材包里的模型名（不含目录与扩展名）。
        /// 素材包每个品种只有四个生长模型；成品模型（<c>_Crop</c>）是第五阶段。
        /// 小麦的 <c>Wheat_Crop</c> 是一块平铺的麦捆（俯视才成立，第三人称下不成立），
        /// 因此成熟小麦继续用 <c>Wheat_4</c>，靠 <see cref="CropStages.ColorOf"/> 的麦金色区分。
        /// </summary>
        public static string CropModelName(string speciesId, CropStage stage)
        {
            if (stage == CropStage.None)
            {
                return null;
            }

            if (speciesId == CropCatalog.RadishId)
            {
                return stage == CropStage.Mature
                    ? "Carrot_Crop"
                    : "Carrot_" + (int)stage;
            }

            if (speciesId == CropCatalog.WheatId)
            {
                var index = Mathf.Min((int)stage, (int)CropStage.Ripening);
                return "Wheat_" + index;
            }

            return null;
        }
    }
}
