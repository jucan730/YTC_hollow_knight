# -*- coding: utf-8 -*-
"""把「还没验证的英文内部名」导出成 Excel 的一个工作表，方便逐条核对。"""
import json
import re
from openpyxl import load_workbook
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side

import sys
XLSX = sys.argv[1] if len(sys.argv) > 1 else r"C:\Users\ruitg\Desktop\空洞骑士_怪物名称与中文译名.xlsx"
SRC = r"C:\Users\ruitg\WorkBuddy\2026-11-14-15-19-46\hk-mod\src\DeathStats.cs"
ALLS = r"C:\Users\ruitg\WorkBuddy\2026-11-14-15-19-46\hk-mod\out\all_strings.json"
TABLE = r"C:\Users\ruitg\WorkBuddy\2026-11-14-15-19-46\hk-mod\out\enemy_table.json"

src = open(SRC, encoding="utf-8").read()
m = re.search(r"EnemyCn = new Dictionary<string, string>\s*\{(.*?)\n\s*\};", src, re.S)
CN = dict(re.findall(r'\{ "([a-z0-9]+)", "([^"]+)" \}', m.group(1)))


def norm(s):
    return re.sub(r"[^a-z0-9]", "", s.lower())


strings = {norm(s) for s in json.load(open(ALLS, encoding="utf-8"))}
table = {t["id"]: t["cn"] for t in json.load(open(TABLE, encoding="utf-8"))}

by_cn = {}
for k, v in CN.items():
    by_cn.setdefault(v, []).append(k)

# 场景里找不到实例名的 39 条：一旦运行时名字对不上，就会掉回英文
RISK_IDS = """ABYSS_TENDRIL BLOBBLE COL_FLYINGSENTRY COL_HOPPER COL_MOSQUITO COL_ROLLER
COL_SHIELD COL_WORM CRYS_LAZER_BUG FLAMEBEARER_LARGE FLAMEBEARER_MED FLAMEBEARER_SMALL
FUNGOON FUNG_ZOMBIE GODSEEKER_MASK HEALTH_SCUTTLER HUNTERMARK INFLATOR KIN_SEAL
MANTIS_FLYER_ADULT PURE_VESSEL SAGE_SLY SENTRY_1 SENTRY_FLYING SENTRY_FLYING_JAVELIN
SPITTING_SPIDER VOID_IDOL_1 VOID_IDOL_2 VOID_IDOL_3 ZAP_BUG ZOM_BURSTING
ZOM_ROYAL_1 ZOM_ROYAL_2 ZOM_ROYAL_FAT ZOM_SPINE ZOM_SPITTING ZOTE_FLYER ZOTE_HOPPER""".split()

# 名字真实存在、但「它是哪只怪」靠字面意思猜的
# 已核实并移出本表：PLANT_SHOOTER（古尔卡）= 场景 Plant Turret，同房预制体 PlantShooter；
#                    SNAPPER_TRAP（愚人吞食者）= 场景 Plant Trap，同房预制体 SnapperTrap。
GUESS = [
    ("BEE", "蜂巢士兵", "Bee Stinger", "Stinger 是刺，可能是蜂巢尖刺机关而非蜂", ),
    ("ORANGE_BALLOON", "光籽", "Orange Scuttler", "直译「橙色爬虫」，和「光籽」对不上"),
    ("MUSH_TURRET", "斯孢格", "Mushroom Turret", "蘑菇炮台，斯孢格确实发射孢子，方向对"),
    ("PALACE_FLY", "翅膀傀儡", "White Palace Fly", "白宫飞虫，方向对"),
]

rows = []
seen = set()
for rid in RISK_IDS:
    zh = table.get(rid, "?")
    keys = by_cn.get(zh, [])
    live = [k for k in keys if k in strings]
    seen.add(norm(rid))
    fake = [k for k in keys if k not in strings]
    if live:
        level, note = "中", "关卡里有 %s，可能命中；若实际名字是别的写法就会掉回英文" % " / ".join(live)
    else:
        level, note = "高", "表里的写法在 501 个关卡文件里都不存在 → 一旦碰到必然显示英文"
    if fake and live:
        note += "；无效键：" + " / ".join(fake)
    rows.append((zh, rid, " / ".join(keys), "有" if live else "无", level, note))

for rid, zh, eng, note in GUESS:
    rows.append((zh, rid, eng, "有", "中", note))

rows.sort(key=lambda r: (0 if r[4] == "高" else 1, r[0]))

wb = load_workbook(XLSX)
if "待确认名字" in wb.sheetnames:
    del wb["待确认名字"]
ws = wb.create_sheet("待确认名字", 1)

head_fill = PatternFill("solid", fgColor="2F5597")
head_font = Font(color="FFFFFF", bold=True, size=11)
hi_fill = PatternFill("solid", fgColor="FCE4D6")
mid_fill = PatternFill("solid", fgColor="FFF2CC")
thin = Side(style="thin", color="BFBFBF")
bd = Border(left=thin, right=thin, top=thin, bottom=thin)

heads = ["序号", "中文名", "日志ID", "表里登记的英文写法", "游戏里是否有这个名字", "风险", "说明"]
ws.append(heads)
for c in range(1, len(heads) + 1):
    cell = ws.cell(row=1, column=c)
    cell.fill, cell.font = head_fill, head_font
    cell.alignment = Alignment(horizontal="center", vertical="center")
    cell.border = bd
ws.row_dimensions[1].height = 26

for i, (zh, rid, eng, has, level, note) in enumerate(rows, start=1):
    ws.append([i, zh, rid, eng, has, level, note])
    r = ws.max_row
    fill = hi_fill if level == "高" else mid_fill
    for c in range(1, 8):
        cell = ws.cell(row=r, column=c)
        cell.border = bd
        cell.alignment = Alignment(vertical="center", wrap_text=(c == 7))
        if c in (2, 6):
            cell.fill = fill
    ws.row_dimensions[r].height = 30

for col, w in zip("ABCDEFG", [6, 16, 24, 34, 20, 7, 62]):
    ws.column_dimensions[col].width = w
ws.freeze_panes = "A2"
ws.auto_filter.ref = "A1:G%d" % ws.max_row

wb.save(XLSX)
hi = sum(1 for r in rows if r[4] == "高")
print("写入 %d 条 | 高风险 %d | 中等 %d" % (len(rows), hi, len(rows) - hi))
print("sheet:", wb.sheetnames)
