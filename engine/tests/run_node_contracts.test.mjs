// Scoped Claude implementation: actual public-command regressions, original data and values preserved.
// Every case starts from the real Run.create and replays the immutable recorded public commands; no state is injected.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { readFileSync } from 'node:fs';
import { generateScenario, replayScenario, stateHash } from '../../tools/gen_traces.mjs';

const require = createRequire(import.meta.url);
const MF = require('../rules-core.js');
const dataBytes = readFileSync(new URL('../../data/game_data.json', import.meta.url));
const data = JSON.parse(dataBytes.toString('utf8'));
const vectors = JSON.parse(readFileSync(new URL('./vectors/run_node_contracts.json', import.meta.url), 'utf8'));
const clone = value => JSON.parse(JSON.stringify(value));
const keys = deck => deck.map(card => card.key);
const runMethods = new Set(['enter', 'evolve', 'takeCard', 'skipCard', 'takeRelic', 'takePart', 'applyFoundPart',
  'proceed', 'rest', 'forgeOffer', 'forgeApply', 'forgeRemove', 'buy', 'eventChoose', 'archive']);

// Same dispatch as tools/gen_traces.mjs: positional Run methods, battle commands through Run.battleCmd (including afterBattle).
function dispatch(run, command) {
  if (command.scope === 'battle') {
    assert.ok(run.battle, 'battle command without an active battle');
    assert.equal(command.v, run.battle.s.v);
    assert.equal(command.rid, `r${run.battle.s.v}-${command.kind}`);
    return run.battleCmd(command.kind, clone(command.args));
  }
  assert.equal(command.scope, 'run');
  assert.ok(runMethods.has(command.kind), `unknown Run command ${command.kind}`);
  return run[command.kind](...clone(command.args));
}
// No mutation means the complete Run JSON (RNG, log, pending, battle), not stateHash, which omits log/ev.
function rejects(run, action, code) {
  const before = JSON.stringify(run.s);
  assert.throws(action, error => { assert.ok(error instanceof MF.RuleError, error); assert.equal(error.code, code); return true; });
  assert.equal(JSON.stringify(run.s), before, `${code} rejection changed the run`);
}
function attempt(run, command) {
  const before = JSON.stringify(run.s);
  try { dispatch(run, command); return null; } catch (error) {
    if (!(error instanceof MF.RuleError)) throw error;
    assert.equal(JSON.stringify(run.s), before, `rejected ${command.scope}:${command.kind} changed the run`);
    return error.code;
  }
}
// Real Create plus the first `upto` recorded steps (default all); every recorded outcome must reproduce. `after` observes the live run.
function replay(name, { upto, after, from = vectors.cases } = {}) {
  const c = from[name];
  const run = MF.Run.create(data, clone(c.setup));
  c.prefix.slice(0, upto).forEach((step, index) => {
    const code = attempt(run, step.command);
    assert.deepEqual([code ? 'rejected' : 'committed', code], [step.outcome, step.errorCode], `${name} step ${index} ${step.command.kind}`);
    after?.(index, run);
  });
  return run;
}
// A committed node entry is the last log line, and a battle that survives begin waits in the player phase.
function enteredNode(run, step) {
  if (step.command.kind !== 'enter' || step.outcome !== 'committed') return;
  const node = run.nodeById(step.command.args[0]);
  assert.equal(run.s.log.at(-1), `${MF.NODE_NAME[node.type]} 진입`);
  if (['battle', 'elite', 'boss'].includes(node.type)) assert.deepEqual([run.s.phase, run.battle.s.phase], ['BATTLE', 'PLAYER']);
}

test('immutable witnesses: frozen original data, three real full runs, self-consistent recorded outcomes', () => {
  assert.equal(vectors.schema, 'mf-run-node-contract-inputs-v1');
  assert.equal(createHash('sha256').update(dataBytes).digest('hex'), vectors.sourceData);
  const runs = Object.fromEntries(Object.entries(vectors.cases).map(([name, c]) => [name, [c.setup.seed, c.setup.length, c.setup.memoryPolicy, c.prefix.length > 0]]));
  assert.deepEqual(runs, { repeatEvent: [1000, 'full', 'legacy', true], nextApLifetime: [1036, 'full', 'legacy', true], secondShopRemoval: [1020, 'full', 'legacy', true] });
  for (const c of Object.values(vectors.cases)) for (const step of c.prefix) assert.equal(step.outcome === 'committed', step.errorCode === null);
});

