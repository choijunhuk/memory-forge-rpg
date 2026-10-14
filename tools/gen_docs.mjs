// Generates the data-driven chapters of the design document from data/game_data.json, the engine and
// reports/economy_v0.7.json, so tables can never drift from the rules. Also runs coverage checks:
// every effect op / condition / counter / RuleError code used anywhere must be documented here.
// Usage: node tools/gen_docs.mjs   (run python3 tools/verify_economy.py first)
import { createRequire } from 'node:module';
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { notes } from '../content/character_notes.mjs';

const require = createRequire(import.meta.url);
const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const MF = require(join(root, 'engine', 'rules-core.js'));
const DATA = JSON.parse(readFileSync(join(root, 'data', 'game_data.json'), 'utf8'));
const ECON = JSON.parse(readFileSync(join(root, 'reports', 'economy_v0.7.json'), 'utf8'));
const IX = MF.indexData(DATA);
const OUT = join(root, 'docs', 'design', 'gen');
mkdirSync(OUT, { recursive: true });

const esc = (s) => String(s ?? '').replace(/\|/g, '\\|').replace(/\n/g, ' ');
const table = (head, rows) => `| ${head.join(' | ')} |\n|${head.map(() => '---').join('|')}|\n${rows.map((r) => `| ${r.map(esc).join(' | ')} |`).join('\n')}\n`;
const pct = (bps) => `${(bps / 100).toFixed(bps % 100 ? 1 : 0)}%`;
const fx2 = (x, d = 1) => Number(x).toFixed(d);
const ATTR = Object.fromEntries(DATA.attributes.map((a) => [a.id, a.name]));
const WT = Object.fromEntries(DATA.weaponTypes.map((w) => [w.id, w.name]));
const RAR = { B: '기본', A: '고급', S: '희귀', T: '토큰', C: '저주' };
const MODE = { y: '지금·예약·다다음', n: '즉시 전용', o: '예약 전용(다음·다다음)', after: '다다음 턴 전용' };
const TYPE = { atk: '공격', skl: '기술' };
const modeOf = (c) => ((c.kw || []).includes('contract') ? '즉시(계약)' : MODE[c.res || 'y']);
const files = {};
const put = (name, text) => { files[name] = text; writeFileSync(join(OUT, name), text); };

// ───────── enemy intent text (same wording as the UI) ─────────
function intentText(mv) {
  switch (mv.t) {
    case 'atk': return `${mv.label ? mv.label + ' ' : ''}공격 ${mv.d}${mv.h > 1 ? `×${mv.h}` : ''}`;
    case 'atkdebuff': return `${mv.label ? mv.label + ' ' : ''}공격 ${mv.d}${mv.h > 1 ? `×${mv.h}` : ''} + 파티 약화 ${mv.n}`;
    case 'def': return `방어 ${mv.b}${mv.str ? `, 힘 +${mv.str}` : ''}`;
    case 'buff': return `힘 +${mv.str}`;
    case 'debuff': return `파티 약화 ${mv.n}`;
    case 'charge': return `${mv.label || '충전'}(다음 행동 강공)`;
    case 'summon': return `${mv.label || '소환'}: ${IX.enemies[mv.e].name} ${mv.n}`;
    case 'heal': return `체력 비율이 가장 낮은 아군 회복 ${mv.n}`;
    case 'insert': return `버린 더미에 「${IX.cards[mv.card].name}」 ${mv.n}장`;
    case 'reflect': return `${mv.label ? mv.label + ' ' : ''}반사 ${mv.n}`;
    case 'expose': return `${mv.label || '약점 노출'}: 자신 취약 ${mv.n} 후 공격 ${mv.then.d}`;
    case 'blockAll': return `${mv.label ? mv.label + ' ' : ''}모든 적 방어 ${mv.b}`;
    default: return mv.t;
  }
}

