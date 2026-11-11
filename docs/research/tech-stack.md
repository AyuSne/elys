# 技术选型与接入方案（DeepSeek + 硅基流动）

> 资料截止 **2026-11-11**，API 细节均取自官方文档原文。本文只保留 star 数足够、值得依赖的开源库；小体量新库已剔除。

## 一、选型结论

| 层 | 选择 | 理由 |
|---|---|---|
| LLM | **DeepSeek `deepseek-flash`**（V4.1-Flash-1017） | 1M 上下文、原生 JSON 输出 + 工具调用、价格极低、国内直连 |
| Embedding | **SiliconFlow `BAAI/bge-m3`** | 8192 token 输入、多语言（中英混排友好）、1024 维 |
| HTTP | **自写薄客户端**（UnityWebRequest + Newtonsoft） | 见 §6：DeepSeek 有非标准字段，第三方库包不住 |
| 异步 | **UniTask**（11.3k★, MIT） | LLM 调用不能卡帧 |
| 导航 / UI | Unity 官方 `AI Navigation`、`uGUI` 或 `UI Toolkit`、TextMeshPro | 不引第三方 |
| JSON | `com.unity.nuget.newtonsoft-json`（官方包） | 解析结构化动作 |

**不采用 LLMUnity**：它面向本地 llama.cpp 推理，既然走云端 API，它就没有价值了（1.7k★ 但方向不符）。

## 二、DeepSeek API 事实

| 项 | 值 |
|---|---|
| base_url（OpenAI 格式） | `https://api.deepseek.com` |
| base_url（Anthropic 格式） | `https://api.deepseek.com/anthropic` |
| 模型名 | `deepseek-flash`、`deepseek-v4-pro` |
| 上下文 | **1M tokens** |
| 最大输出 | **384K** |
| JSON Output | ✅ |
| Tool Calls | ✅ |
| Vision | ✅ 仅 flash |
| 并发上限 | flash 2500 / pro 500 |

**价格**（每 1M tokens，美元；错峰为峰时的一半）：

| | flash 错峰 | flash 峰时 | v4-pro 错峰 | v4-pro 峰时 |
|---|---|---|---|---|
| 输入（缓存命中） | $0.003 | $0.006 | $0.022 | $0.044 |
| 输入（缓存未命中） | $0.15 | $0.30 | $0.66 | $1.32 |
| 输出 | $0.60 | $1.20 | $1.98 | $3.96 |

峰时为 UTC 周一至周五 01:00–04:00 与 06:00–10:00（北京时间 09:00–12:00 与 14:00–18:00），中国法定节假日全按错峰。**这条直接影响你哪天录演示视频。**

## 三、⚠️ 四个必须知道的坑

### 坑 1：thinking 模式**默认开启**，且默认 effort 是 `high`

对游戏内对话是灾难级延迟。必须按用途分档：

```
短反应 / 气泡  →  {"thinking": {"type": "disabled"}}
意图解析      →  {"thinking": {"type": "enabled"}}, "reasoning_effort": "low"
日计划 / 重规划 →  {"thinking": {"type": "enabled"}}, "reasoning_effort": "high"
```

用 OpenAI SDK 时，`thinking` 必须塞进 `extra_body`：

```python
extra_body={"thinking": {"type": "enabled"}}
```

effort 映射：`minimal/low → low`，`medium/high/xhigh → high`，`max/ultra → max`。

### 坑 2：thinking 模式下 `temperature` **无效**

文档明确：thinking 模式**不支持** `temperature`、`presence_penalty`、`frequency_penalty`，设了不报错但**不生效**。

所以想让 NPC 说话有性格变化，**要么关掉 thinking 用 temperature，要么靠 prompt 里的人设**。这条会推翻很多「调 temperature 让 NPC 有个性」的直觉做法。

### 坑 3：带 `tools` 时，`reasoning_content` 必须回传，否则 400

带 `tools` 参数的请求，前几轮返回的 `reasoning_content` **必须原样带回**，否则 API 直接返回 **400**。不带 `tools` 时则相反——传了也会被忽略。

这是做工具调用循环时最容易卡住的点，C# 客户端里要把 `reasoning_content` 当成消息的一部分存住。

### 坑 4：JSON 输出失败时返回**一空格子**，不是报错

`response_format: {"type":"json_object"}` 时，若模型输出无法解析为 JSON，API 会把非 JSON 内容**替换成空格 `U+0020`**，于是 `content` 可能全是空格，而 `finish_reason` 仍是 `stop`。

必须做的三件事：

1. `trim()` 后判空 → 重试；
2. prompt 里**必须出现 "json" 这个词**并给一个格式示例；
3. `max_tokens` 给足，防止 JSON 被截断。

## 四、缓存：省 50 倍的关键

缓存命中的输入价格是**未命中的 1/50**（$0.003 vs $0.15）。

