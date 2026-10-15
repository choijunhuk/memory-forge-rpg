// R1c §8.9–8.14/§16.2/§17.5/§20.9 P1: actual public Run memory boundary oracle. memoryChanges, previewMemory and commitMemory
// (legacy base-kit rebuild; v2 stable identity against the live store) run on fresh Creates and on real gen_traces terminal saves,
// each reached from a fresh Run.create through the trace's own recorded public commands. JS_ORACLE_ONLY: no C# Run memory call has
// replayed it. No account, disk or server operation and no economic or capacity policy change; malformed stores are declared inputs.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { mkdtempSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { isDeepStrictEqual } from 'node:util';
import { gzipSync } from 'node:zlib';
import { generateScenario, stateHash } from './gen_traces.mjs';
import { decodeVectors } from './gen_run_create_vectors.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const require = createRequire(import.meta.url), MF = require('../engine/rules-core.js');
const data = JSON.parse(readFileSync(join(root, 'data/game_data.json'), 'utf8')), originalData = JSON.stringify(data);
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const schema = 'mf-run-memory-v1';
const status = 'JS_ORACLE_ONLY';
const scope = 'Actual public Run memoryChanges/previewMemory/commitMemory on fresh Creates and on four real gen_traces terminal saves replayed from a fresh Run.create: complete JS Run/store points, arguments, returns and errors. C# Run memory replay 0; account storage, disk, server, Unity and device acceptance PENDING';
const isMain = process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url);
export const fixturePath = join(root, 'engine/tests/vectors/run_memory_vectors.json.gz');
const MAX = Number.MAX_SAFE_INTEGER;

// Fixed ordered gen_traces inputs. A clear base stops at the first candidate that also reaches OVER_CAPACITY naturally, else takes
// the first meeting its role; every scanned candidate is recorded. Nothing is restored from trace snapshots.
const clearCandidates = [[1000, 'short'], [1000, 'full'], [1001, 'full'], [1002, 'full']];
const basePlan = [
  { id: 'legacy-clear', memoryPolicy: 'legacy', role: 'clear', candidates: clearCandidates },
  { id: 'legacy-defeat', memoryPolicy: 'legacy', role: 'defeat', candidates: [[1102, 'full']] },
  { id: 'v2-clear', memoryPolicy: 'v2', role: 'clear', candidates: clearCandidates },
  { id: 'v2-defeat', memoryPolicy: 'v2', role: 'defeat', candidates: [[1102, 'full']] },
];
const selectionRules = {
  clear: 'Scan in order and stop at the first VICTORY save that also has a natural OVER_CAPACITY selection (one character\'s full change set); else take the first VICTORY save with a committable selection (legacy: in-capacity greedy keep of at least one change; v2: a jointly previewable pair of change IDs)',
  defeat: 'The single listed input must reach a DEFEAT save; it is complete when at least two changes exist for DEFEAT_KEEP_ONE',
};
// Prefix commands are the trace's own public Run surface minus its final memory commit; the canonical source hash pins the bot.
const prefixMethods = new Set(['enter', 'evolve', 'takeCard', 'skipCard', 'takeRelic', 'takePart', 'applyFoundPart',
  'proceed', 'rest', 'forgeOffer', 'forgeApply', 'forgeRemove', 'buy', 'eventChoose', 'archive']);
const memoryMethods = new Set(['memoryChanges', 'previewMemory', 'commitMemory']);
const topLevel = ['schema', 'status', 'scope', 'dataVersion', 'sources', 'nativeDiagnosticsRuntime', 'contract', 'denominators', 'coverage', 'bases', 'scenarios'];
const contract = {
  bases: 'A base is one fixed gen_traces input whose recorded commands, except its own final commitMemory, are applied from a fresh Run.create through public Run methods or run.battleCmd (v2 starts from the actual MemoryV2.migrate(data, {}, {}) store, as gen_traces does). prefix holds those command envelopes with the recorded outcome/errorCode (inputs); created and terminal are comparison points. Candidates are scanned in order under selection.rule and every scanned one is recorded',
  points: 'A point is {run: Run.s, memory: live v2 store or null} as ordered JSON including log, battle and RNG. stateHash is the gen_traces canonical hash (log/ev omitted), jsonHash SHA-256 of the ordered JSON text, undefinedPaths the own members or array items holding undefined (JSON omits the member or writes null)',
  scenarios: 'Each scenario opens its own Run at created (fresh Create) or terminal (fresh Create + prefix, asserted equal to the base point) and applies ops in order on that one Run. before/after reference the current point: ref -1 is the base point named by at, else points[ref]. Only a returned commitMemory may append a full point; every other op references the unchanged point, asserted JSON-identical',
  store: 'currentStoreMode live: the store starts as the actual MemoryV2.migrate(data, {}, {}) result and every returned v2 commitMemory replaces it with that return\'s state, never with recorded data. store.mode live passes that live store itself; live-patch passes a copy with the listed top-level members removed, then set (an existing member keeps its position, a new one appends); literal passes value. Patches and literals are declared public controlled inputs, never Core outputs; jsonHash is SHA-256 of the argument\'s ordered JSON text',
  calls: 'args are the public JSON arguments before the store. Every op asserts its arguments, store included, are JSON-identical after the call; memoryChanges, previewMemory and every rejection must leave the complete point unchanged',
  results: 'Return captured immediately at call time as {kind:"json",value,jsonHash,undefinedPaths}. sourceAliases lists result subgraphs that are the identical JS object as live Run state or an argument (legacy commitMemory returns run.savedMemory itself). C# returns detached graphs, so mutating a return is a C# consumer probe, not a source promise',
  errors: 'RuleError {name,code,message,detail}. MemoryError is memory_v2 fail(code), a plain Error carrying code, which C# raises as RuleException with that code. NativeError is a JS TypeError/SyntaxError at a declared native boundary, recorded with its V8 name and message; it must not become a RuleError. Any other exception, or any outcome other than the declared expect, aborts generation and preserves prior output',
  notCovered: 'An op whose role is not naturally reachable on the selected base is recorded NOT_COVERED with its reason; no phase, number or state is injected to reach it',
  visibility: 'Private test oracle: full states expose draw order and RNG',
  separate: 'The 1000-trace hash corpus, the five-scenario progression oracle and the M0 memory rule vectors are separate evidence; nothing here performs account, disk or server storage',
};

function sources() {
  return Object.fromEntries(Object.entries({ data: 'data/game_data.json', engine: 'engine/rules-core.js', memory: 'proposals/memory_v2.cjs',
    canonical: 'tools/gen_traces.mjs', fuzzPolicy: 'engine/tests/fuzz_runs.mjs', runCreateDecoder: 'tools/gen_run_create_vectors.mjs', generator: 'tools/gen_run_memory_vectors.mjs',
  }).map(([key, path]) => [key, { path, sha256: sha256(readFileSync(join(root, path))) }]));
}
const loadedSources = sources();

