// 전투 시뮬레이션(Sim)의 데이터 모델.
// 원칙: BattleState는 직렬화 가능한 순수 데이터만 담는다(함수·클래스 인스턴스 없음).
// 그래서 structuredClone 한 번으로 되돌리기 스냅샷·리플레이·서버 재검증이 가능하다.

export type Side = 'ally' | 'enemy';
export type CardType = 'attack' | 'skill' | 'power';

/**
 * 대상 규칙. '상대편'은 효과를 일으킨 유닛 기준으로 정한다(아군 카드면 적, 적 행동이면 아군).
 * - enemy: 사용 시점에 지정한 상대 1명 (타겟팅 카드)
 * - allEnemies: 살아 있는 상대 전원
 * - randomEnemy: 타격마다 살아 있는 상대 중 무작위 1명
 * - self: 효과를 일으킨 유닛 자신
 * - all: 살아 있는 모든 유닛(테스트·특수 효과용)
 * - none: 대상 없음
 */
export type TargetRule = 'enemy' | 'allEnemies' | 'randomEnemy' | 'self' | 'all' | 'none';

export type CardTag = 'exhaust' | 'retain' | 'finisher' | 'unplayable';
export type Zone = 'draw' | 'hand' | 'resolving' | 'discard' | 'exhaust' | 'powers';

export type EffectSpec =
  | { op: 'damage'; amount: number; hits?: number; poise?: number; retarget?: boolean }
  | { op: 'block'; amount: number }
  | { op: 'draw'; count: number }
  | { op: 'gainAp'; amount: number }
  | { op: 'delayFuse'; amount: number }
  | { op: 'choose'; prompt: string; options: EffectSpec[][] }
  | { op: 'addReaction'; reactionId: string };

export interface CardDef {
  id: string;
  name: string;
  cost: number;
  type: CardType;
  target: TargetRule;
  tags?: CardTag[];
  effects: EffectSpec[];
  text: string;
}

/** 적 행동. timing='fuse'면 퓨즈가 0이 될 때(카드 사용 직후) 끼어들고, 'turnEnd'면 플레이어 턴 종료 시 행동한다. */
export interface EnemyActionDef {
  name: string;
  timing: 'fuse' | 'turnEnd';
  fuse?: number;
  target: TargetRule;
  effects: EffectSpec[];
}

export interface EnemyDef {
  id: string;
  name: string;
  hp: number;
  poise?: number;
  pattern: EnemyActionDef[];
}

export interface ReactionDef {
  id: string;
  name: string;
  on: GameEvent['type'];
  when?: { cardType?: CardType; sourceSide?: Side; targetSide?: Side };
  /** 턴당 발동 횟수 상한. 엔진이 중앙에서 집행한다(카드 스크립트에 맡기지 않는다). */
  limitPerTurn?: number;
  /** 같은 사건에 동시에 걸린 반응의 순서: priority 높은 순 → 소유자 위치 → 등록 순. */
  priority?: number;
  /** 'enemy'면 사건의 대상(targetId)을 그대로 대상으로 쓴다. 기본값 'self'. */
  target?: TargetRule;
  effects: EffectSpec[];
}

export interface Content {
  cards: Record<string, CardDef>;
  enemies: Record<string, EnemyDef>;
  reactions: Record<string, ReactionDef>;
}

export interface UnitState {
  id: string;
  name: string;
  side: Side;
  position: number;
  hp: number;
  maxHp: number;
  block: number;
  alive: boolean;
  // 적 전용
  enemyDefId?: string;
  patternIndex?: number;
  /** 다음 행동까지 남은 '손패 카드 사용' 수. null이면 이번 행동은 턴 종료 타이밍. */
  fuse?: number | null;
  poise?: number;
  maxPoise?: number;
  staggered?: boolean;
}

export interface CardInstance {
  uid: string;
  defId: string;
}

export interface OpContext {
  sourceId: string;
  cardUid?: string;
  targetRule: TargetRule;
  targetId?: string;
}

/** 해결 큐에 쌓이는 원자 연산. 전부 데이터라서 선택 대기 중에도 상태에 그대로 보관된다. */
export type Op =
  | { kind: 'effect'; effect: EffectSpec; ctx: OpContext }
  | { kind: 'hit'; ctx: OpContext; amount: number; poise: number; hitIndex: number; hitCount: number; retarget: boolean }
  | { kind: 'reaction'; reactionUid: string; eventTargetId?: string }
  | { kind: 'settleCard'; cardUid: string }
  | { kind: 'tempoTick'; amount: number }
  | { kind: 'runInterrupts' }
  | { kind: 'enemyTurnEndAction'; unitId: string }
  | { kind: 'enemyActionEnd'; unitId: string }
  | { kind: 'endTurn' }
  | { kind: 'startTurn' }
  | { kind: 'dealHand' };