做法：把**稳定的长前缀**放在 prompt 最前面——世界设定、人设卡、动作白名单、输出 schema——每次请求一字不改；易变的（当前时间、田块状态、检索到的记忆）放后面。

一个 agent 一天的调用里，前缀能命中绝大部分，成本会掉到几乎可以忽略。

## 五、Embedding（硅基流动）

| 项 | 值 |
|---|---|
| 端点 | `POST https://api.siliconflow.cn/v1/embeddings` |
| 推荐模型 | `BAAI/bge-m3` |
| 最大输入 | bge-m3 **8192** tokens；Qwen3-Embedding 系列 **32768**；bge-large-zh / bce **512** |
| 可选维度 | 仅 `Qwen/Qwen3-Embedding-*` 支持 `dimensions` 参数（8B 可到 4096，4B 到 2560，0.6B 到 1024）；**bge-m3 固定 1024 维** |
| 其他参数 | `encoding_format`（float / base64）、`truncate`（left / right） |

**为什么选 bge-m3 而不是 Qwen3-Embedding-8B**：8B 质量更高、维度可调、上下文更长，但每次检索都要付一次向量计算，demo 里 1024 维已经够用，且 bge-m3 中英混排是强项。若后期检索不准再换 8B。

**还有 rerank**：硅基流动提供独立的 `/v1/rerank` 接口。记忆检索两步走（先向量召回 10 条，再 rerank 取 3 条）能显著提升「记得上一件事」的命中率，成本又低。

## 六、为什么自写客户端而不是用 com.openai.unity

[com.openai.unity](https://github.com/RageAgainstThePixel/com.openai.unity)（598★, MIT, 2026-11 仍在更新）是最主流的 C# OpenAI 客户端，但 DeepSeek 有三处非标准：

1. `thinking` 要放在 `extra_body` 里；
2. 响应里多一个 `reasoning_content` 字段，且**带 tools 时必须回传**；
3. `response_format` 的失败行为是「返回空格」而非报错。

这些要么库不支持、要么得改源码。自写一个约 200 行的客户端（`UnityWebRequest` + `Newtonsoft`）反而更可控：`/chat/completions` 和 `/embeddings` 两个 POST，没有别的。

**结论**：自写客户端；`com.openai.unity` 只当参考实现读。

## 七、三次调用 × 三档参数（建议默认值）

| 用途 | 触发 | thinking | 其他 | 期望延迟 |
|---|---|---|---|---|
| 意图解析（自然语言 → 任务） | 玩家回车时 | `enabled`, effort `low` | JSON 输出 + 动作白名单 | 1–3 s |
| 日计划 / 重规划 | 每天开工、被打断时 | `enabled`, effort `high` | JSON 输出 | 3–8 s |
| 对话 / 内心独白 | 与 NPC 交互、事件触发 | `disabled` | `temperature` 0.8–1.0，流式 | 首 token 快，整体 1–2 s |
| 短反应 / 气泡 | 频繁、可预测 | **不调 API** | 本地模板表 | 0 ms |

最后一档最重要：**能本地模板出的就不要调 API**。状态变化的气泡（「渴了」「累了」「这地该翻了」）用模板表 + 少量随机变体，既零延迟又不花 token，占观察价值的很大一部分。

## 八、成本估算

单次调用按输入 1.5k / 输出 200 tokens、缓存命中率 80% 估：

- 单个 agent 一天约 100 次调用（1 次日计划 + 6 段对话 + 若干反应）；
- 6 个 agent 一天 ≈ 600 次调用 ≈ 90 万 input + 12 万 output tokens；
- 按错峰价：输入约 `0.9 × (0.8×0.003 + 0.2×0.15) ≈ $0.029`，输出约 `0.12 × 0.6 ≈ $0.072`；
- **一个游戏日约 $0.1**，跑一整个面试演示周也不到 $1。

Embedding 调用量更小（每条记忆一次），可忽略。**成本不是这道题的约束，延迟才是。**

## 九、待确认

- **API key 归属**：面试用的 key 是公司给还是自己出？自出的话建议申请独立 key，别用日常的。
- **网络**：`api.deepseek.com` 与 `api.siliconflow.cn` 国内直连；若在受限网络里演示，需要提前验证连通性并准备离线回退（本地模板 + 假向量）。
- **SiliconFlow 定价**：以平台定价页为准，本文未引具体价格（各模型差异大且会变）。

## 来源

- DeepSeek 首次调用与模型名 — https://api-docs.deepseek.com/
- 模型与价格 — https://api-docs.deepseek.com/quick_start/pricing
- Thinking Mode（开关、effort、temperature 失效、reasoning_content 回传） — https://api-docs.deepseek.com/guides/thinking_mode
- JSON Output（空格替换行为） — https://api-docs.deepseek.com/guides/json_mode
- SiliconFlow 嵌入接口 — https://api-docs.siliconflow.cn/docs/api/embeddings-post
