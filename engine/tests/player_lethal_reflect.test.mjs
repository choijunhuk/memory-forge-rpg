import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const MF = require('../rules-core.js');
const DATA = require('../../data/game_data.json');

// §5.4 step 6 / §5.14: end the battle between hits without skipping card cleanup.
function battle({ card = 'sera_double', charId = 'sera', hp = 2, enemies = 1, relics = [] } = {}) {
  const b = MF.Battle.create(DATA, { party: [{ charId }],
    deck: Array.from({ length: 10 }, () => ({ id: card })),
    hp, maxHp: 81, enemies: Array(enemies).fill('e_mirrorclerk'),
    relics: ['detective_notebook', ...relics], seed: 3 });
  b.do('begin');
  return b;
}

function assertConserved(b, uid, zone = 'discard') {
  b.checkInvariants();
  const placed = [...Object.values(b.s.zones).flat(), ...b.s.res.map(r => r.uid)];
  assert.equal(placed.length, 10);
  assert.equal(new Set(placed).size, 10);
  assert.equal(b.s.zones[zone].filter(u => u === uid).length, 1);
  assert.equal(b.s.cards[uid].used, 1);
}

test('fatal reflect stops the next card hit before it kills a marked enemy for coins', () => {
  const b = battle(); b.do('end');
  const enemy = b.alive()[0]; enemy.hp = 7; enemy.st.mark = 1;
  const uid = b.s.zones.hand[0];
  const v = b.s.v;
  const response = b.do('play', { uid, mode: 'now', target: enemy.uid });
  assert.equal(response.v, v + 1);
  assert.equal(response.phase, 'DEFEAT');
  assert.equal(b.s.hp, 0);
  assert.equal(b.s.enemies[0].hp, 3);
  assert.equal(b.s.enemies[0].alive, true);
  assert.equal(b.s.ev.filter(e => e.e === 'dmg').length, 1);
  assert.equal(b.s.ev.some(e => e.e === 'kill'), false);
  assert.equal(b.s.runFlags.coinsBonus || 0, 0);
  assertConserved(b, uid);
  const before = structuredClone(b.s);
  assert.throws(() => b.do('play', { uid: b.s.zones.hand[0], mode: 'now', target: enemy.uid }), { code: 'WRONG_PHASE' });
  assert.deepEqual(b.s, before);
});

test('reserved fatal reflect stops later hits and returns every pending reservation once', () => {
  const b = battle();
  const enemy = b.alive()[0]; enemy.hp = 7; enemy.st.mark = 1;
  const [uid, due, future] = b.s.zones.hand;
  b.do('play', { uid, mode: 'next', target: enemy.uid });
  b.do('play', { uid: due, mode: 'next', target: enemy.uid });
  b.do('play', { uid: future, mode: 'after', target: enemy.uid });
  const req = { rid: 'resolve-fatal-reflect', v: b.s.v, kind: 'end', args: {} };
  const response = b.command(req);
  assert.equal(response.v, req.v + 1);
  assert.equal(response.phase, 'DEFEAT');
  assert.equal(b.s.enemies[0].hp, 1);
  assert.equal(b.s.enemies[0].alive, true);
  assert.equal(b.s.ev.filter(e => e.e === 'dmg').length, 1);
  assert.equal(b.s.runFlags.coinsBonus || 0, 0);
  assert.equal(b.s.res.length, 0);
  assertConserved(b, uid);
  for (const pending of [due, future]) {
    assert.equal(b.s.zones.discard.filter(u => u === pending).length, 1);
    assert.equal(b.s.cards[pending].used, 0);
  }
  const before = structuredClone(b.s);
  assert.deepEqual(b.command(structuredClone(req)), response);
  assert.deepEqual(b.s, before);
});

