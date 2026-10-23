"""Compose design proof images from unmodified PNGs and layout.json; not game code."""
import json, math, html
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageOps
R=Path(__file__).resolve().parent
L=json.loads((R/'layout.json').read_text()); C=L['tokens']
FONT='/Users/choi/Documents/Memory_forge/client/MemoryForgeUnity/Assets/Resources/Fonts/IBMPlexSansKR-Regular.ttf'
DATA=json.loads(Path('/Users/choi/Documents/Memory_forge/data/game_data.json').read_text())
EN={x['id']:x for x in DATA['enemies'] if x['zone']==1}
OUT=R/'previews';OUT.mkdir(exist_ok=True)
D=None; IM=None; OVER=[]

def font(s):return ImageFont.truetype(FONT,int(s))
def color(c):return C.get(c,c)
def box(r,fill='panel',edge='line',alpha=1,radius=8,border=1):
 x,y,w,h=map(round,r);layer=Image.new('RGBA',(1600,900));d=ImageDraw.Draw(layer)
 rgb=tuple(bytes.fromhex(color(fill).lstrip('#')))+(int(alpha*255),)
 d.rounded_rectangle((x,y,x+w-1,y+h-1),radius,fill=rgb,outline=color(edge) if edge else None,width=border)
 IM.alpha_composite(layer)

def text(t,r,s=22,c='ink',center=False,maxlines=None):
 x,y,w,h=r; f=font(s);lines=[]
 for part in str(t).split('\n'):
  line=''
  for ch in part:
   if line and D.textlength(line+ch,font=f)>w:lines.append(line);line=ch
   else:line+=ch
  lines.append(line)
 lh=math.ceil(s*1.25)
 if len(lines)*lh>h+5:OVER.append({'text':t,'rect':r,'font':s,'lines':len(lines),'required':len(lines)*lh})
 for j,line in enumerate(lines):
  xx=x+(w-D.textlength(line,font=f))/2 if center else x
  D.text((round(xx),round(y+j*lh)),line,font=f,fill=color(c),stroke_width=0)

def asset(p,r,fit='contain',alpha=1,uv=None):
 p=R/p
 if not p.exists():return
 a=Image.open(p).convert('RGBA');x,y,w,h=map(round,r)
 if uv:
  u,v,uw,vh=uv;sw,sh=a.size;a=a.crop((round(u*sw),round(v*sh),round((u+uw)*sw),round((v+vh)*sh)))
 if fit=='cover':a=ImageOps.fit(a,(w,h),method=Image.Resampling.LANCZOS)
 else:a.thumbnail((max(1,w),max(1,h)),Image.Resampling.LANCZOS);x+=(w-a.width)//2;y+=(h-a.height)//2
 if alpha!=1:a.putalpha(a.getchannel('A').point(lambda v:round(v*alpha)))
 IM.alpha_composite(a,(x,y))

def button(t,r,primary=False,selected=False,s=22,disabled=False):
 box(r,'brass' if primary and not disabled else 'panel','memory' if selected else 'brass' if primary else 'line',border=3 if selected else 1)
 x,y,w,h=r;lines=str(t).count('\n')+1
 text(t,[x+8,y+(h-s*1.25*lines)/2,w-16,h],s,'background' if primary and not disabled else 'muted' if disabled else 'ink',True)

def bar(r,n=1,c='memory'):
 box(r,'line',None,radius=2)
 if n>0:box([r[0],r[1],r[2]*n,r[3]],c,None,radius=2)

def start(kind,title,context='1구역 · 멈춘 시계탑 거리'):
 global IM,D,OVER
 OVER=[];IM=Image.new('RGBA',(1600,900),color('background'));D=ImageDraw.Draw(IM)
 bg=L['screens'][kind]['background'];asset('assets/backgrounds/'+bg+'.png',[0,0,1600,900],'cover')
 scrim=Image.new('RGBA',(1600,900),(13,19,34,round(255*L['screens'][kind]['scrim_alpha'])));IM.alpha_composite(scrim)
 box([32,16,1536,60],'background',None,.96)
 text('기억 공방',[48,24,244,40],30,'brass');text(title+'\n'+context,[310,20,600,54],20)
 button('덱 18장',[930,20,240,52]);button('기록',[1186,20,120,52]);button('탐사 포기',[1322,20,246,52])

