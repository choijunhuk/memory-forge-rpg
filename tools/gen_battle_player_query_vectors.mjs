// A0: authored combat definitions and pure instance queries from real Create/begin only.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { mkdtempSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { isDeepStrictEqual } from 'node:util';
import { gzipSync, gunzipSync } from 'node:zlib';
import { stateHash } from './gen_traces.mjs';
import { generateCases as openingCases } from './gen_battle_opening_vectors.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const require = createRequire(import.meta.url);
const MF = require('../engine/rules-core.js');
const data = JSON.parse(readFileSync(join(root, 'data/game_data.json'), 'utf8'));
const originalData = JSON.stringify(data);
const ix = MF.indexData(data);
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const schema = 'mf-battle-player-queries-v1';
const scope = 'Fresh Battle.create/begin pure EffectiveCard/AllowedModes/CostOf/NeedsTarget only; play/ult and full Battle/Run replay NOT_IMPLEMENTED';
export const fixturePath = join(root, 'engine/tests/vectors/battle_player_query_vectors.json.gz');

function sources() {
  return Object.fromEntries(Object.entries({ data: 'data/game_data.json', engine: 'engine/rules-core.js',
    memory: 'proposals/memory_v2.cjs', canonical: 'tools/gen_traces.mjs',
    openingGenerator: 'tools/gen_battle_opening_vectors.mjs', generator: 'tools/gen_battle_player_query_vectors.mjs',
  }).map(([key, path]) => [key, { path, sha256: sha256(readFileSync(join(root, path))) }]));
}
const loadedSources = sources();
const evolvedFrom = Object.fromEntries(data.cards.flatMap(c => (c.evo || []).map(id => [id, c.id])));

function catalog() {
  return clone({ cards: data.cards, characters: data.characters.map(c => ({ id: c.id, name: c.name, attr: c.attr,
    passive: c.passive, ult: c.ult, resonance: c.resonance.map(r => r.fx) })),
  weapons: data.weapons, parts: data.parts, relics: data.relics });
}
function cardSpec(card, plus = false, extra = {}) {
  return { id: card.id, plus, ...(evolvedFrom[card.id] ? { from: evolvedFrom[card.id] } : {}), ...extra };
}
function setup(party, deck, overrides = {}) {
  const maxHp = party.reduce((sum, a) => sum + (a.hp || MF.Stats.REFERENCE.hp), 0);
  return clone({ party, deck, hp: maxHp, maxHp, seed: 0, enemies: ['e_seed'], relics: [], ...overrides });
}
function cardSetup(card, plus) {
  const weapon = data.weapons.find(w => w.card === card.id);
  const character = weapon ? data.characters.find(c => c.id === weapon.signature) || data.characters.find(c => c.weapon === weapon.type)
    : data.characters.find(c => c.id === card.owner) || data.characters[0];
  assert.ok(character, `No authored character for ${card.id}`);
  const actor = { charId: character.id, ...(weapon ? { weaponId: weapon.id, refine: 1 } : {}) };
  return setup([actor], [cardSpec(card, plus, weapon ? { wielder: character.id } : {})]);
}
function checkpoint(battle) {
  const state = clone(battle.s), publicView = clone(battle.publicView()), receipts = clone(battle.receipts);
  return { state, stateHash: stateHash(state), rngHash: stateHash(state.rng), eventsHash: stateHash(state.ev),
    logHash: stateHash(state.log), publicView, publicViewHash: stateHash(publicView), receipts, receiptsHash: stateHash(receipts) };
}
function queries(battle) {
  const before = clone(battle.s), receipts = clone(battle.receipts);
  const pure = fn => {
    const value = fn();
    assert.ok(isDeepStrictEqual(battle.s, before), 'Player query changed state/RNG/log/ev/version');
    assert.ok(isDeepStrictEqual(battle.receipts, receipts), 'Player query changed receipts');
    return value;
  };
  return Object.keys(battle.s.cards).map(uid => ({ uid, cardId: battle.s.cards[uid].id,
    effective: clone(pure(() => battle.effDef(battle.s.cards[uid]))),
    allowedModes: clone(pure(() => battle.allowedModes(uid))), needsTarget: pure(() => battle.needsTarget(uid)),
    costs: Object.fromEntries(['now', 'next', 'after'].map(mode => [mode, pure(() => battle.costOf(uid, mode))])) }));
}
function recordCase(id, initial, metadata, expectedCatalogHash) {
  const definitionsBeforeHash = stateHash(catalog());
  assert.equal(definitionsBeforeHash, expectedCatalogHash, 'Original combat definitions changed before a case');
  const battle = MF.Battle.create(data, clone(initial));
  const created = checkpoint(battle), createdQueries = queries(battle), afterCreatedQueries = checkpoint(battle);
  const request = { rid: `${id}-begin`, v: 0, kind: 'begin', args: {} };
  const response = clone(battle.command(clone(request)));
  const begun = checkpoint(battle), queried = queries(battle), afterQueries = checkpoint(battle);
  const definitionsAfterHash = stateHash(catalog());
  assert.equal(definitionsAfterHash, definitionsBeforeHash, 'Player queries changed authored definitions');
  assert.equal(JSON.stringify(data), originalData, 'Player queries changed original source data');
  return { id, ...metadata, setup: clone(initial), created, createdQueries, afterCreatedQueries, request, response,
    begun, queries: queried, afterQueries, definitionsBeforeHash, definitionsAfterHash };
}

