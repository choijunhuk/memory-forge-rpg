import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const here = dirname(fileURLToPath(import.meta.url));
const MF = require(join(here, '..', 'rules-core.js'));
const DATA = JSON.parse(readFileSync(join(here, '..', '..', 'data', 'game_data.json'), 'utf8'));
const copy = x => JSON.parse(JSON.stringify(x));
const party = ['sera', 'harin', 'yuri'].map(charId => ({ charId, res: 0 }));
const capacities = Object.fromEntries(party.map(p => [p.charId, 6]));

function initialMemory() {
  return Object.fromEntries(party.map(p => [p.charId, MF.MemoryV2.baseline(DATA, p.charId)]));
}
function record(memory = initialMemory()) {
  return { schema: 'mf-memory-v2', version: 0, memory: copy(memory), quarantine: {}, legacyBackup: {}, history: [], receipts: {} };
}
let nextRunId = 0;
function run(current = record(), options = {}) {
  return MF.Run.create(DATA, { party, memory: current.memory, memoryPolicy: 'v2', memoryVersion: current.version,
    runId: `integration-${++nextRunId}`, seed: 11, ...options });
}
function finishForSelection(r, defeat = false) {
  // Selection tests isolate the save boundary; the battle-to-defeat test below exercises an actual terminal battle.
  r.s.phase = defeat ? 'DEFEAT' : 'VICTORY';
  r.s.pending = { type: 'save', defeat };
}
function delta(r, cid, kind, key) {
  const found = r.memoryChanges()[cid].changes.find(c => c.kind === kind && (!key || c.key === key));
  assert.ok(found, `expected ${kind} change for ${cid}`);
  return found.id;
}
function commit(r, current, selectedChangeIds, requestId = 'save-1') {
  return r.commitMemory({ requestId, expectedMemoryVersion: current.version, runId: r.s.runId, selectedChangeIds }, current);
}
function winFirstBattle(r) {
  r.enter(r.available()[0]);
  for (let step = 0; r.s.phase === 'BATTLE' && step < 100; step++) {
    const b = r.battle;
    const target = b.s.enemies.find(e => e.alive)?.uid;
    const uid = [...b.s.zones.hand].find(u => b.check(u, 'now', target) === null);
    if (uid) r.battleCmd('play', { uid, mode: 'now', target });
    else r.battleCmd('end');
  }
  assert.equal(r.s.phase, 'REWARD');
}

test('v2 opt-in preserves stable baseline keys across the next run', () => {
  const state = record();
  const first = run(state);
  const keys = first.s.deck.filter(c => !c.weapon && (c.owner === 'sera' || c.assigned === 'sera')).map(c => c.key);
  const second = run(state);
  assert.deepEqual(keys, state.memory.sera.map(c => c.key));
  assert.deepEqual(second.s.deck.filter(c => !c.weapon && (c.owner === 'sera' || c.assigned === 'sera')).map(c => c.key), keys);
});

test('v2 keeps saved before snapshot when useMemory is false', () => {
  const state = record();
  state.memory.sera[0].plus = true;
  const r = run(state, { useMemory: false });
  assert.deepEqual(r.memoryChanges().sera.before, state.memory.sera);
  assert.equal(r.s.deck.find(c => c.key === state.memory.sera[0].key).plus, undefined);
});

test('an actual battle defeat with no selection preserves the prior upgrade', () => {
  const state = record();
  state.memory.sera[0].plus = true;
  const r = run(state, { difficulty: { hp: 100, dmg: 1000 } });
  r.enter(r.available()[0]);
  for (let turn = 0; r.s.phase === 'BATTLE' && turn < 20; turn++) r.battleCmd('end');
  assert.equal(r.s.phase, 'DEFEAT');
  const saved = commit(r, state, []);
  assert.deepEqual(saved.state.memory, state.memory);
  assert.equal(saved.state.version, 1);
});

