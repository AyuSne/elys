# 斯坦福 AI 小镇与 LLM 驱动 NPC（2026 视图）

> 资料截止：2026-11-11。标注约定：**「论文」**= Park et al., *Generative Agents*, UIST 2023；**「发布代码」**= `joonspk-research/generative_agents`；论文与发布代码不一致处已标出。

## 结论摘要

1. **「斯坦福 AI 小镇」= Smallville**：Park 等人在 UIST 2023 发表的 *Generative Agents*（[arXiv:2304.03442](https://arxiv.org/abs/2304.03442)），25 个 LLM agent 在 Sims 风格小镇生活两天。核心是 **memory stream + retrieval + reflection + planning** 四模块的外部记忆架构，而非某句 prompt。
2. **论文的评估是「访谈式 believability」，不是任务成功率**：100 名评估者排序，完整架构 TrueSkill μ=29.89，去掉 memory/planning/reflection 只剩 21.21；证明模块有贡献，不证明长时稳定。
3. **官方仓库已不可直接跑**：最后提交停在 2023-08-11，锁死 `openai==0.27.0`、`Django==2.2`、Python 3.9；AI Town 反而仍维护（2026-11 有提交），是更好的工程参考。
4. **2024-2026 的主线是「规模化 + 记忆专门化 + 推理基础设施化」**：1,052 人个体模拟、10k/百万 agent 社会模拟、A-Mem/Mem0 记忆层；商用侧 ACE、Convai 走「端侧小模型 + 结构化 action」，Inworld 主推推理/语音服务。
5. **对这道题**：保留 **memory stream + importance 检索**（它制造「记得上次」和「八卦扩散」的观察价值）；最该砍 **逐小时→5-15 分钟的递归 LLM 分解**——调用多、易漂移，玩家看不见。

---

## 1. 源头：论文与 Smallville

论文 [arXiv:2304.03442](https://arxiv.org/abs/2304.03442)（v2），UIST '23，DOI [10.1145/3586183.3606763](https://doi.org/10.1145/3586183.3606763)。演示页为论文脚注的 [reverie.herokuapp.com/UIST_Demo](https://reverie.herokuapp.com/UIST_Demo/)，代码在 [joonspk-research/generative_agents](https://github.com/joonspk-research/generative_agents)。

25 个 agent 各有一段自然语言人设作种子记忆；环境是「world → sector → arena → game object」树，agent 只记见过的子图；一个 game step = 10 秒游戏时间（README）。

## 2. 架构：四个模块的真实细节

### 2.1 memory stream

发布代码的 `AssociativeMemory` 就是论文的 memory stream（[associative_memory.py](https://github.com/joonspk-research/generative_agents/blob/main/reverie/backend_server/persona/memory_structures/associative_memory.py)）。每条 `ConceptNode` 含 `description`、`created`/`last_accessed`、`poignancy`（1-10，创建时 LLM 打分）、embedding、SPO 三元组、keywords、`depth`、`filling`（反思证据）、`expiration`；类型分 `event`/`chat`/`thought`，`chat` 的 `filling` 存逐句 transcript。

### 2.2 retrieval

论文公式 `score = α_recency·recency + α_importance·importance + α_relevance·relevance`，三项 min-max 归一化到 [0,1]，**所有 α=1**；取最高分若干条塞进上下文。

- **recency**：距上次检索的游戏小时数做指数衰减，**因子 0.995**。
- **importance**：即 `poignancy`。
- **relevance**：记忆与当前 query 的 embedding 余弦相似度。

**论文 vs 发布代码**：代码 `recency_decay=0.99`，recency 用「按 last_accessed 排序的序位」近似时间，`new_retrieve` 还留有 `gw=[0.5,3,2]`（relevance 最高），与论文等权不一致（[scratch.py](https://github.com/joonspk-research/generative_agents/blob/main/reverie/backend_server/persona/memory_structures/scratch.py)、[retrieve.py](https://github.com/joonspk-research/generative_agents/blob/main/reverie/backend_server/persona/cognitive_modules/retrieve.py)）。复刻照论文即可。

### 2.3 reflection

新感知事件的 importance 累加超过 **150**（代码 `importance_trigger_max`）触发并重置；论文称实际约每天 2-3 次。流程：取最近 **100 条** → 让 LLM 提「3 个高层问题」→ 检索 → 输出「5 条 insight + 证据」→ 存成 `thought`，`depth` 取被引用节点最大深度 +1，形成**反思树**（[reflect.py](https://github.com/joonspk-research/generative_agents/blob/main/reverie/backend_server/persona/cognitive_modules/reflect.py)）。反思不是流水账，而是「Klaus 对研究很投入」这类判断，之后像普通记忆一样被检索。

### 2.4 planning / react

早上生成 **5-8 条**日计划（含时间、地点），递归分解为**小时段**和 **5-15 分钟**动作；计划也进 memory stream，参与检索。每个时间步：感知 → 判断继续还是 reaction → 若要 reaction，**从该时刻起重生成剩余计划**（§4.3.1）。发布代码提前 1-2 小时分解，睡眠等动作不分解（[plan.py](https://github.com/joonspk-research/generative_agents/blob/main/reverie/backend_server/persona/cognitive_modules/plan.py)）——这是成本大头。

**对话**逐句生成，每句以「对对方的记忆摘要 + 意图」为条件；结束后摘要成 `chat` 写回双方（§4.3.2）。

### 2.5 信息扩散

Sam 参选：知道者 **1 人（4%）→ 8 人（32%）**；Isabella 的派对：**1 人（4%）→ 13 人（52%）**，无干预。网络密度 **0.167 → 0.74**；453 条「你认识谁」回答中 **1.3%（6 条）为幻觉**；受邀 12 人、实到 5 人。

失败模式：检索不到、记忆碎片（Tom 记得要在派对聊选举，却不记得有派对）、添油加醋（把邻居说成《国富论》作者）、语气正式、过度顺从。

### 2.6 评估、成本与局限

100 名 Prolific 评估者对五类访谈回答排序，换算 TrueSkill：完整 **29.89**、无反思 **26.88**、无反思无计划 **25.64**、人类众包写手 **22.95**、三者全无 **21.21**；与 prior-work 基线效应量 **d=8.16**（[§6.5](https://arxiv.org/html/2304.03442v2)）。消融条件共用同一份记忆、只测访谈回答，不是各自重跑两天。

成本：25 agent 跑 2 天「**数千美元 token 费、数天机时**」（§8.2）。局限：成本、检索错误、幻觉、一致性、prompt/memory hacking。

## 3. 开源实现现状

### 3.1 官方仓库：能读，不能直接跑

最后提交 **2023-08-11**（[commits](https://github.com/joonspk-research/generative_agents/commits/main)）。依赖钉死 `openai==0.27.0`（旧 `ChatCompletion.create`）、`Django==2.2`、`gensim==3.8`、Python 3.9.12（[requirements.txt](https://github.com/joonspk-research/generative_agents/blob/main/requirements.txt)），用今天的 OpenAI SDK 会直接报错。双进程：Django 环境服务器 + `reverie.py`，要在 `utils.py` 手填 key。它是**参考实现**，不是 SDK；社区有加 Ollama 的 fork。

### 3.2 AI Town：仍在维护的工程参考

[a16z-infra/ai-town](https://github.com/a16z-infra/ai-town)：Convex（数据库 + 向量检索 + 调度 + 事务）+ React/PixiJS，TypeScript 全栈。

- 60 tick/s 平滑移动，但只 **1 step/s** 落库；输入全走 `inputs` 表，引擎单线程用 generation number 防并发；输入延迟约 **1.5 s**（[ARCHITECTURE.md](https://github.com/a16z-infra/ai-town/blob/main/ARCHITECTURE.md)）。
- agent 是规则 + LLM：对话后摘要 → embedding → Convex 向量库；下次检索「我对 Danny 的看法」最相似的 **3 条**注入 prompt；embedding 有缓存；默认 `llama3`，可换 OpenAI/Together 等（[README](https://github.com/a16z-infra/ai-town)）。
- 2026-11 仍有提交；2026-06 升 Convex ^1.41 支持匿名本地开发；2026-08 修了「检索记忆与 system prompt 隔离」（[commit](https://github.com/a16z-infra/ai-town/commit/705fd1f5417973d306494ef9c387c4aad5534350)），回应 memory injection。
- 限制：world state 每步全量载入（建议 < 几十 KB）；输入走数据库；单线程引擎。

### 3.3 其他复刻/后继

- **Concordia**（DeepMind，[GABM 库](https://github.com/google-deepmind/concordia)）、**Project Sid**（Minecraft 1000+ agent，PIANO，[arXiv:2411.00114](https://arxiv.org/abs/2411.00114)）、**OASIS**（百万 agent，[arXiv:2411.11581](https://arxiv.org/abs/2411.11581)）、**AgentSociety**（10k/500 万交互，[arXiv:2502.08691](https://arxiv.org/abs/2502.08691)）把规模推到社会实验级。
- **Unity 侧没有看到官方 AI 小镇框架**。可用：[LLMUnity](https://github.com/undreamai/LLMUnity)（本地 llama.cpp、RAG、grammar/函数调用，Unity 6，Apache 2.0）、[SmartNPC](https://github.com/iiishop/SmartNPC)（`SmartFunction` 把 C# 方法暴露成工具，分层记忆 + 目标系统）、[Agenerela](https://github.com/agenerela/agenerela)（MIT，注册 action + 合法动作过滤 + 执行前二次校验）。

## 4. 2024-2026：从「小镇」到「社会模拟 + 推理基础设施」

### 4.1 规模与个体保真度

**1,052 人个体模拟**：用 2 小时访谈/问卷建 agent，在留出的 GSS 题上达到本人两周重测一致性的 **83%（访谈）/82%（问卷）/86%（两者）**，人口基线 74%（[arXiv:2411.10109](https://arxiv.org/abs/2411.10109)，v3 2026-06）。规模侧见 §3.3；规模化的代价是异步调度、分层决策和便宜模型。

### 4.2 记忆专门化

- **A-Mem**（NeurIPS 2025）：Zettelkasten 式笔记，新记忆自动生成关键词/标签并建链，还能反向更新旧记忆，[arXiv:2502.12110](https://arxiv.org/abs/2502.12110)。
- **Mem0**：从对话抽取、合并、检索记忆；比全量上下文 **p95 延迟 -91%、token -90%+**，LLM-as-a-Judge 相对 OpenAI 方案 +26%，[arXiv:2504.19413](https://arxiv.org/abs/2504.19413)。

趋势：不再把全部历史塞 prompt，而是「抽取 → 结构化 → 按需检索」；与论文 memory stream 同源，但更省。

### 4.3 2026 年的工程共识

- **研究侧真实水平**：2026-11 的系统映射研究筛 377 篇、细析 42 篇：短期记忆 60%、世界状态 grounding 69%，但**长期记忆仅 31%、reflection 38%、RAG 14%**；响应 **2.6-15 秒**，并列六类商用障碍（[arXiv:2611.05427](https://arxiv.org/abs/2611.05427)）。完整架构在游戏里仍属少数。
- **延迟是体验问题**：2026 年的实证研究显示 token 延迟会改变玩家对 NPC 社交性的感知（[Entertainment Computing 2026](https://doi.org/10.1016/j.entcom.2026.101213)）；对话轮间隙 0-300 ms，端到端必须流式 + 异步。
- **工具调用贵，能批就批**：NVIDIA 2026-03 对比「tool calling 3 次推理」vs「一次生成动作序列，1 次推理」，并给出 Lua 沙箱限制（[博客](https://developer.nvidia.com/blog/how-to-minimize-game-runtime-inference-costs-with-coding-agents/)）。启示不是让 LLM 写 C#，而是**一次调用返回多步动作**。
- **结构化 + 合法性校验是标配**：只把合法动作与目标给模型，执行前再校验。Agenerela 实测：2B 模型「只给合法动作」后 31/53 → 45/53，加校验后 20 条指令对 19 条；大一倍模型不加约束只有 13/20（[README](https://github.com/agenerela/agenerela)）。
- **常见失败**：非法动作、记忆幻觉、重复对话、被玩家话语或检索记忆注入、超时、过度顺从。手段：白名单 + 二次校验、证据指针、对话冷却、记忆隔离、超时回退模板。

### 4.4 商用与 Unity（2026 年状态）

- **NVIDIA ACE**：云 + 端侧模型套件（ASR/LLM/TTS/Audio2Face）。2026 年主推 **Game Agent SDK（Beta）**：C99 ABI + C++ 封装，Agent/Chat/RAG 三组 API，含 tool-call 链，Windows + NVIDIA GPU，SLM 约 3.8 GB 显存；Unreal 有 5.4-5.7 插件，已落地 PUBG、inZOI、MIR5 等（[ACE](https://developer.nvidia.com/ace-for-games)、[SDK](https://github.com/NVIDIA/game-agent-sdk)）。**没有 Unity 官方插件**。
- **Convai**：云端角色平台，Unity/Unreal 插件，卖点是长期记忆、情绪、guardrails、实时感知与动作（[convai.com](https://convai.com/)、[Unity SDK](https://assetstore.unity.com/packages/sdk/convai-sdk-for-unity-9235479)）。
- **Inworld**：2026 年主张变成「Models and inference for consumer applications」，主推推理与 Realtime API（2026-10 的 Inworld Inference：首 token 快 7.8×、90% prompt 可缓存、一次消费者回合约 20 次调用）（[官网](https://inworld.ai/)、[发布文](https://inworld.ai/blog/introducing-inworld-inference)）；[Unity SDK](https://github.com/uthark/inworld-unity-sdk) 停在 2023-02。
- **Unity 生态**：LLMUnity、SmartNPC、Agenerela，以及「规则拥有世界状态、LLM 只表达」的自研项目，如 [Zhao 的 Unity 6 AI Town](https://zhao0424.github.io/CV/projects/ai-town.html)（13+1 居民、9 天、Qwen3-4B 本地，断网回退模板）。

## 5. 对这道面试题：保留什么、砍掉什么

题目要「种田全流程 + 类 AI 小镇的观察价值」。种田是**确定性状态机**，小镇是**记忆与社交**；别用 LLM 同时干这两件事。

### 5.1 必须保留

1. **人设 + 状态卡**：性格/说话风格/关系 + 体力/心情/天气/田块状态，每次决策注入。没有状态，NPC 只是聊天机器人。
2. **memory stream + importance 检索**：每条事件记 `{文本, 时间, 重要性}`，检索用 `recency + importance + relevance`（小 demo 用关键词或本地 embedding 即可）。这是「记得你浇过水」和「把 A 的秘密告诉 B」的唯一来源。
3. **粗粒度计划 + 动作队列 + 重规划**：一天 5-8 个 beat（起床→巡田→浇水→午休→施肥→收获→回家），LLM 只在 beat 级决策；每动作有时长，完成取下一个；打断只重排之后的 beat。
4. **结构化动作 + 校验 + 回退**：`{action, target, duration, speech, emote}`；action 在白名单、target 存在、前置条件满足；失败重试一次，再回退默认动作。**不让 LLM 直接改 Unity 状态。**
5. **双层延迟**：即时层模板/本地小模型出短 bark + 表情，异步层出完整对话/计划；走路时预生成下一个动作。
6. **可观察层**：气泡、emoji/表情、内心独白、关系图、日记、跟随镜头。论文的 emoji 就是「动作→表情符号」的渲染层（`generate_action_pronunciatio`），一次动作一次调用，**不需要单独的表情包模型**。

### 5.2 好看但不划算

- **全天逐小时 + 5-15 分钟递归 LLM 分解**：一个 agent 一天几十次调用，多数动作玩家看不到，还越分解越漂。用「手工 beat 模板 + LLM 只填 1-2 句」替代。
- **每 tick 对每个 agent 做感知→LLM 反应**：O(N²) 调用。改为事件驱动：玩家靠近或高 importance 事件才触发。
- **25 个 agent**：5-8 个有区分度的人设比 25 个模板人更有观察价值。
- **反思树 + 证据引用**：保留「每天结束 1 次反思、产出 2-3 条 insight」即可。

### 5.3 最小架构与成本

```
玩家自然语言 → 意图解析（要谁/做什么/哪块田）
            → 任务分配（效用：干湿度/杂草/成熟度/距离/体力）
            → NPC 决策（LLM：人设+状态+检索记忆+合法动作集）
            → 动作 JSON（白名单校验）→ 行为树/NavMesh 执行
            → 事件写回记忆（importance）→ 对话/反思/八卦
```

6 个 agent ×（1 次日计划 + 6 beat + 8 段对话 + 1 反思）≈ 100 次调用/游戏日；用小模型或本地 4B，成本分钱级，延迟靠流式与预生成兜住。真正难的是**把自由文本收敛成合法动作**、**让记忆在正确的时候被想起来**。

## 来源清单

**论文与官方代码**
- *Generative Agents*, UIST '23 — https://arxiv.org/abs/2304.03442
- 论文 HTML（§4 架构、§6 评估、§7 边界、§8 局限）— https://arxiv.org/html/2304.03442v2
- 官方仓库 — https://github.com/joonspk-research/generative_agents
- memory stream — https://github.com/joonspk-research/generative_agents/blob/main/reverie/backend_server/persona/memory_structures/associative_memory.py
- retrieval — https://github.com/joonspk-research/generative_agents/blob/main/reverie/backend_server/persona/cognitive_modules/retrieve.py
- reflection — https://github.com/joonspk-research/generative_agents/blob/main/reverie/backend_server/persona/cognitive_modules/reflect.py
- planning — https://github.com/joonspk-research/generative_agents/blob/main/reverie/backend_server/persona/cognitive_modules/plan.py
- 超参数 — https://github.com/joonspk-research/generative_agents/blob/main/reverie/backend_server/persona/memory_structures/scratch.py
- 依赖版本 — https://github.com/joonspk-research/generative_agents/blob/main/requirements.txt

**开源复刻**
- AI Town — https://github.com/a16z-infra/ai-town
- AI Town 架构 — https://github.com/a16z-infra/ai-town/blob/main/ARCHITECTURE.md
- AI Town 记忆隔离修复（2026-08）— https://github.com/a16z-infra/ai-town/commit/705fd1f5417973d306494ef9c387c4aad5534350
- Concordia — https://github.com/google-deepmind/concordia（论文 https://arxiv.org/abs/2312.03664）
- Project Sid — https://arxiv.org/abs/2411.00114
- OASIS — https://arxiv.org/abs/2411.11581
- AgentSociety — https://arxiv.org/abs/2502.08691

**2024-2026 研究与工程**
- 1,052 人个体模拟 — https://arxiv.org/abs/2411.10109
- A-Mem（NeurIPS 2025）— https://arxiv.org/abs/2502.12110
- Mem0 — https://arxiv.org/abs/2504.19413
- LLM-based NPCs 系统性映射研究（2026-11）— https://arxiv.org/abs/2611.05427
- Token 延迟与 NPC 社交性（2026）— https://doi.org/10.1016/j.entcom.2026.101213
- NVIDIA：用 code agent 降低推理调用 — https://developer.nvidia.com/blog/how-to-minimize-game-runtime-inference-costs-with-coding-agents/

**商用与 Unity**
- NVIDIA ACE — https://developer.nvidia.com/ace-for-games
- NVIDIA Game Agent SDK — https://github.com/NVIDIA/game-agent-sdk
- Convai — https://convai.com/；Unity SDK — https://assetstore.unity.com/packages/sdk/convai-sdk-for-unity-9235479
- Inworld — https://inworld.ai/；Inworld Inference — https://inworld.ai/blog/introducing-inworld-inference；Unity SDK — https://github.com/uthark/inworld-unity-sdk
- LLMUnity — https://github.com/undreamai/LLMUnity
- SmartNPC — https://github.com/iiishop/SmartNPC
- Agenerela — https://github.com/agenerela/agenerela
- Unity 6 AI Town（Zhao）— https://zhao0424.github.io/CV/projects/ai-town.html
