import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { gunzipSync } from 'node:zlib';
import { generateCases, checkFixture, exportVectors, fixturePath } from '../../tools/gen_battle_opening_vectors.mjs';

const require = createRequire(import.meta.url);
const MF = require('../rules-core.js');
const data = JSON.parse(readFileSync(new URL('../../data/game_data.json', import.meta.url), 'utf8'));
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = value => createHash('sha256').update(value).digest('hex');
const fixture = generateCases();
const byId = id => {
  const vector = fixture.cases.find(row => row.id === id);
  assert.ok(vector, `Missing opening vector ${id}`);
  return vector;
};
const baseSetup = () => clone(byId('harin-opening-preserved').setup);

// Collecting a sorted replacer independently checks the state hash without sharing the exporter serializer.
function hashState(value) {
  const keys = new Set();
  const collect = item => {
    if (!item || typeof item !== 'object') return;
    for (const key of Object.keys(item)) {
      if (key === 'log' || key === 'ev') continue;
      if (!Array.isArray(item)) keys.add(key);
      collect(item[key]);
    }
  };
  collect(value);
  return sha256(JSON.stringify(value, [...keys].sort()));
}

test('opening oracle covers every authored character/resonance, weapon/refine, relic and enemy', () => {
  assert.equal(data.characters.length, 10);
  assert.equal(data.weapons.length, 22);
  assert.equal(data.relics.length, 32);
  assert.equal(new Set(fixture.cases.map(row => row.id)).size, fixture.cases.length);
  for (const character of data.characters) for (let res = 0; res <= 6; res++) {
    const vector = byId(`character-${character.id}-res${res}`);
    assert.deepEqual(vector.setup.party.map(p => [p.charId, p.res]), [[character.id, res]]);
  }
  for (const weapon of data.weapons) for (let refine = 1; refine <= 5; refine++) {
    const vector = byId(`weapon-${weapon.id}-r${refine}`);
    assert.equal(vector.setup.party[0].weaponId, weapon.id);
    assert.equal(vector.setup.party[0].refine, refine);
    assert.equal(data.characters.find(c => c.id === vector.setup.party[0].charId).weapon, weapon.type);
    assert.ok(vector.setup.deck.some(card => card.id === weapon.card && card.wielder === vector.setup.party[0].charId));
  }
  for (const relic of data.relics) assert.deepEqual(byId(`relic-${relic.id}`).setup.relics, [relic.id]);
  for (const enemy of data.enemies) assert.deepEqual(byId(`enemy-${enemy.id}`).setup.enemies, [enemy.id]);
});

test('only the four currently authored opening DSL shapes are exercised, including every resonance tag', () => {
  const tags = new Set();
  const ops = [];
  const visit = fx => {
    tags.add(fx.type);
    if (fx.type === 'multi') fx.list.forEach(visit);
    if (fx.type === 'battleStart') ops.push(...fx.eff);
  };
  for (const character of data.characters) character.resonance.forEach(node => visit(node.fx));
  assert.deepEqual([...tags].sort(), ['battleStart', 'capacity', 'cardMod', 'hook', 'multi', 'passiveParam', 'rule', 'skillLv', 'ultExtra', 'ultLv', 'ultReplace'].sort());
  assert.deepEqual(ops, [
    { op: 'tune', part: 'amp', pick: 'random' }, { op: 'blk', v: 10 },
    { op: 'heal', v: 10 }, { op: 'st', s: 'mark', n: 2, tgt: 'rand' },
  ]);
  assert.ok(byId('character-nia-res4').response.ev.some(e => e.e === 'tune'));
  assert.ok(byId('character-harin-res4').response.ev.some(e => e.e === 'block'));
  assert.ok(byId('character-aila-res4').response.ev.some(e => e.e === 'heal'));
  assert.ok(byId('character-kaya-res4').response.ev.some(e => e.e === 'status' && e.s === 'mark'));
});

