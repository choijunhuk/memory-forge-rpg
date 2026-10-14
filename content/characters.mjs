// Ten launch characters: all playable characters are women (6 adults, 4 teens aged 16-17); the player character
// (공방장) never fights. Card numbers are printed at the reference battle (power 40, 81 HP per character, skill Lv.1);
// account growth and stage level scale them through engine Stats (see content/progression.mjs).
import {
  card, D, B, H, ST, BUFF, CTR, DRAW, DISCARD, AP, GAUGE, CREATE, PULL, SLOT, GROW, GROWPICK,
  LOAN, DEFER, RESTRAINT, COLLATERAL, OVERLOAD, CLEANSE, EXPICK, EXHAND, TUNE, COSTDOWN, RECALL,
  COPYPICK, POWER, ECHO, NEXTBOOST, FREENEXT, CLEARDEBT, IF, C, per,
} from './helpers.mjs';

export const characters = [];
const ch = (o) => { characters.push(o); return o; };

// ───────────────────────── 1. 세라 ─────────────────────────
ch({
  id: 'sera', name: '세라', en: 'Sera', rarity: 'SSR', pool: 'standard', attr: 'volt', weapon: 'ranged',
  role: '예약 폭발 딜러', archetype: 'A 시간 예약', age: 24, tier: 'adult',
  oneLine: '미래의 한 발을 먼저 쏘아 두는 시간 저격수.',
  personality: '침착하고 건조한 농담을 한다. 미래는 보이는데 자기 과거는 군데군데 비어 있다.',
  storyHook: '사라진 기억 한 칸에 "쏘지 못한 한 발"이 남아 있다.',
  passive: { id: 'sera_foresight', name: '선견', params: { bps: 2500 },
    text: '세라의 예약 공격이 균열 상태인 적에게 명중하면 피해 +25%.' },
  ult: { name: '시간 관통탄', eff: [D(28, { brk: 4, pierce: 3000 }), SLOT(1, 0)],
    text: '적 1명에게 피해 28(관통 30%, 균열 4). 이번 턴 다음 턴 예약 칸 +1.' },
  resonance: [
    { n: 1, text: '유리탄이 예약으로 해결되면 카드 1장 뽑기(턴당 1회).', fx: { type: 'hook', id: 'sera_r1' } },
    { n: 2, text: '선견 보너스 +25% → +40%.', fx: { type: 'passiveParam', param: 'bps', value: 4000 } },
    { n: 3, text: '고유 카드 스킬 레벨 +2(피해·방어 ×1.08).', fx: { type: 'skillLv', delta: 2 } },
    { n: 4, text: '시한 포격 균열 +2. 기억 용량 +1.', fx: { type: 'multi', list: [
      { type: 'cardMod', cards: ['sera_timed'], prop: 'brk', add: 2 }, { type: 'capacity', n: 1 }] } },
    { n: 5, text: '필살기 레벨 +2(필살기 수치 ×1.08).', fx: { type: 'ultLv', delta: 2 } },
    { n: 6, text: '필살기를 쓴 다음 턴 시작에 해결되는 세라의 예약 공격 피해 +50%.', fx: { type: 'hook', id: 'sera_r6' } },
  ],
  visual: { height: 170, build: '늘씬한 성인 체형', hair: '긴 은백색 머리, 낮게 묶은 포니테일', eyes: '시계 바늘 무늬가 비치는 호박색 눈',
    outfit: '몸에 붙는 검은 바디수트 위에 앞을 연 황동 톱니 장식 남색 롱코트, 허벅지 홀스터, 손가락 없는 장갑', weapon: '모래시계 약실이 달린 장거리 저격총',
    motif: '모래시계·시계 바늘', palette: ['#1d2b4f', '#d9dde6', '#ffd84d'], expression: '차분하고 조용한 미소' },
  voice: '낮고 또렷한 톤, 짧은 문장',
});
card('sera_round', '유리탄', 'sera', { rar: 'B', cost: 1, type: 'atk', start: true, eff: [D(8, { rv: 12 })], flavor: '깨지기 쉬운 탄환일수록 정확해야 한다.' });
card('sera_cover', '엄폐 사격', 'sera', { rar: 'B', cost: 1, type: 'atk', start: true, eff: [B(5, { rv: 7 }), D(4, { rv: 6 })] });
card('sera_aim', '정조준', 'sera', { rar: 'B', cost: 1, type: 'skl', start: true, eff: [BUFF('charge', 4000, { turns: 2 })] });
card('sera_reload', '재장전', 'sera', { rar: 'B', cost: 0, type: 'skl', res: 'n', eff: [DRAW(2), DISCARD(1)] });
card('sera_timed', '시한 포격', 'sera', { rar: 'A', cost: 2, type: 'atk', res: 'o', start: true, eff: [D(16, { rv: 22, ra: 30, brk: 3 })], flavor: '기다린 만큼 무거워지는 탄.' });
card('sera_calc', '탄도 계산', 'sera', { rar: 'A', cost: 1, type: 'skl', res: 'n', eff: [SLOT(1, 0), DRAW(1)] });
card('sera_pull', '시간 당기기', 'sera', { rar: 'A', cost: 0, type: 'skl', res: 'n', uses: 2, eff: [PULL()] });
card('sera_double', '연속 저격', 'sera', { rar: 'A', cost: 1, type: 'atk', eff: [D(4, { rv: 6, hits: 2 }), IF(C.marked, [D(4, { rv: 6 })])] });
card('sera_phase', '위상 저격', 'sera', { rar: 'S', cost: 2, type: 'atk', eff: [D(16, { rv: 24, brk: 2, pierce: 5000 })] });
card('sera_lock', '미래 확정', 'sera', { rar: 'S', cost: 1, type: 'skl', res: 'n', eff: [BUFF('seraLock', 3000, { turns: 2 })] });
card('sera_flash', '섬광 탄막', 'sera', { rar: 'S', cost: 2, type: 'atk', eff: [D(7, { rv: 10, tgt: 'all' })] });
card('sera_last', '종언의 초침', 'sera', { rar: 'S', cost: 3, type: 'atk', res: 'after', kw: ['exhaust'], eff: [D(45, { ra: 45, brk: 5 })] });

