# -*- coding: utf-8 -*-
import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
from docx import Document
from docx.table import Table
from docx.text.paragraph import Paragraph

path = sys.argv[1] if len(sys.argv) > 1 else r'C:\Users\ruitg\WorkBuddy\2026-11-14-15-19-46\hk-mod\out\小残死亡统计_安装步骤.docx'
d = Document(path)

print('表格数', len(d.tables))


def style_name(p):
    try:
        s = p.style
        return s.name if s is not None else '(none)'
    except Exception:
        return '(err)'


body = d.element.body
for child in body.iterchildren():
    tag = child.tag.split('}')[-1]
    if tag == 'p':
        p = Paragraph(child, d)
        t = p.text.strip()
        if t:
            print('[%-10s] %s' % (style_name(p), t[:72]))
    elif tag == 'tbl':
        tb = Table(child, d)
        print('  <表格 %d行 %d列>' % (len(tb.rows), len(tb.columns)))
        for r in tb.rows:
            print('    ' + ' | '.join(c.text.strip()[:40] for c in r.cells))
