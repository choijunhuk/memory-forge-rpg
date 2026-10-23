// §11.5 성장 던전 · §11.6 주간 보스 · §11.7 기록 균열 · §11.13 일일 활동 — engine/content-modes.js.
// Fixture waves, rotations, star thresholds and daily pools below are TEST-ONLY stand-ins for DECISION_PENDING data;
// the shipped data/content_modes.json keeps them null and the engine refuses to run them.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const MF = require('../rules-core.js');
const CM = require('../content-modes.js');
const DATA = require('../../data/game_data.json');
const MODES = require('../../data/content_modes.json');
const clone = (v) => JSON.parse(JSON.stringify(v));
const P = DATA.progression;
const ALL = DATA.characters.map((c) => c.id);
const account = (extra = {}) => ({ level: 60, stamina: 240, owned: [...ALL], ...extra });
const team = (...ids) => ids.map((charId) => ({ charId, level: 60 }));
const KST = (y, mo, d, h, mi = 0) => Date.UTC(y, mo - 1, d, h - 9, mi);

// greedy bot: first affordable "now" card on the lowest-HP enemy, otherwise end the turn
function fight(getBattle, cmd, maxTurns = 40) {
  for (let guard = 0; guard < 500; guard++) {
    const b = getBattle();
    if (!b || b.s.phase !== 'PLAYER' || b.s.turn > maxTurns) return;
    const target = b.alive().slice().sort((x, y) => x.hp - y.hp)[0]?.uid ?? null;
    const uid = b.s.zones.hand.find((u) => b.check(u, 'now', target, []) === null);
    if (uid) cmd('play', { uid, mode: 'now', target }); else cmd('end');
  }
}
const fightBattle = (b) => fight(() => b, (k, a) => b.do(k, a));
function modesWith(fn) { const m = clone(MODES); fn(m); return m; }
const ended = (b, phase, extra = {}) => { const s = clone(b.s); s.phase = phase; s.result = { phase, turn: s.turn, hp: s.hp, ...extra }; return s; };

test('content-mode data only restates the source and agrees with progression data', () => {
  assert.deepEqual(Object.keys(MODES.rift.modifiers).filter((k) => k !== 'source').sort(), P.recordRift.modifiers.map((m) => m.id).sort());
  assert.deepEqual([...CM.RULES].sort(), P.recordRift.modifiers.map((m) => m.id).sort());
  for (const id of MODES.weeklyBoss.bosses) assert.equal(DATA.enemies.find((e) => e.id === id)?.tier, 'boss', id);
  assert.deepEqual(Object.keys(MODES.daily.goals), P.missions.daily.tasks.map((t) => t.id));
  // §13.3 day 1·15·29 = 14-day cycles, three per 42-day season
  assert.equal(P.recordRift.cycleDays, 14); assert.equal(P.recordRift.cyclesPerSeason * P.recordRift.cycleDays, P.seasonIncome.days);
  // §11.7/§11.13: star portions are exact for every floor; 800 per cycle, floors 1–3 = 520 (65%), one star = 312 (39%)
  assert.equal(MODES.rift.starPortionsPct.reduce((a, b) => a + b, 0), 100);
  for (const f of P.recordRift.floors) for (const pct of MODES.rift.starPortionsPct) assert.equal(f.reward * pct % 100, 0, `floor ${f.floor}`);
  assert.equal(P.recordRift.floors.reduce((a, f) => a + f.reward, 0), 800);
  const early = P.recordRift.floors.filter((f) => f.floor <= 3);
  assert.equal(early.reduce((a, f) => a + f.reward, 0), 520);
  assert.equal(early.reduce((a, f) => a + f.reward * MODES.rift.starPortionsPct[0] / 100, 0), 312);
  // pending items stay null in shipped data
  assert.equal(MODES.dungeon.waves.encounters, null); assert.equal(MODES.rift.rotation.cycles, null);
  assert.equal(MODES.rift.stars.maxTotalTurns, null); assert.equal(MODES.rift.stars.minHpPct, null); assert.equal(MODES.daily.targetPool.tasks, null);
});

test('reset clocks: daily 05:00 KST, weekly Monday 05:00 KST, rift cycles season day 1·15·29 11:00 KST', () => {
  assert.equal(CM.Time.dayKey(MODES, KST(2026, 10, 19, 5) - 1), '2026-10-18');
  assert.equal(CM.Time.dayKey(MODES, KST(2026, 10, 19, 5)), '2026-10-19');
  assert.equal(CM.Time.weekKey(MODES, KST(2026, 10, 19, 5) - 1), '2026-10-12');
  assert.equal(CM.Time.weekKey(MODES, KST(2026, 10, 19, 5)), '2026-10-19');
  assert.equal(CM.Time.weekKey(MODES, KST(2026, 10, 26, 4, 59)), '2026-10-19');
  const at = (d, h) => CM.Time.riftCycle(DATA, MODES, '2026-11-01', KST(2026, 11, d, h));
  assert.throws(() => at(1, 10), { code: 'RIFT_CLOSED' });
  assert.deepEqual([at(1, 11).index, at(1, 11).key], [0, '2026-11-01']);
  assert.deepEqual([at(15, 10).index, at(15, 11).index, at(15, 11).key], [0, 1, '2026-11-15']);
  assert.deepEqual([at(29, 11).index, at(29, 11).key], [2, '2026-11-29']);
  assert.throws(() => CM.Time.riftCycle(DATA, MODES, '2026-11-01', KST(2026, 12, 13, 11)), { code: 'RIFT_CLOSED' });
  assert.throws(() => CM.Time.riftCycle(DATA, MODES, '11/01', KST(2026, 11, 2, 0)), { code: 'INVALID_SEASON' });
  assert.throws(() => CM.Time.dayKey(MODES, NaN), { code: 'INVALID_TIME' });
});

