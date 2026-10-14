import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const require = createRequire(import.meta.url);
const MF = require('../rules-core.js');
const DATA = require('../../data/game_data.json');

function battle({ type = 'atk', hp = 5, relics = [] } = {}) {
  const data = structuredClone(DATA);
  if (type === 'atkdebuff') data.enemies.find(e => e.id === 'e_twinink').pattern[0] =
    { t: 'atkdebuff', d: 12, h: 3, s: 'weak', n: 1 };
  const b = MF.Battle.create(data, { party: [{ charId: 'harin' }],
    deck: Array.from({ length: 10 }, () => ({ id: 'harin_shield' })),
    hp, maxHp: 81, enemies: ['e_twinink'], relics, seed: 2 });
  b.do('begin'); b.s.counter = 2;
  return b;
}

for (const type of ['atk', 'atkdebuff']) test(`${type} stops later hits and effects after the fatal hit`, () => {
  const b = battle({ type }); b.do('end');
  assert.equal(b.s.phase, 'DEFEAT');
  assert.equal(b.s.ev.filter(e => e.e === 'hit').length, 1);
  assert.equal(b.alive()[0].hp, 78, 'the fatal hit keeps its existing counter ordering, with no later counters');
  assert.equal(b.s.pst.weak, 0);
  assert.equal(b.s.ev.filter(e => e.e === 'end').length, 1);
});

test('a surviving party still receives every hit and the attack debuff', () => {
  const b = battle({ type: 'atkdebuff', hp: 81 }); b.do('end');
  assert.equal(b.s.phase, 'PLAYER');
  assert.equal(b.s.hp, 45);
  assert.equal(b.s.ev.filter(e => e.e === 'hit').length, 3);
  assert.equal(b.alive()[0].hp, 74);
  assert.equal(b.s.pst.weak, 1);
});

test('memory envelope survival permits the next hit before actual defeat stops the rest', () => {
  const b = battle({ relics: ['memory_envelope'] }); b.do('end');
  assert.equal(b.s.phase, 'DEFEAT');
  assert.equal(b.s.ev.filter(e => e.e === 'envelope').length, 1);
  assert.equal(b.s.ev.filter(e => e.e === 'hit').length, 2);
  assert.equal(b.alive()[0].hp, 76);
});