// ───────────────────────── 2. 니아 ─────────────────────────
ch({
  id: 'nia', name: '니아', en: 'Nia', rarity: 'SR', pool: 'standard', attr: 'void', weapon: 'device',
  role: '전투 중 개조 서포터', archetype: 'C 카드 개조', age: 17, tier: 'teen', starter: true,
  oneLine: '싸우는 도중에도 카드를 뜯어고치는 공방 막내 엔지니어.',
  personality: '말이 많고 밝다. 남의 기억은 척척 고치면서 자기 기억은 열어 보지 않는다.',
  storyHook: '공방의 금고 하나에 "열지 말 것"이라고 쓴 자기 이름표가 붙어 있다.',
  passive: { id: 'nia_handiwork', name: '손재주', params: { bps: 1500 },
    text: '개조(임시 포함)가 붙은 카드의 피해·방어 +15%.' },
  ult: { name: '총괄 정비', eff: [COSTDOWN(1, 'all'), DRAW(1)], text: '이번 턴 손패의 모든 카드 비용 -1(최소 0). 카드 1장 뽑기.' },
  resonance: [
    { n: 1, text: '미세 조정 사용 가능 횟수 +1(전투당 3회).', fx: { type: 'cardMod', cards: ['nia_tune'], prop: 'uses', add: 1 } },
    { n: 2, text: '손재주 보너스 +15% → +25%.', fx: { type: 'passiveParam', param: 'bps', value: 2500 } },
    { n: 3, text: '고유 카드 스킬 레벨 +2(피해·방어 ×1.08).', fx: { type: 'skillLv', delta: 2 } },
    { n: 4, text: '전투 시작 시 손패의 카드 1장에 임시 회로 "증폭" 부착. 기억 용량 +1.', fx: { type: 'multi', list: [
      { type: 'battleStart', eff: [TUNE('amp', 'random')] }, { type: 'capacity', n: 1 }] } },
    { n: 5, text: '필살기 레벨 +2(필살기 수치 ×1.08).', fx: { type: 'ultLv', delta: 2 } },
    { n: 6, text: '필살기 사용 시 손패의 모든 카드에 "증폭" 부착.', fx: { type: 'ultExtra', eff: [TUNE('amp', 'all')] } },
  ],
  visual: { height: 158, build: '10대 후반 소녀', hair: '흰 브릿지가 들어간 짙은 청록색 단발, 부스스함', eyes: '보라빛이 도는 큰 눈',
    outfit: '허리에 묶은 오버사이즈 작업 재킷, 기름 얼룩 반팔 티셔츠, 카고 반바지, 공구 벨트, 작업 부츠', weapon: '기계식 만능 공구 건틀릿과 소형 드론',
    motif: '렌치·회로 기판', palette: ['#0f3d3e', '#111827', '#9b7bff'], expression: '장난스러운 웃음' },
  voice: '빠르고 높은 톤, 감탄사가 많다',
});
card('nia_wrench', '스패너 타격', 'nia', { rar: 'B', cost: 1, type: 'atk', start: true, eff: [D(7, { rv: 10 })] });
card('nia_barrier', '응급 차폐막', 'nia', { rar: 'B', cost: 1, type: 'skl', start: true, eff: [B(7, { rv: 10 })] });
card('nia_tune', '미세 조정', 'nia', { rar: 'B', cost: 0, type: 'skl', res: 'n', uses: 2, start: true, eff: [TUNE('amp', 'one')] });
card('nia_scan', '구조 분석', 'nia', { rar: 'B', cost: 1, type: 'skl', start: true, eff: [ST('vul', 2, { tgt: 'one' }), DRAW(1)] });
card('nia_drone', '보조 드론', 'nia', { rar: 'A', cost: 1, type: 'skl', eff: [CREATE('nia_drone_shot', 2, 'hand')] });
card('nia_overclock', '과열 개조', 'nia', { rar: 'A', cost: 1, type: 'skl', res: 'n', eff: [COSTDOWN(1, 'one'), TUNE('overheat', 'same')] });
card('nia_rebuild', '재조립', 'nia', { rar: 'A', cost: 1, type: 'skl', res: 'n', eff: [RECALL(), B(4)] });
card('nia_shock', '공허 방전', 'nia', { rar: 'A', cost: 2, type: 'atk', eff: [D(14, { rv: 20, brk: 2 }), IF(C.modded, [{ op: 'breakBonus', n: 2 }])] });
card('nia_blueprint', '설계도 전개', 'nia', { rar: 'S', cost: 1, type: 'skl', res: 'n', kw: ['exhaust'], eff: [POWER('blueprint', { bps: 2000 })] });
card('nia_assembly', '즉석 조립', 'nia', { rar: 'S', cost: 2, type: 'skl', res: 'n', kw: ['exhaust'], eff: [TUNE('amp', 'all')] });
card('nia_fieldlab', '이동 공방', 'nia', { rar: 'S', cost: 1, type: 'skl', eff: [DRAW(2, { rn: 3 })] });
card('nia_masterwork', '걸작', 'nia', { rar: 'S', cost: 3, type: 'atk', eff: [D(24, { rv: 34, brk: 3, scale: per('tempParts', 3, 18) })] });
card('nia_drone_shot', '드론 사격', 'nia', { rar: 'T', cost: 0, type: 'atk', res: 'n', kw: ['exhaust'], pool: false, eff: [D(4)] });

