// 시나리오 데모: 입력 → Sim 즉시 해결 → 연출 재생 → 입력 재개의 타임라인을 가상 시계로 출력한다.
//   node src/demo.ts
import { BattleEngine } from './engine.ts';
import { CARDS, CONTENT } from './cards.ts';
import { Presenter } from './presenter.ts';
import type { GateResult } from './presenter.ts';
import type { GameEvent } from './types.ts';

const engine = new BattleEngine(CONTENT, {
  seed: 2026,
  hero: { id: 'hero', name: '주인공', hp: 50 },
  deck: ['flurry', 'strike', 'improvise', 'guard', 'insight', 'keepsake', 'breather', 'finale', 'sweep', 'pinShot', 'chainFlurry', 'scatter'],
  enemies: [
    { id: 'e1', defId: 'grunt' },
    { id: 'e2', defId: 'brute' },
  ],
  shuffleDeck: false,
});
const p = new Presenter(engine, { maxBacklog: 2 });

const unitName = (id: string) => engine.unit(id).name;
const cardName = (uid: string) => CARDS[engine.state.cards[uid].defId].name;
const card = (defId: string) => {
  const uid = engine.state.zones.hand.find((u) => engine.state.cards[u].defId === defId);
  if (!uid) throw new Error(`${defId} not in hand`);
  return uid;
};
const ms = (t: number) => `${String(Math.round(t)).padStart(6)}ms`;

function describe(e: GameEvent): string | null {
  switch (e.type) {
    case 'TurnStarted':
      return `── 턴 ${e.turn} 시작 ──`;
    case 'HandDealt':
      return `손패 배분 완료 (이후 첫 입력 가능)`;
    case 'ApChanged':
      return `AP ${e.from} → ${e.to}`;
    case 'CardPlayed':
      return `카드 사용: ${cardName(e.cardUid)}${e.targetId ? ` → ${unitName(e.targetId)}` : ''}`;
    case 'CardDrawn':
      return `드로우: ${cardName(e.cardUid)}`;
    case 'DrawOverflow':
      return `손패 가득 → ${cardName(e.cardUid)} 버린 더미로`;
    case 'Reshuffled':
      return `버린 더미 ${e.count}장을 섞어 뽑을 더미로`;
    case 'CardResolved':
      return `해결 완료: ${cardName(e.cardUid)}`;
    case 'HitResolved':
      return `  타격 ${e.hitIndex + 1}/${e.hitCount}: ${unitName(e.sourceId)} → ${unitName(e.targetId)} 피해 ${e.damage}${e.blocked ? ` (방어 ${e.blocked})` : ''}`;
    case 'HitFizzled':
      return `  타격 ${e.hitIndex + 1}/${e.hitCount}: 대상 없음(불발)`;
    case 'BlockGained':
      return `${unitName(e.unitId)} 방어도 +${e.amount}`;
    case 'BlockCleared':
      return `${unitName(e.unitId)} 방어도 초기화`;
    case 'PoiseChanged':
      return `${unitName(e.unitId)} 자세 ${e.from} → ${e.to}`;
    case 'Staggered':
      return `${unitName(e.unitId)} 경직!`;
    case 'FuseChanged':
      return `${unitName(e.unitId)} 퓨즈 ${e.from} → ${e.to} (${{ tick: '카드 사용', effect: '효과', stagger: '경직' }[e.reason]})`;
    case 'IntentChanged':
      return `${unitName(e.unitId)} 다음 행동 예고: ${e.actionName} (${e.timing === 'fuse' ? `퓨즈 ${e.fuse}` : '턴 종료 시'})`;
    case 'UnitDied':
      return `${unitName(e.unitId)} 쓰러짐`;
    case 'ReactionTriggered':
      return `반응 발동: ${CONTENT.reactions[e.reactionId].name}`;
    case 'ChoiceRequested':
      return `선택지 표시: ${e.prompt} (${e.optionCount}개)`;
    case 'ChoiceMade':
      return `선택 확정: ${e.index}번`;
    case 'EnemyActionStarted':
      return `▶ ${unitName(e.unitId)} 행동: ${e.actionName} (${e.trigger === 'fuse' ? '퓨즈 0 → 인터럽트' : '턴 종료 타이밍'})`;
    case 'EnemyActionEnded':
      return `◀ ${unitName(e.unitId)} 행동 끝`;
    case 'TurnEnding':
      return `── 턴 ${e.turn} 종료 처리 ──`;
    case 'ResolutionAborted':
      return `해결 중단(루프 가드)`;
    case 'BattleEnded':
      return `전투 종료: ${e.result === 'won' ? '승리' : '패배'}`;
    case 'StateRestored':
      return `상태 복원(되돌리기)`;
    case 'CardMoved':
      return null;
  }
}

let printed = 0;
function flushLog(): void {
  for (; printed < p.log.length; printed++) {
    const { at, event } = p.log[printed];
    const text = describe(event);
    if (text) console.log(`[${ms(at)}]   ${text}`);
  }
}

function input(label: string, res: GateResult): void {
  flushLog();
  const verdict = res.ok ? `수락 (백로그 ${p.backlog()}${p.backlog() > 1 ? ' → 화면상 예약 대기' : ''})` : `거절: ${res.reason}`;
  console.log(`[${ms(p.now)}] ◆ 입력 '${label}' → ${verdict}`);
}

function waitForInput(): void {
  const waited = p.runUntilInputOpen();
  flushLog();
  console.log(`[${ms(p.now)}] ○ 입력 가능 (${Math.round(waited)}ms 대기, AP ${engine.state.ap})`);
}

function waitIdle(note: string): void {
  p.runUntilIdle();
  flushLog();
  console.log(`[${ms(p.now)}] ○ ${note}`);
}

console.log('== 카드 사용 → 효과 해결 → 다음 입력: 타임라인 데모 (1배속, 백로그 2) ==\n');
p.start();
waitForInput();

input('연속 찌르기 → 덩치', p.tryPlay(card('flurry'), 'e2'));
input('베기 → 졸개', p.tryPlay(card('strike'), 'e1'));
input('방어 태세', p.tryPlay(card('guard')));
waitForInput();

input('임기응변', p.tryPlay(card('improvise')));
input('선택 0번(화면에 뜨기 전)', p.tryChoose(0));
waitIdle(`선택지가 화면에 표시됨 (Sim 단계: ${engine.state.phase})`);
input('선택 0번: 방어도 8', p.tryChoose(0));
input('통찰', p.tryPlay(card('insight')));
waitForInput();

input('통찰', p.tryPlay(card('insight')));
waitForInput();

input('턴 종료', p.tryEndTurn());
waitForInput();

input('피날레 → 졸개', p.tryPlay(card('finale'), 'e1'));
console.log(
  `[${ms(p.now)}]   (확정 사망 표시) 화면상 졸개 생존=${p.displayedAlive('e1')}, 지정 가능한 적=[${p.targetableEnemyIds().map(unitName).join(', ')}]`,
);
waitForInput();

const hero = engine.unit('hero');
console.log(`\n결과: 턴 ${engine.state.turn}, 주인공 HP ${hero.hp}/${hero.maxHp}, 남은 적: ${engine.targetableEnemyIds().map(unitName).join(', ')}`);