test('ModeBattle#command is Battle#command with only the candidate constructor changed', () => {
  const norm = (f) => f.toString().replace(/\s+/g, ' ');
  assert.equal(norm(CM.ModeBattle.prototype.command), norm(MF.Battle.prototype.command).replace('new Battle(this.ix', 'new this.constructor(this.ix'));
});

test('without waves or a rule a ModeBattle plays exactly like Battle', () => {
  const setup = { party: team('sera', 'harin', 'rien'), deck: CM.partySetup(MF.indexData(DATA), team('sera', 'harin', 'rien'), null, ALL).deck,
    hp: 150, maxHp: 150, seed: 77, enemies: ['e_moth', 'e_cog'] };
  const plain = MF.Battle.create(DATA, clone(setup)); const mode = CM.ModeBattle.create(DATA, clone(setup), { kind: 'test' });
  plain.do('begin'); mode.do('begin');
  for (let guard = 0; guard < 400 && plain.s.phase === 'PLAYER'; guard++) {
    const target = plain.alive()[0]?.uid ?? null;
    const uid = plain.s.zones.hand.find((u) => plain.check(u, 'now', target, []) === null);
    const args = uid ? ['play', { uid, mode: 'now', target }] : ['end'];
    plain.do(...args); mode.do(...args);
    const { mode: _m, ...rest } = mode.s; assert.deepEqual(rest, plain.s);
  }
  assert.ok(['VICTORY', 'DEFEAT'].includes(plain.s.phase));
});

test('§11.5 dungeon rewards scale by tier with an integer floor; unlock by account level', () => {
  assert.deepEqual(CM.Dungeon.reward(DATA, 'dg_ink', 6), { ink: 24000 });
  assert.deepEqual(CM.Dungeon.reward(DATA, 'dg_ink', 1), { ink: 6000 });
  assert.deepEqual(CM.Dungeon.reward(DATA, 'dg_oil', 1), { oil: 3000, alloy: 0 });
  assert.deepEqual(CM.Dungeon.reward(DATA, 'dg_credit', 5), { credits: 34000 });
  assert.throws(() => CM.Dungeon.reward(DATA, 'dg_ink', 7), { code: 'UNKNOWN_TIER' });
  const opts = { dungeonId: 'dg_ink', tier: 1, party: team('sera', 'harin', 'rien') };
  assert.throws(() => CM.Dungeon.start(DATA, MODES, account(), opts), { code: 'CONTENT_NOT_AUTHORED' });
  const m = modesWith((x) => { x.dungeon.waves.encounters = { dg_ink: { 1: [['e_page']] }, dg_polish: { 1: [['e_page']] } }; });
  assert.throws(() => CM.Dungeon.start(DATA, m, account({ level: 4 }), opts), { code: 'LOCKED' });
  assert.throws(() => CM.Dungeon.start(DATA, m, account({ level: 19 }), { ...opts, dungeonId: 'dg_polish' }), { code: 'LOCKED' });
  assert.throws(() => CM.Dungeon.start(DATA, m, account({ stamina: 19 }), opts), { code: 'NOT_ENOUGH_STAMINA' });
  assert.throws(() => CM.Dungeon.start(DATA, m, account({ owned: ['sera', 'harin'] }), opts), { code: 'NOT_OWNED' });
  const four = modesWith((x) => { x.dungeon.waves.encounters = { dg_ink: { 1: [['e_page'], ['e_page'], ['e_page'], ['e_page']] } }; });
  assert.throws(() => CM.Dungeon.start(DATA, four, account(), opts), { code: 'INVALID_CONTENT' });
});

test('§11.5 stars: victory, within 4 turns, party HP at least 50%', () => {
  const s = (turn, hp) => CM.Dungeon.stars(MODES, { phase: 'VICTORY', turn, hp }, 200);
  assert.equal(s(4, 100), 3); assert.equal(s(5, 100), 2); assert.equal(s(4, 99), 2); assert.equal(s(5, 99), 1);
  assert.equal(CM.Dungeon.stars(MODES, { phase: 'DEFEAT', turn: 1, hp: 0 }, 200), 0);
});