// ───────────────────────── 3. 유리 ─────────────────────────
ch({
  id: 'yuri', name: '유리', en: 'Yuri', rarity: 'SSR', pool: 'standard', attr: 'frost', weapon: 'ritual',
  role: '성장·진화 탱커/서포터', archetype: 'E 살아 있는 카드', age: 16, tier: 'teen',
  oneLine: '카드 속에서 자라는 생명을 동료라고 부르는 식물학자.',
  personality: '수줍고 다정하다. 사람보다 카드 속 작은 생물과 먼저 인사한다.',
  storyHook: '얼어붙은 온실에 아직 깨어나지 않은 씨앗 하나가 있다.',
  passive: { id: 'yuri_symbiosis', name: '공생 결속', params: { perGrowth: 2 },
    text: '성장 1 이상인 카드가 해결될 때 성장치 ×2 만큼 방어 추가.' },
  ult: { name: '만개', eff: [{ op: 'growLowest', n: 2 }, B(12)], text: '성장이 가장 낮은 성장 카드 2장 성장 +1(전투 상한과 별도). 방어 12.' },
  resonance: [
    { n: 1, text: '전투당 파티 성장 상한 3 → 4.', fx: { type: 'rule', key: 'growthCap', value: 4 } },
    { n: 2, text: '공생 결속 방어 성장×2 → ×3.', fx: { type: 'passiveParam', param: 'perGrowth', value: 3 } },
    { n: 3, text: '고유 카드 스킬 레벨 +2(피해·방어 ×1.08).', fx: { type: 'skillLv', delta: 2 } },
    { n: 4, text: '진화 카드의 피해·방어 +15%. 기억 용량 +1.', fx: { type: 'multi', list: [
      { type: 'hook', id: 'yuri_r4', bps: 1500 }, { type: 'capacity', n: 1 }] } },
    { n: 5, text: '필살기 레벨 +2(필살기 수치 ×1.08).', fx: { type: 'ultLv', delta: 2 } },
    { n: 6, text: '필살기 사용 시 덱의 모든 성장 카드 성장 +1(상한 무시, 전투당 1회).', fx: { type: 'ultExtra', eff: [{ op: 'growAll' }], once: true } },
  ],
  visual: { height: 155, build: '10대 소녀', hair: '끝이 투명한 유리처럼 비치는 연청색 긴 웨이브', eyes: '맑고 순한 눈매',
    outfit: '민소매 여름 원피스 위에 긴 식물학자 가운, 샌들, 허리에 서리 낀 유리병', weapon: '작은 생물이 빛나는 테라리움 등불 지팡이',
    motif: '유리 고치·새싹', palette: ['#bfe6ff', '#9de0c8', '#f4fbff'], expression: '수줍은 미소' },
  voice: '조용하고 부드러운 톤, 말끝을 흐린다',
});
card('yuri_cocoon', '유리 고치', 'yuri', { rar: 'B', cost: 1, type: 'skl', start: true, growth: true, evo: ['yuri_cocoon_guard', 'yuri_cocoon_prey'], eff: [B(6, { rv: 8 }), GROW()] });
card('yuri_sprout', '새싹 화살', 'yuri', { rar: 'B', cost: 1, type: 'atk', start: true, growth: true, evo: ['yuri_sprout_thorn', 'yuri_sprout_pollen'], eff: [D(6, { rv: 8 }), GROW()] });
card('yuri_root', '공생 뿌리', 'yuri', { rar: 'B', cost: 1, type: 'skl', start: true, growth: true, evo: ['yuri_root_net', 'yuri_root_hunt'], eff: [B(4, { rv: 6 }), DRAW(1), GROW()] });
card('yuri_dew', '이슬 맺기', 'yuri', { rar: 'B', cost: 0, type: 'skl', res: 'n', start: true, kw: ['exhaust'], eff: [GROWPICK(1)] });
card('yuri_greenhouse', '온실', 'yuri', { rar: 'A', cost: 1, type: 'skl', res: 'n', kw: ['exhaust'], eff: [POWER('greenhouse', { blk: 3 })] });
card('yuri_frostbloom', '서리꽃', 'yuri', { rar: 'A', cost: 2, type: 'atk', growth: true, evo: ['yuri_frostbloom_ice', 'yuri_frostbloom_snow'], eff: [D(10, { rv: 14, tgt: 'all' }), GROW()] });
card('yuri_tend', '돌봄', 'yuri', { rar: 'A', cost: 1, type: 'skl', eff: [H(6, { rv: 8 }), IF(C.growthInHand(2), [DRAW(1)])] });
card('yuri_mutual', '상리공생', 'yuri', { rar: 'A', cost: 1, type: 'atk', eff: [D(7, { rv: 10, scale: per('growthSum', 1, 12) })] });
card('yuri_metamorph', '탈피', 'yuri', { rar: 'S', cost: 1, type: 'skl', res: 'n', eff: [COPYPICK('growth')] });
card('yuri_canopy', '수관', 'yuri', { rar: 'S', cost: 2, type: 'skl', eff: [B(14, { rv: 20, scale: per('growthCards', 2, 10) })] });
card('yuri_seedvault', '씨앗 금고', 'yuri', { rar: 'S', cost: 1, type: 'skl', growth: true, evo: ['yuri_seedvault_plenty', 'yuri_seedvault_fruit'], eff: [DRAW(1, { rn: 2 }), GROW()] });
card('yuri_worldtree', '세계수', 'yuri', { rar: 'S', cost: 3, type: 'skl', res: 'n', kw: ['exhaust'], eff: [{ op: 'growAll' }, B(15)] });
// evolution branches (granted at the battle-reward screen when growth reaches 3)
card('yuri_cocoon_guard', '유리 고치: 수호', 'yuri', { rar: 'S', cost: 1, type: 'skl', pool: false, eff: [B(9, { rv: 13 })], flavor: '수호형 진화' });
card('yuri_cocoon_prey', '유리 고치: 포식', 'yuri', { rar: 'S', cost: 1, type: 'atk', pool: false, eff: [D(9, { rv: 12, brk: 2 }), IF({ c: 'killedThis' }, [DRAW(1)])], flavor: '포식형 진화' });
card('yuri_sprout_thorn', '새싹 화살: 가시덤불', 'yuri', { rar: 'S', cost: 1, type: 'atk', pool: false, eff: [D(5, { rv: 7, hits: 2 })] });
card('yuri_sprout_pollen', '새싹 화살: 꽃가루', 'yuri', { rar: 'S', cost: 1, type: 'atk', pool: false, eff: [D(7, { rv: 10 }), ST('weak', 1, { tgt: 'same' })] });
card('yuri_root_net', '공생 뿌리: 보호망', 'yuri', { rar: 'S', cost: 1, type: 'skl', pool: false, eff: [B(8, { rv: 11 }), DRAW(1)] });
card('yuri_root_hunt', '공생 뿌리: 사냥망', 'yuri', { rar: 'S', cost: 1, type: 'skl', pool: false, eff: [DRAW(2), ST('mark', 1, { tgt: 'rand' })] });
card('yuri_frostbloom_ice', '서리꽃: 빙화', 'yuri', { rar: 'S', cost: 2, type: 'atk', pool: false, eff: [D(13, { rv: 18, tgt: 'all' })] });
card('yuri_frostbloom_snow', '서리꽃: 만년설', 'yuri', { rar: 'S', cost: 2, type: 'atk', pool: false, eff: [D(10, { rv: 14, tgt: 'all' }), ST('weak', 1, { tgt: 'all' })] });
card('yuri_seedvault_plenty', '씨앗 금고: 풍요', 'yuri', { rar: 'S', cost: 1, type: 'skl', pool: false, eff: [DRAW(2, { rn: 3 })] });
card('yuri_seedvault_fruit', '씨앗 금고: 결실', 'yuri', { rar: 'S', cost: 1, type: 'skl', pool: false, eff: [DRAW(1, { rn: 2 }), AP(1)] });

