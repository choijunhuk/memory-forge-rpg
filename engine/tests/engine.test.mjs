// Unit tests for the v0.7 reference engine. Run: node --test engine/tests/*.test.mjs
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
const IX = MF.indexData(DATA);
const clone = (o) => JSON.parse(JSON.stringify(o));

const PARTY = [{ charId: 'sera', res: 0 }, { charId: 'harin', res: 0 }, { charId: 'nia', res: 0 }];
function battle({ deck, enemies, party = PARTY, hp = 243, seed = 7, relics = [], difficulty }) {
  const b = MF.Battle.create(DATA, { party, deck: deck.map((c) => (typeof c === 'string' ? { id: c } : c)), relics, hp, maxHp: 243, seed, enemies, difficulty });
  b.do('begin');
  return b;
}
const handIds = (b) => b.s.zones.hand.map((u) => b.s.cards[u].id);
const firstOf = (b, id) => b.s.zones.hand.find((u) => b.s.cards[u].id === id);

// ───────── RNG ─────────
test('xoshiro128** via splitmix32 is deterministic and bounded', () => {
  const a = MF.Rng.seed(20261014); const b = MF.Rng.seed(20261014);
  const xs = Array.from({ length: 8 }, () => MF.Rng.next(a));
  const ys = Array.from({ length: 8 }, () => MF.Rng.next(b));
  assert.deepEqual(xs, ys);
  assert.ok(xs.every((x) => Number.isInteger(x) && x >= 0 && x < 2 ** 32));
  const c = MF.Rng.seed(1);
  for (let i = 0; i < 2000; i++) { const v = MF.Rng.below(c, 7); assert.ok(v >= 0 && v < 7); }
  assert.throws(() => MF.Rng.below(c, 0), { code: 'RNG_BOUND' });
});

test('rng vectors file matches (cross-language contract)', () => {
  const vec = JSON.parse(readFileSync(join(here, 'vectors', 'rng_gacha_vectors.json'), 'utf8'));
  for (const v of vec.rng) {
    const s = MF.Rng.seed(v.seed);
    assert.deepEqual(Array.from({ length: v.next.length }, () => MF.Rng.next(s)), v.next, `seed ${v.seed}`);
  }
});

test('indexData is idempotent', () => {
  assert.equal(MF.indexData(IX), IX);
  assert.equal(IX.cards.sera_round.name, '유리탄');
});

// ───────── battle basics ─────────
test('begin: 4 AP, 5 cards, enemies show current and next intent', () => {
  const b = battle({ deck: Array(10).fill('sera_round'), enemies: ['e_page'] });
  assert.equal(b.s.phase, 'PLAYER');
  assert.equal(b.s.ap, 4);
  assert.equal(b.s.zones.hand.length, 5);
  assert.ok(b.s.enemies[0].intent && b.s.enemies[0].next);
});

test('immediate damage uses printed value at reference power (power4 160)', () => {
  const b = battle({ deck: Array(10).fill('sera_round'), enemies: ['e_page'] });
  const e = b.s.enemies[0];
  b.do('play', { uid: b.s.zones.hand[0], mode: 'now', target: e.uid });
  assert.equal(b.s.enemies[0].hp, 12 - 8);
  assert.equal(b.s.ap, 3);
});

// RULES_NOW_PLAY (2026-10-19): the time-reservation mechanic is gone; every card is played now.
test('every card is played now: next/after modes are rejected with NOT_RESERVABLE and nothing is reserved', () => {
  assert.ok(DATA.cards.every((c) => c.res === 'n'), 'all authored cards are now-only');
  // sera_timed / com_capacitor were reserve-only before; they now play now like any other card
  const b = battle({ deck: ['sera_timed', 'com_capacitor', 'sera_round', 'sera_round', 'sera_round'], enemies: ['e_shelf'] });
  const t = b.s.enemies[0].uid;
  for (const u of b.s.zones.hand) {
    assert.deepEqual(b.allowedModes(u), ['now']);
    for (const mode of ['next', 'after']) {
      assert.equal(b.check(u, mode, t), 'NOT_RESERVABLE');
      const before = JSON.stringify(b.s);
      assert.throws(() => b.do('play', { uid: u, mode, target: t }), { code: 'NOT_RESERVABLE' });
      assert.equal(JSON.stringify(b.s), before);
    }
  }
  b.do('play', { uid: firstOf(b, 'sera_timed'), mode: 'now', target: t });
  b.do('play', { uid: firstOf(b, 'com_capacitor'), mode: 'now' });
  assert.equal(b.s.ap, 4 - 2 - 1 + 2); // sera_timed cost 2, com_capacitor cost 1 then AP +2
  b.do('end');
  assert.equal(b.s.res.length, 0);
});

