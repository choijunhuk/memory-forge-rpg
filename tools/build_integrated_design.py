#!/usr/bin/env python3
"""Build the complete current design. Run from any working directory.
Dependencies: markdown-it-py, beautifulsoup4, weasyprint (and a local Korean font).
Font binaries are deliberately not distributed. Failure never falls back to a partial PDF.
"""
from pathlib import Path
import argparse,json,re,html,subprocess
ROOT=Path(__file__).resolve().parents[1]
VER='0.8.1'
def prompt_markdown():
    d=json.loads((ROOT/f'art/briefs/prompts_v{VER}.json').read_text())
    parts=[]
    for c in d['characters']:
        parts.append(f"### 15.6.{len(parts)+1} {c['name']} / `{c['id']}` / 현재 {c['age']}세\n\n기본 의상과 사복 대비: {c['briefKorean']}\n\n")
        block=f"공통 화풍·맥락: {c['style']}\n\n"
        # Individual payloads are executable as standalone image briefs; no placeholder labels.
        block+='\n\n'.join(f"**[{k}]**\n\n```text\n{v}\n```" for k,v in c['prompts'].items())
        parts[-1]+=block
    return '\n\n'.join(parts)
def results_markdown():
    p=ROOT/'reports/audit_v081/status.json'
    if not p.exists():raise FileNotFoundError('Run the audit and create reports/audit_v081/status.json before publishing.')
    d=json.loads(p.read_text())
    out='| 검사 | 이번 결과 | 범위·근거 |\n|---|---|---|\n'
    for x in d['checks']:
        out+=f"| {x['name']} | **{x['status']}** | {x['detail']} |\n"
    return out+'\n'+d.get('scope','')+'\n'
def assemble():
    files=sorted((ROOT/f'docs/source_v{VER}').glob('[0-9][0-9]_chapter.md'))
    if len(files)!=24:raise RuntimeError(f'Expected 24 chapter source files; found {len(files)}')
    md='\n\n'.join(p.read_text() for p in files)
    ids=re.findall(r'^# <a id="ch(\d+)"></a>',md,re.M)
    if ids!=list(map(str,range(24))):raise RuntimeError('Chapter identity/order invalid')
    if md.count('<!-- PROMPTS_V081 -->')!=1 or md.count('<!-- AUDIT_RESULTS_V081 -->')!=1:raise RuntimeError('Generated-block marker count invalid')
    md=md.replace('<!-- PROMPTS_V081 -->',prompt_markdown()).replace('<!-- AUDIT_RESULTS_V081 -->',results_markdown())
    # Explicit aliases from prior editorial JSON; executable IDs are never renamed.
    md=md.replace('id="char-eila"','id="char-aila"').replace('id="char-shion"','id="char-sion"')
    out=ROOT/f'docs/design_v{VER}.md';out.write_text(md)
    return md
