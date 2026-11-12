using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ElysFarm.Core;
using UnityEngine.Networking;

namespace ElysFarm.Model
{
    /// <summary>
    /// 手写的 DeepSeek 客户端（ADR-0003）。本轮只做 /chat/completions 一个 POST。
    /// 每次请求都带硬超时；超时、断网与协议错误统一抛 <see cref="ModelRequestException"/>，
    /// 调用方捕获后走本地模板，绝不阻塞游戏循环。
    /// 注意：UnityWebRequest 必须在主线程发起，CompleteAsync 也要从主线程调用。
    /// </summary>
    public sealed class DeepSeekModelClient : IModelClient
    {
        readonly ModelEndpointSettings settings;

        public DeepSeekModelClient(ModelEndpointSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                throw new ArgumentException("缺少 DeepSeek BaseUrl", nameof(settings));
            }

            if (string.IsNullOrWhiteSpace(settings.Model))
            {
                throw new ArgumentException("缺少 DeepSeek 模型名", nameof(settings));
            }
        }

        /// <summary>本次接入使用的模型名；失败时决策可视化也要能展示它。</summary>
        public string ModelName => settings.Model;

        /// <summary>只有配置了硅基流动密钥时才发向量请求；否则记忆检索用关键词。</summary>
        public bool SupportsEmbeddings => settings.HasEmbeddingKey;

        /// <summary>发起一次对话补全。</summary>
        public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!settings.HasChatKey)
            {
                throw new ModelRequestException("没有配置 DeepSeek 密钥");
            }

            var body = DeepSeekProtocol.BuildChatBody(request, settings.Model);
            var stopwatch = Stopwatch.StartNew();
            var json = await PostAsync(
                Combine(settings.BaseUrl, "/chat/completions"),
                settings.ApiKey,
                body,
                request.TimeoutSeconds,
                cancellationToken);

            return DeepSeekProtocol.ParseChatResponse(json, settings.Model, stopwatch.Elapsed.TotalSeconds);
        }

        /// <summary>批量取记忆与检索线索的向量；未配置密钥时抛错，由路由器退回关键词检索。</summary>
        public async Task<IReadOnlyList<float[]>> EmbedAsync(
            IReadOnlyList<string> inputs,
            CancellationToken cancellationToken = default)
        {
            if (inputs == null || inputs.Count == 0)
            {
                return Array.Empty<float[]>();
            }

            if (!settings.HasEmbeddingKey)
            {
                throw new ModelRequestException("没有配置硅基流动密钥");
            }

            var body = DeepSeekProtocol.BuildEmbeddingBody(inputs, settings.EmbeddingModel);
            var json = await PostAsync(
                Combine(settings.EmbeddingBaseUrl, "/embeddings"),
                settings.EmbeddingApiKey,
                body,
                settings.EmbeddingTimeoutSeconds,
                cancellationToken);

            return DeepSeekProtocol.ParseEmbeddingResponse(json, inputs.Count);
        }

        static async Task<string> PostAsync(
            string url,
            string apiKey,
            string payload,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            var body = Encoding.UTF8.GetBytes(payload);
            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body) { contentType = "application/json" },
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = Math.Max(1, timeoutSeconds)
            };

            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            var operation = request.SendWebRequest();
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (operation.isDone)
            {
                completion.TrySetResult(true);
            }
            else
            {
                operation.completed += _ => completion.TrySetResult(true);
            }

            using var registration = cancellationToken.CanBeCanceled
                ? cancellationToken.Register(() =>
                {
                    request.Abort();
                    completion.TrySetCanceled(cancellationToken);
                })
                : default;

            try
            {
                await completion.Task;
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                var response = request.downloadHandler != null ? request.downloadHandler.text : null;
                throw new ModelRequestException(
                    $"模型请求失败（HTTP {request.responseCode}）：{DescribeFailure(response, request.error)}");
            }

            return request.downloadHandler.text;
        }

        static string Combine(string baseUrl, string path)
        {
            return baseUrl.TrimEnd('/') + path;
        }

        static string DescribeFailure(string text, string fallback)
        {
            var value = string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
            if (string.IsNullOrEmpty(value))
            {
                return "无响应内容";
            }

            return value.Length <= 300 ? value : value.Substring(0, 300) + "…";
        }
    }
}
