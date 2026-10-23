// P1-EXPEDITION-BATTLE-FORECAST / P1-RESERVED-FORECAST-CORE §14.2/§14.3/§5.4-5.6/§6/§16.2: independent JS oracle for the C#
// Battle.Forecast / Run.BattleForecast play forecast. Only the original public JS API supplies expectations: check() gives the precheck
// code; otherwise a fresh Battle.create (or Run.create, enter and battleCmd) replay of the recorded setup and real requests plays the
// request once and its before/after states are read. A next/after outcome follows the original HTML preview's reserved branch
// (prototype/index.html preview, source-pinned and statement-checked) on another fresh replay. The C# method is never consulted. Each C#
// case starts from the same setup and replays the same real commands; no state is restored.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { mkdtempSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { isDeepStrictEqual } from 'node:util';
import { gzipSync } from 'node:zlib';
import { stateHash } from './gen_traces.mjs';
import { decodeVectors } from './gen_run_create_vectors.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const require = createRequire(import.meta.url);
const MF = require('../engine/rules-core.js');
const data = JSON.parse(readFileSync(join(root, 'data/game_data.json'), 'utf8'));
const originalData = JSON.stringify(data);
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const schema = 'mf-battle-preview-v2';
const scope = 'Fresh Battle.create/command and Run.create/enter/battleCmd replays; each play forecast is the original JS check() code and one independent fresh-candidate play. Under RULES_NOW_PLAY (2026-10-19) every next/after request is rejected by check() with NOT_RESERVABLE, so the original HTML preview current-state reserved resolution is kept in the oracle but never reached; C# Battle.Forecast/Run.BattleForecast comparison only. Unity view, device and server acceptance PENDING';
export const fixturePath = join(root, 'engine/tests/vectors/battle_preview_vectors.json.gz');
const fields = ['error', 'mode', 'cost', 'due', 'hidden', 'enemyHpLoss', 'blockDelta', 'hpDelta', 'defeat', 'enemyDamage', 'newlyBroken'];
const contract = {
  request: 'A forecast reads the play request {uid, mode, target, picks} at the current state and never changes it',
  error: 'check() code, else the RuleError code of the real play, else a RuleError of the current-state reservation estimate (Run: battleCmd NO_BATTLE outside battle); every other field keeps its default',
  reservation: 'next/after: cost from the real play event and due from its reserve event; the outcome fields are the original HTML preview current-state estimate: a JSON copy of an independent fresh replay of the unpaid point, the card spliced out of the hand, phase RESOLVING, resolveCard(uid, {reserved: mode, target, picks: []}) with the target the real reservation stores (the HTML UI passes app.target || first alive; for authored cards these differ only for untargeted cards, whose effects never read the target). No payment, turn advance, enemy turn, draw or capture; never a promise of the future',
  hidden: 'tgt rand, pick random or draw anywhere in the effective if/then/else, or the resolution (now: the real play; next/after: the estimate) changed the RNG or the draw pile; every outcome field keeps its default. hiddenBy lists definition/rng/draw as oracle metadata only',
  outcome: 'enemyHpLoss sums max(0, hp before - hp after) per enemy; enemyDamage sums max(0, (hp+block) before - (hp+block) after) as the HTML preview dmg; newlyBroken counts enemies whose brokenUntil turned truthy from falsy as the HTML preview brokeN; blockDelta and hpDelta are signed party changes; defeat is phase DEFEAT',
  replay: 'Battle cases: fresh Battle.create(setup) and the recorded requests. Run case: fresh Run.create(setup), enter, then Run.battleCmd. Recorded states are comparison data only',
  transition: 'v1 -> v2: two appended fields (enemyDamage, newlyBroken) and current-state next/after estimates. RULES_NOW_PLAY (2026-10-19): every card is played now only; reservation-only cases became now-play cases (heal-now-only-empty-clock, picks-recall, now-estimates, now-hidden-draw-hook), the reserved hidden-definition case was dropped (covered by hidden-definition and hidden-hook-rng), and held-retain-forecast was appended',
};
const reachability = {
  notCovered: { definitionPickRandom: 'No authored card effect has pick "random" (only the Nia resonance battleStart tune, never a card play); the rule branch is kept for parity with the accepted practice view',
    reservationEstimate: 'UNREACHABLE under RULES_NOW_PLAY (2026-10-19): every authored card is now-only, so check() rejects next/after with NOT_RESERVABLE before any reserved estimate' },
  csharpOnly: { lateCommandRejection: 'Every authored play that passes check() commits; the C# consumer probes a local-definition fault instead',
    estimateRejection: 'No authored reservation estimate raises a RuleError; the C# consumer probes a local-definition fault that only the current-state resolution reaches' },
};
// The original HTML preview statements this oracle follows; generation stops if the pinned source no longer contains them verbatim.
const htmlSeam = ['const c = new MF.Battle(IX, JSON.parse(JSON.stringify(s)));',
  'const before = { hp: c.s.hp, block: c.s.block, e: Object.fromEntries(c.s.enemies.map((e) => [e.uid, e.hp + e.block])) };',
  "c.s.zones.hand.splice(c.s.zones.hand.indexOf(uid), 1); c.s.phase = 'RESOLVING'; c.resolveCard(uid, { reserved: mode, target, picks: [] });",
  'let dmg = 0; for (const e of c.s.enemies) { const was = before.e[e.uid]; if (was !== undefined) dmg += Math.max(0, was - (e.hp + e.block)); }',
  'const brokeN = c.s.enemies.filter((e) => e.brokenUntil && !s.enemies.find((x) => x.uid === e.uid).brokenUntil).length;'];
