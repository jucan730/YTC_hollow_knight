# -*- coding: utf-8 -*-
"""生成《空洞骑士_怪物名称与中文译名.xlsx》（v2）

v1 的毛病：拿「场景扫描出的实例名」当全集，但那次 level 文件解析不全，
Zombie Runner / Buzzer / Climber / Radiance 这些名字根本没进扫描结果，
于是表里只有 104 条，辐光这种 BOSS 也被漏在外面。

v2 改成：**以官方猎人日志 170 条为骨架**（这是游戏认定的全部可击杀目标，
中文译名、日志描述都齐全），再尽量把每条对回场景里的实例名。

数据源：
  out/enemy_table.json   官方猎人日志 170 条（id / 官方中文 / 描述）
  src/DeathStats.cs      EnemyCn 表（221 键）
  out/enemy_candidates.txt  场景对象名扫描（覆盖不全，只用来补"场景里叫什么"）
  out/sheet_data.json    场景实例清单（同上，仅参考）
"""
import json
import re
import os
import difflib
from openpyxl import Workbook
from openpyxl.styles import Font, PatternFill, Alignment, Border, Side
from openpyxl.utils import get_column_letter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(os.path.expanduser("~"), "Desktop", "空洞骑士_怪物名称与中文译名.xlsx")


def norm(s):
    return re.sub(r"[^a-z0-9]", "", s.lower())


def strip_num(s):
    return re.sub(r"[\s_\-]*\(?\d+\)?$", "", s).strip()


# ---------- 数据 ----------
src = open(os.path.join(ROOT, "src", "DeathStats.cs"), encoding="utf-8").read()
m = re.search(r"EnemyCn = new Dictionary<string, string>\s*\{(.*?)\n\s*\};", src, re.S)
ENEMY_CN = dict(re.findall(r'\{ "([a-z0-9]+)", "([^"]+)" \}', m.group(1)))

journal = json.load(open(os.path.join(ROOT, "out", "enemy_table.json"), encoding="utf-8"))
sheet = json.load(open(os.path.join(ROOT, "out", "sheet_data.json"), encoding="utf-8"))

cand = {}
for line in open(os.path.join(ROOT, "out", "enemy_candidates.txt"), encoding="utf-8"):
    p = line.rstrip("\n").rsplit(None, 1)
    if len(p) == 2 and p[1].isdigit():
        k = norm(strip_num(p[0]))
        c = int(p[1])
        if k and (k not in cand or c > cand[k][1]):
            cand[k] = (p[0], c)

# BOSS 判定：场景清单里已标成 BOSS 的中文名
boss_cn_map = {}
for e in sheet["enemies"]:
    if "BOSS" in e["kind"]:
        k = norm(e["name"])
        if k in ENEMY_CN:
            boss_cn_map[ENEMY_CN[k]] = True

BOSS_IDS = {
    "HORNET", "MANTIS_LORD", "MAGE_LORD", "FINAL_BOSS", "PURE_VESSEL",
    "HOLLOW_KNIGHT", "GRIMM", "NIGHTMARE_GRIMM", "FALSEKNIGHT", "MAWLEK",
    "DUNG_DEFENDER", "BLACK_KNIGHT", "JAR_COLLECTOR", "MEGA_JELLYFISH", "MIMIC_SPIDER",
    "INFECTED_KNIGHT", "FLUKE_MOTHER", "TRAITOR_LORD", "HIVE_KNIGHT", "GREY_PRINCE",
    "WHITE_DEFENDER", "NAILMASTERS", "PAINTMASTER", "SAGE_SLY", "OBLOBBLE",
    "MEGA_BEAM_MINER", "BIGFLY", "BIGBUZZER", "LOBSTER_LANCER",
}
NON_COMBAT = {
    "KIN_SEAL": "非战斗：纹章封印，不会攻击",
    "HUNTERMARK": "非战斗：猎人的标记，成就物",
    "GODSEEKER_MASK": "非战斗：神居风化面具",
    "VOID_IDOL_1": "非战斗：虚空神像（收集物）",
    "VOID_IDOL_2": "非战斗：虚空神像（收集物）",
    "VOID_IDOL_3": "非战斗：虚空神像（收集物）",
    "MENDERBUG": "非战斗：维修虫，室内装饰",
    "EGG_SAC": "非战斗：臭蛋口袋，破坏后出小虫",
}

PREFIX = {"ZOM": "Zombie", "COL": "Colosseum", "CRYS": "Crystal", "FUNG": "Fungus"}
SPECIAL = {
    "BIGBUZZER": "Big Buzzer",
    "BIGFLY": "Big Fly",
    "BIG_BEE": "Big Bee",
    "FALSEKNIGHT": "False Knight",
    "FINAL_BOSS": "Radiance",
    "HUNTERMARK": "Hunter's Mark",
    "UUMUU": "Uumuu",
    "ZOTE_BALLOON": "Zote Balloon",
}


