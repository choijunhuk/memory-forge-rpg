import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { gunzipSync } from 'node:zlib';
import { canonicalJSON, stateHash, scenarioPlan, generateScenario, replayScenario, exportTraces } from '../../tools/gen_traces.mjs';

const require = createRequire(import.meta.url);
const MF = require('../rules-core.js');
const data = JSON.parse(readFileSync(new URL('../../data/game_data.json', import.meta.url), 'utf8'));
const clone = value => JSON.parse(JSON.stringify(value));
function hashJSON(value) {
  const keys = new Set();
  const collect = item => {
    if (!item || typeof item !== 'object') return;
    for (const key of Object.keys(item)) {
      if (key === 'log' || key === 'ev') continue;
      if (!Array.isArray(item)) keys.add(key);
      collect(item[key]);
    }
  };
  collect(value);
  return createHash('sha256').update(JSON.stringify(value, [...keys].sort())).digest('hex');
}

test('trace canonical JSON sorts even numeric keys, excludes diagnostics, and never changes RNG or array order', () => {
  const state = { text: '기억', 2: 'two', 10: 'ten', nested: { z: [3, 2], log: ['ignored'], a: 1 }, ev: [7] };
  const before = clone(state);
  const expected = '{"10":"ten","2":"two","nested":{"a":1,"z":[3,2]},"text":"기억"}';
  assert.equal(canonicalJSON(state), expected);
  assert.equal(stateHash(state), createHash('sha256').update(expected).digest('hex'));
  assert.deepEqual(state, before);
  assert.notEqual(stateHash([3, 2]), stateHash([2, 3]));
  assert.equal(canonicalJSON({ omitted: undefined, list: [undefined, null] }), '{"list":[null,null]}');
  assert.equal(canonicalJSON(new Array(2)), '[null,null]');
  assert.throws(() => stateHash({ fractional: 0.5 }), /integer/i);
});

test('independent hash preserves pinned ordinal numeric keys through nested objects and arrays without mutation', () => {
  const state = { 2: 'two', 10: 'ten', nested: {
    2: { 2: 'inner-two', 10: 'inner-ten' },
    10: [{ 2: 'array-two', 10: 'array-ten' }, [2, 10]], log: ['ignored'],
  }, rng: MF.Rng.seed(7), ev: ['ignored'] };
  const before = structuredClone(state);
  const expected = '{"10":"ten","2":"two","nested":{"10":[{"10":"array-ten","2":"array-two"},[2,10]],"2":{"10":"inner-ten","2":"inner-two"}},"rng":[3838393609,4210744933,801465066,1648156487]}';
  const expectedHash = createHash('sha256').update(expected).digest('hex');
  assert.equal(hashJSON(state), expectedHash);
  assert.equal(canonicalJSON(state), expected);
  assert.equal(stateHash(state), expectedHash);
  assert.deepEqual(state, before);
});

test('default plan contains all 1000 distinct seeds with 700 short and 300 full scenarios', () => {
  const plan = [...scenarioPlan()];
  assert.equal(plan.length, 1000);
  assert.equal(plan.filter(s => s.length === 'short').length, 700);
  assert.equal(plan.filter(s => s.length === 'full').length, 300);
  assert.equal(new Set(plan.map(s => s.seed)).size, 1000);
  assert.throws(() => [...scenarioPlan({ shortCount: -1 })], /count/i);
  assert.throws(() => [...scenarioPlan({ shortCount: 0, fullCount: 0 })], /count/i);
  assert.throws(() => [...scenarioPlan({ seed: 4294967295 })], /seed/i);
  assert.throws(() => generateScenario({ seed: 1000, length: 'short', memoryPolicy: 'unknown' }), /policy/i);
});

test('real short/full Run commands replay every committed and rejected state hash deterministically', () => {
  const options = [...scenarioPlan({ shortCount: 1, fullCount: 1 })];
  const traces = options.map(generateScenario);
  assert.deepEqual(generateScenario(options[0]), traces[0]);
  assert.notDeepEqual(traces[0].party, traces[1].party);
  for (const trace of traces) {
    const run = MF.Run.create(data, clone(trace.setup));
    const context = { run: run.s, memory: null };
    assert.equal(hashJSON(context), trace.initialStateHash);
    let previous = trace.initialStateHash;
    let rejected = 0;
    for (const step of trace.commands) {
      const command = clone(step.command);
      let code = null;
      try {
        if (command.scope === 'battle') {
          assert.equal(command.rid, `r${run.battle.s.v}-${command.kind}`);
          assert.equal(command.v, run.battle.s.v);
          run.battleCmd(command.kind, command.args);
        } else run[command.kind](...command.args);
      } catch (error) { code = error.code; }
      context.run = run.s;
      assert.equal(hashJSON(context), step.stateHash);
      assert.equal(code, step.errorCode);
      assert.equal(step.outcome, code ? 'rejected' : 'committed');
      if (code) { rejected++; assert.equal(step.stateHash, previous); }
      previous = step.stateHash;
    }
    assert.ok(rejected > 0, 'random play must exercise legal rejections');
    assert.equal(run.s.phase, 'DONE');
    assert.equal(trace.finalStateHash, previous);
    assert.deepEqual(replayScenario(trace).finalState, trace.finalState);
    const observed = structuredClone(run.s);
    stateHash(context);
    assert.deepEqual(run.s, observed, 'hashing must not consume RNG or change state');
  }
});

