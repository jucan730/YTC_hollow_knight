# -*- coding: utf-8 -*-
"""把 out/enemy_table.json 生成 Excel 怪物名称表（官方简中名 + 描述 + 猎人笔记）。"""
import io
import json
import os

from openpyxl import Workbook
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, '..', 'out'))
TARGET = r'C:/Users/ruitg/Desktop/空洞骑士_怪物名称表.xlsx'

HEAD_FILL = PatternFill('solid', fgColor='2F5597')
HEAD_FONT = Font(color='FFFFFF', bold=True, size=11)
THIN = Side(style='thin', color='BFBFBF')
BORDER = Border(left=THIN, right=THIN, top=THIN, bottom=THIN)


def style_header(ws, widths):
    for i, w in enumerate(widths, 1):
        ws.column_dimensions[chr(64 + i)].width = w
    for c in ws[1]:
        c.fill = HEAD_FILL
        c.font = HEAD_FONT
        c.alignment = Alignment(horizontal='center', vertical='center')
        c.border = BORDER
    ws.freeze_panes = 'A2'


def main():
    data = json.load(io.open(os.path.join(OUT, 'enemy_table.json'), encoding='utf-8'))
    wb = Workbook()
    ws = wb.active
    ws.title = '怪物名称'
    ws.append(['序号', '日志编号', '击杀字段', '官方中文名', '描述', '猎人笔记'])
    for i, r in enumerate(data, 1):
        line = [i, r['id'], r['kill_field'], r['cn'], r['desc'], r['note']]
        ws.append([str(x).replace('<br>', '\n') if isinstance(x, str) else x for x in line])
    style_header(ws, [6, 22, 24, 18, 60, 70])
    for row in ws.iter_rows(min_row=2, max_row=ws.max_row):
        for c in row:
            c.border = BORDER
            c.alignment = Alignment(vertical='top', wrap_text=(c.column in (5, 6)))
        row[3].font = Font(bold=True)

    print('已生成', TARGET, '共', len(data), '种')
    try:
        wb.save(TARGET)
        print('保存成功')
    except PermissionError:
        alt = TARGET.replace('.xlsx', '_新.xlsx')
        wb.save(alt)
        print('文件被占用，改存到', alt)


if __name__ == '__main__':
    main()