test('§11.5 waves continue the same battle; victory spends 20 집중 once and records best stars', () => {
  const m = modesWith((x) => { x.dungeon.waves.encounters = { dg_ink: { 1: [['e_page'], ['e_page', 'e_page']] } }; });
  const acc = account();
  const b = CM.Dungeon.start(DATA, m, acc, { dungeonId: 'dg_ink', tier: 1, party: team('sera', 'harin', 'rien'), seed: 5 });
  assert.equal(b.s.diff.hp, MF.Stats.stageScale(MF.indexData(DATA), 10).hp);
  fightBattle(b);
  assert.equal(b.s.phase, 'VICTORY');
  assert.deepEqual(b.s.log.filter((l) => l.e === 'wave').map((l) => [l.wave, l.left]), [[2, 0]]);
  assert.equal(b.s.enemies.length, 3); assert.ok(b.s.enemies.every((e) => !e.alive));
  const before = clone(acc);
  const r = CM.Dungeon.settle(DATA, m, acc, b.s);
  assert.deepEqual(acc, before, 'input account is not mutated');
  assert.equal(r.stars, CM.Dungeon.stars(m, b.s.result, b.s.maxHp));
  assert.equal(r.account.stamina, 220);
  assert.equal(r.account.dungeons['dg_ink:1'], r.stars);
  assert.deepEqual(r.grant, { ink: 6000, accountXp: 20 });
  assert.equal(r.battle.mode.settled, true);
  assert.throws(() => CM.Dungeon.settle(DATA, m, r.account, r.battle), { code: 'ALREADY_SETTLED' });
  assert.throws(() => CM.Dungeon.settle(DATA, m, acc, CM.Dungeon.start(DATA, m, acc, { dungeonId: 'dg_ink', tier: 1, party: team('sera') }).s), { code: 'BATTLE_NOT_OVER' });
  // defeat stamina is DECISION_PENDING: a lost battle cannot settle, so it is neither free nor charged
  assert.throws(() => CM.Dungeon.settle(DATA, m, acc, ended(b, 'DEFEAT')), { code: 'CONTENT_NOT_AUTHORED' });
  // a better clear raises best stars, a worse one keeps it
  const three = CM.Dungeon.settle(DATA, m, { ...acc, dungeons: { 'dg_ink:1': 1 } }, ended(b, 'VICTORY', { turn: 2, hp: b.s.maxHp }));
  assert.equal(three.account.dungeons['dg_ink:1'], 3);
  const worse = CM.Dungeon.settle(DATA, m, three.account, ended(b, 'VICTORY', { turn: 9, hp: 1 }));
  assert.equal(worse.account.dungeons['dg_ink:1'], 3);
  assert.throws(() => CM.WeeklyBoss.settle(DATA, m, acc, b.s, 0), { code: 'WRONG_CONTENT' });
});

test('§11.5 sweep: only after 3 stars on that tier, 1..12 times, same reward and 집중 per run', () => {
  const acc = account({ dungeons: { 'dg_ink:6': 3, 'dg_ink:5': 2 } });
  const r = CM.Dungeon.sweep(DATA, MODES, acc, { dungeonId: 'dg_ink', tier: 6, times: 12 });
  assert.equal(r.account.stamina, 0);
  assert.deepEqual(r.grant, { ink: 24000 * 12, accountXp: 240 });
  assert.equal(acc.stamina, 240, 'input account is not mutated');
  assert.throws(() => CM.Dungeon.sweep(DATA, MODES, acc, { dungeonId: 'dg_ink', tier: 5, times: 1 }), { code: 'SWEEP_LOCKED' });
  assert.throws(() => CM.Dungeon.sweep(DATA, MODES, acc, { dungeonId: 'dg_ink', tier: 6, times: 13 }), { code: 'INVALID_SWEEP' });
  assert.throws(() => CM.Dungeon.sweep(DATA, MODES, acc, { dungeonId: 'dg_ink', tier: 6, times: 0 }), { code: 'INVALID_SWEEP' });
  assert.throws(() => CM.Dungeon.sweep(DATA, MODES, acc, { dungeonId: 'dg_ink', tier: 6, times: 1.5 }), { code: 'INVALID_SWEEP' });
  assert.throws(() => CM.Dungeon.sweep(DATA, MODES, { ...acc, stamina: 39 }, { dungeonId: 'dg_ink', tier: 6, times: 2 }), { code: 'NOT_ENOUGH_STAMINA' });
});

test('§11.6 weekly boss: unlock 15, four bosses, difficulty table, real victory pays once', () => {
  const opts = { bossId: 'b_bell', difficulty: 1, party: team('sera', 'harin', 'rien'), seed: 3 };
  assert.throws(() => CM.WeeklyBoss.start(DATA, MODES, account({ level: 14 }), opts), { code: 'LOCKED' });
  assert.throws(() => CM.WeeklyBoss.start(DATA, MODES, account(), { ...opts, bossId: 'e_moth' }), { code: 'UNKNOWN_CONTENT' });
  assert.throws(() => CM.WeeklyBoss.start(DATA, MODES, account(), { ...opts, difficulty: 6 }), { code: 'UNKNOWN_TIER' });
  const five = CM.WeeklyBoss.start(DATA, MODES, account(), { ...opts, difficulty: 5 });
  const sc = MF.Stats.stageScale(MF.indexData(DATA), 60);
  assert.deepEqual(five.s.diff, { hp: Math.round(130 * sc.hp / 100), dmg: Math.round(120 * sc.dmg / 100) });
  assert.equal(five.s.enemies[0].maxHp, Math.round(130 * five.s.diff.hp / 100));
  // a real fight against a weakened copy of the boss (test data) reaches the settle step
  const weak = clone(DATA); weak.enemies.find((e) => e.id === 'b_bell').hp = 6;
  const b = CM.WeeklyBoss.start(weak, MODES, account(), opts);
  fightBattle(b);
  assert.equal(b.s.phase, 'VICTORY');
  const r = CM.WeeklyBoss.settle(weak, MODES, account(), b.s, KST(2026, 10, 20, 12));
  assert.deepEqual(r.grant, { core: 2, credits: 20000, bossToken: 40, accountXp: 100 });
  assert.deepEqual(r.account.weekly, { key: '2026-10-19', claimed: 1 });
  assert.equal(r.battle.mode.rewarded, true);
});