export interface ReactionInstance {
  uid: string;
  reactionId: string;
  ownerId: string;
  usesThisTurn: number;
}

export interface PendingChoice {
  prompt: string;
  options: EffectSpec[][];
  ctx: OpContext;
}

export type Phase = 'playerMain' | 'awaitingChoice' | 'won' | 'lost';

export interface BattleState {
  turn: number;
  phase: Phase;
  ap: number;
  rng: number;
  units: UnitState[];
  cards: Record<string, CardInstance>;
  zones: Record<Zone, string[]>;
  reactions: ReactionInstance[];
  queue: Op[];
  pendingChoice: PendingChoice | null;
  interruptQueue: string[];
  /** 이번 턴에 사용 후 버린 더미로 간 카드(리셔플 제외 규칙용). */
  playedThisTurn: string[];
  cardsPlayedThisTurn: number;
  opsThisCommand: number;
  nextUid: number;
}

export interface RulesConfig {
  apPerTurn: number;
  drawPerTurn: number;
  handLimit: number;
  /** 명령 1회당 최대 원자 연산 수(무한 루프 가드). */
  maxOpsPerCommand: number;
  /** 경직(자세 0) 시 대상 퓨즈 +N. */
  staggerFuseDelay: number;
  /** 플레이어 턴 종료 시 모든 퓨즈 -N (0이면 카드 사용으로만 감소). */
  fuseTickAtTurnEnd: number;
  /** 리셔플 때 이번 턴에 사용한 카드도 섞을지. */
  reshuffleIncludesPlayedThisTurn: boolean;
  /** 같은 원자 연산에서 양쪽이 동시에 전멸했을 때의 판정. */
  simultaneousDeath: 'won' | 'lost';
}

export interface BattleSetup {
  seed: number;
  hero: { id: string; name: string; hp: number };
  deck: string[];
  enemies: { id: string; defId: string }[];
  /** false면 덱 순서 그대로 뽑는다(테스트·데모용). */
  shuffleDeck?: boolean;
}

export type GameEvent =
  | { type: 'TurnStarted'; turn: number }
  | { type: 'HandDealt'; turn: number }
  | { type: 'ApChanged'; from: number; to: number; reason: 'turnStart' | 'cardCost' | 'effect' }
  | { type: 'CardPlayed'; cardUid: string; defId: string; cardType: CardType; cost: number; targetId?: string }
  | { type: 'CardMoved'; cardUid: string; from: Zone; to: Zone }
  | { type: 'CardDrawn'; cardUid: string; defId: string }
  | { type: 'DrawOverflow'; cardUid: string; defId: string }
  | { type: 'Reshuffled'; count: number }
  | { type: 'CardResolved'; cardUid: string; defId: string }
  | { type: 'HitResolved'; sourceId: string; targetId: string; damage: number; blocked: number; hitIndex: number; hitCount: number; cardUid?: string }
  | { type: 'HitFizzled'; sourceId: string; hitIndex: number; hitCount: number; cardUid?: string }
  | { type: 'BlockGained'; unitId: string; amount: number }
  | { type: 'BlockCleared'; unitId: string }
  | { type: 'PoiseChanged'; unitId: string; from: number; to: number }
  | { type: 'Staggered'; unitId: string }
  | { type: 'FuseChanged'; unitId: string; from: number; to: number; reason: 'tick' | 'effect' | 'stagger' }
  | { type: 'IntentChanged'; unitId: string; actionName: string; timing: 'fuse' | 'turnEnd'; fuse: number | null }
  | { type: 'UnitDied'; unitId: string }
  | { type: 'ReactionTriggered'; reactionUid: string; reactionId: string; ownerId: string }
  | { type: 'ChoiceRequested'; prompt: string; optionCount: number }
  | { type: 'ChoiceMade'; index: number }
  | { type: 'EnemyActionStarted'; unitId: string; actionName: string; trigger: 'fuse' | 'turnEnd' }
  | { type: 'EnemyActionEnded'; unitId: string }
  | { type: 'TurnEnding'; turn: number }
  | { type: 'ResolutionAborted'; reason: 'opLimit' }
  | { type: 'BattleEnded'; result: 'won' | 'lost' }
  | { type: 'StateRestored' };

export type Command =
  | { type: 'PlayCard'; cardUid: string; targetId?: string }
  | { type: 'Choose'; index: number }
  | { type: 'EndTurn' };

export type RejectReason =
  | 'BATTLE_OVER'
  | 'AWAITING_CHOICE'
  | 'NO_PENDING_CHOICE'
  | 'NOT_IN_HAND'
  | 'UNPLAYABLE'
  | 'NOT_ENOUGH_AP'
  | 'TARGET_REQUIRED'
  | 'INVALID_TARGET'
  | 'BAD_CHOICE_INDEX';

export type DispatchResult = { ok: true; events: GameEvent[] } | { ok: false; reason: RejectReason };
