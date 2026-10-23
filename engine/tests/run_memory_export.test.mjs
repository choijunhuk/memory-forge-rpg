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
import { generateCases, checkFixture, exportVectors, fixturePath } from '../../tools/gen_run_memory_vectors.mjs';

const require = createRequire(import.meta.url);
const MF = require('../rules-core.js');
const data = JSON.parse(readFileSync(new URL('../../data/game_data.json', import.meta.url), 'utf8'));
const ix = MF.indexData(data);
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = value => createHash('sha256').update(value).digest('hex');
const fixture = generateCases();
const find = (list, key, value, what) => {
  const found = list.find(x => x[key] === value);
  assert.ok(found, `Missing ${what} ${value}`);
  return found;
};
const base = id => find(fixture.bases, 'id', id, 'Run memory base');
const scenario = id => find(fixture.scenarios, 'id', id, 'Run memory scenario');
const op = (id, label) => find(scenario(id).ops, 'label', label, `${id} op`);
const covered = (id, label) => { const found = op(id, label); return found.status === 'COVERED' ? found : null; };
const outcome = o => o.error ? `${o.error.kind}:${o.error.code ?? o.error.name}` : 'returned';
const pointOf = (sc, r) => r.ref === -1 ? base(sc.base)[sc.at].state : sc.points[r.ref].state;
const stateAfter = (id, label) => pointOf(scenario(id), op(id, label).after);
const json = value => value === undefined ? undefined : JSON.stringify(value);
const recorded = result => result.kind === 'undefined' ? undefined : JSON.stringify(result.value);

// Consumer-style replay: real Create, the recorded prefix envelopes and the actual live store; no recorded state is restored.
function open(b, at) {
  const ctx = { run: MF.Run.create(data, clone(b.trace.setup)), live: b.memoryPolicy === 'v2' ? MF.MemoryV2.migrate(data, {}, {}) : null };
  if (at === 'terminal') for (const [index, { command, errorCode }] of b.prefix.entries()) {
    assert.notEqual(command.kind, 'commitMemory', `${b.id} prefix#${index}`);
    let code = null;
    try {
      if (command.scope === 'battle') ctx.run.battleCmd(command.kind, clone(command.args));
      else ctx.run[command.kind](...clone(command.args));
    } catch (error) { assert.ok(error instanceof MF.RuleError, `${b.id} prefix#${index}: ${error.message}`); code = error.code; }
    assert.equal(code, errorCode, `${b.id} prefix#${index}`);
  }
  return ctx;
}
const now = ctx => JSON.stringify({ run: ctx.run.s, memory: ctx.live });
function storeArgument(liveStore, spec) {
  if (spec.mode === 'live') return liveStore;
  if (spec.mode === 'literal') return clone(spec.value);
  const out = clone(liveStore);
  for (const key of spec.remove) delete out[key];
  return Object.assign(out, clone(spec.set));
}