// ───────────────────────── 4. 라비엔 ─────────────────────────
ch({
  id: 'ravien', name: '라비엔', en: 'Ravien', rarity: 'SSR', pool: 'standard', attr: 'flame', weapon: 'device',
  role: '계약·자원 운용 딜러', archetype: 'F 차입·상환', age: 28, tier: 'adult',
  oneLine: '미래의 행동력을 빌려 오늘 이기는 계약 중개인.',
  personality: '여유롭고 짓궂지만 약속에는 누구보다 엄격하다.',
  storyHook: '그녀 자신도 아직 갚지 못한 계약서 한 장을 품고 다닌다.',
  passive: { id: 'ravien_credit', name: '신용', params: { bps: 2000 },
    text: '부채가 있는 동안 라비엔의 공격 피해 +20%.' },
  ult: { name: '채무 탕감', eff: [CLEARDEBT(), D(6, { tgt: 'all', scale: per('lastCount', 6, 30) }), D(6, { tgt: 'all' })],
    text: '모든 부채를 없애고, 없앤 부채 1당 모든 적에게 피해 6, 이후 모든 적에게 피해 6.' },
  resonance: [
    { n: 1, text: '선불 계약을 전투당 2회 사용 가능.', fx: { type: 'cardMod', cards: ['ravien_advance'], prop: 'uses', add: 1 } },
    { n: 2, text: '신용 보너스 +20% → +35%.', fx: { type: 'passiveParam', param: 'bps', value: 3500 } },
    { n: 3, text: '고유 카드 스킬 레벨 +2(피해·방어 ×1.08).', fx: { type: 'skillLv', delta: 2 } },
    { n: 4, text: '부채 상한 4 → 5. 기억 용량 +1.', fx: { type: 'multi', list: [{ type: 'rule', key: 'debtCap', value: 5 }, { type: 'capacity', n: 1 }] } },
    { n: 5, text: '필살기 레벨 +2(필살기 수치 ×1.08).', fx: { type: 'ultLv', delta: 2 } },
    { n: 6, text: '필살기를 쓰면 다음 턴 행동력 +1.', fx: { type: 'ultExtra', eff: [{ op: 'apNext', n: 1 }] } },
  ],
  visual: { height: 175, build: '우아하고 볼륨감 있는 성인 체형', hair: '웨이브 진 와인빛 긴 머리', eyes: '날카로운 초록 눈',
    outfit: '가슴선이 깊게 파인 검은 베스트, 트임이 깊은 펜슬 스커트, 가터 스타킹, 느슨하게 맨 붉은 넥타이, 긴 검은 장갑', weapon: '황금 저울 펜던트와 불타는 봉인의 계약서',
    motif: '저울·밀랍 인장', palette: ['#8c1c2b', '#141014', '#d4a24c'], expression: '여유로운 미소' },
  voice: '낮고 느긋한 톤, 존댓말 속 장난기',
});
card('ravien_advance', '선불 계약', 'ravien', { rar: 'B', cost: 0, type: 'skl', res: 'n', uses: 1, start: true, kw: ['contract'], eff: [LOAN(2)] });
card('ravien_strike', '인장 타격', 'ravien', { rar: 'B', cost: 1, type: 'atk', start: true, eff: [D(8, { rv: 11 }), IF(C.debt, [ST('burn', 2, { tgt: 'same' })])] });
card('ravien_restraint', '절제 계약', 'ravien', { rar: 'B', cost: 0, type: 'skl', res: 'n', start: true, kw: ['contract'], eff: [RESTRAINT()] });
card('ravien_collateral', '담보 계약', 'ravien', { rar: 'B', cost: 0, type: 'skl', res: 'n', start: true, kw: ['contract'], eff: [COLLATERAL(16)] });
card('ravien_defer', '유예 협상', 'ravien', { rar: 'A', cost: 0, type: 'skl', res: 'n', kw: ['contract'], eff: [DEFER()] });
card('ravien_interest', '이자 징수', 'ravien', { rar: 'A', cost: 1, type: 'atk', eff: [D(6, { rv: 9, scale: per('debt', 4, 20) })] });
card('ravien_seal', '불꽃 인장', 'ravien', { rar: 'A', cost: 1, type: 'skl', eff: [ST('burn', 4, { rn: 6, tgt: 'one' })] });
card('ravien_penalty', '위약 조항', 'ravien', { rar: 'A', cost: 1, type: 'atk', eff: [D(10, { rv: 14, brk: 2 }), IF(C.burning, [{ op: 'breakBonus', n: 1 }])] });
card('ravien_bigloan', '대출 한도', 'ravien', { rar: 'S', cost: 1, type: 'skl', res: 'n', uses: 1, kw: ['contract'], eff: [LOAN(3)] });
card('ravien_auction', '경매', 'ravien', { rar: 'S', cost: 2, type: 'atk', eff: [D(18, { rv: 25, brk: 2, scale: per('targetBurn', 1, 20) })] });
card('ravien_goldscale', '황금 저울질', 'ravien', { rar: 'S', cost: 1, type: 'skl', res: 'n', eff: [DRAW(0, { scale: per('contracts', 1, 3) })] });
card('ravien_settlement', '최종 결산', 'ravien', { rar: 'S', cost: 3, type: 'atk', eff: [D(14, { rv: 20, tgt: 'all', brk: 2 }), { op: 'breakBonusAll', scale: per('debt', 1, 4) }] });

// ───────────────────────── 5. 하린 ─────────────────────────
ch({
  id: 'harin', name: '하린', en: 'Harin', rarity: 'SR', pool: 'standard', attr: 'frost', weapon: 'melee',
  role: '방어·반격 탱커', archetype: '방어 반격', age: 26, tier: 'adult', starter: true,
  oneLine: '방패를 펼치면 칼날이 되는 경계 수호대장.',
  personality: '무뚝뚝하고 농담이 서툴다. 귀여운 것에 약한 걸 숨긴다.',
  storyHook: '지키지 못한 경계선 하나를 매일 밤 다시 걷는다.',
  passive: { id: 'harin_stance', name: '반격 자세', params: { n: 1 },
    text: '하린의 카드로 방어를 얻을 때마다 반격 1(적의 공격 1회마다 그 적에게 피해).' },
  ult: { name: '빙벽', eff: [B(30), CTR(6)], text: '방어 30, 반격 6.' },
  resonance: [
    { n: 1, text: '반격 태세의 반격 +2.', fx: { type: 'cardMod', cards: ['harin_counter'], prop: 'n', add: 2 } },
    { n: 2, text: '반격 자세 획득량 1 → 2.', fx: { type: 'passiveParam', param: 'n', value: 2 } },
    { n: 3, text: '고유 카드 스킬 레벨 +2(피해·방어 ×1.08).', fx: { type: 'skillLv', delta: 2 } },
    { n: 4, text: '전투 시작 시 방어 10. 기억 용량 +1.', fx: { type: 'multi', list: [{ type: 'battleStart', eff: [B(10)] }, { type: 'capacity', n: 1 }] } },
    { n: 5, text: '필살기 레벨 +2(필살기 수치 ×1.08).', fx: { type: 'ultLv', delta: 2 } },
    { n: 6, text: '필살기 후 2턴 동안 반격 피해 2배.', fx: { type: 'ultExtra', eff: [BUFF('counterDouble', 1, { turns: 2 })] } },
  ],
  visual: { height: 180, build: '키 크고 탄탄한 성인 체형', hair: '짧은 짙은 남색 머리', eyes: '왼쪽 눈썹 위 흉터, 회청색 눈',
    outfit: '어깨와 허리 라인이 드러나는 경량 서리빛 갑주, 한쪽 어깨에 두른 긴 망토, 하이웨이스트 전투 바지', weapon: '펼치면 칼날이 되는 대형 방패',
    motif: '서리 결정·성벽', palette: ['#eef6ff', '#5b7ea6', '#1b2a3d'], expression: '무표정, 가끔 서툰 미소' },
  voice: '낮고 단단한 톤',
});
card('harin_shield', '경계 방패', 'harin', { rar: 'B', cost: 1, type: 'skl', start: true, eff: [B(8, { rv: 11 })] });
card('harin_bash', '방패 강타', 'harin', { rar: 'B', cost: 1, type: 'atk', start: true, eff: [D(6, { rv: 9, brk: 2 })] });
card('harin_counter', '반격 태세', 'harin', { rar: 'B', cost: 1, type: 'skl', start: true, eff: [B(5, { rv: 7 }), CTR(3)] });
card('harin_taunt', '도발', 'harin', { rar: 'B', cost: 0, type: 'skl', res: 'n', start: true, eff: [IF(C.intentAttack, [ST('weak', 1, { tgt: 'one' })]), B(3)] });
card('harin_frostwall', '서리 장벽', 'harin', { rar: 'A', cost: 2, type: 'skl', eff: [B(14, { rv: 20 })] });
card('harin_retaliate', '보복', 'harin', { rar: 'A', cost: 1, type: 'atk', res: 'n', eff: [D(0, { scale: per('block', 1, 30) })] });
card('harin_hold', '버티기', 'harin', { rar: 'A', cost: 1, type: 'skl', eff: [B(6, { rv: 13 })] });
card('harin_iceguard', '빙결 수호', 'harin', { rar: 'A', cost: 1, type: 'skl', eff: [B(7, { rv: 10 }), ST('weak', 1, { tgt: 'one' })] });
card('harin_bulwark', '불굴', 'harin', { rar: 'S', cost: 2, type: 'skl', res: 'n', kw: ['exhaust'], eff: [POWER('bulwark', { keep: 10 })] });
card('harin_glacier', '빙하 압박', 'harin', { rar: 'S', cost: 2, type: 'atk', eff: [D(10, { rv: 14, brk: 2, scale: per('counter', 2, 20) })] });
card('harin_oath', '수호 맹세', 'harin', { rar: 'S', cost: 1, type: 'skl', eff: [B(10, { rv: 14 }), CTR(5)] });
card('harin_avalanche', '눈사태', 'harin', { rar: 'S', cost: 3, type: 'atk', eff: [D(12, { rv: 16, tgt: 'all', brk: 2 }), B(12)] });

