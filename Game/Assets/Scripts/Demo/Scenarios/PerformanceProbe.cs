using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 压测探针（#10）：三名 NPC 自主、16x、决策抽屉常开，采一段时间的未缩放帧间隔，
    /// 输出平均 / p50 / p95 / p99 / 最低帧率与低于 30 / 60fps 的帧数。
    /// 关掉垂直同步再测，否则 60Hz 显示器会把所有机器的成绩都压成 60。
    /// </summary>
    public sealed class PerformanceProbe : MonoBehaviour
    {
        const float WarmupSeconds = 5f;

        [Serializable]
        public sealed class Stats
        {
            public string scenario;
            public string startedAt;
            public int width;
            public int height;
            public float warmupSeconds;
            public float sampleSeconds;
            public int frames;
            public float fpsAvg;
            public float fpsP50;
            public float fpsP95;
            public float fpsP99;
            public float fpsMin;
            public float fpsMax;
            public float frameMsAvg;
            public float frameMsP99;
            public int below30;
            public int below60;
            public int vSync;
            public int targetFrameRate;
            public int processorCount;
            public string graphicsDevice;
            public int graphicsMemoryMB;
            public int systemMemoryMB;
            public string unityVersion;
            public string operatingSystem;
            public bool art;
            public int npcCount;
            public bool inspectorOpen;
            public float clockSpeed;
            public bool autonomy;
            public long managedStartMB;
            public long managedEndMB;
            public long allocatedStartMB;
            public long allocatedEndMB;
        }

        FarmDemoBootstrap demo;
        float seconds = 30f;
        string outputPath;
        bool begun;
        int originalVSync;
        int originalTargetFrameRate;

        public bool Completed { get; private set; }

        public Stats Result { get; private set; }

        public void Initialize(FarmDemoBootstrap bootstrap, float sampleSeconds, string resultPath)
        {
            demo = bootstrap;
            seconds = Mathf.Max(5f, sampleSeconds);
            outputPath = resultPath;
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
            originalVSync = QualitySettings.vSyncCount;
            originalTargetFrameRate = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            demo.BeginStressDemo();
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var managedStart = MB(GC.GetTotalMemory(false));
            var allocatedStart = MB(Profiler.GetTotalAllocatedMemoryLong());
            var startedAt = DateTime.Now;

            yield return new WaitForSecondsRealtime(WarmupSeconds);

            var frameTimes = new List<float>(8192);
            var deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                var dt = Time.unscaledDeltaTime;
                if (dt > 0f && dt < 1f)
                {
                    frameTimes.Add(dt);
                }
            }

            var allocatedEnd = MB(Profiler.GetTotalAllocatedMemoryLong());
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var managedEnd = MB(GC.GetTotalMemory(false));

            Result = Summarize(frameTimes, startedAt, managedStart, managedEnd, allocatedStart, allocatedEnd);
            var path = Write(Result);
            Debug.Log($"[Elys][perf] {Result.width}x{Result.height} "
                + $"avg {Result.fpsAvg:0.0} fps · p99 {Result.frameMsP99:0.0} ms · "
                + $"min {Result.fpsMin:0.0} fps · below30 {Result.below30} · {path}");
            Debug.Log($"[Elys][perf-json] {JsonUtility.ToJson(Result)}");

            QualitySettings.vSyncCount = originalVSync;
            Application.targetFrameRate = originalTargetFrameRate;
            Completed = true;

            yield return new WaitForSecondsRealtime(0.5f);
            if (!Application.isEditor)
            {
                Application.Quit();
            }
        }

        Stats Summarize(
            List<float> frameTimes,
            DateTime startedAt,
            long managedStart,
            long managedEnd,
            long allocatedStart,
            long allocatedEnd)
        {
            var stats = new Stats
            {
                scenario = "stress-autonomy-16x-inspector",
                startedAt = startedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                width = Screen.width,
                height = Screen.height,
                warmupSeconds = WarmupSeconds,
                sampleSeconds = seconds,
                vSync = originalVSync,
                targetFrameRate = originalTargetFrameRate,
                processorCount = SystemInfo.processorCount,
                graphicsDevice = SystemInfo.graphicsDeviceName,
                graphicsMemoryMB = SystemInfo.graphicsMemorySize,
                systemMemoryMB = SystemInfo.systemMemorySize,
                unityVersion = Application.unityVersion,
                operatingSystem = SystemInfo.operatingSystem,
                art = PixelArt.UseArt,
                npcCount = demo.World.Npcs.Count,
                inspectorOpen = GetComponent<FarmDemoHud>() != null && GetComponent<FarmDemoHud>().InspectorOpen,
                clockSpeed = demo.World.Clock.Speed,
                autonomy = demo.AutonomyEnabled,
                managedStartMB = managedStart,
                managedEndMB = managedEnd,
                allocatedStartMB = allocatedStart,
                allocatedEndMB = allocatedEnd
            };

            if (frameTimes.Count == 0)
            {
                return stats;
            }

            frameTimes.Sort();
            var total = 0f;
            foreach (var dt in frameTimes)
            {
                total += dt;
            }

            stats.frames = frameTimes.Count;
            stats.fpsAvg = frameTimes.Count / total;
            stats.fpsP50 = FpsAt(frameTimes, 0.50f);
            stats.fpsP95 = FpsAt(frameTimes, 0.95f);
            stats.fpsP99 = FpsAt(frameTimes, 0.99f);
            stats.fpsMin = 1f / frameTimes[frameTimes.Count - 1];
            stats.fpsMax = 1f / frameTimes[0];
            stats.frameMsAvg = total / frameTimes.Count * 1000f;
            stats.frameMsP99 = frameTimes[PercentileIndex(frameTimes.Count, 0.99f)] * 1000f;
            stats.below30 = CountOver(frameTimes, 1f / 30f);
            stats.below60 = CountOver(frameTimes, 1f / 60f);
            return stats;
        }

        static float FpsAt(List<float> sortedFrameTimes, float percentile)
        {
            var dt = sortedFrameTimes[PercentileIndex(sortedFrameTimes.Count, percentile)];
            return 1f / dt;
        }

        static int PercentileIndex(int count, float percentile)
        {
            var index = Mathf.CeilToInt(count * percentile) - 1;
            return Mathf.Clamp(index, 0, count - 1);
        }

        static int CountOver(List<float> frameTimes, float thresholdSeconds)
        {
            var count = 0;
            foreach (var dt in frameTimes)
            {
                if (dt > thresholdSeconds)
                {
                    count++;
                }
            }

            return count;
        }

        static long MB(long bytes)
        {
            return bytes / 1024L / 1024L;
        }

        string Write(Stats stats)
        {
            var path = outputPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Path.Combine(
                    Application.persistentDataPath,
                    "perf",
                    "perf-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json");
            }

            path = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonUtility.ToJson(stats, true), new UTF8Encoding(false));
            return path;
        }
    }
}
