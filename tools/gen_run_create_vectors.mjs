// R0 §5/7/8/16.2/20.9: actual Run.create and pure queries; expedition commands remain0.
// Ownership: this generator, its focused test and its new fixture only. Existing A1 artifacts stay frozen.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { mkdtempSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, isAbsolute, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { isDeepStrictEqual } from 'node:util';
import { gunzipSync, gzipSync, inflateRawSync } from 'node:zlib';
import { stateHash } from './gen_traces.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const require = createRequire(import.meta.url), MF = require('../engine/rules-core.js');
const data = JSON.parse(readFileSync(join(root, 'data/game_data.json'), 'utf8'));
const originalData = JSON.stringify(data), ix = MF.indexData(data);
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const schema = 'mf-run-create-v1';
const status = 'COMPLETE_CURRENT_SOURCE_R0_FACTORY_QUERIES';
const scope = 'Actual Run.create and pure available/nodeById from fresh creation only; Run commands, saving, restoration, server and Unity acceptance remain pending';
const isMain = process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url);
export const fixturePath = join(root, 'engine/tests/vectors/run_create_vectors.json.gz');

function sources() {
  return Object.fromEntries(Object.entries({ data: 'data/game_data.json', engine: 'engine/rules-core.js', memory: 'proposals/memory_v2.cjs',
    canonical: 'tools/gen_traces.mjs', fuzzPolicy: 'engine/tests/fuzz_runs.mjs', manifest: 'MANIFEST_AIO.json', manifestSha: 'MANIFEST.sha256', generator: 'tools/gen_run_create_vectors.mjs',
  }).map(([key, path]) => [key, { path, sha256: sha256(readFileSync(join(root, path))) }]));
}
const loadedSources = sources();
function inputAudit() {
  const m = JSON.parse(readFileSync(join(root, 'MANIFEST_AIO.json'), 'utf8')), rows = new Map(), changed = [], seen = new Set();
  for (const line of readFileSync(join(root, 'MANIFEST.sha256'), 'utf8').trim().split('\n')) {
    const match = /^([a-f0-9]{64})  (.+)$/.exec(line); assert.ok(match, 'Malformed manifest SHA row');
    assert.ok(!rows.has(match[2]), 'Duplicate manifest SHA path'); rows.set(match[2], match[1]);
  }
  assert.equal(rows.get('MANIFEST_AIO.json'), sha256(readFileSync(join(root, 'MANIFEST_AIO.json'))), 'Manifest pair hash differs');
  for (const f of m.files) {
    assert.ok(!isAbsolute(f.path) && !f.path.split('/').includes('..'), 'Unsafe manifest path'); assert.ok(!seen.has(f.path), 'Duplicate manifest input'); seen.add(f.path);
    assert.equal(rows.get(f.path), f.sha256, 'Manifest SHA pair differs'); const bytes = readFileSync(join(root, f.path));
    if (bytes.length !== f.bytes || sha256(bytes) !== f.sha256) changed.push(f.path);
  }
  assert.equal(rows.size, seen.size + 1, 'Manifest index differs');
  return { entries: m.files.length, manifestMatch: changed.length === 0, preExistingChangedPaths: changed, structuralErrors: [], regenerated: false };
}
const loadedInputAudit = inputAudit();
function shape(value) {
  const out = { objects: [], arrays: [], nulls: [], undefined: [] };
  const visit = (v, path) => {
    if (v === undefined) out.undefined.push(path);
    else if (v === null) out.nulls.push(path);
    else if (Array.isArray(v)) { out.arrays.push({ path, length: v.length }); v.forEach((x, i) => visit(x, [...path, i])); }
    else if (typeof v === 'object') { const keys = Object.keys(v); out.objects.push({ path, keys }); for (const key of keys) visit(v[key], [...path, key]); }
  };
  visit(value, []); return out;
}
function point(run, allowNoncanonical = false) {
  const json = JSON.stringify(run.s), state = JSON.parse(json), fieldShape = shape(run.s);
  let hash;
  try { hash = stateHash(state); }
  catch (e) { if (!allowNoncanonical || e.message !== 'Trace state requires safe integer numbers') throw e; hash = null; }
  return { state, stateHash: hash, ...(hash === null ? { canonicalError: 'NON_CANONICAL_NUMBER' } : {}), jsonHash: sha256(json), rngHash: stateHash(state.rng), mapHash: stateHash(state.map), logHash: stateHash(state.log), fieldShape, shapeHash: stateHash(fieldShape) };
}
const diagnostics = p => Object.fromEntries(Object.entries(p).filter(([key]) => !['state', 'fieldShape'].includes(key)));
function record(id, setup, metadata = {}, expected = null) {
  const input = clone(setup), beforeInput = JSON.stringify(input), row = { id, ...metadata, setup: clone(input), inputHash: sha256(beforeInput) };
  let run;
  try { run = MF.Run.create(data, input); }
  catch (e) {
    if (!expected || (expected.code ? e.code !== expected.code : e.constructor.name !== expected.name)) throw e;
    row.outcome = 'rejected'; row.error = { kind: e instanceof MF.RuleError ? 'RuleError' : e.code ? 'MemoryError' : 'NativeError', name: e.name, ...(e.code ? { code: e.code } : {}), message: e.message };
  }
  assert.equal(JSON.stringify(input), beforeInput, `${id}: Create mutated input`);
  assert.equal(JSON.stringify(data), originalData, `${id}: source definitions changed`);
  if (!run) return row;
  assert.equal(expected, null, `${id}: expected rejection did not occur`);
  const allowNoncanonical = metadata.hashDomain === 'json-only';
  row.outcome = 'created'; row.created = point(run, allowNoncanonical); row.queries = []; let latest = row.created;
  const nodes = [...run.s.map.floors.flat(), run.s.map.boss];
  for (const [method, args] of [['available', []], ...nodes.map(n => ['nodeById', [n.id]]), ['nodeById', ['missing']], ['nodeById', ['']], ['available', []]]) {
    const before = latest, result = run[method](...args), after = point(run, allowNoncanonical); latest = after;
    assert.ok(isDeepStrictEqual(before, after), `${id}: ${method} changed complete state/RNG/shape`);
    const encoded = result === undefined ? { kind: 'undefined' } : { kind: 'json', value: clone(result) };
    row.queries.push({ method, args, result: encoded, resultHash: stateHash(encoded), before: diagnostics(before), after: diagnostics(after) });
  }
  row.afterQueries = latest;
  assert.ok(isDeepStrictEqual(row.created, row.afterQueries), `${id}: queries changed state`);
  assert.equal(JSON.stringify(input), beforeInput, `${id}: queries mutated input`);
  assert.equal(JSON.stringify(data), originalData, `${id}: queries changed definitions`);
  return row;
}

