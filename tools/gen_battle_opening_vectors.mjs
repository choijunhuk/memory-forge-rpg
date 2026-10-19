// §5.3/5.15-18, §6.3 and §16.2: real JS CREATED → atomic begin oracle for the C# opening port.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { mkdtempSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync, gunzipSync } from 'node:zlib';
import { stateHash } from './gen_traces.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const require = createRequire(import.meta.url);
const MF = require('../engine/rules-core.js');
const data = JSON.parse(readFileSync(join(root, 'data/game_data.json'), 'utf8'));
const ix = MF.indexData(data);
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const schema = 'mf-battle-opening-v1';
const scope = 'Battle.create and atomic begin only; full Battle/Run replay NOT_IMPLEMENTED';
export const fixturePath = join(root, 'engine/tests/vectors/battle_opening_vectors.json.gz');

function sources() {
  return Object.fromEntries(Object.entries({
    data: 'data/game_data.json', engine: 'engine/rules-core.js', memory: 'proposals/memory_v2.cjs',
    canonical: 'tools/gen_traces.mjs', generator: 'tools/gen_battle_opening_vectors.mjs',
  }).map(([key, path]) => [key, { path, sha256: sha256(readFileSync(join(root, path))) }]));
}
const loadedSources = sources();

function setup(party, overrides = {}) {
  const maxHp = party.reduce((sum, actor) => sum + (actor.hp || MF.Stats.REFERENCE.hp), 0);
  const deck = party.flatMap(actor => [
    ...MF.Run.baseKit(ix, actor.charId),
    ...(actor.weaponId ? [{ id: ix.weapons[actor.weaponId].card, wielder: actor.charId }] : []),
  ]);
  return clone({ party, deck, relics: [], hp: maxHp, maxHp, seed: 20261014, enemies: ['e_shelf'], ...overrides });
}

