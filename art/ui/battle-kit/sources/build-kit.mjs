// Art production only. No game rules, runtime imports, or network calls.
import fs from 'node:fs/promises';
import path from 'node:path';
import crypto from 'node:crypto';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const sharp = require(process.env.BATTLE_KIT_SHARP || '/Users/choi/.npm/_npx/3d55143a3497ce4a/node_modules/sharp');
const root = path.resolve(import.meta.dirname, '..');
process.chdir(root);
for (const dir of ['assets', 'sources', 'qa']) await fs.mkdir(dir, { recursive: true });
const assets = [];
const C = { navy:'#0d1322', panel:'#141c31', brass:'#e2b45f', teal:'#62d6c6', danger:'#ff6f6f', ink:'#e9eef8', muted:'#7e8ca8' };
const defs = `<defs>
 <linearGradient id="gold" x2=".35" y2="1"><stop stop-color="#fff0b7"/><stop offset=".28" stop-color="#e2b45f"/><stop offset=".62" stop-color="#82542c"/><stop offset="1" stop-color="#dfb46e"/></linearGradient>
 <linearGradient id="ink" x2="0" y2="1"><stop stop-color="#26324d"/><stop offset="1" stop-color="#0d1322"/></linearGradient>
 <linearGradient id="glass" x2=".7" y2="1"><stop stop-color="#b8fff1"/><stop offset=".3" stop-color="#62d6c6"/><stop offset="1" stop-color="#166f7e"/></linearGradient>
 <linearGradient id="scarlet" x2=".8" y2="1"><stop stop-color="#fff0dc"/><stop offset=".28" stop-color="#ffad97"/><stop offset="1" stop-color="#ff6f6f"/></linearGradient>
 <linearGradient id="veil"><stop stop-color="#141c31" stop-opacity="0"/><stop offset=".16" stop-color="#141c31" stop-opacity=".97"/><stop offset=".84" stop-color="#141c31" stop-opacity=".97"/><stop offset="1" stop-color="#141c31" stop-opacity="0"/></linearGradient>
 </defs>`;
