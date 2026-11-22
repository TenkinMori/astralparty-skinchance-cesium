# -*- coding: utf-8 -*-
"""从 GameData 里抽出「角色 -> 皮肤 -> 立绘资源」对照表，生成 SKINS.md / skins.json。

数据源（客户端抽出来的 protobuf 单表，3.3.0 快照）：
  D:\\AstralPartyGit\\server-repo\\data\\gamedata\\{Character,Skin,Item,STRCharacter,STRItem}.bin

字段号来自反编译：
  CharacterInfoConfigure  : 4 NameID / 27 StandingPainting（立绘组 id）
  SkinStandingPaintingConfigureItem : 1 Index / 2 IsDefault / 3 ItemID
                             7 Character / 9 InGameCharacter / 10 InGameCharacterSFW
                             15 Bust（repeated）/ 16 SfwBust
  ItemInfoConfigure       : 9 NameID → STRItem 里的中文皮肤名

exclude_heroes：DIY 占位角色（307 / 308 / 9001）不在正式客户端资源里，
  是另一个项目加进来的占位条目，不计入对照表。
"""
import json
import os
import struct

TABLE_DIR = os.environ.get("SKIN_TABLE_DIR", r"D:\AstralPartyGit\server-repo\data\gamedata")
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_MD = os.path.join(REPO, "SKINS.md")
OUT_JSON = os.path.join(REPO, "skins.json")

# 9001（天川苍鲤）是另一个项目的 DIY 角色，立绘复用了 UT_Hero_Card_307，不计入。
# 307 / 308 是正式角色，资源后续会上，不过滤。
EXCLUDE_HEROES = {9001}


def read_varint(data, i):
    shift = 0
    val = 0
    while True:
        b = data[i]
        i += 1
        val |= (b & 0x7F) << shift
        if not b & 0x80:
            return val, i
        shift += 7


def parse_fields(data):
    out = []
    i = 0
    n = len(data)
    while i < n:
        key, i = read_varint(data, i)
        field, wire = key >> 3, key & 7
        if wire == 0:
            val, i = read_varint(data, i)
        elif wire == 1:
            val = struct.unpack_from("<q", data, i)[0]
            i += 8
        elif wire == 5:
            val = struct.unpack_from("<i", data, i)[0]
            i += 4
        elif wire == 2:
            ln, i = read_varint(data, i)
            val = data[i:i + ln]
            i += ln
        elif wire in (3, 4):
            break
        else:
            raise ValueError("wire %d" % wire)
        out.append((field, wire, val))
    return out


def table(name):
    with open(os.path.join(TABLE_DIR, name + ".bin"), "rb") as fh:
        return fh.read()


def text(raw):
    try:
        return raw.decode("utf-8")
    except UnicodeDecodeError:
        return ""


def map_entries(name, key_field=1, value_field=2):
    out = {}
    for f, w, v in parse_fields(table(name)):
        if f != 2 or w != 2:
            continue
        key = None
        body = None
        for q, r, y in parse_fields(v):
            if q == key_field and r == 5:
                key = y
            elif q == value_field and r == 2:
                body = y
        if key is not None and body is not None:
            out[key] = body
    return out


def str_table(name):
    out = {}
    for f, w, v in parse_fields(table(name)):
        if f != 1 or w != 2:
            continue
        sid = None
        txt = None
        for q, r, y in parse_fields(v):
            if q == 1:
                sid = y
            elif q == 2 and r == 2:
                txt = text(y)
        if sid is not None and txt:
            out[sid] = txt
    return out