// ───────── characters ─────────
function passiveValueText(w, all = true) {
  const vals = w.passive.refineAlt || w.passive.values;
  const f = (v) => (v >= 100 ? v / 100 : v);
  return w.passive.text.replace('{v}', all ? vals.map(f).join('/') : f(vals[0]));
}
function charSheet(ch, n) {
  const nt = notes[ch.id];
  const kit = DATA.cards.filter((c) => c.owner === ch.id && c.pool !== false);
  const extra = DATA.cards.filter((c) => c.owner === ch.id && c.pool === false);
  const sig = DATA.weapons.find((w) => w.signature === ch.id);
  const v = ch.visual;
  let s = `### ${n}. ${ch.name} (${ch.en}) — ${ch.rarity}${ch.pool === 'limited' ? ' 한정' : ''} · ${ATTR[ch.attr]} · ${WT[ch.weapon]}\n\n`;
  s += `> ${ch.oneLine}\n\n`;
  s += table(['항목', '내용'], [
    ['역할 / 축', `${ch.role} / ${ch.archetype}`],
    ['나이·구분', `${ch.age}세 · ${ch.tier === 'teen' ? '10대(성적 대상화 금지 규칙 적용)' : '성인(15세 기준 팬서비스 허용)'}`],
    ['획득', ch.pool === 'limited' ? '한정 모집(복각 전까지 획득 불가)' : ch.starter ? '시작 지급(프롤로그 클리어 시) + 상시 모집' : '상시 모집'],
    ['성격', ch.personality], ['이야기의 실마리', ch.storyHook], ['스토리 비중', nt.chapter], ['목소리', ch.voice],
  ]);
  s += `\n**디자인 의도.** ${nt.intent}\n\n`;
  s += `**외형(아트 기준).**\n\n` + table(['키', '체형', '머리', '눈', '의상', '무기', '모티프', '표정'], [[`${v.height}cm`, v.build, v.hair, v.eyes, v.outfit, v.weapon, v.motif, v.expression]]);
  s += `\n팔레트: ${v.palette.map((c) => `\`${c}\``).join(' · ')}\n\n`;
  s += `**패시브 「${ch.passive.name}」** — ${ch.passive.text}\n\n`;
  s += `**필살기 「${ch.ult.name}」** (게이지 100, 행동력 0) — ${ch.ult.text}\n\n`;
  s += `**공명(중복 획득 1~6).**\n\n` + table(['단계', '효과'], ch.resonance.map((r) => [`공명 ${r.n}`, r.text]));
  s += `\n**고유 카드 12장** (\`시작\` = 시작 기억 덱 4장. 수치는 기준 전투 값).\n\n`;
  s += table(['ID', '이름', '등급', '비용', '종류', '사용 방식', '효과'], kit.map((c) => [`\`${c.id}\`${c.start ? ' 시작' : ''}`, c.name, RAR[c.rar], c.cost, TYPE[c.type], modeOf(c), MF.cardText(c, IX)]));
  if (extra.length) {
    const evo = extra.filter((c) => c.rar !== 'T');
    const tok = extra.filter((c) => c.rar === 'T');
    if (evo.length) {
      const from = (id) => DATA.cards.find((c) => (c.evo || []).includes(id));
      s += `\n**진화 분기** (성장 3 달성 시 전투 보상 화면에서 2개 중 1개 선택, 이번 탐사 유지, 기억 저장 용량 2).\n\n`;
      s += table(['원본', '진화', '비용', '사용 방식', '효과'], evo.map((c) => [from(c.id)?.name || '-', `${c.name} (\`${c.id}\`)`, c.cost, modeOf(c), MF.cardText(c, IX)]));
    }
    if (tok.length) s += `\n**토큰**: ${tok.map((c) => `${c.name}(\`${c.id}\`) — ${MF.cardText(c, IX)}`).join('; ')}\n`;
  }
  s += `\n**시작 공용 카드**: ${IX.cards[DATA.startingCommon[ch.id]].name}. 시작 기억 덱 = 시작 카드 4장 + 공용 1장 + 장착 무기 카드 1장(무기 카드는 저장 대상 아님).\n\n`;
  if (sig) s += `**전용 무기 「${sig.name}」** (SSR) — ${sig.passive.name}: ${passiveValueText(sig)} (정련 R1→R5). 무기 카드: ${IX.cards[sig.card].name} — ${MF.cardText(IX.cards[sig.card], IX)}\n\n`;
  s += `**운용 가이드.**\n\n${nt.guide.map((g) => `- ${g}`).join('\n')}\n\n`;
  s += `**상성.** ${nt.synergy}\n\n**약점.** ${nt.weakness}\n\n`;
  s += `**인연(호감도) 톤.** ${nt.bond.tone} 에피소드: ${nt.bond.episodes.map((e, i) => `${i + 1}「${e}」`).join(' · ')}\n\n`;
  s += `**대사 샘플.** 로비: “${nt.lines.lobby[0]}” / “${nt.lines.lobby[1]}” · 전투 시작: “${nt.lines.battle}” · 필살기: “${nt.lines.ult}” · 승리: “${nt.lines.win}”\n\n`;
  return s;
}
let chars = '';
DATA.characters.forEach((ch, i) => { chars += charSheet(ch, i + 1); });
put('characters.md', chars);

// roster overview
put('roster.md', table(['ID', '이름', '등급', '획득', '속성', '무기', '역할', '축', '나이', '시작 지급'],
  DATA.characters.map((c) => [`\`${c.id}\``, c.name, c.rarity, c.pool === 'limited' ? '한정' : '상시', ATTR[c.attr], WT[c.weapon], c.role, c.archetype, `${c.age}(${c.tier === 'teen' ? '10대' : '성인'})`, c.starter ? '예' : '-'])));

// ───────── items ─────────
let items = '';
items += table(['ID', '이름', '종류', '등급', '획득', '전용', '패시브(R1/R2/R3/R4/R5)', '무기 카드'],
  DATA.weapons.map((w) => [`\`${w.id}\``, w.name, WT[w.type], w.rarity, { limited: '한정 무기 모집', standard: '상시/무기 모집', craft: '제작(기본 지급)' }[w.pool] || w.pool, w.signature ? IX.chars[w.signature].name : '-',
    `${w.passive.name}: ${passiveValueText(w)}`, `${IX.cards[w.card].name} — ${MF.cardText(IX.cards[w.card], IX)}`]));
put('weapons.md', items);
put('parts.md', table(['ID', '이름', '칸', '등급', '효과', '기억 저장 비용'], DATA.parts.map((p) => [`\`${p.id}\``, p.name, { temp: '임시(전투 한정)', circuit: '시간 회로', engraving: '보조 각인' }[p.slot], { temp: '-', common: '일반', rare: '희귀' }[p.rarity], p.text,
  p.slot === 'temp' ? '저장 불가' : (p.rarity === 'rare' ? DATA.rules.memoryCost.partRare : DATA.rules.memoryCost.partCommon)])));
put('relics.md', table(['ID', '이름', '등급', '효과'], DATA.relics.map((r) => [`\`${r.id}\``, r.name, { common: '일반', rare: '희귀', boss: '보스' }[r.rarity], r.text])));
put('commons.md', table(['ID', '이름', '등급', '비용', '종류', '사용 방식', '효과'], DATA.cards.filter((c) => c.owner === 'common' || c.owner === 'curse').map((c) => [`\`${c.id}\``, c.name, RAR[c.rar], c.cost, TYPE[c.type], modeOf(c), MF.cardText(c, IX)])));
put('statuses.md', table(['ID', '이름', '대상', '규칙'], DATA.statuses.map((x) => [`\`${x.id}\``, x.name, { enemy: '적', party: '파티', both: '양쪽' }[x.side], x.text])) + '\n' + table(['키워드', '규칙'], DATA.keywords.map((k) => [k.name, k.text])));

