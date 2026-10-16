// P1-EXPEDITION-BATTLE-FORECAST / P1-RESERVED-FORECAST-CORE: source pins, strict freshness, purity and independent literals for the v2
// battle play forecast oracle, plus the original HTML preview() itself executed against every visible empty-pick forecast.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { gzipSync } from 'node:zlib';
import { generateCases, checkFixture, exportVectors, fixturePath, nextAfterDiffers } from '../../tools/gen_battle_preview_vectors.mjs';
import { decodeVectors } from '../../tools/gen_run_create_vectors.mjs';

const require = createRequire(import.meta.url);
const data = require('../../data/game_data.json');
const MF = require('../rules-core.js');
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = value => createHash('sha256').update(value).digest('hex');
const fixture = generateCases();
const none = { error: null, cost: 0, due: null, hidden: false, enemyHpLoss: 0, blockDelta: 0, hpDelta: 0, defeat: false, enemyDamage: 0, newlyBroken: 0 };
const hit = (loss, damage = loss) => ({ enemyHpLoss: loss, enemyDamage: damage });
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
  let after = battle.s;
  const cost = response.ev.find(e => e.e === 'play').cost, reservation = after.res.find(r => r.uid === args.uid);
  const due = args.mode === 'now' ? null : reservation.due;
  if (args.mode !== 'now') {
    // Second, independent use of the original reserved resolver: the stored reservation target on an unpaid copy, card out of the hand.
    const copy = new MF.Battle(data, structuredClone(fresh().battle.s));
    copy.s.zones.hand = copy.s.zones.hand.filter(uid => uid !== args.uid); copy.s.phase = 'RESOLVING';
    try { copy.resolveCard(args.uid, { reserved: args.mode, target: reservation.target, picks: [] }); }
    catch (error) { if (error instanceof MF.RuleError) return { expected: { ...base, error: error.code }, hiddenBy: [] }; throw error; }
    after = copy.s;
  }
  const hiddenBy = [...(hiddenDefinition(live.effDef(live.s.cards[args.uid]).eff) ? ['definition'] : []),
    ...(before.rng.join() === after.rng.join() ? [] : ['rng']), ...(before.zones.draw.join() === after.zones.draw.join() ? [] : ['draw'])];
  assert.deepEqual(live.s, snapshot, 'effDef() must not change the live battle');
  if (hiddenBy.length) return { expected: { ...base, cost, due, hidden: true }, hiddenBy };
  const counted = before.enemies.map(e => [e, after.enemies.find(x => x.uid === e.uid)]);
  const loss = counted.reduce((n, [e, x]) => n + Math.max(0, e.hp - x.hp), 0), damage = counted.reduce((n, [e, x]) => n + Math.max(0, e.hp + e.block - x.hp - x.block), 0);
  const broken = counted.filter(([e, x]) => !e.brokenUntil && x.brokenUntil).length;
  return { expected: { ...base, cost, due, enemyHpLoss: loss, blockDelta: after.block - before.block, hpDelta: after.hp - before.hp, defeat: after.phase === 'DEFEAT',
    enemyDamage: damage, newlyBroken: broken }, hiddenBy };
}
// The original HTML preview() source text, executed as-is with the live battle; ERR is empty so any failure reads '사용 불가'.
const html = readFileSync(new URL('../../prototype/index.html', import.meta.url), 'utf8');
const previewStart = html.indexOf('function preview(uid, mode) {'), previewEnd = html.indexOf('// choice requests for immediate plays', previewStart);
assert.ok(previewStart > 0 && previewEnd > previewStart, 'the original HTML preview() is missing');
const originalPreview = new Function('app', 'MF', 'IX', 'ERR', `${html.slice(previewStart, previewEnd)}\nreturn preview;`);
const IX = MF.indexData(data);
// What the original preview() prints for a forecast's own fields (it never prints HP loss or defeat).
function htmlLine(expected) {
  const parts = [];
  if (expected.enemyDamage) parts.push(`피해 ${expected.enemyDamage}`);
  if (expected.blockDelta > 0) parts.push(`방어 +${expected.blockDelta}`);
  if (expected.hpDelta > 0) parts.push(`회복 ${expected.hpDelta}`);
  if (expected.hpDelta < 0) parts.push(`체력 ${expected.hpDelta}`);
  if (expected.newlyBroken) parts.push('균열!');
  return (parts.join(' · ') || '효과 적용') + (expected.mode !== 'now' ? ' (현재 상태 기준 예상)' : '');
}
const htmlChecks = { now: 0, reserved: 0 };
function checkHtml(live, record, label) {
  if (record.expected.error !== null || record.expected.hidden || record.args.picks.length) return;
  const before = structuredClone(live.s);
  const shown = originalPreview({ run: { battle: live }, target: record.args.target }, MF, IX, {})(record.args.uid, record.args.mode);
  assert.deepEqual(live.s, before, `${label}: the original preview changed the live battle`);
  assert.equal(shown, htmlLine(record.expected), `${label}: the original HTML preview disagrees`);
  htmlChecks[record.args.mode === 'now' ? 'now' : 'reserved']++;
}

