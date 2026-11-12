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
        /// 每名 NPC 的六档表情落在自己头像集的第几格。
        /// 阿禾与阿芽的头像集各有六张以上的脸，六档各用一张；
        /// 阿土的头像集只有四张脸，懵与不耐烦、累与得意就近共用。
        /// </summary>
        static readonly int[] AheFrames = { 0, 1, 4, 3, 5, 2 };

        static readonly int[] AyaFrames = { 0, 1, 6, 3, 4, 11 };

        static readonly int[] AtuFrames = { 0, 1, 2, 3, 3, 2 };

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
            switch (npcId)
            {
                case FarmDemoBootstrap.AheId:
                    return "Portraits/Robin";
                case FarmDemoBootstrap.AyaId:
                    return "Portraits/Haley";
                case FarmDemoBootstrap.AtuId:
                    return "Portraits/Gus";
                default:
                    return null;
            }
        }

        /// <summary>头像集里一共几张脸；未知 NPC 为 0。</summary>
        public static int FrameCountOf(string npcId)
        {
            switch (npcId)
            {
                case FarmDemoBootstrap.AheId:
                    return 6;
                case FarmDemoBootstrap.AyaId:
                    return 12;
                case FarmDemoBootstrap.AtuId:
                    return 4;
                default:
                    return 0;
            }
        }

        /// <summary>某档表情在这名 NPC 的头像集里的格号；未知 NPC 返回 -1。</summary>
        public static int FrameOf(string npcId, FaceEmotion emotion)
        {
            var frames = FramesOf(npcId);
            var index = (int)emotion;
            return frames == null || index < 0 || index >= frames.Length ? -1 : frames[index];
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

        /// <summary>某名 NPC 某档表情的格位；未知 NPC 返回无效格位，由调用方回退。</summary>
        public static SpriteCell CellOf(string npcId, FaceEmotion emotion)
        {
            var frame = FrameOf(npcId, emotion);
            return frame < 0 ? default : CellOfFrame(frame);
        }

        static int[] FramesOf(string npcId)
        {
            switch (npcId)
            {
                case FarmDemoBootstrap.AheId:
                    return AheFrames;
                case FarmDemoBootstrap.AyaId:
                    return AyaFrames;
                case FarmDemoBootstrap.AtuId:
                    return AtuFrames;
                default:
                    return null;
            }
        }
    }
}
