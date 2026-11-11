# Unity 项目工作笔记（Elys）

这一页只记实际踩过的坑，给新会话省时间。先读 `AGENTS.md`；动 `Game/` 之前读这里。

## 文件与编码

- **不要用 PowerShell 改写带中文注释的 `.cs`。** 本机 `Get-Content -Raw` / `Set-Content` 会把 UTF-8 当 GBK 处理，写回的文件可能变成非法 UTF-8 或乱码；Unity 仍然能编译，肉眼看编辑器也不一定发现。用 `write` / `edit` 工具，或 `git checkout -- <file>` 恢复。
- 校验编码要严格解码，不要凭显示判断：

  ```powershell
  [System.Text.UTF8Encoding]::new($false,$true).GetString([IO.File]::ReadAllBytes($path))
  ```

- `.meta` 由 Unity 首次导入生成，提交资源时连 `.meta` 一起提交；`.asmdef` 里的引用可以直接写程序集名。

## 跑测试

- 编辑模式测试不需要开 Editor：`unity test Game --mode EditMode --timeout 900`。Editor 开着（尤其 Play Mode）时 `unity test` 会拒绝。
- 退出码 6 + `TEST_RUN_ERROR` 通常只是编译失败；真正的 `error CSxxxx` 在 `%LOCALAPPDATA%\Unity\Editor\Editor.log`，CLI 也可能只打印「编辑器日志报告了以下错误」。JSON 里没有编译错误详情。
- 测试引用尚不存在的 API 会让整个测试程序集编译失败。一次只加一个纵向切片；红灯先看是「编译错」还是「断言错」。
- 全套 25 个测试本身不到 0.1 秒，主要时间花在打开工程与编译；不要用测试跑代替编译检查，也别在没改代码时反复跑全套。

## 驱动 Editor 与验证演示

- 打开：`unity open Game`；等待：`unity status --until-ready --project-path D:\Elys\Game`。
- **Play 前先切场景**：`unity command scene open -- --path Assets/Scenes/FarmDemo.unity`。Editor 会恢复上次打开的场景（常是 `SampleScene`），不是 Build Settings 的第一项；不切场景会看到空场景或默认相机。
- 无焦点时 Play Mode 可能冻在第 1 帧。先 `unity command editor set_auto_tick` 与 `editor.playmode set_run_in_background`，进入后按 `job wait` 的 `Time.frameCount` 条件确认帧在动：

  ```powershell
  unity command job wait -- --condition '{"member":"UnityEngine.Time.frameCount","op":"changed"}' --timeout 10
  ```

- HUD 是 IMGUI：截图用 `capture game_view -- --source screen --output-path Screenshots/xxx.png`；`--source camera` 不含 IMGUI，锁屏时还可能得到纯色图。
- 结束时 `editor.playmode stop`，把 `set_run_in_background` / `set_auto_tick` 关掉，再 `editor quit`。
- **`script eval` 的引号很脆。** 代码里带 `|`、双引号或多个语句时，PowerShell 容易拆坏参数，报 `Bad Request. Give either code or file, not both`。把代码写到 `Game/Temp/<name>.cs`（`Temp/` 被 gitignore），用 `--file Temp/<name>.cs`。
- `capture` / `scene open` / `--file` 的路径都相对于 **Unity 工程根目录**（`Game/`），不是仓库根目录。

## 场景与表现

- 不要手改 `.unity` YAML。用 `Assets/Editor/FarmSceneBuilder.cs` 的 `[MenuItem]` 生成：

  ```powershell
  unity run Game --editor-version 6000.3.10f1 -- -executeMethod ElysFarm.EditorTools.FarmSceneBuilder.Build
  ```

  生成后提交场景与 `.meta`。
- 场景相机与运行时 `FarmDemoBootstrap.ApplyCameraPose` 共用同一方法；改了机位要重新生成场景，否则提交的场景与运行时不一致。
- URP 里 `GameObject.CreatePrimitive` 的默认材质属于内置管线（粉色）。运行时建块必须显式赋 `Universal Render Pipeline/Lit`。
- 本工程 `activeInputHandler: 1`（只启用新 Input System），`Input.GetKeyDown` 会在运行时抛异常。演示输入用 IMGUI 按钮或新 Input System。
- 新建场景会生成 `ProjectSettings/SceneTemplateSettings.json`；不属于本次功能，别顺手提交。

## 领域与规格

