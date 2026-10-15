// R1a §7/8/16.2/20.9 P1-1: actual Run.create → enter → Battle → rewards → node services → archive → defeat/clear → memory,
// recorded as complete JS states, returns, errors and pure queries for five fixed gen_traces scenarios.
// JS_ORACLE_ONLY: no C# Run command has replayed it. The 1000-trace hash corpus and the R0 factory matrix stay separate evidence.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { mkdtempSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { isDeepStrictEqual } from 'node:util';
import { gzipSync } from 'node:zlib';
import { generateScenario, replayScenario, stateHash } from './gen_traces.mjs';
import { decodeVectors } from './gen_run_create_vectors.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const require = createRequire(import.meta.url), MF = require('../engine/rules-core.js');
const data = JSON.parse(readFileSync(join(root, 'data/game_data.json'), 'utf8')), originalData = JSON.stringify(data);
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const schema = 'mf-run-progression-v1';
const status = 'JS_ORACLE_ONLY';
const scope = 'Actual Run.create and the recorded public Run/battle commands of five fixed gen_traces scenarios: complete JS states, returns, errors and pure queries. C# Run command replay 0; restoration, abandonment, persistence, server, Unity and device acceptance PENDING';
const isMain = process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url);
export const fixturePath = join(root, 'engine/tests/vectors/run_progression_vectors.json.gz');

// Fixed real generator inputs. Roles are asserted against actual observations; nothing is restored from trace snapshots.
const plan = [
  { id: 'seed1000-short-legacy', seed: 1000, length: 'short', memoryPolicy: 'legacy', role: 'ordinary-exploration' },
  { id: 'seed1000-full-legacy', seed: 1000, length: 'full', memoryPolicy: 'legacy', role: 'ordinary-exploration' },
  { id: 'seed1102-full-legacy', seed: 1102, length: 'full', memoryPolicy: 'legacy', role: 'begin-defeat' },
  { id: 'seed1102-full-v2', seed: 1102, length: 'full', memoryPolicy: 'v2', role: 'begin-defeat' },
  { id: 'seed2654-full-legacy', seed: 2654, length: 'full', memoryPolicy: 'legacy', role: 'earned-ap-bonus' },
];
// The public Run surface gen_traces dispatches; its dispatcher is not exported and the canonical source hash pins it.
const runMethods = new Set(['enter', 'evolve', 'takeCard', 'skipCard', 'takeRelic', 'takePart', 'applyFoundPart',
  'proceed', 'rest', 'forgeOffer', 'forgeApply', 'forgeRemove', 'buy', 'eventChoose', 'archive', 'commitMemory']);
const battleNodes = new Set(['battle', 'elite', 'boss']);
const topLevel = ['schema', 'status', 'scope', 'dataVersion', 'sources', 'contract', 'denominators', 'coverage', 'cases'];
const contract = {
  context: 'Each full point is {run: Run.s, memory: v2 store or null} as ordered JSON including log, ev, battle and RNG. stateHash is the gen_traces canonical hash (log/ev omitted) and equals the trace hash; jsonHash is SHA-256 of the ordered JSON text; undefinedPaths lists own members or array items holding undefined (JSON omits the member or writes null)',
  steps: 'Trace commands applied from a fresh Run.create through public Run methods or run.battleCmd; a v2 commitMemory return supplies the next store. before references the current point (ref -1 = created, else steps[ref].after); committed steps carry the full after point; rejected steps reference the unchanged point, asserted JSON-identical',
  results: 'Return captured at call time: {kind:"undefined"} or {kind:"json",value,jsonHash,undefinedPaths}. Rejected steps have result null and error {kind:"RuleError",name,code,message,detail}; only the RuleError code the trace recorded is accepted and any other exception aborts generation',
  queries: 'available(), nodeById(pos, or the first floor-0 node while pos is null) and, during battle, battle.publicView() after Create, after every committed Run command and after each battle command that starts a turn or ends the battle; each is asserted to leave the complete JSON and undefinedPaths unchanged',
  visibility: 'Private test oracle: full states expose draw order and RNG. battle.publicView() is the actual source API return and is not certified here as a safe client view',
  runtimeOnly: 'Battle receipts live outside Run.s and are not captured; battle responses are recorded as results',
  reload: 'gen_traces saveMemory rebuild check recorded as Run.create only: saved memory, seed+99, v2 reload runId and committed store version',
  separate: 'The existing 1000-trace hash corpus and the R0 factory/query matrix are separate evidence; this fixture does not claim full C# replay',
};

