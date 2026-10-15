import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { gzipSync } from 'node:zlib';
import { generateScenario, stateHash } from '../../tools/gen_traces.mjs';
import { decodeVectors } from '../../tools/gen_run_create_vectors.mjs';
import { generateCases, checkFixture, exportVectors, fixturePath } from '../../tools/gen_run_progression_vectors.mjs';

const require = createRequire(import.meta.url);
const MF = require('../rules-core.js');
const data = JSON.parse(readFileSync(new URL('../../data/game_data.json', import.meta.url), 'utf8'));
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = value => createHash('sha256').update(value).digest('hex');
const fixture = generateCases();
const row = id => {
  const found = fixture.cases.find(c => c.id === id);
  assert.ok(found, `Missing run progression case ${id}`);
  return found;
};
const battleNodes = new Set(['battle', 'elite', 'boss']);
const nodeOf = (run, id) => [...run.map.floors.flat(), run.map.boss].find(node => node.id === id);
// Full states around every step, following the fixture's own references (a rejection keeps the current point).
function* walk(c) {
  let state = c.created.state;
  for (const step of c.steps) {
    const before = state;
    if (step.outcome === 'committed') state = step.after.state;
    yield { step, before, after: state };
  }
}
const final = c => [...walk(c)].at(-1).after;
const json = value => value === undefined ? undefined : JSON.stringify(value);
const recorded = result => result.kind === 'undefined' ? undefined : JSON.stringify(result.value);

test('five fixed gen_traces inputs replay from a fresh Create through actual commands to DONE with complete states, returns and pure queries', () => {
  assert.deepEqual(fixture.cases.map(c => [c.id, c.seed, c.length, c.memoryPolicy]), [
    ['seed1000-short-legacy', 1000, 'short', 'legacy'], ['seed1000-full-legacy', 1000, 'full', 'legacy'],
    ['seed1102-full-legacy', 1102, 'full', 'legacy'], ['seed1102-full-v2', 1102, 'full', 'v2'], ['seed2654-full-legacy', 2654, 'full', 'legacy'],
  ]);
  assert.deepEqual(fixture.denominators, { cases: 5, seeds: [1000, 1102, 2654], policies: { legacy: 4, v2: 1 }, lengths: { short: 1, full: 4 } });
  assert.deepEqual(Object.values(fixture.sources).map(source => source.path), ['data/game_data.json', 'engine/rules-core.js', 'proposals/memory_v2.cjs',
    'tools/gen_traces.mjs', 'engine/tests/fuzz_runs.mjs', 'tools/gen_run_create_vectors.mjs', 'tools/gen_run_progression_vectors.mjs']);
  for (const source of Object.values(fixture.sources)) assert.equal(source.sha256, sha256(readFileSync(new URL(`../../${source.path}`, import.meta.url))), source.path);
  assert.equal(fixture.status, 'JS_ORACLE_ONLY');
  assert.equal(fixture.coverage.totals.csharpCommandReplay, 0);
  for (const c of fixture.cases) {
    const trace = generateScenario({ seed: c.seed, length: c.length, memoryPolicy: c.memoryPolicy });
    assert.deepEqual(c.trace.setup, trace.setup);
    assert.equal(c.trace.policy.seed, (c.seed ^ 0xc0decafe) >>> 0);
    assert.deepEqual(c.steps.map(s => [s.command, s.outcome, s.errorCode, s.after.stateHash]), trace.commands.map(s => [s.command, s.outcome, s.errorCode, s.stateHash]), c.id);
    assert.equal(stateHash(c.created.state), trace.initialStateHash);
    assert.equal(stateHash(final(c)), trace.finalStateHash);
    // Independent consumer-style replay: real Create and public commands only; every state, return and query compares as ordered JSON.
    const run = MF.Run.create(data, clone(c.trace.setup));
    let memory = c.memoryPolicy === 'v2' ? MF.MemoryV2.migrate(data, {}, {}) : null;
    const live = () => JSON.stringify({ run: run.s, memory });
    const ask = (queries, where) => {
      for (const q of queries) {
        const before = live();
        const value = q.method === 'available' ? run.available() : q.method === 'nodeById' ? run.nodeById(...q.args) : run.battle.publicView();
        assert.equal(json(value), recorded(q.result), `${where} ${q.method}`);
        assert.equal(live(), before, `${where}: ${q.method} changed state, RNG or log`);
      }
    };
    assert.equal(live(), JSON.stringify(c.created.state));
    ask(c.created.queries, `${c.id} created`);
    for (const { step, after } of walk(c)) {
      const { command } = step, where = `${c.id}#${step.index}`;
      let value, code = null;
      try { value = command.scope === 'battle' ? run.battleCmd(command.kind, clone(command.args)) : run[command.kind](...clone(command.args)); }
      catch (error) { assert.ok(error instanceof MF.RuleError, `${where}: ${error.message}`); code = error.code; }
      assert.equal(code, step.errorCode, where);
      if (code === null) {
        assert.equal(json(value), recorded(step.result), `${where}: return value`);
        if (command.kind === 'commitMemory' && memory) memory = value.state;
      }
      assert.equal(live(), JSON.stringify(after), `${where}: complete state, log and RNG`);
      ask(step.queries, where);
    }
    assert.equal(run.s.phase, 'DONE');
  }
  for (const c of fixture.cases) {
    const created = c.created.state, done = final(c);
    assert.deepEqual([created.run.phase, created.run.pos, created.run.battle, created.run.log], ['MAP', null, null, []], c.id);
    assert.deepEqual([done.run.phase, done.run.pending], ['DONE', { type: 'done' }], c.id);
    assert.equal(c.reload.setup.seed, c.seed + 99);
    // Own-undefined members (absent in JSON, never null) come only from legacy saved specs and the live battle's
    // latest events (e.g. resolved.fizzled); Run fields, the v2 store and fresh/reload Create stay fully defined.
    for (const p of [c.created, ...c.steps.filter(s => s.outcome === 'committed').map(s => s.after), c.reload.created])
      for (const path of p.undefinedPaths) assert.ok(['savedMemory', 'battle'].includes(path[1]) && path[0] === 'run', `${c.id}: ${path.join('.')}`);
    assert.deepEqual([c.created.undefinedPaths, c.reload.created.undefinedPaths], [[], []], c.id);
    if (c.memoryPolicy === 'legacy') {
      assert.deepEqual([Object.hasOwn(created.run, 'memoryPolicy'), created.memory, done.memory], [false, null, null], c.id);
      continue;
    }
    const runId = c.trace.setup.runId;
    assert.deepEqual(created.memory, MF.MemoryV2.migrate(data, {}, {}));
    assert.deepEqual([done.memory.version, done.memory.history.length, Object.keys(done.memory.receipts)], [1, 1, [`${runId}-save`]]);
    assert.deepEqual(done.run.savedMemory, done.memory.history[0].memory);
    assert.deepEqual([c.reload.setup.runId, c.reload.created.state.run.memoryVersion], [`${runId}-reload`, 1]);
  }
});

