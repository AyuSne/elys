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
- `unity test` 成功时通常**没有 stdout**，报告默认写到**当前工作目录**的 `test-results.xml`（不是 `Game/`）。读报告要严格按 UTF-8 解码；PowerShell 的 `[xml](Get-Content ...)` 遇到中文测试名会报「意外标记」：

  ```powershell
  $text = [IO.File]::ReadAllText((Resolve-Path test-results.xml), [Text.UTF8Encoding]::new($false,$true))
  [xml]$x = $text
  $x.'test-run' | Select-Object total,passed,failed
  ```

- 需要 `await` 的 EditMode 测试直接写 `[Test] public async Task ...`，不需要 `[UnityTest]` 协程。用 `Task.WhenAny` + 很小的 deadline 测超时，别用 `.Result` / `.Wait()` 阻塞主线程。
- 测试程序集 `overrideReferences: true` 时，引用新 asmdef（例如 `ElysFarm.Model`）要显式加进 `references`；想在测试里直接用 Newtonsoft 还要加 `precompiledReferences`，否则编译不到。
- 全套 51 个测试本身不到 0.1 秒，主要时间花在打开工程与编译；不要用测试跑代替编译检查，也别在没改代码时反复跑全套。

## 驱动 Editor 与验证演示

- 打开：`unity open Game`；等待：`unity status --until-ready --project-path D:\Elys\Game`。
- **Play 前先切场景**：`unity command scene open -- --path Assets/Scenes/FarmDemo.unity`。Editor 会恢复上次打开的场景（常是 `SampleScene`），不是 Build Settings 的第一项；不切场景会看到空场景或默认相机。
- 无焦点时 Play Mode 可能冻在第 1 帧。先 `unity command editor set_auto_tick` 与 `editor.playmode set_run_in_background`，进入后用 `capture` 或 `script eval` 确认场景对象 / `Time.frameCount` 在变。
- `job wait --condition` 需要一段 JSON，PowerShell 会吞掉内联引号（本机报 `WaitConditionInput` 解析失败）；不要照抄带引号的示例，改用上面的 `capture` / `script eval` 探测。
- HUD 是 IMGUI：截图用 `capture game_view -- --source screen --output-path Screenshots/xxx.png`；`--source camera` 不含 IMGUI，锁屏时还可能得到纯色图。
- 结束时 `editor.playmode stop`，把 `set_run_in_background` / `set_auto_tick` 关掉，再 `editor quit`。
- **`script eval` 的引号很脆。** 代码里带 `|`、双引号或多个语句时，PowerShell 容易拆坏参数，报 `Bad Request. Give either code or file, not both`。把代码写到 `Game/Temp/<name>.cs`（`Temp/` 被 gitignore），用 `--file Temp/<name>.cs`。
- **Editor 不一定自动重编译。** 改完 `.cs` 后 Play 里仍是旧行为、`script eval` 反射不到新方法时，执行 `unity command script recompile`，轮询 `script recompile_status` 到 `status: completed`（不是 `idle`），再重开 Play；并用反射确认新程序集已加载。
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
- `response_format: json_object` 失败时 DeepSeek 会把内容替换成**空格**、`finish_reason` 仍是 `stop`；客户端必须把空白内容当失败并回退，不能当合法 JSON 继续解析。
- 不点名地块时不要退回「最近的地块」，要交给效用打分挑（`IntentRouter.TryResolveCommandField`）；否则「给地浇水」会选中刚浇过的近地，违背「指令进入效用打分」。
- 种植类工单在等待成熟时必须允许插做维护（`FarmWorld.AdvanceWorkOrder` 的 `NextMaintenanceStep`），否则小麦这类 7 天作物会因为只浇一次水而永远等不到收获。测试要断言五个计划工步的顺序，维护步不计入工单进度。
- 模型 HTTP 客户端用自己的 `UnityWebRequest`（ADR-0003）。`thinking` / `reasoning_effort` 是请求体顶层字段，不要套 OpenAI SDK 的 `extra_body`。
- 带中文注释的请求/响应字段用 `Newtonsoft.Json` 解析；`com.unity.nuget.newtonsoft-json` 已加入 manifest，测试程序集若 `overrideReferences: true` 则仍需显式引用。

## 演示与 HUD

- Game 视图实际是 1175×547 时，`capture` 输出 1280×720；按 720 摆 HUD 会重叠。布局要按 `Screen.width/height` 算，并在 547 高度下截图验收。
- 左侧状态面板与右侧决策面板在 547 高度下各只有约 326 逻辑像素。`GUILayout.Label` 不设宽度会换行，直接把面板撑到下方日志区；三块地并排成一行、账本压成一行能省出 60+ 像素。相机也要避开底部 HUD：本票把机位从 z=-10 退到 z=-16，NPC 和草垛才不会被日志面板挡住。
- NPC 一开局就自主干活会抢在玩家输入之前把地种掉，一句话指挥的演示看起来像被拒绝。演示默认让 NPC **待命**，玩家第一句话被接受后才恢复自主；HUD 另有「开始自主/暂停自主」。
- IMGUI 输入框与按钮走同一套提交路径；`UnityEngine.Input` 在本工程不可用（activeInputHandler=1），但 IMGUI 事件不受影响。
- `Game/Temp/` 会被 Unity 启动时清空；`script eval --file` 的临时文件要在 Editor 打开后重写。

## 评审与 Git

