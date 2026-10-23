import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const MF = require('../rules-core.js');
const DATA = require('../../data/game_data.json');
const clone = value => JSON.parse(JSON.stringify(value));
function fixture(useMemory = false) {
  const state = MF.MemoryV2.migrate(DATA, { yuri: MF.Run.baseKit(MF.indexData(DATA), 'yuri') }, { yuri: 6 });
  const cocoon = state.memory.yuri.find(c => c.id === 'yuri_cocoon');
  cocoon.id = 'yuri_cocoon_guard'; cocoon.from = 'yuri_cocoon'; cocoon.plus = true;
  const run = MF.Run.create(DATA, { party: [{ charId: 'yuri' }], memoryPolicy: 'v2', memory: state.memory, runId: 'edges', useMemory });
  return { run, state, cocoon };
}
function end(run) { run.s.phase = 'DEFEAT'; run.s.pending = { type: 'save', defeat: true }; run.captureMemory(); }
test('baseline play keeps an existing evolved and upgraded memory without a reverse evolution', () => {
  const { run, state } = fixture(); end(run);
  assert.deepEqual(run.previewMemory([]).memory, state.memory);
  assert.deepEqual(run.memoryChanges().yuri.changes, []);
});
test('baseline play saves a new change without replacing the old evolved instance', () => {
  const { run, state, cocoon } = fixture();
  const played = run.s.deck.find(c => c.id === 'yuri_cocoon');
  run.s.phase = 'REST'; run.s.pending = { type: 'rest', uses: 1 }; run.rest('upgrade', played.key); end(run);
  const change = run.memoryChanges().yuri.changes.find(c => c.key === played.key);
  assert.equal(change.kind, 'add');
  const result = run.previewMemory([change.id]);
  assert.equal(result.error, null);
  assert.deepEqual(result.memory.yuri.find(c => c.key === cocoon.key), cocoon);
  assert.equal(result.memory.yuri.find(c => c.key === played.key).plus, true);
  assert.equal(state.memory.yuri.length + 1, result.memory.yuri.length);
});
test('a part attached after terminal capture is included in the actual save delta', () => {
  const { run } = fixture(true); end(run);
  const card = run.s.deck.find(c => c.id === 'yuri_dew');
  run.s.partsFound.push('c_delay'); run.applyFoundPart('c_delay', card.key);
  const change = run.memoryChanges().yuri.changes.find(c => c.key === card.key && c.kind === 'part');
  assert.ok(change);
  assert.equal(run.previewMemory([change.id]).memory.yuri.find(c => c.key === card.key).mods.c, 'c_delay');
});
test('stable baseline cost distinguishes removing the original from removing its same-kind duplicate', () => {
  const base = MF.MemoryV2.baseline(DATA, 'sera');
  const replaced = [...clone(base.slice(1)), { id: base[0].id, key: 'new-copy' }];
  assert.equal(MF.MemoryV2.cost(DATA, 'sera', replaced, base), 2);
});
test('accepted request IDs are persisted as own receipts and replay after JSON storage', () => {
  const { run, state } = fixture(true); end(run);
  const request = { requestId: '__proto__', runId: run.s.runId, expectedMemoryVersion: 0, selectedChangeIds: [] };
  const first = run.commitMemory(request, state);
  const stored = clone(first.state);
  assert.ok(Object.hasOwn(stored.receipts, request.requestId));
  assert.deepEqual(run.commitMemory(request, stored).response, first.response);
});