test('original-JS regeneration keeps every v1 case/query/command and adds the bounded v2 reserved cases: 91 forecasts / 52 real commands', () => {
  assert.equal(fixture.schema, 'mf-battle-preview-v2');
  assert.equal(fixture.status, 'JS_ORACLE');
  assert.match(fixture.scope, /original HTML preview current-state reserved resolution/);
  assert.match(fixture.scope, /Unity view, device and server acceptance PENDING/);
  assert.deepEqual(fixture.cases.map(c => [c.id, c.group]), [['damage-block-targets', 'battle'], ['heal-reservation-slots', 'battle'], ['reflect-defeat', 'battle'],
    ['overload-self-damage', 'battle'], ['hidden-definition', 'battle'], ['hidden-hook-draw', 'battle'], ['hidden-hook-rng', 'battle'], ['picks-pull', 'battle'], ['run-expedition', 'run'],
    ['reserved-estimates', 'battle'], ['reserved-hidden-definition-rng', 'battle'], ['reserved-hidden-draw-hook', 'battle']]);
  assert.deepEqual(fixture.counts, { cases: 12, battleForecasts: 72, battleCommands: 35, runFixedForecasts: 6, runPolicyForecasts: 13, runCommands: 17, forecasts: 91, commands: 52 });
  const forecasts = fixture.cases.reduce((n, row) => n + row.steps.reduce((m, step) => m + step.forecasts.length, 0), 0);
  const commands = fixture.cases.reduce((n, row) => n + row.steps.filter(step => step.request).length, 0);
  assert.deepEqual([forecasts, commands], [91, 52]);
  // The v1 corpus is intact: the first nine cases still hold exactly 70 forecasts and 40 commands.
  const v1 = fixture.cases.slice(0, 9);
  assert.deepEqual([v1.reduce((n, row) => n + row.steps.reduce((m, step) => m + step.forecasts.length, 0), 0), v1.reduce((n, row) => n + row.steps.filter(step => step.request).length, 0)], [70, 40]);
  assert.deepEqual(fixture.coverage.errors, ['AP_LOW', 'BAD_TARGET', 'INVALID_MODE', 'NOT_IN_HAND', 'NOT_RESERVABLE', 'NO_BATTLE', 'OVERLOAD_WOULD_KILL', 'RESERVE_ONLY', 'SLOT_FULL', 'WRONG_PHASE']);
  for (const reason of ['definition', 'rng', 'draw']) assert.ok(fixture.coverage.hidden.includes(reason), `missing a forecast hidden only by ${reason}`);
  assert.deepEqual(Object.keys(fixture.sources), ['data', 'engine', 'memory', 'canonical', 'runCreateDecoder', 'htmlPreview', 'generator']);
  assert.equal(fixture.sources.htmlPreview.path, 'prototype/index.html');
  assert.deepEqual(fixture.fields, ['error', 'mode', 'cost', 'due', 'hidden', 'enemyHpLoss', 'blockDelta', 'hpDelta', 'defeat', 'enemyDamage', 'newlyBroken']);
  assert.deepEqual(Object.keys(fixture.contract), ['request', 'error', 'reservation', 'hidden', 'outcome', 'replay', 'transition']);
  assert.deepEqual(Object.keys(fixture.reachability.csharpOnly), ['lateCommandRejection', 'estimateRejection']);
  for (const record of fixture.cases.flatMap(row => row.steps.flatMap(step => step.forecasts))) {
    assert.ok(record.expected.enemyDamage >= record.expected.enemyHpLoss, `${record.label}: damage includes HP loss`);
    if (record.expected.error !== null || record.expected.hidden) assert.deepEqual([record.expected.enemyHpLoss, record.expected.blockDelta, record.expected.hpDelta,
      record.expected.defeat, record.expected.enemyDamage, record.expected.newlyBroken], [0, 0, 0, false, 0, 0], `${record.label}: rejected/hidden outcome fields stay default`);
  }
  for (const row of fixture.cases.filter(c => c.group === 'battle')) {
    assert.deepEqual(Object.keys(row.setup), ['party', 'deck', 'hp', 'maxHp', 'seed', 'enemies', 'relics'], `${row.id}: setup holds only Battle.create inputs`);
    assert.ok(row.setup.party.every(a => isDeepStrictEqual(Object.keys(a), ['charId'])) && row.setup.deck.every(c => isDeepStrictEqual(Object.keys(c), ['id'])), `${row.id}: no injected actor/card state`);
  }
  const walk = value => Array.isArray(value) ? value.some(walk) : value !== null && typeof value === 'object' ? value.pick === 'random' || Object.values(value).some(walk) : false;
  assert.ok(!data.cards.some(card => walk(card.eff) || walk(card.plus ?? null)), 'pick:"random" stays NOT_COVERED only while no authored card has it');
  assert.ok(fixture.reachability.notCovered.definitionPickRandom && fixture.reachability.csharpOnly.lateCommandRejection);
  // The next/after witness holds at exactly one forecast point: the timed keeper pair of reserved-estimates step 1 (22 against 30).
  assert.ok(nextAfterDiffers(fixture.cases));
  const points = fixture.cases.flatMap(row => row.steps.map((step, index) => [row.id, index, step])).filter(([, , step]) => nextAfterDiffers([{ steps: [step] }]));
  assert.deepEqual(points.map(([id, index, step]) => [id, index, step.forecasts.slice(0, 2).map(f => f.label)]),
    [['reserved-estimates', 1, ['timed next keeper', 'timed after keeper']]]);
  // Control: equalising that pair leaves a cross-point lookalike (step 5 'timed next broken keeper' 18 against step 1 after 22) that a
  // global uid/target comparison still accepts, while the one-point witness rejects it.
  const lookalike = clone(fixture), [timedNext, timedAfter] = lookalike.cases[9].steps[1].forecasts;
  Object.assign(timedAfter.expected, { enemyHpLoss: timedNext.expected.enemyHpLoss, enemyDamage: timedNext.expected.enemyDamage });
  const shown = lookalike.cases.flatMap(row => row.steps.flatMap(step => step.forecasts)).filter(f => f.args.mode !== 'now' && f.expected.error === null && !f.expected.hidden);
  assert.ok(shown.some(a => shown.some(b => a.args.uid === b.args.uid && a.args.target === b.args.target && a.args.mode !== b.args.mode
    && a.expected.enemyDamage !== b.expected.enemyDamage)), 'the control keeps a cross-point lookalike');
  assert.equal(nextAfterDiffers(lookalike.cases), false, 'a cross-point-only lookalike cannot satisfy the one-point witness');
  assert.ok(isDeepStrictEqual(generateCases(), fixture), 'regeneration is deterministic');
});

