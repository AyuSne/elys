# Unity 项目工作笔记（Elys）

这一页只记实际踩过的坑，给新会话省时间。先读 `AGENTS.md`；动 `Game/` 之前读这里。

## 文件与编码

- **不要用 PowerShell 改写带中文注释的 `.cs`。** 本机 `Get-Content -Raw` / `Set-Content` 会把 UTF-8 当 GBK 处理，写回的文件可能变成非法 UTF-8 或乱码；Unity 仍然能编译，肉眼看编辑器也不一定发现。用 `write` / `edit` 工具，或 `git checkout -- <file>` 恢复。**哪怕只是 `-replace 'beat.Number','beat.Target'` 这种纯 ASCII 替换也会整文件重编码**：#10 就把 `FixedScenarios.cs` 的中文写成了乱码，只能 `git checkout` 后逐处 `edit`。`edit` 工具还会去掉 `.ps1` 的 BOM，改完脚本要按上一节重新补 BOM。
- 校验编码要严格解码，不要凭显示判断：

  ```powershell
  [System.Text.UTF8Encoding]::new($false,$true).GetString([IO.File]::ReadAllBytes($path))
  ```

- `.meta` 由 Unity 首次导入生成，提交资源时连 `.meta` 一起提交；`.asmdef` 里的引用可以直接写程序集名。

## 跑测试

- 编辑模式测试不需要开 Editor：`unity test Game --mode EditMode --timeout 900`。Editor 开着（尤其 Play Mode）时 `unity test` 会拒绝。
- 退出码 6 + `TEST_RUN_ERROR` 通常只是编译失败；真正的 `error CSxxxx` 在 `%LOCALAPPDATA%\Unity\Editor\Editor.log`，CLI 也可能只打印「编辑器日志报告了以下错误」。JSON 里没有编译错误详情。
- **编译失败时 `TestResults/editmode.xml` 不会被覆盖**，留在那里的是上一次的成功报告。看到 CLI 说「Scripts have compiler errors」就不要再读报告，否则会把上一次的报告（例如 170/170）当成这一次通过。隔壁 `Editor.log` 不是合法 UTF-8，严格解码会抛 `DecoderFallbackException`；用 `[Text.Encoding]::GetEncoding(28591)`（Latin1）读，再过滤 `error CS\d+` 即可（这些行是 ASCII）。
- 测试引用尚不存在的 API 会让整个测试程序集编译失败。一次只加一个纵向切片；红灯先看是「编译错」还是「断言错」。
- `unity test` 成功时通常**没有 stdout**，报告默认写到**当前工作目录**的 `test-results.xml`（不是 `Game/`）。读报告要严格按 UTF-8 解码；PowerShell 的 `[xml](Get-Content ...)` 遇到中文测试名会报「意外标记」：

  ```powershell
  $text = [IO.File]::ReadAllText((Resolve-Path test-results.xml), [Text.UTF8Encoding]::new($false,$true))
  [xml]$x = $text
  $x.'test-run' | Select-Object total,passed,failed
  ```

- 需要 `await` 的 EditMode 测试直接写 `[Test] public async Task ...`，不需要 `[UnityTest]` 协程。用 `Task.WhenAny` + 很小的 deadline 测超时，别用 `.Result` / `.Wait()` 阻塞主线程。
- 测试程序集 `overrideReferences: true` 时，引用新 asmdef（例如 `ElysFarm.Model`）要显式加进 `references`；想在测试里直接用 Newtonsoft 还要加 `precompiledReferences`，否则编译不到。
- 全套测试本身不到 0.5 秒，主要时间花在打开工程与编译；不要用测试跑代替编译检查，也别在没改代码时反复跑全套。
- #9 的表现层测试在 `ElysFarm.Tests` 里引用 `ElysFarm.Demo`；`overrideReferences: true` 时只在 `references` 里加程序集名就够，不需要 `precompiledReferences`。2D 的纯逻辑测试（`CameraRig`、`CropStages`、`PixelArt`、`FarmLayout`、`SortingOrder`、`FarmStartup`）同样只引这个程序集。
- 2D 的素材测试不 `Resources.Load` 真实 PNG；切格与四方向都用纯函数断言，素材本身由 Play Mode 截图验收。3D 版本（`main` 合并前的历史）仍用 `Resources/Quaternius` 与 `ArtMaterialTests`，2D 分支已删除这组测试并移出 `Resources/Quaternius`。`AssetLayoutTests` 是 #16 的打包护栏：它只检查 `Assets/Resources` 的文件布局（没有 FBX/材质、PixelArt 目录仍在），不加载贴图；移动素材目录会让它红，这是有意为之。跑 `unity command test run --mode editor` 比关掉 Editor 再开快得多，TDD 循环用前者。

