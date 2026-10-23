// Common cards, weapons (+weapon cards), card parts (mods), relics, enemies, encounters, events.
import { card, D, B, H, ST, BUFF, DRAW, DISCARD, AP, GAUGE, SLOT, EXPICK, RECALL, CREATE, IF, C } from './helpers.mjs';

export const attributes = [
  { id: 'volt', name: '전격', color: '#ffd84d', icon: 'bolt' },
  { id: 'flame', name: '화염', color: '#ff7a3d', icon: 'flame' },
  { id: 'frost', name: '빙결', color: '#7cc8ff', icon: 'snow' },
  { id: 'void', name: '공허', color: '#b68cff', icon: 'void' },
  { id: 'none', name: '무속성', color: '#9aa6b8', icon: 'dot' },
];

export const weaponTypes = [
  { id: 'ranged', name: '원거리' }, { id: 'melee', name: '근접' }, { id: 'device', name: '장치' }, { id: 'ritual', name: '의식' },
];

// Statuses. Enemy statuses tick down at ROUND END (after the enemy phase);
// party statuses tick down at the END OF THE PLAYER TURN.
export const statuses = [
  { id: 'vul', name: '취약', side: 'enemy', text: '받는 피해 +25%. 라운드 종료 시 1 감소.' },
  { id: 'weak', name: '약화', side: 'both', text: '주는 피해 -25%. 적은 라운드 종료, 파티는 플레이어 턴 종료 시 1 감소.' },
  { id: 'mark', name: '표식', side: 'enemy', text: '고정 대상 카드의 조건. 라운드 종료 시 1 감소.' },
  { id: 'burn', name: '화상', side: 'enemy', text: '라운드 종료 시 화상 수치만큼 피해(방어 무시) 후 1 감소.' },
  { id: 'str', name: '힘', side: 'enemy', text: '공격 1회마다 피해 +수치. 지속.' },
  { id: 'broken', name: '균열', side: 'enemy', text: '균열 게이지 0: 다음 행동 1회 취소, 다음 플레이어 턴 종료까지 받는 피해 +25%. 파티 다음 턴 행동력 +1(턴당 최대 +2).' },
  { id: 'counter', name: '반격', side: 'party', text: '적의 공격 1회마다 공격한 적에게 반격 수치만큼 피해. 플레이어 턴 시작 시 0.' },
  { id: 'charge', name: '충전', side: 'party', text: '다음 피해 카드 1장의 피해 +n%. 사용하거나 기간이 끝나면 사라짐.' },
  { id: 'reflect', name: '반사', side: 'enemy', text: '카드 피해를 받을 때마다 파티가 반사 수치만큼 피해. 적 행동 시작 시 사라짐.' },
];

export const keywords = [
  { id: 'exhaust', name: '소멸', text: '사용 후 이번 전투에서 제외(소멸 더미).' },
  { id: 'contract', name: '계약', text: '즉시 사용 전용. 부채·봉인 등 미래 비용이 있다.' },
  { id: 'uses', name: '전투당 n회', text: 'n회 사용하면 소멸.' },
  { id: 'retain', name: '보존', text: '턴이 끝나도 손에 남는다.' },
  { id: 'fixed', name: '고정 대상', text: '표식이 있는 적만 지정 가능. 해결 시 대상이 죽었거나 표식이 없으면 불발(비용 환급 없음).' },
  { id: 'growth', name: '성장', text: '해결될 때 성장 +1(카드당 전투 1회, 파티 전투당 3회). 성장 3이면 전투 보상 화면에서 진화 분기 선택.' },
  { id: 'overload', name: '과부하 n', text: '사용 시 파티 체력 n을 잃는다(방어 무시).' },
  { id: 'shock', name: '충격 n', text: '속성과 관계없이 적의 균열 게이지를 n 깎는다.' },
];