test('independent original-source literals: damage, block, heal, self-damage, reflect defeat, reservation estimates, breaks, picks and hidden reasons', () => {
  const D = 'damage-block-targets', H = 'heal-reservation-slots', R = 'reflect-defeat', O = 'overload-self-damage', F = 'hidden-definition', P = 'picks-pull';
  // power4 160 / vit 81 / level 1: sera_round 8, harin_shield 8, sera_cover blk5+dmg4, harin_bash 6; e_cog gains block 6 on its first turn.
  expect(D, 'turn1 sera_round cog', 'now', { cost: 1, ...hit(8) });
  expect(D, 'turn1 harin_shield', 'now', { cost: 1, blockDelta: 8 });
  expect(D, 'turn2 sera_round cog block6', 'now', { cost: 1, ...hit(2, 8) });
  expect(D, 'turn2 sera_cover cog block6', 'now', { cost: 1, blockDelta: 5, ...hit(0, 4) });
  expect(D, 'turn2 harin_bash moth', 'now', { cost: 1, ...hit(6) });
  expect(D, 'turn2 sera_cover cog after hit', 'now', { cost: 1, blockDelta: 5, ...hit(4) });
  // Current-state estimate: sera_round rv12 against the unblocked turn-1 cog.
  expect(D, 'turn1 sera_round next', 'next', { cost: 1, due: 2, ...hit(12) });
  for (const [label, error, mode] of [['created phase', 'WRONG_PHASE', 'now'], ['turn1 missing target', 'BAD_TARGET', 'now'], ['turn1 invalid mode', 'INVALID_MODE', 'later'],
    ['turn1 unknown uid', 'NOT_IN_HAND', 'now'], ['turn2 played card', 'NOT_IN_HAND', 'now']]) expect(D, label, mode, { error });
  // aila_bell heals 5 at HP150/162; empty_clock -1 only before the first reservation; one base after slot. Each estimate kills the 12-HP page.
  expect(H, 'heal', 'now', { cost: 1, hpDelta: 5 });
  expect(H, 'heal again', 'now', { cost: 1, hpDelta: 5 });
  expect(H, 'page damage', 'now', { cost: 1, ...hit(8) });
  expect(H, 'first reservation next', 'next', { cost: 1, due: 2, ...hit(12) });
  expect(H, 'first reservation after', 'after', { cost: 1, due: 3, ...hit(12) });
  expect(H, 'after-only after', 'after', { cost: 2, due: 3, ...hit(12) });
  expect(H, 'discounted zero cost after', 'after', { cost: 0, due: 3, ...hit(12) });
  expect(H, 'clock used next', 'next', { cost: 1, due: 2, ...hit(12) });
  expect(H, 'reserve-only now', 'now', { error: 'RESERVE_ONLY' });
  expect(H, 'after-only next', 'next', { error: 'NOT_RESERVABLE' });
  expect(H, 'after slot full', 'after', { error: 'SLOT_FULL' });
  // e_mirrorclerk reflects 3 from its first turn; at HP3 the first reflected hit is lethal and stops the second hit (the rv6 estimate too).
  expect(R, 'turn1 no reflect yet', 'now', { cost: 1, ...hit(8) });
  expect(R, 'reflect lethal multi-hit', 'now', { cost: 1, ...hit(4), hpDelta: -3, defeat: true });
  expect(R, 'reflect lethal single hit', 'now', { cost: 1, ...hit(8), hpDelta: -3, defeat: true });
  expect(R, 'reflect reservation', 'next', { cost: 1, due: 3, ...hit(6), hpDelta: -3, defeat: true });
  expect(R, 'after defeat', 'now', { error: 'WRONG_PHASE' });
  // HP5/81 is at most 50%: lumi_critical +30% (18 -> 23, 8 -> 10); overdrive's self-damage 5 would leave HP below 1.
  expect(O, 'selfdmg critical damage', 'now', { cost: 1, ...hit(23), hpDelta: -4 });
  expect(O, 'critical jab', 'now', { cost: 1, ...hit(10) });
  expect(O, 'hp1 critical jab', 'now', { cost: 1, ...hit(10) });
  expect(O, 'overload would kill', 'now', { error: 'OVERLOAD_WOULD_KILL' });
  expect(O, 'hp1 overload would kill', 'now', { error: 'OVERLOAD_WOULD_KILL' });
  expect(F, 'draw definition', 'now', { cost: 0, hidden: true });
  expect(F, 'random target definition', 'now', { cost: 3, hidden: true });
  expect(F, 'nested draw definition', 'now', { cost: 1, hidden: true });
  expect(F, 'nested draw definition after owner change', 'now', { cost: 1, hidden: true });
  expect(F, 'visible control', 'now', { cost: 1, ...hit(8) });
  expect(F, 'nested draw reserved', 'next', { cost: 1, due: 2, hidden: true });
  expect(F, 'ap low', 'now', { error: 'AP_LOW' });
  expect('hidden-hook-draw', 'third owner hook draw', 'now', { cost: 1, hidden: true });
  // The estimate starts before the third owner's play, so unfinished_score never draws in it: nia_wrench rv10 or nia_barrier rv10.
  const reserve = recordOf('hidden-hook-draw', 'third owner reserve'), reserveCard = caseOf('hidden-hook-draw').created.state.cards[reserve.args.uid].id;
  expect('hidden-hook-draw', 'third owner reserve', 'next', { cost: 1, due: 2, ...(reserveCard === 'nia_wrench' ? hit(10) : { blockDelta: 10 }) });
  assert.ok(['nia_wrench', 'nia_barrier'].includes(reserveCard));
  const control = recordOf('hidden-hook-draw', 'earlier owner control').expected;
  assert.ok(control.error === null && !control.hidden && control.cost === 1 && (control.enemyHpLoss > 0 || control.blockDelta > 0), 'an earlier owner stays visible');
  expect('hidden-hook-rng', 'exhaust hook random damage', 'now', { cost: 3, hidden: true });
  expect('hidden-hook-rng', 'harin_shield control', 'now', { cost: 1, blockDelta: 8 });
  expect('hidden-hook-rng', 'after hook harin_shield', 'now', { cost: 1, blockDelta: 8 });
  expect('hidden-hook-rng', 'after hook harin_bash page', 'now', { cost: 1, ...hit(6) });
  // sera_pull: no pick, the after pick and an invalid pick move sera_timed; picking the next-turn sera_round resolves it now at rv12.
  expect(P, 'pull default', 'now', {});
  expect(P, 'pull next reservation', 'now', hit(12));
  expect(P, 'pull after reservation', 'now', {});
  expect(P, 'pull invalid pick', 'now', {});
  for (const [label, mode] of [['map now', 'now'], ['map after with picks', 'after'], ['after battle', 'now']]) expect('run-expedition', label, mode, { error: 'NO_BATTLE' });
  expect('run-expedition', 'run unknown uid', 'now', { error: 'NOT_IN_HAND' });

  // v2: keeper 38 HP/brk4, cog 24 HP/brk4 (both volt-weak). Sera foresight +25% (reserved vs broken) and broken +25% multiply: rv6 -> 9, rv22 -> 34.
  const E = 'reserved-estimates', X = 'reserved-hidden-definition-rng', K = 'reserved-hidden-draw-hook';
  expect(E, 'timed next keeper', 'next', { cost: 2, due: 2, ...hit(22) });
  expect(E, 'timed after keeper', 'after', { cost: 2, due: 3, ...hit(30) });
  expect(E, 'aim next zero outcome', 'next', { cost: 1, due: 2 });
  expect(E, 'cover next keeper', 'next', { cost: 1, due: 2, blockDelta: 7, ...hit(6) });
  expect(E, 'timed next breaks keeper', 'next', { cost: 2, due: 2, ...hit(22), newlyBroken: 1 });
  expect(E, 'cover now breaks keeper', 'now', { cost: 1, blockDelta: 5, ...hit(4), newlyBroken: 1 });
  expect(E, 'timed next ap low', 'next', { error: 'AP_LOW' });
  expect(E, 'cover next broken keeper', 'next', { cost: 1, due: 3, blockDelta: 7, ...hit(9) });
  expect(E, 'cover now broken keeper', 'now', { cost: 1, blockDelta: 5, ...hit(5) });
  expect(E, 'round next cog block', 'next', { cost: 1, due: 3, ...hit(6, 12) });
  expect(E, 'round now cog block', 'now', { cost: 1, ...hit(2, 8) });
  expect(E, 'double after cog block', 'after', { cost: 1, due: 4, ...hit(6, 12) });
  expect(E, 'timed next broken keeper', 'next', { cost: 2, due: 3, ...hit(18) });
  expect(X, 'scan next draw definition', 'next', { cost: 1, due: 2, hidden: true });
  expect(X, 'hunt after random definition', 'after', { cost: 1, due: 3, hidden: true });
  expect(X, 'last after rng hook', 'after', { cost: 3, due: 3, hidden: true });
  expect(X, 'round next visible control', 'next', { cost: 1, due: 2, ...hit(12) });
  expect(K, 'crossfire next marked branch', 'next', { cost: 2, due: 2, ...hit(30) });
  expect(K, 'crossfire now marked branch', 'now', { cost: 2, ...hit(21) });
  expect(K, 'timed next marked kill hook', 'next', { cost: 2, due: 2, hidden: true });
  expect(K, 'round now marked page control', 'now', { cost: 1, ...hit(8) });

  const reasons = (id, label) => recordOf(id, label).hiddenBy;
  assert.deepEqual(reasons(F, 'draw definition'), ['definition']);
  assert.deepEqual(reasons(F, 'random target definition'), ['definition', 'rng']);
  assert.deepEqual(reasons(F, 'nested draw definition'), ['definition']);
  assert.deepEqual(reasons(F, 'nested draw definition after owner change'), ['definition']);
  assert.deepEqual(reasons(F, 'nested draw reserved'), ['definition']);
  assert.deepEqual(reasons('hidden-hook-draw', 'third owner hook draw'), ['draw']);
  assert.deepEqual(reasons('hidden-hook-rng', 'exhaust hook random damage'), ['rng']);
  assert.deepEqual(reasons(X, 'scan next draw definition'), ['definition']);
  assert.deepEqual(reasons(X, 'hunt after random definition'), ['definition', 'rng']);
  assert.deepEqual(reasons(X, 'last after rng hook'), ['rng']);
  assert.deepEqual(reasons(K, 'timed next marked kill hook'), ['rng', 'draw']);
});