const runSetup = { party: [{ charId: 'sera' }, { charId: 'harin' }, { charId: 'nia' }], seed: 4242, length: 'short', memoryPolicy: 'v2', memory: {},
  relics: [], difficulty: { hp: 100, dmg: 100 }, zones: [1], runId: 'preview-run', memoryVersion: 0, useMemory: true };

function sources() {
  return Object.fromEntries(Object.entries({ data: 'data/game_data.json', engine: 'engine/rules-core.js', memory: 'proposals/memory_v2.cjs',
    canonical: 'tools/gen_traces.mjs', runCreateDecoder: 'tools/gen_run_create_vectors.mjs', htmlPreview: 'prototype/index.html',
    generator: 'tools/gen_battle_preview_vectors.mjs',
  }).map(([key, path]) => [key, { path, sha256: sha256(readFileSync(join(root, path))) }]));
}
const loadedSources = sources();

const blank = (mode, error) => ({ error, mode, cost: 0, due: null, hidden: false, enemyHpLoss: 0, blockDelta: 0, hpDelta: 0, defeat: false, enemyDamage: 0, newlyBroken: 0 });
// Unity MemoryForgeBattleClient.HiddenOutcome, the accepted practice-view rule, read from the original effective definition.
const hiddenOps = ops => (ops || []).some(op => op.tgt === 'rand' || op.pick === 'random' || op.op === 'draw' || hiddenOps(op.then) || hiddenOps(op.else));
const q = (label, uid, mode = 'now', target = null, picks = []) => ({ label, uid, mode, target, picks });
const playArgs = query => ({ uid: query.uid, mode: query.mode, target: query.target, picks: [...query.picks] });
const ruleCode = action => {
  try { action(); } catch (error) { if (error instanceof MF.RuleError) return error.code; throw error; }
  return null;
};
const enemy = (battle, id) => battle.s.enemies.find(e => e.id === id).uid;
const inHand = (battle, owners) => {
  const uid = battle.s.zones.hand.find(card => owners.includes(battle.s.cards[card].owner));
  assert.ok(uid, `No ${owners.join('/')} card in hand`);
  return uid;
};
const point = battle => { const state = clone(battle.s); return { state, stateHash: stateHash(state) }; };
const lost = (before, after) => before.enemies.reduce((sum, e) => sum + Math.max(0, e.hp - (after.enemies.find(x => x.uid === e.uid)?.hp ?? 0)), 0);
// The visible outcome fields; enemyDamage and newlyBroken are the HTML preview's own dmg and brokeN expressions.
function outcome(before, after) {
  const was = Object.fromEntries(before.enemies.map(e => [e.uid, e.hp + e.block]));
  let enemyDamage = 0;
  for (const e of after.enemies) if (was[e.uid] !== undefined) enemyDamage += Math.max(0, was[e.uid] - (e.hp + e.block));
  const newlyBroken = after.enemies.filter(e => e.brokenUntil && !before.enemies.find(x => x.uid === e.uid).brokenUntil).length;
  return { enemyHpLoss: lost(before, after), blockDelta: after.block - before.block, hpDelta: after.hp - before.hp, defeat: after.phase === 'DEFEAT', enemyDamage, newlyBroken };
}
// The original HTML preview's reserved branch on an independent fresh replay `base` of the unpaid point; returns the estimate state.
function reservedEstimate(base, uid, mode, target) {
  const c = new MF.Battle(data, JSON.parse(JSON.stringify(base.s)));
  c.s.zones.hand.splice(c.s.zones.hand.indexOf(uid), 1); c.s.phase = 'RESOLVING'; c.resolveCard(uid, { reserved: mode, target, picks: [] });
  return c.s;
}

