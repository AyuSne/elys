# 幻兽帕鲁与「小帕鲁」

> 资料截止 2026-11-11。「官方」＝Pocketpair 官网 / Steam 公告 / 官方 Wiki（依补丁说明编写）；「社区推断」＝玩家观察或二手报道，已标注。

## 结论摘要

1. **Palworld（幻兽帕鲁）**：Pocketpair（东京）的开放世界生存建造 + 生物收集游戏，2024-01-19 EA，**2026-07-10 发布 1.0**。官方口径**4000 万玩家**（2026-07），媒体口径**销量 2500 万+**——两者不同。
2. **「小帕鲁」不是专有名词**：无官方产品或衍生作用该名。最可能是对基地打工小生物的爱称，叠加「帕鲁＝被压榨打工人」这个 2024 年初成型的梗。
3. 帕鲁的自动化**不是** LLM 驱动的 NPC，而是**工作适应性 + 设施需求匹配 + 基地级优先级 + 需求驱动中断**的效用式调度；玩家给的是「许可与指派」，不是「指令」。
4. **关键落差**：原题的「施肥、除草」在 Palworld 里**没有**对应工作类型，「收获」属 Gathering 而非独立 Harvesting。出题人写的是农场模拟流程，套了帕鲁的皮。
5. 「类帕鲁」与「类斯坦福 AI 小镇」是两套不重叠的技术栈，难点是把两者缝起来。

## 1. 基础事实

- **开发/发行**：Pocketpair, Inc.（东京），自称 small indie studio。
- **版本**：2024-01-19 Steam/Xbox EA → 2026-07-10 全平台 1.0（`1.0.0.100427`），新增 72 只帕鲁（总数 287）、新地图 Sunreach 与 World Tree、等级上限 65→80。
- **玩法定位**：生存建造 + Pal Sphere 捕获帕鲁 + 派帕鲁到基地**自动打工** + 骑乘射击。基地自动化是它与同类生存游戏的分野。
- **「Pokémon with guns」**：源自 2021 年首支预告片后的媒体/社区描述，因「生物收集 + 枪械 + 奴役式劳作」得名，已成通用外号；但**不是官方定位**，Pocketpair 公开表示不喜欢该说法。
- **数字**：3200 万玩家（2025-02）→ **4000 万玩家**（2026-07）；销量 2500 万+（2026-09，媒体口径）。官方从未把 4000 万称作销量。

## 2. 「小帕鲁」到底指什么

**（A）最可能：对「打工帕鲁」的口语爱称，叠加「帕鲁＝被压榨打工人」的梗。**

- 「小 X」是中文对萌系生物的常规爱称（小精灵、小动物），「小帕鲁」＝那只可爱的小打工仔。
- 「帕鲁」在 2024 年初已脱离游戏，成为形容被过度使唤的劳动者的通用词，衍生「帕鲁文学」「你不干，有的是帕鲁干」等句式，源于游戏机制与「打工人」自嘲的共振。
- 非纯外部附会：游戏内**真有一个叫「社畜」（Work Slave）的被动技能**（Rank 1），把自嘲写进数值。代入原题「AI NPC（类似小帕鲁）会自动完成种田全流程」——「像帕鲁那样的小家伙替我把农活全干了」，语义通顺。**这是最可能的读法。**

**（B）字面义：体型小的帕鲁。** 攻略里确有「大的帕鲁容易卡模型，所以想用小帕鲁」之说，指模型小、不易卡寻路的个体。但它是攻略分类词，不是「类似小帕鲁」这种类比句会选的对象。

**（C）某个衍生作品的误记。** 衍生有手游《Palworld Online》（Garena）、《Palworld Mobile》（Krafton）、农场向《Palworld: Palfarm》、恋爱 parody《Pal♡world!》、官方卡牌。其中 **Palfarm 的官方文案几乎逐字对应题目**——「From sowing and watering to harvesting and crafting, your Pals are here to handle the heavy lifting」。若指某部作品，它最接近；但**无官方材料称其「小帕鲁」**。

