# -*- coding: utf-8 -*-
"""
把安装说明 docx 里的旧名字统一换成 YTC_死亡统计mod / YTC_DeathStats.dll。

做法：先按 run 精确替换（保留字体样式）；跨 run 拆开的段落再整段合并重写。
替换用负向断言 (?<![A-Za-z0-9_])DeathStats，所以 XiaoCanDeathStats 不会被误伤，
重复执行也不会叠成 YTC_YTC_。
"""
import os
import re
import sys
import shutil
from docx import Document

SRC = os.path.join(os.path.expanduser("~"), "Desktop", "死亡统计mod_安装步骤.docx")
DST = os.path.join(os.path.expanduser("~"), "Desktop", "YTC_死亡统计mod_安装步骤.docx")

# 独立的 DeathStats（前面不是字母数字下划线才算），XiaoCanDeathStats 天然被排除
RE_DLL = re.compile(r"(?<![A-Za-z0-9_])DeathStats\.dll")
RE_DIR = re.compile(r"(?<![A-Za-z0-9_])DeathStats(?!\.dll)")
RE_VER = re.compile(r"v0\.9\.2\d")

UPGRADE_OLD = "从旧版本升级："
UPGRADE_NEW = (
    "从旧版本升级：老版本的文件名是 XiaoCanDeathStats.dll 或 DeathStats.dll。"
    "如果 BepInEx\\plugins 里还留着它们，装新版之前先删掉，否则同一个插件会被加载两次，"
    "一次死亡记两遍。删完不用管旧的数据文件夹（plugins\\XiaoCanDeathStats、plugins\\DeathStats），"
    "新版第一次启动会把里面的记录自动搬到 plugins\\YTC_DeathStats。"
)


def sub_text(text):
    text = RE_DLL.sub("YTC_DeathStats.dll", text)
    text = RE_DIR.sub("YTC_DeathStats", text)
    text = text.replace("v0.9.22", "v0.9.23")
    text = text.replace("死亡统计 Mod", "YTC_死亡统计mod")
    return text


def fix_paragraph(p):
    if not p.runs:
        return False
    old = "".join(r.text for r in p.runs)
    new = sub_text(old)
    if new == old:
        return False

    for r in p.runs:
        t = sub_text(r.text)
        if t != r.text:
            r.text = t

    if "".join(r.text for r in p.runs) != new:
        # 关键字横跨多个 run → 整段合并，保留第一个 run 的格式
        p.runs[0].text = new
        for r in p.runs[1:]:
            r.text = ""
    return True


def iter_table_paragraphs(tables):
    # 不做单元格去重：lxml 的 element 代理对象 id 会被复用，用 id 去重会随机漏段落。
    # 合并单元格最多让同一段落跑两遍，替换是幂等的，重复执行没有副作用。
    for t in tables:
        for row in t.rows:
            for cell in row.cells:
                for p in cell.paragraphs:
                    yield p
                for sub in iter_table_paragraphs(cell.tables):
                    yield sub


def all_text(doc):
    out = [p.text for p in doc.paragraphs]
    for p in iter_table_paragraphs(doc.tables):
        out.append(p.text)
    return out


def main():
    if not os.path.isfile(SRC):
        print("找不到源文件: " + SRC)
        return 1

    shutil.copy2(SRC, DST)
    doc = Document(DST)

    n = 0
    for p in doc.paragraphs:
        if p.text.startswith(UPGRADE_OLD) and p.runs:
            p.runs[0].text = UPGRADE_NEW
            for r in p.runs[1:]:
                r.text = ""
            n += 1
            continue
        if fix_paragraph(p):
            n += 1

    for p in iter_table_paragraphs(doc.tables):
        if fix_paragraph(p):
            n += 1

    doc.save(DST)
    print("段落改动: %d" % n)
    print("输出: " + DST)

    bad = [t for t in all_text(Document(DST))
           if "YTC_YTC" in t or re.search(r"(?<![A-Za-z0-9_])DeathStats", t)]
    print("残留可疑段落: %d" % len(bad))
    for b in bad:
        print("  ! " + b[:120])
    return 0


if __name__ == "__main__":
    sys.exit(main())
