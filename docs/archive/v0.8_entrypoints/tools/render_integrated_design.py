from __future__ import annotations
from pathlib import Path
import re,html,textwrap,json
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfbase.cidfonts import UnicodeCIDFont
from reportlab.lib import colors
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.pagesizes import A4
from reportlab.platypus import BaseDocTemplate,PageTemplate,Frame,Paragraph,Table,TableStyle,Spacer,PageBreak
from reportlab.platypus.tableofcontents import TableOfContents
root=Path(__file__).resolve().parents[1]
source=root/'docs/design_v0.8.md'
out=root/'docs/Memory_Forge_v0.8_Integrated_Design.pdf'
font='HYSMyeongJo-Medium'
paths=[]
for directory in ['/usr/share/fonts','/usr/local/share/fonts',str(Path.home()/'.local/share/fonts'),'C:/Windows/Fonts']:
    p=Path(directory)
    if p.exists():paths.extend(p.glob('**/*.ttf'))
for p in sorted(paths,key=lambda x:(0 if 'nanumgothic' in x.name.lower() else 1,len(str(x)))):
    if any(x in p.name.lower() for x in ['nanum','notosanskr','malgun']):
        try:pdfmetrics.registerFont(TTFont('KR',str(p)));font='KR';break
        except Exception:pass
if font!='KR':pdfmetrics.registerFont(UnicodeCIDFont(font))
W,H=A4;M=45;CW=W-2*M
body=ParagraphStyle('body',fontName=font,fontSize=10.2,leading=15.5,spaceAfter=7,wordWrap='CJK',textColor=colors.HexColor('#213248'))
small=ParagraphStyle('small',parent=body,fontSize=8.2,leading=12,spaceAfter=5)
cell=ParagraphStyle('cell',parent=body,fontSize=8.8,leading=13,spaceAfter=0)
h1=ParagraphStyle('h1',parent=body,fontSize=20,leading=27,spaceBefore=12,spaceAfter=14,keepWithNext=True,textColor=colors.HexColor('#173f54'))
h2=ParagraphStyle('h2',parent=body,fontSize=15,leading=21,spaceBefore=14,spaceAfter=9,keepWithNext=True,textColor=colors.HexColor('#205469'))
h3=ParagraphStyle('h3',parent=body,fontSize=11.7,leading=18,spaceBefore=11,spaceAfter=7,keepWithNext=True)
code=ParagraphStyle('code',parent=body,fontSize=7.8,leading=11.5,spaceAfter=1,backColor=colors.HexColor('#f1f5f7'))
def clean(s):
    s=re.sub(r'<br\s*/?>','\n',s,flags=re.I);s=re.sub(r'<[^>]*>','',s)
    s=re.sub(r'\[([^\]]+)\]\(([^)]+)\)',r'\1 (\2)',s)
    return html.escape(s.replace('**','').replace('`','')).replace('\n','<br/>')
class Doc(BaseDocTemplate):
    def __init__(self):
        super().__init__(str(out),pagesize=A4,leftMargin=M,rightMargin=M,topMargin=51,bottomMargin=45,title='기억 공방 v0.8 전체 통합 설계서',author='Memory Forge',pageCompression=1)
        self.i=0;self.addPageTemplates(PageTemplate(id='main',frames=Frame(M,45,CW,H-96,leftPadding=0,rightPadding=0,topPadding=0,bottomPadding=0),onPage=self.page))
    def page(self,c,d):
        c.setStrokeColor(colors.HexColor('#b7c9cc'));c.line(M,H-32,W-M,H-32)
        c.setFillColor(colors.HexColor('#617283'));c.setFont(font,7.5)
        c.drawString(M,H-23,'MEMORY FORGE / v0.8 INTEGRATED DESIGN');c.drawRightString(W-M,H-23,'2026.10.14')
        c.drawString(M,25,'전체 설계 · 실행 참조 · 미구현 상태 구분');c.drawRightString(W-M,25,str(d.page))
    def beforeDocument(self):self.i=0
    def afterFlowable(self,f):
        if isinstance(f,Paragraph) and f.style.name in ['h1','h2']:
            self.i+=1;level=0 if f.style.name=='h1' else 1;k=f'h{self.i}'
            self.canv.bookmarkPage(k)
            try:self.canv.addOutlineEntry(f.getPlainText(),k,level=level,closed=True)
            except Exception:pass
            self.notify('TOCEntry',(level,f.getPlainText(),self.page,k))
