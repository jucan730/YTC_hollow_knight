# -*- coding: utf-8 -*-
"""生成到临时文件（原表被 Excel 占用时用）"""
import os, sys, runpy

SRC = os.path.join(os.path.dirname(__file__), "gen_enemy_xlsx.py")
src = open(SRC, encoding="utf-8").read()
import re as _re
src = _re.sub(
    r'OUT = os\.path\.join\([^\n]*\)',
    'OUT = r"C:\\\\Users\\\\ruitg\\\\WorkBuddy\\\\2026-11-14-15-19-46\\\\hk-mod\\\\out\\\\怪物表_tmp.xlsx"',
    src,
)
g = {"__file__": SRC, "__name__": "__main__"}
exec(compile(src, SRC, "exec"), g)
print("OK ->", g.get("XLSX", "out/怪物表_tmp.xlsx"))
