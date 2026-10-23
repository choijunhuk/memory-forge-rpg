// 입력 게이트(I-xx) 규칙 테스트. 가상 시계로 '언제 다음 입력을 받는가'를 검증한다.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Presenter } from '../src/presenter.ts';
import type { PresenterOptions } from '../src/presenter.ts';
import { handCard, newEngine } from './helpers.ts';
import type { BattleSetup } from '../src/types.ts';

function newPresenter(deck: string[], enemies?: BattleSetup['enemies'], opts?: PresenterOptions): Presenter {
  const p = new Presenter(newEngine(deck, enemies), opts);
  p.start();
  p.runUntilInputOpen();
  return p;
}

test('I-01 턴 시작 연출(배리어)이 끝나야 첫 입력이 열린다', () => {
  const p = new Presenter(newEngine(['strike', 'guard', 'guard', 'guard', 'guard']));
  p.start();
  assert.equal(p.inputOpen(), false);
  assert.deepEqual(p.tryPlay(handCard(p.engine, 'guard')), { ok: false, reason: 'BARRIER_PENDING' });
  const waited = p.runUntilInputOpen();
  assert.ok(waited > 0);
  assert.equal(p.inputOpen(), true);
});

test('I-02 연출 중 다음 카드는 예약(백로그 2)되고, 그 이상은 BACKLOG_FULL', () => {
  const p = newPresenter(['flurry', 'strike', 'guard', 'guard', 'guard']);
  const strike = handCard(p.engine, 'strike');
  assert.deepEqual(p.tryPlay(handCard(p.engine, 'flurry'), 'e1'), { ok: true });
  assert.deepEqual(p.tryPlay(strike, 'e1'), { ok: true });
  assert.equal(p.backlog(), 2);

  // Sim은 이미 '베기'까지 해결을 끝냈지만, 화면에는 아직 재생되지 않았다(= 사용 예약 상태로 보임).
  assert.equal(p.engine.state.zones.discard.includes(strike), true);
  assert.equal(p.log.some((l) => l.event.type === 'CardPlayed' && l.event.cardUid === strike), false);

  assert.deepEqual(p.tryPlay(handCard(p.engine, 'guard')), { ok: false, reason: 'BACKLOG_FULL' });
  p.runUntilInputOpen();
  assert.deepEqual(p.tryPlay(handCard(p.engine, 'guard')), { ok: true });
});

test('I-03 적 인터럽트가 대기 중이면 연출이 끝날 때까지 입력을 막는다(BARRIER_PENDING)', () => {
  const p = newPresenter(['guard', 'guard', 'guard', 'guard', 'guard'], [{ id: 'e1', defId: 'quick' }]);
  assert.deepEqual(p.tryPlay(handCard(p.engine, 'guard')), { ok: true });
  assert.equal(p.barrierPending(), true);
  assert.deepEqual(p.tryPlay(handCard(p.engine, 'guard')), { ok: false, reason: 'BARRIER_PENDING' });

  const waited = p.runUntilInputOpen();
  const started = p.log.find((l) => l.event.type === 'EnemyActionStarted');
  assert.ok(started, '적 행동 연출이 재생되었다');
  assert.ok(waited >= 450 + 180 + 150, '행동 시작 + 타격 + 행동 종료 연출 이상 기다린다');
  assert.deepEqual(p.tryPlay(handCard(p.engine, 'guard')), { ok: true });
});

test('I-04 확정 사망 표시: 쓰러질 적은 연출 전이라도 즉시 대상에서 빠진다', () => {
  const p = newPresenter(['strike', 'strike', 'guard', 'guard', 'guard'], [
    { id: 'e1', defId: 'weakling' },
    { id: 'e2', defId: 'dummy' },
  ]);
  assert.deepEqual(p.tryPlay(handCard(p.engine, 'strike'), 'e1'), { ok: true });
  assert.equal(p.displayedAlive('e1'), true, '화면에는 아직 서 있다');
  assert.deepEqual(p.targetableEnemyIds(), ['e2'], '하지만 지정할 수는 없다');
  // 낡은 화면을 믿고 보낸 입력도 Sim이 거절한다(비용 소모 없음).
  assert.deepEqual(p.tryPlay(handCard(p.engine, 'strike'), 'e1'), { ok: false, reason: 'INVALID_TARGET' });
  assert.equal(p.engine.state.ap, 2);
  p.runUntilIdle();
  assert.equal(p.displayedAlive('e1'), false);
});

test('I-05 선택지는 연출이 따라잡아 화면에 뜬 뒤에만 고를 수 있다', () => {
  const p = newPresenter(['improvise', 'guard', 'guard', 'guard', 'guard', 'strike', 'strike']);
  assert.deepEqual(p.tryPlay(handCard(p.engine, 'improvise')), { ok: true });
  assert.equal(p.engine.state.phase, 'awaitingChoice');
  assert.deepEqual(p.tryChoose(0), { ok: false, reason: 'CHOICE_NOT_VISIBLE' });
  p.runUntilIdle();
  assert.equal(p.visibleChoice(), true);
  assert.deepEqual(p.tryChoose(0), { ok: true });
  assert.equal(p.engine.state.phase, 'playerMain');
  assert.equal(p.engine.unit('hero').block, 8);
});

test('I-06 배속은 재생 시간만 줄이고 해결 결과는 바꾸지 않는다', () => {
  const run = (speed: number) => {
    const p = newPresenter(['flurry', 'guard', 'guard', 'guard', 'guard'], undefined, { speed, maxBacklog: 1 });
    p.tryPlay(handCard(p.engine, 'flurry'), 'e1');
    return { waited: p.runUntilInputOpen(), hp: p.engine.unit('e1').hp };
  };
  const x1 = run(1);
  const x2 = run(2);
  assert.equal(x2.waited, x1.waited / 2);
  assert.equal(x2.hp, x1.hp);
});

test('I-07 되돌리기: 남은 연출을 버리고 복원 상태를 다시 그린 뒤 바로 입력이 열린다', () => {
  const p = newPresenter(['strike', 'guard', 'guard', 'guard', 'guard']);
  const strike = handCard(p.engine, 'strike');
  p.tryPlay(strike, 'e1');
  assert.equal(p.undo(), true);
  p.runUntilInputOpen();
  assert.equal(p.inputOpen(), true);
  assert.equal(p.engine.state.zones.hand.includes(strike), true);
  assert.equal(p.engine.state.ap, 3);
});
