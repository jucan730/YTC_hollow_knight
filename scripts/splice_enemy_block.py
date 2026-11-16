# -*- coding: utf-8 -*-
"""把 out/enemy_cn_block.cs 幂等插入 src/DeathStats.cs，替换旧的 EnemyCn 字典。

锚点：包含 `Dictionary<string, string> EnemyCn` 的声明行，到其后第一行 `};`。
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, '..', 'src', 'DeathStats.cs')
BLOCK = os.path.join(HERE, '..', 'out', 'enemy_cn_block.cs')

src = io.open(SRC, encoding='utf-8').read().split('\n')
block = io.open(BLOCK, encoding='utf-8').read().rstrip('\n').split('\n')

start = None
for i, line in enumerate(src):
    if 'Dictionary<string, string> EnemyCn' in line:
        start = i
        break
if start is None:
    sys.exit('未找到 EnemyCn 声明')

end = None
for j in range(start, len(src)):
    if src[j].strip() == '};':
        end = j
        break
if end is None:
    sys.exit('未找到 EnemyCn 结束的 };')

out = src[:start] + block + src[end + 1:]
io.open(SRC, 'w', encoding='utf-8', newline='\n').write('\n'.join(out))
print('已替换 EnemyCn：原 %d 行 -> 新 %d 行（条目 %d）'
      % (end - start + 1, len(block), sum(1 for l in block if '{ "' in l)))