test('every forecast re-derives from independent public replays, matches the original HTML preview() when visible, and every real command reproduces its state', () => {
  htmlChecks.now = htmlChecks.reserved = 0;
  for (const row of fixture.cases.filter(c => c.group === 'battle')) {
    const live = MF.Battle.create(data, clone(row.setup)), requests = [];
    assert.deepEqual(clone(live.s), row.created.state, `${row.id} created`);
    for (const step of row.steps) {
      const fresh = () => {
        const battle = MF.Battle.create(data, clone(row.setup));
        for (const request of requests) battle.command(clone(request));
        return { battle, play: args => battle.command({ rid: null, v: battle.s.v, kind: 'play', args }) };
      };
      for (const record of step.forecasts) {
        assert.deepEqual(rederive(live, fresh, record.args), { expected: record.expected, hiddenBy: record.hiddenBy }, `${row.id}/${record.label}`);
        checkHtml(live, record, `${row.id}/${record.label}`);
      }
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
        checkHtml(live.battle, record, `run/${record.label}`);
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
  assert.ok(htmlChecks.now > 0 && htmlChecks.reserved > 0, 'the original HTML preview checked immediate and reserved forecasts');
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
    tampered(f => { f.counts.forecasts = 90; }),
    tampered(f => { f.cases[0].steps[1].forecasts[5].expected.enemyDamage += 1; }),
    tampered(f => { f.cases[9].steps[2].forecasts[0].expected.newlyBroken = 0; }),
    tampered(f => { f.cases[9].steps[5].forecasts[0].expected = { ...f.cases[9].steps[5].forecasts[0].expected, enemyHpLoss: 0, enemyDamage: 0, blockDelta: 0 }; }),
    tampered(f => { delete f.sources.htmlPreview; }),
    tampered(f => { f.schema = 'mf-battle-preview-v1'; }),
  ]) assert.throws(() => checkFixture(bad), /preview/i);
});

test('strict shared decoder: exact export round trip, corrupted containers and empty gzip prefix/suffix members rejected, previous output preserved', t => {
  const directory = mkdtempSync(join(tmpdir(), 'mf-battle-preview-'));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  const output = join(directory, 'preview.json.gz');
  const summary = exportVectors(output), bytes = readFileSync(output), text = Buffer.from(`${JSON.stringify(fixture)}\n`);
  assert.deepEqual([summary.forecasts, summary.commands, summary.bytes, summary.sha256], [91, 52, bytes.length, sha256(bytes)]);
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
  assert.deepEqual([saved.schema, saved.counts.forecasts, saved.counts.commands], ['mf-battle-preview-v2', 91, 52]);
});