test('fatal reflect stops the next area target and later card block', () => {
  const b = battle({ card: 'harin_avalanche', charId: 'harin', enemies: 2 }); b.do('end');
  b.s.enemies[0].hp = 20; b.s.enemies[1].hp = 12; b.s.enemies[1].st.mark = 1;
  const uid = b.s.zones.hand[0];
  b.do('play', { uid, mode: 'now' });
  assert.equal(b.s.phase, 'DEFEAT');
  assert.deepEqual(b.s.enemies.map(e => e.hp), [8, 12]);
  assert.ok(b.s.enemies.every(e => e.alive));
  assert.equal(b.s.ev.filter(e => e.e === 'dmg').length, 1);
  assert.equal(b.s.runFlags.coinsBonus || 0, 0);
  assert.equal(b.s.block, 0);
  assert.equal(b.s.counter, 0);
  assertConserved(b, uid);
});

test('surviving reflect keeps every card hit and its marked kill reward', () => {
  const b = battle({ hp: 20 }); b.do('end');
  const enemy = b.alive()[0]; enemy.hp = 7; enemy.st.mark = 1;
  const uid = b.s.zones.hand[0];
  b.do('play', { uid, mode: 'now', target: enemy.uid });
  assert.equal(b.s.phase, 'VICTORY');
  assert.equal(b.s.hp, 14);
  assert.equal(b.s.ev.filter(e => e.e === 'dmg').length, 2);
  assert.equal(b.s.runFlags.coinsBonus, 10);
  assertConserved(b, uid);
});

test('surviving area reflect keeps the next target and later card block', () => {
  const b = battle({ card: 'harin_avalanche', charId: 'harin', hp: 20, enemies: 2 }); b.do('end');
  b.s.enemies[0].hp = 20; b.s.enemies[1].hp = 12; b.s.enemies[1].st.mark = 1;
  const uid = b.s.zones.hand[0];
  b.do('play', { uid, mode: 'now' });
  assert.equal(b.s.phase, 'PLAYER');
  assert.equal(b.s.hp, 14);
  assert.deepEqual(b.s.enemies.map(e => e.hp), [8, 0]);
  assert.equal(b.s.ev.filter(e => e.e === 'dmg').length, 2);
  assert.equal(b.s.runFlags.coinsBonus, 10);
  assert.equal(b.s.block, 12);
  assert.equal(b.s.counter, 1);
  assertConserved(b, uid);
});

test('memory envelope permits another scaled hit before actual defeat stops the rest', () => {
  const b = battle({ card: 'rien_finale', charId: 'rien', relics: ['memory_envelope'] }); b.do('end');
  b.s.echo = 2;
  b.s.enemies[0].hp = 25; b.s.enemies[0].st.mark = 1;
  const uid = b.s.zones.hand[0];
  b.do('play', { uid, mode: 'now' });
  assert.equal(b.s.phase, 'DEFEAT');
  assert.equal(b.s.hp, 0);
  assert.equal(b.s.enemies[0].hp, 5);
  assert.equal(b.s.enemies[0].alive, true);
  assert.equal(b.s.ev.filter(e => e.e === 'envelope').length, 1);
  assert.equal(b.s.ev.filter(e => e.e === 'dmg').length, 2);
  assert.equal(b.s.runFlags.envelopeUsed, true);
  assert.equal(b.s.runFlags.coinsBonus || 0, 0);
  assertConserved(b, uid, 'exhaust');
});

test('the fatal reflecting hit retains its own kill and reward ordering', () => {
  const b = battle(); b.do('end');
  const enemy = b.alive()[0]; enemy.hp = 4; enemy.st.mark = 1;
  const uid = b.s.zones.hand[0];
  b.do('play', { uid, mode: 'now', target: enemy.uid });
  assert.equal(b.s.phase, 'DEFEAT');
  assert.equal(b.s.enemies[0].hp, 0);
  assert.equal(b.s.enemies[0].alive, false);
  assert.equal(b.s.ev.filter(e => e.e === 'dmg').length, 1);
  assert.equal(b.s.ev.filter(e => e.e === 'kill').length, 1);
  assert.equal(b.s.runFlags.coinsBonus, 10);
  assertConserved(b, uid);
});