test('four bases replay real gen_traces prefixes from a fresh Create to their terminal save, with the candidate scan and provenance recorded', () => {
  assert.deepEqual(fixture.bases.map(b => [b.id, b.memoryPolicy, b.role, b.status]), [['legacy-clear', 'legacy', 'clear', 'COVERED'],
    ['legacy-defeat', 'legacy', 'defeat', 'COVERED'], ['v2-clear', 'v2', 'clear', 'COVERED'], ['v2-defeat', 'v2', 'defeat', 'COVERED']]);
  assert.deepEqual(['legacy-defeat', 'v2-defeat'].map(id => [base(id).seed, base(id).length]), [[3561, 'full'], [3561, 'full']]);
  assert.deepEqual([fixture.denominators.bases, fixture.denominators.scenarios, fixture.denominators.plannedOps, fixture.denominators.policies], [4, 15, 88, { legacy: 2, v2: 2 }]);
  assert.deepEqual(Object.values(fixture.sources).map(source => source.path), ['data/game_data.json', 'engine/rules-core.js', 'proposals/memory_v2.cjs',
    'tools/gen_traces.mjs', 'engine/tests/fuzz_runs.mjs', 'tools/gen_run_create_vectors.mjs', 'tools/gen_run_memory_vectors.mjs']);
  for (const source of Object.values(fixture.sources)) assert.equal(source.sha256, sha256(readFileSync(new URL(`../../${source.path}`, import.meta.url))), source.path);
  assert.deepEqual([fixture.status, fixture.coverage.totals.csharpMemoryReplay, fixture.nativeDiagnosticsRuntime], ['JS_ORACLE_ONLY', 0, { node: process.versions.node }]);
  for (const b of fixture.bases) {
    const trace = generateScenario({ seed: b.seed, length: b.length, memoryPolicy: b.memoryPolicy });
    assert.deepEqual(b.trace.setup, trace.setup, b.id);
    assert.equal(trace.commands.at(-1).command.kind, 'commitMemory', `${b.id}: the trace's own save is the only command left out`);
    assert.deepEqual(b.prefix, trace.commands.slice(0, -1).map(({ command, outcome: result, errorCode }) => ({ command, outcome: result, errorCode })), b.id);
    assert.equal(stateHash(b.created.state), trace.initialStateHash, b.id);
    for (const at of ['created', 'terminal']) assert.equal(now(open(b, at)), JSON.stringify(b[at].state), `${b.id} ${at}`);
    const run = b.terminal.state.run, { candidates, chosen } = b.selection;
    assert.deepEqual([b.created.state.run.phase, run.phase, run.pending], ['MAP', ...(b.role === 'defeat'
      ? ['DEFEAT', { type: 'save', defeat: true }] : ['VICTORY', { type: 'save', defeat: false }])], b.id);
    assert.deepEqual([candidates[chosen].seed, candidates[chosen].length], [b.seed, b.length], b.id);
    if (b.memoryPolicy === 'legacy') { assert.deepEqual([Object.hasOwn(run, 'memoryPolicy'), b.terminal.state.memory], [false, null], b.id); continue; }
    // The M0 empty-account store is untouched until a memory commit; the Run captured its own memoryAfter at the save.
    assert.deepEqual(b.terminal.state.memory, MF.MemoryV2.migrate(data, {}, {}), b.id);
    assert.deepEqual([run.memoryVersion, run.runId, Object.hasOwn(run, 'memoryAfter')], [0, b.trace.setup.runId, true], b.id);
  }
});

