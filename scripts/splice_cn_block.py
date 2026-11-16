# -*- coding: utf-8 -*-
"""把 out/scene_cn_block.cs 的字典片段 + 解析方法插进 DeathStats.cs（幂等：已有旧块会先删掉）"""
import io
import os

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
TARGET = os.path.join(ROOT, "src", "DeathStats.cs")
BLOCK = os.path.join(ROOT, "out", "scene_cn_block.cs")

src = io.open(TARGET, encoding="utf-8").read()
block = io.open(BLOCK, encoding="utf-8").read().rstrip() + "\n"

START = "        // ===== 场景中文名（由 scripts/gen_cn_block.py 从填好的对照表生成，改表后重跑脚本）====="
END = "        private static string CleanName(string n)"

helpers = r'''
        private static Dictionary<string, string> sSceneCn;

        /// <summary>
        /// 解析 SceneNameData（「中文名|代号 代号 ...」一行一个名字）。
        /// 同一名字对应多个代号 = 一个大地名管着一片房间（白色宫殿就管着 19 个房间）。
        /// </summary>
        private static void EnsureSceneCn()
        {
            if (sSceneCn != null) return;

            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                string[] lines = SceneNameData.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int bar = line.IndexOf('|');
                    if (bar <= 0) continue;

                    string cn = line.Substring(0, bar).Trim();
                    if (cn.Length == 0) continue;

                    string[] codes = line.Substring(bar + 1)
                        .Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int k = 0; k < codes.Length; k++) d[codes[k]] = cn;
                }
            }
            catch { }

            sSceneCn = d;
        }

        /// <summary>
        /// 场景代号 → 显示名：表里有就用地名（白色宫殿），没有就回落成原始代号。
        /// 一个大地名管着几十个房间，但显示层不补「·06」这种房间号 —— 统计要的是「在哪片区域」。
        /// 想看具体房间代号，看右上角 HUD 第二行，那里永远是原始 sceneName。
        /// </summary>
        private static string SceneCnName(string code)
        {
            if (string.IsNullOrEmpty(code)) return code;
            if (!sUseCnSceneNames) return code.Replace('_', ' ');

            EnsureSceneCn();

            string cn;
            if (sSceneCn.TryGetValue(code, out cn)) return cn;
            return code.Replace('_', ' ');
        }

        /// <summary>
        /// 场景排行按显示名（大地名）合并：白色宫殿有 19 个房间，
        /// 不合并的话右栏会出现十几行一模一样的「白色宫殿」，看不出到底死了几次。
        /// 表里没填的房间仍走原始代号，各自独立，不会互相并进去。
        /// </summary>
        private static Dictionary<string, int> MergeScenesByArea(Dictionary<string, int> byScene)
        {
            Dictionary<string, int> d = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> kv in byScene)
            {
                string area = SceneCnName(kv.Key);
                int n;
                d.TryGetValue(area, out n);
                d[area] = n + kv.Value;
            }
            return d;
        }

'''

if START in src:
    i = src.index(START)
    j = src.index(END, i)
    src = src[:i] + src[j:]

anchor = "        private static string CleanName(string n)"
if anchor not in src:
    raise SystemExit("找不到插入锚点")

src = src.replace(anchor, block + helpers + anchor, 1)
io.open(TARGET, "w", encoding="utf-8", newline="\n").write(src)
print("插入完成，行数", src.count("\n") + 1)
