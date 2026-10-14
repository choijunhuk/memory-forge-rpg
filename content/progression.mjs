// Account-side progression, live content and economy for Memory Forge v0.7.
// Everything here is DATA. Formulas are written as small integer expressions so the client,
// the server and the reference engine compute identical numbers. KRW prices are proposals.

// ───────────────────────── combat stats from growth ─────────────────────────
// Reference battle (prototype "기준 전투"): power 40 → power4 160, 81 HP per character.
// Real accounts: stats come from levels/gear; content is scaled by its stage level so that an
// at-level party feels like the reference battle. Over-levelling helps, under-levelling hurts.
export const stats = {
  levelCap: 60,
  referencePower: 40, referenceHpPerChar: 81,
  // C(Lv) = 20 + floor(14·Lv/60)            → Lv.1 20 · Lv.30 27 · Lv.60 34
  charPower: { base: 20, num: 14, den: 60 },
  // W(wLv) = base + floor(k·wLv/60)          → SSR 2..8 · SR 1..6 · R 1..4
  weaponPower: { SSR: { base: 2, k: 6 }, SR: { base: 1, k: 5 }, R: { base: 1, k: 3 } },
  // HP = 60 + Lv (+ armour)
  hpBase: 60,
  // gear (2 slots, +0..+15). armour: flat 2+n and +(300+60n) bps of base HP. accessory: +(250+50n) bps damage & block.
  gear: { maxEnhance: 15, armorFlatBase: 2, armorBpsBase: 300, armorBpsPer: 60, accessoryBpsBase: 250, accessoryBpsPer: 50 },
  // card-skill and ultimate levels: +4% per level above 1 (resonance R3/R5 add +2 on top, max 12)
  skill: { maxLevel: 10, bpsPerLevel: 400, resonanceBonus: 2 },
  // memory capacity: 6 base, +1 at ascension 2, +1 at ascension 4, +1 at resonance 4 → max 9
  capacity: { base: 6, ascension: [{ asc: 2, add: 1 }, { asc: 4, add: 1 }] },
  // stage scaling for at-level parity: P_ref(L) = C(L) + W_SR(L); hpBps = round(10000·P_ref/40); dmgBps = round(10000·(60+L)/81)
  stageScale: { refWeapon: 'SR' },
};

// ───────────────────────── levelling & materials ─────────────────────────
export const levelling = {
  // XP to go from L to L+1 (character): 40·(4 + L + floor(L²/10)). Weapon = half. Credits = XP/2.
  charXp: { k: 40, a: 4, sqDiv: 10 },
  weaponXpRatio: 0.5, creditsPerXp: 0.5,
  xpItems: [ { id: 'ink_s', name: '기록 잉크(소)', xp: 1000 }, { id: 'ink_m', name: '기록 잉크(중)', xp: 5000 }, { id: 'ink_l', name: '기록 잉크(대)', xp: 20000 } ],
  weaponXpItems: [ { id: 'oil_s', name: '태엽 기름(소)', xp: 1000 }, { id: 'oil_m', name: '태엽 기름(중)', xp: 5000 }, { id: 'oil_l', name: '태엽 기름(대)', xp: 20000 } ],
  // ascension: level cap 20 → 30 → 40 → 50 → 60
  charAscension: [
    { stage: 1, cap: 30, account: 10, seals: 4, cores: 0, credits: 10000 },
    { stage: 2, cap: 40, account: 20, seals: 8, cores: 0, credits: 20000 },
    { stage: 3, cap: 50, account: 30, seals: 12, cores: 2, credits: 40000 },
    { stage: 4, cap: 60, account: 40, seals: 16, cores: 4, credits: 60000 },
  ],
  weaponAscension: [
    { stage: 1, cap: 30, alloys: 2, credits: 5000 },
    { stage: 2, cap: 40, alloys: 4, credits: 10000 },
    { stage: 3, cap: 50, alloys: 6, credits: 20000 },
    { stage: 4, cap: 60, alloys: 8, credits: 30000 },
  ],
  // per skill line (card skill, ultimate): cost to reach level L (index 0 = Lv.2)
  skillCosts: [
    { to: 2, books: 2, credits: 5000, cores: 0 }, { to: 3, books: 4, credits: 10000, cores: 0 },
    { to: 4, books: 6, credits: 15000, cores: 0 }, { to: 5, books: 8, credits: 20000, cores: 0 },
    { to: 6, books: 10, credits: 30000, cores: 0 }, { to: 7, books: 14, credits: 40000, cores: 0 },
    { to: 8, books: 18, credits: 60000, cores: 0 }, { to: 9, books: 24, credits: 80000, cores: 2 },
    { to: 10, books: 32, credits: 120000, cores: 2 },
  ],
  skillLines: ['card', 'ult'],
  // gear: craft 20 polish + 10,000 credits per piece; enhance +n costs n polish and 1,000·n credits
  gearCraft: { polish: 20, credits: 10000 }, gearEnhance: { polishPerLevel: 1, creditsPerLevel: 1000 },
};