test('every scenario replays as a consumer: actual public calls on its own Run, the live store from each returned commit, ordered JSON states/returns/errors/inputs', () => {
  for (const sc of fixture.scenarios) {
    assert.equal(sc.status, 'COVERED', sc.id);
    const b = base(sc.base), ctx = open(b, sc.at);
    assert.equal(sc.currentStoreMode, b.memoryPolicy === 'v2' ? 'live' : 'none', sc.id);
    assert.equal(now(ctx), JSON.stringify(b[sc.at].state), `${sc.id}: opening point`);
    for (const o of sc.ops) {
      const where = `${sc.id} ${o.label}`;
      if (o.status === 'NOT_COVERED') { assert.ok(o.reason, where); continue; }
      assert.equal(now(ctx), JSON.stringify(pointOf(sc, o.before)), `${where}: before`);
      const args = clone(o.args);
      if (o.store) {
        const store = storeArgument(ctx.live, o.store);
        assert.equal(sha256(JSON.stringify(store)), o.store.jsonHash, `${where}: store argument`);
        args.push(store);
      }
      const input = JSON.stringify(args);
      let value, error = null;
      try { value = ctx.run[o.method](...args); } catch (e) { error = e; }
      if (o.outcome === 'returned') {
        assert.equal(error, null, `${where}: ${error?.message}`);
        assert.equal(json(value), recorded(o.result), `${where}: return`);
        if (o.method === 'commitMemory' && b.memoryPolicy === 'v2') ctx.live = value.state;
      } else {
        const e = o.error;
        assert.ok(error, `${where}: expected ${outcome(o)}`);
        if (e.kind === 'RuleError') assert.ok(error instanceof MF.RuleError && error.code === e.code && json(error.detail) === recorded(e.detail), where);
        else if (e.kind === 'MemoryError') assert.ok(!(error instanceof MF.RuleError) && Object.getPrototypeOf(error) === Error.prototype && error.code === e.code, where);
        else assert.ok(!(error instanceof MF.RuleError) && error.code === undefined && error.name === e.name && error.message === e.message, where);
      }
      assert.equal(JSON.stringify(args), input, `${where}: caller arguments changed`);
      assert.equal(now(ctx), JSON.stringify(pointOf(sc, o.after)), `${where}: complete Run/store point`);
    }
  }
});
test('legacy boundary: phase before selection shape, exact BAD_CHOICE details, native prototype keys, capacity/defeat order and the base-kit rebuild', () => {
  const ordered = id => scenario(id).ops.filter(o => o.status === 'COVERED').map(o => [o.label, outcome(o)]);
  const lc = base('legacy-clear'), lcRun = lc.terminal.state.run;
  // Fresh Creates have nothing to save; the phase decides before any selection detail.
  for (const [cid, info] of Object.entries(op('legacy-fresh', 'changes-fresh').result.value)) assert.deepEqual(info.changes, [], cid);
  assert.deepEqual(ordered('legacy-fresh'), [['changes-fresh', 'returned'], ['preview-legacy-policy', 'RuleError:WRONG_PHASE'], ['commit-phase-before-shape', 'RuleError:WRONG_PHASE']]);
  const changes = op('legacy-clear-selection-rejections', 'changes-terminal').result.value;
  assert.deepEqual(Object.keys(changes), lc.facts.charIds);
  for (const p of lcRun.party) assert.equal(changes[p.charId].capacity, MF.Run.capacityOf(ix, p), p.charId);
  for (const info of Object.values(changes)) for (const change of info.changes)
    assert.ok(['remove', 'evo', 'plus', 'part', 'add'].includes(change.type) && Number.isInteger(change.cost) && typeof change.label === 'string', change.label);
  const objectShape = new Set(['selection-null', 'selection-array', 'selection-string']);
  for (const o of scenario('legacy-clear-selection-rejections').ops.slice(2))
    assert.deepEqual([o.error.code, o.error.detail.value], ['BAD_CHOICE', objectShape.has(o.label) ? 'memory selection must be an object' : 'invalid memory selection'], o.label);
  // The rebuild runs only after every check; JS returns run.savedMemory itself, which the oracle records instead of hiding.
  const over = covered('legacy-clear-commit', 'over-capacity');
  if (over) {
    const detail = over.error.detail.value;
    assert.ok(detail.charId === lc.facts.overCapacity.charId && detail.cost === lc.facts.overCapacity.cost && detail.cost > detail.capacity);
  }
  const valid = op('legacy-clear-commit', 'commit-valid'), done = stateAfter('legacy-clear-commit', 'commit-valid');
  assert.ok(lc.facts.kept > 0 && Object.values(valid.args[0]).flat().length === lc.facts.kept);
  assert.deepEqual([done.run.phase, done.run.pending], ['DONE', { type: 'done' }]);
  assert.equal(JSON.stringify(done.run.savedMemory), JSON.stringify(valid.result.value));
  assert.deepEqual(valid.sourceAliases, [{ result: [], shares: ['run', 'savedMemory'] }]);
  assert.deepEqual(ordered('legacy-clear-commit').slice(-2), [['commit-after-done', 'RuleError:WRONG_PHASE'], ['changes-after-done', 'returned']]);
  // An Object.prototype member name is a native TypeError once an index is read (never a RuleError); an empty list commits base kits.
  assert.deepEqual(op('legacy-clear-prototype-key', 'prototype-key-index').error,
    { kind: 'NativeError', name: 'TypeError', message: "Cannot read properties of undefined (reading 'length')" });
  assert.deepEqual(op('legacy-clear-prototype-key', 'prototype-key-empty').result.value, Object.fromEntries(lc.facts.charIds.map(cid => [cid, MF.Run.baseKit(ix, cid)])));
  // A real defeat keeps at most one change; two kept fail before any capacity check.
  const two = covered('legacy-defeat', 'keep-two');
  if (two) assert.equal(two.error.code, 'DEFEAT_KEEP_ONE');
  assert.ok(Object.values(op('legacy-defeat', 'commit-valid').args[0]).flat().length <= 1);
  assert.deepEqual([stateAfter('legacy-defeat', 'commit-valid').run.phase, op('legacy-defeat', 'commit-after-done').error.code], ['DONE', 'WRONG_PHASE']);
});