test('retain: the 보존 keyword and the c_delay part keep a card in hand as held; other hand cards are discarded', () => {
  // a five-card deck is the whole opening hand, so turn 2 can only redraw what turn 1 discarded
  const b = battle({ deck: ['sera_timed', 'com_capacitor', { id: 'sera_round', mods: { c: 'c_delay' } }, 'sera_round', 'sera_round'], enemies: ['e_shelf'], difficulty: { hp: 400, dmg: 10 } });
  const kept = b.s.zones.hand.filter((u) => ['sera_timed', 'com_capacitor'].includes(b.s.cards[u].id) || b.s.cards[u].mods.c === 'c_delay');
  const plain = b.s.zones.hand.filter((u) => !kept.includes(u));
  assert.equal(kept.length, 3); assert.equal(plain.length, 2);
  assert.ok(b.s.zones.hand.every((u) => b.s.cards[u].held === false));
  const r = b.do('end');
  for (const u of kept) { assert.ok(b.s.zones.hand.includes(u)); assert.equal(b.s.cards[u].held, true); }
  // the two plain cards went to the discard pile at end of turn and came back only through the turn-2 reshuffle
  assert.ok(r.ev.some((e) => e.e === 'reshuffle'));
  for (const u of plain) { assert.ok(b.s.zones.hand.includes(u)); assert.equal(b.s.cards[u].held, false); }
  assert.equal(b.s.zones.hand.length, 5);
  b.checkInvariants();
});

test('held is read and cleared on play, and reset when a card is drawn or recalled', () => {
  const b = battle({ deck: ['sera_timed', 'sera_round', 'sera_round', 'sera_round', 'sera_round'], enemies: ['e_shelf'], relics: ['time_stamp'], difficulty: { hp: 400, dmg: 10 } });
  b.do('end');
  const timed = firstOf(b, 'sera_timed');
  assert.equal(b.s.cards[timed].held, true);
  b.do('play', { uid: timed, mode: 'now', target: b.s.enemies[0].uid });
  assert.equal(b.s.cards[timed].held, false);
  // time_stamp reads ctx.held: +10 per card and +5 because the card had been held
  assert.equal(b.actor('sera').gauge, 15);
  b.do('play', { uid: firstOf(b, 'sera_round'), mode: 'now', target: b.s.enemies[0].uid });
  assert.equal(b.actor('sera').gauge, 25, 'a card that was not held gets no time_stamp bonus');
  // drawing resets a stale held flag
  const stale = b.s.zones.discard[0]; b.s.cards[stale].held = true;
  b.s.zones.discard.splice(0, 1); b.s.zones.draw.unshift(stale);
  b.draw(1);
  assert.ok(b.s.zones.hand.includes(stale)); assert.equal(b.s.cards[stale].held, false);
  // recall (sera_pull) resets it too
  const r = battle({ deck: ['sera_pull', 'sera_round', 'sera_round', 'sera_round', 'sera_round'], enemies: ['e_shelf'], difficulty: { hp: 400, dmg: 10 } });
  const shot = firstOf(r, 'sera_round');
  r.do('play', { uid: shot, mode: 'now', target: r.s.enemies[0].uid });
  r.s.cards[shot].held = true;
  r.do('play', { uid: firstOf(r, 'sera_pull'), mode: 'now', picks: [shot] });
  assert.ok(r.s.zones.hand.includes(shot)); assert.equal(r.s.cards[shot].held, false);
});

test('collateral: block v plus debt 1 due next turn; rejected with DEBT_CAP at the cap', () => {
  const party = [{ charId: 'ravien', res: 0 }, { charId: 'harin', res: 0 }, { charId: 'nia', res: 0 }];
  const b = battle({ party, deck: Array(10).fill('ravien_collateral'), enemies: ['e_shelf'], difficulty: { hp: 400, dmg: 10 } });
  const v = DATA.cards.find((c) => c.id === 'ravien_collateral').eff[0].v;
  b.do('play', { uid: b.s.zones.hand[0], mode: 'now' });
  assert.equal(b.s.block, b.scaleVit(v));
  assert.deepEqual(b.s.debts.map((d) => [d.n, d.due]), [[1, 2]]);
  assert.equal(b.s.sealedUntil, 0, 'no reservation slot is sealed');
  assert.equal(b.nextTurnAp(), DATA.rules.apPerTurn - 1);
  while (b.debtTotal() < b.s.debtCap) b.do('play', { uid: b.s.zones.hand[0], mode: 'now' });
  const u = b.s.zones.hand[0];
  assert.equal(b.check(u, 'now', null), 'DEBT_CAP');
  const before = JSON.stringify(b.s);
  assert.throws(() => b.do('play', { uid: u, mode: 'now' }), { code: 'DEBT_CAP' });
  assert.equal(JSON.stringify(b.s), before);
  b.do('end');
  assert.equal(b.s.ap, DATA.rules.apPerTurn - b.s.debtCap);
  assert.equal(b.s.debts.length, 0);
});