def status(battle=False):
 box([32,88,1536,82]);text('파티 체력 243 / 243',[52,96,390,36],28);bar([52,144,390,10])
 if battle:
  text('1턴 · 방어 0 · 반격 0 · 부채 0 · 잔향 0/3',[466,94,722,32],22)
  text('뽑을 13 / 버림 0 / 소멸 0 · 동전 60',[466,130,722,30],20,'muted');text('행동력 4',[1270,100,278,52],38,'brass')
 else:
  text('동전 60 · 유물 0개 · 부품 0개',[466,94,550,32],22,'brass');text('보유 유물 없음',[466,130,550,32],20,'muted')
  text('연습용 탐사 · 진행과 기억은 이 세션에만 있어요.\n앱을 닫으면 사라져요.',[1040,96,508,62],20,'muted')

def party():
 box([32,182,342,316])
 for i,(cid,name,attr) in enumerate([('sera','세라','전격'),('harin','하린','빙결'),('nia','니아','공허')]):
  y=182+i*104;asset(f'references/char_{cid}_sd_default_v02.png',[40,y+8,80,88])
  text(name,[128,y+8,104,32],25);bar([128,y+48,104,10],0,'brass');text(attr+' 0/100',[128,y+66,104,30],20,'muted')
  button('충전 중',[246,y+16,116,64],s=20,disabled=True)

def intent(v):
 t=v['t'];labels={'atk':'공격','def':'방어','buff':'힘','debuff':'약화','insert':'저주 카드','reflect':'반사','charge':'준비','summon':'소환','expose':'취약','atkdebuff':'공격·약화','heal':'회복'}
 if t=='expose':return '취약 2 후 공격 52'
 n=v.get('d',v.get('b',v.get('n',v.get('str',''))));st=f'{labels.get(t,t)} {n}'
 if v.get('h',1)>1:st+=f" ×{v['h']}"
 if t=='insert':st+='장 추가'
 return st

def enemy(eid,i,n,dead=False):
 e=EN[eid];w=(1178-16*(n-1))/n;x=390+i*(w+16);y=182
 if n<=3:
  aw=min(450,int(w*.40));box([x,y,w,316],'panel',None,.22)
  asset(f'assets/enemies/enemy_{eid}_default_v01.png',[x+12,y+10,aw-16,292],alpha=.25 if dead else 1)
  dx=x+aw+16;dy=y+8;dw=w-aw-32;dh=300
 else:
  asset(f'assets/enemies/enemy_{eid}_default_v01.png',[x+(w-64)/2,y,64,64],alpha=.25 if dead else 1)
  dx=x;dy=y+64;dw=w;dh=252
 box([dx,dy,dw,dh],edge='memory' if i==0 else 'line',border=3 if i==0 else 1)
 tc='muted' if dead else 'ink'
 text(e['name']+(' · 처치' if dead else ''),[dx+12,dy+4,dw-24,28],22,tc)
 text(f"체력 {0 if dead else e['hp']} / {e['hp']}",[dx+12,dy+34,dw-24,24],20,tc);bar([dx+12,dy+60,dw-24,8],0 if dead else 1,'danger')
 text(f"균열 {e['brk']}/{e['brk']}",[dx+12,dy+72,dw-24,24],20,'memory')
 attrs={'flame':'화염','frost':'빙결','volt':'전격','void':'공허'}
 weak='약점 '+ '·'.join(attrs[a] for a in e['weak']);delta=26 if D.textlength(weak,font=font(20))>dw-24 else 0
 text(weak,[dx+12,dy+100,dw-24,24+delta],20)
 text('선택' if i==0 else '처치' if dead else '상태 없음',[dx+12,dy+128+delta,dw-24,28],20,'muted')
 text('이번 '+intent(e['pattern'][0]),[dx+12,dy+160+delta,dw-24,44],20,'brass')
 text('다음 '+intent(e['pattern'][1%len(e['pattern'])]),[dx+12,dy+204+delta,dw-24,44],20)