test('v2 boundary: request, store, receipt, version and selection order; commit, replay and conflicts; metadata, "__proto__" and max-safe receipts', () => {
  const ordered = id => scenario(id).ops.filter(o => o.status === 'COVERED').map(o => [o.label, outcome(o)]);
  const v2 = base('v2-clear'), [a, b] = v2.facts.pair, runId = v2.trace.setup.runId, beforeRows = v2.terminal.state.run.memoryBefore;
  for (const [cid, info] of Object.entries(op('v2-fresh', 'changes-fresh').result.value)) assert.deepEqual([info.changes, info.usedBefore], [[], 0], cid);
  assert.deepEqual(ordered('v2-fresh').slice(1), [['preview-before-save', 'RuleError:WRONG_PHASE'], ['request-null-before-phase', 'RuleError:INVALID_SELECTION'],
    ['new-request-before-save', 'RuleError:WRONG_PHASE']]);
  const changes = op('v2-clear-previews', 'changes-terminal').result.value, empty = op('v2-clear-previews', 'preview-empty').result.value;
  for (const [cid, info] of Object.entries(changes)) {
    assert.deepEqual([info.before, info.after], [beforeRows[cid], v2.terminal.state.run.memoryAfter[cid]], cid);
    for (const change of info.changes) assert.match(change.label, /^.+ (추가|제거|진화|강화 변경|(회로|각인) (교체|제거))$/);
  }
  assert.deepEqual([empty.memory, empty.error], [beforeRows, null]);
  assert.deepEqual(empty.capacities, Object.fromEntries(Object.entries(changes).map(([cid, info]) => [cid, info.capacity])));
  assert.equal(op('v2-clear-previews', 'preview-pair-reversed').result.value.error, null);
  for (const label of ['preview-duplicate', 'preview-unknown-id', 'preview-non-string-id', 'preview-not-array'])
    assert.deepEqual(op('v2-clear-previews', label).error, { kind: 'MemoryError', name: 'Error', code: 'INVALID_SELECTION', message: 'INVALID_SELECTION' }, label);
  const overPreview = covered('v2-clear-previews', 'preview-over-capacity');
  if (overPreview) assert.equal(overPreview.result.value.error, 'OVER_CAPACITY');

  const shape = 'RuleError:INVALID_SELECTION', snapshot = 'RuleError:INVALID_SNAPSHOT';
  assert.deepEqual(ordered('v2-clear-request-order'), [['request-null', shape], ['request-id-empty', shape], ['expected-negative', shape], ['expected-fraction', shape],
    ['expected-string', shape], ['selection-not-array', shape], ['selection-duplicate', shape], ['selection-non-string', shape],
    ['run-mismatch-before-store', 'RuleError:RUN_MISMATCH'], ['run-id-absent', 'RuleError:RUN_MISMATCH']]);
  assert.deepEqual(ordered('v2-clear-store-order'), [['store-null', snapshot], ['store-schema', snapshot], ['store-version-negative', snapshot], ['store-version-string', snapshot],
    ['store-history-null', snapshot], ['store-receipts-array', snapshot], ['store-memory-absent', 'MemoryError:INVALID_SNAPSHOT'],
    ['store-memory-duplicate-key', 'MemoryError:IDENTITY_REQUIRED'], ['receipt-null', 'NativeError:TypeError'], ['receipt-without-response', 'NativeError:SyntaxError'],
    ['receipt-conflict-before-version', 'RuleError:REQUEST_CONFLICT']]);
  assert.equal(op('v2-clear-store-order', 'receipt-null').error.message, "Cannot read properties of null (reading 'body')");
  assert.equal(op('v2-clear-store-order', 'receipt-without-response').error.message, '"undefined" is not valid JSON');
  const versions = ordered('v2-clear-version-conflicts'), conflict = 'RuleError:VERSION_CONFLICT';
  assert.deepEqual(versions.slice(0, 6), [['stale-request-version', conflict], ['stale-run-version', conflict], ['store-moved-on', conflict],
    ['stored-rows-reordered', conflict], ['stored-members-reordered', conflict], ['unknown-change-id', 'MemoryError:INVALID_SELECTION']]);
  if (v2.facts.overCapacity) assert.deepEqual(versions[6], ['over-capacity', 'RuleError:OVER_CAPACITY']);
  // The reordered rows hold the same cards; only the source's ordered JSON text comparison rejects them.
  const [lead, ...rest] = v2.facts.charIds, reordered = op('v2-clear-version-conflicts', 'stored-members-reordered').store.set.memory[lead];
  assert.deepEqual(reordered, beforeRows[lead]);
  assert.notEqual(JSON.stringify(reordered), JSON.stringify(beforeRows[lead]));

  // Commit, then exact own-receipt replays (no new version, history or receipt); a changed body conflicts before the phase check.
  const committed = op('v2-clear-commit-replay', 'commit-pair'), { state, response } = committed.result.value;
  assert.deepEqual([state.version, state.history.length, Object.keys(state.receipts), response.version], [1, 1, ['mem-clear'], 1]);
  assert.deepEqual(state.history[0].selectedChangeIds, [b, a], 'history keeps the caller order');
  assert.equal(state.receipts['mem-clear'].body, JSON.stringify({ runId, expectedMemoryVersion: 0, selectedChangeIds: [a, b].sort() }));
  assert.deepEqual(state.receipts['mem-clear'].response, response);
  const finished = stateAfter('v2-clear-commit-replay', 'commit-pair');
  assert.deepEqual([finished.run.phase, finished.run.pending, finished.run.savedMemory, finished.memory], ['DONE', { type: 'done' }, response.memory, state]);
  for (const label of ['replay-reordered', 'replay-extra-member']) {
    const replay = op('v2-clear-commit-replay', label);
    assert.deepEqual([replay.result.value, replay.after], [committed.result.value, replay.before], label);
  }
  assert.deepEqual(ordered('v2-clear-commit-replay').slice(3), [['changed-selection-conflict', 'RuleError:REQUEST_CONFLICT'], ['changed-version-conflict', 'RuleError:REQUEST_CONFLICT'],
    ['new-request-after-done', 'RuleError:WRONG_PHASE'], ['preview-after-done', 'RuleError:WRONG_PHASE'], ['changes-after-done', 'returned']]);

  // Explicit equal party rows pass; non-party memory and M0 metadata survive; existing members keep position, new party members append.
  const meta = op('v2-clear-store-metadata', 'commit-with-metadata'), merged = meta.result.value.state, outside = v2.facts.nonPartyCharId;
  assert.deepEqual(Object.keys(merged.memory), [lead, outside, ...rest]);
  assert.deepEqual([merged.memory[outside], meta.store.set.memory[lead]], [MF.MemoryV2.baseline(data, outside), beforeRows[lead]]);
  assert.deepEqual([merged.quarantine, merged.legacyBackup, merged.version], [{}, {}, 1]);
  assert.deepEqual(op('v2-clear-store-metadata', 'replay-live').result.value, meta.result.value);

  const proto = op('v2-clear-proto-request-id', 'commit-proto-id').result.value, receipts = proto.state.receipts;
  assert.ok(Object.hasOwn(receipts, '__proto__') && Object.getPrototypeOf(receipts) === Object.prototype);
  assert.deepEqual(Object.keys(receipts), ['__proto__']);
  assert.deepEqual(op('v2-clear-proto-request-id', 'replay-proto-id').result.value, proto);
  assert.equal(op('v2-clear-proto-request-id', 'proto-id-conflict').error.code, 'REQUEST_CONFLICT');

  // A declared controlled receipt: the max-safe version is written as integer text inside the source JSON.stringify body.
  const preview = op('v2-clear-max-safe-receipt', 'preview-pair').result.value, replay = op('v2-clear-max-safe-receipt', 'max-safe-replay');
  const receipt = replay.store.set.receipts['retry-max-safe'], replayed = stateAfter('v2-clear-max-safe-receipt', 'max-safe-replay');
  assert.equal(op('v2-clear-max-safe-receipt', 'max-safe-body-conflict').error.code, 'REQUEST_CONFLICT');
  assert.equal(receipt.body, `{"runId":${JSON.stringify(runId)},"expectedMemoryVersion":9007199254740991,"selectedChangeIds":${JSON.stringify([a, b].sort())}}`);
  assert.deepEqual([replay.args[0].expectedMemoryVersion, replay.result.value.response, receipt.response.memory], [Number.MAX_SAFE_INTEGER, receipt.response, preview.memory]);
  assert.deepEqual([replayed.run.phase, replayed.run.savedMemory, replayed.memory.version, replayed.memory.history], ['DONE', preview.memory, 0, []]);

  // A real v2 defeat: two IDs fail as DEFEAT_KEEP_ONE without consuming the request ID.
  const previewTwo = covered('v2-defeat', 'preview-two'), commitTwo = covered('v2-defeat', 'commit-two'), kept = op('v2-defeat', 'commit-one');
  if (previewTwo) assert.equal(previewTwo.result.value.error, 'DEFEAT_KEEP_ONE');
  if (commitTwo) assert.equal(commitTwo.error.code, 'DEFEAT_KEEP_ONE');
  assert.ok(kept.args[0].selectedChangeIds.length <= 1);
  assert.deepEqual([kept.result.value.state.history[0].requestId, kept.result.value.state.version], ['mem-defeat', 1]);
  assert.deepEqual(op('v2-defeat', 'replay-one').result.value, kept.result.value);
  assert.equal(op('v2-defeat', 'new-request-after-done').error.code, 'WRONG_PHASE');

  for (const sc of fixture.scenarios) for (const o of sc.ops) if (o.status === 'COVERED' && o.sourceAliases.length)
    assert.ok(base(sc.base).memoryPolicy === 'legacy' && o.method === 'commitMemory' && o.outcome === 'returned', `${sc.id} ${o.label}: unexpected source alias`);
  assert.deepEqual(fixture.coverage.notCovered, fixture.scenarios.flatMap(sc => sc.ops.filter(o => o.status === 'NOT_COVERED').map(o => ({ scenario: sc.id, label: o.label, reason: o.reason }))));
});
test('strict checks reject stale provenance, identities, omissions, forged outcomes, references, hashes, store arguments and coverage', () => {
  assert.deepEqual(checkFixture(fixture), fixture.coverage.totals);
  const tamper = mutate => { const copy = clone(fixture); mutate(copy); return copy; };
  const at = (copy, id, label) => copy.scenarios.find(s => s.id === id).ops.find(o => o.label === label);
  for (const mutate of [
    copy => { copy.sources.engine.sha256 = '0'.repeat(64); },
    copy => { copy.status = 'CSHARP_REPLAY_COMPLETE'; },
    copy => { copy.extra = true; },
    copy => { copy.bases.pop(); },
    copy => { copy.scenarios.reverse(); },
    copy => { copy.bases[0].prefix.pop(); },
    copy => { copy.scenarios.find(s => s.id === 'legacy-clear-selection-rejections').ops.splice(2, 1); },
    copy => { at(copy, 'legacy-clear-commit', 'commit-valid').outcome = 'rejected'; },
    copy => { at(copy, 'v2-clear-store-order', 'store-null').error.code = 'OTHER_RULE'; },
    copy => { copy.scenarios.find(s => s.id === 'v2-clear-commit-replay').points[0].stateHash = '0'.repeat(64); },
    copy => { copy.bases[0].terminal.state.run.coins += 1; },
    copy => { at(copy, 'v2-clear-previews', 'preview-empty').result.value = null; },
    copy => { at(copy, 'v2-clear-version-conflicts', 'stale-run-version').store.set.version = 2; },
    copy => { at(copy, 'v2-clear-commit-replay', 'replay-reordered').after.ref = 5; },
    copy => { copy.coverage.totals.ops += 1; },
  ]) assert.throws(() => checkFixture(tamper(mutate)), /Run memory/);
  // An internally consistent forgery is exposed only by fresh actual execution.
  const forged = tamper(copy => {
    const result = at(copy, 'legacy-clear-selection-rejections', 'changes-terminal').result;
    for (const info of Object.values(result.value)) info.capacity += 1;
    result.jsonHash = sha256(JSON.stringify(result.value));
  });
  assert.throws(() => checkFixture(forged), /fresh actual execution/);
});