test('§11.6/§11.13 shared 3 claims per week, defeat and practice never count, Monday 05:00 resets', () => {
  const start = (bossId, practice) => CM.WeeklyBoss.start(DATA, MODES, account(), { bossId, difficulty: 2, party: team('sera'), practice });
  const now = KST(2026, 10, 21, 9);
  let acc = account();
  const settle = (s, at = now) => { const r = CM.WeeklyBoss.settle(DATA, MODES, acc, s, at); acc = r.account; return r; };
  assert.equal(settle(ended(start('b_bell'), 'DEFEAT')).reason, 'DEFEAT');
  assert.equal(acc.weekly.claimed, 0);
  assert.equal(settle(ended(start('b_bell', true), 'VICTORY')).reason, 'PRACTICE');
  assert.equal(acc.weekly.claimed, 0);
  for (const boss of ['b_bell', 'b_notary', 'b_archivist']) assert.equal(settle(ended(start(boss), 'VICTORY')).reason, null);
  assert.equal(acc.weekly.claimed, 3);
  const over = settle(ended(start('b_garden'), 'VICTORY'));
  assert.deepEqual([over.reason, over.grant, over.battle.mode.settled, over.battle.mode.rewarded], ['WEEKLY_LIMIT', null, true, false]);
  assert.throws(() => CM.WeeklyBoss.settle(DATA, MODES, acc, over.battle, KST(2026, 10, 27, 9)), { code: 'ALREADY_SETTLED' });
  assert.equal(settle(ended(start('b_garden'), 'VICTORY'), KST(2026, 10, 26, 4, 59)).reason, 'WEEKLY_LIMIT');
  const fresh = settle(ended(start('b_garden'), 'VICTORY'), KST(2026, 10, 26, 5));
  assert.equal(fresh.reason, null); assert.deepEqual(acc.weekly, { key: '2026-10-26', claimed: 1 });
});

// rift fixtures: one cycle with a rule per floor and three weak lineups per floor
const RIFT_RULES = ['rift_reserve', 'rift_break', 'rift_ink', 'rift_debt', 'rift_bloom', 'rift_echo'];
const riftModes = (fn = () => {}) => modesWith((x) => {
  x.rift.rotation.cycles = [{ rules: RIFT_RULES, encounters: RIFT_RULES.map(() => [['e_page'], ['e_page'], ['e_page']]) }];
  fn(x);
});
const CYCLE = { index: 0, key: '2026-11-01' };
const A = team('sera', 'harin', 'rien'); const B = team('yuri', 'nia', 'kaya');
function clearFloor(m, acc, opts) {
  const run = CM.RiftRun.create(DATA, m, acc, { cycle: CYCLE, seed: 9, ...opts });
  while (run.s.phase === 'READY') { run.next(); fight(() => run.battle, (k, a) => run.battleCmd(k, a)); }
  return run;
}