// ───────── enemies & events ─────────
let en = table(['ID', '이름', '구역', '구분', '체력', '균열', '약점', '행동 순환(반복)', '특수'], DATA.enemies.map((e) => [`\`${e.id}\``, e.name, e.zone, { normal: '일반', elite: '엘리트', boss: '보스', minion: '소환물' }[e.tier], e.hp, e.brk,
  e.weak.map((a) => ATTR[a]).join('·'), e.pattern.map(intentText).join(' → '), e.passive ? (e.passive.id === 'seedHeal' ? `씨앗 1개당 라운드 종료 시 자신 회복 ${e.passive.n}` : e.passive.id) : '-']));
en += '\n**조우표** (일반 전투·엘리트·보스 노드는 해당 구역 목록에서 균등 무작위).\n\n';
const encRows = [];
for (const [z, tiers] of Object.entries(DATA.encounters)) for (const [t, list] of Object.entries(tiers)) encRows.push([`${z}구역`, { normal: '일반', elite: '엘리트', boss: '보스' }[t], list.map((g) => g.map((id) => IX.enemies[id].name).join(' + ')).join(' / ')]);
en += table(['구역', '구분', '조합'], encRows);
put('enemies.md', en);
put('events.md', DATA.events.map((ev) => `**${ev.name}** (\`${ev.id}\`) — ${ev.text}\n\n` + table(['선택지', '결과', '효과 데이터', '조건'], ev.choices.map((c) => [c.label, c.text, `\`${JSON.stringify(c.fx)}\``, c.need ? `동전 ${c.need.coins} 이상` : '-']))).join('\n'));

// ───────── progression tables ─────────
const P = DATA.progression; const S = P.stats;
const lvRows = [];
for (let L = 1; L <= 60; L++) {
  if (!(L === 1 || L % 5 === 0 || L === 21)) continue;
  const sc = MF.Stats.stageScale(IX, L);
  lvRows.push([L, MF.Stats.charPower(S.charPower, L), MF.Stats.weaponPower(S.weaponPower, 'SSR', L), MF.Stats.weaponPower(S.weaponPower, 'SR', L), MF.Stats.weaponPower(S.weaponPower, 'R', L),
    S.hpBase + L, `${sc.hp}% / ${sc.dmg}%`, L < 60 ? MF.Stats.xpToNext(IX, L) : '-', L < 60 ? MF.Stats.xpToNext(IX, L, 'weapon') : '-']);
}
let pr = table(['레벨', '캐릭터 전투력 C', '무기 SSR', '무기 SR', '무기 R', '체력(장비 없음)', '스테이지 배율(적 체력/적 피해)', '다음 레벨 경험치(캐릭터)', '(무기)'], lvRows);
pr += '\n**캐릭터 승급**\n\n' + table(['단계', '레벨 상한', '필요 계정 레벨', '기억 인장', '승급 핵', '크레딧'], P.levelling.charAscension.map((a) => [a.stage, `${a.cap - 10}→${a.cap}`, a.account, a.seals, a.cores, a.credits.toLocaleString()]));
pr += '\n**무기 승급**\n\n' + table(['단계', '레벨 상한', '기계 합금', '크레딧'], P.levelling.weaponAscension.map((a) => [a.stage, `${a.cap - 10}→${a.cap}`, a.alloys, a.credits.toLocaleString()]));
pr += '\n**스킬 레벨(계열마다: 고유 카드·필살기)**\n\n' + table(['목표 레벨', '공방 교본', '크레딧', '승급 핵', '효과(누적)'], P.levelling.skillCosts.map((x) => [`Lv.${x.to}`, x.books, x.credits.toLocaleString(), x.cores, `×${(1 + 0.04 * (x.to - 1)).toFixed(2)}`]));
pr += `\n**장비(갑주·장신구)**: 제작 연마재 ${P.levelling.gearCraft.polish} + 크레딧 ${P.levelling.gearCraft.credits.toLocaleString()}. 강화 +n: 연마재 n, 크레딧 ${P.levelling.gearEnhance.creditsPerLevel.toLocaleString()}×n. `
  + `갑주 체력 = (2+n) + floor((60+Lv)×(300+60n)/10000). 장신구 = 피해·방어 +(250+50n)bps(가산 그룹).\n\n`;
pr += '**성장 던전** (집중 20, 3성 클리어 후 소탕)\n\n' + table(['ID', '이름', '해금(계정)', '1회 보상'], P.dungeons.map((d) => [`\`${d.id}\``, d.name, d.unlock, Object.entries(d.yield).map(([k, v]) => `${({ ink: '캐릭터 경험치', credits: '크레딧', oil: '무기 경험치', alloy: '기계 합금', seal: '기억 인장', book: '공방 교본', polish: '연마재' })[k] || k} ${v.toLocaleString()}`).join(', ')]));
pr += '\n**계정 레벨 해금**\n\n' + table(['계정 레벨', '해금'], P.accountLevel.unlocks.map((u) => [u.lv, u.what]));
pr += '\n**인연 보상**\n\n' + table(['인연 레벨', '보상'], P.affinity.rewards.map((r) => [r.lv, r.reward]));
pr += '\n**기록 균열 층**\n\n' + table(['층', '스테이지 레벨', '추가 배율', '보상(결정, 주기당 1회)'], P.recordRift.floors.map((f) => [f.floor, f.stage, f.hpBps ? `체력 ${pct(f.hpBps)} · 피해 ${pct(f.dmgBps)}` : '-', f.reward]));
pr += '\n**기록 균열 규칙(14일마다 층별 1개 교체)**\n\n' + table(['ID', '이름', '효과'], P.recordRift.modifiers.map((m) => [`\`${m.id}\``, m.name, m.text]));
pr += '\n**주간 보스 난이도**\n\n' + table(['난이도', '스테이지 레벨', '추가 배율'], P.weeklyBoss.difficulty.map((d) => [d.lv, d.stage, d.hpBps ? `체력 ${pct(d.hpBps)} · 피해 ${pct(d.dmgBps)}` : '-']));
const bossName = (id) => (IX.enemies[id] ? IX.enemies[id].name : `${id}(미작성)`);
pr += '\n**메인 스토리 챕터**\n\n' + table(['장', '제목', '스테이지 레벨', '구역 순서', '고정 보스', '첫 클리어 보상', '해금(계정)', '데이터'], P.storyChapters.map((c) => [c.ch, c.name, c.stage, c.zones.join(' → '), c.bosses.map(bossName).join(' → '), Object.entries(c.firstClear).map(([k, v]) => `${({ crystals: '결정', standardTicket: '상시권', limitedTicket: '한정권' })[k]} ${v}`).join(', '), c.unlockAccount, c.authored === false ? 'P2 작성 예정' : '완성']));
put('progression.md', pr);

