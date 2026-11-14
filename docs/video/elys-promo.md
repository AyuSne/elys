# Elys 宣传片

> 成片：[elys-promo.mp4](elys-promo.mp4)（带 BGM）· [elys-promo-nobgm.mp4](elys-promo-nobgm.mp4)（保留配音与音效，便于后期自配音乐）
> 规格：1920×1080 / 30fps / 79.4 秒 / 2380 帧 / H.264（yuv420p, bt709）+ AAC 48kHz /
> 响度 −14.0 LUFS、真峰 −1.3 dBFS

## 这是什么

一支面向「B 站 / YouTube 也站得住」的作品片：**画面全部是真实游玩，不是动画复刻**。
玩家在输入框打一句「把中间那块地收了」，三名 AI NPC 接下并完成五步农活；
片子把「她们为什么这么干」摊开——记忆命中、候选打分、被否决的原因、以及真实的模型耗时与 token。

和已有的 [elys-demo.mp4](elys-demo.mp4)（三条固定剧本的交付证据）分工不同：

| | elys-demo.mp4 | elys-promo.mp4（本片） |
|---|---|---|
| 路线 | 剧本回放，**离线**（本地模板） | **live play + 真模型**（DeepSeek） |
| 用途 | 可复现的证据 | 给人看的作品 |
| 延迟 | 固定步长 | 真实 1.9–2.3 秒 |

## 结构（2380 帧）

| 时间 | 镜头 | 内容 |
|---|---|---|
| 0:00 | 钩子 | 「你只说一句话。」→ 实机慢速打字 → 「好，去把中间那块（A2）的小麦收了。」 |
| 0:06 | 片名 | 羊皮纸卡：ELYS / 一句话，三个人下地干活 |
| 0:10 | 五步各演一遍 | **每一格绑死一步**：01 播种 / 02 浇水 / 03 施肥 / 04 除草 / 05 收获；右上角贴同一时刻的 HUD 意图行截图，片子报第几步、画面就写着第几步 |
| 0:30 | 决策可视化 | 抽屉推到 3× 特写：① 记忆带权重 ③ 候选与否决原因 ④ `deepseek-flash 2.34s · 747+269 tok` |
| 0:42 | 三个人 | 阿禾 / 阿芽 / 阿土，各一句真实台词 + 配音 + 人设参数 |
| 1:02 | 反转一 | 关掉美术 → 体块世界照常运转（领域层与表现层分开的现场证据） |
| 1:10 | 反转二 | 不配 key 启动：HUD 自己标「本地模板」，她照样接单（无模型降级） |
| 1:14 | 落版 | 三张头像飞入合影 + ELYS + 定位 + 技术脚注 + 仓库地址 |

## 画面怎么来的

1. `capture/capture.ps1`：用 `.secrets.local.json` 里的真 key 启动 `Builds/Windows/Elys.exe`，
   窗口钉到物理 (0,0)、**1920×1080 无边框置顶**，再用 `ffmpeg ddagrab`（DXGI 桌面复制）抓物理像素，60fps。
2. `capture/steps/*.txt` + `capture/drive.ps1`：脚本化输入（后台 `PostMessage` 注入中文、真键盘回车），
   每个动作打一条 `mark`，记录相对录制起点的秒数，后期据此切点。
3. 原始 take 在 `out/raw/`，按标记切成 `public/footage/*.mp4`（30fps，crf 16）。
4. Remotion 只做**编辑层**：整数倍裁切（1× / 2× / 3×，最近邻）、字幕、标注、音效与母带。

为什么运镜在后期而不是游戏里：相机只有俯视 3× 与跟拍 4× 两档、`LateUpdate` 每帧覆盖位姿，
而像素画的整数倍裁切是无损的——后期推近反而更可控，也不用改动已收口的交付提交。

## 声音

| 层 | 来源 | 说明 |
|---|---|---|
| BGM | Mixkit「Cat Walk」(Arulo) | 免署名、可商用；已按人声段做闪避（0.19 → 0.07） |
| SFX | Freesound，5 条 CC0 | `fs-736852` 转场打击、`fs-856174` 低频冲击、`fs-848212` whoosh、`fs-341832` 键盘拟音；清单与许可见工程内 `audio-manifest.json` / `FREESOUND-CREDITS.md` |
| 配音 | **DeepSeek TTS**（2026-11-13 开放） | 阿禾 `stella` 原声、阿土 `tide` 原声、阿芽 `stella` 派生（音高 +5% / 语速 +12%）；只有两个音色，所以第三个人是派生的 |

SFX 的 `from` 按「目标峰值帧 − 音源峰值滞后 − 输出偏移」推：`fs-736852` 峰值 0.695s、
`fs-856174` 1.035s、`fs-848212` 1.565s、`fs-341832` 1.755s。输出链路实测有
**+1.2～1.3 帧的系统性滞后**（AAC priming 量级），统一在 `PEAK` 常量里多扣 1 帧，
不逐条改帧号（`src/AudioLayers.tsx`）。

## 诚实性说明

- 画面是**真模型、真输入、真延迟**跑出来的，没有一处动画复刻游戏。
- 五个农活快剪取自同一场 session 的不同时刻（收货、播种、浇水施肥除草、16× 生长），
  按叙事顺序重新排列 —— 这是蒙太奇，不是一镜到底。
- 三句台词都是游戏真会说的：阿禾那句是当次模型的真实输出（2.34s / 747+269 tok 也是当次抽屉里的真数）；
  阿芽那句出自交付记录里体力 52% 的休息分支；阿土那句是转述模板 `听{来源}说，…` 的产物。
- 中文字幕用 `Microsoft YaHei`（游戏 HUD 实际用的系统字体），像素点阵字体只给数字与英文技术名词。
- 片中 NPC 配音是后期合成，**游戏本身零音效**；这一点在片尾没有加注，若对外发布建议在简介里说明。

## 复现

```powershell
# 1. 采集（会接管屏幕 1–2 分钟，窗口置顶）
powershell -NoProfile -ExecutionPolicy Bypass -File Builds\Video\capture\capture.ps1 `
  -Steps Builds\Video\capture\steps\hook.txt -DurationSec 28 -Out Builds\Video\out\raw\H1-hook.mp4

# 2. 渲染（首次会下载 Chrome Headless Shell）
cd Builds\Video
npx remotion render src/index.ts ElysPromo out/promo.mp4
npx remotion render src/index.ts ElysPromo out/promo-nobgm.mp4 --props=D:\Elys\Builds\Video\props-nobgm.json

# 3. 母带（响度对齐平台标准）
ffmpeg -i out/promo.mp4 -c:v copy -af loudnorm=I=-14:TP=-1.5 -c:a aac -b:a 192k docs/video/elys-promo.mp4
```

工程在 `Builds/Video/`（不入库）：`DESIGN-SPEC.md` 是阶段 0–3 的判断记录，`src/` 是 Remotion 时间线，
`out/qa/` 是逐版本静帧档案。