test('empty_clock: the first card of the battle costs 1 less in now mode, once', () => {
  const b = battle({ deck: Array(10).fill('sera_round'), enemies: ['e_shelf'], relics: ['empty_clock'] });
  const [u1, u2] = b.s.zones.hand;
  assert.equal(b.costOf(u1, 'now'), 0); // sera_round cost 1 − 1
  b.do('play', { uid: u1, mode: 'now', target: b.s.enemies[0].uid });
  assert.equal(b.s.ap, 4);
  assert.equal(b.s.battleFlags.emptyClockUsed, true);
  assert.equal(b.costOf(u2, 'now'), 1);
  b.do('play', { uid: u2, mode: 'now', target: b.s.enemies[0].uid });
  assert.equal(b.s.ap, 3);
});

test('draw relics: twin_pendulum and infinite_circuit +1 draw every turn, cracked_hourglass +2 on turn 1 only', () => {
  const D = DATA.rules.handDraw;
  const mk = (relic) => battle({ deck: Array(20).fill('sera_round'), enemies: ['e_shelf'], relics: [relic], difficulty: { hp: 400, dmg: 10 } });
  const twin = mk('twin_pendulum');
  assert.equal(twin.s.handMax, 9);
  assert.equal(twin.s.zones.hand.length, D + 1);
  twin.do('end'); assert.equal(twin.s.zones.hand.length, D + 1);
  const circuit = mk('infinite_circuit');
  assert.equal(circuit.s.zones.hand.length, D + 1);
  assert.equal(circuit.s.hp, 243 - 2);
  circuit.do('end'); assert.equal(circuit.s.zones.hand.length, D + 1);
  const glass = mk('cracked_hourglass');
  assert.deepEqual([glass.s.zones.hand.length, glass.s.ap], [D + 2, DATA.rules.apPerTurn - 1]);
  glass.do('end');
  assert.deepEqual([glass.s.zones.hand.length, glass.s.ap], [D, DATA.rules.apPerTurn]);
  assert.equal(glass.s.slotsBase.after, DATA.rules.slots.after, 'no reservation slot is added');
});

test('grow: a growth card grows when it resolves now, once per card per battle', () => {
  const party = [{ charId: 'yuri', res: 0 }, { charId: 'harin', res: 0 }, { charId: 'nia', res: 0 }];
  const b = battle({ party, deck: ['yuri_cocoon'], enemies: ['e_shelf'], difficulty: { hp: 400, dmg: 10 } });
  const u = b.s.zones.hand[0];
  const r1 = b.do('play', { uid: u, mode: 'now' });
  assert.equal(b.s.cards[u].growth, 1);
  assert.deepEqual(r1.ev.filter((e) => e.e === 'grow'), [{ e: 'grow', uid: u, growth: 1, src: 'resolve' }]);
  b.do('end');
  const r2 = b.do('play', { uid: u, mode: 'now' });
  assert.equal(b.s.cards[u].growth, 1);
  assert.ok(r2.ev.every((e) => e.e !== 'grow'));
  assert.equal(b.s.growthCount, 1);
});

test('held-keyed bonuses: precision_lens break +1 and w_reservedDmg apply only to a held card', () => {
  const v = DATA.cards.find((c) => c.id === 'sera_timed').eff[0];
  const hitTwice = (party, relics) => {
    const b = battle({ party, deck: Array(5).fill('sera_timed'), enemies: ['b_archivist'], relics, difficulty: { hp: 400, dmg: 10 } });
    const play = () => { const e = b.s.enemies[0]; const brk = e.brk;
      const r = b.do('play', { uid: b.s.zones.hand[0], mode: 'now', target: e.uid });
      return { brk: brk - b.s.enemies[0].brk, n: r.ev.find((x) => x.e === 'dmg' && x.src === 'card').n }; };
    const fresh = play(); b.do('end'); // the other four sera_timed are 보존 and held on turn 2
    assert.equal(b.s.cards[b.s.zones.hand[0]].held, true);
    return [fresh, play()];
  };
  // b_archivist is weak to volt: sera_timed (brk 3) breaks 3 normally, 3 + 1 when held with precision_lens
  const [lensFresh, lensHeld] = hitTwice([{ charId: 'sera', res: 0 }], ['precision_lens']);
  assert.deepEqual([lensFresh.brk, lensHeld.brk], [v.brk, v.brk + 1]);
  const scope = DATA.weapons.find((w) => w.id === 'w_sera').passive.values[0];
  const [wFresh, wHeld] = hitTwice([{ charId: 'sera', res: 0, weaponId: 'w_sera', refine: 1 }], []);
  assert.deepEqual([wFresh.n, wHeld.n], [v.v, Math.floor(v.v * (10000 + scope) / 10000)]);
});

