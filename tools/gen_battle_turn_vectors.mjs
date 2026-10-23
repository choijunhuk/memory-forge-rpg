// §5.3/5.7-9/5.13-16, §10.2, §16.2: real Create/begin/repeated end oracle.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { mkdtempSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync, gunzipSync } from 'node:zlib';
import { isDeepStrictEqual } from 'node:util';
import { stateHash } from './gen_traces.mjs';
import { generateCases as openingCases } from './gen_battle_opening_vectors.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const require = createRequire(import.meta.url);
const MF = require('../engine/rules-core.js');
const data = JSON.parse(readFileSync(join(root, 'data/game_data.json'), 'utf8'));
const ix = MF.indexData(data);
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const schema = 'mf-battle-turns-v1';
const scope = 'Real Battle.create/begin/repeated end with skipped player turns; play/ult and full Battle/Run replay NOT_IMPLEMENTED';
export const fixturePath = join(root, 'engine/tests/vectors/battle_turn_vectors.json.gz');

function sources() {
  return Object.fromEntries(Object.entries({
    data: 'data/game_data.json', engine: 'engine/rules-core.js', memory: 'proposals/memory_v2.cjs',
    canonical: 'tools/gen_traces.mjs', openingGenerator: 'tools/gen_battle_opening_vectors.mjs',
    generator: 'tools/gen_battle_turn_vectors.mjs',
  }).map(([key, path]) => [key, { path, sha256: sha256(readFileSync(join(root, path))) }]));
}
const loadedSources = sources();
const baseline = [{ charId: 'sera' }, { charId: 'nia' }, { charId: 'harin' }];

function setup(overrides = {}) {
  const party = overrides.party || baseline;
  const maxHp = party.reduce((total, a) => total + (a.hp || MF.Stats.REFERENCE.hp), 0);
  const deck = party.flatMap(a => [...MF.Run.baseKit(ix, a.charId),
    ...(a.weaponId ? [{ id: ix.weapons[a.weaponId].card, wielder: a.charId }] : [])]);
  return clone({ party, deck, hp: maxHp, maxHp, seed: 0, relics: [], enemies: ['e_seed'], ...overrides });
}

function checkpoint(battle, includeReceipts) {
  const state = clone(battle.s), publicView = clone(battle.publicView());
  const point = { state, stateHash: stateHash(state), rngHash: stateHash(state.rng),
    eventsHash: stateHash(state.ev), logHash: stateHash(state.log), publicView, publicViewHash: stateHash(publicView) };
  if (includeReceipts) point.receipts = clone(battle.receipts);
  return point;
}

function recordCase(id, initial, metadata, run) {
  const battle = MF.Battle.create(data, clone(initial));
  const includeReceipts = metadata.group === 'protocol';
  const vector = { id, ...metadata, setup: clone(initial), created: checkpoint(battle, includeReceipts), steps: [] };
  const perform = (request, expectedError = null) => {
    const before = clone(battle.s), receipts = clone(battle.receipts);
    let response = null, errorCode = null;
    try {
      response = clone(battle.command(clone(request)));
    } catch (error) {
      // Only this exact, deliberately requested rejection is acceptable. Positive failures are fatal.
      if (!expectedError || !(error instanceof MF.RuleError) || error.code !== expectedError) throw error;
      errorCode = error.code;
      assert.deepEqual(battle.s, before, `${id}: rejected command changed state`);
      assert.deepEqual(battle.receipts, receipts, `${id}: rejected command changed receipts`);
    }
    assert.equal(errorCode, expectedError, `${id}: expected rejection did not occur`);
    const outcome = errorCode ? 'rejected' : battle.s.v === before.v ? 'replayed' : 'committed';
    if (outcome === 'replayed') {
      assert.deepEqual(battle.s, before, `${id}: receipt replay changed state`);
      assert.deepEqual(battle.receipts, receipts, `${id}: receipt replay changed receipts`);
    } else if (outcome === 'committed') assert.equal(battle.s.v, before.v + 1, `${id}: command version did not advance once`);
    vector.steps.push({ request: clone(request), outcome, errorCode, response,
      responseEventsHash: response ? stateHash(response.ev) : null, ...checkpoint(battle, includeReceipts) });
    return response;
  };
  run(battle, perform);
  return vector;
}