test('seed1036: ev_rift AP+2 reaches only the next battle first turn and afterBattle no longer refunds it', () => {
  const c = vectors.cases.nextApLifetime;
  const bonus = data.events.find(ev => ev.id === 'ev_rift').choices[0].fx.nextApBonus;
  const [first, victory, second] = c.observations;
  for (const o of c.observations) assert.equal(c.prefix[o.at].command.kind, o.kind);
  assert.equal(c.prefix[c.eventCommand].command.kind, 'eventChoose');
  const firstEnd = c.prefix.findIndex((step, index) => index > first.at && step.command.kind === 'end' && step.outcome === 'committed');
  assert.ok(first.at < firstEnd && firstEnd < victory.at);
  const watch = new Set([c.eventCommand - 1, c.eventCommand, first.at - 1, first.at, firstEnd, victory.at, second.at]);
  const seen = {};
  let firstBattle = null;
  const run = replay('nextApLifetime', { after(index, live) {
    if (index === first.at) firstBattle = live.battle;
    if (!watch.has(index)) return;
    const b = live.battle;
    seen[index] = { phase: live.s.phase, flags: clone(live.s.runFlags), pending: clone(live.s.pending),
      battle: b && { turn: b.s.turn, ap: b.s.ap, apNext: b.s.apNext, flags: clone(b.s.runFlags) },
      firstBattleFlags: firstBattle && clone(firstBattle.s.runFlags) };
  } });

  // ev_rift choice 0 is the only AP source here: base = 4 + chrono_crown (zone-1 boss relic), no cracked_hourglass.
  assert.ok(run.s.relics.includes('chrono_crown') && !run.s.relics.includes('cracked_hourglass'));
  const base = data.rules.apPerTurn + 1;
  assert.deepEqual([bonus, base], [2, 5]);
  assert.equal(seen[c.eventCommand - 1].flags.nextApBonus, 0);
  const rift = seen[c.eventCommand];
  assert.deepEqual([rift.pending.id, rift.pending.done, rift.pending.choice, rift.flags.nextApBonus], ['ev_rift', true, 0, bonus]);
  const before = seen[first.at - 1].flags;
  assert.equal(before.nextApBonus, bonus);

  // The first battle after the event keeps the original, unchanged +2 on its first turn (observed AP7).
  const one = seen[first.at];
  assert.deepEqual([one.phase, one.battle.turn, one.battle.ap, one.flags.nextApBonus], [first.phase, first.battleTurn, first.ap, first.runBonus]);
  assert.equal(one.battle.ap, base + bonus);

  // Victory: the original merged the Battle copy's 2 back here (observed runBonus 2); the Run flag now stays consumed.
  const won = seen[victory.at];
  assert.equal(won.phase, victory.phase);
  assert.equal(won.flags.nextApBonus, 0, 'afterBattle refunded the consumed event AP bonus');

  // The next battle starts at the plain base AP (the original observed AP7 again).
  const two = seen[second.at];
  assert.deepEqual([two.phase, two.battle.turn, two.flags.nextApBonus], [second.phase, second.battleTurn, second.runBonus]);
  assert.equal(two.battle.ap, base, 'a later battle repeated the one-shot event AP bonus');

  // Root cause: no Battle copy carries the bonus (the original copied 2); unrelated persistent flags are untouched.
  assert.deepEqual(one.battle.flags, { ...before, nextApBonus: 0 });
  assert.deepEqual(one.flags, { ...before, nextApBonus: 0 });
  assert.equal(won.firstBattleFlags.nextApBonus, 0);
  assert.deepEqual(won.flags, { ...before, nextApBonus: 0 });
  assert.deepEqual([two.battle.flags.nextApBonus, two.battle.apNext], [0, 0]);

  // No second-turn bonus: apNext, the only Battle carrier, was already spent when turn 1 started.
  assert.equal(one.battle.apNext, 0);
  assert.equal(seen[firstEnd].battle.turn, 2);
});