function baseline(cid) {
  return [...data.cards.filter(c => c.owner === cid && c.start).map(c => ({ id: c.id })), { id: data.startingCommon[cid], assigned: cid }]
    .map((sp, i) => ({ ...sp, key: `base:${cid}:${i}` }));
}
const evolvedFrom = Object.fromEntries(data.cards.flatMap(c => (c.evo || []).map(id => [id, c.id])));
const compatible = weapon => data.characters.find(c => c.id === weapon.signature) || data.characters.find(c => c.weapon === weapon.type);
function party(first, n = 3) {
  const index = data.characters.findIndex(c => c.id === first.charId);
  return [first, ...Array.from({ length: n - 1 }, (_, i) => ({ charId: data.characters[(index + i + 1) % data.characters.length].id }))];
}
function casePlan() {
  const plan = [];
  const add = (id, actors, policy = 'legacy', extra = {}, metadata = {}, expected = null) => {
    plan.push({ id, setup: clone({ party: actors, ...(policy === 'v2' ? { memoryPolicy: 'v2', runId: id } : {}), ...extra }),
      metadata: { parity: 'supported', policy, ...metadata }, expected });
  };
  for (const c of data.characters) for (let res = 0; res <= 6; res++) for (const policy of ['legacy', 'v2']) for (const length of ['short', 'full']) {
    add(`character-${c.id}-r${res}-${policy}-${length}`, party({ charId: c.id, res }), policy, { length, seed: res * 17 + data.characters.indexOf(c) }, { group: 'character', characterId: c.id, resonance: res });
  }
  for (const w of data.weapons) for (let refine = 1; refine <= 5; refine++) {
    const c = compatible(w), level = [1, 20, 59, 60, 61][refine - 1];
    add(`weapon-${w.id}-r${refine}`, [{ charId: c.id, weaponId: w.id, refine, level, weaponLevel: level }], 'v2', {}, { group: 'weapon', weaponId: w.id, refine });
  }
  for (let level = 1; level <= 60; level++) add(`level-${level}`, [{ charId: 'sera', level }], level % 2 ? 'legacy' : 'v2', {}, { group: 'level', level });
  const profiles = [
    ['reference', {}], ['reference-null', { level: null, armor: 15, accessory: 15, hp: 999, power4: 999, gearBps: 999 }],
    ['minimum', { level: 0, weaponLevel: 0, armor: 0, accessory: 0, skillLv: 0, ultLv: 0 }],
    ['negative', { level: -1, weaponLevel: -1, armor: -1, accessory: -1, skillLv: -1, ultLv: -1, asc: -1, res: -1, refine: 0 }],
    ['maximum', { level: 60, weaponLevel: 60, armor: 15, accessory: 15, skillLv: 10, ultLv: 10, asc: 4, res: 6, refine: 5 }],
    ['over-cap', { level: 61, weaponLevel: 61, armor: 16, accessory: 16, skillLv: 11, ultLv: 11, asc: 5, res: 7, refine: 6 }],
    ['i32-max', { level: 2147483647, weaponLevel: 2147483647, armor: 2147483647, accessory: 2147483647, skillLv: 2147483647, ultLv: 2147483647 }],
    ['i32-min', { level: -2147483648, weaponLevel: -2147483648, armor: -2147483648, accessory: -2147483648, skillLv: -2147483648, ultLv: -2147483648 }],
    ['supplied-derived-ignored', { hp: 1, power4: 1, gearBps: 9000, skillLv: 4, ultLv: 7 }],
    ['default-level20', { level: 20 }], ['armor-null', { level: 20, armor: null, accessory: null }], ['gear-zero', { level: 20, armor: 0, accessory: 0 }],
  ];
  for (const c of data.characters) for (const [profile, fields] of profiles) add(`stats-${c.id}-${profile}`, [{ charId: c.id, ...fields }], 'v2', {}, { group: 'stats', characterId: c.id, profile });
  for (const rarity of ['R', 'SR', 'SSR']) {
    const w = data.weapons.find(w => w.rarity === rarity), c = compatible(w);
    for (let weaponLevel = 1; weaponLevel <= 60; weaponLevel++) add(`weapon-level-${rarity}-${weaponLevel}`, [{ charId: c.id, level: 20, weaponId: w.id, weaponLevel }], 'legacy', {}, { group: 'weapon-level', rarity, weaponLevel });
  }
  for (let enhance = -1; enhance <= 16; enhance++) for (const slot of ['armor', 'accessory', 'both']) {
    const fields = slot === 'both' ? { armor: enhance, accessory: enhance } : { [slot]: enhance };
    add(`gear-${slot}-${enhance}`, [{ charId: 'aila', level: 20, ...fields }], 'v2', {}, { group: 'gear', slot, enhance });
  }
  for (const c of data.characters) for (let skillLv = 1; skillLv <= 10; skillLv++) add(`skill-${c.id}-${skillLv}`, [{ charId: c.id, skillLv, ultLv: 11 - skillLv, res: 6 }], 'legacy', {}, { group: 'skill', characterId: c.id, skillLv });
  for (let level = 1; level <= 60; level++) for (const hard of [false, true]) add(`stage-${level}-${hard ? 'hard' : 'normal'}`, party({ charId: 'sera', level, weaponId: 'w_star_bow', weaponLevel: level }), 'v2',
    { stageLevel: level, difficulty: hard ? { hp: 120, dmg: 125 } : { hp: 100, dmg: 100 } }, { group: 'stage', stageLevel: level, hard });
  for (let seed = 0; seed < 300; seed++) add(`map-seed-${seed}`, party({ charId: 'sera' }), seed % 2 ? 'v2' : 'legacy', { seed, length: seed % 2 ? 'full' : 'short' }, { group: 'map', seed });
  for (const c of data.cards) {
    const cid = ix.chars[c.owner] ? c.owner : 'sera';
    const raw = { id: c.id, plus: false, ...(evolvedFrom[c.id] ? { from: evolvedFrom[c.id] } : {}), ...(c.owner === 'common' ? { assigned: cid } : {}), ...(c.owner === 'weapon' ? { wielder: cid } : {}) };
    add(`legacy-card-${c.id}`, [{ charId: cid }], 'legacy', { memory: { [cid]: [raw] } }, { group: 'legacy-card', cardId: c.id });
    if (!ix.chars[c.owner] && c.owner !== 'common') continue;
    for (const plus of [false, true]) add(`memory-card-${c.id}-${plus ? 'plus' : 'base'}`, [{ charId: cid }], 'v2', { memory: { [cid]: [...baseline(cid), { ...raw, key: `extra:${c.id}`, plus }] } }, { group: 'memory-card', cardId: c.id, plus });
  }
  for (const part of data.parts.filter(p => p.slot !== 'temp')) for (const policy of ['legacy', 'v2']) {
    const specs = baseline('sera'); specs[0].mods = { [part.slot === 'circuit' ? 'c' : 'e']: part.id };
    add(`part-${part.id}-${policy}`, [{ charId: 'sera' }], policy, { memory: { sera: specs } }, { group: 'part', partId: part.id });
  }
  for (const relic of data.relics) add(`relic-${relic.id}`, party({ charId: 'sera' }), 'v2', { relics: [relic.id] }, { group: 'relic', relicId: relic.id });
  for (let asc = 0; asc <= 4; asc++) for (let res = 0; res <= 6; res++) for (let cost = 6; cost <= 9; cost++) {
    const capacity = 6 + (asc >= 2 ? 1 : 0) + (asc >= 4 ? 1 : 0) + (res >= 4 ? 1 : 0);
    const memory = { sera: [...baseline('sera'), ...Array.from({ length: cost }, (_, n) => ({ id: 'sera_round', key: `extra:${n}` }))] };
    add(`capacity-a${asc}-r${res}-cost${cost}`, [{ charId: 'sera', asc, res }], 'v2', { memory }, { group: 'capacity', asc, resonance: res, memoryCost: cost, capacity }, cost > capacity ? { code: 'OVER_CAPACITY' } : null);
  }
  for (const res of [-2147483648, -7, -6, -5, -3, -2, -1, 7, 2147483647]) {
    const capacity = [-2, -1, 7, 2147483647].includes(res) ? 7 : 6;
    add(`edge-capacity-signed-res-${res}`, [{ charId: 'sera', res }], 'v2', { memory: { sera: [...baseline('sera'), ...Array.from({ length: 7 }, (_, n) => ({ id: 'sera_round', key: `signed:${n}` }))] } },
      { group: 'edge', memoryCost: 7, capacity }, capacity < 7 ? { code: 'OVER_CAPACITY' } : null);
  }
  const edge = (id, actors, policy, extra = {}, expected = null, metadata = {}) => add(id, actors, policy, extra, { group: 'edge', ...metadata }, expected);
  for (const policy of ['legacy', 'v2']) {
    edge(`edge-defaults-${policy}`, party({ charId: 'sera' }), policy);
    edge(`edge-solo-${policy}`, [{ charId: 'sera' }], policy);
    edge(`edge-all10-party-${policy}`, data.characters.map(c => ({ charId: c.id })), policy);
    edge(`edge-empty-party-${policy}`, [], policy, {}, policy === 'v2' ? { code: 'INVALID_SNAPSHOT' } : null);
    edge(`edge-duplicate-party-${policy}`, [{ charId: 'sera' }, { charId: 'sera' }], policy, {}, policy === 'v2' ? { code: 'INVALID_SNAPSHOT' } : null);
    edge(`edge-empty-zones-${policy}`, [{ charId: 'sera' }], policy, { zones: [] });
    edge(`edge-null-optionals-${policy}`, [{ charId: 'sera', weaponId: null, level: null, skillLv: null, ultLv: null }], policy, { memory: null, relics: null, seed: null, zones: null, bosses: null, stageLevel: null, difficulty: null });
    edge(`edge-explicit-empty-memory-${policy}`, [{ charId: 'sera' }], policy, { memory: { sera: [] } });
    edge(`edge-character-null-memory-${policy}`, [{ charId: 'sera' }], policy, { memory: { sera: null } });
    edge(`edge-zone2-first-${policy}`, party({ charId: 'sera' }), policy, { zones: [2, 1, 2], seed: 42 });
    edge(`edge-unknown-weapon-fallback-${policy}`, [{ charId: 'sera', level: 20, weaponId: 'missing' }], policy);
    edge(`edge-incompatible-authored-weapon-${policy}`, [{ charId: 'sera', level: 20, weaponId: 'w_sion', weaponLevel: 20 }], policy);
    for (const zone of [1, 2]) for (const group of data.encounters[zone].boss) {
      const boss = group[0]; edge(`edge-boss-${boss}-${policy}`, party({ charId: 'sera' }), policy, { zones: [zone], bosses: { [zone]: boss } }, null, { bossId: boss });
    }
  }
  for (const seed of [-9007199254740991, -4294967296, -1, 0, 2147483647, 2147483648, 4294967295, 4294967296, 9007199254740991]) edge(`edge-seed-${seed}`, [{ charId: 'sera' }], 'v2', { seed });
  for (const stageLevel of [-2147483648, -1, 0, 61, 2147483647]) edge(`edge-stage-boundary-${stageLevel}`, [{ charId: 'sera' }], 'v2', { stageLevel });
  for (const [name, difficulty] of [['empty', {}], ['partial', { hp: 120 }], ['zeros', { hp: 0, dmg: 0 }], ['negative', { hp: -50, dmg: -2 }]]) for (const stageLevel of [0, 1])
    edge(`edge-difficulty-${name}-stage${stageLevel}`, [{ charId: 'sera' }], 'legacy', { stageLevel, difficulty });
  edge('edge-explicit-legacy-policy', [{ charId: 'sera' }], 'legacy', { memoryPolicy: 'legacy' });
  edge('edge-unknown-policy-is-legacy', [{ charId: 'sera' }], 'legacy', { memoryPolicy: 'future-policy', runId: 'ignored-legacy-run-id', memoryVersion: 99 });
  const colliding = baseline('sera').concat([1, 2, 4].map(n => ({ id: 'sera_round', key: `run:collision:${n}` })));
  edge('edge-run-key-collisions', party({ charId: 'sera' }), 'v2', { runId: 'collision', memory: { sera: colliding } });
  edge('edge-nonparty-memory-preserved', [{ charId: 'sera' }], 'v2', { runId: 'outside', memory: { nia: [{ key: 'run:outside:1', id: 'nia_wrench', plus: true }] } });
  edge('edge-special-run-id', [{ charId: 'sera' }], 'v2', { runId: '한글:/😀%\u0000' });
  edge('edge-memory-version-max-safe', [{ charId: 'sera' }], 'v2', { memoryVersion: 9007199254740991 });
  for (const policy of ['legacy', 'v2']) {
    const specs = baseline('sera');
    Object.assign(specs[0], { plus: false, mods: { c: null, e: null }, growth: 0, gained: false, evolved: false, opaque: { zero: 0, false: false, nil: null, list: [0, false, null] } });
    edge(`edge-sparse-card-metadata-${policy}`, [{ charId: 'sera' }], policy, { memory: { sera: specs } });
  }
  for (const root of data.cards.filter(c => c.growth)) for (const evolved of root.evo) for (const useMemory of [false, true]) {
    const specs = baseline('yuri'), existing = specs.find(sp => sp.id === root.id);
    const evolvedSpec = { id: evolved, from: root.id, key: existing?.key || `saved:${evolved}`, plus: true, mods: { e: 'e_guard' } };
    if (existing) specs[specs.indexOf(existing)] = evolvedSpec; else specs.push(evolvedSpec);
    edge(`edge-evolved-${evolved}-${useMemory ? 'memory' : 'base'}`, [{ charId: 'yuri', asc: 2 }], 'v2', { memory: { yuri: specs }, useMemory }, null,
      { evolvedId: evolved, useMemory, inputDomain: 'Authored ascension2 gives capacity7 needed by S-root evolution+upgrade+permanent part' });
  }
  for (const mode of ['removed', 'evolved', 'rekeyed', 'empty']) {
    const specs = mode === 'empty' ? [] : baseline('yuri');
    if (mode === 'removed') specs.shift();
    if (mode === 'evolved') Object.assign(specs[0], { id: 'yuri_cocoon_guard', from: 'yuri_cocoon' });
    if (mode === 'rekeyed') specs[0].key = 'run:base-collision:1';
    edge(`edge-use-memory-false-${mode}`, [{ charId: 'yuri' }], 'v2', { runId: 'base-collision', memory: { yuri: specs }, useMemory: false });
  }
  const mismatched = baseline('sera'); mismatched.at(-1).assigned = 'nia';
  edge('edge-legacy-common-assigned-is-preserved', [{ charId: 'sera' }], 'legacy', { memory: { sera: mismatched } });
  const bad = (id, extra, code, actors = [{ charId: 'sera' }]) => add(id, actors, 'v2', extra, { group: 'error' }, { code });
  for (const [name, runId] of [['empty', ''], ['null', null], ['number', 0]]) bad(`error-run-id-${name}`, { runId }, 'RUN_ID_REQUIRED');
  for (const [name, memoryVersion] of [['negative', -1], ['fraction', 1.5], ['bool', true], ['string', '1']]) bad(`error-memory-version-${name}`, { memoryVersion }, 'INVALID_SNAPSHOT');
  for (const zone of [0, -1, 3]) bad(`error-zone-${zone}`, { zones: [zone] }, 'ZONE_NOT_AUTHORED');
  bad('error-zone-later-unauthored', { zones: [1, 3] }, 'ZONE_NOT_AUTHORED');
  bad('error-v2-unknown-character', {}, 'INVALID_SNAPSHOT', [{ charId: 'missing' }]);
  const corrupt = (id, mutate, code) => { const memory = { sera: baseline('sera') }; mutate(memory.sera); bad(id, { memory }, code); };
  corrupt('error-memory-empty-key', rows => { rows[0].key = ''; }, 'IDENTITY_REQUIRED');
  corrupt('error-memory-duplicate-key', rows => { rows[1].key = rows[0].key; }, 'IDENTITY_REQUIRED');
  corrupt('error-memory-unknown-card', rows => { rows[0].id = 'missing'; }, 'INVALID_CARD');
  corrupt('error-memory-foreign-card', rows => { rows[0].id = 'nia_wrench'; }, 'INVALID_CARD');
  corrupt('error-memory-weapon', rows => { rows[0].id = 'wc_sera'; }, 'INVALID_CARD');
  corrupt('error-memory-token-flag', rows => { rows[0].token = true; }, 'INVALID_CARD');
  corrupt('error-memory-weapon-flag', rows => { rows[0].weapon = true; }, 'INVALID_CARD');
  corrupt('error-memory-common-assignment', rows => { rows.at(-1).assigned = 'nia'; }, 'INVALID_ASSIGNMENT');
  corrupt('error-memory-plus-null', rows => { rows[0].plus = null; }, 'INVALID_UPGRADE');
  corrupt('error-memory-part-temp', rows => { rows[0].mods = { c: 'amp' }; }, 'INVALID_PART');
  corrupt('error-memory-part-wrong-slot', rows => { rows[0].mods = { e: 'c_echo' }; }, 'INVALID_PART');
  corrupt('error-memory-part-unknown', rows => { rows[0].mods = { c: 'missing' }; }, 'INVALID_PART');
  corrupt('error-memory-part-unknown-field', rows => { rows[0].mods = { temp: 'amp' }; }, 'INVALID_PART');
  corrupt('error-memory-part-array', rows => { rows[0].mods = []; }, 'INVALID_PART');
  const evoBad = [{ key: 'evo', id: 'yuri_cocoon_guard' }, { key: 'evo', id: 'yuri_cocoon_guard', from: 'yuri_sprout' }];
  for (let n = 0; n < evoBad.length; n++) bad(`error-memory-evolution-${n}`, { memory: { yuri: [evoBad[n]] } }, 'INVALID_EVOLUTION', [{ charId: 'yuri' }]);
  bad('error-memory-cross-character-key', { memory: { sera: baseline('sera'), nia: baseline('nia').map((sp, n) => n ? sp : { ...sp, key: 'base:sera:0' }) } }, 'IDENTITY_REQUIRED', [{ charId: 'sera' }, { charId: 'nia' }]);
  for (const [id, setup, name] of [
    ['missing-party', {}, 'SyntaxError'], ['null-party', { party: null }, 'TypeError'], ['unknown-character', { party: [{ charId: 'missing' }] }, 'TypeError'],
    ['unknown-card', { party: [{ charId: 'sera' }], memory: { sera: [{ id: 'missing' }] } }, 'TypeError'],
    ['string-card', { party: [{ charId: 'sera' }], memory: { sera: ['sera_round'] } }, 'TypeError'],
  ]) plan.push({ id: `native-${id}`, setup, metadata: { group: 'native-error', parity: 'js-source-only', policy: 'legacy' }, expected: { name } });
  for (const [id, actors, extra, hashDomain] of [
    ['unsafe-seed-positive', [{ charId: 'sera' }], { seed: 9007199254740992 }, 'json-only'],
    ['unsafe-seed-negative', [{ charId: 'sera' }], { seed: -9007199254740992 }, 'json-only'],
    ['level-outside-i32', [{ charId: 'sera', level: 2147483648 }], {}, 'integer'],
    ['stage-outside-i32', [{ charId: 'sera' }], { stageLevel: 2147483648 }, 'integer'],
    ['difficulty-derived-overflow', [{ charId: 'sera' }], { stageLevel: 60, difficulty: { hp: 2147483647, dmg: 2147483647 } }, 'integer'],
  ]) add(`domain-${id}`, actors, 'legacy', extra, { group: 'domain', parity: 'typed-domain-rejection', typedExpectedCode: 'RUN_NUMBER_OUT_OF_RANGE', hashDomain });
  const opaque = baseline('sera'); opaque[0].opaque = { fraction: 1.25, negativeHalf: -0.5 };
  add('edge-opaque-fractional-metadata', [{ charId: 'sera' }], 'v2', { memory: { sera: opaque }, useMemory: false }, { group: 'edge', parity: 'supported-json-metadata', hashDomain: 'json-only' });
  return plan;
}

