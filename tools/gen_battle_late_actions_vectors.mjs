// P1-CSHARP-LATE-ENEMY-ACTIONS §5.3–5.7/§10.4–10.6/§16.2: original JS oracle for apDrain/sealSlot/delayRes/silence/eatName/phase.
// Fresh Battle.create plus real begin/play/ult/end commands on the unchanged candidate overlay (proposals/late_enemies.cjs) and on
// fixed test-local boundary enemies. Recorded states are comparison data only. Live content and Run/Unity activation are NOT approved.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { mkdtempSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { isDeepStrictEqual } from 'node:util';
import { gzipSync, gunzipSync } from 'node:zlib';
import { stateHash } from './gen_traces.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const require = createRequire(import.meta.url);
const MF = require('../engine/rules-core.js');
const candidates = require('../proposals/late_enemies.cjs');
const data = JSON.parse(readFileSync(join(root, 'data/game_data.json'), 'utf8'));
const originalData = JSON.stringify(data);
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const schema = 'mf-battle-late-actions-v1';
const status = 'JS_ORACLE_CANDIDATE_ONLY';
const scope = 'Fresh Battle.create and real begin/play/ult/end commands for apDrain/sealSlot/delayRes/silence/eatName/phase on the unchanged candidate overlay and fixed test-local boundary enemies; live content, Run/Unity activation, balance and server acceptance NOT_APPROVED';
export const behaviors = ['apDrain', 'sealSlot', 'delayRes', 'silence', 'eatName', 'phase'];
export const fixturePath = join(root, 'engine/tests/vectors/battle_late_actions_vectors.json.gz');

function sources() {
  return Object.fromEntries(Object.entries({
    data: 'data/game_data.json', engine: 'engine/rules-core.js', memory: 'proposals/memory_v2.cjs',
    canonical: 'tools/gen_traces.mjs', proposal: 'proposals/late_enemies.cjs', generator: 'tools/gen_battle_late_actions_vectors.mjs',
  }).map(([key, path]) => [key, { path, sha256: sha256(readFileSync(join(root, path))) }]));
}
const loadedSources = sources();

// Test-local boundary definitions, never candidate content: each one isolates one original rule or rejection.
const testEnemy = (id, extra = {}) => ({ id, name: '시험 적', tier: 'normal', hp: 200, brk: 20, weak: ['frost'], pattern: [{ t: 'charge' }], ...extra });
const testPhase = (extra = {}) => ({ t: 'phase', hp: 30, brk: 20, weak: ['frost'], pattern: [{ t: 'charge' }], ...extra });
const testBoss = (id, phase, extra = {}) => testEnemy(id, { tier: 'boss', hp: 1, phase: testPhase(phase), ...extra });
const missingWeak = testBoss('t_phase_missing_weak');
delete missingWeak.phase.weak;
export const invalidPhases = [['bad_t', 'INVALID_ENEMY_MOVE'], ['bad_hp', 'INVALID_ENEMY_MOVE'], ['zero_brk', 'INVALID_ENEMY_MOVE'],
  ['dup_weak', 'INVALID_ENEMY_MOVE'], ['unknown_weak', 'INVALID_ENEMY_MOVE'], ['empty_weak', 'INVALID_ENEMY_MOVE'],
  ['missing_weak', 'INVALID_ENEMY_MOVE'], ['empty_pattern', 'INVALID_ENEMY_MOVE'], ['nested_phase', 'INVALID_ENEMY_MOVE'], ['not_boss', 'WRONG_PHASE']];