// Own members and array items holding undefined: ordered JSON omits the member or writes null.
function undefinedPaths(value) {
  const out = [];
  const visit = (v, path) => {
    if (v === undefined) out.push(path);
    else if (Array.isArray(v)) v.forEach((x, i) => visit(x, [...path, i]));
    else if (v !== null && typeof v === 'object') for (const key of Object.keys(v)) visit(v[key], [...path, key]);
  };
  visit(value, []); return out;
}
function encode(value) {
  if (value === undefined) return { kind: 'undefined' };
  const json = JSON.stringify(value);
  return { kind: 'json', value: JSON.parse(json), jsonHash: sha256(json), undefinedPaths: undefinedPaths(value) };
}
const live = ctx => ({ run: ctx.run.s, memory: ctx.live });
function point(ctx) {
  const json = JSON.stringify(live(ctx)), state = JSON.parse(json);
  return { state, stateHash: stateHash(state), jsonHash: sha256(json), undefinedPaths: undefinedPaths(live(ctx)) };
}
const ref = (index, p) => ({ ref: index, stateHash: p.stateHash, jsonHash: p.jsonHash });
// Generating Node release is truthful provenance for the V8 native messages only: exactly { node: "major.minor.patch" }.
function runtimeNode(runtime) {
  assert.ok(runtime !== null && typeof runtime === 'object' && !Array.isArray(runtime) && isDeepStrictEqual(Object.keys(runtime), ['node'])
    && typeof runtime.node === 'string' && /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.test(runtime.node), 'Run memory native diagnostics runtime must be exactly {node: numeric semver}');
  return runtime.node;
}
// A fresh Run from the recorded setup; v2 gets the same actual M0 empty-account store gen_traces starts from.
function open(setup, policy) {
  return { run: MF.Run.create(data, clone(setup)), live: policy === 'v2' ? MF.MemoryV2.migrate(data, {}, {}) : null };
}
// One recorded prefix envelope through the public surface; only the RuleError the trace itself recorded is a rejection.
function advance(ctx, step, where) {
  const { command } = step, run = ctx.run;
  let code = null;
  try {
    if (command.scope === 'battle') {
      assert.ok(run.battle && command.v === run.battle.s.v && command.rid === `r${run.battle.s.v}-${command.kind}` && ['play', 'ult', 'end'].includes(command.kind),
        `${where}: battle command envelope differs from the live battle`);
      run.battleCmd(command.kind, clone(command.args));
    } else {
      assert.ok(command.scope === 'run' && prefixMethods.has(command.kind) && Array.isArray(command.args), `${where}: unexpected prefix command ${command.kind}`);
      run[command.kind](...clone(command.args));
    }
  } catch (e) {
    if (!(e instanceof MF.RuleError) || step.outcome !== 'rejected' || e.code !== step.errorCode) throw e;
    code = e.code;
  }
  assert.equal(code === null ? 'committed' : 'rejected', step.outcome, `${where}: prefix outcome differs from trace`);
}

// Pure source queries whose results choose later inputs; any change to the complete JSON (RNG/log included) aborts generation.
function pure(ctx, what, call) {
  const json = JSON.stringify(live(ctx)), absent = JSON.stringify(undefinedPaths(live(ctx))), value = call();
  assert.ok(JSON.stringify(live(ctx)) === json && JSON.stringify(undefinedPaths(live(ctx))) === absent, `${what} changed complete state/RNG/log`);
  return value;
}
const nonParty = charIds => data.characters.find(c => !charIds.includes(c.id)).id;
function legacyFacts(ctx) {
  const run = ctx.run, defeat = !!run.s.pending.defeat, charIds = run.s.party.map(p => p.charId);
  const all = pure(ctx, 'facts memoryChanges', () => run.memoryChanges());
  const flat = charIds.flatMap(cid => all[cid].changes.map((change, index) => ({ cid, index, cost: change.cost })));
  const total = cid => all[cid].changes.reduce((n, change) => n + change.cost, 0);
  // Defeat keeps the first in-capacity change; a clear keeps, per character in change order, every change that still fits.
  const keep = Object.fromEntries(charIds.map(cid => [cid, []]));
  if (defeat) { const one = flat.find(c => c.cost <= all[c.cid].capacity); if (one) keep[one.cid].push(one.index); }
  else for (const cid of charIds) {
    let used = 0;
    all[cid].changes.forEach((change, index) => { if (used + change.cost <= all[cid].capacity) { keep[cid].push(index); used += change.cost; } });
  }
  const over = defeat ? undefined : charIds.find(cid => total(cid) > all[cid].capacity), keepTwo = {};
  for (const c of flat.slice(0, 2)) (keepTwo[c.cid] ||= []).push(c.index);
  return { charIds, nonPartyCharId: nonParty(charIds), defeat, changeCounts: Object.fromEntries(charIds.map(cid => [cid, all[cid].changes.length])),
    capacities: Object.fromEntries(charIds.map(cid => [cid, all[cid].capacity])), keep, kept: Object.values(keep).flat().length,
    overCapacity: over ? { charId: over, indices: all[over].changes.map((_, index) => index), cost: total(over), capacity: all[over].capacity } : null,
    keepTwo: defeat && flat.length >= 2 ? keepTwo : null };
}
function v2Facts(ctx) {
  const run = ctx.run, defeat = !!run.s.pending.defeat, charIds = run.s.party.map(p => p.charId);
  const all = pure(ctx, 'facts memoryChanges', () => run.memoryChanges()), ids = charIds.flatMap(cid => all[cid].changes.map(change => change.id));
  const error = list => pure(ctx, 'facts previewMemory', () => run.previewMemory(list)).error;
  assert.equal(error([]), null, 'Run memory: an empty selection must preview cleanly at the terminal save');
  let pair = null, overCapacity = null;
  if (!defeat) {
    search: for (let i = 0; i < ids.length; i++) for (let j = i + 1; j < ids.length; j++) if (error([ids[i], ids[j]]) === null) { pair = [ids[i], ids[j]]; break search; }
    for (const cid of charIds) {
      const mine = all[cid].changes.map(change => change.id);
      if (mine.length && error(mine) === 'OVER_CAPACITY') { overCapacity = { charId: cid, ids: mine }; break; }
    }
  }
  return { charIds, nonPartyCharId: nonParty(charIds), defeat, changeIds: ids, single: ids.find(id => error([id]) === null) ?? null, pair,
    two: defeat && ids.length >= 2 ? ids.slice(0, 2) : null, overCapacity };
}
const meetsRole = (spec, o) => spec.role === 'defeat' ? o.phase === 'DEFEAT'
  : o.phase === 'VICTORY' && (spec.memoryPolicy === 'v2' ? o.facts.pair !== null : o.facts.kept > 0);
