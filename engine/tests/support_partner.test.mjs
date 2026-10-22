// S4 isolated support action, query and compatibility regression cases.
// Owned non-party support (docs/plans/contract-account-progression-s4-20261021.md): one deployed caller spends its full individual gauge
// to fire the frozen partner's authored ultimate once per Battle. Expectations come from authored data, Stats.derive and the documented
// damage/block/heal formulas, or from the same ultimate fired by a deployed actor with every party/passive/start difference named.
// CREATE/copy/pending/reservation/pick/loan fixtures edit a CLONE of the data only; production content is never changed.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFileSync } from 'node:fs';
import { gunzipSync } from 'node:zlib';

const require = createRequire(import.meta.url);
const MF = require('../rules-core.js');
const CM = require('../content-modes.js');
const DATA = require('../../data/game_data.json');
const MODES = require('../../data/content_modes.json');
const DATA_TEXT = JSON.stringify(DATA);
const clone = (v) => JSON.parse(JSON.stringify(v));
const ix = MF.indexData(DATA);
const BPS = 10000;
const GAUGE = DATA.rules.gaugeMax;
const CHARS = DATA.characters.map((c) => c.id);
const charOf = (id) => DATA.characters.find((c) => c.id === id);
const code = (fn) => { try { fn(); } catch (e) { if (e instanceof MF.RuleError) return e.code; throw e; } return null; };
const cmd = (b, kind, args = {}, rid = `${kind}-${b.s.v}`) => b.command({ rid, v: b.s.v, kind, args });
const handOf = (b, id) => b.s.zones.hand.find((u) => b.s.cards[u].id === id);

// ───── frozen builds: Lv1 basic R weapon / Lv60 authored SSR refine5 armor+15 accessory+15 ranks10 ─────
const BASIC = { ranged: 'w_basic_ranged', melee: 'w_basic_melee', device: 'w_basic_device', ritual: 'w_basic_ritual' };
const STANDARD_SSR = { ranged: 'w_star_bow', melee: 'w_frost_blade', device: 'w_clock_core', ritual: 'w_silver_hymnal' };
function build(charId, res, kind) {
  const ch = charOf(charId);
  if (kind === 'basic') return { charId, level: 1, asc: 0, res, weaponId: BASIC[ch.weapon], weaponLevel: 1, refine: 1, armor: null, accessory: null, skillLv: 1, ultLv: 1 };
  const signature = DATA.weapons.find((w) => w.signature === charId);
  return { charId, level: 60, asc: 4, res, weaponId: signature ? signature.id : STANDARD_SSR[ch.weapon], weaponLevel: 60, refine: 5, armor: 15, accessory: 15, skillLv: 10, ultLv: 10 };
}
// the trusted Battle-level input, derived exactly like a party entry by the existing Stats.derive
function battleSupport(b) {
  const d = MF.Stats.derive(ix, b);
  return { charId: b.charId, res: b.res, weaponId: b.weaponId, refine: b.refine, power4: d.power4, hp: d.hp, gearBps: d.gearBps, skillLv: d.skillLv, ultLv: d.ultLv };
}
const flat = (fx) => (fx.type === 'multi' ? fx.list.flatMap(flat) : [fx]);
const nodes = (charId, res) => charOf(charId).resonance.slice(0, res).flatMap((n) => flat(n.fx));
// independent frozen source graph from authored resonance nodes: ranks/card mods/ult replace+extra/hooks once; party effects never
function expectedSource(b) {
  const ch = charOf(b.charId); const d = MF.Stats.derive(ix, b); const fx = nodes(b.charId, b.res);
  const add = (type) => fx.filter((f) => f.type === type).reduce((n, f) => n + f.delta, 0);
  const replace = fx.filter((f) => f.type === 'ultReplace');
  return { id: ch.id, name: ch.name, attr: ch.attr, res: b.res, power4: d.power4, vit: d.hp, gearBps: d.gearBps, skillLv: d.skillLv + add('skillLv'),
    ultLv: d.ultLv + add('ultLv'), weapon: { id: b.weaponId, refine: b.refine }, cardMods: fx.filter((f) => f.type === 'cardMod'),
    ultExtra: fx.filter((f) => f.type === 'ultExtra').map((f) => ({ eff: f.eff, once: !!f.once })), ultReplace: replace.length ? replace.at(-1).eff : null,
    hooks: fx.filter((f) => f.type === 'hook') };
}
function expectedText(src) {
  const ops = [...(src.ultReplace || charOf(src.id).ult.eff), ...src.ultExtra.flatMap((x) => x.eff)];
  let text = ops.map((op) => MF.opText(op, ix)).filter(Boolean).join('. ') + '.';
  if (src.hooks.some((h) => h.id === 'aila_r6')) text += ' 필살기의 초과 회복은 100% 방어로 전환.';
  if (src.hooks.some((h) => h.id === 'sera_r6')) text += ' 필살기를 쓴 다음 턴, 세라의 카드 공격 피해 +50%.';
  return text;
}
// documented ult arithmetic with one floor: power4/160 · ult level (+4%/level) · (1 + additive bps) ; block vit/81 ; heal without gear
const lvMul = (lv) => BigInt(BPS + 400 * (lv - 1));
const dmgL = (base, src, extra = 0) => Number(BigInt(base) * BigInt(src.power4) * lvMul(src.ultLv) * BigInt(BPS + src.gearBps + extra) / (160n * 10000n * 10000n));
const blkL = (base, src) => Number(BigInt(base) * BigInt(src.vit) * lvMul(src.ultLv) * BigInt(BPS + src.gearBps) / (81n * 10000n * 10000n));
const healL = (base, src) => Number(BigInt(base) * BigInt(src.vit) * lvMul(src.ultLv) / (81n * 10000n));
const dmgEv = (uid, n) => ({ e: 'dmg', uid, n, blocked: 0, src: 'ult', card: null });

// ───── the paired support battle and ordinary-ult oracle: same deck, seed, enemies, HP and frozen stats; weaponless oracle ─────
const ENEMIES = ['b_archivist', 'b_garden'];
const DECK = [{ id: 'yuri_cocoon' }, { id: 'yuri_sprout', growth: 2 }, { id: 'yuri_root', growth: 1 }, { id: 'com_slash' }, { id: 'com_guard' },
  { id: 'com_tactics' }, { id: 'com_pin' }, { id: 'com_slash' }, { id: 'com_guard' }, { id: 'com_barrier' }];
const callerFor = (charId) => (charId === 'sera' ? 'nia' : 'sera');
function pair(charId, res, kind, extra = {}) {
  const b = build(charId, res, kind); const sup = battleSupport(b); const src = expectedSource(b); const caller = callerFor(charId);
  const stats = { power4: sup.power4, hp: sup.hp, gearBps: sup.gearBps, skillLv: sup.skillLv, ultLv: sup.ultLv };
  const common = { deck: clone(DECK), hp: charId === 'aila' ? 100 : 197, maxHp: 200, seed: 31, enemies: [...ENEMIES], relics: [], ...extra };
  const support = MF.Battle.create(DATA, { ...clone(common), party: [{ charId: caller, ...stats, gauge: GAUGE }], support: sup });
  const oracle = MF.Battle.create(DATA, { ...clone(common), party: [{ charId, res, ...stats, gauge: GAUGE }] });
  support.do('begin'); oracle.do('begin');
  return { b, sup, src, caller, support, oracle };
}

// ───── no-support compatibility: the existing full-state corpora regenerate exactly; only the reviewed engine pins move ─────
const CORPORA = [['battle player actions', 'gen_battle_player_action_vectors'], ['battle player queries', 'gen_battle_player_query_vectors'],
  ['battle preview', 'gen_battle_preview_vectors'], ['battle turns', 'gen_battle_turn_vectors'], ['battle openings', 'gen_battle_opening_vectors'],
  ['battle late actions', 'gen_battle_late_actions_vectors'], ['run progression', 'gen_run_progression_vectors'], ['run memory', 'gen_run_memory_vectors'],
  ['run create', 'gen_run_create_vectors'], ['content modes', 'gen_content_modes_vectors'], ['memory rules', 'gen_memory_rule_vectors']];