// ───────────────────────── 6. 리엔 ─────────────────────────
ch({
  id: 'rien', name: '리엔', en: 'Rien', rarity: 'SR', pool: 'standard', attr: 'volt', weapon: 'ritual',
  role: '즉시 연쇄 서브딜러', archetype: '잔향 연쇄', age: 17, tier: 'teen', starter: true,
  oneLine: '침묵을 싫어하는 거리의 바이올리니스트.',
  personality: '에너지가 넘치고 수다스럽다. 조용해지면 불안해서 일부러 소리를 낸다.',
  storyHook: '그녀가 연주하는 곡 중 한 곡만은 끝까지 기억나지 않는다.',
  passive: { id: 'rien_echo', name: '잔향', params: { max: 3 },
    text: '리엔이 편성되어 있으면, 직전 카드와 다른 캐릭터의 카드를 낼 때마다 잔향 +1(최대 3, 턴 시작 시 0).' },
  ult: { name: '앙코르', eff: [FREENEXT(2), ECHO(3)], text: '이번 턴 다음에 내는 카드 2장 비용 0. 잔향 +3.' },
  resonance: [
    { n: 1, text: '첫 음 피해 +3.', fx: { type: 'cardMod', cards: ['rien_note'], prop: 'dmg', add: 3 } },
    { n: 2, text: '잔향 최대 3 → 4.', fx: { type: 'passiveParam', param: 'max', value: 4 } },
    { n: 3, text: '고유 카드 스킬 레벨 +2(피해·방어 ×1.08).', fx: { type: 'skillLv', delta: 2 } },
    { n: 4, text: '턴 시작 시 잔향 1. 기억 용량 +1.', fx: { type: 'multi', list: [{ type: 'rule', key: 'echoStart', value: 1 }, { type: 'capacity', n: 1 }] } },
    { n: 5, text: '필살기 레벨 +2(필살기 수치 ×1.08).', fx: { type: 'ultLv', delta: 2 } },
    { n: 6, text: '앙코르의 비용 0 카드 2장 → 3장.', fx: { type: 'ultReplace', eff: [FREENEXT(3), ECHO(3)] } },
  ],
  visual: { height: 160, build: '10대 후반 소녀', hair: '분홍빛 금발 트윈테일, 끝으로 갈수록 그라데이션', eyes: '노란빛 생기 있는 눈',
    outfit: '음파 무늬 오버사이즈 후디, 플리츠 스커트와 검은 레깅스, 하이탑 운동화, 헤드폰', weapon: '현이 빛나는 전자 바이올린',
    motif: '음표·파형', palette: ['#ffd84d', '#ff4fa3', '#ffffff'], expression: '활짝 웃는 얼굴' },
  voice: '밝고 높은 톤, 리듬감 있는 말투',
});
card('rien_note', '첫 음', 'rien', { rar: 'B', cost: 0, type: 'atk', res: 'n', start: true, eff: [D(4), ECHO(1)] });
card('rien_chord', '화음', 'rien', { rar: 'B', cost: 1, type: 'atk', start: true, eff: [D(6, { rv: 8, scale: per('echo', 3, 12) })] });
card('rien_rest', '쉼표', 'rien', { rar: 'B', cost: 1, type: 'skl', start: true, eff: [B(6, { rv: 8 }), IF(C.echo(1), [DRAW(1)])] });
card('rien_tempo', '템포 업', 'rien', { rar: 'B', cost: 0, type: 'skl', res: 'n', start: true, uses: 2, eff: [DRAW(1), ECHO(1)] });
card('rien_crescendo', '크레셴도', 'rien', { rar: 'A', cost: 1, type: 'atk', res: 'n', eff: [D(5, { hits: 1, hitsScale: per('echo', 1, 4) })] });
card('rien_duet', '이중주', 'rien', { rar: 'A', cost: 1, type: 'skl', res: 'n', eff: [NEXTBOOST(5000)] });
card('rien_feedback', '피드백', 'rien', { rar: 'A', cost: 1, type: 'atk', eff: [D(5, { rv: 7, tgt: 'all' }), IF(C.echo(3), [AP(1)])] });
card('rien_harmony', '하모니', 'rien', { rar: 'A', cost: 1, type: 'skl', res: 'n', eff: [H(4, { scale: per('echo', 2, 8) })] });
card('rien_solo', '독주', 'rien', { rar: 'S', cost: 2, type: 'atk', res: 'n', eff: [D(10, { brk: 2, scale: per('echo', 6, 24) }), { op: 'breakBonus', scale: per('echo', 1, 4) }] });
card('rien_echochamber', '메아리 방', 'rien', { rar: 'S', cost: 1, type: 'skl', res: 'n', kw: ['exhaust'], eff: [POWER('echochamber', {})] });
card('rien_amplify', '앰프', 'rien', { rar: 'S', cost: 0, type: 'skl', res: 'n', kw: ['exhaust'], eff: [ECHO(2)] });
card('rien_finale', '피날레', 'rien', { rar: 'S', cost: 3, type: 'atk', res: 'n', kw: ['exhaust'], eff: [D(10, { tgt: 'all', hits: 1, hitsScale: per('echo', 1, 4) })] });

