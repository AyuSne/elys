using ElysFarm.Core;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 体块演示场景的 HUD：只读 <see cref="FarmDemoBootstrap"/> 暴露的领域快照，
    /// 把「第几天、三本账、候选工步与否决理由」摊开给观察者看，并提供一句话输入框。
    /// </summary>
    [RequireComponent(typeof(FarmDemoBootstrap))]
    public sealed class FarmDemoHud : MonoBehaviour
    {
        FarmDemoBootstrap demo;
        Font font;
        GUIStyle titleStyle;
        GUIStyle labelStyle;
        GUIStyle smallStyle;
        GUIStyle boldStyle;
        GUIStyle inputStyle;
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

            if (font != null)
            {
                titleStyle.font = font;
                labelStyle.font = font;
                smallStyle.font = font;
                boldStyle.font = font;
                inputStyle.font = font;
            }
        }

        void DrawStatusPanel()
        {
            var clock = demo.World.Clock;
            var ledger = demo.World.Ledger;
            var npc = demo.Npc;

            GUILayout.BeginArea(new Rect(12f, 8f, 360f, 326f), GUI.skin.box);
            GUILayout.Label("Elys · 农场闭环", titleStyle);
            GUILayout.Label($"第 {clock.Day} 天 · 当天已过 {clock.DayProgress:P0}", labelStyle);
            DrawProgressBar(clock.DayProgress);
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            foreach (var speed in FarmClock.AllowedSpeeds)
            {
                var label = speed == clock.Speed ? $"[{speed:0}x]" : $"{speed:0}x";
                if (GUILayout.Button(label))
                {
                    clock.Speed = speed;
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(6f);

            var cropName = CropCatalog.Radish.DisplayName;
            GUILayout.Label($"种子库：{cropName} × {ledger.SeedsOf(CropCatalog.RadishId)}", labelStyle);
            GUILayout.Label(
                $"产出仓：{cropName} × {ledger.ProduceOf(CropCatalog.RadishId)}（随身 {ledger.HeldOf(demo.Npc.Id, CropCatalog.RadishId)}）",
                labelStyle);
            GUILayout.Label(
                $"流水：种子 +{ledger.SeedsAdded}/-{ledger.SeedsRemoved} · 产出 +{ledger.ProduceAdded}/-{ledger.ProduceRemoved} · 随身 +{ledger.HeldAdded}/-{ledger.HeldRemoved}",
                smallStyle);
            GUILayout.Label($"{npc.DisplayName}体力：{npc.Stamina:P0}", labelStyle);
            GUILayout.Space(6f);

            foreach (var field in demo.World.Fields)
            {
                GUILayout.Label(
                    field.IsSown
                        ? $"{field.Id} · {FarmDemoText.StateName(field.State)} · 生长 {field.Growth:P0} · 水 {field.Water:P0} · 草 {field.Weeds:P0}"
                        : $"{field.Id} · {FarmDemoText.StateName(field.State)}",
                    labelStyle);
            }

            GUILayout.Label($"账本自检：{(ledger.Verify(out var error) ? "入账 − 出账 = 持有 ✓" : error)}", smallStyle);
            GUILayout.EndArea();
        }

        void DrawDecisionPanel()
        {
            var panelWidth = 400f;
            GUILayout.BeginArea(new Rect(Screen.width - panelWidth - 12f, 8f, panelWidth, 326f), GUI.skin.box);
            var order = demo.World.WorkOrderOf(FarmDemoBootstrap.NpcId);
            GUILayout.Label($"{demo.Npc.DisplayName}的决策", titleStyle);
            GUILayout.Label(
                $"决策来源：{(order != null ? "玩家工单 + " : string.Empty)}本地效用打分 · 模型{(demo.UsingModel ? "在线" : "离线")}",
                smallStyle);
            GUILayout.Label($"最近调用：{FarmDemoText.Call(demo.LastCall)}", smallStyle);
            GUILayout.Label($"当前意图：{demo.CurrentIntent}", labelStyle);
            GUILayout.Label($"上一结果：{demo.LastMessage}", smallStyle);
            GUILayout.Space(6f);

            if (order != null)
            {
                GUILayout.Label(
                    $"工单：{order.FieldId} · {order.CompletedCount}/{order.Steps.Count} 步"
                    + (order.NextStep.HasValue
                        ? $" · 下一步 {FarmDemoText.StepName(order.NextStep.Value)}"
                        : string.Empty),
                    boldStyle);
            }

            GUILayout.Label("候选工步（分数 / 否决理由）", boldStyle);

            var candidates = demo.World.RankCandidates(FarmDemoBootstrap.NpcId);
            var shown = 0;
            foreach (var candidate in candidates)
            {
                if (shown++ >= 5)
                {
                    break;
                }

                var name = $"{FarmDemoText.StepName(candidate.Step)} {candidate.FieldId}";
                if (candidate.IsValid)
                {
                    GUILayout.Label($"{name}   {candidate.Score:0.00}", labelStyle);
                }
                else
                {
                    GUILayout.Label($"{name}   ✕ {candidate.RejectionReason}", smallStyle);
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
                var yieldText = entry.Yield > 0 ? $" +{entry.Yield} {CropCatalog.Radish.DisplayName}" : string.Empty;
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
            GUILayout.Label("玩家说：", boldStyle, GUILayout.Width(64f));
            commandInput = GUILayout.TextField(commandInput ?? string.Empty, 120, inputStyle, GUILayout.MinWidth(260f));

            GUI.enabled = !demo.IsThinking;
            if (GUILayout.Button("下达", GUILayout.Width(56f)))
            {
                Submit();
            }

            if (GUILayout.Button("种满萝卜", GUILayout.Width(90f)))
            {
                commandInput = "把东边那块地种满萝卜";
                Submit();
            }

            if (GUILayout.Button("去种月亮", GUILayout.Width(90f)))
            {
                commandInput = "去种月亮";
                Submit();
            }

            if (GUILayout.Button(demo.AutonomyEnabled ? "暂停自主" : "开始自主", GUILayout.Width(80f)))
            {
                demo.ToggleAutonomy();
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
                    ? "模型" + (demo.UsingModel ? "在线" : "离线") + " · NPC 自主中"
                    : "模型" + (demo.UsingModel ? "在线" : "离线") + " · NPC 待命";
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

        void DrawProgressBar(float progress)
        {
            var rect = GUILayoutUtility.GetRect(1f, 12f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none);

            var fill = new Rect(
                rect.x + 2f,
                rect.y + 2f,
                (rect.width - 4f) * Mathf.Clamp01(progress),
                rect.height - 4f);

            var previous = GUI.color;
            GUI.color = new Color(0.45f, 0.8f, 0.4f);
            GUI.DrawTexture(fill, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