export const invalidMoves = [['drain-negative', 't_bad_drain_negative'], ['drain-missing', 't_bad_drain_missing'], ['silence-count', 't_bad_silence']];
export const boundaryEnemies = [
  testEnemy('t_drain', { pattern: [{ t: 'apDrain', n: 1 }, { t: 'apDrain', n: 6 }, { t: 'charge' }] }),
  testEnemy('t_smasher', { pattern: [{ t: 'atk', d: 99 }] }),
  testBoss('t_phase_multihit', { hp: 100 }, { pattern: [{ t: 'atk', d: 10, h: 3 }] }),
  testBoss('t_phase_reflect', {}, { pattern: [{ t: 'reflect', n: 50 }, { t: 'charge' }] }),
  testBoss('t_phase_bad_t', { t: 'charge' }),
  testBoss('t_phase_bad_hp', { hp: -1 }),
  testBoss('t_phase_zero_brk', { brk: 0 }),
  testBoss('t_phase_dup_weak', { weak: ['volt', 'volt'] }),
  testBoss('t_phase_unknown_weak', { weak: ['shadow'] }),
  testBoss('t_phase_empty_weak', { weak: [] }),
  missingWeak,
  testBoss('t_phase_empty_pattern', { pattern: [] }),
  testBoss('t_phase_nested_phase', { pattern: [{ t: 'phase' }] }),
  testEnemy('t_phase_not_boss', { hp: 1, phase: testPhase() }),
  testEnemy('t_bad_drain_negative', { pattern: [{ t: 'apDrain', n: -1 }] }),
  testEnemy('t_bad_drain_missing', { pattern: [{ t: 'apDrain' }] }),
  testEnemy('t_bad_silence', { pattern: [{ t: 'silence', n: 2 }] }),
];

function datasets() {
  const candidate = candidates.withLateEnemies(data);
  const boundary = clone(candidate);
  const ids = new Set(boundary.enemies.map(e => e.id));
  assert.ok(boundaryEnemies.every(e => e.id.startsWith('t_') && !ids.has(e.id)), 'Test-local enemies must stay separate from live and candidate content');
  boundary.enemies.push(...clone(boundaryEnemies));
  return { candidate, boundary };
}

const baseParty = [{ charId: 'sera' }, { charId: 'nia' }, { charId: 'harin' }];
function baseSetup(enemies, extra = {}) {
  const ix = MF.indexData(data);
  return clone({ party: baseParty, deck: baseParty.flatMap(a => MF.Run.baseKit(ix, a.charId)), hp: 243, maxHp: 243, seed: 0, relics: [], enemies, ...extra });
}
function smallSetup(party, deck, enemies, extra = {}) {
  const maxHp = party.reduce((n, a) => n + (a.hp || MF.Stats.REFERENCE.hp), 0);
  return clone({ party, deck: deck.map(id => ({ id })), hp: maxHp, maxHp, seed: 0, relics: [], enemies, ...extra });
}

function point(battle) {
  const state = clone(battle.s), publicView = clone(battle.publicView());
  return { state, stateHash: stateHash(state), rngHash: stateHash(state.rng), eventsHash: stateHash(state.ev),
    logHash: stateHash(state.log), publicView, publicViewHash: stateHash(publicView) };
}

function record(sets, id, group, dataset, initial, script) {
  const battle = MF.Battle.create(sets[dataset], clone(initial));
  const row = { id, group, dataset, setup: clone(initial), created: point(battle), steps: [] };
  const command = (kind, args = {}, expectedError = null, request = null) => {
    const req = request || { rid: `${id}-${row.steps.length}-${kind}`, v: battle.s.v, kind, args };
    const before = clone(battle.s), receipts = clone(battle.receipts);
    let response = null, errorCode = null;
    try { response = clone(battle.command(clone(req))); }
    catch (error) {
      // Only the exact, deliberately requested rejection is acceptable; any other failure is fatal.
      if (!expectedError || !(error instanceof MF.RuleError) || error.code !== expectedError) throw error;
      errorCode = error.code;
    }
    assert.equal(errorCode, expectedError, `${id}: expected rejection did not occur`);
    const outcome = errorCode ? 'rejected' : battle.s.v === before.v ? 'replayed' : 'committed';
    if (outcome === 'committed') assert.equal(battle.s.v, before.v + 1, `${id}: a commit advances exactly one version`);
    else {
      assert.ok(isDeepStrictEqual(battle.s, before), `${id}: ${outcome} request changed state`);
      assert.ok(isDeepStrictEqual(battle.receipts, receipts), `${id}: ${outcome} request changed receipts`);
    }
    row.steps.push({ request: clone(req), outcome, errorCode, response, responseEventsHash: response ? stateHash(response.ev) : null, ...point(battle) });
    return response;
  };
  const find = cardId => {
    const uid = battle.s.zones.hand.find(u => battle.s.cards[u].id === cardId);
    assert.ok(uid, `${id}: ${cardId} is not actually in hand`);
    return uid;
  };
  const play = (cardId, mode = 'now', expectedError = null, extra = {}) => {
    const uid = find(cardId);
    command('play', { uid, mode, target: battle.needsTarget(uid) ? battle.alive()[0].uid : null, picks: [], ...extra }, expectedError);
    return uid;
  };
  const end = (expectedError = null) => command('end', {}, expectedError);
  const events = kind => battle.s.ev.filter(e => e.e === kind);
  const cost = () => events('play')[0].cost;
  script({ battle, command, play, end, events, cost });
  return row;
}