test('sera hooks without reservations: r1 draws on any 유리탄 play once per turn, r6 is +50% on the turn after her ultimate', () => {
  const r1 = battle({ party: [{ charId: 'sera', res: 1 }], deck: Array(12).fill('sera_round'), enemies: ['e_shelf'], difficulty: { hp: 400, dmg: 10 } });
  const t = r1.s.enemies[0].uid;
  r1.do('play', { uid: r1.s.zones.hand[0], mode: 'now', target: t });
  assert.equal(r1.s.zones.hand.length, DATA.rules.handDraw); // −1 played, +1 drawn
  r1.do('play', { uid: r1.s.zones.hand[0], mode: 'now', target: t });
  assert.equal(r1.s.zones.hand.length, DATA.rules.handDraw - 1);
  const r6 = battle({ party: [{ charId: 'sera', res: 6, gauge: 100 }], deck: Array(12).fill('sera_round'), enemies: ['e_shelf'], difficulty: { hp: 400, dmg: 10 } });
  const hit = () => r6.do('play', { uid: r6.s.zones.hand[0], mode: 'now', target: r6.s.enemies[0].uid }).ev.find((e) => e.e === 'dmg' && e.src === 'card').n;
  r6.do('ult', { actor: 'sera', target: r6.s.enemies[0].uid });
  const sameTurn = hit(); r6.do('end'); const nextTurn = hit(); r6.do('end'); const later = hit();
  assert.deepEqual([sameTurn, nextTurn, later], [8, 12, 8]); // 8, 8 × 1.5, 8
  assert.equal(r6.s.battleFlags.seraR6Active, 2); // the boost belonged to turn 2 alone (a second ultimate then only arms turn 3)
});

test('a failing command changes nothing (atomic) and precheck is pure', () => {
  const b = battle({ deck: Array(10).fill('sera_round'), enemies: ['e_page'] });
  b.s.ap = 0;
  const before = JSON.stringify(b.s);
  assert.equal(b.check(b.s.zones.hand[0], 'now', 'auto'), 'AP_LOW');
  assert.throws(() => b.do('play', { uid: b.s.zones.hand[0], mode: 'now' }), { code: 'AP_LOW' });
  assert.equal(JSON.stringify(b.s), before);
});

test('idempotency: same rid returns the stored response; changed payload conflicts; stale version rejected', () => {
  const b = battle({ deck: Array(10).fill('sera_round'), enemies: ['e_moth', 'e_moth'] });
  const uid = b.s.zones.hand[0]; const t = b.s.enemies[0].uid;
  const req = { rid: 'R1', v: b.s.v, kind: 'play', args: { uid, mode: 'now', target: t } };
  const r1 = b.command(req);
  const r2 = b.command(clone(req));
  assert.deepEqual(r1, r2);
  assert.throws(() => b.command({ ...req, args: { ...req.args, target: b.s.enemies[1].uid } }), { code: 'IDEMPOTENCY_CONFLICT' });
  assert.throws(() => b.command({ rid: 'R2', v: 0, kind: 'end', args: {} }), { code: 'STALE_VERSION' });
});

test('break: weakness hits empty the gauge → stun, +25% damage taken, +1 AP next turn', () => {
  // 기억 나방: 18 HP, weak flame/volt, break 3. 세라 = volt.
  const b = battle({ deck: Array(10).fill('sera_round'), enemies: ['e_moth', 'e_shelf'] });
  const moth = b.s.enemies[0];
  for (let i = 0; i < 2; i++) b.do('play', { uid: b.s.zones.hand[0], mode: 'now', target: moth.uid });
  assert.equal(b.s.enemies[0].hp, 2);
  assert.equal(b.s.enemies[0].brk, 1);
  // hit the shelf (not weak to volt): no gauge change
  const shelf = b.s.enemies[1];
  b.do('play', { uid: b.s.zones.hand[0], mode: 'now', target: shelf.uid });
  assert.equal(b.s.enemies[1].brk, b.s.enemies[1].brkMax);
  b.do('play', { uid: b.s.zones.hand[0], mode: 'now', target: moth.uid });
  assert.equal(b.s.enemies[0].alive, false); // 2 HP left → dies; check break bookkeeping on a sturdier target next
  const b2 = battle({ deck: Array(10).fill('sera_round'), enemies: ['e_lantern'], difficulty: { hp: 300, dmg: 100 } }); // 60 HP, weak void/volt, break 3
  const L = b2.s.enemies[0];
  for (let i = 0; i < 3; i++) b2.do('play', { uid: b2.s.zones.hand[0], mode: 'now', target: L.uid });
  const e = b2.s.enemies[0];
  assert.ok(e.brokenUntil > 0, 'broken');
  assert.equal(e.intent.t, 'stun');
  assert.equal(b2.s.apNext, 1);
  const hp = e.hp;
  b2.do('play', { uid: b2.s.zones.hand[0], mode: 'now', target: L.uid });
  // 8 × (1 + 25% sera_foresight, additive) × 1.25 broken = 12.5 → 12; foresight now applies to every Sera attack (RULES_NOW_PLAY)
  assert.equal(hp - b2.s.enemies[0].hp, Math.floor(8 * (10000 + 2500) * (10000 + DATA.rules.brokenDamageBps) / 10000 / 10000));
});

