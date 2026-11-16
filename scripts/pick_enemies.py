# -*- coding: utf-8 -*-
"""
从 scan_names.py 的 all_strings.json 里挑出「看起来像怪物名」的候选。
判据：去掉克隆后缀 (2) 后，是 2~5 个首字母大写的英文单词（或带连字符/撇号），
并且至少含一个已知的敌人家族词根。这样能过滤掉引擎/结构名（Chunk 0 1、CameraLockArea 之类）。
"""
import json
import re
import os

OUT = r"C:/Users/ruitg/WorkBuddy/2026-11-14-15-19-46/hk-mod/out"

# 空洞骑士里敌人/头目的家族词根（英文原名里几乎必带其中一个）
FAMILIES = [
    "husk", "fly", "aspid", "mantis", "crawler", "hopper", "squit", "vengefly", "baldur",
    "mossfly", "spitter", "charger", "brooding", "dung", "twister", "crystal", "watcher",
    "sibling", "kingsmould", "winged", "oblobble", "primal", "beast", "carver", "fool",
    "warrior", "knight", "defender", "guardian", "hunter", "shade", "ghost", "snipper",
    "spiny", "bouncer", "roller", "lumafly", "jelly", "uumuu", "moss", "plant", "turret",
    "zote", "grimm", "soul", "mawlek", "nosk", "radiance", "hollow", "vessel", "sentry",
    "great", "grave", "glimback", "deephunter", "dirtcarver", "elder", "boofly", "gruz",
    "mother", "grub", "hatcher", "fluke", "worm", "maggot", "weaver", "devout", "stalking",
    "garpede", "womb", "diver", "belfly", "volatile", "rancid", "blob", "slob", "slug",
    "junk", "miner", "acid", "spike", "blade", "spinner", "thwomp", "press", "gladiator",
    "colosseum", "arena", "spawner", "hatchling", "defender", "nightmare", "dancer",
    "collector", "hornet", "quake", "seer", "mantis", "lord", "traitor", "pale", "king",
    "myla", "cornifer", "quirrel", "cloth", "tiso", "oro", "mato", "sheo", "sly", "zote",
    "bretta", "divine", "grimmsteel", "nailsmith", "salubra", "lemm", "isk",
]

CLONE = re.compile(r"\s*\(\d+\)$")
NAME_OK = re.compile(r"^[A-Z][A-Za-z'\-\.]*(?: [A-Za-z0-9'\-\.]+){0,5}$")


def main():
    path = os.path.join(OUT, "all_strings.json")
    counter = json.load(open(path, encoding="utf-8"))

    merged = {}
    for s, n in counter.items():
        base = CLONE.sub("", s)
        merged[base] = merged.get(base, 0) + n

    cands = []
    for s, n in merged.items():
        if not (3 <= len(s) <= 42):
            continue
        if not NAME_OK.match(s):
            continue
        low = s.lower()
        if not any(k in low for k in FAMILIES):
            continue
        cands.append((s, n))

    cands.sort(key=lambda x: (-x[1], x[0]))
    with open(os.path.join(OUT, "enemy_candidates.txt"), "w", encoding="utf-8") as fh:
        for s, n in cands:
            fh.write("%-42s %d\n" % (s, n))
    print("candidates", len(cands))


if __name__ == "__main__":
    main()