// live: the battle at the forecast point (pure check/effDef only). fresh(): an independent public replay {battle, play(args)} of that point.
function expectForecast(live, fresh, query) {
  const state = clone(live.s), receipts = clone(live.receipts);
  const pure = () => assert.ok(isDeepStrictEqual(live.s, state) && isDeepStrictEqual(live.receipts, receipts), 'JS precheck/definition query changed the live battle');
  const code = live.check(query.uid, query.mode, query.target, clone(query.picks)); pure();
  if (code !== null) return { expected: blank(query.mode, code), hiddenBy: [] };
  const definition = hiddenOps(live.effDef(live.s.cards[query.uid]).eff); pure();
  const candidate = fresh();
  assert.ok(isDeepStrictEqual(candidate.battle.s, live.s), 'Independent candidate replay differs from the live battle');
  const before = clone(candidate.battle.s);
  let response;
  try { response = clone(candidate.play(playArgs(query))); }
  catch (error) { if (error instanceof MF.RuleError) return { expected: blank(query.mode, error.code), hiddenBy: [] }; throw error; }
  let after = candidate.battle.s;
  const expected = blank(query.mode, null);
  expected.cost = response.ev.find(e => e.e === 'play').cost;
  if (query.mode !== 'now') {
    expected.due = response.ev.find(e => e.e === 'reserve' && e.uid === query.uid).due;
    const stored = after.res.find(r => r.uid === query.uid).target, base = fresh().battle;
    assert.ok(isDeepStrictEqual(base.s, live.s), 'Independent estimate replay differs from the live battle');
    try { after = reservedEstimate(base, query.uid, query.mode, stored); }
    catch (error) { if (error instanceof MF.RuleError) return { expected: blank(query.mode, error.code), hiddenBy: [] }; throw error; }
    assert.ok(isDeepStrictEqual(base.s, before), 'The reserved estimate changed its replay instead of its own copy');
  }
  const hiddenBy = [...(definition ? ['definition'] : []), ...(isDeepStrictEqual(before.rng, after.rng) ? [] : ['rng']),
    ...(isDeepStrictEqual(before.zones.draw, after.zones.draw) ? [] : ['draw'])];
  if (hiddenBy.length) { expected.hidden = true; return { expected, hiddenBy }; }
  Object.assign(expected, outcome(before, after));
  return { expected, hiddenBy };
}
// Determinism sanity: when a step's real play is one of its forecast requests, the live result must equal that forecast. A reservation's
// real play schedules only (cost and due); its outcome fields are a current-state estimate, not the play's result.
function assertRealPlay(forecasts, request, before, after, response, label) {
  const twin = forecasts.find(f => isDeepStrictEqual(f.args, request.args));
  if (request.kind !== 'play' || !twin || twin.expected.error !== null) return;
  assert.equal(response.ev.find(e => e.e === 'play').cost, twin.expected.cost, `${label}: real play cost differs from its forecast`);
  if (twin.expected.mode !== 'now') { assert.equal(response.ev.find(e => e.e === 'reserve').due, twin.expected.due, `${label}: real due differs`); return; }
  if (twin.expected.hidden) return;
  const { error, mode, cost, due, hidden, ...shown } = twin.expected;
  assert.deepEqual(outcome(before, after), shown, `${label}: real outcome differs from its forecast`);
}

function freshBattle(setup, requests) {
  const battle = MF.Battle.create(data, clone(setup));
  for (const request of requests) battle.command(clone(request));
  return { battle, play: args => battle.command({ rid: null, v: battle.s.v, kind: 'play', args }) };
}
// plan: [{forecasts?: battle => [q(...)], request?: battle => {kind, args}}], evaluated in order on the live battle.
function battleCase(id, role, setup, plan) {
  const live = MF.Battle.create(data, clone(setup)), requests = [], steps = [];
  const created = point(live);
  plan.forEach((step, index) => {
    const forecasts = (step.forecasts ? step.forecasts(live) : []).map(query => {
      const { expected, hiddenBy } = expectForecast(live, () => freshBattle(setup, requests), query);
      return { label: query.label, args: playArgs(query), expected, hiddenBy };
    });
    const row = { forecasts, request: null };
    if (step.request) {
      const { kind, args } = step.request(live);
      const request = { rid: `${id}-s${index}-${kind}`, v: live.s.v, kind, args: clone(args) };
      const before = clone(live.s), response = clone(live.command(clone(request)));
      assert.equal(response.v, request.v + 1, `${id} step ${index}: the real ${kind} did not commit`);
      assertRealPlay(forecasts, request, before, live.s, response, `${id} step ${index}`);
      requests.push(request);
      Object.assign(row, { request, response, after: point(live) });
    }
    steps.push(row);
  });
  return { id, group: 'battle', role, setup: clone(setup), created, steps };
}