CSS='''
@font-face{font-family:KoreanDoc;src:local("NanumBarunGothic");font-weight:400}
@font-face{font-family:KoreanDoc;src:local("NanumBarunGothic Bold");font-weight:700}
@page{size:A4;margin:18mm 15mm 18mm;@top-left{content:"MEMORY FORGE   /   VERIFIED INTEGRATED DESIGN";font:7pt KoreanDoc;color:#5d7480} @top-right{content:"v0.8.1 · 2026.10.14";font:7pt KoreanDoc;color:#5d7480} @bottom-left{content:"설계 · 실행 참조 · 미구현 범위 구분";font:7pt KoreanDoc;color:#647582} @bottom-right{content:counter(page) " / " counter(pages);font:7pt KoreanDoc;color:#647582}}
@page cover{margin:22mm 20mm;@top-left{content:none}@top-right{content:none}@bottom-left{content:none}}
*{box-sizing:border-box}html{font-family:KoreanDoc,"Noto Sans CJK KR",sans-serif;color:#20303d;font-size:9.25pt;line-height:1.48}body{margin:0}
p{margin:2.7mm 0;orphans:3;widows:3}a{color:#0b706b;text-decoration:none}strong{font-weight:700}
h1{font-size:22pt;line-height:1.24;margin:0 0 7mm;padding-top:2mm;border-bottom:1.1mm solid #12857e;padding-bottom:4mm;break-before:page;break-after:avoid;bookmark-level:1;color:#152d40}
h2{font-size:13pt;line-height:1.35;margin:7mm 0 3mm;break-after:avoid;bookmark-level:2;color:#086f69}
h3{font-size:11pt;margin:5mm 0 2mm;break-after:avoid;bookmark-level:3}h3.character{break-before:page;font-size:16pt;border-top:.5mm solid #b4cfca;padding-top:5mm}
h4,h5{font-size:10pt;line-height:1.35;margin:4mm 0 2mm;break-after:avoid;bookmark-level:none;color:#355665}
ul,ol{padding-left:5mm;margin:3mm 0}li{margin:1.6mm 0;orphans:2;widows:2}
blockquote{background:#eff6f5;border-left:1mm solid #16877d;margin:4mm 0;padding:2mm 4mm;color:#35515e;font-size:8.6pt;break-inside:avoid}
blockquote p{margin:1mm 0}
table{border-collapse:collapse;width:100%;table-layout:auto;margin:3.5mm 0 4.5mm;font-size:8pt;line-height:1.44}
thead{display:table-header-group}th{background:#e5eeef;color:#174651;font-weight:700;text-align:left;white-space:nowrap;word-break:normal;border-top:.45mm solid #45837f}
th,td{padding:2mm 1.8mm;vertical-align:top;border-bottom:.2mm solid #d5dfe2;overflow-wrap:anywhere;word-break:break-all}
tr{break-inside:avoid}tr:nth-child(even) td{background:#f6f8fa}table.wide{font-size:7.65pt}
table.keyvalue th:first-child,table.keyvalue td:first-child{width:23%}table.cards th:first-child,table.cards td:first-child{width:20%}table.cards th:last-child,table.cards td:last-child{width:42%}
code{font-family:"DejaVu Sans Mono",KoreanDoc,monospace;font-size:.86em;color:#244e69;overflow-wrap:anywhere}
pre{font-size:7.6pt;line-height:1.45;white-space:pre-wrap;overflow-wrap:anywhere;background:#f2f5f8;padding:3mm;border-left:.6mm solid #bbd0d8;margin:3mm 0;break-inside:auto}
pre code{font-family:"DejaVu Sans Mono",KoreanDoc,monospace;font-size:7.6pt;color:#293f4c;white-space:pre-wrap;overflow-wrap:anywhere}
pre.diagram{font-size:6.8pt;line-height:1.2}pre.diagram code{font-size:6.8pt}
hr{border:0;border-top:.2mm solid #ccd9de;margin:5mm 0}
.cover{page:cover;height:246mm;display:block}.kicker{font-size:10pt;letter-spacing:1.5pt;color:#197c78;padding-top:20mm}.cover-title{font-size:46pt;line-height:1.18;margin-top:17mm;color:#162d40;font-weight:700}.cover-en{font-size:23pt;color:#62848b;margin:5mm 0 16mm}.cover-sub{font-size:16pt;border-top:1mm solid #12857e;padding-top:7mm}.cover-note{margin-top:14mm;background:#edf4f3;padding:6mm;font-size:10pt;line-height:1.7}.cover-foot{margin-top:20mm;font-size:9pt;color:#54717d}
.toc{break-before:page;break-after:page}.toc h1{break-before:auto}.toc a{display:block;padding:1.65mm 0;border-bottom:.15mm solid #e5edef}.toc a::after{content:leader('.') target-counter(attr(href),page)}.toc .minor{font-size:8.5pt;padding-left:6mm;color:#4b6b75}
@media screen{body{max-width:1100px;padding:28px;margin:auto}.cover{height:auto}h1{margin-top:50px}}
'''
def make_html(md):
    from markdown_it import MarkdownIt
    from bs4 import BeautifulSoup
    mk=MarkdownIt('commonmark',{'html':True}).enable('table')
    soup=BeautifulSoup(mk.render(md),'html.parser')
    toc=[]
    for h in soup.find_all('h1'):
        a=h.find('a',id=True)
        if not a:raise RuntimeError('Unanchored chapter heading '+h.get_text()[:50])
        ident=a['id'];h['id']=ident;a.decompose()
        toc.append((ident,h.get_text()))
    for h in soup.find_all('h3'):
        a=h.find('a',id=True)
        if a and a['id'].startswith('char-'):
            h['id']=a['id'];a.decompose();h['class']='character'
    for t in soup.find_all('table'):
        headers=[x.get_text(' ',strip=True) for x in t.find_all('th')]
        if headers==['키','체형','머리','눈','의상','무기','모티프','표정']:
            vals=[x.decode_contents() for x in t.select('tbody tr:first-child td')]
            if len(vals)==len(headers):
                new=BeautifulSoup('<table class="keyvalue"><thead><tr><th>외형 항목</th><th>기준 내용</th></tr></thead><tbody>'+''.join('<tr><td>'+html.escape(k)+'</td><td>'+v+'</td></tr>' for k,v in zip(headers,vals))+'</tbody></table>','html.parser').table
                t.replace_with(new);continue
        t['class']=['wide' if len(headers)>=6 else 'normal']
        if len(headers)==2:t['class'].append('keyvalue')
        if 'ID' in headers and ('효과' in headers or '사용 방식' in headers):
            t['class'].append('cards')
            if len(headers)==7 and headers[:3]==['ID','이름','등급']:
                cg=BeautifulSoup('<colgroup>'+''.join(f'<col style="width:{w}%">' for w in [20,11,6,4,6,15,38])+'</colgroup>','html.parser').colgroup
                t.insert(0,cg)
    for pre in soup.find_all('pre'):
        if any(x in pre.get_text() for x in ['┌','──','┬']):pre['class']='diagram'
    cover='''<section class="cover"><div class="kicker">PROJECT BIBLE / SOURCE-VERIFIED EDITION</div><div class="cover-title">기억 공방</div><div class="cover-en">Memory Forge</div><div class="cover-sub">v0.8.1 전체 복구·검증 통합 설계서</div><div class="cover-note">원본 23개 장 · 캐릭터 10명 전체 전투 시트<br>시간 예약·개조 · 기억 저장 · 성장·경제·상품<br>캐릭터 제작 브리프 · UI/UX · 서버 · AI 협업<br><br>v0.8 편집 누락을 원본과 대조하여 복구했습니다.<br>설계 통합과 실제 게임 구현은 구분합니다.</div><div class="cover-foot">Android Google Play 우선 · PC 고려<br>최준혁 / Claude + Codex 개발 인계<br>2026년 10월 14일</div></section>'''
    nav='<section class="toc"><h1>목차</h1><p>장 제목을 누르면 해당 페이지로 이동합니다. 캐릭터별 상세는 9장, 데이터·아트의 실제 구현 상태는 0장과 19장을 확인하세요.</p>'+''.join(f'<a href="#{i}">{html.escape(t)}</a>' for i,t in toc)+'</section>'
    return '<!doctype html><html lang="ko"><head><meta charset="utf-8"><title>Memory Forge v0.8.1</title><style>'+CSS+'</style></head><body>'+cover+nav+str(soup)+'</body></html>'
def main():
    ap=argparse.ArgumentParser();ap.add_argument('--pdf',action='store_true');ap.add_argument('--output',type=Path);args=ap.parse_args()
    subprocess.run(['node',str(ROOT/'tools/audit/generate_prompts.mjs')],check=True,cwd=ROOT)
    md=assemble();markup=make_html(md)
    hp=ROOT/f'docs/Memory_Forge_v{VER}_Integrated.html';hp.write_text(markup)
    if args.pdf:
        from weasyprint import HTML
        target=args.output or ROOT/f'docs/Memory_Forge_v{VER}_Restored_Design.pdf'
        target.parent.mkdir(parents=True,exist_ok=True)
        HTML(string=markup,base_url=str(ROOT/'docs')).write_pdf(str(target))
        print('PDF:',target)
    print('Full Markdown:',len(md),'characters; 24 chapter anchors')
if __name__=='__main__':main()
