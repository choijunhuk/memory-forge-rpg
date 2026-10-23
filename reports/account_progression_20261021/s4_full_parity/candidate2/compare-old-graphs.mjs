import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { gunzipSync } from 'node:zlib';

// Compare complete old graphs, including logs/events. This never rewrites a fixture or pin.
const output = dirname(fileURLToPath(import.meta.url));
const root = resolve(process.argv[2] || '');
const expectedEngine = process.argv[3];
const baseline = process.argv[4] || '/Users/choi/Documents/Memory_forge/.omx/context/account-progression-s4-baseline-20261021';
assert.match(expectedEngine || '', /^[a-f0-9]{64}$/);
const sha = x => createHash('sha256').update(x).digest('hex');
const bytes = p => readFileSync(join(root, p));
assert.equal(sha(bytes('engine/rules-core.js')), expectedEngine, 'Source must match the frozen candidate');
const read = name => JSON.parse(gunzipSync(readFileSync(join(baseline, name))));
const preservation = JSON.parse(readFileSync(join(baseline, 'preservation.json')));
assert.equal(sha(bytes('data/game_data.json')), preservation.sources['data/game_data.json'].sha256);
const require = createRequire(join(root, 'package.json'));
const MF = require(join(root, 'engine/rules-core.js'));
const data = require(join(root, 'data/game_data.json'));
const clone = x => JSON.parse(JSON.stringify(x));
function full(x) {
  if (x === null || typeof x !== 'object') return JSON.stringify(x);
  if (Array.isArray(x)) return '[' + x.map(v => full(v) ?? 'null').join(',') + ']';
  return '{' + Object.keys(x).sort().filter(k => x[k] !== undefined).map(k => JSON.stringify(k) + ':' + full(x[k])).join(',') + '}';
}
const forbidden = new Set(['support', 'supportUsed', 'supportSourceId', 'supportSource', 'caller']);
function noSupport(x) {
  if (x === null || typeof x !== 'object') return;
  for (const [k, v] of Object.entries(x)) { assert.ok(!forbidden.has(k), 'Unexpected S4 member ' + k); noSupport(v); }
}
const actual = [];
function index(x, path, family) {
  if (x === null || typeof x !== 'object') return;
  if (Object.hasOwn(x, 'rng') && Object.hasOwn(x, 'phase') && Object.hasOwn(x, 'log')) {
    noSupport(x);
    actual.push({ family, path, kind: Object.hasOwn(x, 'actors') ? 'Battle' : 'Run', fullSha256: sha(full(x)),
      rngSha256: sha(full(x.rng)), logSha256: sha(full(x.log)), eventsSha256: Object.hasOwn(x, 'ev') ? sha(full(x.ev)) : null, keys: Object.keys(x) });
  }
  for (const [k, v] of Object.entries(x)) index(v, path + '.' + k, family);
}
for (const [family, exporter] of [['player-actions', 'gen_battle_player_action_vectors.mjs'], ['run-create', 'gen_run_create_vectors.mjs'],
  ['run-memory', 'gen_run_memory_vectors.mjs'], ['preview', 'gen_battle_preview_vectors.mjs']]) {
  const { generateCases } = await import(pathToFileURL(join(root, 'tools', exporter)));
  index(generateCases(), '$', family);
}
const old = read('growth-ults.json.gz');
const replay = clone(old);
let commands = 0, queries = 0;
for (const row of replay.cases) {
  const battle = MF.Battle.create(data, clone(row.setup));
  row.created = clone(battle.s);
  for (let at = 0; at <= row.steps.length; at++) {
    for (const q of row.queries.filter(q => q.at === at)) {
      const before = full(battle.s), receipts = full(battle.receipts);
      const value = clone(battle[q.method](...q.args));
      assert.deepEqual(value, q.result, row.id + ' ' + q.method);
      assert.equal(full(battle.s), before); assert.equal(full(battle.receipts), receipts);
      q.result = value; q.state = clone(battle.s); queries++;
    }
    if (at === row.steps.length) break;
    const step = row.steps[at]; const response = clone(battle.command(clone(step.request)));
    assert.deepEqual(response, step.response, row.id + ' command response');
    step.response = response; step.state = clone(battle.s); commands++;
  }
}
index(replay, '$', 'growth-ults');
const expected = read('full-graph-index.json.gz');
assert.equal(expected.length, 11657);
assert.deepEqual(actual, expected, 'Every frozen complete graph, RNG, log, event, and omission must match');
assert.equal(sha(bytes('engine/rules-core.js')), expectedEngine, 'Source changed during execution');
const families = Object.fromEntries([...new Set(actual.map(x => x.family))].map(f => [f, actual.filter(x => x.family === f).length]));
const result = { status: 'PASS', graphWitnesses: actual.length, families, authoredOrdinaryUltCases: replay.cases.length,
  authoredUltCommands: commands, authoredUltQueries: queries, fullGraphIncludesLogEvRng: true, optionalSupportEntirelyAbsent: true,
  sourceRoot: root, sourceSha256: expectedEngine, expectedIndexSha256: sha(readFileSync(join(baseline, 'full-graph-index.json.gz'))),
  actualIndexSha256: sha(JSON.stringify(actual)), fixtureOrPinWrites: false };
writeFileSync(join(output, 'old-js-full-graph-candidate2.json'), JSON.stringify(result, null, 2) + '\n');
console.log(JSON.stringify(result));
