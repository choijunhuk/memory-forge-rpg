// §11.5/§11.6/§11.7/§11.13/§13.3: content-mode oracle for the C# port (core/MemoryForge.Core/ContentModes.cs and Battle.Modes.cs).
// Each scenario is an ordered op script run against engine/content-modes.js; the C# check replays the same ops and compares results,
// errors, state hashes and full checkpoint states. Fixture waves, rotations, star thresholds and daily pools are TEST-ONLY stand-ins
// for DECISION_PENDING data, kept as named modes variants here; data/content_modes.json keeps them null.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { mkdtempSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync, gunzipSync } from 'node:zlib';
import { isDeepStrictEqual } from 'node:util';
import { stateHash } from './gen_traces.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const require = createRequire(import.meta.url);
const MF = require('../engine/rules-core.js');
const CM = require('../engine/content-modes.js');
const LATE = require('../proposals/late_enemies.cjs');
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const schema = 'mf-content-modes-v1';
const scope = 'Actual content-mode op scripts: reset clocks, party setup, stage scaling, rift_break data, ModeBattle waves and six rift rules, ' +
  'dungeon/weekly boss/record rift/daily ledgers; DECISION_PENDING data only as TEST-ONLY modes variants';
export const fixturePath = join(root, 'engine/tests/vectors/content_modes_vectors.json.gz');

function sources() {
  return Object.fromEntries(Object.entries({
    data: 'data/game_data.json', contentModes: 'data/content_modes.json', engine: 'engine/rules-core.js', contentEngine: 'engine/content-modes.js',
    lateEnemies: 'proposals/late_enemies.cjs', canonical: 'tools/gen_traces.mjs', generator: 'tools/gen_content_modes_vectors.mjs',
  }).map(([key, path]) => [key, { path, sha256: sha256(readFileSync(join(root, path))) }]));
}
const loadedSources = sources();
const DATA = JSON.parse(readFileSync(join(root, 'data/game_data.json'), 'utf8'));
const MODES = JSON.parse(readFileSync(join(root, 'data/content_modes.json'), 'utf8'));
const ALL = DATA.characters.map(c => c.id);
const KST = (y, mo, d, h, mi = 0) => Date.UTC(y, mo - 1, d, h - 9, mi);
const account = (extra = {}) => ({ level: 60, stamina: 240, owned: [...ALL], ...extra });
const team = (...ids) => ids.map(charId => ({ charId, level: 60 }));
const RULES = ['rift_reserve', 'rift_break', 'rift_ink', 'rift_debt', 'rift_bloom', 'rift_echo'];

// ───── data variants: game data patches the C# host applies the same way ─────
const dataPatches = {
  live: {},
  weakBell: { enemyHp: { b_bell: 6 } }, // TEST-ONLY weakened boss so a real fight reaches the weekly settle
  late: { overlayEnemies: clone(LATE.enemies) }, // TEST-ONLY proposals/late_enemies.cjs overlay (b_nameless has a phase)
};
function buildData(patch) {
  const d = clone(DATA);
  for (const [id, hp] of Object.entries(patch.enemyHp || {})) d.enemies.find(e => e.id === id).hp = hp;
  if (patch.overlayEnemies) {
    const ids = new Set(d.enemies.map(e => e.id));
    assert.ok(patch.overlayEnemies.every(e => !ids.has(e.id)), 'Overlay would replace live enemies');
    d.enemies.push(...clone(patch.overlayEnemies));
  }
  return d;
}
const DATA_V = Object.fromEntries(Object.entries(dataPatches).map(([k, p]) => [k, buildData(p)]));
assert.deepEqual(DATA_V.late.enemies, LATE.withLateEnemies(DATA).enemies, 'Late overlay differs from the proposal');

// ───── modes variants (TEST-ONLY except shipped) ─────
const page3 = () => [['e_page'], ['e_page'], ['e_page']];
const modesWith = fn => { const m = clone(MODES); fn(m); return m; };
const rotation = [
  { rules: [...RULES], encounters: RULES.map(page3) },
  { rules: [...RULES].reverse(), encounters: [
    [['e_moth'], ['e_cog', 'e_page'], ['e_smudge']], [['e_seed', 'e_seed'], ['e_lantern'], ['e_page', 'e_page']], [['e_inkswarm'], ['e_moth'], ['e_cog']],
    [['e_hound'], ['e_page'], ['e_moth', 'e_seed']], [['e_lantern'], ['e_smudge', 'e_page'], ['e_inkswarm']], [['e_moth'], ['e_page'], ['e_cog']]] },
  null,
];
const modesVariants = {
  shipped: MODES,
  resetsAlt: modesWith(x => { Object.assign(x.resets, { utcOffsetMinutes: -300, dailyHour: 0, weeklyWeekday: 0, weeklyHour: 23 }); x.rift.cycleStartHour = 0; }),
  resetsOdd: modesWith(x => { Object.assign(x.resets, { utcOffsetMinutes: 330, dailyHour: 26, weeklyWeekday: 9, weeklyHour: -3 }); }),
  dungeonWaves: modesWith(x => {
    x.dungeon.waves.encounters = {
      dg_ink: { 1: [['e_page'], ['e_page', 'e_page']], 6: [['e_moth', 'e_cog'], ['e_hound'], ['e_lantern', 'e_page']] },
      dg_credit: { 2: [['e_smudge']] }, dg_oil: { 3: [['e_scribe'], ['e_seed', 'e_seed']] }, dg_polish: { 1: [['e_page']] },
      dg_seal: { 1: [['e_page'], ['e_page'], ['e_page'], ['e_page']], 2: [['e_page'], []], 3: [] }, dg_book: { 1: [['e_unknown']] },
    };
  }),
  riftPending: modesWith(x => { x.rift.rotation.cycles = clone(rotation); }),
  riftStrict: modesWith(x => { x.rift.rotation.cycles = clone(rotation); x.rift.stars.maxTotalTurns = 0; x.rift.stars.minHpPct = 101; }),
  riftLenient: modesWith(x => { x.rift.rotation.cycles = clone(rotation); x.rift.stars.maxTotalTurns = 99; x.rift.stars.minHpPct = 1; }),
  riftMid: modesWith(x => { x.rift.rotation.cycles = clone(rotation); x.rift.stars.maxTotalTurns = 9; x.rift.stars.minHpPct = 70; }),
  riftTarget: modesWith(x => { x.rift.rotation.cycles = clone(rotation); x.rift.activeTeamPolicy = 'target'; x.rift.stars.maxTotalTurns = 12; x.rift.stars.minHpPct = 40; }),
  riftBad: modesWith(x => {
    x.rift.rotation.cycles = [{ rules: ['source', 'rift_x', 'rift_ink', 'rift_debt', null, 'rift_echo'],
      encounters: [page3(), page3(), [['e_page'], ['e_page']], [['e_page'], [], ['e_page']], page3(), null] }, { rules: null, encounters: null }];
  }),
  dailyTarget: modesWith(x => {
    x.daily.targetPool.tasks = ['login', 'staminaSpent', 'battleWin', 'affinity', 'contentClear', 'levelUp', 'lobbyTalk'].map((event, i) => ({ id: `t${i}`, event, n: i % 3 + 1, pts: 20 }));
  }),
  riftUnknownEnemy: modesWith(x => { x.rift.rotation.cycles = [{ rules: [...RULES], encounters: RULES.map(() => [['e_page'], ['e_nope'], ['e_page']]) }]; }),
  dailyTargetDefault: modesWith(x => {
    x.daily.activePolicy = 'target';
    x.daily.targetPool.tasks = [{ id: 'a', event: 'login', n: 1, pts: 40 }, { id: 'b', event: 'battleWin', n: 2, pts: 40 }, { id: 'c', event: 'contentClear', n: 1, pts: 40 }];
  }),
};
assert.equal(MODES.dungeon.waves.encounters, null); assert.equal(MODES.rift.rotation.cycles, null);
assert.equal(MODES.rift.stars.maxTotalTurns, null); assert.equal(MODES.rift.stars.minHpPct, null); assert.equal(MODES.daily.targetPool.tasks, null);

