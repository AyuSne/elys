using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ElysFarm.EditorTools
{
    /// <summary>
    /// Windows 交付包的构建入口（#4）。固定只打农场演示场景，构建前后由
    /// <see cref="BuildSecrets"/> 检查工程与产物里有没有疑似密钥文件。
    ///
    /// 命令行（由 scripts/build-windows.ps1 调用）：
    /// unity build Game --target StandaloneWindows64
    ///   --execute-method ElysFarm.EditorTools.FarmBuild.BuildWindows
    ///   --output-path D:\Elys\Builds\Windows\Elys.exe
    /// </summary>
    public static class FarmBuild
    {
        /// <summary>交付包启动的唯一场景。</summary>
        public const string DemoScenePath = "Assets/Scenes/FarmDemo.unity";

        [MenuItem("Elys/构建 Windows 包")]
        public static void BuildWindows()
        {
            var outputPath = ResolveOutputPath();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoScenePath) == null)
            {
                throw new BuildFailedException($"[Elys] 找不到演示场景：{DemoScenePath}");
            }

            BuildSecrets.EnsureNone(Application.dataPath, "Assets");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { DemoScenePath },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });

            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    $"[Elys] Windows 构建失败：{summary.result}，错误 {summary.totalErrors} 条。"
                    + "日志在 Game/Logs/ 下。");
            }

            BuildSecrets.EnsureNone(Path.GetDirectoryName(outputPath), "构建产物");

            Debug.Log(
                $"[Elys] Windows 构建成功：{summary.outputPath}"
                + $"（{summary.totalSize / 1024f / 1024f:0.0} MB，用时 {summary.totalTime.TotalSeconds:0} 秒）");
        }

        /// <summary>产物路径：命令行 -buildOutput 优先，否则落到代码仓的 Builds/Windows/Elys.exe。</summary>
        public static string ResolveOutputPath()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], "-buildOutput", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFullPath(args[i + 1]);
                }
            }

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var repoRoot = Path.GetFullPath(Path.Combine(projectRoot, ".."));
            return Path.Combine(repoRoot, "Builds", "Windows", "Elys.exe");
        }
    }
}
