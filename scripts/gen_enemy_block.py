# -*- coding: utf-8 -*-
"""
从游戏运行时导出的官方语言表生成「敌人中文名」映射块。

输入（游戏 / BepInEx/plugins/XiaoCanLangDump/）：
    sheets.txt          Language._currentEntrySheets 全量文本（含 Journal sheet）
    journal_entries.txt JournalEntryStats 组件清单：击杀字段 / 语言键 / playerDataName

输出：
    out/enemy_cn_block.cs   C# 字典块（键为归一化名字），由 splice_enemy_block.py 插入 DeathStats.cs
    out/enemy_table.json    完整表格数据，供 gen_enemy_table_xlsx.py 生成 Excel
"""
import io
import json
import os
import re
import sys

D = r'C:/Program Files (x86)/Steam/steamapps/common/Hollow Knight/BepInEx/plugins/XiaoCanLangDump'
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'out')


def norm(s):
    """归一化：小写、只保留字母数字。ZombieRunner / ZOMBIE_RUNNER / Zombie Runner(Clone) 归一化后相同。"""
    return re.sub(r'[^a-z0-9]', '', (s or '').lower())


def load_sheet(sheet_name):
    src = io.open(os.path.join(D, 'sheets.txt'), encoding='utf-8').read().split('\n')
    start = None
    for i, line in enumerate(src):
        if line.startswith('##SHEET ' + sheet_name):
            start = i + 1
            break
    if start is None:
        sys.exit('sheets.txt 里找不到 sheet: ' + sheet_name)
    d = {}
    for line in src[start:]:
        if line.startswith('##'):
            break
        if '\t' in line:
            k, v = line.split('\t', 1)
            d[k] = v
    return d