def card(name,owner,cost,r,selected=False):
 x,y,w,h=r;box(r,'panel','memory' if selected else 'line',border=3 if selected else 1)
 if owner:asset(f'references/char_{owner}_cutin_default_v01.png',[x+2,y+2,w-4,48],'cover',.65,[0,.05,1,.40])
 box([x+8,y+5,72,32],'background',None)
 text('AP '+str(cost),[x+12,y+7,65,28],21,'brass')
 text(name,[x+10,y+54,w-20,48],23)
 text('즉시 · 예약' if cost else '즉시 사용',[x+10,y+106,w-20,26],20,'memory')

def finish(name):
 text('DESIGN PREVIEW · 정적 구성 / Unity 실행 아님',[40,881,1500,18],12,'muted')
 IM.convert('RGB').save(OUT/(name+'.png'))
 (R/'qa'/('text-fit-'+name+'.json')).write_text(json.dumps(OVER,ensure_ascii=False,indent=2))
 return name

def battle(name,ids):
 start('battle','탐사 연습 · 전투');status(True);party()
 for i,eid in enumerate(ids):enemy(eid,i,len(ids))
 box([32,506,1536,60]);text('기억 시간표',[44,516,160,42],23,'memory')
 for i,t in enumerate(['다음 빈 칸','다음 빈 칸','다음 -','다다음 빈 칸','다다음 -']):
  x=218+i*264;box([x,512,250,48]);text(t,[x+8,524,234,30],20,'muted',True)
 box([32,574,1536,48]);text('행동 결과',[44,580,176,36],20,'memory');text('확정된 카드의 결과가 여기에 표시돼요.',[228,580,712,34],20,'muted')
 for t,r in [('보통',[952,580,112,36]),('2배속',[1072,580,112,36]),('동작 줄이기',[1192,580,180,36]),('건너뛰기',[1380,580,172,36])]:button(t,r,s=20)
 box([32,630,1536,96]);text('카드를 고르고 방식을 정한 뒤 사용을 확정하세요. 고르는 동안에는 아무것도 쓰지 않아요.',[50,634,1498,26],21,'muted')
 text('엄폐 사격 → 적 선택',[50,668,308,48],23);button('카드 설명',[370,664,136,56],s=20)
 for t,r in [('즉시 · 비용 1\n대상을 먼저 고르세요',[522,664,248,56]),('다음 턴 · 비용 1\n대상을 먼저 고르세요',[782,664,248,56]),('다다음 턴 · 비용 1\n대상을 먼저 고르세요',[1042,664,248,56])]:button(t,r,s=20)
 button('방식 선택',[1310,664,240,56],disabled=True)
 box([32,734,1308,142],'background',None)
 cards=[('경계 방패','harin',1),('유리탄','sera',1),('미세 조정','nia',0),('스패너 타격','nia',1),('엄폐 사격','sera',1),('기본 공격',None,1)]
 for i,(nm,o,c) in enumerate(cards):card(nm,o,c,[32+i*194,738,180,138],i==4)
 button('턴 종료\n1턴',[1360,734,208,142],primary=True,s=30)
 return finish(name)

