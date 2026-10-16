// design-spec.md의 규칙 번호(R-xx)를 테스트 이름에 붙여 명세 ↔ 구현을 추적한다.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { firstIndex, handCard, newBattle, play, types } from './helpers.ts';
import type { GameEvent } from '../src/types.ts';

const is =
  <T extends GameEvent['type']>(type: T, extra: (e: Extract<GameEvent, { type: T }>) => boolean = () => true) =>
  (e: GameEvent) =>
    e.type === type && extra(e as Extract<GameEvent, { type: T }>);

test('R-02 커밋: CardPlayed → AP 지불 → 손패→해결 구역 이동이 어떤 효과보다 먼저 일어난다', () => {
  const engine = newBattle(['strike', 'guard', 'guard', 'guard', 'guard']);
  const events = play(engine, 'strike', 'e1');
  assert.deepEqual(types(events).slice(0, 3), ['CardPlayed', 'ApChanged', 'CardMoved']);
  assert.deepEqual(events[1], { type: 'ApChanged', from: 3, to: 2, reason: 'cardCost' });
  assert.deepEqual(events[2], { type: 'CardMoved', cardUid: 'c1', from: 'hand', to: 'resolving' });
  assert.ok(firstIndex(events, is('CardMoved', (e) => e.from === 'hand')) < firstIndex(events, is('HitResolved')));
});

function playedUid(events: GameEvent[]): string {
  const e = events.find(is('CardPlayed'));
  return e && e.type === 'CardPlayed' ? e.cardUid : '';
}

test('R-04/R-05 정산 → 템포 틱 순서: 카드가 목적지로 간 뒤에 퓨즈가 줄어든다', () => {
  const engine = newBattle(['strike', 'guard', 'guard', 'guard', 'guard']);
  const events = play(engine, 'strike', 'e1');
  const settled = firstIndex(events, is('CardResolved'));
  const ticked = firstIndex(events, is('FuseChanged', (e) => e.reason === 'tick'));
  assert.ok(firstIndex(events, is('HitResolved')) < settled);
  assert.ok(settled < ticked);
  assert.equal(engine.state.zones.discard.includes(playedUid(events)), true);
});

test('R-03 해결 중인 카드는 손패에서 빠져 있다: 손패 10장에서 2장 드로우 시 1장만 넘친다', () => {
  const deck = ['insight', ...Array(9).fill('guard'), 'strike', 'strike'];
  const engine = newBattle(deck, undefined, { drawPerTurn: 10 });
  assert.equal(engine.state.zones.hand.length, 10);
  const events = play(engine, 'insight');
  assert.deepEqual(
    events.filter((e) => e.type === 'CardDrawn' || e.type === 'DrawOverflow').map((e) => e.type),
    ['CardDrawn', 'DrawOverflow'],
  );
  assert.equal(engine.state.zones.hand.length, 10);
  assert.equal(engine.state.zones.exhaust.length, 1, '소멸 태그 카드는 소멸 더미로');
});

test('R-11 경직(퓨즈 +1)은 같은 카드의 템포 틱보다 먼저 반영된다 → 퓨즈 1인 적이 행동하지 않는다', () => {
  const engine = newBattle(['strike', 'guard', 'guard', 'guard', 'guard'], [{ id: 'e1', defId: 'quick' }]);
  const events = play(engine, 'strike', 'e1');
  const fuses = events.filter((e) => e.type === 'FuseChanged');
  assert.deepEqual(
    fuses.map((e) => (e.type === 'FuseChanged' ? [e.reason, e.from, e.to] : [])),
    [
      ['stagger', 1, 2],
      ['tick', 2, 1],
    ],
  );
  assert.equal(events.some(is('EnemyActionStarted')), false);
});

test('R-12 퓨즈 0 → 카드 정산 후 같은 명령 안에서 인터럽트가 해결되고 다음 행동이 예고된다', () => {
  const engine = newBattle(['guard', 'guard', 'guard', 'guard', 'guard'], [{ id: 'e1', defId: 'quick' }]);
  const events = play(engine, 'guard');
  const resolved = firstIndex(events, is('CardResolved'));
  const started = firstIndex(events, is('EnemyActionStarted'));
  const hit = firstIndex(events, is('HitResolved', (e) => e.sourceId === 'e1'));
  const ended = firstIndex(events, is('EnemyActionEnded'));
  assert.ok(resolved < started && started < hit && hit < ended);
  assert.deepEqual(events[events.length - 1], { type: 'IntentChanged', unitId: 'e1', actionName: '숨기', timing: 'fuse', fuse: 3 });
  assert.equal(engine.unit('hero').hp, 50, '방어도 6이 피해 4를 막는다');
  assert.equal(engine.state.phase, 'playerMain');
});