export function generateCases() {
  assert.deepEqual(sources(), loadedSources, 'Late-action sources changed after module load; rerun the exporter');
  const sets = datasets();
  const byId = Object.fromEntries(sets.candidate.enemies.map(e => [e.id, e]));
  const cases = [];
  const add = (...args) => cases.push(record(sets, ...args));
  const skipped = (ends, expectTurn = true) => ({ battle, command, end }) => {
    command('begin');
    for (let i = 0; i < ends; i++) end();
    if (expectTurn) assert.deepEqual([battle.s.phase, battle.s.turn], ['PLAYER', ends + 1], 'Skipped-turn cycles must stay alive');
  };

  // Every candidate runs two complete pattern cycles alive at the authored-matrix setup.
  for (const enemy of candidates.enemies) add(`duel-${enemy.id}`, 'duel', 'candidate', baseSetup([enemy.id]), skipped(2 * enemy.pattern.length));
  // Every candidate encounter group of the overlay (not authored live content).
  let encounters = 0;
  for (const [zone, tiers] of Object.entries(candidates.encounters)) for (const [tier, groups] of Object.entries(tiers)) {
    groups.forEach((enemies, index) => {
      add(`encounter-${zone}-${tier}-${index}`, 'encounter', 'candidate', baseSetup(enemies), skipped(2 * Math.max(...enemies.map(e => byId[e].pattern.length))));
      encounters++;
    });
  }

  const edge = (id, dataset, initial, script) => add(`edge-${id}`, 'edge', dataset, initial, script);
  const shields = n => Array(n).fill('harin_shield');
  const rounds = n => Array(n).fill('sera_round');

  // apDrain: next-turn AP only, separate from due debt, consumed once and clamped at 0.
  edge('drain-debt-clamp', 'boundary', smallSetup([{ charId: 'ravien' }, { charId: 'harin' }], ['ravien_advance', ...shields(2)], ['t_drain']),
    ({ battle, command, play, end }) => {
      command('begin');
      play('ravien_advance');
      assert.deepEqual([battle.s.ap, battle.debtTotal()], [6, 2]);
      end();
      assert.deepEqual([battle.s.turn, battle.s.ap, battle.s.apDrainNext, battle.s.debts.length], [2, 1, 0, 0]);
      end();
      assert.deepEqual([battle.s.turn, battle.s.ap], [3, 0]);
      play('harin_shield', 'now', 'AP_LOW');
      end();
      assert.deepEqual([battle.s.turn, battle.s.ap, battle.s.apDrainNext], [4, 4, 0]);
    });
  // silence: +1 on next/after only, non-stacking, expiry after the silenced turn, then reapplication. RULES_NOW_PLAY: every card is played now,
  // so the surcharge never applies; freeNext still zeroes now costs.
  edge('silence-free-next', 'candidate', smallSetup([{ charId: 'rien', gauge: 100 }, { charId: 'harin' }], shields(3), ['e_choir', 'e_choir']),
    ({ battle, command, play, end, cost }) => {
      command('begin');
      end();
      assert.equal(battle.s.silenceUntil, 2);
      play('harin_shield'); assert.equal(cost(), 1);
      command('ult', { actor: 'rien', target: null, picks: [] }); assert.equal(battle.s.freeNext, 2);
      play('harin_shield'); assert.equal(cost(), 0);
      play('harin_shield'); assert.equal(cost(), 0);
      end();
      assert.equal(battle.s.silenceUntil, 0);
      play('harin_shield'); assert.equal(cost(), 1);
      end();
      assert.equal(battle.s.silenceUntil, 4);
      play('harin_shield'); assert.equal(cost(), 1);
    });
  // silence: a free instance stays free and silenced now plays keep their printed cost.
  edge('silence-free-instance', 'candidate', smallSetup([{ charId: 'yuri' }, { charId: 'harin' }], ['yuri_metamorph', 'yuri_cocoon', 'harin_shield'], ['e_choir']),
    ({ battle, command, play, end, cost }) => {
      command('begin');
      play('yuri_metamorph');
      const copy = battle.s.zones.hand.find(u => battle.s.cards[u].free);
      assert.ok(copy && battle.s.cards[copy].token && battle.s.cards[copy].echoCopy, 'copyPick must create a free token copy');
      end();
      assert.equal(battle.s.silenceUntil, 2);
      command('play', { uid: copy, mode: 'now', target: null, picks: [] }); assert.equal(cost(), 0);
      play('yuri_cocoon'); assert.equal(cost(), 1);
      play('yuri_metamorph'); assert.equal(cost(), 1);
      play('harin_shield'); assert.equal(cost(), 1);
      end();
      assert.equal(battle.s.silenceUntil, 0);
    });
  // sealSlot with nothing to seal (RULES_NOW_PLAY: no reservation exists): it still marks the following player turn and gains no block;
  // now plays keep their cost while sealed.
  edge('seal-no-reservation', 'candidate', smallSetup([{ charId: 'harin' }, { charId: 'sera' }], shields(5), ['e_sealgolem']),
    ({ battle, command, play, end, events, cost }) => {
      command('begin');
      play('harin_shield'); play('harin_shield');
      end();
      assert.deepEqual([battle.s.sealedUntil, battle.s.res.length, events('sealSlot')[0].fallback, battle.alive()[0].block], [2, 0, false, 0]);
      play('harin_shield'); assert.equal(cost(), 1);
      end();
      play('harin_shield');
      end();
      assert.deepEqual([battle.s.sealedUntil, events('sealSlot')[0].fallback, battle.alive()[0].block], [4, false, 0]);
    });
  // RULES_NOW_PLAY: collateral no longer seals (it is block + debt 1), so a later enemy seal is an ordinary one, not a fallback.
  edge('seal-after-collateral-debt', 'candidate', smallSetup([{ charId: 'ravien' }, { charId: 'harin' }], ['ravien_collateral', ...shields(2)], ['e_sealgolem']),
    ({ battle, command, play, end, events }) => {
      command('begin');
      play('ravien_collateral');
      assert.deepEqual([battle.s.sealedUntil, battle.debtTotal()], [0, 1]);
      end();
      assert.deepEqual(events('sealSlot'), [{ e: 'sealSlot', uid: battle.alive()[0].uid, until: 2, fallback: false }]);
      assert.deepEqual([battle.alive()[0].block, battle.s.ap, battle.debtTotal()], [0, 3, 0]);
      play('harin_shield');
      end();
    });
  edge('seal-two-sealers', 'candidate', baseSetup(['e_sealgolem', 'e_timeshard']), ({ battle, command, end, events }) => {
    command('begin');
    end();
    assert.deepEqual(events('sealSlot').map(e => e.fallback), [false, true]);
    assert.deepEqual(battle.s.enemies.map(e => e.block), [0, 10]);
    for (let i = 0; i < 3; i++) end();
    assert.equal(battle.s.phase, 'PLAYER');
  });
  // delayRes with no reservation (RULES_NOW_PLAY): a null event, no state change; the now-played card resolved once at its own value.
  edge('delay-no-reservation', 'candidate', smallSetup([{ charId: 'sera' }, { charId: 'harin' }], ['sera_timed', ...shields(4)], ['e_prompter']),
    ({ battle, command, play, end, events }) => {
      command('begin');
      play('harin_shield');
      const timed = play('sera_timed');
      end();
      assert.deepEqual(events('delayRes'), [{ e: 'delayRes', uid: null, discarded: false }]);
      assert.deepEqual([battle.s.res.length, battle.s.cards[timed].used], [0, 1]);
      end();
      assert.deepEqual(events('dmg').filter(e => e.card === 'sera_timed').map(e => e.n), []);
      end();
    });
  // eatName: discard tokens first (the overdue penalty note), through the replayed RNG.
  edge('eat-token-priority', 'candidate', baseSetup(['e_overdue', 'e_echo']), ({ battle, command, end, events }) => {
    command('begin');
    for (let i = 0; i < 4; i++) {
      end();
      for (const eaten of events('eatName')) assert.ok(battle.s.cards[eaten.uid].token && battle.s.cards[eaten.uid].id === 'penalty_note');
    }
    assert.equal(battle.s.exhausted, 2);
  });
  // eatName: an empty discard reports a null uid and draws no RNG (RULES_NOW_PLAY: an all-보존 hand stays in hand, so nothing is discarded).
  edge('eat-empty-discard', 'candidate', smallSetup([{ charId: 'harin' }, { charId: 'sera' }], ['sera_timed', 'sera_last'], ['e_echo']),
    ({ battle, command, end, events }) => {
      command('begin');
      const rng = clone(battle.s.rng);
      end();
      assert.deepEqual(events('eatName'), [{ e: 'eatName', uid: null }]);
      assert.deepEqual([battle.s.rng, battle.s.exhausted, battle.s.zones.discard.length, battle.s.zones.hand.length], [rng, 0, 0, 2]);
      end();
    });
  // eatName: the normal exhaust hooks run (oblivion block, void shard RNG damage); oblivion ink gives AP only in a player phase.
  edge('eat-exhaust-hooks', 'candidate', smallSetup([{ charId: 'sion' }, { charId: 'harin' }], ['sion_oblivion', ...shields(3)], ['e_echo'],
    { relics: ['void_shard', 'oblivion_ink'] }), ({ battle, command, play, end }) => {
    command('begin');
    play('sion_oblivion');
    assert.equal(battle.s.ap, 4);
    end();
    const kinds = battle.s.ev.map(e => e.e), at = kinds.indexOf('eatName');
    assert.deepEqual(kinds.slice(0, at + 1), ['exhaust', 'block', 'dmg', 'eatName']);
    assert.equal(battle.s.exhausted, 2);
    end();
  });
  // phase: the first HP 0 is no kill (mark reset, no deduction); only the final body's death runs kill hooks.
  edge('nameless-kill-hooks', 'candidate', smallSetup([{ charId: 'sera' }, { charId: 'kaya' }], ['sera_round', 'sera_round', 'kaya_clue', 'kaya_clue'], ['b_nameless'],
    { relics: ['detective_notebook'], difficulty: { hp: 1, dmg: 100 } }), ({ battle, command, play, end, events }) => {
    command('begin');
    const uid = battle.alive()[0].uid;
    play('kaya_clue');
    play('sera_round');
    const body = battle.enemy(uid);
    assert.deepEqual([body.alive, body.phaseIndex, body.hp, body.maxHp, body.brk, body.st.mark, body.weak], [true, 1, 2, 2, 15, 0, ['flame', 'frost']]);
    assert.deepEqual([events('kill').length, events('phase').length, battle.s.killedMarked], [0, 1, 0]);
    play('kaya_clue');
    play('sera_round');
    assert.deepEqual([battle.s.phase, events('kill').length, battle.s.killedMarked, battle.s.runFlags.coinsBonus, battle.s.turnFlags.deduction],
      ['VICTORY', 1, 1, 10, true]);
    end('WRONG_PHASE');
  });
  // phase: later hits of one card damage the second body; only its death emits kill.
  edge('nameless-multihit', 'candidate', smallSetup([{ charId: 'sera' }], ['sera_double'], ['b_nameless'], { difficulty: { hp: 1, dmg: 100 } }),
    ({ battle, command, play, end }) => {
      command('begin');
      play('sera_double');
      assert.deepEqual(battle.s.ev.map(e => e.e).filter(e => ['dmg', 'phase', 'kill', 'end'].includes(e)), ['dmg', 'phase', 'dmg', 'kill', 'end']);
      assert.equal(battle.s.phase, 'VICTORY');
      end('WRONG_PHASE');
    });
  // phase: round-end burn opens the second body (fresh status, burn not carried), whose pattern then runs two full cycles.
  edge('nameless-phase2-cycles', 'candidate', baseSetup(['b_nameless'], { hp: 500, maxHp: 500, relics: ['pocket_brazier'], difficulty: { hp: 1, dmg: 100 } }),
    ({ battle, command, end }) => {
      command('begin');
      end();
      const body = battle.s.enemies[0];
      assert.deepEqual([body.alive, body.phaseIndex, body.hp, body.st.burn, body.pi, body.intent.t], [true, 1, 2, 0, 0, 'sealSlot']);
      for (let i = 0; i < 12; i++) end();
      assert.deepEqual([battle.s.phase, battle.s.turn, battle.s.hp], ['PLAYER', 14, 252]);
    });
  // phase: a counter that opens the attacker's second body ends the old body's remaining hits.
  edge('phase-counter-multihit', 'boundary', smallSetup([{ charId: 'harin' }], shields(3), ['t_phase_multihit']),
    ({ battle, command, play, end, events }) => {
      command('begin');
      play('harin_shield');
      assert.equal(battle.s.counter, 1);
      end();
      const body = battle.s.enemies[0];
      assert.deepEqual([events('hit').length, battle.s.hp, body.phaseIndex, body.hp, body.intent.t], [1, 79, 1, 100, 'charge']);
      end();
    });
  // phase: reflect from the lethal hit ends the battle in defeat after the transition.
  edge('phase-reflect-defeat', 'boundary', smallSetup([{ charId: 'sera' }], rounds(3), ['t_phase_reflect'], { hp: 40 }),
    ({ battle, command, play, end }) => {
      command('begin');
      end();
      assert.equal(battle.s.enemies[0].st.reflect, 50);
      play('sera_round');
      const body = battle.s.enemies[0];
      assert.deepEqual([battle.s.phase, battle.s.hp, body.alive, body.phaseIndex, body.hp], ['DEFEAT', 0, true, 1, 30]);
      end('WRONG_PHASE');
    });
  // Invalid descriptors reject the whole lethal command atomically with the original codes; the battle continues.
  for (const [name, code] of invalidPhases) {
    edge(`phase-invalid-${name.replace(/_/g, '-')}`, 'boundary', smallSetup([{ charId: 'sera' }], rounds(3), [`t_phase_${name}`]), ({ battle, command, play, end }) => {
      command('begin');
      play('sera_round', 'now', code);
      end();
      assert.ok(battle.s.enemies[0].hp === 1 && !Object.hasOwn(battle.s.enemies[0], 'phaseIndex'));
    });
  }
  for (const [name, enemy] of invalidMoves) {
    edge(`move-invalid-${name}`, 'boundary', smallSetup([{ charId: 'sera' }], rounds(3), [enemy]), ({ command, end }) => {
      command('begin');
      end('INVALID_ENEMY_MOVE');
    });
  }
  // Terminal stop: silence/drain/seal/eat state stays as set when a later enemy ends the battle before the next turn.
  edge('late-terminal-defeat', 'boundary', smallSetup([{ charId: 'sera' }], rounds(3), ['e_choir', 'e_collector', 'e_sealgolem', 'e_echo', 't_smasher'], { hp: 30 }),
    ({ battle, command, end }) => {
      command('begin');
      end();
      assert.deepEqual([battle.s.phase, battle.s.apDrainNext, battle.s.silenceUntil, battle.s.sealedUntil, battle.s.exhausted], ['DEFEAT', 2, 2, 2, 1]);
      end('WRONG_PHASE');
    });

  // Receipts across a late-move command: replay, conflict, stale version, later commit, old replay.
  add('protocol-late-receipts', 'protocol', 'candidate', baseSetup(['e_collector']), ({ battle, command }) => {
    const begin = { rid: 'protocol-late-begin', v: 0, kind: 'begin', args: {} };
    const first = { rid: 'protocol-late-end', v: 1, kind: 'end', args: {} };
    command(null, null, null, begin);
    command(null, null, null, first);
    assert.equal(battle.s.ap, 2);
    command(null, null, null, first);
    command(null, null, 'IDEMPOTENCY_CONFLICT', { ...first, args: { changed: true } });
    command(null, null, 'STALE_VERSION', { rid: 'protocol-late-stale', v: 1, kind: 'end', args: {} });
    command(null, null, null, { rid: 'protocol-late-next', v: 2, kind: 'end', args: {} });
    command(null, null, null, begin);
  });

  assert.equal(JSON.stringify(data), originalData, 'Live data must stay unchanged');
  assert.equal(new Set(cases.map(c => c.id)).size, cases.length, 'Duplicate late-action case id');
  const seen = new Set(cases.flatMap(c => c.steps.flatMap(s => (s.response ? s.response.ev : []).map(e => e.e))));
  assert.ok(behaviors.every(b => seen.has(b)), 'Every late behavior must actually run');
  assert.deepEqual(sources(), loadedSources, 'Late-action sources changed during generation; rerun the exporter');
  const steps = cases.flatMap(c => c.steps);
  return { schema, scope, status, dataVersion: data.meta.version, sources: clone(loadedSources),
    overlay: { provenance: clone(candidates.provenance), enemies: clone(candidates.enemies), encounters: clone(candidates.encounters) },
    boundaryEnemies: clone(boundaryEnemies), behaviors: [...behaviors],
    coverage: { candidates: candidates.enemies.length, encounters, edges: cases.filter(c => c.group === 'edge').length, protocols: cases.filter(c => c.group === 'protocol').length },
    counts: { cases: cases.length, commands: steps.length, committed: steps.filter(s => s.outcome === 'committed').length,
      replayed: steps.filter(s => s.outcome === 'replayed').length, rejected: steps.filter(s => s.outcome === 'rejected').length }, cases };
}

