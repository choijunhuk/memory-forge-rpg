// 예시 콘텐츠. 이름·수치·효과 모두 이 프로토타입용으로 새로 만든 것이다(레퍼런스 게임의 카드가 아님).

import type { CardDef, Content, EnemyDef, ReactionDef } from './types.ts';

function byId<T extends { id: string }>(items: T[]): Record<string, T> {
  return Object.fromEntries(items.map((x) => [x.id, x]));
}

export const CARDS = byId<CardDef>([
  { id: 'strike', name: '베기', cost: 1, type: 'attack', target: 'enemy', effects: [{ op: 'damage', amount: 6, poise: 1 }], text: '피해 6 (자세 1)' },
  { id: 'flurry', name: '연속 찌르기', cost: 1, type: 'attack', target: 'enemy', effects: [{ op: 'damage', amount: 3, hits: 3, poise: 1 }], text: '피해 3 × 3회 (타격마다 자세 1)' },
  { id: 'chainFlurry', name: '사슬 찌르기', cost: 1, type: 'attack', target: 'enemy', effects: [{ op: 'damage', amount: 3, hits: 3, retarget: true }], text: '피해 3 × 3회. 대상이 쓰러지면 남은 타격은 무작위 적에게' },
  { id: 'sweep', name: '휩쓸기', cost: 2, type: 'attack', target: 'allEnemies', effects: [{ op: 'damage', amount: 5, poise: 1 }], text: '모든 적에게 피해 5 (자세 1)' },
  { id: 'scatter', name: '난사', cost: 1, type: 'attack', target: 'randomEnemy', effects: [{ op: 'damage', amount: 2, hits: 4 }], text: '무작위 적에게 피해 2 × 4회' },
  { id: 'pinShot', name: '발묶기 사격', cost: 1, type: 'attack', target: 'enemy', effects: [{ op: 'damage', amount: 3 }, { op: 'delayFuse', amount: 1 }], text: '피해 3, 대상 퓨즈 +1' },
  { id: 'finale', name: '피날레', cost: 2, type: 'attack', target: 'enemy', tags: ['finisher'], effects: [{ op: 'damage', amount: 14, poise: 2 }], text: '피해 14 (자세 2). 해결이 끝나면 턴 종료' },
  { id: 'guard', name: '방어 태세', cost: 1, type: 'skill', target: 'self', effects: [{ op: 'block', amount: 6 }], text: '방어도 6' },
  { id: 'keepsake', name: '부적', cost: 1, type: 'skill', target: 'self', tags: ['retain'], effects: [{ op: 'block', amount: 4 }], text: '방어도 4. 보존' },
  { id: 'insight', name: '통찰', cost: 0, type: 'skill', target: 'none', tags: ['exhaust'], effects: [{ op: 'draw', count: 2 }], text: '카드 2장 뽑기. 소멸' },
  { id: 'breather', name: '숨 고르기', cost: 0, type: 'skill', target: 'none', tags: ['exhaust'], effects: [{ op: 'gainAp', amount: 1 }], text: 'AP +1. 소멸' },
  {
    id: 'improvise',
    name: '임기응변',
    cost: 1,
    type: 'skill',
    target: 'none',
    effects: [{ op: 'choose', prompt: '하나를 고르세요', options: [[{ op: 'block', amount: 8 }], [{ op: 'draw', count: 2 }]] }],
    text: '선택: 방어도 8 또는 카드 2장 뽑기',
  },
  { id: 'battleRhythm', name: '전투 리듬', cost: 1, type: 'power', target: 'none', effects: [{ op: 'addReaction', reactionId: 'rhythmGuard' }], text: '이번 전투 동안: 공격 카드를 낼 때마다 방어도 2 (턴당 2회)' },
  { id: 'jam', name: '고장', cost: 0, type: 'skill', target: 'none', tags: ['unplayable'], effects: [], text: '사용할 수 없음' },
]);

export const REACTIONS = byId<ReactionDef>([
  { id: 'rhythmGuard', name: '전투 리듬', on: 'CardPlayed', when: { cardType: 'attack' }, limitPerTurn: 2, effects: [{ op: 'block', amount: 2 }] },
]);

export const ENEMIES = byId<EnemyDef>([
  {
    id: 'grunt',
    name: '졸개',
    hp: 20,
    poise: 2,
    pattern: [
      { name: '할퀴기', timing: 'fuse', fuse: 3, target: 'enemy', effects: [{ op: 'damage', amount: 5 }] },
      { name: '몸통 박치기', timing: 'fuse', fuse: 4, target: 'enemy', effects: [{ op: 'damage', amount: 8 }] },
    ],
  },
  {
    id: 'brute',
    name: '덩치',
    hp: 36,
    poise: 3,
    pattern: [
      { name: '내려찍기', timing: 'turnEnd', target: 'enemy', effects: [{ op: 'damage', amount: 10 }] },
      { name: '연타', timing: 'fuse', fuse: 2, target: 'enemy', effects: [{ op: 'damage', amount: 3, hits: 2 }] },
    ],
  },
]);

export const CONTENT: Content = { cards: CARDS, enemies: ENEMIES, reactions: REACTIONS };