function applyRun(run, request) {
  if (request.scope === 'run') { run.enter(request.args.node); return null; }
  return run.battleCmd(request.kind, clone(request.args));
}
function runPoint(run) {
  const battle = run.s.battle ? clone(run.s.battle) : null;
  return { phase: run.s.phase, battle, battleHash: battle ? stateHash(battle) : null };
}
// Actual expedition: forecasts before entering, on the Run's own battle (fixed probes, then one forecast before every policy play) and after it ends.
function runCase() {
  const id = 'run-expedition', setup = clone(runSetup), history = [], steps = [];
  const live = MF.Run.create(data, clone(setup));
  const freshRun = () => { const run = MF.Run.create(data, clone(setup)); for (const request of history) applyRun(run, request); return run; };
  const created = runPoint(live);
  const outside = query => {
    assert.equal(live.battle, null, `${id}: outside-battle forecast point has a live battle`);
    const code = ruleCode(() => freshRun().battleCmd('play', playArgs(query)));
    assert.equal(code, 'NO_BATTLE', `${id}: the JS Run must reject a battle command outside battle with NO_BATTLE`);
    return { label: query.label, args: playArgs(query), expected: blank(query.mode, code), hiddenBy: [] };
  };
  const inside = query => {
    const fresh = () => { const run = freshRun(), battle = run.battle; return { battle, play: args => run.battleCmd('play', args) }; };
    const { expected, hiddenBy } = expectForecast(live.battle, fresh, query);
    return { label: query.label, args: playArgs(query), expected, hiddenBy };
  };
  const record = (forecasts, request) => {
    const battle = live.battle, before = battle ? clone(battle.s) : null;
    const response = applyRun(live, request);
    if (battle) assertRealPlay(forecasts, request, before, battle.s, response, `${id} step ${steps.length}`);
    history.push(clone(request));
    steps.push({ forecasts, request: clone(request), response: response === null ? null : clone(response), after: runPoint(live) });
  };
  assert.equal(created.phase, 'MAP', `${id}: a new Run must start on the map`);
  record([outside(q('map now', 'c1')), outside(q('map after with picks', 'c1', 'after', 'e1', ['c2']))],
    { scope: 'run', kind: 'enter', args: { node: live.available()[0] } });
  assert.ok(live.battle && live.battle.s.phase === 'PLAYER', `${id}: the first node did not open a player turn`);
  for (let index = 0, plays = 0; live.battle; index++) {
    assert.ok(index < 400, `${id}: the policy battle did not finish`);
    const battle = live.battle, target = battle.alive()[0]?.uid ?? null, queries = [];
    if (index === 0) {
      const first = battle.s.zones.hand[0];
      queries.push(q('run first hand now', first, 'now', target), q('run first hand next', first, 'next', target), q('run unknown uid', 'c999', 'now', target));
    }
    const uid = battle.s.zones.hand.find(card => battle.check(card, 'now', target, []) === null);
    if (uid) queries.push(q(`run policy play ${plays++}`, uid, 'now', target));
    record(queries.map(inside), uid ? { scope: 'battle', kind: 'play', args: { uid, mode: 'now', target, picks: [] } } : { scope: 'battle', kind: 'end', args: {} });
  }
  assert.ok(['REWARD', 'DEFEAT'].includes(live.s.phase), `${id}: the battle ended outside REWARD/DEFEAT`);
  steps.push({ forecasts: [outside(q('after battle', 'c1'))], request: null });
  return { id, group: 'run', role: 'Run-owned private battle: NO_BATTLE outside battle, live forecasts without Run mutation, the next real Run command executing once',
    setup, created, steps };
}

// v2 next/after witness, bounded to one forecast point: within one step of one case, a visible next and a visible after forecast of the same
// uid, target and picks whose estimates differ. Same uids recur across steps whose states differ, so pairs are never matched across points.
const estimateOutcome = e => [e.enemyHpLoss, e.enemyDamage, e.blockDelta, e.hpDelta, e.defeat, e.newlyBroken].join();
export function nextAfterDiffers(cases) {
  return cases.some(row => row.steps.some(step => {
    const shown = step.forecasts.filter(f => f.expected.error === null && !f.expected.hidden);
    return shown.some(a => a.args.mode === 'next' && shown.some(b => b.args.mode === 'after' && b.args.uid === a.args.uid && b.args.target === a.args.target
      && isDeepStrictEqual(b.args.picks, a.args.picks) && estimateOutcome(b.expected) !== estimateOutcome(a.expected)));
  }));
}

const begin = () => ({ kind: 'begin', args: {} });
const end = () => ({ kind: 'end', args: {} });
const play = (uid, mode = 'now', target = null, picks = []) => ({ kind: 'play', args: playArgs(q('', uid, mode, target, picks)) });

