// 연출 타임라인 + 입력 게이트(헤드리스). 실제 엔진(Unity/Godot 등)의 View 계층이 할 일을
// 가상 시계로 흉내 내서 "언제 다음 입력을 받아도 되는가"를 테스트할 수 있게 한다.
//
// 입력 개방 조건(IOC, design-spec.md §7):
//   Sim이 playerMain  AND  재생 대기/재생 중인 배리어 이벤트 없음  AND  연출 백로그 < maxBacklog
// Sim은 이미 끝까지 해결했으므로, 백로그 안의 카드는 화면상 '사용 예약' 상태로 보일 뿐이다.

import type { BattleEngine } from './engine.ts';
import type { GameEvent, RejectReason } from './types.ts';

export type GateReason = 'BARRIER_PENDING' | 'BACKLOG_FULL' | 'CHOICE_NOT_VISIBLE';
export type GateResult = { ok: true } | { ok: false; reason: GateReason | RejectReason };
type EventType = GameEvent['type'];

/** 1배속 기준 재생 시간(ms). 0이면 다음 클립과 같은 순간에 처리된다. */
export const BASE_DURATION_MS: Record<EventType, number> = {
  TurnStarted: 400,
  HandDealt: 300,
  ApChanged: 0,
  CardPlayed: 0,
  CardMoved: 0,
  CardDrawn: 100,
  DrawOverflow: 150,
  Reshuffled: 350,
  CardResolved: 120,
  HitResolved: 180,
  HitFizzled: 80,
  BlockGained: 150,
  BlockCleared: 0,
  PoiseChanged: 0,
  Staggered: 300,
  FuseChanged: 0,
  IntentChanged: 0,
  UnitDied: 400,
  ReactionTriggered: 150,
  ChoiceRequested: 0,
  ChoiceMade: 0,
  EnemyActionStarted: 450,
  EnemyActionEnded: 150,
  TurnEnding: 300,
  ResolutionAborted: 0,
  BattleEnded: 0,
  StateRestored: 0,
};

/** 카드가 손패를 떠나 필드로 날아가는 연출. 다른 구역 이동은 즉시 처리한다. */
export const CARD_FLIGHT_MS = 250;

function baseDuration(table: Record<EventType, number>, e: GameEvent): number {
  if (e.type === 'CardMoved' && e.from === 'hand' && e.to === 'resolving') return CARD_FLIGHT_MS;
  return table[e.type];
}

/** 이 이벤트가 타임라인에 남아 있는 동안에는 새 입력을 받지 않는다. */
export const BARRIER_EVENTS: ReadonlySet<EventType> = new Set<EventType>([
  'EnemyActionStarted',
  'EnemyActionEnded',
  'ChoiceRequested',
  'TurnEnding',
  'TurnStarted',
  'HandDealt',
  'BattleEnded',
]);

interface Clip {
  batch: number;
  event: GameEvent;
  duration: number;
}

export interface PresenterOptions {
  /** 배속. 이후에 들어오는 연출 묶음에 적용된다. */
  speed?: number;
  /** 동시에 화면에 쌓일 수 있는 명령 묶음 수. 1이면 '연출 끝까지 잠금', 2면 '1장 예약 가능'. */
  maxBacklog?: number;
  durations?: Partial<Record<EventType, number>>;
}

export class Presenter {
  readonly engine: BattleEngine;
  readonly log: { at: number; event: GameEvent }[] = [];
  now = 0;
  speed: number;
  readonly maxBacklog: number;
  private readonly durations: Record<EventType, number>;
  private pending: Clip[] = [];
  private playing: Clip | null = null;
  private playingEndsAt = 0;
  private nextBatch = 1;
  private shownDead = new Set<string>();
  private choiceVisible = false;

  constructor(engine: BattleEngine, opts: PresenterOptions = {}) {
    this.engine = engine;
    this.speed = opts.speed ?? 1;
    this.maxBacklog = opts.maxBacklog ?? 2;
    this.durations = { ...BASE_DURATION_MS, ...opts.durations };
  }

  start(): void {
    this.enqueue(this.engine.start());
  }

  // ───────── 입력 ─────────

  inputOpen(): boolean {
    return this.engine.state.phase === 'playerMain' && this.gate() === null;
  }

  tryPlay(cardUid: string, targetId?: string): GateResult {
    const blocked = this.gate();
    if (blocked) return { ok: false, reason: blocked };
    const res = this.engine.dispatch({ type: 'PlayCard', cardUid, targetId });
    if (!res.ok) return res;
    this.enqueue(res.events);
    return { ok: true };
  }