const completesRole = (spec, facts) => spec.role === 'defeat' ? (spec.memoryPolicy === 'v2' ? facts.two : facts.keepTwo) !== null : facts.overCapacity !== null;

function observe(spec, seed, length) {
  const where = `Run memory base ${spec.id} seed${seed}-${length}`, S = loadedSources;
  const trace = generateScenario({ seed, length, memoryPolicy: spec.memoryPolicy });
  assert.deepEqual(trace.sources, { data: S.data, engine: S.engine, memory: S.memory, generator: S.canonical, fuzzPolicy: S.fuzzPolicy }, `${where}: trace provenance differs`);
  const final = trace.commands.at(-1), steps = trace.commands.slice(0, -1);
  assert.ok(final && final.command.scope === 'run' && final.command.kind === 'commitMemory' && final.outcome === 'committed', `${where}: trace does not end in its terminal memory commit`);
  const ctx = open(trace.setup, spec.memoryPolicy), created = point(ctx);
  assert.equal(created.stateHash, trace.initialStateHash, `${where}: initial state hash differs from trace`);
  steps.forEach((step, index) => {
    advance(ctx, step, `${where}#${index}`);
    assert.equal(stateHash(live(ctx)), step.stateHash, `${where}#${index}: state hash differs from trace`);
  });
  const s = ctx.run.s;
  assert.ok(s.pending?.type === 'save' && ['VICTORY', 'DEFEAT'].includes(s.phase) && !!s.pending.defeat === (s.phase === 'DEFEAT'), `${where}: prefix does not stop at the terminal save`);
  const terminal = point(ctx), facts = spec.memoryPolicy === 'v2' ? v2Facts(ctx) : legacyFacts(ctx);
  assert.equal(JSON.stringify(data), originalData, `${where}: source definitions changed`);
  return { seed, length, trace, prefix: steps.map(({ command, outcome, errorCode }) => ({ command, outcome, errorCode })), created, terminal,
    phase: s.phase, outcome: s.result.outcome, facts };
}
function captureBase(spec) {
  const scanned = []; let fallback = null, chosen = null;
  for (const [seed, length] of spec.candidates) {
    const o = observe(spec, seed, length), eligible = meetsRole(spec, o), complete = eligible && completesRole(spec, o.facts);
    scanned.push({ seed, length, phase: o.phase, outcome: o.outcome, prefixCommands: o.prefix.length, eligible, complete });
    if (eligible && !fallback) fallback = { index: scanned.length - 1, o };
    if (complete) { chosen = { index: scanned.length - 1, o }; break; }
  }
  const pick = chosen || fallback, head = { id: spec.id, memoryPolicy: spec.memoryPolicy, role: spec.role };
  const selection = { rule: selectionRules[spec.role], candidates: scanned, chosen: pick ? pick.index : null };
  if (!pick) return { ...head, status: 'NOT_COVERED', reason: `No listed candidate reaches the ${spec.role} role naturally`, selection };
  const { o } = pick, { trace } = o;
  return { ...head, status: 'COVERED', selection, seed: o.seed, length: o.length,
    trace: { schema: trace.schema, policySeed: trace.policy.seed, setup: trace.setup, initialStateHash: trace.initialStateHash,
      commandCount: trace.commands.length, finalStateHash: trace.finalStateHash, summary: trace.summary },
    prefix: o.prefix, created: o.created, terminal: o.terminal, facts: o.facts };
}
// Op builders. Each scenario op is built lazily from env {facts, setup, state (the opening point), results (earlier returns)}.
const LIVE = { mode: 'live' };
const patch = (set, remove = []) => ({ mode: 'live-patch', set, remove });
const literal = value => ({ mode: 'literal', value });
const rule = code => ({ kind: 'RuleError', code });
const memoryFail = code => ({ kind: 'MemoryError', code });
const native = name => ({ kind: 'NativeError', name });
const call = (label, method, args = [], expect = 'returned', previewError) => ({ label, method, args, expect, previewError });
const commit = (label, request, store, expect = 'returned') => ({ label, method: 'commitMemory', args: [request], store, expect });
const uncovered = (label, method, reason) => ({ label, method, notCovered: reason });
const request = (e, requestId, selectedChangeIds, extra = {}) => ({ requestId, runId: e.setup.runId, expectedMemoryVersion: 0, selectedChangeIds, ...extra });
// The source receipt body formula, used only to build declared controlled receipts.
const receiptBody = (e, expectedMemoryVersion, ids) => JSON.stringify({ runId: e.setup.runId, expectedMemoryVersion, selectedChangeIds: [...ids].sort() });
const lead = e => e.facts.charIds[0];
const leadRows = e => e.state.run.memoryBefore[lead(e)];
const reversed = e => [e.facts.pair[1], e.facts.pair[0]];
const kept = e => e.facts.single ? [e.facts.single] : [];
const maxSafeReceipt = e => patch({ receipts: { 'retry-max-safe': { body: receiptBody(e, MAX, e.facts.pair),
  response: { memory: e.results[0].memory, used: e.results[0].used, version: MAX } } } });
const noOverCapacity = 'No party character on the selected clear base has a full change set above its capacity; nothing is injected to force OVER_CAPACITY';
const noSecondChange = 'The defeat base has fewer than two memory changes; nothing is injected to force DEFEAT_KEEP_ONE';