test('hand never exceeds the cap; card count is conserved (invariants)', () => {
  const b = battle({ deck: Array(30).fill('sera_reload'), enemies: ['e_shelf'] });
  for (let i = 0; i < 6; i++) { const u = b.s.zones.hand.find((x) => b.check(x, 'now', null) === null); if (!u) break; b.do('play', { uid: u, mode: 'now', picks: [b.s.zones.hand.find((x) => x !== u)] }); }
  assert.ok(b.s.zones.hand.length <= b.s.handMax);
  b.checkInvariants();
});

test('contracts: loan raises AP now and repays next turn; DEBT_CAP enforced', () => {
  const party = [{ charId: 'ravien', res: 0 }, { charId: 'harin', res: 0 }, { charId: 'nia', res: 0 }];
  const b = battle({ party, deck: Array(10).fill('ravien_advance'), enemies: ['e_shelf'] });
  const ap0 = b.s.ap;
  const u = b.s.zones.hand[0];
  const cost = b.costOf(u, 'now');
  b.do('play', { uid: u, mode: 'now' });
  assert.ok(b.s.ap > ap0 - cost, 'loan added AP');
  assert.ok(b.debtTotal() > 0);
  let guard = 0; let code = null;
  while (guard++ < 10) { const x = b.s.zones.hand[0]; if (!x) break; code = b.check(x, 'now', null); if (code) break; b.do('play', { uid: x, mode: 'now' }); }
  assert.ok(['DEBT_CAP', 'LOAN_OVERFLOW', 'AP_LOW', null].includes(code));
  assert.ok(b.debtTotal() <= b.s.debtCap);
});

test('grow: a winning blow still grows its card (RULES_NOW_PLAY)', () => {
  const party = [{ charId: 'yuri', res: 0 }, { charId: 'harin', res: 0 }, { charId: 'nia', res: 0 }];
  const b = battle({ party, deck: ['yuri_sprout'], enemies: ['e_seed'], difficulty: { hp: 1, dmg: 100 } });
  const u = b.s.zones.hand[0]; const e = b.s.enemies[0];
  assert.ok(e.hp + e.block <= 6, 'precondition: the sprout kills the only enemy');
  const r = b.do('play', { uid: u, mode: 'now', target: e.uid });
  assert.equal(b.s.phase, 'VICTORY');
  assert.equal(b.s.cards[u].growth, 1);
  assert.ok(r.ev.some((x) => x.e === 'grow' && x.uid === u));
});

// ───────── growth stats ─────────
test('stats: reference values, level/weapon/gear derivation and stage parity', () => {
  const ref = MF.Stats.derive(IX, { charId: 'sera' });
  assert.equal(ref.power4, 160); assert.equal(ref.hp, 81);
  const max = MF.Stats.derive(IX, { charId: 'sera', level: 60, weaponId: 'w_sera', weaponLevel: 60, armor: 15, accessory: 15, skillLv: 10, ultLv: 10 });
  assert.deepEqual({ p: max.power, hp: max.hp, g: max.gearBps }, { p: 42, hp: 151, g: 1000 });
  const fresh = MF.Stats.derive(IX, { charId: 'sera', level: 1, weaponId: 'w_basic_ranged', weaponLevel: 1 });
  assert.deepEqual({ p: fresh.power, hp: fresh.hp }, { p: 21, hp: 61 });
  assert.deepEqual(MF.Stats.stageScale(IX, 60), { hp: 100, dmg: 148 });
  assert.deepEqual(MF.Stats.stageScale(IX, 1), { hp: 53, dmg: 75 });
  let xp = 0; for (let L = 1; L < 60; L++) xp += MF.Stats.xpToNext(IX, L);
  assert.equal(xp, 360000);
});

test('stage level scales enemy HP; accessory adds to the additive damage group', () => {
  const run = MF.Run.create(DATA, { party: [{ charId: 'sera', level: 30, weaponId: 'w_basic_ranged', weaponLevel: 30, accessory: 0 }, { charId: 'harin', level: 30 }, { charId: 'nia', level: 30 }], seed: 3, stageLevel: 30 });
  assert.deepEqual(run.s.difficulty, MF.Stats.stageScale(IX, 30));
  assert.equal(run.s.maxHp, 3 * 90);
  const b = MF.Battle.create(DATA, { party: run.s.party, deck: Array(10).fill({ id: 'sera_round' }), relics: [], hp: 270, maxHp: 270, seed: 1, enemies: ['e_shelf'], difficulty: run.s.difficulty });
  b.do('begin');
  const e = b.s.enemies[0];
  assert.equal(e.maxHp, Math.round(46 * run.s.difficulty.hp / 100));
  const hp0 = e.hp + e.block;
  b.do('play', { uid: b.s.zones.hand[0], mode: 'now', target: e.uid });
  // power = C(30) 27 + W_R(30) 2 = 29 → power4 116; accessory +0 → +2.5% (additive group); single floor at the end
  assert.equal(run.s.party[0].power4, 116);
  assert.equal(hp0 - (b.s.enemies[0].hp + b.s.enemies[0].block), Math.floor(8 * 116 * 10250 / (160 * 10000)));
});

