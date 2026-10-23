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

// RULES_NOW_PLAY (2026-10-19): every card is now-only. next/after requests are forecast as NOT_RESERVABLE rejections, the reservation-only
// cases became now-play cases and the reserved hidden-definition case was dropped (hidden-definition and hidden-hook-rng cover it).
test('original-JS regeneration keeps the v1 case order with now-play replacements and the appended now cases: 80 forecasts / 50 real commands', () => {
  assert.equal(fixture.schema, 'mf-battle-preview-v2');
  assert.equal(fixture.status, 'JS_ORACLE');
  assert.match(fixture.scope, /original HTML preview current-state reserved resolution/);
  assert.match(fixture.scope, /Unity view, device and server acceptance PENDING/);
  assert.deepEqual(fixture.cases.map(c => [c.id, c.group]), [['damage-block-targets', 'battle'], ['heal-now-only-empty-clock', 'battle'], ['reflect-defeat', 'battle'],
    ['overload-self-damage', 'battle'], ['hidden-definition', 'battle'], ['hidden-hook-draw', 'battle'], ['hidden-hook-rng', 'battle'], ['picks-recall', 'battle'], ['run-expedition', 'run'],
    ['now-estimates', 'battle'], ['now-hidden-draw-hook', 'battle'], ['held-retain-forecast', 'battle']]);
  assert.deepEqual(fixture.counts, { cases: 12, battleForecasts: 63, battleCommands: 36, runFixedForecasts: 6, runPolicyForecasts: 11, runCommands: 14, forecasts: 80, commands: 50 });
  const forecasts = fixture.cases.reduce((n, row) => n + row.steps.reduce((m, step) => m + step.forecasts.length, 0), 0);
  const commands = fixture.cases.reduce((n, row) => n + row.steps.filter(step => step.request).length, 0);
  assert.deepEqual([forecasts, commands], [80, 50]);
  // The first nine positions keep the v1 subjects (two reservation cases rewritten as now plays): 65 forecasts and 37 commands.
  const v1 = fixture.cases.slice(0, 9);
  assert.deepEqual([v1.reduce((n, row) => n + row.steps.reduce((m, step) => m + step.forecasts.length, 0), 0), v1.reduce((n, row) => n + row.steps.filter(step => step.request).length, 0)], [65, 37]);
  assert.deepEqual(fixture.coverage.errors, ['AP_LOW', 'BAD_TARGET', 'INVALID_MODE', 'NOT_IN_HAND', 'NOT_RESERVABLE', 'NO_BATTLE', 'OVERLOAD_WOULD_KILL', 'WRONG_PHASE']);
  assert.ok(fixture.reachability.notCovered.reservationEstimate.startsWith('UNREACHABLE'));
  const records = fixture.cases.flatMap(row => row.steps.flatMap(step => step.forecasts));
  assert.ok(records.every(r => r.args.mode === 'now' || r.expected.error !== null), 'no next/after request is accepted');
  assert.ok(records.filter(r => r.args.mode === 'next' || r.args.mode === 'after').some(r => r.expected.error === 'NOT_RESERVABLE'));
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
  // RULES_NOW_PLAY: no visible reserved estimate exists any more, so the one-point next/after witness is false on the real corpus.
  assert.equal(nextAfterDiffers(fixture.cases), false);
  // The witness logic itself still holds on synthetic records: a differing next/after pair at one point counts, the same pair split across points does not.
  const visible = (mode, loss) => ({ args: { uid: 'c1', mode, target: 'e1', picks: [] }, expected: { ...none, mode, enemyHpLoss: loss, enemyDamage: loss } });
  assert.equal(nextAfterDiffers([{ steps: [{ forecasts: [visible('next', 22), visible('after', 30)] }] }]), true);
  assert.equal(nextAfterDiffers([{ steps: [{ forecasts: [visible('next', 22)] }, { forecasts: [visible('after', 30)] }] }]), false);
  assert.equal(nextAfterDiffers([{ steps: [{ forecasts: [visible('next', 22), visible('after', 22)] }] }]), false);
  assert.ok(isDeepStrictEqual(generateCases(), fixture), 'regeneration is deterministic');
});