def main():
    chars = map_entries("Character")
    strs_char = str_table("STRCharacter")
    items_tbl = map_entries("Item")
    strs_item = str_table("STRItem")

    heroes = []
    for hero_id, body in sorted(chars.items()):
        if hero_id in EXCLUDE_HEROES:
            continue
        scalars = {q: y for q, w, y in parse_fields(body) if w in (0, 5)}
        heroes.append({
            "id": hero_id,
            "name": strs_char.get(scalars.get(4, 0), ""),
            "group": scalars.get(27, hero_id),
        })

    skins_by_group = {}
    for f, w, v in parse_fields(table("Skin")):
        if f != 1 or w != 2:
            continue
        group = None
        entries = []
        for q, r, y in parse_fields(v):
            if q == 1 and r == 5:
                group = y
            elif q == 2 and r == 2:
                entries.append(y)
        if group is None:
            continue
        skins = []
        for raw in entries:
            scalars = {}
            multi = {}
            for q, r, y in parse_fields(raw):
                if r in (0, 5):
                    scalars[q] = y
                elif r == 2:
                    multi.setdefault(q, []).append(text(y))

            def s(field):
                return multi.get(field, [""])[0]

            skins.append({
                "index": scalars.get(1, 0),
                "isDefault": bool(scalars.get(2, 0)),
                "itemId": scalars.get(3, 0),
                "appearanceType": scalars.get(4, 0),
                "character": s(7),
                "inGameCharacter": s(9),
                "inGameCharacterSfw": s(10),
                "bust": multi.get(15, []),
                "bustSfw": multi.get(16, []),
            })
        skins.sort(key=lambda s: s["index"])
        skins_by_group[group] = skins

    def skin_name(item_id):
        body = items_tbl.get(item_id)
        if not body:
            return ""
        for q, w, y in parse_fields(body):
            if q == 9 and w == 5:
                return strs_item.get(y, "")
        return ""

    rows = []
    for hero in heroes:
        for s in skins_by_group.get(hero["group"], []):
            if s["itemId"] == 1009001001:
                continue
            rows.append({
                "heroId": hero["id"],
                "heroName": hero["name"] or ("角色 %d" % hero["id"]),
                "group": hero["group"],
                "itemId": s["itemId"],
                "skinName": skin_name(s["itemId"]),
                "isDefault": s["isDefault"],
                "index": s["index"],
                "appearanceType": s["appearanceType"],
                "character": s["character"],
                "inGameCharacter": s["inGameCharacter"],
                "inGameCharacterSfw": s["inGameCharacterSfw"],
                "bust": s["bust"],
                "bustSfw": s["bustSfw"],
            })

    os.makedirs(REPO, exist_ok=True)
    with open(OUT_JSON, "w", encoding="utf-8") as fh:
        json.dump(rows, fh, ensure_ascii=False, indent=2)

    lines = [
        "# 角色 / 皮肤 / 立绘资源对照表",
        "",
        "自动从客户端 GameData（3.3.0）抽取（`Character.bin` 字段 27 = 立绘组，`Skin.bin` 的 `StandingPaintings`，"
        "皮肤名取 `ItemInfoConfigure.NameID` → `STRItem`）。生成脚本：`tools/gen_skins_catalog.py`。",
        "",
        "- 配置里的 `itemId` 填本表「皮肤 ItemID」；未配置的皮肤不会被动。",
        "- **左下角立绘** = `SkinStandingPaintingConfigureItem.Character`（`BattleInfoPanel.RefreshHeroSkin()` → "
        "`GetCharacterInGame()`，`InGameCharacter` 全表只有 1 条非空，实际都走 `Character`）；AngelMode 下优先用同名的 `_sfw` 变体。",
        "- `Bust` 字段不是左下角立绘：只有 `FightWindow.cs:888` 的「发起 PK」图标用它（按金币阈值取 `[0]`/`[1]`），不要拿它当战损图。",
        "- 同一角色有多张皮肤，**ItemID 才是唯一键**（heroId 只用来分组）。",
        "- 已排除 9001（天川苍鲤）的 DIY 占位条目：它的立绘复用了 `UT_Hero_Card_307`，不是正式皮肤。",
        "",
        "| 角色 | 角色ID | 立绘组 | 皮肤 ItemID | 皮肤名 | 默认 | 左下立绘 | PK 图集 |",
        "|---|---|---|---|---|---|---|---|",
    ]
    for r in rows:
        lines.append("| %s | %d | %d | %d | %s | %s | %s | %s |" % (
            r["heroName"], r["heroId"], r["group"], r["itemId"],
            r["skinName"], "是" if r["isDefault"] else "",
            r["character"], " / ".join(r["bust"])))
    lines.append("")
    lines.append("共 **%d** 个角色、**%d** 条皮肤立绘。" % (len(heroes), len(rows)))
    with open(OUT_MD, "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines) + "\n")

    print("heroes=%d skins=%d" % (len(heroes), len(rows)))


main()