function addAnchors(cases) {
  const byId = new Map(cases.map(c => [c.id, c]));
  const put = (id, tag, facts) => {
    const row = byId.get(id); assert.ok(row?.created, `Missing literal anchor ${id}`);
    for (const f of facts) {
      let value = row.created; for (const key of f.path) value = value?.[key];
      if (f.absent) { let parent = row.created; for (const key of f.path.slice(0, -1)) parent = parent?.[key]; assert.ok(parent && !Object.hasOwn(parent, f.path.at(-1)), `${id}: ${tag} absent field`); }
      else assert.deepEqual(value, f.value, `${id}/${tag}: ${f.path.join('.')}`);
    }
    (row.anchors ||= []).push({ tag, facts });
  };
  const f = (path, value) => ({ path: ['state', ...path], value });
  put('edge-defaults-legacy', 'normal-source-party243', [f(['hp'], 243), f(['maxHp'], 243), f(['hpRef'], 243), f(['coins'], 60), f(['deck', 'length'], 18), f(['seqCard'], 18), f(['phase'], 'MAP'), f(['battle'], null), { path: ['state', 'memoryPolicy'], absent: true }]);
  put('stats-sera-default-level20', 'default-weapon-does-not-invent-stat-bonus', [f(['party', 0, 'power4'], 96), f(['party', 0, 'hp'], 80), f(['deck', 5, 'id'], 'wc_basic_ranged'), { path: ['state', 'party', 0, 'weaponId'], absent: true }]);
  put('weapon-w_sera-r2', 'supplied-SSR-level20', [f(['party', 0, 'power4'], 112), f(['party', 0, 'hp'], 80), f(['party', 0, 'refine'], 2)]);
  put('stats-harin-maximum', 'level60-armor15-skill10', [f(['party', 0, 'power4'], 136), f(['party', 0, 'hp'], 151), f(['party', 0, 'gearBps'], 1000), f(['party', 0, 'skillLv'], 10), f(['party', 0, 'ultLv'], 10)]);
  put('gear-both-0', 'zero-enhance-is-equipped', [f(['party', 0, 'hp'], 84), f(['party', 0, 'gearBps'], 250)]);
  put('gear-both-15', 'gear-floor-boundary', [f(['party', 0, 'hp'], 106), f(['party', 0, 'gearBps'], 1000)]);
  put('stats-sera-supplied-derived-ignored', 'derived-inputs-overwritten', [f(['party', 0, 'power4'], 160), f(['party', 0, 'hp'], 81), f(['party', 0, 'gearBps'], 0)]);
  for (const [id, hp, dmg] of [['stage-1-normal', 53, 75], ['stage-1-hard', 64, 94], ['stage-60-normal', 100, 148], ['edge-difficulty-negative-stage1', -26, -1], ['edge-difficulty-zeros-stage1', 53, 75], ['edge-difficulty-zeros-stage0', 0, 0]])
    put(id, 'stage-rounding-and-source-zero-fallback', [f(['difficulty'], { hp, dmg })]);
  put('edge-difficulty-empty-stage0', 'empty-difficulty-preserves-absence', [f(['difficulty'], {}), f(['stageLevel'], null)]);
  put('edge-empty-zones-v2', 'source-own-undefined-is-not-null', [{ path: ['state', 'zone'], absent: true }, { path: ['state', 'map', 'zone'], absent: true },
    { path: ['fieldShape', 'undefined'], value: [['zone'], ['map', 'zone']] }, f(['map', 'boss', 'id'], 'zundefinedboss')]);
  put('edge-run-key-collisions', 'shared-key-sequence-skips-before-and-deck', [f(['seqCard'], 6), f(['deck', 8, 'key'], 'run:collision:3'), f(['deck', 14, 'key'], 'run:collision:5'), f(['deck', 20, 'key'], 'run:collision:6')]);
  put('edge-use-memory-false-evolved', 'before-baseline-playBefore-are-distinct', [f(['memoryBefore', 'yuri', 0, 'id'], 'yuri_cocoon_guard'), f(['memoryBaseline', 'yuri', 0, 'id'], 'yuri_cocoon'), f(['playBefore', 'yuri', 0, 'id'], 'yuri_cocoon'), f(['playBefore', 'yuri', 0, 'key'], 'run:base-collision:1'), f(['seqCard'], 2)]);
  put('edge-use-memory-false-rekeyed', 'base-play-rekeys-around-saved-identity', [f(['memoryBefore', 'yuri', 0, 'key'], 'run:base-collision:1'), f(['playBefore', 'yuri', 0, 'key'], 'run:base-collision:2'), f(['seqCard'], 3)]);
  put('edge-use-memory-false-empty', 'empty-before-stays-empty-during-base-play', [f(['memoryBefore', 'yuri'], []), f(['playBefore', 'yuri', 'length'], 5), f(['seqCard'], 6)]);
  put('edge-duplicate-party-legacy', 'legacy-does-not-invent-party-deduplication', [f(['hp'], 162), f(['deck', 'length'], 12), f(['seqCard'], 12), f(['offered'], { sera: 0 })]);
  put('edge-empty-party-legacy', 'legacy-empty-party-source-creation', [f(['hp'], 0), f(['deck'], []), f(['seqCard'], 0), f(['phase'], 'MAP')]);
  put('edge-all10-party-v2', 'v2-has-no-source-upper-party-limit', [f(['hp'], 810), f(['deck', 'length'], 60), f(['seqCard'], 10)]);
  put('map-seed-0', 'map-seed0-lazy-left-and-rest-guarantee', [f(['rng'], [4058753769, 2789522146, 1627861374, 3282270325]), f(['map', 'floors', 1, 0, 'type'], 'event'), f(['map', 'floors', 1, 2, 'next'], ['z1f2n1', 'z1f2n0']), f(['map', 'floors', 3, 0, 'type'], 'rest'), { path: ['state', 'map', 'boss', 'next'], absent: true }]);
  put('map-seed-1', 'map-seed1-right-and-width-order', [f(['rng'], [1519060595, 2189352444, 1311632316, 3393262978]), f(['map', 'floors', 0, 0, 'next'], ['z1f1n0', 'z1f1n1']), f(['map', 'floors', 6, 'length'], 3)]);
  put('map-seed-3', 'elite-cap-preserves-first-two', [f(['map', 'floors', 3, 0, 'type'], 'elite'), f(['map', 'floors', 4, 1, 'type'], 'elite'), f(['map', 'floors', 5, 0, 'type'], 'battle')]);
  put('map-seed-4', 'adjacent-elite-and-unparented-edge-repair', [f(['rng'], [1105816929, 3057896731, 831829349, 2938419454]), f(['map', 'floors', 3, 2, 'type'], 'elite'), f(['map', 'floors', 4, 1, 'type'], 'battle'), f(['map', 'floors', 5, 1, 'next'], ['z1f6n2', 'z1f6n1'])]);
}