const ENGINE_PATHS = ['engine/rules-core.js', 'engine/content-modes.js'];
for (const [name, file] of CORPORA) test(`no-support ${name} corpus regenerates byte-identically apart from the S4 engine source pins`, async () => {
  const gen = await import(`../../tools/${file}.mjs`);
  const committed = JSON.parse(gunzipSync(readFileSync(gen.fixturePath)).toString('utf8'));
  const fresh = gen.generateCases();
  assert.deepEqual(Object.keys(fresh.sources), Object.keys(committed.sources));
  for (const [key, row] of Object.entries(fresh.sources)) if (row.sha256 !== committed.sources[key].sha256)
    assert.ok(ENGINE_PATHS.includes(row.path) && row.path === committed.sources[key].path, `${name}: ${row.path} changed outside the S4 engine slice`);
  const comparable = { ...fresh, sources: committed.sources };
  if (Object.hasOwn(committed, 'nativeDiagnosticsRuntime')) comparable.nativeDiagnosticsRuntime = committed.nativeDiagnosticsRuntime;
  if (Object.hasOwn(committed, 'inputManifestAudit')) {
    const strip = (a) => ({ ...a, manifestMatch: null, preExistingChangedPaths: a.preExistingChangedPaths.filter((p) => !ENGINE_PATHS.includes(p)) });
    assert.deepEqual(strip(fresh.inputManifestAudit), strip(committed.inputManifestAudit));
    comparable.inputManifestAudit = committed.inputManifestAudit;
  }
  assert.ok(JSON.stringify(comparable) === JSON.stringify(committed), `${name}: a no-support state, log, event, query, receipt or hash differs from the committed witness`);
});

test('support is omitted when absent; when present it adds only {source, used} and no actor, HP, card, passive, start effect or party rule', () => {
  const base = { party: [{ charId: 'harin' }], deck: clone(DECK), hp: 150, maxHp: 200, seed: 9, enemies: ['e_moth', 'e_cog'], relics: [] };
  const plain = MF.Battle.create(DATA, clone(base));
  for (const absent of [undefined, null]) {
    const b = MF.Battle.create(DATA, { ...clone(base), support: absent });
    assert.equal(JSON.stringify(b.s), JSON.stringify(plain.s)); assert.equal(JSON.stringify(b.publicView()), JSON.stringify(plain.publicView()));
  }
  plain.do('begin'); cmd(plain, 'end');
  assert.ok(!/support|"caller"/.test(JSON.stringify([plain.s, plain.publicView(), plain.receipts])), 'a legacy battle graph carries a support member');
  for (const charId of CHARS.filter((c) => c !== 'harin')) for (let res = 0; res <= 6; res++) for (const kind of ['basic', 'advanced']) {
    const label = `${charId} r${res} ${kind}`; const b = build(charId, res, kind);
    const withS = MF.Battle.create(DATA, { ...clone(base), support: battleSupport(b) }); const without = MF.Battle.create(DATA, clone(base));
    for (const step of ['create', 'begin']) {
      if (step === 'begin') { withS.do('begin'); without.do('begin'); }
      const { support, ...rest } = withS.s;
      assert.equal(JSON.stringify(rest), JSON.stringify(without.s), `${label} ${step}: support changed party, deck, HP, RNG, log, rules or start effects`);
      assert.deepEqual(Object.keys(withS.s), [...Object.keys(without.s), 'support']);
      assert.equal(JSON.stringify(support), JSON.stringify({ source: expectedSource(b), used: false }), `${label}: frozen source graph`);
      const { support: summary, ...view } = withS.publicView();
      assert.equal(JSON.stringify(view), JSON.stringify(without.publicView())); assert.deepEqual(summary, { charId, used: false, gaugeCost: GAUGE });
    }
  }
});

test('Battle support input is trusted-derived but strict, and its frozen source is detached from input, data and other battles', () => {
  const ok = battleSupport(build('kaya', 4, 'advanced'));
  const setup = (support, party = [{ charId: 'sera' }]) => ({ party, deck: [], hp: 81, maxHp: 81, seed: 1, enemies: ['e_page'], support });
  const create = (support, party) => code(() => MF.Battle.create(DATA, setup(support, party)));
  assert.equal(create(ok), null);
  assert.equal(create(ok, [{ charId: 'kaya' }]), 'SUPPORT_IN_PARTY');
  for (const charId of ['nobody', 'constructor', undefined]) assert.equal(create({ ...ok, charId }), 'UNKNOWN_CHARACTER');
  for (const bad of [{ res: 7 }, { res: -1 }, { refine: 0 }, { refine: 6 }, { power4: 0 }, { hp: undefined }, { gearBps: -1 }, { skillLv: 0 }, { ultLv: 1.5 },
    { weaponId: undefined }, { weaponId: 'w_nothing' }, { weaponId: 'toString' }]) assert.equal(create({ ...ok, ...bad }), 'INVALID_SNAPSHOT', JSON.stringify(bad));
  const input = clone(ok); const b = MF.Battle.create(DATA, setup(input)); const frozen = clone(b.s.support);
  input.power4 = 1; input.res = 0;
  assert.deepEqual(b.s.support, frozen);
  b.s.support.source.cardMods.push({ junk: true }); b.s.support.source.hooks.length = 0;
  assert.equal(JSON.stringify(DATA), DATA_TEXT);
  assert.deepEqual(MF.Battle.create(DATA, setup(clone(ok))).s.support, frozen);
});

