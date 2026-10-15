// Actual original public commands, for Editor Play Mode full-content save/restart checks.
import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {createHash} from 'node:crypto';
import {fileURLToPath} from 'node:url';
import {resolve} from 'node:path';
import {readFileSync, writeFileSync} from 'node:fs';
import {decodeVectors} from '../../tools/gen_run_create_vectors.mjs';
import {stateHash} from '../../tools/gen_traces.mjs';
const require = createRequire(import.meta.url), MF = require('../../engine/rules-core.js'), data = require('../../data/game_data.json');
const fixture = decodeVectors(readFileSync(new URL('../../engine/tests/vectors/run_progression_vectors.json.gz', import.meta.url)));
export const selected = ['seed1000-short-legacy', 'seed1102-full-legacy', 'seed1102-full-v2'];
export const referencePath = fileURLToPath(new URL('./unity_full_run_reference.json', import.meta.url));
const transcript = run => structuredClone({runLog: run.s.log, battleLog: run.s.battle?.log ?? null, ev: run.s.battle?.ev ?? null});
export function generateReference() {
const cases = []; let queries = 0, restarts = 0;
for (const id of selected) {
  const source = fixture.cases.find(c => c.id === id), setup = structuredClone(source.trace.setup), run = MF.Run.create(data, structuredClone(setup));
  let store = source.memoryPolicy === 'v2' ? MF.MemoryV2.migrate(data, {}, {}) : null;
  const hash = () => stateHash({run: run.s, memory: store});
  assert.equal(hash(), source.created.stateHash);
  const value = {id, setup, initial: hash(), initialTranscript: transcript(run), outcome: source.trace.summary.outcome, steps: [], reload: null};
  for (const step of source.steps) {
    const command = structuredClone(step.command), row = {command, outcome: step.outcome, code: step.errorCode, hash: null};
    if (command.kind === 'commitMemory') {
      const before = JSON.stringify({run: run.s, memory: store});
      row.changes = structuredClone(run.memoryChanges()); queries++;
      if (store) { row.preview = structuredClone(run.previewMemory(command.args[0].selectedChangeIds)); queries++; }
      assert.equal(JSON.stringify({run: run.s, memory: store}), before, 'source memory queries must be pure');
    }
    let code = null, returned;
    try {
      if (command.scope === 'battle') returned = run.battleCmd(command.kind, command.args);
      else if (command.kind === 'commitMemory') {
        if (store) assert.deepEqual(command.args[1], store);
        returned = run.commitMemory(command.args[0], store);
        if (store) store = returned.state;
      } else returned = run[command.kind](...command.args);
    } catch (e) { if (!(e instanceof MF.RuleError)) throw e; code = e.code; }
    assert.equal(code, step.errorCode);
    row.result = code ? null : returned === undefined ? {kind: 'undefined'} : {kind: 'json', value: structuredClone(returned)};
    if (!code) {
      assert.equal(row.result.kind, step.result.kind);
      if (row.result.kind === 'json') {
        row.result.value = JSON.parse(JSON.stringify(row.result.value));
        assert.deepEqual(row.result.value, step.result.value); row.result.undefinedPaths = step.result.undefinedPaths;
      }
    }
    row.transcript = transcript(run); row.hash = hash(); assert.equal(row.hash, step.after.stateHash); value.steps.push(row);
  }
  assert.equal(run.s.phase, 'DONE');
  const reload = {...structuredClone(setup), memory: structuredClone(run.s.savedMemory), seed: (setup.seed + 99) >>> 0,
    ...(store ? {runId: setup.runId + '-reload', memoryVersion: store.version} : {})};
  const restarted = MF.Run.create(data, reload);
  value.reload = stateHash({run: restarted.s, memory: store}); value.reloadTranscript = transcript(restarted); restarts++;
  assert.equal(value.reload, source.reload.created.stateHash); cases.push(value);
}
const source = {path: 'reports/run_memory_core_20261016/unity_full_run_reference_generator.mjs', sha256: createHash('sha256').update(readFileSync(fileURLToPath(import.meta.url))).digest('hex')};
return {schema: 'mf-unity-full-run-v1', sources: fixture.sources, source, selected, cases,
  counts: {cases: cases.length, actualCommands: cases.reduce((n, c) => n + c.steps.length, 0), sourceMemoryQueries: queries, actualSaveDerivedRestarts: restarts}};
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const result = generateReference(), text = JSON.stringify(result) + '\n';
  if (process.argv[2] === '--check') assert.equal(readFileSync(referencePath, 'utf8'), text, 'Unity reference differs from fresh public JS execution');
  else writeFileSync(referencePath, text);
  console.log(JSON.stringify(result.counts));
}
