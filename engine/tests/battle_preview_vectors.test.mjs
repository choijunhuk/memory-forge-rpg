// P1-EXPEDITION-BATTLE-FORECAST: source pins, strict freshness, purity and independent literals for the battle play forecast oracle.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { gzipSync } from 'node:zlib';
import { generateCases, checkFixture, exportVectors, fixturePath } from '../../tools/gen_battle_preview_vectors.mjs';
import { decodeVectors } from '../../tools/gen_run_create_vectors.mjs';

const require = createRequire(import.meta.url);
const data = require('../../data/game_data.json');
const MF = require('../rules-core.js');
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = value => createHash('sha256').update(value).digest('hex');
const fixture = generateCases();
const none = { error: null, cost: 0, due: null, hidden: false, enemyHpLoss: 0, blockDelta: 0, hpDelta: 0, defeat: false };
const caseOf = id => { const row = fixture.cases.find(c => c.id === id); assert.ok(row, `Missing preview case ${id}`); return row; };
const recordOf = (id, label) => {
  const found = caseOf(id).steps.flatMap(step => step.forecasts).find(f => f.label === label);
  assert.ok(found, `Missing preview forecast ${id}/${label}`);
  return found;
};
const expect = (id, label, mode, values = {}) => assert.deepEqual(recordOf(id, label).expected, { ...none, mode, ...values }, `${id}/${label}`);

// Independent of the generator: the accepted practice-view rule and a second derivation from the public API.
const hiddenDefinition = ops => Array.isArray(ops) && ops.some(op => op.tgt === 'rand' || op.pick === 'random' || op.op === 'draw' || hiddenDefinition(op.then) || hiddenDefinition(op.else));
function rederive(live, fresh, args) {
  const snapshot = structuredClone(live.s), base = { ...none, mode: args.mode };
  const code = live.check(args.uid, args.mode, args.target, [...args.picks]);
  assert.deepEqual(live.s, snapshot, 'check() must not change the live battle');
  if (code !== null) return { expected: { ...base, error: code }, hiddenBy: [] };
  const { battle, play } = fresh();
  const before = clone(battle.s);
  let response;
  try { response = play({ uid: args.uid, mode: args.mode, target: args.target, picks: [...args.picks] }); }
  catch (error) { if (error instanceof MF.RuleError) return { expected: { ...base, error: error.code }, hiddenBy: [] }; throw error; }
  const after = battle.s, cost = response.ev.find(e => e.e === 'play').cost;
  if (args.mode !== 'now') return { expected: { ...base, cost, due: after.res.find(r => r.uid === args.uid).due }, hiddenBy: [] };
  const hiddenBy = [...(hiddenDefinition(live.effDef(live.s.cards[args.uid]).eff) ? ['definition'] : []),
    ...(before.rng.join() === after.rng.join() ? [] : ['rng']), ...(before.zones.draw.join() === after.zones.draw.join() ? [] : ['draw'])];
  assert.deepEqual(live.s, snapshot, 'effDef() must not change the live battle');
  if (hiddenBy.length) return { expected: { ...base, cost, hidden: true }, hiddenBy };
  const loss = before.enemies.map(e => Math.max(0, e.hp - after.enemies.find(x => x.uid === e.uid).hp)).reduce((a, b) => a + b, 0);
  return { expected: { ...base, cost, enemyHpLoss: loss, blockDelta: after.block - before.block, hpDelta: after.hp - before.hp, defeat: after.phase === 'DEFEAT' }, hiddenBy };
}