export function generateCases() {
  assert.deepEqual(sources(), loadedSources, 'Opening sources changed after module load; rerun the exporter');
  const cases = [];
  const add = (id, initial) => {
    const battle = MF.Battle.create(data, clone(initial));
    const createdState = clone(battle.s);
    const request = { rid: `opening-${id}`, v: 0, kind: 'begin', args: {} };
    // Every case is expected to commit. Even familiar RuleErrors are fatal here.
    const response = clone(battle.command(clone(request)));
    cases.push({ id, setup: clone(initial), createdState, createdStateHash: stateHash(createdState),
      request, response, finalState: clone(battle.s), finalStateHash: stateHash(battle.s), publicView: clone(battle.publicView()) });
  };

  for (const character of data.characters) for (let res = 0; res <= 6; res++) {
    add(`character-${character.id}-res${res}`, setup([{ charId: character.id, res }]));
  }
  for (const weapon of data.weapons) for (let refine = 1; refine <= 5; refine++) {
    const character = data.characters.find(c => c.id === weapon.signature) || data.characters.find(c => c.weapon === weapon.type);
    assert.ok(character && character.weapon === weapon.type, `No authored wielder for ${weapon.id}`);
    add(`weapon-${weapon.id}-r${refine}`, setup([{ charId: character.id, res: 6, weaponId: weapon.id, refine }]));
  }
  const relicParty = [{ charId: 'harin', res: 4 }, { charId: 'nia', res: 4 }, { charId: 'yuri', res: 1 }];
  for (const relic of data.relics) add(`relic-${relic.id}`, setup(relicParty, { relics: [relic.id] }));
  for (const enemy of data.enemies) add(`enemy-${enemy.id}`, setup([{ charId: 'sera', res: 0 }], { enemies: [enemy.id] }));

  const baseline = [{ charId: 'sera', res: 0 }, { charId: 'nia', res: 0 }, { charId: 'harin', res: 0 }];
  add('seed-zero', setup(baseline, { seed: 0 }));
  add('seed-uint-max', setup(baseline, { seed: 0xffffffff }));
  const omittedSeed = setup(baseline);
  delete omittedSeed.seed;
  add('seed-default-omitted', omittedSeed);
  add('seed-default-null', setup(baseline, { seed: null }));
  add('zero-fallback-values', setup([{ charId: 'harin', res: 6, weaponId: 'w_basic_melee', refine: 0,
    gauge: 0, power4: 0, hp: 0, gearBps: 0, skillLv: 0, ultLv: 0 }], { difficulty: { hp: 0, dmg: 0 } }));
  add('full-starting-gauges', setup(baseline.map(p => ({ ...p, gauge: data.rules.gaugeMax }))));
  add('harin-opening-preserved', setup([{ charId: 'harin', res: 4 }, { charId: 'nia', weaponId: 'w_ledger_lock', refine: 5 }],
    { deck: [{ id: 'harin_shield' }], hp: 200, maxHp: 243, relics: ['bronze_cog'] }));
  add('start-block-per-actor', setup([{ charId: 'harin', res: 6 },
    { charId: 'nia', weaponId: 'w_ledger_lock', refine: 1 }, { charId: 'ravien', weaponId: 'w_ledger_lock', refine: 5 }], { relics: ['bronze_cog'] }));

  const derived = [
    { charId: 'harin', res: 6, weaponId: 'w_iron_tonfa', refine: 5, level: 40, weaponLevel: 40, armor: 10, accessory: 10, skillLv: 6, ultLv: 6 },
    { charId: 'nia', res: 6, weaponId: 'w_ledger_lock', refine: 5, level: 1, weaponLevel: 1, armor: 0, accessory: 0, skillLv: 1, ultLv: 1 },
    { charId: 'aila', res: 6, weaponId: 'w_silver_hymnal', refine: 5, level: 60, weaponLevel: 60, armor: 15, accessory: 15, skillLv: 10, ultLv: 10 },
  ].map(entry => {
    const stats = MF.Stats.derive(ix, entry);
    return { charId: entry.charId, res: entry.res, weaponId: entry.weaponId, refine: entry.refine,
      power4: stats.power4, hp: stats.hp, gearBps: stats.gearBps, skillLv: stats.skillLv, ultLv: stats.ultLv };
  });
  add('mixed-derived-vitality-gear', setup(derived, { relics: ['bronze_cog', 'infinite_circuit'], difficulty: MF.Stats.stageScale(ix, 40) }));
  add('aila-overflow-strongest-heal', setup([{ charId: 'aila', res: 6, weaponId: 'w_choir_bell', refine: 5 },
    { charId: 'yuri', weaponId: 'w_silver_hymnal', refine: 5 }], { relics: ['bronze_cog'] }));
  add('aila-partial-overflow', setup([{ charId: 'aila', res: 4, weaponId: 'w_silver_hymnal', refine: 5 }], { hp: 80 }));
  add('aila-before-circuit-cost', setup([{ charId: 'aila', res: 6 }], { hp: 1, relics: ['infinite_circuit'] }));
  add('brazier-before-kaya', setup([{ charId: 'kaya', res: 6 }], { seed: 0, relics: ['pocket_brazier'], enemies: ['e_moth', 'e_cog', 'e_smudge'] }));
  add('brazier-kaya-uint-max', setup([{ charId: 'kaya', res: 4 }], { seed: 0xffffffff, relics: ['pocket_brazier'], enemies: ['e_moth', 'e_cog', 'e_smudge'] }));

  // Authored draw max is 5 + twin_pendulum 1 + quick_hands 2 + strongest 2 = 10 against twin_pendulum's cap 9 (RULES_NOW_PLAY: its slot became a draw), so the strongest opening burns one card.
  add('draw-max-strongest-only', setup([{ charId: 'nia', res: 6, weaponId: 'w_clock_core', refine: 5 },
    { charId: 'ravien', res: 6, weaponId: 'w_clock_core', refine: 4 }, { charId: 'harin', res: 6 }],
  { relics: ['quick_hands', 'twin_pendulum', 'memory_anvil'] }));
  add('draw-stronger-weapon-later', setup([{ charId: 'nia', weaponId: 'w_clock_core', refine: 1 },
    { charId: 'ravien', weaponId: 'w_clock_core', refine: 5 }, { charId: 'harin' }], { relics: ['quick_hands'] }));
  add('tuning-only-one-eligible', setup([{ charId: 'nia', res: 4 }],
    { deck: [{ id: 'nia_wrench' }, ...Array.from({ length: 4 }, () => ({ id: 'smudge' }))], relics: ['memory_anvil'] }));
  add('tuning-only-curses', setup([{ charId: 'nia', res: 4 }],
    { deck: Array.from({ length: 5 }, () => ({ id: 'smudge' })), relics: ['memory_anvil'] }));
  add('empty-deck', setup([{ charId: 'nia', res: 6 }], { deck: [], relics: ['memory_anvil', 'eternal_seed'] }));

  add('seed-moss-single', setup([{ charId: 'yuri' }], { deck: [{ id: 'yuri_cocoon' }], relics: ['eternal_seed', 'symbiotic_moss'] }));
  add('seed-no-eligible', setup([{ charId: 'yuri', res: 6 }], { deck: [
    { id: 'yuri_cocoon', growth: 3 }, { id: 'yuri_cocoon_guard', from: 'yuri_cocoon' },
  ], relics: ['eternal_seed', 'symbiotic_moss'] }));
  add('seed-mixed-eligibility', setup([{ charId: 'yuri', res: 6 }, { charId: 'harin', res: 0 }], { seed: 0, deck: [
    { id: 'yuri_cocoon', growth: 3 }, { id: 'yuri_cocoon_guard', from: 'yuri_cocoon' },
    { id: 'yuri_sprout', growth: 2 }, { id: 'yuri_root' }, { id: 'harin_shield', from: 'yuri_cocoon' },
  ], relics: ['eternal_seed', 'symbiotic_moss'] }));
  add('seed-creation-order-duplicates', setup([{ charId: 'yuri', res: 1 }], { seed: 0xffffffff, deck: [
    { id: 'yuri_sprout', growth: 2 }, { id: 'yuri_cocoon' }, { id: 'yuri_cocoon' }, { id: 'yuri_root', growth: 1 },
  ], relics: ['eternal_seed', 'symbiotic_moss'] }));
  add('card-fields-and-lineage', setup([{ charId: 'harin', res: 6 }, { charId: 'nia', res: 6, weaponId: 'w_ledger_lock', refine: 5 }, { charId: 'yuri', res: 6 }], { deck: [
    { id: 'harin_shield', key: 'mem-harin-1', plus: true, mods: { c: 'c_echo', e: 'e_guard' } },
    { id: 'harin_shield', key: 'mem-harin-2' }, { id: 'com_guard', assigned: 'nia', key: 'mem-common-1' },
    { id: 'wc_ledger_lock', wielder: 'nia', key: 'mem-weapon-1' }, { id: 'smudge', key: 'mem-curse-1' },
    { id: 'yuri_cocoon_guard', from: 'yuri_cocoon', key: 'mem-evolved-1' },
  ] }));

  for (const [id, hp, relics, runFlags] of [
    ['circuit-fatal', 1, ['infinite_circuit'], {}],
    ['circuit-exact-fatal', 2, ['infinite_circuit'], {}],
    ['circuit-survives', 3, ['infinite_circuit'], {}],
    ['circuit-envelope-used', 1, ['infinite_circuit', 'memory_envelope'], { envelopeUsed: true }],
    ['circuit-envelope-unused', 1, ['infinite_circuit', 'memory_envelope'], { envelopeUsed: false }],
  ]) add(id, setup([{ charId: 'sera' }], { hp, relics, runFlags }));
  add('ap-cap-high', setup(baseline, { nextApBonus: data.rules.apMax, relics: ['chrono_crown'] }));
  add('ap-cap-low', setup(baseline, { nextApBonus: -data.rules.apMax, relics: ['cracked_hourglass'] }));
  add('ap-crown-hourglass', setup(baseline, { relics: ['chrono_crown', 'cracked_hourglass'] }));
  add('ap-carried-bonus', setup(baseline, { nextApBonus: data.rules.breakApBonus, relics: ['chrono_crown', 'cracked_hourglass'] }));
  add('echo-and-rule-interactions', setup([{ charId: 'rien', res: 6 }, { charId: 'ravien', res: 6 }, { charId: 'yuri', res: 6 }],
    { relics: ['tuning_fork', 'contract_seal', 'twin_pendulum', 'cracked_hourglass', 'eternal_seed', 'symbiotic_moss'] }));
  add('three-opening-actors', setup([{ charId: 'harin', res: 6 }, { charId: 'aila', res: 6 }, { charId: 'kaya', res: 6 }],
    { relics: ['bronze_cog', 'pocket_brazier', 'memory_anvil'], enemies: ['e_moth', 'e_cog', 'e_smudge', 'e_hound', 'e_lantern'] }));
  assert.equal(new Set(cases.map(vector => vector.id)).size, cases.length, 'Duplicate opening case id');
  return { schema, scope, dataVersion: data.meta.version, sources: clone(loadedSources), cases };
}