test('each vector retains complete real CREATED and committed snapshots, events, hashes and public view', () => {
  for (const vector of fixture.cases) {
    const battle = MF.Battle.create(data, clone(vector.setup));
    assert.equal(Object.keys(vector.createdState).length, 59, vector.id);
    assert.equal(vector.createdState.phase, 'CREATED', vector.id);
    assert.deepEqual(battle.s, vector.createdState, `${vector.id}: created snapshot`);
    assert.equal(hashState(battle.s), vector.createdStateHash, `${vector.id}: created hash`);
    for (const actor of vector.createdState.actors) {
      assert.equal(Object.keys(actor).length, 19);
      const definition = data.characters.find(c => c.id === actor.id);
      assert.equal(actor.passive.name, definition.passive.name);
      assert.equal(actor.passive.text, definition.passive.text);
      assert.ok(actor.cardMods.every(fx => fx.type === 'cardMod'));
      assert.ok(actor.hooks.every(fx => fx.type === 'hook'));
    }
    for (const enemy of vector.createdState.enemies) {
      assert.equal(Object.keys(enemy).length, 18);
      assert.equal(enemy.intent, null);
      assert.equal(enemy.next, null);
      assert.ok(!Object.hasOwn(enemy, 'phaseIndex'));
      assert.ok(!Object.hasOwn(enemy, 'pattern'));
    }
    for (const card of Object.values(vector.createdState.cards)) assert.equal(Object.keys(card).length, 15);
    assert.deepEqual(battle.command(clone(vector.request)), vector.response, `${vector.id}: response/events`);
    assert.deepEqual(battle.s, vector.finalState, `${vector.id}: final snapshot`);
    assert.equal(hashState(battle.s), vector.finalStateHash, `${vector.id}: final hash`);
    assert.deepEqual(vector.response.ev, vector.finalState.ev);
    assert.deepEqual(battle.publicView(), vector.publicView);
    assert.ok(!Object.hasOwn(vector.publicView, 'rng'));
    assert.ok(!Object.hasOwn(vector.publicView, 'draw'));
    assert.ok(!Object.hasOwn(vector.publicView, 'zones'));
    assert.equal(vector.publicView.drawCount, vector.finalState.zones.draw.length);
    assert.deepEqual(vector.publicView.hand, vector.finalState.zones.hand.map(uid => vector.finalState.cards[uid]));
    assert.deepEqual(vector.publicView.res, vector.finalState.res);
    assert.deepEqual(vector.publicView.enemies, vector.finalState.enemies);
  }
});

test('pinned first-turn regressions independently retain Harin block26/counter2 and seed+moss growth1/block3', () => {
  const harin = byId('harin-opening-preserved');
  assert.equal(harin.finalState.block, 26);
  assert.equal(harin.finalState.counter, 2);
  assert.deepEqual(harin.response.ev, [
    { e: 'block', n: 8, src: 'relic' }, { e: 'block', n: 10, src: 'card' },
    { e: 'block', n: 8, src: 'weapon' }, { e: 'turn', turn: 1, ap: 4 },
  ]);
  const moss = byId('seed-moss-single');
  assert.equal(moss.finalState.cards.c1.growth, 1);
  assert.equal(moss.finalState.block, 3);
  assert.equal(moss.finalState.growthCount, 0);
  assert.deepEqual(moss.response.ev, [
    { e: 'grow', uid: 'c1', growth: 1, src: 'eternal_seed' },
    { e: 'block', n: 3, src: 'moss' }, { e: 'turn', turn: 1, ap: 4 },
  ]);
});

