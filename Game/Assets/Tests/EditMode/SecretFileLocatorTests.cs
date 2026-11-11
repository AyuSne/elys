using System.IO;
using ElysFarm.Model;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// 密钥文件来源（#4「密钥不出现在仓库、也不打进包内」）。
    /// 编辑器里允许读仓库根下被 .gitignore 排除的本地文件；打包后的播放器不猜任何路径，
    /// 只接受显式指定的文件或环境变量，避免包旁边散落的密钥被误读。
    /// </summary>
    public class SecretFileLocatorTests
    {
        static string DataPath => Path.GetFullPath(Path.Combine(Path.GetTempPath(), "elys-repo", "Game", "Assets"));

        [Test]
        public void 编辑器里从仓库根的本地密钥文件读取()
        {
            Assert.AreEqual(
                Path.GetFullPath(Path.Combine(DataPath, "..", "..", ".secrets.local.json")),
                SecretFileLocator.Resolve(isEditor: true, DataPath, explicitPath: null),
                "编辑器的默认来源是仓库根，不是工程目录或 Assets");
        }

        [Test]
        public void 播放器不隐式读取任何密钥文件()
        {
            Assert.IsNull(
                SecretFileLocator.Resolve(isEditor: false, DataPath, explicitPath: null),
                "打包后的播放器只认环境变量，不在包旁边翻密钥文件");
        }

        [Test]
        public void 显式指定的密钥文件优先()
        {
            var explicitPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "elys-keys", "team.json"));

            Assert.AreEqual(
                explicitPath,
                SecretFileLocator.Resolve(isEditor: true, DataPath, explicitPath),
                "显式路径应覆盖仓库根的默认来源");
            Assert.AreEqual(
                explicitPath,
                SecretFileLocator.Resolve(isEditor: false, DataPath, explicitPath),
                "播放器也允许显式指定，但只读这一份");
        }
    }
}