// Generating Node release is truthful provenance only: exactly { node: "major.minor.patch" }, no other runtime fields.
function runtimeNode(runtime) {
  assert.ok(runtime !== null && typeof runtime === 'object' && !Array.isArray(runtime) && isDeepStrictEqual(Object.keys(runtime), ['node'])
    && typeof runtime.node === 'string' && /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.test(runtime.node), 'Native diagnostics runtime must be exactly {node: numeric semver}');
  return runtime.node;
}

export function generateCases() {
  assert.deepEqual(sources(), loadedSources, 'Run factory sources changed after load; rerun');
  const plan = casePlan(), cases = [], failures = [];
  for (const c of plan) {
    try { cases.push(record(c.id, c.setup, c.metadata, c.expected)); }
    catch (e) { failures.push({ id: c.id, code: e.code || e.name, message: e.message }); }
    if (isMain && cases.length && cases.length % 500 === 0) process.stderr.write(`R0 factory progress ${cases.length}/${plan.length}\n`);
  }
  if (failures.length) throw new Error(JSON.stringify({ complete: false, planned: plan.length, recorded: cases.length, failures }));
  assert.equal(new Set(cases.map(c => c.id)).size, cases.length, 'Duplicate run-create case id');
  addAnchors(cases);
  const group = name => cases.filter(c => c.group === name), supported = cases.filter(c => c.parity.startsWith('supported'));
  const counts = { cases: cases.length, factories: cases.length, created: cases.filter(c => c.outcome === 'created').length, rejected: cases.filter(c => c.outcome === 'rejected').length,
    queries: cases.reduce((n, c) => n + (c.queries?.length || 0), 0), supportedFactories: supported.length,
    supportedCreated: supported.filter(c => c.outcome === 'created').length, supportedRejected: supported.filter(c => c.outcome === 'rejected').length,
    supportedQueries: supported.reduce((n, c) => n + (c.queries?.length || 0), 0), sourceOnlyFactories: cases.filter(c => c.parity === 'js-source-only').length,
    typedDomainProbes: group('domain').length, runCommands: 0 };
  const coverage = { caseIds: cases.map(c => c.id), groups: Object.fromEntries([...new Set(cases.map(c => c.group))].map(g => [g, group(g).map(c => c.id)])),
    characterResonancePolicyLengths: group('character').map(c => `${c.characterId}:${c.resonance}:${c.policy}:${c.setup.length}`),
    weaponRefinements: group('weapon').map(c => `${c.weaponId}:r${c.refine}`), levels: group('level').map(c => c.level),
    weaponRarityLevels: group('weapon-level').map(c => `${c.rarity}:${c.weaponLevel}`), gearEnhancementSlots: group('gear').map(c => `${c.slot}:${c.enhance}`),
    skills: group('skill').map(c => `${c.characterId}:${c.skillLv}`), stages: group('stage').map(c => `${c.stageLevel}:${c.hard ? 'hard' : 'normal'}`), mapSeeds: group('map').map(c => c.seed),
    legacyCardIds: group('legacy-card').map(c => c.cardId), stableMemoryCardVariants: group('memory-card').map(c => `${c.cardId}:${c.plus ? 'plus' : 'base'}`),
    permanentPartPolicies: group('part').map(c => `${c.partId}:${c.policy}`), relicIds: group('relic').map(c => c.relicId),
    declaredBossIds: [...new Set(group('edge').filter(c => c.bossId).map(c => c.bossId))],
    anchors: cases.flatMap(c => (c.anchors || []).map((a, anchorIndex) => ({ caseId: c.id, anchorIndex, tag: a.tag }))),
    dimensions: 'Exact sets above; no full Cartesian claim outside character/resonance/policy/length and weapon/refine matrices', bossEncounterSelections: 0 };
  assert.equal(coverage.characterResonancePolicyLengths.length, data.characters.length * 7 * 2 * 2);
  assert.equal(coverage.weaponRefinements.length, data.weapons.length * 5);
  assert.deepEqual(coverage.legacyCardIds, data.cards.map(c => c.id));
  assert.deepEqual(coverage.stableMemoryCardVariants, data.cards.filter(c => ix.chars[c.owner] || c.owner === 'common').flatMap(c => [`${c.id}:base`, `${c.id}:plus`]));
  assert.deepEqual(coverage.mapSeeds, Array.from({ length: 300 }, (_, n) => n));
  assert.deepEqual(sources(), loadedSources, 'Run factory sources changed during generation; rerun');
  return { schema, status, scope, dataVersion: data.meta.version, sources: clone(loadedSources), inputManifestAudit: clone(loadedInputAudit),
    nativeDiagnosticsRuntime: { node: runtimeNode({ node: process.versions.node }) },
    domains: { arithmetic: 'Party/stage arithmetic inputs and derived fields use signedInt32; seed/version use JS safe-integer long',
      opaqueMetadata: 'Copied JSON values retain fractions; json-only rows preserve full graphs and JSON hashes while explicitly withholding unsupported integer canonical hashes',
      sourceOnlyNativeErrors: 'Native SyntaxError/TypeError are diagnostic rows, not invented C# RuleError equivalence',
      typedDomainRejections: 'Actual JS creation evidence retained; C# must reject these explicitly with RUN_NUMBER_OUT_OF_RANGE',
      undefined: 'Source own-undefined paths are listed in fieldShape.undefined; JSON graph omits those fields. Query undefined is a distinct result kind, never null' },
    reachability: { factoryOnly: true, runCommands: 0, safePublicView: false, available: 'Fresh MAP/pos:null only; no state injection to exercise later node/phase paths',
      nodeByIdMissing: 'Explicit JSON null; no normal fresh query returns undefined', memorySnapshots: 'Legitimate departure inputs only; no saved-result or afterBattle injection',
      boss: 'Provided authored IDs are retained; encounter selection happens after creation and is not claimed', savingAndRestoration: 'PENDING' }, coverage, counts, cases };
}

