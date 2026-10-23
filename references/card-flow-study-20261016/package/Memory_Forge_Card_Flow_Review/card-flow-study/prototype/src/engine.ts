// 전투 시뮬레이션(Sim). 명령 하나를 받으면 선택 대기가 아닌 한 끝까지 즉시 해결하고,
// 일어난 일을 순서대로 담은 이벤트 목록을 돌려준다. 연출·입력 잠금은 presenter.ts의 몫이다.
//
// 해결 모델 요약 (design-spec.md §4~§6):
//   - 모든 처리는 state.queue의 원자 연산(Op)으로 진행된다.
//   - 원자 연산 하나가 끝날 때마다 ① 상태 판정(사망·승패) ② 반응 창(트리거된 반응을 큐 맨 앞에 삽입).
//   - 카드 사용 = 커밋(AP 지불, 손패→해결 구역) → 효과 → 정산(목적지 이동) → 템포 틱 → 인터럽트.

import { rngInt, shuffleInPlace } from './rng.ts';
import type {
  BattleSetup,
  BattleState,
  CardDef,
  Command,
  Content,
  DispatchResult,
  EffectSpec,
  GameEvent,
  Op,
  OpContext,
  ReactionDef,
  ReactionInstance,
  RejectReason,
  RulesConfig,
  UnitState,
  Zone,
} from './types.ts';

export const DEFAULT_RULES: RulesConfig = {
  apPerTurn: 3,
  drawPerTurn: 5,
  handLimit: 10,
  maxOpsPerCommand: 500,
  staggerFuseDelay: 1,
  fuseTickAtTurnEnd: 0,
  reshuffleIncludesPlayedThisTurn: true,
  simultaneousDeath: 'won',
};

interface Triggered {
  reaction: ReactionInstance;
  def: ReactionDef;
  eventTargetId?: string;
  order: number;
}

function eventTargetOf(e: GameEvent): string | undefined {
  if ('targetId' in e) return e.targetId;
  if ('unitId' in e) return e.unitId;
  return undefined;
}

function effectOps(effects: EffectSpec[], ctx: OpContext): Op[] {
  return effects.map((effect): Op => ({ kind: 'effect', effect, ctx }));
}

export class BattleEngine {
  readonly content: Content;
  readonly rules: RulesConfig;
  state: BattleState;
  private readonly history: BattleState[] = [];
  private out: GameEvent[] = [];
  private triggered: Triggered[] = [];
  private triggerOrder = 0;

  constructor(content: Content, setup: BattleSetup, rules: Partial<RulesConfig> = {}) {
    this.content = content;
    this.rules = { ...DEFAULT_RULES, ...rules };
    this.state = this.createState(setup);
  }

  // ───────────────────────── 공개 API ─────────────────────────

  /** 1턴을 시작한다(턴 시작 처리 + 첫 드로우). */
  start(): GameEvent[] {
    if (this.state.turn !== 0) throw new Error('battle already started');
    this.state.queue.unshift({ kind: 'startTurn' });
    this.drain();
    return this.takeEvents();
  }

  /** 상태를 바꾸지 않고 명령의 적법성만 검사한다. UI가 카드 하이라이트·대상 표시에 쓴다. */
  validate(cmd: Command): RejectReason | null {
    const s = this.state;
    if (s.phase === 'won' || s.phase === 'lost') return 'BATTLE_OVER';
    if (cmd.type === 'Choose') {
      if (s.phase !== 'awaitingChoice' || !s.pendingChoice) return 'NO_PENDING_CHOICE';
      const n = s.pendingChoice.options.length;
      return Number.isInteger(cmd.index) && cmd.index >= 0 && cmd.index < n ? null : 'BAD_CHOICE_INDEX';
    }
    if (s.phase === 'awaitingChoice') return 'AWAITING_CHOICE';
    if (cmd.type === 'EndTurn') return null;

    if (!s.zones.hand.includes(cmd.cardUid)) return 'NOT_IN_HAND';
    const def = this.cardDef(cmd.cardUid);
    if (def.tags?.includes('unplayable')) return 'UNPLAYABLE';
    if (def.cost > s.ap) return 'NOT_ENOUGH_AP';
    if (def.target === 'enemy') {
      if (cmd.targetId === undefined) return 'TARGET_REQUIRED';
      const t = s.units.find((u) => u.id === cmd.targetId);
      if (!t || !t.alive || t.side !== 'enemy') return 'INVALID_TARGET';
    }
    return null;
  }

