# Elys 交付说明（#10）

> 一句话：三条固定剧本可复现地跑通，录屏覆盖三条剧本，压测有数据，交付包在隔离环境里跑过。
> 本页是 #10「收口：固定剧本、录屏与压测」的交付说明；玩法与实现见 [README](../README.md)。

## 交付物

| 交付物 | 位置 | 复现方式 |
|---|---|---|
| Windows 可运行包 | `Builds/Windows/Elys.exe`（整个目录约 102 MB） | `scripts/build-windows.ps1` |
| 可交付压缩包 | `Builds/Elys-Windows.zip` + `.sha256` + `.manifest.json` | `scripts/package-windows.ps1 -RequireClean` |
| 三条剧本录屏 | [video/elys-demo.mp4](video/elys-demo.mp4)（1280×720 / 20fps / 无音轨） | `scripts/record-demo.ps1` |
| 三次一致证据 | `TestResults/scenarios/`（9 份 trace + 参考 + summary.md） | `scripts/run-scenarios.ps1` |
| 帧率压测 | `TestResults/perf/perf-report.md` + 原始 JSON | `scripts/perf-test.ps1` |
| 干净环境验收 | `TestResults/clean-environment/report.md` | `scripts/verify-clean-environment.ps1` |
| 汇总交付目录 | `Builds/Delivery/`（包、说明、录屏、证据） | `scripts/package-windows.ps1` |

`Builds/` 与 `TestResults/` 是生成物，不入库；录屏与说明入库，方便评审直接打开。

## 五分钟验收

1. **看录屏**：打开 `docs/video/elys-demo.mp4`。三段依次是剧本一（全流程）、剧本二（拒绝与抱怨）、
   剧本三（记忆体现），每段开头有字幕与章节说明。
2. **不看录屏，直接跑包**：解压 `Builds/Delivery/Elys-Windows.zip` 或直接打开 `Builds/Windows`，双击 `Elys.exe`。
   正常游玩不需要 Unity、不需要联网、不需要密钥；不配密钥时意图解析走本地模板。
3. **跑固定剧本**（包旁边执行，窗口会自动退出）：

   ```powershell
   .\Builds\Windows\Elys.exe -elysScenario full-flow -elysQuit 1
   .\Builds\Windows\Elys.exe -elysScenario refusal -elysQuit 1
   .\Builds\Windows\Elys.exe -elysScenario memory -elysQuit 1
   ```

   加 `-elysTrace <文件>` 会把该次的可观察结局写成证据文本。
4. **跑回归测试**：

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-tests.ps1
   ```

   全量 232 个 EditMode 用例，报告在 `TestResults/editmode.xml`；`ScenarioTests.*` 是 #10 新增的四条。
5. **复现三次一致**：

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-scenarios.ps1 -Runs 3
   ```

## 三条固定剧本

三条剧本共用同一份台本（`FixedScreenplays`）与同一个开局世界（`FarmDemoWorld.Create()`）。
领域跑（测试 / 参考证据）与演示回放（录屏）只在「怎么等」上不同：前者瞬间推进游戏日，后者真的等画面走完。
因此录屏里第 N 天、库存与拒绝理由，就是测试断言过的那一份。

### 剧本一 · 全流程

| 项目 | 内容 |
|---|---|
| 玩家输入 | `把中间那块地收了` → `把中间那块地种满萝卜` |
| 做了什么 | 先收掉 A2 成熟的小麦，空出地；再按五步工单播种 → 浇水 → 施肥 → 除草 → 收获 |
| 时间 | 第 1 天播种；16x 推进到第 4 天收获萝卜 |
| 背包 | 萝卜种子 12 → 9，每天补 2 到 15；小麦种子 4 → 2，每天补 1 到 5；产出仓 0 → 8（小麦 5 + 萝卜 3）；三本账收支相抵 |
| 证据 | `TestResults/scenarios/full-flow-run-{1,2,3}.trace` |

### 剧本二 · 拒绝与抱怨

| 项目 | 内容 |
|---|---|
| 玩家输入 | `去种月亮`、`把西边那块地收了`、四次单步农活、`你怎么不去干活` |
| 拒绝 | 品种不在白名单：「月亮」可种不出来，地里只能种萝卜、小麦；A1 还没熟 |
| 抱怨 | 阿芽连做四步后体力 52%（人设阈值 55%）进入休息，回答「我累了在歇着：体力只剩 52%…」 |
| 状态 | 两次拒绝都没有产生工单、没有改地块 / 时钟 / 三本账；抱怨是追问，只读 |
| 证据 | `TestResults/scenarios/refusal-run-{1,2,3}.trace` |

