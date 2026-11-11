using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.Build;

namespace ElysFarm.EditorTools
{
    /// <summary>
    /// 构建期的密钥防线：Assets 与产物目录里都不允许出现密钥类文件。
    /// 只按确切文件名或 .env 后缀判断，避免把 SecretFileLocator 这类源码误判成密钥。
    /// </summary>
    public static class BuildSecrets
    {
        static readonly string[] ExactNames =
        {
            ".env",
            ".env.local",
            ".secrets.local.json",
            "secrets.local.json",
            "secrets.json"
        };

        /// <summary>文件名是否像密钥文件。</summary>
        public static bool LooksLikeSecret(string fileName)
        {
            var name = fileName.ToLowerInvariant();
            foreach (var exact in ExactNames)
            {
                if (name == exact)
                {
                    return true;
                }
            }

            return name.EndsWith(".env");
        }

        /// <summary>目录里发现密钥类文件就抛 <see cref="BuildFailedException"/>。</summary>
        public static void EnsureNone(string root, string description)
        {
            var found = Find(root);
            if (found.Count == 0)
            {
                return;
            }

            throw new BuildFailedException(
                $"[Elys] {description}里发现疑似密钥文件，拒绝打包：\n" + string.Join("\n", found));
        }

        static List<string> Find(string root)
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