const scenarioPlan = [
  { id: 'legacy-fresh', base: 'legacy-clear', at: 'created', role: 'fresh query; legacy policy and phase before selection shape', ops: [
    () => call('changes-fresh', 'memoryChanges'),
    () => call('preview-legacy-policy', 'previewMemory', [[]], rule('WRONG_PHASE')),
    () => call('commit-phase-before-shape', 'commitMemory', [[]], rule('WRONG_PHASE')),
  ] },
  { id: 'legacy-clear-selection-rejections', base: 'legacy-clear', at: 'terminal', role: 'terminal query and BAD_CHOICE selection shapes', ops: [
    () => call('changes-terminal', 'memoryChanges'),
    () => call('preview-legacy-policy', 'previewMemory', [[]], rule('WRONG_PHASE')),
    () => call('selection-null', 'commitMemory', [null], rule('BAD_CHOICE')),
    () => call('selection-array', 'commitMemory', [[]], rule('BAD_CHOICE')),
    () => call('selection-string', 'commitMemory', ['all'], rule('BAD_CHOICE')),
    e => call('selection-nonparty-character', 'commitMemory', [{ [e.facts.nonPartyCharId]: [] }], rule('BAD_CHOICE')),
    e => call('indices-not-array', 'commitMemory', [{ [lead(e)]: 0 }], rule('BAD_CHOICE')),
    e => call('indices-duplicate', 'commitMemory', [{ [lead(e)]: [0, 0] }], rule('BAD_CHOICE')),
    e => call('index-fraction', 'commitMemory', [{ [lead(e)]: [0.5] }], rule('BAD_CHOICE')),
    e => call('index-string', 'commitMemory', [{ [lead(e)]: ['0'] }], rule('BAD_CHOICE')),
    e => call('index-negative', 'commitMemory', [{ [lead(e)]: [-1] }], rule('BAD_CHOICE')),
    e => call('index-out-of-bounds', 'commitMemory', [{ [lead(e)]: [e.facts.changeCounts[lead(e)]] }], rule('BAD_CHOICE')),
  ] },
  { id: 'legacy-clear-commit', base: 'legacy-clear', at: 'terminal', role: 'OVER_CAPACITY, then a valid base-kit rebuild, then DONE', ops: [
    e => e.facts.overCapacity ? call('over-capacity', 'commitMemory', [{ [e.facts.overCapacity.charId]: e.facts.overCapacity.indices }], rule('OVER_CAPACITY'))
      : uncovered('over-capacity', 'commitMemory', noOverCapacity),
    e => call('commit-valid', 'commitMemory', [e.facts.keep]),
    e => call('commit-after-done', 'commitMemory', [e.facts.keep], rule('WRONG_PHASE')),
    () => call('changes-after-done', 'memoryChanges'),
  ] },
  { id: 'legacy-clear-prototype-key', base: 'legacy-clear', at: 'terminal', role: 'native Object.prototype member name as a selection key', ops: [
    () => call('prototype-key-index', 'commitMemory', [{ constructor: [0] }], native('TypeError')),
    () => call('prototype-key-empty', 'commitMemory', [{ constructor: [] }]),
  ] },
  { id: 'legacy-defeat', base: 'legacy-defeat', at: 'terminal', role: 'DEFEAT_KEEP_ONE, then a one-change rebuild', ops: [
    () => call('changes-terminal', 'memoryChanges'),
    e => e.facts.keepTwo ? call('keep-two', 'commitMemory', [e.facts.keepTwo], rule('DEFEAT_KEEP_ONE')) : uncovered('keep-two', 'commitMemory', noSecondChange),
    e => call('commit-valid', 'commitMemory', [e.facts.keep]),
    () => call('commit-after-done', 'commitMemory', [{}], rule('WRONG_PHASE')),
  ] },
  { id: 'v2-fresh', base: 'v2-clear', at: 'created', role: 'fresh query; request shape before the phase', ops: [
    () => call('changes-fresh', 'memoryChanges'),
    () => call('preview-before-save', 'previewMemory', [[]], rule('WRONG_PHASE')),
    () => commit('request-null-before-phase', null, LIVE, rule('INVALID_SELECTION')),
    e => commit('new-request-before-save', request(e, 'fresh-save', []), LIVE, rule('WRONG_PHASE')),
  ] },
  { id: 'v2-clear-previews', base: 'v2-clear', at: 'terminal', role: 'terminal query and previews: valid, invalid IDs, natural OVER_CAPACITY', ops: [
    () => call('changes-terminal', 'memoryChanges'),
    () => call('preview-empty', 'previewMemory', [[]], 'returned', null),
    e => call('preview-one', 'previewMemory', [[e.facts.pair[0]]]),
    e => call('preview-pair-reversed', 'previewMemory', [reversed(e)], 'returned', null),
    e => call('preview-duplicate', 'previewMemory', [[e.facts.pair[0], e.facts.pair[0]]], memoryFail('INVALID_SELECTION')),
    () => call('preview-unknown-id', 'previewMemory', [['delta:missing']], memoryFail('INVALID_SELECTION')),
    () => call('preview-non-string-id', 'previewMemory', [[0]], memoryFail('INVALID_SELECTION')),
    () => call('preview-not-array', 'previewMemory', [null], memoryFail('INVALID_SELECTION')),
    e => e.facts.overCapacity ? call('preview-over-capacity', 'previewMemory', [e.facts.overCapacity.ids], 'returned', 'OVER_CAPACITY')
      : uncovered('preview-over-capacity', 'previewMemory', noOverCapacity),
  ] },
  { id: 'v2-clear-request-order', base: 'v2-clear', at: 'terminal', role: 'request shape before runId before the store', ops: [
    () => commit('request-null', null, literal(null), rule('INVALID_SELECTION')),
    e => commit('request-id-empty', request(e, '', [], { runId: 'other-run' }), literal(null), rule('INVALID_SELECTION')),
    e => commit('expected-negative', request(e, 'mem-shape', [], { runId: 'other-run', expectedMemoryVersion: -1 }), literal(null), rule('INVALID_SELECTION')),
    e => commit('expected-fraction', request(e, 'mem-shape', [], { runId: 'other-run', expectedMemoryVersion: 1.5 }), literal(null), rule('INVALID_SELECTION')),
    e => commit('expected-string', request(e, 'mem-shape', [], { runId: 'other-run', expectedMemoryVersion: '0' }), literal(null), rule('INVALID_SELECTION')),
    e => commit('selection-not-array', request(e, 'mem-shape', 'all', { runId: 'other-run' }), literal(null), rule('INVALID_SELECTION')),
    e => commit('selection-duplicate', request(e, 'mem-shape', [e.facts.pair[0], e.facts.pair[0]], { runId: 'other-run' }), literal(null), rule('INVALID_SELECTION')),
    e => commit('selection-non-string', request(e, 'mem-shape', [7], { runId: 'other-run' }), literal(null), rule('INVALID_SELECTION')),
    e => commit('run-mismatch-before-store', request(e, 'mem-shape', [], { runId: 'other-run' }), literal(null), rule('RUN_MISMATCH')),
    () => commit('run-id-absent', { requestId: 'mem-shape', expectedMemoryVersion: 0, selectedChangeIds: [] }, LIVE, rule('RUN_MISMATCH')),
  ] },
  { id: 'v2-clear-store-order', base: 'v2-clear', at: 'terminal', role: 'store shape and inspect, then own receipt before phase/version', ops: [
    e => commit('store-null', request(e, 'mem-store', []), literal(null), rule('INVALID_SNAPSHOT')),
    e => commit('store-schema', request(e, 'mem-store', []), patch({ schema: 'mf-memory-v1' }), rule('INVALID_SNAPSHOT')),
    e => commit('store-version-negative', request(e, 'mem-store', []), patch({ version: -1 }), rule('INVALID_SNAPSHOT')),
    e => commit('store-version-string', request(e, 'mem-store', []), patch({ version: '0' }), rule('INVALID_SNAPSHOT')),
    e => commit('store-history-null', request(e, 'mem-store', []), patch({ history: null }), rule('INVALID_SNAPSHOT')),
    e => commit('store-receipts-array', request(e, 'mem-store', []), patch({ receipts: [] }), rule('INVALID_SNAPSHOT')),
    e => commit('store-memory-absent', request(e, 'mem-store', []), patch({}, ['memory']), memoryFail('INVALID_SNAPSHOT')),
    e => commit('store-memory-duplicate-key', request(e, 'mem-store', []), patch({ memory: { [lead(e)]: [leadRows(e)[0], leadRows(e)[0]] } }), memoryFail('IDENTITY_REQUIRED')),
    e => commit('receipt-null', request(e, 'mem-store', []), patch({ receipts: { 'mem-store': null } }), native('TypeError')),
    e => commit('receipt-without-response', request(e, 'mem-store', []), patch({ receipts: { 'mem-store': { body: receiptBody(e, 0, []) } } }), native('SyntaxError')),
    e => commit('receipt-conflict-before-version', request(e, 'mem-store', [], { expectedMemoryVersion: 5 }),
      patch({ receipts: { 'mem-store': { body: receiptBody(e, 0, []), response: { memory: {}, used: {}, version: 1 } } } }), rule('REQUEST_CONFLICT')),
  ] },
  { id: 'v2-clear-version-conflicts', base: 'v2-clear', at: 'terminal', role: 'versions, then ordered stored rows, then the selection', ops: [
    e => commit('stale-request-version', request(e, 'mem-version', [], { expectedMemoryVersion: 1 }), LIVE, rule('VERSION_CONFLICT')),
    e => commit('stale-run-version', request(e, 'mem-version', [], { expectedMemoryVersion: 1 }), patch({ version: 1 }), rule('VERSION_CONFLICT')),
    e => commit('store-moved-on', request(e, 'mem-version', []), patch({ version: 1 }), rule('VERSION_CONFLICT')),
    e => commit('stored-rows-reordered', request(e, 'mem-version', []), patch({ memory: { [lead(e)]: [...leadRows(e)].reverse() } }), rule('VERSION_CONFLICT')),
    e => commit('stored-members-reordered', request(e, 'mem-version', []),
      patch({ memory: { [lead(e)]: leadRows(e).map(row => Object.fromEntries(Object.entries(row).reverse())) } }), rule('VERSION_CONFLICT')),
    e => commit('unknown-change-id', request(e, 'mem-version', ['delta:missing']), LIVE, memoryFail('INVALID_SELECTION')),
    e => e.facts.overCapacity ? commit('over-capacity', request(e, 'mem-version', e.facts.overCapacity.ids), LIVE, rule('OVER_CAPACITY'))
      : uncovered('over-capacity', 'commitMemory', noOverCapacity),
  ] },
  { id: 'v2-clear-commit-replay', base: 'v2-clear', at: 'terminal', role: 'commit, own-receipt replays, REQUEST_CONFLICT before phase, new ID WRONG_PHASE', ops: [
    e => commit('commit-pair', request(e, 'mem-clear', reversed(e)), LIVE),
    e => commit('replay-reordered', request(e, 'mem-clear', [...e.facts.pair]), LIVE),
    e => commit('replay-extra-member', request(e, 'mem-clear', reversed(e), { retry: 1 }), LIVE),
    e => commit('changed-selection-conflict', request(e, 'mem-clear', [e.facts.pair[0]]), LIVE, rule('REQUEST_CONFLICT')),
    e => commit('changed-version-conflict', request(e, 'mem-clear', reversed(e), { expectedMemoryVersion: 1 }), LIVE, rule('REQUEST_CONFLICT')),
    e => commit('new-request-after-done', request(e, 'mem-clear-next', [], { expectedMemoryVersion: 1 }), LIVE, rule('WRONG_PHASE')),
    () => call('preview-after-done', 'previewMemory', [[]], rule('WRONG_PHASE')),
    () => call('changes-after-done', 'memoryChanges'),
  ] },
  { id: 'v2-clear-store-metadata', base: 'v2-clear', at: 'terminal', role: 'explicit party rows accepted; non-party memory and store metadata preserved', ops: [
    e => commit('commit-with-metadata', request(e, 'mem-meta', kept(e)),
      patch({ memory: { [lead(e)]: leadRows(e), [e.facts.nonPartyCharId]: MF.MemoryV2.baseline(data, e.facts.nonPartyCharId) } })),
    e => commit('replay-live', request(e, 'mem-meta', kept(e)), LIVE),
  ] },
  { id: 'v2-clear-proto-request-id', base: 'v2-clear', at: 'terminal', role: 'own "__proto__" receipt', ops: [
    e => commit('commit-proto-id', request(e, '__proto__', []), LIVE),
    e => commit('replay-proto-id', request(e, '__proto__', []), LIVE),
    e => commit('proto-id-conflict', request(e, '__proto__', [e.facts.pair[0]]), LIVE, rule('REQUEST_CONFLICT')),
  ] },
  { id: 'v2-clear-max-safe-receipt', base: 'v2-clear', at: 'terminal', role: 'controlled own receipt whose body carries a max-safe version', ops: [
    e => call('preview-pair', 'previewMemory', [reversed(e)], 'returned', null),
    e => commit('max-safe-body-conflict', request(e, 'retry-max-safe', reversed(e), { expectedMemoryVersion: MAX - 1 }), maxSafeReceipt(e), rule('REQUEST_CONFLICT')),
    e => commit('max-safe-replay', request(e, 'retry-max-safe', reversed(e), { expectedMemoryVersion: MAX }), maxSafeReceipt(e)),
  ] },
  { id: 'v2-defeat', base: 'v2-defeat', at: 'terminal', role: 'DEFEAT_KEEP_ONE preview/commit, rejected ID reused, replay', ops: [
    () => call('changes-terminal', 'memoryChanges'),
    e => e.facts.two ? call('preview-two', 'previewMemory', [e.facts.two], 'returned', 'DEFEAT_KEEP_ONE') : uncovered('preview-two', 'previewMemory', noSecondChange),
    e => e.facts.two ? commit('commit-two', request(e, 'mem-defeat', e.facts.two), LIVE, rule('DEFEAT_KEEP_ONE')) : uncovered('commit-two', 'commitMemory', noSecondChange),
    e => commit('commit-one', request(e, 'mem-defeat', kept(e)), LIVE),
    e => commit('replay-one', request(e, 'mem-defeat', kept(e)), LIVE),
    e => commit('new-request-after-done', request(e, 'mem-defeat-next', [], { expectedMemoryVersion: 1 }), LIVE, rule('WRONG_PHASE')),
  ] },
];
// The store argument for one call; liveStore is the actual store at that moment (validation rebuilds it from the before point).
function storeArgument(liveStore, spec) {
  if (spec.mode === 'live') return liveStore;
  if (spec.mode === 'literal') return clone(spec.value);
  assert.equal(spec.mode, 'live-patch', 'Run memory store mode');
  const out = clone(liveStore);
  for (const key of spec.remove) delete out[key];
  return Object.assign(out, clone(spec.set));
}
function storeRecord(spec, value) {
  const fields = spec.mode === 'live-patch' ? { set: clone(spec.set), remove: [...spec.remove] } : spec.mode === 'literal' ? { value: clone(spec.value) } : {};
  return { mode: spec.mode, ...fields, jsonHash: sha256(JSON.stringify(value)) };
}
const errorFields = { RuleError: ['kind', 'name', 'code', 'message', 'detail'], MemoryError: ['kind', 'name', 'code', 'message'], NativeError: ['kind', 'name', 'message'] };
// Only these three exception families are recordable; anything else (assertions, mocked faults, other natives) is unexpected.
function classify(e) {
  if (e instanceof MF.RuleError) return { kind: 'RuleError', name: e.name, code: e.code, message: e.message, detail: encode(e.detail) };
  if (e !== null && typeof e === 'object' && Object.getPrototypeOf(e) === Error.prototype && typeof e.code === 'string' && e.message === e.code)
    return { kind: 'MemoryError', name: e.name, code: e.code, message: e.message };
  if (e instanceof TypeError || e instanceof SyntaxError) return { kind: 'NativeError', name: e.name, message: e.message };
  return null;
}
const matches = (expect, error) => expect !== 'returned' && expect.kind === error.kind && (error.kind === 'NativeError' ? expect.name === error.name : expect.code === error.code);
// Result subgraphs that are the identical JS object as live Run state or a caller argument (the first owner path wins).
function aliases(value, roots) {
  const owners = new Map(), out = [];
  const index = (v, path) => {
    if (v === null || typeof v !== 'object' || owners.has(v)) return;
    owners.set(v, path);
    for (const [key, item] of Object.entries(v)) index(item, [...path, Array.isArray(v) ? Number(key) : key]);
  };
  for (const [name, graph] of roots) index(graph, [name]);
  const visit = (v, path) => {
    if (v === null || typeof v !== 'object') return;
    if (owners.has(v)) { out.push({ result: path, shares: owners.get(v) }); return; }
    for (const [key, item] of Object.entries(v)) visit(item, [...path, Array.isArray(v) ? Number(key) : key]);
  };
  visit(value, []);
  return out;
}

