import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFileSync } from 'node:fs';

const require = createRequire(import.meta.url);
const MF = require('../rules-core.js');
const DATA = JSON.parse(readFileSync(new URL('../../data/game_data.json', import.meta.url), 'utf8'));
const clone = (x) => JSON.parse(JSON.stringify(x));
const PARTY = [{ charId: 'sera' }, { charId: 'harin' }];

function battle(move, { deck = Array(10).fill('harin_shield'), enemy = {}, relics = [], cardEff = null } = {}) {
  const data = clone(DATA);
  if (cardEff) data.cards.find((c) => c.id === 'sera_round').eff = cardEff;
  data.enemies.push({ ...clone(DATA.enemies.find((e) => e.id === 'e_shelf')), id: 'e_late_fixture', hp: 200, brk: 20,
    pattern: Array.isArray(move) ? move : [move], ...enemy });
  const b = MF.Battle.create(data, { party: PARTY, deck: deck.map((id) => ({ id })), relics, hp: 200, maxHp: 200,
    seed: 91, enemies: ['e_late_fixture'] });
  b.do('begin');
  return b;
}

function first(b, id) { return b.s.zones.hand.find((uid) => b.s.cards[uid].id === id); }
function cardLocations(b) {
  return [...b.s.zones.draw, ...b.s.zones.hand, ...b.s.zones.discard, ...b.s.zones.exhaust,
    ...b.s.res.map((r) => r.uid)];
}

test('apDrain removes AP on the next turn separately from due debt and then clears', () => {
  const b = battle({ t: 'apDrain', n: 2 });
  b.s.debts.push({ n: 1, due: 2 });
  b.do('end');
  assert.equal(b.s.ap, 1);
  assert.equal(b.s.apDrainNext, 0);
  assert.equal(b.s.debts.length, 0);
});

test('repeated apDrain cannot make next-turn AP negative', () => {
  const b = battle({ t: 'apDrain', n: 3 });
  b.enemyMove(b.alive()[0], { t: 'apDrain', n: 4 });
  b.do('end');
  assert.equal(b.s.ap, 0);
  assert.equal(b.s.apDrainNext, 0);
});

test('silence increases next-turn reservation cost once and expires after that turn', () => {
  const b = battle([{ t: 'silence' }, { t: 'charge' }]);
  b.enemyMove(b.alive()[0], { t: 'silence' });
  b.do('end');
  const uid = first(b, 'harin_shield');
  assert.equal(b.costOf(uid, 'now'), 1);
  assert.equal(b.costOf(uid, 'next'), 2);
  b.do('end');
  assert.equal(b.costOf(first(b, 'harin_shield'), 'next'), 1);
});

test('silence never charges a free instance or a freeNext reservation', () => {
  const b = battle({ t: 'silence' });
  b.do('end');
  const uid = first(b, 'harin_shield');
  b.s.cards[uid].free = true;
  assert.equal(b.costOf(uid, 'next'), 0);
  b.s.cards[uid].free = false;
  b.s.freeNext = 1;
  assert.equal(b.costOf(uid, 'next'), 0);
});

test('sealSlot expires after its protected turn and can seal the following turn again', () => {
  const b = battle({ t: 'sealSlot' });
  b.do('end');
  assert.equal(b.s.sealedUntil, b.s.turn);
  assert.equal(b.slotCapacity('next'), b.s.slotsBase.next - 1);
  b.do('end');
  assert.equal(b.s.sealedUntil, b.s.turn);
  assert.equal(b.alive()[0].block, 0);
});

test('two same-phase sealSlot enemies make the second gain block 10', () => {
  const b = battle({ t: 'sealSlot' });
  const second = MF.Battle.addEnemy(b.s, b.ix, 'e_late_fixture');
  b.setIntentsFor(second);
  b.do('end');
  assert.equal(b.s.sealedUntil, b.s.turn);
  assert.equal(b.s.enemies[0].block, 0);
  assert.equal(b.s.enemies[1].block, 10);
});