  dispatch(cmd: Command): DispatchResult {
    const reason = this.validate(cmd);
    if (reason) return { ok: false, reason };

    // 되돌리기 지점은 '플레이어 결정으로 시작되는 명령' 직전. 선택(Choose)은 카드 해결의 일부라 제외.
    if (cmd.type !== 'Choose') this.history.push(structuredClone(this.state));
    this.state.opsThisCommand = 0;

    switch (cmd.type) {
      case 'PlayCard':
        this.commitCard(cmd.cardUid, cmd.targetId);
        break;
      case 'Choose':
        this.applyChoice(cmd.index);
        break;
      case 'EndTurn':
        this.state.queue.unshift({ kind: 'endTurn' });
        break;
    }
    this.drain();
    return { ok: true, events: this.takeEvents() };
  }

  canUndo(): boolean {
    return this.history.length > 0;
  }

  /** 마지막 PlayCard/EndTurn 직전 상태로 복원. 연출 쪽은 StateRestored를 받고 상태 전체를 다시 그린다. */
  undo(): GameEvent[] | null {
    const prev = this.history.pop();
    if (!prev) return null;
    this.state = prev;
    this.triggered = [];
    return [{ type: 'StateRestored' }];
  }

  /** 살아 있는(= 아직 지정 가능한) 적. 연출상 아직 쓰러지지 않았어도 Sim 기준으로 판단한다. */
  targetableEnemyIds(): string[] {
    return this.state.units.filter((u) => u.side === 'enemy' && u.alive).map((u) => u.id);
  }

  unit(id: string): UnitState {
    const u = this.state.units.find((x) => x.id === id);
    if (!u) throw new Error(`unknown unit ${id}`);
    return u;
  }

  cardDef(uid: string): CardDef {
    return this.content.cards[this.state.cards[uid].defId];
  }

  // ───────────────────────── 카드 사용 파이프라인 ─────────────────────────

  private commitCard(uid: string, targetId: string | undefined): void {
    const s = this.state;
    const def = this.cardDef(uid);
    const hero = this.hero();
    const target = def.target === 'enemy' ? targetId : undefined;

    // P2 커밋: 비용 지불과 카드 이동은 효과보다 먼저 확정된다.
    this.emit({ type: 'CardPlayed', cardUid: uid, defId: def.id, cardType: def.type, cost: def.cost, targetId: target });
    this.setAp(s.ap - def.cost, 'cardCost');
    this.moveCard(uid, 'hand', 'resolving');
    s.cardsPlayedThisTurn += 1;

    const ctx: OpContext = { sourceId: hero.id, cardUid: uid, targetRule: def.target, targetId: target };
    const ops = effectOps(def.effects, ctx); // P3 효과
    ops.push(
      { kind: 'settleCard', cardUid: uid }, // P4 정산
      { kind: 'tempoTick', amount: 1 }, // P5 템포 틱
      { kind: 'runInterrupts' }, // P5 인터럽트
    );
    if (def.tags?.includes('finisher')) ops.push({ kind: 'endTurn' });
    s.queue.unshift(...ops);
    this.afterStep(); // CardPlayed에 걸린 반응은 카드 효과보다 먼저 해결된다.
  }

