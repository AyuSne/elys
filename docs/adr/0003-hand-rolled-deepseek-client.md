# 自写 DeepSeek HTTP 客户端，不用现成 C# SDK

Unity 侧直接手写约 200 行的 `UnityWebRequest` + Newtonsoft 客户端，而不是引入 [com.openai.unity](https://github.com/RageAgainstThePixel/com.openai.unity)（598★，MIT，仍在维护）。原因是 DeepSeek 的接口有三处偏离 OpenAI 标准，第三方库包不住：

1. `thinking` 参数必须放在 `extra_body` 里，且**默认开启、默认强度为 high**——游戏内对话必须显式关闭，否则延迟不可接受；
2. 响应多一个 `reasoning_content` 字段，带 `tools` 时**必须原样回传**，否则直接 400；
3. `response_format` 的 JSON 输出失败时**不报错**，而是把内容替换成空格，`finish_reason` 仍是 `stop`。

这三条要么库不支持，要么得改源码。而实际只需要两个 POST（`/chat/completions`、`/embeddings`），自写的可控性远高于嵌一个抽象层。

**后果**：流式解析、重试、超时这些本来由库负责的部分要自己写，约多半天工作量。