export function generateCases() {
  assert.deepEqual(sources(), loadedSources, 'Preview sources changed after module load; rerun exporter');
  assert.equal(JSON.stringify(data), originalData, 'Original preview source data changed');
  const html = readFileSync(join(root, 'prototype/index.html'), 'utf8');
  for (const statement of htmlSeam) assert.ok(html.includes(statement), `The pinned HTML preview no longer contains: ${statement}`);
  const cases = [
    battleCase('damage-block-targets', 'Exact card damage and block, enemy block absorbing damage, two-enemy targeting and invalid requests',
      { party: [{ charId: 'sera' }, { charId: 'harin' }], deck: [{ id: 'sera_round' }, { id: 'sera_cover' }, { id: 'harin_shield' }, { id: 'harin_bash' }],
        hp: 162, maxHp: 162, seed: 11, enemies: ['e_cog', 'e_moth'], relics: [] }, [
        { forecasts: b => [q('created phase', 'c1', 'now', enemy(b, 'e_cog'))], request: begin },
        { forecasts: b => [q('turn1 missing target', 'c1'), q('turn1 sera_round cog', 'c1', 'now', enemy(b, 'e_cog')), q('turn1 harin_shield', 'c3'),
          q('turn1 invalid mode', 'c1', 'later', enemy(b, 'e_cog')), q('turn1 unknown uid', 'c99', 'now', enemy(b, 'e_cog')),
          q('turn1 sera_round next', 'c1', 'next', enemy(b, 'e_cog'))], request: end },
        { forecasts: b => [q('turn2 sera_round cog block6', 'c1', 'now', enemy(b, 'e_cog')), q('turn2 sera_cover cog block6', 'c2', 'now', enemy(b, 'e_cog')),
          q('turn2 harin_bash moth', 'c4', 'now', enemy(b, 'e_moth'))], request: b => play('c1', 'now', enemy(b, 'e_cog')) },
        { forecasts: b => [q('turn2 played card', 'c1', 'now', enemy(b, 'e_cog')), q('turn2 sera_cover cog after hit', 'c2', 'now', enemy(b, 'e_cog'))] },
      ]),
    battleCase('heal-now-only-empty-clock', 'Healing, now-only modes (next/after rejected), the empty_clock discount on the first card of the battle in now mode, then full costs',
      { party: [{ charId: 'aila' }, { charId: 'sera' }], deck: [{ id: 'aila_bell' }, { id: 'sera_timed' }, { id: 'sera_last' }, { id: 'sera_round' }],
        hp: 150, maxHp: 162, seed: 12, enemies: ['e_page'], relics: ['empty_clock'] }, [
        { request: begin },
        { forecasts: b => [q('heal discounted', 'c1'), q('timed now discounted', 'c2', 'now', enemy(b, 'e_page')), q('timed next not reservable', 'c2', 'next', enemy(b, 'e_page')),
          q('last after not reservable', 'c3', 'after', enemy(b, 'e_page')), q('round now discounted zero cost', 'c4', 'now', enemy(b, 'e_page'))],
          request: b => play('c4', 'now', enemy(b, 'e_page')) },
        { forecasts: b => [q('heal after clock', 'c1'), q('timed after clock', 'c2', 'now', enemy(b, 'e_page')), q('last after clock', 'c3', 'now', enemy(b, 'e_page'))] },
      ]),
    battleCase('reflect-defeat', 'A reflected hit ends the battle in defeat and stops later hits; the terminal phase rejects further plays',
      { party: [{ charId: 'sera' }], deck: [{ id: 'sera_double' }, { id: 'sera_round' }], hp: 3, maxHp: 81, seed: 13, enemies: ['e_mirrorclerk'], relics: [] }, [
        { request: begin },
        { forecasts: () => [q('turn1 no reflect yet', 'c1')], request: end },
        { forecasts: () => [q('reflect lethal multi-hit', 'c1'), q('reflect lethal single hit', 'c2'), q('reflect reservation', 'c1', 'next')], request: () => play('c1') },
        { forecasts: () => [q('after defeat', 'c2')] },
      ]),
    battleCase('overload-self-damage', 'Self-damage with the low-HP Lumi damage passive and the overload-would-kill rejection',
      { party: [{ charId: 'lumi' }], deck: [{ id: 'lumi_thunderstep' }, { id: 'lumi_overdrive' }, { id: 'lumi_jab' }], hp: 5, maxHp: 81, seed: 14, enemies: ['e_keeper'], relics: [] }, [
        { request: begin },
        { forecasts: () => [q('selfdmg critical damage', 'c1'), q('overload would kill', 'c2'), q('critical jab', 'c3')], request: () => play('c1') },
        { forecasts: () => [q('hp1 critical jab', 'c3'), q('hp1 overload would kill', 'c2')] },
      ]),
    battleCase('hidden-definition', 'Draw, random-target and nested if/then draw definitions hide numbers while a plain attack stays visible; AP shortage',
      { party: [{ charId: 'sion' }, { charId: 'lumi' }, { charId: 'rien' }], deck: [{ id: 'sion_erase' }, { id: 'lumi_storm' }, { id: 'rien_rest' }, { id: 'sion_cut' }],
        hp: 243, maxHp: 243, seed: 15, enemies: ['e_cog', 'e_page'], relics: [] }, [
        { request: begin },
        { forecasts: b => [q('draw definition', 'c1'), q('random target definition', 'c2'), q('nested draw definition', 'c3'),
          q('visible control', 'c4', 'now', enemy(b, 'e_page')), q('nested draw reserved', 'c3', 'next')], request: b => play('c4', 'now', enemy(b, 'e_page')) },
        { forecasts: () => [q('nested draw definition after owner change', 'c3')], request: () => play('c3') },
        { forecasts: () => [q('ap low', 'c2')] },
      ]),
    battleCase('hidden-hook-draw', 'unfinished_score draws for the third owner: a draw-pile change without RNG hides a non-random card; an earlier owner stays visible',
      { party: [{ charId: 'sera' }, { charId: 'harin' }, { charId: 'nia' }],
        deck: [{ id: 'sera_round' }, { id: 'sera_cover' }, { id: 'harin_shield' }, { id: 'harin_bash' }, { id: 'nia_wrench' }, { id: 'nia_barrier' }],
        hp: 243, maxHp: 243, seed: 16, enemies: ['e_scribe'], relics: ['unfinished_score'] }, [
        { request: begin },
        { request: b => play(inHand(b, ['sera']), 'now', enemy(b, 'e_scribe')) },
        { request: b => play(inHand(b, ['harin']), 'now', enemy(b, 'e_scribe')) },
        { forecasts: b => [q('third owner hook draw', inHand(b, ['nia']), 'now', enemy(b, 'e_scribe')),
          q('earlier owner control', inHand(b, ['sera', 'harin']), 'now', enemy(b, 'e_scribe')), q('third owner reserve', inHand(b, ['nia']), 'next', enemy(b, 'e_scribe'))],
          request: b => play(inHand(b, ['nia']), 'now', enemy(b, 'e_scribe')) },
      ]),
    battleCase('hidden-hook-rng', 'void_shard random damage on exhaust: an RNG change without a draw hides a visible block card',
      { party: [{ charId: 'yuri' }, { charId: 'harin' }], deck: [{ id: 'yuri_worldtree' }, { id: 'harin_shield' }, { id: 'harin_bash' }],
        hp: 162, maxHp: 162, seed: 17, enemies: ['e_cog', 'e_page'], relics: ['void_shard'] }, [
        { request: begin },
        { forecasts: () => [q('exhaust hook random damage', 'c1'), q('harin_shield control', 'c2')], request: () => play('c1') },
        { forecasts: b => [q('after hook harin_shield', 'c2'), q('after hook harin_bash page', 'c3', 'now', enemy(b, 'e_page'))] },
      ]),
    battleCase('picks-recall', 'Caller picks choose which discarded card sera_pull returns to the hand; the hand change is not a forecast field, so every pick forecasts a zero outcome',
      { party: [{ charId: 'sera' }], deck: [{ id: 'sera_round' }, { id: 'sera_timed' }, { id: 'sera_pull' }], hp: 81, maxHp: 81, seed: 18, enemies: ['e_keeper'], relics: [] }, [
        { request: begin },
        { request: b => play('c1', 'now', enemy(b, 'e_keeper')) },
        { request: b => play('c2', 'now', enemy(b, 'e_keeper')) },
        { forecasts: () => [q('pull default', 'c3'), q('pull round pick', 'c3', 'now', null, ['c1']), q('pull timed pick', 'c3', 'now', null, ['c2']),
          q('pull invalid pick', 'c3', 'now', null, ['c99'])], request: () => play('c3', 'now', null, ['c1']) },
      ]),
    runCase(),
    // v2 cases appended after the expedition; RULES_NOW_PLAY turned the reserved estimates into now plays.
    battleCase('now-estimates', 'Now plays: a newly broken enemy, the broken bonus with Sera foresight (all Sera attacks) on an already broken enemy, enemy block absorbing damage, a zero outcome and an AP rejection',
      { party: [{ charId: 'sera' }], deck: [{ id: 'sera_round' }, { id: 'sera_double' }, { id: 'sera_cover' }, { id: 'sera_timed' }, { id: 'sera_aim' }],
        hp: 81, maxHp: 81, seed: 19, enemies: ['e_keeper', 'e_cog'], relics: [] }, [
        { request: begin },
        { forecasts: b => [q('timed now keeper', 'c4', 'now', enemy(b, 'e_keeper')), q('aim now zero outcome', 'c5'), q('cover now keeper', 'c3', 'now', enemy(b, 'e_keeper'))],
          request: b => play('c1', 'now', enemy(b, 'e_keeper')) },
        { forecasts: b => [q('timed now breaks keeper', 'c4', 'now', enemy(b, 'e_keeper'))], request: b => play('c2', 'now', enemy(b, 'e_keeper')) },
        { forecasts: b => [q('cover now breaks keeper', 'c3', 'now', enemy(b, 'e_keeper'))], request: b => play('c3', 'now', enemy(b, 'e_keeper')) },
        { forecasts: b => [q('timed now ap low', 'c4', 'now', enemy(b, 'e_keeper'))], request: end },
        { forecasts: b => [q('timed now broken keeper', 'c4', 'now', enemy(b, 'e_keeper')), q('cover now broken keeper', 'c3', 'now', enemy(b, 'e_keeper')),
          q('round now cog block', 'c1', 'now', enemy(b, 'e_cog')), q('double now cog block', 'c2', 'now', enemy(b, 'e_cog'))],
          request: b => play('c4', 'now', enemy(b, 'e_keeper')) },
      ]),
    battleCase('now-hidden-draw-hook', 'A now kill of a marked enemy draws through detective_notebook and stays hidden; the marked conditional branch and a plain control stay visible',
      { party: [{ charId: 'kaya' }, { charId: 'sera' }], deck: [{ id: 'kaya_clue' }, { id: 'kaya_clue' }, { id: 'kaya_crossfire' }, { id: 'sera_timed' }, { id: 'sera_round' }],
        hp: 162, maxHp: 162, seed: 21, enemies: ['e_page', 'e_keeper'], relics: ['detective_notebook'] }, [
        { request: begin },
        { request: b => play('c1', 'now', enemy(b, 'e_page')) },
        { request: b => play('c2', 'now', enemy(b, 'e_keeper')) },
        { forecasts: b => [q('crossfire now marked branch', 'c3', 'now', enemy(b, 'e_keeper')), q('timed now marked kill hook', 'c4', 'now', enemy(b, 'e_page')),
          q('round now marked page control', 'c5', 'now', enemy(b, 'e_page'))],
          request: b => play('c4', 'now', enemy(b, 'e_page')) },
      ]),
    battleCase('held-retain-forecast', 'A 보존 card kept over a real turn end is held, and its forecast includes the held-only precision_lens break +1',
      { party: [{ charId: 'sera' }], deck: [{ id: 'sera_timed' }, { id: 'sera_round' }], hp: 81, maxHp: 81, seed: 22, enemies: ['e_keeper'], relics: ['precision_lens'] }, [
        { request: begin },
        { forecasts: b => [q('timed unheld no lens', 'c1', 'now', enemy(b, 'e_keeper'))], request: end },
        { forecasts: b => [q('timed held lens breaks', 'c1', 'now', enemy(b, 'e_keeper'))], request: b => play('c1', 'now', enemy(b, 'e_keeper')) },
      ]),
  ];
  assert.equal(JSON.stringify(data), originalData, 'The preview oracle changed original source data');
  assert.equal(new Set(cases.map(c => c.id)).size, cases.length, 'Duplicate preview case id');
  for (const row of cases) {
    const labels = row.steps.flatMap(step => step.forecasts.map(f => f.label));
    assert.equal(new Set(labels).size, labels.length, `${row.id}: duplicate preview forecast label`);
  }
  const records = cases.flatMap(row => row.steps.flatMap(step => step.forecasts));
  const errors = [...new Set(records.map(r => r.expected.error).filter(code => code !== null))].sort();
  const hidden = [...new Set(records.filter(r => r.expected.hidden).map(r => r.hiddenBy.join('+')))].sort();
  assert.deepEqual(errors, ['AP_LOW', 'BAD_TARGET', 'INVALID_MODE', 'NOT_IN_HAND', 'NOT_RESERVABLE', 'NO_BATTLE', 'OVERLOAD_WOULD_KILL', 'WRONG_PHASE'],
    'Preview error-code coverage changed');
  for (const reason of ['definition', 'rng', 'draw']) assert.ok(hidden.includes(reason), `Preview lacks a forecast hidden only by ${reason}`);
  assert.ok(records.some(r => r.expected.defeat) && records.some(r => r.expected.hpDelta > 0) && records.some(r => r.expected.hpDelta < 0)
    && records.some(r => r.expected.blockDelta > 0) && records.some(r => r.expected.enemyHpLoss > 0), 'Preview lacks a visible outcome kind');
  // RULES_NOW_PLAY: next/after requests are still forecast, and inside a battle every one is rejected before any reserved estimate.
  for (const mode of ['next', 'after']) assert.ok(records.some(r => r.expected.mode === mode && r.expected.error === 'NOT_RESERVABLE'), `Preview lacks a rejected ${mode} request`);
  assert.ok(records.every(r => r.expected.mode === 'now' || ['NOT_RESERVABLE', 'NO_BATTLE', 'INVALID_MODE'].includes(r.expected.error)), 'A reservation forecast was accepted');
  assert.ok(!nextAfterDiffers(cases), 'A reserved estimate became reachable');
  const now = records.filter(r => r.expected.mode === 'now' && r.expected.error === null), visible = now.filter(r => !r.expected.hidden);
  const numbers = e => e.enemyHpLoss || e.blockDelta || e.hpDelta || e.defeat || e.enemyDamage || e.newlyBroken;
  const held = cases.find(c => c.id === 'held-retain-forecast').steps, [unheld] = held[1].forecasts, [kept] = held[2].forecasts;
  for (const [ok, what] of [
    [visible.some(r => numbers(r.expected)), 'a visible now estimate'], [visible.some(r => !numbers(r.expected)), 'a zero now outcome'],
    [visible.some(r => r.expected.newlyBroken > 0), 'a now play breaking an enemy'], [visible.some(r => r.expected.defeat), 'a now play ending in defeat'],
    [visible.some(r => r.expected.enemyDamage > r.expected.enemyHpLoss), 'immediate damage absorbed by enemy block'],
    [now.some(r => r.expected.hidden && r.hiddenBy.includes('definition')), 'a now forecast hidden by its definition'],
    [now.some(r => r.expected.hidden && !r.hiddenBy.includes('definition')), 'a now forecast hidden only by a hook'],
    [unheld.expected.newlyBroken === 0 && kept.expected.newlyBroken === 1 && unheld.expected.enemyHpLoss === kept.expected.enemyHpLoss, 'a held forecast whose precision_lens break differs from the unheld one'],
  ]) assert.ok(ok, `Preview lacks ${what}`);
  for (const r of records) assert.ok(r.expected.enemyDamage >= r.expected.enemyHpLoss, `${r.label}: enemy damage below enemy HP loss`);
  assert.ok(!data.cards.some(card => JSON.stringify([card.eff, card.plus ?? null]).includes('"pick":"random"')), 'An authored pick:"random" card now exists; cover it');
  const battles = cases.filter(row => row.group === 'battle'), run = cases.find(row => row.group === 'run');
  const forecastCount = list => list.reduce((n, row) => n + row.steps.reduce((m, step) => m + step.forecasts.length, 0), 0);
  const commandCount = list => list.reduce((n, row) => n + row.steps.filter(step => step.request).length, 0);
  const runPolicyForecasts = run.steps.flatMap(step => step.forecasts).filter(f => f.label.startsWith('run policy play ')).length;
  const counts = { cases: cases.length, battleForecasts: forecastCount(battles), battleCommands: commandCount(battles),
    runFixedForecasts: forecastCount([run]) - runPolicyForecasts, runPolicyForecasts, runCommands: commandCount([run]),
    forecasts: forecastCount(cases), commands: commandCount(cases) };
  // Now-play corpus: 8 battle cases 48 forecasts/23 commands + now-estimates 10/6, now-hidden-draw-hook 3/4, held-retain-forecast 2/3.
  assert.deepEqual([counts.cases, counts.battleForecasts, counts.battleCommands, counts.runFixedForecasts], [12, 63, 36, 6], 'Preview fixed denominators changed');
  assert.ok(counts.runPolicyForecasts >= 1 && counts.runCommands > counts.runPolicyForecasts, 'Run policy did not play and finish its battle');
  assert.deepEqual(sources(), loadedSources, 'Preview sources changed during generation; rerun exporter');
  return { schema, scope, status: 'JS_ORACLE', dataVersion: data.meta.version, sources: clone(loadedSources), fields: [...fields], contract: clone(contract),
    reachability: clone(reachability), coverage: { errors, hidden }, counts, cases };
}