function captureScenario(spec, bases) {
  const base = bases.find(b => b.id === spec.base);
  const head = { id: spec.id, base: spec.base, at: spec.at, role: spec.role, currentStoreMode: base.memoryPolicy === 'v2' ? 'live' : 'none' };
  if (base.status !== 'COVERED') return { ...head, status: 'NOT_COVERED', reason: `Base ${base.id} is NOT_COVERED`, points: [], ops: [] };
  const ctx = open(base.trace.setup, base.memoryPolicy), start = base[spec.at];
  if (spec.at === 'terminal') base.prefix.forEach((step, index) => advance(ctx, step, `Run memory ${spec.id} prefix#${index}`));
  let current = point(ctx), currentRef = -1;
  assert.ok(current.jsonHash === start.jsonHash && isDeepStrictEqual(current.undefinedPaths, start.undefinedPaths), `Run memory ${spec.id}: replay differs from base ${spec.at} point`);
  current = start;
  const env = { facts: base.facts, setup: base.trace.setup, state: start.state, results: [] }, points = [], ops = [];
  for (const make of spec.ops) {
    const op = make(env), index = ops.length, where = `Run memory ${spec.id}#${index} ${op.label}`;
    assert.ok(memoryMethods.has(op.method), `${where}: not a public memory method`);
    if (op.notCovered) {
      ops.push({ index, label: op.label, method: op.method, status: 'NOT_COVERED', reason: op.notCovered }); env.results.push(null); continue;
    }
    const args = clone(op.args), store = op.store ? storeArgument(ctx.live, op.store) : undefined;
    if (op.store) args.push(store);
    const before = ref(currentRef, current), input = JSON.stringify(args);
    let value, error = null;
    try { value = ctx.run[op.method](...args); }
    catch (e) {
      // Only the declared rejection is recorded; anything else aborts and preserves prior output.
      error = classify(e);
      if (!error || !matches(op.expect, error)) throw e;
    }
    if (!error) assert.equal(op.expect, 'returned', `${where}: declared rejection did not occur`);
    // Capture the return before anything else can reach it: JS may hand back live Run structures.
    const result = error ? null : encode(value), sourceAliases = error ? [] : aliases(value, [['run', ctx.run.s], ['arguments', args]]);
    assert.equal(JSON.stringify(args), input, `${where}: caller arguments changed`);
    if (op.previewError !== undefined) assert.equal(value.error, op.previewError, `${where}: preview error differs`);
    if (!error && op.method === 'commitMemory' && base.memoryPolicy === 'v2') ctx.live = value.state;
    const after = point(ctx), unchanged = after.jsonHash === current.jsonHash && isDeepStrictEqual(after.undefinedPaths, current.undefinedPaths);
    if (error || op.method !== 'commitMemory') assert.ok(unchanged, `${where}: ${error ? 'rejection' : 'query'} changed complete state/RNG/log/store`);
    else assert.equal(ctx.run.s.phase, 'DONE', `${where}: returned commit did not finish the Run`);
    if (!unchanged) { points.push(after); current = after; currentRef = points.length - 1; }
    ops.push({ index, label: op.label, method: op.method, status: 'COVERED', args: clone(op.args), ...(op.store ? { store: storeRecord(op.store, store) } : {}),
      expect: op.expect, outcome: error ? 'rejected' : 'returned', result, error, sourceAliases, before, after: ref(currentRef, current) });
    env.results.push(result ? result.value : null);
  }
  assert.equal(JSON.stringify(data), originalData, `Run memory ${spec.id}: source definitions changed`);
  return { ...head, status: 'COVERED', points, ops };
}
// Coverage is derived only from recorded ops, so check mode recomputes it instead of trusting stored counts.
function summarize(bases, scenarios) {
  const methods = {}, outcomes = {}, notCovered = [], perScenario = [];
  const add = (map, key) => { map[key] = (map[key] || 0) + 1; };
  for (const sc of scenarios) {
    const b = bases.find(x => x.id === sc.base), count = { returned: 0, rejected: 0, notCovered: 0 };
    if (sc.status !== 'COVERED') notCovered.push({ scenario: sc.id, label: null, reason: sc.reason });
    for (const op of sc.ops) {
      if (op.status === 'NOT_COVERED') { count.notCovered++; notCovered.push({ scenario: sc.id, label: op.label, reason: op.reason }); continue; }
      count[op.outcome]++;
      const tally = methods[op.method] ||= { returned: 0, rejected: 0 }; tally[op.outcome]++;
      add(outcomes, op.error ? `${op.method}:${op.error.kind}:${op.error.code ?? op.error.name}` : `${op.method}:returned`);
    }
    const last = sc.points.length ? sc.points.at(-1).state : sc.status === 'COVERED' ? b[sc.at].state : null;
    perScenario.push({ id: sc.id, base: sc.base, at: sc.at, status: sc.status, ops: sc.ops.length, ...count,
      finalPhase: last ? last.run.phase : null, storeVersion: last?.memory ? last.memory.version : null });
  }
  const sum = key => perScenario.reduce((n, s) => n + s[key], 0);
  return { totals: { bases: bases.length, coveredBases: bases.filter(b => b.status === 'COVERED').length, scenarios: scenarios.length,
    coveredScenarios: scenarios.filter(s => s.status === 'COVERED').length, ops: sum('ops'), returned: sum('returned'), rejected: sum('rejected'),
    notCovered: notCovered.length, csharpMemoryReplay: 0 },
  bases: bases.map(b => ({ id: b.id, status: b.status, seed: b.seed ?? null, length: b.length ?? null,
    phase: b.terminal ? b.terminal.state.run.phase : null, outcome: b.terminal ? b.terminal.state.run.result.outcome : null })),
  perScenario, methods, outcomes, notCovered };
}

