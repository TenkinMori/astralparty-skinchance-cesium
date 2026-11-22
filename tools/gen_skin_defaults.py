# -*- coding: utf-8 -*-
"""从 skins.json 生成 SkinDefaults.cs，并在游戏 mods 目录里建好每个皮肤的贴图文件夹。

用法：python _genskindefaults.py
"""
import json
import os
import re
import shutil

REPO = r"D:\AstralPartyGit\astralparty-skinchance-cesium"
SRC_JSON = os.path.join(REPO, "skins.json")
OUT_CS = os.path.join(REPO, "src", "SkinChanceMod", "SkinDefaults.cs")
MOD_DIR = r"D:\Steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_ModLoader\mods\SkinChanceMod"
SKINS_DIR = os.path.join(MOD_DIR, "skins")

STAGES = [(100, "full.png"), (60, "damaged.png"), (25, "broken.png")]


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
        "    /// 首次启动时写进配置的默认规则：每个皮肤三档（完好 / 受损 / 重伤）。",
        "    /// 由 tools/gen_skin_defaults.py 从 SKINS.md（3.3.0 数据）生成，共 %d 条。" % len(ids),
        "    /// 用户只要把 PNG 丢进 skins\\&lt;文件夹&gt;\\ 下对应的文件名即可，缺失的档位会被忽略。",
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
        "        public static List<SkinRule> BuildRules()",
        "        {",
        "            List<SkinRule> rules = new List<SkinRule>(ItemIds.Length);",
        "            for (int i = 0; i < ItemIds.Length && i < Folders.Length; i++)",
        "            {",
        '                string dir = "skins/" + Folders[i] + "/";',
        "                SkinRule rule = new SkinRule();",
        "                rule.itemId = ItemIds[i];",
        "                rule.stages = new List<SkinStage>",
        "                {",
        '                    new SkinStage { hpPercent = FullPercent, image = dir + "full.png" },',
        '                    new SkinStage { hpPercent = DamagedPercent, image = dir + "damaged.png" },',
        '                    new SkinStage { hpPercent = BrokenPercent, image = dir + "broken.png" },',
        "                };",
        "                rules.Add(rule);",
        "            }",
        "            return rules;",
        "        }",
        "    }",
        "}",
        "",
    ]
    with open(OUT_CS, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(lines))
    print("SkinDefaults.cs wrote, skins=%d" % len(ids))

    os.makedirs(SKINS_DIR, exist_ok=True)
    created = 0
    for f in folders:
        p = os.path.join(SKINS_DIR, f)
        if not os.path.isdir(p):
            os.makedirs(p)
            created += 1
    print("folders created=%d total=%d" % (created, len(folders)))
    print("mod skins dir:", SKINS_DIR)


main()