export function generateCases() {
  assert.deepEqual(sources(), loadedSources, 'Player-query sources changed after module load; rerun exporter');
  assert.equal(JSON.stringify(data), originalData, 'Original player-query source data changed');
  assert.equal(String(data.rules.upgradeMult), '1.3', 'Review the exact authored upgrade ratio before changing this oracle');
  const definitions = catalog(), catalogHash = stateHash(definitions), cases = [];
  const add = (id, initial, metadata) => cases.push(recordCase(id, initial, metadata, catalogHash));
  for (const card of data.cards) for (const plus of [false, true]) {
    const variant = plus ? 'plus' : 'base';
    add(`card-${card.id}-${variant}`, cardSetup(card, plus), { group: 'card', cardId: card.id, variant });
  }
  const openings = openingCases().cases;
  for (const row of openings) add(`opening-${row.id}`, row.setup, { group: 'opening', openingId: row.id });
  for (const character of data.characters) for (let res = 0; res <= 6; res++) {
    const deck = data.cards.filter(c => c.owner === character.id).flatMap(c => [cardSpec(c), cardSpec(c, true)]);
    add(`resonance-${character.id}-r${res}`, setup([{ charId: character.id, res }], deck), { group: 'resonance', characterId: character.id, resonance: res });
  }
  const queryCards = ['sera_round', 'sera_timed', 'sera_last', 'nia_tune'].map(id => ix.cards[id]);
  const party = [{ charId: 'sera' }, { charId: 'nia' }];
  const permanentParts = data.parts.filter(p => p.slot !== 'temp');
  for (const part of permanentParts) {
    const slot = part.slot === 'circuit' ? 'c' : 'e';
    add(`part-${part.id}`, setup(party, queryCards.map(c => cardSpec(c, false, { mods: { [slot]: part.id } }))), { group: 'part', partId: part.id });
  }
  add('temp-amp-begin', setup([{ charId: 'nia', res: 4 }, { charId: 'sera' }], queryCards.map(c => cardSpec(c))), { group: 'temp-part', partId: 'amp' });
  add('edge-empty-clock-lite', setup(party, queryCards.map(c => cardSpec(c, false, { mods: { c: 'c_lite' } })), { relics: ['empty_clock'] }), { group: 'edge' });
  add('edge-empty-clock-no-reserve', setup(party, queryCards.map(c => cardSpec(c, false, { mods: { c: 'c_quick' } })), { relics: ['empty_clock'] }), { group: 'edge' });
  add('edge-character-weapon-common-owner', setup([{ charId: 'aila', res: 1, weaponId: 'w_choir_bell', refine: 5 }], [
    cardSpec(ix.cards.aila_bell, true), cardSpec(ix.cards.wc_choir_bell, false, { wielder: 'aila' }), cardSpec(ix.cards.com_patch, false, { assigned: 'aila' }),
  ]), { group: 'edge' });
  add('edge-unknown-part-ignored', setup([{ charId: 'sera' }], [cardSpec(ix.cards.sera_round, false,
    { mods: { c: 'unknown-circuit', e: 'unknown-engraving' } })]), { group: 'edge', inputDomain: 'Unknown setup part IDs are ignored by the source queries' });

  assert.equal(new Set(cases.map(c => c.id)).size, cases.length, 'Duplicate player-query case id');
  const base = cases.filter(c => c.group === 'card' && c.variant === 'base');
  assert.deepEqual(base.map(c => c.cardId), data.cards.map(c => c.id), 'Missing authored card query coverage');
  assert.deepEqual(cases.filter(c => c.group === 'opening').map(c => c.openingId), openings.map(c => c.id), 'Missing opening query coverage');
  assert.deepEqual(cases.filter(c => c.group === 'part').map(c => c.partId), permanentParts.map(p => p.id), 'Missing permanent-part query coverage');
  assert.deepEqual(sources(), loadedSources, 'Player-query sources changed during generation; rerun exporter');
  return { schema, scope, dataVersion: data.meta.version, sources: clone(loadedSources), catalog: definitions, catalogHash,
    upgradeRatio: { literal: '1.3', numerator: 13, denominator: 10 },
    unresolved: { base0: { cardId: 'sion_forget', status: 'ORIGINAL_SOURCE_CONFLICT',
      retained: 'Current JS/live-card base5 +5*lastCount (added cap10); generic base0 conformance UNRESOLVED',
      anchors: ['docs/source_v0.8.1/06_chapter.md:42', 'docs/source_v0.8.1/09_chapter.md:833', 'content/characters.mjs:310', 'engine/rules-core.js:1064-1095'] } },
    reachability: { setupPartIds: permanentParts.map(p => p.id), beginTempPartIds: ['amp'],
      pendingA1: ['overheat temporary attachment', 'positive silenceUntil', 'positive freeNext', 'nonzero costMod', 'free/echoCopy tokens'],
      notAuthored: ['explicit plus.eff override', 'costIf'],
      note: 'No hand/state/data mutation. All16 part definitions are covered; temporary overheat is not put in a permanent slot. Setup plus variants do not assert Run upgrade eligibility.' },
    coverage: { cards: data.cards.length, baseModePairs: base.reduce((n, c) => n + c.createdQueries[0].allowedModes.length, 0),
      characterResonances: cases.filter(c => c.group === 'resonance').length,
      weaponRefinements: openings.filter(c => c.id.startsWith('weapon-')).length,
      relics: data.relics.length, partDefinitions: data.parts.length, permanentPartCases: permanentParts.length, beginTempPartCases: 1 },
    counts: { cases: cases.length, beginCommands: cases.length,
      queryRecords: cases.reduce((n, c) => n + c.createdQueries.length + c.queries.length, 0),
      queryCalls: cases.reduce((n, c) => n + 6 * (c.createdQueries.length + c.queries.length), 0) }, cases };
}

