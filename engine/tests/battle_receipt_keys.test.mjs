// §5.15: only stored request IDs replay; rejected commands do not reserve an ID.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const MF = require('../rules-core.js');
const DATA = require('../../data/game_data.json');
const clone = value => JSON.parse(JSON.stringify(value));
const requestIds = ['ordinary-request', ...Object.getOwnPropertyNames(Object.prototype)];

function battle() {
  return MF.Battle.create(DATA, {
    party: [{ charId: 'sera' }], deck: Array.from({ length: 10 }, () => ({ id: 'sera_round' })),
    hp: 81, maxHp: 81, seed: 7, enemies: ['e_shelf'],
  });
}

for (const rid of requestIds) {
  test(`request ID ${JSON.stringify(rid)} stores and replays a detached response after version advance`, () => {
    const b = battle();
    const receiptPrototype = Object.getPrototypeOf(b.receipts);
    const objectPrototype = Object.getOwnPropertyDescriptors(Object.prototype);
    const request = { rid, v: 0, kind: 'begin', args: {} };
    const first = b.command(request);
    const expected = clone(first);
    assert.equal(first.v, 1);
    assert.equal(first.phase, 'PLAYER');
    assert.equal(Object.getPrototypeOf(b.receipts), receiptPrototype);
    assert.deepEqual(Object.getOwnPropertyDescriptors(Object.prototype), objectPrototype);
    assert.ok(Object.hasOwn(b.receipts, rid));
    assert.deepEqual(Object.keys(b.receipts), [rid]);

    const stored = clone(b.receipts);
    assert.ok(Object.hasOwn(stored, rid), 'receipt survives JSON storage as an own property');
    assert.deepEqual(stored[rid].resp, expected);
    b.do('end');
    assert.equal(b.s.v, 2);
    const state = clone(b.s);
    const receipts = clone(b.receipts);

    assert.ok(first.ev.length > 0);
    first.ev[0].e = 'caller-edited-first';
    first.ev.push({ e: 'caller-added-first' });
    const replay = b.command(clone(request));
    assert.deepEqual(replay, expected);
    replay.ev[0].e = 'caller-edited-replay';
    replay.ev.length = 0;
    assert.deepEqual(b.command(clone(request)), expected);
    assert.throws(() => b.command({ ...request, kind: 'end' }), { code: 'IDEMPOTENCY_CONFLICT' });
    assert.deepEqual(b.s, state);
    assert.deepEqual(b.receipts, receipts);
    assert.equal(Object.getPrototypeOf(b.receipts), receiptPrototype);
    assert.deepEqual(Object.getOwnPropertyDescriptors(Object.prototype), objectPrototype);

    b.receipts = clone(b.receipts);
    assert.ok(Object.hasOwn(b.receipts, rid));
    assert.deepEqual(b.command(clone(request)), expected);
    assert.deepEqual(b.s, state);
  });

  test(`rejected request ID ${JSON.stringify(rid)} remains available for a corrected command`, () => {
    const b = battle();
    const state = clone(b.s);
    const receipts = clone(b.receipts);
    const request = { rid, v: 0, kind: 'unknown-command', args: {} };
    assert.throws(() => b.command(request), { code: 'UNKNOWN_COMMAND' });
    assert.deepEqual(b.s, state);
    assert.deepEqual(b.receipts, receipts);
    assert.ok(!Object.hasOwn(b.receipts, rid));

    const response = b.command({ ...request, kind: 'begin' });
    assert.equal(response.v, 1);
    assert.equal(response.phase, 'PLAYER');
    assert.ok(Object.hasOwn(b.receipts, rid));
  });
}
