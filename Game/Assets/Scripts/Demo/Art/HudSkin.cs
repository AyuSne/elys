using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 羊皮纸 HUD 的皮肤：运行时生成 9 宫格贴图（圆角、深褐描边、投影），
    /// 字号只留三档——标题 18 / 正文 15 / 备注 12；间距按 8px 网格。
    /// IMGUI 没有内置圆角样式，这里用「一张小图 + <see cref="GUIStyle.border"/>」做 9 宫格缩放。
    /// 生成的贴图由 <see cref="Dispose"/> 释放；UI 素材不受素材开关影响。
    /// </summary>
    public sealed class HudSkin : System.IDisposable
    {
        public static readonly Color Parchment = new Color32(247, 233, 203, 255);
        public static readonly Color Cream = new Color32(255, 250, 236, 255);
        public static readonly Color Shade = new Color32(233, 214, 178, 255);
        public static readonly Color Outline = new Color32(74, 51, 32, 255);
        public static readonly Color Ink = new Color32(56, 39, 24, 255);
        public static readonly Color MutedInk = new Color32(122, 95, 64, 255);
        public static readonly Color Green = new Color32(88, 130, 72, 255);
        public static readonly Color GreenHover = new Color32(104, 146, 84, 255);
        public static readonly Color GreenActive = new Color32(74, 110, 60, 255);
        public static readonly Color BarTrough = new Color32(214, 190, 152, 255);

        static readonly Color Selection = new Color(0.35f, 0.51f, 0.28f, 0.35f);

        readonly Font font;
        readonly Texture2D panel;
        readonly Texture2D slot;
        readonly Texture2D portraitFrame;
        readonly Texture2D shadow;
        readonly Texture2D pill;
        readonly Texture2D pillHover;
        readonly Texture2D pillActive;
        readonly Texture2D pillGreen;
        readonly Texture2D pillGreenHover;
        readonly Texture2D pillGreenActive;
        readonly Texture2D chip;
        readonly Texture2D chipHover;
        readonly Texture2D input;
        readonly Texture2D inputFocused;
        readonly Texture2D white;

        public HudSkin()
        {
            font = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Arial" },
                18);

            panel = Rounded(48, 48, 12, 2, Parchment, Outline);
            slot = Rounded(40, 40, 8, 2, Shade, Outline);
            portraitFrame = Rounded(40, 40, 8, 2, Cream, new Color32(126, 166, 178, 255));
            shadow = Rounded(48, 48, 12, 0, new Color(0f, 0f, 0f, 0.31f), Color.clear);
            pill = Rounded(64, 32, 14, 2, Parchment, Outline);
            pillHover = Rounded(64, 32, 14, 2, Cream, Outline);
            pillActive = Rounded(64, 32, 14, 2, Shade, Outline);
            pillGreen = Rounded(64, 32, 14, 2, Green, Outline);
            pillGreenHover = Rounded(64, 32, 14, 2, GreenHover, Outline);
            pillGreenActive = Rounded(64, 32, 14, 2, GreenActive, Outline);
            chip = Rounded(64, 24, 8, 2, Shade, Outline);
            chipHover = Rounded(64, 24, 8, 2, Cream, Outline);
            input = Rounded(48, 48, 8, 2, Cream, Outline);
            inputFocused = Rounded(48, 48, 8, 2, Cream, MutedInk);
            white = WhiteTexture();

            Panel = Box(panel, 14);
            Slot = Box(slot, 10);
            PortraitFrame = Box(portraitFrame, 10);
            Shadow = Box(shadow, 14);

            Title = Label(18, Ink, FontStyle.Bold, TextAnchor.MiddleLeft);
            Body = Label(15, Ink, FontStyle.Normal, TextAnchor.MiddleLeft);
            BodyBold = Label(15, Ink, FontStyle.Bold, TextAnchor.MiddleLeft);
            Small = Label(12, MutedInk, FontStyle.Normal, TextAnchor.MiddleLeft);
            SmallInk = Label(12, Ink, FontStyle.Normal, TextAnchor.MiddleLeft);
            SmallRight = Label(12, MutedInk, FontStyle.Normal, TextAnchor.UpperRight);
            Count = Label(15, Ink, FontStyle.Bold, TextAnchor.MiddleRight);
            TagText = Label(18, Ink, FontStyle.Bold, TextAnchor.MiddleCenter);

            Pill = Button(15, 15, pill, pillHover, pillActive, Ink, Ink);
            PillSelected = Button(15, 15, pillGreen, pillGreenHover, pillGreenActive, Cream, Cream);
            SpeedPill = Button(12, 15, pill, pillHover, pillActive, Ink, Ink);
            SpeedPillSelected = Button(12, 15, pillGreen, pillGreenHover, pillGreenActive, Cream, Cream);
            Chip = Button(12, 9, chip, chipHover, chipHover, MutedInk, Ink);
            Send = Button(15, 15, pillGreen, pillGreenHover, pillGreenActive, Cream, Cream);
            Input = Field();
        }

        public GUIStyle Panel { get; }

        public GUIStyle Slot { get; }

        public GUIStyle PortraitFrame { get; }

        public GUIStyle Shadow { get; }

        public GUIStyle Title { get; }

        public GUIStyle Body { get; }

        public GUIStyle BodyBold { get; }

        public GUIStyle Small { get; }

        public GUIStyle SmallInk { get; }

        public GUIStyle SmallRight { get; }

        public GUIStyle Count { get; }

        public GUIStyle TagText { get; }

        public GUIStyle Pill { get; }

        public GUIStyle PillSelected { get; }

        public GUIStyle SpeedPill { get; }

        public GUIStyle SpeedPillSelected { get; }

        public GUIStyle Chip { get; }

        public GUIStyle Send { get; }

        public GUIStyle Input { get; }

        public Texture2D White => white;

        /// <summary>把 HUD 的字体与光标色套到 IMGUI 皮肤上；每帧调用一次即可。</summary>
        public void Apply()
        {
            if (font != null)
            {
                GUI.skin.font = font;
            }

            GUI.skin.settings.cursorColor = Ink;
            GUI.skin.settings.selectionColor = Selection;
        }

        /// <summary>画一块带投影的羊皮纸面板。</summary>
        public void DrawPanel(Rect rect, GUIStyle style)
        {
            GUI.Box(new Rect(rect.x, rect.y + 4f, rect.width, rect.height), GUIContent.none, Shadow);
            GUI.Box(rect, GUIContent.none, style);
        }

        public void Dispose()
        {
            Destroy(panel);
            Destroy(slot);
            Destroy(portraitFrame);
            Destroy(shadow);
            Destroy(pill);
            Destroy(pillHover);
            Destroy(pillActive);
            Destroy(pillGreen);
            Destroy(pillGreenHover);
            Destroy(pillGreenActive);
            Destroy(chip);
            Destroy(chipHover);
            Destroy(input);
            Destroy(inputFocused);
            Destroy(white);
        }

        GUIStyle Label(int size, Color color, FontStyle fontStyle, TextAnchor anchor)
        {
            return new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = size,
                fontStyle = fontStyle,
                alignment = anchor,
                wordWrap = false,
                clipping = TextClipping.Clip,
                richText = false,
                margin = new RectOffset(0, 0, 0, 0),
                normal = { textColor = color }
            };
        }

        static GUIStyle Box(Texture2D background, int border)
        {
            var style = new GUIStyle(GUI.skin.box)
            {
                border = new RectOffset(border, border, border, border),
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(border, border, border, border),
                stretchHeight = false,
                stretchWidth = false
            };
            style.normal.background = background;
            return style;
        }

        GUIStyle Button(
            int size,
            int border,
            Texture2D normal,
            Texture2D hover,
            Texture2D active,
            Color text,
            Color hoverText)
        {
            var style = new GUIStyle(GUI.skin.button)
            {
                font = font,
                fontSize = size,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(border, border, border, border),
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                stretchHeight = false,
                stretchWidth = false
            };

            style.normal.background = normal;
            style.normal.textColor = text;
            style.hover.background = hover;
            style.hover.textColor = hoverText;
            style.active.background = active;
            style.active.textColor = hoverText;
            return style;
        }

        GUIStyle Field()
        {
            var style = new GUIStyle(GUI.skin.textField)
            {
                font = font,
                fontSize = 15,
                alignment = TextAnchor.MiddleLeft,
                border = new RectOffset(10, 10, 10, 10),
                padding = new RectOffset(12, 12, 8, 8),
                margin = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Clip
            };

            style.normal.background = input;
            style.normal.textColor = Ink;
            style.hover.background = inputFocused;
            style.hover.textColor = Ink;
            style.focused.background = inputFocused;
            style.focused.textColor = Ink;
            return style;
        }

        static Texture2D WhiteTexture()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// 一张圆角矩形贴图。用有符号距离场算圆角与描边：
        /// 描边沿四条直边也保持同样的粗细，而不是只在四个角上出现。
        /// </summary>
        static Texture2D Rounded(int width, int height, int radius, int borderWidth, Color fill, Color edge)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[width * height];
            var halfWidth = width * 0.5f;
            var halfHeight = height * 0.5f;
            var innerWidth = Mathf.Max(0f, halfWidth - radius);
            var innerHeight = Mathf.Max(0f, halfHeight - radius);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var px = Mathf.Abs(x + 0.5f - halfWidth) - innerWidth;
                    var py = Mathf.Abs(y + 0.5f - halfHeight) - innerHeight;
                    var outside = Mathf.Sqrt(
                        Mathf.Max(px, 0f) * Mathf.Max(px, 0f)
                        + Mathf.Max(py, 0f) * Mathf.Max(py, 0f));
                    var distance = outside + Mathf.Min(Mathf.Max(px, py), 0f) - radius;

                    Color color;
                    if (distance <= -borderWidth)
                    {
                        color = fill;
                    }
                    else if (distance <= 0f)
                    {
                        color = edge;
                    }
                    else
                    {
                        color = Color.clear;
                    }

                    pixels[y * width + x] = color;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        static void Destroy(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(texture);
            }
            else
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