test('seed1020: a shop sells its single card removal once; a second public removal is NOT_FOR_SALE with no mutation', () => {
  const c = vectors.cases.secondShopRemoval;
  const last = c.prefix.length - 1;
  assert.deepEqual(c.prefix[last].command, { scope: 'run', kind: 'buy', args: ['remove', null, 'k5'] });
  assert.deepEqual(c.attempt, { scope: 'run', kind: 'buy', args: ['remove', null, 'k1'] });
  const run = replay('secondShopRemoval', { upto: last });
  const shop = run.s.pending;
  // §7.3 price of this run's first shop removal: 50 + 25 × 0.
  assert.deepEqual([run.s.phase, shop.type, run.s.runFlags.removals || 0, shop.removeCost], ['SHOP', 'shop', 0, 50]);
  const coins = run.s.coins, deck = keys(run.s.deck);
  assert.equal(attempt(run, c.prefix[last].command), null);
  assert.deepEqual([run.s.coins, run.s.runFlags.removals, shop.removeCost], [coins - 50, 1, null]);
  assert.deepEqual(keys(run.s.deck), deck.filter(key => key !== 'k5'));
  assert.deepEqual({ coins: run.s.coins, deck: run.s.deck.length, removeCost: shop.removeCost, removals: run.s.runFlags.removals }, c.observed);

  // The second request names a real removable card, so only the used service can refuse it (the original removed it for 0 coins).
  assert.ok(run.s.deck.some(card => card.key === c.attempt.args[2] && !card.weapon));
  rejects(run, () => dispatch(run, c.attempt), 'NOT_FOR_SALE');
  // As for sold wares, availability is checked before the card.
  rejects(run, () => run.buy('remove', null, run.s.deck.find(card => card.weapon).key), 'NOT_FOR_SALE');
  rejects(run, () => run.buy('remove', null, 'missing'), 'NOT_FOR_SALE');

  // The rest of the same shop is unaffected.
  const ware = shop.cards.find(item => !item.sold), paid = run.s.coins;
  assert.equal(attempt(run, { scope: 'run', kind: 'buy', args: ['cards', ware.id] }), null);
  assert.deepEqual([run.s.coins, ware.sold, run.s.runFlags.removals], [paid - ware.price, true, 1]);

  // A later shop prices its own single removal from the unchanged count: 50 + 25 × 1.
  run.proceed();
  assert.equal(run.makeShop().removeCost, 75);
});

test('seed1000: a resolved ev_archive rejects reselection with BAD_CHOICE before effects or RNG; phase order kept', () => {
  const c = vectors.cases.repeatEvent;
  const last = c.prefix.length - 1;
  assert.deepEqual(c.prefix[last].command, c.attempt);
  assert.deepEqual(c.attempt, { scope: 'run', kind: 'eventChoose', args: [0, 'k2'] });
  const run = replay('repeatEvent', { upto: last });
  assert.deepEqual([run.s.phase, run.s.pending.id, run.s.pending.done], ['EVENT', 'ev_archive', undefined]);
  const maxHp = run.s.maxHp, deck = clone(run.s.deck);
  assert.ok(deck.some(card => card.key === 'k2' && !card.weapon));

  // Normal first selection with one key (§7.7 정리한다): maxHp -5 at reference party HP, k2 plus one auto-picked card.
  assert.equal(attempt(run, c.prefix[last].command), null);
  assert.equal(run.s.maxHp, maxHp - 5);
  assert.equal(run.s.deck.length, deck.length - 2);
  assert.ok(!run.s.deck.some(card => card.key === 'k2'));
  assert.equal(run.s.deck.filter(card => card.weapon).length, deck.filter(card => card.weapon).length);
  assert.deepEqual([run.s.pending.done, run.s.pending.choice], [true, 0]);
  assert.deepEqual([run.s.hp, run.s.maxHp, run.s.coins, run.s.deck.length], [149, 238, 138, 19]); // witnessed after the first selection

  // Reselection must not apply maxHp 238 -> 233 and two more removals (nor any other choice) again.
  rejects(run, () => dispatch(run, c.attempt), 'BAD_CHOICE');
  for (const args of [[1], [2], [0, keys(run.s.deck).slice(0, 2)], [3]]) rejects(run, () => run.eventChoose(...args), 'BAD_CHOICE');

  // Leaving still works, nothing event-specific outlives the pending, and outside EVENT the phase check still comes first.
  const flags = clone(run.s.runFlags);
  run.proceed();
  assert.deepEqual([run.s.phase, run.s.pending, run.s.runFlags], ['MAP', null, flags]);
  rejects(run, () => dispatch(run, c.attempt), 'WRONG_PHASE');
});

test('seed1000: a key-array first ev_archive choice removes exactly the chosen cards, once', () => {
  const c = vectors.cases.repeatEvent;
  const run = replay('repeatEvent', { upto: c.prefix.length - 1 });
  const deck = keys(run.s.deck), maxHp = run.s.maxHp;
  // The last two removable cards, so neither can be mistaken for the curse/first-card auto-pick.
  const chosen = keys(run.s.deck.filter(card => !card.weapon && card.owner !== 'curse')).slice(-2);
  assert.equal(chosen.length, 2);
  assert.equal(attempt(run, { scope: 'run', kind: 'eventChoose', args: [0, chosen] }), null);
  assert.deepEqual(keys(run.s.deck), deck.filter(key => !chosen.includes(key)));
  assert.equal(run.s.maxHp, maxHp - 5);
  rejects(run, () => run.eventChoose(0, chosen), 'BAD_CHOICE');
});