function validatePoint(p, row) {
  let hash;
  try { hash = stateHash(p.state); } catch (e) { assert.equal(row.hashDomain, 'json-only'); assert.equal(e.message, 'Trace state requires safe integer numbers'); hash = null; }
  assert.equal(p.stateHash, hash, `${row.id}: state hash`); if (hash === null) assert.equal(p.canonicalError, 'NON_CANONICAL_NUMBER');
  assert.equal(p.jsonHash, sha256(JSON.stringify(p.state)), `${row.id}: ordered JSON hash`);
  for (const [field, value] of [['rngHash', p.state.rng], ['mapHash', p.state.map], ['logHash', p.state.log], ['shapeHash', p.fieldShape]]) assert.equal(p[field], stateHash(value), `${row.id}: ${field}`);
  const portable = shape(p.state), projected = clone(p.fieldShape);
  for (const object of projected.objects) object.keys = object.keys.filter(k => !projected.undefined.some(path => isDeepStrictEqual(path, [...object.path, k])));
  for (const path of projected.undefined) { let parent = p.state; for (const key of path.slice(0, -1)) parent = parent?.[key]; assert.ok(parent && !Object.hasOwn(parent, path.at(-1))); }
  projected.undefined = []; assert.deepEqual(projected, portable, `${row.id}: lossless shape`);
}