## 驱动 Editor 与验证演示

- 打开：`unity open Game`；等待：`unity status --until-ready --project-path D:\Elys\Game`。
- **Play 前先切场景**：`unity command scene open -- --path Assets/Scenes/FarmDemo.unity`。Editor 会恢复上次打开的场景（常是 `SampleScene`），不是 Build Settings 的第一项；不切场景会看到空场景或默认相机。
- 无焦点时 Play Mode 可能冻在第 1 帧。先 `unity command editor set_auto_tick` 与 `editor.playmode set_run_in_background`，进入后用 `capture` 或 `script eval` 确认场景对象 / `Time.frameCount` 在变。
- `job wait --condition` 需要一段 JSON，PowerShell 会吞掉内联引号（本机报 `WaitConditionInput` 解析失败）；不要照抄带引号的示例，改用上面的 `capture` / `script eval` 探测。
- HUD 是 IMGUI：截图用 `capture game_view --source screen --output-path Screenshots/xxx.png`（本机 CLI 不接受 `--` 分隔符，加了会把参数当命令帮助打印）。`--source camera` 只渲染相机，锁屏时还可能得到纯色图。
- 结束时 `editor.playmode stop`，把 `set_run_in_background` / `set_auto_tick` 关掉，再 `editor quit`。
- **`script eval` 的引号很脆。** 代码里带 `|`、双引号或多个语句时，PowerShell 容易拆坏参数，报 `Bad Request. Give either code or file, not both`。把代码写到 `Game/Temp/<name>.cs`（`Temp/` 被 gitignore），用 `--file Temp/<name>.cs`。
- **Editor 不一定自动重编译。** 改完 `.cs` 后 Play 里仍是旧行为、`script eval` 反射不到新方法时，执行 `unity command script recompile`，轮询 `script recompile_status` 到 `status: completed`（不是 `idle`），再重开 Play；并用反射确认新程序集已加载。
- **别在 Play Mode 里 `script recompile`。** 域重载会把场景对象重建在帧中间，`FarmDemoBootstrap.Update` 与 `FarmDemoHud.OnGUI` 会刷出成百上千条 `NullReferenceException`（`World` 还是 null），日志看起来像场景崩了。顺序是 `editor.playmode stop` → `script recompile` → `editor.playmode start`。
- 人设参数里不要放行走速度这类纯表现数据：`NpcPersona` 只留影响决策的字段，走路速度由 `FarmDemoBootstrap.WalkSpeedOf` 映射；否则范围注记与实现会打架。
- `capture` / `scene open` / `--file` 的路径都相对于 **Unity 工程根目录**（`Game/`），不是仓库根目录。

## 场景与表现

- 不要手改 `.unity` YAML。用 `Assets/Editor/FarmSceneBuilder.cs` 的 `[MenuItem]` 生成：

  ```powershell
  unity run Game --editor-version 6000.3.10f1 -- -executeMethod ElysFarm.EditorTools.FarmSceneBuilder.Build
  ```

  生成后提交场景与 `.meta`。