// ───────── run & memory ─────────
test('map: every path has at least 3 fights before the boss', () => {
  for (let seed = 1; seed <= 300; seed++) {
    const r = MF.Run.create(DATA, { party: PARTY, seed });
    const m = r.s.map; const by = Object.fromEntries(m.floors.flat().map((n) => [n.id, n]));
    const memo = {};
    const f = (id) => { if (id === m.boss.id) return 0; if (memo[id] !== undefined) return memo[id]; const n = by[id]; memo[id] = (['battle', 'elite'].includes(n.type) ? 1 : 0) + Math.min(...n.next.map(f)); return memo[id]; };
    assert.ok(Math.min(...m.floors[0].map((n) => f(n.id))) >= 3, `seed ${seed}`);
  }
});

test('memory save: capacity is enforced, defeat keeps one change, saved deck rebuilds the next run', () => {
  const r = MF.Run.create(DATA, { party: PARTY, seed: 11 });
  // simulate gains: add 4 sera S-cards (cost 3 each) and upgrade a base card
  for (const id of ['sera_phase', 'sera_flash', 'sera_last', 'sera_lock']) r.s.deck.push({ id, key: `kx${id}`, owner: 'sera', gained: true });
  r.s.deck.find((c) => c.id === 'sera_round').plus = true;
  r.s.phase = 'VICTORY'; r.s.pending = { type: 'save', defeat: false };
  const info = r.memoryChanges().sera;
  assert.equal(info.capacity, 6);
  const all = info.changes.map((_, i) => i);
  assert.throws(() => r.commitMemory({ sera: all }), { code: 'OVER_CAPACITY' });
  const plus = info.changes.findIndex((c) => c.type === 'plus');
  const add = info.changes.findIndex((c) => c.type === 'add');
  const mem = r.commitMemory({ sera: [plus, add] });
  assert.ok(mem.sera.some((sp) => sp.id === 'sera_round' && sp.plus));
  assert.equal(mem.sera.length, 6); // 4 start + 1 common + 1 added
  const r2 = MF.Run.create(DATA, { party: PARTY, memory: mem, seed: 12 });
  assert.equal(r2.s.deck.filter((c) => c.owner === 'sera').length, 5);
  // defeat
  const r3 = MF.Run.create(DATA, { party: PARTY, seed: 13 });
  r3.s.deck.push({ id: 'sera_phase', key: 'kz', owner: 'sera', gained: true }); r3.s.deck.push({ id: 'harin_frostwall', key: 'kz2', owner: 'harin', gained: true });
  r3.s.phase = 'DEFEAT'; r3.s.pending = { type: 'save', defeat: true };
  assert.throws(() => r3.commitMemory({ sera: [0], harin: [0] }), { code: 'DEFEAT_KEEP_ONE' });
  r3.commitMemory({ sera: [0] });
});

test('capacity grows with ascension 2 and 4 and resonance 4', () => {
  assert.equal(MF.Run.capacityOf(IX, { charId: 'sera', res: 0, asc: 0 }), 6);
  assert.equal(MF.Run.capacityOf(IX, { charId: 'sera', res: 0, asc: 2 }), 7);
  assert.equal(MF.Run.capacityOf(IX, { charId: 'sera', res: 4, asc: 4 }), 9);
});

// ───────── gacha ─────────
const BPS = 10000;
function exactMean(curve) { let surv = 1; let mean = 0; for (let k = 1; k <= curve.hard; k++) { const p = MF.Gacha.ssrRate(curve, k - 1) / BPS; mean += k * surv * p; surv *= 1 - p; } return mean; }

test('character banner: SSR by pull 80 at the latest; lost 50:50 guarantees the next featured', () => {
  const g = MF.Gacha.newState(99); g.crystals = 10 ** 9;
  let sinceSsr = 0; let guarOk = true; let lastLost = false;
  for (let i = 0; i < 4000; i++) {
    const [r] = MF.Gacha.pull(DATA, g, 'limited_lumi', 1);
    sinceSsr += 1;
    if (r.rarity === 'SSR') {
      assert.ok(sinceSsr <= 80, `SSR gap ${sinceSsr}`);
      if (lastLost && r.id !== 'lumi') guarOk = false;
      lastLost = !!r.lost; sinceSsr = 0;
    }
  }
  assert.ok(guarOk);
});