// ───────────── common cards (18), 무속성 ─────────────
card('com_slash', '기본 공격', 'common', { rar: 'B', cost: 1, type: 'atk', attr: 'none', eff: [D(6, { shock: 1 })] });
card('com_guard', '기본 방어', 'common', { rar: 'B', cost: 1, type: 'skl', attr: 'none', eff: [B(6)] });
card('com_sort', '정리', 'common', { rar: 'B', cost: 0, type: 'skl', attr: 'none', res: 'n', uses: 2, eff: [DRAW(1), DISCARD(1)] });
card('com_patch', '응급 수리', 'common', { rar: 'B', cost: 1, type: 'skl', attr: 'none', kw: ['exhaust'], eff: [H(6)] });
card('com_capacitor', '시간 축전기', 'common', { rar: 'A', cost: 1, type: 'skl', attr: 'none', kw: ['retain'], eff: [AP(2)] });
card('com_focus', '집중', 'common', { rar: 'B', cost: 0, type: 'skl', attr: 'none', res: 'n', kw: ['exhaust'], eff: [BUFF('charge', 2500, { turns: 1 })] });
card('com_shockwave', '충격파', 'common', { rar: 'A', cost: 2, type: 'atk', attr: 'none', eff: [D(5, { tgt: 'all', shock: 1 })] });
card('com_barrier', '공용 차폐', 'common', { rar: 'A', cost: 2, type: 'skl', attr: 'none', eff: [B(12)] });
card('com_recall', '회상', 'common', { rar: 'A', cost: 1, type: 'skl', attr: 'none', res: 'n', eff: [RECALL()] });
card('com_flash', '섬광탄', 'common', { rar: 'A', cost: 0, type: 'skl', attr: 'none', res: 'n', kw: ['exhaust'], eff: [ST('weak', 1, { tgt: 'all' })] });
card('com_pin', '고정핀', 'common', { rar: 'B', cost: 1, type: 'skl', attr: 'none', eff: [ST('vul', 2, { tgt: 'one' })] });
card('com_rally', '사기 진작', 'common', { rar: 'A', cost: 1, type: 'skl', attr: 'none', res: 'n', eff: [GAUGE(15, 'all')] });
card('com_salvage', '재활용', 'common', { rar: 'A', cost: 0, type: 'skl', attr: 'none', res: 'n', eff: [EXPICK('hand', 1), AP(1)] });
card('com_overwatch', '경계 사격', 'common', { rar: 'B', cost: 1, type: 'atk', attr: 'none', eff: [D(9, { shock: 1 })] });
card('com_bandage', '붕대', 'common', { rar: 'B', cost: 0, type: 'skl', attr: 'none', res: 'n', kw: ['exhaust'], eff: [H(3)] });
card('com_tactics', '전술 노트', 'common', { rar: 'A', cost: 1, type: 'skl', attr: 'none', res: 'n', eff: [DRAW(2)] });
card('com_anchor', '시간 닻', 'common', { rar: 'A', cost: 1, type: 'skl', attr: 'none', res: 'n', eff: [DRAW(2)] });
card('com_breaker', '균열 쐐기', 'common', { rar: 'S', cost: 1, type: 'atk', attr: 'none', eff: [D(4, { shock: 3 })] });
// tokens / curses
card('smudge', '얼룩', 'curse', { rar: 'C', cost: 1, type: 'skl', attr: 'none', res: 'n', kw: ['exhaust'], pool: false, eff: [], flavor: '턴 종료 시 손에 있으면 파티 체력 2를 잃는다.' });
card('penalty_note', '위약금', 'curse', { rar: 'C', cost: 2, type: 'skl', attr: 'none', res: 'n', kw: ['exhaust'], pool: false, eff: [], flavor: '사용하면 사라지는 빚 문서.' });

// ───────────── weapons ─────────────
// rarity SSR(signature/standard) / SR / R(지급·제작). passive values are listed for R1..R5 (정련).
export const weapons = [];
const w = (o) => { weapons.push(o); return o; };
// Signature SSR (limited/standard character signatures)
w({ id: 'w_sera', name: '영겁의 조준경', type: 'ranged', rarity: 'SSR', signature: 'sera', pool: 'standard',
  passive: { id: 'w_reservedDmg', name: '먼 미래의 표적', values: [1200, 1500, 1800, 2100, 2400], scope: 'wielder', text: '장착자가 보존했던 카드로 공격하면 피해 +{v}%.' },
  card: 'wc_sera' });
card('wc_sera', '조준경 고정', 'weapon', { rar: 'S', cost: 1, type: 'skl', attr: 'volt', res: 'n', eff: [BUFF('charge', 3000, { turns: 2 }), DRAW(1)] });
w({ id: 'w_yuri', name: '온실 등불', type: 'ritual', rarity: 'SSR', signature: 'yuri', pool: 'standard',
  passive: { id: 'w_growthHeal', name: '생장의 빛', values: [2, 2, 3, 3, 4], scope: 'party', text: '성장 카드가 해결될 때 파티 회복 {v}.' },
  card: 'wc_yuri' });
card('wc_yuri', '등불 빛', 'weapon', { rar: 'S', cost: 1, type: 'skl', attr: 'frost', eff: [B(6), { op: 'growPick', n: 1 }] });
w({ id: 'w_ravien', name: '황금 저울', type: 'device', rarity: 'SSR', signature: 'ravien', pool: 'standard',
  passive: { id: 'w_debtDmg', name: '균형 감각', values: [800, 1000, 1200, 1400, 1600], scope: 'party', text: '부채가 있을 때 파티 공격 피해 +{v}%.' },
  card: 'wc_ravien' });