test('every witnessed event resolves once; rejected reselections leave all later recorded commands reproducible', () => {
  for (const [name, c] of Object.entries(vectors.cases)) {
    let resolved = 0;
    replay(name, { after(index, run) {
      const step = c.prefix[index];
      if (step.command.kind !== 'eventChoose' || step.outcome !== 'committed') return;
      assert.deepEqual([run.s.phase, run.s.pending.done, run.s.pending.choice], ['EVENT', true, step.command.args[0]]);
      const event = data.events.find(ev => ev.id === run.s.pending.id);
      for (let choice = 0; choice < event.choices.length; choice++) rejects(run, () => run.eventChoose(choice, ...clone(step.command.args.slice(1))), 'BAD_CHOICE');
      resolved += 1;
    } });
    assert.equal(resolved, 1, name);
  }
});

test('continuation witnesses: separate real full runs recorded on engine 3ad, self-consistent outcomes', () => {
  const { sourceEngine, ...runs } = vectors.continuations;
  assert.equal(sourceEngine, '3ad9751b24e514199b05c804a8d4eb4d2e6b7cf0069cd9393b4988fe77a3e0f2');
  const shape = Object.fromEntries(Object.entries(runs).map(([name, c]) => [name, [c.setup.seed, c.setup.length, c.setup.memoryPolicy, c.prefix.length]]));
  assert.deepEqual(shape, { nextEvent: [1000, 'full', 'legacy', 119], openingDefeat: [1102, 'full', 'legacy', 221] });
  for (const c of Object.values(runs)) for (const step of c.prefix) assert.equal(step.outcome === 'committed', step.errorCode === null);
});

