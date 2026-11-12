using System;
using ElysFarm.Core;
using ElysFarm.Model;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// DeepSeek 的三处非标准行为（thinking 顶层字段、JSON 输出失败返回空格、usage 读取）
    /// 在这里锁死。只测报文契约，不联网。
    /// </summary>
    public class DeepSeekProtocolTests
    {
        static ChatRequest Request(bool thinking, bool json, float temperature = 0f)
        {
            return new ChatRequest(new[]
            {
                new ChatMessage("system", "只输出 json"),
                new ChatMessage("user", "玩家说：把东边那块地种满萝卜")
            })
            {
                Thinking = thinking,
                JsonOutput = json,
                ReasoningEffort = "low",
                Temperature = temperature,
                MaxTokens = 400
            };
        }

        [Test]
        public void 意图请求把thinking与JSON输出放在请求体顶层()
        {
            var body = DeepSeekProtocol.BuildChatBody(Request(thinking: true, json: true), "deepseek-flash");

            StringAssert.Contains("\"thinking\":{\"type\":\"enabled\"}", body);
            StringAssert.Contains("\"reasoning_effort\":\"low\"", body);
            StringAssert.Contains("\"response_format\":{\"type\":\"json_object\"}", body);
            StringAssert.DoesNotContain("extra_body", body);
            StringAssert.Contains("\"model\":\"deepseek-flash\"", body);
        }

        [Test]
        public void 对话请求关闭thinking并保留temperature()
        {
            var body = DeepSeekProtocol.BuildChatBody(
                Request(thinking: false, json: false, temperature: 0.8f),
                "deepseek-flash");

            StringAssert.Contains("\"thinking\":{\"type\":\"disabled\"}", body);
            StringAssert.Contains("\"temperature\":0.8", body);
            StringAssert.DoesNotContain("reasoning_effort", body);
            StringAssert.DoesNotContain("response_format", body);
        }

        [Test]
        public void JSON输出失败返回空格时按空内容失败并触发降级()
        {
            const string response =
                "{\"model\":\"deepseek-flash\",\"choices\":[{\"message\":{\"content\":\"   \"}}]}";

            Assert.Throws<ModelRequestException>(
                () => DeepSeekProtocol.ParseChatResponse(response, "deepseek-flash", 0.5d));
        }

        [Test]
        public void 构造embedding请求体并解析向量()
        {
            var body = DeepSeekProtocol.BuildEmbeddingBody(
                new[] { "给 A1 浇水", "今天天气不错" },
                "BAAI/bge-m3");

            StringAssert.Contains("\"model\":\"BAAI/bge-m3\"", body);
            StringAssert.Contains("\"input\":[\"给 A1 浇水\",\"今天天气不错\"]", body);
            StringAssert.Contains("\"encoding_format\":\"float\"", body);

            const string response =
                "{\"data\":[{\"index\":0,\"embedding\":[0.1,0.2]},{\"index\":1,\"embedding\":[0.3,0.4]}]}";
            var vectors = DeepSeekProtocol.ParseEmbeddingResponse(response, 2);

            Assert.AreEqual(2, vectors.Count);
            Assert.AreEqual(0.2f, vectors[0][1], 0.0001f);
            Assert.AreEqual(0.3f, vectors[1][0], 0.0001f);
        }

        [Test]
        public void embedding条数或维度对不上时按不可用处理()
        {
            const string tooFew = "{\"data\":[{\"index\":0,\"embedding\":[0.1,0.2]}]}";
            Assert.Throws<ModelRequestException>(
                () => DeepSeekProtocol.ParseEmbeddingResponse(tooFew, 2));

            const string ragged =
                "{\"data\":[{\"index\":0,\"embedding\":[0.1,0.2]},{\"index\":1,\"embedding\":[0.3]}]}";
            Assert.Throws<ModelRequestException>(
                () => DeepSeekProtocol.ParseEmbeddingResponse(ragged, 2));

            const string notNumbers = "{\"data\":[{\"index\":0,\"embedding\":[0.1,\"oops\"]}]}";
            Assert.Throws<ModelRequestException>(
                () => DeepSeekProtocol.ParseEmbeddingResponse(notNumbers, 1));
        }

        [Test]
        public void 解析模型名与token用量()
        {
            const string response =
                "{\"model\":\"deepseek-flash\",\"choices\":[{\"message\":{\"content\":\"{\\\"outlet\\\":\\\"chat\\\"}\"}}]," +
                "\"usage\":{\"prompt_tokens\":129,\"completion_tokens\":7}}";

            var parsed = DeepSeekProtocol.ParseChatResponse(response, "fallback", 1.5d);

            Assert.AreEqual("{\"outlet\":\"chat\"}", parsed.Content);
            Assert.AreEqual("deepseek-flash", parsed.Model);
            Assert.AreEqual(129, parsed.PromptTokens);
            Assert.AreEqual(7, parsed.CompletionTokens);
            Assert.AreEqual(1.5d, parsed.LatencySeconds, 0.0001d);
        }
    }
}
