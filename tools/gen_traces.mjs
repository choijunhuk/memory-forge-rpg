// §16.2 / §20.9 P1-1: JS Run oracle for the future C# Battle/Run replay checks.
// node tools/gen_traces.mjs --help describes the portable trace contract.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { createWriteStream, readFileSync } from 'node:fs';
import { mkdir, mkdtemp, rename, rm } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import { fileURLToPath } from 'node:url';
import { createGzip } from 'node:zlib';

const root = fileURLToPath(new URL('..', import.meta.url));
const require = createRequire(import.meta.url);
const MF = require('../engine/rules-core.js');
const data = JSON.parse(readFileSync(join(root, 'data/game_data.json'), 'utf8'));
const copy = value => JSON.parse(JSON.stringify(value));
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const sources = Object.fromEntries(Object.entries({
  data: 'data/game_data.json', engine: 'engine/rules-core.js', memory: 'proposals/memory_v2.cjs',
  generator: 'tools/gen_traces.mjs', fuzzPolicy: 'engine/tests/fuzz_runs.mjs',
}).map(([key, path]) => [key, { path, sha256: sha256(readFileSync(join(root, path))) }]));
const schema = 'mf-run-traces-v1';
const policy = {
  id: 'fuzz-random-trace-v1', source: 'engine/tests/fuzz_runs.mjs',
  rng: 'JS Number LCG: (state * 1103515245 + 12345) >>> 0; state / 4294967296',
  seedDerivation: '(runSeed ^ 0xc0decafe) >>> 0; isolated per scenario, never the engine RNG',
  differences: 'Same random action probabilities; empty candidate lists are skipped; terminal saves select only valid capacity/defeat choices; v2 uses previewMemory. Guards and unknown errors are fatal.',
  limits: { runSteps: 2000, battleTurns: 400, attemptsPerTurn: 30 },
};
const battleRejections = ['AP_LOW', 'SLOT_FULL', 'BAD_TARGET', 'FIXED_MARK_REQUIRED', 'NOT_RESERVABLE', 'RESERVE_ONLY',
  'DEBT_CAP', 'LOAN_OVERFLOW', 'NOT_DEFERRABLE', 'RESTRAINT_UNAVAILABLE', 'NO_EMPTY_SLOT', 'RESERVATION_REQUIRED',
  'PULL_DESTINATION_FULL', 'OVERLOAD_WOULD_KILL'];
const legalRejections = {
  'battle:play': battleRejections, 'battle:ult': ['ULT_NOT_READY', 'BAD_TARGET'],
  'run:rest': ['CANNOT_HEAL', 'BAD_UPGRADE'], 'run:buy': ['COINS_LOW'],
  'run:eventChoose': ['COINS_LOW', 'WOULD_KILL'],
};
const runMethods = new Set(['enter', 'evolve', 'takeCard', 'skipCard', 'takeRelic', 'takePart', 'applyFoundPart',
  'proceed', 'rest', 'forgeOffer', 'forgeApply', 'forgeRemove', 'buy', 'eventChoose', 'archive', 'commitMemory']);

// Serialize directly: rebuilding an object would make JSON.stringify reorder integer-looking keys.
export function canonicalJSON(value) {
  if (value === null) return 'null';
  if (value === undefined) return undefined;
  if (typeof value === 'number') {
    if (!Number.isSafeInteger(value)) throw new Error('Trace state requires safe integer numbers');
    return JSON.stringify(value);
  }
  if (typeof value === 'string' || typeof value === 'boolean') return JSON.stringify(value);
  if (Array.isArray(value)) return `[${Array.from(value, item => canonicalJSON(item) ?? 'null').join(',')}]`;
  if (typeof value !== 'object') throw new Error(`Unsupported trace JSON value: ${typeof value}`);
  return `{${Object.keys(value).sort().filter(key => key !== 'log' && key !== 'ev' && value[key] !== undefined)
    .map(key => `${JSON.stringify(key)}:${canonicalJSON(value[key])}`).join(',')}}`;
}
export const stateHash = value => sha256(canonicalJSON(value));
const snapshot = context => JSON.parse(canonicalJSON({ run: context.run.s, memory: context.memory }));
const contextHash = context => stateHash({ run: context.run.s, memory: context.memory });

