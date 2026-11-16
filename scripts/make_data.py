# -*- coding: utf-8 -*-
"""汇总建表数据：场景（按游戏打包顺序 + 所属区域）+ 怪物（已在游戏文件里核对过的名字）。"""
import json
import os
import re

DATA = r"C:/Program Files (x86)/Steam/steamapps/common/Hollow Knight/hollow_knight_Data"
OUT = r"C:/Users/ruitg/WorkBuddy/2026-11-14-15-19-46/hk-mod/out"

# 菜单 / 调试等非游玩场景，永远不会产生死亡记录
NON_GAME = {
    "Pre_Menu_Intro", "Menu_Title", "Quit_To_Menu", "BetaEnd", "Knight_Pickup",
    "Opening_Sequence", "Cinematic_Stag_travel",
}

EXTRA_BOSS = ["Hive Knight", "Sly Boss", "Sheo Boss", "Oro Battle"]


def scenes():
    d = open(os.path.join(DATA, "globalgamemanagers"), "rb").read()
    paths = re.findall(rb"Assets/Scenes/[ -~]*?\.unity", d)
    rows, seen = [], set()
    for x in paths:
        s = x.decode()
        rel = s[len("Assets/Scenes/"):-len(".unity")]
        parts = rel.split("/")
        name = parts[-1]
        area = parts[0] if len(parts) > 1 else "公共房间"
        if name in seen:
            continue
        seen.add(name)
        if name in NON_GAME or area == "Testing":
            note = "非游玩场景，不会记录死亡"
        elif area == "Data":
            note = "数据预制体场景，不会记录死亡"
        else:
            note = ""
        rows.append({"name": name, "area": area, "note": note})
    return rows


def enemies():
    pool = json.load(open(os.path.join(OUT, "all_strings.json"), encoding="utf-8"))
    rows = []
    for line in open(os.path.join(OUT, "enemy_final.txt"), encoding="utf-8"):
        kind, name, cnt = line.rstrip("\n").split("\t")
        rows.append({"name": name, "kind": kind, "count": int(cnt)})
    for n in EXTRA_BOSS:
        if n in pool:
            rows.append({"name": n, "kind": "头目/BOSS", "count": pool[n]})
    rows.sort(key=lambda r: (r["kind"] != "头目/BOSS", -r["count"], r["name"]))
    return rows


if __name__ == "__main__":
    data = {"scenes": scenes(), "enemies": enemies()}
    json.dump(data, open(os.path.join(OUT, "sheet_data.json"), "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)
    print("scenes", len(data["scenes"]), "enemies", len(data["enemies"]))