test('a newly earned AP bonus reaches only the next battle first turn, and begin-defeat closes through the normal finalizer', () => {
  const { apPerTurn, apMax } = data.rules, rift = data.events.find(e => e.id === 'ev_rift');
  const observed = { beginEndedBattles: [], apBonusEarned: [], apBonusConsumed: [] }, rerun = 'seed2654-full-legacy', rerunStarts = [];
  for (const c of fixture.cases) for (const { step, before, after } of walk(c)) {
    if (step.outcome !== 'committed') continue;
    const { kind, args } = step.command, b = before.run, a = after.run, where = `${c.id}#${step.index}`;
    const node = kind === 'enter' ? nodeOf(b, args[0]) : null, battleStart = !!node && battleNodes.has(node.type);
    if (a.runFlags.nextApBonus > b.runFlags.nextApBonus) {
      // §7.7: only the authored ev_rift first choice earns "+2 AP on the next battle's first turn".
      assert.deepEqual([kind, b.pending.id, args[0], a.runFlags.nextApBonus - b.runFlags.nextApBonus], ['eventChoose', 'ev_rift', 0, rift.choices[0].fx.nextApBonus], where);
      observed.apBonusEarned.push({ caseId: c.id, step: step.index, eventId: 'ev_rift', choice: 0, nextApBonus: a.runFlags.nextApBonus });
    } else if (a.runFlags.nextApBonus !== b.runFlags.nextApBonus) assert.ok(battleStart, `${where}: AP bonus changed outside earning or battle start (refund)`);
    if (!battleStart) continue;
    assert.equal(a.runFlags.nextApBonus, 0, `${where}: battle start must consume the Run-side bonus`);
    if (a.battle) {
      const relic = id => b.relics.includes(id) ? 1 : 0;
      assert.deepEqual([a.battle.turn, a.battle.phase, a.battle.runFlags.nextApBonus], [1, 'PLAYER', 0], where);
      assert.equal(a.battle.ap, Math.max(0, Math.min(apMax, apPerTurn + b.runFlags.nextApBonus + relic('chrono_crown') - relic('cracked_hourglass'))), `${where}: first-turn AP`);
      if (c.id === rerun) rerunStarts.push({ step: step.index, carried: b.runFlags.nextApBonus, ap: a.battle.ap, relics: relic('chrono_crown') + relic('cracked_hourglass') });
    }
    if (b.runFlags.nextApBonus) observed.apBonusConsumed.push({ caseId: c.id, step: step.index, carried: b.runFlags.nextApBonus, firstTurnAp: a.battle ? a.battle.ap : null });
    if (a.phase !== 'BATTLE') observed.beginEndedBattles.push({ caseId: c.id, step: step.index, nodeId: node.id, type: node.type, phase: a.phase });
  }
  // Seed 2654 earns the bonus twice in the same Run, each from zero. Each gain is spent on the very next battle's
  // first turn (base 4 + 2 = 6), and the eight real battles between them start at the base 4 (no refund or accumulation).
  assert.deepEqual([apPerTurn, rift.choices[0].fx.nextApBonus], [4, 2], 'original §5 base AP and §7.7 ev_rift bonus');
  const mine = list => list.filter(e => e.caseId === rerun);
  const earned = mine(observed.apBonusEarned), consumed = mine(observed.apBonusConsumed);
  assert.deepEqual(earned.map(e => e.nextApBonus), [2, 2], `${rerun}: two fresh same-Run gains`);
  assert.deepEqual(consumed.map(e => [e.carried, e.firstTurnAp]), [[2, 6], [2, 6]], `${rerun}: each gain spent once on a real first turn`);
  assert.ok(earned[0].step < consumed[0].step && consumed[0].step < earned[1].step && earned[1].step < consumed[1].step, `${rerun}: gain/spend order`);
  assert.ok(rerunStarts.every(s => s.relics === 0), `${rerun}: no AP relic on observed starts`);
  const between = rerunStarts.filter(s => s.step > earned[0].step && s.step < earned[1].step);
  assert.deepEqual(between.map(s => [s.carried, s.ap]), [[2, 6], ...Array(8).fill([0, 4])], `${rerun}: first gain spent, then eight ordinary battles at base AP`);
  assert.deepEqual(rerunStarts.find(s => s.step > earned[1].step), { step: consumed[1].step, carried: 2, ap: 6, relics: 0 }, `${rerun}: second gain spent by the next battle`);
  assert.deepEqual(observed, fixture.coverage.events);

  for (const id of ['seed1102-full-legacy', 'seed1102-full-v2']) {
    const c = row(id), hits = observed.beginEndedBattles.filter(e => e.caseId === id);
    assert.equal(hits.length, 1, `${id}: exactly one battle ends during begin`);
    const index = hits[0].step, { step, before, after } = [...walk(c)][index], node = nodeOf(before.run, step.command.args[0]);
    assert.deepEqual([before.run.phase, after.run.phase, after.run.battle, after.run.hp], ['MAP', 'DEFEAT', null, 0], id);
    assert.deepEqual(after.run.result, { outcome: 'defeat', floor: node.f });
    assert.deepEqual(after.run.pending, { type: 'save', defeat: true });
    assert.deepEqual(after.run.log.slice(-2), [`${MF.NODE_NAME[node.type]} 진입`, '패배']);
    assert.deepEqual(after.run.stats, before.run.stats, `${id}: a defeat is not counted as a won battle`);
    assert.equal(Object.hasOwn(after.run, 'memoryAfter'), c.memoryPolicy === 'v2');
    assert.deepEqual(step.result, { kind: 'undefined' });
    assert.deepEqual(step.queries.find(q => q.method === 'available').result.value, []);
    const rest = c.steps.slice(index + 1), selection = rest[0].command.args[0];
    assert.deepEqual(rest.map(s => [s.command.kind, s.outcome]), [['commitMemory', 'committed']], `${id}: only the defeat save follows`);
    assert.ok((c.memoryPolicy === 'v2' ? selection.selectedChangeIds : Object.values(selection).flat()).length <= 1, `${id}: defeat keeps at most one change`);
  }
});