  tryEndTurn(): GateResult {
    const blocked = this.gate();
    if (blocked) return { ok: false, reason: blocked };
    const res = this.engine.dispatch({ type: 'EndTurn' });
    if (!res.ok) return res;
    this.enqueue(res.events);
    return { ok: true };
  }

  /** 선택지는 연출이 ChoiceRequested까지 따라잡아 화면에 뜬 뒤에만 고를 수 있다. */
  tryChoose(index: number): GateResult {
    if (!this.choiceVisible) return { ok: false, reason: 'CHOICE_NOT_VISIBLE' };
    const res = this.engine.dispatch({ type: 'Choose', index });
    if (!res.ok) return res;
    this.choiceVisible = false;
    this.enqueue(res.events);
    return { ok: true };
  }

  /** 되돌리기: 남은 연출을 버리고 복원된 상태로 화면을 다시 그린다(이벤트 역재생이 아님). */
  undo(): boolean {
    const events = this.engine.undo();
    if (!events) return false;
    this.pending = [];
    this.playing = null;
    const s = this.engine.state;
    this.shownDead = new Set(s.units.filter((u) => !u.alive).map((u) => u.id));
    this.choiceVisible = s.phase === 'awaitingChoice';
    this.enqueue(events);
    return true;
  }

  // ───────── 화면 상태 질의 ─────────

  /** 지정 가능한 적 = Sim 기준 생존. 연출상 아직 서 있어도 확정 사망이면 고를 수 없다. */
  targetableEnemyIds(): string[] {
    return this.engine.targetableEnemyIds();
  }

  /** 화면에 아직 서 있는지(사망 연출이 시작되기 전이면 true). */
  displayedAlive(unitId: string): boolean {
    return !this.shownDead.has(unitId);
  }

  visibleChoice(): boolean {
    return this.choiceVisible;
  }

  backlog(): number {
    const batches = new Set(this.pending.map((c) => c.batch));
    if (this.playing) batches.add(this.playing.batch);
    return batches.size;
  }

  barrierPending(): boolean {
    if (this.playing && BARRIER_EVENTS.has(this.playing.event.type)) return true;
    return this.pending.some((c) => BARRIER_EVENTS.has(c.event.type));
  }

  idle(): boolean {
    return this.playing === null && this.pending.length === 0;
  }

  // ───────── 시간 ─────────

  /** 가상 시계를 ms만큼 진행하고, 그동안 재생이 시작된 이벤트를 돌려준다. */
  advance(ms: number): GameEvent[] {
    const until = this.now + ms;
    const started: GameEvent[] = [];
    for (;;) {
      if (!this.playing) {
        const next = this.pending.shift();
        if (!next) {
          this.now = until;
          break;
        }
        this.playing = next;
        this.playingEndsAt = this.now + next.duration;
        started.push(next.event);
        this.onClipStart(next.event);
      }
      if (this.playingEndsAt <= until) {
        this.now = this.playingEndsAt;
        this.playing = null;
        continue;
      }
      this.now = until;
      break;
    }
    return started;
  }

  /** 입력이 열리거나 더 재생할 것이 없을 때까지 진행하고 걸린 시간을 돌려준다. */
  runUntilInputOpen(): number {
    const begin = this.now;
    while (!this.inputOpen() && !this.idle()) {
      const step = this.playing ? this.playingEndsAt - this.now : 0;
      this.advance(step);
    }
    return this.now - begin;
  }

  runUntilIdle(): number {
    const begin = this.now;
    while (!this.idle()) this.advance(this.playing ? this.playingEndsAt - this.now : 0);
    return this.now - begin;
  }

  private gate(): GateReason | null {
    if (this.barrierPending()) return 'BARRIER_PENDING';
    if (this.backlog() >= this.maxBacklog) return 'BACKLOG_FULL';
    return null;
  }

  private enqueue(events: GameEvent[]): void {
    const batch = this.nextBatch++;
    for (const event of events) {
      this.pending.push({ batch, event, duration: baseDuration(this.durations, event) / this.speed });
    }
  }

  private onClipStart(event: GameEvent): void {
    this.log.push({ at: this.now, event });
    if (event.type === 'UnitDied') this.shownDead.add(event.unitId);
    if (event.type === 'ChoiceRequested') this.choiceVisible = true;
  }
}