export const materials = [
  { id: 'credits', name: '크레딧', use: '모든 육성' },
  { id: 'ink', name: '기록 잉크', use: '캐릭터 경험치' },
  { id: 'oil', name: '태엽 기름', use: '무기 경험치' },
  { id: 'seal', name: '기억 인장', use: '캐릭터 승급' },
  { id: 'alloy', name: '기계 합금', use: '무기 승급' },
  { id: 'book', name: '공방 교본', use: '스킬 레벨' },
  { id: 'polish', name: '연마재', use: '장비 제작·강화' },
  { id: 'core', name: '승급 핵', use: '캐릭터 승급 3·4단계, 스킬 Lv.9·10' },
  { id: 'gift', name: '선물', use: '인연 경험치' },
];

// ───────────────────────── stamina & content ─────────────────────────
export const stamina = { name: '집중', cap: 240, regenMinutes: 6, dailyRegen: 240, missionBonusPerDay: 30, refillCrystals: [50, 100, 100, 150], refillAmount: 60, overflowCap: 999 };

export const dungeons = [
  { id: 'dg_ink', name: '잉크 저장고', unlock: 5, cost: 20, yield: { ink: 24000 }, note: '캐릭터 경험치(잉크 아이템 합산 환산)' },
  { id: 'dg_credit', name: '반짝이는 금고', unlock: 5, cost: 20, yield: { credits: 40000 } },
  { id: 'dg_oil', name: '태엽 공장', unlock: 10, cost: 20, yield: { oil: 12000, alloy: 1 } },
  { id: 'dg_seal', name: '인장 회랑', unlock: 10, cost: 20, yield: { seal: 3 } },
  { id: 'dg_book', name: '금서 열람실', unlock: 15, cost: 20, yield: { book: 8 } },
  { id: 'dg_polish', name: '연마 작업장', unlock: 20, cost: 20, yield: { polish: 20 } },
];
// growth dungeons: 3-star first clear unlocks sweep (instant result, same reward). No in-battle randomness in rewards.
// yields above are the tier-6 (stage 60) values; lower tiers pay a percentage (integer floor).
export const dungeonTiers = [ { tier: 1, stage: 10, pct: 25 }, { tier: 2, stage: 20, pct: 40 }, { tier: 3, stage: 30, pct: 55 }, { tier: 4, stage: 40, pct: 70 }, { tier: 5, stage: 50, pct: 85 }, { tier: 6, stage: 60, pct: 100 } ];
export const dungeonStars = { star1: '승리', star2: '4턴 이내 승리', star3: '승리 시 파티 체력 50% 이상', sweepUnlock: 3, sweepMax: 12 };

export const weeklyBoss = { unlock: 15, claimsPerWeek: 3, resetDay: 'MON 05:00 KST', perClaim: { core: 2, credits: 20000, bossToken: 40 },
  difficulty: [ { lv: 1, stage: 30 }, { lv: 2, stage: 40 }, { lv: 3, stage: 50 }, { lv: 4, stage: 60 }, { lv: 5, stage: 60, hpBps: 13000, dmgBps: 12000 } ],
  note: '보상은 주 3회, 모든 보스 공유. 연습·재도전은 무제한(보상 없음). 저장된 기억 덱으로 1전투.' };

