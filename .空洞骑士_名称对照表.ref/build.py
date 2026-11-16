# -*- coding: utf-8 -*-
"""生成《空洞骑士 场景与怪物名称对照表》：两张填写用的明细表。"""
import json
import os

try:
    import openpyxl
except ImportError:
    import subprocess
    import sys
    subprocess.check_call([sys.executable, "-m", "pip", "install", "--quiet", "openpyxl>=3.1.0"])
    import openpyxl

from openpyxl import Workbook
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
from openpyxl.utils import get_column_letter


def xl_color(css_hex: str) -> str:
    value = css_hex.removeprefix("#").upper()
    if len(value) != 6:
        raise ValueError(f"Expected #RRGGBB, got: {css_hex}")
    return "FF" + value


XL_PRIMARY = xl_color("#4472C4")      # 表头底
XL_WHITE = xl_color("#FFFFFF")
XL_BORDER = xl_color("#BFBFBF")
XL_BAND = xl_color("#F2F5FA")         # 分组浅底
XL_INPUT = xl_color("#FFF2CC")        # 待填列
XL_TEXT = xl_color("#1A1A1A")
XL_MUTED = xl_color("#7F7F7F")

FONT_NAME = "微软雅黑"
THIN = Side(style="thin", color=XL_BORDER)
BORDER = Border(left=THIN, right=THIN, top=THIN, bottom=THIN)

DATA = json.load(open(
    r"C:/Users/ruitg/WorkBuddy/2026-11-14-15-19-46/hk-mod/out/sheet_data.json", encoding="utf-8"))
OUT_XLSX = r"C:/Users/ruitg/WorkBuddy/2026-11-14-15-19-46/hk-mod/空洞骑士_名称对照表.xlsx"


def head_cells(ws, headers, widths):
    for i, (h, w) in enumerate(zip(headers, widths), start=1):
        c = ws.cell(row=1, column=i, value=h)
        c.font = Font(name=FONT_NAME, size=11, bold=True, color=XL_WHITE)
        c.fill = PatternFill("solid", fgColor=XL_PRIMARY)
        c.alignment = Alignment(horizontal="center", vertical="center")
        c.border = BORDER
        ws.column_dimensions[get_column_letter(i)].width = w
    ws.row_dimensions[1].height = 26


def build_scenes(wb):
    ws = wb.active
    ws.title = "场景名称"
    headers = ["序号", "场景代号", "所属区域", "中文名（待填）", "备注"]
    widths = [6, 30, 16, 30, 30]
    head_cells(ws, headers, widths)

    rows = DATA["scenes"]
    prev_area = None
    band = False
    for idx, item in enumerate(rows):
        r = 2 + idx
        if item["area"] != prev_area:
            band = not band
            prev_area = item["area"]
        fill = PatternFill("solid", fgColor=XL_BAND) if band else None

        vals = [idx + 1, item["name"], item["area"], None, item["note"] or None]
        for col, v in enumerate(vals, start=1):
            c = ws.cell(row=r, column=col, value=v)
            c.border = BORDER
            c.font = Font(name=FONT_NAME, size=10, color=XL_TEXT)
            if col == 4:
                c.fill = PatternFill("solid", fgColor=XL_INPUT)
                c.alignment = Alignment(horizontal="left", vertical="center")
            elif col == 5:
                c.font = Font(name=FONT_NAME, size=9, color=XL_MUTED)
                c.alignment = Alignment(horizontal="left", vertical="center", wrap_text=True)
            elif col == 1:
                c.alignment = Alignment(horizontal="center", vertical="center")
            else:
                c.alignment = Alignment(horizontal="left", vertical="center")
                if fill:
                    c.fill = fill

    last = 1 + len(rows)
    ws.freeze_panes = "B2"
    ws.auto_filter.ref = f"A1:E{last}"
    return last


def build_enemies(wb):
    ws = wb.create_sheet("怪物名称")
    headers = ["序号", "游戏原名", "分类", "中文名（待填）", "出现关卡数（参考）", "备注（待填）"]
    widths = [6, 30, 14, 30, 18, 34]
    head_cells(ws, headers, widths)

    rows = DATA["enemies"]
    prev_kind = None
    band = False
    for idx, item in enumerate(rows):
        r = 2 + idx
        if item["kind"] != prev_kind:
            band = not band
            prev_kind = item["kind"]
        fill = PatternFill("solid", fgColor=XL_BAND) if band else None

        vals = [idx + 1, item["name"], item["kind"], None, item["count"], None]
        for col, v in enumerate(vals, start=1):
            c = ws.cell(row=r, column=col, value=v)
            c.border = BORDER
            c.font = Font(name=FONT_NAME, size=10, color=XL_TEXT)
            if col == 4:
                c.fill = PatternFill("solid", fgColor=XL_INPUT)
                c.alignment = Alignment(horizontal="left", vertical="center")
            elif col == 6:
                c.fill = PatternFill("solid", fgColor=XL_INPUT)
                c.alignment = Alignment(horizontal="left", vertical="center", wrap_text=True)
            elif col == 5:
                c.number_format = "0"
                c.alignment = Alignment(horizontal="center", vertical="center")
                c.font = Font(name=FONT_NAME, size=9, color=XL_MUTED)
            elif col == 1:
                c.alignment = Alignment(horizontal="center", vertical="center")
            else:
                c.alignment = Alignment(horizontal="left", vertical="center")
                if fill:
                    c.fill = fill

    last = 1 + len(rows)
    ws.freeze_panes = "B2"
    ws.auto_filter.ref = f"A1:F{last}"
    return last


def main():
    wb = Workbook()
    build_scenes(wb)
    build_enemies(wb)
    wb.properties.title = "空洞骑士 场景与怪物名称对照表"
    wb.save(OUT_XLSX)
    print("saved", OUT_XLSX)


if __name__ == "__main__":
    main()