card('wc_ravien', '저울질', 'weapon', { rar: 'S', cost: 0, type: 'skl', attr: 'flame', res: 'n', eff: [DRAW(0, { scale: { per: 'debt', k: 1, max: 2 } }), ST('burn', 2, { tgt: 'all' })] });
w({ id: 'w_sion', name: '망각의 책갈피', type: 'melee', rarity: 'SSR', signature: 'sion', pool: 'standard',
  passive: { id: 'w_exhaustGuard', name: '접힌 페이지', values: [2, 2, 3, 3, 4], scope: 'party', text: '카드가 소멸할 때 방어 {v}.' },
  card: 'wc_sion' });
card('wc_sion', '책장 넘기기', 'weapon', { rar: 'S', cost: 1, type: 'atk', attr: 'void', eff: [D(7), EXPICK('hand', 1)] });
w({ id: 'w_kaya', name: '추적자의 석궁', type: 'ranged', rarity: 'SSR', signature: 'kaya', pool: 'limited',
  passive: { id: 'w_markedDmg', name: '빗나가지 않는 화살', values: [1200, 1500, 1800, 2100, 2400], scope: 'wielder', text: '장착자가 표식 적에게 주는 피해 +{v}%.' },
  card: 'wc_kaya' });
card('wc_kaya', '추적 화살', 'weapon', { rar: 'S', cost: 1, type: 'atk', attr: 'flame', eff: [D(7), ST('mark', 2, { tgt: 'same' })] });
w({ id: 'w_lumi', name: '섬전 쌍인', type: 'melee', rarity: 'SSR', signature: 'lumi', pool: 'limited',
  passive: { id: 'w_lowHpDmg', name: '한계선', values: [1200, 1500, 1800, 2100, 2400], scope: 'wielder', text: '파티 체력 50% 이하일 때 장착자 피해 +{v}%.' },
  card: 'wc_lumi' });
card('wc_lumi', '섬전', 'weapon', { rar: 'S', cost: 0, type: 'atk', attr: 'volt', res: 'n', eff: [{ op: 'selfdmg', v: 2 }, D(9, { brk: 2 })] });
// Standard SSR (generic, one per type) — pulled from weapon banners when the 75% is lost
w({ id: 'w_star_bow', name: '별자리 장궁', type: 'ranged', rarity: 'SSR', pool: 'standard',
  passive: { id: 'w_reservedDmg', name: '별의 궤도', values: [1000, 1250, 1500, 1750, 2000], scope: 'wielder', text: '장착자가 보존했던 카드로 공격하면 피해 +{v}%.' }, card: 'wc_star_bow' });
card('wc_star_bow', '별빛 화살', 'weapon', { rar: 'A', cost: 1, type: 'atk', eff: [D(9)] });
w({ id: 'w_frost_blade', name: '서리 결정검', type: 'melee', rarity: 'SSR', pool: 'standard',
  passive: { id: 'w_brokenDmg', name: '결정 파쇄', values: [1000, 1250, 1500, 1750, 2000], scope: 'wielder', text: '장착자가 균열 상태 적에게 주는 피해 +{v}%.' }, card: 'wc_frost_blade' });
card('wc_frost_blade', '결정 베기', 'weapon', { rar: 'A', cost: 1, type: 'atk', eff: [D(10, { brk: 2 })] });
w({ id: 'w_clock_core', name: '태엽 핵심', type: 'device', rarity: 'SSR', pool: 'standard',
  passive: { id: 'w_startDraw', name: '첫 태엽', values: [1, 1, 1, 2, 2], scope: 'party', text: '전투 첫 턴에 카드 {v}장 추가로 뽑기.' }, card: 'wc_clock_core' });
card('wc_clock_core', '태엽 감기', 'weapon', { rar: 'A', cost: 1, type: 'skl', res: 'n', eff: [DRAW(2)] });
w({ id: 'w_silver_hymnal', name: '은빛 성가집', type: 'ritual', rarity: 'SSR', pool: 'standard',
  passive: { id: 'w_healUp', name: '은빛 선율', values: [1000, 1250, 1500, 1750, 2000], scope: 'party', text: '파티 회복량 +{v}%.' }, card: 'wc_silver_hymnal' });