const svg = (w,h,body) => `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}">${defs}${body}</svg>`;
const p=(d,fill='none',stroke=C.ink,sw=5,extra='')=>`<path d="${d}" fill="${fill}" stroke="${stroke}" stroke-width="${sw}" stroke-linejoin="round" stroke-linecap="round" ${extra}/>`;
const diamond=(x,y,r,fill=C.brass)=>p(`M${x} ${y-r}l${r} ${r}-${r} ${r}-${r}-${r}Z`,fill,'none',0);
const line=(x1,y1,x2,y2,color=C.brass,width=2)=>`<path d="M${x1} ${y1}H${x2}" fill="none" stroke="${color}" stroke-width="${width}"/>`.replace(`H${x2}`,`L${x2} ${y2}`);
const circle=(x,y,r,fill='none',stroke=C.brass,sw=2)=>`<circle cx="${x}" cy="${y}" r="${r}" fill="${fill}" stroke="${stroke}" stroke-width="${sw}"/>`;
const rect=(x,y,w,h,r,fill,stroke='none',sw=1)=>`<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${r}" fill="${fill}" stroke="${stroke}" stroke-width="${sw}"/>`;
const group=(body,transform)=>`<g transform="${transform}">${body}</g>`;
const shield=p('M64 19L98 31V57Q98 86 64 108Q30 86 30 57V31Z','url(#ink)',C.ink,7)+p('M64 32L84 40V58Q84 77 64 92','none',C.teal,5);
const blade=p('M44 83L37 76L79 27L101 19L93 42L51 90Z','url(#scarlet)',C.navy,5)+p('M32 80L49 97M39 91L27 104','none',C.brass,7);
const cross=p('M55 25H73V52H100V70H73V98H55V70H28V52H55Z','url(#glass)',C.navy,5);
const eye=p('M17 64Q64 17 111 64Q64 111 17 64Z','none',C.ink,7)+circle(64,64,17,'url(#glass)',C.navy,5);
const clock=circle(64,64,36,'url(#ink)',C.ink,6)+p('M64 37V64L83 74','none',C.teal,7);
const glyphs = {
 defend:shield,
 strength:p('M30 66L64 29L98 66M30 94L64 57L98 94','none',C.brass,11),
 debuff:p('M31 29L64 63L97 29M31 58L64 92L97 58','none',C.danger,11),
 attack_debuff:group(blade,'translate(-8 -5) scale(.85)')+p('M72 80L89 97L106 80','none',C.teal,9),
 charge:p('M34 22H94M34 106H94M41 26Q41 49 63 64Q87 78 87 102M87 26Q87 49 65 64Q41 78 41 102','none',C.brass,7)+p('M48 93L64 77L80 93Z',C.teal,'none',0),
 summon:p('M27 104V51Q27 15 64 15Q101 15 101 51V104','none',C.brass,8)+diamond(64,63,17,C.teal)+diamond(36,94,10,C.ink)+diamond(92,94,10,C.ink),
 heal:cross,
 curse:p('M32 23L94 29L88 108L27 100Z','url(#ink)',C.ink,6)+p('M63 35L49 60L69 65L52 94','none',C.danger,9),
 reflect:p('M64 15L102 64L64 113L26 64Z','url(#ink)',C.ink,6)+p('M51 73L75 44M42 89L58 70','none',C.teal,7),
 expose:p('M64 20L99 32V56L79 66L90 76Q80 95 64 108Q30 85 30 56V32Z','url(#ink)',C.ink,6)+p('M69 24L55 53L73 63L54 94','none',C.danger,7),
 defend_all:group(shield,'translate(-1 4) scale(.72)')+group(shield,'translate(40 4) scale(.72)'),
 unknown:eye+p('M27 100L100 27','none',C.brass,13),
 counter:p('M28 55Q28 21 64 21Q108 21 108 64Q108 101 78 106','none',C.brass,8)+p('M20 40L28 57L47 48','none',C.brass,8)+group(blade,'translate(4 17) scale(.77)'),
 debt:group(clock,'translate(-8 -8) scale(.88)')+p('M70 88L82 100Q91 108 99 100Q108 92 99 83L88 73M60 78L72 90Q81 98 89 90','none',C.brass,7),
 echo:p('M19 55Q40 31 61 55Q82 79 109 55M19 76Q40 52 61 76Q82 100 109 76','none',C.teal,9),
 weak:p('M43 87L36 80L65 45L76 53L93 26L101 19L93 42L81 56L64 52L51 95Z','url(#scarlet)',C.navy,5)+p('M31 85L47 100M36 97L25 108','none',C.brass,7),
 mark:circle(64,64,29,'none',C.danger,7)+circle(64,64,9,C.brass,'none',0)+p('M64 13V32M64 96V115M13 64H32M96 64H115','none',C.ink,7),
 burn:p('M66 13Q100 45 94 61Q109 84 91 103Q63 125 36 101Q15 80 37 49Q38 68 51 66Q68 54 66 13Z','url(#scarlet)',C.navy,5)+p('M63 69Q82 89 62 105Q44 94 63 69Z',C.brass,'none',0),
 circuit:rect(27,27,74,74,9,'url(#ink)',C.brass,6)+rect(45,45,38,38,5,'url(#glass)')+p('M43 13V27M64 13V27M85 13V27M43 101V115M64 101V115M85 101V115M13 43H27M13 64H27M13 85H27M101 43H115M101 64H115M101 85H115','none',C.ink,6),
 engraving:p('M29 18H99V110H29Z','url(#ink)',C.brass,7)+p('M44 43L64 29L84 43L64 59ZM44 88L64 74L84 88L64 102Z','none',C.teal,6),
};
async function add(id,w,h,body,slot,border=[0,0,0,0],notes='',kind='vector') {
 const source=`sources/${id}.svg`, file=`assets/${id}.png`;
 const text=svg(w,h,body);
 await fs.writeFile(source,text);
 await sharp(Buffer.from(text),{density:w<=512?288:144}).resize(w,h).ensureAlpha().png().toFile(file);
 assets.push({id,file,source,width:w,height:h,slot,border_lbrt:[border[0],border[3],border[2],border[1]],border_ltrb:border,notes,method:kind});
}
async function generated(id,source,size,slot,notes='') {
 const file=`assets/${id}.png`;
 // Mechanical downsampling only: no colour key, retouching or alpha replacement.
 const pad=Math.ceil(size*.035);
 await sharp(source).resize(size-pad*2,size-pad*2,{fit:'contain',background:'#00000000'}).extend({top:pad,bottom:pad,left:pad,right:pad,background:'#00000000'}).ensureAlpha().png().toFile(file);
 assets.push({id,file,source,width:size,height:size,slot,border_lbrt:[0,0,0,0],border_ltrb:[0,0,0,0],notes,method:'subscription-imagegen; mechanical downsample'});
}

