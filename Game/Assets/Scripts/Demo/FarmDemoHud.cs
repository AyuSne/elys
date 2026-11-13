using System.Collections.Generic;
using System.Text;
using ElysFarm.Core;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 羊皮纸风格的常驻 HUD：顶部一条（第几天 / 当天进度 / 倍速）、左上三人卡、
    /// 左下背包条、底部对话框；决策可视化退到 <c>Tab</c> 召唤的右侧抽屉。
    /// 除顶部条的三档倍速（演示用时间控制，不是自然语言输入）外只读快照：
    /// 不推进时钟、不扣体力、不写记忆。
    /// </summary>
    [RequireComponent(typeof(FarmDemoBootstrap))]
    public sealed class FarmDemoHud : MonoBehaviour
    {
        const string CommandControl = "ElysCommandInput";

        const float Margin = 16f;
        const float TopBarWidth = 376f;
        const float TopBarY = 10f;
        const float TopBarHeight = 44f;
        const float CardX = Margin;
        const float CardY = 62f;
        const float CardWidth = 280f;
        const float CardHeight = 192f;
        const float HeaderHeight = 36f;
        const float RowHeight = 46f;
        const float DialogueX = 24f;
        const float DialogueHeight = 168f;
        const float DialogueGap = 16f;
        const float BagSlot = 52f;
        const float BagGap = 6f;
        const float DrawerWidth = 420f;

        /// <summary>三人卡行内「取消」按钮的宽度；行里留出这么宽才不压到意图文字。</summary>
        const float CancelWidth = 52f;

        /// <summary>三人卡一行里头像那一列的宽度：名字、体力条与意图文字都从这之后开始。</summary>
        const float RowTextOffset = 48f;

        /// <summary>三人卡那一行意图文字的可用宽度：行宽减去头像与名字那一列。</summary>
        const float IntentWidth = CardWidth - 16f - RowTextOffset;

        /// <summary>对话框正文最多两行；本地追问的回答可能到 80+ 字，超出就省略，不能硬切在边外。</summary>
        const int ReplyLimit = 110;

        static readonly string[] Examples =
        {
            "去东边那块地除草",
            "听说阿芽偷懒了",
            "去种月亮"
        };

        FarmDemoBootstrap demo;
        HudSkin skin;
        string commandInput = string.Empty;
        Vector2 drawerScroll;

        /// <summary>决策抽屉是否打开；Tab、关闭按钮、剧本回放与压测共用这一个开关。</summary>
        public bool InspectorOpen { get; set; }

        void Start()
        {
            demo = GetComponent<FarmDemoBootstrap>();
            // HudSkin 要读 GUI.skin，只能在 OnGUI 里构造；见 OnGUI 的懒初始化。
        }

        void OnDestroy()
        {
            skin?.Dispose();
            skin = null;
        }

        void OnGUI()
        {
            if (demo == null || demo.World == null)
            {
                return;
            }

            if (skin == null)
            {
                skin = new HudSkin();
            }

            skin.Apply();
            HandleKeys();
            DrawTopBar();
            if (!InspectorOpen)
            {
                DrawTopControls();
            }

            DrawCaption();
            DrawNpcCard();
            DrawBag();
            DrawDialogue();
            if (InspectorOpen)
            {
                DrawDrawer();
            }
        }

        /// <summary>
        /// 键盘兜底：Tab 开关抽屉，Esc 关抽屉；输入框有焦点时回车发送，
        /// 镜头快捷键只在没有控件抢键盘时生效。
        /// </summary>
        void HandleKeys()
        {
            var current = Event.current;
            if (current.type != EventType.KeyDown)
            {
                return;
            }

            if (current.keyCode == KeyCode.Tab)
            {
                InspectorOpen = !InspectorOpen;
                current.Use();
                return;
            }

            if (current.keyCode == KeyCode.Escape)
            {
                if (InspectorOpen)
                {
                    InspectorOpen = false;
                    current.Use();
                }

                return;
            }

            if (GUI.GetNameOfFocusedControl() == CommandControl)
            {
                if ((current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter) && Submit())
                {
                    current.Use();
                }

                return;
            }

            if (GUIUtility.keyboardControl != 0)
            {
                return;
            }

            switch (current.keyCode)
            {
                case KeyCode.C:
                    demo.UseOverheadCamera();
                    current.Use();
                    break;
                case KeyCode.Alpha1:
                    Follow(FarmDemoBootstrap.AheId);
                    current.Use();
                    break;
                case KeyCode.Alpha2:
                    Follow(FarmDemoBootstrap.AyaId);
                    current.Use();
                    break;
                case KeyCode.Alpha3:
                    Follow(FarmDemoBootstrap.AtuId);
                    current.Use();
                    break;
            }
        }

        void Follow(string npcId)
        {
            demo.SelectNpc(npcId);
            demo.FollowCamera(npcId);
        }

        bool Submit()
        {
            if (demo.IsThinking || string.IsNullOrWhiteSpace(commandInput))
            {
                return false;
            }

            demo.SubmitPlayerText(commandInput);
            commandInput = string.Empty;
            return true;
        }

        void DrawTopBar()
        {
            var rect = new Rect(Mathf.Round((Screen.width - TopBarWidth) * 0.5f), TopBarY, TopBarWidth, TopBarHeight);
            skin.DrawPanel(rect, skin.Panel);

            var clock = demo.World.Clock;
            GUI.Label(new Rect(rect.x + 18f, rect.y, 100f, rect.height), $"第 {clock.Day} 天", skin.Title);
            GUI.Label(new Rect(rect.x + 112f, rect.y, 110f, rect.height), $"当天 {clock.DayProgress:P0}", skin.Body);

            var speeds = FarmClock.AllowedSpeeds;
            const float pillWidth = 44f;
            const float pillGap = 8f;
            var total = speeds.Count * pillWidth + (speeds.Count - 1) * pillGap;
            var x = rect.xMax - 4f - total;
            var y = rect.y + (rect.height - 28f) * 0.5f;

            for (var i = 0; i < speeds.Count; i++)
            {
                var speed = speeds[i];
                var selected = Mathf.Approximately(clock.Speed, speed);
                if (GUI.Button(
                        new Rect(x + i * (pillWidth + pillGap), y, pillWidth, 28f),
                        $"{speed:0}x",
                        selected ? skin.SpeedPillSelected : skin.SpeedPill))
                {
                    // 演示用的时间控制，不是自然语言输入；文本输入仍只经 IntentRouter。
                    clock.Speed = speed;
                }
            }
        }

        /// <summary>右上角：镜头档位、观察对象与素材开关；抽屉打开时让位给抽屉。</summary>
        void DrawTopControls()
        {
            const float width = 46f;
            const float artWidth = 58f;
            const float gap = 8f;
            var count = 2 + demo.World.Npcs.Count;
            var total = count * width + (count - 1) * gap + (artWidth - width);
            var x = Screen.width - 36f - total;
            const float y = 16f;
            var index = 0;

            if (GUI.Button(PillRect(x, y, width, gap, ref index), "俯视", demo.IsOverheadCamera ? skin.PillSelected : skin.Pill))
            {
                demo.UseOverheadCamera();
            }

            foreach (var npc in demo.World.Npcs)
            {
                var selected = demo.IsFollowing(npc.Id);
                if (GUI.Button(PillRect(x, y, width, gap, ref index), npc.DisplayName, selected ? skin.PillSelected : skin.Pill))
                {
                    Follow(npc.Id);
                }
            }

            var artX = x + (count - 1) * (width + gap);
            if (GUI.Button(new Rect(artX, y, artWidth, 30f), demo.UsingArt ? "素材开" : "素材关", skin.SpeedPill))
            {
                demo.ToggleArt();
            }

            GUI.Label(new Rect(x, y + 38f, total, 22f), "Tab 决策可视化 · 流水", skin.SmallInk);
        }

        static Rect PillRect(float x, float y, float width, float gap, ref int index)
        {
            var rect = new Rect(x + index * (width + gap), y, width, 30f);
            index++;
            return rect;
        }

        /// <summary>
        /// 剧本字幕：夹在三人卡与右侧抽屉之间的横幅。只在剧本回放与压测里出现，
        /// 正常游玩时为空、不占地方。
        /// </summary>
        void DrawCaption()
        {
            if (string.IsNullOrEmpty(demo.CaptionTitle))
            {
                return;
            }

            var left = CardX + CardWidth + 18f;
            var right = InspectorOpen ? Screen.width - DrawerWidth - 12f : Screen.width - 36f;
            var rect = new Rect(left, CardY, Mathf.Max(320f, right - left), 96f);
            skin.DrawPanel(rect, skin.Panel);
            GUI.Label(
                new Rect(rect.x + 14f, rect.y + 2f, rect.width - 28f, 30f),
                demo.CaptionTitle,
                skin.Title);
            GUI.Label(
                new Rect(rect.x + 14f, rect.y + 30f, rect.width - 28f, rect.height - 34f),
                demo.CaptionDetail,
                skin.BodyWrap);
        }

        void DrawNpcCard()
        {
            var rect = new Rect(CardX, CardY, CardWidth, CardHeight);
            skin.DrawPanel(rect, skin.Panel);
            GUI.Label(new Rect(rect.x + 18f, rect.y + 6f, 160f, 30f), "三名 NPC", skin.Title);

            // 卡头就是自主开关：开局待命，点一下让她们自己找活；再点暂停。
            if (GUI.Button(
                    new Rect(rect.xMax - 100f, rect.y + 12f, 84f, 24f),
                    demo.AutonomyEnabled ? "自主中" : "待命",
                    skin.Chip))
            {
                demo.ToggleAutonomy();
            }

            var y = rect.y + HeaderHeight;
            foreach (var npc in demo.World.Npcs)
            {
                DrawNpcRow(new Rect(rect.x + 8f, y, rect.width - 16f, RowHeight), npc);
                y += RowHeight;
            }
        }

        void DrawNpcRow(Rect row, Npc npc)
        {
            if (npc == demo.SelectedNpc)
            {
                var previous = GUI.color;
                GUI.color = HudSkin.Green;
                GUI.DrawTexture(new Rect(row.x - 4f, row.y + 6f, 3f, 38f), skin.White);
                GUI.color = previous;
            }

            // 「取消」占行内右上角（不越出 RowHeight，免得它吃掉下一行的点击），并先于整行按钮声明：
            // IMGUI 按声明顺序命中，先声明的控件先拿到鼠标，否则整行那个透明按钮会吞掉这一下。
            // 正在等上一句时按钮置灰：那一下不会被收下，也就不该看起来可点。
            var hasOrder = demo.World.WorkOrderOf(npc.Id) != null;
            var canCancel = hasOrder && !demo.IsThinking;
            var previousEnabled = GUI.enabled;
            GUI.enabled = canCancel;
            var cancelClicked = canCancel
                && GUI.Button(
                    new Rect(row.xMax - CancelWidth, row.y + 10f, CancelWidth, 22f),
                    "取消",
                    skin.Chip);
            GUI.enabled = previousEnabled;

            if (cancelClicked)
            {
                demo.CancelWorkOrder(npc.Id);
            }

            if (GUI.Button(row, GUIContent.none, GUIStyle.none))
            {
                demo.SelectNpc(npc.Id);
            }

            var frame = new Rect(row.x, row.y, 36f, 36f);
            GUI.Box(frame, GUIContent.none, skin.Slot);
            DrawFace(new Rect(frame.x + 2f, frame.y + 2f, 32f, 32f), npc);

            GUI.Label(new Rect(row.x + RowTextOffset, row.y + 12f, 110f, 26f), npc.DisplayName, skin.BodyBold);
            GUI.Label(
                new Rect(row.xMax - CancelWidth - 50f, row.y + 14f, 44f, 22f),
                $"{npc.Stamina:P0}",
                skin.SmallRight);
            DrawBar(
                new Rect(row.x + RowTextOffset, row.y + 34f, 146f, 8f),
                npc.Stamina,
                npc.IsResting ? HudSkin.GreenActive : HudSkin.Green);
            GUI.Label(
                new Rect(row.x + RowTextOffset, row.y + 42f, IntentWidth, 22f),
                IntentLine(npc),
                skin.Small);
        }

        string IntentLine(Npc npc)
        {
            // 有工单时与决策抽屉第 2 项是同一句话（等条件 / 跳过 / 没做成），没有工单才用演示层的临时状态。
            // 卡片窄，按可用宽度收尾，不把字切在边上。
            var line = FarmDemoText.CardIntent(demo.World, npc, demo.IntentOf(npc.Id));
            return FarmDemoText.Fit(line, IntentWidth, text => skin.Small.CalcSize(new GUIContent(text)).x);
        }

        /// <summary>左下背包条：四格图标 + 数量；数字直接读三本账，不另存一份。</summary>
        void DrawBag()
        {
            var ledger = demo.World.Ledger;
            var y = DialogueTop() - 56f - BagSlot;
            var x = Margin;
            var radish = CropCatalog.RadishId;
            var wheat = CropCatalog.WheatId;

            DrawBagSlot(new Rect(x, y, BagSlot, BagSlot), PixelArt.RadishSeedIcon, ledger.SeedsOf(radish));
            DrawBagSlot(new Rect(x + (BagSlot + BagGap), y, BagSlot, BagSlot), PixelArt.WheatSeedIcon, ledger.SeedsOf(wheat));
            DrawBagSlot(new Rect(x + (BagSlot + BagGap) * 2f, y, BagSlot, BagSlot), PixelArt.RadishIcon, ledger.ProduceOf(radish));
            DrawBagSlot(new Rect(x + (BagSlot + BagGap) * 3f, y, BagSlot, BagSlot), PixelArt.WheatIcon, ledger.ProduceOf(wheat));

            var label = $"随身 {ledger.HeldTotal}";
            if (ledger.HeldTotal > 0)
            {
                GUI.Label(
                    new Rect(x + (BagSlot + BagGap) * 4f, y + BagSlot - 20f, 70f, 18f),
                    label,
                    skin.Small);
            }
        }

        void DrawBagSlot(Rect rect, int itemId, int count)
        {
            GUI.Box(rect, GUIContent.none, skin.Slot);

            var sprite = PixelArt.LoadItem(itemId);
            if (sprite != null)
            {
                DrawSprite(new Rect(rect.x + 10f, rect.y + 8f, 32f, 32f), sprite);
            }

            GUI.Label(new Rect(rect.x, rect.yMax - 26f, rect.width - 5f, 26f), count.ToString(), skin.Count);
        }

        void DrawDialogue()
        {
            var box = new Rect(DialogueX, DialogueTop(), Screen.width - DialogueX * 2f, DialogueHeight);
            skin.DrawPanel(box, skin.Panel);

            var tag = new Rect(box.x + 160f, box.y - 30f, 76f, 32f);
            skin.DrawPanel(tag, skin.Panel);
            GUI.Label(tag, SpeakerName(), skin.TagText);
            DrawLocalTemplateBadge(tag);

            var frame = new Rect(box.x + 4f, box.y + 4f, 136f, 136f);
            GUI.Box(frame, GUIContent.none, skin.PortraitFrame);
            DrawFace(new Rect(frame.x + 4f, frame.y + 4f, 128f, 128f), Speaker());

            var textX = box.x + 160f;
            var textWidth = box.width - 160f - 140f;
            var reply = DialogueLine();
            var replyHeight = Mathf.Clamp(
                skin.BodyWrap.CalcHeight(new GUIContent(reply), textWidth),
                27f,
                52f);
            GUI.Label(new Rect(textX, box.y + 12f, textWidth, replyHeight), reply, skin.BodyWrap);

            // 一行回答时保持锚点布局；两行时把「你：…」与 chips / 输入框往下让，
            // 同时收紧两处行距，保证 168 高的对话框仍装得下发送按钮。
            var extra = replyHeight - 27f;
            var hintY = box.y + 42f + extra;
            GUI.Label(new Rect(textX, hintY, textWidth, 22f), DialogueHint(), skin.Small);

            var chipX = textX;
            var chipY = hintY + 22f + Mathf.Lerp(16f, 2f, extra / 25f);
            foreach (var example in Examples)
            {
                var chipWidth = skin.Chip.CalcSize(new GUIContent(example)).x + 22f;
                if (GUI.Button(new Rect(chipX, chipY, chipWidth, 20f), example, skin.Chip))
                {
                    commandInput = example;
                    Submit();
                }

                chipX += chipWidth + 8f;
            }

            var inputY = chipY + 20f + Mathf.Lerp(6f, 4f, extra / 25f);
            var inputRect = new Rect(
                box.x + 164f,
                inputY,
                box.width - 164f - 128f - 14f,
                46f);
            GUI.SetNextControlName(CommandControl);
            commandInput = GUI.TextField(inputRect, commandInput ?? string.Empty, 120, skin.Input);
            if (string.IsNullOrEmpty(commandInput) && GUI.GetNameOfFocusedControl() != CommandControl)
            {
                // TextField 会把光标色留在 GUI.color 里；占位文字显式用不透明颜色画，免得被冲淡。
                var previousColor = GUI.color;
                var previousContent = GUI.contentColor;
                GUI.color = Color.white;
                GUI.contentColor = Color.white;
                GUI.Label(
                    new Rect(inputRect.x + 14f, inputRect.y, inputRect.width - 20f, inputRect.height),
                    $"对 {TargetName()} 说…",
                    skin.SmallInk);
                GUI.contentColor = previousContent;
                GUI.color = previousColor;
            }

            var send = new Rect(box.xMax - 128f, inputY + 2f, 94f, 42f);
            GUI.enabled = !demo.IsThinking;
            if (GUI.Button(send, "下达", skin.Send))
            {
                Submit();
            }

            GUI.enabled = true;
        }

        /// <summary>
        /// 这句话不是模型说的（离线、分类降级或表达层没给出可用台词）时，在名字旁标出来：
        /// 观察者一眼能看出现在用的是本地模板，而不是模型在回话。
        /// </summary>
        void DrawLocalTemplateBadge(Rect tag)
        {
            if (!ShowsLocalTemplate())
            {
                return;
            }

            var badge = new Rect(tag.xMax + 6f, tag.y + 2f, 82f, 28f);
            skin.DrawPanel(badge, skin.Panel);
            GUI.Label(badge, "本地模板", skin.SmallInk);
        }

        bool ShowsLocalTemplate()
        {
            if (demo.IsThinkingAboutSelected || demo.IsRetellingSelected)
            {
                return false;
            }

            var round = demo.CurrentRound;
            return round.HasValue
                && round.Value.Outlet == IntentOutlet.Chat
                && round.Value.UsedFallback;
        }

        string DialogueLine()
        {
            if (demo.IsThinkingAboutSelected)
            {
                return "……（正在理解你这句话）";
            }

            if (demo.IsRetellingSelected)
            {
                return FarmDemoText.Shorten(demo.RetoldLine, ReplyLimit);
            }

            var round = demo.CurrentRound;
            if (!round.HasValue)
            {
                return $"还没和{SpeakerName()}聊过。";
            }

            var reply = FarmDemoText.Shorten(round.Value.Reply, ReplyLimit);
            return string.IsNullOrWhiteSpace(reply) ? "……" : reply;
        }

        string DialogueHint()
        {
            if (demo.IsThinkingAboutSelected)
            {
                return "等模型回话…（游戏没有暂停）";
            }

            if (demo.IsRetellingSelected)
            {
                return "她们自己聊的；说句话就能把话头接回来。";
            }

            var round = demo.CurrentRound;
            if (!round.HasValue)
            {
                return demo.UsingModel
                    ? "先说句话（模型在线）；卡片右上角可以切换她们自己找活。"
                    : "先说句话（本地模板）；卡片右上角可以切换她们自己找活。";
            }

            return $"你：{FarmDemoText.Shorten(round.Value.PlayerText, 32)} · {round.Value.Understood}";
        }

        Npc Speaker()
        {
            foreach (var npc in demo.World.Npcs)
            {
                if (npc.Id == demo.SpeakerId)
                {
                    return npc;
                }
            }

            return demo.SelectedNpc;
        }

        string SpeakerName()
        {
            return DisplayNameOf(Speaker());
        }

        /// <summary>下一句话发给谁；NPC 之间转述占用对话框时，输入框仍指向指挥对象。</summary>
        string TargetName()
        {
            return DisplayNameOf(demo.SelectedNpc);
        }

        static string DisplayNameOf(Npc npc)
        {
            return npc != null ? npc.DisplayName : "NPC";
        }

        void DrawFace(Rect rect, Npc npc)
        {
            if (npc == null)
            {
                return;
            }

            var sprite = PixelArt.LoadPortrait(npc.Id, EmotionOf(npc.Id));
            if (sprite != null)
            {
                DrawSprite(rect, sprite);
                return;
            }

            var previous = GUI.color;
            GUI.color = npc == demo.SelectedNpc ? HudSkin.Green : HudSkin.Shade;
            GUI.DrawTexture(rect, skin.White);
            GUI.color = previous;
            GUI.Label(rect, npc.DisplayName.Substring(0, 1), skin.TagText);
        }

        /// <summary>卡片里只有正在说话的人用最近一次出口，其余人保持平常脸。</summary>
        FaceEmotion EmotionOf(string npcId)
        {
            var condition = demo.FaceConditionOf(npcId);
            if (npcId != demo.SpeakerId)
            {
                return condition == FaceCondition.Resting ? FaceEmotion.Tired : FaceEmotion.Neutral;
            }

            return PortraitFace.Of(demo.LastOutlet, demo.LastAccepted, condition);
        }

        void DrawDrawer()
        {
            var height = DialogueTop() - DialogueGap;
            var rect = new Rect(Screen.width - DrawerWidth, 0f, DrawerWidth, height);
            skin.DrawPanel(rect, skin.Panel);

            GUI.Label(new Rect(rect.x + 16f, rect.y + 8f, 220f, 30f), "决策可视化", skin.Title);
            if (GUI.Button(new Rect(rect.xMax - 88f, rect.y + 12f, 72f, 26f), "关闭", skin.Chip))
            {
                InspectorOpen = false;
            }

            GUI.Label(new Rect(rect.x + 16f, rect.y + 42f, rect.width - 32f, 22f), "Tab 开关 · 只读快照", skin.Small);

            var content = new Rect(rect.x + 16f, rect.y + 68f, rect.width - 32f, rect.height - 80f);
            GUILayout.BeginArea(content);
            drawerScroll = GUILayout.BeginScrollView(drawerScroll, false, false);

            var snapshot = demo.World.Inspect(demo.SelectedNpc.Id, demo.LastCall);
            DrawMemories(snapshot.Memories);
            DrawIntent(snapshot.NpcName, snapshot.Intent);
            DrawCandidates(snapshot.Candidates, snapshot.FieldClaims);
            DrawModelCall(snapshot.ModelCall);
            DrawStatus(snapshot.Status);
            DrawLog();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawMemories(IReadOnlyList<MemoryHit> memories)
        {
            GUILayout.Label("① 检索命中的记忆", skin.BodyBold);
            if (memories.Count == 0)
            {
                GUILayout.Label("还没有记忆，干点活就会留下。", skin.Small);
                return;
            }

            foreach (var hit in memories)
            {
                GUILayout.BeginHorizontal();
                DrawBar(GUILayoutUtility.GetRect(48f, 8f), hit.Weight, HudSkin.Green);
                GUILayout.Label($"{hit.Weight:0.00}", skin.Small, GUILayout.Width(38f));
                GUILayout.Label(
                    $"第 {hit.Day} 天 · {FarmDemoText.Shorten(hit.Text, 14)}",
                    skin.Small,
                    GUILayout.Width(220f));
                GUILayout.EndHorizontal();
            }
        }

        void DrawIntent(string npcName, string intent)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("②", skin.BodyBold, GUILayout.Width(20f));
            GUILayout.Label($"{npcName}的当前意图：{intent}", skin.SmallWrap);
            GUILayout.EndHorizontal();
        }

        void DrawCandidates(IReadOnlyList<StepCandidate> candidates, IReadOnlyList<FieldClaim> claims)
        {
            GUILayout.Space(4f);
            GUILayout.Label($"③ 候选动作与否决原因（共 {candidates.Count} 条）", skin.BodyBold);
            GUILayout.Label(FieldClaimsLine(claims), skin.SmallWrap);

            var validShown = 0;
            var rejectedShown = 0;
            foreach (var candidate in candidates)
            {
                if (validShown + rejectedShown >= 3)
                {
                    break;
                }

                if (candidate.IsValid && validShown < 2)
                {
                    validShown++;
                    GUILayout.Label(
                        $"✓ {FarmDemoText.StepName(candidate.Step)} {candidate.FieldId}  {candidate.Score:0.00}",
                        skin.SmallWrap);
                }
                else if (!candidate.IsValid && rejectedShown < 2)
                {
                    rejectedShown++;
                    GUILayout.Label(
                        $"✕ {FarmDemoText.StepName(candidate.Step)} {candidate.FieldId} · {candidate.RejectionReason}",
                        skin.SmallWrap);
                }
            }
        }

        /// <summary>
        /// ③ 的一行注脚：每块地归谁。占位就是候选被否决的原因之一，所以它跟着快照里的
        /// 候选一起下发（<see cref="DecisionSnapshot.FieldClaims"/>），HUD 不另读领域状态，也不算第 6 项。
        /// </summary>
        static string FieldClaimsLine(IReadOnlyList<FieldClaim> claims)
        {
            const string Prefix = "地块归属：";
            var line = new StringBuilder(Prefix);
            foreach (var claim in claims)
            {
                if (line.Length > Prefix.Length)
                {
                    line.Append(" · ");
                }

                line.Append(claim.FieldId).Append(' ').Append(claim.IsClaimed ? claim.NpcName : "无人");
            }

            return line.ToString();
        }

        void DrawModelCall(ModelCallInfo call)
        {
            GUILayout.Space(4f);
            GUILayout.Label("④ 最近一次模型调用", skin.BodyBold);
            GUILayout.Label(FarmDemoText.Call(call), skin.SmallWrap);
        }

        void DrawStatus(NpcStatus status)
        {
            GUILayout.Space(4f);
            GUILayout.Label("⑤ 状态", skin.BodyBold);
            DrawStatusBar("体力", status.Stamina, HudSkin.Green);
            DrawStatusBar("心情", status.Mood, new Color(0.45f, 0.68f, 0.95f));
            DrawStatusBar("饥饿", status.Hunger, new Color(0.92f, 0.62f, 0.28f));
        }

        void DrawStatusBar(string name, float value, Color color)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, skin.Small, GUILayout.Width(32f));
            DrawBar(GUILayoutUtility.GetRect(150f, 8f), value, color);
            GUILayout.Label($"{value:P0}", skin.Small, GUILayout.Width(44f));
            GUILayout.EndHorizontal();
        }

        void DrawLog()
        {
            GUILayout.Space(8f);
            GUILayout.Label("流水", skin.BodyBold);

            // 种子库补货不是工步，却是账本流水的一部分：种子数涨了要能看见是怎么涨的。
            foreach (var line in FarmDemoText.RestockLines(demo.World.SeedRestocks, 2))
            {
                GUILayout.Label(line, skin.SmallWrap);
            }

            var log = demo.World.Log;
            if (log.Count == 0)
            {
                GUILayout.Label("还没有执行过工步。", skin.Small);
                return;
            }

            for (var i = Mathf.Max(0, log.Count - 3); i < log.Count; i++)
            {
                var entry = log[i];
                var yieldText = entry.Yield > 0 ? $" +{entry.Yield} 产出" : string.Empty;
                var actor = demo.World.GetNpc(entry.NpcId).DisplayName;
                GUILayout.Label(
                    $"第 {entry.Day} 天 · {actor} · {FarmDemoText.StepName(entry.Step)} {entry.FieldId}{yieldText}",
                    skin.SmallWrap);
            }
        }

        float DialogueTop()
        {
            return Screen.height - DialogueHeight;
        }

        void DrawBar(Rect rect, float value, Color color)
        {
            GUI.DrawTexture(
                rect,
                skin.White,
                ScaleMode.StretchToFill,
                true,
                0f,
                HudSkin.BarTrough,
                0f,
                0f);

            var fill = new Rect(
                rect.x + 1f,
                rect.y + 1f,
                Mathf.Max(0f, rect.width - 2f) * Mathf.Clamp01(value),
                Mathf.Max(0f, rect.height - 2f));
            GUI.DrawTexture(fill, skin.White, ScaleMode.StretchToFill, true, 0f, color, 0f, 0f);
        }

        static void DrawSprite(Rect rect, Sprite sprite)
        {
            var texture = sprite.texture;
            if (texture == null)
            {
                return;
            }

            var source = sprite.rect;
            GUI.DrawTextureWithTexCoords(
                rect,
                texture,
                new Rect(
                    source.x / texture.width,
                    source.y / texture.height,
                    source.width / texture.width,
                    source.height / texture.height),
                true);
        }
    }
}
