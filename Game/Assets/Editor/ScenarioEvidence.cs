using System;
using System.IO;
using System.Text;
using ElysFarm.Demo;
using UnityEditor;
using UnityEngine;

namespace ElysFarm.EditorTools
{
    /// <summary>
    /// 把三条固定剧本的领域跑证据写到 TestResults/scenarios/reference/。
    /// scripts/run-scenarios.ps1 用这份参考去逐字比对交付包回放出来的 trace（#10）。
    /// </summary>
    public static class ScenarioEvidence
    {
        /// <summary>仓库根的 TestResults/scenarios/reference。</summary>
        public static string ReferenceDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "TestResults", "scenarios", "reference"));

        [MenuItem("Elys/生成固定剧本证据")]
        public static void Generate()
        {
            var directory = ReferenceDirectory;
            Directory.CreateDirectory(directory);

            // 离线路由返回已完成的 Task，这里不会死等；三条剧本合计不到一秒。
            var reports = FixedScenarios.RunAllAsync().GetAwaiter().GetResult();
            foreach (var report in reports)
            {
                var name = ScenarioCatalog.IdOf(report.Scenario) + ".trace";
                File.WriteAllText(
                    Path.Combine(directory, name),
                    report.Trace,
                    new UTF8Encoding(false));
                if (report.Failure != null)
                {
                    Debug.LogError($"[Elys][scenario] {name} 没跑通：{report.Failure}");
                }
                else
                {
                    Debug.Log($"[Elys][scenario] {name} 已生成（{report.Trace.Length} 字符）");
                }
            }
        }
    }
}