export function generateCases() {
  assert.deepEqual(sources(), loadedSources, 'Turn sources changed after module load; rerun the exporter');
  const cases = [];
  const add = (id, initial, ends, metadata) => cases.push(recordCase(id, initial, metadata, (battle, perform) => {
    perform({ rid: `${id}-begin`, v: 0, kind: 'begin', args: {} });
    for (let i = 0; i < ends && !battle.isOver(); i++) {
      perform({ rid: `${id}-end${i + 1}`, v: battle.s.v, kind: 'end', args: {} });
    }
    if (battle.isOver()) perform({ rid: `${id}-after-terminal`, v: battle.s.v, kind: 'end', args: {} }, 'WRONG_PHASE');
  }));

  const openings = openingCases().cases;
  for (const row of openings) add(`opening-${row.id}`, row.setup, 2, { group: 'opening', openingId: row.id });
  for (const enemy of data.enemies) {
    add(`enemy-${enemy.id}-two-cycles`, setup({ enemies: [enemy.id] }), enemy.pattern.length * 2, { group: 'enemy', enemyId: enemy.id });
  }
  let encounters = 0;
  for (const [zone, tiers] of Object.entries(data.encounters)) for (const [tier, groups] of Object.entries(tiers)) {
    for (const [index, enemies] of groups.entries()) {
      const cycles = 2 * Math.max(...enemies.map(id => ix.enemies[id].pattern.length));
      add(`encounter-${zone}-${tier}-${index}`, setup({ enemies }), cycles, { group: 'encounter', zone, tier, index });
      encounters++;
    }
  }

  const edge = (id, overrides, ends = 2) => add(`edge-${id}`, setup(overrides), ends, { group: 'edge' });
  const harin = [{ charId: 'harin', res: 4 }, { charId: 'sera' }, { charId: 'nia' }];
  const kaya = [{ charId: 'kaya', res: 4 }, { charId: 'sera' }, { charId: 'nia' }];
  const markedCounter = [{ charId: 'harin', res: 4 }, { charId: 'kaya', res: 4 }, { charId: 'sera' }];
  const cards = (id, n) => Array.from({ length: n }, () => ({ id }));
  edge('summon-cap', { enemies: ['e_gardener'] }, 7);
  edge('summon-initial-cap', { enemies: ['e_gardener', 'e_seed', 'e_seed', 'e_seed'] });
  edge('summon-before-block-all', { enemies: ['e_gardener', 'b_archivist'] }, 1);
  edge('summon-after-block-all', { enemies: ['b_archivist', 'e_gardener'] }, 1);
  edge('summon-refill-dead-slot', { enemies: ['e_gardener', 'e_seed', 'e_seed', 'e_seed', 'e_seed'], relics: ['pocket_brazier'], seed: 0 }, 4);
  edge('heal-lowest-tie', { party: harin, enemies: ['e_lantern', 'e_page', 'e_page'] }, 3);
  edge('heal-ratio-not-hp', { party: harin, enemies: ['e_lantern', 'e_seed', 'e_page'] }, 3);
  edge('heal-full', { enemies: ['e_lantern'] }, 3);
  edge('heal-js-rounded-tie', { party: harin, enemies: ['e_lantern', 'e_moth', 'e_page'], relics: ['pocket_brazier'],
    difficulty: { hp: 2000000009, dmg: 100 } }, 6);
  edge('notebook-continuing', { party: kaya, enemies: ['e_seed', 'e_seed'], relics: ['pocket_brazier', 'detective_notebook'], seed: 2 });
  edge('notebook-victory', { party: kaya, relics: ['pocket_brazier', 'detective_notebook'], runFlags: { coinsBonus: 7 } });
  edge('burn-reflect', { enemies: ['e_mirrorclerk'], relics: ['pocket_brazier'] });
  edge('raw-scaling', { party: [{ charId: 'harin', res: 4, power4: 240 }], enemies: ['e_page'],
    relics: ['pocket_brazier', 'ember_ring', 'silver_needle', 'eye_of_rift'] }, 1);
  edge('party-power-rounding', { party: [{ charId: 'harin', res: 4, power4: 159 }, { charId: 'sera', power4: 160 }],
    enemies: ['e_page'], relics: ['pocket_brazier'] }, 1);
  edge('curse-scaled', { party: [{ charId: 'sera' }], deck: cards('smudge', 5), difficulty: { hp: 100, dmg: 150 } });
  edge('smudge-fatal-loop', { party: [{ charId: 'sera' }], deck: cards('smudge', 5), hp: 1, relics: ['memory_envelope'] });
  edge('smudge-fatal-no-envelope', { party: [{ charId: 'sera' }], deck: cards('smudge', 5), hp: 1 });
  edge('penalty-note-inert', { party: [{ charId: 'sera' }], deck: cards('penalty_note', 5), hp: 1 }, 3);
  edge('eye-of-rift-cycle', { relics: ['eye_of_rift'] }, 8);
  edge('insert-cycle', { enemies: ['e_smudge'] }, 8);
  edge('small-deck', { party: [{ charId: 'sera' }], deck: cards('com_guard', 1) }, 4);
  edge('first-attack-fatal', { party: [{ charId: 'sera' }], hp: 1, enemies: ['e_twinink'] });
  edge('envelope-multihit', { party: [{ charId: 'sera' }], hp: 1, enemies: ['e_twinink'], relics: ['memory_envelope'] });
  edge('envelope-used', { party: [{ charId: 'sera' }], hp: 1, enemies: ['e_twinink'], relics: ['memory_envelope'], runFlags: { envelopeUsed: true } });
  edge('same-hit-defeat-reward', { party: markedCounter, hp: 1, enemies: ['e_page'], difficulty: { hp: 1, dmg: 200 }, relics: ['detective_notebook'] });
  edge('envelope-counter-kill', { party: markedCounter, hp: 1, enemies: ['e_page'], difficulty: { hp: 1, dmg: 200 }, relics: ['detective_notebook', 'memory_envelope'] });
  edge('counter-kill-stops-hits', { party: markedCounter, enemies: ['e_page'], difficulty: { hp: 1, dmg: 200 }, relics: ['detective_notebook'] });
  edge('attack-debuff-fatal', { party: [{ charId: 'sera' }], hp: 22, enemies: ['e_scribe'] });
  edge('multiple-enemy-fatal', { party: [{ charId: 'sera' }], hp: 1, enemies: ['e_moth', 'e_page'] });
  edge('circuit-next-fatal', { party: [{ charId: 'sera' }], hp: 3, relics: ['infinite_circuit'] });
  edge('circuit-envelope-next', { party: [{ charId: 'sera' }], hp: 3, relics: ['infinite_circuit', 'memory_envelope'] }, 3);
  edge('circuit-envelope-already-used', { party: [{ charId: 'sera' }], hp: 3, relics: ['infinite_circuit', 'memory_envelope'], runFlags: { envelopeUsed: true } });
  edge('carried-ap-once', { nextApBonus: 2, relics: ['chrono_crown', 'cracked_hourglass'] }, 3);
  edge('log-cap-400', {}, 425);

  cases.push(recordCase('protocol-receipts', setup(), { group: 'protocol' }, (battle, perform) => {
    perform({ rid: 'failed-then-begin', v: 0, kind: 'end', args: {} }, 'WRONG_PHASE');
    perform({ rid: 'failed-then-begin', v: 0, kind: 'begin', args: {} });
    perform({ rid: 'failed-stale', v: 0, kind: 'end', args: {} }, 'STALE_VERSION');
    perform({ rid: 'failed-phase', v: 1, kind: 'begin', args: {} }, 'WRONG_PHASE');
    perform({ rid: 'failed-kind', v: 1, kind: 'unknown-command', args: {} }, 'UNKNOWN_COMMAND');
    const first = { rid: 'stable-end', v: 1, kind: 'end', args: { a: 1, b: 2, log: ['request'], ev: [] } };
    perform(first);
    perform({ rid: 'advance', v: 2, kind: 'end', args: {} });
    perform(first);
    perform({ ...first, args: { b: 2, a: 1, log: ['request'], ev: [] } }, 'IDEMPOTENCY_CONFLICT');
    perform({ ...first, args: { ...first.args, log: [] } }, 'IDEMPOTENCY_CONFLICT');
    perform({ rid: 'failed-stale', v: battle.s.v, kind: 'end', args: {} });
    perform({ v: battle.s.v, kind: 'end', args: {} });
  }));
  cases.push(recordCase('protocol-terminal-replay', setup({ party: [{ charId: 'sera' }], hp: 3, relics: ['infinite_circuit'] }),
    { group: 'protocol' }, (battle, perform) => {
      perform({ rid: 'terminal-begin', v: 0, kind: 'begin', args: {} });
      const end = { rid: 'terminal-end', v: 1, kind: 'end', args: {} };
      perform(end);
      perform(end);
      perform({ rid: 'new-terminal-end', v: battle.s.v, kind: 'end', args: {} }, 'WRONG_PHASE');
      perform({ ...end, args: { changed: true } }, 'IDEMPOTENCY_CONFLICT');
    }));
  for (const [index, rid] of Object.getOwnPropertyNames(Object.prototype).entries()) {
    const id = `protocol-own-rid-${index}`;
    cases.push(recordCase(id, setup(), { group: 'protocol', receiptKey: rid }, (battle, perform) => {
      perform({ rid, v: 0, kind: 'unknown-command', args: {} }, 'UNKNOWN_COMMAND');
      const begin = { rid, v: 0, kind: 'begin', args: {} };
      perform(begin);
      perform({ rid: `${id}-advance`, v: 1, kind: 'end', args: {} });
      perform(begin);
      perform({ ...begin, kind: 'end' }, 'IDEMPOTENCY_CONFLICT');
    }));
  }

  assert.deepEqual(cases.filter(c => c.group === 'opening').map(c => c.openingId), openings.map(c => c.id), 'Missing opening coverage');
  assert.deepEqual(cases.filter(c => c.group === 'enemy').map(c => c.enemyId), data.enemies.map(e => e.id), 'Missing live enemy coverage');
  assert.equal(cases.filter(c => c.group === 'encounter').length, encounters, 'Missing live encounter coverage');
  assert.equal(new Set(cases.map(c => c.id)).size, cases.length, 'Duplicate turn case id');
  assert.deepEqual(sources(), loadedSources, 'Turn sources changed during generation; rerun the exporter');
  const steps = cases.flatMap(c => c.steps);
  return { schema, scope, dataVersion: data.meta.version, sources: clone(loadedSources),
    coverage: { openings: openings.length, enemies: data.enemies.length, encounters,
      edges: cases.filter(c => c.group === 'edge').length, protocols: cases.filter(c => c.group === 'protocol').length },
    counts: { cases: cases.length, commands: steps.length, committed: steps.filter(s => s.outcome === 'committed').length,
      replayed: steps.filter(s => s.outcome === 'replayed').length, rejected: steps.filter(s => s.outcome === 'rejected').length }, cases };
}