test('§11.7 rift floor setup: unlock 20, authored rotation required, team count by policy, re-entry only below six owned', () => {
  const opts = { floor: 1, cycle: CYCLE, teams: [A, B] };
  assert.throws(() => CM.RiftRun.create(DATA, MODES, account(), opts), { code: 'CONTENT_NOT_AUTHORED' });
  const m = riftModes();
  assert.throws(() => CM.RiftRun.create(DATA, m, account({ level: 19 }), opts), { code: 'LOCKED' });
  assert.throws(() => CM.RiftRun.create(DATA, m, account(), { ...opts, floor: 7 }), { code: 'UNKNOWN_TIER' });
  assert.throws(() => CM.RiftRun.create(DATA, m, account(), { ...opts, cycle: { index: 3, key: 'x' } }), { code: 'INVALID_CYCLE' });
  assert.throws(() => CM.RiftRun.create(DATA, m, account(), { ...opts, teams: [A] }), { code: 'TEAM_COUNT' });
  // §11.13 target: floors 1–3 one team (all three battles), 4–6 two teams
  const one = CM.RiftRun.create(DATA, m, account(), { ...opts, teams: [A], policy: 'target' });
  assert.deepEqual(one.s.order, [0, 0, 0]);
  assert.throws(() => CM.RiftRun.create(DATA, m, account(), { ...opts, floor: 4, teams: [A], policy: 'target' }), { code: 'TEAM_COUNT' });
  assert.throws(() => CM.RiftRun.create(DATA, m, account(), { ...opts, policy: 'later' }), { code: 'UNKNOWN_POLICY' });
  // §11.7 six different characters: two full parties (a 1+1 entry cannot dodge the roster requirement)
  assert.throws(() => CM.RiftRun.create(DATA, m, account(), { ...opts, teams: [team('sera'), team('yuri')] }), { code: 'TEAM_SIZE' });
  assert.throws(() => CM.RiftRun.create(DATA, m, account(), { ...opts, teams: [A, team('yuri', 'nia')] }), { code: 'TEAM_SIZE' });
  const reuse = [A, team('sera', 'yuri', 'nia')];
  assert.throws(() => CM.RiftRun.create(DATA, m, account(), { ...opts, teams: reuse }), { code: 'PARTY_OVERLAP' });
  const small = CM.RiftRun.create(DATA, m, account({ owned: ['sera', 'harin', 'rien', 'yuri', 'nia'] }), { ...opts, teams: reuse });
  assert.equal(small.s.starCap, 2);
  const six = CM.RiftRun.create(DATA, m, account(), opts);
  assert.deepEqual([six.s.order, six.s.starCap, six.s.rule], [[0, 1, 0], 3, 'rift_reserve']);
  const f6 = CM.RiftRun.create(DATA, m, account(), { ...opts, floor: 6 });
  const sc = MF.Stats.stageScale(MF.indexData(DATA), 60);
  assert.deepEqual(f6.s.difficulty, { hp: Math.round(125 * sc.hp / 100), dmg: Math.round(120 * sc.dmg / 100) });
});

test('§11.7 three battles: team order 1·2·1, team HP carries, deck/reservations/gauge reset each battle', () => {
  const m = riftModes();
  const run = CM.RiftRun.create(DATA, m, account(), { floor: 1, cycle: CYCLE, teams: [A, B], seed: 9 });
  const seen = [];
  while (run.s.phase === 'READY') {
    const carried = run.s.teams[run.s.order[run.s.idx]].hp;
    run.next();
    const bs = run.s.battle;
    seen.push({ party: bs.actors.map((a) => a.id), hp: bs.hp, carried, cards: Object.keys(bs.cards).length, gauges: bs.actors.map((a) => a.gauge), res: bs.res.length, rule: bs.mode.rule });
    fight(() => run.battle, (k, a) => run.battleCmd(k, a));
  }
  assert.equal(run.s.phase, 'CLEARED');
  assert.deepEqual(seen.map((x) => x.party), [['sera', 'harin', 'rien'], ['yuri', 'nia', 'kaya'], ['sera', 'harin', 'rien']]);
  for (const x of seen) { assert.equal(x.hp, x.carried); assert.ok(x.gauges.every((g) => g === 0)); assert.equal(x.res, 0); assert.equal(x.rule, 'rift_reserve'); }
  assert.equal(seen[2].hp, run.s.results[0].hp, 'team 1 starts battle 3 with the HP it ended battle 1 with');
  assert.equal(seen[2].cards, seen[0].cards, 'battle 3 rebuilds the saved deck');
  assert.equal(run.s.results.length, 3);
  assert.throws(() => run.next(), { code: 'WRONG_PHASE' });
  // persisted state restores mid-floor (rift_break needs its data rule back)
  const f2 = CM.RiftRun.create(DATA, m, account(), { floor: 2, cycle: CYCLE, teams: [A, B], seed: 4 }); f2.next();
  const restored = new CM.RiftRun(DATA, m, clone(f2.s));
  assert.equal(restored.battle.ix.rules.brokenDamageBps, 5000);
  assert.equal(restored.battle.constructor, CM.ModeBattle);
});

