// P1-CSHARP-LATE-ENEMY-ACTIONS: original-JS late-action oracle checks. Candidate overlay and test-local enemies only; no live activation.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { createHash } from 'node:crypto';
import { mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { gzipSync } from 'node:zlib';
import { generateCases, checkFixture, exportVectors, fixturePath, boundaryEnemies, behaviors, invalidPhases, invalidMoves } from '../../tools/gen_battle_late_actions_vectors.mjs';
import { decodeVectors } from '../../tools/gen_run_create_vectors.mjs';

const require = createRequire(import.meta.url);
const data = require('../../data/game_data.json');
const candidates = require('../../proposals/late_enemies.cjs');
const MF = require('../rules-core.js');
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = value => createHash('sha256').update(value).digest('hex');
const fixture = generateCases();
const byId = id => {
  const row = fixture.cases.find(c => c.id === id);
  assert.ok(row, `Missing late-action vector ${id}`);
  return row;
};
const edge = id => byId(`edge-${id}`);
const after = (row, i) => row.steps[i].state;
const ev = (row, i) => row.steps[i].response.ev;
const of = (row, i, kind) => ev(row, i).filter(e => e.e === kind);

// Independent sorted replacer, rather than the exporter's canonical serializer.
function hashState(value) {
  const keys = new Set();
  const visit = item => {
    if (!item || typeof item !== 'object') return;
    for (const key of Object.keys(item)) {
      if (key === 'log' || key === 'ev') continue;
      if (!Array.isArray(item)) keys.add(key);
      visit(item[key]);
    }
  };
  visit(value);
  return sha256(JSON.stringify(value, [...keys].sort()));
}

test('the overlay is the unchanged candidate proposal, test-local enemies stay separate and live content gains nothing', () => {
  const original = JSON.stringify(data);
  assert.deepEqual(fixture.overlay, { provenance: candidates.provenance, enemies: candidates.enemies, encounters: candidates.encounters });
  assert.equal(fixture.overlay.provenance.status, 'candidate-only');
  assert.deepEqual(fixture.boundaryEnemies, boundaryEnemies);
  assert.deepEqual(fixture.behaviors, ['apDrain', 'sealSlot', 'delayRes', 'silence', 'eatName', 'phase']);
  const live = new Set(data.enemies.map(e => e.id)), candidate = new Set(candidates.enemies.map(e => e.id));
  assert.deepEqual([data.enemies.length, candidates.enemies.length, boundaryEnemies.length], [20, 23, 17]);
  assert.ok(candidates.enemies.every(e => !live.has(e.id)));
  assert.ok(boundaryEnemies.every(e => e.id.startsWith('t_') && !live.has(e.id) && !candidate.has(e.id)));
  const late = new Set(behaviors);
  const visit = move => late.has(move.t) || (move.then ? visit(move.then) : false);
  assert.ok(data.enemies.every(e => !e.phase && !e.pattern.some(visit)), 'live content must not contain a late behavior or phase descriptor');
  const tags = new Set(candidates.enemies.flatMap(e => [...e.pattern, ...(e.phase ? e.phase.pattern : [])].map(m => m.t)));
  assert.ok(['apDrain', 'sealSlot', 'delayRes', 'silence', 'eatName'].every(t => tags.has(t)));
  assert.deepEqual(candidates.enemies.filter(e => e.phase).map(e => e.id), ['b_nameless']);
  // Independent literals from the proposal source: boss budgets and the second-body descriptor are unchanged.
  const named = Object.fromEntries(candidates.enemies.map(e => [e.id, e]));
  assert.deepEqual(['b_usurer', 'b_overture', 'b_nameless'].map(id => [named[id].hp, named[id].brk]), [[260, 13], [290, 14], [170, 15]]);
  assert.deepEqual([named.b_nameless.phase.t, named.b_nameless.phase.hp, named.b_nameless.phase.brk, named.b_nameless.phase.weak],
    ['phase', 170, 15, ['flame', 'frost']]);
  assert.deepEqual(named.b_nameless.phase.pattern.map(m => m.t), ['sealSlot', 'atk', 'delayRes', 'silence', 'charge', 'atk']);
  assert.equal(JSON.stringify(data), original);
});

test('all 23 candidate patterns run two full cycles alive and all 23 candidate encounter groups run', () => {
  // RULES_NOW_PLAY (2026-10-19): five reservation-only seal/delay edges were dropped and the rest replay as now plays.
  assert.deepEqual(fixture.counts, { cases: 76, commands: 437, committed: 415, replayed: 2, rejected: 20 });
  assert.deepEqual(fixture.coverage, { candidates: 23, encounters: 23, edges: 29, protocols: 1 });
  for (const enemy of candidates.enemies) {
    const row = byId(`duel-${enemy.id}`), last = row.steps.at(-1).state;
    assert.equal(row.dataset, 'candidate');
    assert.equal(row.steps.length, 1 + 2 * enemy.pattern.length);
    assert.deepEqual([last.phase, last.turn], ['PLAYER', 1 + 2 * enemy.pattern.length]);
    assert.ok(last.enemies.every(e => e.alive && !Object.hasOwn(e, 'phaseIndex') && !Object.hasOwn(e, 'pattern')), enemy.id);
    const seen = new Set(row.steps.flatMap(s => s.response.ev.map(e => e.e)));
    for (const move of enemy.pattern) if (behaviors.includes(move.t)) assert.ok(seen.has(move.t), `${enemy.id} executed ${move.t}`);
  }
  // The second-body pattern itself runs two complete cycles alive (12 of the 13 ends).
  const second = edge('nameless-phase2-cycles');
  assert.deepEqual([second.steps.length, after(second, 1).enemies[0].phaseIndex, after(second, 13).phase], [14, 1, 'PLAYER']);
  const groups = [];
  for (const [zone, tiers] of Object.entries(candidates.encounters)) for (const [tier, list] of Object.entries(tiers)) {
    list.forEach((enemies, index) => groups.push([`encounter-${zone}-${tier}-${index}`, enemies]));
  }
  assert.equal(groups.length, 23);
  assert.deepEqual(fixture.cases.filter(c => c.group === 'encounter').map(c => c.id), groups.map(([id]) => id));
  for (const [id, enemies] of groups) assert.deepEqual(byId(id).setup.enemies, enemies);
  const seen = new Set(fixture.cases.flatMap(c => c.steps.flatMap(s => (s.response ? s.response.ev : []).map(e => e.e))));
  assert.ok(behaviors.every(b => seen.has(b)), 'every late behavior actually ran');
});

test('every checkpoint is reproduced by fresh creation and the recorded real commands with independent hashes', () => {
  const sets = { candidate: candidates.withLateEnemies(data) };
  sets.boundary = clone(sets.candidate);
  sets.boundary.enemies.push(...clone(boundaryEnemies));
  let commands = 0, committed = 0, replayed = 0, rejected = 0;
  const compare = (battle, point, label) => {
    assert.deepEqual(clone(battle.s), point.state, `${label}: complete state`);
    assert.deepEqual(clone(battle.publicView()), point.publicView, `${label}: public view`);
    for (const [value, key] of [[point.state, 'stateHash'], [point.state.rng, 'rngHash'], [point.state.ev, 'eventsHash'], [point.state.log, 'logHash'], [point.publicView, 'publicViewHash']])
      assert.equal(hashState(value), point[key], `${label}: ${key}`);
    for (const key of ['rng', 'cards', 'zones', 'draw']) assert.ok(!Object.hasOwn(point.publicView, key), `${label}: hidden ${key}`);
  };
  for (const row of fixture.cases) {
    const battle = MF.Battle.create(sets[row.dataset], clone(row.setup));
    compare(battle, row.created, row.id);
    for (const [index, step] of row.steps.entries()) {
      commands++;
      const before = clone(battle.s), receipts = clone(battle.receipts);
      if (step.outcome === 'rejected') {
        rejected++;
        assert.throws(() => battle.command(clone(step.request)), e => e instanceof MF.RuleError && e.code === step.errorCode, `${row.id}:${index}`);
        assert.deepEqual([step.response, step.responseEventsHash], [null, null]);
        assert.deepEqual(clone(battle.s), before);
        assert.deepEqual(clone(battle.receipts), receipts);
      } else {
        const response = battle.command(clone(step.request));
        assert.deepEqual(clone(response), step.response, `${row.id}:${index}`);
        assert.equal(hashState(response.ev), step.responseEventsHash);
        if (step.outcome === 'replayed') { replayed++; assert.deepEqual(clone(battle.s), before); }
        else { committed++; assert.equal(step.outcome, 'committed'); assert.equal(battle.s.v, before.v + 1); }
      }
      compare(battle, step, `${row.id}:${index}`);
    }
  }
  assert.deepEqual({ commands, committed, replayed, rejected }, { commands: 437, committed: 415, replayed: 2, rejected: 20 });
});

test('the six behaviors keep the exact original rules at their boundaries', () => {
  const cost = (row, i) => of(row, i, 'play')[0].cost;
  // apDrain: separate from debt, consumed once, clamped at 0.
  const drain = edge('drain-debt-clamp');
  assert.deepEqual([0, 1, 2, 3, 5].map(i => after(drain, i).ap), [4, 6, 1, 0, 4]);
  assert.equal(drain.steps[4].errorCode, 'AP_LOW');
  assert.deepEqual([of(drain, 2, 'apDrain').map(e => e.n), of(drain, 2, 'repay').map(e => e.n), of(drain, 3, 'apDrain').map(e => e.n)], [[1], [2], [6]]);
  assert.deepEqual([after(drain, 2).apDrainNext, after(drain, 2).debts, after(drain, 5).apDrainNext], [0, [], 0]);
  // silence: non-stacking, expiry, reapplication; RULES_NOW_PLAY makes every play now, so its next/after surcharge never applies,
  // while freeNext and free instances stay 0.
  const free = edge('silence-free-next');
  assert.deepEqual([2, 4, 5, 7, 9].map(i => cost(free, i)), [1, 0, 0, 1, 1]);
  assert.deepEqual([1, 6, 8].map(i => after(free, i).silenceUntil), [2, 0, 4]);
  assert.deepEqual(of(free, 1, 'silence').map(e => e.until), [2, 2]);
  const copy = edge('silence-free-instance');
  assert.deepEqual([1, 3, 4, 5, 6].map(i => cost(copy, i)), [1, 0, 1, 1, 1]);
  assert.deepEqual([after(copy, 2).silenceUntil, after(copy, 7).silenceUntil], [2, 0]);
  const copied = of(copy, 3, 'play')[0].uid;
  assert.ok(after(copy, 3).cards[copied].free && after(copy, 3).cards[copied].echoCopy && after(copy, 7).zones.exhaust.includes(copied));
  // sealSlot: the following player turn's next column; an existing seal gives block 10. With no reservation it seals nothing a now play uses.
  const seals = (row, i) => of(row, i, 'sealSlot').map(({ until, fallback }) => [until, fallback]);
  const sealed = edge('seal-no-reservation');
  assert.deepEqual([seals(sealed, 3), seals(sealed, 7), cost(sealed, 4), after(sealed, 3).res, after(sealed, 3).enemies[0].block], [[[2, false]], [[4, false]], 1, [], 0]);
  const collateral = edge('seal-after-collateral-debt');
  assert.deepEqual([after(collateral, 1).sealedUntil, after(collateral, 1).debts.map(d => [d.n, d.due]), seals(collateral, 2), after(collateral, 2).ap, after(collateral, 2).enemies[0].block],
    [0, [[1, 2]], [[2, false]], 3, 0]);
  assert.deepEqual([seals(edge('seal-two-sealers'), 1), after(edge('seal-two-sealers'), 1).enemies.map(e => e.block)], [[[2, false], [2, true]], [0, 10]]);
  // delayRes with no reservation: a null event and no state change; the now-played card already resolved at its authored value.
  const delay = edge('delay-no-reservation');
  assert.deepEqual([of(delay, 3, 'delayRes'), after(delay, 3).res], [[{ e: 'delayRes', uid: null, discarded: false }], []]);
  assert.deepEqual(of(delay, 2, 'dmg').map(e => e.n), [data.cards.find(c => c.id === 'sera_timed').eff[0].v]);
  // eatName: tokens first, explicit null uid and no RNG on an empty discard (an all-보존 hand discards nothing), normal exhaust hooks only.
  const empty = edge('eat-empty-discard');
  assert.deepEqual([of(empty, 1, 'eatName'), after(empty, 1).rng, after(empty, 1).zones.hand.length], [[{ e: 'eatName', uid: null }], after(empty, 0).rng, 2]);
  const tokens = edge('eat-token-priority');
  const eaten = tokens.steps.flatMap(s => s.response.ev.filter(e => e.e === 'eatName').map(e => s.state.cards[e.uid]));
  assert.ok(eaten.length === 2 && eaten.every(c => c.token && c.id === 'penalty_note'));
  const hooks = ev(edge('eat-exhaust-hooks'), 2);
  assert.deepEqual(hooks.slice(0, 4).map(e => [e.e, e.src]), [['exhaust', undefined], ['block', 'oblivion'], ['dmg', 'relic'], ['eatName', undefined]]);
  assert.equal(after(edge('eat-exhaust-hooks'), 1).ap, 4);
  // phase: the first HP 0 is no kill; only the final body's death runs kill hooks.
  const kill = edge('nameless-kill-hooks'), body = after(kill, 2).enemies[0];
  assert.deepEqual([body.alive, body.phaseIndex, body.hp, body.maxHp, body.brk, body.st.mark, body.weak, body.pattern.length], [true, 1, 2, 2, 15, 0, ['flame', 'frost'], 6]);
  assert.deepEqual(ev(kill, 2).filter(e => e.e === 'phase' || e.e === 'kill'), [{ e: 'phase', uid: body.uid, index: 1, hp: 2, brk: 15 }]);
  const won = after(kill, 4);
  assert.deepEqual([won.phase, won.killedMarked, won.runFlags.coinsBonus, won.turnFlags.deduction, of(kill, 4, 'kill').length], ['VICTORY', 1, 10, true, 1]);
  assert.deepEqual(ev(edge('nameless-multihit'), 1).map(e => e.e).filter(e => ['dmg', 'phase', 'kill', 'end'].includes(e)), ['dmg', 'phase', 'dmg', 'kill', 'end']);
  const cycles = edge('nameless-phase2-cycles');
  assert.deepEqual([after(cycles, 1).enemies[0].hp, after(cycles, 1).enemies[0].st.burn, after(cycles, 13).turn, after(cycles, 13).hp], [2, 0, 14, 252]);
  const counter = after(edge('phase-counter-multihit'), 2);
  assert.deepEqual([of(edge('phase-counter-multihit'), 2, 'hit').length, counter.hp, counter.enemies[0].phaseIndex, counter.enemies[0].hp], [1, 79, 1, 100]);
  const reflect = after(edge('phase-reflect-defeat'), 2);
  assert.deepEqual([reflect.phase, reflect.hp, reflect.enemies[0].alive, reflect.enemies[0].phaseIndex, reflect.enemies[0].hp], ['DEFEAT', 0, true, 1, 30]);
  for (const [name, code] of invalidPhases) {
    const row = edge(`phase-invalid-${name.replace(/_/g, '-')}`);
    assert.equal(row.steps[1].errorCode, code, name);
    assert.ok(!Object.hasOwn(after(row, 2).enemies[0], 'phaseIndex') && after(row, 2).enemies[0].hp === 1, name);
  }
  for (const [name] of invalidMoves) assert.equal(edge(`move-invalid-${name}`).steps[1].errorCode, 'INVALID_ENEMY_MOVE');
  const terminal = edge('late-terminal-defeat');
  assert.deepEqual([after(terminal, 1).phase, after(terminal, 1).apDrainNext, after(terminal, 1).silenceUntil, after(terminal, 1).sealedUntil, after(terminal, 1).exhausted],
    ['DEFEAT', 2, 2, 2, 1]);
  assert.deepEqual([terminal.steps[1].publicView.apDrainNext, terminal.steps[1].publicView.silenceUntil, terminal.steps[2].errorCode], [2, 2, 'WRONG_PHASE']);
  assert.deepEqual(byId('protocol-late-receipts').steps.map(s => s.outcome), ['committed', 'committed', 'replayed', 'rejected', 'rejected', 'committed', 'replayed']);
});

test('fixture freshness rejects stale pins, overlay or test-local edits, missing/duplicate cases and diagnostic tampering', () => {
  for (const source of Object.values(fixture.sources)) assert.equal(source.sha256, sha256(readFileSync(new URL(`../../${source.path}`, import.meta.url))));
  assert.deepEqual(checkFixture(fixture), fixture.counts);
  const first = fixture.cases[0], step = first.steps[0];
  const retuned = clone(fixture.overlay);
  retuned.enemies.find(e => e.id === 'b_nameless').phase.hp = 171;
  const local = clone(fixture.boundaryEnemies);
  local[0].pattern[1].n = 5;
  for (const bad of [
    { ...fixture, sources: { ...fixture.sources, proposal: { ...fixture.sources.proposal, sha256: '0'.repeat(64) } } },
    { ...fixture, overlay: retuned },
    { ...fixture, boundaryEnemies: local },
    { ...fixture, cases: fixture.cases.slice(1) },
    { ...fixture, cases: [...fixture.cases, first] },
    { ...fixture, cases: [{ ...first, steps: [{ ...step, state: { ...step.state, log: [] } }, ...first.steps.slice(1)] }, ...fixture.cases.slice(1)] },
    { ...fixture, cases: [{ ...first, steps: [{ ...step, publicViewHash: '0'.repeat(64) }, ...first.steps.slice(1)] }, ...fixture.cases.slice(1)] },
  ]) assert.throws(() => checkFixture(bad), /source|overlay|test-local|case|count|coverage|fixture|differ/i);
});

test('exports pass only the existing strict single-member decoder, are byte deterministic and preserve the old fixture on failure', t => {
  const dir = mkdtempSync(join(tmpdir(), 'mf-late-oracle-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  const path = join(dir, 'late.json.gz');
  const summary = exportVectors(path), bytes = readFileSync(path);
  assert.deepEqual([summary.cases, summary.commands, summary.sha256], [76, 437, sha256(bytes)]);
  assert.deepEqual(decodeVectors(bytes), fixture);
  const empty = gzipSync(Buffer.alloc(0), { level: 9 }), crc = Buffer.from(bytes);
  crc[crc.length - 8] ^= 1;
  for (const bad of [Buffer.concat([empty, bytes]), Buffer.concat([bytes, empty]), bytes.subarray(0, bytes.length - 1), crc,
    gzipSync(Buffer.from('{"schema":1,"schema":2}'), { level: 9 }), Buffer.from([0xef, 0xbb, 0xbf, 0x7b, 0x7d]), Buffer.from([0x7b, 0xff, 0x7d])])
    assert.throws(() => decodeVectors(bad));
  exportVectors(path);
  assert.deepEqual(readFileSync(path), bytes);
  writeFileSync(path, 'previous artifact');
  for (const error of [new Error('unexpected late failure'), new MF.RuleError('INVALID_ENEMY_MOVE')]) {
    // Fault injection tests exporter failure handling, never a positive oracle trajectory.
    const broken = t.mock.method(MF.Battle.prototype, 'command', () => { throw error; });
    assert.throws(() => exportVectors(path), caught => caught === error);
    assert.equal(readFileSync(path, 'utf8'), 'previous artifact');
    assert.deepEqual(readdirSync(dir), ['late.json.gz']);
    broken.mock.restore();
  }
});

test('the checked-in late-action fixture is a lossless fresh command oracle through the strict decoder', () => {
  assert.deepEqual(checkFixture(decodeVectors(readFileSync(fixturePath))), fixture.counts);
});
