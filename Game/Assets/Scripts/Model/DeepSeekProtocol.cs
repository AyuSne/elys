using System;
using System.Collections.Generic;
using ElysFarm.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ElysFarm.Model
{
    /// <summary>
    /// DeepSeek / 硅基流动的报文构造与解析。把三处非标准行为集中在这里：
    /// thinking 放顶层、response_format 失败时返回空格、usage 与 reasoning_content 的读取。
    /// 与网络无关，便于测试。
    /// </summary>
    public static class DeepSeekProtocol
    {
        /// <summary>构造 /chat/completions 的请求体。</summary>
        public static string BuildChatBody(ChatRequest request, string model)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                throw new ArgumentException("必须指定模型名", nameof(model));
            }

            var messages = new JArray();
            foreach (var message in request.Messages)
            {
                messages.Add(new JObject
                {
                    ["role"] = message.Role,
                    ["content"] = message.Content
                });
            }

            var body = new JObject
            {
                ["model"] = model,
                ["messages"] = messages,
                ["max_tokens"] = request.MaxTokens,
                ["stream"] = false
            };

            if (request.JsonOutput)
            {
                body["response_format"] = new JObject { ["type"] = "json_object" };
            }

            if (request.Thinking)
            {
                // DeepSeek 的 thinking 与 reasoning_effort 是顶层字段（SDK 里的 extra_body 只是合并进 body）。
                body["thinking"] = new JObject { ["type"] = "enabled" };
                body["reasoning_effort"] = string.IsNullOrWhiteSpace(request.ReasoningEffort)
                    ? "low"
                    : request.ReasoningEffort;
            }
            else
            {
                body["thinking"] = new JObject { ["type"] = "disabled" };
                if (request.Temperature > 0f)
                {
                    body["temperature"] = request.Temperature;
                }
            }

            return body.ToString(Formatting.None);
        }

        /// <summary>
        /// 解析 /chat/completions 的响应。JSON 输出失败时服务端会用空格替换内容且 finish_reason 仍是 stop，
        /// 因此空内容按失败处理，交给调用方重试或降级。
        /// </summary>
        public static ChatResponse ParseChatResponse(string json, string fallbackModel, double latencySeconds)
        {
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception ex)
            {
                throw new ModelRequestException("模型响应不是合法 JSON", ex);
            }

            var choice = root["choices"] as JArray;
            var message = choice != null && choice.Count > 0 ? choice[0]["message"] as JObject : null;
            var content = (string)message?["content"];
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new ModelRequestException("模型返回了空内容");
            }

            var usage = root["usage"] as JObject;
            return new ChatResponse(
                content,
                (string)root["model"] ?? fallbackModel,
                latencySeconds,
                (int?)usage?["prompt_tokens"] ?? 0,
                (int?)usage?["completion_tokens"] ?? 0);
        }

        /// <summary>构造 /embeddings 的请求体；硅基流动兼容 OpenAI 的批量 input。</summary>
        public static string BuildEmbeddingBody(IReadOnlyList<string> inputs, string model)
        {
            if (inputs == null || inputs.Count == 0)
            {
                throw new ArgumentException("embedding 至少需要一个输入", nameof(inputs));
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                throw new ArgumentException("必须指定 embedding 模型名", nameof(model));
            }

            var array = new JArray();
            foreach (var input in inputs)
            {
                array.Add(input ?? string.Empty);
            }

            var body = new JObject
            {
                ["model"] = model,
                ["input"] = array,
                ["encoding_format"] = "float"
            };

            return body.ToString(Formatting.None);
        }

        /// <summary>
        /// 解析 /embeddings 的响应。条数或维度不对时按不可用处理，
        /// 让调用方退回关键词检索，而不是拿半截向量去算余弦。
        /// </summary>
        public static IReadOnlyList<float[]> ParseEmbeddingResponse(string json, int expectedCount)
        {
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception ex)
            {
                throw new ModelRequestException("embedding 响应不是合法 JSON", ex);
            }

            var data = root["data"] as JArray;
            if (data == null || data.Count != expectedCount)
            {
                throw new ModelRequestException(
                    $"embedding 返回 {data?.Count ?? 0} 条，期望 {expectedCount} 条");
            }

            var vectors = new float[expectedCount][];
            var dimension = -1;
            foreach (var item in data)
            {
                var index = (int?)item?["index"] ?? -1;
                var embedding = item?["embedding"] as JArray;
                if (index < 0 || index >= expectedCount || embedding == null || embedding.Count == 0)
                {
                    throw new ModelRequestException("embedding 响应缺少向量或索引非法");
                }

                if (dimension >= 0 && embedding.Count != dimension)
                {
                    throw new ModelRequestException(
                        $"embedding 维度不一致：{dimension} 与 {embedding.Count}");
                }

                dimension = embedding.Count;
                var vector = new float[dimension];
                for (var i = 0; i < dimension; i++)
                {
                    try
                    {
                        vector[i] = (float)embedding[i];
                    }
                    catch (Exception ex)
                    {
                        throw new ModelRequestException("embedding 向量含非数值", ex);
                    }
                }

                vectors[index] = vector;
            }

            foreach (var vector in vectors)
            {
                if (vector == null)
                {
                    throw new ModelRequestException("embedding 响应缺少某条向量");
                }
            }

            return vectors;
        }
    }
}