test('sealSlot ignores reservations due before the protected following-player turn', () => {
  const b = battle({ t: 'sealSlot' });
  for (let i = 0; i < b.s.slotsBase.next; i++) b.do('play', { uid: first(b, 'harin_shield'), mode: 'next' });
  b.do('end');
  assert.equal(b.s.sealedUntil, b.s.turn);
  assert.equal(b.alive()[0].block, 0);
});

test('sealSlot gains enemy block 10 when future base slots are full', () => {
  const b = battle({ t: 'sealSlot' }, { relics: ['cracked_hourglass'] });
  for (let i = 0; i < 2; i++) b.do('play', { uid: first(b, 'harin_shield'), mode: 'after' });
  b.do('end');
  assert.equal(b.alive()[0].block, 10);
  assert.equal(b.s.sealedUntil, 0);
});

test('delayRes shifts the newest reservation due date while preserving its power kind', () => {
  const b = battle({ t: 'delayRes' });
  const u1 = first(b, 'harin_shield');
  b.do('play', { uid: u1, mode: 'next' });
  const u2 = first(b, 'harin_shield');
  b.do('play', { uid: u2, mode: 'next' });
  const originals = clone(b.s.res);
  b.do('end');
  const held = b.s.res.find((r) => r.uid === u2);
  assert.equal(held?.due, originals[1].due + 1);
  assert.equal(held?.kind, originals[1].kind);
  assert.equal(b.s.cards[u2].used, 0);
  assert.equal(b.s.res.some((r) => r.uid === u1), false);
});

test('delayRes discards an overflowing reservation without refund or losing its card', () => {
  const b = battle({ t: 'delayRes' }, { relics: ['cracked_hourglass'] });
  for (let i = 0; i < 2; i++) b.do('play', { uid: first(b, 'harin_shield'), mode: 'after' });
  const newest = first(b, 'harin_shield');
  b.do('play', { uid: newest, mode: 'next' });
  const ap = b.s.ap;
  b.enemyMove(b.alive()[0], { t: 'delayRes' });
  assert.ok(b.s.zones.discard.includes(newest));
  assert.equal(b.s.res.some((r) => r.uid === newest), false);
  assert.equal(new Set(cardLocations(b)).size, Object.keys(b.s.cards).length);
  assert.equal(b.s.ev.some((e) => e.uid === newest && /delay|discard|fizzle/i.test(e.e)), true);
  assert.equal(b.s.ap, ap);
});

test('delayRes with no reservation leaves RNG and card zones unchanged', () => {
  const b = battle({ t: 'delayRes' });
  const rng = clone(b.s.rng);
  const zones = clone(b.s.zones);
  b.enemyMove(b.alive()[0], { t: 'delayRes' });
  assert.deepEqual(b.s.rng, rng);
  assert.deepEqual(b.s.zones, zones);
});

test('delayRes discards a reservation shifted outside the two-turn board', () => {
  const b = battle({ t: 'charge' });
  const uid = first(b, 'harin_shield');
  b.do('play', { uid, mode: 'after' });
  b.enemyMove(b.alive()[0], { t: 'delayRes' });
  assert.equal(b.s.res[0].due, b.s.turn + 3);
  b.enemyMove(b.alive()[0], { t: 'delayRes' });
  assert.equal(b.s.res.length, 0);
  assert.ok(b.s.zones.discard.includes(uid));
  assert.equal(new Set(cardLocations(b)).size, Object.keys(b.s.cards).length);
});

test('eatName exhausts a discard token before a normal card', () => {
  const b = battle({ t: 'eatName' }, { relics: ['oblivion_ink'] });
  const uid = b.s.zones.hand[0];
  b.s.zones.hand.splice(0, 1);
  b.s.zones.discard.push(uid);
  const token = b.s.zones.draw.pop();
  b.s.cards[token].token = true;
  b.s.zones.discard.push(token);
  b.do('end');
  assert.ok(b.s.zones.exhaust.includes(token));
  assert.ok(!b.s.zones.exhaust.includes(uid));
  assert.equal(b.s.exhausted, 1);
  assert.equal(new Set(cardLocations(b)).size, Object.keys(b.s.cards).length);
});