function checkPolicy(memoryPolicy) {
  if (!['legacy', 'v2'].includes(memoryPolicy)) throw new Error('Memory policy must be legacy or v2');
}
function checkSeed(seed) {
  if (!Number.isInteger(seed) || seed < 0 || seed > 0xffffffff) throw new Error('Seed must be a uint32');
}
export function* scenarioPlan({ shortCount = 700, fullCount = 300, seed = 1000, memoryPolicy = 'legacy' } = {}) {
  checkPolicy(memoryPolicy);
  checkSeed(seed);
  if (![shortCount, fullCount].every(n => Number.isSafeInteger(n) && n >= 0) || shortCount + fullCount < 1)
    throw new Error('Scenario counts must be nonnegative integers with a positive total count');
  if (seed + shortCount + fullCount - 1 > 0xffffffff) throw new Error('Scenario seed range exceeds uint32');
  for (let i = 0; i < shortCount + fullCount; i++) yield { seed: seed + i, length: i < shortCount ? 'short' : 'full', memoryPolicy };
}

function dispatch(context, command) {
  const run = context.run;
  if (command.scope === 'battle') {
    assert.ok(run.battle, 'Trace battle command requires an active battle');
    assert.equal(command.v, run.battle.s.v, 'Trace command version mismatch');
    assert.equal(command.rid, `r${run.battle.s.v}-${command.kind}`, 'Trace command request id mismatch');
    assert.ok(['play', 'ult', 'end'].includes(command.kind), 'Unknown trace battle command');
    return run.battleCmd(command.kind, copy(command.args));
  }
  assert.equal(command.scope, 'run', 'Unknown trace command scope');
  assert.ok(runMethods.has(command.kind) && Array.isArray(command.args), 'Unknown trace Run command');
  const result = run[command.kind](...copy(command.args));
  if (command.kind === 'commitMemory' && run.s.memoryPolicy === 'v2') context.memory = result.state;
  return result;
}
function execute(context, command) {
  let errorCode = null;
  try { dispatch(context, command); } catch (error) {
    const allowed = legalRejections[`${command.scope}:${command.kind}`] || [];
    if (!(error instanceof MF.RuleError) || !allowed.includes(error.code)) throw error;
    errorCode = error.code;
  }
  return { command, outcome: errorCode ? 'rejected' : 'committed', errorCode, stateHash: contextHash(context) };
}