// ───── all ten authored ultimates × resonance 0..6 × basic/advanced frozen builds ─────
function literal(id, res, src, before, after, ev, target) {
  const alive = before.enemies.filter((e) => e.alive).map((e) => e.uid);
  switch (id) {
    case 'sera': {
      assert.deepEqual(ev, [dmgEv(target, dmgL(28, src))]);
      assert.equal(after.zones.hand.length, before.zones.hand.length + 1);
      const t0 = before.enemies.find((e) => e.uid === target); const t1 = after.enemies.find((e) => e.uid === target);
      assert.equal(t1.brk, t0.weak.includes('volt') ? t0.brk - 4 : t0.brk);
      assert.equal(after.battleFlags.seraR6Turn, res >= 6 ? after.turn + 1 : undefined);
      break;
    }
    case 'nia':
      for (const u of before.zones.hand) assert.equal(after.cards[u].costMod, -1);
      assert.equal(after.zones.hand.length, before.zones.hand.length + 1); assert.equal(after.cards[after.zones.hand.at(-1)].costMod, 0);
      assert.deepEqual(ev, res >= 6 ? after.zones.hand.map((uid) => ({ e: 'tune', uid, part: 'amp' })) : []);
      break;
    case 'yuri': {
      const cards = clone(before.cards); const out = [];
      const lowest = Object.values(cards).filter((c) => ix.cards[c.id].growth && c.growth < 3).sort((a, b) => a.growth - b.growth || (a.uid < b.uid ? -1 : 1)).slice(0, 2);
      for (const c of lowest) { c.growth += 1; out.push({ e: 'grow', uid: c.uid, growth: c.growth, src: 'growLowest' }); }
      out.push({ e: 'block', n: blkL(12, src), src: 'card' });
      if (res >= 6) for (const c of Object.values(cards)) if (ix.cards[c.id].growth && c.growth < 3) { c.growth += 1; out.push({ e: 'grow', uid: c.uid, growth: c.growth, src: 'growAll' }); }
      assert.deepEqual(ev, out);
      assert.equal(after.battleFlags.ultOnce_yuri, res >= 6 ? true : undefined);
      break;
    }
    case 'ravien':
      assert.deepEqual(ev, [...alive.map((u) => dmgEv(u, dmgL(6, src))), ...alive.map((u) => dmgEv(u, dmgL(6, src)))]);
      assert.equal(after.apNext, before.apNext + (res >= 6 ? 1 : 0));
      break;
    case 'harin':
      assert.deepEqual(ev, [{ e: 'block', n: blkL(30, src), src: 'card' }]);
      assert.equal(after.counter, before.counter + 6);
      assert.deepEqual(after.buffs.filter((x) => x.s === 'counterDouble'), res >= 6 ? [{ s: 'counterDouble', n: 1, until: after.turn + 1 }] : []);
      break;
    case 'rien':
      assert.deepEqual(ev, []); assert.equal(after.freeNext, res >= 6 ? 3 : 2); assert.equal(after.echoActive, false); assert.equal(after.echo, 0);
      break;
    case 'aila':
      assert.deepEqual(ev, [{ e: 'heal', n: healL(20, src) }, { e: 'block', n: blkL(10, src), src: 'card' }]);
      break;
    case 'sion': {
      const hand = before.zones.hand; const n = dmgL(7, src);
      assert.deepEqual(ev, [...hand.map((uid) => ({ e: 'exhaust', uid })), ...hand.flatMap(() => alive.map((u) => dmgEv(u, n)))]);
      assert.equal(after.zones.hand.length, res >= 6 ? Math.min(5, hand.length) : 0);
      break;
    }
    case 'kaya':
      assert.deepEqual(ev, [...alive.map((uid) => ({ e: 'status', uid, s: 'mark', n: 3 })), ...alive.map((u) => dmgEv(u, dmgL(15, src)))]);
      break;
    case 'lumi':
      assert.equal(ev.length, res >= 6 ? 8 : 5);
      for (const hit of ev) { assert.ok(alive.includes(hit.uid)); assert.deepEqual(hit, dmgEv(hit.uid, dmgL(8, src))); }
      assert.notDeepEqual(after.rng, before.rng);
      break;
    default: assert.fail(`no literal oracle for ${id}`);
  }
}
for (const charId of CHARS) test(`${charId}: support ult r0..6 × basic/advanced equals literal effects and the ordinary ult minus named deployed-only differences`, () => {
  for (let res = 0; res <= 6; res++) for (const kind of ['basic', 'advanced']) {
    const label = `${charId} r${res} ${kind}`;
    const { src, caller, support, oracle } = pair(charId, res, kind);
    assert.equal(support.supportNeedsTarget(), charId === 'sera' ? 'one' : null);
    const target = charId === 'sera' ? support.alive()[0].uid : null;
    const sBefore = clone(support.s); const oBefore = clone(oracle.s);
    const rs = clone(cmd(support, 'support', { caller, target }, `${label}-support`));
    const ro = clone(cmd(oracle, 'ult', { actor: charId, target }, `${label}-ult`));
    assert.deepEqual(rs.ev[0], { e: 'support', actor: charId, caller }); assert.deepEqual(ro.ev[0], { e: 'ult', actor: charId });
    assert.ok(rs.ev.slice(1).every((e) => !Object.hasOwn(e, 'caller')));
    assert.equal(support.actor(caller).gauge, 0); assert.equal(support.s.support.used, true);
    assert.equal(JSON.stringify(support.s.support.source), JSON.stringify(src), `${label}: frozen source changed`);
    assert.equal(support.s.actors.length, 1); assert.equal(support.s.hp, sBefore.hp + (charId === 'aila' ? healL(20, src) : 0));
    literal(charId, res, src, sBefore, support.s, rs.ev.slice(1), target);
    // the same authored ultimate fired by a deployed actor: only its own passive, start effects, party rules and gauge differ
    let se = rs.ev.slice(1); let oe = ro.ev.slice(1);
    if (charId === 'sion') {
      const p = { ...charOf('sion').passive.params }; let start = 0;
      for (const f of nodes('sion', res)) { if (f.type === 'passiveParam') p[f.param] = f.value; if (f.type === 'rule' && f.key === 'exhaustStart') start = Math.max(start, f.value); }
      const shelf = Math.min(p.maxBps, p.perBps * (oBefore.zones.hand.length + start));
      for (const e of oe.filter((x) => x.e === 'dmg')) assert.equal(e.n, dmgL(7, src, shelf), `${label}: deployed Sion shelf`);
      const mask = (list) => list.map((e) => (e.e === 'dmg' ? { ...e, n: 'source arithmetic' } : e));
      se = mask(se); oe = mask(oe);
    }
    if (charId === 'nia' && res >= 6) {
      const pre = Object.values(oBefore.cards).filter((c) => c.temp.includes('amp')).map((c) => c.uid);
      assert.equal(pre.length, 1, `${label}: deployed Nia r4 battle-start tune`);
      se = se.filter((e) => !(e.e === 'tune' && pre.includes(e.uid)));
    }
    assert.deepEqual(se, oe, `${label}: support effects differ from the ordinary ult beyond the named differences`);
    if (charId === 'harin') assert.equal((oracle.s.counter - oBefore.counter) - (support.s.counter - sBefore.counter), res >= 2 ? 2 : 1);
    if (charId === 'rien') assert.equal(oracle.s.echo, Math.min(res >= 2 ? 4 : 3, (res >= 4 ? 1 : 0) + 3));
    assert.equal(oracle.actor(charId).gauge, 0);
  }
});

test('support command: selected unused partner, PLAYER phase, deployed caller with a full gauge; rejections commit nothing; receipts replay', () => {
  const b = MF.Battle.create(DATA, { party: [{ charId: 'sera', gauge: GAUGE }, { charId: 'harin', gauge: GAUGE - 1 }, { charId: 'rien', gauge: GAUGE }],
    deck: clone(DECK), hp: 150, maxHp: 200, seed: 5, enemies: ['b_archivist'], support: battleSupport(build('aila', 0, 'basic')) });
  const reject = (args, expected, v = b.s.v) => {
    const state = clone(b.s), receipts = clone(b.receipts);
    assert.throws(() => b.command({ rid: `bad-${expected}`, v, kind: 'support', args }), (e) => e instanceof MF.RuleError && e.code === expected, expected);
    assert.deepEqual(b.s, state); assert.deepEqual(b.receipts, receipts);
  };
  reject({ caller: 'sera' }, 'WRONG_PHASE');
  b.do('begin');
  reject({ caller: 'harin' }, 'ULT_NOT_READY');
  for (const caller of ['aila', 'nobody', undefined, 'constructor']) reject({ caller }, 'UNKNOWN_ACTOR');
  reject({ caller: 'sera' }, 'STALE_VERSION', b.s.v - 1);
  reject({ caller: 'sera', picks: [b.s.zones.hand[0]] }, 'PICK_INVALID');
  reject({ caller: 'sera', picks: 7 }, 'INVALID_COMMAND_ARGS');
  const v = b.s.v; const harin = b.actor('harin').gauge;
  const response = clone(cmd(b, 'support', { caller: 'sera' }, 'support-1'));
  assert.deepEqual(response.ev[0], { e: 'support', actor: 'aila', caller: 'sera' });
  assert.deepEqual([b.actor('sera').gauge, b.actor('harin').gauge, b.actor('rien').gauge, b.s.support.used], [0, harin, GAUGE, true]);
  assert.deepEqual(b.s.log.filter((l) => l.e === 'support'), [{ t: 1, e: 'support', actor: 'aila', caller: 'sera' }]);
  reject({ caller: 'rien' }, 'SUPPORT_ALREADY_USED');
  assert.deepEqual(b.command({ rid: 'support-1', v, kind: 'support', args: { caller: 'sera' } }), response);
  assert.throws(() => b.command({ rid: 'support-1', v, kind: 'support', args: { caller: 'rien' } }), { code: 'IDEMPOTENCY_CONFLICT' });
  reject({ caller: 'rien' }, 'STALE_VERSION', v);
  assert.throws(() => b.command({ rid: 'competing-ult', v, kind: 'ult', args: { actor: 'rien' } }), { code: 'STALE_VERSION' });
  assert.equal(cmd(b, 'ult', { actor: 'rien' }).ev[0].e, 'ult');
  const none = MF.Battle.create(DATA, { party: [{ charId: 'sera', gauge: GAUGE }], deck: clone(DECK), hp: 81, maxHp: 81, seed: 5, enemies: ['e_page'] });
  none.do('begin');
  assert.equal(code(() => cmd(none, 'support', { caller: 'sera' })), 'SUPPORT_NOT_SELECTED');
  assert.equal(code(() => none.supportInfo('sera')), 'SUPPORT_NOT_SELECTED'); assert.equal(code(() => none.supportNeedsTarget()), 'SUPPORT_NOT_SELECTED');
  assert.equal(none.supportChoices('sera').error, 'SUPPORT_NOT_SELECTED'); assert.equal(none.supportForecast('sera').error, 'SUPPORT_NOT_SELECTED');
});