// ───────────────────────── 7. 에일라 ─────────────────────────
ch({
  id: 'aila', name: '에일라', en: 'Aila', rarity: 'SR', pool: 'standard', attr: 'frost', weapon: 'ritual',
  role: '회복·정화 힐러', archetype: '회복 전환', age: 23, tier: 'adult',
  oneLine: '따뜻한 기억만 골라 보관하는 기록 보관소의 관리인.',
  personality: '조용하고 다정하지만 사람을 지킬 때는 단호하다.',
  storyHook: '보관소 맨 아래 칸에 자기 기억만 비어 있는 서랍이 있다.',
  passive: { id: 'aila_warmth', name: '온기의 기록', params: { pct: 50 },
    text: '최대 체력을 넘는 회복량의 50%를 방어로 얻는다.' },
  ult: { name: '새벽 종', eff: [H(20), CLEANSE(), B(10)], text: '회복 20, 해로운 효과 모두 제거, 방어 10.' },
  resonance: [
    { n: 1, text: '맑은 종 회복 +2.', fx: { type: 'cardMod', cards: ['aila_bell'], prop: 'heal', add: 2 } },
    { n: 2, text: '온기의 기록 50% → 80%.', fx: { type: 'passiveParam', param: 'pct', value: 80 } },
    { n: 3, text: '고유 카드 스킬 레벨 +2(피해·방어 ×1.08).', fx: { type: 'skillLv', delta: 2 } },
    { n: 4, text: '전투 시작 시 회복 10. 기억 용량 +1.', fx: { type: 'multi', list: [{ type: 'battleStart', eff: [H(10)] }, { type: 'capacity', n: 1 }] } },
    { n: 5, text: '필살기 레벨 +2(필살기 수치 ×1.08).', fx: { type: 'ultLv', delta: 2 } },
    { n: 6, text: '필살기의 초과 회복은 100% 방어로 전환.', fx: { type: 'hook', id: 'aila_r6' } },
  ],
  visual: { height: 168, build: '단정한 성인 체형', hair: '라벤더빛 은발 긴 땋은 머리', eyes: '부드러운 보랏빛 눈',
    outfit: '등이 깊게 드러나는 서리 자수 기록관 로브, 넓은 소매, 옆트임 치마', weapon: '은빛 종이 달린 작은 등불',
    motif: '종·눈송이', palette: ['#c9b8ff', '#ffffff', '#a8d8ff'], expression: '다정한 미소' },
  voice: '차분하고 따뜻한 톤',
});
card('aila_bell', '맑은 종', 'aila', { rar: 'B', cost: 1, type: 'skl', start: true, eff: [H(5, { rv: 7 })] });
card('aila_frostlight', '서리빛', 'aila', { rar: 'B', cost: 1, type: 'atk', start: true, eff: [D(7, { rv: 10 })] });
card('aila_veil', '장막', 'aila', { rar: 'B', cost: 1, type: 'skl', start: true, eff: [B(7, { rv: 10 }), { op: 'cleanseOne', s: 'weak' }] });
card('aila_prayer', '기도문', 'aila', { rar: 'B', cost: 0, type: 'skl', res: 'n', start: true, eff: [BUFF('healUp', 5000, { turns: 1 }), DRAW(1)] });
card('aila_cleanse', '정화', 'aila', { rar: 'A', cost: 1, type: 'skl', res: 'n', eff: [CLEANSE(), B(4, { scale: per('lastCount', 4, 16) })] });
card('aila_mend', '봉합', 'aila', { rar: 'A', cost: 1, type: 'skl', eff: [H(8, { rv: 12 })] });
card('aila_snowfall', '눈송이', 'aila', { rar: 'A', cost: 1, type: 'atk', eff: [D(4, { rv: 6, tgt: 'all' }), ST('weak', 1, { tgt: 'all' })] });
card('aila_sanctuary', '성역', 'aila', { rar: 'A', cost: 2, type: 'skl', eff: [B(12, { rv: 17 }), H(4)] });
card('aila_lullaby', '자장가', 'aila', { rar: 'S', cost: 1, type: 'skl', res: 'n', eff: [ST('weak', 2, { tgt: 'one' }), H(4)] });
card('aila_rebirth', '재생의 기록', 'aila', { rar: 'S', cost: 2, type: 'skl', res: 'n', kw: ['exhaust'], eff: [POWER('rebirth', { heal: 3 })] });
card('aila_overflow', '넘치는 온기', 'aila', { rar: 'S', cost: 1, type: 'skl', eff: [H(10, { rv: 14, overflowBlock: 100 })] });
card('aila_dawnhymn', '새벽 성가', 'aila', { rar: 'S', cost: 3, type: 'skl', res: 'n', kw: ['exhaust'], eff: [H(15), B(15), D(8, { tgt: 'all' })] });