export function checkFixture(fixture) {
  assert.equal(fixture.schema, schema, 'Wrong player-query fixture schema');
  assert.ok(Array.isArray(fixture.cases), 'Player-query fixture requires cases');
  assert.equal(new Set(fixture.cases.map(c => c.id)).size, fixture.cases.length, 'Duplicate player-query case id');
  assert.deepEqual(fixture.sources, loadedSources, 'Player-query source provenance is stale');
  const expected = generateCases();
  assert.deepEqual(fixture.coverage, expected.coverage, 'Player-query coverage differs');
  assert.deepEqual(fixture.counts, expected.counts, 'Player-query case/query counts differ');
  assert.ok(isDeepStrictEqual(fixture, expected), 'Player-query fixture catalog, snapshots, queries, hashes or cases differ');
  return fixture.counts;
}

export function exportVectors(output = fixturePath) {
  const fixture = generateCases(), json = Buffer.from(`${JSON.stringify(fixture)}\n`);
  const bytes = output.endsWith('.gz') ? gzipSync(json, { level: 9 }) : json;
  const temporary = mkdtempSync(join(dirname(output), '.battle-player-queries-'));
  try {
    const path = join(temporary, 'fixture');
    writeFileSync(path, bytes);
    assert.deepEqual(sources(), fixture.sources, 'Player-query sources changed before export; previous fixture preserved');
    renameSync(path, output);
  } finally { rmSync(temporary, { recursive: true, force: true }); }
  return { ...fixture.counts, bytes: bytes.length, uncompressedBytes: json.length, sha256: sha256(bytes), sources: fixture.sources };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  if (args.length > 1 || (args.length && !['--check', '--help'].includes(args[0]))) throw new Error('Usage: node tools/gen_battle_player_query_vectors.mjs [--check|--help]');
  if (args[0] === '--help') console.log('Export the source-pinned A0 combat catalog and pure Create/begin player queries, or --check the lossless gzip fixture. play/ult remain NOT_IMPLEMENTED.');
  else if (args[0] === '--check') console.log(JSON.stringify({ checked: fixturePath, ...checkFixture(JSON.parse(gunzipSync(readFileSync(fixturePath)).toString('utf8'))) }));
  else console.log(JSON.stringify({ output: fixturePath, ...exportVectors() }, null, 2));
}
