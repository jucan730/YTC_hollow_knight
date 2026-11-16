# -*- coding: utf-8 -*-
"""对照图：v0.8.2 旧的标记开关 vs v0.8.3 新的描边开关。
只画标题栏那一条，坐标/颜色照 DeathStats.cs 的常量。"""
from PIL import Image, ImageDraw, ImageFont

FONT = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 20)
FONT_S = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 15)

PANEL_BG = (11, 13, 15)
SLOT_BG_OFF = (26, 28, 31)
SLOT_TX_OFF = (204, 209, 217)
FOCUS_GOLD = (189, 156, 71)
FOCUS_TX = (13, 13, 15)
OLD_SLOT_IDLE = (84, 74, 43)
NEW_SLOT_IDLE = (56, 48, 26)
SLOT_TX_IDLE = (240, 209, 128)
UNDER_GOLD = (230, 194, 97)
SW_FILL = (26, 28, 31)
SW_EDGE_ON = (209, 176, 87)
SW_TX_ON = (240, 209, 128)
WHITE = (255, 250, 235)
PANEL_X = 120

W, H = 1920, 340
img = Image.new("RGB", (W, H), (8, 9, 11))
d = ImageDraw.Draw(img)


def caption(text, oy):
    d.text((PANEL_X, oy - 26), text, font=FONT_S, fill=(160, 160, 160))


def tabs(d, oy, slot_idle):
    d.rectangle([PANEL_X, oy - 34, PANEL_X + 1680, oy + 60], fill=PANEL_BG)
    names = ["全部", "槽1", "槽2", "槽3", "槽4"]
    for i, n in enumerate(names):
        x = PANEL_X + 44 + i * 132
        cur = (i == 4)
        d.rectangle([x, oy, x + 120, oy + 44], fill=slot_idle if cur else SLOT_BG_OFF)
        tw = d.textlength(n, font=FONT)
        d.text((x + 60 - tw / 2, oy + 10), n, font=FONT,
               fill=SLOT_TX_IDLE if cur else SLOT_TX_OFF)
        if cur:
            d.rectangle([x, oy + 41, x + 120, oy + 44], fill=UNDER_GOLD)


def focus_ring(d, x, y, w, h, pad, th):
    d.rectangle([x - pad, y - pad, x + w + pad, y - pad + th - 1], fill=WHITE)
    d.rectangle([x - pad, y + h + pad - th + 1, x + w + pad, y + h + pad], fill=WHITE)
    d.rectangle([x - pad, y - pad, x - pad + th - 1, y + h + pad], fill=WHITE)
    d.rectangle([x + w + pad - th + 1, y - pad, x + w + pad, y + h + pad], fill=WHITE)


# ---- 上排：旧版 ----
oy = 90
caption("v0.8.2 旧：开关紧贴槽4（间距 12px，和标签间距一样）+ 实心金底 + 3px 外的白框", oy)
tabs(d, oy, OLD_SLOT_IDLE)
x, y = PANEL_X + 704, oy
d.rectangle([x, y, x + 208, y + 44], fill=FOCUS_GOLD)
t = "标记 ● 开"
tw = d.textlength(t, font=FONT)
d.text((x + 104 - tw / 2, y + 10), t, font=FONT, fill=FOCUS_TX)
focus_ring(d, x, y, 208, 44, 3, 2)

# ---- 下排：新版 ----
oy = 240
caption("v0.8.3 新：开关右对齐远离标签页，深色填充 + 状态描边；焦点框贴边 1px", oy)
tabs(d, oy, NEW_SLOT_IDLE)
x, y, w, h = PANEL_X + 1428, oy, 208, 44
d.rectangle([x, y, x + w, y + h], fill=SW_EDGE_ON)
d.rectangle([x + 2, y + 2, x + w - 2, y + h - 2], fill=SW_FILL)
t = "死亡标记 · 开"
tw = d.textlength(t, font=FONT)
d.text((x + w / 2 - tw / 2, y + 10), t, font=FONT, fill=SW_TX_ON)
focus_ring(d, x, y, w, h, 1, 2)

img.save(r"C:\Users\ruitg\WorkBuddy\2026-11-14-15-19-46\hk-mod\out\btn_compare.png")
print("saved")