test('lethal first effect still spends 100 and the use once; Kaya never refunds the caller; Rien echo stays inert; Aila r6 overflow; Sera target/break', () => {
  const sera = battleSupport(build('sera', 0, 'advanced')); const src = expectedSource(build('sera', 0, 'advanced'));
  const lethal = MF.Battle.create(DATA, { party: [{ charId: 'nia', gauge: GAUGE }], deck: clone(DECK), hp: 81, maxHp: 81, seed: 2, enemies: ['e_page'], support: sera });
  lethal.do('begin'); const hand = lethal.s.zones.hand.length; const page = lethal.alive()[0].uid;
  assert.ok(dmgL(28, src) >= lethal.alive()[0].hp);
  const r = cmd(lethal, 'support', { caller: 'nia' });
  assert.deepEqual(r.ev, [{ e: 'support', actor: 'sera', caller: 'nia' }, dmgEv(page, dmgL(28, src)), { e: 'kill', uid: page, id: 'e_page' }, { e: 'end', phase: 'VICTORY' }]);
  assert.deepEqual([r.phase, lethal.actor('nia').gauge, lethal.s.support.used, lethal.s.zones.hand.length], ['VICTORY', 0, true, hand]);
  assert.equal(code(() => cmd(lethal, 'support', { caller: 'nia' })), 'SUPPORT_ALREADY_USED');
  // Kaya r6 kill: the deployed Kaya refunds her own 50 and her passive draws; the support refunds nobody and adds no passive
  const kaya = pair('kaya', 6, 'advanced', { enemies: ['e_page', 'b_archivist'] });
  const ap = kaya.support.s.ap; const supHand = kaya.support.s.zones.hand.length;
  cmd(kaya.support, 'support', { caller: 'sera' }); cmd(kaya.oracle, 'ult', { actor: 'kaya' });
  assert.ok(kaya.support.s.enemies.some((e) => !e.alive) && kaya.oracle.s.enemies.some((e) => !e.alive));
  assert.deepEqual([kaya.support.actor('sera').gauge, kaya.support.s.ap, kaya.support.s.zones.hand.length], [0, ap, supHand]);
  assert.equal(kaya.oracle.actor('kaya').gauge, 50);
  assert.ok(!expectedText(kaya.src).includes('환급') && !kaya.support.supportInfo('nia').text.includes('환급'));
  // Rien support with the echo relic: freeNext still executes, echo needs a deployed Rien
  const rien = MF.Battle.create(DATA, { party: [{ charId: 'sera', gauge: GAUGE }], deck: [{ id: 'com_barrier' }], hp: 150, maxHp: 200, seed: 4, enemies: ['b_archivist'],
    relics: ['tuning_fork'], support: battleSupport(build('rien', 6, 'basic')) });
  rien.do('begin'); cmd(rien, 'support', { caller: 'sera' });
  assert.deepEqual([rien.s.echoActive, rien.s.echo, rien.s.freeNext], [false, 0, 3]);
  const paid = handOf(rien, 'com_barrier'); cmd(rien, 'play', { uid: paid, mode: 'now' });
  assert.equal(rien.s.freeNext, 2);
  // Aila r6 overflow: the frozen hook converts all of the ult's excess heal; r5 has no Aila passive to convert any
  for (const res of [5, 6]) {
    const a = expectedSource(build('aila', res, 'advanced'));
    const ab = MF.Battle.create(DATA, { party: [{ charId: 'sera', gauge: GAUGE }], deck: clone(DECK), hp: 197, maxHp: 200, seed: 4, enemies: ['b_archivist'],
      support: battleSupport(build('aila', res, 'advanced')) });
    ab.do('begin'); const ev = cmd(ab, 'support', { caller: 'sera' }).ev;
    assert.deepEqual(ev.filter((e) => e.e === 'heal'), [{ e: 'heal', n: 3 }]);
    assert.deepEqual(ev.filter((e) => e.src === 'warmth'), res >= 6 ? [{ e: 'block', n: healL(20, a) - 3, src: 'warmth' }] : []);
    assert.equal(expectedText(a).endsWith('필살기의 초과 회복은 100% 방어로 전환.'), res >= 6);
  }
  // Sera: explicit live target among two, its volt break and break AP; no target or a dead target is BAD_TARGET without commit
  const sb = MF.Battle.create(DATA, { party: [{ charId: 'nia', gauge: GAUGE }], deck: clone(DECK), hp: 150, maxHp: 200, seed: 6, enemies: ['e_page', 'e_moth'],
    support: battleSupport(build('sera', 6, 'basic')) });
  sb.do('begin'); const [p1, moth] = sb.alive().map((e) => e.uid); const before = clone(sb.s);
  for (const target of [null, 'e999']) { assert.equal(code(() => cmd(sb, 'support', { caller: 'nia', target })), 'BAD_TARGET'); assert.deepEqual(sb.s, before); }
  assert.equal(sb.supportInfo('nia').code, 'BAD_TARGET');
  const ev = cmd(sb, 'support', { caller: 'nia', target: moth }).ev;
  const sSrc = expectedSource(build('sera', 6, 'basic'));
  assert.deepEqual(ev.slice(1, 3), [dmgEv(moth, dmgL(28, sSrc)), { e: 'break', uid: moth }]);
  assert.equal(sb.s.enemies.find((e) => e.uid === p1).hp, before.enemies.find((e) => e.uid === p1).hp);
  assert.equal(sb.s.apNext, before.apNext + DATA.rules.breakApBonus); assert.equal(sb.s.battleFlags.seraR6Turn, sb.s.turn + 1);
  assert.ok(sb.supportInfo('nia').text.endsWith('필살기를 쓴 다음 턴, 세라의 카드 공격 피해 +50%.'));
});

test('pure support info/needsTarget/choices/forecast keep the live battle exact; visible forecasts equal the real command, draw/random hide all numbers', () => {
  const blank = { error: null, mode: 'now', cost: GAUGE, due: null, hidden: false, enemyHpLoss: 0, blockDelta: 0, hpDelta: 0, defeat: false, enemyDamage: 0, newlyBroken: 0, enemyHpLosses: [] };
  const outcome = (before, after) => {
    const out = { ...blank, enemyHpLosses: [], blockDelta: after.block - before.block, hpDelta: after.hp - before.hp, defeat: after.phase === 'DEFEAT' };
    for (const e of before.enemies) {
      const now = after.enemies.find((x) => x.uid === e.uid); const lost = Math.max(0, e.hp - now.hp);
      out.enemyHpLoss += lost; if (lost) out.enemyHpLosses.push({ uid: e.uid, hpLoss: lost }); out.enemyDamage += Math.max(0, e.hp + e.block - now.hp - now.block);
    }
    out.newlyBroken = after.enemies.filter((e) => e.brokenUntil && before.enemies.some((x) => x.uid === e.uid && !x.brokenUntil)).length;
    return out;
  };
  for (const charId of CHARS) for (const res of [0, 4, 6]) {
    const label = `${charId} r${res}`; const { src, caller, support: b } = pair(charId, res, 'basic');
    const target = charId === 'sera' ? b.alive()[0].uid : null;
    const state = clone(b.s), receipts = clone(b.receipts);
    const info = b.supportInfo(caller, target, []); const need = b.supportNeedsTarget(); const choices = b.supportChoices(caller, []);
    const extra = b.supportChoices(caller, [b.s.zones.hand[0]]); const forecast = b.supportForecast(caller, target, []);
    assert.deepEqual(b.s, state, `${label}: a query changed state, RNG, version, gauge or use`); assert.deepEqual(b.receipts, receipts);
    assert.deepEqual(info, { kind: 'support', caller, charId, name: charOf(charId).name, ultName: charOf(charId).ult.name, text: expectedText(src), attr: src.attr,
      gaugeCost: GAUGE, used: false, ready: true, code: null, needsTarget: need, target, picks: [] });
    assert.deepEqual(choices, { error: null, uid: '', mode: 'now', requests: [], accepted: 0, index: 0, left: 0, blocked: null, candidates: [] });
    assert.equal(extra.error, 'PICK_INVALID'); assert.equal(extra.accepted, 0);
    const hidden = ['sera', 'nia', 'lumi'].includes(charId) || (charId === 'sion' && res >= 6);
    const before = clone(b.s); cmd(b, 'support', { caller, target });
    assert.deepEqual(forecast, hidden ? { ...blank, hidden: true } : outcome(before, b.s), `${label}: forecast`);
    const used = b.supportInfo(caller); assert.deepEqual([used.used, used.ready, used.code], [true, false, 'SUPPORT_ALREADY_USED']);
    assert.deepEqual(b.supportForecast(caller), { ...blank, cost: 0, error: 'SUPPORT_ALREADY_USED' });
  }
  // an RNG or draw-pile movement hides a definition-visible support even when its visible result is fully determined
  const sion = pair('sion', 0, 'basic', { enemies: ['b_archivist'], relics: ['void_shard'] }).support;
  assert.equal(sion.supportForecast('sera').hidden, true);
  const kaya = pair('kaya', 0, 'advanced', { enemies: ['e_page', 'b_archivist'], relics: ['detective_notebook'] }).support;
  assert.equal(kaya.supportForecast('sera').hidden, true);
  const ready = pair('kaya', 0, 'basic').support; ready.actor('sera').gauge = GAUGE - 1;
  const notReady = ready.supportInfo('sera');
  assert.deepEqual([notReady.ready, notReady.code, ready.supportChoices('sera').error, ready.supportForecast('sera').error], [false, 'ULT_NOT_READY', 'ULT_NOT_READY', 'ULT_NOT_READY']);
  assert.equal(ready.supportInfo('kaya').code, 'UNKNOWN_ACTOR');
});

