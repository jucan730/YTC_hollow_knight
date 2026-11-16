# -*- coding: utf-8 -*-
"""把安装说明 docx 里的版本号刷成当前发布版，并留一份到项目 out/doc 下给打包用。"""
import os
import re
import sys
import shutil
from docx import Document

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from rename_docx import iter_table_paragraphs

VER = "v0.9.26"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
KEEP = os.path.join(ROOT, "out", "doc", "YTC_死亡统计mod_安装步骤.docx")
DESKTOP = os.path.join(os.path.expanduser("~"), "Desktop")
# 用户可能把文档挪进了解压出来的文件夹里，两处都找一遍
CANDIDATES = [
    os.path.join(DESKTOP, "YTC_死亡统计mod_安装步骤.docx"),
    os.path.join(DESKTOP, "YTC_死亡统计mod_v0.9.23", "YTC_死亡统计mod_安装步骤.docx"),
    os.path.join(DESKTOP, "YTC_死亡统计mod_v0.9.24", "YTC_死亡统计mod_安装步骤.docx"),
]


def bump(path):
    doc = Document(path)
    n = 0
    pat = re.compile(r"v0\.9\.\d+")
    for p in list(doc.paragraphs) + list(iter_table_paragraphs(doc.tables)):
        for r in p.runs:
            t = pat.sub(VER, r.text)
            if t != r.text:
                r.text = t
                n += 1
    doc.save(path)
    return n


def main():
    src = None
    for c in CANDIDATES:
        if os.path.isfile(c):
            src = c
            break
    if src is None and os.path.isfile(KEEP):
        src = KEEP
    if src is None:
        print("找不到文档，先找过这些位置：")
        for c in CANDIDATES:
            print("  " + c)
        return 1

    print("源: " + src)
    print("改版本号 run 数: %d" % bump(src))
    os.makedirs(os.path.dirname(KEEP), exist_ok=True)
    if os.path.abspath(src) != os.path.abspath(KEEP):
        shutil.copy2(src, KEEP)
    print("留档: " + KEEP)
    for p in Document(KEEP).paragraphs[:2]:
        print("  " + p.text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
