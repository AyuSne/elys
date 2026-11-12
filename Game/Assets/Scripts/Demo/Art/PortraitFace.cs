using System.Collections.Generic;
using ElysFarm.Core;

namespace ElysFarm.Demo
{
    /// <summary>对话框头像的六档表情（见 #14）：平常、笑、得意、不耐烦、懵、累。</summary>
    public enum FaceEmotion
    {
        Neutral,
        Smile,
        Smug,
        Impatient,
        Confused,
        Tired
    }

    /// <summary>判断表情时的状态；状态优先于出口。</summary>
    public enum FaceCondition
    {
        Idle,
        Thinking,
        Resting
    }

    /// <summary>
    /// 把「出口 + 状态」映射到六档表情，并把表情换算成头像集里的格位。
    /// 全部是纯函数，测试不需要打开场景或加载贴图。
    /// </summary>
    public static class PortraitFace
    {
        /// <summary>头像集里一格 64×64；对话头像按 2× 放大到 128。</summary>
        public const int FrameSize = 64;

        const int Columns = 2;

        /// <summary>
        /// 每名 NPC 的头像集：图名、一共几张脸、六档表情各用第几格。
        /// 阿禾与阿芽的脸够用，六档各一张；阿土的头像集只有四张，
        /// 懵与不耐烦、累与得意就近共用。
        /// </summary>
        static readonly Dictionary<string, Sheet> Sheets = new Dictionary<string, Sheet>
        {
            { FarmDemoBootstrap.AheId, new Sheet("Portraits/Robin", 6, new[] { 0, 1, 4, 3, 5, 2 }) },
            { FarmDemoBootstrap.AyaId, new Sheet("Portraits/Haley", 12, new[] { 0, 1, 6, 3, 4, 11 }) },
            { FarmDemoBootstrap.AtuId, new Sheet("Portraits/Gus", 4, new[] { 0, 1, 2, 3, 3, 2 }) }
        };

        public static FaceEmotion Of(IntentOutlet outlet, bool accepted, FaceCondition condition)
        {
            if (condition == FaceCondition.Resting)
            {
                return FaceEmotion.Tired;
            }

            if (condition == FaceCondition.Thinking)
            {
                return FaceEmotion.Confused;
            }

            switch (outlet)
            {
                case IntentOutlet.FarmCommand:
                    return accepted ? FaceEmotion.Smug : FaceEmotion.Impatient;
                case IntentOutlet.Chat:
                    return FaceEmotion.Smile;
                default:
                    return FaceEmotion.Neutral;
            }
        }

        /// <summary>头像集在 Resources/PixelArt 下的名字；未知 NPC 返回 null。</summary>
        public static string AtlasOf(string npcId)
        {
            return SheetOf(npcId)?.Atlas;
        }

        /// <summary>头像集里一共几张脸；未知 NPC 为 0。</summary>
        public static int FrameCountOf(string npcId)
        {
            return SheetOf(npcId)?.FrameCount ?? 0;
        }

        /// <summary>某档表情在这名 NPC 的头像集里的格号；未知 NPC 返回 -1。</summary>
        public static int FrameOf(string npcId, FaceEmotion emotion)
        {
            var sheet = SheetOf(npcId);
            var index = (int)emotion;
            return sheet == null || index < 0 || index >= sheet.Frames.Length ? -1 : sheet.Frames[index];
        }

        /// <summary>头像集按两列排布：列是格号对 2 取余，行是格号除以 2。</summary>
        public static SpriteCell CellOfFrame(int frame)
        {
            if (frame < 0)
            {
                return default;
            }

            return new SpriteCell(frame % Columns, frame / Columns, FrameSize, FrameSize);
        }

        /// <summary>某名 NPC 某档表情的格位；未知 NPC 或格号越界时返回无效格位。</summary>
        public static SpriteCell CellOf(string npcId, FaceEmotion emotion)
        {
            var frame = FrameOf(npcId, emotion);
            return frame < 0 || frame >= FrameCountOf(npcId) ? default : CellOfFrame(frame);
        }

        static Sheet SheetOf(string npcId)
        {
            return npcId != null && Sheets.TryGetValue(npcId, out var sheet) ? sheet : null;
        }

        sealed class Sheet
        {
            public Sheet(string atlas, int frameCount, int[] frames)
            {
                Atlas = atlas;
                FrameCount = frameCount;
                Frames = frames;
            }

            public string Atlas { get; }

            public int FrameCount { get; }

            public int[] Frames { get; }
        }
    }
}
