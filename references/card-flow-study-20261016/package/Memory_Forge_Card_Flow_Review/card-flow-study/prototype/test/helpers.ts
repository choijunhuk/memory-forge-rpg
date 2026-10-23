import { BattleEngine } from '../src/engine.ts';
import { CONTENT } from '../src/cards.ts';
import type { BattleSetup, Content, GameEvent, RulesConfig } from '../src/types.ts';

/** 테스트 전용 적·카드·반응. 퓨즈와 체력을 극단값으로 두어 관심 있는 규칙만 드러나게 한다. */
export const TEST_CONTENT: Content = {
  cards: {
    ...CONTENT.cards,
    feedbackLoop: { id: 'feedbackLoop', name: '되먹임 장치', cost: 0, type: 'power', target: 'none', effects: [{ op: 'addReaction', reactionId: 'feedback' }], text: '무한 루프 유발(테스트용)' },
    nova: { id: 'nova', name: '대폭발', cost: 0, type: 'attack', target: 'all', effects: [{ op: 'damage', amount: 999 }], text: '모두에게 피해 999(테스트용)' },
    lowPower: { id: 'lowPower', name: '낮은 우선순위', cost: 0, type: 'power', target: 'none', effects: [{ op: 'addReaction', reactionId: 'lowPrio' }], text: '' },
    highPower: { id: 'highPower', name: '높은 우선순위', cost: 0, type: 'power', target: 'none', effects: [{ op: 'addReaction', reactionId: 'highPrio' }], text: '' },
  },
  reactions: {
    ...CONTENT.reactions,
    feedback: { id: 'feedback', name: '되먹임', on: 'BlockGained', effects: [{ op: 'block', amount: 1 }] },
    lowPrio: { id: 'lowPrio', name: '낮음', on: 'CardPlayed', when: { cardType: 'attack' }, effects: [{ op: 'block', amount: 1 }] },
    highPrio: { id: 'highPrio', name: '높음', on: 'CardPlayed', when: { cardType: 'attack' }, priority: 10, effects: [{ op: 'block', amount: 1 }] },
  },
  enemies: {
    ...CONTENT.enemies,
    dummy: { id: 'dummy', name: '허수아비', hp: 100, poise: 2, pattern: [{ name: '대기', timing: 'fuse', fuse: 99, target: 'enemy', effects: [] }] },
    weakling: { id: 'weakling', name: '약골', hp: 5, pattern: [{ name: '대기', timing: 'fuse', fuse: 99, target: 'enemy', effects: [] }] },
    quick: {
      id: 'quick',
      name: '날쌘이',
      hp: 30,
      poise: 1,
      pattern: [
        { name: '찌르기', timing: 'fuse', fuse: 1, target: 'enemy', effects: [{ op: 'damage', amount: 4 }] },
        { name: '숨기', timing: 'fuse', fuse: 3, target: 'enemy', effects: [] },
      ],
    },
    slammer: {
      id: 'slammer',
      name: '강타꾼',
      hp: 30,
      pattern: [
        { name: '강타', timing: 'turnEnd', target: 'enemy', effects: [{ op: 'damage', amount: 7 }] },
        { name: '대기', timing: 'fuse', fuse: 99, target: 'enemy', effects: [] },
      ],
    },
    killer: { id: 'killer', name: '처형자', hp: 30, pattern: [{ name: '처형', timing: 'fuse', fuse: 1, target: 'enemy', effects: [{ op: 'damage', amount: 999 }] }] },
  },
};

export function newEngine(
  deck: string[],
  enemies: BattleSetup['enemies'] = [{ id: 'e1', defId: 'dummy' }],
  rules: Partial<RulesConfig> = {},
): BattleEngine {
  const setup: BattleSetup = { seed: 7, hero: { id: 'hero', name: '주인공', hp: 50 }, deck, enemies, shuffleDeck: false };
  return new BattleEngine(TEST_CONTENT, setup, rules);
}

export function newBattle(deck: string[], enemies?: BattleSetup['enemies'], rules?: Partial<RulesConfig>): BattleEngine {
  const engine = newEngine(deck, enemies, rules);
  engine.start();
  return engine;
}

export function handCard(engine: BattleEngine, defId: string): string {
  const uid = engine.state.zones.hand.find((u) => engine.state.cards[u].defId === defId);
  if (!uid) throw new Error(`${defId} is not in hand`);
  return uid;
}

export function play(engine: BattleEngine, defId: string, targetId?: string): GameEvent[] {
  const res = engine.dispatch({ type: 'PlayCard', cardUid: handCard(engine, defId), targetId });
  if (!res.ok) throw new Error(`play ${defId} rejected: ${res.reason}`);
  return res.events;
}

export function types(events: GameEvent[]): string[] {
  return events.map((e) => e.type);
}

export function firstIndex(events: GameEvent[], pred: (e: GameEvent) => boolean): number {
  const i = events.findIndex(pred);
  if (i < 0) throw new Error('expected event not found');
  return i;
}
