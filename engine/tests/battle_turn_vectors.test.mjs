import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { createHash } from 'node:crypto';
import { mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { gunzipSync } from 'node:zlib';
import { generateCases, checkFixture, exportVectors, fixturePath } from '../../tools/gen_battle_turn_vectors.mjs';
import { generateCases as openingCases } from '../../tools/gen_battle_opening_vectors.mjs';

const require = createRequire(import.meta.url);
const data = require('../../data/game_data.json');
const MF = require('../rules-core.js');
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = value => createHash('sha256').update(value).digest('hex');
const fixture = generateCases();
const byId = id => {
  const row = fixture.cases.find(c => c.id === id);
  assert.ok(row, `Missing turn vector ${id}`);
  return row;
};
const ends = row => row.steps.filter(s => s.request.kind === 'end' && s.outcome === 'committed');
const last = row => row.steps.at(-1);

// Independent sorted replacer, rather than the exporter's canonical serializer.
function hashState(value) {
  const keys = new Set();
  const visit = item => {
    if (!item || typeof item !== 'object') return;
    for (const key of Object.keys(item)) {
      if (key === 'log' || key === 'ev') continue;
      if (!Array.isArray(item)) keys.add(key);
      visit(item[key]);
    }
  };
  visit(value);
  return sha256(JSON.stringify(value, [...keys].sort()));
}

test('turn oracle extends every opening setup and executes two full patterns for every live enemy', () => {
  assert.equal(fixture.schema, 'mf-battle-turns-v1');
  const opening = fixture.cases.filter(c => c.group === 'opening');
  assert.equal(opening.length, openingCases().cases.length, 'every authored opening must reach an end or terminal rejection');
  assert.deepEqual(opening.map(c => c.openingId), openingCases().cases.map(c => c.id));
  assert.equal(new Set(fixture.cases.map(c => c.id)).size, fixture.cases.length);
  for (const enemy of data.enemies) {
    const vector = fixture.cases.find(c => c.id === `enemy-${enemy.id}-two-cycles`);
    assert.ok(vector, `missing live enemy ${enemy.id}`);
    assert.equal(vector.setup.hp, 243);
    assert.equal(vector.setup.maxHp, 243);
    assert.equal(vector.steps.filter(s => s.request.kind === 'end' && s.outcome === 'committed').length, 2 * enemy.pattern.length);
    assert.equal(vector.steps.at(-1).state.turn, 1 + 2 * enemy.pattern.length);
    assert.equal(vector.steps.at(-1).state.phase, 'PLAYER');
  }
});

test('every live encounter and every opening character, resonance, weapon and relic remains present', () => {
  const expected = [];
  for (const [zone, tiers] of Object.entries(data.encounters)) for (const [tier, groups] of Object.entries(tiers)) {
    groups.forEach((enemies, index) => {
      const id = `encounter-${zone}-${tier}-${index}`;
      expected.push(id);
      assert.deepEqual(byId(id).setup.enemies, enemies);
      assert.equal(byId(id).setup.hp, 243);
    });
  }
  assert.deepEqual(fixture.cases.filter(c => c.group === 'encounter').map(c => c.id), expected);
  for (const ch of data.characters) for (let res = 0; res <= 6; res++) byId(`opening-character-${ch.id}-res${res}`);
  for (const weapon of data.weapons) for (let refine = 1; refine <= 5; refine++) byId(`opening-weapon-${weapon.id}-r${refine}`);
  for (const relic of data.relics) byId(`opening-relic-${relic.id}`);
  const tags = new Set();
  const visit = move => { tags.add(move.t); if (move.then) visit(move.then); };
  data.enemies.forEach(e => e.pattern.forEach(visit));
  assert.deepEqual([...tags].sort(), ['atk', 'atkdebuff', 'blockAll', 'buff', 'charge', 'debuff', 'def', 'expose', 'heal', 'insert', 'reflect', 'summon'].sort());
});

test('each checkpoint is reproduced by real creation and requests with complete diagnostics, public view and independent hashes', () => {
  let commands = 0, committed = 0, rejected = 0, replayed = 0;
  const compare = (battle, point, label) => {
    assert.deepEqual(battle.s, point.state, `${label}: complete state`);
    assert.deepEqual(battle.publicView(), point.publicView, `${label}: public view`);
    assert.equal(hashState(point.state), point.stateHash, `${label}: state hash`);
    assert.equal(hashState(point.state.rng), point.rngHash, `${label}: RNG hash`);
    assert.equal(hashState(point.state.ev), point.eventsHash, `${label}: events hash`);
    assert.equal(hashState(point.state.log), point.logHash, `${label}: log hash`);
    assert.equal(hashState(point.publicView), point.publicViewHash, `${label}: public hash`);
    if (point.receipts) assert.deepEqual(clone(battle.receipts), point.receipts, `${label}: receipts`);
    for (const key of ['rng', 'draw', 'zones']) assert.ok(!Object.hasOwn(point.publicView, key), `${label}: hidden ${key}`);
    assert.deepEqual(point.publicView.hand, point.state.zones.hand.map(uid => point.state.cards[uid]));
  };
  for (const row of fixture.cases) {
    const battle = MF.Battle.create(data, clone(row.setup));
    compare(battle, row.created, row.id);
    for (const [index, step] of row.steps.entries()) {
      commands++;
      const before = clone(battle.s), receipts = clone(battle.receipts);
      if (step.outcome === 'rejected') {
        rejected++;
        assert.throws(() => battle.command(clone(step.request)), error => error instanceof MF.RuleError && error.code === step.errorCode);
        assert.equal(step.response, null);
        assert.equal(step.responseEventsHash, null);
        assert.deepEqual(battle.s, before);
        assert.deepEqual(clone(battle.receipts), receipts);
      } else {
        const response = battle.command(clone(step.request));
        assert.deepEqual(response, step.response, row.id);
        assert.equal(hashState(response.ev), step.responseEventsHash);
        if (step.outcome === 'replayed') {
          replayed++;
          assert.deepEqual(battle.s, before);
          assert.deepEqual(clone(battle.receipts), receipts);
        } else {
          committed++;
          assert.equal(step.outcome, 'committed');
          assert.equal(battle.s.v, before.v + 1);
        }
      }
      compare(battle, step, `${row.id}:${index}`);
    }
  }
  assert.deepEqual(fixture.counts, { cases: fixture.cases.length, commands, committed, replayed, rejected });
});

test('ordinary turn two clears opening block/counter without repeating start rewards or carried AP', () => {
  const harin = byId('opening-harin-opening-preserved');
  assert.deepEqual([harin.steps[0].state.block, harin.steps[0].state.counter], [26, 2]);
  assert.deepEqual([ends(harin)[0].state.turn, ends(harin)[0].state.block, ends(harin)[0].state.counter], [2, 0, 0]);
  assert.equal(ends(harin)[0].state.enemies[0].block, 10);
  assert.deepEqual(ends(harin)[0].state.enemies[0].intent, { t: 'atk', d: 25 });
  const moss = byId('opening-seed-moss-single');
  assert.equal(moss.steps[0].state.block, 3);
  assert.equal(ends(moss)[0].state.block, 0);
  assert.equal(ends(moss)[0].state.cards.c1.growth, 1);
  assert.ok(ends(moss).every(s => s.response.ev.every(e => e.e !== 'grow')));
  const draw = byId('opening-draw-max-strongest-only');
  assert.equal(draw.steps[0].state.zones.hand.length, 9);
  assert.equal(draw.steps[0].state.tempParts, 3);
  assert.ok(ends(draw).every(s => s.state.zones.hand.length === 5 && s.state.tempParts === 3));
  assert.ok(ends(draw).every(s => s.response.ev.every(e => e.e !== 'tune')));
  assert.deepEqual(byId('edge-carried-ap-once').steps.map(s => s.state.ap), [6, 5, 5, 5]);
  assert.ok(ends(byId('opening-echo-and-rule-interactions')).every(s => s.state.echo === 1));
});

test('summons wait for their first action, respect alive capacity and receive blockAll in appearance order', () => {
  const garden = byId('enemy-e_gardener-two-cycles');
  const first = ends(garden)[0], second = ends(garden)[1];
  assert.deepEqual(first.state.enemies.map(e => [e.id, e.pi, e.block]), [['e_gardener', 1, 0], ['e_seed', 0, 0], ['e_seed', 0, 0]]);
  assert.deepEqual(first.response.ev, [{ e: 'enemyHeal', uid: 'e16', n: 10 }, { e: 'turn', turn: 2, ap: 4 }]);
  assert.deepEqual(second.state.enemies.slice(1).map(e => e.block), [2, 2]);
  assert.equal(ends(byId('enemy-b_garden-two-cycles'))[0].response.ev[0].n, 14);
  assert.equal(last(byId('edge-summon-cap')).state.enemies.length, 5);
  assert.equal(last(byId('edge-summon-initial-cap')).state.enemies.length, 5);
  assert.deepEqual(ends(byId('edge-summon-before-block-all'))[0].state.enemies.map(e => e.block), [20, 20, 20, 20]);
  assert.deepEqual(ends(byId('edge-summon-after-block-all'))[0].state.enemies.map(e => e.block), [20, 0, 0, 0]);
  const refill = last(byId('edge-summon-refill-dead-slot')).state;
  assert.equal(refill.enemies.filter(e => e.alive).length, 5);
  assert.equal(refill.enemies.length, 6, 'dead seed remains recorded but does not occupy alive capacity');
  assert.equal(refill.seq, 21);
});

test('lowest HP ratio keeps stable ties and reports requested healing rather than only effective healing', () => {
  const tie = ends(byId('edge-heal-lowest-tie'));
  assert.deepEqual(tie[0].state.enemies.slice(1).map(e => e.hp), [8, 8]);
  assert.deepEqual(tie[2].state.enemies.slice(1).map(e => e.hp), [12, 8]);
  assert.deepEqual(tie[2].response.ev[0], { e: 'enemyHeal', uid: 'e17', n: 6 });
  const ratio = ends(byId('edge-heal-ratio-not-hp'))[2];
  assert.deepEqual(ratio.state.enemies.slice(1).map(e => e.hp), [6, 12]);
  assert.deepEqual(ratio.response.ev[0], { e: 'enemyHeal', uid: 'e18', n: 6 });
  assert.deepEqual(ends(byId('edge-heal-full'))[2].response.ev, [{ e: 'enemyHeal', uid: 'e16', n: 6 }, { e: 'reshuffle' }, { e: 'turn', turn: 4, ap: 4 }]);
});

test('a reachable large-integer JS division tie selects the first enemy even when exact rational ratios differ', () => {
  const row = byId('edge-heal-js-rounded-tie');
  assert.equal(row.setup.hp, 243);
  assert.equal(row.setup.difficulty.hp, 2000000009);
  const before = ends(row)[4].state;
  assert.equal(before.hp, 96);
  const [, moth, page] = before.enemies;
  assert.deepEqual([moth.hp, moth.maxHp, page.hp, page.maxHp], [359999996, 360000002, 239999997, 240000001]);
  assert.equal(moth.hp / moth.maxHp, page.hp / page.maxHp);
  assert.equal(BigInt(moth.hp) * BigInt(page.maxHp) - BigInt(page.hp) * BigInt(moth.maxHp), 2n);
  const after = ends(row)[5];
  assert.deepEqual(after.response.ev[0], { e: 'enemyHeal', uid: 'e17', n: 6 });
  assert.deepEqual(after.state.enemies.slice(1).map(e => e.hp), [360000002, 239999997]);
  assert.equal(after.state.hp, 84);
});

test('burn bypasses seed block and raw damage does not apply reflect, card multipliers or break hooks', () => {
  const notebook = ends(byId('edge-notebook-continuing'));
  assert.deepEqual([notebook[0].state.enemies[0].hp, notebook[0].state.enemies[0].block], [2, 2]);
  const state = notebook[1].state;
  assert.equal(state.phase, 'PLAYER');
  assert.equal(state.turn, 3);
  assert.equal(state.zones.hand.length, 6);
  assert.equal(state.killedMarked, 1);
  assert.equal(state.runFlags.coinsBonus, 10);
  assert.equal(state.ap, 4);
  assert.ok(!state.turnFlags.deduction);
  assert.ok(state.enemies.every(e => e.st.burn === 0 || !e.alive), 'Kaya enemy-phase kill cannot create follow-up burn');
  const victory = ends(byId('edge-notebook-victory'))[1].state;
  assert.equal(victory.phase, 'VICTORY');
  assert.equal(victory.runFlags.coinsBonus, 17);
  const mirror = ends(byId('edge-burn-reflect'));
  assert.deepEqual([mirror[0].state.enemies[0].st.reflect, mirror[0].state.enemies[0].hp], [3, 48]);
  assert.equal(mirror[1].state.enemies[0].st.reflect, 0);
  assert.ok(mirror.every(s => s.response.ev.every(e => e.src !== 'reflect')));
  const raw = ends(byId('edge-raw-scaling'))[0];
  assert.deepEqual(raw.response.ev.filter(e => e.e === 'dmg').map(e => [e.n, e.src]), [[3, 'counter'], [3, 'counter'], [6, 'burn']]);
  assert.ok(raw.response.ev.every(e => e.e !== 'break' && e.e !== 'heal'));
  assert.equal(raw.state.enemies[0].brk, 2);
  const rounded = ends(byId('edge-party-power-rounding'))[0];
  assert.deepEqual(rounded.response.ev.filter(e => e.e === 'dmg').map(e => e.n), [2, 2, 4]);
});

test('curse insertion creates conserved tokens and actual reshuffle/draw/cleanup damages without exhausting them', () => {
  const row = byId('edge-insert-cycle');
  const inserted = Object.values(ends(row)[0].state.cards).filter(c => c.token);
  assert.equal(inserted.length, 1);
  assert.deepEqual(inserted[0], { uid: 'c17', id: 'smudge', owner: 'curse', wielder: null, plus: false,
    mods: { c: null, e: null }, temp: [], growth: 0, grown: false, used: 0, costMod: 0, token: true, free: false, assigned: null, from: null });
  assert.deepEqual(ends(row)[0].response.ev[0], { e: 'insert', card: 'smudge', n: 1 });
  assert.ok(ends(row).some(s => s.response.ev.some(e => e.e === 'reshuffle')));
  assert.ok(ends(row).some(s => s.response.ev.some(e => e.e === 'hpLoss' && e.src === 'smudge')));
  const state = last(row).state;
  assert.equal(state.zones.exhaust.length, 0);
  assert.equal(state.exhausted, 0);
  assert.equal(Object.keys(state.cards).length, state.zones.draw.length + state.zones.hand.length + state.zones.discard.length);
  assert.deepEqual(ends(byId('edge-curse-scaled'))[0].response.ev.filter(e => e.e === 'hpLoss').map(e => e.n), [3, 3, 3, 3, 3]);
  assert.ok(ends(byId('edge-penalty-note-inert')).every(s => s.state.hp === 1 && s.state.zones.exhaust.length === 0));
  assert.ok(ends(byId('edge-eye-of-rift-cycle')).some(s => s.response.ev.some(e => e.src === 'smudge')));
  const small = byId('edge-small-deck');
  assert.ok(ends(small).every(s => s.state.zones.hand.length === 1 && s.response.ev[0].e === 'reshuffle'));
  assert.ok(small.steps.every(s => s.rngHash === small.created.rngHash));
});

test('strength persists, party weak expires before the next action, and expose precedes its attack then decays', () => {
  const moth = ends(byId('enemy-e_moth-two-cycles'));
  assert.equal(moth[2].state.enemies[0].st.str, 1);
  assert.equal(moth[3].response.ev[0].d, 13);
  assert.equal(moth[5].state.enemies[0].st.str, 2);
  const keeper = ends(byId('enemy-e_keeper-two-cycles'));
  assert.deepEqual(keeper[1].response.ev.filter(e => e.e === 'hit').map(e => e.d), [15, 15]);
  assert.equal(keeper[2].response.ev[0].d, 22);
  const scribe = ends(byId('enemy-e_scribe-two-cycles'));
  assert.deepEqual(scribe.slice(0, 3).map(s => s.state.pst.weak), [0, 1, 0]);
  assert.ok(scribe[1].response.ev.every(e => e.e !== 'partyDebuff'));
  assert.deepEqual(ends(byId('enemy-e_lantern-two-cycles'))[0].response.ev[0], { e: 'partyDebuff', s: 'weak', n: 1 });
  const bell = ends(byId('enemy-b_bell-two-cycles'));
  assert.deepEqual(bell[2].response.ev.slice(0, 2), [{ e: 'expose', uid: 'e16' }, { e: 'hit', uid: 'e16', d: 52, blocked: 0 }]);
  assert.deepEqual([bell[2].state.enemies[0].st.vul, bell[3].state.enemies[0].st.vul], [1, 0]);
  for (const row of fixture.cases) for (const point of [row.created, ...row.steps]) {
    assert.deepEqual(point.state.res, [], row.id);
    assert.deepEqual(point.state.debts, [], row.id);
    assert.deepEqual(point.state.powers, [], row.id);
    assert.deepEqual(point.state.buffs, [], row.id);
    assert.equal(point.state.exhausted, 0, row.id);
  }
});

test('multi-hit death, envelope, curse cleanup and next-turn circuit stop at their exact terminal boundaries', () => {
  for (const [id, hits, envelopes] of [['first-attack-fatal', 1, 0], ['envelope-multihit', 2, 1], ['envelope-used', 1, 0]]) {
    const end = ends(byId(`edge-${id}`))[0];
    assert.equal(end.state.phase, 'DEFEAT');
    assert.equal(end.state.turn, 1);
    assert.equal(end.response.ev.filter(e => e.e === 'hit').length, hits);
    assert.equal(end.response.ev.filter(e => e.e === 'envelope').length, envelopes);
    assert.equal(end.response.ev.filter(e => e.e === 'end').length, 1);
  }
  for (const id of ['smudge-fatal-loop', 'smudge-fatal-no-envelope']) {
    const end = ends(byId(`edge-${id}`))[0];
    assert.equal(end.response.ev.filter(e => e.e === 'hpLoss').length, 5);
    assert.equal(end.state.zones.hand.length, 0);
    assert.equal(end.state.zones.discard.length, 5);
    assert.ok(end.response.ev.every(e => e.e !== 'hit'));
  }
  const debuff = ends(byId('edge-attack-debuff-fatal'))[1];
  assert.equal(debuff.state.phase, 'DEFEAT');
  assert.equal(debuff.state.pst.weak, 0);
  assert.equal(debuff.state.enemies[0].pi, 2);
  const multiple = ends(byId('edge-multiple-enemy-fatal'))[0];
  assert.deepEqual(multiple.state.enemies.map(e => e.pi), [1, 0]);
  for (const id of ['circuit-next-fatal', 'circuit-envelope-already-used']) {
    const end = ends(byId(`edge-${id}`))[0];
    assert.equal(end.state.turn, 2);
    assert.equal(end.state.phase, 'DEFEAT');
    assert.equal(end.state.zones.hand.length, 0);
    assert.deepEqual(end.response.ev, [{ e: 'hpLoss', n: 2, src: 'relic' }, { e: 'end', phase: 'DEFEAT' }]);
  }
  const circuit = ends(byId('edge-circuit-envelope-next'));
  assert.deepEqual(circuit.map(s => [s.state.turn, s.state.hp, s.state.phase]), [[2, 1, 'PLAYER'], [3, 0, 'DEFEAT']]);
  assert.equal(circuit[0].state.runFlags.envelopeUsed, true);
});

test('a fatal hit keeps its own counter and marked reward, then defeat wins while later hits stop', () => {
  const end = ends(byId('edge-same-hit-defeat-reward'))[0];
  assert.equal(end.state.phase, 'DEFEAT');
  assert.equal(end.state.hp, 0);
  assert.equal(end.state.zones.hand.length, 1);
  assert.equal(end.state.runFlags.coinsBonus, 10);
  assert.deepEqual(end.response.ev, [
    { e: 'hit', uid: 'e16', d: 14, blocked: 10 }, { e: 'hpLoss', n: 4, src: 'enemy' },
    { e: 'dmg', uid: 'e16', n: 2, blocked: 0, src: 'counter' }, { e: 'kill', uid: 'e16', id: 'e_page' },
    { e: 'end', phase: 'DEFEAT' },
  ]);
  for (const [id, hp] of [['envelope-counter-kill', 1], ['counter-kill-stops-hits', 239]]) {
    const step = ends(byId(`edge-${id}`))[0];
    assert.equal(step.state.phase, 'VICTORY');
    assert.equal(step.state.hp, hp);
    assert.equal(step.response.ev.filter(e => e.e === 'hit').length, 1);
    assert.equal(step.response.ev.filter(e => e.e === 'end').length, 1);
  }
});

test('long skipped-player turns retain exactly the latest 400 timestamped events', () => {
  const row = byId('edge-log-cap-400');
  assert.equal(ends(row).length, 425);
  assert.equal(last(row).state.turn, 426);
  assert.equal(last(row).state.v, 426);
  assert.equal(last(row).state.hp, 243);
  const events = row.steps.flatMap(s => s.response.ev.map(e => ({ t: s.state.turn, ...e })));
  assert.ok(events.length > 400);
  assert.deepEqual(last(row).state.log, events.slice(-400));
  assert.equal(last(row).state.log.length, 400);
});

test('receipt replays retain old responses across version advance and failures never reserve IDs', () => {
  const row = byId('protocol-receipts');
  assert.deepEqual(row.steps.slice(0, 5).map(s => s.errorCode), ['WRONG_PHASE', null, 'STALE_VERSION', 'WRONG_PHASE', 'UNKNOWN_COMMAND']);
  assert.equal(row.steps[0].receipts['failed-then-begin'], undefined);
  assert.ok(Object.hasOwn(row.steps[1].receipts, 'failed-then-begin'));
  const replay = row.steps.find(s => s.outcome === 'replayed');
  assert.equal(replay.response.v, 2);
  assert.equal(replay.state.v, 3);
  assert.deepEqual(replay.response, row.steps[5].response);
  assert.deepEqual(row.steps.slice(8, 10).map(s => s.errorCode), ['IDEMPOTENCY_CONFLICT', 'IDEMPOTENCY_CONFLICT']);
  assert.equal(last(row).state.v, 5);
  assert.ok(Object.hasOwn(last(row).receipts, 'failed-stale'));
  const terminal = byId('protocol-terminal-replay');
  assert.equal(terminal.steps[2].outcome, 'replayed');
  assert.equal(terminal.steps[2].state.phase, 'DEFEAT');
  assert.deepEqual(terminal.steps[2].response, terminal.steps[1].response);
  assert.deepEqual(terminal.steps.slice(3).map(s => s.errorCode), ['WRONG_PHASE', 'IDEMPOTENCY_CONFLICT']);
  const battle = MF.Battle.create(data, clone(row.setup));
  const request = { rid: 'detached', v: 0, kind: 'begin', args: {} };
  const response = battle.command(request), expected = clone(response);
  response.ev[0].e = 'caller-mutation';
  const again = battle.command(request);
  assert.deepEqual(again, expected);
  again.ev.length = 0;
  assert.deepEqual(battle.command(request), expected);
});

test('prototype-named request IDs are actual own receipts and can retry after rejected commands', () => {
  const rows = fixture.cases.filter(c => Object.hasOwn(c, 'receiptKey'));
  assert.deepEqual(rows.map(c => c.receiptKey), Object.getOwnPropertyNames(Object.prototype));
  for (const row of rows) {
    assert.equal(row.steps[0].errorCode, 'UNKNOWN_COMMAND');
    assert.ok(!Object.hasOwn(row.steps[0].receipts, row.receiptKey));
    assert.ok(Object.hasOwn(row.steps[1].receipts, row.receiptKey));
    assert.equal(row.steps[3].outcome, 'replayed');
    assert.deepEqual(row.steps[3].response, row.steps[1].response);
    assert.equal(row.steps[3].state.v, 2);
    assert.equal(row.steps[4].errorCode, 'IDEMPOTENCY_CONFLICT');
  }
});

test('fixture freshness and complete case coverage reject omitted cases, stale sources and diagnostic tampering', () => {
  for (const source of Object.values(fixture.sources)) assert.equal(source.sha256, sha256(readFileSync(new URL(`../../${source.path}`, import.meta.url))));
  assert.deepEqual(checkFixture(fixture), fixture.counts);
  assert.match(fixture.scope, /play\/ult.*NOT_IMPLEMENTED/);
  const first = fixture.cases[0], step = first.steps[0];
  for (const bad of [
    { ...fixture, sources: { ...fixture.sources, engine: { ...fixture.sources.engine, sha256: '0'.repeat(64) } } },
    { ...fixture, cases: fixture.cases.slice(1) },
    { ...fixture, cases: [...fixture.cases, first] },
    { ...fixture, cases: [{ ...first, steps: [{ ...step, state: { ...step.state, log: [] } }, ...first.steps.slice(1)] }, ...fixture.cases.slice(1)] },
    { ...fixture, cases: [{ ...first, steps: [{ ...step, eventsHash: '0'.repeat(64) }, ...first.steps.slice(1)] }, ...fixture.cases.slice(1)] },
    { ...fixture, cases: [{ ...first, steps: [{ ...step, publicView: { ...step.publicView, rng: [] } }, ...first.steps.slice(1)] }, ...fixture.cases.slice(1)] },
  ]) assert.throws(() => checkFixture(bad), /source|case|fixture|hash|snapshot|coverage/i);
});

test('exports are byte deterministic and an unexpected engine error preserves the previous fixture', t => {
  const dir = mkdtempSync(join(tmpdir(), 'mf-turn-oracle-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  const path = join(dir, 'turns.json.gz');
  const summary = exportVectors(path), bytes = readFileSync(path);
  assert.equal(summary.cases, fixture.counts.cases);
  assert.equal(summary.commands, fixture.counts.commands);
  assert.equal(summary.sha256, sha256(bytes));
  assert.deepEqual(JSON.parse(gunzipSync(bytes)), fixture);
  exportVectors(path);
  assert.deepEqual(readFileSync(path), bytes);
  writeFileSync(path, 'previous artifact');
  for (const error of [new Error('unexpected turn failure'), new MF.RuleError('WRONG_PHASE'), new MF.RuleError('UNSUPPORTED_EFFECT')]) {
    // Fault injection tests exporter failure handling, never a positive oracle trajectory.
    const broken = t.mock.method(MF.Battle.prototype, 'command', () => { throw error; });
    assert.throws(() => generateCases(), caught => caught === error);
    assert.throws(() => exportVectors(path), caught => caught === error);
    assert.equal(readFileSync(path, 'utf8'), 'previous artifact');
    assert.deepEqual(readdirSync(dir), ['turns.json.gz']);
    broken.mock.restore();
  }
});

test('the checked-in turn fixture is a lossless fresh command oracle', () => {
  const saved = JSON.parse(gunzipSync(readFileSync(fixturePath)));
  assert.deepEqual(checkFixture(saved), fixture.counts);
});