// full level table (appendix)
const allLv = [];
for (let L = 1; L <= 60; L++) { const sc = MF.Stats.stageScale(IX, L); allLv.push([L, MF.Stats.charPower(S.charPower, L), S.hpBase + L, sc.hp, sc.dmg, L < 60 ? MF.Stats.xpToNext(IX, L) : '-']); }
put('level_table.md', table(['Lv', 'C(Lv)', '체력 60+Lv', '적 체력 %', '적 피해 %', '다음 레벨 XP'], allLv));

// ───────── gacha & economy ─────────
const G = DATA.gacha; const ch = G.curves.character; const wc = G.curves.weapon;
let ga = table(['풀', 'SSR 기본', '소프트 시작', '소프트 증가/회', '하드', 'SR 기본', 'SR 보장', '픽업 SSR', '픽업 SR'], Object.entries(G.curves).map(([k, c]) => [
  { character: '한정 캐릭터', weapon: '무기', standard: '상시', beginner: '초심자' }[k], pct(c.ssrBase), c.softStart < c.hard ? c.softStart : '-', c.softStep && c.softStart < c.hard ? `+${pct(c.softStep)}` : '-', c.hard, pct(c.srBase), `${c.srGuarantee}회`, c.featuredSsrBps ? pct(c.featuredSsrBps) : '-', c.featuredSrBps ? pct(c.featuredSrBps) : '-']));
const softRows = [];
for (let k = ch.softStart; k <= ch.hard; k++) softRows.push([k, pct(MF.Gacha.ssrRate(ch, k - 1)), k <= wc.hard ? pct(MF.Gacha.ssrRate(wc, k - 1)) : '-']);
ga += '\n**회차별 SSR 확률(직전 SSR 이후 k번째 뽑기)**\n\n' + table(['k', '캐릭터(하드 80)', '무기(하드 70)'], [[`1~${wc.softStart - 1}`, pct(ch.ssrBase), pct(wc.ssrBase)],
  ...Array.from({ length: ch.softStart - wc.softStart }, (_, i) => wc.softStart + i).map((k) => [k, pct(MF.Gacha.ssrRate(ch, k - 1)), pct(MF.Gacha.ssrRate(wc, k - 1))]), ...softRows.map((r) => r)]);
const e = ECON.gacha;
ga += '\n**정확 계산 결과** (tools/verify_economy.py, 마르코프 연쇄로 정확 계산 — 표본 추출 아님)\n\n';
ga += table(['지표', '한정 캐릭터', '무기'], [
  ['SSR 1개 평균 뽑기 수', fx2(e.character.mean, 2), fx2(e.weapon.mean, 2)], ['종합 SSR 확률(1/평균)', `${fx2(100 * e.character.consolidatedRate, 3)}%`, `${fx2(100 * e.weapon.consolidatedRate, 3)}%`],
  ['픽업 1개 평균 뽑기 수', fx2(e.character.featuredMean, 2), fx2(e.weapon.featuredMean, 2)], ['픽업 확정 최대(풀천장)', e.character.featuredWorst, e.weapon.featuredWorst],
  ['50회 안에 픽업', `${fx2(100 * e.character.pFeaturedWithin[50])}%`, `${fx2(100 * e.weapon.pFeaturedWithin[50])}%`],
  ['80회 안에 픽업', `${fx2(100 * e.character.pFeaturedWithin[80])}%`, `${fx2(100 * e.weapon.pFeaturedWithin[80])}%`],
  ['96회(무과금 1시즌 최대) 안에 픽업', `${fx2(100 * e.character.pFeaturedWithin[96])}%`, `${fx2(100 * e.weapon.pFeaturedWithin[96])}%`],
  ['120회 안에 픽업', `${fx2(100 * e.character.pFeaturedWithin[120])}%`, `${fx2(100 * e.weapon.pFeaturedWithin[120])}%`],
  ['140회 안에 픽업', `${fx2(100 * e.character.pFeaturedWithin[140])}%`, `${fx2(Math.min(100, 100 * e.weapon.pFeaturedWithin[140]))}%`],
]);
ga += '\n**SSR 누적 확률(픽업 여부 무관, 한정 캐릭터 곡선)**: ' + Object.entries(e.character.cdf).map(([k, v]) => `${k}회 ${fx2(100 * v)}%`).join(' · ') + '\n';
put('gacha.md', ga);
const se = ECON.season;
let inc = table(['출처', '단가', '횟수', '결정', '비고'], P.seasonIncome.f2pRecurring.map((r) => [r.src, r.per, r.times, r.crystals.toLocaleString(), r.optional ? '고난도(선택)' : '']));
inc += `\n합계 결정 **${se.crystalsMax.toLocaleString()}**(기록 균열 제외 ${se.crystalsBase.toLocaleString()}) + 한정 모집권 ${se.limitedTickets}(${P.seasonIncome.tickets.limitedSources}) + 상시 모집권 ${se.standardTickets}(${P.seasonIncome.tickets.standardSources}).\n`
  + `한정 배너 환산 **${fx2(se.limitedPullsMax)}회**(균열 제외 ${fx2(se.limitedPullsBase)}회). 천장 0에서 시작할 때 이 안에 픽업을 얻을 확률 ${fx2(100 * se.pFeaturedWithinMaxPulls)}%(균열 제외 ${fx2(100 * se.pFeaturedWithinBasePulls)}%).\n`;