test('actual defeat rejects two selected upgrades without changing the save state', () => {
  const state = record();
  const r = run(state, { difficulty: { hp: 100, dmg: 1000 } });
  const sera = r.s.deck.find(c => c.id === 'sera_round');
  const harin = r.s.deck.find(c => c.id === 'harin_shield');
  r.s.phase = 'REST'; r.s.pending = { type: 'rest', uses: 2 };
  r.rest('upgrade', sera.key); r.rest('upgrade', harin.key);
  r.s.phase = 'MAP'; r.s.pending = null;
  r.enter(r.available()[0]);
  for (let turn = 0; r.s.phase === 'BATTLE' && turn < 20; turn++) r.battleCmd('end');
  assert.equal(r.s.phase, 'DEFEAT');
  const selected = [delta(r, 'sera', 'upgrade', sera.key), delta(r, 'harin', 'upgrade', harin.key)];
  assert.equal(r.previewMemory(selected).error, 'DEFEAT_KEEP_ONE');
  const before = copy(r.s);
  assert.throws(() => commit(r, state, selected), { code: 'DEFEAT_KEEP_ONE' });
  assert.deepEqual(r.s, before);
});

test('temporary battle token cards are excluded from the persistent after snapshot', () => {
  const r = run();
  r.s.deck.push({ id: 'sera_round', key: 'battle-token', owner: 'sera', token: true });
  assert.equal(r.memorySnapshot().sera.some(c => c.key === 'battle-token'), false);
});

test('normal return accepts multiple selected changes while preserving unselected prior traits', () => {
  const state = record();
  state.memory.sera[0].plus = true;
  const r = run(state);
  const harin = r.s.deck.find(c => c.id === 'harin_shield');
  const yuri = r.s.deck.find(c => c.id === 'yuri_cocoon');
  r.s.phase = 'REST'; r.s.pending = { type: 'rest', uses: 2 };
  r.rest('upgrade', harin.key);
  r.rest('upgrade', yuri.key);
  finishForSelection(r);
  const selected = [delta(r, 'harin', 'upgrade', harin.key), delta(r, 'yuri', 'upgrade', yuri.key)];
  const saved = commit(r, state, selected);
  assert.equal(saved.state.memory.sera[0].plus, true);
  assert.equal(saved.state.memory.harin.find(c => c.key === harin.key).plus, true);
  assert.equal(saved.state.memory.yuri.find(c => c.key === yuri.key).plus, true);
});

test('a real battle reward card appears as an add delta and persists when selected', () => {
  const state = record();
  const r = run(state, { difficulty: { hp: 1, dmg: 1 } });
  winFirstBattle(r);
  const offered = r.s.pending.cards.find(id => DATA.cards.find(c => c.id === id).owner !== 'common');
  assert.ok(offered);
  r.takeCard(offered);
  const gained = r.s.deck.findLast(c => c.id === offered);
  finishForSelection(r);
  const saved = commit(r, state, [delta(r, gained.owner, 'add', gained.key)]);
  assert.equal(saved.state.memory[gained.owner].find(c => c.key === gained.key).id, offered);
});

test('same-kind duplicate card upgrade changes only the selected stable key', () => {
  const state = record();
  state.memory.sera.push({ id: 'sera_round', key: 'sera-duplicate' });
  const r = run(state);
  r.s.phase = 'REST'; r.s.pending = { type: 'rest', uses: 1 };
  r.rest('upgrade', 'sera-duplicate');
  finishForSelection(r);
  const saved = commit(r, state, [delta(r, 'sera', 'upgrade', 'sera-duplicate')]);
  assert.equal(saved.state.memory.sera.find(c => c.key === 'sera-duplicate').plus, true);
  assert.equal(saved.state.memory.sera.find(c => c.key === state.memory.sera[0].key).plus, undefined);
});

test('same-kind duplicate card removal keeps the other stable key', () => {
  const state = record();
  state.memory.sera.push({ id: 'sera_round', key: 'sera-duplicate' });
  const r = run(state);
  r.s.phase = 'FORGE'; r.s.pending = { type: 'forge', uses: 1, offers: null };
  r.forgeRemove('sera-duplicate');
  finishForSelection(r);
  const saved = commit(r, state, [delta(r, 'sera', 'remove', 'sera-duplicate')]);
  assert.equal(saved.state.memory.sera.some(c => c.key === 'sera-duplicate'), false);
  assert.equal(saved.state.memory.sera.some(c => c.key === state.memory.sera[0].key), true);
});

test('choosing only evolution does not import a separate upgrade', () => {
  const state = record();
  const r = run(state);
  const cocoon = r.s.deck.find(c => c.id === 'yuri_cocoon');
  cocoon.growth = 3;
  r.s.difficulty = { hp: 1, dmg: 1 };
  winFirstBattle(r);
  assert.ok(r.s.pending.evolutions.some(e => e.key === cocoon.key));
  r.evolve(cocoon.key, 'yuri_cocoon_guard');
  r.s.phase = 'REST'; r.s.pending = { type: 'rest', uses: 1 };
  r.rest('upgrade', cocoon.key);
  finishForSelection(r);
  const saved = commit(r, state, [delta(r, 'yuri', 'evolve', cocoon.key)]);
  const evolved = saved.state.memory.yuri.find(c => c.key === cocoon.key);
  assert.equal(evolved.id, 'yuri_cocoon_guard');
  assert.equal(evolved.plus, undefined);
});

