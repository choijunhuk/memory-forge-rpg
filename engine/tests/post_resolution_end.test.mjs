import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const MF = require('../rules-core.js');
const DATA = require('../../data/game_data.json');

// §5.9 / §5.14: only the envelope prevents death; reservation cleanup must retain the result.
function battle({ deck = Array(10).fill('sera_last'), party = [{ charId: 'sera' }], hp = 81,
  relics = ['void_shard'], seed = 3 } = {}) {
  const b = MF.Battle.create(DATA, { party, deck: deck.map(id => ({ id })),
    hp, maxHp: 81, enemies: ['e_mirrorclerk'], relics, seed });
  b.do('begin');
  return b;
}

function assertConserved(b) {
  b.checkInvariants();
  const placed = [...Object.values(b.s.zones).flat(), ...b.s.res.map(r => r.uid)];
  assert.equal(placed.length, 10);
  assert.equal(new Set(placed).size, 10);
}

function reserveLast(b) {
  const uid = b.s.zones.hand.find(u => b.s.cards[u].id === 'sera_last');
  b.do('play', { uid, mode: 'after', target: b.alive()[0].uid });
  b.do('end');
  b.alive()[0].hp = 46;
  return uid;
}

test('post-resolution void shard victory stops turn startup before drawing', () => {
  const b = battle(); const uid = reserveLast(b);
  b.do('end');
  assert.equal(b.s.phase, 'VICTORY');
  assert.deepEqual(b.s.result, { phase: 'VICTORY', turn: 3, hp: 57 });
  assert.equal(b.alive().length, 0);
  assert.equal(b.s.ev.filter(e => e.e === 'end').length, 1);
  assert.equal(b.s.ev.some(e => e.e === 'turn'), false);
  assert.equal(b.s.zones.hand.length, 0);
  assert.deepEqual(b.s.zones.exhaust, [uid]);
  assertConserved(b);
});

test('post-resolution victory discards queued reservations without using them or granting gauge', () => {
  const deck = Array(10).fill('harin_shield'); deck[6] = 'sera_last';
  const b = battle({ deck, party: [{ charId: 'sera' }, { charId: 'harin' }] });
  const uid = reserveLast(b);
  const [due, future] = b.s.zones.hand;
  b.do('play', { uid: due, mode: 'next' });
  b.do('play', { uid: future, mode: 'after' });
  b.do('end');
  assert.equal(b.s.phase, 'VICTORY');
  assert.equal(b.actor('sera').gauge, 10);
  assert.equal(b.actor('harin').gauge, 0);
  for (const pending of [due, future]) {
    assert.equal(b.s.cards[pending].used, 0);
    assert.equal(b.s.zones.discard.filter(u => u === pending).length, 1);
  }
  assert.equal(b.s.res.length, 0);
  assert.equal(b.s.cards[uid].used, 1);
  assert.equal(b.s.ev.filter(e => e.e === 'resolved').length, 1);
  assert.equal(b.s.ev.filter(e => e.e === 'end').length, 1);
  assertConserved(b);
});

test('a reservation that already ended the battle emits one end event', () => {
  const b = battle({ deck: Array(10).fill('sera_round'), relics: [] });
  const enemy = b.alive()[0]; enemy.hp = 8;
  const uid = b.s.zones.hand[0];
  b.do('play', { uid, mode: 'next', target: enemy.uid });
  b.do('end');
  assert.equal(b.s.phase, 'VICTORY');
  assert.equal(b.s.ev.filter(e => e.e === 'end').length, 1);
  assert.equal(b.s.zones.discard.filter(u => u === uid).length, 1);
  assertConserved(b);
});

test('post-resolution victory cannot become a smudge defeat on a spurious extra turn', () => {
  const b = battle({ deck: [...Array(9).fill('sera_last'), 'smudge'], hp: 28, seed: 1 });
  reserveLast(b); b.do('end');
  // On the faulty path the extra draw includes smudge, and this end turns the win into defeat.
  if (!b.isOver()) b.do('end');
  assert.equal(b.s.phase, 'VICTORY');
  assert.deepEqual(b.s.result, { phase: 'VICTORY', turn: 3, hp: 2 });
  assert.equal(b.s.ev.filter(e => e.e === 'end').length, 1);
  assert.equal(b.s.ev.some(e => e.e === 'hpLoss' && e.src === 'smudge'), false);
  const before = structuredClone(b.s);
  assert.throws(() => b.do('end'), { code: 'WRONG_PHASE' });
  assert.deepEqual(b.s, before);
  assertConserved(b);
});

for (const [enemyHp, remainingHp, coins] of [[52, 44, 0], [8, 0, 10]]) {
  test(`silver needle cannot revive fatal reflect when the same hit leaves enemy HP ${remainingHp}`, () => {
    const b = battle({ deck: Array(10).fill('sera_round'), hp: 2,
      relics: ['silver_needle', 'detective_notebook'] }); b.do('end');
    const enemy = b.alive()[0]; enemy.hp = enemyHp; enemy.brk = 1; enemy.st.mark = 1;
    const uid = b.s.zones.hand[0];
    b.do('play', { uid, mode: 'now', target: enemy.uid });
    assert.equal(b.s.phase, 'DEFEAT');
    assert.equal(b.s.hp, 0);
    assert.equal(b.s.result.hp, 0);
    assert.equal(b.s.enemies[0].hp, remainingHp);
    assert.equal(b.s.enemies[0].alive, remainingHp > 0);
    assert.equal(b.s.runFlags.coinsBonus || 0, coins);
    assert.equal(b.s.ev.filter(e => e.e === 'break').length, 1);
    assert.equal(b.s.ev.some(e => e.e === 'heal'), false);
    assert.equal(b.s.ev.filter(e => e.e === 'end').length, 1);
    assert.equal(b.s.zones.discard.filter(u => u === uid).length, 1);
    assertConserved(b);
  });
}

test('silver needle still heals a party that survives reflect', () => {
  const b = battle({ deck: Array(10).fill('sera_round'), hp: 20, relics: ['silver_needle'] }); b.do('end');
  const enemy = b.alive()[0]; enemy.brk = 1;
  b.do('play', { uid: b.s.zones.hand[0], mode: 'now', target: enemy.uid });
  assert.equal(b.s.phase, 'PLAYER');
  assert.equal(b.s.hp, 20);
  assert.deepEqual(b.s.ev.filter(e => e.e === 'heal'), [{ e: 'heal', n: 3 }]);
  assertConserved(b);
});

test('memory envelope survival still permits silver needle healing at HP one', () => {
  const b = battle({ deck: Array(10).fill('sera_round'), hp: 2,
    relics: ['silver_needle', 'memory_envelope'] }); b.do('end');
  const enemy = b.alive()[0]; enemy.brk = 1;
  b.do('play', { uid: b.s.zones.hand[0], mode: 'now', target: enemy.uid });
  assert.equal(b.s.phase, 'PLAYER');
  assert.equal(b.s.hp, 4);
  assert.equal(b.s.runFlags.envelopeUsed, true);
  assert.equal(b.s.ev.filter(e => e.e === 'envelope').length, 1);
  assert.deepEqual(b.s.ev.filter(e => e.e === 'heal'), [{ e: 'heal', n: 3 }]);
  assertConserved(b);
});