test('§11.7 reward: per floor per cycle, star portions 60/20/20, 2-star cap on re-entry; pending thresholds block settlement', () => {
  // shipped data: total-turn / remaining-HP thresholds are DECISION_PENDING, so a cleared floor cannot be scored or settled
  const m = riftModes();
  const pending = clearFloor(m, account(), { floor: 1, teams: [A, B] });
  assert.equal(pending.s.phase, 'CLEARED');
  assert.throws(() => pending.stars(), { code: 'CONTENT_NOT_AUTHORED' });
  const acc0 = account(); const state0 = clone(pending.s);
  assert.throws(() => CM.Rift.settle(DATA, m, acc0, pending.s), { code: 'CONTENT_NOT_AUTHORED' });
  assert.deepEqual([acc0, pending.s], [account(), state0], 'nothing changes on refusal');
  // TEST-ONLY thresholds: strict → star 1 only (60%), lenient → all stars; the improvement pays only the unpaid 20% + 20%
  const strict = riftModes((x) => { x.rift.stars.maxTotalTurns = 0; x.rift.stars.minHpPct = 101; });
  const lenient = riftModes((x) => { x.rift.stars.maxTotalTurns = 99; x.rift.stars.minHpPct = 1; });
  const run = clearFloor(strict, account(), { floor: 1, teams: [A, B] });
  const r1 = CM.Rift.settle(DATA, strict, account(), run.s);
  assert.deepEqual([r1.stars, r1.grant], [1, { crystals: 120 }]);
  assert.deepEqual(r1.account.rift, { key: '2026-11-01', floors: { 1: 1 } });
  assert.throws(() => CM.Rift.settle(DATA, strict, r1.account, r1.rift), { code: 'ALREADY_SETTLED' });
  assert.equal(CM.Rift.settle(DATA, strict, r1.account, run.s).grant, null, 'same stars again in the cycle pay nothing');
  const better = clearFloor(lenient, account(), { floor: 1, teams: [A, B] });
  const r2 = CM.Rift.settle(DATA, lenient, r1.account, better.s);
  assert.deepEqual([r2.stars, r2.grant], [3, { crystals: 80 }]);
  assert.equal(CM.Rift.settle(DATA, lenient, r2.account, better.s).grant, null, 'a floor never pays more than its reward per cycle');
  // re-entry below six owned caps the floor at two stars
  const small = account({ owned: ['sera', 'harin', 'rien', 'yuri', 'nia'] });
  const capped = clearFloor(lenient, small, { floor: 3, teams: [A, team('sera', 'yuri', 'nia')] });
  const r3 = CM.Rift.settle(DATA, lenient, small, capped.s);
  assert.deepEqual([r3.stars, r3.grant], [2, { crystals: 128 }]);
  // cycles: older states are refused, a newer cycle resets the ledger, a failed floor pays nothing (no threshold needed)
  assert.throws(() => CM.Rift.settle(DATA, strict, { ...r1.account, rift: { key: '2026-11-15', floors: {} } }, run.s), { code: 'STALE_CYCLE' });
  const next = CM.Rift.settle(DATA, strict, { ...r1.account, rift: { key: '2026-10-18', floors: { 1: 3 } } }, run.s);
  assert.deepEqual(next.account.rift, { key: '2026-11-01', floors: { 1: 1 } });
  const failed = { ...clone(pending.s), phase: 'FAILED' };
  assert.deepEqual([CM.Rift.settle(DATA, m, account(), failed).stars, CM.Rift.settle(DATA, m, account(), failed).grant], [0, null]);
  const mid = CM.RiftRun.create(DATA, m, account(), { floor: 1, cycle: CYCLE, teams: [A, B] });
  assert.throws(() => CM.Rift.settle(DATA, m, account(), mid.s), { code: 'RIFT_NOT_OVER' });
});

// ── rift rules: each compared with the same battle without the rule ──
function pair(rule, setup) {
  const params = MODES.rift.modifiers[rule];
  const plain = CM.ModeBattle.create(DATA, clone(setup), { kind: 'rift' });
  const ruled = CM.ModeBattle.create(DATA, clone(setup), { kind: 'rift', rule, params });
  return { plain, ruled, params };
}
const bossSetup = (party, deck) => ({ party: party.map((charId) => ({ charId })), deck: deck.map((id) => ({ id })), hp: 300, maxHp: 300, seed: 11, enemies: ['b_archivist'] });

// RULES_NOW_PLAY (2026-10-19): reservations are gone; rift_reserve now keys on ctx.held (a 보존 card kept from an earlier turn).
test('rift_reserve: held card damage +30%, every other card hit −10%', () => {
  const { plain, ruled, params } = pair('rift_reserve', bossSetup(['sera'], Array(10).fill('sera_timed')));
  const e = ruled.s.enemies[0];
  const diff = (ctx) => ruled.dmgBonusBps(ctx, e) - plain.dmgBonusBps(ctx, e);
  assert.equal(diff({ held: true }), params.reservedBps);
  assert.equal(diff({ held: false }), params.immediateBps);
  assert.equal(diff({ reserved: 'next' }), params.immediateBps, 'a legacy reservation kind no longer earns the bonus');
  assert.deepEqual([params.reservedBps, params.immediateBps], [3000, -1000]);
  plain.do('begin'); ruled.do('begin');
  const v = DATA.cards.find((c) => c.id === 'sera_timed').eff[0].v;
  const hit = (b) => { const uid = b.s.zones.hand[0]; const held = b.s.cards[uid].held;
    const r = b.do('play', { uid, mode: 'now', target: b.s.enemies[0].uid });
    return [held, r.ev.find((x) => x.e === 'dmg' && x.src === 'card').n]; };
  // turn 1: a fresh (not held) sera_timed
  assert.deepEqual([hit(plain), hit(ruled)], [[false, v], [false, Math.floor(v * (10000 + params.immediateBps) / 10000)]]);
  // turn 2: the other four sera_timed stayed in hand (보존) and are held
  plain.do('end'); ruled.do('end');
  assert.deepEqual([hit(plain), hit(ruled)], [[true, v], [true, Math.floor(v * (10000 + params.reservedBps) / 10000)]]);
});

