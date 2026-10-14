#!/usr/bin/env python3
"""Rebuild audit report from review/pages.json. Requires a separately installed Korean font."""
from pathlib import Path
import os,json,html
from markdown_it import MarkdownIt
from weasyprint import HTML
ROOT=Path(__file__).resolve().parents[2]
reg=Path(os.environ.get('MF_REVIEW_FONT_REGULAR','/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc'))
bold=Path(os.environ.get('MF_REVIEW_FONT_BOLD','/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc'))
if not reg.is_file() or not bold.is_file():
    raise SystemExit('Install a Korean font and set MF_REVIEW_FONT_REGULAR / MF_REVIEW_FONT_BOLD to its paths. Fonts are not included.')
css=(ROOT/'review/styles.css').read_text().replace('file:///usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc',reg.resolve().as_uri()).replace('file:///usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc',bold.resolve().as_uri())
md=MarkdownIt('commonmark',{'html':True}).enable('table');blocks=[]
for i,p in enumerate(json.loads((ROOT/'review/pages.json').read_text())):
    cls='cover' if i==0 else 'character' if 22<=i<=31 else 'normal'
    title=html.escape(p['title']).replace('\n','<br>')
    blocks.append(f'<section class="{cls}"><div class="eyebrow">{html.escape(p["section"])}</div><h1>{title}</h1>{md.render(p["body"])}</section>')
doc='<!doctype html><html lang="ko"><meta charset="utf-8"><title>기억 공방 v0.7.1 교차검토·수정 명세</title><style>'+css+'</style><body>'+''.join(blocks)+'</body></html>'
(ROOT/'review/Review_v0.7.1.html').write_text(doc)
HTML(string=doc,base_url=str(ROOT/'review')).write_pdf(ROOT/'review/Review_v0.7.1.pdf')
print('review/Review_v0.7.1.pdf rebuilt')