test('original-JS regeneration keeps the fixed bounded cases and exactly 70 forecasts / 40 real commands', () => {
  assert.equal(fixture.schema, 'mf-battle-preview-v1');
  assert.equal(fixture.status, 'JS_ORACLE');
  assert.match(fixture.scope, /Unity view, device and server acceptance PENDING/);
  assert.deepEqual(fixture.cases.map(c => [c.id, c.group]), [['damage-block-targets', 'battle'], ['heal-reservation-slots', 'battle'], ['reflect-defeat', 'battle'],
    ['overload-self-damage', 'battle'], ['hidden-definition', 'battle'], ['hidden-hook-draw', 'battle'], ['hidden-hook-rng', 'battle'], ['picks-pull', 'battle'], ['run-expedition', 'run']]);
  assert.deepEqual(fixture.counts, { cases: 9, battleForecasts: 51, battleCommands: 23, runFixedForecasts: 6, runPolicyForecasts: 13, runCommands: 17, forecasts: 70, commands: 40 });
  const forecasts = fixture.cases.reduce((n, row) => n + row.steps.reduce((m, step) => m + step.forecasts.length, 0), 0);
  const commands = fixture.cases.reduce((n, row) => n + row.steps.filter(step => step.request).length, 0);
  assert.deepEqual([forecasts, commands], [70, 40]);
  assert.deepEqual(fixture.coverage.errors, ['AP_LOW', 'BAD_TARGET', 'INVALID_MODE', 'NOT_IN_HAND', 'NOT_RESERVABLE', 'NO_BATTLE', 'OVERLOAD_WOULD_KILL', 'RESERVE_ONLY', 'SLOT_FULL', 'WRONG_PHASE']);
  for (const reason of ['definition', 'rng', 'draw']) assert.ok(fixture.coverage.hidden.includes(reason), `missing a forecast hidden only by ${reason}`);
  assert.deepEqual(Object.keys(fixture.sources), ['data', 'engine', 'memory', 'canonical', 'runCreateDecoder', 'generator']);
  assert.deepEqual(fixture.fields, ['error', 'mode', 'cost', 'due', 'hidden', 'enemyHpLoss', 'blockDelta', 'hpDelta', 'defeat']);
  for (const row of fixture.cases.filter(c => c.group === 'battle')) {
    assert.deepEqual(Object.keys(row.setup), ['party', 'deck', 'hp', 'maxHp', 'seed', 'enemies', 'relics'], `${row.id}: setup holds only Battle.create inputs`);
    assert.ok(row.setup.party.every(a => isDeepStrictEqual(Object.keys(a), ['charId'])) && row.setup.deck.every(c => isDeepStrictEqual(Object.keys(c), ['id'])), `${row.id}: no injected actor/card state`);
  }
  const walk = value => Array.isArray(value) ? value.some(walk) : value !== null && typeof value === 'object' ? value.pick === 'random' || Object.values(value).some(walk) : false;
  assert.ok(!data.cards.some(card => walk(card.eff) || walk(card.plus ?? null)), 'pick:"random" stays NOT_COVERED only while no authored card has it');
  assert.ok(fixture.reachability.notCovered.definitionPickRandom && fixture.reachability.csharpOnly.lateCommandRejection);
  assert.ok(isDeepStrictEqual(generateCases(), fixture), 'regeneration is deterministic');
});