test('full-result capacity rejects a new S card atomically after six points were saved', () => {
  const state = record();
  state.memory.sera.push({ id: 'sera_phase', key: 'saved-a' }, { id: 'sera_lock', key: 'saved-b' });
  assert.equal(MF.MemoryV2.cost(DATA, 'sera', state.memory.sera), 6);
  const r = run(state, { seed: 6, difficulty: { hp: 1, dmg: 1 } });
  winFirstBattle(r);
  assert.ok(r.s.pending.cards.includes('sera_flash'));
  r.takeCard('sera_flash');
  finishForSelection(r);
  const beforeRun = copy(r.s); const beforeRecord = copy(state);
  assert.throws(() => commit(r, state, [delta(r, 'sera', 'add')]), { code: 'OVER_CAPACITY' });
  assert.deepEqual(r.s, beforeRun);
  assert.deepEqual(state, beforeRecord);
});

test('replacing a part in the same slot charges the full resulting deck', () => {
  const state = record();
  state.memory.sera[0].mods = { c: 'c_delay' };
  const r = run(state);
  r.s.partsFound.push('c_echo');
  r.applyFoundPart('c_echo', state.memory.sera[0].key);
  finishForSelection(r);
  const selected = [delta(r, 'sera', 'part', state.memory.sera[0].key)];
  const preview = r.previewMemory(selected);
  assert.equal(preview.used.sera, 2);
  assert.equal(preview.memory.sera[0].mods.c, 'c_echo');
});

test('same request and body replay returns the recorded response after DONE', () => {
  const state = record(); const r = run(state); finishForSelection(r);
  const dto = { requestId: 'retry-save', expectedMemoryVersion: 0, runId: r.s.runId, selectedChangeIds: [] };
  const first = r.commitMemory(dto, state);
  const retry = r.commitMemory(copy(dto), first.state);
  assert.deepEqual(retry.response, first.response);
  assert.deepEqual(retry.state, first.state);
});

test('a reused request ID with changed selection conflicts without mutation', () => {
  const state = record(); const r = run(state); finishForSelection(r);
  const first = commit(r, state, [], 'same-id');
  const before = copy(first.state);
  assert.throws(() => r.commitMemory({ requestId: 'same-id', expectedMemoryVersion: 0, runId: r.s.runId,
    selectedChangeIds: ['different'] }, first.state), { code: 'REQUEST_CONFLICT' });
  assert.deepEqual(first.state, before);
});

test('stale memory version rejects a new save without mutation', () => {
  const state = record(); const r = run(state); finishForSelection(r);
  const newer = copy(state); newer.version = 1;
  const before = copy(r.s);
  assert.throws(() => commit(r, newer, []), { code: 'VERSION_CONFLICT' });
  assert.deepEqual(r.s, before);
});

test('migration keeps an independent original backup and is deterministic', () => {
  const legacy = { sera: [{ id: 'sera_round', plus: true }], harin: [{ id: 'harin_shield' }] };
  const original = copy(legacy);
  const first = MF.MemoryV2.migrate(DATA, legacy, capacities);
  assert.deepEqual(first, MF.MemoryV2.migrate(DATA, legacy, capacities));
  assert.deepEqual(first.legacyBackup, original);
  assert.deepEqual(legacy, original);
  assert.notStrictEqual(first.legacyBackup, legacy);
});

test('migration quarantines oversized legacy decks without truncating their backup', () => {
  const legacy = { sera: [{ id: 'sera_round' }, { id: 'sera_cover' }, { id: 'sera_aim' },
    { id: 'sera_timed' }, { id: 'sera_phase' }, { id: 'sera_lock' }, { id: 'sera_last' }] };
  const migrated = MF.MemoryV2.migrate(DATA, legacy, capacities);
  assert.equal(migrated.legacyBackup.sera.length, 7);
  assert.deepEqual(migrated.quarantine.sera.original, legacy.sera);
  assert.equal(migrated.memory.sera, undefined);
});
