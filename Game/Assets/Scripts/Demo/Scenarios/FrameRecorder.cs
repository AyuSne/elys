using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 逐帧录制演示画面。用 <see cref="ScreenCapture.CaptureScreenshotAsTexture"/> 直接抓播放器后缓冲，
    /// 不依赖窗口位置、DPI 缩放或桌面是否被遮挡——这是 #10 能稳定产出 1280×720 录屏的原因。
    /// 帧文件交给 scripts/record-demo.ps1 用 ffmpeg 编码。
    /// </summary>
    public sealed class FrameRecorder : MonoBehaviour
    {
        string directory;
        float fps = 20f;
        bool jpeg;
        bool started;
        bool stopRequested;
        int frameCount;

        public bool Finished { get; private set; }

        /// <summary>
        /// <paramref name="useJpeg"/> 为 true 时写 JPEG 而不是 PNG：1080p 下
        /// <c>EncodeToPNG</c> 每帧要 ~70ms，实测只能跑到 13fps；JPEG 质量 92 能跑到 30fps 以上，
        /// 肉眼与无损无差（宣传片素材采集用这条）。
        /// </summary>
        public void Initialize(string targetDirectory, float framesPerSecond, bool useJpeg = false)
        {
            directory = targetDirectory;
            fps = Mathf.Max(1f, framesPerSecond);
            jpeg = useJpeg;
            if (!started && isActiveAndEnabled)
            {
                StartRecording();
            }
        }

        void Start()
        {
            StartRecording();
        }

        public void StartRecording()
        {
            if (started || string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            started = true;
            Directory.CreateDirectory(directory);
            Debug.Log($"[Elys][record] 开始录制：{Path.GetFullPath(directory)} · {fps:0.#} fps · {Screen.width}×{Screen.height}");
            StartCoroutine(CaptureLoop());
        }

        /// <summary>请求收尾；录制协程会写完 manifest 再置 <see cref="Finished"/>。</summary>
        public void StopRecording()
        {
            stopRequested = true;
        }

        IEnumerator CaptureLoop()
        {
            var interval = 1f / fps;
            // 先让场景把第一帧画完，避免录到黑场。
            yield return new WaitForSecondsRealtime(0.4f);
            yield return new WaitForEndOfFrame();

            var nextAt = Time.unscaledTime;
            while (!stopRequested)
            {
                if (Time.unscaledTime >= nextAt)
                {
                    CaptureFrame();
                    nextAt += interval;
                    if (Time.unscaledTime > nextAt + interval * 2f)
                    {
                        // 编码比录制间隔还慢时不补拍，保持帧序而不是一口气抖出一串。
                        nextAt = Time.unscaledTime + interval;
                    }
                }

                yield return new WaitForEndOfFrame();
            }

            WriteManifest();
            Finished = true;
            Debug.Log($"[Elys][record] 录制完成：{frameCount} 帧 · {Path.GetFullPath(directory)}");
        }

        void CaptureFrame()
        {
            Texture2D texture = null;
            try
            {
                texture = ScreenCapture.CaptureScreenshotAsTexture();
                if (texture == null)
                {
                    return;
                }

                frameCount++;
                var ext = jpeg ? "jpg" : "png";
                var path = Path.Combine(directory, $"frame-{frameCount:000000}.{ext}");
                File.WriteAllBytes(path, jpeg ? texture.EncodeToJPG(92) : texture.EncodeToPNG());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Elys][record] 第 {frameCount + 1} 帧抓取失败：{ex.Message}");
            }
            finally
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }
        }

        void WriteManifest()
        {
            var builder = new StringBuilder();
            builder.Append("fps=").Append(fps.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            builder.Append("frames=").Append(frameCount).Append('\n');
            builder.Append("width=").Append(Screen.width).Append('\n');
            builder.Append("height=").Append(Screen.height).Append('\n');
            builder.Append("unity=").Append(Application.unityVersion).Append('\n');
            File.WriteAllText(Path.Combine(directory, "manifest.txt"), builder.ToString(), new UTF8Encoding(false));
        }
    }
}
