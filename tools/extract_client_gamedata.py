# -*- coding: utf-8 -*-
"""从已安装的客户端里抽出最新的 GameData 表（Character / Skin / Item / STR*）。

为什么需要它：以前 scripts 读的是 server-repo 里的 gamedata 快照，
那批表停在旧版本，会漏掉新皮肤（例如 130 苍鲤、100104005 秘密清扫-小町）。
真正准的是客户端自己下载的 gamedata bundle，本脚本直接从游戏目录里翻出来。

用法：
    python tools/extract_client_gamedata.py
    python tools/extract_client_gamedata.py --game "D:\\Steam\\steamapps\\common\\Astral Party\\8vJXn6CN"

输出：<repo>/gamedata/{Character,Skin,Item,STRCharacter,STRItem,STRSkin,Version}.bin
（该目录已在 .gitignore 里，不需要提交）

依赖：UnityPy（pip install UnityPy）
"""
import argparse
import os
import sys

WANT = {
    "Skin",
    "Character",
    "Item",
    "STRItem",
    "STRCharacter",
    "STRSkin",
    "Version",
}

DEFAULT_GAME = r"D:\Steam\steamapps\common\Astral Party\8vJXn6CN"
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(REPO, "gamedata")


def candidate_bundles(game_root):
    """列出客户端 Addressables 缓存里「可能装着 GameData」的 bundle。

    不能靠明文搜表名来判断：UnityFS 里 TextAsset 的名字大多被压进 LZ4 块，
    只有极少数会落在明文里（实测 `STRItem` 就搜不到）。所以先按体积粗筛
    （GameData 那一坨 160 张表大约 1 MB），再逐个用 UnityPy 打开看内容。
    """
    out = []
    for profile in sorted(os.listdir(game_root)):
        if not profile.endswith("_Data"):
            continue
        base = os.path.join(game_root, profile, "com.unity.addressables", "AssetBundles")
        if not os.path.isdir(base):
            continue
        for dirpath, _dirnames, filenames in os.walk(base):
            for fn in filenames:
                path = os.path.join(dirpath, fn)
                try:
                    size = os.path.getsize(path)
                    if size < 300 * 1024 or size > 2 * 1024 * 1024:
                        continue
                    with open(path, "rb") as fh:
                        if fh.read(7) != b"UnityFS":
                            continue
                except Exception:
                    continue
                out.append((size, path))
    # 越接近 1 MB 越可能是那一包表，先试这些
    out.sort(key=lambda item: abs(item[0] - 1024 * 1024))
    return [p for _s, p in out]


def read_gamedata(path):
    """打开一个 bundle，返回 {表名: 字节}；不是 GameData 包就返回空 dict。"""
    import UnityPy

    env = UnityPy.load(path)
    got = {}
    for obj in env.objects:
        if obj.type.name != "TextAsset":
            continue
        try:
            ta = obj.read()
        except Exception:
            continue
        name = getattr(ta, "m_Name", "")
        if name not in WANT:
            continue
        raw = ta.m_Script
        if isinstance(raw, str):
            raw = raw.encode("utf-8", "surrogateescape")
        got[name] = raw
    # 真正的 GameData 包至少有 Skin / Character / Item / STRCharacter / STRItem
    return got if len(got) >= 4 else {}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game", default=DEFAULT_GAME, help="Astral Party 安装目录")
    args = ap.parse_args()

    try:
        import UnityPy
    except ImportError:
        print("需要 UnityPy：pip install UnityPy", file=sys.stderr)
        return 1

    if not os.path.isdir(args.game):
        print("找不到游戏目录：%s" % args.game, file=sys.stderr)
        return 1

    bundles = candidate_bundles(args.game)
    if not bundles:
        print("客户端里没找到可用的 bundle", file=sys.stderr)
        return 1

    os.makedirs(OUT_DIR, exist_ok=True)
    for path in bundles:
        try:
            got = read_gamedata(path)
        except Exception:
            continue
        if not got:
            continue
        print("找到 GameData：%s" % path)
        written = []
        for name, raw in got.items():
            with open(os.path.join(OUT_DIR, name + ".bin"), "wb") as fh:
                fh.write(raw)
            written.append((name, len(raw)))
        break
    else:
        print("没抽到 GameData（客户端可能还没下载完）", file=sys.stderr)
        return 1

    version_path = os.path.join(OUT_DIR, "Version.bin")
    if os.path.exists(version_path):
        with open(version_path, "rb") as fh:
            raw = fh.read()
        text = "".join(chr(b) if 32 <= b < 127 else "." for b in raw)
        print("GameData 版本：%s" % text)

    print("已写入 %s：" % OUT_DIR)
    for name, size in sorted(written):
        print("   %-14s %8d B" % (name, size))
    return 0


if __name__ == "__main__":
    sys.exit(main())
