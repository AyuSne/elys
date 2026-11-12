# PixelArt 素材

这里的 PNG 来自《星露谷物语》原作图集，**只用于本仓库的私有演示**，不随演示包对外分发，也不适用
仓库里 Quaternius 素材的 CC0 结论。

来源：`StardewValley-Assets/星露谷物语素材.rar`（仓库外，`.gitignore` 忽略）。只提交用到的图：

| 文件 | 用途 |
| --- | --- |
| `Characters/Robin.png`、`Haley.png`、`Gus.png` | 三名 NPC 的四方向行走图（16×32/帧，4 列 × 4 行） |
| `Crops/crops.png` | 作物图集：每个品种 8 格横条（本演示取其中 5 个阶段，见 `CropStages`） |
| `Tiles/spring_outdoors.png` | 春季室外瓦片（16×16/格）：草地与室内的装饰瓦片来源 |
| `Tiles/hoeDirt.png` | 耕地土块（干/湿各四列；代码取干土第 3 列、湿土第 7 列，即 2/6） |
| `LooseSprites/shadow.png` | 角色脚下的影子 |
| `Emotes/emotes.png` | 头顶表情（16×16/格，4 帧 × 16 行；本演示取行号 3/4/6/10，0 起） |
| `Buildings/farmhouse.png` | 农舍（118×150，取自 `houses.png` 的一级农舍） |
| `Buildings/coop.png` | 鸡舍（96×128） |
| `Buildings/well.png` | 水井（48×80） |
| `Buildings/barrel.png` | 产出仓的木桶（16×16，取自 `Craftables.png`） |
| `Nature/tree1.png`、`tree2.png` | 两种树（48×80），落点在底边中点 |
| `Nature/stump.png` | 树桩（16×26） |
| `Nature/grass1.png`、`grass2.png`、`grass3.png` | 三丛草（约 15×20） |

导入规则在 `Assets/Editor/PixelArtImportPostprocessor.cs`：点采样、无压缩、无 mipmap。

土路不在这份清单里：原作图集里没有一条能无缝铺开的土路，`FarmDressing` 按目标图的配色
（`#A68258` / `#9E7B52` / `#8E6E48`）在运行时画一张 16×16 的贴图，明暗两块交替铺。