// ───── witnesses: every rule branch the C# port must reproduce is exercised by at least one recorded command ─────
const witness = {};
const bump = key => { witness[key] = (witness[key] || 0) + 1; };
function instrument() {
  const P = CM.ModeBattle.prototype; const saved = { dmgBonusBps: P.dmgBonusBps, scaleAtk: P.scaleAtk, startTurn: P.startTurn, onGrowthCardResolved: P.onGrowthCardResolved, play: P.play, checkEnd: P.checkEnd };
  P.dmgBonusBps = function (ctx, enemy) {
    // RULES_NOW_PLAY: rift_reserve's reserved bonus now keys on a held (보존) card, so its witness is reserveHeld.
    if (this.rule('rift_reserve')) bump(ctx.isUlt ? 'reserveUlt' : ctx.held ? 'reserveHeld' : 'reserveImmediate');
    if (this.rule('rift_ink') && this.counterVal('exhausted', ctx) > 0) bump('inkExhausted');
    if (this.rule('rift_debt') && this.debtTotal() > 0) bump('debtCardBonus');
    return saved.dmgBonusBps.call(this, ctx, enemy);
  };
  P.scaleAtk = function (n) { if (this.rule('rift_debt') && this.debtTotal() > 0 && n > 0) bump('debtRaw'); return saved.scaleAtk.call(this, n); };
  P.startTurn = function (first) { if (this.rule('rift_ink')) bump('inkInsert'); return saved.startTurn.call(this, first); };
  P.onGrowthCardResolved = function (inst) { if (this.rule('rift_bloom')) bump('bloomBlock'); return saved.onGrowthCardResolved.call(this, inst); };
  P.play = function (uid, mode, target, picks) {
    const had = (this.s.turnFlags.riftChain || []).length; const result = saved.play.call(this, uid, mode, target, picks);
    if (this.rule('rift_echo') && !this.isOver()) { if (had && !this.s.turnFlags.riftChain.length) bump('echoChainCleared'); }
    return result;
  };
  P.checkEnd = function () { const w = this.s.mode && this.s.mode.wave; const r = saved.checkEnd.call(this); if (this.s.mode && this.s.mode.wave !== w) bump('waveSpawn'); return r; };
  return () => Object.assign(P, saved);
}

// ───── op interpreter ─────
const M = name => { assert.ok(modesVariants[name], `unknown modes ${name}`); return modesVariants[name]; };
const D = name => { assert.ok(DATA_V[name], `unknown data ${name}`); return DATA_V[name]; };
function resolveRef(value, slots) {
  if (!value || typeof value !== 'object' || Array.isArray(value) || !('ref' in value)) return clone(value);
  const holder = slots[value.ref]; assert.ok(holder !== undefined, `unknown slot ${value.ref}`);
  const out = clone(value.path === 'state' ? holder.s : value.path ? holder[value.path] : holder);
  if (value.merge) Object.assign(out, clone(value.merge));
  const p = value.patch;
  if (p) {
    if ('schema' in p) out.schema = p.schema;
    if ('phase' in p) out.phase = p.phase;
    if (p.result) out.result = { phase: out.phase, turn: p.result.turn ?? out.turn, hp: p.result.hp ?? out.hp };
  }
  return out;
}
const checkpoint = (s, full) => ({ stateHash: stateHash(s), logHash: stateHash(s.log), evHash: stateHash(s.ev), ...(full ? { state: clone(s) } : {}) });
const runCheckpoint = (s, full) => ({ runHash: stateHash(s), battleLogHash: s.battle ? stateHash(s.battle.log) : null, ...(full ? { state: clone(s) } : {}) });

function execute(op, slots, full) {
  const R = v => resolveRef(v, slots);
  switch (op.op) {
    case 'time':
      if (op.fn === 'dayKey') return CM.Time.dayKey(M(op.modes), op.nowMs);
      if (op.fn === 'weekKey') return CM.Time.weekKey(M(op.modes), op.nowMs);
      return CM.Time.riftCycle(D(op.data), M(op.modes), op.season, op.nowMs);
    case 'partySetup': return CM.partySetup(MF.indexData(D(op.data)), R(op.party), R(op.memory), clone(op.owned));
    case 'stageDiff': return CM.stageDiff(MF.indexData(D(op.data)), clone(op.row));
    case 'modeData': {
      const d = D(op.data); const out = CM.modeData(d, clone(op.mode));
      return { same: out === d, brokenDamageBps: out.rules.brokenDamageBps, enemies: out.enemies.map(e => ({ id: e.id, brk: e.brk, phaseBrk: e.phase ? e.phase.brk ?? null : null })) };
    }
    case 'battleCreate': { const b = CM.ModeBattle.create(D(op.data), clone(op.setup), clone(op.mode)); slots[op.slot] = b; return checkpoint(b.s, true); }
    case 'battleCmd': { const b = slots[op.slot]; const response = clone(b.command(clone(op.request))); return { response, ...checkpoint(b.s, full) }; }
    case 'dungeonReward': return CM.Dungeon.reward(D(op.data), op.dungeonId, op.tier);
    case 'dungeonStars': return CM.Dungeon.stars(M(op.modes), clone(op.battleResult), op.maxHp);
    case 'dungeonStart': { const b = CM.Dungeon.start(D(op.data), M(op.modes), R(op.account), R(op.opts)); slots[op.slot] = b; return checkpoint(b.s, true); }
    case 'dungeonSettle': return CM.Dungeon.settle(D(op.data), M(op.modes), R(op.account), R(op.battle));
    case 'dungeonSweep': return CM.Dungeon.sweep(D(op.data), M(op.modes), R(op.account), clone(op.args));
    case 'weeklyStart': { const b = CM.WeeklyBoss.start(D(op.data), M(op.modes), R(op.account), R(op.opts)); slots[op.slot] = b; return checkpoint(b.s, true); }
    case 'weeklySettle': return CM.WeeklyBoss.settle(D(op.data), M(op.modes), R(op.account), R(op.battle), op.nowMs);
    case 'riftCreate': { const run = CM.RiftRun.create(D(op.data), M(op.modes), R(op.account), R(op.opts)); slots[op.slot] = run; return runCheckpoint(run.s, true); }
    case 'riftNext': { const run = slots[op.slot]; run.next(); return runCheckpoint(run.s, true); }
    case 'riftCmd': { const run = slots[op.slot]; const response = clone(run.battleCmd(op.kind, clone(op.args))); return { response, ...runCheckpoint(run.s, full) }; }
    case 'riftStars': return slots[op.slot].stars();
    case 'riftSettle': return CM.Rift.settle(D(op.data), M(op.modes), R(op.account), R(op.rift));
    case 'dailyTasks': return CM.Daily.tasks(D(op.data), M(op.modes), op.policy);
    case 'dailyOpen': return CM.Daily.open(D(op.data), M(op.modes), R(op.account), op.nowMs);
    case 'dailyRecord': return CM.Daily.record(D(op.data), M(op.modes), R(op.account), clone(op.event), op.nowMs, op.policy);
    case 'dailyPoints': return CM.Daily.points(D(op.data), M(op.modes), R(op.account), op.nowMs, op.policy);
    case 'dailyClaim': return CM.Daily.claim(D(op.data), M(op.modes), R(op.account), op.at, op.nowMs, op.policy);
    default: throw new Error(`Unknown op ${op.op}`);
  }
}
const STATEFUL = { battleCmd: 'battle', riftNext: 'rift', riftCmd: 'rift' };
// Pure ops whose expected outcome is whatever the source gives (boundary sweeps); stateful ops never use this.
const outcome = op => { try { execute({ data: 'live', modes: 'shipped', ...op }, {}, true); return null; } catch (e) { return e instanceof MF.RuleError ? e.code : e.name; } };

function scenario(id, build) {
  const slots = {}; const ops = []; let commands = 0;
  const run = (input, expected = null) => {
    const op = { data: 'live', modes: 'shipped', ...input };
    const isCmd = op.op === 'battleCmd' || op.op === 'riftCmd';
    const full = !isCmd || commands++ % 6 === 0;
    const record = clone(op);
    let value;
    try {
      value = execute(op, slots, full);
      assert.equal(expected, null, `${id}: expected ${expected} from ${op.op}`);
      record.outcome = 'ok'; record.result = clone(value);
      if (op.save) slots[op.save] = value;
      // a command that ends a battle always carries its full state
      if (isCmd && !full && value.response.phase !== 'PLAYER') record.result.state = clone(slots[op.slot].s);
    } catch (error) {
      if (error instanceof MF.RuleError) {
        assert.equal(error.code, expected, `${id}: ${op.op} threw ${error.code}`);
        record.outcome = 'error'; record.error = { code: error.code };
      } else {
        if (expected !== error.name) throw error;
        record.outcome = 'error'; record.error = { name: error.name };
      }
      if (STATEFUL[op.op] === 'battle') record.after = checkpoint(slots[op.slot].s, false);
      if (STATEFUL[op.op] === 'rift') record.after = runCheckpoint(slots[op.slot].s, true);
    }
    ops.push(record);
    return value;
  };
  build({ run, slots });
  return { id, ops };
}