// ───── isolated test-only fixtures (cloned data) for paths no production ultimate reaches ─────
function iso(mutate) { const d = clone(DATA); mutate(d); return d; }
const setUlt = (d, charId, eff) => { d.characters.find((c) => c.id === charId).ult.eff = eff; };
const addCard = (d, card) => d.cards.push({ rar: 'T', cost: 0, type: 'atk', res: 'n', kw: [], pool: false, attr: 'void', ...card });
const NIA = { charId: 'nia', res: 3, weaponId: 'w_basic_device', refine: 5, power4: 120, hp: 70, gearBps: 300, skillLv: 2, ultLv: 1 };
// Nia token arithmetic: own power4 120, skill rank 2+2 (r3), gear 300, R weapon refine5 w_none alt 1200 — never the party average
const tokenL = (base, extra = 0) => Number(BigInt(base) * 120n * BigInt(BPS + 400 * 3) * BigInt(BPS + 300 + 1200 + extra) / (160n * 10000n * 10000n));
const isoBattle = (data, party, deck, opts = {}) => {
  const b = MF.Battle.create(data, { party, deck, hp: 200, maxHp: 200, seed: opts.seed ?? 3, enemies: opts.enemies || ['b_archivist'], relics: opts.relics || [], support: opts.support ?? NIA });
  b.do('begin'); return b;
};

test('support-created own tokens carry provenance and frozen source arithmetic but never deployed ownership, gauge, echo or score', () => {
  const data = iso((d) => {
    setUlt(d, 'nia', [{ op: 'create', card: 'nia_drone_shot', n: 2, zone: 'hand' }, { op: 'create', card: 'com_slash', n: 1, zone: 'hand' }, { op: 'create', card: 'sera_round', n: 1, zone: 'hand' }]);
    d.characters.find((c) => c.id === 'nia').resonance[0].fx = { type: 'cardMod', cards: ['nia_drone_shot'], prop: 'dmg', add: 3 };
  });
  const b = isoBattle(data, [{ charId: 'sera', gauge: GAUGE, power4: 200, hp: 90 }, { charId: 'rien', power4: 150, hp: 80 }],
    [{ id: 'sera_round' }, { id: 'sera_round' }, { id: 'rien_note' }, { id: 'rien_duet' }, { id: 'com_guard' }], { relics: ['unfinished_score'] });
  const own = new Set(b.s.zones.hand); assert.equal(b.supportChoices('sera').requests.length, 0);
  cmd(b, 'support', { caller: 'sera' });
  const made = b.s.zones.hand.filter((u) => !own.has(u)).map((u) => b.s.cards[u]);
  assert.deepEqual(made.map((c) => [c.id, c.token, c.supportSourceId ?? null, Object.hasOwn(c, 'key')]),
    [['nia_drone_shot', true, 'nia', false], ['nia_drone_shot', true, 'nia', false], ['com_slash', true, null, false], ['sera_round', true, null, false]]);
  assert.equal(JSON.stringify(b.s.cards[made[0].uid]).endsWith('"from":null,"supportSourceId":"nia"}'), true);
  const target = b.alive()[0].uid;
  cmd(b, 'play', { uid: [...own].find((u) => b.s.cards[u].id === 'sera_round'), mode: 'now', target });
  const gauges = () => b.s.actors.map((a) => a.gauge); const g = gauges(); const echo = b.s.echo; const brk = b.alive()[0].brk;
  const shot = cmd(b, 'play', { uid: made[0].uid, mode: 'now', target });
  assert.deepEqual(shot.ev.find((e) => e.e === 'dmg'), { e: 'dmg', uid: target, n: tokenL(7), blocked: 0, src: 'card', card: 'nia_drone_shot' });
  assert.equal(b.alive()[0].brk, brk - 1, 'void token attribute breaks the void-weak enemy');
  assert.deepEqual([gauges(), b.s.lastOwner, b.s.ownersThisTurn, b.s.echo], [g, 'sera', ['sera'], echo]);
  cmd(b, 'play', { uid: [...own].find((u) => b.s.cards[u].id === 'rien_duet'), mode: 'now' });
  assert.deepEqual(b.s.nextBoost, { bps: 5000, from: 'rien' });
  const boosted = cmd(b, 'play', { uid: made[1].uid, mode: 'now', target });
  assert.equal(boosted.ev.find((e) => e.e === 'dmg').n, tokenL(7, 5000)); assert.equal(b.s.nextBoost, null);
  assert.deepEqual([b.s.ownersThisTurn, b.s.turnFlags.score], [['sera', 'rien'], undefined]);
  // the partner's ordinary deck card without provenance is not a support card: it keeps the existing absent-owner arithmetic
  const wrench = (support) => { const w = isoBattle(DATA, [{ charId: 'sera', power4: 200, hp: 90 }], [{ id: 'nia_wrench' }], { support }); return cmd(w, 'play', { uid: 'c1', mode: 'now' }).ev; };
  assert.deepEqual(wrench(NIA), wrench(null));
});

test('provenance survives copies, pending echo copies and reservations; copied deployed cards gain none; forged provenance commits nothing', () => {
  const data = iso((d) => {
    addCard(d, { id: 't_bud', name: '시험 새싹', owner: 'nia', growth: true, eff: [{ op: 'dmg', v: 3 }, { op: 'grow' }] });
    addCard(d, { id: 't_mine', name: '시험 지뢰', owner: 'nia', res: 'y', eff: [{ op: 'dmg', v: 5 }] });
    setUlt(d, 'nia', [{ op: 'create', card: 't_bud', n: 1, zone: 'hand' }, { op: 'create', card: 't_mine', n: 1, zone: 'hand' }, { op: 'tune', part: 'c_echo', pick: 'all' }]);
  });
  const b = isoBattle(data, [{ charId: 'yuri', gauge: GAUGE }], [{ id: 'yuri_metamorph' }, { id: 'yuri_metamorph' }, { id: 'yuri_cocoon' }, { id: 'com_guard' }, { id: 'com_guard' }]);
  cmd(b, 'support', { caller: 'yuri' });
  const bud = handOf(b, 't_bud'); const mine = handOf(b, 't_mine'); const cocoon = handOf(b, 'yuri_cocoon');
  const [m1, m2] = b.s.zones.hand.filter((u) => b.s.cards[u].id === 'yuri_metamorph');
  cmd(b, 'play', { uid: m1, mode: 'now', picks: [bud] }); cmd(b, 'play', { uid: m2, mode: 'now', picks: [cocoon] });
  const copies = b.s.zones.hand.map((u) => b.s.cards[u]).filter((c) => c.echoCopy);
  assert.deepEqual(copies.map((c) => [c.id, c.free, c.supportSourceId ?? null]), [['t_bud', true, 'nia'], ['yuri_cocoon', true, null]]);
  cmd(b, 'play', { uid: bud, mode: 'now' });
  assert.equal(JSON.stringify(b.s.pendingCopies.find((p) => p.id === 't_bud')), JSON.stringify({ id: 't_bud', wielder: null, from: null, due: 2, supportSourceId: 'nia' }));
  assert.ok(b.s.pendingCopies.filter((p) => p.id !== 't_bud').every((p) => !Object.hasOwn(p, 'supportSourceId')));
  cmd(b, 'play', { uid: mine, mode: 'next' });
  assert.deepEqual(b.s.res.map((r) => r.uid), [mine]);
  const next = cmd(b, 'end');
  assert.equal(next.ev.find((e) => e.e === 'dmg' && e.card === 't_mine').n, tokenL(5), 'the reserved token resolves with the frozen source');
  const echo = b.s.zones.hand.map((u) => b.s.cards[u]).find((c) => c.id === 't_bud' && c.echoCopy && c.temp.includes('echoHalf'));
  assert.deepEqual([echo.supportSourceId, echo.temp, echo.token], ['nia', ['echoHalf'], true]);
  assert.equal(cmd(b, 'play', { uid: echo.uid, mode: 'now' }).ev.find((e) => e.e === 'dmg').n, tokenL(3, -5000));
  // forged provenance: rejected at the next command without committing anything
  const forge = (mutate, support = NIA) => {
    const f = isoBattle(data, [{ charId: 'yuri', gauge: GAUGE }], [{ id: 'yuri_cocoon' }, { id: 'com_guard' }], { support }); mutate(f.s);
    const state = clone(f.s); assert.equal(code(() => cmd(f, 'end')), 'INVALID_SNAPSHOT'); assert.deepEqual(f.s, state);
  };
  forge((s) => { s.cards.c1.supportSourceId = 'nia'; }, null);
  forge((s) => { s.cards.c1.supportSourceId = 'nia'; s.cards.c1.token = true; });
  forge((s) => { s.cards.c1.supportSourceId = 'yuri'; s.cards.c1.token = true; });
  forge((s) => { s.support = null; });
  forge((s) => { s.support.source.id = 'yuri'; });
});