export const recordRift = { unlock: 20, cycleDays: 14, cyclesPerSeason: 3,
  floors: [
    { floor: 1, stage: 40, reward: 200 }, { floor: 2, stage: 45, reward: 160 }, { floor: 3, stage: 50, reward: 160 },
    { floor: 4, stage: 55, reward: 120 }, { floor: 5, stage: 60, reward: 80 }, { floor: 6, stage: 60, hpBps: 12500, dmgBps: 12000, reward: 80 },
  ],
  battlesPerFloor: 3, rule: '층마다 3연전. 1·3전은 1파티, 2전은 2파티(서로 다른 6명). 파티별 체력은 층 안에서 이어진다. 저장된 기억 덱만 사용(탐사 보상 없음). 층마다 균열 규칙 1개(14일마다 교체).',
  modifiers: [
    { id: 'rift_reserve', name: '시간의 틈', text: '예약으로 해결되는 피해 +30%, 즉시 피해 -10%.' },
    { id: 'rift_break', name: '금 간 거울', text: '균열 게이지 +50%, 균열 시 받는 피해 +50%(기본 +25% 대신).' },
    { id: 'rift_ink', name: '번지는 잉크', text: '매 턴 시작 시 얼룩 1장이 버린 더미에 섞인다. 소멸시킨 카드 1장당 피해 +3%.' },
    { id: 'rift_debt', name: '이자의 밤', text: '부채 상한 +2, 부채가 있으면 모든 피해 +15%.' },
    { id: 'rift_bloom', name: '서리 온실', text: '성장 상한 +2, 성장 카드 해결 시 방어 +3.' },
    { id: 'rift_echo', name: '공명하는 무대', text: '잔향 최대 +2, 서로 다른 캐릭터 카드를 연속 3장 내면 행동력 +1(턴당 1회).' },
  ] };

export const storyChapters = [
  // zones: visited in order (archive between zones). bosses: fixed boss per zone (null = random from the zone pool).
  { ch: 0, name: '프롤로그: 자정에 멈춘 시계', stage: 1, zones: [1], bosses: ['b_bell'], firstClear: { crystals: 300, standardTicket: 5 }, unlockAccount: 1, tutorial: true },
  { ch: 1, name: '1장: 잉크가 번지는 거리', stage: 8, zones: [1], bosses: ['b_bell'], firstClear: { crystals: 600, standardTicket: 2 }, unlockAccount: 3 },
  { ch: 2, name: '2장: 열지 말 것', stage: 16, zones: [1], bosses: ['b_notary'], firstClear: { crystals: 600, limitedTicket: 1 }, unlockAccount: 8 },
  { ch: 3, name: '3장: 잠긴 기록소', stage: 26, zones: [1, 2], bosses: ['b_garden', 'b_archivist'], firstClear: { crystals: 800, limitedTicket: 1 }, unlockAccount: 15 },
  { ch: 4, name: '4장: 갚지 못한 계약', stage: 36, zones: [3], bosses: ['b_usurer'], firstClear: { crystals: 800, limitedTicket: 1 }, unlockAccount: 22, authored: false },
  { ch: 5, name: '5장: 끝나지 않는 곡', stage: 46, zones: [4], bosses: ['b_overture'], firstClear: { crystals: 800, limitedTicket: 1 }, unlockAccount: 30, authored: false },
  { ch: 6, name: '6장: 마지막 태엽', stage: 56, zones: [4, 5], bosses: ['b_overture', 'b_nameless'], firstClear: { crystals: 1000, limitedTicket: 2 }, unlockAccount: 38, authored: false },
];
// authored: false → zones 3-5 and their bosses are specified in the design doc (§10.4) but not yet in data (P2 content task).

