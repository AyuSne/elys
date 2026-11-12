# Elys · 一句话指挥的 AI 农场

> 玩家说一句话，AI NPC 自己把「播种 → 浇水 → 施肥 → 除草 → 收获」做完。
> 模型只负责理解人话和措辞，所有动作都要过确定性的白名单与前置条件校验；
> 模型不可用时整条链路自动回退本地模板，演示不会卡死。
> 三名 NPC 在同一批地块上各自用效用打分决定此刻做什么，行为差异来自人设与体力，而不是台词。

![三名 NPC 在同一批地块上自主干活：阿芽体力见底去草垛休息，阿禾与阿土继续找活](docs/images/three-npc-autonomy.png)

这是 #6 之后的状态：农场里同时跑着阿禾、阿芽、阿土三名 NPC，右侧的决策可视化层可以
当场摊开选中 NPC 的记忆、意图、候选与否决原因、最近一次模型调用与状态条。完整的种田闭环、
可复核的 EditMode 回归报告、可直接双击运行的 Windows 包，以及这份 README 都保留着。
追问与社交仍是后续加分项，见 [GitHub Issues](https://github.com/AyuSne/elys/issues)。

![决策可视化层：阿禾的当前意图是等 A3 成熟，两条有效候选与一条播种的否决原因同时可见；记忆带权重条，模型调用带模型名、耗时与 token](docs/images/decision-inspector.png)

## 目录

- [跑起来](#跑起来)
- [跑测试与看报告](#跑测试与看报告)
- [打 Windows 包](#打-windows-包)
- [配置模型密钥](#配置模型密钥)
- [怎么玩](#怎么玩)
- [仓库结构](#仓库结构)
- [文档与设计](#文档与设计)
- [已知边界](#已知边界)

## 跑起来

两种方式，任选一种。**只跑包不需要装 Unity。**

### 方式一：直接跑已打好的包

如果拿到的是带 `Builds/Windows/` 的交付目录（本机已经构建过），双击 `Builds/Windows/Elys.exe`
（窗口 1280×720）。

- 仓库本身不提交这 98 MB 二进制（`.gitignore` 排除了 `Builds/`）。只拿到源码时，按
  [打 Windows 包](#打-windows-包) 一条命令生成；交给评审时把整个 `Builds/Windows/` 目录一起打包即可。
- 没有配置密钥也能跑：NPC 用本地模板理解你的话，闭环照常。
- `Builds/Windows/` 可以直接整个目录拷给别人，不依赖仓库与 Unity。

### 方式二：在 Unity 里打开工程

1. 安装 **Unity 6000.3.10f1**（含 Windows Build Support 模块）。
2. 用 Unity Hub 打开仓库下的 `Game/` 目录（不是仓库根目录）。
3. 打开 `Assets/Scenes/FarmDemo.unity`，点 Play。

命令行等价做法：

```powershell
unity open Game
unity status --until-ready
```

同时开了多个 Unity 工程时，再给 `status` 加 `--project-path <仓库绝对路径>\Game`。

> 第一次打开会导入 URP 与测试框架，需要几分钟。场景里只有一个引导器与 HUD，
> 地块、作物、NPC 的方块都是运行时由代码生成的，所以改布局只改代码，不用手改场景。

## 跑测试与看报告

### 一条命令（推荐）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-tests.ps1
```

脚本会跑全套 EditMode 用例，把 NUnit XML 写到 `TestResults/editmode.xml`，
并在终端打印总数、失败数与**题目要求的三项验收**各自的结果。退出码非 0 表示有失败。
报告是生成物（`TestResults/` 不入库），每次运行覆盖。

### 等价的原生命令

```powershell
unity test Game --mode EditMode --timeout 900 --output TestResults/editmode.xml
```

Unity 工程在 `Game/` 子目录，所以命令要带 `Game`；`cd Game` 之后可以直接
`unity test --mode EditMode`。跑测试不需要打开 Unity 编辑器；编辑器开着（尤其 Play Mode）时测试会被拒绝。
需要 Unity CLI（`unity` 命令，安装：`irm https://unity.com/install.ps1 | iex`）。

### 报告里看什么

`TestResults/editmode.xml` 是标准 NUnit 3 XML，CI 与 IDE 都能直接读。

题目要求与 #4–#6 验收的对应关系：

| 题目要求 | 验收用例（报告里可直接搜） | 断言的是 |
|---|---|---|
| 五个工步完整（题面「步骤完整」） | `AcceptanceTests.五个工步完整_按序执行并留下流水` | 五个工步按顺序执行、每条都留下流水、最后一步是收获 |
| 时间、背包功能正常 | `AcceptanceTests.背包守恒_全流程结束后三本账收支相抵` | 入账减出账等于持有；播 1 颗种子、收 3 个萝卜 |
| 时间推进 | `AcceptanceTests.时间推进_不跨日不会成熟且萝卜第4天收获` | 不跨日不生长；第 1 天播种、第 4 天收获 |
| 三名 NPC 的差异（#5） | `NpcDecisionTests.同一批地块上三名NPC抢活摸鱼与磨蹭可区分` | 同一批地块上，决定速度、出勤率与休息时长可区分 |
| 打分可解释（#5） | `NpcDecisionTests.候选工步带需求人设体力与距离的分项权重` | 总分 = 需求 × 人设 × 体力 − 距离，分项可单独展示 |
| 体力耗尽中断（#5） | `NpcDecisionTests.体力耗尽会中断工单并去休息` | 休息时工单不推进、地块不变；休息后可继续 |
| 不接模型也能跑（#5） | `NpcDecisionTests.不接模型也能把一句话变成三名NPC的农活` | 无模型客户端时本地模板接单，NPC 照常干活 |
| 决策可视化五项（#6） | `DecisionInspectorTests.决策快照包含五项且状态条随工步更新` | 五项齐全；体力 / 心情 / 饥饿随工步与跨日变化 |
| 否决原因真实（#6） | `DecisionInspectorTests.候选动作的否决原因直接来自动作校验` | 面板原因 = 动作校验返回值 = 拒绝台词 |
| 记忆检索（#6） | `DecisionInspectorTests.记忆最多三条且只属于该NPC` | 只取自己的三条，按权重降序 |
| 记忆排序（#6） | `DecisionInspectorTests.记忆检索优先命中当前意图指向的地块`、`同一件事新的记忆权重更高` | 相似度与时间衰减都参与排序 |
| 只读快照（#6） | `DecisionInspectorTests.读取决策快照不改变任何游戏状态` | 读面板不改时钟、账本与 NPC 状态 |
| 密钥不泄露 | `SecretFileLocatorTests.*` | 编辑器只读仓库根；打包后的播放器不隐式读任何密钥文件 |

全部用例共 70 个，覆盖领域闭环、时钟、三本账、三人效用打分与休息、决策可视化五项、
记忆的加权检索、意图路由（含模型失败降级）、模型协议与密钥来源。领域测试只打 `FarmWorld` 这条缝，
模型测试用假客户端，不联网、不花钱。
包本身的装配由构建脚本固定（`FarmBuild` 只把 `FarmDemo.unity` 传给 `BuildPlayer`），
运行证据由 `scripts/smoke-test-build.ps1` 与上面的截图给出，不写成 EditMode 用例。

## 打 Windows 包

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-windows.ps1
```

产物：`Builds/Windows/Elys.exe`。构建由 `Assets/Editor/FarmBuild.cs` 执行：

- 只打 `Assets/Scenes/FarmDemo.unity` 一个场景，保证启动就是演示；
- 构建前扫一遍 `Assets/`、构建后复核产物目录，发现 `.env` /
  `.secrets.local.json` 一类文件就失败（构建后的检查发生在包已经落盘之后，需要人工清理）；
- 默认允许在有未提交改动的工作树上构建（打包本身会改写 URP 预过滤等工程文件，
  下一次打包时工作树往往已经不干净）。要「从干净提交构建」时加 `-RequireClean`，它会要求工作树干净，
  构建产物的 `Elys.provenance.json` 也会记录对应提交。

等价的 CLI 命令（`--output-path` 用仓库下的绝对路径；相对路径会按 Unity 的工作目录解析）：

```powershell
unity build Game --target StandaloneWindows64 `
  --execute-method ElysFarm.EditorTools.FarmBuild.BuildWindows `
  --output-path <仓库绝对路径>\Builds\Windows\Elys.exe `
  --allow-dirty-build
```

也可以在 Unity 菜单里点 **Elys → 构建 Windows 包**。

打完包想确认它真能启动，跑一次冒烟验收（需要可交互的桌面会话）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\smoke-test-build.ps1
```

脚本会启动包、截一张窗口图到 `TestResults/smoke-window.png`、检查 `Player.log`
里有没有 `[Elys]` 启动日志与异常，然后关掉进程。

## 配置模型密钥

**不配也能完整跑完种田闭环**：意图解析、追问、闲聊全部回退本地模板，只是不如模型灵活。
配了之后 NPC 能听懂更多说法，HUD 会显示「模型在线」。

### 在 Unity 编辑器里

复制示例文件，填入自己的密钥：

```powershell
Copy-Item .secrets.local.example.json .secrets.local.json
```

```json
{
  "deepseek": {
    "baseUrl": "https://api.deepseek.com",
    "model": "deepseek-flash",
    "apiKey": "sk-你的密钥"
  }
}
```

`.secrets.local.json` 在 `.gitignore` 里，**不会入库**；它放在仓库根目录（`Game/` 之外）。

也可以用环境变量（优先级更高）：`DEEPSEEK_API_KEY`、`DEEPSEEK_BASE_URL`、
`DEEPSEEK_MODEL`。

### 在打好的包里

打包后的播放器**不隐式读取任何文件**，只认环境变量；或者显式指定一个文件。
先设置环境变量，再启动包：

```powershell
# 当前 PowerShell 窗口里设置后再启动
$env:DEEPSEEK_API_KEY = 'sk-你的密钥'
.\Builds\Windows\Elys.exe
```

也可以显式指定一个密钥文件（该文件不在包内即可）：

```powershell
$env:ELYS_SECRETS_FILE = 'D:\keys\elys.json'
.\Builds\Windows\Elys.exe
```

### 为什么密钥不会进仓库、也不会进包

| 环节 | 保证 |
|---|---|
| 入库 | `.secrets.local.json`、`.env`、`*.env.local` 在 `.gitignore` 里；仓库里只提交 `*.example.json` |
| 进包 | 构建只打 `Assets/Scenes/FarmDemo.unity`；`Assets/` 在构建前会被扫描，发现密钥类文件直接失败 |
| 运行时 | `SecretFileLocator` 只在编辑器里找仓库根的本地文件；播放器不隐式读任何文件，只认环境变量或显式指定的 `ELYS_SECRETS_FILE`（有测试覆盖） |
| 日志 | 模型配置只打印模型名与地址，不打印密钥 |

## 怎么玩

三名 NPC 开局**待命**，不会抢在玩家输入前把地种掉。

1. 在底部输入框输入一句话，按回车或点「下达」。右侧面板顶部的「阿禾 / 阿芽 / 阿土」按钮决定
   这句话说给谁听；左侧三行也可以直接点名字切换观察对象。
2. 试这些：

| 输入 | 会发生什么 |
|---|---|
| `把东边那块地种满萝卜` | 选中的 NPC 接单，按五步工单去 A3 地块播种、浇水、施肥、除草，等成熟后收获 |
| `种点萝卜` | 不点名地块，交给效用打分挑最该种的那块 |
| `给地浇水` | 只派一个浇水工步；不点名地块时同样由效用打分挑 |
| `你在干什么` | 追问，只读回答，不改变任何状态 |
| `今天天气不错` | 闲聊，只回话，不改变任何状态 |
| `去种月亮` | 品种不在白名单，当场拒绝并给出可读理由 |

3. 右侧「决策」面板显示选中 NPC 的当前意图、工单进度和前四个候选工步；
   有效候选带一行 `分数 = 需求 × 人设 × 体力 − 距离` 的分项，被否决的候选直接带校验给出的理由。
   这是临时状态面板，不是决策可视化层。
4. 点左侧或右侧的「决策可视化」打开决策可视化层（再点「关闭可视化」收起）。它只展示五项：
   ① 检索命中的三条记忆与权重条、② 当前意图、③ 候选动作与否决原因、④ 最近一次模型调用
   （模型名 / 耗时 / token）、⑤ 体力 / 心情 / 饥饿。候选里 `✓` 是有效工步并带效用分，
   `✕` 是被否决的工步并带 `ActionValidator` 当场返回的理由；关闭时右侧回到临时状态面板。
5. 左侧显示第几天、当天进度、1x / 4x / 16x 时间档位、三名 NPC 的体力条与当前意图、三块地的状态，
   以及「账本自检：入账 − 出账 = 持有 ✓」。
6. 点「开始自主」让三个人一起找活，观察差异：
   - **阿禾**（稳、勤、话少）决定不慢、体力消耗低，出勤最高，是「抢着干」的那个；
   - **阿芽**（快、爱偷懒、爱甩锅）决定最快，但累得快，体力一低就走去草垛休息，是「摸鱼」的那个；
   - **阿土**（慢、懂多、爱八卦）决定间隔最长、走路也慢，是「磨蹭」的那个。
7. 「暂停自主」只影响之后的选择；正在走的路或工单会先干完。
8. 一个游戏日等于 5 分钟真实时间。萝卜 3 个游戏日成熟，想快点看收获就切 16x。

## 仓库结构

```
Game/                        Unity 工程（Unity Hub 打开这一层）
  Assets/Scripts/Core/       纯领域层：FarmWorld、地块、时钟、三本账、效用打分、人设、动作校验
  Assets/Scripts/Core/Decision 决策可视化的五项只读快照
  Assets/Scripts/Core/Memory 记忆记录与加权检索
  Assets/Scripts/Core/Intent 意图解析：模型出口、本地模板、三出口路由
  Assets/Scripts/Model/      DeepSeek HTTP 客户端与密钥来源
  Assets/Scripts/Demo/       体块演示场景的引导器与 IMGUI HUD
  Assets/Tests/EditMode/     EditMode 回归测试
  Assets/Editor/             场景生成器、密钥防线与 Windows 构建入口
scripts/                     跑测试、打包、冒烟验收的 PowerShell 脚本
TestResults/                 测试报告生成目录（不入库）
docs/                        规格、ADR、调研、领域词汇表与 agent 笔记
```

## 文档与设计

| 文档 | 内容 |
|---|---|
| [ALIGNMENT.md](ALIGNMENT.md) | 与出题人逐条对齐的决策（含 7 天计划与第 3 天切点） |
| [GLOSSARY.md](GLOSSARY.md) | 领域词汇表：地块、作物、工步、工单、种子库、产出仓、随身、心情、饥饿、决策可视化 |
| [docs/spec/ai-farm-npc.md](docs/spec/ai-farm-npc.md) | 规格：分层、模块、时间、背包、降级与测试接缝 |
| [docs/adr/](docs/adr/) | 三条难回退的决策：确定性任务层 + 生成式表达层、效用打分而非 GOAP、自写 HTTP 客户端 |
| [docs/agents/unity-pitfalls.md](docs/agents/unity-pitfalls.md) | 这个 Unity 工程实际踩过的坑（编码、测试、打包、驱动编辑器） |

## 已知边界

这是完成 #6 后的版本，刻意只做到这里：

- **三名 NPC 与 3 块地**已就位；三人对话与信息传播在 #8。
- **决策可视化层**已按五项实现，左侧顶部或右侧临时面板都能开关；临时状态 HUD 仍在，两者不是同一个东西。
- **记忆只做到「自己做过的事」的关键词检索**；玩家原话、跨日追问与 embedding 余弦在 #7。
- **素材是纯体块**，Quaternius 导入与双机位在 #9。
- 只是 Windows 单机包；没有存档、多人、语音。
- 数值平衡不在考察范围，没有调；模型延迟以实际网络为准，超时即回退。

> 技术说明：运行时创建的方块材质用 `Shader.Find("Universal Render Pipeline/Lit")`，
> 因此 URP Lit 已加入 GraphicsSettings 的 Always Included Shaders，避免打包后材质丢失。
