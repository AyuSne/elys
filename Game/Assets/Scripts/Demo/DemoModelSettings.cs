using System;
using System.IO;
using ElysFarm.Core;
using ElysFarm.Model;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 演示场景的模型配置读取。密钥来源只有两处：
    /// 环境变量，或编辑器里代码仓根目录下被 .gitignore 排除的 .secrets.local.json。
    /// 打包后的播放器不隐式读取文件，只认环境变量（或显式指定的 ELYS_SECRETS_FILE），
    /// 因此包旁边散落的密钥不会被误用。两处都没有时返回 null，演示自动切到本地模板。
    /// </summary>
    internal static class DemoModelSettings
    {
        public static IModelClient CreateClient()
        {
            try
            {
                var settings = Load();
                if (settings == null || !settings.HasChatKey)
                {
                    Debug.Log("[Elys] 未配置 DeepSeek 密钥，意图解析走本地模板。");
                    return null;
                }

                Debug.Log($"[Elys] 模型已配置：{settings.Model} @ {settings.BaseUrl}（密钥不落日志）。");
                return new DeepSeekModelClient(settings);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Elys] 模型配置读取失败，改用本地模板：{ex.Message}");
                return null;
            }
        }

        static ModelEndpointSettings Load()
        {
            var settings = new ModelEndpointSettings();
            var secrets = ReadSecretsFile();
            if (secrets != null)
            {
                settings.BaseUrl = FirstNonEmpty((string)secrets["deepseek"]?["baseUrl"], settings.BaseUrl);
                settings.Model = FirstNonEmpty((string)secrets["deepseek"]?["model"], settings.Model);
                settings.ApiKey = (string)secrets["deepseek"]?["apiKey"];
            }

            settings.ApiKey = FirstNonEmpty(Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY"), settings.ApiKey);
            settings.BaseUrl = FirstNonEmpty(Environment.GetEnvironmentVariable("DEEPSEEK_BASE_URL"), settings.BaseUrl);
            settings.Model = FirstNonEmpty(Environment.GetEnvironmentVariable("DEEPSEEK_MODEL"), settings.Model);

            return settings;
        }

        static JObject ReadSecretsFile()
        {
            var candidates = SecretFileLocator.Candidates(
                Application.isEditor,
                Application.dataPath,
                Environment.GetEnvironmentVariable("ELYS_SECRETS_FILE"));

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    return JObject.Parse(File.ReadAllText(path));
                }
            }

            return null;
        }

        static string FirstNonEmpty(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