// Intent: shape changes, rather than tiny tick counts or colour changes, encode threat.
const attacks=[
 group(blade,'translate(19 19) scale(.7)'),
 blade,
 p('M25 98L29 67L78 24L109 17L99 45L52 89Z','url(#scarlet)',C.navy,5)+p('M42 95L83 44','none',C.brass,5),
 blade+group(blade,'translate(128 0) scale(-1 1)'),
 p('M17 101L34 43L53 22L41 77ZM48 111L57 33L77 12L73 84ZM79 103L84 40L108 23L104 78Z','url(#scarlet)',C.navy,5),
 p('M64 10L75 42L108 24L91 56L119 66L87 77L102 108L72 91L61 119L50 88L20 102L36 73L9 62L39 50L23 21L53 39Z',C.danger,C.navy,5)+group(blade,'translate(16 16) scale(.75)'),
 p('M64 7L76 30L99 14L97 42L122 47L103 66L120 88L93 90L89 117L66 101L45 121L39 96L13 101L26 77L7 61L32 51L18 28L47 32Z','url(#scarlet)',C.navy,5)+p('M61 29L46 66H64L54 100L88 53H69L78 29Z',C.navy,C.brass,3)
];
for(let i=0;i<attacks.length;i++) await add(`ui_intent_attack_${String(i+1).padStart(2,'0')}`,128,128,attacks[i],`현재/다음 공격 의도 · 시각 단계 ${i+1}`,[0,0,0,0],'피해량→단계 매핑은 게임의 기존 정책을 사용. 새 수치 규칙 없음.');
for(const key of ['defend','strength','debuff','attack_debuff','charge','summon','heal','curse','reflect','expose','defend_all','unknown']) await add(`ui_intent_${key}`,128,128,glyphs[key],`적 머리 위 의도 · ${key}`);
await add('ui_shield_block',64,64,group(shield,'scale(.5)'), 'HP 왼쪽 방어 수치 받침',[0,0,0,0],'숫자 안전 영역 x24..40, y18..34; 초상/파티별 HP 의미를 새로 만들지 않음.');