  private applyChoice(index: number): void {
    const s = this.state;
    const pc = s.pendingChoice!;
    s.pendingChoice = null;
    s.phase = 'playerMain';
    this.emit({ type: 'ChoiceMade', index });
    s.queue.unshift(...effectOps(pc.options[index], pc.ctx));
    this.afterStep();
  }

  // ───────────────────────── 해결 루프 ─────────────────────────

  private drain(): void {
    const s = this.state;
    while (s.queue.length > 0 && s.phase === 'playerMain') {
      s.opsThisCommand += 1;
      if (s.opsThisCommand > this.rules.maxOpsPerCommand) {
        this.abortResolution();
        return;
      }
      const op = s.queue.shift()!;
      this.execute(op);
      this.afterStep();
    }
  }

  /** 원자 연산 하나가 끝난 직후: 상태 판정 → 반응 창. */
  private afterStep(): void {
    this.checkStateBased();
    if (this.isOver()) {
      this.triggered = [];
      return;
    }
    this.flushTriggers();
  }

  private execute(op: Op): void {
    switch (op.kind) {
      case 'effect':
        return this.applyEffect(op.effect, op.ctx);
      case 'hit':
        return this.applyHit(op);
      case 'reaction':
        return this.runReaction(op.reactionUid, op.eventTargetId);
      case 'settleCard':
        return this.settleCard(op.cardUid);
      case 'tempoTick':
        return this.tempoTick(op.amount);
      case 'runInterrupts':
        return this.runNextInterrupt();
      case 'enemyTurnEndAction': {
        const u = this.unit(op.unitId);
        if (u.alive) this.beginEnemyAction(u, 'turnEnd');
        return;
      }
      case 'enemyActionEnd':
        return this.finishEnemyAction(op.unitId);
      case 'endTurn':
        return this.endTurn();
      case 'startTurn':
        return this.startTurn();
      case 'dealHand':
        return this.dealHand();
    }
  }

  private applyEffect(effect: EffectSpec, ctx: OpContext): void {
    const s = this.state;
    const source = this.unit(ctx.sourceId);
    switch (effect.op) {
      case 'damage': {
        // 다단 히트는 타격마다 독립 원자 연산 → 타격 사이에 반응·사망 판정이 끼어든다.
        const hits = Math.max(1, effect.hits ?? 1);
        const ops: Op[] = [];
        for (let i = 0; i < hits; i++) {
          ops.push({
            kind: 'hit',
            ctx,
            amount: effect.amount,
            poise: effect.poise ?? 0,
            hitIndex: i,
            hitCount: hits,
            retarget: effect.retarget ?? false,
          });
        }
        s.queue.unshift(...ops);
        return;
      }
      case 'block':
        this.gainBlock(source, effect.amount);
        return;
      case 'draw':
        for (let i = 0; i < effect.count; i++) this.drawOne();
        return;
      case 'gainAp':
        if (source.side === 'ally') this.setAp(s.ap + effect.amount, 'effect');
        return;
      case 'delayFuse':
        for (const t of this.resolveTargets(ctx, source, false)) this.changeFuse(t, effect.amount, 'effect');
        return;
      case 'choose':
        // 해결을 멈추고 남은 큐를 상태에 둔 채 Choose 명령을 기다린다.
        s.pendingChoice = { prompt: effect.prompt, options: effect.options, ctx };
        s.phase = 'awaitingChoice';
        this.emit({ type: 'ChoiceRequested', prompt: effect.prompt, optionCount: effect.options.length });
        return;
      case 'addReaction':
        s.reactions.push({ uid: `r${s.nextUid++}`, reactionId: effect.reactionId, ownerId: source.id, usesThisTurn: 0 });
        return;
    }
  }

  private applyHit(op: Extract<Op, { kind: 'hit' }>): void {
    const source = this.unit(op.ctx.sourceId);
    const targets = source.alive ? this.resolveTargets(op.ctx, source, op.retarget) : [];
    if (targets.length === 0) {
      this.emit({ type: 'HitFizzled', sourceId: source.id, hitIndex: op.hitIndex, hitCount: op.hitCount, cardUid: op.ctx.cardUid });
      return;
    }
    for (const t of targets) this.dealHit(source, t, op);
  }

