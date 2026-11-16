# -*- coding: utf-8 -*-
r"""
打包 YTC_死亡统计mod 到桌面。
输出：桌面\YTC_死亡统计mod_v0.9.25.zip
包内结构：YTC_死亡统计mod_v0.9.25\BepInEx\plugins\YTC_DeathStats.dll + README.txt
"""
import os
import sys
import shutil
import zipfile

VER = "0.9.25"
NAME = "YTC_死亡统计mod"
DLLNAME = "YTC_DeathStats.dll"
DATADIR = "YTC_DeathStats"

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PKG = os.path.join(ROOT, "out", "pkg", NAME + "_v" + VER)
DESKTOP = os.path.join(os.path.expanduser("~"), "Desktop")
ZIP_PATH = os.path.join(DESKTOP, NAME + "_v" + VER + ".zip")

README = """YTC_死亡统计mod  v0.9.25
================================================

【怎么装】
1) 解压本压缩包，会得到一个 BepInEx 文件夹。
2) 找到游戏根目录（就是 hollow_knight.exe 所在的那个文件夹）：

     Steam 默认：
     C:\\Program Files (x86)\\Steam\\steamapps\\common\\Hollow Knight\\

     装在别的盘：Steam 库 -> 右键游戏 -> 管理 -> 浏览本地文件

3) 把解压出来的 BepInEx 文件夹整个拖进游戏根目录。

4) 如果弹出「替换或跳过文件」，上面写着
   “目标已包含一个名为 YTC_DeathStats.dll 的文件”，
   点第一项「替换目标中的文件」。
   不要点「跳过该文件」—— 点了等于没装，用的还是旧版本。
   （第一次安装不会有这个弹窗，拖进去就放好了。）

装完的位置必须正好是这一条，多一层少一层都不会被加载：
  Hollow Knight\\BepInEx\\plugins\\YTC_DeathStats.dll

[!] 前置条件：游戏必须已经装过 BepInEx。
    打开游戏根目录，能看到 winhttp.dll 和 BepInEx 文件夹才算装过；
    没有的话先装 BepInEx 5.4.23.4（x64 版），再回到上面第 1 步。

[!] 从旧版升级：老版本的文件名是 XiaoCanDeathStats.dll 或 DeathStats.dll，
    装新版之前先把它们从 BepInEx\\plugins 里删掉，
    否则同一个插件会被加载两次，一次死亡记两遍。

【首次启动】
进游戏后等约 10 秒，按 F4 打开面板。
第一次运行会自动生成配置文件（不用手动建）：
  Hollow Knight\\BepInEx\\config\\deathstats.hollowknight.cfg

【操作】
  F4              打开 / 关闭统计面板
  ↑ ↓             上下滚动
  ← →             左右切换栏目（死因 / 场景 …）
  回车            确认当前选中项
  ESC             关闭面板

【统计存在哪】
  BepInEx\\plugins\\YTC_DeathStats\\slot1.tsv
  文本文件，Tab 分隔；想清零直接删这个文件，游戏内面板也能清空。
  从老版本升级过来的，旧目录里的记录会在第一次启动时自动搬到这里。

【怎么看日志】
本 mod 写两份日志，用途不一样：

  1) 判定日志（排查问题看这个）
     BepInEx\\plugins\\YTC_DeathStats\\diag-20261116.log
     只记死因、怪物归属的判定过程；追加写、重启不丢，一天一个文件。
     里面长得像这样：
       18:42:07  [死亡统计] +1 | 槽位=1 | 场景=Crossroads_48 | 死因=躯壳守卫 | 类型=敌人·冲击波
       18:42:07  [死亡统计] 攻击物归属 | 物体=Shockwave Spurt L | 主人=躯壳守卫 | 依据=预制体名+首词

  2) BepInEx 总日志
     BepInEx\\LogOutput.log
     所有插件混在一起，每次启动会被重写，只留最后一次。

  两份都是纯文本，右键 -> 打开方式 -> 记事本 就能看。
  要反馈问题，把出问题的那几行复制出来即可，不用发整个文件。
  不想让它写判定日志：把 cfg 里的 AppendDiagLog 改成 false。

【常用配置】
配置文件：BepInEx\\config\\deathstats.hollowknight.cfg

  ShowRoomHud = true / false      右上角显示当前房间（默认 false，已关）
  ShowDeathMarkers = true         死亡地点立红色图钉
  DeathMarkerHeightRatio = 0.33   图钉在身上的高度：0 脚底 / 0.5 腰 / 1 头顶
  DeathMarkerScale = 1            图钉大小
  AppendDiagLog = true            写判定日志（排查问题用）
  TogglePanel = F4                改开关键

【卸载】
删掉 BepInEx\\plugins\\YTC_DeathStats.dll 即可。
可选：一并删掉 BepInEx\\plugins\\YTC_DeathStats 文件夹与对应的 cfg。

详细说明见《YTC_死亡统计mod_安装步骤.docx》。
"""


def main():
    dll_src = os.path.join(
        r"C:\Program Files (x86)\Steam\steamapps\common\Hollow Knight",
        "BepInEx", "plugins", DLLNAME)

    if not os.path.isfile(dll_src):
        print("找不到 DLL: " + dll_src)
        return 1

    # 每次重建，避免旧包里的残留文件被一起打进去
    if os.path.isdir(PKG):
        shutil.rmtree(PKG)

    dll_dst = os.path.join(PKG, "BepInEx", "plugins", DLLNAME)
    os.makedirs(os.path.dirname(dll_dst), exist_ok=True)
    shutil.copy2(dll_src, dll_dst)

    # utf-8-sig 带 BOM，老记事本打开也不乱码
    with open(os.path.join(PKG, "README.txt"), "w", encoding="utf-8-sig") as f:
        f.write(README)

    # 安装说明一起放进来，解压就是「dll + 图文步骤」，不用再单独找文件
    doc_src = os.path.join(ROOT, "out", "doc", "YTC_死亡统计mod_安装步骤.docx")
    if os.path.isfile(doc_src):
        shutil.copy2(doc_src, os.path.join(PKG, "YTC_死亡统计mod_安装步骤.docx"))
    else:
        print("提醒：没找到安装说明 docx，压缩包里将不含文档: " + doc_src)

    if os.path.exists(ZIP_PATH):
        os.remove(ZIP_PATH)

    base = os.path.dirname(PKG)
    with zipfile.ZipFile(ZIP_PATH, "w", zipfile.ZIP_DEFLATED) as z:
        for root, dirs, files in os.walk(PKG):
            for fn in files:
                full = os.path.join(root, fn)
                rel = os.path.relpath(full, base)
                z.write(full, rel.replace("\\", "/"))

    print("zip: " + ZIP_PATH)
    print("大小: %.1f KB" % (os.path.getsize(ZIP_PATH) / 1024.0))
    with zipfile.ZipFile(ZIP_PATH) as z:
        for n in z.namelist():
            print("  " + n)
    return 0


if __name__ == "__main__":
    sys.exit(main())