export function checkFixture(fixture) {
  assert.equal(fixture.schema, schema, 'Wrong run-create schema'); assert.equal(fixture.status, status, 'Run factory/query closure incomplete');
  const node = runtimeNode(fixture.nativeDiagnosticsRuntime);
  assert.deepEqual(fixture.sources, loadedSources, 'Run factory source provenance stale');
  assert.ok(Array.isArray(fixture.cases)); assert.equal(new Set(fixture.cases.map(c => c.id)).size, fixture.cases.length, 'Duplicate run-create case id');
  assert.deepEqual(fixture.cases.map(c => c.id), casePlan().map(c => c.id), 'Run factory case coverage omitted/reordered');
  for (const row of fixture.cases) {
    assert.equal(row.inputHash, sha256(JSON.stringify(row.setup)), `${row.id}: input hash`);
    if (row.outcome !== 'created') { assert.equal(row.outcome, 'rejected'); assert.ok(row.error); assert.ok(!Object.hasOwn(row, 'created') && !Object.hasOwn(row, 'queries')); continue; }
    validatePoint(row.created, row); validatePoint(row.afterQueries, row); assert.deepEqual(row.created, row.afterQueries, `${row.id}: query purity`);
    for (const q of row.queries) { assert.equal(q.resultHash, stateHash(q.result), `${row.id}: query result hash`); assert.deepEqual(q.before, diagnostics(row.created)); assert.deepEqual(q.after, q.before); }
  }
  const expected = generateCases();
  assert.deepEqual(fixture.counts, expected.counts, 'Run factory/query counts differ'); assert.deepEqual(fixture.coverage, expected.coverage, 'Run factory exact coverage differs');
  // Only the validated generating Node version may differ; every other field and byte, native error rows included, stays exact.
  const comparable = { ...expected, nativeDiagnosticsRuntime: { node } };
  assert.ok(isDeepStrictEqual(fixture, comparable) && JSON.stringify(fixture) === JSON.stringify(comparable), 'Run factory state/shape/JSON/query/error data differ from fresh creation');
  return fixture.counts;
}

