using ElysFarm.Core;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 作物的五个生长阶段（表现层）。领域只给 0..1 的生长进度，
    /// 是哪一阶段、用图集里的哪一格是表现层的决定。
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
    /// 生长进度到阶段、再到图集格位的映射。
    /// 原作作物图集把每个品种排成一条 8 格的横条：两个品种共用一行，各占 8 列。
    /// 这里只取其中五格，让五个阶段两两可区分；成熟取整条的最后格（带果实/金黄那一格）。
    /// </summary>
    public static class CropStages
    {
        /// <summary>萝卜在原作图集里的行号（rowInSpriteSheet）；奇数行在右半边。</summary>
        public const int RadishRowInSheet = 7;

        /// <summary>小麦在原作图集里的行号；奇数行在右半边。</summary>
        public const int WheatRowInSheet = 11;

        /// <summary>一条作物横条的像素尺寸。</summary>
        public const int CellWidth = 16;

        public const int CellHeight = 32;

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

        /// <summary>
        /// 品种与阶段对应的图集格位。原作横条有 8 格，但每个品种的最后一格不一定有图：
        /// 萝卜的成熟格是第 6 格（大红根，第 7 格留给留茬再生、原作里是空的），
        /// 小麦的成熟格是第 5 格（密金色麦穗，第 6/7 格为空）。
        /// 未知品种或空阶段返回无效格位，调用方退回体块。
        /// </summary>
        public static SpriteCell CellOf(string speciesId, CropStage stage)
        {
            if (stage == CropStage.None || string.IsNullOrEmpty(speciesId))
            {
                return default;
            }

            int rowInSheet;
            if (speciesId == CropCatalog.RadishId)
            {
                rowInSheet = RadishRowInSheet;
            }
            else if (speciesId == CropCatalog.WheatId)
            {
                rowInSheet = WheatRowInSheet;
            }
            else
            {
                return default;
            }

            var column = rowInSheet % 2 == 0 ? 0 : 8;
            return new SpriteCell(column + PhaseColumnOf(speciesId, stage), rowInSheet / 2, CellWidth, CellHeight);
        }

        /// <summary>某品种的五个阶段取横条上的第几格。萝卜与小麦的成熟格不同。</summary>
        public static int PhaseColumnOf(string speciesId, CropStage stage)
        {
            var wheat = speciesId == CropCatalog.WheatId;
            switch (stage)
            {
                case CropStage.Seedling:
                    return 0;
                case CropStage.Sprout:
                    return 2;
                case CropStage.Growing:
                    return 3;
                case CropStage.Ripening:
                    return wheat ? 4 : 5;
                case CropStage.Mature:
                    return wheat ? 5 : 6;
                default:
                    return -1;
            }
        }

        /// <summary>体块回退时该阶段的颜色，也是素材缺失时唯一能用的「阶段色」。</summary>
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

        /// <summary>体块回退时该阶段的作物相对高度；像素素材不乘这个尺寸。</summary>
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
    }
}
