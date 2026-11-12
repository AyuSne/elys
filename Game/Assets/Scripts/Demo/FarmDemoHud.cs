using System.Collections.Generic;
using ElysFarm.Core;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 体块演示场景的 HUD：只读 <see cref="FarmDemoBootstrap"/> 暴露的领域快照，
    /// 把「第几天、三本账、三名 NPC 的体力与意图、候选工步的分项权重」摊开给观察者看，
    /// 并提供一句话输入框与指挥对象切换。它不是 #6 的决策可视化层。
    /// </summary>
    [RequireComponent(typeof(FarmDemoBootstrap))]
    public sealed class FarmDemoHud : MonoBehaviour
    {
        static readonly Color StaminaColor = new Color(0.45f, 0.8f, 0.4f);
        static readonly Color RestColor = new Color(0.9f, 0.65f, 0.25f);
        static readonly Color MoodColor = new Color(0.45f, 0.68f, 0.95f);
        static readonly Color HungerColor = new Color(0.92f, 0.62f, 0.28f);
        static readonly Color MemoryColor = new Color(0.62f, 0.78f, 0.95f);

        const float PanelMargin = 12f;
        const float PanelTop = 8f;
        const float LeftPanelWidth = 372f;
        const float DecisionPanelWidth = 400f;
        const float InspectorPanelWidth = 412f;
        const float StatusPanelHeight = 326f;

        FarmDemoBootstrap demo;
        Font font;
        GUIStyle titleStyle;
        GUIStyle labelStyle;
        GUIStyle smallStyle;
        GUIStyle boldStyle;
        GUIStyle inputStyle;
        GUIStyle buttonStyle;
        bool stylesReady;
        bool inspectorOpen;
        string commandInput = string.Empty;

        void Start()
        {
            demo = GetComponent<FarmDemoBootstrap>();
            font = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Arial" },
                15);
        }

        void OnGUI()
        {
            if (demo == null)
            {
                return;
            }

            if (font != null)
            {
                GUI.skin.font = font;
            }

            if (!stylesReady)
            {
                InitStyles();
                stylesReady = true;
            }

            HandleCameraKeys();
            DrawStatusPanel();
            DrawCameraBar();
            DrawDecisionPanel();
            DrawLogPanel();
            DrawCommandPanel();
        }

        /// <summary>
        /// 键盘兜底：窗口太窄、顶部镜头条放不下时，仍能切机位。
        /// 输入框获得焦点（keyboardControl ≠ 0）时不抢按键。
        /// </summary>
        void HandleCameraKeys()
        {
            var current = Event.current;
            if (current.type != EventType.KeyDown || GUIUtility.keyboardControl != 0)
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

        /// <summary>
        /// 镜头与素材开关。放在左右面板之间的顶部：俯视时两侧面板都开，
        /// 跟拍时右侧面板与流水收起，把画面让给 NPC。
        /// </summary>
        void DrawCameraBar()
        {
            var leftEdge = PanelMargin + LeftPanelWidth;
            var rightEdge = Screen.width - PanelMargin - InspectorPanelWidth;
            var gap = rightEdge - leftEdge;
            var width = Mathf.Min(520f, gap);
            if (width < 280f)
            {
                return;
            }

            var x = leftEdge + (gap - width) * 0.5f;
            GUILayout.BeginArea(new Rect(x, PanelTop, width, 76f), GUI.skin.box);
            GUILayout.Label(
                demo.IsOverheadCamera ? "镜头 · 俯视 45°" : $"镜头 · 跟拍{FollowedName()}",
                smallStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(demo.IsOverheadCamera ? "●俯视" : "俯视", buttonStyle, GUILayout.Width(58f)))
            {
                demo.UseOverheadCamera();
            }

            foreach (var npc in demo.World.Npcs)
            {
                var label = demo.IsFollowing(npc.Id) ? $"●{npc.DisplayName}" : npc.DisplayName;
                if (GUILayout.Button(label, buttonStyle, GUILayout.Width(58f)))
                {
                    Follow(npc.Id);
                }
            }

            if (GUILayout.Button(demo.UsingArt ? "素材开" : "素材关", buttonStyle, GUILayout.Width(58f)))
            {
                demo.ToggleArt();
            }

            GUILayout.EndHorizontal();
            GUILayout.Label("快捷键：C 俯视 · 1/2/3 跟拍", smallStyle);
            GUILayout.EndArea();
        }

        string FollowedName()
        {
            var followed = demo.FollowedNpcId;
            if (string.IsNullOrEmpty(followed))
            {
                return string.Empty;
            }

            foreach (var npc in demo.World.Npcs)
            {
                if (npc.Id == followed)
                {
                    return npc.DisplayName;
                }
            }

            return string.Empty;
        }

        void InitStyles()
        {
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.92f, 0.95f, 0.9f) }
            };

            smallStyle = new GUIStyle(labelStyle)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.73f, 0.79f, 0.73f) }
            };

            boldStyle = new GUIStyle(labelStyle)
            {
                fontStyle = FontStyle.Bold
            };

            inputStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 14,
                padding = new RectOffset(6, 6, 4, 4)
            };

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                padding = new RectOffset(6, 6, 2, 2)
            };

            if (font != null)
            {
                titleStyle.font = font;
                labelStyle.font = font;
                smallStyle.font = font;
                boldStyle.font = font;
                inputStyle.font = font;
                buttonStyle.font = font;
            }
        }

        void DrawStatusPanel()
        {
            var clock = demo.World.Clock;
            var ledger = demo.World.Ledger;
            var radish = CropCatalog.RadishId;
            var wheat = CropCatalog.WheatId;

            GUILayout.BeginArea(new Rect(PanelMargin, PanelTop, LeftPanelWidth, StatusPanelHeight), GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Elys · 三名 NPC", titleStyle);
            if (GUILayout.Button(inspectorOpen ? "关闭可视化" : "决策可视化", buttonStyle, GUILayout.Width(86f)))
            {
                inspectorOpen = !inspectorOpen;
            }

            GUILayout.EndHorizontal();
            GUILayout.Label(
                $"第 {clock.Day} 天 · 当天已过 {clock.DayProgress:P0} · 自主{(demo.AutonomyEnabled ? "开" : "关")}",
                labelStyle);
            DrawProgressBar(clock.DayProgress, StaminaColor);
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            foreach (var speed in FarmClock.AllowedSpeeds)
            {
                var label = speed == clock.Speed ? $"[{speed:0}x]" : $"{speed:0}x";
                if (GUILayout.Button(label, buttonStyle, GUILayout.Width(42f)))
                {
                    // 演示用的时间控制，不是自然语言输入；文本输入仍只经 IntentRouter。
                    clock.Speed = speed;
                }
            }

            if (GUILayout.Button(demo.AutonomyEnabled ? "暂停自主" : "开始自主", buttonStyle, GUILayout.Width(74f)))
            {
                demo.ToggleAutonomy();
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(4f);

            GUILayout.Label(
                $"种子 萝卜×{ledger.SeedsOf(radish)} 小麦×{ledger.SeedsOf(wheat)} · 产出 萝卜×{ledger.ProduceOf(radish)} 小麦×{ledger.ProduceOf(wheat)}",
                smallStyle);
            GUILayout.Space(4f);

            foreach (var npc in demo.World.Npcs)
            {
                DrawNpcRow(npc);
            }

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            foreach (var field in demo.World.Fields)
            {
                GUILayout.Label(FieldLine(field), smallStyle, GUILayout.Width(120f));
            }

            GUILayout.EndHorizontal();

            GUILayout.Label($"账本自检：{(ledger.Verify(out var error) ? "入账 − 出账 = 持有 ✓" : error)}", smallStyle);
            GUILayout.EndArea();
        }

        static string FieldLine(Field field)
        {
            if (!field.IsSown)
            {
                return $"{field.Id} {FarmDemoText.StateName(field.State)}";
            }

            var growth = field.State == FieldState.Mature ? "熟" : $"生{field.Growth * 100f:0}";
            return $"{field.Id} {growth} 水{field.Water * 100f:0} 草{field.Weeds * 100f:0}";
        }

        void DrawNpcRow(Npc npc)
        {
            var selected = npc == demo.SelectedNpc;
            var name = selected ? $"▶{npc.DisplayName}" : npc.DisplayName;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(name, buttonStyle, GUILayout.Width(58f)))
            {
                demo.SelectNpc(npc.Id);
            }

            GUILayout.Label($"{npc.Stamina:P0}", selected ? boldStyle : labelStyle, GUILayout.Width(38f));
            DrawBar(npc.Stamina, 64f, npc.IsResting ? RestColor : StaminaColor);

            var state = RowState(npc, selected);
            GUILayout.Label(state, smallStyle);
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// 左侧一行的状态文案。决策可视化打开时，选中行改用与面板相同的领域意图，
        /// 避免观察者在同一屏里看到两个不同的「当前意图」。
        /// </summary>
        string RowState(Npc npc, bool selected)
        {
            if (inspectorOpen && selected)
            {
                return FarmDemoText.Shorten(demo.World.IntentOf(npc.Id), 15);
            }

            return npc.IsResting
                ? $"休息 {npc.RestSecondsRemaining:0}s"
                : FarmDemoText.Shorten(demo.IntentOf(npc.Id), 15);
        }

        void DrawDecisionPanel()
        {
            if (!demo.IsOverheadCamera)
            {
                return;
            }

            if (inspectorOpen)
            {
                DrawInspectorPanel();
                return;
            }

            GUILayout.BeginArea(
                new Rect(
                    Screen.width - DecisionPanelWidth - PanelMargin,
                    PanelTop,
                    DecisionPanelWidth,
                    StatusPanelHeight),
                GUI.skin.box);

            var npc = demo.SelectedNpc;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{npc.DisplayName}的决策", titleStyle);
            foreach (var each in demo.World.Npcs)
            {
                if (each == npc)
                {
                    continue;
                }

                if (GUILayout.Button(each.DisplayName, buttonStyle, GUILayout.Width(46f)))
                {
                    demo.SelectNpc(each.Id);
                }
            }

            if (GUILayout.Button("决策可视化", buttonStyle, GUILayout.Width(86f)))
            {
                inspectorOpen = true;
            }

            GUILayout.EndHorizontal();

            GUILayout.Label(
                $"{FarmDemoText.PersonaNote(npc.Persona.Id)} · 决定间隔 {npc.Persona.DecisionSeconds:0.0}s · 每步体力 -{npc.Persona.ExertionPerStep:0.00}",
                smallStyle);
            GUILayout.Label($"当前意图：{demo.IntentOf(npc.Id)}", labelStyle);
            GUILayout.Label($"上一结果：{FarmDemoText.Shorten(demo.LastMessage, 34)}", smallStyle);
            GUILayout.Label(
                $"本地效用打分 · 模型{(demo.UsingModel ? "在线" : "离线")} · 最近调用：{FarmDemoText.Call(demo.LastCall)}",
                smallStyle);

            var order = demo.World.WorkOrderOf(npc.Id);
            if (order != null)
            {
                GUILayout.Label(
                    $"工单：{order.FieldId} · {order.CompletedCount}/{order.Steps.Count} 步"
                    + (order.NextStep.HasValue
                        ? $" · 下一步 {FarmDemoText.StepName(order.NextStep.Value)}"
                        : string.Empty),
                    boldStyle);
            }

            GUILayout.Label("候选工步：分数 = 需求 × 人设 × 体力 − 距离", boldStyle);

            var candidates = demo.World.RankCandidates(npc.Id);
            var shown = 0;
            foreach (var candidate in candidates)
            {
                if (shown++ >= 4)
                {
                    break;
                }

                var name = $"{FarmDemoText.StepName(candidate.Step)} {candidate.FieldId}";
                if (candidate.IsValid)
                {
                    GUILayout.Label(
                        $"{name}  {candidate.Score:0.00}  ({candidate.Need:0.00}×{candidate.PersonaWeight:0.00}×{candidate.StaminaFactor:0.00}−{candidate.DistancePenalty:0.00})",
                        smallStyle);
                }
                else
                {
                    GUILayout.Label($"{name}  ✕ {candidate.RejectionReason}", smallStyle);
                }
            }

            GUILayout.EndArea();
        }

        /// <summary>
        /// 决策可视化层（#6）：只呈现五项，数据全部来自 <see cref="FarmWorld.Inspect"/> 的只读快照。
        /// 关闭时这个分支不会执行；临时状态面板与流水照旧。
        /// </summary>
        void DrawInspectorPanel()
        {
            GUILayout.BeginArea(
                new Rect(
                    Screen.width - InspectorPanelWidth - PanelMargin,
                    PanelTop,
                    InspectorPanelWidth,
                    406f),
                GUI.skin.box);

            var snapshot = demo.World.Inspect(demo.SelectedNpc.Id, demo.LastCall);

            DrawMemories(snapshot.Memories);
            DrawIntent(snapshot.NpcName, snapshot.Intent);
            DrawCandidates(snapshot.Candidates);
            DrawModelCall(snapshot.ModelCall);
            DrawStatus(snapshot.Status);

            GUILayout.EndArea();
        }

        void DrawMemories(IReadOnlyList<MemoryHit> memories)
        {
            GUILayout.Label("① 检索命中的记忆", boldStyle);
            if (memories.Count == 0)
            {
                GUILayout.Label("还没有记忆，干点活就会留下。", smallStyle);
                return;
            }

            foreach (var hit in memories)
            {
                GUILayout.BeginHorizontal();
                DrawBar(hit.Weight, 52f, MemoryColor);
                GUILayout.Label($"{hit.Weight:0.00}", smallStyle, GUILayout.Width(34f));
                GUILayout.Label($"第 {hit.Day} 天 · {FarmDemoText.Shorten(hit.Text, 20)}", smallStyle);
                GUILayout.EndHorizontal();
            }
        }

        void DrawIntent(string npcName, string intent)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("②", boldStyle, GUILayout.Width(18f));
            GUILayout.Label($"{npcName}的当前意图：{intent}", labelStyle);
            GUILayout.EndHorizontal();
        }

        void DrawCandidates(IReadOnlyList<StepCandidate> candidates)
        {
            GUILayout.Space(2f);
            GUILayout.Label($"③ 候选动作与否决原因（共 {candidates.Count} 条，最多展示 3 条）", boldStyle);

            var validShown = 0;
            var rejectedShown = 0;

            // 临时面板按排名展示前四名（含分项）；这里只挑少量有效与否决候选做对照，
            // 两处的展示目的不同，不共用这段筛选。
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
                        smallStyle);
                }
                else if (!candidate.IsValid && rejectedShown < 2)
                {
                    rejectedShown++;
                    GUILayout.Label(
                        $"✕ {FarmDemoText.StepName(candidate.Step)} {candidate.FieldId} · {candidate.RejectionReason}",
                        smallStyle);
                }
            }
        }

        void DrawModelCall(ModelCallInfo call)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("④", boldStyle, GUILayout.Width(18f));
            GUILayout.Label($"最近一次模型调用：{FarmDemoText.Call(call)}", smallStyle);
            GUILayout.EndHorizontal();
        }

        void DrawStatus(NpcStatus status)
        {
            GUILayout.Space(2f);
            GUILayout.Label("⑤ 状态", boldStyle);
            DrawStatusBar("体力", status.Stamina, StaminaColor);
            DrawStatusBar("心情", status.Mood, MoodColor);
            DrawStatusBar("饥饿", status.Hunger, HungerColor);
        }

        void DrawStatusBar(string name, float value, Color color)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, smallStyle, GUILayout.Width(30f));
            DrawBar(value, 150f, color);
            GUILayout.Label($"{value:P0}", smallStyle, GUILayout.Width(40f));
            GUILayout.EndHorizontal();
        }

        void DrawLogPanel()
        {
            if (inspectorOpen || !demo.IsOverheadCamera)
            {
                return;
            }

            GUILayout.BeginArea(
                new Rect(PanelMargin, Screen.height - 208f, Screen.width - PanelMargin * 2f, 76f),
                GUI.skin.box);
            GUILayout.Label("已执行的工步（流水）", boldStyle);

            var log = demo.World.Log;
            var first = Mathf.Max(0, log.Count - 2);
            if (first == log.Count)
            {
                GUILayout.Label("还没有执行过工步。", smallStyle);
            }

            for (var i = first; i < log.Count; i++)
            {
                var entry = log[i];
                var yieldText = entry.Yield > 0 ? $" +{entry.Yield} 产出" : string.Empty;
                var actor = demo.World.GetNpc(entry.NpcId).DisplayName;
                GUILayout.Label(
                    $"第 {entry.Day} 天 · {actor} · {FarmDemoText.StepName(entry.Step)} {entry.FieldId}{yieldText}",
                    labelStyle);
            }

            GUILayout.EndArea();
        }

        void DrawCommandPanel()
        {
            GUILayout.BeginArea(
                new Rect(PanelMargin, Screen.height - 128f, Screen.width - PanelMargin * 2f, 120f),
                GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"玩家说（对 {demo.SelectedNpc.DisplayName}）：", boldStyle, GUILayout.Width(140f));
            commandInput = GUILayout.TextField(commandInput ?? string.Empty, 120, inputStyle, GUILayout.MinWidth(240f));

            GUI.enabled = !demo.IsThinking;
            if (GUILayout.Button("下达", buttonStyle, GUILayout.Width(52f)))
            {
                Submit();
            }

            if (GUILayout.Button("种满萝卜", buttonStyle, GUILayout.Width(82f)))
            {
                commandInput = "把东边那块地种满萝卜";
                Submit();
            }

            if (GUILayout.Button("去种月亮", buttonStyle, GUILayout.Width(82f)))
            {
                commandInput = "去种月亮";
                Submit();
            }

            if (GUILayout.Button("听说阿芽偷懒", buttonStyle, GUILayout.Width(104f)))
            {
                commandInput = "听说阿芽今天偷懒了";
                Submit();
            }

            if (GUILayout.Button("你在干什么", buttonStyle, GUILayout.Width(82f)))
            {
                commandInput = "你在干什么";
                Submit();
            }

            if (GUILayout.Button("怎么不去干活", buttonStyle, GUILayout.Width(96f)))
            {
                commandInput = "你怎么不去干活";
                Submit();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                && !demo.IsThinking
                && !string.IsNullOrWhiteSpace(commandInput))
            {
                Submit();
                Event.current.Use();
            }

            var status = demo.IsThinking
                ? "等模型回话…（游戏没有暂停）"
                : demo.AutonomyEnabled
                    ? "模型" + (demo.UsingModel ? "在线" : "离线") + " · 三人自主中，点名字可换观察对象"
                    : "模型" + (demo.UsingModel ? "在线" : "离线") + " · 三人待命";
            GUILayout.Label($"上一句：{demo.LastPlayerText}　（{status}）", smallStyle);
            GUILayout.Label($"NPC：{demo.LastReply}", labelStyle);
            GUILayout.Label(demo.LastUnderstood, smallStyle);
            GUILayout.EndArea();
        }

        void Submit()
        {
            if (demo.IsThinking || string.IsNullOrWhiteSpace(commandInput))
            {
                return;
            }

            demo.SubmitPlayerText(commandInput);
            commandInput = string.Empty;
        }

        void DrawProgressBar(float progress, Color color)
        {
            DrawBarRect(GUILayoutUtility.GetRect(1f, 12f, GUILayout.ExpandWidth(true)), progress, color, 2f);
        }

        void DrawBar(float value, float width, Color color)
        {
            DrawBarRect(GUILayoutUtility.GetRect(width, 9f), value, color, 1f);
        }

        static void DrawBarRect(Rect rect, float value, Color color, float inset)
        {
            GUI.Box(rect, GUIContent.none);

            var fill = new Rect(
                rect.x + inset,
                rect.y + inset,
                Mathf.Max(0f, rect.width - inset * 2f) * Mathf.Clamp01(value),
                Mathf.Max(0f, rect.height - inset * 2f));

            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(fill, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