test('gzip export is deterministic and atomic, damaged containers fail, and unexpected source behaviour aborts with the previous output kept', t => {
  const directory = mkdtempSync(join(tmpdir(), 'mf-run-memory-'));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  const output = join(directory, 'vectors.json.gz');
  const summary = exportVectors(output), bytes = readFileSync(output), text = Buffer.from(`${JSON.stringify(fixture)}\n`);
  assert.deepEqual([summary.scenarios, summary.ops, summary.bytes, summary.sha256], [fixture.scenarios.length, fixture.coverage.totals.ops, bytes.length, sha256(bytes)]);
  assert.ok(gzipSync(text, { level: 9 }).equals(bytes), 'fresh export must be byte identical to this process generation');
  assert.deepEqual(readdirSync(directory), ['vectors.json.gz']);
  const crc = Buffer.from(bytes); crc[crc.length - 8] ^= 1;
  for (const bad of [bytes.subarray(0, bytes.length - 1), crc, Buffer.concat([bytes, gzipSync(Buffer.from('{}'), { level: 9 })]),
    gzipSync(Buffer.from('{"schema":"a","schema":"b"}'), { level: 9 })]) assert.throws(() => decodeVectors(bad));
  writeFileSync(output, 'previous artifact');
  const { commitMemory, previewMemory } = MF.Run.prototype;
  for (const [method, replacement, expected] of [
    // gen_traces never commits outside a save, so only the oracle's fresh-Run calls reach these faults.
    ['commitMemory', function (...args) { if (this.s.phase === 'MAP') throw new Error('unexpected memory fault'); return commitMemory.apply(this, args); }, /unexpected memory fault/],
    ['commitMemory', function (...args) { if (this.s.phase === 'MAP') throw new MF.RuleError('BAD_CHOICE'); return commitMemory.apply(this, args); }, { code: 'BAD_CHOICE' }],
    // A commit that writes into its caller's selection is caught by the argument check, not by the trace bot.
    ['commitMemory', function (selection, current) {
      const out = commitMemory.call(this, selection, current);
      if (selection && typeof selection === 'object') selection.tampered = true;
      return out;
    }, /caller arguments changed/],
    // Only the oracle previews an empty selection; a log-only mutation is caught by the complete-JSON purity check.
    ['previewMemory', function (ids) { if (Array.isArray(ids) && !ids.length) this.s.log.push('impure preview'); return previewMemory.call(this, ids); }, /changed complete state/],
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
  assert.match(saved.scope, /C# Run memory replay 0/);
});