test('SR or better at least every 10 pulls on every banner', () => {
  for (const id of ['limited_lumi', 'weapon_lumi', 'standard']) {
    const g = MF.Gacha.newState(5); g.crystals = 10 ** 9; let gap = 0;
    for (let i = 0; i < 2000; i++) { const [r] = MF.Gacha.pull(DATA, g, id, 1); gap = r.rarity === 'R' ? gap + 1 : 0; assert.ok(gap <= 9, `${id} gap ${gap}`); }
  }
});

test('weapon banner: 70 hard pity, 75% featured, guarantee after a loss', () => {
  const g = MF.Gacha.newState(3); g.crystals = 10 ** 9; let since = 0; let lost = false; let feat = 0; let ssr = 0;
  for (let i = 0; i < 24000; i++) {
    const [r] = MF.Gacha.pull(DATA, g, 'weapon_lumi', 1); since += 1;
    if (r.rarity === 'SSR') { assert.ok(since <= 70); if (lost) assert.equal(r.id, 'w_lumi'); lost = !!r.lost; since = 0; ssr += 1; if (r.id === 'w_lumi') feat += 1; }
  }
  assert.ok(feat / ssr > 0.76 && feat / ssr < 0.84, `featured share ${feat / ssr}`); // 75% + guarantee after a loss → 1/1.25 = 0.80
});

test('Gacha.stats mean equals the exact survival-sum and Monte Carlo agrees (±3%)', () => {
  const curve = DATA.gacha.curves.character;
  const st = MF.Gacha.stats(curve, curve.featuredSsrBps);
  assert.ok(Math.abs(st.mean - exactMean(curve)) < 1e-9);
  const g = MF.Gacha.newState(42); g.crystals = 10 ** 9; let n = 0; let k = 0;
  for (let i = 0; i < 60000; i++) { const [r] = MF.Gacha.pull(DATA, g, 'limited_kaya', 1); n += 1; if (r.rarity === 'SSR') k += 1; }
  assert.ok(Math.abs(n / k - st.mean) / st.mean < 0.03, `mc ${n / k} vs ${st.mean}`);
});

test('beginner banner: 10-pulls only, 8 tickets each, SSR guaranteed within 50', () => {
  const g = MF.Gacha.newState(8); g.tickets.standardTicket = 40;
  assert.throws(() => MF.Gacha.pull(DATA, g, 'beginner', 1), { code: 'BEGINNER_TEN_ONLY' });
  let got = false;
  for (let i = 0; i < 5; i++) { const rs = MF.Gacha.pull(DATA, g, 'beginner', 10); if (rs.some((r) => r.rarity === 'SSR')) got = true; }
  assert.ok(got); assert.equal(g.tickets.standardTicket, 0);
  assert.throws(() => MF.Gacha.pull(DATA, g, 'beginner', 10), { code: 'BEGINNER_LIMIT' });
});

test('pull count must be 1 or 10; standard selector after 300 pulls, once', () => {
  const g = MF.Gacha.newState(4); g.crystals = 10 ** 9;
  assert.throws(() => MF.Gacha.pull(DATA, g, 'standard', 5), { code: 'BAD_COUNT' });
  assert.throws(() => MF.Gacha.claimSelector(DATA, g, 'sera'), { code: 'SELECTOR_NOT_READY' });
  for (let i = 0; i < 30; i++) MF.Gacha.pull(DATA, g, 'standard', 10);
  assert.throws(() => MF.Gacha.claimSelector(DATA, g, 'lumi'), { code: 'SELECTOR_BAD_CHOICE' });
  const r = MF.Gacha.claimSelector(DATA, g, 'yuri');
  assert.equal(r.id, 'yuri');
  assert.throws(() => MF.Gacha.claimSelector(DATA, g, 'sera'), { code: 'SELECTOR_USED' });
});

test('duplicates: 2nd copy = resonance 1, capped at 6; weapons refine to R5', () => {
  const g = MF.Gacha.newState(1);
  g.owned.chars.sera = 1; assert.equal(MF.Gacha.resonance(g, 'sera'), 0);
  g.owned.chars.sera = 2; assert.equal(MF.Gacha.resonance(g, 'sera'), 1);
  g.owned.chars.sera = 12; assert.equal(MF.Gacha.resonance(g, 'sera'), 6);
  g.owned.weapons.w_sera = 9; assert.equal(MF.Gacha.refine(g, 'w_sera'), 5);
});

test('gacha vectors (cross-language contract): same seed → same 30 results', () => {
  const vec = JSON.parse(readFileSync(join(here, 'vectors', 'rng_gacha_vectors.json'), 'utf8'));
  for (const v of vec.gacha) {
    const g = MF.Gacha.newState(v.seed); g.crystals = 10 ** 9;
    const got = []; for (let i = 0; i < v.results.length / 10; i++) got.push(...MF.Gacha.pull(DATA, g, v.banner, 10).map((r) => `${r.rarity}:${r.id}`));
    assert.deepEqual(got, v.results, v.banner);
  }
});