export const accountLevel = {
  cap: 60, xp: { per: 20, base: 80 }, // need(AL→AL+1) = 20·AL + 80
  xpSources: { staminaSpent: 1, dailyMissionComplete: 300, storyFirstClear: 200, weeklyBossClaim: 100 },
  unlocks: [
    { lv: 1, what: '탐사(프롤로그), 모집, 기억 저장' }, { lv: 3, what: '1장, 도감' }, { lv: 5, what: '잉크·크레딧 던전, 일일 임무' },
    { lv: 8, what: '2장, 인연(선물·에피소드)' }, { lv: 10, what: '캐릭터 1차 승급, 태엽 공장·인장 회랑, 장비 제작' },
    { lv: 15, what: '3장, 주간 보스, 금서 열람실' }, { lv: 20, what: '캐릭터 2차 승급, 기록 균열, 연마 작업장' },
    { lv: 22, what: '4장' }, { lv: 30, what: '캐릭터 3차 승급, 5장' }, { lv: 38, what: '6장' }, { lv: 40, what: '캐릭터 4차 승급(Lv.60)' },
  ],
};

// 인연(affinity): 1..10. Teen characters' bond content is platonic (mentor / friend / family) — design §4.5 and §11.10.
export const affinity = {
  maxLevel: 10, xpPerLevel: 100, // need(L→L+1) = 100·L → total 4,500
  sources: { battleTogether: 5, dailyBattleCap: 50, giftSmall: 20, giftMid: 50, giftLarge: 100, preferredMult: 1.5 },
  rewards: [
    { lv: 2, reward: '프로필·생일 해금' }, { lv: 3, reward: '인연 에피소드 1 + 결정 50' }, { lv: 4, reward: '로비 대사 추가' },
    { lv: 5, reward: '인연 에피소드 2 + 결정 50 + 공방장의 기억 조각 1' }, { lv: 6, reward: '전투 대사 추가' },
    { lv: 7, reward: '인연 에피소드 3 + 결정 50' }, { lv: 8, reward: '로비 상호작용 추가' },
    { lv: 9, reward: '공방장의 기억 조각 2' }, { lv: 10, reward: '인연 에피소드 4(최종) + 결정 100 + 의상 색 변경 1 + 칭호' },
  ],
};

// ───────────────────────── missions & season income ─────────────────────────
export const missions = {
  daily: { points: 100, tasks: [
    { id: 'd_login', text: '접속', pts: 20 }, { id: 'd_stamina', text: '집중 120 사용', pts: 20 }, { id: 'd_battle', text: '전투 3회 승리', pts: 20 },
    { id: 'd_level', text: '캐릭터 또는 무기 레벨 올리기 1회', pts: 20 }, { id: 'd_affinity', text: '인연 선물 1회 또는 로비 대화 1회', pts: 20 }],
    milestones: [ { at: 20, crystals: 10, credits: 10000 }, { at: 40, crystals: 10, stamina: 30 }, { at: 60, crystals: 10, ink_m: 2 }, { at: 80, crystals: 10, credits: 10000 }, { at: 100, crystals: 20, passXp: 1000 } ] },
  weekly: { crystals: 420, standardTicket: 2, passXp: 4000, tasks: ['일일 임무 5일 완료', '주간 보스 보상 3회', '탐사 2회 완주', '기억 저장 1회', '성장 던전 20회'] },
  weeklyRun: { crystals: 200, text: '이번 주 탐사(짧게 이상) 첫 완주 보상' },
  attendance: { weeklyCrystals: 160, text: '7일 누적 접속 보상(주 단위, 연속 불필요)' },
};