> 落地建议：表述为「类似 Palworld 中在基地自动务农的帕鲁」，别假设「小帕鲁」是某部作品。

## 3. 帕鲁的自动化工作系统

### 3.1 工作适应性：12 类，等级 1–8（上限 10）

| 类型 | 作用 |
|---|---|
| Kindling 点火 | 灶、熔炉、加热器 |
| Watering 浇水 | 灌溉、粉碎机、磨坊 |
| Planting 播种 | 下种，启动生长周期 |
| Generating Electricity 发电 | 发电机、流水线供电 |
| Handiwork 手工 | 制作、建造、**修理**（1.0 起免工具包） |
| Gathering 采集 | **收割成熟作物**、拾取；等级影响产量 |
| Lumbering 伐木 / Mining 采矿 | 木材、矿石；等级解锁矿种 |
| Medicine Production 制药 | 药台 |
| Cooling 冷却 | 冷藏、孵化器控温 |
| Transporting 运输 | 搬货入箱；等级影响单趟范围 |
| Farming 牧场 | 牧场被动产出羊毛/奶/蛋/蜜 |

自然值通常 1–8；**Pal Essence Condenser** 升星 +1（1–3 星各加一项，4 星全加），配合技术手册与队友增益可达上限 10。1.0 重做了工作适应性数值，设施可**要求最低等级**，不达标不干活。

⚠️ 清单里**没有施肥、除草**。作物只经历「空地→已播种→生长→成熟→被采集」，无杂草与肥料状态。照抄五步须自建机制并说明是扩展，或归并进 Planting/Gathering。

### 3.2 任务分配：三层玩家控制 + 一张隐藏优先级

官方确认（`Monitoring Stand`，1.0 追加 High-Quality / Ancient 两档，后者降低 SAN 消耗）：①**允许/禁止某类工作**（0.2.0.6 起）；②**工作强度**档位（0.1.5.0 起）；③**把某只帕鲁永久固定到某设施**（0.3.1 起）。

其余靠帕鲁自己找活：Wiki 写「Active Pals automatically perform tasks at compatible structures within the base area」，补丁说明又多次「调整/重排默认工作优先级」，说明底层有**一张官方调参、玩家不可编辑的优先级表**。**算法未公开**；社区推断（二手）是按「自身适应性 → 未饱和的设施需求 → 距离/寻路可达」打分挑选，优先级表用于打破平局。无官方材料称其为行为树。

### 3.3 状态：SAN、饥饿、疾病、极端行为

帕鲁有三条状态条：**Health / Hunger(SAT) / Sanity(SAN)**。

- **SAN**：工作消耗 SAN，量取决于工作类型、设施与强度；SAN 低时自行去 Hot Spring 或床上休息，过低则白天「压力睡眠」；**没床睡觉反而掉 SAN**。触底后工作意愿下降 → **越来越频繁拒绝干活**，HUD 显示 "Extremely unhealthy due to neglect"，表情变阴郁 → 进入疾病（Sick/Cold、Sprain、Ulcer、Fracture、Weakened、**Depressed** 最重：工作 −20/移动 −10），医疗用品分 4 级对症。
- **最极端 `Troublemaker`**：工作 −50、移动 −50，描述是「极度可疑的行为……只有洗脑才能让这个叛徒恢复正常」。这是题目问的「罢工/暴走」——不是打砸，而是**彻底怠工 + 需外科式干预**。
- **饥饿**：SAT 归零进 Starving，持续掉血但**不致死**；基地帕鲁自动去 Feed Box 吃，随队帕鲁需手动喂。
- **受伤与救助**：Minor→Major→Incapacitated（回 Palbox 复活，默认 10 分钟）；带 Handiwork 的帕鲁**会去救倒地同伴**——由工作适应性驱动的利他行为，没有专门的救援 AI。这是「涌现感」最便宜的一个例子。
- **性格即数值**：0–4 个被动槽，`Work Slave`（社畜，效率↑）、`Slacker`（偷懒，−3 档）、`Workaholic` 等直接改写劳动表现。**人格是修正项，不是对话。**