test('strict checks reject stale provenance, case identity, omission, forged outcomes, hashes, returns, coverage and query data', () => {
  assert.deepEqual(checkFixture(fixture), fixture.coverage.totals);
  // Shallow copies keep the multi-megabyte fixture shared; only the mutated case is cloned.
  const tamper = (index, mutate) => {
    const copy = { ...fixture, sources: clone(fixture.sources), coverage: clone(fixture.coverage), cases: [...fixture.cases] };
    if (index !== null) copy.cases[index] = clone(copy.cases[index]);
    mutate(copy); return copy;
  };
  const committed = c => c.steps.find(s => s.outcome === 'committed' && s.command.scope === 'run' && s.index > 0);
  const rejected = c => c.steps.find(s => s.outcome === 'rejected');
  for (const [index, mutate] of [
    [null, copy => { copy.sources.engine.sha256 = '0'.repeat(64); }],
    [null, copy => { copy.status = 'CSHARP_REPLAY_COMPLETE'; }],
    [null, copy => { copy.extra = true; }],
    [null, copy => { copy.cases.pop(); }],
    [null, copy => { copy.cases.reverse(); }],
    [0, copy => { copy.cases[0].steps.splice(3, 1); }],
    [0, copy => { committed(copy.cases[0]).outcome = 'rejected'; }],
    [0, copy => { const step = rejected(copy.cases[0]); step.errorCode = step.error.code = 'OTHER_RULE'; }],
    [0, copy => { committed(copy.cases[0]).after.stateHash = '0'.repeat(64); }],
    [0, copy => { committed(copy.cases[0]).after.state.run.coins += 1; }],
    [0, copy => { copy.cases[0].steps.find(s => s.result?.kind === 'json').result.value = null; }],
    [0, copy => { copy.cases[0].trace.finalStateHash = '0'.repeat(64); }],
    [null, copy => { copy.coverage.totals.committed += 1; }],
  ]) assert.throws(() => checkFixture(tamper(index, mutate)), /run progression|step|hash/i);
  // An internally consistent forgery is exposed only by fresh actual execution.
  const forged = tamper(0, copy => {
    const result = copy.cases[0].created.queries[0].result;
    result.value.reverse(); result.jsonHash = sha256(JSON.stringify(result.value));
  });
  assert.throws(() => checkFixture(forged), /fresh actual execution/);
});

