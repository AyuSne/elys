using System;
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

        /// <summary>构造 /embeddings 的请求体。</summary>
        public static string BuildEmbeddingBody(string model, string text)
        {
            return new JObject
            {
                ["model"] = model,
                ["input"] = text
            }.ToString(Formatting.None);
        }

        /// <summary>解析 /embeddings 的响应，返回第一条向量。</summary>
        public static float[] ParseEmbedding(string json)
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
            var embedding = data != null && data.Count > 0 ? data[0]["embedding"] as JArray : null;
            if (embedding == null || embedding.Count == 0)
            {
                throw new ModelRequestException("embedding 响应没有向量");
            }

            var vector = new float[embedding.Count];
            for (var i = 0; i < embedding.Count; i++)
            {
                vector[i] = (float)embedding[i];
            }

            return vector;
        }
    }
}