test('isolated support picks follow the shared pick policy and real resolver; bad picks, full pulls and loan overflow commit nothing', () => {
  const data = iso((d) => {
    setUlt(d, 'nia', [{ op: 'costdown', n: 1, pick: 'one' }, { op: 'recall' }, { op: 'growPick', n: 1 }, { op: 'tune', part: 'amp', pick: 'one' }]);
    setUlt(d, 'kaya', [{ op: 'pull' }]); setUlt(d, 'lumi', [{ op: 'loan', n: 9 }]);
    addCard(d, { id: 't_res', name: '시험 예약', owner: 'common', res: 'y', type: 'skl', attr: 'none', eff: [{ op: 'blk', v: 4 }] });
  });
  const deck = [{ id: 'yuri_cocoon' }, { id: 'yuri_sprout', growth: 2 }, { id: 'com_sort' }, { id: 'com_guard' }, { id: 'com_barrier' }];
  const b = isoBattle(data, [{ charId: 'yuri', gauge: GAUGE }], clone(deck));
  const [cocoon, sprout, sort, guard, barrier] = ['yuri_cocoon', 'yuri_sprout', 'com_sort', 'com_guard', 'com_barrier'].map((id) => handOf(b, id));
  cmd(b, 'play', { uid: guard, mode: 'now' });
  const sheet = b.supportChoices('yuri');
  assert.deepEqual(sheet.requests, [
    { label: '비용을 낮출 카드', from: 'hand', count: 1, growthOnly: false, op: 'costdown', part: null, leading: true },
    { label: '손으로 가져올 카드', from: 'discard', count: 1, growthOnly: false, op: 'recall', part: null, leading: false },
    { label: '성장시킬 카드', from: 'hand', count: 1, growthOnly: true, op: 'growPick', part: null, leading: false },
    { label: '「증폭」을 붙일 카드', from: 'hand', count: 1, growthOnly: false, op: 'tune', part: 'amp', leading: false }]);
  assert.deepEqual(sheet.candidates, [cocoon, sprout, barrier].filter((u) => b.s.zones.hand.includes(u)).sort((x, y) => b.s.zones.hand.indexOf(x) - b.s.zones.hand.indexOf(y)));
  assert.ok(!sheet.candidates.includes(sort), 'a zero-cost card cannot take the leading costdown');
  const picks = [barrier, guard, sprout, cocoon];
  assert.deepEqual(b.supportChoices('yuri', picks), { error: null, uid: '', mode: 'now', requests: sheet.requests, accepted: 4, index: 4, left: 0, blocked: null, candidates: [] });
  const state = clone(b.s); assert.equal(code(() => cmd(b, 'support', { caller: 'yuri', picks: ['c999'] })), 'PICK_INVALID'); assert.deepEqual(b.s, state);
  cmd(b, 'support', { caller: 'yuri', picks });
  assert.deepEqual([b.s.cards[barrier].costMod, b.s.zones.hand.includes(guard), b.s.cards[sprout].growth, b.s.cards[cocoon].temp], [-1, true, 3, ['amp']]);
  // FreeNext prices every hand card at 0, so the leading costdown has no candidate (the support itself spends no charge)
  const free = isoBattle(data, [{ charId: 'yuri', gauge: GAUGE }, { charId: 'rien', gauge: GAUGE }], clone(deck));
  cmd(free, 'ult', { actor: 'rien' }); assert.deepEqual([free.supportChoices('yuri').candidates, free.s.freeNext], [[], 2]);
  const capped = isoBattle(iso((d) => { setUlt(d, 'nia', [{ op: 'growPick', n: 1 }]); d.rules.growthCapPerBattle = 0; }), [{ charId: 'yuri', gauge: GAUGE }], clone(deck));
  assert.deepEqual([capped.supportChoices('yuri').blocked, capped.supportChoices('yuri').candidates], ['PICK_GROWTH_CAP', []]);
  // pull on existing reservations: a full next column rejects before payment; a picked due-next reservation resolves now
  const resDeck = [{ id: 't_res' }, { id: 't_res' }, { id: 't_res' }, { id: 'com_guard' }, { id: 'com_guard' }];
  const kaya = { ...battleSupport(build('kaya', 0, 'basic')) };
  const p = isoBattle(data, [{ charId: 'sera', gauge: GAUGE }], clone(resDeck), { support: kaya });
  const [r1, r2, r3] = p.s.zones.hand.filter((u) => p.s.cards[u].id === 't_res');
  cmd(p, 'play', { uid: r1, mode: 'next' }); cmd(p, 'play', { uid: r2, mode: 'next' }); cmd(p, 'play', { uid: r3, mode: 'after' });
  assert.deepEqual(p.supportChoices('sera').candidates, [r1, r2, r3]);
  const pState = clone(p.s); assert.equal(code(() => cmd(p, 'support', { caller: 'sera' })), 'PULL_DESTINATION_FULL'); assert.deepEqual(p.s, pState);
  const pulled = cmd(p, 'support', { caller: 'sera', picks: [r1] }).ev;
  assert.deepEqual(pulled.slice(0, 2), [{ e: 'support', actor: 'kaya', caller: 'sera' }, { e: 'pull', uid: r1, due: p.s.turn }]);
  assert.ok(pulled.some((e) => e.e === 'resolved' && e.uid === r1 && e.reserved === 'next'));
  const loanData = clone(data); loanData.rules.debtCap = 20; // isolate AP overflow after the independent debt-cap check
  const loan = isoBattle(loanData, [{ charId: 'sera', gauge: GAUGE }], clone(resDeck), { support: battleSupport(build('lumi', 0, 'basic')) });
  const lState = clone(loan.s); assert.equal(code(() => cmd(loan, 'support', { caller: 'sera' })), 'LOAN_OVERFLOW'); assert.deepEqual(loan.s, lState);
});

test('support gauge ops reach deployed actors only: self has no recipient and a provenanced token charges nobody; rift echo chain breaks', () => {
  const data = iso((d) => {
    addCard(d, { id: 't_charge', name: '시험 충전', owner: 'nia', type: 'skl', eff: [{ op: 'gauge', n: 10, who: 'self' }] });
    setUlt(d, 'nia', [{ op: 'gauge', n: 30, who: 'self' }, { op: 'gauge', n: 20, who: 'all' }, { op: 'create', card: 't_charge', n: 1, zone: 'hand' },
      { op: 'create', card: 'nia_drone_shot', n: 1, zone: 'hand' }]);
  });
  const b = isoBattle(data, [{ charId: 'sera', gauge: GAUGE }, { charId: 'harin', gauge: 40 }], [{ id: 'com_guard' }]);
  cmd(b, 'support', { caller: 'sera' });
  assert.deepEqual(b.s.actors.map((a) => a.gauge), [20, 60]);
  cmd(b, 'play', { uid: handOf(b, 't_charge'), mode: 'now' });
  assert.deepEqual(b.s.actors.map((a) => a.gauge), [20, 60]);
  const rift = (withToken) => {
    const m = CM.ModeBattle.create(data, { party: [{ charId: 'sera', gauge: GAUGE }, { charId: 'rien' }, { charId: 'harin' }], hp: 200, maxHp: 200, seed: 3,
      deck: [{ id: 'sera_round' }, { id: 'rien_note' }, { id: 'harin_bash' }, { id: 'com_guard' }, { id: 'com_guard' }], enemies: ['b_archivist'], support: NIA },
    { kind: 'test', rule: 'rift_echo', params: clone(MODES.rift.modifiers.rift_echo) });
    m.do('begin'); const t = m.alive()[0].uid; cmd(m, 'support', { caller: 'sera' });
    const ev = [];
    for (const id of ['sera_round', 'rien_note', ...(withToken ? ['nia_drone_shot'] : []), 'harin_bash']) ev.push(...cmd(m, 'play', { uid: handOf(m, id), mode: 'now', target: t }).ev);
    return ev.some((e) => e.e === 'riftEcho');
  };
  assert.deepEqual([rift(false), rift(true)], [true, false]);
});