test('independent original-source literals: damage, block, heal, self-damage, reflect defeat, reservations, picks and hidden reasons', () => {
  const D = 'damage-block-targets', H = 'heal-reservation-slots', R = 'reflect-defeat', O = 'overload-self-damage', F = 'hidden-definition', P = 'picks-pull';
  // power4 160 / vit 81 / level 1: sera_round 8, harin_shield 8, sera_cover blk5+dmg4, harin_bash 6; e_cog gains block 6 on its first turn.
  expect(D, 'turn1 sera_round cog', 'now', { cost: 1, enemyHpLoss: 8 });
  expect(D, 'turn1 harin_shield', 'now', { cost: 1, blockDelta: 8 });
  expect(D, 'turn2 sera_round cog block6', 'now', { cost: 1, enemyHpLoss: 2 });
  expect(D, 'turn2 sera_cover cog block6', 'now', { cost: 1, blockDelta: 5 });
  expect(D, 'turn2 harin_bash moth', 'now', { cost: 1, enemyHpLoss: 6 });
  expect(D, 'turn2 sera_cover cog after hit', 'now', { cost: 1, enemyHpLoss: 4, blockDelta: 5 });
  expect(D, 'turn1 sera_round next', 'next', { cost: 1, due: 2 });
  for (const [label, error, mode] of [['created phase', 'WRONG_PHASE', 'now'], ['turn1 missing target', 'BAD_TARGET', 'now'], ['turn1 invalid mode', 'INVALID_MODE', 'later'],
    ['turn1 unknown uid', 'NOT_IN_HAND', 'now'], ['turn2 played card', 'NOT_IN_HAND', 'now']]) expect(D, label, mode, { error });
  // aila_bell heals 5 at HP150/162; empty_clock -1 only before the first reservation; one base after slot.
  expect(H, 'heal', 'now', { cost: 1, hpDelta: 5 });
  expect(H, 'heal again', 'now', { cost: 1, hpDelta: 5 });
  expect(H, 'page damage', 'now', { cost: 1, enemyHpLoss: 8 });
  expect(H, 'first reservation next', 'next', { cost: 1, due: 2 });
  expect(H, 'first reservation after', 'after', { cost: 1, due: 3 });
  expect(H, 'after-only after', 'after', { cost: 2, due: 3 });
  expect(H, 'discounted zero cost after', 'after', { cost: 0, due: 3 });
  expect(H, 'clock used next', 'next', { cost: 1, due: 2 });
  expect(H, 'reserve-only now', 'now', { error: 'RESERVE_ONLY' });
  expect(H, 'after-only next', 'next', { error: 'NOT_RESERVABLE' });
  expect(H, 'after slot full', 'after', { error: 'SLOT_FULL' });
  // e_mirrorclerk reflects 3 from its first turn; at HP3 the first reflected hit is lethal and stops the second hit.
  expect(R, 'turn1 no reflect yet', 'now', { cost: 1, enemyHpLoss: 8 });
  expect(R, 'reflect lethal multi-hit', 'now', { cost: 1, enemyHpLoss: 4, hpDelta: -3, defeat: true });
  expect(R, 'reflect lethal single hit', 'now', { cost: 1, enemyHpLoss: 8, hpDelta: -3, defeat: true });
  expect(R, 'reflect reservation', 'next', { cost: 1, due: 3 });
  expect(R, 'after defeat', 'now', { error: 'WRONG_PHASE' });
  // HP5/81 is at most 50%: lumi_critical +30% (18 -> 23, 8 -> 10); overdrive's self-damage 5 would leave HP below 1.
  expect(O, 'selfdmg critical damage', 'now', { cost: 1, enemyHpLoss: 23, hpDelta: -4 });
  expect(O, 'critical jab', 'now', { cost: 1, enemyHpLoss: 10 });
  expect(O, 'hp1 critical jab', 'now', { cost: 1, enemyHpLoss: 10 });
  expect(O, 'overload would kill', 'now', { error: 'OVERLOAD_WOULD_KILL' });
  expect(O, 'hp1 overload would kill', 'now', { error: 'OVERLOAD_WOULD_KILL' });
  expect(F, 'draw definition', 'now', { cost: 0, hidden: true });
  expect(F, 'random target definition', 'now', { cost: 3, hidden: true });
  expect(F, 'nested draw definition', 'now', { cost: 1, hidden: true });
  expect(F, 'nested draw definition after owner change', 'now', { cost: 1, hidden: true });
  expect(F, 'visible control', 'now', { cost: 1, enemyHpLoss: 8 });
  expect(F, 'nested draw reserved', 'next', { cost: 1, due: 2 });
  expect(F, 'ap low', 'now', { error: 'AP_LOW' });
  expect('hidden-hook-draw', 'third owner hook draw', 'now', { cost: 1, hidden: true });
  expect('hidden-hook-draw', 'third owner reserve', 'next', { cost: 1, due: 2 });
  const control = recordOf('hidden-hook-draw', 'earlier owner control').expected;
  assert.ok(control.error === null && !control.hidden && control.cost === 1 && (control.enemyHpLoss > 0 || control.blockDelta > 0), 'an earlier owner stays visible');
  expect('hidden-hook-rng', 'exhaust hook random damage', 'now', { cost: 3, hidden: true });
  expect('hidden-hook-rng', 'harin_shield control', 'now', { cost: 1, blockDelta: 8 });
  expect('hidden-hook-rng', 'after hook harin_shield', 'now', { cost: 1, blockDelta: 8 });
  expect('hidden-hook-rng', 'after hook harin_bash page', 'now', { cost: 1, enemyHpLoss: 6 });
  // sera_pull: no pick, the after pick and an invalid pick move sera_timed; picking the next-turn sera_round resolves it now at rv12.
  expect(P, 'pull default', 'now', {});
  expect(P, 'pull next reservation', 'now', { enemyHpLoss: 12 });
  expect(P, 'pull after reservation', 'now', {});
  expect(P, 'pull invalid pick', 'now', {});
  for (const [label, mode] of [['map now', 'now'], ['map after with picks', 'after'], ['after battle', 'now']]) expect('run-expedition', label, mode, { error: 'NO_BATTLE' });
  expect('run-expedition', 'run unknown uid', 'now', { error: 'NOT_IN_HAND' });
  const reasons = (id, label) => recordOf(id, label).hiddenBy;
  assert.deepEqual(reasons(F, 'draw definition'), ['definition']);
  assert.deepEqual(reasons(F, 'random target definition'), ['definition', 'rng']);
  assert.deepEqual(reasons(F, 'nested draw definition'), ['definition']);
  assert.deepEqual(reasons(F, 'nested draw definition after owner change'), ['definition']);
  assert.deepEqual(reasons('hidden-hook-draw', 'third owner hook draw'), ['draw']);
  assert.deepEqual(reasons('hidden-hook-rng', 'exhaust hook random damage'), ['rng']);
});