function sources() {
  return Object.fromEntries(Object.entries({ data: 'data/game_data.json', engine: 'engine/rules-core.js', memory: 'proposals/memory_v2.cjs',
    canonical: 'tools/gen_traces.mjs', fuzzPolicy: 'engine/tests/fuzz_runs.mjs', runCreateDecoder: 'tools/gen_run_create_vectors.mjs', generator: 'tools/gen_run_progression_vectors.mjs',
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
const live = context => ({ run: context.run.s, memory: context.memory });
function point(context) {
  const json = JSON.stringify(live(context)), state = JSON.parse(json);
  return { state, stateHash: stateHash(state), jsonHash: sha256(json), undefinedPaths: undefinedPaths(live(context)) };
}
const ref = (index, p) => ({ ref: index, stateHash: p.stateHash, jsonHash: p.jsonHash });
const nodeOf = (run, id) => [...run.map.floors.flat(), run.map.boss].find(n => n.id === id);

// Pure source queries at a boundary; any change to the complete JSON (RNG/log included) or undefined members aborts generation.
function queries(context) {
  const run = context.run, out = [], node = run.s.pos ?? run.s.map.floors[0][0].id;
  const json = JSON.stringify(live(context)), absent = JSON.stringify(undefinedPaths(live(context)));
  const ask = (method, args, call) => {
    const result = encode(call());
    assert.ok(JSON.stringify(live(context)) === json && JSON.stringify(undefinedPaths(live(context))) === absent, `${method} changed complete state/RNG/log`);
    out.push({ method, args, result });
  };
  ask('available', [], () => run.available());
  ask('nodeById', [node], () => run.nodeById(node));
  if (run.battle) ask('battle.publicView', [], () => run.battle.publicView());
  return out;
}

function dispatch(context, command) {
  const run = context.run;
  if (command.scope === 'battle') {
    assert.ok(run.battle && command.v === run.battle.s.v && command.rid === `r${run.battle.s.v}-${command.kind}` && ['play', 'ult', 'end'].includes(command.kind),
      'Battle command envelope differs from the live battle');
    return run.battleCmd(command.kind, clone(command.args));
  }
  assert.ok(command.scope === 'run' && runMethods.has(command.kind) && Array.isArray(command.args), `Unknown Run command ${command.kind}`);
  const result = run[command.kind](...clone(command.args));
  if (command.kind === 'commitMemory' && run.s.memoryPolicy === 'v2') context.memory = result.state;
  return result;
}

function captureCase(spec) {
  const { id, seed, length, memoryPolicy } = spec, S = loadedSources;
  const trace = generateScenario({ seed, length, memoryPolicy });
  replayScenario(trace); // established hash replay: throws on any outcome/code/hash drift
  assert.deepEqual(trace.sources, { data: S.data, engine: S.engine, memory: S.memory, generator: S.canonical, fuzzPolicy: S.fuzzPolicy }, `${id}: trace provenance differs`);
  const context = { run: MF.Run.create(data, clone(trace.setup)), memory: memoryPolicy === 'v2' ? MF.MemoryV2.migrate(data, {}, {}) : null };
  let current = point(context), currentRef = -1;
  assert.equal(current.stateHash, trace.initialStateHash, `${id}: initial state hash differs from trace`);
  const row = { ...spec, trace: { schema: trace.schema, policy: trace.policy, setup: trace.setup, initialStateHash: trace.initialStateHash,
    finalStateHash: trace.finalStateHash, commandCount: trace.commands.length, summary: trace.summary }, created: { ...current, queries: queries(context) }, steps: [] };
  for (const [index, expected] of trace.commands.entries()) {
    const where = `${id}#${index}`, command = clone(expected.command), before = ref(currentRef, current), turn = context.run.s.battle?.turn;
    let result, error = null;
    try { result = dispatch(context, command); }
    catch (e) {
      // Only the RuleError the trace itself recorded is a rejection; anything else aborts and preserves prior output.
      if (!(e instanceof MF.RuleError) || expected.outcome !== 'rejected' || e.code !== expected.errorCode) throw e;
      error = { kind: 'RuleError', name: e.name, code: e.code, message: e.message, detail: encode(e.detail) };
    }
    assert.equal(error ? 'rejected' : 'committed', expected.outcome, `${where}: outcome differs from trace`);
    const step = { index, command, outcome: expected.outcome, errorCode: error ? error.code : null, error, result: error ? null : encode(result), before };
    if (error) {
      assert.ok(sha256(JSON.stringify(live(context))) === current.jsonHash && isDeepStrictEqual(undefinedPaths(live(context)), current.undefinedPaths),
        `${where}: rejection changed complete state`);
      Object.assign(step, { after: { ...before }, queries: [] });
    } else {
      current = point(context); currentRef = index;
      const battle = context.run.s.battle;
      Object.assign(step, { after: current, queries: command.scope === 'run' || !battle || battle.turn !== turn ? queries(context) : [] });
    }
    assert.equal(step.after.stateHash, expected.stateHash, `${where}: state hash differs from trace`);
    row.steps.push(step);
  }
  assert.equal(context.run.s.phase, 'DONE', `${id}: no terminal memory commit`);
  assert.equal(current.stateHash, trace.finalStateHash, `${id}: final state hash differs from trace`);
  // gen_traces saveMemory's rebuild check, recorded: committed memory (and the v2 store version) feeds a fresh Create.
  const reload = { ...clone(trace.setup), memory: clone(context.run.s.savedMemory), seed: (seed + 99) >>> 0,
    ...(memoryPolicy === 'v2' ? { runId: `${trace.setup.runId}-reload`, memoryVersion: context.memory.version } : {}) };
  row.reload = { setup: reload, created: point({ run: MF.Run.create(data, clone(reload)), memory: context.memory }) };
  assert.equal(JSON.stringify(data), originalData, `${id}: source definitions changed`);
  return row;
}

// Coverage is derived only from recorded steps, so check mode recomputes it instead of trusting stored counts.
function summarize(cases) {
  const methods = {}, phases = {}, rejections = {}, queryCounts = {}, terminalOutcomes = {}, perCase = [];
  const events = { beginEndedBattles: [], apBonusEarned: [], apBonusConsumed: [] };
  const add = (map, key) => { map[key] = (map[key] || 0) + 1; };
  for (const c of cases) {
    let state = c.created.state, maxBattleTurn = 0;
    for (const q of c.created.queries) add(queryCounts, q.method);
    for (const step of c.steps) {
      const { scope, kind, args } = step.command, key = `${scope}:${kind}`, before = state.run;
      const tally = methods[key] ||= { attempted: 0, committed: 0, rejected: 0 };
      tally.attempted++; tally[step.outcome]++;
      add(phases, before.phase);
      if (step.errorCode) add(rejections, `${key}:${step.errorCode}`);
      for (const q of step.queries) add(queryCounts, q.method);
      if (step.outcome !== 'committed') continue;
      state = step.after.state; const after = state.run;
      maxBattleTurn = Math.max(maxBattleTurn, after.battle?.turn || 0);
      if (kind === 'eventChoose' && after.runFlags.nextApBonus > before.runFlags.nextApBonus)
        events.apBonusEarned.push({ caseId: c.id, step: step.index, eventId: before.pending.id, choice: args[0], nextApBonus: after.runFlags.nextApBonus });
      const node = kind === 'enter' ? nodeOf(before, args[0]) : null;
      if (!node || !battleNodes.has(node.type)) continue;
      if (before.runFlags.nextApBonus) events.apBonusConsumed.push({ caseId: c.id, step: step.index, carried: before.runFlags.nextApBonus, firstTurnAp: after.battle ? after.battle.ap : null });
      if (after.phase !== 'BATTLE') events.beginEndedBattles.push({ caseId: c.id, step: step.index, nodeId: node.id, type: node.type, phase: after.phase });
    }
    const run = state.run, count = outcome => c.steps.filter(s => s.outcome === outcome).length;
    add(terminalOutcomes, run.result.outcome);
    perCase.push({ id: c.id, attempted: c.steps.length, committed: count('committed'), rejected: count('rejected'),
      battleCommands: c.steps.filter(s => s.command.scope === 'battle').length, finalPhase: run.phase, outcome: run.result.outcome, floor: run.result.floor,
      zonesVisited: run.zoneIdx + 1, battles: run.stats.battles, maxBattleTurn, storeVersion: state.memory ? state.memory.version : null });
  }
  const sum = field => perCase.reduce((n, c) => n + c[field], 0);
  return { totals: { cases: cases.length, attempted: sum('attempted'), committed: sum('committed'), rejected: sum('rejected'),
    queries: Object.values(queryCounts).reduce((n, k) => n + k, 0), csharpCommandReplay: 0 },
  perCase, methods, rejections, phases, queries: queryCounts, terminalOutcomes, events };
}

export function generateCases() {
  assert.deepEqual(sources(), loadedSources, 'Run progression sources changed after load; rerun');
  const cases = plan.map(captureCase), coverage = summarize(cases), { events } = coverage;
  for (const [i, c] of cases.entries()) {
    const seen = coverage.perCase[i], trace = c.trace.summary, mine = e => e.caseId === c.id;
    assert.deepEqual([seen.outcome, seen.committed, seen.rejected, seen.zonesVisited], [trace.outcome, trace.committed, trace.rejected, trace.visitedZones], `${c.id}: observed totals differ from trace summary`);
    if (c.role === 'begin-defeat') assert.ok(events.beginEndedBattles.some(e => mine(e) && e.phase === 'DEFEAT'), `${c.id}: required begin-defeat was not observed`);
    if (c.role === 'earned-ap-bonus') assert.ok(events.apBonusEarned.some(mine) && events.apBonusConsumed.some(mine), `${c.id}: required earned and consumed AP bonus was not observed`);
  }
  assert.deepEqual(sources(), loadedSources, 'Run progression sources changed during generation; rerun');
  const count = key => Object.fromEntries([...new Set(plan.map(p => p[key]))].map(v => [v, plan.filter(p => p[key] === v).length]));
  return { schema, status, scope, dataVersion: data.meta.version, sources: clone(loadedSources), contract: clone(contract),
    denominators: { cases: plan.length, seeds: [...new Set(plan.map(p => p.seed))], policies: count('memoryPolicy'), lengths: count('length') }, coverage, cases };
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
function validateCase(c) {
  const results = (list, where) => list.forEach(q => validateResult(q.result, `${where} ${q.method}`));
  validatePoint(c.created, `${c.id} created`); results(c.created.queries, `${c.id} created`);
  assert.equal(c.created.stateHash, c.trace.initialStateHash, `${c.id}: created hash differs from trace`);
  assert.equal(c.steps.length, c.trace.commandCount, `${c.id}: step omitted or added`);
  let current = c.created, currentRef = -1;
  for (const [index, step] of c.steps.entries()) {
    const where = `${c.id}#${index}`;
    assert.equal(step.index, index, `${where}: step index`);
    assert.deepEqual(step.before, ref(currentRef, current), `${where}: before reference`);
    if (step.outcome === 'committed') {
      assert.ok(step.errorCode === null && step.error === null && step.result, `${where}: committed step error/result`);
      validateResult(step.result, where); validatePoint(step.after, where); current = step.after; currentRef = index;
    } else {
      assert.ok(step.outcome === 'rejected' && step.error?.kind === 'RuleError' && step.error.code === step.errorCode && step.result === null && !step.queries.length,
        `${where}: rejected step error/result`);
      validateResult(step.error.detail, where);
      assert.deepEqual(step.after, step.before, `${where}: rejected step must reference the unchanged state`);
    }
    results(step.queries, where);
  }
  assert.equal(current.stateHash, c.trace.finalStateHash, `${c.id}: final hash differs from trace`);
  assert.equal(current.state.run.phase, 'DONE', `${c.id}: final phase is not DONE`);
  validatePoint(c.reload.created, `${c.id} reload`);
}

export function checkFixture(fixture) {
  assert.deepEqual(Object.keys(fixture), topLevel, 'Run progression fixture fields differ');
  assert.equal(fixture.schema, schema, 'Wrong run-progression schema');
  assert.equal(fixture.status, status, 'Run progression status must stay JS_ORACLE_ONLY');
  assert.equal(fixture.scope, scope, 'Run progression scope differs');
  assert.deepEqual(fixture.sources, loadedSources, 'Run progression source provenance stale');
  assert.ok(Array.isArray(fixture.cases), 'Run progression fixture requires cases');
  assert.deepEqual(fixture.cases.map(({ id, seed, length, memoryPolicy, role }) => ({ id, seed, length, memoryPolicy, role })), plan, 'Run progression case identities omitted/reordered/changed');
  fixture.cases.forEach(validateCase);
  assert.ok(isDeepStrictEqual(fixture.coverage, summarize(fixture.cases)), 'Run progression coverage/counts differ from the recorded steps');
  // Every record (states, returns, errors, queries, reload, coverage) is compared with fresh actual execution; nothing is skipped.
  const expected = generateCases();
  assert.ok(isDeepStrictEqual(fixture, expected) && JSON.stringify(fixture) === JSON.stringify(expected), 'Run progression data differ from fresh actual execution');
  return fixture.coverage.totals;
}

export function exportVectors(output = fixturePath) {
  const fixture = generateCases(), text = JSON.stringify(fixture), json = Buffer.from(`${text}\n`);
  const bytes = output.endsWith('.gz') ? gzipSync(json, { level: 9 }) : json;
  assert.equal(JSON.stringify(decodeVectors(bytes)), text, 'Exported fixture fails the strict reader; previous output preserved');
  const temporary = mkdtempSync(join(dirname(output), '.run-progression-'));
  try {
    const path = join(temporary, 'fixture'); writeFileSync(path, bytes);
    assert.deepEqual(sources(), fixture.sources, 'Run progression sources changed before export; previous output preserved'); renameSync(path, output);
  } finally { rmSync(temporary, { recursive: true, force: true }); }
  return { ...fixture.coverage.totals, bytes: bytes.length, uncompressedBytes: json.length, sha256: sha256(bytes), sources: fixture.sources };
}

if (isMain) {
  const args = process.argv.slice(2);
  if (args.length > 1 || (args.length && !['--check', '--help'].includes(args[0]))) throw new Error('Usage: node tools/gen_run_progression_vectors.mjs [--check|--help]');
  if (args[0] === '--help') console.log('Export the actual five-scenario Run command full-state JS oracle (R1a), or --check strict source/gzip/JSON and fresh replay of every record. JS_ORACLE_ONLY: C# Run command replay remains 0.');
  else if (args[0] === '--check') console.log(JSON.stringify({ checked: fixturePath, ...checkFixture(decodeVectors(readFileSync(fixturePath))) }));
  else console.log(JSON.stringify({ output: fixturePath, ...exportVectors() }, null, 2));
}