export function checkFixture(fixture) {
  assert.equal(fixture.schema, schema, 'Wrong preview fixture schema');
  assert.ok(Array.isArray(fixture.cases), 'Preview fixture requires cases');
  assert.equal(new Set(fixture.cases.map(c => c.id)).size, fixture.cases.length, 'Duplicate preview case id');
  assert.deepEqual(fixture.sources, loadedSources, 'Preview source provenance is stale');
  const expected = generateCases();
  assert.deepEqual(fixture.counts, expected.counts, 'Preview case/forecast/command counts differ');
  assert.ok(isDeepStrictEqual(fixture, expected), 'Preview fixture forecasts, states, requests or cases differ');
  return fixture.counts;
}

export function exportVectors(output = fixturePath) {
  const fixture = generateCases(), text = JSON.stringify(fixture), json = Buffer.from(`${text}\n`);
  const bytes = output.endsWith('.gz') ? gzipSync(json, { level: 9 }) : json;
  assert.equal(JSON.stringify(decodeVectors(bytes)), text, 'Exported preview fixture fails the strict reader; previous output preserved');
  const temporary = mkdtempSync(join(dirname(output), '.battle-preview-'));
  try {
    const path = join(temporary, 'fixture');
    writeFileSync(path, bytes);
    assert.deepEqual(sources(), fixture.sources, 'Preview sources changed before export; previous fixture preserved');
    renameSync(path, output);
  } finally { rmSync(temporary, { recursive: true, force: true }); }
  return { ...fixture.counts, bytes: bytes.length, uncompressedBytes: json.length, sha256: sha256(bytes), sources: fixture.sources };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  if (args.length > 1 || (args.length && !['--check', '--help'].includes(args[0]))) throw new Error('Usage: node tools/gen_battle_preview_vectors.mjs [--check|--help]');
  if (args[0] === '--help') console.log('Export the source-pinned v2 battle play forecast oracle (fresh public JS replays of bounded authored battles and one actual expedition; next/after outcomes follow the original HTML preview current-state reserved resolution), or --check the lossless gzip fixture.');
  else if (args[0] === '--check') console.log(JSON.stringify({ checked: fixturePath, ...checkFixture(decodeVectors(readFileSync(fixturePath))) }));
  else console.log(JSON.stringify({ output: fixturePath, ...exportVectors() }, null, 2));
}
