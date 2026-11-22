# 战损立绘 · SkinChanceMod

吉星派对（Astral Party）CesiumLoader 模组：**局内左下角的角色立绘，按当前血量百分比自动换成配置里的贴图**，做成战损效果。

- 只影响**自己**的立绘，队友和敌人不受影响。
- 按**皮肤**（`ItemID`）配置，同一角色穿不同皮肤可以配不同的一套图。
- **每张皮肤一个文件夹，文件夹里有自己的 `config.json`**：阈值、图名、缩放、偏移都在里面改，改完保存就生效（热重载，不用重进游戏）。
- **全部 135 张皮肤的文件夹和 `config.json` 都已预置好，你只要把 PNG 丢进去**。
- **局内按 F12 打开编辑器**，直接拖动立绘调大小和位置，点「保存到配置」写回该皮肤的 `config.json`，点「恢复默认」回到 1 倍无偏移。

> 资源名对照表见 [SKINS.md](SKINS.md)：按最新 GameData（3.3.0）抽取，共 37 个角色、135 条皮肤立绘，`皮肤 ItemID` 就是文件夹名前缀。
>
> **只想会用、不想看原理** → 看 [使用说明.md](使用说明.md)（装在哪、怎么放图、`config.json` 每个键什么意思、常见配方、排查表）。

## 安装

1. 下载 Release 里的 `SkinChanceMod-1.3.1.zip`，解压到 `Astral Party\AstralParty_ModLoader\mods\SkinChanceMod\`，应当能看到 `SkinChanceMod.dll`、`SkinChanceMod.json`、`icon.png`、`SKINS.md`、`使用说明.md`。
2. 再下载 `skins.zip`，把里面的 `skins\` 解压到同一个模组目录（135 个文件夹，每个文件夹里有一个 `config.json`）。
3. 启动游戏，选人进局。

> 第 2 步也可以跳过：首次启动会自动建好 135 个文件夹并给每个写一份默认 `config.json`。

## 使用

**只要放图；要调手感才改 `config.json`，或者局内按 F12 直接拖。**

### 局内编辑器（F12）

进对局后左下角立绘出现时，按 **F12** 弹出编辑器：

| 操作 | 效果 |
|---|---|
| 在立绘上按住拖动 | 上下左右移动（改 `offsetX` / `offsetY`） |
| 按住 **Ctrl** 在立绘上拖动 | 上拖放大、下拖缩小（改 `scale`） |
| 「缩小 / 放大 / ← → ↑ ↓」按钮 | 每次 ±0.05 缩放、±10 像素偏移，方便微调 |
| 「保存到配置」 | 把当前数值写回该皮肤文件夹的 `config.json`，并立刻热重载 |
| 「恢复默认」 | 缩放回 1、偏移回 0（要再点保存才落盘） |
| 再按一次 F12 / 点「关闭」 | 收起编辑器，立绘交还给游戏 |

编辑器改的是**当前血量命中的那一档**。想让满血也用新图，就把那一档的 `image` 从 `"0"` 改成你的文件名。

F12 可以在模组根的 `config.json` 里用 `editKey` 改（写 Unity `KeyCode` 名称，写 `None` 停用）。

```
mods\SkinChanceMod\skins\
  100101001_初始皮肤-帕露南\
    config.json
    1.png     ← 受伤
    2.png     ← 重伤
  100117006_钢羽森姬-茉莉\
    config.json
    1.png
    2.png