card('wc_silver_hymnal', '성가', 'weapon', { rar: 'A', cost: 1, type: 'skl', eff: [H(4), B(4)] });
// SR weapons (2 per type)
const srw = [
  ['w_hunter_rifle', '사냥용 소총', 'ranged', 'w_firstAtk', '첫 사격', [1500, 1875, 2250, 2625, 3000], '전투 첫 공격 카드 피해 +{v}%.', 'wc_hunter_rifle', '조준 사격', [D(8)]],
  ['w_mech_bow', '기계 활', 'ranged', 'w_multiHit', '연사 기구', [1, 1, 2, 2, 3], '다중 타격 카드의 첫 타격 피해 +{v}.', 'wc_mech_bow', '연발 화살', [D(4, { hits: 2 })]],
  ['w_guard_saber', '경비대 세이버', 'melee', 'w_breakDmg', '돌파 훈련', [800, 1000, 1200, 1400, 1600], '균열 상태 적에게 피해 +{v}%.', 'wc_guard_saber', '세이버 일격', [D(9)]],
  ['w_iron_tonfa', '강철 톤파', 'melee', 'w_blockOnAtk', '막고 친다', [2, 2, 3, 3, 4], '장착자의 공격 카드 사용 시 방어 {v}.', 'wc_iron_tonfa', '톤파 연타', [D(6), B(4)]],
  ['w_pocket_engine', '포켓 엔진', 'device', 'w_gaugeUp', '예열', [1000, 1250, 1500, 1750, 2000], '장착자 필살기 게이지 획득 +{v}%.', 'wc_pocket_engine', '엔진 점화', [DRAW(1), GAUGE(10)]],
  ['w_ledger_lock', '장부 자물쇠', 'device', 'w_startBlock', '잠금 장치', [4, 5, 6, 7, 8], '전투 시작 시 방어 {v}.', 'wc_ledger_lock', '잠금', [B(8)]],
  ['w_choir_bell', '성가대 종', 'ritual', 'w_healUp', '합창', [800, 1000, 1200, 1400, 1600], '파티 회복량 +{v}%.', 'wc_choir_bell', '종소리', [H(4)]],
  ['w_glass_rod', '유리 지팡이', 'ritual', 'w_blockUp', '굴절', [800, 1000, 1200, 1400, 1600], '장착자 방어 +{v}%.', 'wc_glass_rod', '굴절막', [B(5), D(4)]],
];
for (const [id, name, type, pid, pname, vals, ptext, cid, cname, eff] of srw) {
  w({ id, name, type, rarity: 'SR', pool: 'standard', passive: { id: pid, name: pname, values: vals, scope: pid === 'w_healUp' ? 'party' : 'wielder', text: ptext }, card: cid });
  card(cid, cname, 'weapon', { rar: 'A', cost: 1, type: eff[0].op === 'dmg' ? 'atk' : 'skl', eff });
}
// R weapons (지급/제작, refined with 제련핵 instead of duplicates)
const rw = [
  ['w_basic_ranged', '훈련용 권총', 'ranged', 'wc_basic_ranged', '권총 사격', [D(7)]],
  ['w_basic_melee', '훈련용 단검', 'melee', 'wc_basic_melee', '단검 찌르기', [D(7)]],
  ['w_basic_device', '기본 공구', 'device', 'wc_basic_device', '공구 방벽', [B(6)]],
  ['w_basic_ritual', '기본 성구', 'ritual', 'wc_basic_ritual', '작은 기도', [H(3), B(3)]],
];
for (const [id, name, type, cid, cname, eff] of rw) {
  w({ id, name, type, rarity: 'R', pool: 'craft', passive: { id: 'w_none', name: '-', values: [0, 0, 0, 0, 0], scope: 'wielder', text: '패시브 없음(제련핵으로 정련 시 피해·방어 +{v}%).', refineAlt: [0, 300, 600, 900, 1200] }, card: cid });
  card(cid, cname, 'weapon', { rar: 'B', cost: 1, type: eff[0].op === 'dmg' ? 'atk' : 'skl', eff });
}

// ───────────── card parts (C 개조) ─────────────
// circuit = 시간 회로(1칸), engraving = 보조 각인(1칸). bps add to the additive damage/block group.
export const parts = [
  { id: 'amp', slot: 'temp', name: '증폭', rarity: 'temp', text: '이번 전투 동안 피해·방어 +30%.', fx: { addBps: 3000 } },
  { id: 'overheat', slot: 'temp', name: '과열', rarity: 'temp', text: '이번 전투 동안 피해 +50%, 사용 후 소멸.', fx: { addBps: 5000, exhaust: true } },
  { id: 'c_delay', slot: 'circuit', name: '지연 증폭', rarity: 'common', text: '보존: 턴이 끝나도 손에 남는다.', fx: { retain: true } },
  { id: 'c_quick', slot: 'circuit', name: '즉발 회로', rarity: 'common', text: '피해·방어 +20%.', fx: { immBps: 2000 } },
  { id: 'c_precise', slot: 'circuit', name: '정밀 회로', rarity: 'common', text: '균열 수치 +1.', fx: { brk: 1 } },
  { id: 'c_echo', slot: 'circuit', name: '메아리 회로', rarity: 'rare', text: '해결되면 다음 턴 손에 50% 위력의 소멸 사본(비용 0) 추가.', fx: { echoCopy: 5000 } },
  { id: 'c_far', slot: 'circuit', name: '원거리 전송', rarity: 'rare', text: '피해·방어 +40%. 비용 +1.', fx: { allBps: 4000, cost: 1 } },
  { id: 'c_lite', slot: 'circuit', name: '경량 회로', rarity: 'rare', text: '비용 -1(최소 0), 피해·방어 -25%.', fx: { cost: -1, allBps: -2500 } },
  { id: 'e_pierce', slot: 'engraving', name: '관통', rarity: 'common', text: '피해의 50%가 방어를 무시.', fx: { pierce: 5000 } },
  { id: 'e_guard', slot: 'engraving', name: '보호', rarity: 'common', text: '해결 시 방어 4.', fx: { extraBlock: 4 } },
  { id: 'e_recall', slot: 'engraving', name: '회수', rarity: 'common', text: '해결 시 카드 1장 뽑기(턴당 1회).', fx: { drawOnce: 1 } },
  { id: 'e_mark', slot: 'engraving', name: '표식', rarity: 'common', text: '피해를 준 적에게 표식 1.', fx: { markOnHit: 1 } },
  { id: 'e_burn', slot: 'engraving', name: '연소', rarity: 'common', text: '피해를 준 적에게 화상 2.', fx: { burnOnHit: 2 } },
  { id: 'e_chill', slot: 'engraving', name: '냉각', rarity: 'common', text: '피해를 준 적에게 약화 1.', fx: { weakOnHit: 1 } },
  { id: 'e_gauge', slot: 'engraving', name: '가속', rarity: 'rare', text: '해결 시 소유자 필살기 게이지 +10.', fx: { gauge: 10 } },
  { id: 'e_sacrifice', slot: 'engraving', name: '소멸 대가', rarity: 'rare', text: '피해·방어 +50%. 사용 후 소멸.', fx: { allBps: 5000, exhaust: true } },
];