test('seed1102: a battle that ends inside begin finishes the run once through the defeat save, after the node entry', () => {
  const c = vectors.continuations.openingDefeat, obs = c.observations;
  const last = c.prefix.length - 1, enter = c.prefix[last].command;
  assert.deepEqual(enter, { scope: 'run', kind: 'enter', args: [obs.lastNode] });
  const run = replay('openingDefeat', { from: vectors.continuations, upto: last, after: (index, live) => enteredNode(live, c.prefix[index]) });
  const node = run.nodeById(obs.lastNode), entry = `${MF.NODE_NAME[node.type]} 진입`;
  assert.deepEqual([run.s.phase, run.s.hp, run.battle, run.available().includes(obs.lastNode)], ['MAP', obs.beforeHp, null, true]);
  assert.ok(['battle', 'elite'].includes(node.type) && obs.beforeHp <= obs.initialBattleHpLoss);
  // infinite_circuit (taken as the zone-1 boss relic) costs party HP at turn start, before the first draw.
  assert.ok(run.s.relics.includes('infinite_circuit'));
  const base = clone(run.s); // independent baseline: coins, stats, deck, flags, RNG and log before the last entry

  // Recording-only wrapper: keeps the live Battle the finalizer receives, then runs the unchanged original.
  const finals = [];
  run.afterBattle = function () {
    finals.push({ battle: this.battle, phase: this.s.phase, log: clone(this.s.log), rng: clone(this.s.rng) });
    return MF.Run.prototype.afterBattle.call(this);
  };
  assert.equal(attempt(run, enter), null);
  delete run.afterBattle;
  assert.equal(run.s.phase, obs.terminalPhase, 'a battle that ended inside begin left the run in BATTLE');
  assert.equal(finals.length, 1, 'the run finalizer must run exactly once');
  const [final] = finals, live = final.battle;

  // The live battle ran begin once (no rerun, no other command), lost the recorded HP before any draw, and ended there.
  assert.deepEqual([live.s.phase, live.s.hp, live.s.turn, live.s.v, Object.keys(live.receipts)], [obs.terminalPhase, 0, 1, 1, ['r0-begin']]);
  assert.deepEqual(live.s.ev.filter(e => e.e === 'hpLoss'), [{ e: 'hpLoss', n: obs.initialBattleHpLoss, src: 'relic' }]);
  assert.deepEqual(live.s.ev.filter(e => e.e === 'end'), [{ e: 'end', phase: obs.terminalPhase }]);
  assert.deepEqual([live.s.zones.hand, live.s.ev.some(e => e.e === 'turn')], [[], false]);

  // Finalized from the started battle, after the entry was logged; the defeat drew no Run RNG.
  assert.deepEqual([final.phase, final.log.at(-1)], ['BATTLE', entry]);
  assert.deepEqual(clone(run.s.rng), final.rng);
  assert.notDeepEqual(final.rng, base.rng);

  // §8 defeat: HP 0, result at this floor, the defeat save pending, no battle left on the run.
  assert.deepEqual([run.s.hp, run.s.result, run.s.pending, run.battle, run.s.battle], [0, { outcome: 'defeat', floor: node.f }, { type: 'save', defeat: true }, null, null]);
  // Legacy policy: captureMemory stores memoryAfter only for v2; the legacy save reads the defeated deck itself.
  assert.equal(Object.hasOwn(run.s, 'memoryAfter'), false);
  // Defeat pays nothing (coins, battles, kills, elites, offers, cards, reward) and refunds no one-shot flag: only the entry,
  // the battle start and the loss differ from the baseline; the log ends entry, then 패배.
  assert.deepEqual(clone(run.s), { ...base, pos: obs.lastNode, floor: node.f, rng: final.rng, battleType: node.type, phase: obs.terminalPhase, hp: 0,
    runFlags: { ...base.runFlags, nextApBonus: 0 }, deck: base.deck.map(card => ({ ...card, growth: card.growth || 0 })),
    result: { outcome: 'defeat', floor: node.f }, pending: { type: 'save', defeat: true }, battle: null, log: [...base.log, entry, '패배'].slice(-100) });

  // Nothing outlives the defeat: later commands are refused without mutation.
  rejects(run, () => dispatch(run, enter), 'NODE_UNREACHABLE');
  rejects(run, () => run.battleCmd('end', {}), 'NO_BATTLE');
  for (const action of [() => run.proceed(), () => run.archive('continue'), () => run.rest('heal'), () => run.eventChoose(0), () => run.buy('remove', null, run.s.deck[0].key)]) rejects(run, action, 'WRONG_PHASE');
  rejects(run, () => run.skipCard(), 'BAD_REWARD');

  // The expedition is not stuck: the defeat save keeps at most one change and then completes.
  const changes = Object.entries(run.memoryChanges()).flatMap(([cid, info]) => info.changes.map((change, index) => ({ cid, index, fits: change.cost <= info.capacity })));
  const keep = list => list.reduce((selection, { cid, index }) => ({ ...selection, [cid]: [...(selection[cid] || []), index] }), {});
  const one = changes.find(change => change.fits);
  assert.ok(changes.length >= 2 && one);
  rejects(run, () => run.commitMemory(keep(changes.slice(0, 2))), 'DEFEAT_KEEP_ONE');
  run.commitMemory(keep([one]));
  assert.deepEqual([run.s.phase, run.s.pending], ['DONE', { type: 'done' }]);
});

test('seed1000 continuation: repeats of a done ev_archive change nothing, and the next ev_promise still commits once', () => {
  const c = vectors.continuations.nextEvent;
  const [archive, promise] = c.observations;
  assert.deepEqual([c.prefix.length, archive.at, archive.event, promise.at, promise.event], [119, 96, 'ev_archive', 118, 'ev_promise']);
  for (const o of c.observations) assert.deepEqual([c.prefix[o.at].command.kind, c.prefix[o.at].command.args[0], c.prefix[o.at - 1].command.kind], ['eventChoose', o.choice, 'enter']);
  const plain = replay('nextEvent', { from: vectors.continuations });
  let repeated = 0;
  const run = replay('nextEvent', { from: vectors.continuations, after(index, live) {
    const step = c.prefix[index];
    enteredNode(live, step);
    // Each recorded choice meets a genuinely new pending from its own node entry.
    const next = c.observations.find(o => o.at === index + 1);
    if (next) assert.deepEqual([live.s.phase, live.s.pending, live.s.runFlags.nextApBonus], ['EVENT', { type: 'event', id: next.event }, next.beforeBonus]);
    const o = c.observations.find(x => x.at === index);
    if (!o) return;
    assert.deepEqual([live.s.phase, live.s.pending.id, live.s.pending.done, live.s.pending.choice, live.s.runFlags.nextApBonus], ['EVENT', o.event, o.done, o.choice, o.afterBonus]);
    const event = data.events.find(ev => ev.id === o.event);
    for (let choice = 0; choice < event.choices.length; choice++) rejects(live, () => live.eventChoose(choice, ...clone(step.command.args.slice(1))), 'BAD_CHOICE');
    rejects(live, () => dispatch(live, step.command), 'BAD_CHOICE');
    repeated += 1;
  } });
  assert.equal(repeated, 2);
  // The rejected repeats left no trace: the complete Run JSON (RNG, log, pending, deck) equals the plain replay.
  assert.equal(JSON.stringify(run.s), JSON.stringify(plain.s), 'rejected repeats changed the continuation');
});