content=source.read_text(encoding='utf-8')
flow=[Paragraph('기억 공방',h1),Paragraph('Memory Forge',h1),Spacer(1,20),Paragraph('v0.8 전체 통합 설계서',h2),Paragraph('전투 · 저장 · 성장 · 경제 · 캐릭터 · UI/UX · 운영 · AI 협업',body),Spacer(1,30),Paragraph('원본 전체 설계와 검토에서 확정한 수정·상세 목표를 해당 장에 통합했다. 이전 PDF 두 권을 번갈아 읽지 않고 이 문서 한 권에서 시스템과 다음 작업을 확인한다.',body),Paragraph('설계의 통합은 Unity 게임·서버·실결제의 완성을 뜻하지 않는다. 실행 연결 여부와 검증 범위는 첫 안내 및 상태표에서 확인한다.',body),Spacer(1,20),Paragraph('Android Google Play 우선 / PC 고려 / Claude + Codex',small),PageBreak(),Paragraph('목차',h1)]
toc=TableOfContents();toc.levelStyles=[ParagraphStyle('toc0',fontName=font,fontSize=10,leading=15,spaceBefore=6),ParagraphStyle('toc1',fontName=font,fontSize=8.5,leading=12,leftIndent=12,spaceBefore=2)]
flow.extend([toc,PageBreak()])
checks=json.loads((root/'reports/integration_v080/checks.json').read_text(encoding='utf-8'))
flow.append(Paragraph('이번 생성 과정의 실행 상태',h2))
for x in checks:flow.append(Paragraph(clean(x['name']+' — '+x['status']+' / '+('종료 코드 '+str(x.get('exit_code','미확정')) if x.get('executed') else x.get('reason','미실행'))),small))
flow.extend([Paragraph('이 표는 기록된 명령 범위의 상태다. 미연결 저장 v2, Unity·실기기·서버·실결제를 통과한 것으로 확대하지 않는다.',body),PageBreak()])
ls=content.splitlines();i=0;fence=False
while i<len(ls):
    s=ls[i].rstrip()
    if s.startswith('```'):fence=not fence;i+=1;continue
    if not s.strip():i+=1;continue
    if fence:
        for seg in textwrap.wrap(s,92,replace_whitespace=False,drop_whitespace=False) or ['']:
            flow.append(Paragraph(clean(seg).replace(' ','&#160;'),code))
        i+=1;continue
    m=re.match(r'^(#{1,6})\s+(.+)',s)
    if m:
        lv=len(m.group(1));title=m.group(2)
        if re.match(r'^\d+\.\s',title) and lv<=2:flow.append(PageBreak())
        flow.append(Paragraph(clean(title),h1 if lv==1 else h2 if lv==2 else h3));i+=1;continue
    if s.lstrip().startswith('|'):
        rows=[]
        while i<len(ls) and ls[i].lstrip().startswith('|'):
            cells=[v.strip() for v in re.split(r'(?<!\\)\|',ls[i].strip().strip('|'))]
            if not all(re.fullmatch(r'[\s:\-]+',v or ' ') for v in cells):rows.append(cells)
            i+=1
        if not rows:continue
        n=max(map(len,rows))
        if n>6 or any(max(map(len,r),default=0)>900 for r in rows):
            head=rows[0]
            for r in rows[1:]:
                for j,v in enumerate(r):
                    if v:flow.append(Paragraph(clean((head[j] if j<len(head) else str(j+1))+': '+v),cell))
                flow.append(Spacer(1,8))
        else:
            widths=[CW/n]*n
            if n==2:widths=[CW*.29,CW*.71]
            elif n==3:widths=[CW*.22,CW*.37,CW*.41]
            elif n==4:widths=[CW*.20,CW*.24,CW*.27,CW*.29]
            data=[[Paragraph(clean(v),cell) for v in r+['']*(n-len(r))] for r in rows]
            t=Table(data,colWidths=widths,repeatRows=1,hAlign='LEFT',splitByRow=1)
            t.setStyle(TableStyle([('BACKGROUND',(0,0),(-1,0),colors.HexColor('#dbe9ed')),('VALIGN',(0,0),(-1,-1),'TOP'),('LEFTPADDING',(0,0),(-1,-1),6),('RIGHTPADDING',(0,0),(-1,-1),6),('TOPPADDING',(0,0),(-1,-1),6),('BOTTOMPADDING',(0,0),(-1,-1),6),('ROWBACKGROUNDS',(0,1),(-1,-1),[colors.white,colors.HexColor('#f5f8f9')]),('LINEBELOW',(0,0),(-1,-1),.25,colors.HexColor('#cfdee1'))]))
            flow.extend([t,Spacer(1,9)])
        continue
    if s.strip() in ['---','***','___'] or s.lstrip().startswith('<!--'):i+=1;continue
    if re.match(r'^!\[',s):
        m=re.match(r'!\[([^\]]*)\]\(([^)]+)\)',s)
        if m:
            from reportlab.platypus import Image
            from PIL import Image as PILImage
            candidates=[root/m.group(2),source.parent/m.group(2)]
            p=next((p for p in candidates if p.is_file()),None)
            if p:
                try:
                    iw,ih=PILImage.open(p).size;sc=min(CW/iw,360/ih);flow.extend([Image(str(p),width=iw*sc,height=ih*sc),Paragraph(clean(m.group(1)),small)])
                except Exception:flow.append(Paragraph(clean(m.group(1)),small))
            else:flow.append(Paragraph(clean(m.group(1))+' — 원본 자산 참조',small))
        i+=1;continue
    if re.match(r'^\s*[-*]\s',s):s='• '+re.sub(r'^\s*[-*]\s','',s)
    text=s
    while i+1<len(ls) and ls[i+1].strip() and not re.match(r'^(?:#{1,6}\s|\s*\||```|!\[|\s*[-*]\s|\s*\d+\.\s)',ls[i+1]):i+=1;text+=' '+ls[i].strip()
    flow.append(Paragraph(clean(text),body));i+=1
try:Doc().multiBuild(flow)
except Exception as e:
    (root/'reports/integration_v080/pdf_layout_fallback.log').write_text(str(e),encoding='utf-8')
    minimal=[]
    for l in content.splitlines():
        if not l.strip() or l.startswith('```'):continue
        if l.startswith('#'):minimal.append(Paragraph(clean(l.lstrip('#').strip()),h2))
        else:
            for x in textwrap.wrap(l,160,break_long_words=True,break_on_hyphens=False) or [l]:minimal.append(Paragraph(clean(x),body))
    Doc().build(minimal)
print(out)