def node_icon(kind,r,c='ink'):
 x,y,w,h=r;co=color(c);cx=x+w/2;cy=y+h/2
 if kind in ['전투','엘리트']:
  D.line((x+8,y+8,x+w-8,y+h-8),fill=co,width=3);D.line((x+w-8,y+8,x+8,y+h-8),fill=co,width=3)
  if kind=='엘리트':D.line((x+6,y+12,x+2,y+2,cx,y+7,x+w-2,y+2,x+w-6,y+12),fill=co,width=3)
 elif kind=='보스':
  D.arc((x+8,y+4,x+w-8,y+h-6),180,360,fill=co,width=3);D.line((x+8,cy,x+5,y+h-7,x+w-5,y+h-7,x+w-8,cy),fill=co,width=3);D.ellipse((cx-3,y+h-5,cx+3,y+h),fill=co)
 elif kind=='휴식':
  D.rectangle((x+6,y+8,x+w-9,y+h-8),outline=co,width=3);D.arc((x+w-14,y+10,x+w-2,y+h-10),-90,90,fill=co,width=3)
 elif kind=='공방':
  D.ellipse((x+7,y+7,x+w-7,y+h-7),outline=co,width=3);D.ellipse((cx-5,cy-5,cx+5,cy+5),outline=co,width=3)
  for a in range(0,360,45):
   t=math.radians(a);D.line((cx+14*math.cos(t),cy+14*math.sin(t),cx+20*math.cos(t),cy+20*math.sin(t)),fill=co,width=4)
 elif kind=='상점':D.ellipse((x+4,y+4,x+w-4,y+h-4),outline=co,width=3);D.line((cx,y+10,cx,y+h-10),fill=co,width=3)
 elif kind=='기록소':D.rectangle((x+3,y+6,x+w-3,y+h-5),outline=co,width=3);D.line((cx,y+6,cx,y+h-5),fill=co,width=3)
 else:text('?',r,30,c,True)

def mapview():
 start('map','탐사 연습 · 지도');status();box([32,182,1536,630],'background',None,.68)
 types=[['전투']*3,['전투']*3,['전투']*3,['휴식','전투','전투'],['전투','엘리트'],['전투','공방'],['휴식','공방'],['보스']]
 cs=[]
 for f,ls in enumerate(types):
  text('보스' if f==7 else f'{f+1}층',[32+f*192,196,192,32],22,'muted',True)
  cs.append([(32+(f+.5)*192,258+(i+.5)*520/len(ls)) for i in range(len(ls))])
 for f in range(7):
  for i,a in enumerate(cs[f]):
   b=cs[f+1][min(i,len(cs[f+1])-1)];D.line((a[0]+76,a[1],b[0]-76,b[1]),fill=color('line'),width=2)
 for f,ls in enumerate(types):
  for i,k in enumerate(ls):
   cx,cy=cs[f][i];x=cx-76;y=cy-56;box([x,y,152,112],edge='brass' if f==0 else 'line',border=3 if f==0 else 1)
   node_icon(k,[x+56,y+8,40,40],'brass' if f==0 else 'muted');text(k,[x+8,y+52,136,24],20,'ink',True)
   text('갈 수 있음' if f==0 else '미방문',[x+8,y+80,136,24],20,'brass' if f==0 else 'muted',True)
 text('전투 · 엘리트 · 보스 · 휴식 · 공방 · 상점 · 사건 · 기록소',[48,826,1200,52],20,'muted')
 text('칸을 누르면 바로 들어가요.',[1250,826,302,52],20,'brass');return finish('map')

def service(kind,title,intro,options):
 start(kind,title);status();asset('assets/backgrounds/bg_zone1_event_workshop_v02.png',[32,182,410,630],'cover',uv=[0,0,.48,1])
 box([462,182,1106,630]);text(title,[486,198,1058,48],32,'brass');text(intro,[486,254,1058,108],22)
 for i,t in enumerate(options):button(t,[486,378+i*108,1058,96],s=22)
 text('선택 가능 여부와 비용은 현재 탐사 상태에 따라 표시',[486,738,780,60],20,'muted');button('떠나기',[1288,740,256,56],primary=True)
 text('정적 디자인 예시 · 표시 문구/선택지는 기존 코드에 연결',[32,826,1536,52],20,'muted');return finish(kind)

