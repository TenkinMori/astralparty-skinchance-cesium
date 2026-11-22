# -*- coding: utf-8 -*-
"""从 skins.json 生成 SkinDefaults.cs（皮肤文件夹名清单 + 默认阈值）。

用法：python tools/gen_skin_defaults.py

生成内容只包含「哪些皮肤、文件夹叫什么名字、默认三档阈值多少」，
每个皮肤的具体配置由模组首次启动时写进各自文件夹的 config.json，
用户改那个文件即可，不需要重建模组。
"""
import json
import os
import re

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC_JSON = os.path.join(REPO, "skins.json")
OUT_CS = os.path.join(REPO, "src", "SkinChanceMod", "SkinDefaults.cs")

STAGES = [(100, "0"), (60, "1.png"), (25, "2.png")]


def safe(name):
    name = re.sub(r'[\\/:*?"<>|]', "_", name).strip()
    return name or "skin"


def main():
    with open(SRC_JSON, "r", encoding="utf-8") as fh:
        rows = json.load(fh)

    folders = []
    ids = []
    seen = set()
    for r in rows:
        item_id = r["itemId"]
        if item_id in seen:
            continue
        seen.add(item_id)
        label = r.get("skinName") or ("皮肤 %d" % item_id)
        folders.append("%d_%s" % (item_id, safe(label)))
        ids.append(item_id)

    lines = [
        "using System.Collections.Generic;",
        "",
        "namespace SkinChanceMod",
        "{",
        "    /// <summary>",
        "    /// 皮肤文件夹清单与默认阈值。由 tools/gen_skin_defaults.py 从最新 GameData 生成，共 %d 条。" % len(ids),
        "    /// 模组首次启动会按这份清单建好 skins\\&lt;文件夹&gt;\\ 并写入各自的 config.json；",
        "    /// 之后用户只改每个文件夹里的 config.json、往里丢图片即可，不需要重建模组。",
        "    /// </summary>",
        "    internal static class SkinDefaults",
        "    {",
        "        public const int FullPercent = %d;" % STAGES[0][0],
        "        public const int DamagedPercent = %d;" % STAGES[1][0],
        "        public const int BrokenPercent = %d;" % STAGES[2][0],
        "",
        "        public static readonly int[] ItemIds =",
        "        {",
    ]
    for i in range(0, len(ids), 8):
        lines.append("            " + ", ".join(str(x) for x in ids[i:i + 8]) + ",")
    lines += [
        "        };",
        "",
        "        public static readonly string[] Folders =",
        "        {",
    ]
    for f in folders:
        lines.append('            "%s",' % f)
    lines += [
        "        };",
        "",
        "        /// <summary>默认三档：满血保留原图，受损用 1.png，重伤用 2.png。</summary>",
        "        public static List<SkinStage> DefaultStages()",
        "        {",
        "            return new List<SkinStage>",
        "            {",
        '                new SkinStage { hpPercent = FullPercent, image = "0", scale = 1f, offsetX = 0f, offsetY = 0f },',
        '                new SkinStage { hpPercent = DamagedPercent, image = "1.png", scale = 1f, offsetX = 0f, offsetY = 0f },',
        '                new SkinStage { hpPercent = BrokenPercent, image = "2.png", scale = 1f, offsetX = 0f, offsetY = 0f },',
        "            };",
        "        }",
        "    }",
        "}",
        "",
    ]
    with open(OUT_CS, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(lines))
    print("SkinDefaults.cs wrote, skins=%d" % len(ids))


main()