test('real opening edges cover seeds, strongest weapons, tuning, growth eligibility, ownership and circuit/AP limits', () => {
  assert.equal(byId('seed-zero').setup.seed, 0);
  assert.equal(byId('seed-uint-max').setup.seed, 0xffffffff);
  assert.deepEqual(byId('seed-default-omitted').createdState.rng, byId('seed-default-null').createdState.rng);
  const maximumDraw = byId('draw-max-strongest-only');
  assert.equal(maximumDraw.finalState.handMax, 9);
  assert.equal(maximumDraw.finalState.zones.hand.length, 9);
  assert.equal(maximumDraw.response.ev.filter(e => e.e === 'burnCard').length, 0);
  assert.equal(maximumDraw.finalState.zones.discard.length, 0, 'fresh authored opening draw has no overflow');
  assert.equal(byId('tuning-only-one-eligible').finalState.tempParts, 1);
  assert.equal(byId('tuning-only-curses').finalState.tempParts, 0);
  assert.ok(byId('seed-no-eligible').response.ev.every(e => e.e !== 'grow' && e.src !== 'moss'));
  const mixedSeed = byId('seed-mixed-eligibility');
  const grown = mixedSeed.response.ev.filter(e => e.e === 'grow');
  assert.equal(grown.length, 1);
  assert.ok(['yuri_sprout', 'yuri_root'].includes(mixedSeed.finalState.cards[grown[0].uid].id));
  const lineage = byId('card-fields-and-lineage');
  assert.deepEqual([...new Set(Object.values(lineage.createdState.cards).map(c => c.owner))].sort(), ['common', 'curse', 'harin', 'weapon', 'yuri']);
  assert.ok(lineage.setup.deck.every(c => c.key));
  assert.ok(Object.values(lineage.finalState.cards).every(c => !Object.hasOwn(c, 'key')), 'Battle.create ignores Run memory keys');
  for (const id of ['circuit-fatal', 'circuit-envelope-used']) {
    const vector = byId(id);
    assert.equal(vector.finalState.phase, 'DEFEAT');
    assert.equal(vector.finalState.zones.hand.length, 0);
    assert.equal(vector.response.ev.filter(e => e.e === 'end').length, 1);
    assert.ok(vector.finalState.enemies.every(e => e.intent === null));
  }
  assert.equal(byId('circuit-envelope-unused').finalState.hp, 1);
  assert.equal(byId('circuit-envelope-unused').finalState.runFlags.envelopeUsed, true);
  assert.equal(byId('ap-cap-high').finalState.ap, 9);
  assert.equal(byId('ap-cap-low').finalState.ap, 0);
  assert.equal(byId('ap-crown-hourglass').finalState.ap, 4);
  const aila = byId('aila-overflow-strongest-heal');
  assert.ok(aila.finalState.block > 0);
  assert.equal(aila.finalState.hp, aila.finalState.maxHp);
  const statusEvents = byId('brazier-before-kaya').response.ev.filter(e => e.e === 'status');
  assert.deepEqual(statusEvents.map(e => e.s), ['burn', 'mark']);
});

test('begin receipts copy responses and compare request property order and diagnostic args before stale/phase checks', () => {
  const battle = MF.Battle.create(data, baseSetup());
  const request = { rid: 'opening-receipt', v: 0, kind: 'begin', args: { log: ['request'], ev: [], label: 'opening' } };
  const response = battle.command(clone(request));
  const expected = clone(response);
  const receipts = clone(battle.receipts);
  response.ev.push({ e: 'caller-mutation' });
  const retry = battle.command(clone(request));
  assert.deepEqual(retry, expected);
  retry.ev.length = 0;
  assert.deepEqual(battle.command(clone(request)), expected);
  assert.deepEqual(battle.receipts, receipts);
  for (const args of [{ label: 'opening', log: ['request'], ev: [] }, { ...request.args, extra: true }, { ...request.args, log: [] }]) {
    assert.throws(() => battle.command({ ...request, args }), { code: 'IDEMPOTENCY_CONFLICT' });
  }
  const beforeRejections = clone(battle.s);
  assert.throws(() => battle.command({ rid: 'new-stale', v: 0, kind: 'begin', args: {} }), { code: 'STALE_VERSION' });
  assert.throws(() => battle.command({ rid: 'new-phase', v: 1, kind: 'begin', args: {} }), { code: 'WRONG_PHASE' });
  assert.deepEqual(battle.s, beforeRejections);
  assert.deepEqual(battle.receipts, receipts);
});