test('rift_break: break gauge ×1.5 (rounded) for every enemy and phase, broken bonus +50% instead of +25%', () => {
  const { plain, ruled } = pair('rift_break', bossSetup(['sera'], ['sera_round']));
  assert.equal(ruled.ix.rules.brokenDamageBps, 5000); assert.equal(plain.ix.rules.brokenDamageBps, 2500);
  assert.equal(DATA.rules.brokenDamageBps, 2500, 'shared data untouched');
  assert.equal(ruled.s.enemies[0].brkMax, 18); assert.equal(plain.s.enemies[0].brkMax, 12);
  for (const e of DATA.enemies) assert.equal(ruled.ix.enemies[e.id].brk, Math.max(1, Math.round(e.brk * 1.5)), e.id);
  const late = require('../../proposals/late_enemies.cjs').withLateEnemies(DATA);
  const ix = CM.ModeBattle.create(late, bossSetup(['sera'], ['sera_round']), { kind: 'rift', rule: 'rift_break', params: MODES.rift.modifiers.rift_break }).ix;
  assert.equal(ix.enemies.b_nameless.phase.brk, Math.round(15 * 1.5));
  // a broken target takes ×1.5 instead of ×1.25 from the same hit
  for (const b of [plain, ruled]) { b.s.enemies[0].brokenUntil = 5; }
  const op = { op: 'dmg', v: 100 };
  const hit = (b) => b.calcDamage(op, { inst: null, actor: null, reserved: null }, b.s.enemies[0]);
  assert.equal(hit(ruled) * 125, hit(plain) * 150);
});

test('rift_ink: one smudge into the discard pile each turn start; +3% damage per exhausted card', () => {
  const { plain, ruled } = pair('rift_ink', bossSetup(['sera'], Array(8).fill('sera_round')));
  plain.do('begin'); ruled.do('begin');
  const smudges = (b) => Object.values(b.s.cards).filter((c) => c.id === 'smudge');
  assert.equal(smudges(plain).length, 0);
  assert.deepEqual(smudges(ruled).map((c) => [c.token, b_zone(ruled, c.uid)]), [[true, 'discard']]);
  ruled.do('end');
  assert.equal(smudges(ruled).length, 2);
  const e = ruled.s.enemies[0];
  const base = ruled.dmgBonusBps({ reserved: null }, e);
  ruled.s.exhausted = 3;
  assert.equal(ruled.dmgBonusBps({ reserved: null }, e) - base, 900);
  // the same exhausted count every other rule reads: Sion R4 starts the battle with exhaustStart counted
  ruled.s.exhausted = 0; ruled.s.exhaustStart = 3;
  assert.equal(ruled.dmgBonusBps({ reserved: null }, e) - base, 900);
});
function b_zone(b, uid) { return Object.entries(b.s.zones).find(([, z]) => z.includes(uid))?.[0]; }

test('rift_debt: debt cap +2 on top of relics; any debt adds +15% damage', () => {
  const setup = { ...bossSetup(['sera'], ['sera_round']), relics: ['contract_seal'] };
  const { plain, ruled } = pair('rift_debt', setup);
  assert.equal(ruled.s.debtCap, plain.s.debtCap + 2); assert.equal(plain.s.debtCap, DATA.rules.debtCap + 1);
  const e = ruled.s.enemies[0];
  assert.equal(ruled.dmgBonusBps({}, e) - plain.dmgBonusBps({}, e), 0);
  // raw party damage (counter, burn, relic) is part of "모든 피해": reference power4 160 → 10 becomes floor(10 × 1.15)
  const raw = (b) => { const t = b.s.enemies[0]; const before = t.hp; b.damageEnemyRaw(t, 10, 'burn'); return before - t.hp; };
  assert.deepEqual([raw(plain), raw(ruled)], [10, 10], 'no debt, no bonus');
  for (const b of [plain, ruled]) b.s.debts.push({ due: 3, n: 1 });
  assert.equal(ruled.dmgBonusBps({}, e) - plain.dmgBonusBps({}, e), 1500);
  assert.deepEqual([raw(plain), raw(ruled)], [10, 11]);
  for (const b of [plain, ruled]) b.s.enemies[0].block = 100;
  const counter = (b) => { const t = b.s.enemies[0]; const blk = t.block; b.damageEnemyRaw(t, 12, 'counter'); return blk - t.block; };
  assert.deepEqual([counter(plain), counter(ruled)], [12, 13]);
});

test('rift_bloom: growth cap +2; a resolving growth card adds 3 block', () => {
  const { plain, ruled } = pair('rift_bloom', bossSetup(['yuri'], ['yuri_sprout']));
  assert.equal(ruled.s.growthCap, plain.s.growthCap + 2);
  plain.do('begin'); ruled.do('begin');
  for (const b of [plain, ruled]) b.do('play', { uid: b.s.zones.hand[0], mode: 'now', target: b.s.enemies[0].uid });
  assert.equal(ruled.s.block - plain.s.block, 3);
});