// Opaque center is supplied by runtime fill or a separate plate, never the border.
for(const who of ['enemy','party']){
 const color=who==='enemy'?'#d49a72':C.teal;
 await add(`ui_hp_frame_${who}`,256,40,rect(4,9,248,22,9,C.navy,C.navy,4)+rect(5,10,246,20,8,'none',color,2)+line(20,12,236,12,C.ink,1)+diamond(12,20,4,color)+diamond(244,20,4,color),`${who} 체력 막대 가장자리`,[20,0,20,0],'가로 slice만; 높이 40→20 균등 축소. fill rect [20,14,216,12].');
}
function portrait(size,ready){
 const c=size/2,r=size*.39;
 return circle(c,c,r+size*.055,'none',C.navy,size*.07)+circle(c,c,r+size*.055,'none',ready?C.brass:'#50647b',size*.018)+circle(c,c,r,'none','url(#gold)',size*.025)+[0,90,180,270].map(a=>group(diamond(c,c-r-size*.022,ready?6:4,ready?C.brass:C.teal),`rotate(${a} ${c} ${c})`)).join('')+(ready?[45,135,225,315].map(a=>group(p(`M${c-6} ${c-r-4}L${c} ${c-r-11}L${c+6} ${c-r-4}`,'none',C.brass,3),`rotate(${a} ${c} ${c})`)).join(''):'');
}
await add('ui_portrait_ring',160,160,portrait(160,false),'파티 초상 원형 틀 · 기본',[0,0,0,0],'중앙 alpha 구멍 r60. 게이지는 r68의 별도 게임 렌더; 이 PNG에는 채움 없음.');
await add('ui_portrait_ring_ready',160,160,portrait(160,true),'파티 초상 원형 틀 · 필살기 준비',[0,0,0,0],'정지 상태에도 4개 추가 금빛 갈매기 표시로 구분.');

// Every ornament fits wholly inside a declared non-stretching cap.
function corners(w,h,c=C.brass){return [[0,0,0],[w,0,90],[w,h,180],[0,h,270]].map(([x,y,a])=>group(p('M10 30V18L18 10H30','none',c,3)+diamond(19,19,4,c),`translate(${x} ${y}) rotate(${a})`)).join('');}
function rail(w,h,time,state){
 const color=state==='sealed'?C.muted:(time==='now'?C.brass:C.teal);
 const shell=rect(5,7,w-10,h-14,8,C.panel,color,state==='reserved'?3:1.5)+corners(w,h,color);
 const marks=Array.from({length:time==='now'?1:time==='next'?2:3},(_,i)=>diamond(20,40+i*10,3,color)).join('');
 const stateMark=state==='reserved'?p(`M${w-29} 22l5 5 9-11`,'none',color,3):state==='sealed'?p(`M${w-32} 21v-4q0-9 7-9t7 9v4M${w-36} 21h22v19h-22Z`,C.panel,C.muted,3):'';
 return shell+marks+stateMark;
}
for(const time of ['now','next','after'])for(const state of ['empty','reserved','sealed'])await add(`ui_rail_${time}_${state}`,320,96,rail(320,96,time,state),`예약 레일 · ${time}/${state}`,[44,0,44,0],'내용 안전 rect [48,14,224,68]. 가로만 stretch, 원본 높이는 균등 축소. now=1, next=2, after=3 마름모.');

for(const turn of ['player','enemy']){
 const col=turn==='player'?C.teal:C.danger;
 const accent=turn==='player'?diamond(180,100,19,C.brass):p('M164 77L193 106M164 106L193 77','none',C.brass,8);
 await add(`ui_banner_turn_${turn}`,1600,200,rect(0,42,1600,116,0,'url(#veil)')+p('M30 44H520L548 32H1060L1088 44H1570M30 156H520L548 168H1060L1088 156H1570','none',col,2)+accent+line(220,100,400,100,col,2)+line(1200,100,1400,100,col,2),`턴 전환 띠 · ${turn}`,[0,0,0,0],'게임 글자 안전 rect [440,56,720,88]. 이동/번쩍임은 아트 수용 범위 밖.');
}
await add('ui_cutin_plate',1600,420,p('M60 91L1490 24L1540 91L1495 329L105 396L62 326Z','url(#veil)','none',0)+p('M90 86L1490 24M106 396L1510 334','none',C.brass,5)+p('M111 99L1420 38M180 380L1487 317','none',C.teal,2)+p('M82 123L55 234L92 309M1509 118L1536 194L1518 284','none',C.brass,3),'필살기 공통 대각선 컷인 판',[0,0,0,0],'캐릭터는 별도 기존 원본을 얹음. 투명 가장자리, 중앙 navy 반투명; 개인 모션/캐릭터 내용 없음.');