export function generateCases() {
  assert.deepEqual(sources(), loadedSources, 'Run memory sources changed after load; rerun');
  const bases = basePlan.map(captureBase), scenarios = scenarioPlan.map(spec => captureScenario(spec, bases));
  assert.deepEqual(sources(), loadedSources, 'Run memory sources changed during generation; rerun');
  const policies = Object.fromEntries([...new Set(basePlan.map(b => b.memoryPolicy))].map(p => [p, basePlan.filter(b => b.memoryPolicy === p).length]));
  // One JSON round trip so the result equals what the strict reader decodes (own "__proto__" members stay own data members).
  return clone({ schema, status, scope, dataVersion: data.meta.version, sources: loadedSources, nativeDiagnosticsRuntime: { node: runtimeNode({ node: process.versions.node }) },
    contract, denominators: { bases: basePlan.length, scenarios: scenarioPlan.length, plannedOps: scenarioPlan.reduce((n, s) => n + s.ops.length, 0), policies,
      candidates: Object.fromEntries(basePlan.map(b => [b.id, b.candidates.map(([seed, length]) => ({ seed, length }))])) },
    coverage: summarize(bases, scenarios), bases, scenarios });
}

function validateJSON(value, paths, where) {
  for (const path of paths) {
    let parent = value; for (const key of path.slice(0, -1)) parent = parent?.[key];
    const key = path.at(-1);
    assert.ok(path.length && (Array.isArray(parent) ? parent[key] === null : parent !== null && typeof parent === 'object' && !Object.hasOwn(parent, key)),
      `${where}: undefined member is present in JSON`);
  }
}
function validatePoint(p, where) {
  assert.equal(p.stateHash, stateHash(p.state), `${where}: state hash`);
  assert.equal(p.jsonHash, sha256(JSON.stringify(p.state)), `${where}: ordered JSON hash`);
  validateJSON(p.state, p.undefinedPaths, where);
}
function validateResult(r, where) {
  if (r.kind === 'undefined') { assert.deepEqual(r, { kind: 'undefined' }, `${where}: undefined result shape`); return; }
  assert.equal(r.kind, 'json', `${where}: result kind`);
  assert.equal(r.jsonHash, sha256(JSON.stringify(r.value)), `${where}: result hash`);
  validateJSON(r.value, r.undefinedPaths, where);
}
function validateError(error, expect, where) {
  assert.ok(error && Object.hasOwn(errorFields, error.kind) && isDeepStrictEqual(Object.keys(error), errorFields[error.kind]) && matches(expect, error),
    `${where}: error differs from its declared expectation`);
  if (error.kind === 'RuleError') validateResult(error.detail, where);
}
function validateBase(b, spec) {
  const where = `Run memory base ${b.id}`, { candidates, chosen } = b.selection || {};
  assert.equal(b.selection?.rule, selectionRules[spec.role], `${where}: selection rule`);
  assert.ok(Array.isArray(candidates) && candidates.length && candidates.length <= spec.candidates.length, `${where}: candidate scan`);
  assert.deepEqual(candidates.map(c => [c.seed, c.length]), spec.candidates.slice(0, candidates.length), `${where}: candidate scan order`);
  const complete = candidates.findIndex(c => c.complete), eligible = candidates.findIndex(c => c.eligible);
  assert.ok(candidates.every(c => !c.complete || c.eligible), `${where}: complete candidate must meet the role`);
  assert.equal(candidates.length, complete >= 0 ? complete + 1 : spec.candidates.length, `${where}: scan must stop at the first complete candidate`);
  assert.equal(chosen, complete >= 0 ? complete : eligible >= 0 ? eligible : null, `${where}: chosen candidate`);
  if (chosen === null) { assert.ok(b.status === 'NOT_COVERED' && b.reason, `${where}: NOT_COVERED shape`); return; }
  assert.equal(b.status, 'COVERED', `${where}: status`);
  const c = candidates[chosen];
  assert.deepEqual([b.seed, b.length, b.prefix.length, b.trace.commandCount - 1], [c.seed, c.length, c.prefixCommands, c.prefixCommands], `${where}: chosen input or prefix length`);
  assert.ok(b.prefix.every(s => s.command.scope === 'battle' || prefixMethods.has(s.command.kind)), `${where}: prefix holds a memory commit or unknown command`);
  validatePoint(b.created, `${where} created`); validatePoint(b.terminal, `${where} terminal`);
  assert.equal(b.created.stateHash, b.trace.initialStateHash, `${where}: created hash differs from trace`);
  const run = b.terminal.state.run;
  assert.ok(run.pending?.type === 'save' && run.phase === c.phase && run.phase === (spec.role === 'defeat' ? 'DEFEAT' : 'VICTORY'), `${where}: terminal save differs from its role`);
}
function validateScenario(sc, bases) {
  const b = bases.find(x => x.id === sc.base), where = `Run memory scenario ${sc.id}`;
  assert.equal(sc.currentStoreMode, b.memoryPolicy === 'v2' ? 'live' : 'none', `${where}: store mode`);
  assert.ok(Array.isArray(sc.points) && Array.isArray(sc.ops), `${where}: points and ops required`);
  if (sc.status !== 'COVERED') {
    assert.ok(sc.status === 'NOT_COVERED' && b.status !== 'COVERED' && sc.reason && !sc.points.length && !sc.ops.length, `${where}: NOT_COVERED shape`); return;
  }
  assert.equal(b.status, 'COVERED', `${where}: base not covered`);
  assert.equal(sc.ops.length, scenarioPlan.find(s => s.id === sc.id).ops.length, `${where}: op omitted or added`);
  sc.points.forEach((p, i) => validatePoint(p, `${where} point ${i}`));
  let current = b[sc.at], currentRef = -1, used = 0;
  for (const [index, op] of sc.ops.entries()) {
    const at = `${where}#${index}`;
    assert.ok(op.index === index && memoryMethods.has(op.method) && typeof op.label === 'string', `${at}: op index/method/label`);
    if (op.status === 'NOT_COVERED') {
      assert.ok(isDeepStrictEqual(Object.keys(op), ['index', 'label', 'method', 'status', 'reason']) && typeof op.reason === 'string' && op.reason, `${at}: NOT_COVERED op shape`); continue;
    }
    assert.equal(op.status, 'COVERED', `${at}: op status`);
    assert.deepEqual(op.before, ref(currentRef, current), `${at}: before reference`);
    if (op.store) assert.equal(op.store.jsonHash, sha256(JSON.stringify(storeArgument(current.state.memory, op.store))), `${at}: store argument hash`);
    if (op.outcome === 'returned') {
      assert.ok(op.expect === 'returned' && op.error === null && op.result, `${at}: returned op error/result`);
      validateResult(op.result, at);
    } else {
      assert.ok(op.outcome === 'rejected' && op.result === null && Array.isArray(op.sourceAliases) && !op.sourceAliases.length, `${at}: rejected op result`);
      validateError(op.error, op.expect, at);
    }
    if (!isDeepStrictEqual(op.after, op.before)) {
      assert.ok(op.outcome === 'returned' && op.method === 'commitMemory' && used < sc.points.length, `${at}: only a returned commit may change the point`);
      current = sc.points[used]; currentRef = used++;
      assert.deepEqual(op.after, ref(currentRef, current), `${at}: after reference`);
    }
  }
  assert.equal(used, sc.points.length, `${where}: unreferenced points`);
}