// ───────────── relics (32) ─────────────
export const relics = [
  // common 18
  { id: 'empty_clock', name: '빈 시계', rarity: 'common', text: '전투마다 첫 카드 비용 -1(최소 0).' },
  { id: 'memory_envelope', name: '기억의 봉투', rarity: 'common', text: '이번 탐사에서 처음 받는 치명 피해를 체력 1로 버틴다(1회).' },
  { id: 'unfinished_score', name: '미완성 악보', rarity: 'common', text: '한 턴에 세 캐릭터의 카드를 모두 쓰면 카드 1장 뽑기(턴당 1회).' },
  { id: 'symbiotic_moss', name: '공생 이끼', rarity: 'common', text: '성장이 일어날 때마다 방어 3.' },
  { id: 'credit_ledger', name: '외상 장부', rarity: 'common', text: '부채가 있는 턴의 첫 공격 피해 +20%.' },
  { id: 'virtue_of_refusal', name: '거절의 미덕', rarity: 'common', text: '카드 보상을 거절하면 최대 체력 +2(탐사당 4회).' },
  { id: 'forge_ember', name: '공방의 불씨', rarity: 'common', text: '휴식·공방 노드를 한 번 더 이용(각 2회).' },
  { id: 'bronze_cog', name: '청동 톱니', rarity: 'common', text: '전투 시작 시 방어 8.' },
  { id: 'pocket_brazier', name: '휴대용 화로', rarity: 'common', text: '전투 시작 시 무작위 적에게 화상 4.' },
  { id: 'silver_needle', name: '은빛 바늘', rarity: 'common', text: '적이 균열 상태가 될 때마다 파티 회복 3.' },
  { id: 'old_compass', name: '낡은 나침반', rarity: 'common', text: '휴식 노드의 회복량 +10%p.' },
  { id: 'lucky_coin', name: '행운의 동전', rarity: 'common', text: '전투 보상 동전 +10.' },
  { id: 'time_stamp', name: '타임 스탬프', rarity: 'common', text: '보존했던 카드를 쓰면 소유자 필살기 게이지 +5.' },
  { id: 'frost_crystal', name: '서리 결정', rarity: 'common', text: '매 턴 첫 빙결 피해의 균열 +1.' },
  { id: 'volt_coil', name: '전류 코일', rarity: 'common', text: '매 턴 첫 전격 카드 피해 +4.' },
  { id: 'void_shard', name: '공허의 조각', rarity: 'common', text: '카드가 소멸할 때 무작위 적에게 피해 3.' },
  { id: 'ember_ring', name: '불씨 반지', rarity: 'common', text: '화상 상태인 적에게 주는 피해 +10%.' },
  { id: 'quick_hands', name: '재빠른 손', rarity: 'common', text: '전투 첫 턴에 카드 2장 추가로 뽑기.' },
  // rare 10
  { id: 'cracked_hourglass', name: '금 간 모래시계', rarity: 'rare', text: '매 전투 첫 턴 카드 2장 더 뽑기. 첫 턴 행동력 -1.' },
  { id: 'twin_pendulum', name: '쌍둥이 시계추', rarity: 'rare', text: '턴 시작 뽑기 +1. 손패 최대 9장.' },
  { id: 'precision_lens', name: '정밀 렌즈', rarity: 'rare', text: '보존했던 공격 카드의 균열 +1.' },
  { id: 'golden_quill', name: '황금 깃펜', rarity: 'rare', text: '개조된 카드의 피해·방어 +15%.' },
  { id: 'eternal_seed', name: '영원의 씨앗', rarity: 'rare', text: '전투 시작 시 무작위 성장 카드 1장 성장 +1.' },
  { id: 'contract_seal', name: '계약 인장', rarity: 'rare', text: '부채 상한 +1.' },
  { id: 'tuning_fork', name: '공명 소리굽쇠', rarity: 'rare', text: '잔향 최대 +1, 턴 시작 시 잔향 1.' },
  { id: 'oblivion_ink', name: '망각의 잉크', rarity: 'rare', text: '매 턴 처음 카드가 소멸하면 행동력 +1.' },
  { id: 'detective_notebook', name: '탐정의 수첩', rarity: 'rare', text: '표식이 있는 적을 처치하면 동전 +10, 카드 1장 뽑기.' },
  { id: 'lightning_rod', name: '피뢰침', rarity: 'rare', text: '과부하로 잃은 체력의 50%만큼 방어를 얻는다.' },
  // boss 4
  { id: 'chrono_crown', name: '시간술사의 왕관', rarity: 'boss', text: '매 턴 행동력 +1. 카드 보상 선택지가 3장에서 2장으로 줄어든다.' },
  { id: 'infinite_circuit', name: '무한 회로', rarity: 'boss', text: '턴 시작 뽑기 +1. 턴 시작 시 파티 체력 2를 잃는다.' },
  { id: 'memory_anvil', name: '기억의 모루', rarity: 'boss', text: '전투 시작 시 손패의 카드 2장에 "증폭" 부착. 휴식 노드에서 회복할 수 없다.' },
  { id: 'eye_of_rift', name: '균열의 눈', rarity: 'boss', text: '모든 카드 피해의 균열 +1. 전투 시작 시 덱에 얼룩 1장 추가.' },
];

