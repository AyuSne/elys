# PixelArt 素材

这里的 PNG 来自《星露谷物语》原作图集，**只用于本仓库的私有演示**，不随演示包对外分发，也不适用
仓库里 Quaternius 素材的 CC0 结论。

来源：`StardewValley-Assets/星露谷物语素材.rar`（仓库外，`.gitignore` 忽略）。只提交用到的图：

| 文件 | 用途 |
| --- | --- |
| `Characters/Robin.png`、`Haley.png`、`Gus.png` | 三名 NPC 的四方向行走图（16×32/帧，4 列 × 4 行） |
| `Crops/crops.png` | 作物图集：每个品种 8 格横条（本演示取其中 5 个阶段，见 `CropStages`） |
| `Tiles/spring_outdoors.png` | 春季室外瓦片（16×16/格） |
| `Tiles/hoeDirt.png` | 耕地土块（干/湿各四列；代码取干土第 3 列、湿土第 7 列，即 2/6） |
| `LooseSprites/shadow.png` | 角色脚下的影子 |

导入规则在 `Assets/Editor/PixelArtImportPostprocessor.cs`：点采样、无压缩、无 mipmap。