// ───── bots (generation only; the fixture records their requests) ─────
function botMove(b, style, allowUlt) {
  const s = b.s;
  const alive = b.alive().slice().sort((x, y) => x.hp - y.hp || (x.uid < y.uid ? -1 : x.uid > y.uid ? 1 : 0));
  const target = alive[0] ? alive[0].uid : null;
  if (style !== 'greedy' && allowUlt) {
    for (const a of s.actors) {
      if (a.gauge < b.ix.rules.gaugeMax) continue;
      const probe = new CM.ModeBattle(b.ix, clone(s));
      try { probe.command({ v: s.v, kind: 'ult', args: { actor: a.id, target } }); return ['ult', { actor: a.id, target }]; } catch (e) { if (!(e instanceof MF.RuleError)) throw e; }
    }
  }
  const modes = style === 'greedy' ? ['now'] : s.turn % 2 ? ['next', 'now', 'after'] : ['now', 'after', 'next'];
  for (const uid of s.zones.hand) for (const mode of modes) if (b.check(uid, mode, target, []) === null) return ['play', { uid, mode, target }];
  return ['end', {}];
}
function fight(ctx, slot, style, { maxTurns = 30, maxSteps = 500, ult = true } = {}) {
  const b = ctx.slots[slot];
  for (let i = 0; i < maxSteps && b.s.phase === 'PLAYER' && b.s.turn <= maxTurns; i++) {
    const [kind, args] = botMove(b, style, ult);
    ctx.run({ op: 'battleCmd', slot, request: { rid: `r${b.s.v}-${kind}`, v: b.s.v, kind, args } });
  }
}
function fightRift(ctx, slot, style, maxTurns = 25) {
  const run = ctx.slots[slot];
  for (let i = 0; i < 400 && run.battle && run.battle.s.phase === 'PLAYER' && run.battle.s.turn <= maxTurns; i++) {
    const [kind, args] = botMove(run.battle, style, true);
    ctx.run({ op: 'riftCmd', slot, kind, args });
  }
}
function clearFloor(ctx, slot, style, opts = {}) {
  const run = ctx.slots[slot];
  for (let guard = 0; guard < 4 && run.s.phase === 'READY'; guard++) {
    ctx.run({ op: 'riftNext', slot });
    fightRift(ctx, slot, style, opts.maxTurns || 25);
    if (run.battle) break; // an unfinished battle stays BATTLE
  }
}
const kitDeck = (ids) => CM.partySetup(MF.indexData(DATA), ids.map(charId => ({ charId })), null, ALL).deck;
const directSetup = (ids, extra = {}) => ({ party: ids.map(charId => ({ charId })), deck: kitDeck(ids), hp: 81 * ids.length, maxHp: 81 * ids.length, seed: 11, enemies: ['b_archivist'], ...extra });