test('every forecast re-derives from independent public replays and every real command reproduces its recorded state', () => {
  for (const row of fixture.cases.filter(c => c.group === 'battle')) {
    const live = MF.Battle.create(data, clone(row.setup)), requests = [];
    assert.deepEqual(clone(live.s), row.created.state, `${row.id} created`);
    for (const step of row.steps) {
      const fresh = () => {
        const battle = MF.Battle.create(data, clone(row.setup));
        for (const request of requests) battle.command(clone(request));
        return { battle, play: args => battle.command({ rid: null, v: battle.s.v, kind: 'play', args }) };
      };
      for (const record of step.forecasts) assert.deepEqual(rederive(live, fresh, record.args), { expected: record.expected, hiddenBy: record.hiddenBy }, `${row.id}/${record.label}`);
      if (!step.request) continue;
      assert.deepEqual(clone(live.command(clone(step.request))), step.response, `${row.id} ${step.request.rid} response`);
      assert.deepEqual(clone(live.s), step.after.state, `${row.id} ${step.request.rid} state`);
      requests.push(step.request);
    }
  }
  const run = caseOf('run-expedition'), live = MF.Run.create(data, clone(run.setup)), history = [];
  const apply = (target, request) => request.scope === 'run' ? target.enter(request.args.node) : target.battleCmd(request.kind, clone(request.args));
  const freshRun = () => { const target = MF.Run.create(data, clone(run.setup)); for (const request of history) apply(target, request); return target; };
  for (const step of run.steps) {
    for (const record of step.forecasts) {
      if (live.battle) {
        const fresh = () => { const target = freshRun(), battle = target.battle; return { battle, play: args => target.battleCmd('play', args) }; };
        assert.deepEqual(rederive(live.battle, fresh, record.args), { expected: record.expected, hiddenBy: record.hiddenBy }, `run/${record.label}`);
        continue;
      }
      let code = null;
      try { freshRun().battleCmd('play', clone(record.args)); } catch (error) { code = error.code; }
      assert.equal(code, 'NO_BATTLE');
      assert.deepEqual([record.expected, record.hiddenBy], [{ ...none, mode: record.args.mode, error: code }, []], `run/${record.label}`);
    }
    if (!step.request) continue;
    const response = apply(live, step.request);
    assert.deepEqual(response === undefined ? null : clone(response), step.response, `run step ${history.length} response`);
    assert.deepEqual({ phase: live.s.phase, battle: live.s.battle ? clone(live.s.battle) : null }, { phase: step.after.phase, battle: step.after.battle }, `run step ${history.length} point`);
    history.push(step.request);
  }
  assert.ok(['REWARD', 'DEFEAT'].includes(live.s.phase) && !live.battle, 'the expedition battle actually ended');
});