```

每个文件夹里的 `config.json`：

```json
{
  "itemId": 100117006,
  "name": "100117006_钢羽森姬-茉莉",
  "stages": [
    { "hpPercent": 100, "image": "0",     "scale": 1.0, "offsetX": 0, "offsetY": 0 },
    { "hpPercent": 60,  "image": "1.png", "scale": 1.0, "offsetX": 0, "offsetY": 0 },
    { "hpPercent": 25,  "image": "2.png", "scale": 1.0, "offsetX": 0, "offsetY": 0 }
  ]
}
```

| 键 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `itemId` | int | 文件夹名前缀 | 皮肤编号，见 SKINS.md；写 0 时用文件夹名的数字前缀。 |
| `name` | string | 文件夹名 | 备注，只影响日志。 |
| `stages[].hpPercent` | int | 100 / 60 / 25 | 这一档管的**血量上限**（0–100）。 |
| `stages[].image` | string | `"0"` / `"1.png"` / `"2.png"` | 文件名；**写 `"0"` 表示保留游戏原图**；只写 `"1"` 会自动补成 `1.png`；也可写子路径。 |
| `stages[].scale` | float | `1.0` | 缩放倍数，1 = 原图大小。 |
| `stages[].offsetX` | float | `0` | 右移像素（负数左移）。 |
| `stages[].offsetY` | float | `0` | 下移像素（负数上移）。 |

文件名随你起，`image` 写什么就读什么；档位顺序也随便写，模组会自己按 `hpPercent` 排序。

**缺图会自动退化**：某档的图不存在时，先试「更完好」的档，再试「更残」的档。所以最省事的用法是把一张受伤图命名为 `1.png`、一张重伤图命名为 `2.png`。

### 档位怎么命中

`hpPercent` 是**上限**，取「不小于当前血量百分比」里最低的那一档：

| 当前血量 | 命中 |
|---|---|
| 61%–100% | `image: "0"`（原图） |
| 26%–60% | `1.png` |
| 0%–25% | `2.png` |

想满血也换图，把第一档的 `image` 改成你自己的文件名即可；想某档不换，就写 `"0"`。

### 贴图要求

- 只支持 **PNG**（8 bit，非隔行，colorType 0/2/3/4/6），带透明通道没问题。
- 尺寸随意，会按游戏原本的立绘框缩放（沿用原 loader 的 `fill`/`align`）；画面对不上就用 `scale` / `offsetX` / `offsetY` 微调。
- 建议和游戏原图同尺寸（左下角立绘是 `UT_Hero_Card_*`，常见 1261×2048）。
- 立绘资源名见 SKINS.md 的「左下立绘」列，可从 `StreamingAssets\aa` 里用 AssetStudio/UnityPy 抽出来改。
- **注意别拿 `UT_Hero_Bust_*`**：那是「发起 PK」弹窗里的小图（`FightWindow.cs:888`），不是左下角立绘。

## 原理

- 左下角立绘是 `UIBattleInfoPanel.loader_Skin`（`GetChildAt(10)`），实际类型 `UICom_HeroSkin`，里面两个子对象：`loader_Skin_Image`（静态图，`GLoader`）与 `loader_Skin_Video`（视频，`GGraph`）。
- 游戏唯一的刷新入口是 `BattleInfoPanel.RefreshHeroSkin()` → `CommonUIManager.RendererSkin(...)`，它会直接改 `loader_Skin_Image.url`。**改 url 一定被顶掉**，所以本模组不动它。
- 做法：在 `UICom_HeroSkin` 里按原图的位置和尺寸加一个自己的 `GLoader`（`SkinChanceOverlay`）盖上去，原 `loader_Skin_Image` 只 `visible = false`；撤掉接管时把 `visible` 还回去。
- 覆盖层的 `fill`/`align`/`verticalAlign`/`shrinkOnly` **完全照抄原 `loader_Skin_Image`**，并把原 loader 的 `customOffset`/`customScale` 作为基准，再叠加配置里的 `scale`/`offsetX`/`offsetY` —— 否则宽高比会和原图不一致（看起来被拉扁）。几何没变化时不碰 FairyGUI，避免每帧重设尺寸/偏移导致合批重建（切档时卡一下）。
- 贴图解码走后台线程（`PngDecoder` + 逐行翻转），主线程只做 `Texture2D.LoadRawTextureData` 上传；进入对局时会把当前皮肤所有档位提前排进解码队列，掉血切档不会现场读盘。缺图/解码失败每 5 秒重试一次，中途补图也能生效。
- 每张皮肤的规则来自 `skins\<文件夹>\config.json`，模组默认每 2 秒按文件时间戳检查一次有没有改动（`reloadSeconds` 可调），改完保存即可，无需重启；热重载只换规则，不清贴图缓存。
- 当前皮肤来自 `GameLogicManager.inst.battle.GetSelfPlayerData().player.standingPainting.ItemID`；血量来自 `BattlePlayerData.Property.HP.Value` 与 `Property.maxHP`。
- 面板通过反射 `UIManager._cachedPanelDict[UIPanelType.BattleInfo]` 取，不会因为轮询而提前构造面板。
- 贴图解码不依赖 `ImageConversion.LoadImage`（HybridCLR 下不一定能反射到），自带一个纯托管 PNG 解码器（`PngDecoder`），解出 RGBA 后逐行垂直翻转再 `LoadRawTextureData`。

## 验证

日志前缀 `[SkinChanceMod]`，正常接管时会看到：

```
[SkinChanceMod] 已建好 135 个皮肤文件夹：...\mods\SkinChanceMod\skins
[SkinChanceMod] 已补上 135 份默认 config.json。
[SkinChanceMod] 战损立绘已加载：共 135 条皮肤规则。
[SkinChanceMod] 当前皮肤：ItemID 100117006（立绘 UT_Hero_Card_117_04），规则 135 条里命中 3 档候选。
[SkinChanceMod] 已加载贴图 1261x2048：...\skins\100117006_钢羽森姬-茉莉\1.png
[SkinChanceMod] 立绘已切换：1.png（HP 48%）
[SkinChanceMod] 立绘已切换：游戏原图（HP 100%）
[SkinChanceMod] 皮肤配置已更新：共 135 条规则。
[SkinChanceMod] 编辑器已打开：100117006_钢羽森姬-茉莉 第 3 档（1.png）。
[SkinChanceMod] 立绘已调整：缩放 1.05，偏移 12,-8（点保存写回配置）。
[SkinChanceMod] 已保存：config.json 第 3 档（缩放 1.05，偏移 12,-8）。
[SkinChanceMod] 编辑器已关闭。
```

没反应时依次看：`enabled` 是否 true、日志里 `当前皮肤：ItemID ...` 那个编号有没有对应文件夹、`config.json` 里的 `image` 指向的文件在不在、有没有「找不到贴图 / 解码失败 / 读不懂」告警。看到 `当前皮肤未配置（ItemID …）` 就说明这张皮肤不在 `SKINS.md` 里（例如新角色），需要手动建 `skins\<ItemID>_<名字>\config.json`。

## 构建

需要 .NET SDK 与 `lib\CesiumLoader.SDK.dll`、`refs\game\*.dll`（已在仓库里）。

```powershell
.\build.ps1
```

产物在 `dist\SkinChanceMod\`，并打包成 `dist\SkinChanceMod-1.3.1.zip` 与 `dist\skins.zip`（135 个文件夹，每个含 `config.json` + 使用说明）。

`tools\` 下是可重跑的生成脚本：`gen_skins_catalog.py`（从 GameData 生成 `SKINS.md` / `skins.json`）、`gen_skin_defaults.py`（从 `skins.json` 生成 `SkinDefaults.cs`）、`make_skins_zip.py`（打 `skins.zip`）。

## 本版核对事实

- 游戏 SDK：3.8.7；清单 `sdkVersion` 写 `3.8.1`。
- 反编译核对：`UIBattleInfoPanel.cs:41/355`、`UICom_HeroSkin.cs`、`BattleInfoPanel.cs:529`、`CommonUIManager.cs:393`、`BattleProperty.cs:22/24`、`GLoader.cs:41/295/505`、`NGraphics.cs:150`。
- 半身取景：原版靠 `Skin.bin` 顶层 `Offsets`（`SkinOffsetConfigure.CharacterStateSkinOffsetX/Y` + `CharacterStateSkinScale`）把 1261×2048 的全身图裁成半身，运行时经 `HeroSkinOffset`（`bustSkinOffset`/`bustSkinScale`）交给 `CommonUIManager.RendererSkin`；`UT_Hero_Card_117_04` 的取值是 offset `(95, 135)`、scale `0.66`。`GLoader.UpdateLayout()`（`GLoader.cs:561`）才真正应用 `customOffset`/`customScale`，而且只在设 `url`/`texture`/尺寸时跑 —— **先赋贴图再写偏移等于没写**。
- 数据抽取：`Character.bin` 字段 27 = 立绘组，`Skin.bin` 顶层字段 1 = `StandingPaintings`，条目字段 3 = `ItemID`、字段 7 = `Character`（**左下角立绘**，`BattleInfoPanel.RefreshHeroSkin()` 就用它）、字段 15 = `Bust`（PK 弹窗小图，别混），皮肤名取 `ItemInfoConfigure.NameID` → `STRItem`。
- 数据源与版本：`Version.bin` = `3.3.0`（`server-repo\data\gamedata`，2026-10-16 快照，37 角色 / 135 条皮肤）。旧的 3.2.0 快照（36 角色 / 131 条）缺 `100117006` 等新皮肤，**已弃用**。`9001 天川苍鲤` 是另一个项目的 DIY 角色（立绘复用 `UT_Hero_Card_307`），已从对照表排除；`307` / `308` 是正式角色占位，保留。
- 缺档退化：`SkinRules.Candidates` 命中档 → 更完好 → 更残；离线单测 231 项全过（`_skintest`，覆盖规则、原图判定、编辑器下标、目录读取、`SaveStages` 往返、默认值与越界夹取）。
- 原图判定：`image` 只认裸 `"0"` 为原图；`"0.png"` 是文件名（有人会把图命名成 0.png），不会被当成原图吃掉。
- 编辑器：`SkinRules.HitIndex` 返回的是 **`rule.stages` 里的下标**（不是排序后的顺序下标），编辑器靠它直接改用户写的那一档；`GLoader` 没有 `opaque` 属性（`opaque` 在 `GComponent`/`Container` 上），立绘拖动靠 `touchable` + `onTouchBegin/Move/End`。注意游戏在 `BattleInfoPanel.InitTouchable()`（`BattleInfoPanel.cs:333-337`）里写死 `loader_Skin.touchableAll = false`，`DisplayObject.InternalHitTest()`（`DisplayObject.cs:1245`）是「父级不可触摸就不下探子对象」，所以编辑器打开时必须临时放开宿主组件（1.3.1）。

## 更新日志

- **1.3.1**：①修编辑器**拖动立绘收不到事件**的问题 —— 游戏在 `BattleInfoPanel.InitTouchable()` 里把 `loader_Skin.touchableAll = false`，而 FairyGUI 的 `DisplayObject.InternalHitTest()` 是「父级不可触摸就不下探子对象」，所以只把覆盖层设成 `touchable` 没用；现在打开编辑器时临时把宿主 `UICom_HeroSkin` 的 `touchable` 放开，关闭时再还原。②对照表重抽并排除 DIY 角色 `9001 天川苍鲤`（立绘复用 `UT_Hero_Card_307`，不是正式皮肤），135 条；`307`/`308` 保留。③新增「当前皮肤」日志（`当前皮肤：ItemID …（立绘 …），规则 … 条里命中 … 档候选` / `当前皮肤未配置（ItemID …）`），换皮肤就能直接从日志看出摸到了哪张皮、有没有对应文件夹。
- **1.3.0**：①修 `image: "0.png"` 被判成「游戏原图」的 bug（现在只认裸 `"0"`）；②新增局内**立绘编辑器** —— 默认 **F12** 打开，在立绘上拖动改位置、按住 Ctrl 拖动改缩放、也可用按钮微调，点「保存到配置」直接写回该皮肤的 `config.json`，点「恢复默认」回到 1 倍无偏移；③模组根配置新增 `editKey`（默认 `"F12"`，写 `None` 停用）。
- **1.2.4**：换新图标；新增 [使用说明.md](使用说明.md)（面向使用者的完整手册：安装、找皮肤编号、`config.json` 逐键说明、档位命中区间、缺图退化、缩放偏移调法、常见配方、排查表），并随 Release 包分发。代码无行为改动。
- **1.2.3**：修 1.2.2 里兜底取景**根本没生效**的问题 —— 兜底要用游戏自己的资源名（`UT_Hero_Card_117_04`，取自 `standingPainting.GetCharacterInGame()`）去查 `heroSkinOffsetDict`，之前误用了我们自己的文件名（`1.png` → `"1"`），永远匹配不上，所以「半身」还是没对上。现在按本局真实资源名兜底，并在日志里打印用到的偏移/缩放。
- **1.2.2**：修「立绘尺寸不对、不是半身」——根因是 `GLoader.UpdateLayout()` 才是真正应用 `customOffset`/`customScale` 的地方（只在设 `url`/`texture`/尺寸时跑），原来先赋贴图后写偏移，偏移永远不生效，等于把 1261×2048 的全身图整张铺上去。现在改为先铺几何再给贴图，并在几何变化后主动重排一次；原 loader 尚未被游戏设过几何时，用游戏自己的 `Skin.bin` Offsets 表（`heroSkinOffsetDict`，`CharacterStateSkinOffsetX/Y` + `CharacterStateSkinScale`）兜底，和原版半身取景一致。
- **1.2.1**：修两处体验问题 —— ①切档时不再卡一下：贴图解码挪到后台线程、进对局提前预解码该皮肤全部档位、几何无变化时不碰 FairyGUI（原来每 0.2 秒重设一次尺寸/偏移会导致合批重建）；②不再被拉扁：覆盖层的 `fill`/`align`/`verticalAlign`/`shrinkOnly` 改为照抄原 `loader_Skin_Image`，并把原 loader 的 `customOffset`/`customScale` 当基准再叠加配置值。另外缺图/解码失败改为每 5 秒重试，中途补图也能生效。
- **1.2.0**：改成**每张皮肤一个 `config.json`**（`skins\<ItemID>_<皮肤名>\config.json`）；支持 `image: "0"` 表示原图、只写数字自动补 `.png`、每档独立的 `scale`/`offsetX`/`offsetY`；模组每 2 秒按文件时间戳热重载各皮肤配置（`reloadSeconds` 可调）；模组根配置只剩 `enabled`/`hideFightVideo`/`reloadSeconds` 三个开关。
- **1.1.0**：默认配置展开全部 135 张皮肤（三档 100/60/25），启动时自动建好 `skins\<ItemID>_<皮肤名>\` 文件夹，并随 Release 提供 `skins.zip`；缺图时按「更完好 → 更残」退化。
- **1.0.1**：`SKINS.md` / `skins.json` 改用 3.3.0 数据重新抽取（36→38 角色、131→136 条皮肤，补上 `100117006 钢羽森姬-茉莉` 等）；模组代码无改动。
- **1.0.0**：首个版本，按 HP 百分比切换局内左下角自己的立绘。

## 已知边界

- 只处理**自己**的立绘；观战、结算、房间、主页等其它地方的立绘不碰（那里走 `GetDefeatStandingPaintingById`，不是同一套接口）。
- 立绘是视频且 `hideFightVideo=false` 时接管不到（视频是 `GGraph` 播放，不是贴图）。
- 编辑器改的是**当前血量命中的那一档**；命中档是「游戏原图」时，编辑器里的缩放/偏移不会立刻可见（原图没有可叠的覆盖层尺寸），把 `image` 改成真图名后才看得到效果。
- 游戏重设 `loader_Skin_Image.url` 时不会打断接管，但如果立绘整体重建（换人、重开一局），下一轮轮询会重新接管。
- 只支持 PNG；WebP/JPG 需要自己转。
- `config.json` 用的是普通 JSON，注释不支持（SDK 的解析器不认 `//`）。

## 许可

MIT，见 [LICENSE](LICENSE)。