export function checkFixture(fixture) {
  assert.deepEqual(Object.keys(fixture), topLevel, 'Run memory fixture fields differ');
  assert.equal(fixture.schema, schema, 'Wrong Run memory schema');
  assert.equal(fixture.status, status, 'Run memory status must stay JS_ORACLE_ONLY');
  assert.equal(fixture.scope, scope, 'Run memory scope differs');
  assert.deepEqual(fixture.sources, loadedSources, 'Run memory source provenance stale');
  const node = runtimeNode(fixture.nativeDiagnosticsRuntime);
  assert.ok(Array.isArray(fixture.bases) && Array.isArray(fixture.scenarios), 'Run memory fixture requires bases and scenarios');
  assert.deepEqual(fixture.bases.map(({ id, memoryPolicy, role }) => ({ id, memoryPolicy, role })), basePlan.map(({ id, memoryPolicy, role }) => ({ id, memoryPolicy, role })),
    'Run memory base identities omitted/reordered/changed');
  assert.deepEqual(fixture.scenarios.map(({ id, base, at, role }) => ({ id, base, at, role })), scenarioPlan.map(({ id, base, at, role }) => ({ id, base, at, role })),
    'Run memory scenario identities omitted/reordered/changed');
  fixture.bases.forEach((b, i) => validateBase(b, basePlan[i]));
  fixture.scenarios.forEach(sc => validateScenario(sc, fixture.bases));
  assert.ok(isDeepStrictEqual(fixture.coverage, summarize(fixture.bases, fixture.scenarios)), 'Run memory coverage/counts differ from the recorded ops');
  // Every record (bases, points, arguments, returns, errors, coverage) is compared with fresh actual execution; only the
  // validated generating Node release may differ, and a differing V8 native message then fails here as it should.
  const expected = { ...generateCases(), nativeDiagnosticsRuntime: { node } };
  assert.ok(isDeepStrictEqual(fixture, expected) && JSON.stringify(fixture) === JSON.stringify(expected), 'Run memory data differ from fresh actual execution');
  return fixture.coverage.totals;
}