- 评审前先提交或冻结工作树。子代理读的是工作树；边改边审会看到中间状态（例如编码损坏的临时文件）。第二轮评审前也要先把上一轮的修复提交，否则报告会反复带「worktree is dirty / uncommitted」的 caveat，结论不可信。
- **两轴评审可能互相矛盾。** 本票遇到过：Standards 轴说 embedding、距离硬门槛是「规格没要求的超范围」，Spec 轴说删掉就违背规格。不要试图同时满足两边；把范围决定写进 `docs/spec` 或 ADR（本票在 `docs/spec/ai-farm-npc.md` 加了「范围注记」），再在最终说明里引用这个决定。
- 评审意见分两类：违背文档/术语的硬项必须改；架构建议可以记录理由后不改（例如演示引导器保持单文件、不为两个浮点数提前引入类型，虽然最后仍抽了 `FarmPosition`）。
- `git rm` 之后不要再 `git add` 那个已不存在的路径：整条 `git add` 会失败，而之前已暂存的删除仍会被提交。用 `git add -A <dir>`，提交后 `git show --stat HEAD` 复核。
- `.gitignore` 里用户自己的未提交修改不要顺手带进提交；`Screenshots/`、`Temp/`、`Logs/` 都在忽略列表里，截图证据要单独说明路径。`unity test` 生成的报告不入库（#4 决定：`TestResults/` 整体忽略；把生成物塞进受版本控制的路径，会让「跑完测试再打包」永远撞上脏工作树保护）。
- `gh issue close` 成功时会把「✓ Closed issue ...」写到 **stderr**，PowerShell 会显示成 `NativeCommandError`，但 issue 其实已经关掉；用 `gh issue view <n> --json state` 复核，不要按退出码重试。
- `gh issue list --json ... --jq` 的表达式含括号、管道或中文时容易被 PowerShell 拆成多个参数；在 PowerShell 里 `ConvertFrom-Json` 后再过滤，或只给 `--jq` 一个不含空格/管道的简单表达式。
- `unity package` 命令在本机 CLI 不存在；加 UPM 包走 `Game/Packages/manifest.json`（或 Pipeline 的 package 命令），改完让 Unity 重新打开工程解析，`packages-lock.json` 的 `depth` 会跟着变。

## 交付与打包（#4）

- 跑测试：`unity test Game --mode EditMode --output TestResults/editmode.xml`，`--output` 会直接覆盖写报告。`scripts/run-tests.ps1` 额外把三个验收项（五个工步完整 / 背包守恒 / 时间推进）的结果打印出来。测试名用「工步」，不用词汇表里的 Avoid 词「步骤」。
- 打包：`unity build Game --target StandaloneWindows64 --execute-method ElysFarm.EditorTools.FarmBuild.BuildWindows --output-path <绝对路径>\Elys.exe`。CLI 把路径作为 `-buildOutput` 传给 Editor，execute-method 必须自己读这个参数。`scripts/build-windows.ps1` 默认带 `--allow-dirty-build`（`-RequireClean` 可强制干净）：打包本身会弄脏工程文件，第二次打包时 `unity build` 的干净树检查几乎必然失败。
- **打包会顺手改工程文件**：`PC_RPAsset.asset` 的 shader prefilter、`UniversalRenderPipelineGlobalSettings.asset` 的 `m_RuntimeSettings`、`ProjectSettings.asset` 的 `m_BuildTargetBatching`。这些是构建副产物，提交前 `git checkout --` 还原；别把它们混进功能提交。
- 运行时 `GameObject.CreatePrimitive` + `Shader.Find("Universal Render Pipeline/Lit")` 的材质在包里可能找不到 shader（方块变粉/不可见）。URP Lit 的 GUID 已加进 `GraphicsSettings.asset` 的 Always Included Shaders；升级 URP 版本后要复核这个 GUID。
- 构建的密钥扫描只按**确切文件名或 `.env` 后缀**判断（`.env`、`.env.local`、`.secrets.local.json`、`secrets.local.json`、`secrets.json`、`*.env`）。第一版按「文件名包含 secret」扫，直接把 `SecretFileLocator.cs` 拦成了密钥文件。`Assets/Editor/BuildSecrets.cs`、`scripts/build-windows.ps1`、`.gitignore` 三处规则要同步改。
- 构建产物的 `Elys.provenance.json` 记录构建时的提交与 `dirty` 标记：从干净提交构建用 `build-windows.ps1 -RequireClean`（不要给 CLI 加 `--allow-dirty-build`），否则包对不上任何提交，评审时说不清。
- 报告路径：`scripts/run-tests.ps1` 写 `TestResults/editmode.xml`；README 里的 `cd Game; unity test` 会写 `Game/test-results.xml`。两者都在 `.gitignore` 里，别把生成物放进受版本控制的路径，否则「跑完测试再打包」会撞干净树保护。
- 构建配置不写成 EditMode 用例：`FarmBuild` 只把 `FarmDemo.unity` 传给 `BuildPlayer`，包必然从演示场景启动；场景/窗口/HUD 的验证放冒烟脚本，避免为了打包再开一条测试接缝。
- 抓玩家窗口截图：PowerShell 进程要先 `SetProcessDPIAware()`，否则 125% 缩放下 `GetWindowRect` 返回 1038×614、截图右下角被裁。最小化窗口的 rect 是 `(-32000,-32000)`，抓图前 `ShowWindow(hwnd, 9)` 还原，并校验宽高。
- 玩家日志在 `%USERPROFILE%\AppData\LocalLow\Elys\Elys\Player.log`（跟着 companyName / productName 变）。冒烟脚本用它确认引导器跑过、没有异常。
- 冒烟截图时游戏窗口会弹到桌面上，可能被人手最小化或关掉；脚本要能自己还原窗口，失败信息要说清是「窗口被最小化」而不是笼统的「截图失败」。
- PowerShell 5.1 读无 BOM 的 `.ps1` 会按 GBK 解中文，脚本必须存 **UTF-8 带 BOM**；`.cs` 仍是无 BOM UTF-8。