test('seed2654 packet: each newly earned ev_rift +2 reaches only its next battle; the battles between keep the base AP', () => {
  const c = vectors.reearnedRift, [first, second] = c.observations;
  assert.equal(c.sourceEngine, 'dae99192bfa137762c1c863efda8207b3851e3a6e7ecf2ff029bed3585a33ab4');
  assert.deepEqual([c.setup.seed, c.setup.length, c.setup.memoryPolicy, c.setup.relics, c.prefix.length], [2654, 'full', 'legacy', [], 1525]);
  for (const step of c.prefix) assert.equal(step.outcome === 'committed', step.errorCode === null);
  // Independent base: original rules.apPerTurn; ev_rift choice 0 is the only nextApBonus source in the original data.
  const bonus = data.events.find(ev => ev.id === 'ev_rift').choices[0].fx.nextApBonus, base = data.rules.apPerTurn;
  assert.deepEqual([bonus, base], [2, 4]);
  assert.deepEqual(data.events.flatMap(ev => ev.choices.filter(ch => ch.fx?.nextApBonus).map(() => ev.id)), ['ev_rift']);
  assert.deepEqual(c.observations.map(o => [o.at, o.event, o.choice, o.beforeBonus, o.afterBonus]), [[78, 'ev_rift', 0, 0, bonus], [1519, 'ev_rift', 0, 0, bonus]]);

  const plain = replay('reearnedRift', { from: vectors });
  const battles = [], resolved = [], bonusChanges = [];
  let eventEntry = null, bonusBefore = 0;
  const run = replay('reearnedRift', { from: vectors, after(index, live) {
    const step = c.prefix[index], bonusNow = live.s.runFlags.nextApBonus;
    enteredNode(live, step);
    if (bonusNow !== bonusBefore) bonusChanges.push([index, bonusBefore, bonusNow]);
    bonusBefore = bonusNow;
    if (step.command.kind === 'enter' && step.outcome === 'committed') {
      const b = live.battle?.s;
      if (live.nodeById(step.command.args[0]).type === 'event') eventEntry = { at: index, pending: live.s.pending };
      if (b) battles.push({ at: index, phase: live.s.phase, turn: b.turn, ap: b.ap, apNext: b.apNext, runBonus: bonusNow, copiedBonus: b.runFlags.nextApBonus, relics: [...live.s.relics] });
    }
    const next = c.observations.find(o => o.at === index + 1);
    if (next) assert.deepEqual([live.s.phase, live.s.pending, bonusNow], ['EVENT', { type: 'event', id: next.event }, next.beforeBonus]);
    const o = c.observations.find(x => x.at === index);
    if (!o) return;
    // The recorded choice resolves the fresh pending its own node entry created, then every repeat is refused.
    assert.equal(live.s.pending, eventEntry.pending);
    assert.deepEqual([live.s.phase, live.s.pending, bonusNow], ['EVENT', { type: 'event', id: o.event, done: o.done, choice: o.choice }, o.afterBonus]);
    resolved.push(eventEntry.at);
    const event = data.events.find(ev => ev.id === o.event);
    for (let choice = 0; choice < event.choices.length; choice++) rejects(live, () => live.eventChoose(choice, ...clone(step.command.args.slice(1))), 'BAD_CHOICE');
    rejects(live, () => dispatch(live, step.command), 'BAD_CHOICE');
  } });

  // Two distinct pendings from two node entries, each resolved once.
  assert.equal(resolved.length, 2);
  assert.ok(resolved[0] < first.at && first.at < resolved[1] && resolved[1] < second.at);
  // Over all 1525 commands the Run flag rises only at the two choices and is spent only by the next battle entry (never refunded).
  const nextBattle = at => battles.find(b => b.at > at).at;
  assert.deepEqual(bonusChanges, [[first.at, 0, bonus], [nextBattle(first.at), bonus, 0], [second.at, 0, bonus], [nextBattle(second.at), bonus, 0]]);
  // Every battle entered after each event is witnessed in order; only the first after each event gets the +2 (AP6), the rest AP4.
  const afterFirst = battles.filter(b => b.at > first.at && b.at < second.at), afterSecond = battles.filter(b => b.at > second.at);
  assert.deepEqual([afterFirst, afterSecond], [first.battles, second.battles]);
  for (const list of [afterFirst, afterSecond]) list.forEach((b, k) => {
    assert.deepEqual([b.phase, b.turn, b.ap, b.apNext, b.runBonus, b.copiedBonus], ['BATTLE', 1, base + (k === 0 ? bonus : 0), 0, 0, 0], `battle at ${b.at}`);
    assert.ok(!b.relics.includes('chrono_crown') && !b.relics.includes('cracked_hourglass'));
  });
  assert.deepEqual([afterFirst.length, afterSecond.length], [9, 1]);
  // The rejected repeats left no trace: the complete Run JSON (RNG, log, pending, battle) equals the plain replay.
  assert.equal(JSON.stringify(run.s), JSON.stringify(plain.s), 'rejected repeats changed the packet');
});

