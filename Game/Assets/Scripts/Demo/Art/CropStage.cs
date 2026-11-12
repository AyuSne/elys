using ElysFarm.Core;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 作物的五个生长阶段（表现层）。领域只给 0..1 的生长进度，
    /// 是哪一阶段、用哪个模型是表现层的决定。
    /// </summary>
    public enum CropStage
    {
        /// <summary>没有作物可看：空地或已收获。</summary>
        None = 0,

        /// <summary>刚发芽。</summary>
        Seedling = 1,

        /// <summary>幼苗。</summary>
        Sprout = 2,

        /// <summary>长高。</summary>
        Growing = 3,

        /// <summary>快熟了。</summary>
        Ripening = 4,

        /// <summary>成熟，可以收获。</summary>
        Mature = 5
    }

    /// <summary>
    /// 生长进度到阶段的映射。
    /// </summary>
    public static class CropStages
    {
        /// <summary>五个可见阶段，不含 <see cref="CropStage.None"/>。</summary>
        public const int VisibleCount = 5;

        public static CropStage Of(FieldState state, float growth)
        {
            if (state == FieldState.Empty || state == FieldState.Harvested)
            {
                return CropStage.None;
            }

            if (state == FieldState.Mature || growth >= 1f)
            {
                return CropStage.Mature;
            }

            if (growth < 0.25f)
            {
                return CropStage.Seedling;
            }

            if (growth < 0.50f)
            {
                return CropStage.Sprout;
            }

            if (growth < 0.75f)
            {
                return CropStage.Growing;
            }

            return CropStage.Ripening;
        }

        /// <summary>体块回退时该阶段的颜色，也是决策面板之外唯一用到的「阶段色」。</summary>
        public static Color ColorOf(CropStage stage)
        {
            switch (stage)
            {
                case CropStage.Seedling:
                    return new Color(0.55f, 0.80f, 0.42f);
                case CropStage.Sprout:
                    return new Color(0.36f, 0.70f, 0.34f);
                case CropStage.Growing:
                    return new Color(0.24f, 0.62f, 0.24f);
                case CropStage.Ripening:
                    return new Color(0.55f, 0.64f, 0.25f);
                case CropStage.Mature:
                    return new Color(0.95f, 0.74f, 0.16f);
                default:
                    return Color.clear;
            }
        }

        /// <summary>该阶段的作物相对尺寸；体块回退与成品模型都按它缩放。</summary>
        public static float ScaleOf(CropStage stage)
        {
            switch (stage)
            {
                case CropStage.Seedling:
                    return 0.35f;
                case CropStage.Sprout:
                    return 0.50f;
                case CropStage.Growing:
                    return 0.70f;
                case CropStage.Ripening:
                    return 0.90f;
                case CropStage.Mature:
                    return 1.15f;
                default:
                    return 0f;
            }
        }

        /// <summary>
        /// 成品模型自带生长比例（越往后越高），所以模型不再乘 <see cref="ScaleOf"/>；
        /// 统一放大到能在 3 米见方的地里看清，成熟期再略高一点。
        /// </summary>
        public static float ModelScaleOf(CropStage stage)
        {
            return stage == CropStage.Mature ? 1.25f : 1.20f;
        }
    }
}