def reward():
 start('reward','전투 보상');status();box([32,182,1536,630],'background',None,.80)
 box([52,198,1496,64]);text('전투 보상 · 동전 +26',[72,208,1456,42],30,'brass')
 text('카드 1장을 덱에 추가 · 카드를 누르면 바로 덱에 들어가요. 먼저 설명을 볼 수 있어요.',[52,278,1496,36],22)
 for i,(nm,o,c) in enumerate([('유리탄','sera',1),('경계 방패','harin',1),('스패너 타격','nia',1)]):
  x=52+380*i;box([x,326,360,260]);asset(f'references/char_{o}_cutin_default_v01.png',[x+2,328,356,74],'cover',.65)
  text(nm,[x+16,410,328,40],27);text('비용 1 · 즉시 / 예약',[x+16,454,328,32],21,'brass')
  defs={z['name']:z for z in DATA['cards']};fx=defs[nm]['eff'][0];op='피해' if fx['op']=='dmg' else '방어';effect=f"기본 효과: {op} {fx['v']}\n예약 {op} {fx['rv']}"
  text(effect,[x+16,492,328,84],22);button('카드 설명 보기',[x,598,360,52],s=20)
 button('받지 않기\n동전 +15',[1192,326,356,80],s=22)
 box([52,682,1496,112]);text('부품 발견: 연소 — 피해를 준 적에게 화상 2.',[72,706,1200,64],23);button('가져가기',[1308,710,220,56],s=22)
 button('지도로',[1288,826,280,52],primary=True);return finish('reward')

def memory(result=False):
 start('result' if result else 'memory_save','탐사 완주' if result else '기억 저장')
 if result:
  box([32,88,1536,788],alpha=.96);text('탐사 완주',[56,104,1488,56],40,'brass')
  text('전투 6회 (엘리트 1) · 처치 11 · 남은 체력 112/243 · 유물 행운의 동전, 무한 회로',[56,172,1488,40],22)
  text('기존 덱 유지 · 세라 용량 0, 하린 용량 0, 니아 용량 0',[56,218,1488,40],22,'memory')
 else:
  box([32,88,1536,136]);text('탐사 종료 · 이번 탐사의 변화를 골라 기억에 더하세요',[52,96,1244,38],28,'brass')
  text('출발 전 기억 덱에 이번 변화를 더해요. 용량은 완성된 결과 덱 전체로 계산해요.',[52,138,1496,48],20)
  text('선택 0개 · 연습용 기억은 이 세션에만 있고 앱을 닫으면 사라져요.',[52,192,1496,26],20,'muted')
 for i,(cid,nm) in enumerate([('sera','세라'),('harin','하린'),('nia','니아')]):
  x=56+i*500 if result else 32+i*518;y=272 if result else 236;w=480 if result else 500
  box([x,y,w,360 if result else 572],'background' if result else 'panel');asset(f'references/char_{cid}_sd_default_v02.png',[x+16,y+10,64,70])
  text(nm,[x+96,y+10,w-116,32],28)
  text('저장된 기억 덱 5장' if result else '출발 전 0/6 · 결과 용량 0/6',[x+96,y+48,w-116,32],20,'memory')
  names={'sera':'유리탄, 엄폐 사격, 정조준, 시한 포격, 기본 방어','harin':'경계 방패, 방패 강타, 반격 태세, 도발, 기본 공격','nia':'스패너 타격, 응급 차폐막, 미세 조정, 구조 분석, 기본 공격'}
  if result:text(names[cid],[x+20,y+108,w-40,232],22,'muted')
  else:
   text('출발 전에 있던 기억 5장 — 유지',[x+20,y+90,w-40,28],21,'memory');text(names[cid],[x+20,y+124,w-40,72],20,'muted')
   text('이번 탐사의 변화 · 선택 목록',[x+20,y+204,w-40,28],21,'memory')
   box([x+20,y+240,w-40,160],'background',None);text('이 영역은 실제 변화가 있을 때\n선택 가능한 행으로 채워져요.\n변화가 없으면 기존 덱을 유지해요.',[x+32,y+252,w-64,132],20,'muted')
   text('결과 덱 미리보기 5장',[x+20,y+408,w-40,28],21,'memory');text(names[cid],[x+20,y+444,w-40,72],20)
 if result:
  text('이 기억은 연습용으로 이 세션에만 있어요. 앱을 닫으면 사라져요. 다음 탐사는 위 기억 덱으로 시작해요.',[56,650,1488,64],22,'muted')
  button('짧은 탐사 (1구역)',[56,738,300,64],selected=True);button('긴 탐사 (2구역)',[372,738,300,64]);button('다음 탐사 출발',[1244,738,300,64],primary=True)
 else:
  text('고른 변화는 확정하기 전까지 아무것도 바꾸지 않아요.',[32,816,850,62],20,'muted')
  button('변화 없이 저장 (기존 덱 유지)',[898,816,390,62],primary=True,s=21);button('저장하지 않고 닫기',[1304,816,264,62],s=21)
 return finish('result' if result else 'memory-save')

