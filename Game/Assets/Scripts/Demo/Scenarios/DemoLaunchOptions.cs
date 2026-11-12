using System;
using System.Globalization;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 交付包的命令行开关。只认 <c>-elys*</c> 前缀，Unity 自己的
    /// <c>-screen-width</c> 等参数原样留给引擎。
    /// </summary>
    public sealed class DemoLaunchOptions
    {
        /// <summary>要回放的固定剧本；没有则为 null（正常游玩）。</summary>
        public FixedScenario? Scenario { get; private set; }

        /// <summary>证据文本写到哪；空则落到 persistentDataPath。</summary>
        public string TracePath { get; private set; }

        /// <summary>逐帧录制的目录；空则不录。</summary>
        public string RecordDirectory { get; private set; }

        /// <summary>录制帧率。</summary>
        public float RecordFps { get; private set; } = 20f;

        /// <summary>剧本跑完后等多少秒退出；0 表示不退出。</summary>
        public float QuitAfter { get; private set; }

        /// <summary>压测时长（秒）；大于 0 时进入压测模式。</summary>
        public float PerfSeconds { get; private set; }

        /// <summary>压测结果写到哪。</summary>
        public string PerfOutput { get; private set; }

        /// <summary>若命令行写了 -elysScenario 但认不出来，这里记下原值，启动器会报错而不是悄悄正常游玩。</summary>
        public string UnknownScenario { get; private set; }

        public static DemoLaunchOptions FromCommandLine(string[] args)
        {
            var options = new DemoLaunchOptions();
            if (args == null)
            {
                return options;
            }

            for (var i = 0; i < args.Length; i++)
            {
                var key = args[i] ?? string.Empty;
                if (!key.StartsWith("-elys", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = i + 1 < args.Length ? args[i + 1] : null;
                switch (key.ToLowerInvariant())
                {
                    case "-elysscenario":
                        if (ScenarioCatalog.TryParse(value, out var scenario))
                        {
                            options.Scenario = scenario;
                        }
                        else
                        {
                            options.UnknownScenario = value ?? string.Empty;
                        }

                        i++;
                        break;
                    case "-elystrace":
                        options.TracePath = value;
                        i++;
                        break;
                    case "-elysrecord":
                        options.RecordDirectory = value;
                        i++;
                        break;
                    case "-elysrecordfps":
                        options.RecordFps = ParseFloat(value, options.RecordFps);
                        i++;
                        break;
                    case "-elysquit":
                        options.QuitAfter = ParseFloat(value, 0f);
                        i++;
                        break;
                    case "-elysperf":
                        options.PerfSeconds = ParseFloat(value, 0f);
                        i++;
                        break;
                    case "-elysperfout":
                        options.PerfOutput = value;
                        i++;
                        break;
                }
            }

            options.RecordFps = Math.Max(1f, Math.Min(60f, options.RecordFps));
            return options;
        }

        static float ParseFloat(string value, float fallback)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : fallback;
        }
    }
}
