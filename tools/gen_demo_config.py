# -*- coding: utf-8 -*-
"""把全部皮肤的默认规则写进游戏配置（SkinChanceMod.json），并给示范皮肤填好图。"""
import json
import os
import re

REPO = r"D:\AstralPartyGit\astralparty-skinchance-cesium"
SRC_JSON = os.path.join(REPO, "skins.json")
MOD_DIR = r"D:\Steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_ModLoader\mods\SkinChanceMod"
CFG = r"D:\Steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_ModLoader\configs\SkinChanceMod.json"

STAGES = [(100, "full.png"), (60, "damaged.png"), (25, "broken.png")]


def safe(name):
    return re.sub(r'[\\/:*?"<>|]', "_", name).strip() or "skin"


def main():
    with open(SRC_JSON, "r", encoding="utf-8") as fh:
        rows = json.load(fh)

    characters = []
    seen = set()
    for r in rows:
        item_id = r["itemId"]
        if item_id in seen:
            continue
        seen.add(item_id)
        folder = "%d_%s" % (item_id, safe(r.get("skinName") or ("皮肤%d" % item_id)))
        stages = [{"hpPercent": p, "image": "skins/%s/%s" % (folder, f)} for p, f in STAGES]
        characters.append({"itemId": item_id, "stages": stages})

    cfg = {"enabled": True, "hideFightVideo": True, "characters": characters}
    with open(CFG, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(cfg, fh, ensure_ascii=False, indent=2)
    print("config written: %d skins -> %s" % (len(characters), CFG))


main()
