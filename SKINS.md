# 角色 / 皮肤 / 立绘资源对照表

自动从客户端 GameData 抽取（`Character.bin` 字段 27 = 立绘组，`Skin.bin` 的 `StandingPaintings`，皮肤名取 `ItemInfoConfigure.NameID` → `STRItem`）。

- 配置里的 `itemId` 填本表「皮肤 ItemID」；未配置的皮肤不会被动。
- **左下角立绘** = `SkinStandingPaintingConfigureItem.Character`（`BattleInfoPanel.RefreshHeroSkin()` → `GetCharacterInGame()`，`InGameCharacter` 全表只有 1 条非空，实际都走 `Character`）；AngelMode 下优先用同名的 `_sfw` 变体。
- `Bust` 字段不是左下角立绘：只有 `FightWindow.cs:888` 的「发起 PK」图标用它（按金币阈值取 `[0]`/`[1]`），不要拿它当战损图。
- 同一角色有多张皮肤，**ItemID 才是唯一键**（heroId 只用来分组）。

| 角色 | 角色ID | 立绘组 | 皮肤 ItemID | 皮肤名 | 默认 | 左下立绘 | PK 图集 |
|---|---|---|---|---|---|---|---|
| 帕露南 | 101 | 101 | 100101001 | 初始皮肤-帕露南 | 是 | UT_Hero_Card_101 | UT_Hero_Bust_101_0 / UT_Hero_Bust_101_1 |
| 帕露南 | 101 | 101 | 100101002 | 羁绊皮肤-帕露南 |  | UT_Hero_Card_101_Max | UT_Hero_Bust_101_Max_0 / UT_Hero_Bust_101_Max_1 |
| 帕露南 | 101 | 101 | 100101003 | 时尚达人-帕露南 |  | UT_Hero_Card_101_01 | UT_Hero_Bust_101_01_0 / UT_Hero_Bust_101_01_1 |
| 帕露南 | 101 | 101 | 100101004 | 善财天官-帕露南 |  | UT_Hero_Card_101_02 | UT_Hero_Bust_101_02_0 / UT_Hero_Bust_101_02_1 |
| 芬妮 | 102 | 102 | 100102001 | 初始皮肤-芬妮 | 是 | UT_Hero_Card_102 | UT_Hero_Bust_102_0 / UT_Hero_Bust_102_1 |
| 芬妮 | 102 | 102 | 100102002 | 羁绊皮肤-芬妮 |  | UT_Hero_Card_102_Max | UT_Hero_Bust_102_Max_0 / UT_Hero_Bust_102_Max_1 |
| 芬妮 | 102 | 102 | 100102003 | 清凉盛夏-芬妮 |  | UT_Hero_Card_102_01 | UT_Hero_Bust_102_01_0 / UT_Hero_Bust_102_01_1 |
| 芬妮 | 102 | 102 | 100102004 | 超然解析-芬妮 |  | UT_Hero_Card_102_02 | UT_Hero_Bust_102_02_0 / UT_Hero_Bust_102_02_1 |
| 芬妮 | 102 | 102 | 100102005 | 科学怪探-芬妮 |  | UT_Hero_Card_102_03 | UT_Hero_Bust_102_03_0 / UT_Hero_Bust_102_03_1 |
| 芬妮 | 102 | 102 | 100102006 | 悠闲假日-芬妮 |  | UT_Hero_Card_102_04 | UT_Hero_Bust_102_04_0 / UT_Hero_Bust_102_04_1 |
| 阿兰娜 | 103 | 103 | 100103001 | 初始皮肤-阿兰娜 | 是 | UT_Hero_Card_103 | UT_Hero_Bust_103_0 / UT_Hero_Bust_103_1 |
| 阿兰娜 | 103 | 103 | 100103002 | 羁绊皮肤-阿兰娜 |  | UT_Hero_Card_103_Max | UT_Hero_Bust_103_Max_0 / UT_Hero_Bust_103_Max_1 |
| 阿兰娜 | 103 | 103 | 100103003 | 悸动沙滩-阿兰娜 |  | UT_Hero_Card_103_01 | UT_Hero_Bust_103_01_0 / UT_Hero_Bust_103_01_1 |
| 阿兰娜 | 103 | 103 | 100103004 | 冒失少女-阿兰娜 |  | UT_Hero_Card_103_02 | UT_Hero_Bust_103_02_0 / UT_Hero_Bust_103_02_1 |
| 阿兰娜 | 103 | 103 | 100103005 | 魔女兵器-阿兰娜 |  | UT_Hero_Card_103_03 | UT_Hero_Bust_103_03_0 / UT_Hero_Bust_103_03_1 |
| 小町 | 104 | 104 | 100104001 | 初始皮肤-小町 | 是 | UT_Hero_Card_104 | UT_Hero_Bust_104_0 / UT_Hero_Bust_104_1 |
| 小町 | 104 | 104 | 100104002 | 羁绊皮肤-小町 |  | UT_Hero_Card_104_Max | UT_Hero_Bust_104_Max_0 / UT_Hero_Bust_104_Max_1 |
| 小町 | 104 | 104 | 100104003 | 忍者为何？-小町 |  | UT_Hero_Card_104_01 | UT_Hero_Bust_104_01_0 / UT_Hero_Bust_104_01_1 |
| 小町 | 104 | 104 | 100104004 | 暗影狼灵-小町 |  | UT_Hero_Card_104_02 | UT_Hero_Bust_104_02_0 / UT_Hero_Bust_104_02_1 |
| 派德曼 | 105 | 105 | 100105001 | 初始皮肤-派德曼 | 是 | UT_Hero_Card_105 | UT_Hero_Bust_105_0 / UT_Hero_Bust_105_1 |
| 派德曼 | 105 | 105 | 100105002 | 羁绊皮肤-派德曼 |  | UT_Hero_Card_105_Max | UT_Hero_Bust_105_Max_0 / UT_Hero_Bust_105_Max_1 |
| 派德曼 | 105 | 105 | 100105003 | 酷暑难耐-派德曼 |  | UT_Hero_Card_105_01 | UT_Hero_Bust_105_01_0 / UT_Hero_Bust_105_01_1 |
| 帕帕拉 | 106 | 106 | 100106001 | 初始皮肤-帕帕拉 | 是 | UT_Hero_Card_106 | UT_Hero_Bust_106_0 / UT_Hero_Bust_106_1 |
| 帕帕拉 | 106 | 106 | 100106002 | 羁绊皮肤-帕帕拉 |  | UT_Hero_Card_106_Max | UT_Hero_Bust_106_Max_0 / UT_Hero_Bust_106_Max_1 |
| 帕帕拉 | 106 | 106 | 100106003 | 护卫队长-帕帕拉 |  | UT_Hero_Card_106_01 | UT_Hero_Bust_106_01_0 / UT_Hero_Bust_106_01_1 |
| 帕帕拉 | 106 | 106 | 100106004 | 入夏露营-帕帕拉 |  | UT_Hero_Card_106_02 | UT_Hero_Bust_106_02_0 / UT_Hero_Bust_106_02_1 |
| 恋 | 107 | 107 | 100107001 | 初始皮肤-恋 | 是 | UT_Hero_Card_107 | UT_Hero_Bust_107_0 / UT_Hero_Bust_107_1 |
| 恋 | 107 | 107 | 100107002 | 羁绊皮肤-恋 |  | UT_Hero_Card_107_Max | UT_Hero_Bust_107_Max_0 / UT_Hero_Bust_107_Max_1 |
| 恋 | 107 | 107 | 100107003 | 拉拉队-恋 |  | UT_Hero_Card_107_01 | UT_Hero_Bust_107_01_0 / UT_Hero_Bust_107_01_1 |
| 恋 | 107 | 107 | 100107004 | 可爱船长-恋 |  | UT_Hero_Card_107_02 | UT_Hero_Bust_107_02_0 / UT_Hero_Bust_107_02_1 |
| 恋 | 107 | 107 | 100107005 | 泳池派对-恋 |  | UT_Hero_Card_107_03 | UT_Hero_Bust_107_03_0 / UT_Hero_Bust_107_03_1 |
| 恋 | 107 | 107 | 100107006 | 雪地精灵-恋 |  | UT_Hero_Card_107_04 | UT_Hero_Bust_107_04_0 / UT_Hero_Bust_107_04_1 |
| 恋 | 107 | 107 | 100107007 | 卡牌魔术-恋 |  | UT_Hero_Card_107_05 | UT_Hero_Bust_107_05_0 / UT_Hero_Bust_107_05_1 |
| 米米 | 108 | 108 | 100108001 | 初始皮肤-米米 | 是 | UT_Hero_Card_108 | UT_Hero_Bust_108_0 / UT_Hero_Bust_108_1 |
| 米米 | 108 | 108 | 100108002 | 羁绊皮肤-米米 |  | UT_Hero_Card_108_Max | UT_Hero_Bust_108_Max_0 / UT_Hero_Bust_108_Max_1 |
| 米米 | 108 | 108 | 100108003 | 赛车女郎-米米 |  | UT_Hero_Card_108_01 | UT_Hero_Bust_108_01_0 / UT_Hero_Bust_108_01_1 |
| 米米 | 108 | 108 | 100108004 | 冰沙快递-米米 |  | UT_Hero_Card_108_02 | UT_Hero_Bust_108_02_0 / UT_Hero_Bust_108_02_1 |
| 米米 | 108 | 108 | 100108005 | 搬运魔法-米米 |  | UT_Hero_Card_108_03 | UT_Hero_Bust_108_03_0 / UT_Hero_Bust_108_03_1 |
| 米米 | 108 | 108 | 100108006 | 球队经理-米米 |  | UT_Hero_Card_108_04 | UT_Hero_Bust_108_04_0 / UT_Hero_Bust_108_04_1 |
| Z3000 | 109 | 109 | 100109001 | 初始皮肤-Z3000 | 是 | UT_Hero_Card_109 | UT_Hero_Bust_109_0 / UT_Hero_Bust_109_1 |
| Z3000 | 109 | 109 | 100109002 | 羁绊皮肤-Z3000 |  | UT_Hero_Card_109_Max_2 | UT_Hero_Bust_109_Max_0 / UT_Hero_Bust_109_Max_1 |
| Z3000 | 109 | 109 | 100109003 | 探天助手-Z3000 |  | UT_Hero_Card_109_01 | UT_Hero_Bust_109_01_0 / UT_Hero_Bust_109_01_1 |
| 潘大猛 | 110 | 110 | 100110001 | 初始皮肤-潘大猛 | 是 | UT_Hero_Card_110 | UT_Hero_Bust_110_0 / UT_Hero_Bust_110_1 |
| 潘大猛 | 110 | 110 | 100110002 | 羁绊皮肤-潘大猛 |  | UT_Hero_Card_110_Max | UT_Hero_Bust_110_Max_0 / UT_Hero_Bust_110_Max_1 |
| 墨影 | 111 | 111 | 100111001 | 初始皮肤-墨影 | 是 | UT_Hero_Card_111 | UT_Hero_Bust_111_0 / UT_Hero_Bust_111_1 |
| 墨影 | 111 | 111 | 100111002 | 羁绊皮肤-墨影 |  | UT_Hero_Card_111_Max | UT_Hero_Bust_111_Max_0 / UT_Hero_Bust_111_Max_1 |
| 璐璐 | 112 | 112 | 100112001 | 初始皮肤-璐璐 | 是 | UT_Hero_Card_112 | UT_Hero_Bust_112_0 / UT_Hero_Bust_112_1 |
| 璐璐 | 112 | 112 | 100112002 | 羁绊皮肤-璐璐 |  | UT_Hero_Card_112_Max | UT_Hero_Bust_112_Max_0 / UT_Hero_Bust_112_Max_1 |
| 璐璐 | 112 | 112 | 100112003 | 宝箱怪-璐璐 |  | UT_Hero_Card_112_01 | UT_Hero_Bust_112_01_0 / UT_Hero_Bust_112_01_1 |
| 璐璐 | 112 | 112 | 100112004 | 甜蜜陷阱-璐璐 |  | UT_Hero_Card_112_02 | UT_Hero_Bust_112_02_0 / UT_Hero_Bust_112_02_1 |
| 璐璐 | 112 | 112 | 100112005 | 拉拉队员-璐璐 |  | UT_Hero_Card_112_03 | UT_Hero_Bust_112_03_0 / UT_Hero_Bust_112_03_1 |
| 姬梦枫 | 113 | 113 | 100113001 | 初始皮肤-枫 | 是 | UT_Hero_Card_113 | UT_Hero_Bust_113_0 / UT_Hero_Bust_113_1 |
| 姬梦枫 | 113 | 113 | 100113002 | 羁绊皮肤-枫 |  | UT_Hero_Card_113_Max | UT_Hero_Bust_113_Max_0 / UT_Hero_Bust_113_Max_1 |
| 姬梦枫 | 113 | 113 | 100113003 | 战斗女仆-枫 |  | UT_Hero_Card_113_01 | UT_Hero_Bust_113_01_0 / UT_Hero_Bust_113_01_1 |
| 姬梦枫 | 113 | 113 | 100113004 | 锦跃游龙-枫 |  | UT_Hero_Card_113_02 | UT_Hero_Bust_113_02_0 / UT_Hero_Bust_113_02_1 |
| 姬梦枫 | 113 | 113 | 100113005 | 潮流公主-枫 |  | UT_Hero_Card_113_03 | UT_Hero_Bust_113_03_0 / UT_Hero_Bust_113_03_1 |
| 姬梦枫 | 113 | 113 | 100113006 | 晨暑惊眠-枫 |  | UT_Hero_Card_113_04 | UT_Hero_Bust_113_04_0 / UT_Hero_Bust_113_04_1 |
| 蓝海晴 | 114 | 114 | 100114001 | 初始皮肤-蓝海晴 | 是 | UT_Hero_Card_114 | UT_Hero_Bust_114_0 / UT_Hero_Bust_114_1 |
| 蓝海晴 | 114 | 114 | 100114002 | 羁绊皮肤-蓝海晴 |  | UT_Hero_Card_114_Max | UT_Hero_Bust_114_Max_0 / UT_Hero_Bust_114_Max_1 |
| 蓝海晴 | 114 | 114 | 100114003 | 变装派对-蓝海晴 |  | UT_Hero_Card_114_01 | UT_Hero_Bust_114_01_0 / UT_Hero_Bust_114_01_1 |
| 蓝海晴 | 114 | 114 | 100114004 | 列车长-蓝海晴 |  | UT_Hero_Card_114_02 | UT_Hero_Bust_114_02_0 / UT_Hero_Bust_114_02_1 |
| 蓝海晴 | 114 | 114 | 100114005 | 优雅身姿-蓝海晴 |  | UT_Hero_Card_114_03 | UT_Hero_Bust_114_03_0 / UT_Hero_Bust_114_03_1 |
| 蓝海晴 | 114 | 114 | 100114006 | 春日野餐-蓝海晴 |  | UT_Hero_Card_114_04 | UT_Hero_Bust_114_04_0 / UT_Hero_Bust_114_04_1 |
| 美咲 | 115 | 115 | 100115001 | 初始皮肤-美咲 | 是 | UT_Hero_Card_115 | UT_Hero_Bust_115_0 / UT_Hero_Bust_115_1 |
| 美咲 | 115 | 115 | 100115002 | 羁绊皮肤-美咲 |  | UT_Hero_Card_115_Max | UT_Hero_Bust_115_Max_0 / UT_Hero_Bust_115_Max_1 |
| 美咲 | 115 | 115 | 100115003 | 战斗女仆-美咲 |  | UT_Hero_Card_115_01 | UT_Hero_Bust_115_01_0 / UT_Hero_Bust_115_01_1 |
| 美咲 | 115 | 115 | 100115004 | 真龙御礼-龙崎美咲 |  | UT_Hero_Card_115_02 | UT_Hero_Bust_115_02_0 / UT_Hero_Bust_115_02_1 |
| 美咲 | 115 | 115 | 100115005 | 魔女兵器-美咲 |  | UT_Hero_Card_115_03 | UT_Hero_Bust_115_03_0 / UT_Hero_Bust_115_03_1 |
| 美咲 | 115 | 115 | 100115006 | 墨韵圆舞-美咲 |  | UT_Hero_Card_115_04 | UT_Hero_Bust_115_04_0 / UT_Hero_Bust_115_04_1 |
| 娜蒂斯 | 116 | 116 | 100116001 | 初始皮肤-娜蒂斯 | 是 | UT_Hero_Card_116 | UT_Hero_Bust_116_0 / UT_Hero_Bust_116_1 |
| 娜蒂斯 | 116 | 116 | 100116002 | 羁绊皮肤-娜蒂斯 |  | UT_Hero_Card_116_Max | UT_Hero_Bust_116_Max_0 / UT_Hero_Bust_116_Max_1 |
| 娜蒂斯 | 116 | 116 | 100116003 | 祭典礼服-娜蒂斯 |  | UT_Hero_Card_116_01 | UT_Hero_Bust_116_01_1 / UT_Hero_Bust_116_01_0 |
| 娜蒂斯 | 116 | 116 | 100116004 | 暮色魔女-娜蒂斯 |  | UT_Hero_Card_116_02 | UT_Hero_Bust_116_02_0 / UT_Hero_Bust_116_02_1 |
| 茉莉 | 117 | 117 | 100117001 | 初始皮肤-茉莉 | 是 | UT_Hero_Card_117 | UT_Hero_Bust_117_0 / UT_Hero_Bust_117_1 |
| 茉莉 | 117 | 117 | 100117002 | 羁绊皮肤-茉莉 |  | UT_Hero_Card_117_Max | UT_Hero_Bust_117_Max_0 / UT_Hero_Bust_117_Max_1 |
| 茉莉 | 117 | 117 | 100117003 | 节日盛装-茉莉 |  | UT_Hero_Card_117_01 | UT_Hero_Bust_117_01_0 / UT_Hero_Bust_117_01_1 |
| 茉莉 | 117 | 117 | 100117004 | 冲浪海岸-茉莉 |  | UT_Hero_Card_117_02 | UT_Hero_Bust_117_02_0 / UT_Hero_Bust_117_02_1 |
| 茉莉 | 117 | 117 | 100117005 | 后厨能手-茉莉 |  | UT_Hero_Card_117_03 | UT_Hero_Bust_117_03_0 / UT_Hero_Bust_117_03_1 |
| 茉莉 | 117 | 117 | 100117006 | 钢羽森姬-茉莉 |  | UT_Hero_Card_117_04 | UT_Hero_Bust_117_04_0 / UT_Hero_Bust_117_04_1 |
| 阿尔 | 118 | 118 | 100118001 | 初始皮肤-阿尔 | 是 | UT_Hero_Card_118 | UT_Hero_Bust_118_0 / UT_Hero_Bust_118_1 |
| 阿尔 | 118 | 118 | 100118002 | 羁绊皮肤-阿尔 |  | UT_Hero_Card_118_Max | UT_Hero_Bust_118_Max_0 / UT_Hero_Bust_118_Max_1 |
| 阿尔 | 118 | 118 | 100118003 | 番茄酱魔法-阿尔 |  | UT_Hero_Card_118_01 | UT_Hero_Bust_118_01_0 / UT_Hero_Bust_118_01_1 |
| 星魅琉华 | 119 | 119 | 100119001 | 初始皮肤-琉华 | 是 | UT_Hero_Card_119 | UT_Hero_Bust_119_0 / UT_Hero_Bust_119_1 |
| 星魅琉华 | 119 | 119 | 100119002 | 羁绊皮肤-琉华 |  | UT_Hero_Card_119_Max | UT_Hero_Bust_119_Max_0 / UT_Hero_Bust_119_Max_1 |
| 星魅琉华 | 119 | 119 | 100119003 | 转角之期-星魅琉华 |  | UT_Hero_Card_119_01 | UT_Hero_Bust_119_01_0 / UT_Hero_Bust_119_01_1 |
| 星魅琉华 | 119 | 119 | 100119004 | 特别礼物-琉华 |  | UT_Hero_Card_119_02 | UT_Hero_Bust_119_02_0 / UT_Hero_Bust_119_02_1 |
| 星魅琉华 | 119 | 119 | 100119005 | 仓栗骑士-琉华 |  | UT_Hero_Card_119_03 | UT_Hero_Bust_119_03_0 / UT_Hero_Bust_119_03_1 |
| 南希露 | 120 | 120 | 100120001 | 初始皮肤-南希露 | 是 | UT_Hero_Card_120 | UT_Hero_Bust_120_0 / UT_Hero_Bust_120_1 |
| 南希露 | 120 | 120 | 100120002 | 羁绊皮肤-南希露 |  | UT_Hero_Card_120_Max | UT_Hero_Bust_120_Max_0 / UT_Hero_Bust_120_Max_1 |
| 南希露 | 120 | 120 | 100120003 | 雍容盛景-南希露 |  | UT_Hero_Card_120_01 | UT_Hero_Bust_120_01_0 / UT_Hero_Bust_120_01_1 |
| 南希露 | 120 | 120 | 100120004 | 热浪沙滩-南希露 |  | UT_Hero_Card_120_02 | UT_Hero_Bust_120_02_0 / UT_Hero_Bust_120_02_1 |
| 南希露 | 120 | 120 | 100120005 | 餐前魔法-南希露 |  | UT_Hero_Card_120_03 | UT_Hero_Bust_120_03_0 / UT_Hero_Bust_120_03_1 |
| 南希露 | 120 | 120 | 100120006 | 装备党-南希露 |  | UT_Hero_Card_120_04 | UT_Hero_Bust_120_04_0 / UT_Hero_Bust_120_04_1 |
| 凛 | 121 | 121 | 100121001 | 初始皮肤-凛 | 是 | UT_Hero_Card_121 | UT_Hero_Bust_121_0 / UT_Hero_Bust_121_1 |
| 凛 | 121 | 121 | 100121002 | 羁绊皮肤-凛 |  | UT_Hero_Card_121_Max | UT_Hero_Bust_121_Max_0 / UT_Hero_Bust_121_Max_1 |
| 凛 | 121 | 121 | 100121003 | 海湾假日-凛 |  | UT_Hero_Card_121_01 | UT_Hero_Bust_121_01_0 / UT_Hero_Bust_121_01_1 |
| 凛 | 121 | 121 | 100121004 | 冬日暖意-凛 |  | UT_Hero_Card_121_02 | UT_Hero_Bust_121_02_0 / UT_Hero_Bust_121_02_1 |
| 凛 | 121 | 121 | 100121005 | 永恒圣火-凛 |  | UT_Hero_Card_121_03 | UT_Hero_Bust_121_03_0 / UT_Hero_Bust_121_03_1 |
| 梅加斯 | 122 | 122 | 100122001 | 初始皮肤-梅加斯 | 是 | UT_Hero_Card_122 | UT_Hero_Bust_122_0 / UT_Hero_Bust_122_1 |
| 梅加斯 | 122 | 122 | 100122002 | 羁绊皮肤-梅加斯 |  | UT_Hero_Card_122_Max | UT_Hero_Bust_122_Max_0 / UT_Hero_Bust_122_Max_1 |
| 梅加斯 | 122 | 122 | 100122003 | 海滨救援-梅加斯 |  | UT_Hero_Card_122_01 | UT_Hero_Bust_122_01_0 / UT_Hero_Bust_122_01_1 |
| 梅加斯 | 122 | 122 | 100122004 | 魔枪表演-梅加斯 |  | UT_Hero_Card_122_02 | UT_Hero_Bust_122_02_0 / UT_Hero_Bust_122_02_1 |
| 姬梦朝 | 123 | 123 | 100123001 | 初始皮肤-姬梦朝 | 是 | UT_Hero_Card_123 | UT_Hero_Bust_123_0 / UT_Hero_Bust_123_1 |
| 姬梦朝 | 123 | 123 | 100123002 | 羁绊皮肤-姬梦朝 |  | UT_Hero_Card_123_Max | UT_Hero_Bust_123_Max_0 / UT_Hero_Bust_123_Max_1 |
| 姬梦朝 | 123 | 123 | 100123003 | 滨边时光-姬梦朝 |  | UT_Hero_Card_123_01 | UT_Hero_Bust_123_01_0 / UT_Hero_Bust_123_01_1 |
| 姬梦朝 | 123 | 123 | 100123004 | 祥瑞天章-姬梦朝 |  | UT_Hero_Card_123_02 | UT_Hero_Bust_123_02_0 / UT_Hero_Bust_123_02_1 |
| 照 | 124 | 124 | 100124001 | 初始皮肤-照 | 是 | UT_Hero_Card_124 | UT_Hero_Bust_124_0 / UT_Hero_Bust_124_1 |
| 照 | 124 | 124 | 100124002 | 羁绊皮肤-照 |  | UT_Hero_Card_124_Max | UT_Hero_Bust_124_Max_0 / UT_Hero_Bust_124_Max_1 |
| 照 | 124 | 124 | 100124003 | 小护士-照 |  | UT_Hero_Card_124_01 | UT_Hero_Bust_124_01_0 / UT_Hero_Bust_124_01_1 |
| 照 | 124 | 124 | 100124004 | 魔旅歌姬-照 |  | UT_Hero_Card_124_02 | UT_Hero_Bust_124_02_0 / UT_Hero_Bust_124_02_1 |
| 摩西 | 125 | 125 | 100125001 | 初始皮肤-摩西 | 是 | UT_Hero_Card_125 | UT_Hero_Bust_125_0 / UT_Hero_Bust_125_1 |
| 摩西 | 125 | 125 | 100125002 | 羁绊皮肤-摩西 |  | UT_Hero_Card_125_Max | UT_Hero_Bust_125_Max_0 / UT_Hero_Bust_125_Max_1 |
| 摩西 | 125 | 125 | 100125003 | 特殊委派-摩西 |  | UT_Hero_Card_125_01 | UT_Hero_Bust_125_01_0 / UT_Hero_Bust_125_01_1 |
| 真梦梓 | 126 | 126 | 100126001 | 初始皮肤-真梦梓 | 是 | UT_Hero_Card_126 | UT_Hero_Bust_126_0 / UT_Hero_Bust_126_1 |
| 真梦梓 | 126 | 126 | 100126002 | 羁绊皮肤-真梦梓 |  | UT_Hero_Card_126_Max | UT_Hero_Bust_126_Max_0 / UT_Hero_Bust_126_Max_1 |
| 真梦梓 | 126 | 126 | 100126003 | 冰上舞者-真梦梓 |  | UT_Hero_Card_126_01 | UT_Hero_Bust_126_01_0 / UT_Hero_Bust_126_01_1 |
| 邦妮 | 127 | 127 | 100127001 | 初始皮肤-邦妮 | 是 | UT_Hero_Card_127 | UT_Hero_Bust_127_0 / UT_Hero_Bust_127_1 |
| 邦妮 | 127 | 127 | 100127002 | 羁绊皮肤-邦妮 |  | UT_Hero_Card_127_Max | UT_Hero_Bust_127_Max_0 / UT_Hero_Bust_127_Max_1 |
| 邦妮 | 127 | 127 | 100127003 | 紧急邀约-邦妮 |  | UT_Hero_Card_127_01 | UT_Hero_Bust_127_01_0 / UT_Hero_Bust_127_01_1 |
| 邦妮 | 127 | 127 | 100127004 | 小红帽-邦妮 |  | UT_Hero_Card_127_02 | UT_Hero_Bust_127_02_0 / UT_Hero_Bust_127_02_1 |
| 玲玲 | 128 | 128 | 100128001 | 初始皮肤-玲玲 | 是 | UT_Hero_Card_128 | UT_Hero_Bust_128_0 / UT_Hero_Bust_128_1 |
| 玲玲 | 128 | 128 | 100128002 | 羁绊皮肤-玲玲 |  | UT_Hero_Card_128_Max | UT_Hero_Bust_128_Max_0 / UT_Hero_Bust_128_Max_1 |
| 玲玲 | 128 | 128 | 100128003 | 安全员-玲玲 |  | UT_Hero_Card_128_01 | UT_Hero_Bust_128_01_0 / UT_Hero_Bust_128_01_1 |
| 赛克斯 | 129 | 129 | 100129001 | 初始皮肤-赛克斯 | 是 | UT_Hero_Card_129 | UT_Hero_Bust_129_1 / UT_Hero_Bust_129_0 |
| 赛克斯 | 129 | 129 | 100129002 | 羁绊皮肤-赛克斯 |  | UT_Hero_Card_129_Max | UT_Hero_Bust_129_Max_1 / UT_Hero_Bust_129_Max_0 |
| 超绝最可爱天使酱 | 301 | 301 | 100301001 | 初始皮肤-超天酱 | 是 | UT_Hero_Card_301 | UT_Hero_Bust_301_0 / UT_Hero_Bust_301_1 |
| 超绝最可爱天使酱 | 301 | 301 | 100301003 | 网络主播-超天酱 |  | UT_Hero_Card_301_01 | UT_Hero_Bust_301_01_0 / UT_Hero_Bust_301_01_1 |
| 主播女孩 | 302 | 302 | 100301002 | 初始皮肤-糖糖 | 是 | UT_Hero_Card_302 | UT_Hero_Bust_302_0 / UT_Hero_Bust_302_1 |
| 主播女孩 | 302 | 302 | 100301004 | 休闲日常-糖糖 |  | UT_Hero_Card_302_01 | UT_Hero_Bust_302_01_0 / UT_Hero_Bust_302_01_1 |
| 吉尔·斯汀雷 | 303 | 303 | 100303001 | 初始皮肤-吉尔 | 是 | UT_Hero_Card_303 | UT_Hero_Bust_303_1 / UT_Hero_Bust_303_0 |
| 多萝西·海兹 | 304 | 304 | 100304001 | 初始皮肤-多萝西 | 是 | UT_Hero_Card_304 | UT_Hero_Bust_304_0 / UT_Hero_Bust_304_1 |
| 远野汉娜 | 305 | 305 | 100305001 | 初始皮肤-远野汉娜 | 是 | UT_Hero_Card_305 | UT_Hero_Bust_305_1 / UT_Hero_Bust_305_0 |
| 橘雪莉 | 306 | 306 | 100306001 | 初始皮肤-橘雪莉 | 是 | UT_Hero_Card_306 | UT_Hero_Bust_306_0 / UT_Hero_Bust_306_1 |
| 角色 307 | 307 | 307 | 100307001 | 初始皮肤-远野汉娜 | 是 | UT_Hero_Card_307 | UT_Hero_Bust_307_0 / UT_Hero_Bust_307_1 |
| 角色 308 | 308 | 308 | 100308001 | 初始皮肤-橘雪莉 | 是 | UT_Hero_Card_308 | UT_Hero_Bust_308_0 / UT_Hero_Bust_308_1 |
| 天川苍鲤 | 9001 | 9001 | 1009001001 | 初始皮肤-天川苍鲤 | 是 | UT_Hero_Card_307 | UT_Hero_Bust_307_0 / UT_Hero_Bust_307_1 |

共 **38** 个角色、**136** 条皮肤立绘。