  private dealHit(source: UnitState, target: UnitState, op: Extract<Op, { kind: 'hit' }>): void {
    const blocked = Math.min(target.block, op.amount);
    target.block -= blocked;
    const damage = op.amount - blocked;
    target.hp = Math.max(0, target.hp - damage);
    this.emit({
      type: 'HitResolved',
      sourceId: source.id,
      targetId: target.id,
      damage,
      blocked,
      hitIndex: op.hitIndex,
      hitCount: op.hitCount,
      cardUid: op.ctx.cardUid,
    });

    if (op.poise > 0 && target.hp > 0 && target.poise !== undefined && !target.staggered) {
      const from = target.poise;
      target.poise = Math.max(0, from - op.poise);
      this.emit({ type: 'PoiseChanged', unitId: target.id, from, to: target.poise });
      if (target.poise === 0) {
        target.staggered = true;
        this.emit({ type: 'Staggered', unitId: target.id });
        this.changeFuse(target, this.rules.staggerFuseDelay, 'stagger');
      }
    }
  }

  private resolveTargets(ctx: OpContext, source: UnitState, retarget: boolean): UnitState[] {
    const s = this.state;
    const foes = s.units.filter((u) => u.alive && u.side !== source.side);
    switch (ctx.targetRule) {
      case 'enemy': {
        const t = ctx.targetId !== undefined ? this.unit(ctx.targetId) : undefined;
        if (t && t.alive) return [t];
        // 지정 대상이 이미 죽었다: 기본은 불발(fizzle), retarget이면 무작위 재지정.
        return retarget && foes.length > 0 ? [foes[rngInt(s, foes.length)]] : [];
      }
      case 'allEnemies':
        return foes;
      case 'randomEnemy':
        return foes.length > 0 ? [foes[rngInt(s, foes.length)]] : [];
      case 'self':
        return source.alive ? [source] : [];
      case 'all':
        return s.units.filter((u) => u.alive);
      case 'none':
        return [];
    }
  }

  private runReaction(reactionUid: string, eventTargetId: string | undefined): void {
    const s = this.state;
    const r = s.reactions.find((x) => x.uid === reactionUid);
    if (!r) return;
    const owner = this.unit(r.ownerId);
    if (!owner.alive) return;
    const def = this.content.reactions[r.reactionId];
    this.emit({ type: 'ReactionTriggered', reactionUid: r.uid, reactionId: def.id, ownerId: r.ownerId });
    const targetRule = def.target ?? 'self';
    const ctx: OpContext = { sourceId: owner.id, targetRule, targetId: targetRule === 'enemy' ? eventTargetId : undefined };
    s.queue.unshift(...effectOps(def.effects, ctx));
  }

  private settleCard(uid: string): void {
    const def = this.cardDef(uid);
    const to: Zone = def.type === 'power' ? 'powers' : def.tags?.includes('exhaust') ? 'exhaust' : 'discard';
    this.moveCard(uid, 'resolving', to);
    if (to === 'discard') this.state.playedThisTurn.push(uid);
    this.emit({ type: 'CardResolved', cardUid: uid, defId: def.id });
  }

  // ───────────────────────── 템포(퓨즈)와 적 행동 ─────────────────────────

  /** 손패 카드 1장 사용 = 모든 적 퓨즈 -1. 인터럽트 대상은 '틱 시점'에만 모은다. */
  private tempoTick(amount: number): void {
    const s = this.state;
    for (const e of this.livingEnemies()) {
      if (e.fuse === null || e.fuse === undefined) continue;
      this.changeFuse(e, -amount, 'tick');
      if (e.fuse === 0 && !s.interruptQueue.includes(e.id)) s.interruptQueue.push(e.id);
    }
  }