export function generateScenario({ seed = 1000, length = 'short', memoryPolicy = 'legacy', policySeed = (seed ^ 0xc0decafe) >>> 0 } = {}) {
  checkSeed(seed); checkSeed(policySeed); checkPolicy(memoryPolicy);
  if (!['short', 'full'].includes(length)) throw new Error('Run length must be short or full');
  let policyState = policySeed;
  const rnd = () => { policyState = (policyState * 1103515245 + 12345) >>> 0; return policyState / 4294967296; };
  const pick = list => { assert.ok(list.length, 'Trace policy cannot pick an empty list'); return list[Math.floor(rnd() * list.length)]; };
  const pool = data.characters.map(c => c.id);
  const party = [];
  while (party.length < 3) party.push({ charId: pool.splice(Math.floor(rnd() * pool.length), 1)[0], res: Math.floor(rnd() * 7), weaponId: null, refine: 1 });
  for (const member of party) if (rnd() < 0.7) {
    const type = data.characters.find(c => c.id === member.charId).weapon;
    member.weaponId = pick(data.weapons.filter(w => w.type === type)).id;
    member.refine = 1 + Math.floor(rnd() * 5);
  }
  const memory = memoryPolicy === 'v2' ? MF.MemoryV2.migrate(data, {}, {}) : null;
  const setup = { party, seed, length, memoryPolicy, memory: memory?.memory || {}, relics: [],
    difficulty: { hp: 100, dmg: 100 }, zones: length === 'full' ? [1, 2] : [1] };
  if (memory) Object.assign(setup, { runId: `trace-${seed}-${length}-v2`, memoryVersion: memory.version, useMemory: true });
  const context = { run: MF.Run.create(data, copy(setup)), memory };
  const run = context.run;
  const trace = { schema, seed, length, party: copy(party), setup: copy(setup), memoryPolicy,
    dataVersion: data.meta.version, sources: copy(sources), policy: { ...copy(policy), seed: policySeed },
    initialState: snapshot(context), initialStateHash: contextHash(context), commands: [] };
  let previousHash = trace.initialStateHash;
  const record = command => {
    const step = execute(context, copy(command));
    if (step.outcome === 'rejected') assert.equal(step.stateHash, previousHash, `Rejected ${command.kind} mutated state`);
    trace.commands.push(step); previousHash = step.stateHash;
    return step.outcome === 'committed';
  };
  const command = (kind, ...args) => record({ scope: 'run', kind, args });
  const battleCommand = (kind, args = {}) => record({ scope: 'battle', rid: `r${run.battle.s.v}-${kind}`, v: run.battle.s.v, kind, args });

  function playBattle() {
    for (let turns = 0; run.s.phase === 'BATTLE'; turns++) {
      assert.ok(turns < policy.limits.battleTurns, 'Trace battle turn guard exceeded');
      const battle = run.battle;
      assert.equal(battle.s.phase, 'PLAYER', 'Trace battle stuck outside player phase');
      for (let tries = 0; tries < policy.limits.attemptsPerTurn && run.s.phase === 'BATTLE' && battle.s.phase === 'PLAYER'; tries++) {
        for (const actor of battle.s.actors) if (actor.gauge >= 100 && rnd() < 0.7) {
          battleCommand('ult', { actor: actor.id, target: battle.alive().length ? pick(battle.alive()).uid : null });
          if (run.s.phase !== 'BATTLE') return;
        }
        if (!battle.s.zones.hand.length) break;
        const uid = pick(battle.s.zones.hand);
        const modes = battle.allowedModes(uid);
        if (!modes.length) continue;
        const mode = pick(modes);
        const alive = battle.alive();
        let target = alive.length ? pick(alive).uid : null;
        if (battle.needsTarget(uid) === 'fixed') { const marked = alive.filter(e => e.st.mark > 0); if (marked.length) target = pick(marked).uid; }
        const picks = [];
        if (rnd() < 0.5 && battle.s.zones.hand.length > 1) picks.push(pick(battle.s.zones.hand.filter(u => u !== uid)));
        if (rnd() < 0.3 && battle.s.res.length) picks.unshift(pick(battle.s.res).uid);
        battleCommand('play', { uid, mode, target, picks });
        if (battle.s.ap === 0 && rnd() < 0.8) break;
      }
      if (run.s.phase === 'BATTLE' && battle.s.phase === 'PLAYER') battleCommand('end');
    }
  }
  function saveMemory() {
    const changes = run.memoryChanges();
    if (memoryPolicy === 'v2') {
      const selectedChangeIds = [];
      for (const info of Object.values(changes)) for (const change of info.changes) {
        if (run.s.pending.defeat && selectedChangeIds.length) continue;
        if (rnd() < 0.8 && !run.previewMemory([...selectedChangeIds, change.id]).error) selectedChangeIds.push(change.id);
      }
      command('commitMemory', { requestId: `${setup.runId}-save`, runId: setup.runId,
        expectedMemoryVersion: memory.version, selectedChangeIds }, context.memory);
    } else {
      const selection = {}; let keptOne = false;
      for (const [cid, info] of Object.entries(changes)) {
        selection[cid] = []; let used = 0;
        info.changes.forEach((change, index) => {
          if (run.s.pending.defeat) {
            if (!keptOne && change.cost <= info.capacity) { selection[cid].push(index); keptOne = true; }
          } else if (used + change.cost <= info.capacity && rnd() < 0.8) { selection[cid].push(index); used += change.cost; }
        });
      }
      command('commitMemory', selection);
    }
    // The existing fuzz contract also requires saved decks to rebuild through Run.create.
    MF.Run.create(data, { ...copy(setup), memory: copy(run.s.savedMemory), seed: (seed + 99) >>> 0,
      ...(memoryPolicy === 'v2' ? { runId: `${setup.runId}-reload`, memoryVersion: context.memory.version } : {}) });
  }

  for (let steps = 0; run.s.phase !== 'DONE'; steps++) {
    assert.ok(steps < policy.limits.runSteps, 'Trace Run step guard exceeded');
    const state = run.s;
    switch (state.phase) {
      case 'MAP': command('enter', pick(run.available())); break;
      case 'BATTLE': playBattle(); break;
      case 'REWARD': {
        const pending = state.pending;
        for (const evolution of [...(pending.evolutions || [])]) if (rnd() < 0.8) command('evolve', evolution.key, pick(evolution.options));
        if (pending.cards.length) { if (rnd() < 0.75) command('takeCard', pick(pending.cards)); else command('skipCard'); }
        if (pending.relic?.length) command('takeRelic', pick(pending.relic));
        if (pending.part) command('takePart');
        const deck = state.deck.filter(card => !card.weapon);
        if (state.partsFound.length && rnd() < 0.5 && deck.length) command('applyFoundPart', state.partsFound[0], pick(deck).key);
        command('proceed'); break;
      }
      case 'REST': {
        const upgrades = state.deck.filter(card => !card.plus && !card.weapon);
        if (rnd() < 0.5) command('rest', 'heal');
        else if (upgrades.length) command('rest', 'upgrade', pick(upgrades).key);
        command('proceed'); break;
      }
      case 'FORGE': {
        const deck = state.deck.filter(card => !card.weapon);
        if (deck.length) {
          if (rnd() < 0.6) { command('forgeOffer', pick(deck).key); command('forgeApply', pick(state.pending.offers.parts)); }
          else command('forgeRemove', pick(deck).key);
        }
        command('proceed'); break;
      }
      case 'SHOP': {
        for (const kind of ['cards', 'parts', 'relics']) for (const item of state.pending[kind]) if (rnd() < 0.4) command('buy', kind, item.id);
        const deck = state.deck.filter(card => !card.weapon);
        if (rnd() < 0.3 && state.pending.removeCost && deck.length) command('buy', 'remove', null, pick(deck).key);
        command('proceed'); break;
      }
      case 'EVENT': {
        const event = data.events.find(ev => ev.id === state.pending.id);
        let done = false;
        for (let k = 0; k < 4 && !done; k++) {
          const deck = state.deck.filter(card => !card.weapon);
          done = command('eventChoose', Math.floor(rnd() * event.choices.length), ...(deck.length ? [pick(deck).key] : []));
        }
        if (!done) assert.ok(command('eventChoose', event.choices.length - 1), 'Trace event fallback was rejected');
        if (run.s.phase !== 'FORGE') command('proceed');
        break;
      }
      case 'ARCHIVE': command('archive', rnd() < 0.7 ? 'continue' : 'return'); break;
      case 'VICTORY': case 'DEFEAT': saveMemory(); break;
      default: throw new Error(`Unknown trace Run phase ${state.phase}`);
    }
  }
  trace.finalState = snapshot(context); trace.finalStateHash = contextHash(context);
  trace.summary = { outcome: run.s.result.outcome, committed: trace.commands.filter(step => step.outcome === 'committed').length,
    rejected: trace.commands.filter(step => step.outcome === 'rejected').length, visitedZones: run.s.zoneIdx + 1 };
  return trace;
}

