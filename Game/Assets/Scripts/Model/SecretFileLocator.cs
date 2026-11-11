using System;
using System.Collections.Generic;
using System.IO;

namespace ElysFarm.Model
{
    /// <summary>
    /// 密钥文件的候选路径，按优先级排列。编辑器里允许读仓库根下被 .gitignore 排除的
    /// <c>.secrets.local.json</c>；打包后的播放器不猜任何位置，只接受显式指定的文件，
    /// 因此密钥不会顺着构建产物被带上。
    /// </summary>
    public static class SecretFileLocator
    {
        /// <summary>仓库根的本地密钥文件名。</summary>
        public const string FileName = ".secrets.local.json";

        /// <summary>
        /// 计算玩家真正会读取的密钥文件。环境变量里的密钥由调用方单独读取，不在这里。
        /// </summary>
        /// <param name="isEditor">是否运行在编辑器里。</param>
        /// <param name="dataPath">Unity 的 <c>Application.dataPath</c>。</param>
        /// <param name="explicitPath">显式指定的密钥文件；非空时它就是唯一来源。</param>
        public static IReadOnlyList<string> Candidates(bool isEditor, string dataPath, string explicitPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                return new[] { Path.GetFullPath(explicitPath) };
            }

            if (!isEditor || string.IsNullOrWhiteSpace(dataPath))
            {
                return Array.Empty<string>();
            }

            var repoRoot = Path.GetFullPath(Path.Combine(dataPath, "..", ".."));
            return new[] { Path.Combine(repoRoot, FileName) };
        }
    }
}
