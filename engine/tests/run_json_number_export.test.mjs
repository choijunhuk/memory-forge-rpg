import assert from 'node:assert/strict';
import test from 'node:test';
import {mkdtempSync, readFileSync, rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {createRequire} from 'node:module';
import {decodeVectors} from '../../tools/gen_run_create_vectors.mjs';
import {checkFixture, exportVectors, generateCases} from '../../tools/gen_run_json_number_vectors.mjs';

test('actual JS numeric strings pin the reproduced Unity receipt failure and IEEE edges', () => {
  const fixture = generateCases(), values = new Map(fixture.cases.map(x => [x.bits, x.json]));
  assert.equal(values.size, 2176);
  assert.equal(values.get('44b52d02c7e14af5'), '9.999999999999997e+22');
  assert.equal(values.get('0000000000000000'), '0'); assert.equal(values.get('8000000000000000'), '0');
  assert.equal(values.get('0000000000000001'), '5e-324');
  assert.equal(values.get('7ff0000000000000'), 'null'); assert.equal(values.get('fff0000000000000'), 'null');
  for (const bits of ['433fffffffffffff', '4340000000000000', '4340000000000001', '0010000000000000', '000fffffffffffff']) assert.ok(values.has(bits));
  assert.deepEqual(checkFixture(fixture), {cases: 2176});
});

test('strict numeric export regenerates every exact-bit input and expected string', () => {
  const directory = mkdtempSync(join(tmpdir(), 'mf-number-json-'));
  try {
    const output = join(directory, 'numbers.gz'); const result = exportVectors(output);
    assert.equal(result.cases, 2176); assert.ok(result.bytes > 0);
    assert.deepEqual(checkFixture(decodeVectors(readFileSync(output))), {cases: 2176});
  } finally { rmSync(directory, {recursive: true, force: true}); }
});

test('actual JS public numeric receipt replay keeps exact strings and rejects both nearby conflicts', () => {
  const require = createRequire(import.meta.url), MF = require('../rules-core.js'), data = require('../../data/game_data.json');
  const view = new DataView(new ArrayBuffer(8)); view.setBigUint64(0, 0x44b52d02c7e14af5n, false);
  const originalVersion = view.getFloat64(0, false), runId = 'numeric-public-witness';
  const body = JSON.stringify({runId, expectedMemoryVersion: originalVersion, selectedChangeIds: []});
  assert.equal(body, '{"runId":"numeric-public-witness","expectedMemoryVersion":9.999999999999997e+22,"selectedChangeIds":[]}');
  for (const mode of ['matching', 'next-double', 'wrong-body']) {
    const run = MF.Run.create(data, {party: ['sera', 'harin', 'nia'].map(charId => ({charId, res: 0})), seed: 1234, length: 'short',
      memoryPolicy: 'v2', memory: {}, runId, memoryVersion: 0});
    const store = MF.MemoryV2.migrate(data, {}, {}), response = {memory: {}, used: {}, version: 0};
    store.receipts['numeric-replay'] = {body: mode === 'wrong-body' ? body.replace('999999999999997', '999999999999998') : body, response};
    view.setBigUint64(0, mode === 'next-double' ? 0x44b52d02c7e14af6n : 0x44b52d02c7e14af5n, false);
    const request = {requestId: 'numeric-replay', runId, expectedMemoryVersion: view.getFloat64(0, false), selectedChangeIds: []};
    const beforeStore = JSON.stringify(store), beforeRun = JSON.stringify(run.s);
    if (mode === 'matching') { assert.deepEqual(run.commitMemory(request, store).response, response); assert.equal(run.s.phase, 'DONE'); }
    else { assert.throws(() => run.commitMemory(request, store), e => e instanceof MF.RuleError && e.code === 'REQUEST_CONFLICT'); assert.equal(JSON.stringify(run.s), beforeRun); }
    assert.equal(JSON.stringify(store), beforeStore);
  }
});

test('numeric evidence rejects substituted sources, a changed value and omitted coverage', () => {
  for (const mutate of [f => { f.source.path = '../unapproved'; }, f => { f.cases[5].json = '0'; }, f => { f.cases.pop(); }]) {
    const fixture = generateCases(); mutate(fixture); assert.throws(() => checkFixture(fixture));
  }
});