// ───────────── enemies ─────────────
// pattern: ordered moves (cycle). weak: attributes that reduce the break gauge.
// move types: atk{d,h}, def{b}, buff{str}, debuff{s,n}, atkdebuff{d,h,s,n}, charge{}, summon{e,n}, heal{n,who}, insert{card,n}, reflect{n}, expose{n}, blockAll{b}
export const enemies = [
  // Zone 1 — 멈춘 시계탑 거리 (tuned: normals ~4 turns / ~10% party HP, elites ~6 turns / ~20%, boss ~10 turns / ~30% with starter decks)
  { id: 'e_moth', name: '기억 나방', zone: 1, tier: 'normal', hp: 18, weak: ['flame', 'volt'], brk: 3,
    pattern: [{ t: 'atk', d: 12 }, { t: 'atk', d: 7, h: 2 }, { t: 'buff', str: 1 }] },
  { id: 'e_cog', name: '톱니 병정', zone: 1, tier: 'normal', hp: 24, weak: ['volt', 'void'], brk: 4,
    pattern: [{ t: 'def', b: 6 }, { t: 'atk', d: 18 }, { t: 'atk', d: 14 }] },
  { id: 'e_smudge', name: '잉크 얼룩', zone: 1, tier: 'normal', hp: 15, weak: ['frost', 'flame'], brk: 2,
    pattern: [{ t: 'insert', card: 'smudge', n: 1 }, { t: 'atk', d: 13 }, { t: 'atk', d: 10 }] },
  { id: 'e_hound', name: '시계 사냥개', zone: 1, tier: 'normal', hp: 22, weak: ['frost', 'void'], brk: 3,
    pattern: [{ t: 'atk', d: 17 }, { t: 'atk', d: 9, h: 2 }, { t: 'def', b: 5 }] },
  { id: 'e_lantern', name: '흐린 등불', zone: 1, tier: 'normal', hp: 20, weak: ['void', 'volt'], brk: 3,
    pattern: [{ t: 'debuff', s: 'weak', n: 1 }, { t: 'atk', d: 18 }, { t: 'heal', n: 6, who: 'lowest' }] },
  { id: 'e_page', name: '떠도는 페이지', zone: 1, tier: 'normal', hp: 12, weak: ['flame'], brk: 2,
    pattern: [{ t: 'atk', d: 7, h: 2 }, { t: 'atk', d: 12 }] },
  { id: 'e_bellwarden', name: '종지기', zone: 1, tier: 'elite', hp: 60, weak: ['frost', 'flame'], brk: 7,
    pattern: [{ t: 'charge', label: '종 들어 올리기' }, { t: 'atk', d: 48, label: '종 내려치기' }, { t: 'def', b: 12, str: 2 }, { t: 'atk', d: 12, h: 2 }] },
  { id: 'e_mirrorclerk', name: '거울 서기', zone: 1, tier: 'elite', hp: 52, weak: ['volt', 'void'], brk: 6,
    pattern: [{ t: 'reflect', n: 3, label: '반사 거울' }, { t: 'atk', d: 24 }, { t: 'atk', d: 13, h: 2 }] },
  { id: 'e_gardener', name: '시든 정원사', zone: 1, tier: 'elite', hp: 55, weak: ['flame'], brk: 6,
    passive: { id: 'seedHeal', n: 5 },
    pattern: [{ t: 'summon', e: 'e_seed', n: 2, label: '씨앗 뿌리기' }, { t: 'atk', d: 25 }, { t: 'atk', d: 14, h: 2 }] },
  { id: 'e_seed', name: '씨앗', zone: 1, tier: 'minion', hp: 6, weak: ['flame', 'frost', 'volt', 'void'], brk: 1,
    pattern: [{ t: 'def', b: 2 }] },
  // Zone 2 — 잠긴 기록소
  { id: 'e_scribe', name: '서명 망령', zone: 2, tier: 'normal', hp: 34, weak: ['frost', 'volt'], brk: 4,
    pattern: [{ t: 'atk', d: 17 }, { t: 'atkdebuff', d: 11, s: 'weak', n: 1 }, { t: 'def', b: 8 }] },
  { id: 'e_shelf', name: '무너진 서가', zone: 2, tier: 'normal', hp: 46, weak: ['flame', 'void'], brk: 5,
    pattern: [{ t: 'def', b: 10 }, { t: 'atk', d: 25 }, { t: 'blockAll', b: 6 }] },
  { id: 'e_inkswarm', name: '잉크 떼', zone: 2, tier: 'normal', hp: 16, weak: ['frost'], brk: 2,
    pattern: [{ t: 'atk', d: 7, h: 2 }, { t: 'insert', card: 'smudge', n: 1 }] },
  { id: 'e_keeper', name: '열쇠지기', zone: 2, tier: 'normal', hp: 38, weak: ['volt', 'flame'], brk: 4,
    pattern: [{ t: 'buff', str: 2 }, { t: 'atk', d: 13, h: 2 }, { t: 'atk', d: 20 }] },
  { id: 'e_censor', name: '검열관', zone: 2, tier: 'elite', hp: 95, weak: ['void', 'frost'], brk: 8,
    pattern: [{ t: 'debuff', s: 'weak', n: 2 }, { t: 'atk', d: 26 }, { t: 'charge', label: '검열 낭독' }, { t: 'atk', d: 49, label: '삭제 판정' }] },
  { id: 'e_twinink', name: '쌍둥이 잉크', zone: 2, tier: 'elite', hp: 80, weak: ['flame', 'volt'], brk: 7,
    pattern: [{ t: 'atk', d: 12, h: 3 }, { t: 'reflect', n: 4 }, { t: 'def', b: 16 }] },
  // Bosses (run finals; the same three are also the weekly bosses with a harder profile, see docs)
  { id: 'b_bell', name: '종을 삼킨 기록관', zone: 1, tier: 'boss', hp: 130, weak: ['frost', 'volt'], brk: 10,
    pattern: [{ t: 'atkdebuff', d: 12, s: 'weak', n: 1, label: '탐색' }, { t: 'def', b: 25, label: '보호막' },
      { t: 'expose', n: 2, then: { t: 'atk', d: 52 }, label: '약점 노출·강타' }, { t: 'atk', d: 11, h: 3, label: '종소리 연타' }] },
  { id: 'b_notary', name: '거울의 공증인', zone: 1, tier: 'boss', hp: 125, weak: ['void', 'flame'], brk: 9,
    pattern: [{ t: 'reflect', n: 4, label: '반사 거울' }, { t: 'atk', d: 22 }, { t: 'atk', d: 10, h: 3 }, { t: 'charge', label: '판결 준비' }, { t: 'atk', d: 50, label: '판결' }] },
  { id: 'b_garden', name: '잠든 정원', zone: 1, tier: 'boss', hp: 125, weak: ['flame', 'frost'], brk: 9,
    passive: { id: 'seedHeal', n: 7 },
    pattern: [{ t: 'summon', e: 'e_seed', n: 2, label: '씨앗 뿌리기' }, { t: 'atk', d: 22 }, { t: 'atkdebuff', d: 14, s: 'weak', n: 1 }, { t: 'atk', d: 12, h: 2 }] },
  { id: 'b_archivist', name: '기록소의 대사서', zone: 2, tier: 'boss', hp: 230, weak: ['void', 'volt'], brk: 12,
    pattern: [{ t: 'blockAll', b: 20, label: '봉인 서가' }, { t: 'atk', d: 28 }, { t: 'insert', card: 'smudge', n: 2 }, { t: 'charge', label: '망각 낭독' }, { t: 'atk', d: 55, label: '망각 낭독' }] },
];

