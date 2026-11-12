using System;

namespace ElysFarm.Model
{
    /// <summary>
    /// 模型接入参数。默认值与技术调研一致：DeepSeek 对话。
    /// 密钥只从环境变量或代码仓外的本地文件读取，绝不写进代码与构建产物。
    /// </summary>
    public sealed class ModelEndpointSettings
    {
        /// <summary>DeepSeek 的 OpenAI 兼容地址。</summary>
        public string BaseUrl { get; set; } = "https://api.deepseek.com";

        /// <summary>对话模型名。</summary>
        public string Model { get; set; } = "deepseek-flash";

        /// <summary>DeepSeek 密钥。</summary>
        public string ApiKey { get; set; }

        /// <summary>硅基流动的 OpenAI 兼容地址，用于 embedding。</summary>
        public string EmbeddingBaseUrl { get; set; } = "https://api.siliconflow.cn/v1";

        /// <summary>embedding 模型名；默认 bge-m3（1024 维，中英混排友好）。</summary>
        public string EmbeddingModel { get; set; } = "BAAI/bge-m3";

        /// <summary>硅基流动密钥。</summary>
        public string EmbeddingApiKey { get; set; }

        /// <summary>一次 embedding 请求的硬超时（秒）。</summary>
        public int EmbeddingTimeoutSeconds { get; set; } = 3;

        /// <summary>是否配置了可用的对话密钥。</summary>
        public bool HasChatKey => !string.IsNullOrWhiteSpace(ApiKey);

        /// <summary>是否配置了可用的 embedding 密钥。</summary>
        public bool HasEmbeddingKey => !string.IsNullOrWhiteSpace(EmbeddingApiKey);
    }

    /// <summary>模型调用失败（网络、协议或空 JSON）。调用方应捕获并走本地降级。</summary>
    public sealed class ModelRequestException : Exception
    {
        public ModelRequestException(string message)
            : base(message)
        {
        }

        public ModelRequestException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