// The corpus generator's own policy plays seed 1102 into the same opening defeat; only its legitimate options differ.
for (const memoryPolicy of ['legacy', 'v2']) test(`seed1102 generated ${memoryPolicy} trace: the opening defeat finishes through its save to DONE`, () => {
  const c = vectors.continuations.openingDefeat, obs = c.observations;
  const trace = generateScenario({ seed: 1102, length: 'full', memoryPolicy });
  assert.equal(trace.sources.data.sha256, vectors.sourceData);
  assert.deepEqual(replayScenario(trace).finalState, trace.finalState);
  assert.equal(trace.summary.outcome, 'defeat');
  const last = trace.commands.findLastIndex(step => step.command.kind === 'enter');
  assert.deepEqual(trace.commands.slice(last).map(step => [step.command.kind, step.outcome]), [['enter', 'committed'], ['commitMemory', 'committed']]);
  assert.deepEqual([last, trace.commands[last].command.args, obs.source], [c.prefix.length - 1, [obs.lastNode], 'infinite_circuit']);
  // Same public path as the frozen witness: legacy exactly, v2 by command and outcome (its card keys are run-scoped strings).
  const shape = steps => steps.map(step => [step.command.scope, step.command.kind, step.outcome, step.errorCode]);
  assert.deepEqual(shape(trace.commands.slice(0, last + 1)), shape(c.prefix));
  if (memoryPolicy === 'legacy') {
    assert.deepEqual(trace.setup, c.setup);
    assert.deepEqual(trace.commands.slice(0, last + 1).map(({ command, outcome, errorCode }) => ({ command, outcome, errorCode })), c.prefix);
  }

  // Live replay from the real Create, tied to the generator by its own state hashes.
  const run = MF.Run.create(data, clone(trace.setup));
  const store = memoryPolicy === 'v2' ? MF.MemoryV2.migrate(data, {}, {}) : null;
  const hash = memory => stateHash({ run: run.s, memory });
  assert.deepEqual(store, trace.initialState.memory);
  if (store) assert.deepEqual([store.version, store.history, store.receipts], [0, [], {}]);
  trace.commands.slice(0, last).forEach((step, index) => {
    assert.equal(attempt(run, step.command), step.errorCode, `${memoryPolicy} step ${index}`);
    enteredNode(run, step);
  });
  assert.equal(hash(store), trace.commands[last - 1].stateHash);
  const node = run.nodeById(obs.lastNode), entry = `${MF.NODE_NAME[node.type]} 진입`;
  assert.deepEqual([run.s.phase, run.s.hp, run.battle, run.available().includes(obs.lastNode), run.s.relics.includes(obs.source)], ['MAP', obs.beforeHp, null, true, true]);
  assert.ok(['battle', 'elite'].includes(node.type));

  const finals = [];
  run.afterBattle = function () { finals.push(this.battle); return MF.Run.prototype.afterBattle.call(this); };
  assert.equal(attempt(run, trace.commands[last].command), null);
  delete run.afterBattle;
  assert.equal(hash(store), trace.commands[last].stateHash);
  assert.equal(finals.length, 1, 'the run finalizer must run exactly once');
  // infinite_circuit's turn-start loss, before any draw, is what ends the battle inside begin.
  const live = finals[0].s;
  assert.deepEqual([live.phase, live.hp, live.turn, live.zones.hand], ['DEFEAT', 0, 1, []]);
  assert.deepEqual(live.ev.filter(e => e.e === 'hpLoss'), [{ e: 'hpLoss', n: obs.initialBattleHpLoss, src: 'relic' }]);
  // The last committed enter does not leave the run in BATTLE: §7.6 defeat with its save pending; entry, then 패배.
  assert.deepEqual([run.s.phase, run.s.hp, run.s.result, run.s.pending, run.battle, run.s.battle], ['DEFEAT', 0, { outcome: 'defeat', floor: node.f }, { type: 'save', defeat: true }, null, null]);
  assert.deepEqual(run.s.log.slice(-2), [entry, '패배']);
  assert.equal(Object.hasOwn(run.s, 'memoryAfter'), memoryPolicy === 'v2');

  // §8.5 / §8.11: keeping two changes is refused without mutation; then the recorded save commits.
  const [request, current] = clone(trace.commands[last + 1].command.args);
  const changes = Object.entries(run.memoryChanges()).flatMap(([cid, info]) => info.changes.map((change, index) => ({ cid, index, id: change.id })));
  assert.ok(changes.length >= 2, 'the defeat save must offer more than one change');
  const two = changes.slice(0, 2);
  rejects(run, () => memoryPolicy === 'v2'
    ? run.commitMemory({ ...request, selectedChangeIds: two.map(change => change.id) }, clone(current))
    : run.commitMemory(two.reduce((keep, { cid, index }) => ({ ...keep, [cid]: [...(keep[cid] || []), index] }), {})), 'DEFEAT_KEEP_ONE');
  const result = run.commitMemory(clone(request), current);
  assert.deepEqual([run.s.phase, run.s.pending, run.s.result], ['DONE', { type: 'done' }, { outcome: 'defeat', floor: node.f }]);
  if (memoryPolicy === 'legacy') {
    // Legacy completion stays distinct: no store and no memoryAfter; the rebuilt deck keeps at most the one change.
    assert.ok(Object.values(request).flat().length <= 1);
    assert.deepEqual([result, trace.finalState.memory], [run.s.savedMemory, null]);
    assert.equal(hash(null), trace.finalStateHash);
    return;
  }
  // v2: the real store goes 0 -> 1 with one history entry and one receipt; the input store itself is untouched.
  assert.deepEqual(current, trace.initialState.memory);
  assert.ok(request.selectedChangeIds.length <= 1);
  const { state } = result;
  assert.deepEqual([state.version, state.history.length, Object.keys(state.receipts)], [1, 1, [request.requestId]]);
  assert.deepEqual([state.history[0].runId, state.history[0].version, state.history[0].selectedChangeIds], [trace.setup.runId, 1, request.selectedChangeIds]);
  // What was stored differs from the run's before snapshot by exactly the selected (0 or 1) change.
  assert.deepEqual(MF.MemoryV2.listChanges(data, run.s.memoryBefore, state.memory).map(change => change.id), request.selectedChangeIds);
  assert.deepEqual(result.response.memory, run.s.savedMemory);
  assert.equal(hash(state), trace.finalStateHash);
});