// ───────────────────────── 8. 시온 ─────────────────────────
ch({
  id: 'sion', name: '시온', en: 'Sion', rarity: 'SSR', pool: 'standard', attr: 'void', weapon: 'melee',
  role: '소멸 활용 딜러', archetype: '망각(덱 압축)', age: 27, tier: 'adult',
  oneLine: '사람을 지키려고 기억을 지우는 금서고의 사서.',
  personality: '냉정하고 말수가 적다. 지운 기억의 무게를 혼자 짊어진다.',
  storyHook: '그녀가 처음 지운 기억은 자기 자신에 관한 것이었다.',
  passive: { id: 'sion_shelf', name: '망각의 서가', params: { perBps: 400, maxBps: 4000 },
    text: '이번 전투에서 소멸한 카드 1장당 시온의 공격 피해 +4%(최대 +40%).' },
  ult: { name: '백지화', eff: [EXHAND(), D(7, { tgt: 'all', hits: 0, hitsScale: per('lastCount', 1, 10) })],
    text: '손패를 모두 소멸시키고, 소멸한 카드 1장당 모든 적에게 피해 7.' },
  resonance: [
    { n: 1, text: '지우개가 카드 2장 뽑기.', fx: { type: 'cardMod', cards: ['sion_erase'], prop: 'draw', add: 1 } },
    { n: 2, text: '망각의 서가 최대 +40% → +60%.', fx: { type: 'passiveParam', param: 'maxBps', value: 6000 } },
    { n: 3, text: '고유 카드 스킬 레벨 +2(피해·방어 ×1.08).', fx: { type: 'skillLv', delta: 2 } },
    { n: 4, text: '전투 시작 시 소멸 수 +3으로 계산. 기억 용량 +1.', fx: { type: 'multi', list: [{ type: 'rule', key: 'exhaustStart', value: 3 }, { type: 'capacity', n: 1 }] } },
    { n: 5, text: '필살기 레벨 +2(필살기 수치 ×1.08).', fx: { type: 'ultLv', delta: 2 } },
    { n: 6, text: '백지화 후 소멸한 수만큼 카드 뽑기(최대 5).', fx: { type: 'ultExtra', eff: [DRAW(0, { scale: per('lastCount', 1, 5) })] } },
  ],
  visual: { height: 172, build: '늘씬하고 차가운 인상의 성인 체형', hair: '허리까지 오는 곧은 흑발', eyes: '반무테 안경 너머 은회색 눈',
    outfit: '책갈피 사슬이 달린 몸에 맞는 짙은 보라 롱코트, 하이웨이스트 미니스커트, 사이하이 부츠', weapon: '거대한 책갈피 모양의 장검, 주변에 떠다니는 찢긴 페이지',
    motif: '책갈피·찢긴 페이지', palette: ['#3b2a5a', '#0e0b14', '#c7ccd6'], expression: '무심한 눈빛' },
  voice: '낮고 건조한 톤',
});
card('sion_cut', '책갈피 베기', 'sion', { rar: 'B', cost: 1, type: 'atk', start: true, eff: [D(8, { rv: 11 })] });
card('sion_erase', '지우개', 'sion', { rar: 'B', cost: 0, type: 'skl', res: 'n', start: true, eff: [EXPICK('hand', 1), DRAW(1)] });
card('sion_bind', '봉인', 'sion', { rar: 'B', cost: 1, type: 'skl', start: true, eff: [B(7, { rv: 10 })] });
card('sion_void', '공허 파편', 'sion', { rar: 'B', cost: 1, type: 'atk', start: true, kw: ['exhaust'], eff: [D(12, { rv: 16, brk: 2 })] });
card('sion_forget', '망각', 'sion', { rar: 'A', cost: 1, type: 'skl', res: 'n', eff: [EXPICK('discard', 2), B(5, { scale: per('lastCount', 5, 10), base0: true })] });
card('sion_index', '색인', 'sion', { rar: 'A', cost: 1, type: 'skl', eff: [DRAW(2, { rn: 3 })] });
card('sion_pageblade', '페이지 칼날', 'sion', { rar: 'A', cost: 1, type: 'atk', eff: [D(5, { rv: 7, scale: per('exhausted', 2, 16) })] });
card('sion_silence', '침묵의 장', 'sion', { rar: 'A', cost: 2, type: 'skl', res: 'n', eff: [ST('weak', 2, { tgt: 'one' }), ST('vul', 2, { tgt: 'same' })] });
card('sion_requiem', '레퀴엠', 'sion', { rar: 'S', cost: 2, type: 'atk', eff: [D(16, { rv: 22, brk: 3 })], costIf: { cond: C.exhaustedAtLeast(5), delta: -1 } });
card('sion_oblivion', '망각의 서', 'sion', { rar: 'S', cost: 1, type: 'skl', res: 'n', kw: ['exhaust'], eff: [POWER('oblivion', { blk: 3 })] });
card('sion_burnbook', '금서 소각', 'sion', { rar: 'S', cost: 0, type: 'atk', res: 'n', eff: [EXHAND(), D(6, { tgt: 'rand', hits: 0, hitsScale: per('lastCount', 1, 10) })] });
card('sion_lastpage', '마지막 페이지', 'sion', { rar: 'S', cost: 3, type: 'atk', kw: ['exhaust'], eff: [D(30, { rv: 40, brk: 4 })] });

// ───────────────────────── 9. 카야 ─────────────────────────
ch({
  id: 'kaya', name: '카야', en: 'Kaya', rarity: 'SSR', pool: 'limited', attr: 'flame', weapon: 'ranged',
  role: '표식 처형 딜러', archetype: '표식 추리', age: 17, tier: 'teen', limitedSeason: 1, limitedPhase: 'D22',
  oneLine: '잃어버린 기억을 쫓는 자칭 명탐정 소녀.',
  personality: '자신만만하고 승부욕이 강하다. "범인은 바로—!"를 외치고 싶어 한다.',
  storyHook: '그녀가 쫓는 마지막 사건의 의뢰인은 다름 아닌 공방장이다.',
  passive: { id: 'kaya_deduction', name: '추리', params: {},
    text: '표식이 있는 적을 처치하면 행동력 +1, 카드 1장 뽑기(턴당 1회).' },
  ult: { name: '결정적 증거', eff: [ST('mark', 3, { tgt: 'all' }), D(15, { tgt: 'all', brk: 2 })], text: '모든 적에게 표식 3을 남기고 피해 15(균열 2).' },
  resonance: [
    { n: 1, text: '단서 포착 표식 +1.', fx: { type: 'cardMod', cards: ['kaya_clue'], prop: 'n', add: 1 } },
    { n: 2, text: '추리 발동 시 무작위 적에게 화상 3.', fx: { type: 'hook', id: 'kaya_r2' } },
    { n: 3, text: '고유 카드 스킬 레벨 +2(피해·방어 ×1.08).', fx: { type: 'skillLv', delta: 2 } },
    { n: 4, text: '전투 시작 시 무작위 적 표식 2. 기억 용량 +1.', fx: { type: 'multi', list: [{ type: 'battleStart', eff: [ST('mark', 2, { tgt: 'rand' })] }, { type: 'capacity', n: 1 }] } },
    { n: 5, text: '필살기 레벨 +2(필살기 수치 ×1.08).', fx: { type: 'ultLv', delta: 2 } },
    { n: 6, text: '결정적 증거로 적을 처치하면 필살기 게이지 50 환급.', fx: { type: 'hook', id: 'kaya_r6' } },
  ],
  visual: { height: 161, build: '10대 후반 소녀', hair: '적갈색 옆으로 땋은 머리', eyes: '자신감 있는 주황빛 눈, 돋보기 모노클',
    outfit: '깃털 장식 탐정 모자, 짧은 망토, 반팔 셔츠와 조끼, 반바지, 롱부츠, 반장갑', weapon: '달아오른 볼트를 쏘는 석궁',
    motif: '돋보기·지문', palette: ['#ff7a3d', '#6b3f22', '#f7ead3'], expression: '의기양양한 미소' },
  voice: '또랑또랑하고 극적인 톤',
});
card('kaya_bolt', '화염 볼트', 'kaya', { rar: 'B', cost: 1, type: 'atk', start: true, eff: [D(8, { rv: 11 })] });
card('kaya_clue', '단서 포착', 'kaya', { rar: 'B', cost: 0, type: 'skl', res: 'n', start: true, eff: [ST('mark', 2, { tgt: 'one' })] });
card('kaya_deduce', '추리 사격', 'kaya', { rar: 'B', cost: 1, type: 'atk', start: true, eff: [D(12, { rv: 16, brk: 2, tgt: 'fixed' })] });
card('kaya_cover', '연막', 'kaya', { rar: 'B', cost: 1, type: 'skl', start: true, eff: [B(7, { rv: 10 })] });
card('kaya_tracking', '추적', 'kaya', { rar: 'A', cost: 1, type: 'skl', eff: [ST('mark', 3, { tgt: 'one' }), ST('burn', 3, { tgt: 'same' })] });
card('kaya_crossfire', '교차 사격', 'kaya', { rar: 'A', cost: 2, type: 'atk', eff: [D(7, { rv: 10, hits: 2 }), IF(C.marked, [D(7, { rv: 10 })])] });
card('kaya_interrogate', '심문', 'kaya', { rar: 'A', cost: 1, type: 'skl', res: 'n', eff: [ST('vul', 2, { tgt: 'fixed' }), DRAW(1)] });
card('kaya_ignite', '점화 볼트', 'kaya', { rar: 'A', cost: 1, type: 'atk', eff: [D(6, { rv: 9 }), ST('burn', 3, { tgt: 'same' }), IF(C.marked, [ST('burn', 3, { tgt: 'same' })])] });
card('kaya_verdict', '판결', 'kaya', { rar: 'S', cost: 2, type: 'atk', eff: [D(20, { rv: 28, brk: 3, tgt: 'fixed', mult: [{ if: C.targetHpBelow(30), x: 2 }] })] });
card('kaya_casefile', '사건 파일', 'kaya', { rar: 'S', cost: 1, type: 'skl', res: 'n', kw: ['exhaust'], eff: [POWER('casefile', { bps: 2000 })] });
card('kaya_flare', '조명탄', 'kaya', { rar: 'S', cost: 1, type: 'skl', res: 'n', eff: [ST('mark', 2, { tgt: 'all' }), DRAW(1)] });
card('kaya_closecase', '사건 종결', 'kaya', { rar: 'S', cost: 3, type: 'atk', kw: ['exhaust'], eff: [D(36, { rv: 48, brk: 5, tgt: 'fixed' }), IF({ c: 'killedThis' }, [AP(2)])] });