put('income.md', inc);
const pd = ECON.products;
let bm = table(['상품', '가격(원)', '결정', '첫 구매 보너스', '1회 뽑기당 원', '첫 구매 시 1회당 원'], P.products.crystalPacks.map((p, i) => [p.id, p.krw.toLocaleString(), p.crystals.toLocaleString(), p.firstBonus.toLocaleString(), Math.round(pd.packs[i].krwPerPull).toLocaleString(), Math.round(pd.packs[i].krwPerPullFirst).toLocaleString()]));
const mo = P.products.monthly; const pa = P.products.pass;
bm += `\n- **${mo.name}** ${mo.krw.toLocaleString()}원/${mo.days}일: 즉시 유료 결정 ${mo.upfrontPaid} + 매일 무료 결정 ${mo.dailyFree} = ${pd.monthlyTotal.toLocaleString()} (1회당 약 ${Math.round(pd.monthlyKrwPerPull)}원). 자동 갱신 없음, 최대 ${mo.maxStackDays}일 누적.\n`;
bm += `- **${pa.name}** ${pa.krw.toLocaleString()}원: ${pa.levels}레벨(레벨당 ${pa.xpPerLevel} XP), 결정 ${pa.crystals.toLocaleString()} 포함(1회당 약 ${Math.round(pd.passKrwPerPull)}원). 플러스 ${pa.plus.krw.toLocaleString()}원: 즉시 ${pa.plus.levelsGranted}레벨 + ${pa.plus.extra}.\n`;
bm += `- **스킨**: ${P.products.skins.map((x) => `${x.tier}단계 ${x.krw.toLocaleString()}원(${x.scope})`).join(' / ')}.\n`;
bm += `- 참고: 최고 효율 팩 기준 하드 천장 80회 ≈ ${Math.round(pd.hardPityKrwAtBestPack).toLocaleString()}원, 풀천장 160회 ≈ ${Math.round(pd.fullPityKrwAtBestPack).toLocaleString()}원(첫 구매 보너스 제외).\n`;
put('products.md', bm);
const fb = ECON.fullBuild; const fa = ECON.farming;
put('build_cost.md', table(['항목', '1명 완성(Lv.60·A4·무기 Lv.60·스킬 10/10·장비 +15×2)'], [
  ['캐릭터 경험치', fb.charXp.toLocaleString()], ['무기 경험치', fb.weaponXp.toLocaleString()], ['기억 인장', fb.seals], ['기계 합금', fb.alloys], ['공방 교본', fb.books], ['승급 핵', fb.cores], ['연마재', fb.polish],
  ['크레딧', `${fb.creditsTotal.toLocaleString()} (레벨 ${fb.credits.charLevel.toLocaleString()} · 승급 ${fb.credits.charAscension.toLocaleString()} · 무기 ${fb.credits.weaponLevel.toLocaleString()} · 무기 승급 ${fb.credits.weaponAscension.toLocaleString()} · 스킬 ${fb.credits.skills.toLocaleString()} · 장비 ${fb.credits.gear.toLocaleString()})`],
  ['성장 던전 횟수', `${fa.totalRuns}회 (${Object.entries(fa.runs).map(([k, v]) => `${k} ${v}`).join(', ')})`], ['집중', `${fa.stamina.toLocaleString()} = 하루 수급(240+30) 기준 ${fx2(fa.daysOfStamina)}일`],
  ['주간 보스', `승급 핵 ${fb.cores}개 = 주 ${fa.coresPerWeek}개 기준 ${fx2(fa.weeksOfWeeklyBoss, 2)}주`], ['42일 동안 완성 가능 인원', `집중 기준 ${fx2(fa.fullBuildsPer42dByStamina, 2)}명 / 승급 핵 기준 ${fx2(fa.fullBuildsPer42dByCores, 2)}명(주간 보스 6주 ${6 * fa.coresPerWeek} + 이벤트 상점 ${fa.eventCoresPerSeason}) → 핵이 병목`],
]) + `\n계정 레벨: 20까지 ${fx2(ECON.account[0].days)}일, 40까지 ${fx2(ECON.account[1].days)}일, 60까지 ${fx2(ECON.account[2].days)}일(하루 집중 270 + 일일 임무 300 XP 기준).\n`);

