# -*- coding: utf-8 -*-
"""把每张皮肤的文件夹（含各自的 config.json）打成一个 skins.zip。

用法：python tools/make_skins_zip.py [输出路径]
默认输出 dist/skins.zip。

每个皮肤文件夹里有独立的 config.json，用户直接改那个文件就能调阈值、
贴图名（0 = 游戏原图）、缩放与偏移，不需要动模组根目录的配置。
"""
import json
import os
import re
import sys
import zipfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC_JSON = os.path.join(REPO, "skins.json")

FULL, DAMAGED, BROKEN = 100, 60, 25


def safe(name):
    name = re.sub(r'[\\/:*?"<>|]', "_", name).strip()
    return name or "skin"


def entries():
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
        out.append((item_id, "%d_%s" % (item_id, safe(label))))
    return out


def stage(hp, image):
    return {"hpPercent": hp, "image": image, "scale": 1.0, "offsetX": 0, "offsetY": 0}


def write_config(zf, folder, item_id):
    cfg = {
        "itemId": item_id,
        "name": folder,
        "stages": [stage(FULL, "0"), stage(DAMAGED, "1.png"), stage(BROKEN, "2.png")],
    }
    text = json.dumps(cfg, ensure_ascii=False, indent=2) + "\n"
    zf.writestr("skins/%s/config.json" % folder, text.encode("utf-8"))


README = """把这里的东西整个解压到 mods\\SkinChanceMod\\ 下（覆盖 skins\\ 即可）。

每个文件夹 = 一张皮肤，名字是 <皮肤编号>_<皮肤名>，编号见 SKINS.md。
文件夹里有一个 config.json，直接改它就行：

  itemId     皮肤编号，一般不用动（留 0 时用文件夹名前缀）
  name       备注名，只影响日志
  stages     从「满血」到「重伤」的档位表
    hpPercent  这一档管血量 <= 该百分比的情况（0-100）
    image      文件名；写 "0" = 保留游戏原图；写 "1" 会自动补成 "1.png"
    scale      缩放，1.0 = 原图大小
    offsetX    右移像素数（负数左移）
    offsetY    下移像素数（负数上移）

默认三档是：
  100  "0"      满血 → 游戏原图
   60  "1.png"  受伤 → 1.png
   25  "2.png"  重伤 → 2.png

放哪几张图都行：某档找不到图会自动用相邻档顶替，
所以最省事的方式是把一张受伤图命名成 1.png 扔进来。

左下角立绘对应的资源是 UT_Hero_Card_<皮肤>（例如钢羽森姬-茉莉是
UT_Hero_Card_117_04，1261x2048）。注意不是 UT_Hero_Bust_*：
那个是「发起 PK」弹窗用的小图。

PNG 8bit、非隔行，带透明通道没问题。改完保存即可，模组默认每 2 秒重读一次。
"""


def main():
    target = sys.argv[1] if len(sys.argv) > 1 else os.path.join(REPO, "dist", "skins.zip")
    os.makedirs(os.path.dirname(target), exist_ok=True)
    items = entries()

    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as zf:
        zf.writestr("skins/使用说明.txt", README.encode("utf-8"))
        for item_id, folder in items:
            zf.writestr(zipfile.ZipInfo("skins/" + folder + "/"), b"")
            write_config(zf, folder, item_id)
    print("skins.zip -> %s (folders=%d, %d bytes)" % (target, len(items), os.path.getsize(target)))


main()