test('R-13 같은 틱에 여러 적이 0이 되면 위치 순으로 하나씩 끝까지 행동한다', () => {
  const engine = newBattle(['guard', 'guard', 'guard', 'guard', 'guard'], [
    { id: 'e1', defId: 'quick' },
    { id: 'e2', defId: 'quick' },
  ]);
  const events = play(engine, 'guard');
  const seq = events
    .filter((e) => e.type === 'EnemyActionStarted' || e.type === 'EnemyActionEnded')
    .map((e) => (e.type === 'EnemyActionStarted' || e.type === 'EnemyActionEnded' ? `${e.type}:${e.unitId}` : ''));
  assert.deepEqual(seq, ['EnemyActionStarted:e1', 'EnemyActionEnded:e1', 'EnemyActionStarted:e2', 'EnemyActionEnded:e2']);
});

test('R-08 다단 히트: 타격 사이에 사망 판정이 끼고, 남은 타격은 불발한다', () => {
  const engine = newBattle(['flurry', 'guard', 'guard', 'guard', 'guard'], [
    { id: 'e1', defId: 'weakling' },
    { id: 'e2', defId: 'dummy' },
  ]);
  const events = play(engine, 'flurry', 'e1');
  assert.deepEqual(
    types(events).filter((t) => t === 'HitResolved' || t === 'UnitDied' || t === 'HitFizzled'),
    ['HitResolved', 'HitResolved', 'UnitDied', 'HitFizzled'],
  );
  assert.equal(engine.unit('e2').hp, 100);
});

test('R-08 retarget 카드는 대상이 쓰러지면 남은 타격을 다른 적에게 보낸다', () => {
  const engine = newBattle(['chainFlurry', 'guard', 'guard', 'guard', 'guard'], [
    { id: 'e1', defId: 'weakling' },
    { id: 'e2', defId: 'dummy' },
  ]);
  const events = play(engine, 'chainFlurry', 'e1');
  const targets = events.filter((e) => e.type === 'HitResolved').map((e) => (e.type === 'HitResolved' ? e.targetId : ''));
  assert.deepEqual(targets, ['e1', 'e1', 'e2']);
});

test('R-06 반응 창: CardPlayed 반응은 카드 효과보다 먼저, 턴당 횟수 제한은 엔진이 집행', () => {
  const engine = newBattle(['battleRhythm', 'strike', 'strike', 'strike', 'guard'], undefined, { apPerTurn: 10 });
  play(engine, 'battleRhythm');
  const first = play(engine, 'strike', 'e1');
  assert.ok(firstIndex(first, is('ReactionTriggered')) < firstIndex(first, is('HitResolved')));
  play(engine, 'strike', 'e1');
  const third = play(engine, 'strike', 'e1');
  assert.equal(third.some(is('ReactionTriggered')), false, '턴당 2회까지만');

  engine.dispatch({ type: 'EndTurn' });
  const next = play(engine, 'strike', 'e1');
  assert.equal(next.some(is('ReactionTriggered')), true, '새 턴에 횟수 초기화');
});

test('R-07 같은 사건에 걸린 반응은 우선순위 높은 순 → 등록 순으로 해결된다', () => {
  const engine = newBattle(['lowPower', 'highPower', 'strike', 'guard', 'guard']);
  play(engine, 'lowPower');
  play(engine, 'highPower');
  const events = play(engine, 'strike', 'e1');
  const order = events.flatMap((e) => (e.type === 'ReactionTriggered' ? [e.reactionId] : []));
  assert.deepEqual(order, ['highPrio', 'lowPrio']);
});

test('R-15 루프 가드: 연산 상한을 넘으면 해결을 중단하고 카드를 정리한 뒤 입력을 돌려준다', () => {
  const engine = newBattle(['feedbackLoop', 'guard', 'guard', 'guard', 'guard'], undefined, { maxOpsPerCommand: 200 });
  play(engine, 'feedbackLoop');
  const events = play(engine, 'guard');
  assert.equal(events.some(is('ResolutionAborted')), true);
  assert.equal(engine.state.zones.resolving.length, 0);
  assert.equal(engine.state.queue.length, 0);
  assert.equal(engine.state.phase, 'playerMain');
  assert.equal(engine.dispatch({ type: 'EndTurn' }).ok, true);
});