### 剧本三 · 记忆体现

| 项目 | 内容 |
|---|---|
| 玩家输入 | 第 1 天 `给西边的地浇水`；第 2 天 `上次你让我浇的是西边那块吗` |
| 回答 | 「对，第 1 天你让我给西边（A1）浇水。现在待命，体力 100%。」 |
| 面板 | 右侧决策抽屉展示命中的两条记忆（权重 0.36 / 0.22）与当前意图 |
| 状态 | 追问不产生新工单、不改时钟与三本账 |
| 证据 | `TestResults/scenarios/memory-run-{1,2,3}.trace` |

### 「三次一致」怎么证明

1. `ElysFarm.EditorTools.ScenarioEvidence.Generate()` 用领域跑生成
   `TestResults/scenarios/reference/<剧本>.trace`。
2. `scripts/run-scenarios.ps1` 启动交付包，每条剧本连续跑三次，每次写
   `full-flow-run-<n>.trace` 等。
3. 脚本用严格 UTF-8 解码后逐字比对：三次之间逐字相同，且与领域参考逐字相同。
4. 领域侧另有 `ScenarioTests.三条剧本连续跑三次_证据逐字一致` 兜底，不依赖构建。

本次交付包的结果：full-flow 3/3、refusal 3/3、memory 3/3，三条都与领域参考逐字一致
（耗时分别为 272.4 / 133.7 / 158.9 秒）；`TestResults/scenarios/summary.md` 是脚本生成的汇总。

证据文本只记可观察结局：游戏日、地块状态与阶段、三本账、每日补货累计（`restocks=`）、
按顺序执行过的计划工步、是否在休息、还挂着的工单。不记墙钟时间、帧率、浮点生长值，
所以同一份台本必须逐字相同。

## 录屏

- 文件：[video/elys-demo.mp4](video/elys-demo.mp4)，1280×720，20fps，H.264（yuv420p），无音轨；
  166.6 秒（2:47），5.0 MB。
- 章节（同目录 `elys-demo.chapters.txt`）：`00:00` 剧本一全流程 · `01:22` 剧本二拒绝与抱怨 · `02:00` 剧本三记忆体现。
- 采集方式：游戏内 `ScreenCapture.CaptureScreenshotAsTexture()` 逐帧抓后缓冲，再由 ffmpeg 编码。
  不是抓桌面窗口，所以不受窗口位置、125% DPI 缩放、标题栏或遮挡影响；画面里也没有鼠标指针。
- 回放期间仿真按固定步长（1/60）推进，遇到字幕会暂停领域但照常刷新画面；录制掉帧只会让
  成片变慢，不会改变第几天、库存或台词。三条剧本各跑一次，编码后用 ffmpeg concat 拼接。
- 复现：`scripts/record-demo.ps1`（需要 ffmpeg/ffprobe 在 PATH 里；会先核对参考证据）。

## 压测

负载是最重的常规演示姿态：三名 NPC 自主决策、16x 时间、决策可视化抽屉常开。
关闭垂直同步、`targetFrameRate = -1`，采 60 秒未缩放帧间隔（前 5 秒预热）。

| 分辨率 | 采样帧数 | 平均 FPS | p50 | p99 | 最低 | p99 帧时 | 低于 30fps | 低于 60fps |
|---|---|---|---|---|---|---|---|---|
| 1280×720（交付） | 22,065 | 367.7 | 396.0 | 160.3 | 26.4 | 6.24 ms | 2 | 11 |
| 1920×1080 | 16,951 | 282.5 | 336.7 | 99.6 | 3.6 | 10.05 ms | 31 | 72 |

结论：

- 两个分辨率的 p99 都高于 60fps（720p 160fps、1080p 100fps），没有持续瓶颈。
- 720p 只有 2/22,065 帧（0.009%）低于 30fps；1080p 有 31/16,951 帧（0.183%）、最低 3.6fps。
  按「偶发卡顿」如实记录，不宣称完全无卡顿，也没有为压测降画质或关 HUD。