def main():
    sheet = load_sheet('Journal')

    rows = []
    for line in io.open(os.path.join(D, 'journal_entries.txt'), encoding='utf-8'):
        if line.startswith('#') or '\t' not in line:
            continue
        p = line.rstrip('\n').split('\t')
        if len(p) < 11:
            continue
        rows.append({
            'kill_field': p[0],
            'name_key': p[1],
            'desc_key': p[2],
            'note_key': p[3],
            'pd_name': p[4],
            'obj': p[10],
        })

    # 注意：神居（Godmaster）里那批雕像组件的 name_key 都指向 NAME_HOLLOW_KNIGHT，
    # 但它们的 playerDataName 各不相同（Nailsage / Paintmaster / VoidIdol_1 ...），
    # 对应的其实是各自独立的日志条目。所以不能整行剔除，而是在 ALIAS 里按物体名指到正确条目。
    print('有效条目', len(rows))

    # 手工补充：游戏物体名与日志 playerDataName 不一致的几个（按日志描述核对过）
    ALIAS = {
        'white palace fly': 'PALACE_FLY',      # 描述：白色外壳+内部深色液体的苍蝇样生物
        'jellyfish baby': 'JELLYFISH',         # 欧玛被打破后爆出的小水母
        'grave zombie': 'MUMMY',               # 游戏里叫 Grave Zombie，日志条目是 Mummy = 埋葬躯壳
        # 神居雕像：组件的 name_key 都写着 NAME_HOLLOW_KNIGHT，但各自对应独立条目
        'nailsage': 'SAGE_SLY',                # 伟大骨钉贤者斯莱
        'hollowknightprime': 'PURE_VESSEL',    # 纯粹容器
        'fatfluke': 'FAT_FLUKE',               # 贪食吸虫
        'godseekermask': 'GODSEEKER_MASK',     # 风化面具
        'voididol1': 'VOID_IDOL_1',
        'voididol2': 'VOID_IDOL_2',
        'voididol3': 'VOID_IDOL_3',
        'nailbros': 'NAILMASTERS',             # 骨钉大师奥罗 & 马托
        'palelurker': 'PALE_LURKER',           # 苍白潜伏者
        'paintmaster': 'PAINTMASTER',          # 绘画大师席奥
        'hiveknight': 'HIVE_KNIGHT',           # 蜂巢骑士（与 Spitter 共用 killedSpitter）
        'crawler': 'CRAWLER',                  # 物体名 Crawler 同时挂在 HOLLOW_SHADE 和 CRAWLER 上，取小爬虫
        'plant trap': 'SNAPPER_TRAP',          # 愚人吞食者：SnapperTrap 预制体，场景物体名叫 Plant Trap（咬人，不吐弹）
        'plant turret': 'PLANT_SHOOTER',       # 古尔卡本体在场景里的物体名叫 Plant Turret（与 Spike Ball 同一预制体）
        'corpse plant turret': 'PLANT_SHOOTER',# 同上的尸体/休眠版本
        'spike ball': 'PLANT_SHOOTER',         # 古尔卡吐出的刺球，全资源只出现 1 处
    }

    # 官方日志里确实没有条目的敌人，才在这里按字面补译（先确认日志里没有同物异名条��）
    EXTRA = {
    }

    by_id = {}
    for r in rows:
        jid = r['name_key'][5:] if r['name_key'].startswith('NAME_') else None
        if not jid:
            continue
        r['id'] = jid
        r['cn'] = sheet.get('NAME_' + jid, '')
        r['desc'] = sheet.get('DESC_' + jid, '')
        r['note'] = sheet.get('NOTE_' + jid, '')
        by_id[jid] = r

    # 语言表里有、但内存里没有组件实例的条目（神居雕像那批共 11 个），
    # 直接按语言表的 NAME_ 键补成条目，否则别名指不过去。
    filled = 0
    for k, v in sheet.items():
        if not k.startswith('NAME_') or not v:
            continue
        jid = k[5:]
        if jid in by_id:
            continue
        by_id[jid] = {
            'id': jid, 'cn': v, 'desc': sheet.get('DESC_' + jid, ''),
            'note': sheet.get('NOTE_' + jid, ''), 'pd_name': '', 'obj': '',
            'kill_field': '', 'no_instance': True,
        }
        filled += 1
    print('语言表补充条目', filled, '｜条目总数', len(by_id))

    alias_ids = {}
    for k, jid in ALIAS.items():
        if jid in by_id:
            alias_ids[norm(k)] = jid
        else:
            print('  ! 别名指向的日志条目不存在:', k, jid)

    # 构建 归一化键 -> 日志ID；冲突时优先「playerDataName 与日志ID 一致」的那条
    table = {}
    conflicts = []
    for jid, r in by_id.items():
        ks = {norm(r['pd_name']), norm(jid)}
        for k in ks:
            if not k:
                continue
            if k in table and table[k] != jid:
                same_pd = norm(r['pd_name']) == k and norm(r['pd_name']) == norm(jid)
                prev = by_id[table[k]]
                prev_same = norm(prev['pd_name']) == k and norm(prev['pd_name']) == norm(jid)
                if same_pd and not prev_same:
                    conflicts.append((k, table[k], jid, 'keep-new'))
                    table[k] = jid
                else:
                    conflicts.append((k, table[k], jid, 'keep-old'))
                continue
            table[k] = jid

    for k, jid in alias_ids.items():
        table[k] = jid

    if conflicts:
        print('键冲突 %d 处：' % len(conflicts))
        for k, a, b, how in conflicts:
            print('  %-24s %s(%s) vs %s(%s) -> %s'
                  % (k, a, by_id[a]['cn'], b, by_id[b]['cn'], 'new' if how == 'keep-new' else 'old'))

    table_cn = {k: by_id[jid]['cn'] for k, jid in table.items() if by_id[jid]['cn']}
    table_cn.update(EXTRA)

    pairs = sorted((k, cn) for k, cn in table_cn.items() if cn)
    print('映射键', len(pairs), '｜覆盖日志条目', len({jid for jid in table.values()}))

    os.makedirs(OUT, exist_ok=True)
    # ---- C# 块 ----
    lines = []
    lines.append('        // ===== 敌人中文名（由 scripts/gen_enemy_block.py 从游戏官方语言表生成，勿手改）=====')
    lines.append('        // 键是「归一化」后的名字：小写、只留字母数字。查表前先归一化，')
    lines.append('        // 所以 Zombie Runner / ZOMBIE_RUNNER / ZombieRunner(Clone) 会命中同一项。')
    lines.append('        private static readonly Dictionary<string, string> EnemyCn = new Dictionary<string, string>')
    lines.append('        {')
    per_line = 3
    buf = []
    for k, cn in pairs:
        buf.append('{ "%s", "%s" }' % (k, cn))
        if len(buf) == per_line:
            lines.append('            ' + ', '.join(buf) + ',')
            buf = []
    if buf:
        lines.append('            ' + ', '.join(buf) + ',')
    lines.append('        };')
    io.open(os.path.join(OUT, 'enemy_cn_block.cs'), 'w', encoding='utf-8', newline='\n').write('\n'.join(lines) + '\n')

    # ---- JSON（给 Excel 生成器用）----
    # 神居那 11 条在内存里没有组件实例，但 PlayerData 里各有一模一样的击杀字段，照实补上。
    FILL_FIELD = {
        'HIVE_KNIGHT': 'killedHiveKnight',
        'FAT_FLUKE': 'killedFatFluke',
        'PALE_LURKER': 'killedPaleLurker',
        'NAILMASTERS': 'killedNailBros',
        'PAINTMASTER': 'killedPaintmaster',
        'SAGE_SLY': 'killedNailsage',
        'PURE_VESSEL': 'killedHollowKnightPrime',
        'GODSEEKER_MASK': 'killedGodseekerMask',
        'VOID_IDOL_1': 'killedVoidIdol_1',
        'VOID_IDOL_2': 'killedVoidIdol_2',
        'VOID_IDOL_3': 'killedVoidIdol_3',
    }
    data = []
    for jid in sorted(by_id):
        r = by_id[jid]
        data.append({
            'id': jid,
            'kill_field': r['kill_field'] or FILL_FIELD.get(jid, ''),
            'cn': r['cn'],
            'desc': r['desc'],
            'note': r['note'],
        })
    io.open(os.path.join(OUT, 'enemy_table.json'), 'w', encoding='utf-8').write(
        json.dumps(data, ensure_ascii=False, indent=1))
    print('已写 out/enemy_cn_block.cs 与 out/enemy_table.json')


if __name__ == '__main__':
    main()