test('R-14 선택 대기: 해결 큐를 보존한 채 멈추고, 선택 후 정산·틱이 이어진다', () => {
  const engine = newBattle(['improvise', 'guard', 'guard', 'strike', 'strike', 'guard', 'guard']);
  const first = play(engine, 'improvise');
  assert.equal(first[first.length - 1].type, 'ChoiceRequested');
  assert.equal(engine.state.phase, 'awaitingChoice');
  assert.equal(engine.state.zones.resolving.length, 1);

  const blocked = engine.dispatch({ type: 'PlayCard', cardUid: handCard(engine, 'guard') });
  assert.deepEqual(blocked, { ok: false, reason: 'AWAITING_CHOICE' });

  const res = engine.dispatch({ type: 'Choose', index: 1 });
  assert.ok(res.ok);
  const events = res.events;
  assert.equal(events[0].type, 'ChoiceMade');
  assert.ok(firstIndex(events, is('CardDrawn')) < firstIndex(events, is('CardResolved')));
  assert.ok(firstIndex(events, is('CardResolved')) < firstIndex(events, is('FuseChanged')));
  assert.equal(engine.state.phase, 'playerMain');
});

test('R-16 피니셔: 카드 정산·틱·인터럽트 뒤에 턴이 끝나고 턴 종료 타이밍 적이 행동한다', () => {
  const engine = newBattle(['finale', 'guard', 'guard', 'guard', 'guard', 'strike', 'strike', 'strike', 'strike', 'strike'], [
    { id: 'e1', defId: 'dummy' },
    { id: 'e2', defId: 'slammer' },
  ]);
  const events = play(engine, 'finale', 'e1');
  const idx = [
    firstIndex(events, is('CardResolved')),
    firstIndex(events, is('FuseChanged', (e) => e.reason === 'tick')),
    firstIndex(events, is('TurnEnding')),
    firstIndex(events, is('EnemyActionStarted', (e) => e.unitId === 'e2' && e.trigger === 'turnEnd')),
    firstIndex(events, is('TurnStarted')),
    firstIndex(events, is('HandDealt')),
  ];
  assert.deepEqual([...idx].sort((a, b) => a - b), idx, '정산 → 틱 → 턴 종료 → 턴 종료 행동 → 새 턴 → 드로우');
  assert.equal(engine.state.turn, 2);
  assert.equal(engine.state.ap, 3);
});

test('R-17 턴 종료: 보존 카드만 손패에 남고, 방어도는 적 턴 종료 행동까지 유지된 뒤 다음 턴에 사라진다', () => {
  const engine = newBattle(
    ['keepsake', 'guard', 'strike', 'strike', 'strike', 'strike', 'strike', 'strike', 'strike', 'strike'],
    [{ id: 'e1', defId: 'slammer' }],
  );
  play(engine, 'guard');
  const res = engine.dispatch({ type: 'EndTurn' });
  assert.ok(res.ok);
  const hit = res.events.find(is('HitResolved'));
  assert.deepEqual(hit && hit.type === 'HitResolved' ? [hit.blocked, hit.damage] : [], [6, 1]);
  const keepsake = Object.values(engine.state.cards).find((c) => c.defId === 'keepsake')!;
  assert.equal(engine.state.zones.hand.includes(keepsake.uid), true);
  assert.equal(engine.state.zones.hand.length, 6, '보존 1장 + 새로 뽑은 5장');
  assert.equal(engine.unit('hero').block, 0);
});

test('R-10 리셔플: 해결 중인 카드는 섞이지 않고, 설정에 따라 이번 턴 사용 카드 포함 여부가 갈린다', () => {
  const deck = ['insight', 'strike', 'strike', 'strike', 'strike', 'guard'];

  const included = newBattle(deck);
  play(included, 'strike', 'e1');
  const a = play(included, 'insight');
  assert.deepEqual(a.filter(is('Reshuffled')), [{ type: 'Reshuffled', count: 1 }]);
  assert.equal(included.state.zones.hand.filter((u) => included.state.cards[u].defId === 'strike').length, 4);

  const excluded = newBattle(deck, undefined, { reshuffleIncludesPlayedThisTurn: false });
  play(excluded, 'strike', 'e1');
  const b = play(excluded, 'insight');
  assert.equal(b.some(is('Reshuffled')), false);
  assert.equal(excluded.state.zones.discard.length, 1);
});