export function generateCases() {
  assert.deepEqual(sources(), loadedSources, 'Content-mode sources changed after module load; rerun the exporter');
  for (const key of Object.keys(witness)) delete witness[key];
  const restore = instrument();
  const cases = [];
  try {
    // ── §13.3 reset clocks ──
    cases.push(scenario('time-day-week', ({ run }) => {
      const base = KST(2026, 10, 19, 5);
      const times = [base - 1, base, base + 1, KST(2026, 10, 26, 4, 59), KST(2026, 10, 26, 5), KST(2026, 12, 31, 23), KST(2027, 1, 1, 4, 59), KST(2027, 1, 1, 5),
        KST(2028, 2, 29, 5), KST(2028, 3, 1, 4), 0, -1, -86400000 * 3 - 7, 253402300799999, 253402300800000, 8640000000000000 - 14400000, -8640000000000000 + 18000000,
        -62198755200000, -62167219200000, 1e15];
      for (let h = 0; h < 8 * 24; h += 5) times.push(base + h * 3600000 - 1);
      // the boundary rows near the ±8.64e15 ms Date limit throw RangeError under some offsets; record whichever the source gives
      for (const modes of ['shipped', 'resetsAlt', 'resetsOdd']) for (const nowMs of times) for (const fn of ['dayKey', 'weekKey']) { const op = { op: 'time', fn, modes, nowMs }; run(op, outcome(op)); }
      run({ op: 'time', fn: 'dayKey', nowMs: 8640000000000000 - 14400000 + 1 }, 'RangeError');
      run({ op: 'time', fn: 'weekKey', nowMs: -8640000000000000 + 18000000 + 1 }, 'RangeError');
      run({ op: 'time', fn: 'weekKey', nowMs: 8640000000000000 }, 'RangeError');
    }));
    cases.push(scenario('time-rift-cycle', ({ run }) => {
      const at = (season, nowMs, expected = null, modes = 'shipped') => run({ op: 'time', fn: 'riftCycle', modes, season, nowMs }, expected);
      for (const season of ['2026-11-01', '2026-02-30', '2024-02-29', '2026-02-31', '0000-01-01', '9999-12-31', '1970-01-01']) {
        const t = Date.parse(`${season}T00:00:00Z`) - 9 * 3600000 + 11 * 3600000;
        for (const d of [-1, 0, 1]) at(season, t + d, d < 0 ? 'RIFT_CLOSED' : null);
        for (const k of [1, 2]) { at(season, t + k * 14 * 86400000 - 1); at(season, t + k * 14 * 86400000); }
        at(season, t + 42 * 86400000 - 1); at(season, t + 42 * 86400000, 'RIFT_CLOSED');
      }
      for (const nowMs of [KST(2026, 11, 1, 0), KST(2026, 11, 1, 11), KST(2026, 11, 1, 14), KST(2026, 11, 15, 10), KST(2026, 11, 15, 15)]) {
        const op = { op: 'time', fn: 'riftCycle', modes: 'resetsAlt', season: '2026-11-01', nowMs }; run(op, outcome(op));
      }
      for (const season of ['11/01', '2026-13-01', '2026-00-10', '2026-02-00', '2026-02-32', '2026-1-01', ' 2026-11-01', '2026-11-01\n', '２０２６-11-01', '2026-11-1a', '', null])
        at(season, KST(2026, 11, 2, 0), 'INVALID_SEASON');
    }));
    // ── shared setup ──
    cases.push(scenario('party-setup', ({ run }) => {
      const ps = (party, memory, owned = ALL, expected = null, data = 'live') => run({ op: 'partySetup', data, party, memory, owned }, expected);
      ps(team('sera', 'harin', 'rien'), null);
      ps([{ charId: 'yuri' }, { charId: 'kaya', weaponId: 'w_kaya', weaponLevel: 30, refine: 3, armor: 5, accessory: 2, skillLv: 4, ultLv: 2, res: 2, level: 45 }], null);
      ps([{ charId: 'sion', weaponId: 'w_unknown', level: 1 }, { charId: 'nia', weaponId: 'w_basic_melee', level: 99, armor: 0, accessory: 20, skillLv: 99 }], null);
      ps(team('sera', 'yuri'), {
        sera: [{ id: 'sera_round', plus: true, mods: { c: 'c_quick', e: 'e_pierce' } }, { id: 'com_guard', assigned: 'nia' }, { id: 'sera_aim', growth: 2, from: 'x' }],
        yuri: [{ id: 'yuri_sprout', growth: 3 }, { id: 'yuri_sprout_thorn', from: 'yuri_sprout', plus: 0, mods: null }], nia: [{ id: 'nia_wrench' }],
      });
      ps(team('lumi'), { lumi: [] });
      ps(team('aila'), { aila: [{ id: 'aila_bell', plus: 1, growth: 0, extra: 'kept?' }] });
      ps([], null, ALL, 'PARTY_SIZE'); ps(team('sera', 'harin', 'rien', 'nia'), null, ALL, 'PARTY_SIZE'); ps(null, null, ALL, 'PARTY_SIZE');
      ps(team('sera', 'sera'), null, ALL, 'PARTY_DUPLICATE'); ps([{ level: 60 }, { level: 1 }], null, ALL, 'PARTY_DUPLICATE');
      ps(team('sera', 'nobody'), null, ALL, 'UNKNOWN_CHARACTER'); ps([{ charId: 7 }], null, ALL, 'UNKNOWN_CHARACTER');
      ps(team('sera', 'harin'), null, ['sera'], 'NOT_OWNED');
      ps(team('sera'), { sera: 'deck' }, ALL, 'INVALID_SNAPSHOT'); ps(team('sera'), { sera: { id: 'sera_round' } }, ALL, 'INVALID_SNAPSHOT');
      for (const bad of [[{ id: 'harin_bash' }], [{ id: 'sera_round', token: true }], [{ id: 'sera_round', weapon: 1 }], [{ id: 'nope' }], [null], ['sera_round'], [{ id: 5 }], [{}]])
        ps(team('sera'), { sera: bad }, ALL, 'INVALID_CARD');
      ps(team('sera'), 'not an object');
      ps(team('sera'), { harin: [{ id: 'harin_bash' }] });
      ps([null], null, ALL, 'TypeError');
    }));
    cases.push(scenario('stage-diff', ({ run }) => {
      const rows = [...DATA.progression.dungeonTiers, ...DATA.progression.weeklyBoss.difficulty, ...DATA.progression.recordRift.floors,
        { stage: 37, hpBps: 12550 }, { stage: 1, dmgBps: 9999 }, { stage: 99 }, { stage: 0 }, { stage: 60, hpBps: 15 }, { stage: 33, hpBps: 10050, dmgBps: 10150 }];
      for (const row of rows) run({ op: 'stageDiff', row: { stage: row.stage, ...(row.hpBps ? { hpBps: row.hpBps } : {}), ...(row.dmgBps ? { dmgBps: row.dmgBps } : {}) } });
      // Stats.combine rounds a binary64 product: bps that are not multiples of 100 can land just below .5 (stage 41, dmgBps 12920 → 161)
      run({ op: 'stageDiff', row: { stage: 41, dmgBps: 12920 } });
      for (const stage of [1, 7, 13, 20, 33, 41, 50, 59, 60]) for (const bps of [12920, 10050, 12345, 9999, 15, 13333, 11111, 10001, 12500, 10150])
        run({ op: 'stageDiff', row: { stage, hpBps: bps, dmgBps: bps } });
      // every stage 1–60 × bps 1–30000 where the binary64 result and the exact rational round differently (3 rows, all stage 41 damage)
      const ix = MF.indexData(DATA); const split = [];
      for (let stage = 1; stage <= 60; stage++) {
        const sc = MF.Stats.stageScale(ix, stage);
        for (let bps = 1; bps <= 30000; bps++) for (const [key, scale] of [['hpBps', sc.hp], ['dmgBps', sc.dmg]])
          if (Math.round(bps / 100 * scale / 100) !== Math.floor((2 * bps * scale + 10000) / 20000)) split.push({ stage, [key]: bps });
      }
      assert.ok(split.some(r => r.stage === 41 && r.dmgBps === 12920), 'Missing the binary64 rounding split');
      for (const row of split) run({ op: 'stageDiff', row });
    }));
    cases.push(scenario('mode-data', ({ run }) => {
      for (const data of ['live', 'late']) {
        run({ op: 'modeData', data, mode: { kind: 'rift', rule: 'rift_break', params: MODES.rift.modifiers.rift_break } });
        for (const p of [{ gaugeBps: 10001, brokenDamageBps: 7777 }, { gaugeBps: 3333, brokenDamageBps: 0 }, { gaugeBps: 0, brokenDamageBps: 2500 }, { gaugeBps: 16667, brokenDamageBps: 5000 }])
          run({ op: 'modeData', data, mode: { kind: 'rift', rule: 'rift_break', params: p } });
        for (const rule of RULES.filter(r => r !== 'rift_break')) run({ op: 'modeData', data, mode: { kind: 'rift', rule, params: MODES.rift.modifiers[rule] } });
        run({ op: 'modeData', data, mode: { kind: 'dungeon' } });
      }
    }));
    // ── ModeBattle: waves and the six rift rules ──
    const parties = {
      reserve: [['sera', 'harin', 'rien'], ['sera', 'kaya', 'nia']], break: [['aila', 'kaya', 'lumi'], ['sera', 'harin', 'rien']],
      ink: [['sion', 'harin', 'aila'], ['sion', 'nia', 'rien']], debt: [['ravien', 'harin', 'kaya'], ['ravien', 'sion', 'lumi']],
      bloom: [['yuri', 'aila', 'harin'], ['yuri', 'nia', 'sera']], echo: [['sera', 'harin', 'rien'], ['rien', 'kaya', 'yuri']],
    };
    for (const rule of RULES) {
      const key = rule.slice(5);
      parties[key].forEach((ids, i) => {
        for (const style of ['greedy', 'mixed']) {
          const relics = rule === 'rift_debt' ? ['pocket_brazier', 'void_shard', 'contract_seal'] : rule === 'rift_bloom' ? ['symbiotic_moss'] : rule === 'rift_ink' ? ['oblivion_ink'] : [];
          const enemies = i === 0 ? ['b_archivist'] : ['e_hound', 'e_cog', 'e_page'];
          cases.push(scenario(`battle-${key}-${i}-${style}`, ctx => {
            ctx.run({ op: 'battleCreate', slot: 'b', setup: directSetup(ids, { relics, enemies, seed: 11 + i * 7 + (style === 'mixed' ? 3 : 0) }), mode: { kind: 'rift', rule, params: MODES.rift.modifiers[rule] } });
            ctx.run({ op: 'battleCmd', slot: 'b', request: { rid: 'r0-begin', v: 0, kind: 'begin', args: {} } });
            fight(ctx, 'b', style, { maxTurns: 14 });
          }));
        }
      });
    }
    cases.push(scenario('battle-debt-extreme-bps', ctx => {
      // TEST-ONLY boundary params: the debt bonus must be added outside Int32 before the single floor
      ctx.run({ op: 'battleCreate', slot: 'b', setup: directSetup(['ravien', 'harin'], { relics: ['pocket_brazier', 'void_shard'], enemies: ['e_page', 'e_moth', 'e_cog'], seed: 4 }),
        mode: { kind: 'rift', rule: 'rift_debt', params: { debtCapAdd: 2, debtDamageBps: 2147483647 } } });
      ctx.run({ op: 'battleCmd', slot: 'b', request: { rid: 'r0-begin', v: 0, kind: 'begin', args: {} } });
      fight(ctx, 'b', 'greedy', { maxTurns: 8 });
    }));
    cases.push(scenario('battle-break-phase-late', ctx => {
      ctx.run({ op: 'battleCreate', slot: 'b', data: 'late', setup: directSetup(['aila', 'kaya', 'lumi'], { enemies: ['b_nameless'], hp: 400, maxHp: 400, seed: 5 }), mode: { kind: 'rift', rule: 'rift_break', params: MODES.rift.modifiers.rift_break } });
      ctx.run({ op: 'battleCmd', slot: 'b', request: { rid: 'r0-begin', v: 0, kind: 'begin', args: {} } });
      fight(ctx, 'b', 'mixed', { maxTurns: 18 });
    }));
    cases.push(scenario('battle-waves-plain', ctx => {
      ctx.run({ op: 'battleCreate', slot: 'b', setup: directSetup(['sera', 'harin', 'rien'], { enemies: ['e_page'], seed: 3 }), mode: { kind: 'test', waves: [['e_moth', 'e_seed'], ['e_page', 'e_page', 'e_page'], ['e_lantern']] } });
      ctx.run({ op: 'battleCmd', slot: 'b', request: { rid: 'r0-begin', v: 0, kind: 'begin', args: {} } });
      fight(ctx, 'b', 'mixed', { maxTurns: 25 });
    }));
    cases.push(scenario('battle-waves-rule-echo', ctx => {
      ctx.run({ op: 'battleCreate', slot: 'b', setup: directSetup(['rien', 'sera', 'harin'], { enemies: ['e_seed'], seed: 8 }), mode: { kind: 'test', rule: 'rift_echo', params: MODES.rift.modifiers.rift_echo, waves: [['e_seed', 'e_seed'], ['e_moth']] } });
      ctx.run({ op: 'battleCmd', slot: 'b', request: { rid: 'r0-begin', v: 0, kind: 'begin', args: {} } });
      fight(ctx, 'b', 'greedy', { maxTurns: 20 });
    }));
    cases.push(scenario('battle-no-rule-plain', ctx => {
      ctx.run({ op: 'battleCreate', slot: 'b', setup: directSetup(['sera', 'harin', 'rien'], { enemies: ['e_moth', 'e_cog'], seed: 77 }), mode: { kind: 'test', rule: '' } });
      ctx.run({ op: 'battleCmd', slot: 'b', request: { rid: 'r0-begin', v: 0, kind: 'begin', args: {} } });
      fight(ctx, 'b', 'greedy');
    }));
    cases.push(scenario('battle-protocol', ctx => {
      const setup = directSetup(['sera'], { deck: [{ id: 'sera_round' }, { id: 'sera_round' }, { id: 'com_guard' }] });
      ctx.run({ op: 'battleCreate', slot: 'x', setup, mode: { kind: 'rift', rule: 'rift_x', params: {} } }, 'UNKNOWN_RULE');
      ctx.run({ op: 'battleCreate', slot: 'x', setup, mode: { kind: 'rift', rule: 'rift_ink' } }, 'UNKNOWN_RULE');
      ctx.run({ op: 'battleCreate', slot: 'x', setup, mode: { kind: 'rift', rule: 'rift_ink', params: null } }, 'UNKNOWN_RULE');
      ctx.run({ op: 'battleCreate', slot: 'b', setup, mode: { kind: 'rift', rule: 'rift_ink', params: MODES.rift.modifiers.rift_ink, contentId: 'floor:3', battle: 2, wave: 9, settled: true } });
      ctx.run({ op: 'battleCreate', slot: 'p', setup: { ...setup, party: [] }, mode: { kind: 'rift', rule: 'rift_ink', params: MODES.rift.modifiers.rift_ink } }, 'PARTY_SIZE');
      const begin = { rid: 'b1', v: 0, kind: 'begin', args: {} };
      ctx.run({ op: 'battleCmd', slot: 'b', request: { rid: 'e1', v: 0, kind: 'end', args: {} } }, 'WRONG_PHASE');
      ctx.run({ op: 'battleCmd', slot: 'b', request: begin });
      ctx.run({ op: 'battleCmd', slot: 'b', request: begin });
      ctx.run({ op: 'battleCmd', slot: 'b', request: { ...begin, kind: 'end' } }, 'IDEMPOTENCY_CONFLICT');
      ctx.run({ op: 'battleCmd', slot: 'b', request: { rid: 'old', v: 0, kind: 'end', args: {} } }, 'STALE_VERSION');
      ctx.run({ op: 'battleCmd', slot: 'b', request: { rid: 'u', v: 1, kind: 'dance', args: {} } }, 'UNKNOWN_COMMAND');
      ctx.run({ op: 'battleCmd', slot: 'b', request: { v: 1, kind: 'end', args: {} } });
      ctx.run({ op: 'battleCmd', slot: 'b', request: { v: 2, kind: 'play', args: { uid: 'c999', mode: 'now' } } }, 'NOT_IN_HAND');
      fight(ctx, 'b', 'greedy', { maxTurns: 6 });
    }));
    // ── §11.5 growth dungeon ──
    cases.push(scenario('dungeon-ledger', ({ run }) => {
      for (const d of DATA.progression.dungeons) for (const t of [1, 2, 3, 4, 5, 6]) run({ op: 'dungeonReward', dungeonId: d.id, tier: t });
      run({ op: 'dungeonReward', dungeonId: 'dg_ink', tier: 7 }, 'UNKNOWN_TIER'); run({ op: 'dungeonReward', dungeonId: 'dg_ink', tier: 0 }, 'UNKNOWN_TIER');
      run({ op: 'dungeonReward', dungeonId: 'dg_none', tier: 1 }, 'UNKNOWN_CONTENT');
      for (const [turn, hp, phase] of [[4, 100, 'VICTORY'], [5, 100, 'VICTORY'], [4, 99, 'VICTORY'], [5, 99, 'VICTORY'], [1, 200, 'VICTORY'], [1, 0, 'DEFEAT'], [0, 0, 'VICTORY']])
        run({ op: 'dungeonStars', battleResult: { phase, turn, hp }, maxHp: 200 });
      run({ op: 'dungeonStars', battleResult: null, maxHp: 200 });
      const sweep = (acc, args, expected = null) => run({ op: 'dungeonSweep', account: acc, args }, expected);
      const acc = account({ dungeons: { 'dg_ink:6': 3, 'dg_ink:5': 2, 'dg_oil:1': 3, 'dg_polish:6': 3 } });
      sweep(acc, { dungeonId: 'dg_ink', tier: 6, times: 12 }); sweep(acc, { dungeonId: 'dg_ink', tier: 6, times: 1 }); sweep(acc, { dungeonId: 'dg_oil', tier: 1, times: 7 });
      sweep(acc, { dungeonId: 'dg_ink', tier: 5, times: 1 }, 'SWEEP_LOCKED'); sweep(acc, { dungeonId: 'dg_ink', tier: 6, times: 13 }, 'INVALID_SWEEP');
      sweep(acc, { dungeonId: 'dg_ink', tier: 6, times: 0 }, 'INVALID_SWEEP'); sweep({ ...acc, stamina: 39 }, { dungeonId: 'dg_ink', tier: 6, times: 2 }, 'NOT_ENOUGH_STAMINA');
      sweep({ ...acc, stamina: 40 }, { dungeonId: 'dg_ink', tier: 6, times: 2 }); sweep({ ...acc, level: 19 }, { dungeonId: 'dg_polish', tier: 6, times: 1 }, 'LOCKED');
      sweep(acc, { dungeonId: 'dg_x', tier: 6, times: 1 }, 'UNKNOWN_CONTENT'); sweep(acc, { dungeonId: 'dg_ink', tier: 9, times: 100 }, 'UNKNOWN_TIER');
      sweep({ ...acc, level: 0 }, { dungeonId: 'dg_ink', tier: 6, times: 1 }, 'INVALID_ACCOUNT'); sweep({ ...acc, stamina: -1 }, { dungeonId: 'dg_ink', tier: 6, times: 1 }, 'INVALID_ACCOUNT');
      sweep({ level: 60, stamina: 240 }, { dungeonId: 'dg_ink', tier: 6, times: 1 }, 'INVALID_ACCOUNT'); sweep({ level: 60, stamina: 240, owned: ['sera', null] }, { dungeonId: 'dg_ink', tier: 6, times: 1 }, 'INVALID_ACCOUNT');
      sweep({ level: 60, stamina: 240, owned: [] }, { dungeonId: 'dg_ink', tier: 6, times: 1 }, 'SWEEP_LOCKED');
    }));
    cases.push(scenario('dungeon-start-errors', ({ run }) => {
      const opts = { dungeonId: 'dg_ink', tier: 1, party: team('sera', 'harin', 'rien') };
      const start = (modes, acc, o, expected) => run({ op: 'dungeonStart', slot: 'b', modes, account: acc, opts: o }, expected);
      start('shipped', account(), opts, 'CONTENT_NOT_AUTHORED');
      start('dungeonWaves', account({ level: 4 }), opts, 'LOCKED'); start('dungeonWaves', account({ level: 19 }), { ...opts, dungeonId: 'dg_polish' }, 'LOCKED');
      start('dungeonWaves', account({ stamina: 19 }), opts, 'NOT_ENOUGH_STAMINA'); start('dungeonWaves', account({ owned: ['sera', 'harin'] }), opts, 'NOT_OWNED');
      for (const tier of [1, 2, 3]) start('dungeonWaves', account(), { ...opts, dungeonId: 'dg_seal', tier }, 'INVALID_CONTENT');
      start('dungeonWaves', account(), { ...opts, dungeonId: 'dg_credit', tier: 1 }, 'CONTENT_NOT_AUTHORED');
      start('dungeonWaves', account(), { ...opts, dungeonId: 'dg_none' }, 'UNKNOWN_CONTENT'); start('dungeonWaves', account(), { ...opts, tier: 8 }, 'UNKNOWN_TIER');
      start('dungeonWaves', account(), { ...opts, party: [] }, 'PARTY_SIZE'); start('dungeonWaves', account(), { ...opts, dungeonId: 'dg_book' }, 'UNKNOWN_ENEMY');
      start('dungeonWaves', { level: 60 }, opts, 'INVALID_ACCOUNT');
    }));
    const dungeonRun = (id, modes, opts, style, after) => cases.push(scenario(id, ctx => {
      const acc = account();
      ctx.run({ op: 'dungeonStart', slot: 'b', modes, account: acc, opts });
      fight(ctx, 'b', style, { maxTurns: 20 });
      after(ctx, acc);
    }));
    dungeonRun('dungeon-ink1-waves', 'dungeonWaves', { dungeonId: 'dg_ink', tier: 1, party: team('sera', 'harin', 'rien'), seed: 5 }, 'greedy', (ctx, acc) => {
      const r = { ref: 'b', path: 'state' };
      ctx.run({ op: 'dungeonSettle', modes: 'dungeonWaves', account: acc, battle: r, save: 's1' });
      ctx.run({ op: 'dungeonSettle', modes: 'dungeonWaves', account: { ref: 's1', path: 'account' }, battle: { ref: 's1', path: 'battle' } }, 'ALREADY_SETTLED');
      ctx.run({ op: 'dungeonSettle', modes: 'dungeonWaves', account: acc, battle: { ...r, patch: { phase: 'DEFEAT', result: {} } } }, 'CONTENT_NOT_AUTHORED');
      ctx.run({ op: 'dungeonSettle', modes: 'dungeonWaves', account: { ...acc, dungeons: { 'dg_ink:1': 1 } }, battle: { ...r, patch: { phase: 'VICTORY', result: { turn: 2, hp: 9999 } } }, save: 's3' });
      ctx.run({ op: 'dungeonSettle', modes: 'dungeonWaves', account: { ref: 's3', path: 'account' }, battle: { ...r, patch: { phase: 'VICTORY', result: { turn: 9, hp: 1 } } } });
      ctx.run({ op: 'dungeonSettle', modes: 'dungeonWaves', account: { ...acc, stamina: 19 }, battle: r }, 'NOT_ENOUGH_STAMINA');
      ctx.run({ op: 'dungeonSettle', modes: 'dungeonWaves', account: { ...acc, level: 0 }, battle: r }, 'INVALID_ACCOUNT');
      ctx.run({ op: 'weeklySettle', modes: 'dungeonWaves', account: acc, battle: r, nowMs: 0 }, 'WRONG_CONTENT');
      ctx.run({ op: 'dungeonStart', slot: 'fresh', modes: 'dungeonWaves', account: acc, opts: { dungeonId: 'dg_ink', tier: 1, party: team('sera') } });
      ctx.run({ op: 'dungeonSettle', modes: 'dungeonWaves', account: acc, battle: { ref: 'fresh', path: 'state' } }, 'BATTLE_NOT_OVER');
      ctx.run({ op: 'dungeonSettle', modes: 'dungeonWaves', account: acc, battle: null }, 'WRONG_CONTENT');
    });
    dungeonRun('dungeon-ink6-three-waves', 'dungeonWaves', { dungeonId: 'dg_ink', tier: 6, party: team('kaya', 'harin', 'aila'), seed: 21 }, 'mixed', (ctx, acc) => {
      ctx.run({ op: 'dungeonSettle', modes: 'dungeonWaves', account: acc, battle: { ref: 'b', path: 'state', patch: { phase: 'VICTORY', result: {} } } });
    });
    dungeonRun('dungeon-oil3-memory', 'dungeonWaves', { dungeonId: 'dg_oil', tier: 3, party: [{ charId: 'yuri', level: 40, weaponId: 'w_yuri', weaponLevel: 40 }, { charId: 'nia', level: 35 }],
      memory: { yuri: [{ id: 'yuri_sprout', growth: 2 }, { id: 'yuri_root', plus: true }, { id: 'com_guard' }, { id: 'yuri_dew' }], nia: [{ id: 'nia_wrench', mods: { c: 'c_quick', e: 'e_burn' } }, { id: 'nia_barrier' }, { id: 'nia_scan' }] }, seed: 9 }, 'mixed', (ctx, acc) => {
      ctx.run({ op: 'dungeonSettle', modes: 'dungeonWaves', account: acc, battle: { ref: 'b', path: 'state', patch: { phase: 'VICTORY', result: {} } } });
    });
    dungeonRun('dungeon-polish1', 'dungeonWaves', { dungeonId: 'dg_polish', tier: 1, party: team('lumi', 'sion'), seed: 2 }, 'greedy', (ctx, acc) => {
      ctx.run({ op: 'dungeonSettle', modes: 'dungeonWaves', account: { ...acc, dungeons: { 'dg_polish:1': 2 } }, battle: { ref: 'b', path: 'state' } });
    });
    // ── §11.6 weekly boss ──
    cases.push(scenario('weekly-start', ({ run }) => {
      const opts = { bossId: 'b_bell', difficulty: 1, party: team('sera', 'harin', 'rien'), seed: 3 };
      run({ op: 'weeklyStart', slot: 'x', account: account({ level: 14 }), opts }, 'LOCKED');
      run({ op: 'weeklyStart', slot: 'x', account: account(), opts: { ...opts, bossId: 'e_moth' } }, 'UNKNOWN_CONTENT');
      run({ op: 'weeklyStart', slot: 'x', account: account(), opts: { ...opts, difficulty: 6 } }, 'UNKNOWN_TIER');
      for (const boss of MODES.weeklyBoss.bosses) for (const difficulty of [1, 2, 3, 4, 5]) run({ op: 'weeklyStart', slot: `${boss}${difficulty}`, account: account(), opts: { ...opts, bossId: boss, difficulty, practice: difficulty === 3 } });
    }));
    cases.push(scenario('weekly-real-fight', ctx => {
      ctx.run({ op: 'weeklyStart', slot: 'b', data: 'weakBell', account: account(), opts: { bossId: 'b_bell', difficulty: 1, party: team('sera', 'harin', 'rien'), seed: 3 } });
      fight(ctx, 'b', 'greedy');
      ctx.run({ op: 'weeklySettle', data: 'weakBell', account: account(), battle: { ref: 'b', path: 'state' }, nowMs: KST(2026, 10, 20, 12), save: 'r' });
      ctx.run({ op: 'weeklySettle', data: 'weakBell', account: { ref: 'r', path: 'account' }, battle: { ref: 'r', path: 'battle' }, nowMs: KST(2026, 10, 20, 12) }, 'ALREADY_SETTLED');
      ctx.run({ op: 'weeklySettle', data: 'weakBell', account: account(), battle: { ref: 'b', path: 'state' }, nowMs: 8640000000000000 }, 'RangeError');
      ctx.run({ op: 'dungeonSettle', data: 'weakBell', account: account(), battle: { ref: 'b', path: 'state' } }, 'WRONG_CONTENT');
    }));
    cases.push(scenario('weekly-claims', ctx => {
      let n = 0; let last = null;
      const start = (bossId, practice) => { const slot = `w${n++}`; ctx.run({ op: 'weeklyStart', slot, account: account(), opts: { bossId, difficulty: 2, party: team('sera'), practice } }); return slot; };
      const settle = (slot, phase, nowMs = KST(2026, 10, 21, 9), expected = null) => {
        const save = `s${n++}`;
        ctx.run({ op: 'weeklySettle', account: last ? { ref: last, path: 'account' } : account(), battle: { ref: slot, path: 'state', patch: { phase, result: {} } }, nowMs, save }, expected);
        if (!expected) last = save;
      };
      settle(start('b_bell'), 'DEFEAT'); settle(start('b_bell', true), 'VICTORY');
      for (const boss of ['b_bell', 'b_notary', 'b_archivist']) settle(start(boss), 'VICTORY');
      settle(start('b_garden'), 'VICTORY'); settle(start('b_garden'), 'VICTORY', KST(2026, 10, 26, 4, 59)); settle(start('b_garden'), 'VICTORY', KST(2026, 10, 26, 5));
      settle(start('b_bell'), 'DEFEAT', KST(2026, 11, 3, 5)); settle(start('b_bell'), 'VICTORY', KST(2026, 11, 2, 4, 59));
      settle(start('b_bell'), 'RESOLVING', KST(2026, 11, 3, 5), 'BATTLE_NOT_OVER');
    }));
    // ── §11.7 record rift ──
    const A = team('sera', 'harin', 'rien'); const B = team('yuri', 'nia', 'kaya'); const C = team('lumi', 'sion', 'aila'); const Rv = team('ravien', 'kaya', 'nia');
    const CYCLE = { index: 0, key: '2026-11-01' }; const CYCLE1 = { index: 1, key: '2026-11-15' };
    cases.push(scenario('rift-create', ({ run }) => {
      const opts = { floor: 1, cycle: CYCLE, teams: [A, B] };
      const create = (modes, acc, o, expected = null) => run({ op: 'riftCreate', slot: 'r', modes, account: acc, opts: o }, expected);
      create('shipped', account(), opts, 'CONTENT_NOT_AUTHORED');
      create('riftPending', account({ level: 19 }), opts, 'LOCKED'); create('riftPending', account(), { ...opts, floor: 7 }, 'UNKNOWN_TIER');
      create('riftPending', account(), { ...opts, cycle: { index: 3, key: 'x' } }, 'INVALID_CYCLE'); create('riftPending', account(), { ...opts, cycle: { index: -1, key: 'x' } }, 'INVALID_CYCLE');
      create('riftPending', account(), { ...opts, cycle: { index: 0 } }, 'INVALID_CYCLE'); create('riftPending', account(), { floor: 1, teams: [A, B] }, 'INVALID_CYCLE');
      create('riftPending', account(), { ...opts, cycle: { index: 2, key: '2026-11-29' } }, 'CONTENT_NOT_AUTHORED');
      create('riftPending', account(), { ...opts, teams: [A] }, 'TEAM_COUNT'); create('riftPending', account(), { ...opts, teams: null }, 'TEAM_COUNT');
      create('riftPending', account(), { ...opts, teams: [A], policy: 'target' });
      create('riftPending', account(), { ...opts, floor: 4, teams: [A], policy: 'target' }, 'TEAM_COUNT');
      create('riftPending', account(), { ...opts, policy: 'later' }, 'UNKNOWN_POLICY'); create('riftPending', account(), { ...opts, policy: 'source' }, 'UNKNOWN_POLICY');
      create('riftPending', account(), { ...opts, teams: [team('sera'), team('yuri')] }, 'TEAM_SIZE'); create('riftPending', account(), { ...opts, teams: [A, team('yuri', 'nia')] }, 'TEAM_SIZE');
      create('riftPending', account(), { ...opts, teams: [A, team('sera', 'yuri', 'nia')] }, 'PARTY_OVERLAP');
      create('riftPending', account({ owned: ['sera', 'harin', 'rien', 'yuri', 'nia'] }), { ...opts, teams: [A, team('sera', 'yuri', 'nia')] });
      create('riftPending', account({ owned: ['sera', 'harin', 'rien', 'yuri', 'nia'] }), opts, 'NOT_OWNED');
      create('riftPending', account(), { ...opts, teams: [A, [{ charId: 'nobody' }]] }, 'UNKNOWN_CHARACTER');
      for (const floor of [1, 2, 3, 4, 5, 6]) for (const cycle of [CYCLE, CYCLE1]) create('riftPending', account(), { floor, cycle, teams: [A, B], seed: floor });
      for (const floor of [1, 2, 3]) create('riftTarget', account(), { floor, cycle: CYCLE1, teams: [C] });
      create('riftTarget', account(), { floor: 4, cycle: CYCLE1, teams: [C, A] });
      create('riftTarget', account(), { floor: 4, cycle: CYCLE1, teams: [C, A], policy: 'v0.8.1' });
      create('riftPending', account(), { floor: 2, cycle: CYCLE, teams: [A, B], memory: { sera: [{ id: 'sera_round', plus: true }, { id: 'sera_aim' }], kaya: [{ id: 'kaya_bolt' }] } });
      for (const floor of [1, 2, 3, 4, 5, 6]) create('riftBad', account(), { floor, cycle: CYCLE, teams: [A, B] }, 'INVALID_CONTENT');
      create('riftBad', account(), { floor: 1, cycle: CYCLE1, teams: [A, B] }, 'INVALID_CONTENT');
      create('riftPending', { level: 60, stamina: 1 }, opts, 'INVALID_ACCOUNT');
    }));
    const riftClear = (id, modes, opts, style, after, maxTurns, acc = account()) => cases.push(scenario(id, ctx => {
      ctx.run({ op: 'riftCreate', slot: 'r', modes, account: acc, opts: { cycle: CYCLE, seed: 9, ...opts } });
      ctx.run({ op: 'riftCmd', slot: 'r', kind: 'end', args: {} }, 'NO_BATTLE');
      ctx.run({ op: 'riftStars', slot: 'r' });
      clearFloor(ctx, 'r', style, { maxTurns });
      after && after(ctx);
    }));
    riftClear('rift-f1-pending', 'riftPending', { floor: 1, teams: [A, B] }, 'greedy', ctx => {
      ctx.run({ op: 'riftStars', slot: 'r' }, ctx.slots.r.s.phase === 'CLEARED' ? 'CONTENT_NOT_AUTHORED' : null);
      ctx.run({ op: 'riftSettle', modes: 'riftPending', account: account(), rift: { ref: 'r', path: 'state' } }, ctx.slots.r.s.phase === 'CLEARED' ? 'CONTENT_NOT_AUTHORED' : null);
      ctx.run({ op: 'riftNext', slot: 'r' }, 'WRONG_PHASE');
      ctx.run({ op: 'riftSettle', modes: 'riftPending', account: account(), rift: { ref: 'r', path: 'state', patch: { phase: 'FAILED' } } });
      ctx.run({ op: 'riftSettle', modes: 'riftPending', account: account(), rift: { ref: 'r', path: 'state', patch: { schema: 'mf-rift-floor-0.8.0' } } }, 'WRONG_CONTENT');
      ctx.run({ op: 'riftSettle', modes: 'riftPending', account: account(), rift: null }, 'WRONG_CONTENT');
    });
    riftClear('rift-f1-strict-lenient', 'riftStrict', { floor: 1, teams: [A, B] }, 'greedy', ctx => {
      const r = { ref: 'r', path: 'state' };
      ctx.run({ op: 'riftStars', slot: 'r' });
      ctx.run({ op: 'riftSettle', modes: 'riftStrict', account: account(), rift: r, save: 's1' });
      ctx.run({ op: 'riftSettle', modes: 'riftStrict', account: { ref: 's1', path: 'account' }, rift: { ref: 's1', path: 'rift' } }, 'ALREADY_SETTLED');
      ctx.run({ op: 'riftSettle', modes: 'riftStrict', account: { ref: 's1', path: 'account' }, rift: r });
      ctx.run({ op: 'riftSettle', modes: 'riftLenient', account: { ref: 's1', path: 'account' }, rift: r, save: 's2' });
      ctx.run({ op: 'riftSettle', modes: 'riftLenient', account: { ref: 's2', path: 'account' }, rift: r });
      ctx.run({ op: 'riftSettle', modes: 'riftMid', account: { ref: 's1', path: 'account' }, rift: r });
      ctx.run({ op: 'riftSettle', modes: 'riftStrict', account: { ref: 's1', path: 'account', merge: { rift: { key: '2026-11-15', floors: {} } } }, rift: r }, 'STALE_CYCLE');
      ctx.run({ op: 'riftSettle', modes: 'riftStrict', account: { ref: 's1', path: 'account', merge: { rift: { key: '2026-10-18', floors: { 1: 3, 4: 2 } } } }, rift: r });
      ctx.run({ op: 'riftSettle', modes: 'riftStrict', account: { ref: 's1', path: 'account', merge: { rift: { key: '', floors: { 1: 3 } } } }, rift: r });
    });
    const small = account({ owned: ['sera', 'harin', 'rien', 'yuri', 'nia'] });
    riftClear('rift-f3-small-roster', 'riftLenient', { floor: 3, teams: [A, team('sera', 'yuri', 'nia')] }, 'greedy', ctx => {
      ctx.run({ op: 'riftSettle', modes: 'riftLenient', account: small, rift: { ref: 'r', path: 'state' } }, ['CLEARED', 'FAILED'].includes(ctx.slots.r.s.phase) ? null : 'RIFT_NOT_OVER');
    }, 25, small);
    for (const floor of [2, 3, 4, 5, 6]) riftClear(`rift-cycle1-f${floor}`, 'riftMid', { floor, cycle: CYCLE1, teams: floor % 2 ? [C, Rv] : [A, B], seed: 30 + floor }, floor % 2 ? 'mixed' : 'greedy', ctx => {
      ctx.run({ op: 'riftStars', slot: 'r' });
      ctx.run({ op: 'riftSettle', modes: 'riftMid', account: account({ rift: { key: '2026-11-15', floors: { [floor]: 1 } } }), rift: { ref: 'r', path: 'state' } }, ['CLEARED', 'FAILED'].includes(ctx.slots.r.s.phase) ? null : 'RIFT_NOT_OVER');
    }, 14);
    for (const floor of [2, 4, 6]) riftClear(`rift-cycle0-f${floor}`, 'riftLenient', { floor, teams: [Rv, C], seed: floor }, 'mixed', ctx => {
      ctx.run({ op: 'riftSettle', modes: 'riftLenient', account: account(), rift: { ref: 'r', path: 'state' } }, ['CLEARED', 'FAILED'].includes(ctx.slots.r.s.phase) ? null : 'RIFT_NOT_OVER');
    }, 12);
    riftClear('rift-target-one-team', 'riftTarget', { floor: 2, cycle: CYCLE1, teams: [C], seed: 4 }, 'mixed', ctx => {
      ctx.run({ op: 'riftSettle', modes: 'riftTarget', account: account(), rift: { ref: 'r', path: 'state' } }, ['CLEARED', 'FAILED'].includes(ctx.slots.r.s.phase) ? null : 'RIFT_NOT_OVER');
    });
    cases.push(scenario('rift-mid-floor', ctx => {
      ctx.run({ op: 'riftCreate', slot: 'r', modes: 'riftLenient', account: account(), opts: { floor: 2, cycle: CYCLE, teams: [A, B], seed: 4 } });
      ctx.run({ op: 'riftNext', slot: 'r' });
      ctx.run({ op: 'riftNext', slot: 'r' }, 'WRONG_PHASE');
      ctx.run({ op: 'riftStars', slot: 'r' });
      ctx.run({ op: 'riftSettle', modes: 'riftLenient', account: account(), rift: { ref: 'r', path: 'state' } }, 'RIFT_NOT_OVER');
      ctx.run({ op: 'riftCmd', slot: 'r', kind: 'end', args: {} });
      ctx.run({ op: 'riftCmd', slot: 'r', kind: 'play', args: { uid: 'c999', mode: 'now' } }, 'NOT_IN_HAND');
      ctx.run({ op: 'riftCmd', slot: 'r', kind: 'fly', args: {} }, 'UNKNOWN_COMMAND');
    }));
    cases.push(scenario('rift-unknown-enemy-next', ctx => {
      // as the source, the floor RNG has already advanced when the second battle's lineup is rejected
      ctx.run({ op: 'riftCreate', slot: 'r', modes: 'riftUnknownEnemy', account: account(), opts: { floor: 1, cycle: CYCLE, teams: [A, B], seed: 2 } });
      ctx.run({ op: 'riftNext', slot: 'r' });
      fightRift(ctx, 'r', 'greedy');
      ctx.run({ op: 'riftNext', slot: 'r' }, ctx.slots.r.s.phase === 'READY' ? 'UNKNOWN_ENEMY' : 'WRONG_PHASE');
      ctx.run({ op: 'riftNext', slot: 'r' }, ctx.slots.r.s.phase === 'READY' ? 'UNKNOWN_ENEMY' : 'WRONG_PHASE');
    }));
    // ── §11.13 daily activity ──
    cases.push(scenario('daily', ({ run }) => {
      const day = KST(2026, 10, 19, 12); const next = KST(2026, 10, 20, 5);
      for (const [modes, policy] of [['shipped', undefined], ['shipped', 'v0.8.1'], ['dailyTarget', 'target'], ['dailyTargetDefault', undefined], ['dailyTargetDefault', 'v0.8.1']]) run({ op: 'dailyTasks', modes, policy });
      run({ op: 'dailyTasks', policy: 'target' }, 'CONTENT_NOT_AUTHORED'); run({ op: 'dailyTasks', policy: 'later' }, 'UNKNOWN_POLICY');
      run({ op: 'dailyRecord', account: account({ level: 4 }), event: { type: 'login', n: 1 }, nowMs: day }, 'LOCKED');
      run({ op: 'dailyOpen', account: account(), nowMs: day });
      run({ op: 'dailyOpen', account: account({ daily: { key: '2026-10-19', counts: { login: 1 }, claimed: [20] } }), nowMs: day });
      run({ op: 'dailyOpen', account: account({ daily: { key: '2026-10-18', counts: { login: 1 }, claimed: [20] } }), nowMs: day });
      run({ op: 'dailyPoints', account: account(), nowMs: 8640000000000000 }, 'RangeError');
      let prev = account(); let k = 0;
      const rec = (type, n, expected = null, o = {}) => {
        const save = `a${++k}`;
        run({ op: 'dailyRecord', modes: o.modes || 'shipped', account: prev, event: type === null ? null : { type, n }, nowMs: o.nowMs ?? day, policy: o.policy, save }, expected);
        if (!expected) prev = { ref: save };
      };
      const points = (o = {}, expected = null) => run({ op: 'dailyPoints', modes: o.modes || 'shipped', account: prev, nowMs: o.nowMs ?? day, policy: o.policy }, expected);
      const claim = (at, expected = null, o = {}) => {
        const save = `c${++k}`;
        run({ op: 'dailyClaim', modes: o.modes || 'shipped', account: prev, at, nowMs: o.nowMs ?? day, policy: o.policy, save }, expected);
        if (!expected) prev = { ref: save, path: 'account' };
      };
      rec('login', 1); points(); rec('staminaSpent', 60); points(); claim(20); rec('staminaSpent', 60); points(); claim(40);
      claim(20, 'ALREADY_CLAIMED'); claim(60, 'NOT_ENOUGH_POINTS'); rec('battleWin', 2); points(); rec('battleWin', 1); points(); rec('affinity', 1); points();
      claim(60); claim(80); claim(100, 'NOT_ENOUGH_POINTS'); rec('levelUp', 1); points(); claim(100); claim(50, 'UNKNOWN_MILESTONE');
      rec('gacha', 1, 'INVALID_EVENT'); rec('login', 0, 'INVALID_EVENT'); rec(null, 0, 'INVALID_EVENT'); rec('contentClear', 1, 'INVALID_EVENT');
      points({ policy: 'target' }, 'CONTENT_NOT_AUTHORED'); points({ policy: 'later' }, 'UNKNOWN_POLICY');
      points({ nowMs: next - 1 }); points({ nowMs: next }); rec('login', 3, null, { nowMs: next }); points({ nowMs: next }); claim(20, null, { nowMs: next });
      prev = account(); const T = { modes: 'dailyTarget', policy: 'target' };
      for (const type of ['login', 'staminaSpent', 'battleWin', 'affinity', 'contentClear']) { rec(type, 1, null, T); points(T); }
      rec('battleWin', 2, null, T); points(T); rec('affinity', 2, null, T); points(T); rec('levelUp', 3, null, T); points(T); rec('lobbyTalk', 1, null, T); points(T);
      claim(100, null, T); claim(20, null, T); points({ modes: 'dailyTarget' }); claim(40, null, { modes: 'dailyTarget' });
      prev = account(); const TD = { modes: 'dailyTargetDefault' };
      rec('login', 1, null, TD); rec('battleWin', 2, null, TD); points(TD); rec('contentClear', 1, null, TD); points(TD); claim(100, null, TD); rec('levelUp', 1, 'INVALID_EVENT', TD);
    }));
  } finally { restore(); }

  const ids = cases.map(c => c.id);
  assert.equal(new Set(ids).size, ids.length, 'Duplicate content-mode case id');
  for (const key of ['reserveHeld', 'reserveImmediate', 'reserveUlt', 'inkExhausted', 'debtCardBonus', 'debtRaw', 'inkInsert', 'bloomBlock', 'echoChainCleared', 'waveSpawn'])
    assert.ok(witness[key] > 0, `Missing content-mode witness ${key}`);
  const events = new Set(cases.flatMap(c => c.ops.flatMap(o => o.result && o.result.response ? o.result.response.ev.map(e => e.e) : [])));
  for (const e of ['wave', 'riftEcho', 'insert', 'phase', 'break']) assert.ok(events.has(e), `Missing content-mode event ${e}`);
  assert.deepEqual(sources(), loadedSources, 'Content-mode sources changed during generation; rerun the exporter');
  const ops = cases.flatMap(c => c.ops);
  const count = name => ops.filter(o => o.op === name).length;
  return {
    schema, scope, dataVersion: DATA.meta.version, sources: clone(loadedSources), dataPatches: clone(dataPatches), modes: clone(modesVariants),
    witness: Object.fromEntries(Object.entries(witness).sort()),
    counts: { cases: cases.length, ops: ops.length, errors: ops.filter(o => o.outcome === 'error').length, battleCommands: count('battleCmd'), riftCommands: count('riftCmd'),
      ledgers: ops.filter(o => /Settle|Sweep|Reward|Stars|daily/.test(o.op)).length, time: count('time') },
    cases,
  };
}