// Native JSON.parse validates syntax; this bounded scan additionally rejects repeated object keys,
// including escape-equivalent spellings that a reviver cannot observe after JSON.parse overwrites them.
function uniqueJSON(text) {
  const value = JSON.parse(text); let at = 0;
  const ws = () => { while (/\s/.test(text[at] || '') && at < text.length) at++; };
  const string = () => { const start = at++; while (at < text.length) { const c = text[at++]; if (c === '\\') at++; else if (c === '"') break; } return JSON.parse(text.slice(start, at)); };
  const walk = () => {
    ws(); const c = text[at];
    if (c === '{') { at++; ws(); const seen = new Set(); if (text[at] !== '}') for (;;) { ws(); const key = string(); assert.ok(!seen.has(key), `Duplicate JSON key ${key}`); seen.add(key); ws(); at++; walk(); ws(); if (text[at] !== ',') break; at++; } at++; }
    else if (c === '[') { at++; ws(); if (text[at] !== ']') for (;;) { walk(); ws(); if (text[at] !== ',') break; at++; } at++; }
    else if (c === '"') string(); else { while (at < text.length && !/[\s,}\]]/.test(text[at])) at++; }
  };
  walk(); ws(); assert.equal(at, text.length, 'Trailing JSON input'); return value;
}