- 场景相机与运行时共用 `DemoCamera.ApplyOverheadTo`（场景生成器经 `FarmDemoBootstrap.ApplyCameraPose` 调它，位置来自 `FarmLayout.OverheadCenter`、倍率来自 `CameraRig.OverheadZoom`）；改了机位要重新生成场景，否则提交的场景与运行时不一致。
- 体块回退用 `Universal Render Pipeline/Unlit`（`DemoMaterials.BlockShaderName`），不参与光照；URP 里 `GameObject.CreatePrimitive` 的默认材质属于内置管线（粉色），必须显式赋 shader。
- 本工程 `activeInputHandler: 1`（只启用新 Input System），`Input.GetKeyDown` 会在运行时抛异常。演示输入用 IMGUI 按钮或新 Input System。
- 新建场景会生成 `ProjectSettings/SceneTemplateSettings.json`；不属于本次功能，别顺手提交。
- **以下四条是 `main` 上 3D 版本（Quaternius FBX）的坑；2D 分支已不用 FBX，3D 素材也从 `Assets/Resources` 移出（#16），保留作历史参考。**
- **Quaternius 的 FBX 自带 Blender 场景的相机与灯。** 不关掉的话，每实例化一个模型就多一个 `depth=0`、`clear=Skybox` 的相机，Game 视图会变成纯背景色（HUD 却照常刷新，很容易误判成相机或 URP 坏了）。在 `AssetPostprocessor.OnPreprocessModel` 里设 `importCameras = false`、`importLights = false`，并在 `ModelPlacement.Spawn` 里删一遍兜底；改完要 `AssetDatabase.ImportAsset(..., ForceUpdate)` 重导入全部 FBX。`FindObjectsByType<Camera>()` 里莫名多出 20+ 个 `Camera` 就是这个。
- **Nature pack vol.3 是白模。** 树、灌木、草、石头的材质全是接近白色，靠使用方染色；`ModelPlacement.Tint` 用 MaterialPropertyBlock 给树四种绿、石头灰、草绿。不染会得到一片白色树石。
- **模型 pivot 不统一。** 有的在脚底、有的在中心，有的带 270° X 轴校正（作物/农舍）而角色没有。落位统一用 `ModelPlacement.SnapToGround` 按渲染器世界包围盒水平居中、底部贴地；不要直接 `transform.position = target` 后 `localRotation = identity`（会把作物的 Z-up 校正转掉，作物全躺在地上）。
- **角色动画不需要 AnimatorController。** `Resources.LoadAll<AnimationClip>("Quaternius/Characters")` 能拿到 4 个角色 FBX 里的 44 个 clip（每个 11 个，含 `CharacterArmature|Idle` 与 `|Walk`），用 `AnimationClip.SampleAnimation(root, t)` 按时间采样即可；通用骨骼的路径一致，随便取一个同名 clip 都能驱动另外三个模型。
- **Game 视图只有背景色、没有 3D 时先查相机数量。** 不是截图坏了：FBX 自带的相机（或临时展示台没清掉的 `Showcase Camera`）会以 `depth=0`、`clear=Skybox` 叠在主相机之上。`FindObjectsByType<Camera>()` 超过 1 个就先删；`--source camera` 只渲染主相机，所以能出图，容易误判成截图问题。清掉后 `--source screen` 能同时拿到正确的 HUD 与 3D。
- `--source camera` 的 IMGUI 叠层按渲染分辨率合成，右侧决策面板会被裁或错位（真实 Game 窗口里没有这个问题）；文档截图优先用 `--source screen` 并先 `editor focus`。
- **演示层截图前先清掉临时对象。** `script eval` 里创建的相机/灯如果不 parent 到临时 root 并 `DestroyImmediate(root)`，会留在打开的场景里、被带进 Play（例如作物展示台的 `Showcase Camera` 会盖住主相机）。重建 `FarmDemo.unity` 能清掉。
- 顶部「镜头」条放在左右面板之间的空档（`PanelMargin + LeftPanelWidth` 到 `Screen.width - PanelMargin - InspectorPanelWidth`），547 高度下五个按钮刚好放下；窗口再窄时顶部条会隐藏，用键盘 `C` 俯视、`1/2/3` 跟拍兜底。跟拍时右侧决策面板与流水收起，靠 `demo.IsOverheadCamera` 判断，不要用 `SelectedNpc` 判断跟拍对象（观察对象和跟拍对象可以不同）。
- **像素素材**：`Assets/Resources/PixelArt` 的 PNG 走 `PixelArtImportPostprocessor`（点采样、无压缩、无 mipmap）。切图在 `PixelArt` 里按 `SpriteCell` 运行时创建，别把 PNG 改成 Sprite 资产，否则 `Resources.Load<Texture2D>` 与切格测试都会变。整张就是一件东西的图（农舍、树、草丛）走 `PixelArt.LoadWhole` / `LoadLandmark` / `LoadDecoration`，不要为它们再切格。
- **排序**：`Renderer.sortingOrder` 是 16 位有符号整数；`100000 - y*100` 会回绕成负数，被地面盖住。用 `SortingOrder.At`（`-y*100 + bias`）：地面 Tilemap -10000、土路 -9500、耕地 -9000，作物、角色、地标与装饰按 y。
- **立在地面上的东西统一用底边中点**：地标与装饰的落点、排序基准都是占位底边的中点（`GroundSpot.AnchorOf/SortBaseOf`，`PixelArt.GroundPivot`）。换成中心会让「谁挡谁」和脚底位置一起偏；改占位尺寸或换图数量时只动 `FarmLayout.ShapeOf` 一处，测试按 `TileSizeOf`/`VariantCountOf` 走。
- **素材开关下的两套语义**：地标（农舍、鸡舍、水井、产出仓）在素材缺失或关掉时退回**同尺寸体块**——房子、井、木桶的位置与体量还在，动线照样读得出来；装饰（树、树桩、草丛）整件跳过，因为它只压画面边缘、不参与玩法。别把地标也做成「跳过」，体块版本会丢掉整张农场。
- **作物图集**：每个品种是一条 8 格横条；行号是 `rowInSpriteSheet / 2`，奇数行占右半边（列再 +8）。成熟格因品种不同：萝卜第 6 格、小麦第 5 格；最后一格常是空的（留茬再生用），别把 7 当成熟。
- **表情图集**：`Emotes/emotes.png` 是 64×256，4 列是弹出动画的四帧、每行一种表情。行号按原作 emote id 除以 4：感叹号 4、恼火 3、睡觉 6、省略号 10；别按「从 0 数第几张脸」猜。16px 一格按 2× 放大到 32（`NpcEmote.Scale`），头顶气泡会占 2 世界单位——和 16×32 的角色一样高，这是规格要的显眼程度。
- **头顶表情的可见性**：`NpcEmote.LateUpdate` 用 `Camera.main` 的正交半高 × aspect 算视野，纯函数 `OverheadEmotes.IsVisible` 判四边与相机背后。别用 `renderer.isVisible`（它要等一次渲染才更新），也别在 `Update` 里判（相机在 LateUpdate 才移动）。排序用 `SortingOrder.Emote = 30000`，表情不被树/房子挡住。
- **Play 截图**：`capture game_view --source camera --overlay false` 不画 IMGUI，能单独看像素场景；`--source screen` 会叠上旧 HUD，旧 HUD 仍在时别拿它判断「场景没画出来」。
- **`capture` 的取景比 Game 视图窄**：正交半高由 `Screen.height`（547）算，`capture` 却按 1280×720 出图 —— 720 高、48px/格时只看到约 20.3 格宽、11.4 格高，而 Game 视图有约 24.5 格宽。构图照 `farm2d_hud.png` 验收时按截图的 20 格宽摆东西：摆在 x=±11 之外的树只剩一条边。
- **开局三个月阶段**：`FarmStartup.Seed` 用 `Field` 的公开工步与 `AdvanceDay` 往前推（不走时钟），所以开局仍是第 1 天；**播种照实数从 `Ledger` 扣种**，所以 `FarmDemoBootstrap.SetUpWorld` 必须先 `AddSeeds` 再 `Seed`，否则三块地是白种上的、种子库对不上。改肥力常量或生长公式后，`FarmStartupTests` 会先红。三块地因此都是「已经种上」的状态，示范播种要么先收一茬，要么用别的例句（现在的例句是「去东边那块地除草」）。
- **`FarmLayout` 是唯一布局来源**：土路（`RoadTiles`）、地标（`Landmarks`）、装饰（`Decorations`）都在这里，`FarmDressing`/`FarmProps` 只照着摆。新加的东西要过四条测试：不压地块、两两不重叠、地标不压在路上、树与草丛压住画面外圈。树冠可以压路，房子不许——路要整条看得见。

