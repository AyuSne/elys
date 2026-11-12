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

        FarmDemoBootstrap demo;
        Font font;
        GUIStyle titleStyle;
        GUIStyle labelStyle;
        GUIStyle smallStyle;
        GUIStyle boldStyle;
        GUIStyle inputStyle;
        GUIStyle buttonStyle;
        bool stylesReady;
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

            DrawStatusPanel();
            DrawDecisionPanel();
            DrawLogPanel();
            DrawCommandPanel();
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

            GUILayout.BeginArea(new Rect(12f, 8f, 372f, 326f), GUI.skin.box);
            GUILayout.Label("Elys · 三名 NPC", titleStyle);
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

            foreach (var npc in demo.Npcs)
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

            var state = npc.IsResting
                ? $"休息 {npc.RestSecondsRemaining:0}s"
                : FarmDemoText.Short(demo.IntentOf(npc.Id), 15);
            GUILayout.Label(state, smallStyle);
            GUILayout.EndHorizontal();
        }

        void DrawDecisionPanel()
        {
            const float panelWidth = 400f;
            GUILayout.BeginArea(new Rect(Screen.width - panelWidth - 12f, 8f, panelWidth, 326f), GUI.skin.box);

            var npc = demo.SelectedNpc;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{npc.DisplayName}的决策", titleStyle);
            foreach (var each in demo.Npcs)
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

            GUILayout.EndHorizontal();

            GUILayout.Label(
                $"{FarmDemoText.PersonaNote(npc.Persona.Id)} · 决定间隔 {npc.Persona.DecisionSeconds:0.0}s · 每步体力 -{npc.Persona.StaminaPerStep:0.00}",
                smallStyle);
            GUILayout.Label($"当前意图：{demo.IntentOf(npc.Id)}", labelStyle);
            GUILayout.Label($"上一结果：{FarmDemoText.Short(demo.LastMessage, 34)}", smallStyle);
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

        void DrawLogPanel()
        {
            GUILayout.BeginArea(
                new Rect(12f, Screen.height - 208f, Screen.width - 24f, 76f),
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
            GUILayout.BeginArea(new Rect(12f, Screen.height - 128f, Screen.width - 24f, 120f), GUI.skin.box);
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
            var rect = GUILayoutUtility.GetRect(1f, 12f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none);

            var fill = new Rect(
                rect.x + 2f,
                rect.y + 2f,
                (rect.width - 4f) * Mathf.Clamp01(progress),
                rect.height - 4f);

            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(fill, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        void DrawBar(float value, float width, Color color)
        {
            var rect = GUILayoutUtility.GetRect(width, 9f);
            GUI.Box(rect, GUIContent.none);

            var fill = new Rect(
                rect.x + 1f,
                rect.y + 1f,
                (rect.width - 2f) * Mathf.Clamp01(value),
                rect.height - 2f);

            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(fill, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
