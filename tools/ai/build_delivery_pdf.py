#!/usr/bin/env python3
"""Build one PDF: current execution guide + every page of the unchanged game design.
Requires reportlab, pypdf, and a local Korean font. Never downloads fonts or calls an AI.
--rebuild-base invokes the existing full game-document generator first.
"""
from pathlib import Path
import argparse,json,subprocess,tempfile,sys
from xml.sax.saxutils import escape
ROOT=Path(__file__).resolve().parents[2]
BASE=ROOT/'docs/Memory_Forge_v0.8.1_Restored_Design.pdf'
OUTPUT=ROOT/'Memory_Forge_Complete_Design.pdf'

def font_path(bold=False):
    names=(['NanumGothicBold.ttf','NanumBarunGothicBold.ttf'] if bold else ['NanumGothic.ttf','NanumBarunGothic.ttf'])
    roots=[Path('/usr/share/fonts/truetype/nanum'),Path.home()/'Library/Fonts',Path('C:/Windows/Fonts')]
    for r in roots:
        for n in names:
            if (r/n).is_file():return r/n
    try:
        p=subprocess.run(['fc-match','-f','%{file}', 'NanumGothic:style=Bold' if bold else 'NanumGothic'],capture_output=True,text=True,check=True).stdout.strip()
        if Path(p).is_file() and p.lower().endswith('.ttf'):return Path(p)
    except (OSError,subprocess.CalledProcessError):pass
    raise RuntimeError('A local Korean TTF is required. Install NanumGothic locally; font files are not included.')

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--rebuild-base',action='store_true');a=ap.parse_args()
    if a.rebuild_base:
        subprocess.run([sys.executable,str(ROOT/'tools/build_integrated_design.py'),'--pdf'],cwd=ROOT,check=True)
    if not BASE.is_file():raise FileNotFoundError('Complete base PDF missing; never replace it with a summary.')
    from reportlab.platypus import SimpleDocTemplate,Paragraph,Spacer,PageBreak,Table,TableStyle,KeepTogether
    from reportlab.lib import colors
    from reportlab.lib.styles import ParagraphStyle
    from reportlab.pdfbase import pdfmetrics
    from reportlab.pdfbase.ttfonts import TTFont
    from reportlab.lib.pagesizes import A4
    from pypdf import PdfReader,PdfWriter
    pdfmetrics.registerFont(TTFont('KR',str(font_path())))
    pdfmetrics.registerFont(TTFont('KR-Bold',str(font_path(True))))
    pdfmetrics.registerFontFamily('KR',normal='KR',bold='KR-Bold')
    ink=colors.HexColor('#1B3345'); teal=colors.HexColor('#0F8079');muted=colors.HexColor('#5C7080')
    styles={
      'title':ParagraphStyle('title',fontName='KR-Bold',fontSize=28,leading=37,textColor=ink,spaceAfter=20,wordWrap='CJK'),
      'h':ParagraphStyle('h',fontName='KR-Bold',fontSize=14,leading=21,textColor=teal,spaceBefore=16,spaceAfter=8,wordWrap='CJK'),
      'body':ParagraphStyle('body',fontName='KR',fontSize=10,leading=17,textColor=ink,spaceAfter=10,wordWrap='CJK'),
      'small':ParagraphStyle('small',fontName='KR',fontSize=8,leading=12.5,textColor=muted,spaceAfter=6,wordWrap='CJK'),
      'cell':ParagraphStyle('cell',fontName='KR',fontSize=9,leading=14,textColor=ink,wordWrap='CJK'),
      'head':ParagraphStyle('head',fontName='KR-Bold',fontSize=9,leading=14,textColor=colors.white,wordWrap='CJK'),
    }
    flow=[]
    def p(txt,style='body'):flow.append(Paragraph(txt,styles[style]))
    def title(kicker,txt):p(kicker,'small');p(txt,'title')
    def table(rows,widths):
        data=[[Paragraph(escape(str(x)),styles['head' if i==0 else 'cell']) for x in row] for i,row in enumerate(rows)]
        t=Table(data,colWidths=widths,hAlign='LEFT',repeatRows=1)
        t.setStyle(TableStyle([('BACKGROUND',(0,0),(-1,0),ink),('ROWBACKGROUNDS',(0,1),(-1,-1),[colors.HexColor('#EDF5F3'),colors.white]),('VALIGN',(0,0),(-1,-1),'TOP'),('LEFTPADDING',(0,0),(-1,-1),10),('RIGHTPADDING',(0,0),(-1,-1),10),('TOPPADDING',(0,0),(-1,-1),8),('BOTTOMPADDING',(0,0),(-1,-1),8),('LINEBELOW',(0,0),(-1,-1),.3,colors.HexColor('#DAE5E8'))]))
        flow.extend([t,Spacer(1,8)])
    def nxt():flow.append(PageBreak())
    base=PdfReader(BASE); n=len(base.pages)
    title('MEMORY FORGE / ALL-IN-ONE / 2026.10.15','기억 공방\n전체 설계·개발 인계판'.replace('\n','<br/>'))
    p('배포판 0.8.1-aio.2<br/>게임 설계 v0.8.1 · 실행 데이터 0.7.0','small')
    p('<b>이 PDF와 코드·시제품·시작 프롬프트가 ZIP 하나에 모두 들어 있다.</b> 이전 프롬프트 ZIP을 추가로 조합할 필요가 없다.')
    p('시작은 한 번만','h')
    p('새 폴더에 ZIP을 풀고 <b>memory-forge-rpg</b>를 Codex로 연다. 첫 메시지에서 <b>START_HERE.txt를 처음부터 읽고 실제 개발을 시작해</b>라고 지시한다.')
    table([['담당 / 기준','이번 통합 기준'],['전체 진행','Codex가 공식 Claude Code CLI 호출·회수·검증'],['AI 사용 요금','둘 다 기존 구독 포함 사용량만. 별도 API·추가 크레딧·자동 충전 금지'],['그림 생성·내용 수정','사용자의 별도 변경 지시 전까지 Codex 고정'],['Claude 역할','UI/UX·서사·콘텐츠·아트 브리프·검수·리소스 적용']],[117,392])
    p('이미 개발한 저장소가 있다면 그 폴더를 삭제하거나 ZIP으로 덮어쓰지 않는다. 새 ZIP을 비교용으로 풀고 기존 커밋·미커밋 변경·생성 그림을 보존하며 병합한다.','small')
    p(f'이후 원본 게임 설계 {n}쪽 전체가 이어진다. 본문 인쇄 페이지와 기존 목차는 그대로 유지했다. 최신 실행·과금·담당 기준은 이 안내와 docs/AI_WORKFLOW.md가 우선한다.','small')
    nxt()
    title('A02 / SUBSCRIPTION ONLY','구독만 사용하는 실행 기준')
    p('로그인과 청구 경로는 별도 확인','h')
    p('Claude Code가 로그인되었다는 사실만으로 추가 과금이 차단되지는 않는다. Codex는 <b>실제 구독 인증 방식</b>과 <b>Usage credits / Extra usage 비활성 상태</b>를 먼저 확인한 뒤 추론 요청을 시작한다. API 키·클라우드·게이트웨이·프로필 인증이 섞였는지도 확인한다. [S1,S3,S4]')
    p('API 키·토큰은 값이 아닌 설정 존재와 인증 유형만 확인한다. 필요한 분리는 Claude 자식 프로세스 범위에서만 하고, 전역 설정이나 다른 프로젝트의 키를 임의 삭제하지 않는다.')
    table([['허용','허용하지 않음'],['현재 구독에 포함된 공식 CLI / 도구 사용','API·Console·별도 API 크레딧·클라우드 종량제'],['확인된 포함 한도 내 작업','Usage credits / Extra usage · 기존 구매 추가 크레딧 소모'],['한도 시 체크포인트 보존','자동 충전·한도 우회·다른 유료 AI 자동 전환']],[225,284])
    p('추가 사용 OFF와 자동 충전 OFF는 다르다','h')
    p('Claude의 Usage credits를 끄면 포함 사용량만 사용할 수 있다. 자동 충전만 끄면 이미 남은 추가 잔액이 소모될 수 있다. Codex도 구매 크레딧·포함 사용량을 구별하고 포함 한도에서 멈춘다. 확인할 수 없으면 관련 모델 호출을 보류한다. [S4,S6]')
    p('공식 CLI 경로 유지','h')
    p('구독 토큰을 추출하여 외부 SDK나 임의 API 클라이언트에 넣지 않는다. Claude의 <b>--bare는 사용하지 않는다</b>. 공식 문서상 이 모드는 구독 로그인을 사용하지 않는다. [S2]')
    p('이 패키지가 계정의 과금 설정을 실제로 변경한 것은 아니다. 모델의 자기 설명이나 프롬프트 한 줄을 청구 차단의 증거로 취급하지 않는다.','small')
    nxt()
    title('A03 / CODEX → CLAUDE CODE','Codex가 호출하고 통합한다')
    table([['순서','Codex가 수행할 작업'],['1. 비추론 검사','CLI 경로·버전·도움말·auth status·인증/과금·훅/MCP 확인'],['2. 작업 분리','기준 커밋과 Claude 전용 worktree/브랜치 확인. 같은 파일 동시 수정 금지'],['3. 짧은 시험','구독/추가 과금 확인 뒤에만 -p 읽기 시험. 실제 응답과 종료 상태 기록'],['4. 작업 배정','docs/START_CLAUDE.md + 작업 ID·허용 파일·완료 시험 전달'],['5. 결과 회수','session_id·diff·검증 로그 확인. 독립 검사 후 병합'],['6. 다음 작업','해당 작업의 명시적 session_id로만 이어가기. 재귀 호출·중복 작업 금지']],[106,403])
    p('Claude 앱을 별도로 여는 것이 기본 절차는 아니다','h')
    p('사용자는 Codex에 한 번 지시한다. Codex가 현재 환경에서 공식 Claude Code CLI를 직접 실행한다. 설치·로그인·권한·사용량 문제가 실제로 있을 때만 필요한 확인을 요청한다. 이 채팅이나 ZIP만으로 사용자 컴퓨터에서 자동 실행이 이미 시작된 것은 아니다.')
    p('권한과 작업 범위','h')
    p('기본 Claude 동시 작업자는 1명. 읽기 시험 뒤 필요한 파일·명령 권한만 부여한다. 프로젝트의 훅과 MCP가 실행될 수 있으므로 먼저 검토한다. 전체 권한 해제나 운영 접근을 자동 설정하지 않는다. [S2,S3]')
    p('기록','h')
    p('ops/ai/tasks/에 작업 ID, 기준 커밋, worktree, 담당 파일, session_id, 실제 결과, 검사와 막힌 점을 남긴다. 비밀값은 넣지 않는다. 사용량이 끝나면 저장하고 해당 작업을 보류한다.')
    nxt()
    title('A04 / IMPLEMENTATION & ART','구현과 그림의 연결 기준')
    p('실제 첫 작업','h')
    p('<b>Codex:</b> 입력 체크섬과 기존 검사 → 구독 확인 후 Claude 시험 → 저장 v2 실제 Run/HTML 연결 → 후반 적 행동 → 같은 조건 밸런스 시험 → C# Battle/Run → 서버.')
    p('<b>Claude:</b> 전체 10명 시트와 브리프 → 저장 전후비교·취소·실패 UI → Codex용 그림 요청 → 후반 서사/콘텐츠 → Unity UI·리소스 적용. 별도 worktree에서 병렬 작업하되 공유 파일·씬 쓰기는 겹치지 않는다.')
    p('Codex 그림 담당은 자동 변경하지 않는다','h')
    p('캐릭터·배경·카드·적·스킨·UI 그림 생성, 재생성, 인페인팅은 Codex가 담당한다. 현재 Codex 구독 기능에 없는 경우 해당 자산만 보류한다. 별도 API를 요구하는 스킬을 구독 기능으로 착각하여 실행하지 않는다.')
    p('Claude는 실제 참조 파일, 보존/변경할 요소, 구도·표정·의상, 해상도·안전 영역, 검수 기준을 요청서에 적는다. 생성 후에는 비생성형 리사이즈·압축·슬라이스·임포트가 가능하다. 평면 PNG를 레이어 PSD나 완성된 리그라고 보고하지 않는다.')
    p('자율 진행과 사람 결정','h')
    p('일반 구현·버그 수정·UI/의상 세부는 기존 범위 안에서 자율 진행한다. 가격·확률·재화 총량·나이·영구 저장 의미의 새 변경, 외부 유료 서비스, 실결제·운영 배포는 별도 결정 범위다. 단계를 완료하려고 기능·문서를 삭제하거나 검사를 약화하지 않는다.')
    p('GitHub는 비공개. 기존 저장소를 먼저 확인하고 원격이 없을 때만 초기 구성을 시도한다. 이번 패키지 제작에서 실제 원격 저장소를 생성하거나 운영에 배포하지 않았다.','small')
    nxt()
    title('A05 / PRESERVATION & EVIDENCE','보존 범위와 확인한 것')
    p(f'<b>원본 게임 PDF {n}쪽, 전체 마크다운, 24개 원본 파일(안내 + 본문 23장), 게임 데이터·JS/C#·서버 명세·아트 프롬프트를 유지했다.</b> 협업 실행 문서와 빌드/검사 진입점만 통합했으며 수정 전 지침도 이력으로 보존했다.')
    p('현재 게임은 개발 인계 단계','h')
    p('JS·HTML 시제품이 있고 저장 v2는 독립 모듈이다. Run/HTML/서버 연결, C# Battle/Run 전체, Unity, 후반 완성 콘텐츠, 최종 그림·음성·실결제는 아직 남아 있다. 이번 통합을 구현 완료로 해석하지 않는다.')
    p('최신 출고 검사는 ZIP의 <b>reports/all_in_one/verification.json</b>과 <b>preservation.json</b>에서 확인한다. 사용자 구독 인증·실제 Claude 호출·계정 추가 과금 OFF는 이번 환경에서 검사하지 않았다. 파일/테스트 검증과 서비스 실연결을 구별한다.')
    p('검사와 문서 경로','h')
    p('START_HERE.txt: 첫 메시지<br/>docs/AI_WORKFLOW.md: 현재 실행 계약<br/>docs/design_v0.8.1.md: 전체 게임 명세<br/>MANIFEST_AIO.json: 현재 입력 무결성<br/>npm run docs: 전체 PDF 재생성','small')
    p('공식 동작 확인 자료 / 2026-10-15','h')
    src=json.loads((ROOT/'docs/AI_SOURCES.json').read_text())['sources']
    for s in src:
        p(f"[{s['id']}] <link href='{escape(s['url'])}' color='#08766D'>{escape(s['title'])}</link>", 'small')
    p('자료는 실행·인증 동작의 근거이며, 사용자의 계정 설정이나 게임 수치가 검증되었다는 뜻이 아니다. 다음 페이지부터 기존 전체 게임 설계가 시작된다.','small')
    def footer(c,doc):
        c.saveState();c.setStrokeColor(colors.HexColor('#CFDDE0'));c.line(43,44,552,44)
        c.setFont('KR',7.5);c.setFillColor(muted);c.drawString(43,31,'MEMORY FORGE  /  ALL-IN-ONE  /  0.8.1-aio.2');c.drawRightString(552,31,f'A{doc.page}');c.restoreState()
    with tempfile.TemporaryDirectory() as td:
        front=Path(td)/'front.pdf'
        doc=SimpleDocTemplate(str(front),pagesize=A4,leftMargin=43,rightMargin=43,topMargin=41,bottomMargin=59,title='Memory Forge ALL-IN-ONE 실행 안내',author='Memory Forge')
        doc.build(flow,onFirstPage=footer,onLaterPages=footer)
        head=PdfReader(front)
        if len(head.pages)!=5:raise RuntimeError(f'Frontmatter unexpectedly spans {len(head.pages)} pages (expected 5). Inspect layout.')
        w=PdfWriter();w.append(head,outline_item='최신 실행 안내 · 구독 전용 · Codex가 Claude 호출')
        w.append(base,outline_item=f'게임 설계 v0.8.1 · 원본 {n}쪽 전체',import_outline=True)
        w.add_metadata({'/Title':'Memory Forge 전체 설계·개발 인계판 / ALL-IN-ONE','/Author':'Memory Forge','/Subject':'Subscription-only Codex-led Claude CLI workflow; complete unchanged game design'})
        with OUTPUT.open('wb') as f:w.write(f)
    print(json.dumps({'output':str(OUTPUT),'front_pages':5,'original_pages':n,'total_pages':n+5,'user_cli_executed':False},ensure_ascii=False))

if __name__=='__main__':main()