test('Run freezes one validated owned support build beside the party; each new Battle starts unused; commands and queries forward', () => {
  const kaya = build('kaya', 6, 'advanced'); const party = [{ charId: 'sera', gauge: GAUGE }, { charId: 'harin' }];
  const d = MF.Stats.derive(ix, kaya);
  assert.deepEqual(MF.Run.supportEntry(ix, kaya, party), { ...kaya, power4: d.power4, hp: d.hp, gearBps: d.gearBps, skillLv: d.skillLv, ultLv: d.ultLv });
  const entry = (patch) => code(() => MF.Run.supportEntry(ix, { ...kaya, ...patch }, party));
  assert.equal(entry({ charId: 'sera' }), 'SUPPORT_IN_PARTY'); assert.equal(entry({ charId: 'nobody' }), 'UNKNOWN_CHARACTER');
  for (const bad of [{ level: undefined }, { level: 61 }, { asc: undefined }, { res: 7 }, { weaponId: 'w_lumi' }, { weaponId: undefined }, { weaponLevel: 0 }, { refine: 0 },
    { skillLv: 11 }, { ultLv: 0 }, { armor: 16 }, { accessory: -1 }]) assert.equal(entry(bad), 'INVALID_SNAPSHOT', JSON.stringify(bad));
  assert.equal(code(() => MF.Run.supportEntry(ix, { ...kaya, weaponInstanceId: 'w1' }, [{ charId: 'sera', weaponInstanceId: 'w1' }])), 'INVALID_SNAPSHOT');
  for (const policy of ['legacy', 'v2']) {
    const setup = { party: clone(party), seed: 11, length: 'short', ...(policy === 'v2' ? { memoryPolicy: 'v2', runId: 'support-run', memoryVersion: 0, memory: {} } : {}) };
    const plain = MF.Run.create(DATA, clone(setup)); const input = clone(kaya); const run = MF.Run.create(DATA, { ...clone(setup), support: input });
    const { support, ...rest } = run.s;
    assert.equal(JSON.stringify(rest), JSON.stringify(plain.s), `${policy}: support changed party, deck, HP, memory or map`);
    assert.ok(!Object.hasOwn(plain.s, 'support'), 'the runId value support-run is not a support graph member');
    input.level = 1; input.res = 0; assert.deepEqual(support, MF.Run.supportEntry(ix, kaya, party));
  }
  const run = MF.Run.create(DATA, { party: clone(party), seed: 11, length: 'short', support: clone(kaya) }); const frozen = clone(run.s.support);
  assert.equal(code(() => run.battleSupportInfo('sera')), 'NO_BATTLE'); assert.equal(run.battleSupportForecast('sera').error, 'NO_BATTLE');
  run.enter(run.available()[0]);
  assert.equal(JSON.stringify(run.battle.s.support), JSON.stringify({ source: expectedSource(kaya), used: false }));
  assert.deepEqual([run.battleSupportInfo('sera'), run.battleSupportNeedsTarget(), run.battleSupportChoices('sera'), run.battleSupportForecast('sera')],
    [run.battle.supportInfo('sera'), run.battle.supportNeedsTarget(), run.battle.supportChoices('sera'), run.battle.supportForecast('sera')]);
  const r = run.battleCmd('support', { caller: 'sera' });
  assert.deepEqual([r.ev[0], r.phase, run.s.phase, run.battle], [{ e: 'support', actor: 'kaya', caller: 'sera' }, 'VICTORY', 'REWARD', null]);
  run.skipCard(); run.proceed();
  for (let guard = 0; run.s.phase !== 'BATTLE'; guard++) {
    assert.ok(guard < 12, 'no next battle reached');
    if (run.s.phase === 'MAP') { const ids = run.available(); run.enter(ids.find((i) => ['battle', 'elite', 'boss'].includes(run.nodeById(i).type)) ?? ids[0]); }
    else if (run.s.phase === 'EVENT') { run.eventChoose(ix.events[run.s.pending.id].choices.length - 1); run.proceed(); }
    else run.proceed();
  }
  assert.deepEqual([run.battle.s.support.used, run.s.support], [false, frozen]);
  assert.equal(run.battleCmd('support', { caller: 'sera' }).ev[0].e, 'support');
  assert.ok(!run.s.deck.some((c) => c.owner === 'kaya') && !Object.hasOwn(run.s.offered, 'kaya') && run.s.party.every((p) => p.charId !== 'kaya'));
});

test('dungeon and weekly launches carry a standalone owned support; waves keep one use per Battle', () => {
  const account = { level: 60, stamina: 240, owned: [...CHARS] };
  const modes = clone(MODES); modes.dungeon.waves.encounters = { dg_ink: { 1: [['e_page'], ['e_page', 'e_page']] } };
  const party = [{ charId: 'sera', level: 60, gauge: GAUGE }, { charId: 'harin', level: 60, gauge: GAUGE }, { charId: 'rien', level: 60 }];
  const opts = { dungeonId: 'dg_ink', tier: 1, party, seed: 5 };
  const plain = CM.Dungeon.start(DATA, modes, account, clone(opts));
  const b = CM.Dungeon.start(DATA, modes, account, { ...clone(opts), support: build('kaya', 6, 'advanced') });
  const { support, ...rest } = b.s;
  assert.equal(JSON.stringify(rest), JSON.stringify(plain.s)); assert.equal(JSON.stringify(support), JSON.stringify({ source: expectedSource(build('kaya', 6, 'advanced')), used: false }));
  const r = cmd(b, 'support', { caller: 'sera' });
  assert.ok(r.ev.some((e) => e.e === 'wave')); assert.deepEqual([b.s.mode.wave, b.s.support.used, b.isOver()], [2, true, false]);
  assert.equal(code(() => cmd(b, 'support', { caller: 'harin' })), 'SUPPORT_ALREADY_USED');
  assert.equal(code(() => CM.Dungeon.start(DATA, modes, { ...account, owned: CHARS.filter((c) => c !== 'kaya') }, { ...clone(opts), support: build('kaya', 6, 'advanced') })), 'NOT_OWNED');
  assert.equal(code(() => CM.Dungeon.start(DATA, modes, account, { ...clone(opts), support: build('sera', 0, 'basic') })), 'SUPPORT_IN_PARTY');
  const weekly = CM.WeeklyBoss.start(DATA, MODES, account, { bossId: 'b_bell', difficulty: 1, party: clone(party), seed: 2, support: build('aila', 2, 'basic') });
  assert.deepEqual(weekly.publicView().support, { charId: 'aila', used: false, gaugeCost: GAUGE });
  assert.equal(JSON.stringify(DATA), DATA_TEXT, 'production data changed');
});

test('S4 parity: support always costs100 even in an isolated rules variant with a larger gauge ceiling', () => {
  const data = clone(DATA); data.rules.gaugeMax = 150;
  const ordinary = MF.Battle.create(data, { party: [{ charId: 'aila', gauge: 100 }], deck: [], hp: 81, maxHp: 81, seed: 4, enemies: ['b_archivist'] });
  ordinary.do('begin'); assert.equal(code(() => cmd(ordinary, 'ult', { actor: 'aila' })), 'ULT_NOT_READY', 'ordinary ult still uses its existing rules gauge ceiling');
  for (const gauge of [99, 100, 150]) {
    const b = MF.Battle.create(data, { party: [{ charId: 'sera', gauge }], deck: [], hp: 81, maxHp: 81, seed: 4, enemies: ['b_archivist'], support: battleSupport(build('aila', 0, 'basic')) });
    b.do('begin'); const before = clone(b.s); const info = b.supportInfo('sera');
    assert.equal(info.gaugeCost, 100); assert.equal(b.publicView().support.gaugeCost, 100);
    assert.equal(info.ready, gauge >= 100); assert.equal(info.code, gauge < 100 ? 'ULT_NOT_READY' : null);
    const forecast = b.supportForecast('sera'); assert.deepEqual(b.s, before);
    if (gauge < 100) { assert.equal(forecast.error, 'ULT_NOT_READY'); assert.equal(code(() => cmd(b, 'support', { caller: 'sera' })), 'ULT_NOT_READY'); }
    else { assert.equal(forecast.cost, 100); cmd(b, 'support', { caller: 'sera' }); assert.equal(b.actor('sera').gauge, gauge - 100); assert.equal(b.s.support.used, true); }
  }
});

