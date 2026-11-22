# -*- coding: utf-8 -*-
"""把每个皮肤的贴图文件夹打成一个 skins.zip，用户解压到模组目录即可。

用法：python tools/make_skins_zip.py [输出路径]
默认输出 dist/skins.zip。
"""
import json
import os
import re
import sys
import zipfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC_JSON = os.path.join(REPO, "skins.json")


def safe(name):
    name = re.sub(r'[\\/:*?"<>|]', "_", name).strip()
    return name or "skin"


def folders():
    with open(SRC_JSON, "r", encoding="utf-8") as fh:
        rows = json.load(fh)
    out = []
    seen = set()
    for r in rows:
        item_id = r["itemId"]
        if item_id in seen:
            continue
        seen.add(item_id)
        label = r.get("skinName") or ("皮肤 %d" % item_id)
        out.append("%d_%s" % (item_id, safe(label)))
    return out


def main():
    target = sys.argv[1] if len(sys.argv) > 1 else os.path.join(REPO, "dist", "skins.zip")
    os.makedirs(os.path.dirname(target), exist_ok=True)
    names = folders()

    readme = (
        "把这里的文件夹整个解压到 mods\\SkinChanceMod\\skins\\ 下。\r\n"
        "\r\n"
        "每个文件夹是一张皮肤，名字是 <皮肤编号>_<皮肤名>，编号见模组目录里的 SKINS.md。\r\n"
        "文件夹里放这几张 PNG（放哪几张都行，缺的档位会自动用相邻档顶替）：\r\n"
        "\r\n"
        "  full.png      满血（血量 61%-100%）\r\n"
        "  damaged.png   受损（血量 26%-60%）\r\n"
        "  broken.png    重伤（血量 0%-25%）\r\n"
        "\r\n"
        "要和游戏原图一致的话，左下角立绘是 UT_Hero_Card_<皮肤>\r\n"
        "（例如钢羽森姬-茉莉是 UT_Hero_Card_117_04，1261x2048）。\r\n"
        "注意不是 UT_Hero_Bust_*：那个是「发起 PK」用的小图，不是左下角。\r\n"
        "\r\n"
        "PNG 8bit、非隔行，带透明通道没问题。改完重新进局就生效，不需要改 config.json。\r\n"
    )

    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.writestr("skins/使用说明.txt", readme.encode("utf-8"))
        for name in names:
            zf.writestr(zipfile.ZipInfo("skins/" + name + "/"), b"")
    print("skins.zip -> %s (folders=%d, %d bytes)" % (target, len(names), os.path.getsize(target)))


main()