export function checkFixture(fixture) {
  assert.equal(fixture.schema, schema, 'Wrong late-action fixture schema');
  assert.ok(Array.isArray(fixture.cases), 'Late-action fixture requires cases');
  assert.equal(new Set(fixture.cases.map(c => c.id)).size, fixture.cases.length, 'Duplicate late-action case id');
  assert.deepEqual(fixture.sources, loadedSources, 'Late-action source provenance is stale');
  const expected = generateCases();
  assert.deepEqual(fixture.overlay, expected.overlay, 'Late-action candidate overlay differs from the proposal source');
  assert.deepEqual(fixture.boundaryEnemies, expected.boundaryEnemies, 'Late-action test-local enemies differ');
  assert.deepEqual(fixture.coverage, expected.coverage, 'Late-action coverage differs');
  assert.deepEqual(fixture.counts, expected.counts, 'Late-action case/command counts differ');
  // A failed multi-megabyte fixture must not build an unbounded assertion diff.
  assert.ok(isDeepStrictEqual(fixture, expected), 'Late-action fixture snapshots, responses, hashes or cases differ');
  return fixture.counts;
}

export function exportVectors(output = fixturePath) {
  const fixture = generateCases();
  const json = Buffer.from(`${JSON.stringify(fixture)}\n`);
  const bytes = output.endsWith('.gz') ? gzipSync(json, { level: 9 }) : json;
  const temporary = mkdtempSync(join(dirname(output), '.battle-late-actions-'));
  try {
    const path = join(temporary, 'fixture');
    writeFileSync(path, bytes);
    assert.deepEqual(sources(), fixture.sources, 'Late-action sources changed before export; previous fixture preserved');
    renameSync(path, output);
  } finally {
    rmSync(temporary, { recursive: true, force: true });
  }
  return { ...fixture.counts, bytes: bytes.length, uncompressedBytes: json.length, sha256: sha256(bytes), sources: fixture.sources };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  if (args.length > 1 || (args.length && !['--check', '--help'].includes(args[0]))) throw new Error('Usage: node tools/gen_battle_late_actions_vectors.mjs [--check|--help]');
  if (args[0] === '--help') console.log('Generate the original-JS late enemy action oracle (candidate overlay + test-local boundaries), or --check its lossless gzip fixture. Candidate activation is NOT approved.');
  else if (args[0] === '--check') console.log(JSON.stringify({ checked: fixturePath, ...checkFixture(JSON.parse(gunzipSync(readFileSync(fixturePath)).toString('utf8'))) }));
  else console.log(JSON.stringify({ output: fixturePath, ...exportVectors() }, null, 2));
}
