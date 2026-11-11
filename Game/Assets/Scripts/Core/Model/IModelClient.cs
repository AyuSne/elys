using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ElysFarm.Core
{
    /// <summary>
    /// 模型调用的统一出口（见规格）：覆盖对话补全与 embedding 两类请求。
    /// 真实实现走 DeepSeek 与硅基流动；测试一律用假实现，永不联网。
    /// 实现必须带硬超时，失败时抛异常交给调用方走本地降级。
    /// </summary>
    public interface IModelClient
    {
        /// <summary>发起一次对话补全请求。</summary>
        Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default);

        /// <summary>把一段文本编码成向量。</summary>
        Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
    }

    /// <summary>对话中的一条消息。</summary>
    public readonly struct ChatMessage
    {
        /// <summary>角色：system / user / assistant。</summary>
        public string Role { get; }

        /// <summary>消息正文。</summary>
        public string Content { get; }

        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }

    /// <summary>一次对话补全请求。参数按用途分档（意图解析 / 闲聊）。</summary>
    public sealed class ChatRequest
    {
        public ChatRequest(IReadOnlyList<ChatMessage> messages)
        {
            Messages = messages;
        }

        /// <summary>消息列表，稳定前缀放最前以便命中缓存。</summary>
        public IReadOnlyList<ChatMessage> Messages { get; }

        /// <summary>是否要求 JSON 输出。</summary>
        public bool JsonOutput { get; set; }

        /// <summary>是否开启思考模式；游戏内对话应关闭。</summary>
        public bool Thinking { get; set; }

        /// <summary>思考强度：low / high；未开启思考时忽略。</summary>
        public string ReasoningEffort { get; set; }

        /// <summary>采样温度；开启思考时服务端会忽略。</summary>
        public float Temperature { get; set; }

        /// <summary>最大输出 token 数。</summary>
        public int MaxTokens { get; set; } = 512;

        /// <summary>本次调用的硬超时（秒）。</summary>
        public int TimeoutSeconds { get; set; } = 6;
    }

    /// <summary>对话补全的返回结果，附带决策可视化要展示的调用信息。</summary>
    public sealed class ChatResponse
    {
        public ChatResponse(
            string content,
            string model = null,
            double latencySeconds = 0d,
            int promptTokens = 0,
            int completionTokens = 0)
        {
            Content = content ?? string.Empty;
            Model = model;
            LatencySeconds = latencySeconds;
            PromptTokens = promptTokens;
            CompletionTokens = completionTokens;
        }

        /// <summary>模型输出的文本。</summary>
        public string Content { get; }

        /// <summary>实际使用的模型名。</summary>
        public string Model { get; }

        /// <summary>本次调用耗时（秒）。</summary>
        public double LatencySeconds { get; }

        /// <summary>输入 token 数。</summary>
        public int PromptTokens { get; }

        /// <summary>输出 token 数。</summary>
        public int CompletionTokens { get; }
    }

    /// <summary>
    /// 最近一次模型调用的结果摘要（模型名 / 耗时 / token），供 HUD 与决策可视化层展示。
    /// </summary>
    public readonly struct ModelCallInfo
    {
        public static readonly ModelCallInfo None = new ModelCallInfo(null, 0d, 0, 0, "offline");

        public ModelCallInfo(
            string model,
            double latencySeconds,
            int promptTokens,
            int completionTokens,
            string outcome,
            string detail = null)
        {
            Model = model;
            LatencySeconds = latencySeconds;
            PromptTokens = promptTokens;
            CompletionTokens = completionTokens;
            Outcome = outcome;
            Detail = detail;
        }

        /// <summary>模型名；离线或失败时可能为空。</summary>
        public string Model { get; }

        /// <summary>耗时（秒）。</summary>
        public double LatencySeconds { get; }

        /// <summary>输入 token 数。</summary>
        public int PromptTokens { get; }

        /// <summary>输出 token 数。</summary>
        public int CompletionTokens { get; }

        /// <summary>ok / timeout / error / unusable / offline。</summary>
        public string Outcome { get; }

        /// <summary>失败时的诊断信息；成功或离线时为 null。</summary>
        public string Detail { get; }

        /// <summary>结果是否来自一次成功的模型调用。</summary>
        public bool Succeeded => Outcome == "ok";
    }
}