names=[battle('battle-two',['e_cog','e_page']),battle('battle-five',['e_moth','e_cog','e_smudge','e_hound','e_lantern']),
       battle('battle-boss-bell',['b_bell']),battle('battle-boss-notary',['b_notary']),battle('battle-boss-garden',['b_garden','e_seed','e_seed']),mapview(),reward(),
       service('rest','휴식','잠깐 쉬어 간다 · 남은 이용 횟수는 현재 상태 표시',['회복 (최대 체력의 30%)','카드 강화 (+)']),
       service('forge','공방','카드를 다듬는다 · 회로·각인 각 1칸 · 남은 이용 횟수 표시',['카드 변주','카드 제거']),
       service('shop','상점 · 떠돌이 기억 상인','모든 실제 카드·부품·유물과 가격을 스크롤로 표시',['카드 · 설명 보기 / 실제 가격으로 구매','부품 · 슬롯과 전체 효과 / 구매','유물 · 전체 효과 / 구매']),
       service('event','사건','실제 사건 제목과 본문 전체를 이 영역에서 표시',['실제 선택지 · 필요한 동전과 효과','선택 후 결과 전문 표시']),
       service('archive','기록소','귀환과 계속 탐사의 실제 회복·패배·저장 조건을 유지',['계속 탐사','귀환하고 저장']),memory(),memory(True)]
# Correct aspect fit, not stretched, for the requested physical pixel canvas.
phone=Image.new('RGB',(2340,1080),color('background'));phone.paste(Image.open(OUT/'battle-five.png').resize((1920,1080),Image.Resampling.LANCZOS),(210,0));phone.save(OUT/'battle-five-phone-2340x1080.png')
thumbs=Image.new('RGB',(1200,math.ceil(len(names)/3)*246),color('background'));dd=ImageDraw.Draw(thumbs)
for i,n in enumerate(names):
 a=Image.open(OUT/(n+'.png'));a.thumbnail((392,221));thumbs.paste(a,((i%3)*400,(i//3)*246));dd.text(((i%3)*400+8,(i//3)*246+223),n,font=font(16),fill=color('ink'))
thumbs.save(OUT/'overview.jpg')
page='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Memory Forge — zone1 visual handoff</title><style>body{background:#0d1322;color:#e9eef8;font:18px system-ui;margin:24px}a{color:#62d6c6}nav{display:flex;gap:16px;flex-wrap:wrap;position:sticky;top:0;background:#0d1322;padding:16px 0}figure{margin:32px 0}img{max-width:100%;height:auto;border:1px solid #2f3d63}figcaption{margin:8px 0;color:#98a5c0}h1{color:#e2b45f}</style><h1>Memory Forge · Midnight, held in brass</h1><p>정적 디자인 미리보기 / Static design compositions. Not Unity captures or gameplay acceptance.</p><p><a href="design-spec.md">Design specification</a> · <a href="layout.json">Exact layout</a> · <a href="manifest.json">Manifest</a> · <a href="qa/self-review.json">Self review</a></p><nav>'''
page+=''.join(f'<a href="#{n}">{html.escape(n)}</a>' for n in names)+'</nav>'
page+=''.join(f'<figure id="{n}"><figcaption>{html.escape(n)} · 1600×900 · illustrative data / source-linked layout</figcaption><img src="previews/{n}.png" alt="{html.escape(n)} design composition" loading="lazy"></figure>' for n in names)
page+='<figure><figcaption>2340×1080 aspect-fit pixel check only; not a physical dp/sp check</figcaption><img src="previews/battle-five-phone-2340x1080.png" alt="Five enemy composition in phone landscape"></figure></html>'
(R/'preview.html').write_text(page)
print('Rendered',len(names),'screens and phone aspect-fit proof. Text-fit logs in qa/.')
