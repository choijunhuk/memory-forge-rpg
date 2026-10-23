import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { createHash } from 'node:crypto';
import { mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { gunzipSync } from 'node:zlib';
import { generateCases, checkFixture, exportVectors, fixturePath } from '../../tools/gen_battle_player_query_vectors.mjs';
import { generateCases as openingCases } from '../../tools/gen_battle_opening_vectors.mjs';

const require = createRequire(import.meta.url);
const data = require('../../data/game_data.json');
const MF = require('../rules-core.js');
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = value => createHash('sha256').update(value).digest('hex');
const fixture = generateCases();
const byId = id => {
  const row = fixture.cases.find(c => c.id === id);
  assert.ok(row, `Missing player-query case ${id}`);
  return row;
};
const query = (row, cardId, plus = false) => {
  const value = row.queries.find(q => q.cardId === cardId && row.begun.state.cards[q.uid].plus === plus);
  assert.ok(value, `${row.id}: missing ${cardId}, plus=${plus}`);
  return value;
};

// Independent sorted-key replacer; do not reuse the generator serializer to prove hashes.
function hashState(value) {
  const keys = new Set();
  const visit = item => {
    if (!item || typeof item !== 'object') return;
    for (const key of Object.keys(item)) {
      if (key === 'log' || key === 'ev') continue;
      if (!Array.isArray(item)) keys.add(key);
      visit(item[key]);
    }
  };
  visit(value);
  return sha256(JSON.stringify(value, [...keys].sort()));
}

// RULES_NOW_PLAY (2026-10-19): every card is play-now only, so the 173 base cards have exactly 173 legal (card, now) pairs.
test('player query oracle covers all 173 base cards and each one\'s single legal now mode', () => {
  assert.equal(fixture.schema, 'mf-battle-player-queries-v1');
  assert.equal(fixture.catalog.cards.length, 173);
  assert.ok(data.cards.every(c => c.res === 'n'));
  const base = fixture.cases.filter(c => c.group === 'card' && c.variant === 'base');
  assert.deepEqual(base.map(c => c.cardId), data.cards.map(c => c.id));
  assert.equal(base.reduce((count, c) => count + c.createdQueries[0].allowedModes.length, 0), data.cards.length);
  assert.deepEqual(fixture.cases.filter(c => c.group === 'card' && c.variant === 'plus').map(c => c.cardId), data.cards.map(c => c.id));
  for (const row of base) {
    const definition = data.cards.find(c => c.id === row.cardId);
    const expected = { y: ['now', 'next', 'after'], n: ['now'], o: ['next', 'after'], after: ['after'] }[definition.res];
    assert.deepEqual(row.createdQueries[0].allowedModes, expected);
    assert.deepEqual(row.queries[0].allowedModes, expected);
    assert.deepEqual(row.queries[0].costs, { now: definition.cost, next: definition.cost, after: definition.cost });
  }
});

test('combat catalog preserves full cards, metadata, 10 ultimates, resonance FX, weapon passives and all16 parts', () => {
  assert.deepEqual(fixture.catalog, { cards: data.cards,
    characters: data.characters.map(({ id, name, attr, passive, ult, resonance }) => ({ id, name, attr, passive, ult, resonance: resonance.map(r => r.fx) })),
    weapons: data.weapons, parts: data.parts, relics: data.relics });
  assert.equal(fixture.catalogHash, hashState(fixture.catalog));
  assert.equal(fixture.catalog.characters.length, 10);
  assert.equal(fixture.catalog.weapons.length, 22);
  assert.equal(new Set(fixture.catalog.weapons.map(w => w.passive.id)).size, 17);
  assert.equal(fixture.catalog.parts.length, 16);
  assert.equal(fixture.catalog.relics.length, 32);
  const tags = new Set(), fields = new Set();
  const walk = list => { for (const op of list || []) { tags.add(op.op); Object.keys(op).forEach(k => fields.add(k)); walk(op.then); walk(op.else); } };
  const fx = value => { if (value.eff) walk(value.eff); if (value.list) value.list.forEach(fx); };
  fixture.catalog.cards.forEach(c => walk(c.eff));
  fixture.catalog.characters.forEach(c => { walk(c.ult.eff); c.resonance.forEach(fx); });
  // RULES_NOW_PLAY removed the reservation ops slot/pull and the reservation fields rv/ra/rn/next/after from all authored effects
  assert.equal(tags.size, 37);
  assert.equal(fields.size, 30);
  for (const gone of ['slot', 'pull']) assert.ok(!tags.has(gone), gone);
  for (const gone of ['rv', 'ra', 'rn', 'next', 'after']) assert.ok(!fields.has(gone), gone);
  const inspect = value => {
    if (typeof value === 'number') assert.ok(Number.isSafeInteger(value), 'catalog must not contain fractional rules');
    if (value && typeof value === 'object') Object.values(value).forEach(inspect);
  };
  inspect(fixture.catalog);
  assert.ok(!Object.hasOwn(fixture.catalog, 'rules'));
  assert.deepEqual(fixture.upgradeRatio, { literal: '1.3', numerator: 13, denominator: 10 });
  const evolved = fixture.catalog.cards.find(c => c.id === 'yuri_cocoon_guard');
  assert.equal(evolved.pool, false);
  assert.ok(!Object.hasOwn(evolved, 'growth'));
  assert.equal(fixture.catalog.cards.find(c => c.id === 'sion_burnbook').eff[1].hits, 0);
  assert.equal(fixture.catalog.characters.find(c => c.id === 'sion').ult.eff[1].hits, 0);
  assert.ok(!Object.hasOwn(fixture.catalog.cards[0], 'uses'));
});

test('every character resonance, weapon refine, relic and legally reachable part setup is actually queried', () => {
  assert.deepEqual(fixture.cases.filter(c => c.group === 'opening').map(c => c.openingId), openingCases().cases.map(c => c.id));
  for (const ch of data.characters) for (let res = 0; res <= 6; res++) {
    const row = byId(`resonance-${ch.id}-r${res}`);
    assert.deepEqual(row.setup.party, [{ charId: ch.id, res }]);
    assert.equal(row.queries.length, 2 * data.cards.filter(c => c.owner === ch.id).length);
  }
  for (const w of data.weapons) for (let r = 1; r <= 5; r++) {
    const row = byId(`opening-weapon-${w.id}-r${r}`);
    assert.equal(row.setup.party[0].weaponId, w.id);
    assert.equal(row.setup.party[0].refine, r);
    assert.ok(row.queries.some(q => q.cardId === w.card));
  }
  for (const relic of data.relics) assert.deepEqual(byId(`opening-relic-${relic.id}`).setup.relics, [relic.id]);
  for (const part of data.parts.filter(p => p.slot !== 'temp')) {
    const row = byId(`part-${part.id}`);
    assert.equal(row.queries.length, 4);
    assert.ok(row.setup.deck.every(c => c.mods[part.slot === 'circuit' ? 'c' : 'e'] === part.id));
  }
  assert.equal(fixture.coverage.characterResonances, 70);
  assert.equal(fixture.coverage.weaponRefinements, 110);
  assert.equal(fixture.coverage.permanentPartCases, 14);
});

test('every query reproduces effective definitions and preserves full state, RNG, diagnostics, receipts and source definitions', () => {
  const original = clone(data);
  let records = 0;
  const samePoint = (battle, point, label) => {
    assert.ok(isDeepStrictEqual(battle.s, point.state), `${label}: full state`);
    assert.deepEqual(battle.publicView(), point.publicView, label);
    assert.deepEqual(clone(battle.receipts), point.receipts, label);
    assert.equal(hashState(point.state), point.stateHash, label);
    assert.equal(hashState(point.state.rng), point.rngHash, label);
    assert.equal(hashState(point.state.ev), point.eventsHash, label);
    assert.equal(hashState(point.state.log), point.logHash, label);
    assert.equal(hashState(point.receipts), point.receiptsHash, label);
    assert.equal(hashState(point.publicView), point.publicViewHash, label);
    for (const key of ['rng', 'draw', 'zones']) assert.ok(!Object.hasOwn(point.publicView, key));
  };
  const checkQueries = (battle, expected, label) => {
    const state = clone(battle.s), receipts = clone(battle.receipts);
    assert.deepEqual(expected.map(q => q.uid), Object.keys(battle.s.cards));
    const unchanged = () => {
      assert.ok(isDeepStrictEqual(battle.s, state), `${label}: query changed full state`);
      assert.deepEqual(clone(battle.receipts), receipts, label);
    };
    for (const q of expected) {
      records++;
      assert.equal(q.cardId, battle.s.cards[q.uid].id);
      assert.deepEqual(clone(battle.effDef(battle.s.cards[q.uid])), q.effective, label); unchanged();
      assert.deepEqual(battle.allowedModes(q.uid), q.allowedModes, label); unchanged();
      assert.equal(battle.needsTarget(q.uid), q.needsTarget, label); unchanged();
      for (const mode of ['now', 'next', 'after']) { assert.equal(battle.costOf(q.uid, mode), q.costs[mode], label); unchanged(); }
    }
    assert.ok(isDeepStrictEqual(data, original), `${label}: source definitions changed`);
  };
  for (const row of fixture.cases) {
    const battle = MF.Battle.create(data, clone(row.setup));
    samePoint(battle, row.created, row.id);
    checkQueries(battle, row.createdQueries, row.id);
    samePoint(battle, row.afterCreatedQueries, row.id);
    assert.deepEqual(row.afterCreatedQueries, row.created);
    assert.deepEqual(battle.command(clone(row.request)), row.response);
    samePoint(battle, row.begun, row.id);
    checkQueries(battle, row.queries, row.id);
    samePoint(battle, row.afterQueries, row.id);
    assert.deepEqual(row.afterQueries, row.begun);
    assert.equal(row.definitionsBeforeHash, fixture.catalogHash);
    assert.equal(row.definitionsAfterHash, fixture.catalogHash);
    const beforeRetry = clone(battle.s);
    assert.deepEqual(battle.command(clone(row.request)), row.response);
    assert.deepEqual(battle.s, beforeRetry, 'queries must not corrupt the stored begin response');
  }
  assert.deepEqual(fixture.counts, { cases: fixture.cases.length, beginCommands: fixture.cases.length, queryRecords: records, queryCalls: 6 * records });
});

test('auto upgrade uses ceiling13/10 before resonance modifiers and preserves explicit0/false/absent fields', () => {
  const round = query(byId('card-sera_round-plus'), 'sera_round', true).effective;
  // rv/ra were removed from data (RULES_NOW_PLAY); only v is upgraded: ceil(8 × 13/10) = 11
  assert.deepEqual(round.eff, [{ op: 'dmg', v: 11 }]);
  const timed = query(byId('resonance-sera-r4'), 'sera_timed', true).effective;
  // sera_timed is now v 20 brk 3: ceil(20 × 13/10) = 26, and Sera r4 adds brk +2 → 5
  assert.deepEqual(timed.eff, [{ op: 'dmg', v: 26, brk: 5 }]);
  const harin = query(byId('resonance-harin-r1'), 'harin_counter', true).effective;
  assert.deepEqual(harin.eff, [{ op: 'blk', v: 7 }, { op: 'counter', n: 6 }]);
  assert.equal(query(byId('resonance-kaya-r1'), 'kaya_clue', true).effective.eff[0].n, 4);
  assert.equal(query(byId('resonance-rien-r1'), 'rien_note', true).effective.eff[0].v, 9);
  assert.deepEqual(query(byId('resonance-aila-r1'), 'aila_bell', true).effective.eff, [{ op: 'heal', v: 9 }]);
  assert.equal(query(byId('resonance-nia-r1'), 'nia_tune').effective.uses, 3);
  assert.equal(query(byId('resonance-ravien-r1'), 'ravien_advance').effective.uses, 2);
  const lumi = query(byId('resonance-lumi-r1'), 'lumi_dash', true).effective;
  assert.deepEqual(lumi.eff.slice(0, 2), [{ op: 'selfdmg', v: 1 }, { op: 'dmg', v: 10 }]);
  assert.equal(query(byId('card-sion_burnbook-plus'), 'sion_burnbook', true).effective.eff[1].hits, 0);
  const nested = query(byId('card-ravien_strike-plus'), 'ravien_strike', true).effective;
  assert.equal(nested.eff[1].then[0].n, 3);
  assert.ok(!Object.hasOwn(round, 'uses'));
  assert.equal(query(byId('card-yuri_cocoon_guard-base'), 'yuri_cocoon_guard').effective.pool, false);
});

// RULES_NOW_PLAY (2026-10-19): c_quick lost noReserve (it is just +20%), there are no reserve-only cards, and empty_clock
// discounts the first card in now mode too. Pure cost queries still answer every mode and never consume the clock.
test('c_quick keeps every card playable now and empty_clock discounts the first card in every mode without being consumed by queries', () => {
  const cost = id => data.cards.find(c => c.id === id).cost;
  const same = n => ({ now: n, next: n, after: n });
  const quick = byId('part-c_quick');
  for (const id of ['sera_round', 'nia_tune', 'sera_timed', 'sera_last']) {
    assert.deepEqual(query(quick, id).allowedModes, ['now'], id);
    assert.deepEqual(query(quick, id).costs, same(cost(id)), id);
  }
  const clock = byId('edge-empty-clock-no-reserve');
  assert.deepEqual(query(clock, 'sera_timed').allowedModes, ['now']);
  assert.deepEqual(query(clock, 'sera_timed').costs, same(cost('sera_timed') - 1));
  const lite = byId('edge-empty-clock-lite');
  const liteCost = data.parts.find(p => p.id === 'c_lite').fx.cost;
  for (const id of ['sera_round', 'sera_timed', 'sera_last', 'nia_tune']) {
    assert.deepEqual(query(lite, id).costs, same(Math.max(0, cost(id) + liteCost - 1)), id);
  }
  assert.ok(!Object.hasOwn(lite.afterQueries.state.battleFlags, 'emptyClockUsed'));
  assert.equal(lite.afterQueries.state.ap, lite.begun.state.ap);
  assert.deepEqual(byId('edge-unknown-part-ignored').queries[0].allowedModes, ['now']);
  assert.deepEqual(byId('edge-unknown-part-ignored').queries[0].costs, same(1));
});

test('target requirements include nested status branches and preserve fixed/all/random distinctions without selecting targets', () => {
  for (const [id, need] of [['sera_round', 'one'], ['sera_cover', 'one'], ['harin_taunt', 'one'], ['kaya_interrogate', 'fixed'],
    ['kaya_deduce', 'fixed'], ['kaya_verdict', 'fixed'], ['sera_flash', null], ['lumi_storm', null], ['nia_drone', null], ['smudge', null], ['wc_basic_melee', 'one']]) {
    assert.equal(byId(`card-${id}-base`).queries[0].needsTarget, need, id);
  }
  const owner = byId('edge-character-weapon-common-owner');
  // rv values were removed from data (RULES_NOW_PLAY); the plus aila_bell is ceil(5 × 13/10) + Aila r1 = 9
  assert.deepEqual(query(owner, 'aila_bell', true).effective.eff, [{ op: 'heal', v: 9 }]);
  assert.deepEqual(query(owner, 'wc_choir_bell').effective.eff, [{ op: 'heal', v: 4 }]);
  assert.deepEqual(query(owner, 'com_patch').effective.eff, [{ op: 'heal', v: 6 }]);
  const instances = Object.values(owner.begun.state.cards);
  assert.deepEqual(instances.map(c => [c.owner, c.wielder, c.assigned]), [['aila', null, null], ['weapon', 'aila', null], ['common', null, 'aila']]);
  const evolved = byId('card-yuri_cocoon_guard-base');
  assert.equal(evolved.created.state.cards.c1.from, 'yuri_cocoon');
  assert.equal(evolved.queries[0].effective.owner, 'yuri');
});

test('begin produces real amp and curse tokens while non-produced A1 states are explicitly not manufactured', () => {
  const amp = byId('temp-amp-begin');
  assert.equal(Object.values(amp.created.state.cards).filter(c => c.temp.length).length, 0);
  assert.equal(Object.values(amp.begun.state.cards).filter(c => c.temp.includes('amp')).length, 1);
  assert.notEqual(amp.created.rngHash, amp.begun.rngHash);
  const eye = byId('opening-relic-eye_of_rift');
  const token = Object.values(eye.created.state.cards).find(c => c.token);
  assert.equal(token.id, 'smudge');
  assert.ok(eye.queries.some(q => q.uid === token.uid));
  assert.deepEqual(fixture.reachability.setupPartIds, data.parts.filter(p => p.slot !== 'temp').map(p => p.id));
  assert.deepEqual(fixture.reachability.beginTempPartIds, ['amp']);
  assert.ok(fixture.reachability.pendingA1.includes('overheat temporary attachment'));
  assert.equal(data.cards.filter(c => c.plus).length, 0);
  assert.equal(data.cards.filter(c => c.costIf).length, 0);
  for (const row of fixture.cases) {
    assert.equal(row.request.kind, 'begin');
    for (const point of [row.created, row.begun]) {
      assert.equal(point.state.silenceUntil, 0);
      assert.equal(point.state.freeNext, 0);
      assert.ok(Object.values(point.state.cards).every(c => c.costMod === 0 && !c.free && !Object.hasOwn(c, 'echoCopy')));
    }
    assert.ok(row.setup.deck.every(c => !['amp', 'overheat'].includes(c.mods?.c) && !['amp', 'overheat'].includes(c.mods?.e)));
  }
});

test('base0 flag and live base5 remain present without claiming generic unresolved conformance', () => {
  const op = byId('card-sion_forget-base').queries[0].effective.eff[1];
  assert.deepEqual(op, { op: 'blk', v: 5, scale: { per: 'lastCount', k: 5, max: 10 }, base0: true });
  assert.equal(fixture.unresolved.base0.status, 'ORIGINAL_SOURCE_CONFLICT');
  assert.match(fixture.unresolved.base0.retained, /base5.*UNRESOLVED/);
  assert.ok(fixture.unresolved.base0.anchors.includes('docs/source_v0.8.1/06_chapter.md:42'));
  assert.ok(fixture.unresolved.base0.anchors.includes('docs/source_v0.8.1/09_chapter.md:833'));
  assert.match(fixture.scope, /play\/ult.*NOT_IMPLEMENTED/);
});

test('source freshness, full catalog and case coverage reject omissions and query/snapshot tampering', () => {
  for (const source of Object.values(fixture.sources)) assert.equal(source.sha256, sha256(readFileSync(new URL(`../../${source.path}`, import.meta.url))));
  assert.deepEqual(checkFixture(fixture), fixture.counts);
  const first = fixture.cases[0];
  for (const bad of [
    { ...fixture, sources: { ...fixture.sources, engine: { ...fixture.sources.engine, sha256: '0'.repeat(64) } } },
    { ...fixture, cases: fixture.cases.slice(1) },
    { ...fixture, cases: [...fixture.cases, first] },
    { ...fixture, catalog: { ...fixture.catalog, parts: fixture.catalog.parts.slice(1) } },
    { ...fixture, cases: [{ ...first, queries: [{ ...first.queries[0], needsTarget: false }] }, ...fixture.cases.slice(1)] },
    { ...fixture, cases: [{ ...first, afterQueries: { ...first.afterQueries, eventsHash: '0'.repeat(64) } }, ...fixture.cases.slice(1)] },
  ]) assert.throws(() => checkFixture(bad), /source|case|fixture|catalog|coverage|snapshot/i);
});

test('atomic deterministic export preserves a previous artifact on query exceptions and RuleErrors', t => {
  const dir = mkdtempSync(join(tmpdir(), 'mf-player-query-oracle-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  const path = join(dir, 'queries.json.gz');
  const summary = exportVectors(path), bytes = readFileSync(path);
  assert.equal(summary.cases, fixture.counts.cases);
  assert.equal(summary.queryRecords, fixture.counts.queryRecords);
  assert.equal(summary.sha256, sha256(bytes));
  assert.ok(isDeepStrictEqual(JSON.parse(gunzipSync(bytes)), fixture));
  exportVectors(path);
  assert.deepEqual(readFileSync(path), bytes);
  writeFileSync(path, 'previous artifact');
  for (const error of [new Error('unexpected query failure'), new MF.RuleError('UNKNOWN_CARD'), new MF.RuleError('UNSUPPORTED_EFFECT')]) {
    // Fault injection only checks exporter failure; positive cases always use unchanged source queries.
    const broken = t.mock.method(MF.Battle.prototype, 'effDef', () => { throw error; });
    assert.throws(() => generateCases(), caught => caught === error);
    assert.throws(() => exportVectors(path), caught => caught === error);
    assert.equal(readFileSync(path, 'utf8'), 'previous artifact');
    assert.deepEqual(readdirSync(dir), ['queries.json.gz']);
    broken.mock.restore();
  }
});

test('checked-in A0 fixture is lossless, fresh and limited to read-only player queries', () => {
  const saved = JSON.parse(gunzipSync(readFileSync(fixturePath)));
  assert.deepEqual(checkFixture(saved), fixture.counts);
});
