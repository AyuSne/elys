using System;
using System.IO;

namespace ElysFarm.Model
{
    /// <summary>
    /// 密钥文件的唯一来源决策。编辑器里读仓库根下被 .gitignore 排除的
    /// <c>.secrets.local.json</c>；打包后的播放器不猜任何位置，只接受显式指定的文件，
    /// 因此密钥不会顺着构建产物被带上。环境变量里的密钥由调用方单独读取，不在这里。
    /// </summary>
    public static class SecretFileLocator
    {
        /// <summary>仓库根的本地密钥文件名。</summary>
        public const string FileName = ".secrets.local.json";

        /// <summary>
        /// 返回应当读取的密钥文件；没有可读来源时返回 null。
        /// </summary>
        /// <param name="isEditor">是否运行在编辑器里。</param>
        /// <param name="dataPath">Unity 的 <c>Application.dataPath</c>。</param>
        /// <param name="explicitPath">显式指定的密钥文件；非空时它就是唯一来源。</param>
        public static string Resolve(bool isEditor, string dataPath, string explicitPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                return Path.GetFullPath(explicitPath);
            }

            if (!isEditor || string.IsNullOrWhiteSpace(dataPath))
            {
                return null;
            }

            var repoRoot = Path.GetFullPath(Path.Combine(dataPath, "..", ".."));
            return Path.Combine(repoRoot, FileName);
        }
    }
}