## 领域与规格

- `FarmWorld.Inspect` 是只读快照：不要在里面推进决定、扣体力或写记忆；测试 `读取决策快照不改变任何游戏状态` 会盯住时钟、账本与 NPC 状态。决策可视化的测试仍打 `FarmWorld` 这条缝，不为面板或 `DecisionInspector` 另开接缝。
- 记忆的 `0.5×相似度 + 0.3×importance + 0.2×时间衰减` 在 `MemoryStore`，两段式：先按相似度召回 10 条，再加权取 3 条。相似度有向量时用余弦，没有时退回 bigram Jaccard；两种尺度**不能逐条混排**——只要有一条记忆缺向量或维度不对，整次检索退回关键词。不要把排序第一的候选塞进 cue，否则「意图指向 A2」会被 A1 的候选稀释。追问的 cue 是玩家原话；`Inspect` 在追问后展示 `RecallForAnswer` 缓存，没有缓存才用 `IntentOf`。
- embedding 走 `IModelClient.EmbedAsync`（硅基流动 `/embeddings`，bge-m3 固定 1024 维，`input` 支持数组）。路由器的批量请求把「检索线索 + 最近缺向量的记忆」一起送；硬超时 2 秒，任何失败都退回关键词，不能挡对话。测试用假客户端返回固定向量，不联网；真实端点只用 `script eval` 或手工请求验。实测两条无关中文短句余弦约 0.5，排序看相对差。
- 追问的「依据」是领域行为，不是 HUD 文案：`LocalIntentParser.QueryReply` 按「上次那件事 / 为什么没干活 / 状态 / 在干什么」四类生成，回答必带地块、工步、体力或记忆原话；`QueryFacts.Build` 把这串当成本地事实（既是提示词里的唯一依据，也是回退台词），`QueryFacts.Accepts` 只放行「地块、工步、体力都在、没编出事实里没有的地块或第 N 天、问往事时仍引用那条记忆」的措辞——#20 之后不再由路由器往模型回答后面追加依据，改掉事实就整句作废回退事实串。别把这段搬进演示层，否则离线追问会退化成套话。改 `AppendWhatDoing` 时注意工单分支也要带体力，「你在干什么」的验收靠它。`QueryReply` 与决策可视化都读同一次 `RecallForAnswer`，问往事优先玩家原话（`BestPlayerMemory`）这条规则不能改。
- `FarmWorld.Remember` 会清掉该 NPC 的 `RecallForAnswer` 缓存（记忆排序变了）；`ExecuteStep`、`TryDecide`、跨日也会清。改缓存生命周期时跑 `MemoryTests.决策面板显示最近一次追问命中的记忆` 和 `DecisionInspectorTests.读取决策快照不改变任何游戏状态`。
- 心情与饥饿只供状态条展示与表达层的状态摘要，由工步、休息与跨日结算驱动；不要把它们加进效用打分，否则会改 ADR-0002 的公式和 #5 的测试预期。
- 决策可视化五项是记忆 / 意图 / 候选与否决原因 / 最近一次模型调用 / 状态条；面板只能读快照，不要把 prompt、token 流、工单进度或人设注脚塞进去。
- 先读 `GLOSSARY.md` 再命名。`仓库` / `carry` 都是明文 Avoid 的词；本票因此做了一次大范围重命名（产出仓 / 随身 / held）。
- 三个出口的最终裁决点在 `LocalIntentParser.IsFarmRequest`（#8）：请求一定进动作校验，非请求一定不产生工单；模型只提供细节。改词表后跑 `ChatTests`。#20 起 `IntentRouter.ResolveAsync` 直接拿 `LocalIntentParser.Parse` 的出口与模型分类比：非请求句里追问（带问句标记）与闲聊也由本地定，模型把「你在干什么」判成 chat 会被纠正成 query——否则追问没有事实注入，还会把玩家的话写回记忆。`种子` / `种类` 里的「种」是名词，`FirstActionIndex` 会跳过；新增动作词时要同步 `DetectGoal`，否则会出现「是请求但没有目标」的拒绝。
- 动作词必须是**连写的**才算数：「除草」能识别，「把…的草除了」里没有相邻的动作词，整句会被判成闲聊。演示里的例句照 `ActionWords` 写（「去东边那块地除草」），别用「把…的草除一下」这种拆开的说法。
- 闲聊只走 `FarmWorld.RememberChat`：它一次写「玩家说「…」」记忆并提取传闻。`FarmWorld.TryRetell` 只写听者的记忆（说法原样、来源换人、importance 每转一手减一），不要顺手给讲者补记忆或流水，否则 A→B→C 的断言会被重复条目搅乱。
- 演示层的 NPC 对话每 4 秒触发一次：挑一名不走路、不休息的 NPC，找一名不走路且听得见的同伴；把两人的 `NextActionAt` 往后推 1.2 秒，意图文案才留得住（`UpdateNpc` 在 `NextActionAt` 之前直接返回）。
- 规格「收获进产出仓」与「随身只在产出仓移交」存在歧义。本票的选择是收获直接进产出仓，随身移交只在 `FarmWorld` API 层测试；演示场景里随身为 0。要加送货/出售任务前，先在 issue 里把语义定死。
- 验收测试要断言规格规则本身，不要只设宽松阈值。例：「不浇水不长」应断言 `Growth == 0`；播种时也别白送水分，否则测试过了但规则没实现。
- 跨日结算在成熟后仍要结算水/草/肥，只冻结生长；AC 测试抓不到这一点，要单独写测试。
- `ALIGNMENT.md` §四把决策可视化限定为五项。临时演示 HUD 不是 `DecisionInspector`，已在 §四加注；不要把两者混在一起评。
- `FarmClock.Speed` 只接受 1x/4x/16x。调试时想大幅加速，用 `Time.timeScale`，不要改时钟档位。