  private runNextInterrupt(): void {
    const s = this.state;
    const id = s.interruptQueue.shift();
    if (id === undefined) return;
    s.queue.unshift({ kind: 'runInterrupts' }); // 이 적의 행동이 끝난 뒤 다음 인터럽트로
    const e = this.unit(id);
    if (e.alive) this.beginEnemyAction(e, 'fuse');
  }

  private beginEnemyAction(e: UnitState, trigger: 'fuse' | 'turnEnd'): void {
    const action = this.content.enemies[e.enemyDefId!].pattern[e.patternIndex!];
    this.emit({ type: 'EnemyActionStarted', unitId: e.id, actionName: action.name, trigger });
    const ctx: OpContext = {
      sourceId: e.id,
      targetRule: action.target,
      targetId: action.target === 'enemy' ? this.hero().id : undefined,
    };
    const ops = effectOps(action.effects, ctx);
    ops.push({ kind: 'enemyActionEnd', unitId: e.id });
    this.state.queue.unshift(...ops);
  }

  private finishEnemyAction(id: string): void {
    const e = this.unit(id);
    this.emit({ type: 'EnemyActionEnded', unitId: id });
    if (!e.alive) return;
    const pattern = this.content.enemies[e.enemyDefId!].pattern;
    e.patternIndex = (e.patternIndex! + 1) % pattern.length;
    const next = pattern[e.patternIndex];
    e.fuse = next.timing === 'fuse' ? (next.fuse ?? 1) : null;
    this.emit({ type: 'IntentChanged', unitId: id, actionName: next.name, timing: next.timing, fuse: e.fuse });
  }

  private changeFuse(u: UnitState, delta: number, reason: 'tick' | 'effect' | 'stagger'): void {
    if (u.fuse === null || u.fuse === undefined || delta === 0) return;
    const from = u.fuse;
    u.fuse = Math.max(0, from + delta);
    if (u.fuse !== from) this.emit({ type: 'FuseChanged', unitId: u.id, from, to: u.fuse, reason });
  }

  // ───────────────────────── 턴 경계 ─────────────────────────

  private endTurn(): void {
    const s = this.state;
    this.emit({ type: 'TurnEnding', turn: s.turn });
    for (const uid of [...s.zones.hand]) {
      if (!this.cardDef(uid).tags?.includes('retain')) this.moveCard(uid, 'hand', 'discard');
    }
    const ops: Op[] = [];
    for (const e of this.livingEnemies()) {
      if (e.fuse === null) ops.push({ kind: 'enemyTurnEndAction', unitId: e.id });
    }
    if (this.rules.fuseTickAtTurnEnd > 0) {
      ops.push({ kind: 'tempoTick', amount: this.rules.fuseTickAtTurnEnd }, { kind: 'runInterrupts' });
    }
    ops.push({ kind: 'startTurn' });
    s.queue.unshift(...ops);
  }

  /** 드로우 전 창(TurnStarted 반응은 드로우보다 먼저 해결된다). */
  private startTurn(): void {
    const s = this.state;
    s.turn += 1;
    s.cardsPlayedThisTurn = 0;
    s.playedThisTurn = [];
    for (const r of s.reactions) r.usesThisTurn = 0;
    this.emit({ type: 'TurnStarted', turn: s.turn });
    for (const u of s.units) {
      if (u.alive && u.block > 0) {
        u.block = 0;
        this.emit({ type: 'BlockCleared', unitId: u.id });
      }
    }
    for (const e of this.livingEnemies()) {
      if (e.staggered) {
        const from = e.poise ?? 0;
        e.staggered = false;
        e.poise = e.maxPoise ?? 0;
        this.emit({ type: 'PoiseChanged', unitId: e.id, from, to: e.poise });
      }
    }
    this.setAp(this.rules.apPerTurn, 'turnStart');
    s.queue.unshift({ kind: 'dealHand' });
  }