export function replayScenario(trace) {
  assert.equal(trace.schema, schema, 'Unsupported trace schema');
  assert.deepEqual(trace.sources, sources, 'Trace source provenance mismatch');
  assert.equal(trace.seed, trace.setup.seed, 'Trace setup seed mismatch');
  assert.equal(trace.length, trace.setup.length, 'Trace setup length mismatch');
  assert.equal(trace.memoryPolicy, trace.setup.memoryPolicy, 'Trace setup memory policy mismatch');
  assert.deepEqual(trace.party, trace.setup.party, 'Trace setup party mismatch');
  const context = { run: MF.Run.create(data, copy(trace.setup)), memory: trace.memoryPolicy === 'v2' ? MF.MemoryV2.migrate(data, {}, {}) : null };
  let previousHash = contextHash(context);
  assert.equal(previousHash, trace.initialStateHash, 'Trace initial state hash mismatch');
  assert.equal(stateHash(trace.initialState), previousHash, 'Trace initial snapshot hash mismatch');
  for (const [index, expected] of trace.commands.entries()) {
    const actual = execute(context, copy(expected.command));
    assert.equal(actual.outcome, expected.outcome, `Trace command ${index} outcome mismatch`);
    assert.equal(actual.errorCode, expected.errorCode, `Trace command ${index} rejection mismatch`);
    assert.equal(actual.stateHash, expected.stateHash, `Trace command ${index} state hash mismatch`);
    if (actual.outcome === 'rejected') assert.equal(actual.stateHash, previousHash, `Trace command ${index} rejection mutated state`);
    previousHash = actual.stateHash;
  }
  assert.equal(context.run.s.phase, 'DONE', 'Trace did not reach a terminal memory commit');
  assert.equal(previousHash, trace.finalStateHash, 'Trace final state hash mismatch');
  assert.equal(stateHash(trace.finalState), previousHash, 'Trace final snapshot hash mismatch');
  return { commands: trace.commands.length, finalState: snapshot(context) };
}

