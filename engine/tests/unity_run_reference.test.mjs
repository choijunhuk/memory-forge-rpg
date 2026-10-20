import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {generateReference, referencePath, selected} from '../../reports/run_memory_core_20261016/unity_full_run_reference_generator.mjs';

test('Unity full Run reference freshly binds every state, log, event, result and restart to actual original JS', () => {
  const value = generateReference();
  assert.deepEqual(value.selected, selected);
  assert.deepEqual(value.counts, {cases: 3, actualCommands: 666, sourceMemoryQueries: 4, actualSaveDerivedRestarts: 3});
  assert.equal(readFileSync(referencePath, 'utf8'), JSON.stringify(value) + '\n');
  assert.ok(value.cases.every(c => c.steps.every(s => s.transcript && (s.code === null ? s.result : s.result === null))));
});