test('rift_echo: echo max +2; three different characters in a row give 1 AP once per turn; ownerless cards break the chain', () => {
  const { plain, ruled } = pair('rift_echo', bossSetup(['sera', 'harin', 'rien'], ['sera_round', 'harin_bash', 'rien_note']));
  assert.equal(ruled.s.echoMax, plain.s.echoMax + 2);
  plain.do('begin'); ruled.do('begin');
  const byId = (b, id) => b.s.zones.hand.find((u) => b.s.cards[u].id === id);
  for (const id of ['sera_round', 'harin_bash', 'rien_note']) for (const b of [plain, ruled]) b.do('play', { uid: byId(b, id), mode: 'now', target: b.s.enemies[0].uid });
  assert.equal(ruled.s.ap, plain.s.ap + 1);
  assert.equal(ruled.s.log.filter((l) => l.e === 'riftEcho').length, 1);
  // a common card between characters resets the chain
  const c = pair('rift_echo', bossSetup(['sera', 'harin', 'rien'], ['sera_round', 'com_guard', 'harin_bash', 'rien_note']));
  c.ruled.do('begin');
  for (const id of ['sera_round', 'com_guard', 'harin_bash', 'rien_note']) c.ruled.do('play', { uid: byId(c.ruled, id), mode: 'now', target: c.ruled.s.enemies[0].uid });
  assert.equal(c.ruled.s.log.filter((l) => l.e === 'riftEcho').length, 0);
});

test('rift rules are validated and only rift_break rewrites data', () => {
  assert.throws(() => CM.ModeBattle.create(DATA, bossSetup(['sera'], ['sera_round']), { kind: 'rift', rule: 'rift_x', params: {} }), { code: 'UNKNOWN_RULE' });
  assert.throws(() => CM.ModeBattle.create(DATA, bossSetup(['sera'], ['sera_round']), { kind: 'rift', rule: 'rift_ink' }), { code: 'UNKNOWN_RULE' });
  for (const rule of RIFT_RULES) assert.equal(CM.modeData(DATA, { rule, params: MODES.rift.modifiers[rule] }) === DATA, rule !== 'rift_break', rule);
});

test('§11.13 daily activity: v0.8.1 needs all five tasks, so a level-capped account stops at 80', () => {
  const day = KST(2026, 10, 19, 12);
  assert.throws(() => CM.Daily.record(DATA, MODES, account({ level: 4 }), { type: 'login', n: 1 }, day), { code: 'LOCKED' });
  let acc = account();
  for (const ev of [{ type: 'login', n: 1 }, { type: 'staminaSpent', n: 60 }, { type: 'staminaSpent', n: 60 }, { type: 'battleWin', n: 3 }, { type: 'affinity', n: 1 }]) acc = CM.Daily.record(DATA, MODES, acc, ev, day);
  assert.equal(CM.Daily.points(DATA, MODES, acc, day), 80);
  assert.throws(() => CM.Daily.claim(DATA, MODES, acc, 100, day), { code: 'NOT_ENOUGH_POINTS' });
  const c80 = CM.Daily.claim(DATA, MODES, acc, 80, day);
  assert.deepEqual(c80.grant, { crystals: 10, credits: 10000 });
  assert.throws(() => CM.Daily.claim(DATA, MODES, c80.account, 80, day), { code: 'ALREADY_CLAIMED' });
  acc = CM.Daily.record(DATA, MODES, c80.account, { type: 'levelUp', n: 1 }, day);
  const c100 = CM.Daily.claim(DATA, MODES, acc, 100, day);
  assert.deepEqual(c100.grant, { crystals: 20, passXp: 1000, accountXp: 300 });
  assert.throws(() => CM.Daily.claim(DATA, MODES, acc, 50, day), { code: 'UNKNOWN_MILESTONE' });
  assert.throws(() => CM.Daily.record(DATA, MODES, acc, { type: 'gacha', n: 1 }, day), { code: 'INVALID_EVENT' });
  assert.throws(() => CM.Daily.record(DATA, MODES, acc, { type: 'login', n: 0 }, day), { code: 'INVALID_EVENT' });
  // 05:00 KST rollover
  assert.equal(CM.Daily.points(DATA, MODES, acc, KST(2026, 10, 20, 4, 59)), 100);
  assert.equal(CM.Daily.points(DATA, MODES, acc, KST(2026, 10, 20, 5)), 0);
});

test('§11.13 target policy: a selectable pool reaches 100 without level-ups and caps at 100 (pool is pending data)', () => {
  const day = KST(2026, 10, 19, 12);
  assert.throws(() => CM.Daily.points(DATA, MODES, account(), day, 'target'), { code: 'CONTENT_NOT_AUTHORED' });
  // TEST-ONLY pool shape: the real task list, goals and points are DECISION_PENDING
  const m = modesWith((x) => { x.daily.targetPool.tasks = ['login', 'staminaSpent', 'battleWin', 'affinity', 'contentClear', 'levelUp', 'lobbyTalk'].map((event, i) => ({ id: `t${i}`, event, n: 1, pts: 20 })); });
  let acc = account();
  for (const type of ['login', 'staminaSpent', 'battleWin', 'affinity', 'contentClear']) acc = CM.Daily.record(DATA, m, acc, { type, n: 1 }, day, 'target');
  assert.equal(CM.Daily.points(DATA, m, acc, day, 'target'), 100);
  acc = CM.Daily.record(DATA, m, acc, { type: 'lobbyTalk', n: 1 }, day, 'target');
  assert.equal(CM.Daily.points(DATA, m, acc, day, 'target'), 100);
  assert.deepEqual(CM.Daily.claim(DATA, m, acc, 100, day, 'target').grant.accountXp, 300);
});