  /** 드로우 후 창(HandDealt 반응은 첫 입력보다 먼저 해결된다). */
  private dealHand(): void {
    for (let i = 0; i < this.rules.drawPerTurn; i++) this.drawOne();
    this.emit({ type: 'HandDealt', turn: this.state.turn });
  }

  // ───────────────────────── 카드 구역 ─────────────────────────

  private drawOne(): void {
    const s = this.state;
    if (s.zones.draw.length === 0) this.reshuffle();
    const uid = s.zones.draw[0];
    if (uid === undefined) return;
    const defId = s.cards[uid].defId;
    if (s.zones.hand.length >= this.rules.handLimit) {
      this.moveCard(uid, 'draw', 'discard');
      this.emit({ type: 'DrawOverflow', cardUid: uid, defId });
    } else {
      this.moveCard(uid, 'draw', 'hand');
      this.emit({ type: 'CardDrawn', cardUid: uid, defId });
    }
  }

  private reshuffle(): void {
    const s = this.state;
    const excluded = this.rules.reshuffleIncludesPlayedThisTurn ? [] : s.playedThisTurn;
    const pool = s.zones.discard.filter((uid) => !excluded.includes(uid));
    if (pool.length === 0) return;
    s.zones.discard = s.zones.discard.filter((uid) => excluded.includes(uid));
    shuffleInPlace(s, pool);
    s.zones.draw.push(...pool);
    this.emit({ type: 'Reshuffled', count: pool.length });
  }

  private moveCard(uid: string, from: Zone, to: Zone): void {
    const zone = this.state.zones[from];
    const i = zone.indexOf(uid);
    if (i < 0) throw new Error(`card ${uid} is not in ${from}`);
    zone.splice(i, 1);
    this.state.zones[to].push(uid);
    this.emit({ type: 'CardMoved', cardUid: uid, from, to });
  }

  // ───────────────────────── 판정·반응·보조 ─────────────────────────

  private checkStateBased(): void {
    const s = this.state;
    if (this.isOver()) return;
    for (const u of s.units) {
      if (u.alive && u.hp <= 0) {
        u.alive = false;
        s.interruptQueue = s.interruptQueue.filter((id) => id !== u.id);
        this.emit({ type: 'UnitDied', unitId: u.id });
      }
    }
    const enemiesDead = s.units.every((u) => u.side !== 'enemy' || !u.alive);
    const heroDead = !this.hero().alive;
    let result: 'won' | 'lost' | null = null;
    if (enemiesDead && heroDead) result = this.rules.simultaneousDeath;
    else if (enemiesDead) result = 'won';
    else if (heroDead) result = 'lost';
    if (result) {
      s.phase = result;
      s.queue = [];
      s.interruptQueue = [];
      s.pendingChoice = null;
      this.emit({ type: 'BattleEnded', result });
    }
  }

  private emit(e: GameEvent): void {
    this.out.push(e);
    if (this.isOver()) return;
    for (const r of this.state.reactions) {
      const def = this.content.reactions[r.reactionId];
      if (def.on !== e.type || !this.reactionMatches(def, e)) continue;
      if (def.limitPerTurn !== undefined && r.usesThisTurn >= def.limitPerTurn) continue;
      r.usesThisTurn += 1;
      this.triggered.push({ reaction: r, def, eventTargetId: eventTargetOf(e), order: this.triggerOrder++ });
    }
  }

  private reactionMatches(def: ReactionDef, e: GameEvent): boolean {
    const w = def.when;
    if (!w) return true;
    if (w.cardType && !(e.type === 'CardPlayed' && e.cardType === w.cardType)) return false;
    if (w.sourceSide) {
      const id = 'sourceId' in e ? e.sourceId : undefined;
      if (id === undefined || this.unit(id).side !== w.sourceSide) return false;
    }
    if (w.targetSide) {
      const id = eventTargetOf(e);
      if (id === undefined || this.unit(id).side !== w.targetSide) return false;
    }
    return true;
  }

