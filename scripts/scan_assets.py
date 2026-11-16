# -*- coding: utf-8 -*-
"""扫描 sharedassets*.assets（预制体所在包），把结果并进 out/all_strings.json。"""
import os
import re
import json

DATA = r"C:/Program Files (x86)/Steam/steamapps/common/Hollow Knight/hollow_knight_Data"
OUT = r"C:/Users/ruitg/WorkBuddy/2026-11-14-15-19-46/hk-mod/out"
RUN = re.compile(rb"[\x20-\x7e]{3,60}")


def strings_of(path):
    d = open(path, "rb").read()
    found = set()
    for m in RUN.finditer(d):
        s = m.start()
        if s < 4:
            continue
        L = int.from_bytes(d[s - 4:s], "little")
        if L < 3 or L > 60 or s + L > len(d):
            continue
        seg = d[s:s + L]
        if any(c < 0x20 or c > 0x7e for c in seg):
            continue
        found.add(seg.decode("ascii"))
    return found


def main():
    files = [f for f in os.listdir(DATA)
             if re.fullmatch(r"sharedassets\d+\.assets", f) or f == "resources.assets"]
    pool = json.load(open(os.path.join(OUT, "all_strings.json"), encoding="utf-8"))
    for i, f in enumerate(files):
        try:
            for s in strings_of(os.path.join(DATA, f)):
                pool[s] = pool.get(s, 0) + 1
        except Exception as e:
            print("skip", f, e)
        if (i + 1) % 40 == 0:
            print("done", i + 1, "/", len(files), flush=True)
    json.dump(pool, open(os.path.join(OUT, "all_strings.json"), "w", encoding="utf-8"), ensure_ascii=False)
    print("merged unique", len(pool))


if __name__ == "__main__":
    main()
