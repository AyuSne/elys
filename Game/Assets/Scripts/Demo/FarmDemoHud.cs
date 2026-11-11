using ElysFarm.Core;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 体块演示场景的 HUD：只读 <see cref="FarmDemoBootstrap"/> 暴露的领域快照，
    /// 把「第几天、三本账、候选工步与否决理由」摊开给观察者看。
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
        bool stylesReady;

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

            if (font != null)
            {
                titleStyle.font = font;
                labelStyle.font = font;
                smallStyle.font = font;
                boldStyle.font = font;
            }
        }

        void DrawStatusPanel()
        {
            var clock = demo.World.Clock;
            var ledger = demo.World.Ledger;
            var field = demo.Field;
            var npc = demo.Npc;

            GUILayout.BeginArea(new Rect(16f, 16f, 350f, 340f), GUI.skin.box);
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

            GUILayout.Label($"种子库：萝卜 × {ledger.SeedsOf(CropCatalog.RadishId)}", labelStyle);
            GUILayout.Label(
                $"产出仓：萝卜 × {ledger.ProduceOf(CropCatalog.RadishId)}（随身 {ledger.HeldOf(CropCatalog.RadishId)}）",
                labelStyle);
            GUILayout.Label($"{npc.DisplayName}体力：{npc.Stamina:P0}", labelStyle);
            GUILayout.Space(6f);

            GUILayout.Label($"地块 {field.Id}：{FarmDemoText.StateName(field.State)}", boldStyle);
            GUILayout.Label(
                $"生长 {field.Growth:P0} · 水分 {field.Water:P0} · 肥力 {field.Fertility:P0} · 杂草 {field.Weeds:P0}",
                labelStyle);
            GUILayout.Label($"账本自检：{(ledger.Verify(out var error) ? "入账 − 出账 = 持有 ✓" : error)}", smallStyle);
            GUILayout.EndArea();
        }

        void DrawDecisionPanel()
        {
            var panelWidth = 400f;
            GUILayout.BeginArea(new Rect(Screen.width - panelWidth - 16f, 16f, panelWidth, 340f), GUI.skin.box);
            GUILayout.Label($"{demo.Npc.DisplayName}的决策", titleStyle);
            GUILayout.Label("决策来源：本地效用打分 · 模型调用 0 次", smallStyle);
            GUILayout.Label($"当前意图：{demo.CurrentIntent}", labelStyle);
            GUILayout.Label($"上一结果：{demo.LastMessage}", smallStyle);
            GUILayout.Space(6f);
            GUILayout.Label("候选工步（分数 / 否决理由）", boldStyle);

            var candidates = demo.World.RankCandidates(FarmDemoBootstrap.NpcId);
            var shown = 0;
            foreach (var candidate in candidates)
            {
                if (shown++ >= 6)
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
                new Rect(16f, Screen.height - 158f, Screen.width - 32f, 142f),
                GUI.skin.box);
            GUILayout.Label("已执行的工步（流水）", boldStyle);

            var log = demo.World.Log;
            var first = Mathf.Max(0, log.Count - 6);
            if (first == log.Count)
            {
                GUILayout.Label("还没有执行过工步。", smallStyle);
            }

            for (var i = first; i < log.Count; i++)
            {
                var entry = log[i];
                var yieldText = entry.Yield > 0 ? $" +{entry.Yield} 萝卜" : string.Empty;
                GUILayout.Label(
                    $"第 {entry.Day} 天 · {demo.Npc.DisplayName} · {FarmDemoText.StepName(entry.Step)} {entry.FieldId}{yieldText}",
                    labelStyle);
            }

            GUILayout.EndArea();
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