for(const state of ['normal','target']){
 const col=state==='normal'?C.teal:C.brass;
 await add(`ui_drag_arrow_head_${state}`,96,96,p('M48 10L84 77L48 62L12 77Z',col,C.navy,6)+p('M48 27V57','none',C.ink,3),'카드 끌기 화살촉 · '+state,[0,0,0,0],'전방 -Y; pivot(.5,.5), 연결 anchor(.5,.68).');
 await add(`ui_drag_arrow_body_${state}`,32,64,rect(7,0,18,64,0,C.navy)+rect(12,0,8,64,0,col)+rect(14,0,2,64,0,C.ink),'카드 끌기 몸통 · '+state,[0,0,0,0],'세로 tile. 상하 가장자리의 연속 alpha는 의도적. 굴곡 경로는 게임이 배치.');
}

// Six hand-authored vector frames per effect; same canvas and registration.
function hitBody(type,i){
 const scale=[.32,.6,1,.94,1.04,1.1][i],opacity=[.45,.85,1,.8,.44,.16][i];
 let body='';
 if(type==='slash')body=p('M34 210Q89 69 224 32Q119 109 34 210Z',C.ink,'none',0)+p('M41 217Q92 90 217 42','none',C.teal,8)+p('M74 194Q132 130 218 79','none',C.brass,3);
 if(type==='blunt')body=p('M128 28L143 91L201 58L166 112L232 128L164 145L200 201L144 168L128 232L111 167L56 201L91 145L25 128L91 110L57 55L111 91Z',C.brass,'none',0)+circle(128,128,34,C.ink,'none',0)+circle(128,128,50+i*7,'none',C.ink,Math.max(1,6-i));
 if(type==='pierce')body=p('M15 185L182 65L239 29L203 86L35 202L94 145Z',C.ink,'none',0)+p('M24 209L217 63','none',C.teal,7)+p('M94 193L205 110M77 128L142 79','none',C.brass,4)+circle(170,87,19+i*3,'none',C.brass,3);
 const sparks=Array.from({length:5},(_,n)=>{const a=(n*72+i*13)*Math.PI/180,r=70+i*5;return diamond(128+Math.cos(a)*r,128+Math.sin(a)*r,Math.max(2,6-i),n%2?C.brass:C.ink);}).join('');
 return `<g opacity="${opacity}" transform="translate(128 128) scale(${scale}) translate(-128 -128)">${body}${sparks}</g>`;
}
for(const type of ['slash','blunt','pierce'])for(let i=0;i<6;i++)await add(`ui_hit_${type}_${String(i+1).padStart(2,'0')}`,256,256,hitBody(type,i),`타격 ${type} · 프레임 ${i+1}/6`,[0,0,0,0],'중심 pivot(.5,.5); 순서 01→06, 60ms/장 제안(콘티 제안만). reduced-motion에서는 생략.');