// Encounter tables (enemy id lists). The run generator picks by zone and tier.
export const encounters = {
  1: {
    normal: [['e_moth', 'e_moth'], ['e_cog', 'e_smudge'], ['e_hound'], ['e_lantern', 'e_page', 'e_page'], ['e_cog', 'e_page'], ['e_smudge', 'e_smudge', 'e_moth'], ['e_hound', 'e_page']],
    elite: [['e_bellwarden'], ['e_mirrorclerk'], ['e_gardener']],
    boss: [['b_bell'], ['b_notary'], ['b_garden']],
  },
  2: {
    normal: [['e_scribe', 'e_inkswarm'], ['e_shelf'], ['e_keeper', 'e_inkswarm'], ['e_scribe', 'e_scribe'], ['e_inkswarm', 'e_inkswarm', 'e_inkswarm']],
    elite: [['e_censor'], ['e_twinink']],
    boss: [['b_archivist']],
  },
};

// ───────────── events ─────────────
// choice effects run in the run engine. cost/effect keys: hpPct, hp, maxHp, coins, part, partRare, relic, card, cardCopy, cardRemove, curse, heal, healPct, nextApBonus, growth
export const events = [
  { id: 'ev_promise', name: '멈춘 약속', text: '시곗바늘이 멈춘 광장에서 누군가 지키지 못한 약속을 내민다.',
    choices: [
      { label: '약속을 대신 짊어진다', text: '최대 체력 -10%, 무작위 희귀 부품 1개', fx: { maxHpPct: -10, partRare: 1 } },
      { label: '대가를 치른다', text: '동전 -40, 무작위 일반 부품 1개', fx: { coins: -40, part: 1 }, need: { coins: 40 } },
      { label: '지나친다', text: '아무 일도 일어나지 않는다', fx: {} }] },
  { id: 'ev_mirror', name: '깨진 거울', text: '조각난 거울마다 다른 순간의 내가 비친다.',
    choices: [
      { label: '한 조각을 집는다', text: '체력 -6, 카드 1장 복제', fx: { hp: -6, cardCopy: 1 } },
      { label: '조각을 묻는다', text: '동전 -30, 카드 1장 제거', fx: { coins: -30, cardRemove: 1 }, need: { coins: 30 } },
      { label: '지나친다', text: '아무 일도 일어나지 않는다', fx: {} }] },
  { id: 'ev_busker', name: '떠도는 악사', text: '태엽 손풍금을 든 악사가 동전 몇 닢에 노래를 들려주겠다고 한다.',
    choices: [
      { label: '노래를 청한다', text: '동전 -25, 무작위 일반 유물 1개', fx: { coins: -25, relic: 'common' }, need: { coins: 25 } },
      { label: '같이 쉰다', text: '체력 15 회복', fx: { heal: 15 } },
      { label: '지나친다', text: '아무 일도 일어나지 않는다', fx: {} }] },
  { id: 'ev_merchant', name: '기억 상인', text: '"흠 없는 기억은 비싸고, 얼룩진 기억은 싸지요."',
    choices: [
      { label: '변주를 산다', text: '동전 -60, 카드 변주 1회', fx: { coins: -60, variation: 1 }, need: { coins: 60 } },
      { label: '얼룩을 판다', text: '덱에 얼룩 1장, 동전 +50', fx: { curse: 'smudge', coins: 50 } },
      { label: '지나친다', text: '아무 일도 일어나지 않는다', fx: {} }] },
  { id: 'ev_archive', name: '잊힌 서고', text: '먼지 쌓인 서가에 아무도 빌려 가지 않은 기억이 꽂혀 있다.',
    choices: [
      { label: '정리한다', text: '최대 체력 -5, 카드 2장 제거', fx: { maxHp: -5, cardRemove: 2 } },
      { label: '한 권 빌린다', text: '무작위 고급 카드 1장 획득', fx: { card: 'A' } },
      { label: '지나친다', text: '아무 일도 일어나지 않는다', fx: {} }] },
  { id: 'ev_rift', name: '시간의 틈', text: '공기가 갈라진 틈 너머로 조금 이른 미래가 보인다.',
    choices: [
      { label: '먼저 들어간다', text: '체력 -8, 다음 전투 첫 턴 행동력 +2', fx: { hp: -8, nextApBonus: 2 } },
      { label: '틈을 닫는다', text: '체력 10 회복', fx: { heal: 10 } }] },
  { id: 'ev_spirit', name: '온실의 정령', text: '작은 정령이 시든 잎을 내밀며 고개를 갸웃한다.',
    choices: [
      { label: '돌봐 준다', text: '성장 카드 1장 성장 +1(이번 탐사 유지)', fx: { growth: 1 } },
      { label: '함께 쉰다', text: '체력 12 회복', fx: { heal: 12 } }] },
  { id: 'ev_contracts', name: '계약서 더미', text: '서명만 하면 되는 계약서가 바람에 날린다.',
    choices: [
      { label: '서명한다', text: '동전 +80, 덱에 위약금 1장', fx: { coins: 80, curse: 'penalty_note' } },
      { label: '지나친다', text: '아무 일도 일어나지 않는다', fx: {} }] },
];
