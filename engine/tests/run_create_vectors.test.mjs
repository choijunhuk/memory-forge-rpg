import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { createHash } from 'node:crypto';
import { mkdtempSync, readFileSync, writeFileSync, readdirSync, rmSync, mkdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { constants, deflateRawSync, gzipSync } from 'node:zlib';
import { generateCases, checkFixture, decodeVectors, exportVectors, fixturePath } from '../../tools/gen_run_create_vectors.mjs';
import { stateHash } from '../../tools/gen_traces.mjs';
const require = createRequire(import.meta.url), data = require('../../data/game_data.json');
const MF = require('../rules-core.js'), clone = x => JSON.parse(JSON.stringify(x));
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
let generated;
const fixture = () => generated ||= generateCases();
const row = id => { const c = fixture().cases.find(c => c.id === id); assert.ok(c, id); return c; };
const state = id => row(id).created.state;
function changedRow(id, change) {
  const f = fixture(), changed = clone(row(id)); change(changed);
  return { ...f, cases: f.cases.map(c => c.id === id ? changed : c) };
}
// Hand-built single member around any valid raw DEFLATE stream: exporter header with a chosen platform OS byte, then the
// CRC32/ISIZE footer taken from native gzip of the same JSON (the footer depends only on the JSON, not on its encoding).
function member(json, deflated, os) {
  return Buffer.concat([Buffer.from([0x1f, 0x8b, 8, 0, 0, 0, 0, 0, 2, os]), deflated, gzipSync(json).subarray(-8)]);
}
const doc = Buffer.from(`${JSON.stringify({ rows: Array.from({ length: 3000 }, (_, id) => ({ id, name: `행-${id % 37}`, list: [id % 3, null, false, 1.25] })) })}\n`);
// High-entropy JSON: a flipped DEFLATE bit changes decoded bytes instead of selecting an equal earlier copy of repetitive text.
const noise = Buffer.from(`${JSON.stringify(Array.from({ length: 2000 }, (_, n) => sha(String(n))))}\n`);

test('all10 authored characters/resonances0..6 create real short/full legacy/v2 runs', () => {
  const f = fixture(), rows = f.cases.filter(c => c.group === 'character');
  const expected = data.characters.flatMap(c => Array.from({ length: 7 }, (_, res) => ['legacy', 'v2'].flatMap(policy => ['short', 'full'].map(length => `${c.id}:${res}:${policy}:${length}`))).flat());
  assert.equal(expected.length, 280); assert.deepEqual(rows.map(c => `${c.characterId}:${c.resonance}:${c.policy}:${c.setup.length}`), expected);
  for (const row of rows) { assert.equal(row.outcome, 'created'); assert.equal(row.created.state.phase, 'MAP'); assert.deepEqual(row.created, row.afterQueries); }
});

test('factory/query counts and exact authored denominators remain distinct from expedition commands', () => {
  const f = fixture(); assert.equal(f.schema, 'mf-run-create-v1'); assert.equal(f.status, 'COMPLETE_CURRENT_SOURCE_R0_FACTORY_QUERIES'); assert.equal(f.dataVersion, data.meta.version);
  assert.equal(f.counts.cases, 2131); assert.equal(f.counts.factories, 2131); assert.equal(f.counts.runCommands, 0);
  assert.equal(f.counts.created + f.counts.rejected, f.cases.length); assert.equal(f.counts.sourceOnlyFactories, 5); assert.equal(f.counts.typedDomainProbes, 5);
  assert.equal(f.counts.supportedFactories, 2121); assert.equal(f.counts.supportedCreated + f.counts.supportedRejected, 2121);
  assert.equal(f.counts.queries, f.cases.reduce((n, c) => n + (c.queries?.length || 0), 0));
  assert.deepEqual(f.coverage.weaponRefinements, data.weapons.flatMap(w => [1, 2, 3, 4, 5].map(r => `${w.id}:r${r}`)));
  assert.deepEqual(f.coverage.legacyCardIds, data.cards.map(c => c.id)); assert.equal(f.coverage.stableMemoryCardVariants.length, 149 * 2);
  assert.equal(f.coverage.permanentPartPolicies.length, 14 * 2); assert.deepEqual(f.coverage.relicIds, data.relics.map(r => r.id));
  assert.deepEqual(f.coverage.levels, Array.from({ length: 60 }, (_, n) => n + 1)); assert.equal(f.coverage.weaponRarityLevels.length, 3 * 60);
  assert.equal(f.coverage.skills.length, 10 * 10); assert.equal(f.coverage.stages.length, 60 * 2); assert.deepEqual(f.coverage.mapSeeds, Array.from({ length: 300 }, (_, n) => n));
  assert.equal(f.reachability.bossEncounterSelections, undefined); assert.equal(f.coverage.bossEncounterSelections, 0); assert.equal(f.reachability.safePublicView, false);
});

test('actual Create and every recorded pure query reproduce full private state without injected snapshots', () => {
  const original = JSON.stringify(data);
  for (const c of fixture().cases) {
    const setup = clone(c.setup), before = JSON.stringify(setup); assert.equal(sha(before), c.inputHash);
    if (c.outcome === 'rejected') {
      assert.throws(() => MF.Run.create(data, setup), e => c.error.code ? e.code === c.error.code : e.name === c.error.name);
      assert.ok(!Object.hasOwn(c, 'created') && !Object.hasOwn(c, 'queries')); assert.equal(JSON.stringify(setup), before); continue;
    }
    const run = MF.Run.create(data, setup), json = JSON.stringify(run.s); assert.deepEqual(JSON.parse(json), c.created.state, c.id); assert.equal(sha(json), c.created.jsonHash);
    for (const o of c.created.fieldShape.objects) { let v = run.s; for (const key of o.path) v = v[key]; assert.deepEqual(Object.keys(v), o.keys, `${c.id}: own keys`); }
    for (const path of c.created.fieldShape.undefined) { let parent = run.s; for (const key of path.slice(0, -1)) parent = parent[key]; assert.ok(Object.hasOwn(parent, path.at(-1))); assert.equal(parent[path.at(-1)], undefined); }
    for (const q of c.queries) {
      const actual = run[q.method](...q.args); if (q.result.kind === 'undefined') assert.equal(actual, undefined); else { assert.equal(q.result.kind, 'json'); assert.deepEqual(actual, q.result.value); }
      assert.equal(stateHash(q.result), q.resultHash); assert.equal(JSON.stringify(run.s), json, `${c.id}: query changed state`); assert.deepEqual(q.before, q.after);
    }
    assert.deepEqual(JSON.parse(JSON.stringify(run.s)), c.afterQueries.state); assert.equal(JSON.stringify(setup), before, `${c.id}: input changed`);
  }
  assert.equal(JSON.stringify(data), original, 'Original authored definitions changed');
});

test('Run stats derive from account inputs instead of trusting supplied combat values', () => {
  const ref = state('stats-sera-supplied-derived-ignored').party[0]; assert.deepEqual([ref.power4, ref.hp, ref.gearBps, ref.skillLv, ref.ultLv], [160, 81, 0, 4, 7]);
  assert.equal(state('stats-sera-default-level20').party[0].power4, 4 * (20 + Math.floor(14 * 20 / 60)));
  const ssr = state('weapon-w_sera-r2').party[0]; assert.equal(ssr.power4, 4 * (20 + Math.floor(14 * 20 / 60) + 2 + Math.floor(6 * 20 / 60))); assert.equal(ssr.hp, 80);
  const basic = state('stats-sera-default-level20'); assert.equal(basic.deck.at(-1).id, 'wc_basic_ranged'); assert.ok(!Object.hasOwn(basic.party[0], 'weaponId'));
  assert.equal(state('gear-both-0').hp, 80 + 2 + Math.floor(80 * 300 / 10000)); assert.equal(state('gear-both-0').party[0].gearBps, 250);
  assert.equal(state('gear-both-15').hp, 80 + 2 + 15 + Math.floor(80 * 1200 / 10000)); assert.equal(state('gear-both-15').party[0].gearBps, 1000);
  const max = state('stats-harin-maximum').party[0]; assert.deepEqual([max.power4, max.hp, max.skillLv, max.ultLv], [136, 151, 10, 10]);
  const neg = state('stats-sera-negative').party[0]; assert.deepEqual([neg.power4, neg.hp, neg.gearBps, neg.skillLv, neg.ultLv, neg.res, neg.refine], [80, 64, 250, 1, 1, -1, 0]);
});

test('stage rounding preserves zero fallback, negative half and empty difficulty shape', () => {
  assert.deepEqual(state('stage-1-normal').difficulty, { hp: 53, dmg: 75 }); assert.deepEqual(state('stage-1-hard').difficulty, { hp: 64, dmg: 94 });
  assert.deepEqual(state('stage-60-normal').difficulty, { hp: 100, dmg: 148 }); assert.deepEqual(state('edge-difficulty-negative-stage1').difficulty, { hp: -26, dmg: -1 });
  assert.deepEqual(state('edge-difficulty-zeros-stage0').difficulty, { hp: 0, dmg: 0 }); assert.deepEqual(state('edge-difficulty-zeros-stage1').difficulty, { hp: 53, dmg: 75 });
  assert.deepEqual(state('edge-difficulty-empty-stage0').difficulty, {}); assert.equal(state('edge-difficulty-empty-stage0').stageLevel, null);
});

test('capacity uses source ascensions and signed resonance slice semantics without retuning', () => {
  for (const c of fixture().cases.filter(c => c.group === 'capacity')) {
    const capacity = 6 + Number(c.asc >= 2) + Number(c.asc >= 4) + Number(c.resonance >= 4); assert.equal(c.capacity, capacity);
    assert.equal(c.outcome, c.memoryCost > capacity ? 'rejected' : 'created'); if (c.outcome === 'rejected') assert.equal(c.error.code, 'OVER_CAPACITY');
  }
  assert.equal(row('edge-capacity-signed-res--1').outcome, 'created'); assert.equal(row('edge-capacity-signed-res--2').outcome, 'created');
  assert.equal(row('edge-capacity-signed-res--3').error.code, 'OVER_CAPACITY'); assert.equal(row('edge-capacity-signed-res--2147483648').error.code, 'OVER_CAPACITY');
});

test('stable keys skip both saved and current-deck collisions while legacy copies get new keys', () => {
  const s = state('edge-run-key-collisions'); assert.equal(s.seqCard, 6); assert.deepEqual(s.deck.filter(c => c.weapon).map(c => c.key), ['run:collision:3', 'run:collision:5', 'run:collision:6']);
  assert.equal(new Set(s.deck.map(c => c.key)).size, s.deck.length); assert.equal(state('legacy-card-sera_round').deck[0].key, 'k1');
  const v2 = state('memory-card-sera_round-base').deck.filter(c => c.id === 'sera_round'); assert.deepEqual(v2.map(c => c.key), ['base:sera:0', 'extra:sera_round']);
  assert.equal(state('edge-nonparty-memory-preserved').deck.at(-1).key, 'run:outside:1'); assert.equal(state('edge-nonparty-memory-preserved').startMemory.nia[0].key, 'run:outside:1');
});

test('base-kit play preserves original before/baseline and uses new identities for removed/evolved cards', () => {
  const s = state('edge-use-memory-false-evolved'); assert.equal(s.startMemory.yuri[0].id, 'yuri_cocoon_guard'); assert.equal(s.memoryBefore.yuri[0].id, 'yuri_cocoon_guard');
  assert.equal(s.memoryBaseline.yuri[0].id, 'yuri_cocoon'); assert.equal(s.playBefore.yuri[0].id, 'yuri_cocoon'); assert.equal(s.playBefore.yuri[0].key, 'run:base-collision:1');
  assert.equal(state('edge-use-memory-false-rekeyed').playBefore.yuri[0].key, 'run:base-collision:2'); assert.equal(state('edge-use-memory-false-rekeyed').seqCard, 3);
  assert.deepEqual(state('edge-use-memory-false-empty').memoryBefore.yuri, []); assert.equal(state('edge-use-memory-false-empty').playBefore.yuri.length, 5);
  for (const c of fixture().cases.filter(c => c.evolvedId)) { assert.equal(c.outcome, 'created'); assert.ok(c.created.state.memoryBefore.yuri.some(sp => sp.id === c.evolvedId && sp.from)); }
});

test('field absence, null, explicit false/zero, own undefined and opaque fractions remain distinguishable', () => {
  const legacy = state('edge-defaults-legacy'), v2 = state('edge-defaults-v2'); assert.ok(!Object.hasOwn(legacy, 'memoryPolicy')); assert.equal(v2.memoryPolicy, 'v2'); assert.equal(legacy.pos, null);
  const empty = row('edge-empty-zones-v2'); assert.ok(!Object.hasOwn(empty.created.state, 'zone')); assert.ok(!Object.hasOwn(empty.created.state.map, 'zone'));
  assert.deepEqual(empty.created.fieldShape.undefined, [['zone'], ['map', 'zone']]); assert.equal(empty.created.state.map.boss.id, 'zundefinedboss');
  const sparse = state('edge-sparse-card-metadata-v2').memoryBefore.sera[0]; assert.equal(sparse.plus, false); assert.equal(sparse.growth, 0); assert.equal(sparse.gained, false); assert.deepEqual(sparse.mods, { c: null, e: null });
  const opaque = row('edge-opaque-fractional-metadata'); assert.equal(opaque.parity, 'supported-json-metadata'); assert.equal(opaque.created.stateHash, null); assert.equal(opaque.created.canonicalError, 'NON_CANONICAL_NUMBER');
  assert.equal(opaque.created.state.memoryBefore.sera[0].opaque.fraction, 1.25); assert.ok(!Object.hasOwn(opaque.created.state.playBefore.sera[0], 'opaque'));
  for (const c of fixture().cases.filter(c => c.outcome === 'created')) { const missing = c.queries.find(q => q.method === 'nodeById' && q.args[0] === 'missing'); assert.deepEqual(missing.result, { kind: 'json', value: null }); }
});

test('legacy/v2 validation, native diagnostics and explicit C# numeric-domain probes keep separate denominators', () => {
  assert.equal(state('edge-empty-party-legacy').hp, 0); assert.equal(state('edge-duplicate-party-legacy').hp, 162); assert.equal(state('edge-all10-party-v2').hp, 810);
  assert.equal(row('edge-empty-party-v2').error.code, 'INVALID_SNAPSHOT'); assert.equal(row('edge-duplicate-party-v2').error.code, 'INVALID_SNAPSHOT');
  assert.equal(row('native-missing-party').error.name, 'SyntaxError'); assert.equal(row('native-unknown-card').error.name, 'TypeError'); assert.equal(row('native-unknown-card').parity, 'js-source-only');
  for (const c of fixture().cases.filter(c => c.group === 'domain')) { assert.equal(c.outcome, 'created'); assert.equal(c.parity, 'typed-domain-rejection'); assert.equal(c.typedExpectedCode, 'RUN_NUMBER_OUT_OF_RANGE'); }
  assert.equal(row('domain-unsafe-seed-positive').created.stateHash, null); assert.ok(state('domain-difficulty-derived-overflow').difficulty.dmg > 2147483647);
  assert.equal(state('edge-memory-version-max-safe').memoryVersion, 9007199254740991);
});

test('all300 maps preserve authored node/floor/edge order, reachability and minimum battle constraints', () => {
  const seenTypes = new Set(), seenWidths = new Set();
  for (const c of fixture().cases.filter(c => c.group === 'map')) {
    const m = c.created.state.map; assert.equal(m.floors.length, 7); assert.equal(m.floors[0].length, 3); assert.ok(!Object.hasOwn(m.boss, 'next'));
    const all = new Map([...m.floors.flat(), m.boss].map(n => [n.id, n])); assert.equal(all.size, m.floors.flat().length + 1);
    assert.ok(m.floors.flat().filter(n => n.type === 'elite').length <= 2); assert.ok(m.floors[3].some(n => ['rest', 'forge'].includes(n.type)));
    for (let f = 0; f < 7; f++) { const r = m.floors[f]; seenWidths.add(r.length); assert.ok(r.length === 2 || r.length === 3);
      r.forEach((n, i) => { seenTypes.add(n.type); assert.deepEqual([n.f, n.i, n.id], [f, i, `z1f${f}n${i}`]); assert.equal(new Set(n.next).size, n.next.length);
        for (const id of n.next) { const next = all.get(id); assert.ok(next); assert.equal(next.f, f + 1); assert.ok(n.type !== 'elite' || next.type !== 'elite'); }
        if ([0, 2, 4].includes(f)) assert.ok(['battle', 'elite'].includes(n.type)); if (f === 6) assert.deepEqual(n.next, ['z1boss']);
      });
      if (f) for (const n of r) assert.ok(m.floors[f - 1].some(p => p.next.includes(n.id)));
    }
    assert.deepEqual(c.queries[0].result.value, m.floors[0].map(n => n.id));
  }
  assert.deepEqual([...seenWidths].sort(), [2, 3]); assert.deepEqual([...seenTypes].sort(), ['battle', 'elite', 'event', 'forge', 'rest', 'shop']);
});

// Independent BigInt formulation of splitmix/xoshiro state advance; no product RNG calls.
function rngPrefix(seed, count) {
  const mask = (1n << 32n) - 1n, rot = (x, n) => ((x << n) | (x >> (32n - n))) & mask;
  let a = (BigInt(seed) ^ 0x5eedn) & mask;
  const s = Array.from({ length: 4 }, () => { a = (a + 0x9e3779b9n) & mask; let t = ((a ^ (a >> 16n)) * 0x21f0aaadn) & mask; t = ((t ^ (t >> 15n)) * 0x735a2d97n) & mask; return (t ^ (t >> 15n)) & mask; });
  const output = [];
  for (let i = 0; i < count; i++) { output.push(Number(rot((s[1] * 5n) & mask, 7n) * 9n & mask)); const t = (s[1] << 9n) & mask; s[2] ^= s[0]; s[3] ^= s[1]; s[1] ^= s[2]; s[0] ^= s[3]; s[2] ^= t; s[3] = rot(s[3], 11n); }
  return { state: s.map(Number), output };
}

test('literal map branches and private RNG consumption include lazy choices, elite pruning and parent repair', () => {
  for (const [id, seed, draws] of [['map-seed-0', 0, 35], ['map-seed-1', 1, 35], ['map-seed-4', 4, 39], ['map-seed-42', 42, 36], ['edge-seed-4294967295', 4294967295, 36]]) assert.deepEqual(state(id).rng, rngPrefix(seed, draws).state);
  assert.deepEqual(state('map-seed-0').map.floors[1][2].next, ['z1f2n1', 'z1f2n0']); assert.deepEqual(state('map-seed-1').map.floors[0][0].next, ['z1f1n0', 'z1f1n1']);
  const cap = rngPrefix(3, 15).output; assert.ok(cap[7] % 100 >= 30 && cap[7] % 100 < 50); assert.ok(cap[11] % 100 >= 60); assert.ok(cap[13] % 100 >= 30 && cap[13] % 100 < 45);
  assert.equal(state('map-seed-3').map.floors[5][0].type, 'battle');
  const repair = rngPrefix(4, 39).output; assert.deepEqual([repair[36] % 2, repair[37] % 2, repair[38] % 3], [1, 1, 2]);
  assert.deepEqual(state('map-seed-4').map.floors[5][1].next, ['z1f6n2', 'z1f6n1']); assert.equal(state('map-seed-4').map.floors[4][1].type, 'battle');
  assert.deepEqual(state('edge-seed--1').rng, state('edge-seed-4294967295').rng); assert.notEqual(state('edge-seed--1').seed, state('edge-seed-4294967295').seed);
});

test('source/manifest provenance remains explicit and no historical input manifest is regenerated', () => {
  const f = fixture(); for (const source of Object.values(f.sources)) assert.equal(sha(readFileSync(new URL(`../../${source.path}`, import.meta.url))), source.sha256);
  assert.equal(f.inputManifestAudit.entries, 423); assert.equal(f.inputManifestAudit.regenerated, false); assert.equal(f.inputManifestAudit.manifestMatch, false); assert.deepEqual(f.inputManifestAudit.structuralErrors, []);
  assert.ok(f.inputManifestAudit.preExistingChangedPaths.includes('engine/rules-core.js')); assert.ok(!f.inputManifestAudit.preExistingChangedPaths.some(p => p.includes('run_create_vectors')));
  for (const path of ['docs/archive/v0.6_materials/pass_rewards.csv', 'reports/audit_v081/preservation_by_chapter.csv', 'reports/daily_income_42d.csv', 'reports/upstream_v070/daily_income_42d.csv'])
    assert.ok(!f.inputManifestAudit.preExistingChangedPaths.includes(path), `${path}: -text checkout must keep the original manifest bytes`);
});

test('saved gzip strictly decodes and checkFixture compares it exhaustively with fresh creation/query evidence', () => {
  assert.deepEqual(checkFixture(decodeVectors(readFileSync(fixturePath))), fixture().counts);
});

test('only exact numeric Node provenance may differ from this runtime; native errors and every other byte still compare exactly', () => {
  const f = fixture(), current = process.versions.node, foreign = current === '22.14.0' ? '25.9.0' : '22.14.0';
  assert.deepEqual(f.nativeDiagnosticsRuntime, { node: current });
  const withRuntime = runtime => ({ ...f, nativeDiagnosticsRuntime: runtime }), missing = { ...f }; delete missing.nativeDiagnosticsRuntime;
  assert.throws(() => checkFixture(missing), /Native diagnostics runtime/);
  for (const runtime of [undefined, null, [], [foreign], foreign, {}, { node: '' }, { node: 22 }, { node: [foreign] }, { node: `v${foreign}` }, { node: '22.14' }, { node: '22.14.0.1' },
    { node: '22.14.0-pre' }, { node: '022.14.0' }, { node: `${foreign}\n` }, { node: foreign, v8: process.versions.v8 }, { node: foreign, zlib: process.versions.zlib }, { v8: process.versions.v8 }])
    assert.throws(() => checkFixture(withRuntime(runtime)), /Native diagnostics runtime/, JSON.stringify(runtime));
  assert.deepEqual(checkFixture(withRuntime({ node: foreign })), f.counts);
  assert.equal(f.cases.filter(c => c.group === 'native-error').length, 5);
  assert.throws(() => checkFixture({ ...changedRow('native-missing-party', c => { c.error.message += ' '; }), nativeDiagnosticsRuntime: { node: foreign } }), /differ from fresh creation/);
});

test('strict gzip reader accepts other valid DEFLATE encodings and platform OS codes of the same JSON', () => {
  const value = JSON.parse(doc), half = doc.length >> 1, canonical = gzipSync(doc, { level: 9 });
  const encodings = [canonical, ...[constants.Z_HUFFMAN_ONLY, constants.Z_FIXED].map(strategy => gzipSync(doc, { level: 9, strategy })),
    member(doc, deflateRawSync(doc, { level: 0 }), 0x13),
    member(doc, Buffer.concat([deflateRawSync(doc.subarray(0, half), { level: 9, finishFlush: constants.Z_SYNC_FLUSH }), deflateRawSync(doc.subarray(half), { level: 1 })]), 0x0a)];
  assert.equal(new Set(encodings.map(b => b.subarray(10).toString('hex'))).size, encodings.length, 'variants must be genuinely different DEFLATE streams');
  const os = [3, 10, 19].map(code => { const b = Buffer.from(canonical); b[9] = code; return b; });
  for (const bytes of [...encodings, gzipSync(doc, { level: 9, strategy: constants.Z_FILTERED }), ...os]) assert.deepEqual(decodeVectors(bytes), value);
});

test('strict gzip reader rejects header, payload, CRC32, ISIZE, truncation, trailing bytes and concatenated members', () => {
  const good = gzipSync(noise, { level: 9 }), end = good.length - 8, flip = (at, bit = 1) => { const b = Buffer.from(good); b[at] ^= bit; return b; };
  for (let at = 2; at < 9; at++) assert.throws(() => decodeVectors(flip(at)), /Gzip header/, `header byte ${at}`);
  for (const at of [0, 1]) assert.throws(() => decodeVectors(flip(at)), `magic byte ${at}`);
  const named = Buffer.concat([good.subarray(0, 3), Buffer.from([8]), good.subarray(4, 10), Buffer.from('fixture\0'), good.subarray(10)]), dated = Buffer.from(good); dated.writeUInt32LE(1760486400, 4);
  for (const valid of [named, dated]) assert.throws(() => decodeVectors(valid), /Gzip header/, 'valid gzip with nondeterministic name/MTIME');
  // Final-byte padding bits are not DEFLATE content, so payload flips stay strictly before the last compressed byte.
  for (const at of [10, 11, (10 + end) >> 1, end - 2]) for (const bit of [1, 2, 0x40]) assert.throws(() => decodeVectors(flip(at, bit)), `payload byte ${at} bit ${bit}`);
  for (let n = 0; n < 4; n++) { assert.throws(() => decodeVectors(flip(end + n)), /CRC32/); assert.throws(() => decodeVectors(flip(end + 4 + n)), /ISIZE/); }
  const tiny = gzipSync(Buffer.from('{}\n'), { level: 9 }), empty = gzipSync(Buffer.alloc(0), { level: 9 });
  for (let n = 0; n < tiny.length; n++) assert.throws(() => decodeVectors(tiny.subarray(0, n)), `tiny prefix ${n}`);
  for (const n of [10, 11, (10 + end) >> 1, end - 1]) assert.throws(() => decodeVectors(good.subarray(0, n)), `DEFLATE prefix ${n}`);
  for (let n = end; n < good.length; n++) assert.throws(() => decodeVectors(good.subarray(0, n)), /exactly CRC32\+ISIZE/);
  for (const extra of [Buffer.from([0]), Buffer.alloc(8), Buffer.from('x'), empty, tiny, good]) assert.throws(() => decodeVectors(Buffer.concat([good, extra])), /exactly CRC32\+ISIZE/);
  for (const first of [empty, tiny]) assert.throws(() => decodeVectors(Buffer.concat([first, good])), /exactly CRC32\+ISIZE/);
  for (const json of [Buffer.from([0xef, 0xbb, 0xbf, 0x7b, 0x7d]), Buffer.from([0x22, 0xc3, 0x28, 0x22]), Buffer.from('{"a":1,"\\u0061":2}'), Buffer.from('{} {}'), Buffer.from('{')])
    assert.throws(() => decodeVectors(gzipSync(json, { level: 9 })));
});

test('strict reader rejects malformed JSON, escape-equivalent duplicate keys, UTF8/BOM and gzip tampering', () => {
  for (const text of ['{', '{"a":1,"a":2}', '{"a":1,"\\u0061":2}', '{} {}']) assert.throws(() => decodeVectors(Buffer.from(text)));
  assert.throws(() => decodeVectors(Buffer.from([0xc3, 0x28]))); assert.throws(() => decodeVectors(Buffer.concat([Buffer.from([0xef, 0xbb, 0xbf]), Buffer.from('{}')])));
  const bytes = gzipSync(Buffer.from('{}\n'), { level: 9 }), crc = Buffer.from(bytes), header = Buffer.from(bytes); crc[crc.length - 5] ^= 1; header[4] ^= 1;
  assert.throws(() => decodeVectors(crc)); assert.throws(() => decodeVectors(header)); assert.deepEqual(decodeVectors(bytes), {});
});

test('case/source/count/shape corruption is rejected and freshly forged complete state/query hashes cannot pass', () => {
  const f = fixture(), id = f.cases[0].id;
  assert.throws(() => checkFixture({ ...f, sources: { ...f.sources, engine: { ...f.sources.engine, sha256: '0'.repeat(64) } } }));
  assert.throws(() => checkFixture({ ...f, cases: f.cases.slice(1) })); assert.throws(() => checkFixture({ ...f, cases: [f.cases[0], ...f.cases] }));
  assert.throws(() => checkFixture(changedRow(id, c => { c.created.fieldShape.objects[0].keys.pop(); c.created.shapeHash = stateHash(c.created.fieldShape); })));
  assert.throws(() => checkFixture({ ...f, counts: { ...f.counts, queries: f.counts.queries + 1 } }));
  assert.throws(() => checkFixture(changedRow(id, c => {
    c.created.state.coins++; c.created.stateHash = stateHash(c.created.state); c.created.jsonHash = sha(JSON.stringify(c.created.state)); c.afterQueries = clone(c.created);
    for (const q of c.queries) { q.before.stateHash = c.created.stateHash; q.before.jsonHash = c.created.jsonHash; q.after = clone(q.before); }
  })));
  assert.throws(() => checkFixture(changedRow(id, c => { c.queries[0].result.value.push('forged'); c.queries[0].resultHash = stateHash(c.queries[0].result); })));
});

test('atomic deterministic export preserves old targets and removes temporary files on real filesystem failures', () => {
  const dir = mkdtempSync(join(tmpdir(), 'mf-run-create-'));
  try {
    const a = join(dir, 'a.json.gz'), b = join(dir, 'b.json.gz'), result = exportVectors(a); exportVectors(b); assert.ok(readFileSync(a).equals(readFileSync(b)), 'Same-runtime export bytes differ'); assert.equal(result.sha256, sha(readFileSync(a)));
    const parent = join(dir, 'file-parent'); writeFileSync(parent, 'KEEP_PARENT'); assert.throws(() => exportVectors(join(parent, 'out.json.gz')), { code: 'ENOTDIR' }); assert.equal(readFileSync(parent, 'utf8'), 'KEEP_PARENT');
    const output = join(dir, 'directory-target'); mkdirSync(output); writeFileSync(join(output, 'sentinel'), 'KEEP_DIRECTORY');
    assert.throws(() => exportVectors(output), e => ['EISDIR', 'ENOTEMPTY', 'EPERM'].includes(e.code)); assert.equal(readFileSync(join(output, 'sentinel'), 'utf8'), 'KEEP_DIRECTORY'); assert.ok(!readdirSync(dir).some(p => p.startsWith('.run-create-')));
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
