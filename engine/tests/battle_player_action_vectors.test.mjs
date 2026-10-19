import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { createHash } from 'node:crypto';
import { mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync, mkdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { gunzipSync } from 'node:zlib';
import { generateCases, checkFixture, exportVectors, fixturePath } from '../../tools/gen_battle_player_action_vectors.mjs';
import { stateHash } from '../../tools/gen_traces.mjs';

const require = createRequire(import.meta.url);
const data = require('../../data/game_data.json');
let generated;
const fixture = () => generated ||= generateCases();
const clone = value => JSON.parse(JSON.stringify(value));
const get = id => { const row = fixture().cases.find(c => c.id === id); assert.ok(row, id); return row; };

// RULES_NOW_PLAY (2026-10-19): every card is played now only, so the 378 reservation-mode pairs became 173 now pairs.
test('every authored card and all173 base legal now modes really resolve without a fizzle', () => {
  const base = fixture().cases.filter(c => c.group === 'card');
  assert.ok(data.cards.every(c => c.res === 'n'));
  const expected = data.cards.flatMap(c => ({ y: ['now', 'next', 'after'], n: ['now'], o: ['next', 'after'], after: ['after'] }[c.res])
    .map(mode => `${c.id}:${mode}`));
  assert.equal(expected.length, 173); assert.equal(expected.length, data.cards.length);
  assert.deepEqual(base.map(c => `${c.main.cardId}:${c.main.mode}`), expected);
  for (const row of base) {
    assert.equal(row.mainOutcome.fizzled, false, row.id);
    assert.ok(row.mainOutcome.resolved, row.id);
    assert.equal(row.mainOutcome.used, 1, row.id);
  }
});

test('whole fixture is complete only with exact case inventory, provenance and independently replayable requests', () => {
  const f = fixture(); assert.equal(f.schema, 'mf-battle-player-actions-v1'); assert.equal(f.status, 'COMPLETE_CURRENT_SOURCE_A1');
  assert.equal(f.dataVersion, data.meta.version); assert.equal(new Set(f.cases.map(c => c.id)).size, f.cases.length);
  assert.equal(f.counts.cases, f.cases.length); assert.equal(f.coverage.baseModePairs.length, 173);
  for (const source of Object.values(f.sources)) {
    const bytes = readFileSync(new URL(`../../${source.path}`, import.meta.url));
    assert.equal(createHash('sha256').update(bytes).digest('hex'), source.sha256);
  }
  const MF = require('../rules-core.js');
  let commands = 0, committed = 0, replayed = 0, rejected = 0;
  for (const row of f.cases) {
    const b = MF.Battle.create(data, clone(row.setup));
    assert.deepEqual(b.s, row.created.state); assert.deepEqual(b.publicView(), row.created.publicView);
    for (const step of row.steps) {
      const before = clone(b.s), receiptsBefore = clone(b.receipts);
      if (step.check) {
        const a = step.check.args; assert.equal(b.check(a.uid, a.mode, a.target, a.picks), step.check.code);
        assert.deepEqual(b.s, before); assert.deepEqual(b.receipts, receiptsBefore);
      }
      if (step.errorCode) { assert.throws(() => b.command(clone(step.request)), e => e instanceof MF.RuleError && e.code === step.errorCode); rejected++; }
      else {
        const response = b.command(clone(step.request)); assert.deepEqual(response, step.response);
        if (step.outcome === 'replayed') replayed++; else committed++;
        // A replay response is detached from both the current state and cached receipt.
        if (step.outcome === 'replayed') {
          response.ev.push({ e: 'CLIENT_ONLY' }); response.v = -999;
          assert.deepEqual(b.command(clone(step.request)), step.response);
        }
      }
      if (step.outcome !== 'committed') { assert.deepEqual(b.s, before); assert.deepEqual(b.receipts, receiptsBefore); }
      assert.deepEqual(b.s, step.state, `${row.id}: actual replay state`); assert.deepEqual(b.publicView(), step.publicView);
      for (const [key, value] of [['stateHash', b.s], ['rngHash', b.s.rng], ['eventsHash', b.s.ev], ['logHash', b.s.log], ['publicViewHash', b.publicView()]]) assert.equal(step[key], stateHash(value));
      if (step.response) assert.equal(step.responseEventsHash, stateHash(step.response.ev));
      if (step.receipts) assert.deepEqual(b.receipts, step.receipts);
      commands++;
    }
  }
  assert.deepEqual(f.counts, { cases: f.cases.length, commands, committed, replayed, rejected });
});

test('matrix metadata retains every plus/resonance/refine/part/relic and normal actor-derived HP', () => {
  const f = fixture(), group = name => f.cases.filter(c => c.group === name);
  assert.deepEqual(group('plus').map(c => c.main.cardId), data.cards.map(c => c.id));
  for (const c of group('plus')) { assert.equal(c.main.plus, true); assert.equal(c.mainOutcome.fizzled, false); }
  assert.deepEqual(group('weapon').map(c => `${c.weaponId}:${c.refine}`), data.weapons.flatMap(w => [1, 2, 3, 4, 5].map(r => `${w.id}:${r}`)));
  assert.deepEqual(group('part').map(c => c.partId), data.parts.map(p => p.id)); assert.deepEqual(group('relic').map(c => c.relicId), data.relics.map(r => r.id));
  for (const row of f.cases.filter(c => ['card', 'plus', 'resonance', 'ult', 'weapon', 'part', 'relic', 'encounter'].includes(c.group))) {
    const maximum = row.setup.party.reduce((n, actor) => n + (actor.hp || 81), 0);
    assert.equal(row.setup.maxHp, maximum, row.id); assert.equal(row.setup.hp, maximum, row.id);
    assert.ok(row.setup.party.every(a => a.hp === undefined), `${row.id}: matrix changed actor vitality`);
  }
});

test('bulwark retains the authored10 and powers merge their numeric parameters additively', () => {
  const row = get('edge-bulwark-retention'), begin = row.steps[0].state, played = row.steps[1].state, end = row.steps[2].state;
  assert.equal(begin.block, 8 + 8 + 10); assert.equal(begin.counter, 2);
  const source = data.cards.find(c => c.id === 'harin_bulwark').eff[0]; assert.equal(source.keep, 10);
  assert.deepEqual(played.powers, [{ id: 'bulwark', keep: source.keep }]); assert.equal(played.ap, 4 - 2); assert.equal(played.actors[0].gauge, 10);
  assert.equal(end.block, 10); assert.equal(end.counter, 0); assert.deepEqual(played.zones.exhaust, ['c1']);
  assert.deepEqual(get('edge-bulwark-stacks').steps[2].state.powers, [{ id: 'bulwark', keep: 20 }]);
});

test('debt deferral repays later and collateral debt1 shares the loan sequence, due next turn', () => {
  const defer = get('edge-defer-debt'); assert.deepEqual(defer.steps[2].state.debts, [{ id: 'd1', n: 3, due: 3, deferred: true }]);
  assert.equal(defer.steps[3].state.ap, 4); assert.equal(defer.steps[4].state.ap, 1);
  const collateral = data.cards.find(c => c.id === 'ravien_collateral').eff[0]; assert.equal(collateral.op, 'collateral');
  const shared = get('edge-collateral-debt-shared-sequence'), played = shared.steps[2].state, next = shared.steps[3].state;
  assert.equal(played.resSeq, 2); assert.deepEqual(played.debts.map(d => [d.id, d.n, d.due]), [['d1', 2, 2], ['d2', 1, 2]]);
  assert.equal(played.block, collateral.v); assert.deepEqual(played.res, []); assert.equal(played.sealedUntil, 0);
  assert.equal(next.turn, 2); assert.equal(next.ap, 4 - 2 - 1); assert.deepEqual(next.debts, []);
  const cap = get('protocol-collateral-debt-cap').steps.at(-1); assert.equal(cap.errorCode, 'DEBT_CAP'); assert.equal(cap.state.debts.reduce((n, d) => n + d.n, 0), data.rules.debtCap);
});

test('retain keeps 보존 cards as held, play/recall/draw clear held, and held keys the reworked relic and weapon bonuses', () => {
  const retain = get('edge-retain-held-end-turn'), ended = retain.steps[2].state;
  const held = Object.values(ended.cards).filter(c => c.held).map(c => c.id).sort(); assert.deepEqual(held, ['com_capacitor', 'com_slash', 'sera_timed']);
  assert.deepEqual(ended.zones.hand.slice(0, 3).map(u => ended.cards[u].id).sort(), held); assert.equal(ended.zones.hand.length, 3 + data.rules.handDraw);
  const capUid = ended.zones.hand.find(u => ended.cards[u].id === 'com_capacitor'); assert.equal(retain.steps[3].state.cards[capUid].held, false);
  const reset = get('edge-held-reset-recall-draw'), timed = reset.steps[1].state.zones.hand.find(u => reset.steps[1].state.cards[u].id === 'sera_timed');
  const flags = reset.steps.map(s => s.state.cards[timed].held);
  assert.deepEqual(reset.steps.map(s => s.request.kind), ['begin', 'play', 'end', 'play', 'play', 'end', 'play', 'end']);
  assert.deepEqual([flags[2], flags[4], flags[5], flags[7]], [true, false, true, false]);
  const relics = get('edge-reserved-break-relics').steps;
  assert.equal(relics[0].state.enemies[0].brk - relics[1].state.enemies[0].brk, 3 + 1); assert.equal(relics[2].state.enemies[0].brk - relics[3].state.enemies[0].brk, 3 + 1 + 1);
  const stamp = get('edge-held-time-stamp-weapon').steps, bonus = data.weapons.find(w => w.id === 'w_sera').passive.values[0];
  const timedV = data.cards.find(c => c.id === 'sera_timed').eff[0].v;
  assert.equal(stamp[1].response.ev.find(e => e.e === 'dmg').n, timedV); assert.equal(stamp[3].response.ev.find(e => e.e === 'dmg').n, Math.floor(timedV * (10000 + bonus) / 10000));
  assert.equal(stamp[3].state.actors[0].gauge - stamp[2].state.actors[0].gauge, data.rules.gaugePerCard + 5);
  const clock = get('edge-empty-clock-first-card').steps; assert.deepEqual(clock.slice(1).map(s => s.response.ev[0].cost), [0, 1]);
});

test('echo delivery and copyPick deliberately preserve different metadata and free lifetimes', () => {
  // RULES_NOW_PLAY: the echo circuit queues its copy on a now resolution (block v9 + evolved growth3 x2), delivered at the next turn start.
  const echo = get('edge-echo-evolved-lineage'), at1 = echo.steps[1].state, at2 = echo.steps[2].state;
  assert.equal(at1.block, 9 + 3 * 2); assert.deepEqual(at1.pendingCopies.map(p => p.due), [2]); assert.deepEqual(at2.pendingCopies, []);
  const ec = Object.values(at2.cards).find(c => c.echoCopy); assert.deepEqual(ec.temp, ['echoHalf']); assert.deepEqual(ec.mods, { c: null, e: null }); assert.equal(ec.plus, false);
  assert.equal(echo.steps[3].request.args.uid, ec.uid); assert.equal(echo.steps[3].response.ev[0].cost, 0); assert.deepEqual(echo.steps[3].state.zones.exhaust, [ec.uid]);
  assert.deepEqual(echo.steps[3].state.pendingCopies, []);
  const picked = get('edge-copy-pick-lineage').steps[1].state, pc = Object.values(picked.cards).find(c => c.echoCopy);
  assert.equal(pc.plus, true); assert.deepEqual(pc.mods, { c: null, e: 'e_guard' }); assert.deepEqual(pc.temp, []); assert.equal(pc.from, 'yuri_cocoon'); assert.equal(pc.free, true);
  assert.equal(get('edge-copy-pick-lineage').steps.at(-1).state.block, Math.ceil(9 * 13 / 10) + 4 + 6);
});

test('growth operations count successful card growth once while ults use the separate uncapped path', () => {
  const first = get('edge-yuri-r6-growth-ult').steps[1]; assert.equal(first.response.ev.filter(e => e.e === 'grow').length, 4);
  assert.equal(first.state.block, 12 + 4 * 3); assert.equal(first.state.growthCount, 0);
  const twice = get('edge-yuri-r6-once-recharge').steps.filter(s => s.request.kind === 'ult'); assert.equal(twice.length, 2);
  assert.equal(twice[1].response.ev.filter(e => e.e === 'grow').length, 2);
  const capped = get('edge-growth-cap-once-and-saturation').steps.at(-1).state; assert.equal(capped.growthCount, 3); assert.equal(capped.cards.c1.growth, 1);
  const maxed = get('edge-growth-saturation-three').steps.filter(s => s.request.kind === 'ult').at(-1); assert.equal(maxed.state.cards.c1.growth, 3); assert.equal(maxed.response.ev.filter(e => e.e === 'grow').length, 0);
});

test('growth-once remains observable below cap and grown copy mechanisms deliberately diverge', () => {
  const once = get('edge-growth-once-below-cap').steps.at(-1).state;
  assert.equal(once.growthCount, 1); assert.equal(once.growthCap, 3); assert.equal(once.cards.c1.growth, 1); assert.equal(once.block, 6 + 2);
  const picked = get('edge-copy-pick-real-grown-root'), createdCopy = picked.steps[3].state.cards.c6;
  assert.equal(createdCopy.growth, 2); assert.equal(createdCopy.plus, true); assert.deepEqual(createdCopy.mods, { c: null, e: 'e_guard' });
  const resolvedCopy = picked.steps.at(-1).state; assert.equal(resolvedCopy.cards.c6.growth, 3); assert.equal(resolvedCopy.cards.c4.growth, 2); assert.equal(resolvedCopy.block, 2 * 3 + Math.ceil(6 * 13 / 10) + 3 + 4 + 6); // same-turn now play keeps the two dew moss blocks
  const echo = get('edge-echo-resets-earned-growth-upgrade-mods'), delivered = echo.steps[4].state;
  assert.equal(delivered.cards.c3.growth, 3); assert.equal(delivered.cards.c3.plus, true); assert.equal(delivered.cards.c5.growth, 0); assert.equal(delivered.cards.c5.plus, false); assert.deepEqual(delivered.cards.c5.mods, { c: null, e: null });
  assert.equal(echo.steps.at(-1).request.v, 6); assert.equal(echo.steps.at(-1).response.ev[0].cost, 0); assert.equal(echo.steps.at(-1).state.block, 3);
});

test('temporary overheat, actual use limits, definition-cost picks and ordinal UIDs stay distinct', () => {
  const mixed = get('edge-overheat-block-excludes-exhaust-add').steps.at(-1);
  assert.equal(mixed.response.ev.find(e => e.e === 'block').n, Math.floor(5 * 1.45)); assert.equal(mixed.response.ev.find(e => e.e === 'dmg').n, Math.floor(4 * 1.95));
  assert.deepEqual(mixed.state.cards.c3.temp, ['overheat', 'amp']); assert.deepEqual(mixed.state.zones.exhaust, ['c3']);
  for (const res of [0, 1]) { const s = get(`edge-nia-tune-used-up-r${res}`).steps.at(-1).state; assert.equal(s.cards.c1.used, 2 + res); assert.deepEqual(s.zones.exhaust, ['c1']); }
  const costs = get('edge-selection-definition-cost-before-payable').steps.at(-1).state;
  assert.equal(costs.cards.c4.costMod, -2); assert.equal(data.cards.find(c => c.id === 'sera_timed').cost + costs.cards.c4.costMod, 0); assert.deepEqual(costs.zones.exhaust, ['c5']);
  assert.deepEqual(get('edge-selection-curse-before-cost').steps.at(-1).state.zones.exhaust, ['c2']);
  const ordinal = get('edge-grow-lowest-ordinal-uid').steps[1].state; assert.equal(ordinal.cards.c1.growth, 1); assert.equal(ordinal.cards.c10.growth, 1); assert.equal(ordinal.cards.c2.growth, 0);
});

test('damage and healing use independently calculated rounding boundaries and ownership', () => {
  const hit = get('edge-vulnerable-weak-and-gear-math').steps.at(-1).response.ev.find(e => e.e === 'dmg');
  // RULES_NOW_PLAY: sera_timed is v20 now (ceil20*13/10=26) and c_quick's +20% replaces the removed delay +40%.
  const expected = Number(26n * 177n * 11200n * 14734n * 12500n / (160n * 10000n * 10000n * 10000n)); assert.equal(expected, 59); assert.equal(hit.n, expected);
  const heal = get('edge-heal-up-weapon-floor-order').steps.at(-1).state;
  const base = Number(10n * 83n * 10800n / (81n * 10000n)); assert.equal(base, 11);
  assert.equal(heal.block, Math.floor(Math.floor(base * 1.5) * 1.125)); assert.equal(heal.block, 18);
  const owner = get('edge-first-attack-and-owner-attribution').steps.filter(s => s.request.kind === 'play'); assert.deepEqual(owner.map(s => s.response.ev.find(e => e.e === 'dmg').n), [6, 8, 10]);
  const pierce = get('edge-pierce-split-rounding').steps.at(-1); assert.equal(pierce.state.enemies[0].hp, 42); assert.equal(pierce.state.enemies[0].block, 6);
});

test('earned gauge distinguishes the30 basic cap, weapon scaling and bypass gains', () => {
  const row = get('edge-gauge-earned-cap-bypass'), four = row.steps[4].state;
  assert.equal(four.actors[0].gaugeTurn, 30); assert.equal(four.actors[0].gauge, 30 + 4 * 12);
  const last = row.steps.at(-1); assert.equal(last.request.kind, 'ult'); assert.equal(last.state.actors[0].gauge, 0);
  assert.equal(row.steps.at(-2).state.actors[0].gauge, 100); assert.equal(last.state.ap, row.steps.at(-2).state.ap);
});

test('lethal card damage preserves same-hit rewards and always performs afterResolve once', () => {
  const row = get('edge-player-same-hit-defeat-reward'), step = row.steps.at(-1);
  assert.deepEqual(step.response.ev.map(e => e.e), ['play', 'dmg', 'hpLoss', 'kill', 'end', 'resolved']);
  assert.equal(step.state.phase, 'DEFEAT'); assert.equal(step.state.killedMarked, 1); assert.equal(step.state.runFlags.coinsBonus, 10); assert.equal(step.state.ap, 4);
  assert.equal(step.state.cards.c1.used, 1); assert.deepEqual(step.state.zones.discard, ['c1']); assert.equal(step.response.ev.filter(e => e.e === 'end').length, 1);
  const reflect = get('edge-lethal-reflect').steps.at(-1); assert.equal(reflect.response.ev.filter(e => e.e === 'dmg').length, 1); assert.equal(reflect.state.enemies[0].hp, 48);
  assert.equal(get('edge-envelope-multihit-continues').steps.at(-1).state.enemies[0].hp, 44);
  assert.equal(get('edge-silver-needle-no-revival').steps.at(-1).state.hp, 0); assert.equal(get('edge-silver-needle-envelope-survival').steps.at(-1).state.hp, 4);
});

test('post-resolution void shard terminal ends a now play exactly once', () => {
  const row = get('edge-void-shard-post-resolve-kill'), step = row.steps.at(-1);
  assert.equal(step.state.phase, 'VICTORY'); assert.equal(step.state.hp, 81); assert.equal(step.state.turn, 1);
  assert.deepEqual(step.response.ev.filter(e => e.e === 'dmg').map(e => [e.src, e.n]), [['card', 45], ['relic', 3]]);
  assert.ok(!step.response.ev.some(e => e.e === 'turn' || e.e === 'reshuffle')); assert.equal(step.response.ev.filter(e => e.e === 'end').length, 1);
  assert.equal(step.response.ev.at(-1).e, 'end'); assert.deepEqual(step.state.zones.exhaust, ['c1']);
});

test('exhaust re-reads the live discard zone after marked-kill notebook reshuffle', () => {
  const row = get('edge-exhaust-discard-reshuffle-live-zone'), prior = row.steps.at(-2).state, last = row.steps.at(-1).state;
  assert.deepEqual(prior.zones.discard, ['c3', 'c2', 'c6', 'c4']); assert.deepEqual(prior.zones.hand, ['c1', 'c5']); assert.deepEqual(prior.zones.draw, []);
  assert.deepEqual(last.zones.exhaust, ['c3']); assert.deepEqual(last.zones.draw, ['c6']); assert.equal(last.block, 10); assert.equal(last.phase, 'PLAYER'); assert.equal(last.killedMarked, 1);
});

test('malformed picks defer behind real phase/mode validation and rejected request IDs remain reusable', () => {
  for (const id of ['protocol-created-play-picks-priority', 'protocol-created-ult-picks-priority', 'protocol-invalid-mode-picks-priority']) {
    const row = get(id), index = row.steps.findIndex(s => s.errorCode); const rejected = row.steps[index], next = row.steps[index + 1];
    assert.equal(rejected.request.args.picks, true); assert.equal(rejected.errorCode, id.includes('invalid-mode') ? 'INVALID_MODE' : 'WRONG_PHASE');
    assert.deepEqual(rejected.state, index ? row.steps[index - 1].state : row.created.state); assert.equal(next.request.rid, rejected.request.rid); assert.equal(next.outcome, 'committed');
  }
  const row = get('protocol-receipts-play-replay-order'), replay = row.steps.find(s => s.outcome === 'replayed');
  assert.equal(replay.response.v, 2); assert.equal(replay.state.v, 3); assert.ok(Object.hasOwn(replay.receipts, '__proto__'));
});

test('hand creation and delivered copies honor the cap after normal drawing, and logs retain the newest400', () => {
  const generated = get('edge-hand-overflow-generated').steps.at(-1).state; assert.equal(generated.zones.hand.length, 10); assert.equal(generated.zones.discard[4], 'c23');
  const copy = get('edge-copy-delivery-after-draw-overflow').steps.at(-1); assert.equal(copy.state.zones.hand.length, 10); assert.equal(copy.state.zones.discard.at(-1), 'c16');
  assert.ok(copy.response.ev.findIndex(e => e.e === 'burnCard') < copy.response.ev.findIndex(e => e.e === 'echoCopy'));
  const log = get('edge-log400-after-real-commands').steps.at(-1).state.log; assert.equal(log.length, 400); assert.deepEqual([log[0].t, log[0].e], [17, 'play']); assert.equal(log.at(-1).turn, 46);
});

test('the original base0 conflict and genuinely un-authored states stay explicit', () => {
  const f = fixture(); assert.equal(f.unresolved.base0.status, 'ORIGINAL_SOURCE_CONFLICT'); assert.match(f.unresolved.base0.retained, /UNRESOLVED/);
  const row = get('card-sion_forget-now'), resolved = row.steps.find(s => s.response.ev.some(e => e.e === 'resolved' && e.id === 'sion_forget'));
  assert.equal(resolved.state.block, 5 + Math.min(2 * 5, 10)); assert.ok(f.reachability.positiveSilenceUntil.startsWith('UNREACHABLE'));
  assert.deepEqual(f.reachability.runOnlyRelics, ['virtue_of_refusal', 'forge_ember', 'old_compass', 'lucky_coin']);
  assert.equal(f.reachability.runAcceptance, 'PENDING');
});

test('each authored effect source path is lossless and its declared execution cases exist', () => {
  // RULES_NOW_PLAY: rv/ra/rn/next/after fields and com_anchor's slot op were removed from authored effects.
  const f = fixture(), ids = new Set(f.cases.map(c => c.id)); assert.equal(f.coverage.effectFields.length, 30);
  assert.equal(f.authoredEffects.length, 294);
  for (const e of f.authoredEffects) {
    assert.ok(e.source.startsWith('data/game_data.json#/')); let raw = data;
    for (const key of e.source.split('#/')[1].split('/')) raw = raw[key];
    assert.equal(raw.op, e.op); assert.equal(stateHash(raw), e.shapeHash); assert.ok(e.caseIds.length); assert.ok(e.caseIds.every(id => ids.has(id)));
  }
  for (const c of f.cases) for (const s of c.steps) for (const e of s.response?.ev || []) {
    if (e.e === 'play') assert.ok(Object.hasOwn(e, 'target'));
    if (e.e === 'resolved') { assert.ok(Object.hasOwn(e, 'reserved')); assert.equal(typeof e.fizzled, 'boolean'); }
    if (e.e === 'dmg' && e.src === 'ult') { assert.ok(Object.hasOwn(e, 'card')); assert.equal(e.card, null); }
  }
});

test('saved gzip is the exact fresh source-pinned fixture', () => {
  const saved = JSON.parse(gunzipSync(readFileSync(fixturePath))); assert.deepEqual(saved, fixture());
});

test('fresh regeneration rejects missing coverage, stale provenance, changed diagnostics and forged witnesses', () => {
  assert.deepEqual(checkFixture(fixture()), fixture().counts);
  for (const change of [
    f => { f.sources.engine.sha256 = '0'.repeat(64); }, f => { f.cases.pop(); },
    f => { f.cases.find(c => c.group === 'card').mainOutcome.fizzled = true; },
    f => { f.cases[0].steps[0].state.log.push({ t: 1, e: 'FORGED_LOG' }); },
    f => { f.cases.find(c => c.witnesses?.length).witnesses[0].facts[0].value = 999999; },
    f => { f.status = 'IN_PROGRESS'; },
  ]) { const changed = clone(fixture()); change(changed); assert.throws(() => checkFixture(changed)); }
});

test('atomic export is deterministic and real filesystem failures preserve existing targets', () => {
  const dir = mkdtempSync(join(tmpdir(), 'mf-player-actions-'));
  try {
    const a = join(dir, 'a.json.gz'), b = join(dir, 'b.json.gz'); const info = exportVectors(a);
    exportVectors(b); assert.deepEqual(readFileSync(a), readFileSync(b));
    assert.equal(info.sha256, createHash('sha256').update(readFileSync(a)).digest('hex'));
    assert.deepEqual(JSON.parse(gunzipSync(readFileSync(a))), fixture());
    const blockedParent = join(dir, 'file-parent'); writeFileSync(blockedParent, 'KEEP_PARENT');
    assert.throws(() => exportVectors(join(blockedParent, 'out.json.gz')), { code: 'ENOTDIR' }); assert.equal(readFileSync(blockedParent, 'utf8'), 'KEEP_PARENT');
    const blockedOutput = join(dir, 'existing-directory'); mkdirSync(blockedOutput); writeFileSync(join(blockedOutput, 'sentinel'), 'KEEP_DIRECTORY');
    assert.throws(() => exportVectors(blockedOutput), e => ['EISDIR', 'ENOTEMPTY', 'EPERM'].includes(e.code));
    assert.equal(readFileSync(join(blockedOutput, 'sentinel'), 'utf8'), 'KEEP_DIRECTORY'); assert.ok(!readdirSync(dir).some(n => n.startsWith('.battle-player-actions-')));
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test('all37 authored ops,12 counters and both branches of10 authored conditions have literal committed witnesses', () => {
  const ops = new Set(), counters = new Set(), conditions = new Set();
  const visit = value => {
    if (!value || typeof value !== 'object') return;
    if (typeof value.op === 'string') ops.add(value.op);
    if (typeof value.per === 'string') counters.add(value.per);
    if (typeof value.c === 'string') conditions.add(value.c);
    for (const child of Object.values(value)) visit(child);
  };
  visit(data.cards); visit(data.characters.map(c => ({ ult: c.ult, resonance: c.resonance })));
  assert.equal(ops.size, 37); assert.equal(counters.size, 12); assert.equal(conditions.size, 10); assert.ok(!ops.has('pull') && !ops.has('slot'));
  const expected = { op: [...ops], counter: [...counters], condition: [...conditions].flatMap(c => [`${c}:false`, `${c}:true`]) };
  const observed = { op: new Set(), counter: new Set(), condition: new Set() };
  for (const row of fixture().cases) for (const w of row.witnesses || []) {
    assert.equal(row.steps[w.stepIndex].outcome, 'committed');
    assert.ok(w.facts.length, `${row.id}: empty evidence`);
    if (observed[w.kind]) observed[w.kind].add(w.tag);
  }
  for (const kind of Object.keys(expected)) assert.deepEqual([...observed[kind]].sort(), expected[kind].sort(), kind);
});
