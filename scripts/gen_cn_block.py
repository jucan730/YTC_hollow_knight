# -*- coding: utf-8 -*-
"""
把《空洞骑士_名称对照表.xlsx》转成 DeathStats.cs 的中文名字典。

要点：中文名列是合并单元格，openpyxl 只有左上角那格有值，
必须按 merged_cells.ranges 把值铺到整段，否则会误判成「只填了几条」。

输出格式：每个名字一行「中文名|代号 代号 ...」，同名多房间共用一个名字。
"""
import os
import io
from collections import defaultdict

import openpyxl

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
XLSX = r"C:/Users/ruitg/Desktop/空洞骑士_名称对照表 副本.xlsx"

wb = openpyxl.load_workbook(XLSX, data_only=True)
ws = wb["场景名称"]

CN_COL, CODE_COL, AREA_COL = 4, 2, 3

# 1. 展开合并单元格
merged_fill = {}
for rng in ws.merged_cells.ranges:
    if rng.min_col != CN_COL or rng.max_col != CN_COL:
        continue
    v = ws.cell(row=rng.min_row, column=CN_COL).value
    for r in range(rng.min_row, rng.max_row + 1):
        merged_fill[r] = v

rows = []
for r in range(2, ws.max_row + 1):
    code = ws.cell(row=r, column=CODE_COL).value
    if not code:
        continue
    area = ws.cell(row=r, column=AREA_COL).value
    cn = ws.cell(row=r, column=CN_COL).value
    if not cn:
        cn = merged_fill.get(r)
    cn = str(cn).strip() if cn else ""
    rows.append((str(code).strip(), str(area or "").strip(), cn))

# 2. 按名字归类
by_name = defaultdict(list)
unknown = []
for code, area, cn in rows:
    if not cn or cn == "未知":
        unknown.append((code, area))
    else:
        by_name[cn].append(code)

# 运行时名字比资源名短的场景：辐光战的场景资源叫 Dream_Final_Boss，
# 但 GameManager.sceneName 返回的是 Dream_Final，表里只填了资源名就会掉回英文「Dream Final」。
# 这里手动补一条别名，重跑脚本也不会丢。
EXTRA = {
    "Dream_Final": "梦境",
    # 空洞骑士最终战的黑卵圣殿。表里只列了区域房间，这个 BOSS 房是单独的场景，
    # 不补就一直显示英文「Room Final Boss Core」。
    "Room_Final_Boss_Core": "黑卵圣殿",
}
for code, cn in EXTRA.items():
    if code not in by_name[cn]:
        by_name[cn].append(code)
    if code in [c for c, _ in unknown]:
        unknown = [(c, a) for c, a in unknown if c != code]

def q(s):
    return '"' + s.replace('"', '\\"') + '"'

lines = []
lines.append("        // ===== 场景中文名（由 scripts/gen_cn_block.py 从填好的对照表生成，改表后重跑脚本）=====")
lines.append("        /// <summary>中文名|代号 代号 ...；同名多房间共用一行</summary>")
lines.append("        private const string SceneNameData =")

parts = []
keys = sorted(by_name, key=lambda n: -len(by_name[n]))
for i, name in enumerate(keys):
    body = name + "|" + " ".join(sorted(by_name[name]))
    suffix = "\\n" if i < len(keys) - 1 else ""
    parts.append(q(body) + ' + "' + suffix + '"' if suffix else q(body))

chunk, rows_out = [], []
for p in parts:
    chunk.append(p)
    if len(chunk) == 2:
        rows_out.append(" + ".join(chunk))
        chunk = []
if chunk:
    rows_out.append(" + ".join(chunk))
for i, ln in enumerate(rows_out):
    if i == len(rows_out) - 1:
        lines.append("            " + ln + ";")
    else:
        lines.append("            " + ln + " +")

out = "\n".join(lines) + "\n"
os.makedirs(os.path.join(ROOT, "out"), exist_ok=True)
io.open(os.path.join(ROOT, "out", "scene_cn_block.cs"), "w", encoding="utf-8", newline="\n").write(out)

print("总行", len(rows))
print("有名字", sum(len(v) for v in by_name.values()), "个场景 /", len(by_name), "个名字")
print("未知或空白", len(unknown))
print("片段字节", len(out.encode("utf-8")))
print()
print("未覆盖（代号 / 区域）：")
for code, area in unknown:
    print("   ", code, "|", area)