- 同一台机器重复采样会波动：先前一次 720p 得到平均 522fps / p99 241fps，本次平均 368fps / p99 160fps；
  压测机同时跑着 DSH 与浏览器，报告保留本次原始 JSON，不用最好的一次冒充稳定值。
- 机器是 RTX 4060 Laptop / 22 逻辑处理器；集成显卡或更老的机器应按 p99 帧时与掉帧数重新评估。
- 完整报告：`TestResults/perf/perf-report.md`；复现：`scripts/perf-test.ps1`。

## 包与干净环境

包由 `Assets/Editor/FarmBuild.cs` 固定只打 `Assets/Scenes/FarmDemo.unity`，启动就是农场演示。
最终包从干净提交构建（`provenance` 的 `source.dirty = false`），压缩包与 SHA256 记录在
`Builds/Elys-Windows.sha256` / `.manifest.json`。

`scripts/verify-clean-environment.ps1` 做的验收：

1. 把整个 `Builds/Windows` 拷贝到 `%TEMP%\elys-clean-<guid>`，不在仓库里运行。
2. 清空子进程环境：不带 `DEEPSEEK_*`、`SILICONFLOW_*`、`ELYS_SECRETS_FILE`；PATH 只留
   `C:\Windows\system32;C:\Windows`；用户目录指向临时 profile。
3. 跑 `Elys.exe -elysScenario memory -elysQuit 1`，核对 trace 与领域参考逐字一致、
   `Player.log` 有 `[Elys]` 启动日志且没有异常、窗口截图非空。

这不是全新 Windows 虚拟机，而是同一台机器上的隔离运行。它能排除「必须装 Unity / 必须在仓库里 /
必须带密钥」这三类依赖；显卡驱动缺失、杀毒拦截等环境差异不在覆盖范围内。报告里如实写了这一点。

本次结果（full-flow 剧本）：退出码 0；trace 与领域参考逐字一致；`Player.log` 有 2 行 `[Elys]`
日志、0 条异常；窗口截图已保存（1304×784，带窗口边框），截图与报告都在 `TestResults/clean-environment/`。

## 取舍与已知边界

- **无音轨**：录屏是画面证据，游戏本身没有音效（见 README「已知边界」）。
- **回放是演示模式**：离线路由、固定步长、不打开自主；正常游玩仍是可变帧率、可接模型、三人自主。
  回放只换时间来源与路由，不改领域规则。**录屏刻意不接模型**：模型输出会破坏「三次逐字一致」，
  模型在线与超时降级由正常游玩和既有 `IntentRouterTests.*` 覆盖，不在录屏里。
- **录制会降速**：逐帧 PNG 编码会拖慢真实帧率，成片按 20fps 固定输出；因为等待是状态驱动，
  内容不受影响。压测不打录制，单独测默认画面。
- **1080p 偶发卡顿**：4 帧低于 30fps，已在报告里记录；交付分辨率没有。
- **干净环境**是同机隔离，不是全新机器；报告里写明了边界。
- 三条剧本 ×3 次约 10 分钟，压测约 3 分钟，录制约 4 分钟；这些都是交付验收脚本，不在 EditMode 套件里。

## 本次改动

- `Game/Assets/Scripts/Demo/Scenarios/`：剧本台本、领域跑、证据文本、回放、逐帧录制、压测探针、
  命令行开关、共享开局世界。
- `Game/Assets/Scripts/Demo/FarmDemoBootstrap.cs`：固定步长 / 暂停 / 字幕 / 离线路由 / 后台运行；
  开局世界改为从 `FarmDemoWorld` 取。
- `Game/Assets/Scripts/Demo/FarmDemoHud.cs`：字幕横幅与抽屉开关。
- `Game/Assets/Tests/EditMode/ScenarioTests.cs`：三条剧本的结局与三次一致。
- `Game/Assets/Editor/ScenarioEvidence.cs`：生成领域参考证据。
- `scripts/run-scenarios.ps1`、`record-demo.ps1`、`perf-test.ps1`、`verify-clean-environment.ps1`、
  `package-windows.ps1`、`focus-game-window.ps1`：交付验收脚本。
- `README.md`、`GLOSSARY.md`、`docs/agents/unity-pitfalls.md` 与本文件：交付说明与踩坑记录。
