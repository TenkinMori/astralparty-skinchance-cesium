# 战损立绘 · SkinChanceMod

吉星派对（Astral Party）CesiumLoader 模组：**局内左下角的角色立绘，按当前血量百分比自动换成配置里的贴图**，做成战损效果。

- 只影响**自己**的立绘，队友和敌人不受影响。
- 按**皮肤**（`ItemID`）配置，同一角色穿不同皮肤可以配不同的一套图。
- 一张图一个档位，血量掉到阈值以下就换下一档；未配置的皮肤完全不动。
- **全部 136 张皮肤的文件夹和配置都已预置好，你只要把 PNG 丢进去**，不用写 JSON。

> 资源名对照表见 [SKINS.md](SKINS.md)：按最新 GameData（3.3.0）抽取，共 38 个角色、136 条皮肤立绘，`皮肤 ItemID` 就是文件夹名前缀。

## 安装

1. 下载 Release 里的 `SkinChanceMod-1.1.0.zip`。
2. 解压到 `Astral Party\AstralParty_ModLoader\mods\SkinChanceMod\`，应当能看到 `SkinChanceMod.dll`、`SkinChanceMod.json`、`icon.png`、`SKINS.md`。
3. **把 `skins.zip` 里的 `skins\` 解压到同一个模组目录**（136 个文件夹，每张皮肤一个）。
4. 启动游戏，选人进局。

> 首次启动会自动把 136 条皮肤规则写进配置，并把贴图文件夹建好（如果 `skins\` 不存在）。所以其实第 3 步也可以跳过：让游戏自己建，再往里丢图。

## 使用

**你只要放图，不用改配置。**

每个皮肤一个文件夹，名字是 `<皮肤 ItemID>_<皮肤名>`，里面三个文件名固定：

```
mods\SkinChanceMod\skins\
  100101001_初始皮肤-帕露南\
    full.png      ← 满血 / 100%
    damaged.png   ← 掉到 60% 以下
    broken.png    ← 掉到 25% 以下
  100117006_钢羽森姬-茉莉\
    full.png
    ...