// ───────── card value budget (per AP) ─────────
{
  // method: cards in the reward pool (pool !== false), exactly one effect that is single-target damage or block,
  // no counter scaling, no 'all'/'rand' target; value × hits; cost 0 counted as 0.5 AP. Weapon cards included.
  const groups = {};
  for (const c of DATA.cards) {
    if (c.pool === false || !['B', 'A', 'S'].includes(c.rar) || c.eff.length !== 1) continue;
    const o = c.eff[0];
    if (!['dmg', 'blk'].includes(o.op) || o.scale || o.tgt === 'all' || o.tgt === 'rand') continue;
    const hits = o.hits || 1; const ap = Math.max(c.cost, 0.5);
    const k = `${o.op}/${c.rar}`; (groups[k] = groups[k] || []).push({ id: c.id, now: (o.v * hits) / ap, next: ((o.rv ?? o.v) * hits) / ap, after: ((o.ra ?? o.rv ?? o.v) * hits) / ap, res: c.res || 'y' });
  }
  const avg = (l, f) => (l.reduce((t, x) => t + x[f], 0) / l.length).toFixed(1);
  const rows = [];
  for (const op of ['dmg', 'blk']) for (const r of ['B', 'A', 'S']) { const l = groups[`${op}/${r}`]; if (l) rows.push([`${op === 'dmg' ? '피해' : '방어'} · ${RAR[r]}(${r})`, l.length, avg(l, 'now'), avg(l, 'next'), avg(l, 'after'), l.map((x) => `\`${x.id}\``).join(' ')]); }
  put('card_budget.md', table(['구분', '카드 수', '지금', '다음 턴 예약', '다다음 턴 예약', '표본'], rows)
    + '\n집계 방법: 보상 풀 카드 중 효과가 정확히 1개(단일 대상 피해 또는 방어, 카운터 비례·광역·무작위 제외)인 카드, 값 × 타수 ÷ 행동력(비용 0은 0.5). 무기 카드 포함, 진화·토큰 제외. 예약 전용 카드의 "지금" 칸은 참고용 기본값 v.\n');
}

// ───────── effect DSL reference with coverage check ─────────
const OPS = {
  dmg: ['피해', 'v 즉시 / rv 다음 턴 예약 / ra 다다음 턴 예약 값, hits 타수, tgt(one·same·all·rand·fixed), brk 균열(기본 1, 약점일 때만), shock 충격(속성 무관), pierce 관통 bps, scale·hitsScale 카운터 비례, mult 조건부 배수'],
  blk: ['방어', 'v/rv/ra, scale, base0=true면 기본값 대신 0에서 스케일만'], heal: ['파티 회복', 'v/rv/ra, scale, overflowBlock 넘친 회복을 방어로'],
  st: ['적 상태 부여', 's(vul·weak·mark·burn), n, rn 예약 시 수치, tgt'], buff: ['파티 버프', 's(charge·seraLock·healUp·lumiRush·overloadFree·counterDouble), n(bps), turns'],
  counter: ['반격 획득', 'n'], draw: ['뽑기', 'n, rn, scale'], discard: ['버리기', 'n (플레이어가 고름)'], ap: ['행동력 즉시 +n', 'n (최대 9)'], apNext: ['다음 턴 행동력 +n', 'n'],
  gauge: ['필살기 게이지 +n', 'n, who(self·all)'], create: ['카드 생성', 'card, n, zone(hand)'], pull: ['예약 1개 1턴 앞당김', '픽 없으면 가장 먼저 건 다다음 예약 → 없으면 가장 먼저 건 예약'],
  slot: ['이번 턴 예약 칸 추가', 'next, after (최대 3/2)'], grow: ['이 카드 성장 +1', '예약 해결일 때만, 카드당 전투 1회·파티 전투 상한'], growPick: ['손의 성장 카드 1장 성장 +n', 'n'],
  growLowest: ['가장 낮은 성장 카드 n장 +1', '전투 상한과 별도'], growAll: ['모든 성장 카드 +1', '공명 6 전용'], copyPick: ['손의 성장 카드 사본(비용 0, 소멸)', 'filter'],
  loan: ['선불 계약', '행동력 +n, 다음 턴 −n (부채). 부채 상한 4, 행동력 최대 9 초과 불가'], defer: ['유예', '다음 턴 부채 1개를 1턴 미루고 +1(재유예 불가)'],
  restraint: ['절제 계약', '이번 턴 3장 이하 → 다음 턴 첫 예약 피해·방어 +30%, 4장째에 위약(다음 턴 드로우 −1)'], collateral: ['담보 계약', '다음 턴 예약 칸 1개 봉인(다음 내 턴 종료까지) + 방어 v'],
  clearDebt: ['부채 모두 제거', '-'], selfdmg: ['과부하', 'v: 파티 체력 손실(방어 무시). 쓰러지면 사용 불가'], cleanse: ['해로운 효과 모두 제거', '파티 약화 등'], cleanseOne: ['해로운 효과 1종 제거', 's'],
  exhaustPick: ['소멸시키기', 'from(hand·discard), n (플레이어가 고름)'], exhaustHand: ['손패 전부 소멸', '-'], tune: ['임시 부품 부착', 'part, pick(one·same·all·random)'],
  costdown: ['비용 감소(이번 턴)', 'n, pick(one·all)'], recall: ['버린 카드 1장 손으로', '플레이어가 고름'], power: ['전투 지속 효과', 'id(blueprint·greenhouse·bulwark·rebirth·oblivion·casefile·echochamber) + 수치'],
  echo: ['잔향 +n', 'n'], nextBoost: ['다음 다른 캐릭터 카드 +bps', 'bps (즉시 사용 카드에만)'], freeNext: ['다음 n장 비용 0', 'n'],
  breakBonus: ['균열 추가 감소', 'n 또는 scale'], breakBonusAll: ['모든 적 균열 감소', 'scale'], if: ['조건 분기', 'cond, then[], else[]'],
};
const CONDS = { marked: '대상에게 표식', burning: '대상이 화상', broken: '대상이 균열 상태', debt: '부채가 있음', modded: '이 카드에 부품(임시 포함)', reserved: '예약으로 해결 중', intentAttack: '대상의 이번 의도가 공격',
  echo: '잔향 ≥ n', hpBelow: '파티 체력 ≤ pct%', targetHpBelow: '대상 체력 ≤ pct%', growthInHand: '손의 성장 카드 ≥ n', exhaustedAtLeast: '소멸한 카드 ≥ n', killedThis: '이 카드로 처치' };