// Earlier skin-kit: fixed-aspect cards plus genuinely stretchable, separated borders.
const attributes={common:C.brass,volt:'#ffd84d',flame:'#ff7a3d',frost:'#7cc8ff',void:'#b68cff'};
for(const [attribute,col]of Object.entries(attributes)){
 const edge=p('M26 8H334L352 26V494L334 512H26L8 494V26Z','none','url(#gold)',5)+p('M31 18H329L342 31V489L329 502H31L18 489V31Z','none',col,3)+corners(360,520,col);
 const face=p('M24 225H336V488L326 498H34L24 488Z',C.panel,'none',0,'opacity=".94"')+line(28,224,332,224,col,2)+diamond(180,224,5,col)+line(40,297,320,297,'#46516a',1);
 await add(`ui_card_frame_${attribute}`,360,520,face+edge,`손패·보상 카드 완성 틀 · ${attribute}`,[0,0,0,0],'고정 비율 Simple; 그림 alpha 창 [28,30,304,186], 이름 [40,242,280,42], 효과 [40,315,280,160]. 184×266 권장.');
 await add(`ui_card_border_${attribute}`,360,520,edge,`가변 비율 카드 테두리 · ${attribute}`,[36,36,36,36],'중앙 완전 투명; 별도 ui_card_face_plate 아래 영역 배치. 최소 72×72.');
}
await add('ui_card_face_plate',256,256,rect(4,4,248,248,5,C.panel,'#46516a',1),'넓거나 긴 카드의 독립 이름/효과 판',[12,12,12,12],'중앙 반투명 표현은 게임 alpha로 조절; 프레임과 분리해서 art aperture 비율 유지.');
await add('ui_gem_cost',96,96,p('M48 5L77 19L91 48L77 77L48 91L19 77L5 48L19 19Z','url(#gold)',C.navy,3)+p('M48 16L71 27L80 48L71 69L48 80L25 69L16 48L25 27Z','url(#ink)',C.brass,2)+p('M25 29L48 19L70 29','none',C.ink,2),'카드 비용 보석',[0,0,0,0],'숫자 안전 rect [28,27,40,42].');
await generated('ui_gem_ap','originals/ui_gem_ap_generated.png',200,'행동력 보석','1254px 생성 원본을 200px로 축소; 숫자 안전 rect [76,69,48,66].');
await generated('ui_button_endturn','originals/ui_button_endturn_generated.png',300,'턴 종료 버튼 · 기본','이미지 안 숫자/바늘 없음; 글자 안전 원 중심(150,150), r83.');
await generated('ui_button_endturn_pressed','originals/ui_button_endturn_pressed_generated.png',300,'턴 종료 버튼 · 눌림','동일 참조로 생성한 상태 변형. 생성형 미세 윤곽 차이는 존재.');
await generated('ui_button_endturn_disabled','originals/ui_button_endturn_disabled_generated.png',300,'턴 종료 버튼 · 비활성','게임의 활성 조건/비활성 이유가 결정. 그림만으로 판정하지 않음.');
for(const [type,glyph]of Object.entries({attack:blade,defend:shield,special:clock}))await add(`ui_badge_intent_${type}`,128,128,p('M64 6L115 64L64 122L13 64Z','url(#ink)','url(#gold)',3)+group(glyph,'translate(23 17) scale(.47)')+rect(34,80,60,23,5,C.navy),`의도 받침 · ${type}`,[0,0,0,0],'숫자 안전 rect [38,80,52,24]. stage-first에서는 받침 없이 ui_intent_* 추천.');
for(const ready of [false,true])await add(`ui_portrait_frame${ready?'_ready':''}`,200,240,p('M25 8H175L192 27V208L170 231H30L8 208V27Z','none',ready?C.brass:'#7c8fa7',ready?5:3)+p('M28 17H172L183 31V202L164 221H36L17 202V31Z','none','url(#gold)',2)+diamond(100,12,7,ready?C.brass:C.teal)+corners(200,240,ready?C.brass:C.teal)+(ready?p('M75 229L100 211L125 229','none',C.brass,4):''),'저장·일반 파티 세로 초상 · '+(ready?'ready':'normal'),[0,0,0,0],'투명 중앙; 초상 crop mask는 게임에서 제공. battle은 원형 링 사용.');
await add('ui_panel_frame',256,256,rect(7,7,242,242,12,'none','#53617a',1)+rect(12,12,232,232,9,'none',C.brass,1.5)+corners(256,256),'반투명 패널의 별도 장식 경계',[40,40,40,40],'중앙 완전 투명. 판 배경은 ui_card_face_plate 또는 게임의 #141c31.');
await add('ui_ribbon_title',512,96,p('M6 48L26 15H486L506 48L486 81H26Z','url(#ink)','url(#gold)',2)+p('M36 23L21 48L36 73M476 23L491 48L476 73','none',C.teal,2),'지도/적/장소 이름 리본',[48,0,48,0],'글자 안전 rect [56,22,400,52].');
for(const color of ['brass','ink'])for(const state of ['normal','pressed']){
 const fill=color==='brass'?(state==='normal'?'url(#gold)':'#976c36'):(state==='normal'?C.panel:C.navy);
 await add(`ui_button_pill_${color}${state==='normal'?'':'_pressed'}`,512,128,rect(6,12,500,104,46,C.navy,C.navy,4)+rect(8,14,496,100,44,fill,C.brass,2)+p('M58 24H454','none',state==='normal'?C.ink:'#785e3d',1)+diamond(31,64,6,color==='brass'?C.navy:C.teal)+diamond(481,64,6,color==='brass'?C.navy:C.teal),'가로 버튼 · '+color+'/'+state,[68,0,68,0],'높이 고정 비율. 글자 영역 [76,28,360,72].');
}
for(const state of ['current','reachable','visited']){
 const col=state==='current'?C.teal:state==='reachable'?C.brass:C.muted;
 const ornament=state==='current'?[0,90,180,270].map(a=>group(diamond(128,18,9,col),`rotate(${a} 128 128)`)).join(''):state==='reachable'?[0,120,240].map(a=>group(p('M118 25L128 16L138 25','none',col,4),`rotate(${a} 128 128)`)).join(''):p('M155 215L170 229L194 201','none',C.brass,8);
 await add(`ui_node_medal_${state}`,256,256,circle(128,128,103,'none',C.navy,14)+circle(128,128,103,'none',col,5)+circle(128,128,91,'none','url(#gold)',2)+ornament,'지도 노드 테두리 · '+state,[0,0,0,0],'중앙 r88 완전 alpha. 현재=4마름모, 가능=3갈매기, 지나감=체크.');
}
for(const type of ['draw','discard']){
 const cards=p('M31 26L107 35L100 135L24 126Z','url(#ink)',C.muted,4)+p('M45 20L122 29L116 129L39 120Z','url(#ink)',C.brass,4)+p('M59 16H136V116H59Z','url(#ink)',C.ink,4);
 const arrow=type==='draw'?p('M78 65H113M103 52L117 65L103 78','none',C.teal,6):p('M112 53L80 85M80 71V87H96','none',C.brass,6);
 await add(`ui_pile_${type}`,160,160,cards+arrow,'카드 더미 · '+type,[0,0,0,0],'숫자는 아이콘 바깥 별도 배지.');
}
for(const state of ['empty','reserved','sealed'])await add(`ui_timeline_slot_${state}`,128,128,rect(8,8,112,112,14,C.panel,state==='sealed'?C.muted:C.brass,2)+group(state==='sealed'?p('M41 56V43Q41 20 64 20T87 43V56M34 56H94V102H34Z',C.navy,C.muted,7):state==='reserved'?clock:circle(64,64,29,'none',C.muted,3),'translate(16 16) scale(.75)'),'구형 작은 시간표 칸 · '+state,[0,0,0,0],'stage-first 가로 레일은 ui_rail_* 사용.');