def pretty(eid):
    if eid in SPECIAL:
        return SPECIAL[eid]
    parts = [p for p in eid.split("_") if p]
    out = []
    for i, p in enumerate(parts):
        if i == 0 and p in PREFIX:
            out.append(PREFIX[p])
        elif p.isdigit():
            out.append(p)
        else:
            out.append(p.capitalize())
    return " ".join(out)


# 已人工核实、场景对象名与日志英文名完全不同的，直接写死，不再靠匹配
HARD_INSTANCE = {
    # Plant Trap 只和 Snapper 同房（level128/131/147/150/160/201/224），
    # 预制体名 SnapperTrap → 日志 SNAPPER_TRAP = 愚人吞食者（食肉植物，谁靠近就咬谁）
    "SNAPPER_TRAP": ("Plant Trap", ""),
    # Plant Turret 只和 PlantShooter 同房（level140/141/156），吐刺球的是它
    "PLANT_SHOOTER": ("Plant Turret", ""),
}


def find_instance(t):
    """日志条目 → 场景里实际叫什么（尽力匹配，匹配不到返回 None）"""
    if t["id"] in HARD_INSTANCE:
        return HARD_INSTANCE[t["id"]]
    k = norm(t["id"])
    if k in cand:
        return cand[k]
    # 日志 ID 是内部代号（FINAL_BOSS），可读英文名才是场景里的叫法（Radiance）
    k2 = norm(pretty(t["id"]))
    if k2 in cand:
        return cand[k2]
    for kk, v in ENEMY_CN.items():
        if v == t["cn"] and kk in cand:
            return cand[kk]
    parts = [p for p in t["id"].split("_") if p]
    if parts and parts[0] in PREFIX:
        alt = norm(PREFIX[parts[0]] + "".join(p.capitalize() for p in parts[1:]))
        if alt in cand:
            return cand[alt]
    hit = difflib.get_close_matches(k, list(cand.keys()), n=1, cutoff=0.9)
    if hit:
        return cand[hit[0]]
    return None


rows = []
for t in journal:
    inst = find_instance(t)
    note = NON_COMBAT.get(t["id"], "")
    if inst is None:
        note = (note + "；" if note else "") + "场景扫描里没找到同名实例"
    kind = "头目/BOSS" if (t["id"] in BOSS_IDS or t["cn"] in boss_cn_map) else "普通敌人"
    rows.append({
        "en": pretty(t["id"]),
        "cn": t["cn"],
        "id": t["id"],
        "kind": kind,
        "inst": inst[0] if inst else "",
        "cnt": inst[1] if inst else "",
        "note": note,
        "desc": (t.get("desc") or "").replace("\n", " "),
    })

rows.sort(key=lambda r: (0 if "BOSS" in r["kind"] else 1, r["cn"]))

# ---------- 写表 ----------
wb = Workbook()
ws = wb.active
ws.title = "怪物总表170"

HEAD_FILL = PatternFill("solid", fgColor="1F4E3D")
HEAD_FONT = Font(name="微软雅黑", bold=True, color="FFFFFF", size=11)
BODY_FONT = Font(name="微软雅黑", size=10)
MONO_FONT = Font(name="Consolas", size=10)
THIN = Side(style="thin", color="D9D9D9")
BORDER = Border(left=THIN, right=THIN, top=THIN, bottom=THIN)
BAND = PatternFill("solid", fgColor="F2F7F4")
BOSS_FILL = PatternFill("solid", fgColor="FDE9E9")
GREY_FILL = PatternFill("solid", fgColor="EFEFEF")

headers = ["序号", "英文名", "中文名", "日志ID", "类别", "场景实例名", "场景出现次数", "猎人日志描述"]
ws.append(headers)
for c in range(1, len(headers) + 1):
    cell = ws.cell(row=1, column=c)
    cell.fill, cell.font = HEAD_FILL, HEAD_FONT
    cell.alignment = Alignment(horizontal="center", vertical="center")
    cell.border = BORDER
ws.row_dimensions[1].height = 26

for i, r in enumerate(rows, start=1):
    note = r["note"]
    desc = r["desc"] or ""
    if note:
        desc = ("[%s] " % note) + desc
    ws.append([i, r["en"], r["cn"], r["id"], r["kind"], r["inst"], r["cnt"], desc])
    row = ws.max_row
    for c in range(1, 9):
        cell = ws.cell(row=row, column=c)
        cell.font = MONO_FONT if c in (2, 4, 6) else BODY_FONT
        cell.border = BORDER
        cell.alignment = Alignment(
            horizontal="center" if c in (1, 5, 7) else "left",
            vertical="center", wrap_text=(c == 8))
    fill = None
    if r["id"] in NON_COMBAT:
        fill = GREY_FILL
    elif "BOSS" in r["kind"]:
        fill = BOSS_FILL
    elif i % 2 == 0:
        fill = BAND
    if fill:
        for c in range(1, 9):
            ws.cell(row=row, column=c).fill = fill
    ws.row_dimensions[row].height = 20

for i, w in enumerate([6, 26, 20, 26, 13, 24, 12, 70], start=1):
    ws.column_dimensions[get_column_letter(i)].width = w