test('gzip export is deterministic, rejects damaged containers, accepts other valid DEFLATE encodings and preserves output on unexpected execution', t => {
  const directory = mkdtempSync(join(tmpdir(), 'mf-run-progression-'));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  const output = join(directory, 'vectors.json.gz');
  const summary = exportVectors(output), bytes = readFileSync(output), text = Buffer.from(`${JSON.stringify(fixture)}\n`);
  assert.deepEqual([summary.cases, summary.bytes, summary.sha256], [5, bytes.length, sha256(bytes)]);
  assert.ok(gzipSync(text, { level: 9 }).equals(bytes), 'fresh export must be byte identical to this process generation');
  assert.deepEqual(readdirSync(directory), ['vectors.json.gz']);
  const alternate = gzipSync(text, { level: 9, memLevel: 1, windowBits: 10 });
  alternate[9] = 0x0b;
  assert.ok(!alternate.subarray(10).equals(bytes.subarray(10)), 'alternate DEFLATE stream must differ');
  assert.equal(JSON.stringify(decodeVectors(alternate)), JSON.stringify(fixture));
  const crc = Buffer.from(bytes); crc[crc.length - 8] ^= 1;
  for (const bad of [bytes.subarray(0, bytes.length - 1), crc, Buffer.concat([bytes, gzipSync(Buffer.from('{}'), { level: 9 })]),
    gzipSync(Buffer.from('﻿{}'), { level: 9 }), gzipSync(Buffer.from('{"schema":"a","schema":"b"}'), { level: 9 })]) assert.throws(() => decodeVectors(bad));
  writeFileSync(output, 'previous artifact');
  const { available, nodeById } = MF.Run.prototype;
  for (const [method, replacement, expected] of [
    ['enter', () => { throw new Error('unexpected run fault'); }, /unexpected run fault/],
    ['proceed', () => { throw new MF.RuleError('COINS_LOW'); }, { code: 'COINS_LOW' }],
    // Only the oracle queries available() outside MAP, so a failing query cannot be skipped or recorded.
    ['available', function () { if (this.s.phase !== 'MAP') throw new Error('query fault'); return available.call(this); }, /query fault/],
    // Log-only mutation escapes the canonical trace hash but not the complete-JSON purity check.
    ['nodeById', function (id) { this.s.log.push('impure query'); return nodeById.call(this, id); }, /changed complete state/],
  ]) {
    const mock = t.mock.method(MF.Run.prototype, method, replacement);
    assert.throws(() => exportVectors(output), expected);
    assert.equal(readFileSync(output, 'utf8'), 'previous artifact');
    assert.deepEqual(readdirSync(directory), ['vectors.json.gz']);
    mock.mock.restore();
  }
});

test('checked-in artifact matches fresh actual JS execution and claims only the JS oracle scope', () => {
  const saved = decodeVectors(readFileSync(fixturePath));
  assert.deepEqual(checkFixture(saved), fixture.coverage.totals);
  assert.equal(saved.status, 'JS_ORACLE_ONLY');
  assert.match(saved.scope, /C# Run command replay 0/);
});