const PERS = { echo: '잔향', exhausted: '이번 전투 소멸 수(+시작 보정)', debt: '부채 합', growthSum: '덱의 성장치 합', growthCards: '성장 카드 수', counter: '현재 반격', missingHp10: '잃은 체력 ÷10(내림)', contracts: '이번 턴 계약 수',
  tempParts: '이번 전투 붙인 임시 부품 수', block: '현재 방어', lastCount: '직전 효과가 처리한 수', targetBurn: '대상 화상' };
const used = { ops: new Set(), conds: new Set(), pers: new Set() };
const walk = (arr) => { for (const o of arr || []) { used.ops.add(o.op); if (o.cond) used.conds.add(o.cond.c); if (o.scale) used.pers.add(o.scale.per); if (o.hitsScale) used.pers.add(o.hitsScale.per); for (const m of o.mult || []) used.conds.add(m.if.c); walk(o.then); walk(o.else); } };
for (const c of DATA.cards) { walk(c.eff); if (c.costIf) used.conds.add(c.costIf.cond.c); }
for (const c of DATA.characters) { walk(c.ult.eff); for (const r of c.resonance) for (const f of (r.fx.type === 'multi' ? r.fx.list : [r.fx])) walk(f.eff); }
const missing = [...[...used.ops].filter((o) => !OPS[o]).map((o) => `op:${o}`), ...[...used.conds].filter((c) => !CONDS[c]).map((c) => `cond:${c}`), ...[...used.pers].filter((p) => !PERS[p]).map((p) => `per:${p}`)];
if (missing.length) { console.error('UNDOCUMENTED', missing); process.exit(1); }
put('dsl.md', table(['op', '의미', '파라미터'], Object.entries(OPS).map(([k, [a, b]]) => [`\`${k}\``, a, b])) + '\n**조건(cond.c)**\n\n' + table(['c', '참일 때'], Object.entries(CONDS).map(([k, v]) => [`\`${k}\``, v]))
  + '\n**카운터(scale.per)** — 값 = k × 카운터(최대 max)\n\n' + table(['per', '카운터'], Object.entries(PERS).map(([k, v]) => [`\`${k}\``, v])));