// ───────── parity scaling (review fixes) ─────────
test('echo-circuit and copy tokens stay cost 0 after the turn they were created', () => {
  const b = battle({ deck: Array(10).fill('sera_phase'), enemies: ['e_shelf'], difficulty: { hp: 400, dmg: 10 } });
  const u = b.s.zones.hand[0]; b.s.cards[u].mods.c = 'c_echo';
  b.s.ap = 9;
  b.do('play', { uid: u, mode: 'now', target: b.s.enemies[0].uid }); // resolves now → copy scheduled for turn 2
  assert.deepEqual((b.s.pendingCopies || []).map((x) => [x.id, x.due]), [['sera_phase', 2]]);
  assert.ok(!Object.values(b.s.cards).some((c) => c.echoCopy), 'no instance yet, so it cannot be shuffled away');
  b.do('end'); // turn 2: copy is created straight into the hand
  const copy = Object.values(b.s.cards).find((c) => c.echoCopy);
  assert.ok(copy && copy.free, 'copy is a free token');
  assert.equal(copy.mods.c, null, 'the copy carries no circuit');
  assert.ok(b.s.zones.hand.includes(copy.uid), 'copy in hand on turn 2');
  assert.equal(b.costOf(copy.uid, 'now'), 0);
  b.do('end'); // even if it is discarded and drawn again later it stays free
  assert.equal(b.costOf(copy.uid, 'now'), 0);
});

test('defence and heals scale with vitality, flat player damage with power, enemy flat damage with stage %', () => {
  const lv60 = [{ charId: 'sera', hp: 120, power4: 160 }, { charId: 'harin', hp: 120, power4: 160 }, { charId: 'aila', hp: 120, power4: 160 }];
  const mk = (party, deck, enemies, difficulty) => { const b = MF.Battle.create(DATA, { party, deck: deck.map((id) => ({ id })), relics: [], hp: 200, maxHp: 360, seed: 3, enemies, difficulty }); b.do('begin'); return b; };
  // common block (com_guard 6) uses party-average vitality: 6 × 120/81 = 8.9 → 8
  const b1 = mk(lv60, Array(10).fill('com_guard'), ['e_shelf']);
  b1.do('play', { uid: b1.s.zones.hand[0], mode: 'now' });
  assert.equal(b1.s.block, Math.floor(6 * 120 / 81));
  // a character heal (aila) scales with that character's vitality
  const b2 = mk(lv60, Array(10).fill('com_patch'), ['e_shelf']);
  const hp0 = b2.s.hp; b2.do('play', { uid: b2.s.zones.hand[0], mode: 'now' });
  assert.equal(b2.s.hp - hp0, Math.floor(6 * 120 / 81));
  // counter damage scales with party power (power4 240 → ×1.5)
  const strong = lv60.map((p) => ({ ...p, power4: 240 }));
  const b3 = mk(strong, Array(10).fill('com_guard'), ['e_shelf']);
  b3.s.counter = 4; const e = b3.s.enemies[0]; e.block = 0; const ehp = e.hp;
  b3.enemyHit(e, 1);
  assert.equal(ehp - b3.s.enemies[0].hp, Math.floor(4 * 240 / 160));
  // reflect (enemy-originated flat damage) scales with the stage damage %
  const b4 = mk(lv60, Array(10).fill('sera_round'), ['e_shelf'], { hp: 100, dmg: 148 });
  b4.s.block = 0; b4.s.enemies[0].st.reflect = 4; const h4 = b4.s.hp;
  b4.do('play', { uid: b4.s.zones.hand[0], mode: 'now', target: b4.s.enemies[0].uid });
  assert.equal(h4 - b4.s.hp, Math.round(4 * 148 / 100));
  // reference battle is unchanged: 81 vitality, power4 160
  const b5 = battle({ deck: Array(10).fill('com_guard'), enemies: ['e_shelf'] });
  b5.do('play', { uid: b5.s.zones.hand[0], mode: 'now' });
  assert.equal(b5.s.block, 6);
});

test('shop offers two distinct relics; event options respect their stated rarity', () => {
  for (let seed = 1; seed <= 40; seed++) {
    const r = MF.Run.create(DATA, { party: PARTY, seed });
    const shop = r.makeShop();
    assert.equal(shop.relics.length, 2);
    assert.notEqual(shop.relics[0].id, shop.relics[1].id);
    const commonPart = r.partChoices(false, 1, true)[0];
    assert.equal(IX.parts[commonPart].rarity, 'common');
    const rel = r.relicChoices('common')[0];
    assert.equal(IX.relics[rel].rarity, 'common');
  }
});
