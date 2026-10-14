import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const MF = require('../rules-core.js');
const DATA = require('../../data/game_data.json');
const candidates = require('../../proposals/late_enemies.cjs');
const clone = value => JSON.parse(JSON.stringify(value));
function battle(enemy = {}) {
  const data = clone(DATA);
  data.enemies.push({ id: 'edge_enemy', name: '시험 적', tier: 'boss', hp: 100, brk: 20, weak: ['frost'], pattern: [{ t: 'charge' }], ...enemy });
  const b = MF.Battle.create(data, { party: [{ charId: 'sera' }], deck: Array.from({ length: 10 }, () => ({ id: 'sera_round' })),
    hp: 200, maxHp: 200, enemies: ['edge_enemy'], seed: 2 });
  return b;
}
test('no silence surcharge exists before the first player turn', () => {
  const b = battle();
  assert.equal(b.costOf(b.s.zones.draw[0], 'next'), 1);
});
test('next AP preview and actual start use the same bonus, debt and drain calculation', () => {
  const b = battle(); b.do('begin');
  b.s.apNext = 4; b.s.apDrainNext = 5; b.s.debts.push({ due: b.s.turn + 1, n: 2 });
  const before = clone(b.s);
  assert.equal(b.nextTurnAp(), 1);
  assert.deepEqual(b.s, before);
  b.do('end');
  assert.equal(b.s.ap, 1);
});
test('AP forecast includes the visible drain once and excludes a stunned enemy', () => {
  const b = battle({ pattern: [{ t: 'apDrain', n: 2 }] }); b.do('begin');
  b.s.debts.push({ due: 2, n: 1 });
  const before = clone(b.s);
  assert.equal(b.nextTurnDrain(), 2);
  assert.equal(b.nextTurnAp(), 1);
  assert.deepEqual(b.s, before);
  b.alive()[0].stunned = true;
  assert.equal(b.nextTurnDrain(), 0);
  assert.equal(b.nextTurnAp(), 3);
  b.alive()[0].stunned = false;
  b.do('end');
  assert.equal(b.s.ap, 1);
});
test('delay preview is pure and matches the actual discard decision', () => {
  const b = battle(); b.do('begin');
  assert.equal(b.previewDelay(), null);
  const uid = b.s.zones.hand[0]; b.do('play', { uid, mode: 'next', target: b.alive()[0].uid });
  b.s.sealedUntil = 2;
  b.s.slotsBase.next = 1;
  const before = clone(b.s);
  const preview = b.previewDelay();
  assert.deepEqual(preview, { uid, from: 2, to: 3, kind: 'next', discarded: true });
  assert.deepEqual(b.s, before);
  b.enemyMove(b.alive()[0], { t: 'delayRes' });
  assert.deepEqual(b.s.ev.at(-1), { e: 'delayRes', ...preview });
  assert.ok(b.s.zones.discard.includes(uid));
});
test('an ordinary after reservation is not marked as enemy-delayed when its column rolls forward', () => {
  const b = battle(); b.do('begin');
  const uid = b.s.zones.hand[0]; b.do('play', { uid, mode: 'after', target: b.alive()[0].uid });
  b.do('end');
  const r = b.s.res.find(x => x.uid === uid);
  assert.equal(r.due, b.s.turn + 1);
  assert.equal(r.kind, 'after');
  assert.ok(!r.delayed);
});
test('successful enemy delay carries an explicit marker independently of the value kind', () => {
  const b = battle(); b.do('begin');
  const uid = b.s.zones.hand[0]; b.do('play', { uid, mode: 'next', target: b.alive()[0].uid });
  b.enemyMove(b.alive()[0], { t: 'delayRes' });
  assert.equal(b.s.res[0].delayed, true);
  assert.equal(b.s.res[0].kind, 'next');
});
test('player pull clears the enemy-delay label when it changes the reservation power kind', () => {
  const b = battle(); b.do('begin');
  const uid = b.s.zones.hand[0]; b.do('play', { uid, mode: 'after', target: b.alive()[0].uid });
  b.enemyMove(b.alive()[0], { t: 'delayRes' });
  b.execOp({ op: 'pull' }, { picks: [uid] });
  assert.equal(b.s.res[0].kind, 'next');
  assert.equal(b.s.res[0].delayed, undefined);
});
test('a malformed phase discriminator cannot leave a living zero-HP boss', () => {
  const b = battle({ hp: 1, phase: { t: 'charge', hp: 10, brk: 2, weak: ['volt'], pattern: [{ t: 'charge' }] } }); b.do('begin');
  const before = clone(b.s);
  assert.throws(() => b.do('play', { uid: b.s.zones.hand[0], mode: 'now', target: b.alive()[0].uid }), { code: 'INVALID_ENEMY_MOVE' });
  assert.deepEqual(b.s, before);
});
test('counter-triggered phase change ends the old enemy multihit action', () => {
  const b = battle({ hp: 1, pattern: [{ t: 'atk', d: 10, h: 3 }],
    phase: { t: 'phase', hp: 100, brk: 20, weak: ['volt'], pattern: [{ t: 'charge' }] } }); b.do('begin');
  b.s.counter = 1; b.do('end');
  assert.equal(b.s.hp, 190);
  assert.equal(b.alive()[0].phaseIndex, 1);
  assert.equal(b.alive()[0].hp, 100);
});
test('candidate overlay preserves all original data and exact declared boss budgets', () => {
  const original = JSON.stringify(DATA); const data = candidates.withLateEnemies(DATA);
  assert.equal(JSON.stringify(DATA), original);
  assert.equal(DATA.enemies.length, 20);
  assert.equal(data.enemies.length, 43);
  assert.deepEqual(data.enemies.slice(0, 20), DATA.enemies);
  const byId = Object.fromEntries(data.enemies.map(e => [e.id, e]));
  assert.deepEqual(['b_usurer', 'b_overture'].map(id => [byId[id].hp, byId[id].brk]), [[260, 13], [290, 14]]);
  assert.equal(byId.b_nameless.hp + byId.b_nameless.phase.hp, 340);
  const groups = Object.values(data.encounters).flatMap(z => [...z.normal, ...z.elite, ...z.boss]);
  assert.equal(groups.length, 44);
  assert.ok(groups.every(g => g.length <= 5 && g.every(id => byId[id])));
});