### 3.4 是 FSM / 行为树，还是更复杂的调度？

**官方未公开。** 行为表征：单一当前任务 + 需求触发抢占（吃、睡、泡汤、看病）+ 固定指派覆盖自主选择。这更像**效用驱动的任务选择器**：纯 FSM 难以解释同环境下选不同设施；优先级表 + 距离权重是典型 utility 打分；SAN/饥饿作为全局需求抢占任务，属 needs-based preemption。引用时写「官方未公开；社区据行为推断」。

## 4. 为什么它是这道题的好参照

**（1）它把自然语言指令降解为「许可 + 指派」。** 玩家从不说「去给那块地浇水」，而是建灌溉设施 → 允许 Watering → 派一只有 Watering 的帕鲁 → 它自己发现地渴了。指令被降解成三类可计算物：**工作类型、目标设施、前置条件**。启发：LLM 只负责把「帮我把这片田种满小麦」翻成 `{work_type: planting, target: 3 号田, until: 满}`，剩下的发现、排队、寻路、抢占交给确定性调度层。让 LLM 逐帧决定「迈左脚」必翻车。

**（2）智能在环境里，不在 NPC 里。** 种植园本身就是状态机（空地/已播种/生长/成熟/待采集），帕鲁只是「能对某状态施加某动作」的执行器；看似「它知道该干什么」，其实是**设施在广播需求**。最稳的架构：地块持有状态 → 广播可执行动作 → NPC 按能力表认领，天然支持并发、中断、存档。

**（3）「自然」来自可归因，而非台词量。** 玩家觉得帕鲁有性格，靠三样廉价表达：头顶的饥饿面包图标、面板写明的「SAN 低/生病/怠工」、一句 "Extremely unhealthy due to neglect"。玩家**随时能回答「它为什么停下」**，行为才变成故事。这正是「表情包输出」该有的形态：**状态 → 图标/气泡/短句**。罢工却给不出原因，观察价值立刻归零。

**（4）它和「斯坦福 AI 小镇」是两套东西。** Generative Agents（Park et al., 2023）靠记忆流 + 反思 + 规划驱动**社交涌现**，输出自然语言；帕鲁靠**可见的劳动过程 + 可干预**，输出动画与数值，完全没有语言模型。本题实际要求两套系统并存：**任务层**用帕鲁式确定性调度（保证「全流程跑通、时间与背包正确」），**表达层**用小镇式记忆/人设/情绪（保证「自然语言 + 表情包 + 观察价值」）。用一个 LLM agent 同时管排程和说话，是这类题最常见的翻车点。

## 5. 2026 年现状

- **版本**：1.0（2026-07-10）后进入 1.0.x 补丁线，最新 **1.0.5（2026-09-14，纯 bug 修复）**；此前 1.0.3（08-12）、1.0.4（09-06/07）。下一个大版本 **《Palworld: Side by Side》** 已于 2026-10-20 宣布**2026 年冬**上线，内容未披露。
- **平台**：PC(Steam)/Xbox/PS5，跨平台。
- **手游（两条线，勿混淆）**：①**《Palworld Online》**——Garena 获授权开发的移动端 MMO，2026-08-03 公布，09-03 韩国开启预约，商店页曾标 11-30，全球定档未公布；②**《Palworld Mobile》**——Krafton 线，2025 Q4 有过 alpha 测试。
- **任天堂专利诉讼（截至 2026-11-11：未宣判）**：2024-09-18 任天堂 + The Pokémon Company 在东京地裁起诉，请求禁令 + 赔偿；2025-11 原告**修改请求，把侵权对象收缩到早期版本**；2026-10-01 证据审理，**11-09 法院预定开示心证（心証開示）**——这是「法院倾向」的程序性披露，**不等于判决**，坊间「11-09 宣判」的说法不准确。当前**无禁令，1.0 正常售卖**；专利分析师 Florian Mueller 称看不到现行版本担责的路径，估算赔偿额仅数万美元量级（非法院认定）。
- **社区活跃度**：第三方统计（tracker.gg）2026 年中日在线约 2.6–5 万人，较首发大幅回落但仍活跃。