// RFC 1952 single member as exported: fixed CM8/FLG0/MTIME0/XFL2 header, one raw DEFLATE stream whose real consumed length
// native inflate reports, then exactly CRC32+ISIZE. Compressed bytes are never compared: valid DEFLATE encodings differ across
// zlib builds, and the OS byte (offset 9) is the writer's platform code, so those are the only container bytes allowed to vary.
// Native APIs only, all present in Node 22.0 (package engines); no zlib.crc32 (22.2+) and no copied CRC implementation.
const gzipHeader = Buffer.from([0x1f, 0x8b, 8, 0, 0, 0, 0, 0, 2]);
function gunzipMember(input) {
  assert.ok(input.subarray(0, 9).equals(gzipHeader), 'Gzip header must be CM8, FLG0, MTIME0, XFL2 (only OS may vary)');
  const { buffer, engine } = inflateRawSync(input.subarray(10), { info: true }), end = 10 + engine.bytesWritten;
  assert.equal(input.length - end, 8, 'Gzip DEFLATE stream must be followed by exactly CRC32+ISIZE (truncated, trailing or concatenated member)');
  assert.equal(input.readUInt32LE(end + 4), buffer.length >>> 0, 'Gzip ISIZE differs from inflated JSON');
  // One member with an exact footer is proven above, so native gunzip's remaining member check is its CRC32 ("incorrect data check").
  let checked;
  try { checked = gunzipSync(input); } catch (e) { assert.fail(`Gzip CRC32 differs from inflated JSON (${e.message})`); }
  assert.ok(checked.equals(buffer), 'Gzip CRC32 pass decoded different JSON');
  return buffer;
}

export function decodeVectors(bytes) {
  const input = Buffer.from(bytes), json = input[0] === 0x1f && input[1] === 0x8b ? gunzipMember(input) : input;
  assert.ok(!(json[0] === 0xef && json[1] === 0xbb && json[2] === 0xbf), 'JSON BOM is outside the fixture contract');
  return uniqueJSON(new TextDecoder('utf-8', { fatal: true }).decode(json));
}

export function exportVectors(output = fixturePath) {
  const fixture = generateCases(), json = Buffer.from(`${JSON.stringify(fixture)}\n`), bytes = output.endsWith('.gz') ? gzipSync(json, { level: 9 }) : json;
  if (bytes !== json) assert.ok(gunzipMember(bytes).equals(json), 'Exported gzip fails the strict reader; old fixture preserved');
  const temporary = mkdtempSync(join(dirname(output), '.run-create-'));
  try { const path = join(temporary, 'fixture'); writeFileSync(path, bytes); assert.deepEqual(sources(), fixture.sources, 'Run sources changed before export; old fixture preserved'); renameSync(path, output); }
  finally { rmSync(temporary, { recursive: true, force: true }); }
  return { ...fixture.counts, bytes: bytes.length, uncompressedBytes: json.length, sha256: sha256(bytes), sources: fixture.sources };
}

if (isMain) {
  const args = process.argv.slice(2);
  if (args.length > 1 || (args.length && !['--check', '--help'].includes(args[0]))) throw new Error('Usage: node tools/gen_run_create_vectors.mjs [--check|--help]');
  if (args[0] === '--help') console.log('Export actual R0 Run.create/pure query evidence, or --check strict source/JSON/gzip/shape/fresh generation. Actual expedition commands remain0.');
  else if (args[0] === '--check') console.log(JSON.stringify({ checked: fixturePath, ...checkFixture(decodeVectors(readFileSync(fixturePath))) }));
  else console.log(JSON.stringify({ output: fixturePath, ...exportVectors() }, null, 2));
}