test('seed 1135 skips a real reserve-only card disabled by c_quick and completes the run', t => {
  const emptyModes = [];
  const allowedModes = MF.Battle.prototype.allowedModes;
  t.mock.method(MF.Battle.prototype, 'allowedModes', function(uid) {
    const modes = allowedModes.call(this, uid);
    if (!modes.length) emptyModes.push({ id: this.s.cards[uid].id, circuit: this.s.cards[uid].mods.c });
    return modes;
  });
  const trace = generateScenario({ seed: 1135, length: 'short' });
  assert.ok(emptyModes.some(card => card.id === 'com_overwatch' && card.circuit === 'c_quick'));
  assert.equal(trace.finalState.run.phase, 'DONE');
  assert.deepEqual(replayScenario(trace).finalState, trace.finalState);
});

test('replay refuses tampered command envelopes, hashes, setup, snapshots, or source provenance', () => {
  const original = generateScenario({ seed: 1000, length: 'short' });
  const tamperedCommand = clone(original);
  tamperedCommand.commands.find(step => step.command.scope === 'battle').command.v++;
  assert.throws(() => replayScenario(tamperedCommand), /command|version/i);
  const tamperedHash = clone(original);
  tamperedHash.commands[0].stateHash = '0'.repeat(64);
  assert.throws(() => replayScenario(tamperedHash), /hash/i);
  const tamperedSetup = clone(original);
  tamperedSetup.setup.seed++;
  assert.throws(() => replayScenario(tamperedSetup), /setup|initial|seed/i);
  const tamperedSnapshot = clone(original);
  tamperedSnapshot.finalState.run.coins++;
  assert.throws(() => replayScenario(tamperedSnapshot), /final|snapshot|hash/i);
  const tamperedSource = clone(original);
  tamperedSource.sources.data.sha256 = '0'.repeat(64);
  assert.throws(() => replayScenario(tamperedSource), /source|data/i);
});

test('opt-in v2 runs use the existing migration and real terminal memory commit without forced state edits', () => {
  const trace = generateScenario({ seed: 1000, length: 'short', memoryPolicy: 'v2' });
  assert.equal(trace.memoryPolicy, 'v2');
  assert.equal(trace.setup.memoryPolicy, 'v2');
  assert.deepEqual(trace.initialState.memory, MF.MemoryV2.migrate(data, {}, {}));
  assert.equal(trace.initialState.run.phase, 'MAP');
  const commit = trace.commands.find(step => step.command.kind === 'commitMemory');
  assert.ok(commit);
  assert.equal(commit.command.args[0].runId, trace.setup.runId);
  assert.equal(commit.outcome, 'committed');
  assert.equal(trace.finalState.run.phase, 'DONE');
  assert.equal(trace.finalState.memory.version, 1);
  assert.equal(trace.finalState.memory.history.length, 1);
  assert.deepEqual(replayScenario(trace).finalState, trace.finalState);
});

test('unexpected exceptions abort generation and preserve the previous output instead of becoming rejections', async t => {
  const dir = mkdtempSync(join(tmpdir(), 'mf-trace-failure-'));
  const output = join(dir, 'traces.json');
  writeFileSync(output, 'previous complete artifact');
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  const broken = t.mock.method(MF.Run.prototype, 'battleCmd', () => { throw new Error('unexpected engine fault'); });
  assert.throws(() => generateScenario({ seed: 1000, length: 'short' }), /unexpected engine fault/);
  await assert.rejects(exportTraces({ output, shortCount: 1, fullCount: 0 }), /unexpected engine fault/);
  assert.equal(readFileSync(output, 'utf8'), 'previous complete artifact');
  assert.deepEqual(readdirSync(dir), ['traces.json']);
  broken.mock.restore();
  const unknownRule = t.mock.method(MF.Run.prototype, 'battleCmd', () => { throw new MF.RuleError('UNEXPECTED_RULE_FAILURE'); });
  assert.throws(() => generateScenario({ seed: 1000, length: 'short' }), { code: 'UNEXPECTED_RULE_FAILURE' });
  unknownRule.mock.restore();
  const impostor = t.mock.method(MF.Run.prototype, 'battleCmd', () => { throw Object.assign(new Error('not a legal RuleError'), { code: 'AP_LOW' }); });
  assert.throws(() => generateScenario({ seed: 1000, length: 'short' }), /not a legal RuleError/);
  impostor.mock.restore();
});

test('portable JSON and gzip JSONL exports retain provenance and completed replayable scenarios', async t => {
  const dir = mkdtempSync(join(tmpdir(), 'mf-trace-export-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  const options = { shortCount: 1, fullCount: 0, seed: 1000 };
  const output = join(dir, 'traces.json');
  const summary = await exportTraces({ ...options, output });
  const json = JSON.parse(readFileSync(output, 'utf8'));
  assert.equal(json.schema, 'mf-run-traces-v1');
  assert.equal(summary.scenarios, 1);
  assert.equal(json.scenarios.length, 1);
  assert.equal(json.sources.data.sha256, createHash('sha256').update(readFileSync(new URL('../../data/game_data.json', import.meta.url))).digest('hex'));
  const compressed = join(dir, 'traces.jsonl.gz');
  await exportTraces({ ...options, output: compressed });
  const [manifest, scenario] = gunzipSync(readFileSync(compressed)).toString('utf8').trim().split('\n').map(JSON.parse);
  assert.equal(manifest.type, 'manifest');
  assert.equal(scenario.type, 'scenario');
  assert.deepEqual(scenario.trace, json.scenarios[0]);
  assert.deepEqual(replayScenario(scenario.trace).finalState, json.scenarios[0].finalState);
});