test('an unsupported opening rejected after candidate RNG/events leaves full state and receipts unchanged; failed rid retries', t => {
  const setup = baseSetup();
  setup.relics.push('pocket_brazier');
  setup.deck.push({ id: 'com_guard' }, { id: 'smudge' });
  const battle = MF.Battle.create(data, setup);
  const before = clone(battle.s);
  const receipts = clone(battle.receipts);
  const request = { rid: 'retry-after-rejection', v: 0, kind: 'begin', args: {} };
  const execOp = MF.Battle.prototype.execOp;
  let sawCandidateWork = false;
  const unsupported = t.mock.method(MF.Battle.prototype, 'execOp', function(op, ctx) {
    if (op.op === 'blk') {
      sawCandidateWork = JSON.stringify(this.s.rng) !== JSON.stringify(before.rng) && this.s.ev.length > 0;
      throw new MF.RuleError('UNSUPPORTED_EFFECT', op.op);
    }
    return execOp.call(this, op, ctx);
  });
  assert.throws(() => battle.command(request), { code: 'UNSUPPORTED_EFFECT' });
  assert.equal(sawCandidateWork, true);
  assert.deepEqual(battle.s, before);
  assert.deepEqual(battle.receipts, receipts);
  unsupported.mock.restore();
  assert.equal(battle.command(request).v, 1);
  assert.ok(battle.receipts[request.rid]);
});

test('fixture source provenance and every case are deterministic and tampering is rejected', () => {
  assert.deepEqual(generateCases(), fixture);
  assert.equal(fixture.schema, 'mf-battle-opening-v1');
  assert.equal(fixture.scope, 'Battle.create and atomic begin only; full Battle/Run replay NOT_IMPLEMENTED');
  for (const source of Object.values(fixture.sources)) {
    assert.equal(source.sha256, sha256(readFileSync(new URL(`../../${source.path}`, import.meta.url))));
  }
  assert.equal(checkFixture(fixture), fixture.cases.length);
  const badSource = clone(fixture);
  badSource.sources.engine.sha256 = '0'.repeat(64);
  assert.throws(() => checkFixture(badSource), /source|provenance/i);
  for (const mutate of [
    copy => copy.cases.push(clone(copy.cases[0])),
    copy => copy.cases.pop(),
    copy => copy.cases[0].createdState.actors[0].passive.text = '',
    copy => copy.cases[0].finalStateHash = '0'.repeat(64),
    copy => copy.cases[0].response.ev.push({ e: 'tampered' }),
    copy => copy.cases[0].publicView.rng = [],
  ]) {
    const changed = clone(fixture);
    mutate(changed);
    assert.throws(() => checkFixture(changed), /case|fixture|snapshot|vector/i);
  }
});

test('lossless fixture exports are byte deterministic and atomic on unexpected exceptions or RuleErrors', t => {
  const dir = mkdtempSync(join(tmpdir(), 'mf-battle-opening-'));
  t.after(() => rmSync(dir, { recursive: true, force: true }));
  const output = join(dir, 'opening.json.gz');
  const summary = exportVectors(output);
  const first = readFileSync(output);
  assert.equal(summary.cases, fixture.cases.length);
  assert.equal(summary.sha256, sha256(first));
  assert.equal(summary.bytes, first.length);
  assert.deepEqual(JSON.parse(gunzipSync(first).toString('utf8')), fixture);
  exportVectors(output);
  assert.deepEqual(readFileSync(output), first);
  assert.deepEqual(readdirSync(dir), ['opening.json.gz']);
  writeFileSync(output, 'previous artifact');
  for (const error of [new Error('unexpected opening fault'), new MF.RuleError('UNSUPPORTED_EFFECT'),
    new MF.RuleError('UNEXPECTED_OPENING_FAILURE'), Object.assign(new Error('impostor'), { code: 'WRONG_PHASE' })]) {
    const broken = t.mock.method(MF.Battle.prototype, 'command', () => { throw error; });
    assert.throws(() => generateCases(), caught => caught === error);
    assert.throws(() => exportVectors(output), caught => caught === error);
    assert.equal(readFileSync(output, 'utf8'), 'previous artifact');
    assert.deepEqual(readdirSync(dir), ['opening.json.gz']);
    broken.mock.restore();
  }
});

test('checked-in opening fixture matches fresh engine results without weakening full Battle/Run acceptance', () => {
  const saved = JSON.parse(gunzipSync(readFileSync(fixturePath)).toString('utf8'));
  assert.equal(checkFixture(saved), fixture.cases.length);
});
