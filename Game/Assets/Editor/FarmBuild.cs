using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ElysFarm.EditorTools
{
    /// <summary>
    /// Windows 交付包的构建入口（#4）。固定只打农场演示场景，构建前后都检查工程与产物里
    /// 有没有疑似密钥文件，避免把 .secrets.local.json 一类的东西带进包。
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

        /// <summary>
        /// 产物与工程里都不允许出现的密钥文件名。只按确切名字判断，避免误伤
        /// SecretFileLocator 这类源码文件；密钥永远不该以这些名字出现在 Assets 里。
        /// </summary>
        static bool LooksLikeSecret(string fileName)
        {
            var name = fileName.ToLowerInvariant();
            return name == ".env"
                || name == ".env.local"
                || name == ".secrets.local.json"
                || name == "secrets.local.json"
                || name == "secrets.json"
                || name.EndsWith(".env");
        }

        [MenuItem("Elys/构建 Windows 包")]
        public static void BuildWindows()
        {
            var outputPath = ResolveOutputPath();
            var secrets = FindSecretFiles(Application.dataPath);
            if (secrets.Count > 0)
            {
                throw new BuildFailedException(
                    "[Elys] Assets 里发现疑似密钥文件，拒绝打包：\n" + string.Join("\n", secrets));
            }

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

            var outputDirectory = Path.GetDirectoryName(outputPath) ?? string.Empty;
            var leaked = FindSecretFiles(outputDirectory);
            if (leaked.Count > 0)
            {
                throw new BuildFailedException(
                    "[Elys] 构建产物的目录里发现疑似密钥文件，拒绝交付：\n" + string.Join("\n", leaked));
            }

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

        static List<string> FindSecretFiles(string root)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return found;
            }

            foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                if (LooksLikeSecret(Path.GetFileName(path)))
                {
                    found.Add(path);
                }
            }

            return found;
        }
    }
}