## 模型与意图

- 一句话指挥要区分「模型解析」与「确定性校验」。品种白名单必须在模型之前用本地规则拦一次：模型会把「去种月亮」改写成一条合法农活，只有本地白名单能稳定拒绝。
- 模型失败不是异常路径，是常规路径。`IntentRouter` 对超时、断网、空 JSON、非法出口一律回退本地解析；测试用假 `IModelClient`，不联网。
- 本机实测 `deepseek-flash` 的意图解析（thinking=low + JSON）稳定在 1–3 秒；偶发一次失败后重试即成功，因此降级路径必须真的可用，不能只写在文档里。
- Play 模式下 `UnityWebRequest` 偶发 `Unable to complete SSL connection`（HTTP 0），而同一客户端在 Editor 的 `script eval` 里可以成功；这是网络/SSL 的偶发失败，不是代码 bug，自动回退本地模板即可。HUD 会显示「失败 … · 回退模板」。
- 农事指令按规划级 15 秒、追问/闲聊按对话级 6 秒硬超时；两者都异步等待，超时或网络错误立即回退本地模板，游戏循环不暂停。`IModelClient` 现在覆盖对话补全与批量 embedding；`SupportsEmbeddings=false` 时路由器不发向量请求。
- **闲聊与追问都是两次调用**（#19 / #20）：先是分类 JSON（`IntentPrompt`，仍产 importance、仍判「闲聊里夹带的农事请求」；#20 起 chat 与 query 的 `reply` 都留空），分类之后才是表达层（`ExpressionPrompt`，temperature 0.8、max_tokens 200、不开思考、不要求 JSON）。闲聊的表达层空响应 / 返回 JSON（包括引号包住的 JSON）重试一次，仍失败只说 `IntentRouter.ChatFailureLine` 并让 HUD 标「本地模板」；追问的表达层同样重试一次，但失败或改掉事实时回退 `QueryFacts.Reply`（本地事实串），不追加依据、也不拿分类调用的 `reply` 冒充回答。分类调用失败（空 JSON、超时）时仍会试一次表达层——出口已由本地规则定好；因此 `UsedFallback` 只表示「台词来自本地模板 / 事实串」，不是「分类一定失败」。表达层只拿分类用完后的剩余预算（对话级 6 秒），`client.Calls` 要按「分类 1 次 + 表达层 1–2 次」算；改 #20 的用例时注意命令与追问各算一次分类。被本地纠正成闲聊或追问的结果仍标 `Correction` 并保留分类调用的 token（ADR-0004），不要被表达层的 `Ok` 覆盖；importance 也要用 `IntentDraft.SetImportance` 跟着纠正后的草稿走，否则误判成农活的闲聊会退回默认 4。口吻在演示层 `NpcTones`（按 `NpcPersona` 取，不进 `NpcPersona` 本身）；最近 6 轮由 `DialogueBook.HistoryOf` 映射成 `DialogueContext` 交给路由器，`DialogueContext.MaxRounds` 是「6 轮」的唯一常量。HUD 的「本地模板」只在「当前这一轮是闲聊且 `UsedFallback`」时画（追问失败不画，按 #19 的范围）。
- `response_format: json_object` 失败时 DeepSeek 会把内容替换成**空格**、`finish_reason` 仍是 `stop`；客户端必须把空白内容当失败并回退，不能当合法 JSON 继续解析。
- 不点名地块时不要退回「最近的地块」，要交给效用打分挑（`IntentRouter.TryResolveCommandField`）；否则「给地浇水」会选中刚浇过的近地，违背「指令进入效用打分」。
- 种植类工单在等待成熟时必须允许插做维护（`FarmWorld.AdvanceWorkOrder` 的 `NextMaintenanceStep`），否则小麦这类 7 天作物会因为只浇一次水而永远等不到收获。测试要断言五个计划工步的顺序，维护步不计入工单进度。
- 模型 HTTP 客户端用自己的 `UnityWebRequest`（ADR-0003）。`thinking` / `reasoning_effort` 是请求体顶层字段，不要套 OpenAI SDK 的 `extra_body`。
- 带中文注释的请求/响应字段用 `Newtonsoft.Json` 解析；`com.unity.nuget.newtonsoft-json` 已加入 manifest，测试程序集若 `overrideReferences: true` 则仍需显式引用。