export const seasonIncome = {
  days: 42, note: 'v0.6 기준선 A안을 유지. 실제 운영 보상표는 이 표를 원장으로 서버가 지급.',
  f2pRecurring: [
    { src: '일일 임무', per: 60, times: 42, crystals: 2520 },
    { src: '주간 임무', per: 420, times: 6, crystals: 2520 },
    { src: '주간 탐사 첫 완주', per: 200, times: 6, crystals: 1200 },
    { src: '주간 누적 접속', per: 160, times: 6, crystals: 960 },
    { src: '시즌 이벤트', per: 2880, times: 1, crystals: 2880 },
    { src: '무료 패스', per: 1440, times: 1, crystals: 1440 },
    { src: '기록 균열(최대)', per: 800, times: 3, crystals: 2400, optional: true },
  ],
  tickets: { limitedTicket: 9, limitedSources: '시즌 이벤트 상점 5 + 무료 패스 4', standardTicket: 12, standardSources: '주간 임무 2×6' },
  f2p: { crystals: 13920, limitedTicket: 9, standardTicket: 12 },
  oneTime: { story: 'storyChapters.firstClear 합계', beginnerBanner: '첫 공방 모집 50회 = 상시권 40장(10회=8장)', launch: { crystals: 1600, standardTicket: 10 } },
};

// ───────────────────────── paid products (KRW proposal) ─────────────────────────
export const products = {
  crystalPacks: [
    { id: 'pk_160', crystals: 160, krw: 2400, firstBonus: 160 }, { id: 'pk_800', crystals: 800, krw: 11000, firstBonus: 800 },
    { id: 'pk_1680', crystals: 1680, krw: 22000, firstBonus: 1680 }, { id: 'pk_3440', crystals: 3440, krw: 44000, firstBonus: 3440 },
    { id: 'pk_7000', crystals: 7000, krw: 88000, firstBonus: 7000 },
  ],
  monthly: { id: 'monthly', name: '공방 정기권', krw: 6900, days: 30, upfrontPaid: 300, dailyFree: 90, total: 3000, autoRenew: false, maxStackDays: 180 },
  pass: { id: 'pass', name: '시즌 패스', krw: 12900, levels: 50, xpPerLevel: 1000, crystals: 2560, limitedTicket: 0, plus: { krw: 24900, levelsGranted: 10, extra: '한정 의상 1(패스 전용)' } },
  skins: [ { tier: 1, krw: 9900, scope: '일러스트·로비 모델 색/소품 변경' }, { tier: 2, krw: 19800, scope: '새 의상(로비·전투 모델·일러스트)' }, { tier: 3, krw: 29800, scope: '새 의상 + 필살기 연출·전용 대사' } ],
  rules: ['유료/무료 결정을 분리 원장으로 관리(환불·청약철회 대응)', '모든 상품 페이지에 구성·확률·기간 표시', '랜덤 상품은 모집(가챠) 외에 판매하지 않는다'],
};

// ───────────────────────── exchange shops ─────────────────────────
export const exchangeShops = {
  shard: { currency: 'shard', name: '기억 결정체 교환', resetMonthly: true, items: [
    { item: 'ticket_limited', cost: 5, limit: 5 }, { item: 'ticket_weapon', cost: 5, limit: 5 },
    { item: 'skin_dye', cost: 30, limit: 1, note: '보유 캐릭터 의상 색 변경 1회' } ] },
  afterglow: { currency: 'afterglow', name: '잔광 교환', resetMonthly: true, items: [
    { item: 'ticket_standard', cost: 75, limit: 5 }, { item: 'ticket_limited', cost: 75, limit: 5 },
    { item: 'credit', cost: 1, qty: 400, limit: 500 }, { item: 'gift_mid', cost: 20, limit: 20 } ] },
  bossToken: { currency: 'boss_token', name: '보스 토큰 교환', resetWeekly: true, items: [
    { item: 'refine_core', cost: 60, limit: 2, note: '기본(R) 무기 정련 1단계' }, { item: 'book', cost: 10, limit: 6 }, { item: 'polish', cost: 5, limit: 20 } ] },
  event: { currency: 'event_token', name: '시즌 이벤트 상점(이벤트마다)', items: [
    { item: 'ticket_limited', cost: 400, limit: 3, note: '첫 이벤트 3장, 두 번째 이벤트 2장 = 시즌 5장' }, { item: 'crystal_free', cost: 100, qty: 80, limit: 6 },
    { item: 'core', cost: 300, limit: 2 }, { item: 'gift_large', cost: 60, limit: 10 }, { item: 'event_relic_unlock', cost: 500, limit: 1, note: '이벤트 탐사 전용 유물 상시화' } ] },
};
