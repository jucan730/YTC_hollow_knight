# -*- coding: utf-8 -*-
"""
扫描空洞骑士关卡文件（hollow_knight_Data/level*），提取其中所有以长度前缀存储的字符串。
用途：拿到游戏内真实的场景对象名（怪物名就是场景里 GameObject 的 m_Name）。

Unity 的 string 存储格式：4 字节小端长度 + UTF-8 内容 + 0~3 字节对齐补零。
所以先找可打印字符段，再回头校验它前面 4 字节是不是正好等于本段长度。
"""
import os
import re
import sys
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
             if re.fullmatch(r"level\d+", f)]
    files.sort(key=lambda x: int(x[5:]))
    counter = {}
    for i, f in enumerate(files):
        p = os.path.join(DATA, f)
        try:
            ss = strings_of(p)
        except Exception as e:
            print("skip", f, e)
            continue
        for s in ss:
            counter[s] = counter.get(s, 0) + 1
        if (i + 1) % 50 == 0:
            print("done", i + 1, "/", len(files), "unique so far", len(counter), flush=True)

    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, "all_strings.json"), "w", encoding="utf-8") as fh:
        json.dump(counter, fh, ensure_ascii=False)
    print("total unique", len(counter))


if __name__ == "__main__":
    main()