export function checkFixture(fixture) {
  assert.equal(fixture.schema, schema, 'Wrong content-mode fixture schema');
  assert.deepEqual(fixture.sources, loadedSources, 'Content-mode source provenance is stale');
  const expected = generateCases();
  assert.deepEqual(fixture.counts, expected.counts, 'Content-mode counts differ');
  assert.ok(isDeepStrictEqual(fixture, expected), 'Content-mode fixture ops, results, hashes or variants differ');
  return fixture.counts;
}

export function exportVectors(output = fixturePath) {
  const fixture = generateCases();
  const json = Buffer.from(`${JSON.stringify(fixture)}\n`);
  const bytes = gzipSync(json, { level: 9 });
  const temporary = mkdtempSync(join(dirname(output), '.content-modes-'));
  try {
    const path = join(temporary, 'fixture');
    writeFileSync(path, bytes);
    assert.deepEqual(sources(), fixture.sources, 'Content-mode sources changed before export; previous fixture preserved');
    renameSync(path, output);
  } finally { rmSync(temporary, { recursive: true, force: true }); }
  return { ...fixture.counts, bytes: bytes.length, uncompressedBytes: json.length, sha256: sha256(bytes) };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  if (args.length > 1 || (args.length && !['--check', '--help'].includes(args[0]))) throw new Error('Usage: node tools/gen_content_modes_vectors.mjs [--check|--help]');
  if (args[0] === '--help') console.log('Generate the content-mode op-script oracle for the C# port, or --check its gzip fixture.');
  else if (args[0] === '--check') console.log(JSON.stringify({ checked: fixturePath, ...checkFixture(JSON.parse(gunzipSync(readFileSync(fixturePath)).toString('utf8'))) }));
  else console.log(JSON.stringify({ output: fixturePath, ...exportVectors() }, null, 2));
}