test('source pins, strict freshness and tampered fixtures are rejected', () => {
  for (const source of Object.values(fixture.sources)) assert.equal(source.sha256, sha256(readFileSync(new URL(`../../${source.path}`, import.meta.url))));
  assert.deepEqual(checkFixture(clone(fixture)), fixture.counts);
  const tampered = change => { const copy = clone(fixture); change(copy); return copy; };
  for (const bad of [
    tampered(f => { f.sources.engine.sha256 = '0'.repeat(64); }),
    tampered(f => { f.cases.pop(); }),
    tampered(f => { f.cases.push(f.cases[0]); }),
    tampered(f => { f.cases[0].steps[1].forecasts[1].expected.enemyHpLoss += 1; }),
    tampered(f => { f.cases[8].steps[1].forecasts[0].expected.error = 'BAD_TARGET'; }),
    tampered(f => { f.cases[0].setup.state = {}; }),
    tampered(f => { f.cases[0].steps[2].after.state.rng[0] ^= 1; }),
    tampered(f => { f.reachability.notCovered = {}; }),
    tampered(f => { f.counts.forecasts = 69; }),
  ]) assert.throws(() => checkFixture(bad), /preview/i);
});

test('strict shared decoder: exact export round trip, corrupted containers and empty gzip prefix/suffix members rejected, previous output preserved', t => {
  const directory = mkdtempSync(join(tmpdir(), 'mf-battle-preview-'));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  const output = join(directory, 'preview.json.gz');
  const summary = exportVectors(output), bytes = readFileSync(output), text = Buffer.from(`${JSON.stringify(fixture)}\n`);
  assert.deepEqual([summary.forecasts, summary.commands, summary.bytes, summary.sha256], [70, 40, bytes.length, sha256(bytes)]);
  assert.ok(gzipSync(text, { level: 9 }).equals(bytes), 'fresh export must be byte identical to this process generation');
  assert.ok(isDeepStrictEqual(decodeVectors(bytes), fixture));
  assert.deepEqual(checkFixture(decodeVectors(bytes)), fixture.counts);
  exportVectors(output);
  assert.ok(readFileSync(output).equals(bytes), 'repeated export is deterministic');
  const empty = gzipSync(Buffer.alloc(0), { level: 9 }), crc = Buffer.from(bytes);
  crc[crc.length - 8] ^= 1;
  for (const bad of [Buffer.concat([empty, bytes]), Buffer.concat([bytes, empty]), bytes.subarray(0, bytes.length - 1), crc,
    gzipSync(Buffer.concat([Buffer.from([0xef, 0xbb, 0xbf]), text]), { level: 9 }), gzipSync(Buffer.from('{"schema":"a","schema":"b"}'), { level: 9 }),
    gzipSync(Buffer.from([0x7b, 0x22, 0xff, 0x22, 0x3a, 0x31, 0x7d]), { level: 9 })]) assert.throws(() => decodeVectors(bad));
  writeFileSync(output, 'previous artifact');
  const { check } = MF.Battle.prototype;
  for (const [replacement, expected] of [
    [() => { throw new Error('unexpected check fault'); }, /unexpected check fault/],
    // Fault injection only: an impure precheck must abort generation, never be recorded as a forecast.
    [function (...args) { this.s.log.push({ e: 'impure' }); return check.apply(this, args); }, /changed the live battle/],
  ]) {
    const mock = t.mock.method(MF.Battle.prototype, 'check', replacement);
    assert.throws(() => exportVectors(output), expected);
    assert.equal(readFileSync(output, 'utf8'), 'previous artifact');
    assert.deepEqual(readdirSync(directory), ['preview.json.gz']);
    mock.mock.restore();
  }
});

test('checked-in fixture decodes strictly and equals fresh original-JS regeneration', () => {
  const saved = decodeVectors(readFileSync(fixturePath));
  assert.deepEqual(checkFixture(saved), fixture.counts);
  assert.deepEqual([saved.counts.forecasts, saved.counts.commands], [70, 40]);
});
