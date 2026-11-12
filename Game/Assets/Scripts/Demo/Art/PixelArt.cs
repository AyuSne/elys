using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>角色朝向：与原作图集的行走行序一致（下、右、上、左）。</summary>
    public enum Facing
    {
        Down = 0,
        Right = 1,
        Up = 2,
        Left = 3
    }

    /// <summary>
    /// 图集里的一个格位：从左上角数第几列、第几行，以及格子的像素尺寸。
    /// 切图与断言都用它；<see cref="RectIn"/> 负责翻成 Unity 的左下角坐标。
    /// </summary>
    public readonly struct SpriteCell : IEquatable<SpriteCell>
    {
        public SpriteCell(int column, int row, int width, int height)
        {
            Column = column;
            Row = row;
            Width = width;
            Height = height;
        }

        public int Column { get; }

        public int Row { get; }

        public int Width { get; }

        public int Height { get; }

        public bool IsValid => Column >= 0 && Row >= 0 && Width > 0 && Height > 0;

        /// <summary>格位在贴图里的矩形；贴图高度用于把行号从左上角翻到左下角。</summary>
        public Rect RectIn(int textureHeight)
        {
            if (!IsValid)
            {
                return Rect.zero;
            }

            return new Rect(Column * Width, textureHeight - (Row + 1) * Height, Width, Height);
        }

        public bool Equals(SpriteCell other)
        {
            return Column == other.Column && Row == other.Row
                && Width == other.Width && Height == other.Height;
        }

        public override bool Equals(object obj)
        {
            return obj is SpriteCell other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Column;
                hash = (hash * 397) ^ Row;
                hash = (hash * 397) ^ Width;
                hash = (hash * 397) ^ Height;
                return hash;
            }
        }

        public override string ToString()
        {
            return $"({Column},{Row}) {Width}x{Height}";
        }

        public static bool operator ==(SpriteCell left, SpriteCell right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SpriteCell left, SpriteCell right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// 像素素材的入口：按名字取图、按格位切图，缺失或显式关掉素材时返回 null。
    /// 贴图导入规则在 <see cref="ElysFarm.EditorTools.PixelArtImportPostprocessor"/>：点采样、不压缩。
    /// 所有切格与四方向选择都是纯函数，测试不需要打开场景。
    /// </summary>
    public static class PixelArt
    {
        /// <summary>Resources 下的素材根目录。</summary>
        public const string Root = "PixelArt/";

        /// <summary>一格 16 像素；与 <see cref="FarmLayout.PixelsPerTile"/> 一致。</summary>
        public const int PixelsPerTile = FarmLayout.PixelsPerTile;

        public const int CharacterFrameWidth = 16;

        public const int CharacterFrameHeight = 32;

        public const int FramesPerDirection = 4;

        /// <summary>行走动画每帧的秒数；停下时锁回第一帧。</summary>
        public const float WalkSecondsPerFrame = 0.12f;

        public const string CropAtlasPath = "Crops/crops";

        public const string GrassAtlasPath = "Tiles/spring_outdoors";

        public const string HoeDirtAtlasPath = "Tiles/hoeDirt";

        public const string ItemAtlasPath = "Items/objects";

        /// <summary>物品图集是 24 列、每格 16×16；物品序号按行优先排布。</summary>
        public const int ItemColumns = 24;

        public const int ItemFrameSize = 16;

        /// <summary>背包条用的四件东西在图集里的序号：两包种子 + 两件产出。</summary>
        public const int RadishSeedIcon = 368;

        public const int WheatSeedIcon = 369;

        public const int RadishIcon = 284;

        public const int WheatIcon = 262;

        /// <summary>
        /// 立在地面上的东西（地标、树、草丛、角色）的落点：底边中点。
        /// 落点就是排序基准，换成中心会让「谁挡谁」和脚底位置一起偏。
        /// </summary>
        public static readonly Vector2 GroundPivot = new Vector2(0.5f, 0f);

        /// <summary>是否使用像素素材；演示启动时会读环境变量 <c>ELYS_FORCE_BLOCKS=1</c> 强制退回体块。</summary>
        public static bool UseArt { get; set; } = true;

        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Texture2D> UiTextures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Sprite> UiSprites = new Dictionary<string, Sprite>();

        /// <summary>给演示启动时调用：环境变量要求体块时关掉素材。测试不调用，保持确定性。</summary>
        public static void ApplyEnvironmentOverride()
        {
            var forced = Environment.GetEnvironmentVariable("ELYS_FORCE_BLOCKS");
            if (string.Equals(forced, "1", StringComparison.OrdinalIgnoreCase))
            {
                UseArt = false;
            }
        }

        /// <summary>清掉贴图与精灵缓存；只在测试或重新导入素材后调用。</summary>
        public static void ClearCache()
        {
            DestroySprites(Sprites);
            DestroySprites(UiSprites);
            Sprites.Clear();
            Textures.Clear();
            UiSprites.Clear();
            UiTextures.Clear();
        }

        static void DestroySprites(Dictionary<string, Sprite> sprites)
        {
            foreach (var sprite in sprites.Values)
            {
                if (sprite == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(sprite);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(sprite);
                }
            }
        }

        /// <summary>按名字加载一张图；关掉素材、名字为空或图不存在时返回 null。</summary>
        public static Texture2D LoadTexture(string name)
        {
            return LoadTexture(name, Textures, true);
        }

        /// <summary>
        /// 加载 UI 素材（头像、物品图标）。UI 不受「素材开/关」影响：
        /// 关掉素材只是把农场退回体块，HUD 不该跟着变空。
        /// </summary>
        public static Texture2D LoadUiTexture(string name)
        {
            return LoadTexture(name, UiTextures, false);
        }

        /// <summary>按格位切出一张精灵；任何一步失败都返回 null，由调用方退回体块。</summary>
        public static Sprite LoadSprite(string name, SpriteCell cell, Vector2 pivot)
        {
            return LoadSprite(name, cell, pivot, Sprites, true);
        }

        /// <summary>按格位切出一张 UI 精灵；缺失时返回 null。</summary>
        public static Sprite LoadUiSprite(string name, SpriteCell cell, Vector2 pivot)
        {
            return LoadSprite(name, cell, pivot, UiSprites, false);
        }

        static Texture2D LoadTexture(string name, Dictionary<string, Texture2D> cache, bool respectToggle)
        {
            if ((respectToggle && !UseArt) || string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (cache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            try
            {
                var texture = Resources.Load<Texture2D>(Root + name);
                cache[name] = texture;
                return texture;
            }
            catch (Exception ex)
            {
                // 素材缺失是常规路径（体块版本必须一直能跑），不能把演示带崩。
                Debug.LogWarning($"[Elys] 素材加载失败：{name} · {ex.Message}");
                return null;
            }
        }

        static Sprite LoadSprite(
            string name,
            SpriteCell cell,
            Vector2 pivot,
            Dictionary<string, Sprite> cache,
            bool respectToggle)
        {
            if ((respectToggle && !UseArt) || string.IsNullOrEmpty(name) || !cell.IsValid)
            {
                return null;
            }

            var texture = respectToggle ? LoadTexture(name) : LoadUiTexture(name);
            if (texture == null || texture.width < cell.Column * cell.Width + cell.Width
                || texture.height < cell.Row * cell.Height + cell.Height)
            {
                return null;
            }

            var key = $"{name}|{cell}|{pivot.x:0.###}|{pivot.y:0.###}";
            if (cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var sprite = Sprite.Create(
                texture,
                cell.RectIn(texture.height),
                pivot,
                PixelsPerTile,
                0,
                SpriteMeshType.FullRect);
            sprite.name = $"{name} {cell}";
            cache[key] = sprite;
            return sprite;
        }

        /// <summary>角色行走图集的 Resources 名字；未知 NPC 返回 null。</summary>
        public static string CharacterAtlasOf(string npcId)
        {
            switch (npcId)
            {
                case FarmDemoBootstrap.AheId:
                    return "Characters/Robin";
                case FarmDemoBootstrap.AyaId:
                    return "Characters/Haley";
                case FarmDemoBootstrap.AtuId:
                    return "Characters/Gus";
                default:
                    return null;
            }
        }

        /// <summary>四方向四帧的格位：列是帧、行是朝向。</summary>
        public static SpriteCell CharacterCell(Facing facing, int frame)
        {
            if (frame < 0 || frame >= FramesPerDirection)
            {
                return default;
            }

            return new SpriteCell(frame, (int)facing, CharacterFrameWidth, CharacterFrameHeight);
        }

        /// <summary>速度的主分量决定朝向；几乎不动时保持上一次朝向。</summary>
        public static Facing FacingOf(Vector2 velocity, Facing fallback)
        {
            if (velocity.sqrMagnitude < 0.0001f)
            {
                return fallback;
            }

            if (Mathf.Abs(velocity.x) >= Mathf.Abs(velocity.y))
            {
                return velocity.x >= 0f ? Facing.Right : Facing.Left;
            }

            return velocity.y >= 0f ? Facing.Up : Facing.Down;
        }

        /// <summary>行走帧序号 0..3；停下时回到第一帧。</summary>
        public static int WalkFrame(bool walking, float elapsed)
        {
            if (!walking)
            {
                return 0;
            }

            return Mathf.FloorToInt(Mathf.Max(0f, elapsed) / WalkSecondsPerFrame) % FramesPerDirection;
        }

        /// <summary>切出一帧角色行走图；用于验证格位与贴图。</summary>
        public static Sprite LoadCharacter(string npcId, Facing facing, int frame)
        {
            var atlas = CharacterAtlasOf(npcId);
            var cell = CharacterCell(facing, frame);
            return string.IsNullOrEmpty(atlas) ? null : LoadSprite(atlas, cell, GroundPivot);
        }

        /// <summary>切出某品种某阶段的作物图；格位由 <see cref="CropStages.CellOf"/> 决定。</summary>
        public static Sprite LoadCrop(string speciesId, CropStage stage)
        {
            var cell = CropStages.CellOf(speciesId, stage);
            return cell.IsValid ? LoadSprite(CropAtlasPath, cell, GroundPivot) : null;
        }

        /// <summary>物品图集里的格位；序号越界（负数）返回无效格位。</summary>
        public static SpriteCell ItemCell(int itemId)
        {
            if (itemId < 0)
            {
                return default;
            }

            return new SpriteCell(
                itemId % ItemColumns,
                itemId / ItemColumns,
                ItemFrameSize,
                ItemFrameSize);
        }

        /// <summary>切出一件物品的图标；缺失时返回 null，由 HUD 跳过或回退。</summary>
        public static Sprite LoadItem(int itemId)
        {
            var cell = ItemCell(itemId);
            return cell.IsValid ? LoadUiSprite(ItemAtlasPath, cell, Center) : null;
        }

        /// <summary>切出某名 NPC 的某档表情头像；未知 NPC 或无效格位返回 null。</summary>
        public static Sprite LoadPortrait(string npcId, FaceEmotion emotion)
        {
            var atlas = PortraitFace.AtlasOf(npcId);
            var cell = PortraitFace.CellOf(npcId, emotion);
            return string.IsNullOrEmpty(atlas) || !cell.IsValid
                ? null
                : LoadUiSprite(atlas, cell, Center);
        }

        /// <summary>切出一张头顶表情；事件或帧号越界、素材关掉或缺失时返回 null。</summary>
        public static Sprite LoadEmote(EmoteEvent emote, int frame)
        {
            var cell = OverheadEmotes.CellOf(emote, frame);
            return cell.IsValid
                ? LoadSprite(OverheadEmotes.AtlasPath, cell, GroundPivot)
                : null;
        }

        /// <summary>切一块 16×16 的室外瓦片；用于生成地面 Tilemap。</summary>
        public static Sprite LoadTile(int column, int row)
        {
            return LoadSprite(GrassAtlasPath, new SpriteCell(column, row, 16, 16), Center);
        }

        /// <summary>耕地土块：干土取第 2 列、湿土取第 6 列，都在第 1 行（四边可拼接）。</summary>
        public static Sprite LoadHoeDirt(bool watered)
        {
            return LoadSprite(HoeDirtAtlasPath, new SpriteCell(watered ? 6 : 2, 1, 16, 16), Center);
        }

        /// <summary>角色脚下的影子；素材缺失时调用方跳过。</summary>
        public static Sprite LoadShadow()
        {
            return LoadSprite("LooseSprites/shadow", new SpriteCell(0, 0, 12, 7), new Vector2(0.5f, 0.35f));
        }

        /// <summary>地标用哪张图。素材关掉或缺失时由调用方跳过这个地标。</summary>
        public static string LandmarkAtlasOf(LandmarkKind kind)
        {
            switch (kind)
            {
                case LandmarkKind.Farmhouse:
                    return "Buildings/farmhouse";
                case LandmarkKind.Coop:
                    return "Buildings/coop";
                case LandmarkKind.Well:
                    return "Buildings/well";
                case LandmarkKind.ProduceStore:
                    return "Buildings/barrel";
                default:
                    return null;
            }
        }

        /// <summary>装饰用哪张图；变体越界返回 null，由调用方跳过。</summary>
        public static string DecorationAtlasOf(DecorationKind kind, int variant)
        {
            if (variant < 0 || variant >= FarmLayout.VariantCountOf(kind))
            {
                return null;
            }

            switch (kind)
            {
                case DecorationKind.Tree:
                    return variant == 0 ? "Nature/tree1" : "Nature/tree2";
                case DecorationKind.Stump:
                    return "Nature/stump";
                case DecorationKind.GrassTuft:
                    switch (variant)
                    {
                        case 0:
                            return "Nature/grass1";
                        case 1:
                            return "Nature/grass2";
                        default:
                            return "Nature/grass3";
                    }

                default:
                    return null;
            }
        }

        /// <summary>
        /// 整张图当一张精灵、落点取底边中点：地标与装饰都是单独裁好的 PNG，不需要切格。
        /// </summary>
        public static Sprite LoadWhole(string name)
        {
            var texture = LoadTexture(name);
            if (texture == null)
            {
                return null;
            }

            return LoadSprite(name, new SpriteCell(0, 0, texture.width, texture.height), GroundPivot);
        }

        /// <summary>一座地标的图；缺失时返回 null，场景退回体块。</summary>
        public static Sprite LoadLandmark(LandmarkKind kind)
        {
            return LoadWhole(LandmarkAtlasOf(kind));
        }

        /// <summary>一棵树、一段树桩或一丛草的图；缺失时返回 null，场景跳过它。</summary>
        public static Sprite LoadDecoration(DecorationKind kind, int variant)
        {
            return LoadWhole(DecorationAtlasOf(kind, variant));
        }
    }
}
