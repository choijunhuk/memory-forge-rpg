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
  const b = MF.Battle.create(DATA, { party, deck: deck.map((id) => ({ id })), relics, hp, maxHp: 243, seed, enemies, difficulty });
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

test('reservation slots: next 2, after 1, SLOT_FULL, resolve at next turn start with rv', () => {
  const b = battle({ deck: Array(12).fill('sera_round'), enemies: ['e_shelf'] }); // 46 HP, weak flame/void
  const t = b.s.enemies[0].uid;
  const h = () => b.s.zones.hand[0];
  b.do('play', { uid: h(), mode: 'next', target: t });
  b.do('play', { uid: h(), mode: 'next', target: t });
  assert.equal(b.freeSlots('next'), 0);
  assert.equal(b.check(h(), 'next', t), 'SLOT_FULL');
  b.do('play', { uid: h(), mode: 'after', target: t });
  assert.equal(b.check(h(), 'after', t), 'SLOT_FULL');
  const hp0 = b.s.enemies[0].hp; const block0 = b.s.enemies[0].block;
  b.do('end');
  // two reserved rounds (rv 12 each) resolved at turn 2 start, before the draw
  const lost = (hp0 + block0) - (b.s.enemies[0].hp + b.s.enemies[0].block);
  assert.ok(lost >= 24 - block0 - 10, `lost ${lost}`); // enemy may have gained block during its turn
  assert.equal(b.s.res.length, 1); // the after-next one is still waiting
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
  assert.equal(hp - b2.s.enemies[0].hp, 10); // 8 × 1.25
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
  b.do('play', { uid: u, mode: 'next', target: b.s.enemies[0].uid });
  b.do('end'); // turn 2: reservation resolves → copy scheduled for turn 3 (no instance yet, cannot be shuffled away)
  assert.equal((b.s.pendingCopies || []).length, 1);
  assert.ok(!Object.values(b.s.cards).some((c) => c.echoCopy));
  b.do('end'); // turn 3: copy is created straight into the hand
  const copy = Object.values(b.s.cards).find((c) => c.echoCopy);
  assert.ok(copy && copy.free, 'copy is a free token');
  assert.ok(b.s.zones.hand.includes(copy.uid), 'copy in hand on turn 3');
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