```

- 文件名就是档位，**放哪几张都行**：只放 `broken.png`，那血量 25% 以下才换图，其它时候保持游戏原图。
- 缺档会自动退化：60% 时只有 `broken.png` 而没有 `damaged.png`，就用 `broken.png`；反之 10% 时只有 `full.png`，就用 `full.png`。优先「更完好」的一档。
- 想改阈值或者换别的文件名，才需要动配置：

```json
{
  "enabled": true,
  "hideFightVideo": true,
  "characters": [
    {
      "itemId": 100101001,
      "stages": [
        { "hpPercent": 100, "image": "skins/100101001_初始皮肤-帕露南/full.png" },
        { "hpPercent": 60,  "image": "skins/100101001_初始皮肤-帕露南/damaged.png" },
        { "hpPercent": 25,  "image": "skins/100101001_初始皮肤-帕露南/broken.png" }
      ]
    }
  ]
}
```

| 键 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `enabled` | bool | `true` | 总开关，关掉立刻恢复游戏原立绘。 |
| `hideFightVideo` | bool | `true` | 立绘是视频时强制切静态图并停掉视频，否则接管不到。 |
| `characters` | array | 全部 136 条（三档） | 每个元素 `{ itemId, stages[] }`，`itemId` 见 SKINS.md。 |
| `characters[].stages[].hpPercent` | int | 100 / 60 / 25 | 这一档的**血量百分比上限**（1–100）。 |
| `characters[].stages[].image` | string | `skins/<id_名>/full.png` 等 | PNG 路径，相对模组目录（也可写绝对路径）。 |

### 档位怎么命中

`hpPercent` 是**上限**，取「不小于当前血量百分比」里最低的那一档：

| 当前血量 | 命中 |
|---|---|
| 61%–100% | `full.png` |
| 26%–60% | `damaged.png` |
| 0%–25% | `broken.png` |

档位顺序随便写，模组会自己排序。不想要「满血也换图」就把 `hpPercent: 100` 那档删掉，满血时就会保持游戏原图。

### 贴图要求

- 只支持 **PNG**（8 bit，非隔行，colorType 0/2/3/4/6），带透明通道没问题。
- 尺寸随意，会按游戏原本的立绘框缩放（沿用原 loader 的 `fill`/`align`/`customOffset`/`customScale`）。
- 建议和游戏原图同尺寸（`UT_Hero_Bust_*` 一般是 1568×900），避免缩放糊掉。
- 立绘资源名见 SKINS.md 的「左下图集」列，可以从 `StreamingAssets\aa` 里用 AssetStudio/UnityPy 抽出来改。

## 原理

- 左下角立绘是 `UIBattleInfoPanel.loader_Skin`（`GetChildAt(10)`），实际类型 `UICom_HeroSkin`，里面两个子对象：`loader_Skin_Image`（静态图，`GLoader`）与 `loader_Skin_Video`（视频，`GGraph`）。
- 游戏唯一的刷新入口是 `BattleInfoPanel.RefreshHeroSkin()` → `CommonUIManager.RendererSkin(...)`，它会直接改 `loader_Skin_Image.url`。**改 url 一定被顶掉**，所以本模组不动它。
- 做法：在 `UICom_HeroSkin` 里按原图的位置和尺寸加一个自己的 `GLoader`（`SkinChanceOverlay`）盖上去，原 `loader_Skin_Image` 只 `visible = false`。撤掉接管时把 `visible` 还回去。
- 当前皮肤来自 `GameLogicManager.inst.battle.GetSelfPlayerData().player.standingPainting.ItemID`；血量来自 `BattlePlayerData.Property.HP.Value` 与 `Property.maxHP`。
- 面板通过反射 `UIManager._cachedPanelDict[UIPanelType.BattleInfo]` 取，不会因为轮询而提前构造面板。
- 贴图解码不依赖 `ImageConversion.LoadImage`（HybridCLR 下不一定能反射到），自带一个纯托管 PNG 解码器（`PngDecoder`），解出 RGBA 后逐行垂直翻转再 `LoadRawTextureData`。

## 验证

日志前缀 `[SkinChanceMod]`，正常接管时会看到：

```
[SkinChanceMod] 战损立绘已加载：共 136 条皮肤规则。
[SkinChanceMod] 已建好 136 个皮肤贴图文件夹：...\mods\SkinChanceMod\skins
[SkinChanceMod] 已加载贴图 1568x900：...\mods\SkinChanceMod\skins\100117006_钢羽森姬-茉莉\full.png
[SkinChanceMod] 战损立绘已接管：皮肤 100117006 → skins/100117006_钢羽森姬-茉莉/full.png（HP 100%）。
[SkinChanceMod] 立绘已切换：damaged.png（HP 55%）
```

没反应时依次看：`enabled` 是否为 true、日志里那个 `皮肤` 编号有没有对应的文件夹、文件夹里有没有 `full.png`/`damaged.png`/`broken.png`、日志有没有「找不到贴图 / 解码失败」。

## 构建

需要 .NET SDK 与 `lib\CesiumLoader.SDK.dll`、`refs\game\*.dll`（已在仓库里）。

```powershell
.\build.ps1
```

产物在 `dist\SkinChanceMod\`，并打包成 `dist\SkinChanceMod-1.1.0.zip` 与 `dist\skins.zip`（136 个空文件夹 + 使用说明）。

`tools\` 下是可重跑的生成脚本：`gen_skin_defaults.py`（从 `skins.json` 生成 `SkinDefaults.cs`）、`gen_demo_config.py`（生成全量配置）、`make_skins_zip.py`（打 `skins.zip`）。

## 本版核对事实

- 游戏 SDK：3.8.7；清单 `sdkVersion` 写 `3.8.1`。
- 反编译核对：`UIBattleInfoPanel.cs:41/355`、`UICom_HeroSkin.cs`、`BattleInfoPanel.cs:529`、`CommonUIManager.cs:393`、`BattleProperty.cs:22/24`、`GLoader.cs:41/295/505`、`NGraphics.cs:150`。
- 数据抽取：`Character.bin` 字段 27 = 立绘组，`Skin.bin` 顶层字段 1 = `StandingPaintings`，条目字段 3 = `ItemID`、字段 7 = `Character`（局内立绘）、字段 15 = `Bust`（左下图集），皮肤名取 `ItemInfoConfigure.NameID` → `STRItem`。
- 数据源与版本：`Version.bin` = `3.3.0`（`server-repo\data\gamedata`，2026-10-16 快照，38 角色 / 136 条皮肤）。旧的 3.2.0 快照（36 角色 / 131 条）缺 `100117006` 等新皮肤，**已弃用**；客户端 Addressables 目录同样已是 `catalog_3.3.0`。
- 缺档退化：`SkinRules.Candidates` 命中档 → 更完好 → 更残，离线单测 39 项全过（`_skintest`）。

## 更新日志

- **1.1.0**：默认配置展开全部 136 张皮肤（三档 100/60/25），启动时自动建好 `skins\<ItemID>_<皮肤名>\` 文件夹，并随 Release 提供 `skins.zip`；缺图时按「更完好 → 更残」退化，用户只要放图即可用。
- **1.0.1**：`SKINS.md` / `skins.json` 改用 3.3.0 数据重新抽取（36→38 角色、131→136 条皮肤，补上 `100117006 钢羽森姬-茉莉` 等）；模组代码无改动。
- **1.0.0**：首个版本，按 HP 百分比切换局内左下角自己的立绘。

## 已知边界

- 只处理**自己**的立绘；观战、结算、房间、主页等其它地方的立绘不碰（那里走 `GetDefeatStandingPaintingById`，不是同一套接口）。
- 立绘是视频且 `hideFightVideo=false` 时接管不到（视频是 `GGraph` 播放，不是贴图）。
- 游戏重设 `loader_Skin_Image.url` 时不会打断接管，但如果立绘整体重建（换人、重开一局），下一轮轮询会重新接管。
- 只支持 PNG；WebP/JPG 需要自己转。

## 许可

MIT，见 [LICENSE](LICENSE)。