export function exportVectors(output = fixturePath) {
  const fixture = generateCases(), text = JSON.stringify(fixture), json = Buffer.from(`${text}\n`);
  const bytes = output.endsWith('.gz') ? gzipSync(json, { level: 9 }) : json;
  assert.equal(JSON.stringify(decodeVectors(bytes)), text, 'Exported Run memory fixture fails the strict reader; previous output preserved');
  const temporary = mkdtempSync(join(dirname(output), '.run-memory-'));
  try {
    const path = join(temporary, 'fixture'); writeFileSync(path, bytes);
    assert.deepEqual(sources(), fixture.sources, 'Run memory sources changed before export; previous output preserved'); renameSync(path, output);
  } finally { rmSync(temporary, { recursive: true, force: true }); }
  return { ...fixture.coverage.totals, bytes: bytes.length, uncompressedBytes: json.length, sha256: sha256(bytes), sources: fixture.sources };
}

if (isMain) {
  const args = process.argv.slice(2);
  if (args.length > 1 || (args.length && !['--check', '--help'].includes(args[0]))) throw new Error('Usage: node tools/gen_run_memory_vectors.mjs [--check|--help]');
  if (args[0] === '--help') console.log('Export the actual public Run memory boundary JS oracle (memoryChanges/previewMemory/commitMemory on fresh Creates and real terminal saves), or --check strict source/gzip/JSON and fresh regeneration of every record. JS_ORACLE_ONLY: C# Run memory replay remains 0.');
  else if (args[0] === '--check') console.log(JSON.stringify({ checked: fixturePath, ...checkFixture(decodeVectors(readFileSync(fixturePath))) }));
  else console.log(JSON.stringify({ output: fixturePath, ...exportVectors() }, null, 2));
}