- 先读 `GLOSSARY.md` 再命名。`仓库` / `carry` 都是明文 Avoid 的词；本票因此做了一次大范围重命名（产出仓 / 随身 / held）。
- 规格「收获进产出仓」与「随身只在产出仓移交」存在歧义。本票的选择是收获直接进产出仓，随身移交只在 `FarmWorld` API 层测试；演示场景里随身为 0。要加送货/出售任务前，先在 issue 里把语义定死。
- 验收测试要断言规格规则本身，不要只设宽松阈值。例：「不浇水不长」应断言 `Growth == 0`；播种时也别白送水分，否则测试过了但规则没实现。
- 跨日结算在成熟后仍要结算水/草/肥，只冻结生长；AC 测试抓不到这一点，要单独写测试。
- `ALIGNMENT.md` §四把决策可视化限定为五项。临时演示 HUD 不是 `DecisionInspector`，已在 §四加注；不要把两者混在一起评。
- `FarmClock.Speed` 只接受 1x/4x/16x。调试时想大幅加速，用 `Time.timeScale`，不要改时钟档位。

## 模型与意图

- 一句话指挥要区分「模型解析」与「确定性校验」。品种白名单必须在模型之前用本地规则拦一次：模型会把「去种月亮」改写成一条合法农活，只有本地白名单能稳定拒绝。
- 模型失败不是异常路径，是常规路径。`IntentRouter` 对超时、断网、空 JSON、非法出口一律回退本地解析；测试用假 `IModelClient`，不联网。
- 本机实测 `deepseek-flash` 的意图解析（thinking=low + JSON）稳定在 1–3 秒；偶发一次失败后重试即成功，因此降级路径必须真的可用，不能只写在文档里。
- Play 模式下 `UnityWebRequest` 偶发 `Unable to complete SSL connection`（HTTP 0），而同一客户端在 Editor 的 `script eval` 里可以成功；这是网络/SSL 的偶发失败，不是代码 bug，自动回退本地模板即可。HUD 会显示「调用失败，已回退本地模板」。
- 农事指令按规划级 15 秒、追问/闲聊按对话级 6 秒硬超时；两者都异步等待，超时或网络错误立即回退本地模板，游戏循环不暂停。`IModelClient` 现有接口只做对话补全，embedding 留给记忆票。
- 不点名地块时不要退回「最近的地块」，要交给效用打分挑（`IntentRouter.TryResolveCommandField`）；否则「给地浇水」会选中刚浇过的近地，违背「指令进入效用打分」。
- 种植类工单在等待成熟时必须允许插做维护（`FarmWorld.AdvanceWorkOrder` 的 `NextMaintenanceStep`），否则小麦这类 7 天作物会因为只浇一次水而永远等不到收获。测试要断言五个计划工步的顺序，维护步不计入工单进度。
- 模型 HTTP 客户端用自己的 `UnityWebRequest`（ADR-0003）。`thinking` / `reasoning_effort` 是请求体顶层字段，不要套 OpenAI SDK 的 `extra_body`。
- 带中文注释的请求/响应字段用 `Newtonsoft.Json` 解析；`com.unity.nuget.newtonsoft-json` 已加入 manifest，测试程序集若 `overrideReferences: true` 则仍需显式引用。

## 演示与 HUD

- Game 视图实际是 1175×547 时，`capture` 输出 1280×720；按 720 摆 HUD 会重叠。布局要按 `Screen.width/height` 算，并在 547 高度下截图验收。
- NPC 一开局就自主干活会抢在玩家输入之前把地种掉，一句话指挥的演示看起来像被拒绝。演示默认让 NPC **待命**，玩家第一句话被接受后才恢复自主；HUD 另有「开始自主/暂停自主」。
- IMGUI 输入框与按钮走同一套提交路径；`UnityEngine.Input` 在本工程不可用（activeInputHandler=1），但 IMGUI 事件不受影响。
- `Game/Temp/` 会被 Unity 启动时清空；`script eval --file` 的临时文件要在 Editor 打开后重写。

## 评审与 Git

- 评审前先提交或冻结工作树。子代理读的是工作树；边改边审会看到中间状态（例如编码损坏的临时文件）。
- 评审意见分两类：违背文档/术语的硬项必须改；架构建议可以记录理由后不改（例如演示引导器保持单文件、不为两个浮点数提前引入类型，虽然最后仍抽了 `FarmPosition`）。
- `git rm` 之后不要再 `git add` 那个已不存在的路径：整条 `git add` 会失败，而之前已暂存的删除仍会被提交。用 `git add -A <dir>`，提交后 `git show --stat HEAD` 复核。
- `.gitignore` 里用户自己的未提交修改不要顺手带进提交；`Screenshots/`、`Temp/`、`Logs/` 都在忽略列表里，截图证据要单独说明路径。
