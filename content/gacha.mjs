// Gacha configuration v0.7. Probabilities are in basis points (1 bp = 0.01%).
// Character banners: 80-pull hard pity with 50:50 ("반천장 80 / 풀천장 160").
// Weapon banners: 70-pull hard pity with 75:25 ("풀천장 140").
export const gacha = {
  costPerPull: 160,
  tenPullDiscount: false,
  curves: {
    character: { ssrBase: 80, softStart: 66, softStep: 700, hard: 80, srBase: 600, srGuarantee: 10, featuredSsrBps: 5000, featuredSrBps: 5000 },
    weapon: { ssrBase: 90, softStart: 60, softStep: 800, hard: 70, srBase: 700, srGuarantee: 10, featuredSsrBps: 7500, featuredSrBps: 7500 },
    standard: { ssrBase: 80, softStart: 66, softStep: 700, hard: 80, srBase: 600, srGuarantee: 10, selectorAt: 300 },
    beginner: { ssrBase: 80, softStart: 66, softStep: 700, hard: 50, srBase: 600, srGuarantee: 10, maxPulls: 50, tenPullTickets: 8 },
  },
  pityRules: [
    '한정 캐릭터 배너끼리 천장 카운트와 확정(풀천장) 상태를 공유하고 다음 한정 배너로 이월한다.',
    '무기 배너끼리 천장 카운트와 확정 상태를 공유·이월한다. 캐릭터 배너와는 별개다.',
    '상시 배너 카운트는 별개이며 초심자 배너 뽑기는 상시 300회 선택권 누적에 포함한다.',
    '10회 뽑기는 1회를 순서대로 10번 처리한다. 매 회차가 천장·확정 규칙을 갖는다.',
    'SR 이상이 9회 연속 나오지 않으면 10번째는 SR 이상 확정(SSR 확률은 현재 천장 곡선 그대로).',
  ],
  pools: {
    standardSsrChars: ['sera', 'yuri', 'ravien', 'sion'],
    srChars: ['nia', 'harin', 'rien', 'aila'],
    standardSsrWeapons: ['w_star_bow', 'w_frost_blade', 'w_clock_core', 'w_silver_hymnal', 'w_sera', 'w_yuri', 'w_ravien', 'w_sion'],
    srWeapons: ['w_hunter_rifle', 'w_mech_bow', 'w_guard_saber', 'w_iron_tonfa', 'w_pocket_engine', 'w_ledger_lock', 'w_choir_bell', 'w_glass_rod'],
  },
  banners: [
    { id: 'limited_lumi', kind: 'character', name: '섬광의 질주', featuredSsr: 'lumi', featuredSr: ['nia', 'rien'], days: [1, 21], ticket: 'limitedTicket' },
    { id: 'limited_kaya', kind: 'character', name: '추적의 불꽃', featuredSsr: 'kaya', featuredSr: ['harin', 'aila'], days: [22, 42], ticket: 'limitedTicket' },
    { id: 'weapon_lumi', kind: 'weapon', name: '섬전 무기고', featuredSsr: 'w_lumi', featuredSr: ['w_iron_tonfa', 'w_pocket_engine'], days: [1, 21], ticket: 'weaponTicket' },
    { id: 'weapon_kaya', kind: 'weapon', name: '추적자 무기고', featuredSsr: 'w_kaya', featuredSr: ['w_hunter_rifle', 'w_choir_bell'], days: [22, 42], ticket: 'weaponTicket' },
    { id: 'standard', kind: 'standard', name: '상시 모집: 공방의 문', ticket: 'standardTicket' },
    { id: 'beginner', kind: 'beginner', name: '첫 공방 모집', ticket: 'standardTicket' },
  ],
  duplicates: {
    character: '두 번째 획득부터 공명 +1(최대 공명 6 = 총 7장). 첫 획득은 결정체 0, 공명 단계 중복은 SSR 10·SR 2, 공명 6 이후는 SSR 25·SR 5.',
    weapon: '두 번째 획득부터 정련 +1(최대 R5 = 총 5개). 무기는 매번 SSR 10·SR 2, R5 이후는 SSR 25·SR 5.',
    shardSsr: 10, shardSsrOverflow: 25, shardSr: 2, shardSrOverflow: 5, afterglowR: 15,
  },
  exchange: [
    { item: 'limitedTicket', costShards: 5, monthlyLimit: 5 },
    { item: 'weaponTicket', costShards: 5, monthlyLimit: 5 },
    { item: 'standardTicket', costAfterglow: 75, monthlyLimit: 5 },
    { item: 'limitedTicket', costAfterglow: 75, monthlyLimit: 5 },
  ],
  rItems: ['잔향 기록: 경험치', '잔향 기록: 크레딧', '잔향 기록: 연마석'],
};