// ───────── error codes ─────────
const src = readFileSync(join(root, 'engine', 'rules-core.js'), 'utf8');
const memorySrc = readFileSync(join(root, 'proposals', 'memory_v2.cjs'), 'utf8');
const codes = new Set([...src.matchAll(/RuleError\('([A-Z_]+)'/g), ...memorySrc.matchAll(/fail\('([A-Z_]+)'/g)].map((m) => m[1]).concat(['RESERVE_ONLY', 'NOT_RESERVABLE']));
const MSG = {
  AP_LOW: '행동력이 부족해요.', SLOT_FULL: '그 턴의 예약 칸이 가득 찼어요.', BAD_TARGET: '대상을 먼저 고르세요.', FIXED_MARK_REQUIRED: '표식이 있는 적만 고를 수 있어요.', NOT_RESERVABLE: '이 카드는 예약할 수 없어요.',
  RESERVE_ONLY: '이 카드는 예약 전용이에요.', DEBT_CAP: '부채 상한을 넘어요.', LOAN_OVERFLOW: '행동력 최대치(9)를 넘어요.', NOT_DEFERRABLE: '다음 턴에 갚을 부채가 없어요.', RESTRAINT_UNAVAILABLE: '이번 턴엔 절제 계약을 맺을 수 없어요.',
  NO_EMPTY_SLOT: '봉인할 빈 다음 턴 칸이 없어요.', RESERVATION_REQUIRED: '앞당길 예약이 없어요.', PULL_DESTINATION_FULL: '다음 턴 칸이 가득 차서 앞당길 수 없어요.', OVERLOAD_WOULD_KILL: '과부하로 쓰러질 수 있어 사용할 수 없어요.',
  ULT_NOT_READY: '필살기 게이지가 부족해요.', CRYSTALS_LOW: '결정이 부족해요.', COINS_LOW: '동전이 부족해요.', CANNOT_HEAL: '기억의 모루 때문에 회복할 수 없어요.', WOULD_KILL: '체력이 부족해 고를 수 없어요.',
  BEGINNER_LIMIT: '초심자 모집은 50회까지예요.', BEGINNER_TEN_ONLY: '초심자 모집은 10회 단위만 가능해요.', OVER_CAPACITY: '기억 용량을 넘었어요.', DEFEAT_KEEP_ONE: '패배 시에는 변경 1개만 남길 수 있어요.', WRONG_PHASE: '지금은 할 수 없는 행동이에요.',
  NOT_IN_HAND: '손에 없는 카드예요.', INVALID_MODE: '잘못된 사용 방식이에요.', IDEMPOTENCY_CONFLICT: '같은 요청 번호로 다른 내용이 왔어요(재시도 오류).', STALE_VERSION: '화면이 최신이 아니에요. 다시 불러와요.', BAD_COUNT: '1회 또는 10회만 모집할 수 있어요.',
  SELECTOR_NOT_READY: '상시 모집 300회를 채워야 선택할 수 있어요.', SELECTOR_USED: '선택권은 이미 사용했어요.', SELECTOR_BAD_CHOICE: '상시 SSR 캐릭터 중에서만 고를 수 있어요.', NODE_UNREACHABLE: '지금 갈 수 없는 칸이에요.',
  NOT_FOR_SALE: '판매 중이 아니에요.', BAD_UPGRADE: '강화할 수 없는 카드예요.', BAD_CARD: '선택할 수 없는 카드예요.', BAD_REWARD: '받을 수 없는 보상이에요.', BAD_EVOLUTION: '선택할 수 없는 진화예요.', BAD_PART: '선택할 수 없는 부품이에요.',
  NO_PART: '부품이 없어요.', BAD_CHOICE: '선택할 수 없어요.', BAD_ASSIGN: '그 캐릭터에게 배정할 수 없어요.', NO_BATTLE: '진행 중인 전투가 없어요.', UNKNOWN_BANNER: '없는 모집이에요.',
  ZONE_NOT_AUTHORED: '아직 열리지 않은 구역이에요(콘텐츠 준비 중).',
  INVALID_ASSIGNMENT: '공용 카드 배정이 올바르지 않아요.', INVALID_CARD: '허용되지 않는 카드예요.', INVALID_PART: '영구 저장할 수 없는 부품이에요.',
  INVALID_EVOLUTION: '진화 계보가 올바르지 않아요.', INVALID_UPGRADE: '강화 기록이 올바르지 않아요.', IDENTITY_REQUIRED: '카드 개체 키가 없거나 중복됐어요.',
  INVALID_SNAPSHOT: '기억 기록의 형식이 올바르지 않아요. 원본을 유지하고 확인하세요.', INVALID_SELECTION: '선택한 탐사 변화가 유효하지 않아요.',
  CHARACTER_SET_MISMATCH: '출발 전후의 캐릭터 목록이 일치하지 않아요.', UNSUPPORTED_TRANSFORMATION: '허용되지 않는 카드 변환이에요.',
  REASSIGNMENT_REQUIRES_SEPARATE_FLOW: '기존 기억의 공용 카드 배정은 여기서 변경할 수 없어요.', RUN_ID_REQUIRED: '탐사 식별자가 없어요.',
  RUN_MISMATCH: '다른 탐사의 저장 요청이에요.', REQUEST_CONFLICT: '같은 요청 번호에 다른 내용이 있어요.', VERSION_CONFLICT: '다른 저장이 먼저 반영됐어요. 최신 기억을 확인하세요.',
};
const internal = ['CAPACITY_INVARIANT', 'CARD_ZONE_CONSERVATION', 'DEBT_INVARIANT', 'ENEMY_RANGE', 'GAUGE_RANGE', 'GROWTH_INVARIANT', 'STATE_RANGE', 'RNG_BOUND', 'UNKNOWN_ACTOR', 'UNKNOWN_CARD', 'UNKNOWN_CHARACTER', 'UNKNOWN_COMMAND', 'UNKNOWN_ENEMY', 'UNSUPPORTED_EFFECT', 'PARTY_SIZE'];
const errRows = [...codes].sort().map((c) => [`\`${c}\``, internal.includes(c) ? '내부 불변식/데이터 오류(사용자에게는 "문제가 생겼어요" + 오류 보고)' : (MSG[c] || (() => { console.error('NO MESSAGE', c); process.exit(1); })())]);
put('errors.md', table(['코드', '사용자 문구 / 처리'], errRows));

// ───────── art prompt pack ─────────
const STYLE = 'Korean subculture mobile game art, clean cel shading with soft painterly lighting, crisp line art, rich but controlled colors, night workbench fantasy city with stopped clock towers and brass instruments';
const SAFE_ADULT = 'adult woman, clearly adult proportions and face, tasteful glamorous fanservice suitable for a 15+ game, no nudity, no exposed nipples or genitals, no see-through clothing on intimate areas, no sexual acts';
const SAFE_TEEN = 'teenage girl character, age-appropriate, wholesome, natural everyday pose, modest neutral camera framing, NOT sexualized: no suggestive or seductive pose, no pin-up framing, no camera focus on chest, hips, thighs or underwear, no lingerie or swimsuit';
const NEG = 'extra fingers, deformed hands, text, watermark, logo, signature, blurry, low resolution, photorealistic, 3d render';
let pp = '';
for (const c of DATA.characters) {
  const n = notes[c.id].en; const teen = c.tier === 'teen';
  const safe = teen ? SAFE_TEEN : SAFE_ADULT;
  const base = `${n.look}; outfit: ${n.outfit}; weapon: ${n.weapon}; motif: ${n.motif}; palette ${c.visual.palette.join(', ')}`;
  pp += `#### ${c.name} (${c.en}) — ${teen ? '10대 규칙' : '성인 규칙'}\n\n`;
  pp += '```text\n';
  pp += `[SHEET] Character design sheet, full body turnaround (front, 3/4, back), neutral standing pose, plain light-grey background, ${base}. ${STYLE}. ${safe}.\n`;
  pp += `[KEYART] Gacha splash illustration, ${n.pose}, ${n.mood}, dynamic composition, attribute glow ${ATTR[c.attr]} (${DATA.attributes.find((a) => a.id === c.attr).color}), ${base}. ${STYLE}. ${safe}. 16:9.\n`;
  pp += `[CUTIN] Card art bust shot, ${n.mood}, holding ${n.weapon}, ${n.look}, ${n.outfit}. ${STYLE}. ${safe}. 3:4, character within the safe area.\n`;
  pp += `[SD] Chibi SD figure, 2.5 heads tall, cheerful idle pose, ${n.look}, simplified ${n.outfit}. ${STYLE}. ${teen ? SAFE_TEEN : 'cute, non-sexual'}. 1:1 transparent background.\n`;
  pp += `[EXPR] Expression sheet, 6 head shots (neutral, happy, angry, surprised, sad, ${teen ? 'determined' : 'confident smirk'}), same angle and lighting, ${n.look}. ${STYLE}. ${safe}.\n`;
  pp += `NEGATIVE: ${NEG}${teen ? ', sexualized, fanservice, suggestive, lingerie, swimsuit, cleavage focus, upskirt, provocative pose' : ', minor-looking, childlike body or face'}\n`;
  pp += '```\n\n';
}
put('prompts.md', pp);

console.log(`generated ${Object.keys(files).length} files in docs/design/gen; ops ${used.ops.size}/${Object.keys(OPS).length} documented, conds ${used.conds.size}, counters ${used.pers.size}, error codes ${codes.size}`);