  /** 반응 창: 이번 원자 연산 동안 걸린 반응을 큐 맨 앞에 넣는다(깊이 우선). */
  private flushTriggers(): void {
    if (this.triggered.length === 0) return;
    const batch = this.triggered;
    this.triggered = [];
    batch.sort(
      (a, b) =>
        (b.def.priority ?? 0) - (a.def.priority ?? 0) ||
        this.unit(a.reaction.ownerId).position - this.unit(b.reaction.ownerId).position ||
        a.order - b.order,
    );
    this.state.queue.unshift(
      ...batch.map((t): Op => ({ kind: 'reaction', reactionUid: t.reaction.uid, eventTargetId: t.eventTargetId })),
    );
  }

  /** 루프 가드: 남은 해결을 버리고 해결 중이던 카드를 버린 더미로 보내 상태를 일관되게 만든다. */
  private abortResolution(): void {
    const s = this.state;
    s.queue = [];
    s.interruptQueue = [];
    this.emit({ type: 'ResolutionAborted', reason: 'opLimit' });
    for (const uid of [...s.zones.resolving]) this.moveCard(uid, 'resolving', 'discard');
    this.triggered = [];
  }

  private gainBlock(u: UnitState, amount: number): void {
    if (!u.alive || amount <= 0) return;
    u.block += amount;
    this.emit({ type: 'BlockGained', unitId: u.id, amount });
  }

  private setAp(to: number, reason: 'turnStart' | 'cardCost' | 'effect'): void {
    const from = this.state.ap;
    this.state.ap = Math.max(0, to);
    if (from !== this.state.ap) this.emit({ type: 'ApChanged', from, to: this.state.ap, reason });
  }

  private hero(): UnitState {
    return this.state.units[0];
  }

  private livingEnemies(): UnitState[] {
    return this.state.units.filter((u) => u.side === 'enemy' && u.alive);
  }

  private isOver(): boolean {
    return this.state.phase === 'won' || this.state.phase === 'lost';
  }

  private takeEvents(): GameEvent[] {
    const events = this.out;
    this.out = [];
    return events;
  }

  private createState(setup: BattleSetup): BattleState {
    const state: BattleState = {
      turn: 0,
      phase: 'playerMain',
      ap: 0,
      rng: setup.seed | 0,
      units: [],
      cards: {},
      zones: { draw: [], hand: [], resolving: [], discard: [], exhaust: [], powers: [] },
      reactions: [],
      queue: [],
      pendingChoice: null,
      interruptQueue: [],
      playedThisTurn: [],
      cardsPlayedThisTurn: 0,
      opsThisCommand: 0,
      nextUid: 1,
    };
    state.units.push({
      id: setup.hero.id,
      name: setup.hero.name,
      side: 'ally',
      position: 0,
      hp: setup.hero.hp,
      maxHp: setup.hero.hp,
      block: 0,
      alive: true,
    });
    setup.enemies.forEach((spec, i) => {
      const def = this.content.enemies[spec.defId];
      if (!def) throw new Error(`unknown enemy ${spec.defId}`);
      const first = def.pattern[0];
      state.units.push({
        id: spec.id,
        name: def.name,
        side: 'enemy',
        position: i + 1,
        hp: def.hp,
        maxHp: def.hp,
        block: 0,
        alive: true,
        enemyDefId: def.id,
        patternIndex: 0,
        fuse: first.timing === 'fuse' ? (first.fuse ?? 1) : null,
        poise: def.poise,
        maxPoise: def.poise,
        staggered: false,
      });
    });
    for (const defId of setup.deck) {
      if (!this.content.cards[defId]) throw new Error(`unknown card ${defId}`);
      const uid = `c${state.nextUid++}`;
      state.cards[uid] = { uid, defId };
      state.zones.draw.push(uid);
    }
    if (setup.shuffleDeck !== false) shuffleInPlace(state, state.zones.draw);
    return state;
  }
}