## 演示与 HUD

- 决策可视化面板在 547 高度下要装五项：去掉面板标题、把「当前意图」与「模型调用」压成一行、候选最多三行（2 有效 + 1 否决），面板高 406；再多第三根状态条就会被裁掉。打开时隐藏下方流水面板，关闭时恢复。
- 开关放在左侧标题行，右侧临时面板也留一个入口；关闭时 `OnGUI` 不调用 `World.Inspect`，也不构造快照。
- 截图验证时可用 `script eval` 反射 `FarmDemoHud.inspectorOpen` 私有字段；这是演示层字段，不要为截图加公开 API。
- 16x 下 1 个游戏日约 18.75 秒，`Start-Sleep` 会睡过头；截状态图前先切回 1x，否则有序工单可能已经干完、意图变成「待命」。
- 面板的「模型/耗时/token」直接来自 `Router.LastCall`；实测 deepseek-flash 意图解析 0.99–1.57 秒，`ModelCallInfo.None` 显示「未调用（本地模式）」。失败/超时也带客户端模型名与实测耗时；token 未返回时显示「token 未返回」，不要拿 0+0 冒充测量值。

- **Game 视图默认是 1175×547，`capture` 会把它拉伸成 1280×720**（横向 1.09、纵向 1.31），按 720 摆的 HUD 会变形。要按 1280×720 验收，先用 `script eval` 反射拿到 `UnityEditor.GameView` 实例，调 `SetCustomResolution(new Vector2(1280, 720), "T3")`；之后 `targetRenderSize` 与 `capture` 都是 1280×720。`GameViewSizes.selectedSizeIndex` 那条路不够，窗口不会立刻换尺寸。
- 左侧状态面板与右侧决策面板在 547 高度下各只有约 326 逻辑像素。`GUILayout.Label` 不设宽度会换行，直接把面板撑到下方日志区；三块地并排成一行、账本压成一行能省出 60+ 像素。相机也要避开底部 HUD：本票把机位从 z=-10 退到 z=-16，NPC 和草垛才不会被日志面板挡住。
- NPC 一开局就自主干活会抢在玩家输入之前把地种掉，一句话指挥的演示看起来像被拒绝。演示默认让 NPC **待命**；开局第一次接单自动恢复自主一次，之后只认玩家的开关（#29 的纯逻辑 `AutonomySwitch`，别把「自动恢复」写回接单分支的临时变量）。HUD 另有「自主中 / 待命」开关。
- IMGUI 输入框与按钮走同一套提交路径；`UnityEngine.Input` 在本工程不可用（activeInputHandler=1），但 IMGUI 事件不受影响。
- **动态字体的 `CalcSize` 比 `fontSize` 高**：18pt 标题约 29px、15pt 正文约 26px、12pt 备注约 22px。给标题留 30px，否则 `TextClipping.Clip` 会把字的顶部切掉（三人卡标题踩过）。
- **`GUI.TextField` 会把光标色留在 `GUI.color` 里**。紧接着画的占位文字会被冲淡成接近背景色；画占位文字前显式 `GUI.color = Color.white`，画完再还原。
- **UI 素材不受「素材开/关」影响**：头像与物品图标走 `PixelArt.LoadUiTexture` / `LoadUiSprite`（独立缓存），不经过 `UseArt` 判断；关掉素材只让农场退回体块，HUD 仍要有头像和图标。头顶表情是农场反馈，走 `LoadSprite`，关掉素材就不画。
- **`HudSkin` 只能在 `OnGUI` 里构造。** 它的 `Box` / `Label` / `Button` 要读 `GUI.skin`，在 `Start` 里 `new HudSkin()` 会抛 `ArgumentException: You can only call GUI functions from inside OnGUI`（#14 遗留，每次进 Play 都有；#15 改成 `OnGUI` 懒初始化，后来者别把构造搬回 `Start`）。
- **对话框正文四行**：720p 的对话框高 232，正文上限 160 字（`FarmDemoText.DialogueReply`），用 `skin.BodyWrap` + `CalcHeight` 在 27–108px（四行）之间取高；正文在上，提示、chips 与输入框贴底，1 行回答不会把输入框吊在半空。**547 高的 Game 视图退回 168 高、两行的旧布局**（`DialogueHeight()`），正文上限同时退回 110 字并留省略号，否则高对话框会盖住三人卡。长句只排一个工步时，「理解成」追加「我按 X 理解」（`FarmDemoText.IsLongSentence`，阈值 20 字）。空输入提示画在名字右边，不占「你：… · 理解成…」那一行；那一行用 `FarmDemoText.HintLine` 先缩玩家原话，保证「理解成」不被截掉。
- **输入框有焦点时 Tab 会被 IMGUI 先吃掉**：Tab 用来切换控件焦点，等 `OnGUI` 跑到 `HandleKeys` 时 `Event.type` 已经变成 `Used`，`keyCode` 也可能变成 `None`，只剩 `character == '\t'`。要同时读 `Event.rawType` 与字符，并在处理完 `GUI.FocusControl` 把焦点还给输入框；C / 1 / 2 / 3 仍只在没有控件抢键盘时生效。编辑器会先把 Tab 用于 Editor 自己的焦点导航，Game 视图里按 Tab 到不了 `OnGUI`，这条只能在播放器里复验。
- **英文输入不经过模型**：`LocalIntentParser.LooksNonChinese` 在模型与记忆检索之前拦下「一个汉字都没有、但至少有一个字母」的句子，统一回「我只听得懂中文。」；中英混写（「把 A1 浇水」）仍按中文解析。结果标 `IntentResult.LanguageNotUnderstood`，HUD 不说「拒绝了这单活」、也不冒被拒表情，只换一张「没听懂」的脸。
- **验证头顶表情的可见性**：用 `script eval` 读 `NpcEmote` 的 `SpriteRenderer.enabled` 比截图可靠（截不到“没画”）。跟拍一名 NPC、把另外两人挪到 (100,100)，应看到被跟拍者 `enabled=True`、屏外两人 `enabled=False sprite=True`；纯函数 `OverheadEmotes.IsVisible` 只判四边与相机前后，别拿 `renderer.isVisible` 代替。
- **2.5 秒的表情要截图就尽快**：`script eval` 提交指令 → 等模型 → 再 `capture` 往往已经超过 2.5 秒；用离线 `IntentRouter` 让 `ApplyIntent` 同步完成，或在同一帧里直接 `EmoteBoard.Post` 再立刻 `capture`。

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
- **`Builds/Windows` 不会被构建清空**：打包只覆盖同名文件，上一版残留的文件会叠在新包上，脚本报的体积就不是干净包。要报体积或交付，先删掉整个 `Builds/Windows` 再跑 `build-windows.ps1`；#16 量到过「旧 3D 文件还在、新文件只多了 14 MB」的假体积。
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

