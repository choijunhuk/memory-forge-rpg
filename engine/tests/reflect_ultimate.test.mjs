import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const MF = require('../rules-core.js');
const DATA = require('../../data/game_data.json');

function mirrorBattle(charId = 'sera') {
  const b = MF.Battle.create(DATA, {
    party: [{ charId, gauge: 100 }],
    deck: Array.from({ length: 10 }, () => ({ id: 'sera_round' })),
    hp: 243, maxHp: 243, seed: 3, enemies: ['e_mirrorclerk'],
  });
  b.do('begin');
  return b;
}

for (const charId of ['sera', 'lumi']) test(`${charId} ultimate damage does not trigger card-only reflect`, () => {
  const b = mirrorBattle(charId); b.do('end');
  const e = b.alive()[0];
  assert.equal(e.st.reflect, 3);
  const hp = b.s.hp;
  b.do('ult', { actor: charId, target: e.uid });
  assert.equal(b.s.hp, hp);
  assert.ok(b.alive()[0].hp < e.hp);
  assert.equal(b.s.ev.some(event => event.e === 'hpLoss' && event.src === 'reflect'), false);
});

test('immediate and reserved card hits still trigger the unchanged reflect value', () => {
  const immediate = mirrorBattle(); immediate.do('end');
  immediate.do('play', { uid: immediate.s.zones.hand[0], mode: 'now', target: immediate.alive()[0].uid });
  assert.equal(immediate.s.hp, 240);
  assert.ok(immediate.s.ev.some(event => event.e === 'hpLoss' && event.src === 'reflect' && event.n === 3));
  const reserved = mirrorBattle();
  reserved.do('play', { uid: reserved.s.zones.hand[0], mode: 'next', target: reserved.alive()[0].uid });
  reserved.do('end');
  assert.equal(reserved.s.hp, 240);
  assert.ok(reserved.s.ev.some(event => event.e === 'hpLoss' && event.src === 'reflect' && event.n === 3));
});