test('seed1102: a fixed boss that throws inside startBattle writes no entry log, as in the original order', () => {
  const c = vectors.continuations.openingDefeat;
  const at = c.prefix.findIndex(step => step.command.kind === 'enter' && step.command.args[0] === 'z1boss');
  // Legitimate setup only: Run.create keeps opaque fixed-boss ids, and 'missing' is not an authored enemy.
  assert.ok(at > 0 && !data.enemies.some(enemy => enemy.id === 'missing'));
  const run = replay('openingDefeat', { from: { openingDefeat: { ...c, setup: { ...c.setup, bosses: ['missing'] } } }, upto: at });
  assert.deepEqual([run.s.phase, run.s.bosses, run.battle, run.nodeById('z1boss').type, run.available().includes('z1boss')], ['MAP', ['missing'], null, 'boss', true]);
  const log = clone(run.s.log);
  // Battle.addEnemy refuses the unknown id from inside Battle.create, before begin and before the entry log.
  assert.throws(() => dispatch(run, c.prefix[at].command), error => { assert.ok(error instanceof MF.RuleError, error); assert.equal(error.code, 'UNKNOWN_ENEMY'); return true; });
  // Only the log is pinned: pos, floor and the battle-seed draw were already changed before the throw in the original; no rollback is claimed.
  assert.deepEqual(run.s.log, log, 'a failed battle setup logged the node entry');
  assert.deepEqual([run.s.phase, run.battle, run.s.battle], ['MAP', null, null]);
});