test('S4 parity: unknown source resonance is refused rather than silently granting a partially interpreted source', () => {
  const data = iso((d) => { d.characters.find((c) => c.id === 'nia').resonance[0].fx = { type: 's4_unknown_source_effect' }; });
  assert.equal(code(() => isoBattle(data, [{ charId: 'sera', gauge: 100 }], [], { support: { ...NIA, res: 1 } })), 'BATTLE_RESONANCE_NOT_IMPLEMENTED');
  assert.equal(JSON.stringify(DATA), DATA_TEXT);
});

test('S4 parity: support numeric overflow rejects the entire candidate, including intermediate state, receipts and mode battles', () => {
  const data = iso((d) => setUlt(d, 'kaya', [{ op: 'dmg', v: 0x7fffffff, tgt: 'all' }]));
  const source = { ...battleSupport(build('kaya', 0, 'basic')), power4: 0x7fffffff };
  const setup = { party: [{ charId: 'sera', gauge: 100 }], deck: [], hp: 81, maxHp: 81, seed: 4, enemies: ['b_archivist'], support: source };
  for (const b of [MF.Battle.create(data, clone(setup)), CM.ModeBattle.create(data, clone(setup), { kind: 'test' })]) {
    b.do('begin'); const state = JSON.stringify(b.s), receipts = clone(b.receipts);
    assert.equal(b.supportForecast('sera').error, 'BATTLE_NUMBER_OUT_OF_RANGE'); assert.equal(JSON.stringify(b.s), state);
    assert.equal(code(() => cmd(b, 'support', { caller: 'sera' }, 'overflow-retry')), 'BATTLE_NUMBER_OUT_OF_RANGE');
    assert.equal(JSON.stringify(b.s), state); assert.deepEqual(b.receipts, receipts);
  }
  const b = MF.Battle.create(data, clone(setup)); b.do('begin');
  assert.equal(code(() => cmd(b, 'support', { caller: 'sera' }, 'retry-after-failure')), 'BATTLE_NUMBER_OUT_OF_RANGE');
  data.characters.find((c) => c.id === 'kaya').ult.eff[0].v = 0;
  assert.equal(cmd(b, 'support', { caller: 'sera' }, 'retry-after-failure').ev[0].e, 'support', 'a rejected command recorded no receipt');

  const intermediate = iso((d) => setUlt(d, 'kaya', [{ op: 'counter', n: 0x7fffffff }, { op: 'counter', n: 1 }, { op: 'counter', n: -0x7fffffff }]));
  const partial = isoBattle(intermediate, [{ charId: 'sera', gauge: 100 }], [], { support: battleSupport(build('kaya', 0, 'basic')) });
  const before = clone(partial.s); assert.equal(code(() => cmd(partial, 'support', { caller: 'sera' })), 'BATTLE_NUMBER_OUT_OF_RANGE'); assert.deepEqual(partial.s, before);
  const ordinary = MF.Battle.create(intermediate, { party: [{ charId: 'kaya', gauge: 100 }], deck: [], hp: 81, maxHp: 81, seed: 4, enemies: ['b_archivist'] });
  ordinary.do('begin'); cmd(ordinary, 'ult', { actor: 'kaya' }); assert.equal(ordinary.s.counter, 1, 'new support range guards leave legacy JS arithmetic unchanged');
});

test('S4 parity: forecast aggregate overflow hides partial results while the individually valid command remains usable', () => {
  const data = iso((d) => {
    setUlt(d, 'kaya', [{ op: 'dmg', v: 1500000000, tgt: 'all' }]);
    d.enemies.push({ id: 't_large', name: '시험 대형', tier: 'normal', hp: 2000000000, brk: 100, weak: [], pattern: [{ t: 'charge' }] });
  });
  const source = { ...battleSupport(build('kaya', 0, 'basic')), power4: 160, gearBps: 0, ultLv: 1 };
  const b = isoBattle(data, [{ charId: 'sera', gauge: 100 }], [], { support: source, enemies: ['t_large', 't_large', 't_large'] });
  const before = clone(b.s), receipts = clone(b.receipts); const forecast = b.supportForecast('sera');
  assert.equal(forecast.error, 'BATTLE_NUMBER_OUT_OF_RANGE'); assert.equal(forecast.enemyHpLoss, 0); assert.deepEqual(forecast.enemyHpLosses, []);
  assert.deepEqual(b.s, before); assert.deepEqual(b.receipts, receipts);
  cmd(b, 'support', { caller: 'sera' }); assert.deepEqual(b.alive().map((e) => e.hp), [500000000, 500000000, 500000000]);
});

test('S4 parity: source-token modifier overflow is rejected before a smaller scaled damage could mask it', () => {
  const data = iso((d) => {
    addCard(d, { id: 't_source_overflow', owner: 'nia', name: '시험 출처', eff: [{ op: 'dmg', v: 1 }] });
    setUlt(d, 'nia', [{ op: 'create', card: 't_source_overflow', n: 1, zone: 'hand' }]);
    d.characters.find((c) => c.id === 'nia').resonance[0].fx = { type: 'cardMod', cards: ['t_source_overflow'], prop: 'dmg', add: 0x7fffffff };
  });
  const b = isoBattle(data, [{ charId: 'sera', gauge: 100 }], [], { support: { ...NIA, res: 1 } });
  cmd(b, 'support', { caller: 'sera' }); const token = handOf(b, 't_source_overflow');
  const before = clone(b.s), receipts = clone(b.receipts);
  assert.equal(code(() => b.effDef(b.s.cards[token])), 'BATTLE_NUMBER_OUT_OF_RANGE');
  assert.equal(code(() => cmd(b, 'play', { uid: token, mode: 'now' })), 'BATTLE_NUMBER_OUT_OF_RANGE');
  assert.deepEqual(b.s, before); assert.deepEqual(b.receipts, receipts);
});

test('S4 parity: opaque runFlags preserve rng and nested rank-named metadata through queries, refusals and support', () => {
  const flags = { rng: 7, skillLv: 3000000000, nested: { rng: { value: 4294967295 }, ultLv: -3000000000,
    runFlags: { value: 5000000000 } }, list: [{ rng: null, skillLv: 4000000000 }] };
  const setup = { party: [{ charId: 'sera', gauge: 100 }], deck: [], hp: 81, maxHp: 81, seed: 4, enemies: ['b_archivist'],
    runFlags: clone(flags), support: battleSupport(build('aila', 0, 'basic')) };
  const mode = { kind: 'test', rule: 'rift_echo', params: { ...MODES.rift.modifiers.rift_echo, rng: 7, skillLv: 3 } };
  for (const b of [MF.Battle.create(DATA, clone(setup)), CM.ModeBattle.create(DATA, clone(setup), mode)]) {
    b.do('begin'); assert.deepEqual(b.s.runFlags, flags);
    const before = JSON.stringify(b.s), receipts = clone(b.receipts);
    assert.equal(b.supportInfo('sera').ready, true); assert.equal(b.supportChoices('sera').error, null); assert.equal(b.supportForecast('sera').error, null);
    assert.equal(JSON.stringify(b.s), before); assert.deepEqual(b.receipts, receipts);
    assert.equal(code(() => cmd(b, 'support', { caller: 'nobody' })), 'UNKNOWN_ACTOR');
    assert.equal(JSON.stringify(b.s), before); assert.deepEqual(b.receipts, receipts);
    cmd(b, 'support', { caller: 'sera' }); assert.deepEqual(b.s.runFlags, flags);
    assert.equal(b.actor('sera').gauge, 0); assert.equal(b.s.support.used, true);
  }
});