ws.freeze_panes = "A2"
ws.auto_filter.ref = "A1:H%d" % ws.max_row

# ---------- Sheet2：场景实例清单 ----------
ws2 = wb.create_sheet("场景实例清单")
heads2 = ["序号", "场景里的名字", "中文名", "类别", "出现次数", "备注"]
ws2.append(heads2)
for c in range(1, len(heads2) + 1):
    cell = ws2.cell(row=1, column=c)
    cell.fill, cell.font = HEAD_FILL, HEAD_FONT
    cell.alignment = Alignment(horizontal="center", vertical="center")
    cell.border = BORDER
ws2.row_dimensions[1].height = 26

inst_rows = []
for e in sheet["enemies"]:
    k = norm(e["name"])
    cn = ENEMY_CN.get(k, "")
    if not cn:
        cn = ENEMY_CN.get(norm(strip_num(e["name"])), "")
    inst_rows.append((e["name"], cn, e["kind"], e.get("count", 0)))
# Fat Fly 是关卡里的 GameObject 名，官方语言表没有这个键，
# 但 level147（苍绿之径 Fungus1_19）内嵌 4 处 BlobFlyer 预制体引用，
# 场景里 4 只 Fat Fly 就是 4 只奥波。
if not any(norm(r[0]) == "fatfly" for r in inst_rows):
    inst_rows.append(("Fat Fly", "奥波", "普通敌人", 0))
inst_rows.sort(key=lambda x: (0 if "BOSS" in x[2] else 1, -int(x[3]), x[0].lower()))

for i, (n, cn, kind, cnt) in enumerate(inst_rows, start=1):
    note = "" if cn else "中文表无此键（内部代号）"
    ws2.append([i, n, cn, kind, cnt, note])
    row = ws2.max_row
    for c in range(1, 7):
        cell = ws2.cell(row=row, column=c)
        cell.font = MONO_FONT if c == 2 else BODY_FONT
        cell.border = BORDER
        cell.alignment = Alignment(
            horizontal="center" if c in (1, 4, 5) else "left", vertical="center")
    if not cn:
        for c in range(1, 7):
            ws2.cell(row=row, column=c).fill = BOSS_FILL
    elif i % 2 == 0:
        for c in range(1, 7):
            ws2.cell(row=row, column=c).fill = BAND

for i, w in enumerate([6, 32, 20, 14, 12, 34], start=1):
    ws2.column_dimensions[get_column_letter(i)].width = w
ws2.freeze_panes = "A2"
ws2.auto_filter.ref = "A1:F%d" % ws2.max_row

# ---------- Sheet3：说明 ----------
ws3 = wb.create_sheet("说明")
notes = [
    ("表一：怪物总表170",
     "以游戏内置猎人日志的 170 条为全集——这是官方认定的全部可击杀目标，"
     "辐光、纯粹容器、梦魇之王、螳螂领主等 BOSS 都在里面。中文名来自游戏语言表，170 条全部有值。"),
    ("表二：场景实例清单",
     "从游戏关卡文件里扫出来的敌人实例名（按名字去重，104 条）。这次扫描不完整，"
     "Zombie Runner / Buzzer / Climber 等都没扫到，只能用来查「这怪在场景里叫什么」，不能当全集。"),
    ("", ""),
    ("为什么改口径",
     "第一版把「场景实例清单」当全集，所以只有 104 条，辐光这类 BOSS 被漏在外面。"
     "真正权威的是猎人日志 170 条，表一以它为准。"),
    ("", ""),
    ("颜色说明", ""),
    ("浅红行", "BOSS"),
    ("浅灰行", "非战斗条目（束缚封印、虚空神像、风化面具、猎人的标记、维修虫、臭蛋口袋）"),
    ("描述前缀", "描述列开头方括号里的话是备注，比如「场景扫描里没找到同名实例」表示这条只在日志里有"),
]
ws3.append(["项目", "内容"])
for c in (1, 2):
    cell = ws3.cell(row=1, column=c)
    cell.fill, cell.font = HEAD_FILL, HEAD_FONT
    cell.border = BORDER
    cell.alignment = Alignment(horizontal="center", vertical="center")
for k, v in notes:
    ws3.append([k, v])
    r = ws3.max_row
    ws3.cell(row=r, column=1).font = Font(name="微软雅黑", bold=True, size=10)
    c2 = ws3.cell(row=r, column=2)
    c2.font = BODY_FONT
    c2.alignment = Alignment(wrap_text=True, vertical="center")
    ws3.row_dimensions[r].height = 40 if len(v) > 50 else 22
ws3.column_dimensions["A"].width = 20
ws3.column_dimensions["B"].width = 95

wb.save(OUT)
boss_n = sum(1 for r in rows if "BOSS" in r["kind"])
inst_n = sum(1 for r in rows if r["inst"])
print("saved:", OUT)
print("总表 %d 条（BOSS %d）| 对上场景实例 %d | 场景清单 %d 条"
      % (len(rows), boss_n, inst_n, len(inst_rows)))