// ───────────────────────── 10. 루미 ─────────────────────────
ch({
  id: 'lumi', name: '루미', en: 'Lumi', rarity: 'SSR', pool: 'limited', attr: 'volt', weapon: 'melee',
  role: '체력 대가 폭딜러', archetype: '과부하', age: 22, tier: 'adult', limitedSeason: 1, limitedPhase: 'D1',
  oneLine: '시간보다 빨리 달리던 전직 퀵서비스 라이더.',
  personality: '무모하고 밝다. 위험할수록 웃는다.',
  storyHook: '배달하지 못한 마지막 소포가 아직 그녀의 가방에 있다.',
  passive: { id: 'lumi_critical', name: '임계 질주', params: { bps: 3000, pct: 50 },
    text: '파티 체력이 50% 이하면 루미의 공격 피해 +30%.' },
  ult: { name: '번개 질주', eff: [D(8, { tgt: 'rand', hits: 5, brk: 1 }), IF(C.hpBelow(50), [D(8, { tgt: 'rand', hits: 2, brk: 1 })])],
    text: '무작위 적에게 피해 8을 5회. 파티 체력 50% 이하면 2회 추가.' },
  resonance: [
    { n: 1, text: '질주의 과부하 3 → 1.', fx: { type: 'cardMod', cards: ['lumi_dash'], prop: 'selfdmg', add: -2 } },
    { n: 2, text: '임계 질주 +30% → +45%, 발동 기준 50% → 60%.', fx: { type: 'multi', list: [
      { type: 'passiveParam', param: 'bps', value: 4500 }, { type: 'passiveParam', param: 'pct', value: 60 }] } },
    { n: 3, text: '고유 카드 스킬 레벨 +2(피해·방어 ×1.08).', fx: { type: 'skillLv', delta: 2 } },
    { n: 4, text: '전투마다 첫 과부하의 체력 대가 0. 기억 용량 +1.', fx: { type: 'multi', list: [{ type: 'rule', key: 'firstOverloadFree', value: true }, { type: 'capacity', n: 1 }] } },
    { n: 5, text: '필살기 레벨 +2(필살기 수치 ×1.08).', fx: { type: 'ultLv', delta: 2 } },
    { n: 6, text: '번개 질주 타격 +3회.', fx: { type: 'ultReplace', eff: [D(8, { tgt: 'rand', hits: 8, brk: 1 }), IF(C.hpBelow(50), [D(8, { tgt: 'rand', hits: 2, brk: 1 })])] } },
  ],
  visual: { height: 167, build: '탄탄한 운동선수형 성인 체형', hair: '전기 파란 브릿지가 들어간 짧고 뻗친 금발', eyes: '날카로운 하늘색 눈',
    outfit: '어깨까지 흘러내린 빛나는 회로 라인 퀵서비스 재킷, 크롭 스포츠 톱, 바이크 쇼츠, 빛 궤적을 남기는 운동화', weapon: '전격 쌍단검',
    motif: '번개·속도선', palette: ['#2b7bff', '#ffe14d', '#0b0d12'], expression: '이를 드러낸 대담한 웃음' },
  voice: '빠르고 시원한 톤',
});
card('lumi_jab', '섬광 찌르기', 'lumi', { rar: 'B', cost: 1, type: 'atk', start: true, eff: [D(8, { rv: 11 })] });
card('lumi_dash', '질주', 'lumi', { rar: 'B', cost: 0, type: 'atk', res: 'n', start: true, eff: [OVERLOAD(3), D(7), DRAW(1)] });
card('lumi_guard', '회피 기동', 'lumi', { rar: 'B', cost: 1, type: 'skl', start: true, eff: [B(7, { rv: 10 })] });
card('lumi_spark', '방전', 'lumi', { rar: 'B', cost: 1, type: 'atk', start: true, eff: [D(4, { rv: 6, hits: 2 })] });
card('lumi_overdrive', '오버드라이브', 'lumi', { rar: 'A', cost: 0, type: 'skl', res: 'n', uses: 2, eff: [OVERLOAD(5), AP(2)] });
card('lumi_chain', '연쇄 번개', 'lumi', { rar: 'A', cost: 2, type: 'atk', eff: [D(7, { rv: 10, tgt: 'all' })] });
card('lumi_adrenaline', '아드레날린', 'lumi', { rar: 'A', cost: 1, type: 'skl', res: 'n', eff: [DRAW(0, { scale: per('missingHp10', 1, 3) })] });
card('lumi_thunderstep', '천둥 걸음', 'lumi', { rar: 'A', cost: 1, type: 'atk', res: 'n', eff: [OVERLOAD(4), D(18, { brk: 2 })] });
card('lumi_limitbreak', '한계 돌파', 'lumi', { rar: 'S', cost: 1, type: 'skl', res: 'n', eff: [OVERLOAD(6), BUFF('lumiRush', 5000, { turns: 1 })] });
card('lumi_lightning', '낙뢰', 'lumi', { rar: 'S', cost: 2, type: 'atk', eff: [D(22, { rv: 30, brk: 3 })] });
card('lumi_secondwind', '재충전', 'lumi', { rar: 'S', cost: 1, type: 'skl', res: 'n', eff: [H(8), BUFF('overloadFree', 1, { turns: 1 })] });
card('lumi_storm', '폭풍의 핵', 'lumi', { rar: 'S', cost: 3, type: 'atk', res: 'n', kw: ['exhaust'], eff: [OVERLOAD(8), D(8, { tgt: 'rand', hits: 6 })] });

// Starting common card per character (their memory deck starts with 4 unique + 1 common).
export const startingCommon = {
  sera: 'com_guard', nia: 'com_slash', yuri: 'com_slash', ravien: 'com_guard', harin: 'com_slash',
  rien: 'com_guard', aila: 'com_slash', sion: 'com_guard', kaya: 'com_guard', lumi: 'com_guard',
};
