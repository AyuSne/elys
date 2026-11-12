using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElysFarm.Core;

namespace ElysFarm.Tests
{
    /// <summary>
    /// 假模型客户端：测试里唯一的外部 IO 替身。测试用预置 JSON 或回调驱动它，永不联网；
    /// <see cref="SupportsEmbeddings"/> 与 <see cref="OnEmbed"/> 控制向量路径。
    /// 意图路由与闲聊出口的测试共用这一份，避免每个文件各写一个假客户端。
    /// </summary>
    sealed class ScriptedModelClient : IModelClient
    {
        readonly Func<ChatRequest, CancellationToken, Task<ChatResponse>> onChat;

        public ScriptedModelClient(string json)
            : this((_, __) => Task.FromResult(new ChatResponse(json, "fake-deepseek")))
        {
        }

        public ScriptedModelClient(Func<ChatRequest, CancellationToken, Task<ChatResponse>> onChat)
        {
            this.onChat = onChat;
        }

        public string ModelName => "fake-deepseek";

        public bool SupportsEmbeddings { get; set; }

        public Func<IReadOnlyList<string>, Task<IReadOnlyList<float[]>>> OnEmbed { get; set; }

        public int EmbedCalls { get; private set; }

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return onChat(request, cancellationToken);
        }

        public Task<IReadOnlyList<float[]>> EmbedAsync(
            IReadOnlyList<string> inputs,
            CancellationToken cancellationToken = default)
        {
            EmbedCalls++;
            if (OnEmbed == null)
            {
                return Task.FromException<IReadOnlyList<float[]>>(
                    new NotSupportedException("假客户端未配置向量"));
            }

            return OnEmbed(inputs);
        }

        public int Calls { get; private set; }
    }
}