## 交付与证据（#10）

- **剧本回放不能直接沿用 `Time.deltaTime` 与 `Time.time`。** 正常游玩用 `Time.time` 做动作冷却；回放改为固定步长（1/60）累计的 `Now`，`World.Tick` 也按整步走。帧率变化只改变「每帧补几步」，不改变日界与工步顺序，三遍 trace 才会逐字一致。改 `FarmDemoBootstrap.UpdateNpc` / `Walk` 时不要漏掉 `deltaSeconds` 参数。
- **回放期间要暂停。** 台词、字幕、等待都用 `SimulationPaused` 冻结领域；否则「你怎么不去干活」的回答里剩余休息秒数会随帧率变化，trace 对不上。暂停只冻结领域与 NPC 动作，`OnGUI` 与逐帧录制照常跑。
- **`FarmDemoWorld.Create()` 是开局世界的唯一来源。** 演示引导器与固定剧本都从这里取地块、NPC、种子与开局作物；不要在剧本里另抄一份 `AddField`/`AddSeeds`，否则测试通过但录屏里是另一个农场。
- **证据参考由 `ElysFarm.EditorTools.ScenarioEvidence.Generate()` 生成**，写入 `TestResults/scenarios/reference/`；交付包用 `-elysScenario` 回放同一份 `FixedScreenplays`。比对必须严格 UTF-8 解码后 `-ceq`，PowerShell 的 `Get-Content -Raw` 在 5.1 下会按 GBK 解中文。
- **`run-scenarios.ps1` 的本地变量不要叫 `$runs`。** PowerShell 变量名大小写不敏感，`$runs = @()` 实际在给 `[int]$Runs` 参数赋数组，报 `Cannot convert System.Object[] to Int32`。用 `$executions` 之类的名字。
- **录屏用游戏内抓帧，不要抓窗口。** `FrameRecorder` 在 `WaitForEndOfFrame` 后 `ScreenCapture.CaptureScreenshotAsTexture()` 再 `EncodeToPNG`，不受窗口位置、DPI 缩放、遮挡与标题栏影响；窗口抓取在 125% 缩放下会裁边。20fps 的 PNG 序列交给 ffmpeg 编码，1280×720 的像素画约 1–2 MB/分钟成片。
- **录制会拖慢游戏，但不要因此改台本。** 回放的等待条件都是领域状态（第几天、工步完成、是否在休息），固定步长保证内容一致；录制只影响成片时长。屏幕越慢，同样的 20fps 视频里动作越慢，这是可接受的。
- **压测先关垂直同步。** `PerformanceProbe` 设 `QualitySettings.vSyncCount = 0`、`Application.targetFrameRate = -1`，采 `Time.unscaledDeltaTime`；不关的话 60Hz 显示器会把所有机器都压成 60fps，看不出瓶颈。报告要同时给 p99 帧时与低于 30/60fps 的帧数，别只给平均。
- **播放器命令行才是交付入口。** `-elysScenario <full-flow|refusal|memory> -elysTrace <path> -elysRecord <dir> -elysQuit <sec>`；Editor 里没有这些参数，用 `script eval` 调 `demo.StartScenario(...)` 验证。`Application.Quit()` 在播放器里退出码为 0，Editor 里不退出。
- **`Builds/Windows` 先删再打包。** Unity 只覆盖同名文件，上一版残留会叠进新包；`package-windows.ps1` 已处理。要「从干净提交构建」时先提交，再 `-RequireClean`，provenance 里 `dirty` 才是 false。
- **干净环境验收**把 `Builds/Windows` 拷到 `%TEMP%`，清空 `DEEPSEEK_*` / `SILICONFLOW_*` / `ELYS_SECRETS_FILE`，PATH 只留 System32，再跑 memory 剧本并比对 trace。Unity 的 `Player.log` 仍可能落到真实用户的 `LocalLow`（Known Folder 由 shell 解析），脚本两处都找；这不是全新虚拟机，报告里要如实写。
- **PowerShell 脚本改完先 `Parser::ParseFile` 检查语法。** 中文注释在编码坏掉时 `pwsh` 的报错会变成一串乱码 token，直接看行号比猜编码快。