test('eatName with an empty discard pile does not advance RNG', () => {
  const b = battle({ t: 'eatName' });
  const rng = clone(b.s.rng);
  b.enemyMove(b.alive()[0], { t: 'eatName' });
  assert.deepEqual(b.s.rng, rng);
  assert.equal(b.s.exhausted, 0);
});

test('a boss first lethal hit changes the same enemy to phase two without a kill', () => {
  const b = battle({ t: 'charge' }, { enemy: { tier: 'boss', hp: 4, brk: 3, weak: ['volt'],
    phase: { t: 'phase', hp: 7, brk: 5, weak: ['frost'], pattern: [{ t: 'atk', d: 11 }] } },
  });
  b.do('play', { uid: first(b, 'harin_shield'), mode: 'now' });
  const e = b.alive()[0];
  e.block = 3; e.st = { vul: 2, weak: 2, mark: 2, burn: 2, str: 2, reflect: 2 };
  e.stunned = true; e.brokenUntil = b.s.turn + 1;
  // Direct damage exercises the transition independent of card targeting.
  b.damageEnemyRaw(e, 99, 'test');
  assert.equal(e.uid, b.s.enemies[0].uid);
  assert.equal(e.phaseIndex, 1);
  assert.equal(e.hp, 7);
  assert.equal(e.brk, 5);
  assert.deepEqual(e.weak, ['frost']);
  assert.deepEqual(e.st, { vul: 0, weak: 0, mark: 0, burn: 0, str: 0, reflect: 0 });
  assert.equal(e.block, 0);
  assert.equal(e.stunned, false);
  assert.equal(e.brokenUntil, 0);
  assert.equal(b.s.phase, 'PLAYER');
  assert.equal(b.s.ev.some((x) => x.e === 'kill'), false);
  assert.equal(e.intent?.d, 11);
});

test('invalid phase metadata makes a lethal player command atomic', () => {
  const b = battle({ t: 'charge' }, { deck: Array(10).fill('sera_round'), enemy: { tier: 'boss', hp: 4,
    phase: { t: 'phase', hp: -1, brk: 4, weak: ['frost'], pattern: [{ t: 'charge' }] } } });
  const before = clone(b.s);
  assert.throws(() => b.do('play', { uid: first(b, 'sera_round'), mode: 'now', target: b.alive()[0].uid }));
  assert.deepEqual(b.s, before);
});

test('later hits of one card damage phase two and only final death emits kill', () => {
  const b = battle({ t: 'charge' }, { deck: Array(10).fill('sera_round'),
    cardEff: [{ op: 'dmg', v: 5, hits: 2 }],
    enemy: { tier: 'boss', hp: 4, brk: 20, weak: ['frost'],
      phase: { t: 'phase', hp: 7, brk: 20, weak: ['volt'], pattern: [{ t: 'charge' }] } } });
  const uid = b.alive()[0].uid;
  b.do('play', { uid: first(b, 'sera_round'), mode: 'now', target: uid });
  assert.equal(b.enemy(uid).phaseIndex, 1);
  assert.equal(b.enemy(uid).hp, 2);
  assert.equal(b.s.ev.filter((e) => e.e === 'kill').length, 0);
  b.do('play', { uid: first(b, 'sera_round'), mode: 'now', target: uid });
  assert.equal(b.s.phase, 'VICTORY');
  assert.equal(b.s.ev.filter((e) => e.e === 'kill').length, 1);
});

test('serialized phase-two battle resumes with the new intent and deterministic next turn', () => {
  const b = battle({ t: 'charge' }, { deck: Array(10).fill('sera_round'),
    enemy: { tier: 'boss', hp: 4, brk: 20, weak: ['frost'],
      phase: { t: 'phase', hp: 70, brk: 20, weak: ['volt'], pattern: [{ t: 'atk', d: 11 }] } } });
  b.do('play', { uid: first(b, 'sera_round'), mode: 'now', target: b.alive()[0].uid });
  const resumed = new MF.Battle(b.ix, clone(b.s));
  assert.deepEqual(resumed.publicView(), b.publicView());
  b.do('end');
  resumed.do('end');
  assert.deepEqual(resumed.s, b.s);
});