export async function exportTraces({ output = join(root, 'engine/tests/vectors/traces.json'), ...options } = {}) {
  const plan = [...scenarioPlan(options)];
  const jsonl = /\.jsonl(?:\.gz)?$/.test(output);
  if (!/\.jsonl?(?:\.gz)?$/.test(output)) throw new Error('Output must end in .json, .jsonl, .json.gz or .jsonl.gz');
  const manifest = { schema, dataVersion: data.meta.version, sources, policy, legalRejections,
    hashing: { algorithm: 'SHA-256', encoding: 'UTF-8', input: '{run: Run.s, memory: v2 store or null}',
      keys: 'recursive ordinal UTF-16 order; arrays retain order', omit: ['log', 'ev'], numbers: 'safe integers only',
      undefined: 'omit object members; array elements become null', whitespace: 'none', strings: 'ECMAScript JSON.stringify escaping; Unicode preserved' },
    counts: { scenarios: plan.length, short: plan.filter(s => s.length === 'short').length, full: plan.filter(s => s.length === 'full').length },
    firstSeed: plan[0].seed, memoryPolicy: plan[0].memoryPolicy, scope: 'live zones 1/2; JS oracle only; C# replay not implemented' };
  const summary = { ...manifest.counts, commands: 0, rejected: 0, output: resolve(output) };
  function* chunks() {
    yield jsonl ? `${JSON.stringify({ type: 'manifest', ...manifest })}\n` : `${JSON.stringify(manifest).slice(0, -1)},"scenarios":[`;
    for (const [index, setup] of plan.entries()) {
      const trace = generateScenario(setup);
      summary.commands += trace.commands.length; summary.rejected += trace.summary.rejected;
      yield jsonl ? `${JSON.stringify({ type: 'scenario', trace })}\n` : `${index ? ',' : ''}${JSON.stringify(trace)}`;
    }
    if (!jsonl) yield ']}\n';
  }
  await mkdir(dirname(resolve(output)), { recursive: true });
  const temporary = await mkdtemp(join(dirname(resolve(output)), '.mf-traces-'));
  const partial = join(temporary, 'partial');
  try {
    await pipeline(Readable.from(chunks()), ...(/\.gz$/.test(output) ? [createGzip()] : []), createWriteStream(partial));
    await rename(partial, resolve(output));
  } finally { await rm(temporary, { recursive: true, force: true }); }
  return summary;
}

const help = `Usage: node tools/gen_traces.mjs [--short 700] [--full 300] [--seed 1000]
  [--memory-policy legacy|v2] [--output engine/tests/vectors/traces.json]

Default: all 1,000 scenarios, 700 short + 300 full, distinct sequential uint32 seeds.
Optional output formats: .json, .jsonl, .json.gz, .jsonl.gz (streamed, atomic replace).
JSON: manifest with scenarios[]. JSONL: manifest first, then {type:"scenario",trace}.
No timestamps. Sources use SHA-256 of the exact UTF-8 file bytes, including the
engine, game data, memory module, generator, and source fuzz policy.

C# consumer contract (mf-run-traces-v1):
  Create Run using each trace.setup, assert initialStateHash, then replay commands.
  Run commands: scope="run", kind=public method, args=ordered positional arguments.
  Battle commands: scope="battle", exact rid/v/kind/args emitted by Battle.do;
    apply through Run.battleCmd (including afterBattle). enter includes begin.
  Each attempt records committed/rejected, errorCode (null on success), stateHash.
  Known legal RuleErrors must preserve state; all other errors abort generation.
  Hash {run:Run.s,memory:<v2 store or null>}; omit log/ev recursively, object keys
    sort by ordinal UTF-16, retain array order, no whitespace, UTF-8 SHA-256 hex.
  Strings follow JSON.stringify (literal Unicode); undefined object members are
    omitted and undefined array elements are null. Numbers must be safe integers.
  initialState/finalState are normalized full snapshots of that same hash input.
  Default memoryPolicy is legacy. --memory-policy v2 uses existing empty-store
    migration, real exploration, preview and terminal versioned commit. The v2
    store is part of every state hash; this does not claim C#/server/Unity parity.
  For local JS validation, import replayScenario(trace) from this module.
`;

async function main(args) {
  if (args.length === 1 && ['--help', '-h'].includes(args[0])) { process.stdout.write(help); return; }
  const options = {};
  const names = { '--short': 'shortCount', '--full': 'fullCount', '--seed': 'seed', '--output': 'output', '--memory-policy': 'memoryPolicy' };
  for (let i = 0; i < args.length; i += 2) {
    const key = names[args[i]];
    if (!key || args[i + 1] === undefined || args[i + 1].startsWith('--') || Object.hasOwn(options, key)) throw new Error(`Invalid trace argument: ${args[i]}`);
    options[key] = ['shortCount', 'fullCount', 'seed'].includes(key) ? Number(args[i + 1]) : args[i + 1];
  }
  process.stdout.write(`${JSON.stringify(await exportTraces(options))}\n`);
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main(process.argv.slice(2)).catch(error => { console.error(error); process.exitCode = 1; });
}