const statuses={block:'defend',counter:'counter',debt:'debt',echo:'echo',vulnerable:'expose',weak:'weak',mark:'mark',burn:'burn',strength:'strength',reflect:'reflect'};
for(const [name,glyph]of Object.entries(statuses))await add(`ui_status_${name}`,64,64,group(glyphs[glyph],'scale(.5)'),'상태 칩 · '+name);
for(const type of ['circuit','engraving'])await add(`ui_part_${type}`,128,128,glyphs[type],'부품 슬롯 종류 · '+type,[0,0,0,0],'슬롯 종류 그림이며 개별 부품의 효과/등급을 뜻하지 않음.');

assets.sort((a,b)=>a.id.localeCompare(b.id));
for(const asset of assets){asset.sha256=crypto.createHash('sha256').update(await fs.readFile(asset.file)).digest('hex');}
await fs.writeFile('manifest.json',JSON.stringify({schema:'memory-forge-ui-art/1',created:'2026-10-18',production_final:false,scope:'local art only',border_order:'LTRB in source pixels; Unity Sprite.border uses L B R T',assets},null,2)+'\n');
console.log(JSON.stringify({assets:assets.length,vector:assets.filter(a=>a.method==='vector').length,generated:assets.filter(a=>a.method!=='vector').length}));
