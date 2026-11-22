# 战损立绘 · SkinChanceMod

吉星派对（Astral Party）CesiumLoader 模组：**局内左下角的角色立绘，按当前血量百分比自动换成配置里的贴图**，做成战损效果。

- 只影响**自己**的立绘，队友和敌人不受影响。
- 按**皮肤**（`ItemID`）配置，同一角色穿不同皮肤可以配不同的一套图。
- 一张图一个档位，血量掉到阈值以下就换下一档；未配置的皮肤完全不动。
- 贴图放在模组目录里，改 JSON 立即生效，不用重开游戏。

> 资源名对照表见 [SKINS.md](SKINS.md)：按最新 GameData（3.3.0）抽取，共 38 个角色、136 条皮肤立绘，`皮肤 ItemID` 就是配置里要填的 `itemId`。

## 安装

1. 下载 Release 里的 `SkinChanceMod-1.0.1.zip`。
2. 解压到 `Astral Party\AstralParty_ModLoader\mods\SkinChanceMod\`，应当能看到 `SkinChanceMod.dll`、`SkinChanceMod.json`、`icon.png`、`SKINS.md`。
3. 在模组目录里建一个 `skins\` 子目录，把你的 PNG 丢进去（也可以是任意相对路径，见下）。
4. 启动游戏，选人进局。

## 使用

打开配置文件（新版加载器在 `AstralParty_ModLoader\configs\SkinChanceMod.json`，老版在 `mods\SkinChanceMod\config.json`），首次启动会自动补全所有键和多语言说明。

```json
{
  "enabled": true,
  "hideFightVideo": true,
  "characters": [
    {
      "itemId": 100101001,
      "stages": [
        { "hpPercent": 60, "image": "skins/101_damaged.png" },
        { "hpPercent": 30, "image": "skins/101_broken.png" }
      ]
    }
  ]
}
```

| 键 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `enabled` | bool | `true` | 总开关，关掉立刻恢复游戏原立绘。 |
| `hideFightVideo` | bool | `true` | 立绘是视频时强制切静态图并停掉视频，否则接管不到。 |
| `characters` | array | `[]` | 每个元素 `{ itemId, stages[] }`，`itemId` 见 SKINS.md。 |
| `characters[].stages[].hpPercent` | int | — | 这一档的**血量百分比上限**（1–100）。 |
| `characters[].stages[].image` | string | — | PNG 路径，相对模组目录（也可写绝对路径或用 `skins/` 前缀）。 |

### 档位怎么命中

`hpPercent` 是**上限**，取「不小于当前血量百分比」里最低的那一档：

| 当前血量 | 命中 |
|---|---|
| 61%–100% | 不接管，显示游戏原图 |
| 31%–60% | `101_damaged.png` |
| 0%–30% | `101_broken.png` |

也就是说：**最满血的那张不用配**，配置的第一档就是「开始受伤」的门槛。档位顺序随便写，模组会自己排序。

### 贴图要求

- 只支持 **PNG**（8 bit，非隔行，colorType 0/2/3/4/6），带透明通道没问题。
- 尺寸随意，会按游戏原本的立绘框缩放（沿用原 loader 的 `fill`/`align`/`customOffset`/`customScale`）。
- 建议和游戏原图同尺寸，避免缩放糊掉。

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
[SkinChanceMod] 战损立绘已加载：共 1 条皮肤规则。
[SkinChanceMod] 已加载贴图 1024x1024：...\mods\SkinChanceMod\skins\101_damaged.png
[SkinChanceMod] 战损立绘已接管：皮肤 100101001 → skins/101_damaged.png（HP 58%）。
[SkinChanceMod] 立绘已切换：101_broken.png（HP 27%）
```

没反应时依次看：`enabled` 是否为 true、`itemId` 是否和当局皮肤一致（日志里那串数字）、图片路径是否存在、日志有没有「找不到贴图 / 解码失败」。

## 构建

需要 .NET SDK 与 `lib\CesiumLoader.SDK.dll`、`refs\game\*.dll`（已在仓库里）。

```powershell
.\build.ps1
```

产物在 `dist\SkinChanceMod\`，并打包成 `dist\SkinChanceMod-1.0.1.zip`。

## 本版核对事实

- 游戏 SDK：3.8.7；清单 `sdkVersion` 写 `3.8.1`。
- 反编译核对：`UIBattleInfoPanel.cs:41/355`、`UICom_HeroSkin.cs`、`BattleInfoPanel.cs:529`、`CommonUIManager.cs:393`、`BattleProperty.cs:22/24`、`GLoader.cs:41/295/505`、`NGraphics.cs:150`。
- 数据抽取：`Character.bin` 字段 27 = 立绘组，`Skin.bin` 顶层字段 1 = `StandingPaintings`，条目字段 3 = `ItemID`、字段 7 = `Character`（局内立绘）、字段 15 = `Bust`（左下图集），皮肤名取 `ItemInfoConfigure.NameID` → `STRItem`。
- 数据源与版本：`Version.bin` = `3.3.0`（`server-repo\data\gamedata`，2026-10-16 快照，38 角色 / 136 条皮肤）。旧的 3.2.0 快照（36 角色 / 131 条）缺 `100117006` 等新皮肤，**已弃用**；客户端 Addressables 目录同样已是 `catalog_3.3.0`。

## 更新日志

- **1.0.1**：`SKINS.md` / `skins.json` 改用 3.3.0 数据重新抽取（36→38 角色、131→136 条皮肤，补上 `100117006 钢羽森姬-茉莉` 等）；模组代码无改动。
- **1.0.0**：首个版本，按 HP 百分比切换局内左下角自己的立绘。

## 已知边界

- 只处理**自己**的立绘；观战、结算、房间、主页等其它地方的立绘不碰（那里走 `GetDefeatStandingPaintingById`，不是同一套接口）。
- 立绘是视频且 `hideFightVideo=false` 时接管不到（视频是 `GGraph` 播放，不是贴图）。
- 游戏重设 `loader_Skin_Image.url` 时不会打断接管，但如果立绘整体重建（换人、重开一局），下一轮轮询会重新接管。
- 只支持 PNG；WebP/JPG 需要自己转。

## 许可

MIT，见 [LICENSE](LICENSE)。