export function checkFixture(fixture) {
  assert.equal(fixture.schema, schema, 'Wrong opening fixture schema');
  assert.ok(Array.isArray(fixture.cases), 'Opening fixture requires cases');
  assert.equal(new Set(fixture.cases.map(vector => vector.id)).size, fixture.cases.length, 'Duplicate opening case id');
  const expected = generateCases();
  assert.deepEqual(fixture.sources, expected.sources, 'Opening source provenance is stale');
  assert.equal(fixture.cases.length, expected.cases.length, 'Opening case count differs');
  assert.deepEqual(fixture, expected, 'Opening fixture snapshots, events or vectors differ');
  return fixture.cases.length;
}

export function exportVectors(output = fixturePath) {
  const fixture = generateCases();
  const json = Buffer.from(`${JSON.stringify(fixture)}\n`);
  const bytes = output.endsWith('.gz') ? gzipSync(json, { level: 9 }) : json;
  const temporary = mkdtempSync(join(dirname(output), '.battle-opening-'));
  try {
    const path = join(temporary, 'fixture');
    writeFileSync(path, bytes);
    renameSync(path, output);
  } finally {
    rmSync(temporary, { recursive: true, force: true });
  }
  return { cases: fixture.cases.length, bytes: bytes.length, uncompressedBytes: json.length, sha256: sha256(bytes), sources: fixture.sources };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  if (args.length > 1 || (args.length && !['--check', '--help'].includes(args[0]))) throw new Error('Usage: node tools/gen_battle_opening_vectors.mjs [--check|--help]');
  if (args[0] === '--help') console.log('Generate the real Battle.create/atomic begin oracle, or --check the checked-in lossless gzip JSON fixture. Full Battle/Run command replay remains NOT_IMPLEMENTED.');
  else if (args[0] === '--check') console.log(`Battle opening vectors checked: ${checkFixture(JSON.parse(gunzipSync(readFileSync(fixturePath)).toString('utf8')))}`);
  else console.log(JSON.stringify({ output: fixturePath, ...exportVectors() }, null, 2));
}