---

## 来源清单

**一手**

- 4000 万玩家（2026-07-08）— https://www.pocketpair.jp/en/game-news/palworld-reaches-40-million-total-players-special-video-released-ahead-of-1-0-release-on-july-10/
- 3200 万玩家（2025-02-19）— https://www.pocketpair.jp/en/news/palworld-blasts-past-32-million-players/
- v1.0 官方更新日志 — https://steamcommunity.com/games/1623730/announcements/detail/686383649529010624
- 《Side by Side》2026 冬 — https://www.pocketpair.jp/en/game-news/palworld-side-by-side-new-update-coming-winter-2026/
- Pocketpair 诉讼声明 — https://www.pocketpair.jp/en/news/regarding-the-lawsuit/
- 《Palworld: Palfarm》官方页 — https://www.pocketpair.jp/en/games-en/palfarm-en/
- 任天堂起诉公告 — https://www.nintendo.co.jp/corporate/release/en/2024/240919.html
- Wiki · 工作适应性 — https://palworld.wiki.gg/wiki/Work_Suitability
- Wiki · Sanity — https://palworld.wiki.gg/wiki/Sanity
- Wiki · Afflictions — https://palworld.wiki.gg/wiki/Afflictions
- Wiki · Base — https://palworld.wiki.gg/wiki/Base
- Wiki · Monitoring Stand — https://palworld.wiki.gg/wiki/Monitoring_Stand
- Wiki · 1.0.0 — https://palworld.wiki.gg/wiki/1.0.0
- Wiki · 1.0.5 — https://palworld.wiki.gg/wiki/1.0.5
- Wiki · Passive Skills — https://palworld.wiki.gg/wiki/Passive_Skills

**权威媒体 / 行业**

- PocketGamer.biz（销量、Palworld Online 预约）— https://www.pocketgamer.biz/palworld-online-opens-for-pre-registration-in-south-korea/
- Garena 官方新闻稿 — https://www.garena.sg/news/7UPUTA
- Patent Arcade（诉讼程序、11-09 心证、无禁令）— https://www.patentarcade.com/2026/07/palworld-1-0-launches-as-nintendo-patent-case-targets-earlier-versions.html
- AUTOMATON WEST（原告收缩到旧版本）— https://automaton-media.com/en/news/nintendo-and-the-pokemon-company-have-reportedly-narrowed-palworld-lawsuit-to-older-versions-of-the-game-only-likely-thanks-to-preventive-updates-by-pocketpair/
- Nintendo Life（「Pokémon with guns」非官方）— https://www.nintendolife.com/news/2025/04/surprise-palworld-dev-isnt-fond-of-the-pokemon-with-guns-description
- GamesRadar+（Pocketpair 不做生成式 AI）— https://www.gamesradar.com/games/survival/gamers-dont-want-it-palworld-lead-says-pocketpair-doesnt-touch-ai-because-players-hate-it-and-artists-like-doing-stuff-themselves/
- arXiv 2304.03442《Generative Agents》— https://arxiv.org/abs/2304.03442

**中文语境（二手，仅用于梗与「小帕鲁」判定）**

- 3DM《在鼠鼠和吗喽之后……》— https://www.3dmgame.com/original/3744428.html
- 游民星空《「帕鲁」成缝合怪最大难关？》— https://www.gamersky.com/news/202411/1841031.shtml
- 工劳网《沉迷〈幻兽帕鲁〉的年轻人》— https://at.laborinfocn5.com/articles/39439
- tracker.gg Steam 在线人数 — https://tracker.gg/population/steam/1623730

**未采用**：lawfold.com、settlementinsight.com、senseos.gg——或为聚合/SEO 站，或自述「AI 起草 + AI 核查」，仅作线索交叉印证。
