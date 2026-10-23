import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const MF = require('../rules-core.js');
const DATA = require('../../data/game_data.json');

// §5.9 / §5.14: only the envelope prevents death; a battle that ends after a card resolves must retain the result.
// RULES_NOW_PLAY (2026-10-19): sera_last is 보존 (retain) + exhaust, so it survives turn 1 as a held card and is played now on turn 2.
function battle({ deck = Array(10).fill('sera_last'), party = [{ charId: 'sera' }], hp = 81,
  relics = ['void_shard'], seed = 3 } = {}) {
  const b = MF.Battle.create(DATA, { party, deck: deck.map(id => ({ id })),
    hp, maxHp: 81, enemies: ['e_mirrorclerk'], relics, seed });
  b.do('begin');
  return b;
}

function assertConserved(b, n = 10) {
  b.checkInvariants();
  const placed = [...Object.values(b.s.zones).flat(), ...b.s.res.map(r => r.uid)];
  assert.equal(placed.length, n);
  assert.equal(new Set(placed).size, n);
}

// Turn 1 ends with the 보존 card held; e_mirrorclerk's first move puts reflect 3 on itself.
function holdThenPlay(b, id = 'sera_last', enemyHp = 46) {
  b.do('end');
  const uid = b.s.zones.hand.find(u => b.s.cards[u].id === id);
  assert.equal(b.s.cards[uid].held, true);
  b.alive()[0].hp = enemyHp;
  const response = b.do('play', { uid, mode: 'now', target: b.alive()[0].uid });
  return { uid, response };
}

test('post-resolution void shard victory ends the battle inside the play command', () => {
  const b = battle();
  // sera_last 45 leaves the 46-HP enemy at 1; its exhaust triggers void_shard 3, which wins after resolution
  const { uid, response } = holdThenPlay(b);
  assert.equal(b.s.phase, 'VICTORY');
  assert.deepEqual(b.s.result, { phase: 'VICTORY', turn: 2, hp: 81 - 3 }); // reflect 3 from the one card hit
  assert.equal(b.alive().length, 0);
  assert.equal(response.ev.filter(e => e.e === 'end').length, 1);
  assert.equal(response.ev.some(e => e.e === 'turn'), false);
  assert.deepEqual(response.ev.filter(e => e.e === 'dmg').map(e => e.src), ['card', 'relic']);
  assert.deepEqual(b.s.zones.exhaust, [uid]);
  assert.equal(b.s.cards[uid].held, false);
  assert.equal(b.actor('sera').gauge, 20, 'only the resolved card grants gauge'); // gaugePerCard 20
  assertConserved(b);
});

test('a held card that ends the battle emits one end event', () => {
  const b = battle({ deck: Array(10).fill('sera_timed'), relics: [] });
  const { uid, response } = holdThenPlay(b, 'sera_timed', 8);
  assert.equal(b.s.phase, 'VICTORY');
  assert.equal(response.ev.filter(e => e.e === 'end').length, 1);
  assert.equal(b.s.zones.discard.filter(u => u === uid).length, 1);
  assert.equal(b.s.cards[uid].held, false);
  assertConserved(b);
});

test('post-resolution victory cannot become a smudge defeat on a spurious extra turn', () => {
  // five cards = the whole opening hand: turn 1 ends with smudge in hand (−2) and the four sera_last held;
  // turn 2 redraws smudge, so at HP 7 − 2 − reflect 3 = 2 one more end turn would be a smudge defeat
  const b = battle({ deck: [...Array(4).fill('sera_last'), 'smudge'], hp: 7, seed: 1 });
  holdThenPlay(b);
  assert.ok(b.s.zones.hand.some(u => b.s.cards[u].id === 'smudge'));
  // On the faulty path the battle keeps going, and this end turns the win into defeat.
  if (!b.isOver()) b.do('end');
  assert.equal(b.s.phase, 'VICTORY');
  assert.deepEqual(b.s.result, { phase: 'VICTORY', turn: 2, hp: 2 });
  assert.equal(b.s.ev.filter(e => e.e === 'end').length, 1);
  assert.equal(b.s.ev.some(e => e.e === 'hpLoss' && e.src === 'smudge'), false);
  const before = structuredClone(b.s);
  assert.throws(() => b.do('end'), { code: 'WRONG_PHASE' });
  assert.deepEqual(b.s, before);
  assertConserved(b, 5);
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