export function checkFixture(fixture) {
  assert.equal(fixture.schema, schema, 'Wrong turn fixture schema');
  assert.ok(Array.isArray(fixture.cases), 'Turn fixture requires cases');
  assert.equal(new Set(fixture.cases.map(c => c.id)).size, fixture.cases.length, 'Duplicate turn case id');
  assert.deepEqual(fixture.sources, loadedSources, 'Turn source provenance is stale');
  const expected = generateCases();
  assert.deepEqual(fixture.coverage, expected.coverage, 'Turn coverage differs');
  assert.deepEqual(fixture.counts, expected.counts, 'Turn case/command counts differ');
  // A failed multi-megabyte fixture must not build an unbounded assertion diff.
  assert.ok(isDeepStrictEqual(fixture, expected), 'Turn fixture snapshots, responses, hashes or cases differ');
  return fixture.counts;
}

export function exportVectors(output = fixturePath) {
  const fixture = generateCases();
  const json = Buffer.from(`${JSON.stringify(fixture)}\n`);
  const bytes = output.endsWith('.gz') ? gzipSync(json, { level: 9 }) : json;
  const temporary = mkdtempSync(join(dirname(output), '.battle-turns-'));
  try {
    const path = join(temporary, 'fixture');
    writeFileSync(path, bytes);
    assert.deepEqual(sources(), fixture.sources, 'Turn sources changed before export; previous fixture preserved');
    renameSync(path, output);
  } finally {
    rmSync(temporary, { recursive: true, force: true });
  }
  return { ...fixture.counts, bytes: bytes.length, uncompressedBytes: json.length, sha256: sha256(bytes), sources: fixture.sources };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  if (args.length > 1 || (args.length && !['--check', '--help'].includes(args[0]))) throw new Error('Usage: node tools/gen_battle_turn_vectors.mjs [--check|--help]');
  if (args[0] === '--help') console.log('Generate the real Battle.create/begin/repeated end oracle, or --check its lossless gzip fixture. play/ult and full Battle/Run replay remain NOT_IMPLEMENTED.');
  else if (args[0] === '--check') console.log(JSON.stringify({ checked: fixturePath, ...checkFixture(JSON.parse(gunzipSync(readFileSync(fixturePath)).toString('utf8'))) }));
  else console.log(JSON.stringify({ output: fixturePath, ...exportVectors() }, null, 2));
}