test('independent original-source literals: damage, block, heal, self-damage, reflect defeat, now-only rejections, breaks, held, picks and hidden reasons', () => {
  const D = 'damage-block-targets', H = 'heal-now-only-empty-clock', R = 'reflect-defeat', O = 'overload-self-damage', F = 'hidden-definition', P = 'picks-recall';
  // power4 160 / vit 81 / level 1: sera_round 8, harin_shield 8, sera_cover blk5+dmg4, harin_bash 6; e_cog gains block 6 on its first turn.
  expect(D, 'turn1 sera_round cog', 'now', { cost: 1, ...hit(8) });
  expect(D, 'turn1 harin_shield', 'now', { cost: 1, blockDelta: 8 });
  expect(D, 'turn2 sera_round cog block6', 'now', { cost: 1, ...hit(2, 8) });
  expect(D, 'turn2 sera_cover cog block6', 'now', { cost: 1, blockDelta: 5, ...hit(0, 4) });
  expect(D, 'turn2 harin_bash moth', 'now', { cost: 1, ...hit(6) });
  expect(D, 'turn2 sera_cover cog after hit', 'now', { cost: 1, blockDelta: 5, ...hit(4) });
  // RULES_NOW_PLAY: the removed reserved estimate is now a NOT_RESERVABLE rejection.
  expect(D, 'turn1 sera_round next', 'next', { error: 'NOT_RESERVABLE' });
  for (const [label, error, mode] of [['created phase', 'WRONG_PHASE', 'now'], ['turn1 missing target', 'BAD_TARGET', 'now'], ['turn1 invalid mode', 'INVALID_MODE', 'later'],
    ['turn1 unknown uid', 'NOT_IN_HAND', 'now'], ['turn2 played card', 'NOT_IN_HAND', 'now']]) expect(D, label, mode, { error });
  // aila_bell heals 5 at HP150/162; empty_clock -1 on the first card of the battle in now mode (sera_timed 2 -> 1, sera_round/aila_bell 1 -> 0),
  // full costs after the discounted sera_round (8) leaves the 12-HP page at 4; next/after are rejected for every card.
  expect(H, 'heal discounted', 'now', { cost: 0, hpDelta: 5 });
  expect(H, 'timed now discounted', 'now', { cost: 1, ...hit(12) });
  expect(H, 'round now discounted zero cost', 'now', { cost: 0, ...hit(8) });
  expect(H, 'timed next not reservable', 'next', { error: 'NOT_RESERVABLE' });
  expect(H, 'last after not reservable', 'after', { error: 'NOT_RESERVABLE' });
  expect(H, 'heal after clock', 'now', { cost: 1, hpDelta: 5 });
  expect(H, 'timed after clock', 'now', { cost: 2, ...hit(4) });
  expect(H, 'last after clock', 'now', { cost: 3, ...hit(4) });
  // e_mirrorclerk reflects 3 from its first turn; at HP3 the first reflected hit is lethal and stops the second hit.
  expect(R, 'turn1 no reflect yet', 'now', { cost: 1, ...hit(8) });
  expect(R, 'reflect lethal multi-hit', 'now', { cost: 1, ...hit(4), hpDelta: -3, defeat: true });
  expect(R, 'reflect lethal single hit', 'now', { cost: 1, ...hit(8), hpDelta: -3, defeat: true });
  expect(R, 'reflect reservation', 'next', { error: 'NOT_RESERVABLE' });
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
  expect(F, 'nested draw reserved', 'next', { error: 'NOT_RESERVABLE' });
  expect(F, 'ap low', 'now', { error: 'AP_LOW' });
  expect('hidden-hook-draw', 'third owner hook draw', 'now', { cost: 1, hidden: true });
  const reserve = recordOf('hidden-hook-draw', 'third owner reserve'), reserveCard = caseOf('hidden-hook-draw').created.state.cards[reserve.args.uid].id;
  expect('hidden-hook-draw', 'third owner reserve', 'next', { error: 'NOT_RESERVABLE' });
  assert.ok(['nia_wrench', 'nia_barrier'].includes(reserveCard));
  const control = recordOf('hidden-hook-draw', 'earlier owner control').expected;
  assert.ok(control.error === null && !control.hidden && control.cost === 1 && (control.enemyHpLoss > 0 || control.blockDelta > 0), 'an earlier owner stays visible');
  expect('hidden-hook-rng', 'exhaust hook random damage', 'now', { cost: 3, hidden: true });
  expect('hidden-hook-rng', 'harin_shield control', 'now', { cost: 1, blockDelta: 8 });
  expect('hidden-hook-rng', 'after hook harin_shield', 'now', { cost: 1, blockDelta: 8 });
  expect('hidden-hook-rng', 'after hook harin_bash page', 'now', { cost: 1, ...hit(6) });
  // sera_pull (cost 0) returns a discarded card to the hand: default/invalid picks take the highest-cost sera_timed, explicit picks choose; no forecast field changes.
  expect(P, 'pull default', 'now', {});
  expect(P, 'pull round pick', 'now', {});
  expect(P, 'pull timed pick', 'now', {});
  expect(P, 'pull invalid pick', 'now', {});
  for (const [label, mode] of [['map now', 'now'], ['map after with picks', 'after'], ['after battle', 'now']]) expect('run-expedition', label, mode, { error: 'NO_BATTLE' });
  expect('run-expedition', 'run unknown uid', 'now', { error: 'NOT_IN_HAND' });

  // Now cases: keeper 38 HP/brk4, cog 24 HP/brk4 (both volt-weak). round brk1 + double brk1x2 + cover brk1 break the keeper; Sera foresight +25% now applies
  // to every Sera attack on a broken enemy and multiplies with broken +25%: cover 4 -> 6 (it was 5 without foresight); timed 20 -> 31 is capped by the 18 HP left.
  const E = 'now-estimates', K = 'now-hidden-draw-hook', W = 'held-retain-forecast';
  expect(E, 'timed now keeper', 'now', { cost: 2, ...hit(20) });
  expect(E, 'aim now zero outcome', 'now', { cost: 1 });
  expect(E, 'cover now keeper', 'now', { cost: 1, blockDelta: 5, ...hit(4) });
  expect(E, 'timed now breaks keeper', 'now', { cost: 2, ...hit(20), newlyBroken: 1 });
  expect(E, 'cover now breaks keeper', 'now', { cost: 1, blockDelta: 5, ...hit(4), newlyBroken: 1 });
  expect(E, 'timed now ap low', 'now', { error: 'AP_LOW' });
  expect(E, 'timed now broken keeper', 'now', { cost: 2, ...hit(38 - 8 - 8 - 4) });
  expect(E, 'cover now broken keeper', 'now', { cost: 1, blockDelta: 5, ...hit(Math.floor(4 * 1.25 * 1.25)) });
  expect(E, 'round now cog block', 'now', { cost: 1, ...hit(2, 8) });
  expect(E, 'double now cog block', 'now', { cost: 1, ...hit(2, 8) });
  expect(K, 'crossfire now marked branch', 'now', { cost: 2, ...hit(21) });
  expect(K, 'timed now marked kill hook', 'now', { cost: 2, hidden: true });
  expect(K, 'round now marked page control', 'now', { cost: 1, ...hit(8) });
  // sera_timed brk3 does not break the brk4 keeper; kept over the turn end it is held and precision_lens adds +1 (break), damage unchanged.
  expect(W, 'timed unheld no lens', 'now', { cost: 2, ...hit(20) });
  expect(W, 'timed held lens breaks', 'now', { cost: 2, ...hit(20), newlyBroken: 1 });
  assert.deepEqual([caseOf(W).steps[1].after.state.cards.c1.held, caseOf(W).steps[2].after.state.cards.c1.held], [true, false]);

  const reasons = (id, label) => recordOf(id, label).hiddenBy;
  assert.deepEqual(reasons(F, 'draw definition'), ['definition']);
  assert.deepEqual(reasons(F, 'random target definition'), ['definition', 'rng']);
  assert.deepEqual(reasons(F, 'nested draw definition'), ['definition']);
  assert.deepEqual(reasons(F, 'nested draw definition after owner change'), ['definition']);
  assert.deepEqual(reasons('hidden-hook-draw', 'third owner hook draw'), ['draw']);
  assert.deepEqual(reasons('hidden-hook-rng', 'exhaust hook random damage'), ['rng']);
  assert.deepEqual(reasons(K, 'timed now marked kill hook'), ['rng']);
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
  // RULES_NOW_PLAY: no reserved forecast is visible any more, so the HTML reserved branch is never compared.
  assert.ok(htmlChecks.now > 0 && htmlChecks.reserved === 0, 'the original HTML preview checked immediate forecasts and no reserved one');
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
  assert.deepEqual([summary.forecasts, summary.commands, summary.bytes, summary.sha256], [80, 50, bytes.length, sha256(bytes)]);
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
  assert.deepEqual([saved.schema, saved.counts.forecasts, saved.counts.commands], ['mf-battle-preview-v2', 80, 50]);
});
