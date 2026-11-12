using System;
using System.Collections;
using System.IO;
using System.Text;
using ElysFarm.Core;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 把 <see cref="FixedScreenplays"/> 的同一份台本在画面上回放一遍（#10）。
    /// 它和领域跑共用台词、等待条件与 <see cref="ScenarioTrace"/>；区别只在于等待是
    /// 真的等画面走完，而不是瞬间推进游戏日。因此录屏里的第 N 天、库存与拒绝理由，
    /// 就是测试断言过的那一份。
    /// </summary>
    public sealed class ScenarioPlayer : MonoBehaviour
    {
        const float ApplyTimeoutSeconds = 15f;
        const float PlanTimeoutSeconds = 240f;
        const float OrderTimeoutSeconds = 420f;
        const float DayTimeoutSeconds = 180f;
        const float RestTimeoutSeconds = 60f;

        FarmDemoBootstrap demo;
        FarmDemoHud hud;
        FixedScenario scenario;
        string tracePath;
        string recordDirectory;
        float recordFps = 20f;
        float quitAfter;
        bool begun;

        ScenarioReport report;
        ScenarioTrace trace;
        ScenarioCommand currentCommand;
        WorkOrder currentOrder;
        FrameRecorder recorder;

        /// <summary>剧本是否已经跑完（不论成败）。</summary>
        public bool Completed { get; private set; }

        /// <summary>最近一次剧本的报告；开始的几帧里可能为 null。</summary>
        public ScenarioReport Report => report;

        public string TraceText => report != null ? report.Trace : string.Empty;

        public void Initialize(
            FarmDemoBootstrap bootstrap,
            FixedScenario fixedScenario,
            string evidencePath,
            string framesDirectory,
            float framesPerSecond,
            float secondsBeforeQuit)
        {
            demo = bootstrap;
            scenario = fixedScenario;
            tracePath = evidencePath;
            recordDirectory = framesDirectory;
            recordFps = framesPerSecond;
            quitAfter = secondsBeforeQuit;
            if (isActiveAndEnabled)
            {
                Begin();
            }
        }

        void Start()
        {
            Begin();
        }

        void Begin()
        {
            if (begun)
            {
                return;
            }

            begun = true;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            hud = GetComponent<FarmDemoHud>();
            demo.BeginScriptedDemo();
            report = new ScenarioReport
            {
                Scenario = scenario,
                Title = ScenarioCatalog.TitleOf(scenario)
            };
            trace = new ScenarioTrace();
            trace.Scenario(scenario, report.Title);

            if (!string.IsNullOrWhiteSpace(recordDirectory))
            {
                recorder = gameObject.AddComponent<FrameRecorder>();
                recorder.Initialize(recordDirectory, recordFps);
            }

            // 让场景先把第一帧画出来，录制与截图都不会抓到黑场。
            yield return new WaitForSecondsRealtime(0.4f);

            foreach (var beat in FixedScreenplays.Of(scenario))
            {
                if (report.Failure != null)
                {
                    break;
                }

                switch (beat.Kind)
                {
                    case ScenarioBeatKind.Caption:
                        demo.SetCaption(beat.Text, beat.Detail);
                        trace.Caption(beat.Text, beat.Detail);
                        demo.SimulationPaused = true;
                        yield return new WaitForSecondsRealtime(beat.Hold);
                        break;
                    case ScenarioBeatKind.Select:
                        demo.SelectNpc(beat.NpcId);
                        break;
                    case ScenarioBeatKind.Say:
                        yield return Say(beat);
                        break;
                    case ScenarioBeatKind.WaitPlan:
                        yield return WaitPlan(beat.Number);
                        break;
                    case ScenarioBeatKind.WaitOrderDone:
                        yield return WaitOrderDone();
                        break;
                    case ScenarioBeatKind.WaitDay:
                        yield return WaitDay(beat.Number);
                        break;
                    case ScenarioBeatKind.WaitRest:
                        yield return WaitRest();
                        break;
                    case ScenarioBeatKind.SetSpeed:
                        demo.World.Clock.Speed = beat.Speed;
                        break;
                    case ScenarioBeatKind.OpenInspector:
                        if (hud != null)
                        {
                            hud.SetInspectorOpen(beat.Flag);
                        }

                        break;
                    default:
                        report.Fail($"未知剧本拍：{beat.Kind}");
                        break;
                }
            }

            report.Final = FixedScenarios.Snapshot(demo.World, report.Commands);
            trace.Final(report.Final);
            report.Trace = trace.Text;
            var writtenTo = WriteTrace();

            demo.SetCaption(
                report.Failure == null ? "剧本完成 · 证据已落盘" : "剧本失败",
                report.Failure ?? ("证据：TestResults/scenarios/" + Path.GetFileName(writtenTo)));
            yield return new WaitForSecondsRealtime(report.Failure == null ? 3f : 6f);

            if (recorder != null)
            {
                recorder.StopRecording();
                var deadline = Time.realtimeSinceStartup + 60f;
                while (!recorder.Finished && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
            }

            Completed = true;
            if (quitAfter > 0f)
            {
                yield return new WaitForSecondsRealtime(quitAfter);
                Application.Quit();
            }
        }

        IEnumerator Say(ScenarioBeat beat)
        {
            demo.SimulationPaused = true;
            var npc = demo.SelectedNpc;
            var logStart = demo.World.Log.Count;
            demo.SubmitPlayerText(beat.Text);

            var deadline = Time.realtimeSinceStartup + ApplyTimeoutSeconds;
            while (demo.IsThinking && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (demo.LastResult == null || npc == null)
            {
                report.Fail($"「{beat.Text}」没有产生对话结果");
                yield break;
            }

            currentCommand = report.AddCommand(demo.LastResult, npc.Id, beat.Text);
            currentCommand.LogStart = logStart;
            trace.Command(currentCommand);
            currentOrder = demo.World.WorkOrderOf(npc.Id);
            yield return new WaitForSecondsRealtime(beat.Hold);
        }

        IEnumerator WaitPlan(int count)
        {
            if (currentCommand == null)
            {
                report.Fail("还没接单就开始等工步");
                yield break;
            }

            demo.SimulationPaused = false;
            var npcId = demo.SelectedNpc.Id;
            var order = currentOrder ?? demo.World.WorkOrderOf(npcId);
            var deadline = Time.realtimeSinceStartup + PlanTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                var live = demo.World.WorkOrderOf(npcId);
                if (live == null)
                {
                    break;
                }

                if (order != null && order.CompletedCount >= count)
                {
                    break;
                }

                yield return null;
            }

            demo.SimulationPaused = true;
            UpdatePerformed();
            trace.Steps(count, currentCommand.Performed);
            if (currentCommand.Performed.Count < count)
            {
                report.Fail($"「{currentCommand.Text}」只执行了 {currentCommand.Performed.Count}/{count} 个计划工步");
            }
        }

        IEnumerator WaitOrderDone()
        {
            if (currentCommand == null)
            {
                report.Fail("还没接单就开始等收工");
                yield break;
            }

            demo.SimulationPaused = false;
            var npcId = demo.SelectedNpc.Id;
            var deadline = Time.realtimeSinceStartup + OrderTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline && demo.World.WorkOrderOf(npcId) != null)
            {
                yield return null;
            }

            demo.SimulationPaused = true;
            UpdatePerformed();
            trace.Steps(currentCommand.PlannedSteps.Count, currentCommand.Performed);
            if (currentCommand.Performed.Count < currentCommand.PlannedSteps.Count)
            {
                report.Fail(
                    $"「{currentCommand.Text}」没跑完：{currentCommand.Performed.Count}/{currentCommand.PlannedSteps.Count}");
            }
        }

        IEnumerator WaitDay(int day)
        {
            demo.SimulationPaused = false;
            var deadline = Time.realtimeSinceStartup + DayTimeoutSeconds;
            while (demo.World.Clock.Day < day && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            demo.SimulationPaused = true;
            trace.Day(demo.World.Clock.Day);
            if (demo.World.Clock.Day < day)
            {
                report.Fail($"时间没推进到第 {day} 天（现在第 {demo.World.Clock.Day} 天）");
            }
        }

        IEnumerator WaitRest()
        {
            var npc = demo.SelectedNpc;
            demo.SimulationPaused = false;
            var deadline = Time.realtimeSinceStartup + RestTimeoutSeconds;
            while (!npc.IsResting && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            demo.SimulationPaused = true;
            trace.Resting(npc.Id);
            if (!npc.IsResting)
            {
                report.Fail($"{npc.DisplayName}没有进入休息（体力 {npc.Stamina:P0}）");
            }
        }

        void UpdatePerformed()
        {
            if (currentCommand == null)
            {
                return;
            }

            currentCommand.Performed = ScenarioLog.PerformedIn(
                demo.World,
                currentCommand.LogStart,
                currentCommand.PlannedSteps);
        }

        string WriteTrace()
        {
            var path = tracePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Path.Combine(
                    Application.persistentDataPath,
                    "scenarios",
                    ScenarioCatalog.IdOf(scenario) + ".trace");
            }

            path = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, report.Trace, new UTF8Encoding(false));
            var verdict = report.Failure == null ? "PASS" : "FAIL";
            Debug.Log($"[Elys][scenario] {ScenarioCatalog.IdOf(scenario)} {verdict} trace={path}");
            if (report.Failure != null)
            {
                Debug.LogError($"[Elys][scenario] {ScenarioCatalog.IdOf(scenario)}：{report.Failure}");
            }

            return path;
        }
    }
}