test('R-18 되돌리기: 스냅샷 복원 후 같은 명령은 같은 난수 결과를 낸다(결정론)', () => {
  const engine = newBattle(['scatter', 'guard', 'guard', 'guard', 'guard'], [
    { id: 'e1', defId: 'dummy' },
    { id: 'e2', defId: 'dummy' },
  ]);
  const before = structuredClone(engine.state);
  const first = play(engine, 'scatter');
  assert.deepEqual(engine.undo(), [{ type: 'StateRestored' }]);
  assert.deepEqual(engine.state, before);
  const second = play(engine, 'scatter');
  assert.deepEqual(second, first);
});

test('R-19 전투 종료: 인터럽트로 영웅이 쓰러지면 즉시 패배, 이후 명령은 거절된다', () => {
  const engine = newBattle(['guard', 'guard', 'guard', 'guard', 'guard'], [{ id: 'e1', defId: 'killer' }]);
  const events = play(engine, 'guard');
  assert.deepEqual(events[events.length - 1], { type: 'BattleEnded', result: 'lost' });
  assert.equal(engine.state.queue.length, 0);
  assert.deepEqual(engine.dispatch({ type: 'EndTurn' }), { ok: false, reason: 'BATTLE_OVER' });
});

test('R-19 같은 원자 연산에서 양쪽이 전멸하면 simultaneousDeath 설정을 따른다', () => {
  for (const rule of ['won', 'lost'] as const) {
    const engine = newBattle(['nova', 'guard', 'guard', 'guard', 'guard'], undefined, { simultaneousDeath: rule });
    const events = play(engine, 'nova');
    assert.deepEqual(events[events.length - 1], { type: 'BattleEnded', result: rule });
  }
});

test('R-01 검증 실패 사유는 상태를 바꾸지 않고 돌려준다', () => {
  const engine = newBattle(['finale', 'strike', 'jam', 'breather', 'guard'], [
    { id: 'e1', defId: 'weakling' },
    { id: 'e2', defId: 'dummy' },
  ]);
  const snapshot = structuredClone(engine.state);
  const uid = (id: string) => handCard(engine, id);
  assert.deepEqual(engine.dispatch({ type: 'PlayCard', cardUid: uid('strike') }), { ok: false, reason: 'TARGET_REQUIRED' });
  assert.deepEqual(engine.dispatch({ type: 'PlayCard', cardUid: uid('strike'), targetId: 'hero' }), { ok: false, reason: 'INVALID_TARGET' });
  assert.deepEqual(engine.dispatch({ type: 'PlayCard', cardUid: uid('jam') }), { ok: false, reason: 'UNPLAYABLE' });
  assert.deepEqual(engine.dispatch({ type: 'PlayCard', cardUid: 'c999' }), { ok: false, reason: 'NOT_IN_HAND' });
  assert.deepEqual(engine.dispatch({ type: 'Choose', index: 0 }), { ok: false, reason: 'NO_PENDING_CHOICE' });
  assert.deepEqual(engine.state, snapshot);

  play(engine, 'finale', 'e1'); // 약골 처치 + 턴 종료
  assert.deepEqual(engine.dispatch({ type: 'PlayCard', cardUid: uid('strike'), targetId: 'e1' }), { ok: false, reason: 'INVALID_TARGET' });
});

test('R-01 AP 부족은 거절되고, 효과로 얻은 AP는 다음 명령부터 바로 쓸 수 있다', () => {
  const engine = newBattle(['finale', 'strike', 'breather', 'guard', 'guard'], [{ id: 'e1', defId: 'dummy' }]);
  play(engine, 'strike', 'e1');
  play(engine, 'guard');
  assert.equal(engine.state.ap, 1);
  assert.deepEqual(engine.dispatch({ type: 'PlayCard', cardUid: handCard(engine, 'finale'), targetId: 'e1' }), {
    ok: false,
    reason: 'NOT_ENOUGH_AP',
  });
  play(engine, 'breather');
  assert.equal(engine.state.ap, 2);
  assert.equal(engine.validate({ type: 'PlayCard', cardUid: handCard(engine, 'finale'), targetId: 'e1' }), null);
});
