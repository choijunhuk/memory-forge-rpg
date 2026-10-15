// A1: actual player commands. Expected snapshots are never execution inputs.
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
const data = JSON.parse(readFileSync(join(root, 'data/game_data.json'), 'utf8'));
const ix = MF.indexData(data);
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const originalData = JSON.stringify(data);
const schema = 'mf-battle-player-actions-v1';
const status = 'COMPLETE_CURRENT_SOURCE_A1';
const scope = 'Actual Battle.create and ordered begin/play/ult/end commands for all current authored player actions; FullRun acceptance remains pending';
export const fixturePath = join(root, 'engine/tests/vectors/battle_player_action_vectors.json.gz');
const evolvedFrom = Object.fromEntries(data.cards.flatMap(c => (c.evo || []).map(id => [id, c.id])));
const baseModes = card => ({ y: ['now', 'next', 'after'], n: ['now'], o: ['next', 'after'], after: ['after'] }[card.res]);
const spec = (id, extra = {}) => ({ id, ...(evolvedFrom[id] ? { from: evolvedFrom[id] } : {}), ...extra });

function sources() {
  return Object.fromEntries(Object.entries({ data: 'data/game_data.json', engine: 'engine/rules-core.js',
    memory: 'proposals/memory_v2.cjs', canonical: 'tools/gen_traces.mjs', fuzzPolicy: 'engine/tests/fuzz_runs.mjs', generator: 'tools/gen_battle_player_action_vectors.mjs',
  }).map(([key, path]) => [key, { path, sha256: sha256(readFileSync(join(root, path))) }]));
}
const loadedSources = sources();
function point(battle, receipts = false) {
  const state = clone(battle.s), publicView = clone(battle.publicView());
  const value = { state, stateHash: stateHash(state), rngHash: stateHash(state.rng), eventsHash: stateHash(state.ev),
    logHash: stateHash(state.log), publicView, publicViewHash: stateHash(publicView) };
  if (receipts) value.receipts = clone(battle.receipts);
  return value;
}
const fact = (at, path, value) => ({ at, path, value });
function witness(row, kind, tag, stepIndex, facts) {
  assert.ok(Number.isInteger(stepIndex) && stepIndex >= 0 && stepIndex < row.steps.length, `${row.id}: witness index`);
  for (const f of facts) {
    let value = f.at === 'before' ? (stepIndex ? row.steps[stepIndex - 1].state : row.created.state) :
      f.at === 'after' ? row.steps[stepIndex].state : row.steps[stepIndex].response;
    for (const key of f.path) value = value?.[key];
    assert.deepEqual(value, f.value, `${row.id}/${kind}/${tag}: ${f.at}.${f.path.join('.')}`);
  }
  (row.witnesses ||= []).push({ kind, tag, stepIndex, facts: clone(facts) });
}
function setup(party, deck, extra = {}) {
  const maxHp = party.reduce((n, a) => n + (a.hp || MF.Stats.REFERENCE.hp), 0);
  return clone({ party, deck, hp: maxHp, maxHp, seed: 0, enemies: ['b_archivist'], relics: [], ...extra });
}
function partyFor(card, actorExtra = {}) {
  const weapon = data.weapons.find(w => w.card === card.id);
  const owner = ix.chars[card.owner] || (weapon && (data.characters.find(c => c.id === weapon.signature) || data.characters.find(c => c.weapon === weapon.type))) || data.characters[0];
  const first = { charId: owner.id, ...(weapon ? { weaponId: weapon.id, refine: 1 } : {}), ...actorExtra };
  const party = [first];
  for (const charId of ['sera', 'kaya', 'harin']) if (party.length < 3 && !party.some(a => a.charId === charId)) party.push({ charId });
  return party;
}
function deckWith(main, required = []) {
  const deck = [main, ...required.map(id => spec(id))];
  for (const id of ['com_guard', 'com_slash', 'com_patch', 'com_sort', 'com_tactics', 'com_focus', 'com_barrier', 'com_shockwave']) {
    if (deck.length >= 8) break;
    if (!deck.some(c => c.id === id)) deck.push(spec(id));
  }
  return deck;
}
function chooseSeed(initial, required) {
  const counts = new Map();
  for (const id of required) counts.set(id, (counts.get(id) || 0) + 1);
  for (let seed = 0; seed < 256; seed++) {
    const candidate = { ...initial, seed };
    const battle = MF.Battle.create(data, clone(candidate));
    battle.do('begin');
    if (battle.isOver()) continue;
    const hand = battle.s.zones.hand.map(uid => battle.s.cards[uid].id);
    if ([...counts].every(([id, n]) => hand.filter(x => x === id).length >= n)) return clone(candidate);
  }
  throw new Error(`No actual opening hand for required cards: ${required.join(',')}`);
}
function record(id, initial, metadata, script) {
  const battle = MF.Battle.create(data, clone(initial)), withReceipts = metadata.group === 'protocol';
  const row = { id, ...metadata, setup: clone(initial), created: point(battle, withReceipts), steps: [] };
  const command = (kind, args = {}, expectedError = null, request = null) => {
    const req = request || { rid: `${id}-${row.steps.length}-${kind}`, v: battle.s.v, kind, args };
    const before = clone(battle.s), receipts = clone(battle.receipts);
    let check = null;
    const a = req.args || {};
    if (req.kind === 'play' && typeof a.uid === 'string' && (!a.mode || typeof a.mode === 'string') &&
      (!a.target || typeof a.target === 'string') && (!a.picks || Array.isArray(a.picks) && a.picks.every(p => typeof p === 'string'))) {
      const checkArgs = { uid: a.uid, mode: a.mode || 'now', target: a.target || null, picks: a.picks || [] };
      check = { args: clone(checkArgs), code: battle.check(checkArgs.uid, checkArgs.mode, checkArgs.target, checkArgs.picks) };
      assert.ok(isDeepStrictEqual(battle.s, before), `${id}: Check changed state`);
      assert.ok(isDeepStrictEqual(battle.receipts, receipts), `${id}: Check changed receipts`);
    }
    let response = null, errorCode = null;
    try { response = clone(battle.command(clone(req))); }
    catch (error) {
      if (!expectedError || !(error instanceof MF.RuleError) || error.code !== expectedError) throw error;
      errorCode = error.code;
      assert.ok(isDeepStrictEqual(battle.s, before), `${id}: rejected command mutated state`);
      assert.ok(isDeepStrictEqual(battle.receipts, receipts), `${id}: rejected command mutated receipts`);
    }
    assert.equal(errorCode, expectedError, `${id}: expected rejection did not occur`);
    const outcome = errorCode ? 'rejected' : battle.s.v === before.v ? 'replayed' : 'committed';
    if (outcome === 'committed') assert.equal(battle.s.v, before.v + 1);
    else { assert.ok(isDeepStrictEqual(battle.s, before)); assert.ok(isDeepStrictEqual(battle.receipts, receipts)); }
    row.steps.push({ request: clone(req), check, outcome, errorCode, response,
      responseEventsHash: response ? stateHash(response.ev) : null, ...point(battle, withReceipts) });
    return response;
  };
  const find = cardId => battle.s.zones.hand.find(uid => battle.s.cards[uid].id === cardId);
  const play = (cardId, mode = 'now', extra = {}) => {
    const uid = find(cardId);
    assert.ok(uid, `${id}: ${cardId} is not actually in hand`);
    const need = battle.needsTarget(uid);
    const target = need ? (need === 'fixed' ? battle.alive().find(e => e.st.mark > 0) : battle.alive()[0])?.uid || null : null;
    command('play', { uid, mode, target, picks: [], ...extra });
    return uid;
  };
  const flush = (minimum = 1) => {
    const start = battle.s.turn;
    for (let n = 0; !battle.isOver() && (battle.s.turn < start + minimum || battle.s.res.length || battle.s.debts.length ||
      battle.s.pendingCopies?.length || battle.s.buffs.length || battle.s.bonus.avail); n++) {
      assert.ok(n < 12, `${id}: produced lifecycle did not drain`);
      command('end');
    }
  };
  const w = (kind, tag, facts, stepIndex = row.steps.length - 1) => witness(row, kind, tag, stepIndex, facts);
  const event = (kind, tag, expected, occurrence = 0, stepIndex = row.steps.length - 1) => {
    const matches = row.steps[stepIndex].response.ev.map((e, i) => ({ e, i })).filter(({ e }) =>
      Object.entries(expected).filter(([k]) => k !== 'n' && k !== 'blocked').every(([k, v]) => isDeepStrictEqual(e[k], v)));
    const match = matches[occurrence]; assert.ok(match, `${id}: missing ${tag} event`);
    w(kind, tag, Object.entries(expected).map(([k, v]) => fact('response', ['ev', match.i, k], v)), stepIndex);
  };
  if (metadata.autoBegin !== false) command('begin');
  script({ battle, command, play, find, flush, row, w, event });
  assert.equal(JSON.stringify(data), originalData, `${id}: source data changed`);
  return row;
}
function cardCase(card, mode, options = {}) {
  const { plus = false, actor: actorExtra = {}, setup: extra = {}, group = 'card', id: caseId, cardExtra = {}, metadata = {} } = options;
  const needsFixed = JSON.stringify(card.eff).includes('"tgt":"fixed"');
  const required = [];
  if (needsFixed) required.push('kaya_tracking');
  if (card.id === 'ravien_defer') required.push('ravien_advance');
  if (card.id === 'sera_pull') required.push('sera_round');
  if (['sion_forget', 'nia_rebuild', 'com_recall'].includes(card.id)) required.push('sera_reload');
  if (['yuri_dew', 'yuri_metamorph', 'wc_yuri', 'yuri_tend', 'yuri_worldtree'].includes(card.id)) required.push('yuri_cocoon', 'yuri_sprout');
  const party = partyFor(card, actorExtra), wielder = card.owner === 'weapon' ? party[0].charId : undefined;
  const main = spec(card.id, { plus, ...(wielder ? { wielder } : {}), ...cardExtra });
  const initial = chooseSeed(setup(party, deckWith(main, required), extra), [card.id, ...required]);
  const id = caseId || `${group}-${card.id}-${plus ? 'plus-' : ''}${mode}${actorExtra.res === undefined ? '' : `-r${actorExtra.res}`}`;
  return record(id, initial, { group, main: { cardId: card.id, mode, plus }, ...metadata, ...(actorExtra.res === undefined ? {} : { resonance: actorExtra.res }) }, ({ battle, play, flush, row }) => {
    if (needsFixed) play('kaya_tracking');
    if (card.id === 'ravien_defer') play('ravien_advance');
    if (card.id === 'sera_pull') play('sera_round', 'after');
    if (required.includes('sera_reload')) {
      const spare = battle.s.zones.hand.find(uid => ![card.id, ...required].includes(battle.s.cards[uid].id));
      assert.ok(spare, `${id}: no legal spare for discard preparation`);
      play('sera_reload', 'now', { picks: [spare] });
    }
    const uid = play(card.id, mode);
    flush();
    const resolved = row.steps.flatMap(s => s.response?.ev || []).filter(e => e.e === 'resolved' && e.uid === uid);
    assert.equal(resolved.length, 1, `${id}: main card did not resolve exactly once`);
    assert.equal(resolved[0].fizzled, false, `${id}: fizzle is not effect acceptance`);
    row.mainOutcome = { uid, resolved: true, fizzled: resolved[0].fizzled, used: battle.s.cards[uid].used };
  });
}

function standardParty(first, others = ['sera', 'kaya', 'harin']) {
  const party = [first];
  for (const charId of others) if (party.length < 3 && !party.some(a => a.charId === charId)) party.push({ charId });
  return party;
}
function fillDeck(items, size = 8) {
  const deck = items.map(c => typeof c === 'string' ? spec(c) : clone(c));
  for (const id of ['com_guard', 'com_slash', 'com_patch', 'com_sort', 'com_tactics', 'com_focus', 'com_barrier', 'com_shockwave']) {
    if (deck.length >= size) break;
    if (!deck.some(c => c.id === id)) deck.push(spec(id));
  }
  return deck;
}
function scenario(id, party, deck, required, script, extra = {}, metadata = {}) {
  return record(id, chooseSeed(setup(party, deck.map(c => typeof c === 'string' ? spec(c) : c), extra), required),
    { group: 'edge', ...metadata }, script);
}
function ultCase(character, res) {
  const party = standardParty({ charId: character.id, res, gauge: 100 });
  const deck = MF.Run.baseKit(ix, character.id).map(c => spec(c.id));
  if (character.id === 'ravien' && !deck.some(c => c.id === 'ravien_advance')) deck.push(spec('ravien_advance'));
  const filled = fillDeck(deck, 12);
  return scenario(`ult-${character.id}-r${res}`, party, filled, character.id === 'ravien' ? ['ravien_advance'] : [], ({ battle, command, play, flush, row }) => {
    if (character.id === 'ravien') play('ravien_advance');
    command('ult', { actor: character.id, target: battle.alive()[0].uid, picks: [] });
    row.ultActor = character.id;
    flush();
  }, {}, { group: 'ult', characterId: character.id, resonance: res, inputDomain: 'Explicit supported setup gauge100; not earned-gauge or balance acceptance' });
}
function weaponCase(weapon, refine) {
  let owner = data.characters.find(c => c.id === weapon.signature) || data.characters.find(c => c.weapon === weapon.type);
  if (weapon.passive.id === 'w_healUp') owner = ix.chars.aila;
  if (weapon.passive.id === 'w_none' && weapon.type === 'melee') owner = ix.chars.sion;
  if (weapon.passive.id === 'w_none' && weapon.type === 'ritual') owner = ix.chars.yuri;
  const actor = { charId: owner.id, weaponId: weapon.id, refine };
  const party = standardParty(actor, weapon.passive.id === 'w_growthHeal' ? ['lumi', 'sera'] : ['sera', 'kaya', 'harin']);
  const needed = [weapon.card], deck = [spec(weapon.card, { wielder: owner.id })];
  const add = (id, extra = {}) => { deck.push(spec(id, extra)); needed.push(id); };
  const pid = weapon.passive.id;
  const reserveWeapon = pid === 'w_reservedDmg' && baseModes(ix.cards[weapon.card]).includes('next');
  if (pid === 'w_reservedDmg' && !reserveWeapon) add('sera_round');
  if (pid === 'w_growthHeal') { add('lumi_dash'); add('yuri_cocoon'); add('yuri_sprout'); }
  if (pid === 'w_debtDmg') { add('ravien_advance'); add('ravien_strike'); }
  if (pid === 'w_markedDmg') { add('kaya_clue'); add('kaya_verdict'); }
  if (['w_brokenDmg', 'w_breakDmg'].includes(pid)) add('com_breaker');
  if (pid === 'w_healUp') add('aila_overflow');
  if (pid === 'w_blockUp') add('nia_barrier', { plus: true });
  if (pid === 'w_none') {
    const follow = { ranged: 'sera_last', melee: 'sion_lastpage', device: 'nia_masterwork', ritual: 'yuri_canopy' }[weapon.type];
    add(follow, weapon.type === 'ritual' ? { plus: true } : {});
    if (weapon.type === 'ritual') for (const id of ['yuri_cocoon', 'yuri_sprout', 'yuri_root', 'yuri_frostbloom', 'yuri_seedvault']) deck.push(spec(id));
  }
  if (pid === 'w_lowHpDmg') {
    return scenario(`weapon-${weapon.id}-r${refine}`, [actor], [spec(weapon.card, { wielder: owner.id }), spec('lumi_overdrive'), spec('lumi_dash')],
      [weapon.card, 'lumi_overdrive', 'lumi_dash'], ({ battle, command, play, flush }) => {
        play('lumi_overdrive'); play('lumi_dash'); command('end');
        play('lumi_overdrive'); play('lumi_dash'); command('end');
        play(weapon.card); flush();
      }, {}, { group: 'weapon', weaponId: weapon.id, refine, inputDomain: 'Normal soloHP81; low HP is produced by real overloads and enemy attack' });
  }
  const enemies = ['w_brokenDmg', 'w_breakDmg'].includes(pid) ? ['e_moth'] : ['b_archivist'];
  return scenario(`weapon-${weapon.id}-r${refine}`, party, fillDeck(deck), needed, ({ play, flush }) => {
    if (pid === 'w_growthHeal') play('lumi_dash');
    if (pid === 'w_debtDmg') play('ravien_advance');
    if (pid === 'w_markedDmg') play('kaya_clue');
    if (['w_brokenDmg', 'w_breakDmg'].includes(pid)) play('com_breaker');
    play(weapon.card, reserveWeapon ? 'next' : 'now');
    if (pid === 'w_reservedDmg' && !reserveWeapon) play('sera_round', 'next');
    if (pid === 'w_growthHeal') play('yuri_cocoon', 'next');
    if (pid === 'w_debtDmg') play('ravien_strike');
    if (pid === 'w_markedDmg') play('kaya_verdict');
    if (pid === 'w_healUp') play('aila_overflow');
    if (pid === 'w_blockUp') play('nia_barrier', 'next');
    if (pid === 'w_none') play({ ranged: 'sera_last', melee: 'sion_lastpage', device: 'nia_masterwork', ritual: 'yuri_canopy' }[weapon.type], 'after');
    flush();
  }, { enemies }, { group: 'weapon', weaponId: weapon.id, refine });
}
function temporaryPartCase(partId) {
  const party = standardParty({ charId: 'sera' }, ['nia', 'kaya']);
  const tune = partId === 'amp' ? 'nia_tune' : 'nia_overclock';
  return scenario(`part-${partId}`, party, fillDeck(['sera_round', tune]), ['sera_round', tune], ({ find, play, flush }) => {
    play(tune, 'now', { picks: [find('sera_round')] });
    play('sera_round'); flush();
  }, {}, { group: 'part', partId });
}

// Literal anchors below are derived from authored card numbers and ordered rules.
// Every preparation is a real command; these assertions never initialize battle state.
function edgeBuilders() {
  const normal = first => standardParty({ charId: first });
  const out = [];
  const add = (id, party, deck, required, script, extra = {}, meta = {}) =>
    out.push([id, () => scenario(id, party, deck, required, script, extra, meta)]);
  add('edge-bulwark-retention', [{ charId: 'harin', res: 4 }, { charId: 'nia', weaponId: 'w_ledger_lock', refine: 5 }, { charId: 'sera' }], ['harin_bulwark'], ['harin_bulwark'], ({ battle, play, command, w }) => {
    w('interaction', 'opening-block26', [fact('after', ['block'], 26), fact('after', ['counter'], 2)]);
    const uid = play('harin_bulwark');
    w('op', 'power', [fact('after', ['powers'], [{ id: 'bulwark', keep: 10 }]), fact('after', ['cards', uid, 'used'], 1), fact('after', ['zones', 'exhaust'], [uid]), fact('after', ['ap'], 2)]);
    command('end'); w('interaction', 'real-keep10', [fact('after', ['block'], 10), fact('after', ['counter'], 0), fact('after', ['turn'], 2)]);
  }, { enemies: ['e_seed'], relics: ['bronze_cog'] });
  add('edge-bulwark-stacks', normal('harin'), ['harin_bulwark', 'harin_bulwark'], ['harin_bulwark', 'harin_bulwark'], ({ play, command, w }) => {
    play('harin_bulwark'); play('harin_bulwark');
    w('interaction', 'power-additive', [fact('after', ['powers'], [{ id: 'bulwark', keep: 20 }])]);
    command('end'); w('interaction', 'stacked-keep20', [fact('after', ['block'], 0)]);
  }, { enemies: ['e_seed'] });
  add('edge-pull-after-next', [{ charId: 'sera' }], [spec('sera_timed', { mods: { c: 'c_far' } }), 'sera_pull'], ['sera_timed', 'sera_pull'], ({ battle, play, command, w, event }) => {
    const uid = play('sera_timed', 'after'); play('sera_pull');
    w('op', 'pull', [fact('after', ['res'], [{ uid, due: 2, order: 1, target: battle.alive()[0].uid, kind: 'next' }])]);
    command('end'); event('interaction', 'pull-next-value22', { e: 'dmg', src: 'card', n: 22, blocked: 10 });
    w('interaction', 'pull-loses-after-bonus', [fact('after', ['enemies', 0, 'hp'], 34), fact('after', ['actors', 0, 'gauge'], 20)]);
  }, { enemies: ['e_shelf'] });
  add('edge-pull-immediate-original-kind', normal('sera'), ['sera_timed', 'sera_pull'], ['sera_timed', 'sera_pull'], ({ play, w, event }) => {
    play('sera_timed', 'next'); play('sera_pull');
    event('interaction', 'immediate-pull-next22', { e: 'dmg', src: 'card', n: 22 });
    w('interaction', 'pull-detaches-before-resolve', [fact('after', ['res', 'length'], 0), fact('after', ['turn'], 1)]);
  });
  add('edge-defer-debt', [{ charId: 'ravien' }], ['ravien_advance', 'ravien_defer'], ['ravien_advance', 'ravien_defer'], ({ play, command, w }) => {
    play('ravien_advance'); w('op', 'loan', [fact('before', ['ap'], 4), fact('after', ['ap'], 6), fact('after', ['debts'], [{ id: 'd1', n: 2, due: 2, deferred: false }])]);
    play('ravien_defer'); w('op', 'defer', [fact('after', ['debts'], [{ id: 'd1', n: 3, due: 3, deferred: true }])]);
    command('end'); w('interaction', 'defer-no-early-repay', [fact('after', ['ap'], 4)]);
    command('end'); w('interaction', 'defer-repay3', [fact('after', ['ap'], 1), fact('after', ['debts'], [])]);
  }, { enemies: ['e_seed'] });
  add('edge-debt-reservation-shared-sequence', normal('ravien'), ['ravien_advance', 'ravien_defer', 'sera_round'], ['ravien_advance', 'ravien_defer', 'sera_round'], ({ play, w }) => {
    play('ravien_advance'); const uid = play('sera_round', 'next'); play('ravien_defer');
    w('interaction', 'shared-resSeq', [fact('after', ['resSeq'], 2), fact('after', ['res', 0, 'uid'], uid), fact('after', ['res', 0, 'order'], 2), fact('after', ['debts', 0, 'id'], 'd1')]);
  });
  add('edge-echo-evolved-lineage', [{ charId: 'yuri' }], [spec('yuri_cocoon_guard', { mods: { c: 'c_echo' } })], ['yuri_cocoon_guard'], ({ battle, play, command, w }) => {
    const uid = play('yuri_cocoon_guard', 'next'); command('end');
    w('interaction', 'evolved-reserved-block19', [fact('after', ['block'], 19), fact('after', ['pendingCopies'], [{ id: 'yuri_cocoon_guard', wielder: null, from: 'yuri_cocoon', due: 3 }]), fact('after', ['cards', uid, 'growth'], 0)]);
    command('end'); const copy = battle.s.zones.hand.find(u => battle.s.cards[u].echoCopy);
    w('interaction', 'echo-copy-default-metadata', [fact('after', ['pendingCopies'], []), fact('after', ['cards', copy, 'temp'], ['echoHalf']), fact('after', ['cards', copy, 'free'], true), fact('after', ['cards', copy, 'plus'], false), fact('after', ['cards', copy, 'mods'], { c: null, e: null }), fact('after', ['cards', copy, 'from'], 'yuri_cocoon')]);
    command('play', { uid: copy, mode: 'now', target: null, picks: [] });
    w('interaction', 'echo-half-and-exhaust', [fact('after', ['block'], 10), fact('after', ['zones', 'exhaust'], [copy]), fact('after', ['ap'], 4)]);
  }, { enemies: ['e_seed'] });
  add('edge-copy-pick-lineage', normal('yuri'), ['yuri_metamorph', spec('yuri_cocoon_guard', { plus: true, mods: { e: 'e_guard' } })], ['yuri_metamorph', 'yuri_cocoon_guard'], ({ battle, find, play, command, w }) => {
    const original = find('yuri_cocoon_guard'); play('yuri_metamorph', 'now', { picks: [original] });
    const copy = battle.s.zones.hand.find(u => battle.s.cards[u].echoCopy);
    w('op', 'copyPick', [fact('after', ['cards', copy, 'plus'], true), fact('after', ['cards', copy, 'mods'], { c: null, e: 'e_guard' }), fact('after', ['cards', copy, 'temp'], []), fact('after', ['cards', copy, 'from'], 'yuri_cocoon'), fact('after', ['cards', copy, 'growth'], 0), fact('after', ['cards', copy, 'free'], true)]);
    command('play', { uid: copy, mode: 'after', target: null, picks: [] }); command('end'); command('end');
    w('interaction', 'copy-pick-full-value', [fact('after', ['block'], 27), fact('after', ['zones', 'exhaust'], [copy])]);
  }, { enemies: ['e_seed'] });
  add('edge-yuri-r6-growth-ult', [{ charId: 'yuri', res: 6, gauge: 100 }], ['yuri_cocoon', 'yuri_sprout', spec('yuri_cocoon_guard')], [], ({ command, w, event }) => {
    command('ult', { actor: 'yuri', picks: [] });
    w('op', 'growLowest', [fact('after', ['cards', 'c1', 'growth'], 2), fact('after', ['cards', 'c2', 'growth'], 2)]);
    w('op', 'growAll', [fact('after', ['block'], 24), fact('after', ['growthCount'], 0), fact('after', ['cards', 'c3', 'growth'], 0), fact('after', ['actors', 0, 'gauge'], 0)]);
    event('interaction', 'moss-one-per-success', { e: 'grow', src: 'growAll', growth: 2 });
  }, { enemies: ['e_seed'], relics: ['symbiotic_moss'] }, { inputDomain: 'Supported setup gauge100, not earned gauge' });
  add('edge-lethal-reflect', [{ charId: 'sera' }], ['sera_double'], ['sera_double'], ({ play, command, w }) => {
    command('end'); const uid = play('sera_double');
    w('interaction', 'reflect-stops-multihit', [fact('after', ['phase'], 'DEFEAT'), fact('after', ['hp'], 0), fact('after', ['enemies', 0, 'hp'], 48), fact('after', ['cards', uid, 'used'], 1), fact('after', ['zones', 'discard'], [uid]), fact('response', ['ev', 3, 'e'], 'end'), fact('response', ['ev', 4, 'e'], 'resolved')]);
  }, { hp: 2, maxHp: 81, enemies: ['e_mirrorclerk'] }, { inputDomain: 'Explicit supported initial HP2/max81 for terminal boundary, not balance' });
  add('edge-player-same-hit-defeat-reward', [{ charId: 'sera' }, { charId: 'kaya', res: 4 }, { charId: 'nia' }], ['sera_round'], ['sera_round'], ({ play, command, w }) => {
    command('end'); const uid = play('sera_round');
    w('interaction', 'same-hit-defeat-reward', [fact('after', ['phase'], 'DEFEAT'), fact('after', ['hp'], 0), fact('after', ['killedMarked'], 1), fact('after', ['runFlags', 'coinsBonus'], 10), fact('after', ['cards', uid, 'used'], 1)]);
  }, { hp: 1, maxHp: 243, enemies: ['e_mirrorclerk'], difficulty: { hp: 1, dmg: 100 }, relics: ['detective_notebook'] }, { inputDomain: 'Explicit initial HP1 and difficulty HP1%, normal max243; terminal precedence only' });
  add('edge-reserved-void-shard-kill', [{ charId: 'sera' }], ['sera_last'], ['sera_last'], ({ play, command, w, event }) => {
    play('sera_last', 'after'); command('end'); command('end');
    event('interaction', 'reserved-last45', { e: 'dmg', src: 'card', n: 45 }); event('interaction', 'void-final3', { e: 'dmg', src: 'relic', n: 3 });
    w('interaction', 'post-resolve-victory', [fact('after', ['phase'], 'VICTORY'), fact('after', ['turn'], 3), fact('after', ['hp'], 57)]);
  }, { enemies: ['e_mirrorclerk'], difficulty: { hp: 88, dmg: 100 }, relics: ['void_shard'] }, { inputDomain: 'Authored difficulty HP88%; enemy starts46, no runtime HP edit' });
  out.push(['edge-exhaust-discard-reshuffle-live-zone', () => record('edge-exhaust-discard-reshuffle-live-zone', setup([{ charId: 'sion' }, { charId: 'kaya', res: 4 }, { charId: 'nia' }], ['sion_forget', 'com_sort', 'kaya_clue', 'kaya_clue', 'kaya_clue', 'kaya_clue'].map(id => spec(id)), { seed: 1, enemies: ['e_seed', 'e_seed'], difficulty: { hp: 50 }, relics: ['void_shard', 'detective_notebook'] }), { group: 'edge', inputDomain: 'Actual supported enemy difficulty HP50%; original partyHP243' }, ({ command, w }) => {
    command('play', { uid: 'c2', mode: 'now', picks: ['c3'] }); command('play', { uid: 'c6', mode: 'now', target: 'e7' }); command('play', { uid: 'c4', mode: 'now', target: 'e8' });
    command('play', { uid: 'c1', mode: 'now', picks: ['c3'] });
    w('interaction', 'live-discard-after-hook-reshuffle', [fact('before', ['zones', 'draw'], []), fact('before', ['zones', 'hand'], ['c1', 'c5']), fact('before', ['zones', 'discard'], ['c3', 'c2', 'c6', 'c4']), fact('after', ['zones', 'exhaust'], ['c3']), fact('after', ['zones', 'draw'], ['c6']), fact('after', ['block'], 10), fact('after', ['phase'], 'PLAYER'), fact('after', ['killedMarked'], 1)]);
  })]);
  add('edge-heal-overflow-rebirth', [{ charId: 'aila' }, { charId: 'lumi' }, { charId: 'sera' }], ['aila_rebirth', 'lumi_dash', 'aila_bell'], ['aila_rebirth', 'lumi_dash', 'aila_bell'], ({ play, command, w, event }) => {
    play('aila_rebirth'); play('lumi_dash'); w('op', 'selfdmg', [fact('before', ['hp'], 243), fact('after', ['hp'], 240)]);
    play('aila_bell'); event('op', 'heal', { e: 'heal', n: 3 }); w('interaction', 'heal-overflow1', [fact('after', ['hp'], 243), fact('after', ['block'], 1)]);
    command('end'); event('interaction', 'rebirth-overflow', { e: 'heal', n: 0 });
  });
  add('edge-oblivion-exhaust-hooks', [{ charId: 'sion', weaponId: 'w_sion', refine: 5 }, { charId: 'sera' }, { charId: 'kaya' }], fillDeck(['sion_oblivion', 'sion_erase']), ['sion_oblivion', 'sion_erase'], ({ find, play, w, event }) => {
    play('sion_oblivion'); w('interaction', 'oblivion-self-exhaust', [fact('after', ['block'], 7), fact('after', ['ap'], 4), fact('after', ['exhausted'], 1)]);
    play('sion_erase', 'now', { picks: [find('com_guard') || find('com_slash') || find('com_patch')] });
    w('op', 'exhaustPick', [fact('after', ['block'], 14), fact('after', ['exhausted'], 2), fact('after', ['ap'], 4)]);
    event('interaction', 'void-exhaust-rng-damage', { e: 'dmg', n: 3, src: 'relic' });
  }, { relics: ['void_shard', 'oblivion_ink'] });
  add('edge-greenhouse-growth', [{ charId: 'yuri', weaponId: 'w_yuri', refine: 1 }, { charId: 'sera' }, { charId: 'kaya' }], ['yuri_greenhouse', 'yuri_cocoon'], ['yuri_greenhouse', 'yuri_cocoon'], ({ play, command, w }) => {
    play('yuri_greenhouse'); const uid = play('yuri_cocoon', 'next'); command('end');
    w('op', 'grow', [fact('after', ['cards', uid, 'growth'], 1), fact('after', ['cards', uid, 'grown'], true), fact('after', ['growthCount'], 1)]);
    w('interaction', 'moss-yuri-greenhouse', [fact('after', ['block'], 16)]);
  }, { enemies: ['e_seed'], relics: ['symbiotic_moss'] });
  add('edge-blueprint-modded-counter', [{ charId: 'nia' }, { charId: 'sera' }, { charId: 'kaya' }], ['nia_blueprint', 'nia_tune', 'nia_masterwork'], ['nia_blueprint', 'nia_tune', 'nia_masterwork'], ({ find, play, w, event }) => {
    play('nia_blueprint'); const uid = find('nia_masterwork'); play('nia_tune', 'now', { picks: [uid] });
    w('op', 'tune', [fact('after', ['cards', uid, 'temp'], ['amp']), fact('after', ['tempParts'], 1)]);
    play('nia_masterwork'); event('counter', 'tempParts', { e: 'dmg', n: 48, src: 'card' });
  }, { relics: ['golden_quill'] });
  add('edge-casefile-marked', normal('kaya'), ['kaya_casefile', 'kaya_tracking', 'kaya_bolt'], ['kaya_casefile', 'kaya_tracking', 'kaya_bolt'], ({ play, event }) => {
    play('kaya_casefile'); play('kaya_tracking'); play('kaya_bolt'); event('interaction', 'casefile20percent', { e: 'dmg', n: 9, src: 'card' });
  });
  add('edge-echo-chamber-owner-change', [{ charId: 'rien' }, { charId: 'sera' }, { charId: 'nia' }], fillDeck(['rien_echochamber', 'rien_note', 'rien_note', 'sera_round']), ['rien_echochamber', 'rien_note', 'rien_note', 'sera_round'], ({ play, w }) => {
    play('rien_echochamber'); play('rien_note'); play('sera_round'); play('rien_note');
    w('interaction', 'echochamber-one-draw', [fact('after', ['echo'], 3), fact('after', ['turnFlags', 'echoChamber'], true), fact('after', ['zones', 'draw', 'length'], 2)]);
  });
  add('edge-next-boost-charge', [{ charId: 'rien' }, { charId: 'sera' }, { charId: 'nia' }], ['rien_duet', 'sera_aim', 'sera_round'], ['rien_duet', 'sera_aim', 'sera_round'], ({ play, w, event }) => {
    play('sera_aim'); play('rien_duet'); w('op', 'nextBoost', [fact('after', ['nextBoost'], { bps: 5000, from: 'rien' })]);
    play('sera_round'); event('op', 'buff', { e: 'dmg', src: 'card', n: 15 }); w('interaction', 'boost-charge-consumed', [fact('after', ['nextBoost'], null), fact('after', ['buffs'], [])]);
  });
  add('edge-free-next-empty-clock', [{ charId: 'rien', gauge: 100 }, { charId: 'sera' }, { charId: 'nia' }], ['sera_last', 'sera_round', 'sera_cover'], ['sera_last', 'sera_round', 'sera_cover'], ({ command, play, w }) => {
    command('ult', { actor: 'rien' }); w('op', 'freeNext', [fact('after', ['freeNext'], 2), fact('after', ['echo'], 3), fact('after', ['ap'], 4)]);
    play('sera_last', 'after'); play('sera_round', 'next'); play('sera_cover', 'next');
    w('interaction', 'free-consumes-first-clock', [fact('after', ['ap'], 3), fact('after', ['freeNext'], 0), fact('after', ['battleFlags', 'emptyClockUsed'], true)]);
  }, { relics: ['empty_clock'] });
  add('edge-recall-engraving-once', normal('sera'), [spec('sera_round', { mods: { e: 'e_recall' } }), 'com_recall', 'com_guard', 'com_slash', 'com_patch', 'com_sort', 'com_tactics', 'com_focus'], ['sera_round', 'com_recall'], ({ play, w }) => {
    const uid = play('sera_round'); w('interaction', 'engraving-first-draw', [fact('after', ['zones', 'draw', 'length'], 2)]);
    play('com_recall', 'now', { picks: [uid] }); w('op', 'recall', [fact('after', ['zones', 'hand', 'length'], 5)]);
    play('sera_round'); w('interaction', 'engraving-no-second-draw', [fact('after', ['zones', 'draw', 'length'], 2), fact('after', ['cards', uid, 'used'], 2)]);
  });
  add('edge-gauge-earned-cap-bypass', [{ charId: 'nia', weaponId: 'w_pocket_engine', refine: 5 }, { charId: 'sera' }, { charId: 'kaya' }], [0, 1, 2, 3].map(() => spec('nia_tune', { mods: { e: 'e_gauge' } })).concat([spec('com_rally')]), ['nia_tune', 'nia_tune', 'nia_tune', 'nia_tune', 'com_rally'], ({ battle, play, command, w }) => {
    for (let n = 0; n < 4; n++) play('nia_tune');
    w('interaction', 'basic-gauge-cap30-bypass48', [fact('after', ['actors', 0, 'gaugeTurn'], 30), fact('after', ['actors', 0, 'gauge'], 78)]);
    play('com_rally'); command('end'); play('com_rally');
    w('interaction', 'earned-gauge100', [fact('after', ['actors', 0, 'gauge'], 100)]);
    command('ult', { actor: 'nia' }); w('interaction', 'earned-ult-spends100', [fact('after', ['actors', 0, 'gauge'], 0), fact('after', ['actors', 0, 'gaugeTurn'], 0)]);
  }, { enemies: ['e_seed'] });
  add('edge-mark-on-lethal-hit', [{ charId: 'sera' }, { charId: 'kaya', res: 2 }, { charId: 'nia' }], fillDeck([spec('sera_round', { mods: { e: 'e_mark' } })]), ['sera_round'], ({ play, w }) => {
    play('sera_round'); w('interaction', 'mark-before-kill-rewards', [fact('after', ['killedMarked'], 1), fact('after', ['runFlags', 'coinsBonus'], 10), fact('after', ['enemies', 1, 'st', 'burn'], 3), fact('after', ['ap'], 4), fact('after', ['zones', 'draw', 'length'], 1)]);
  }, { enemies: ['e_seed', 'b_archivist'], relics: ['detective_notebook'] });
  add('edge-ember-ring', [{ charId: 'nia' }, { charId: 'ravien' }, { charId: 'sera' }], ['ravien_seal', 'nia_masterwork'], ['ravien_seal', 'nia_masterwork'], ({ play, event }) => {
    play('ravien_seal'); play('nia_masterwork'); event('interaction', 'ember10percent', { e: 'dmg', n: 26, src: 'card' });
  }, { relics: ['ember_ring'] });
  add('edge-strongest-party-heal', [{ charId: 'aila', weaponId: 'w_silver_hymnal', refine: 5 }, { charId: 'yuri', weaponId: 'w_choir_bell', refine: 5 }, { charId: 'sera' }], ['aila_overflow'], ['aila_overflow'], ({ play, w }) => {
    play('aila_overflow'); w('interaction', 'strongest-heal20-not-sum36', [fact('after', ['block'], 12)]);
  });
  add('edge-marked-conditional-true', normal('sera'), ['kaya_tracking', 'sera_double'], ['kaya_tracking', 'sera_double'], ({ play, w }) => {
    play('kaya_tracking'); play('sera_double'); w('condition', 'marked:true', [fact('response', ['ev', 'length'], 5), fact('response', ['ev', 3, 'n'], 4)]);
  });
  add('edge-intent-attack-conditional-true', normal('harin'), ['harin_taunt'], ['harin_taunt'], ({ play, command, w, event }) => {
    play('harin_taunt'); w('condition', 'intentAttack:true', [fact('after', ['enemies', 0, 'st', 'weak'], 1), fact('after', ['block'], 3)]);
    command('end'); event('interaction', 'weak-floor-and-counter', { e: 'hit', d: 9, blocked: 3 });
  }, { enemies: ['e_moth'] });
  add('edge-burning-break-bonus', normal('ravien'), ['ravien_seal', 'ravien_penalty'], ['ravien_seal', 'ravien_penalty'], ({ play, w }) => {
    play('ravien_seal'); play('ravien_penalty'); w('condition', 'burning:true', [fact('after', ['enemies', 0, 'brk'], 0), fact('after', ['enemies', 0, 'stunned'], true), fact('after', ['apNext'], 1)]);
    w('op', 'breakBonus', [fact('after', ['enemies', 0, 'brokenUntil'], 2)]);
  }, { enemies: ['e_moth'] });
  add('edge-debt-positive-effects', normal('ravien'), ['ravien_advance', 'ravien_interest', 'ravien_strike', 'ravien_auction'], ['ravien_advance', 'ravien_interest', 'ravien_strike', 'ravien_auction'], ({ play, event, w }) => {
    play('ravien_advance'); play('ravien_interest'); event('counter', 'debt', { e: 'dmg', src: 'card', n: 16 });
    play('ravien_strike'); w('condition', 'debt:true', [fact('after', ['enemies', 0, 'st', 'burn'], 2)]);
    play('ravien_auction'); event('counter', 'targetBurn', { e: 'dmg', src: 'card', n: 24 });
  });
  add('edge-debt-break-bonus-all', normal('ravien'), ['ravien_advance', 'ravien_settlement'], ['ravien_advance', 'ravien_settlement'], ({ play, w }) => {
    play('ravien_advance'); play('ravien_settlement'); w('op', 'breakBonusAll', [fact('after', ['enemies', 0, 'brk'], 0), fact('after', ['enemies', 1, 'brk'], 0), fact('after', ['enemies', 2, 'brk'], 0), fact('after', ['apNext'], 2), fact('after', ['breakApTurn'], 2)]);
  }, { enemies: ['e_moth', 'e_moth', 'e_moth'] });
  add('edge-modded-conditional-true', normal('nia'), [spec('nia_shock', { mods: { c: 'c_precise' } })], ['nia_shock'], ({ play, w, event }) => {
    play('nia_shock'); event('condition', 'modded:true', { e: 'dmg', n: 16, src: 'card' }); w('interaction', 'precise-plus-conditional-shock', [fact('after', ['enemies', 0, 'brk'], 7)]);
  });
  add('edge-target-low-hp-conditional-true', normal('kaya'), ['kaya_tracking', 'kaya_bolt', 'com_slash', 'kaya_verdict'], ['kaya_tracking', 'kaya_bolt', 'com_slash', 'kaya_verdict'], ({ play, command, w, event }) => {
    play('kaya_tracking'); play('kaya_bolt'); play('com_slash'); command('end');
    w('interaction', 'target-hp-produced1', [fact('after', ['enemies', 0, 'hp'], 1)]);
    play('kaya_verdict'); event('condition', 'targetHpBelow:true', { e: 'dmg', src: 'card', n: 40 });
  }, { enemies: ['e_moth'] });
  add('edge-killed-this-conditional-true', normal('yuri'), [spec('yuri_cocoon_prey'), 'com_guard', 'com_slash', 'com_patch', 'com_sort', 'com_tactics', 'com_focus', 'com_barrier'], ['yuri_cocoon_prey'], ({ play, w }) => {
    play('yuri_cocoon_prey'); w('condition', 'killedThis:true', [fact('after', ['enemies', 0, 'alive'], false), fact('before', ['zones', 'draw', 'length'], 3), fact('after', ['zones', 'draw', 'length'], 2)]);
  }, { enemies: ['e_seed', 'b_archivist'] });
  for (const res of [0, 2]) add(`edge-lumi-earned-low-hp-r${res}`, [{ charId: 'lumi', res, gauge: 100 }], ['lumi_overdrive', 'lumi_dash'], ['lumi_overdrive', 'lumi_dash'], ({ play, command, w, event }) => {
    play('lumi_overdrive'); play('lumi_dash'); command('end'); play('lumi_overdrive'); play('lumi_dash'); command('end');
    w('interaction', `earned-hp-r${res}`, [fact('after', ['hp'], res ? 41 : 37)]);
    command('ult', { actor: 'lumi' }); event('condition', `hpBelow:${!res}`, { e: 'dmg', n: res ? 11 : 10, src: 'ult' });
    w('interaction', `ult-hit-count-r${res}`, [fact('response', ['ev', 'length'], res ? 6 : 8)]);
  }, {}, { inputDomain: 'Normal soloHP81; only gauge100 supplied, lowHP earned by commands' });
  add('edge-growth-counters-and-picks', normal('yuri'), ['yuri_dew', 'yuri_cocoon', 'yuri_sprout', 'yuri_mutual', 'yuri_canopy'], ['yuri_dew', 'yuri_cocoon', 'yuri_sprout', 'yuri_mutual', 'yuri_canopy'], ({ find, play, event, w }) => {
    const root = find('yuri_cocoon'); play('yuri_dew', 'now', { picks: ['missing', root] });
    w('op', 'growPick', [fact('after', ['cards', root, 'growth'], 1), fact('after', ['growthCount'], 1)]);
    play('yuri_mutual'); event('counter', 'growthSum', { e: 'dmg', src: 'card', n: 8 });
    play('yuri_canopy'); event('counter', 'growthCards', { e: 'block', src: 'card', n: 18 });
  });
  add('edge-growth-in-hand-false', normal('yuri'), fillDeck(['yuri_tend']), ['yuri_tend'], ({ play, w }) => {
    play('yuri_tend'); w('condition', 'growthInHand:false', [fact('before', ['zones', 'draw', 'length'], 3), fact('after', ['zones', 'draw', 'length'], 3)]);
  });
  add('edge-exhausted-counter', normal('sion'), ['sion_erase', 'sion_pageblade', 'com_guard'], ['sion_erase', 'sion_pageblade', 'com_guard'], ({ find, play, event, w }) => {
    play('sion_erase', 'now', { picks: [find('com_guard')] }); play('sion_pageblade');
    event('counter', 'exhausted', { e: 'dmg', n: 7, src: 'card' }); w('interaction', 'actual-exhaust-count1', [fact('after', ['exhausted'], 1)]);
  });
  add('edge-counter-scaling', normal('harin'), ['harin_shield', 'harin_oath', 'harin_glacier'], ['harin_shield', 'harin_oath', 'harin_glacier'], ({ play, event, w }) => {
    play('harin_shield'); play('harin_oath'); w('op', 'counter', [fact('after', ['counter'], 7)]);
    play('harin_glacier'); event('counter', 'counter', { e: 'dmg', src: 'card', n: 24 });
  });
  add('edge-block-scaling', [{ charId: 'harin', res: 4 }, { charId: 'nia', weaponId: 'w_ledger_lock', refine: 5 }, { charId: 'sera' }], ['harin_frostwall', 'harin_retaliate'], ['harin_frostwall', 'harin_retaliate'], ({ play, w, event }) => {
    play('harin_frostwall'); w('interaction', 'block41', [fact('after', ['block'], 41)]); play('harin_retaliate'); event('counter', 'block', { e: 'dmg', src: 'card', n: 32 });
  }, { relics: ['bronze_cog'] });
  add('edge-missing-hp-counter', [{ charId: 'lumi' }, { charId: 'sera' }, { charId: 'kaya' }], fillDeck(['lumi_thunderstep', 'lumi_overdrive', 'lumi_dash', 'lumi_adrenaline']), ['lumi_thunderstep', 'lumi_overdrive', 'lumi_dash', 'lumi_adrenaline'], ({ play, w }) => {
    play('lumi_thunderstep'); play('lumi_overdrive'); play('lumi_dash'); play('lumi_adrenaline');
    w('counter', 'missingHp10', [fact('before', ['hp'], 231), fact('before', ['zones', 'draw', 'length'], 2), fact('after', ['zones', 'draw', 'length'], 1)]);
  });
  add('edge-contract-counter-collateral', normal('ravien'), fillDeck(['ravien_advance', 'ravien_collateral', 'ravien_goldscale']), ['ravien_advance', 'ravien_collateral', 'ravien_goldscale'], ({ play, w }) => {
    play('ravien_advance'); play('ravien_collateral'); w('op', 'collateral', [fact('after', ['sealedUntil'], 2), fact('after', ['block'], 16)]);
    play('ravien_goldscale'); w('counter', 'contracts', [fact('after', ['contracts'], 2), fact('before', ['zones', 'draw', 'length'], 3), fact('after', ['zones', 'draw', 'length'], 1)]);
  });
  add('edge-echo-counter-conditional', [{ charId: 'rien' }, { charId: 'sera' }, { charId: 'nia' }], fillDeck(['rien_amplify', 'rien_chord', 'rien_rest']), ['rien_amplify', 'rien_chord', 'rien_rest'], ({ play, event, w }) => {
    play('rien_amplify'); w('op', 'echo', [fact('after', ['echo'], 2)]); play('rien_chord'); event('counter', 'echo', { e: 'dmg', n: 12, src: 'card' });
    play('rien_rest'); w('condition', 'echo:true', [fact('before', ['zones', 'draw', 'length'], 3), fact('after', ['zones', 'draw', 'length'], 2)]);
  });
  for (const cleanse of ['aila_veil', 'aila_cleanse']) add(`edge-${cleanse}-real-debuff`, normal('aila'), [cleanse], [cleanse], ({ play, command, w }) => {
    command('end'); w('interaction', 'enemy-produced-weak', [fact('after', ['pst', 'weak'], 1)]); play(cleanse);
    w('op', cleanse === 'aila_veil' ? 'cleanseOne' : 'cleanse', [fact('after', ['pst', 'weak'], 0), fact('after', ['block'], cleanse === 'aila_veil' ? 7 : 8)]);
  }, { enemies: ['e_lantern'] });
  add('edge-restraint-kept-bonus', normal('ravien'), ['ravien_restraint', 'sera_round'], ['ravien_restraint', 'sera_round'], ({ play, command, w, event }) => {
    play('ravien_restraint'); w('op', 'restraint', [fact('after', ['restraint', 'active'], true)]); play('sera_round', 'next'); command('end');
    event('interaction', 'restraint30percent', { e: 'dmg', src: 'card', n: 15 }); w('interaction', 'bonus-consumed', [fact('after', ['bonus', 'avail'], false)]);
  });
  add('edge-restraint-broken-penalty', [{ charId: 'ravien' }, { charId: 'rien' }, { charId: 'sera' }], ['ravien_restraint', 'rien_note', 'rien_note', 'rien_note'], ['ravien_restraint', 'rien_note', 'rien_note', 'rien_note'], ({ play, command, w }) => {
    play('ravien_restraint'); for (let n = 0; n < 3; n++) play('rien_note');
    w('interaction', 'fourth-play-breaks-restraint', [fact('after', ['drawPenalty'], 1), fact('after', ['restraint', 'active'], false)]); command('end');
    w('interaction', 'restraint-next-draw4', [fact('after', ['zones', 'hand', 'length'], 4), fact('after', ['drawPenalty'], 0)]);
  });
  add('edge-yuri-r6-once-recharge', [{ charId: 'yuri', res: 6, gauge: 100 }, { charId: 'sera' }, { charId: 'kaya' }], ['yuri_cocoon', 'yuri_sprout', 'yuri_root', ...Array(7).fill('com_rally')], [], ({ battle, command, play, w }) => {
    command('ult', { actor: 'yuri' });
    w('interaction', 'first-grow-all-once', [fact('after', ['cards', 'c1', 'growth'], 2), fact('after', ['cards', 'c2', 'growth'], 2), fact('after', ['cards', 'c3', 'growth'], 1)]);
    for (let n = 0; battle.actor('yuri').gauge < 100; n++) {
      assert.ok(n < 30, 'real rally recharge bound');
      if (battle.s.ap && battle.s.zones.hand.some(u => battle.s.cards[u].id === 'com_rally')) play('com_rally'); else command('end');
    }
    command('ult', { actor: 'yuri' });
    w('interaction', 'no-second-grow-all', [fact('after', ['cards', 'c1', 'growth'], 3), fact('after', ['cards', 'c2', 'growth'], 2), fact('after', ['cards', 'c3', 'growth'], 2), fact('after', ['growthCount'], 0)]);
  }, { enemies: ['e_seed'], relics: ['symbiotic_moss'] }, { inputDomain: 'First supported gauge100; second gauge100 is earned by actual rally commands' });
  add('edge-growth-cap-once-and-saturation', normal('yuri'), ['yuri_cocoon', 'yuri_root', 'yuri_seedvault', 'yuri_dew', 'yuri_dew'], ['yuri_cocoon', 'yuri_root', 'yuri_seedvault'], ({ battle, play, command, find, w }) => {
    const cocoon = play('yuri_cocoon', 'next'); const root = play('yuri_root', 'next'); command('end');
    w('interaction', 'two-reserve-growths', [fact('after', ['growthCount'], 2), fact('after', ['cards', cocoon, 'growth'], 1), fact('after', ['cards', root, 'growth'], 1)]);
    play('yuri_seedvault', 'next'); command('end');
    w('interaction', 'party-growth-cap3', [fact('after', ['growthCount'], 3)]);
    const before = battle.s.cards[cocoon].growth; assert.equal(before, 1);
    if (!find('yuri_cocoon')) command('end');
    play('yuri_cocoon', 'next'); command('end');
    w('interaction', 'grown-once-no-second-increment', [fact('after', ['cards', cocoon, 'growth'], 1), fact('after', ['growthCount'], 3)]);
  }, { enemies: ['e_seed'], relics: ['symbiotic_moss'] });
  add('edge-growth-saturation-three', [{ charId: 'yuri', gauge: 100 }, { charId: 'sera' }, { charId: 'kaya' }], ['yuri_cocoon', ...Array(7).fill('com_rally')], [], ({ battle, command, play, w }) => {
    for (let use = 0; use < 4; use++) {
      if (use) for (let n = 0; battle.actor('yuri').gauge < 100; n++) {
        assert.ok(n < 40); if (battle.s.ap && battle.s.zones.hand.some(u => battle.s.cards[u].id === 'com_rally')) play('com_rally'); else command('end');
      }
      command('ult', { actor: 'yuri' });
      w('interaction', `uncapped-ult-growth-${use + 1}`, [fact('after', ['cards', 'c1', 'growth'], Math.min(3, use + 1)), fact('after', ['growthCount'], 0)]);
    }
  }, { enemies: ['e_seed'], relics: ['symbiotic_moss'] }, { inputDomain: 'First supported gauge100; all later ult gauges earned, original HP243' });
  add('edge-sera-r6-follow-up', [{ charId: 'sera', res: 6, gauge: 100 }, { charId: 'nia' }, { charId: 'kaya' }], ['sera_round'], ['sera_round'], ({ command, play, w, event }) => {
    command('ult', { actor: 'sera' }); play('sera_round', 'next'); command('end');
    event('interaction', 'sera-r6-due19', { e: 'dmg', src: 'card', n: 19 }); w('interaction', 'sera-r6-cleared-after-due', [fact('after', ['battleFlags', 'seraR6Turn'], 0)]);
  }, {}, { inputDomain: 'Explicit supported gauge100' });
  add('edge-kaya-r6-refund', [{ charId: 'kaya', res: 6, gauge: 100 }, { charId: 'sera' }, { charId: 'nia' }], ['com_guard'], [], ({ command, w }) => {
    command('ult', { actor: 'kaya' }); w('interaction', 'kaya-r6-refund50', [fact('after', ['actors', 0, 'gauge'], 50), fact('after', ['killedMarked'], 1)]);
  }, { enemies: ['e_seed', 'b_archivist'] }, { inputDomain: 'Explicit supported gauge100' });
  add('edge-harin-r6-counter-double', [{ charId: 'harin', res: 6, gauge: 100 }, { charId: 'sera' }, { charId: 'nia' }], ['com_guard'], [], ({ command, event, w }) => {
    command('ult', { actor: 'harin' }); w('interaction', 'counter10-before-double', [fact('after', ['counter'], 10)]); command('end');
    event('interaction', 'counter-double20', { e: 'dmg', n: 20, src: 'counter' }); w('interaction', 'counter-reset-after-double', [fact('after', ['counter'], 0)]);
  }, { enemies: ['b_bell'] }, { inputDomain: 'Explicit supported gauge100' });
  add('edge-buff-free-overload-consume', [{ charId: 'lumi' }, { charId: 'aila' }, { charId: 'sera' }], ['lumi_secondwind', 'lumi_overdrive', 'lumi_dash'], ['lumi_secondwind', 'lumi_overdrive', 'lumi_dash'], ({ play, w }) => {
    play('lumi_secondwind'); play('lumi_overdrive'); w('interaction', 'overload-buff-free-once', [fact('after', ['hp'], 243), fact('after', ['buffs'], [])]);
    play('lumi_dash'); w('interaction', 'next-overload-paid', [fact('after', ['hp'], 240), fact('after', ['block'], 5)]);
  }, { relics: ['lightning_rod'] });
  add('edge-ult-reflect-does-not-consume-card-charge', [{ charId: 'sera', gauge: 100 }, { charId: 'ravien' }, { charId: 'lumi' }], ['sera_aim', 'ravien_advance', 'sera_round'], ['sera_aim', 'ravien_advance', 'sera_round'], ({ play, command, w, event }) => {
    command('end'); play('sera_aim'); play('ravien_advance'); command('ult', { actor: 'sera' });
    event('interaction', 'ult-no-card-charge-ledger', { e: 'dmg', n: 28, src: 'ult', card: null });
    w('interaction', 'ult-no-reflect', [fact('after', ['hp'], 243), fact('after', ['buffs', 0, 's'], 'charge')]);
    play('sera_round'); event('interaction', 'card-charge-ledger-additive', { e: 'dmg', n: 12, src: 'card' }); w('interaction', 'card-reflect3', [fact('after', ['hp'], 240)]);
  }, { enemies: ['e_mirrorclerk'], relics: ['credit_ledger'] }, { inputDomain: 'Supported initial gauge100; actual debt and charge created by cards' });
  add('edge-volt-first-damage-op', [{ charId: 'lumi' }, { charId: 'sera' }, { charId: 'kaya' }], ['lumi_spark', 'lumi_jab'], ['lumi_spark', 'lumi_jab'], ({ play, event }) => {
    play('lumi_spark'); event('interaction', 'volt-flat4-first-op-hit1', { e: 'dmg', src: 'card', n: 8 }); event('interaction', 'volt-flat4-first-op-hit2', { e: 'dmg', src: 'card', n: 8 }, 1);
    play('lumi_jab'); event('interaction', 'volt-once-per-turn', { e: 'dmg', src: 'card', n: 8 });
  }, { relics: ['volt_coil'] });
  add('edge-first-attack-and-owner-attribution', [{ charId: 'sera', weaponId: 'w_hunter_rifle', refine: 5, skillLv: 9 }, { charId: 'nia' }, { charId: 'kaya' }], ['com_slash', spec('wc_hunter_rifle', { wielder: 'sera' }), 'sera_round'], ['com_slash', 'wc_hunter_rifle', 'sera_round'], ({ play, event, w }) => {
    play('com_slash'); event('interaction', 'common-no-owner-weapon-or-skill', { e: 'dmg', n: 6, src: 'card' });
    play('wc_hunter_rifle'); event('interaction', 'weapon-no-character-skill-level', { e: 'dmg', n: 8, src: 'card' });
    play('sera_round'); event('interaction', 'character-skill-level', { e: 'dmg', n: 10, src: 'card' }); w('interaction', 'battle-first-atk-flag', [fact('after', ['firstAtkDone'], true)]);
  }, {}, { inputDomain: 'Explicit supported skillLv9 numeric domain; no HP or card numbers changed' });
  add('edge-reservation-dead-retarget', normal('sera'), ['sera_round', 'sera_double'], ['sera_round', 'sera_double'], ({ battle, play, command, w, event }) => {
    const target = battle.alive()[0].uid; play('sera_round', 'next', { target }); play('sera_double', 'now', { target }); command('end');
    event('interaction', 'ordinary-reservation-retarget', { e: 'dmg', card: 'sera_round', uid: battle.s.enemies[1].uid, n: 12, src: 'card' });
    w('interaction', 'dead-target-did-not-fizzle', [fact('after', ['cards', 'c1', 'used'], 1)]);
  }, { enemies: ['e_seed', 'b_archivist'] });
  add('edge-fixed-target-mark-expiry-fizzle', normal('kaya'), ['kaya_clue', 'kaya_deduce'], ['kaya_clue', 'kaya_deduce'], ({ play, command, w, event }) => {
    play('kaya_clue'); const uid = play('kaya_deduce', 'after'); command('end'); command('end');
    event('interaction', 'fixed-mark-expired', { e: 'fizzle', uid });
    event('interaction', 'expected-fixed-target-fizzle', { e: 'resolved', uid, fizzled: true }); w('interaction', 'fizzle-still-cleans-up', [fact('after', ['cards', uid, 'used'], 1)]);
  }, {}, { expectedFizzle: true, denominator: 'Negative resolution behavior; never used as positive base-mode coverage' });
  add('edge-terminal-reservation-conservation', [{ charId: 'sera' }], ['sera_last', 'sera_round', 'sera_cover'], ['sera_last', 'sera_round', 'sera_cover'], ({ play, command, w }) => {
    const last = play('sera_last', 'after'); command('end'); const current = play('sera_round', 'next'); const future = play('sera_cover', 'after'); command('end');
    w('interaction', 'terminal-due-future-conservation', [fact('after', ['phase'], 'VICTORY'), fact('after', ['res'], []), fact('after', ['zones', 'exhaust'], [last]), fact('after', ['zones', 'discard'], [future, current])]);
  }, { enemies: ['e_mirrorclerk'], difficulty: { hp: 88, dmg: 100 }, relics: ['void_shard'] }, { inputDomain: 'Supported HP88% enemy difficulty for exact45+3 post-resolve kill' });
  add('edge-envelope-multihit-continues', [{ charId: 'sera' }], ['sera_double'], ['sera_double'], ({ command, play, w }) => {
    command('end'); play('sera_double'); w('interaction', 'envelope-only-first-hit-survival', [fact('after', ['phase'], 'DEFEAT'), fact('after', ['hp'], 0), fact('after', ['runFlags', 'envelopeUsed'], true), fact('after', ['enemies', 0, 'hp'], 44)]);
  }, { hp: 2, maxHp: 81, enemies: ['e_mirrorclerk'], relics: ['memory_envelope'] }, { inputDomain: 'Explicit initialHP2/max81 terminal boundary' });
  add('edge-lethal-reflect-aoe-stops-targets', [{ charId: 'lumi' }], ['lumi_chain'], ['lumi_chain'], ({ command, play, w }) => {
    command('end'); play('lumi_chain'); w('interaction', 'aoe-stops-after-fatal-first-target', [fact('after', ['phase'], 'DEFEAT'), fact('after', ['enemies', 0, 'hp'], 43), fact('after', ['enemies', 1, 'hp'], 46)]);
  }, { hp: 2, maxHp: 81, enemies: ['e_mirrorclerk', 'e_shelf'] }, { inputDomain: 'Explicit initialHP2/max81; second enemy remains fullHP' });
  add('edge-lethal-reflect-random-stops-hits', [{ charId: 'lumi' }], ['lumi_storm'], ['lumi_storm'], ({ command, play, w }) => {
    command('end'); play('lumi_storm'); w('interaction', 'random-stops-on-first-reflect', [fact('after', ['phase'], 'DEFEAT'), fact('after', ['enemies', 0, 'hp'], 42)]);
  }, { hp: 11, maxHp: 81, enemies: ['e_mirrorclerk'] }, { inputDomain: 'Explicit initialHP11; actual selfcost8 leaves3 then reflect3, no runtime mutation' });
  add('edge-score-three-owners', [{ charId: 'sera' }, { charId: 'nia' }, { charId: 'kaya' }], fillDeck(['sera_round', 'nia_tune', 'kaya_clue']), ['sera_round', 'nia_tune', 'kaya_clue'], ({ play, w }) => {
    play('sera_round'); play('nia_tune'); play('kaya_clue'); w('interaction', 'score-third-owner-draw1', [fact('before', ['zones', 'draw', 'length'], 3), fact('after', ['zones', 'draw', 'length'], 2), fact('after', ['turnFlags', 'score'], true)]);
  }, { relics: ['unfinished_score'] });
  add('edge-silver-needle-real-injury', [{ charId: 'lumi' }, { charId: 'sera' }, { charId: 'nia' }], ['lumi_dash', 'com_breaker'], ['lumi_dash', 'com_breaker'], ({ play, w }) => {
    play('lumi_dash'); play('com_breaker'); w('interaction', 'break-heal3', [fact('before', ['hp'], 240), fact('after', ['hp'], 243), fact('after', ['enemies', 0, 'stunned'], true)]);
  }, { enemies: ['e_moth'], relics: ['silver_needle'] });
  for (const envelope of [false, true]) add(`edge-silver-needle-${envelope ? 'envelope-survival' : 'no-revival'}`, [{ charId: 'sera' }], ['com_breaker'], ['com_breaker'], ({ play, command, w }) => {
    play('com_breaker'); command('end'); play('com_breaker');
    w('interaction', envelope ? 'break-heals-envelope-survivor' : 'break-cannot-heal-hp0', [fact('after', ['hp'], envelope ? 4 : 0), fact('after', ['phase'], envelope ? 'PLAYER' : 'DEFEAT'), fact('after', ['enemies', 0, 'brk'], 0), fact('after', ['enemies', 0, 'hp'], 44)]);
  }, { hp: 2, maxHp: 81, enemies: ['e_mirrorclerk'], relics: ['silver_needle', ...(envelope ? ['memory_envelope'] : [])] }, { inputDomain: 'Explicit initialHP2; all break/reflect state produced by real cards/end' });
  add('edge-frost-first-break-once', normal('harin'), ['harin_glacier', 'harin_glacier'], ['harin_glacier', 'harin_glacier'], ({ play, w }) => {
    play('harin_glacier'); w('interaction', 'frost-crystal-first-plus1', [fact('after', ['enemies', 0, 'brk'], 7)]); play('harin_glacier');
    w('interaction', 'frost-crystal-no-second-plus1', [fact('after', ['enemies', 0, 'brk'], 5)]);
  }, { enemies: ['b_bell'], relics: ['frost_crystal'] });
  add('edge-reserved-break-relics', normal('sera'), ['sera_timed'], ['sera_timed'], ({ play, command, w }) => {
    play('sera_timed', 'next'); command('end'); w('interaction', 'precision-eye-break5', [fact('after', ['enemies', 0, 'brk'], 7), fact('after', ['cards', 'c1', 'used'], 1)]);
  }, { relics: ['precision_lens', 'eye_of_rift'] });
  add('edge-foresight-broken-damage', [{ charId: 'sera', res: 2 }, { charId: 'nia' }, { charId: 'kaya' }], ['com_breaker', 'com_breaker', 'sera_timed'], ['com_breaker', 'com_breaker', 'sera_timed'], ({ play, command, event, w }) => {
    play('com_breaker'); play('com_breaker'); play('sera_timed', 'next'); command('end');
    event('interaction', 'foresight40-times-broken25', { e: 'dmg', src: 'card', card: 'sera_timed', n: 38 }); w('interaction', 'stunned-skip-before-due', [fact('after', ['enemies', 0, 'stunned'], false)]);
  }, { enemies: ['e_mirrorclerk'] });
  add('edge-vulnerable-weak-and-gear-math', [{ charId: 'sera', power4: 177, skillLv: 4, gearBps: 1234 }, { charId: 'nia' }, { charId: 'kaya' }], ['com_pin', spec('sera_timed', { plus: true, mods: { c: 'c_delay' } })], ['com_pin', 'sera_timed'], ({ play, command, event }) => {
    play('com_pin'); play('sera_timed', 'next'); command('end');
    // ceil22*13/10=29; delay40% + gear12.34% + Nia15%; floor(29*177/160*1.12*1.6734*1.25)=75.
    event('interaction', 'single-division-damage75', { e: 'dmg', src: 'card', n: 75 });
  }, {}, { inputDomain: 'Supported power4=177/skillLv4/gear1234 setup arithmetic; original HP243' });
  add('edge-pierce-split-rounding', normal('sera'), [spec('sera_round', { mods: { e: 'e_pierce' } })], ['sera_round'], ({ command, play, w, event }) => {
    command('end'); play('sera_round'); event('interaction', 'pierce-half4-normal4', { e: 'dmg', n: 8, blocked: 4, src: 'card' }); w('interaction', 'pierce-hp4-block6', [fact('after', ['enemies', 0, 'hp'], 42), fact('after', ['enemies', 0, 'block'], 6)]);
  }, { enemies: ['e_shelf'] });
  add('edge-heal-up-weapon-floor-order', [{ charId: 'aila', hp: 83, skillLv: 3, weaponId: 'w_silver_hymnal', refine: 2 }, { charId: 'sera' }, { charId: 'kaya' }], ['aila_prayer', 'aila_overflow'], ['aila_prayer', 'aila_overflow'], ({ play, w }) => {
    play('aila_prayer'); play('aila_overflow');
    // floor(10*83/81*1.08)=11; floor(11*1.5)=16; floor(16*1.125)=18.
    w('interaction', 'heal-three-floor-boundaries', [fact('after', ['block'], 18), fact('after', ['hp'], 245), fact('after', ['buffs'], [])]);
  }, {}, { inputDomain: 'Supported actor vitality83/skillLv3; party max245 derived from actors, not inflated to prolong combat' });
  add('edge-party-average-common-attribution', [{ charId: 'sera', power4: 161, hp: 82, skillLv: 10 }, { charId: 'nia', power4: 160, hp: 81 }, { charId: 'kaya', power4: 160, hp: 81 }], [spec('com_slash', { assigned: 'sera' }), spec('com_guard', { assigned: 'sera' })], ['com_slash', 'com_guard'], ({ play, event, w }) => {
    play('com_slash'); event('interaction', 'common-rounded-party-power160', { e: 'dmg', n: 6, src: 'card' }); play('com_guard');
    w('interaction', 'assigned-is-not-owner', [fact('after', ['actors', 0, 'gauge'], 0), fact('after', ['block'], 6)]);
  }, {}, { inputDomain: 'Supported heterogeneous actor stats; actual max244=82+81+81; assigned metadata never changes ownership' });
  add('edge-buff-max-not-add', normal('sera'), ['sera_aim', 'com_focus', spec('wc_sera', { wielder: 'sera' }), 'sera_round'], ['sera_aim', 'com_focus', 'wc_sera', 'sera_round'], ({ play, w, event }) => {
    play('sera_aim'); play('com_focus'); play('wc_sera'); w('interaction', 'charge-maximum4000', [fact('after', ['buffs'], [{ s: 'charge', n: 4000, until: 2 }])]);
    play('sera_round'); event('interaction', 'charge40-only', { e: 'dmg', n: 11, src: 'card' });
  });
  add('edge-hand-overflow-generated', [{ charId: 'nia', res: 6, gauge: 100 }, { charId: 'sera' }, { charId: 'kaya' }], Array(12).fill('nia_drone'), ['nia_drone'], ({ battle, play, command, w }) => {
    command('ult', { actor: 'nia' });
    for (let n = 0; n < 8 && battle.s.zones.hand.length < battle.s.handMax; n++) {
      if (battle.s.zones.hand.some(u => battle.s.cards[u].id === 'nia_drone') && battle.s.ap >= battle.costOf(battle.s.zones.hand.find(u => battle.s.cards[u].id === 'nia_drone'), 'now')) play('nia_drone');
      else if (battle.s.zones.hand.some(u => battle.s.cards[u].id === 'com_tactics') && battle.s.ap >= battle.costOf(battle.s.zones.hand.find(u => battle.s.cards[u].id === 'com_tactics'), 'now')) play('com_tactics'); else break;
    }
    assert.equal(battle.s.zones.hand.length, 10, 'real generated/draw hand reached cap');
    const drone = battle.s.zones.hand.find(u => battle.s.cards[u].id === 'nia_drone');
    assert.ok(drone); command('play', { uid: drone, mode: 'now' });
    w('interaction', 'create-overflow-to-discard', [fact('after', ['zones', 'hand', 'length'], 10), fact('after', ['seq'], 23), fact('after', ['cards', 'c23', 'token'], true), fact('after', ['zones', 'discard', 4], 'c23')]);
  }, {}, { inputDomain: 'Supported NiaR6/gauge100; hand generated by actual ult/cards, no hand injection' });
  add('edge-copy-delivery-after-draw-overflow', [{ charId: 'sera' }, { charId: 'nia' }, { charId: 'kaya' }], [spec('sera_round', { mods: { c: 'c_echo' } }), spec('nia_fieldlab', { plus: true }), spec('nia_fieldlab', { plus: true }), ...Array(11).fill('com_guard')], ['sera_round', 'nia_fieldlab', 'nia_fieldlab'], ({ battle, play, command, w, event }) => {
    play('sera_round', 'next'); play('nia_fieldlab', 'after'); play('nia_fieldlab', 'after'); command('end'); command('end');
    event('interaction', 'copy-after-normal-draw', { e: 'echoCopy', uid: 'c16' });
    w('interaction', 'copy-overflow-to-discard', [fact('after', ['zones', 'hand', 'length'], 10), fact('after', ['cards', 'c16', 'free'], true), fact('after', ['zones', 'discard', battle.s.zones.discard.length - 1], 'c16')]);
  }, { relics: ['cracked_hourglass'] });
  add('edge-log400-after-real-commands', [{ charId: 'sera' }], Array(4).fill('com_guard'), Array(4).fill('com_guard'), ({ play, command, w }) => {
    for (let turn = 0; turn < 45; turn++) { for (let n = 0; n < 4; n++) play('com_guard'); command('end'); }
    w('interaction', 'log400-trims-oldest', [fact('after', ['log', 'length'], 400), fact('after', ['log', 0, 't'], 17), fact('after', ['log', 0, 'e'], 'play'), fact('after', ['log', 399, 'e'], 'turn'), fact('after', ['log', 399, 'turn'], 46)]);
  }, { enemies: ['e_seed'] });
  for (const id of ['kaya_crossfire', 'kaya_ignite']) add(`edge-${id}-marked-branch`, normal('kaya'), ['kaya_tracking', id], ['kaya_tracking', id], ({ play, event, w }) => {
    play('kaya_tracking'); play(id);
    if (id === 'kaya_crossfire') { event('interaction', 'crossfire-third-marked-hit', { e: 'dmg', src: 'card', n: 7 }, 2); w('interaction', 'crossfire-three-hits', [fact('after', ['enemies', 0, 'hp'], 209)]); }
    else w('interaction', 'ignite-marked-double-burn', [fact('after', ['enemies', 0, 'st', 'burn'], 9)]);
  });
  add('edge-close-case-kill-refunds', normal('kaya'), ['kaya_tracking', 'kaya_closecase'], ['kaya_tracking', 'kaya_closecase'], ({ play, w }) => {
    play('kaya_tracking'); play('kaya_closecase'); w('interaction', 'killed-this-ap2-plus-deduction1', [fact('after', ['ap'], 3), fact('after', ['enemies', 0, 'alive'], false)]);
  }, { enemies: ['e_seed', 'b_archivist'] });
  add('edge-feedback-echo-three', [{ charId: 'rien' }, { charId: 'sera' }, { charId: 'nia' }], ['rien_amplify', 'rien_note', 'rien_feedback'], ['rien_amplify', 'rien_note', 'rien_feedback'], ({ play, w }) => {
    play('rien_amplify'); play('rien_note'); play('rien_feedback'); w('interaction', 'feedback-echo3-refund1', [fact('after', ['ap'], 4), fact('after', ['echo'], 3)]);
  });
  add('edge-contract-seal-real-cap5', normal('ravien'), ['ravien_advance', 'ravien_bigloan'], ['ravien_advance', 'ravien_bigloan'], ({ play, w }) => {
    play('ravien_advance'); play('ravien_bigloan'); w('interaction', 'contract-seal-allows-debt5', [fact('after', ['debts', 0, 'n'], 2), fact('after', ['debts', 1, 'n'], 3), fact('after', ['debtCap'], 5), fact('after', ['ap'], 8)]);
  }, { relics: ['contract_seal'] });
  add('edge-tuning-fork-real-echo4', [{ charId: 'rien' }, { charId: 'sera' }, { charId: 'nia' }], ['rien_amplify', 'rien_note', 'rien_chord'], ['rien_amplify', 'rien_note', 'rien_chord'], ({ play, w, event }) => {
    play('rien_amplify'); play('rien_note'); play('rien_chord'); event('interaction', 'fork-echo4-chord18', { e: 'dmg', src: 'card', n: 18 }); w('interaction', 'echo-max4', [fact('after', ['echo'], 4)]);
  }, { relics: ['tuning_fork'] });
  add('edge-forged-lineage-does-not-grow', normal('yuri'), [spec('sera_round', { from: 'yuri_cocoon' }), 'yuri_mutual'], ['sera_round', 'yuri_mutual'], ({ play, command, event, w }) => {
    play('sera_round', 'next'); command('end'); w('interaction', 'unrelated-from-not-growth', [fact('after', ['growthCount'], 0), fact('after', ['cards', 'c1', 'growth'], 0), fact('after', ['block'], 0)]);
    play('yuri_mutual'); event('interaction', 'forged-from-no-effective-growth3', { e: 'dmg', src: 'card', n: 7 });
  });
  add('edge-growth-once-below-cap', normal('yuri'), ['yuri_cocoon'], ['yuri_cocoon'], ({ play, command, w }) => {
    const uid = play('yuri_cocoon', 'next'); command('end'); play('yuri_cocoon', 'next'); command('end');
    w('interaction', 'once-check-independent-of-cap', [fact('after', ['growthCount'], 1), fact('after', ['cards', uid, 'growth'], 1), fact('after', ['cards', uid, 'grown'], true), fact('after', ['block'], 10)]);
  }, { enemies: ['e_seed'], relics: ['symbiotic_moss'] });
  add('edge-copy-pick-real-grown-root', normal('yuri'), ['yuri_dew', 'yuri_dew', 'yuri_metamorph', spec('yuri_cocoon', { plus: true, mods: { e: 'e_guard' } })], ['yuri_dew', 'yuri_dew', 'yuri_metamorph', 'yuri_cocoon'], ({ battle, find, play, command, w }) => {
    const original = find('yuri_cocoon'); const dew1 = play('yuri_dew', 'now', { picks: [original] }); const dew2 = play('yuri_dew', 'now', { picks: [original] }); play('yuri_metamorph', 'now', { picks: [original] });
    const copy = battle.s.zones.hand.find(u => battle.s.cards[u].echoCopy);
    w('interaction', 'copy-preserves-earned-growth2', [fact('after', ['cards', copy, 'growth'], 2), fact('after', ['cards', copy, 'plus'], true), fact('after', ['cards', copy, 'mods'], { c: null, e: 'e_guard' })]);
    command('play', { uid: copy, mode: 'next', target: null }); command('end');
    w('interaction', 'copy-can-grow-then-exhaust', [fact('after', ['cards', copy, 'growth'], 3), fact('after', ['cards', original, 'growth'], 2), fact('after', ['growthCount'], 3), fact('after', ['block'], 24), fact('after', ['zones', 'exhaust'], [dew1, dew2, copy])]);
  }, { enemies: ['e_seed'], relics: ['symbiotic_moss'] });
  add('edge-echo-resets-earned-growth-upgrade-mods', normal('yuri'), ['yuri_dew', 'yuri_dew', spec('yuri_cocoon', { plus: true, mods: { c: 'c_echo', e: 'e_guard' } })], ['yuri_dew', 'yuri_dew', 'yuri_cocoon'], ({ battle, find, play, command, w }) => {
    const original = find('yuri_cocoon'); const dew1 = play('yuri_dew', 'now', { picks: [original] }); const dew2 = play('yuri_dew', 'now', { picks: [original] }); play('yuri_cocoon', 'next'); command('end'); command('end');
    const copy = battle.s.zones.hand.find(u => battle.s.cards[u].echoCopy);
    w('interaction', 'echo-defaults-differ-from-grown-original', [fact('after', ['cards', original, 'growth'], 3), fact('after', ['cards', original, 'plus'], true), fact('after', ['cards', copy, 'growth'], 0), fact('after', ['cards', copy, 'grown'], false), fact('after', ['cards', copy, 'used'], 0), fact('after', ['cards', copy, 'plus'], false), fact('after', ['cards', copy, 'mods'], { c: null, e: null })]);
    command('end'); command('play', { uid: copy, mode: 'now', target: null });
    w('interaction', 'echo-free-survives-another-turn', [fact('response', ['ev', 0, 'cost'], 0), fact('after', ['block'], 3), fact('after', ['zones', 'exhaust'], [dew1, dew2, copy])]);
  }, { enemies: ['e_seed'], relics: ['symbiotic_moss'] });
  add('edge-overheat-block-excludes-exhaust-add', [{ charId: 'nia' }, { charId: 'sera' }, { charId: 'kaya' }], ['nia_overclock', 'nia_tune', 'sera_cover'], ['nia_overclock', 'nia_tune', 'sera_cover'], ({ find, play, w, event }) => {
    const uid = find('sera_cover'); play('nia_overclock', 'now', { picks: [uid] }); play('nia_tune', 'now', { picks: [uid] }); play('sera_cover');
    event('interaction', 'block-amp30-handi15-only', { e: 'block', n: 7, src: 'card' }); event('interaction', 'damage-overheat50-amp30-handi15', { e: 'dmg', n: 7, src: 'card' });
    w('interaction', 'temp-order-and-exhaust', [fact('after', ['cards', uid, 'temp'], ['overheat', 'amp']), fact('after', ['zones', 'exhaust'], [uid])]);
  });
  for (const validPick of [false, true]) add(`edge-pull-multiple-${validPick ? 'explicit' : 'invalid'}-pick`, normal('sera'), ['sera_round', 'sera_cover', 'sera_pull'], ['sera_round', 'sera_cover', 'sera_pull'], ({ battle, play, w, event }) => {
    const after = play('sera_round', 'after'), next = play('sera_cover', 'next'); play('sera_pull', 'now', { picks: [validPick ? next : 'missing'] });
    if (validPick) { event('interaction', 'explicit-next-pull-resolves-immediately', { e: 'dmg', card: 'sera_cover', n: 6, src: 'card' }); w('interaction', 'unpicked-after-remains', [fact('after', ['res', 0, 'uid'], after), fact('after', ['res', 0, 'due'], 3)]); }
    else w('interaction', 'invalid-pick-falls-back-to-after', [fact('after', ['res', 0, 'uid'], after), fact('after', ['res', 0, 'kind'], 'next'), fact('after', ['res', 0, 'due'], 2), fact('after', ['res', 1, 'uid'], next)]);
  });
  for (const res of [0, 1]) add(`edge-nia-tune-used-up-r${res}`, [{ charId: 'nia', res }, { charId: 'sera' }, { charId: 'kaya' }], ['nia_tune', ...Array(res ? 2 : 1).fill('com_recall')], ['nia_tune', ...Array(res ? 2 : 1).fill('com_recall')], ({ find, play, w }) => {
    const uid = find('nia_tune'); play('nia_tune'); for (let n = 0; n < (res ? 2 : 1); n++) { play('com_recall', 'now', { picks: [uid] }); play('nia_tune'); }
    w('interaction', `used-up-exact-${res ? 3 : 2}`, [fact('after', ['cards', uid, 'used'], res ? 3 : 2), fact('after', ['zones', 'exhaust'], [uid])]);
  });
  add('edge-grow-lowest-ordinal-uid', [{ charId: 'yuri', gauge: 100 }, { charId: 'sera' }, { charId: 'kaya' }], Array(11).fill('yuri_cocoon'), [], ({ command, w }) => {
    command('ult', { actor: 'yuri' }); w('interaction', 'c10-sorts-before-c2', [fact('after', ['cards', 'c1', 'growth'], 1), fact('after', ['cards', 'c10', 'growth'], 1), fact('after', ['cards', 'c2', 'growth'], 0), fact('after', ['growthCount'], 0)]);
  }, { enemies: ['e_seed'] }, { inputDomain: 'Supported gauge100; eleven ordinary root cards, no growth injection' });
  add('edge-selection-definition-cost-before-payable', [{ charId: 'sion' }, { charId: 'nia' }, { charId: 'sera' }], ['sion_erase', 'nia_overclock', 'nia_overclock', 'sera_timed', 'com_guard'], ['sion_erase', 'nia_overclock', 'nia_overclock', 'sera_timed', 'com_guard'], ({ find, play, w }) => {
    const timed = find('sera_timed'); play('nia_overclock', 'now', { picks: [timed] }); play('nia_overclock', 'now', { picks: [timed] }); play('sion_erase', 'now', { picks: ['missing'] });
    w('interaction', 'lowest-definition-cost-not-payable', [fact('after', ['cards', timed, 'costMod'], -2), fact('after', ['tempParts'], 1), fact('after', ['zones', 'exhaust'], ['c5'])]);
  });
  add('edge-selection-curse-before-cost', normal('sion'), ['sion_erase', 'smudge', 'com_focus'], ['sion_erase', 'smudge', 'com_focus'], ({ play, w }) => {
    play('sion_erase'); w('interaction', 'curse-priority-over-zero-cost', [fact('after', ['zones', 'exhaust'], ['c2']), fact('after', ['cards', 'c3', 'used'], 0)]);
  });
  add('edge-temporary-extra-slot-resolves-after-reset', [{ charId: 'sera' }, { charId: 'nia' }, { charId: 'kaya' }], ['sera_calc', 'sera_round', 'sera_cover', 'nia_wrench'], ['sera_calc', 'sera_round', 'sera_cover', 'nia_wrench'], ({ play, command, w }) => {
    play('sera_calc'); play('sera_round', 'next'); play('sera_cover', 'next'); play('nia_wrench', 'next');
    w('interaction', 'three-real-next-reservations', [fact('after', ['res', 'length'], 3), fact('after', ['ap'], 0)]); command('end');
    w('interaction', 'slot-temp-reset-does-not-drop-dues', [fact('after', ['res'], []), fact('after', ['slotsTemp', 'next'], 0), fact('after', ['cards', 'c2', 'used'], 1), fact('after', ['cards', 'c3', 'used'], 1), fact('after', ['cards', 'c4', 'used'], 1)]);
  });
  add('edge-after-reservations-use-next-bucket', [{ charId: 'sera' }, { charId: 'nia' }, { charId: 'kaya' }], ['sera_round', 'sera_cover', 'nia_wrench', 'sera_calc'], ['sera_round', 'sera_cover', 'nia_wrench', 'sera_calc'], ({ find, play, command, w }) => {
    play('sera_round', 'after'); play('sera_cover', 'after'); command('end'); command('play', { uid: find('nia_wrench'), mode: 'next' }, 'SLOT_FULL');
    play('sera_calc'); play('nia_wrench', 'next'); w('interaction', 'after-old-due-and-new-next-share-capacity', [fact('after', ['res', 'length'], 3), fact('after', ['res', 0, 'due'], 3), fact('after', ['res', 2, 'due'], 3)]);
    command('end'); w('interaction', 'all-three-same-due-resolve', [fact('after', ['res'], []), fact('after', ['cards', 'c1', 'used'], 1), fact('after', ['cards', 'c2', 'used'], 1), fact('after', ['cards', 'c3', 'used'], 1)]);
  }, { relics: ['cracked_hourglass'] });
  add('edge-collateral-sealed-slot-expiry', [{ charId: 'ravien' }, { charId: 'sera' }, { charId: 'nia' }], ['ravien_collateral', 'sera_round', 'sera_cover'], ['ravien_collateral', 'sera_round', 'sera_cover'], ({ find, play, command, w }) => {
    play('ravien_collateral'); play('sera_round', 'next'); command('end'); play('sera_round', 'next');
    command('play', { uid: find('sera_cover'), mode: 'next' }, 'SLOT_FULL'); command('end');
    play('sera_round', 'next'); play('sera_cover', 'next');
    w('interaction', 'sealed-until2-expires-on-turn3', [fact('after', ['turn'], 3), fact('after', ['sealedUntil'], 2), fact('after', ['res', 'length'], 2)]);
  });
  for (const enemy of data.enemies) add(`encounter-${enemy.id}-player-cycle`, normal('sera'), ['com_guard'], ['com_guard'], ({ battle, play, command }) => {
    play('com_guard'); for (let n = 0; n < enemy.pattern.length * 2 && !battle.isOver(); n++) command('end');
  }, { enemies: [enemy.id] }, { group: 'encounter', enemyId: enemy.id, maxPatternCycles: 2, inputDomain: 'Normal originalHP243; stops honestly at a real terminal' });
  return out;
}

function protocolBuilders() {
  const out = [], party = standardParty({ charId: 'sera' });
  const add = (id, deck, required, script, extra = {}, first = party) => out.push([id, () => scenario(id, first, deck, required, script, extra, { group: 'protocol' })]);
  for (const kind of ['play', 'ult']) out.push([`protocol-created-${kind}-picks-priority`, () => record(`protocol-created-${kind}-picks-priority`, setup(party, [spec('sera_round')]), { group: 'protocol', autoBegin: false }, ({ command, battle }) => {
    const rid = `${kind}-failed-reusable`; command(kind, kind === 'play' ? { uid: 'c1', picks: true } : { actor: 'sera', picks: true }, 'WRONG_PHASE', { rid, v: 0, kind, args: kind === 'play' ? { uid: 'c1', picks: true } : { actor: 'sera', picks: true } });
    command('begin', {}, null, { rid, v: battle.s.v, kind: 'begin', args: {} });
  })]);
  add('protocol-invalid-mode-picks-priority', ['sera_round'], ['sera_round'], ({ command, battle }) => {
    command('play', { uid: 'c1', mode: 'bad', picks: true }, 'INVALID_MODE', { rid: 'retry-mode', v: battle.s.v, kind: 'play', args: { uid: 'c1', mode: 'bad', picks: true } });
    command('play', {}, null, { rid: 'retry-mode', v: battle.s.v, kind: 'play', args: { uid: 'c1', mode: 'now' } });
  });
  for (const [label, deck, card, mode, error] of [
    ['not-in-hand', ['sera_round'], 'missing', 'now', 'NOT_IN_HAND'],
    ['reserve-only', ['sera_timed'], 'sera_timed', 'now', 'RESERVE_ONLY'],
    ['now-only', ['sera_pull'], 'sera_pull', 'next', 'NOT_RESERVABLE'],
    ['after-only', ['sera_last'], 'sera_last', 'next', 'NOT_RESERVABLE'],
  ]) add(`protocol-${label}`, deck, deck, ({ find, command }) => command('play', { uid: find(card) || 'missing', mode }, error));
  add('protocol-ap-low', ['sera_last', 'sera_timed'], ['sera_last', 'sera_timed'], ({ play, find, command }) => {
    play('sera_last', 'after'); command('play', { uid: find('sera_timed'), mode: 'next' }, 'AP_LOW');
  });
  add('protocol-bad-target', ['sera_round'], ['sera_round'], ({ play, find, command }) => {
    command('play', { uid: find('sera_round'), mode: 'now', target: 'missing' }, 'BAD_TARGET'); play('sera_round');
  });
  add('protocol-fixed-mark-required', ['kaya_deduce'], ['kaya_deduce'], ({ find, command }) => command('play', { uid: find('kaya_deduce'), mode: 'now' }, 'FIXED_MARK_REQUIRED'));
  add('protocol-debt-cap', ['ravien_advance', 'ravien_bigloan'], ['ravien_advance', 'ravien_bigloan'], ({ play, find, command }) => {
    play('ravien_advance'); command('play', { uid: find('ravien_bigloan'), mode: 'now' }, 'DEBT_CAP');
  }, {}, standardParty({ charId: 'ravien' }));
  add('protocol-loan-overflow', ['lumi_overdrive', 'ravien_advance', 'ravien_bigloan'], ['lumi_overdrive', 'ravien_advance', 'ravien_bigloan'], ({ play, find, command }) => {
    play('lumi_overdrive'); play('ravien_advance'); command('play', { uid: find('ravien_bigloan'), mode: 'now' }, 'LOAN_OVERFLOW');
  }, {}, [{ charId: 'ravien', res: 4 }, { charId: 'lumi' }, { charId: 'sera' }]);
  add('protocol-not-deferrable', ['ravien_defer'], ['ravien_defer'], ({ find, command }) => command('play', { uid: find('ravien_defer'), mode: 'now' }, 'NOT_DEFERRABLE'));
  add('protocol-restraint-unavailable', ['rien_note', 'rien_note', 'rien_note', 'ravien_restraint'], ['rien_note', 'rien_note', 'rien_note', 'ravien_restraint'], ({ play, find, command }) => {
    for (let n = 0; n < 3; n++) play('rien_note'); command('play', { uid: find('ravien_restraint'), mode: 'now' }, 'RESTRAINT_UNAVAILABLE');
  }, {}, [{ charId: 'rien' }, { charId: 'ravien' }, { charId: 'sera' }]);
  add('protocol-no-empty-slot', ['ravien_collateral', 'ravien_collateral'], ['ravien_collateral', 'ravien_collateral'], ({ play, find, command }) => {
    play('ravien_collateral'); command('play', { uid: find('ravien_collateral'), mode: 'now' }, 'NO_EMPTY_SLOT');
  });
  add('protocol-reservation-required', ['sera_pull'], ['sera_pull'], ({ find, command }) => command('play', { uid: find('sera_pull'), mode: 'now' }, 'RESERVATION_REQUIRED'));
  add('protocol-pull-destination-full', ['sera_round', 'sera_cover', 'nia_wrench', 'sera_pull'], ['sera_round', 'sera_cover', 'nia_wrench', 'sera_pull'], ({ play, find, command }) => {
    play('sera_round', 'after'); play('sera_cover', 'next'); play('nia_wrench', 'next'); command('play', { uid: find('sera_pull'), mode: 'now' }, 'PULL_DESTINATION_FULL');
  });
  add('protocol-slot-full', ['sera_round', 'sera_cover', 'nia_wrench'], ['sera_round', 'sera_cover', 'nia_wrench'], ({ play, find, command }) => {
    play('sera_round', 'next'); play('sera_cover', 'next'); command('play', { uid: find('nia_wrench'), mode: 'next' }, 'SLOT_FULL');
  });
  add('protocol-overload-would-kill-earned-hp1', [...Array(8).fill('lumi_overdrive'), 'lumi_dash'], ['lumi_overdrive'], ({ battle, find, play, command }) => {
    for (let n = 0; battle.s.hp > 1; n++) {
      assert.ok(n < 80, 'overload real sequence bound'); if (find('lumi_overdrive')) play('lumi_overdrive'); else command('end');
    }
    for (let n = 0; !find('lumi_dash'); n++) { assert.ok(n < 5); command('end'); }
    command('play', { uid: find('lumi_dash'), mode: 'now' }, 'OVERLOAD_WOULD_KILL');
  }, { enemies: ['e_seed'] }, [{ charId: 'lumi' }]);
  add('protocol-ult-validation', ['sera_round'], ['sera_round'], ({ command }) => {
    command('ult', { actor: 'missing', picks: true }, 'UNKNOWN_ACTOR'); command('ult', { actor: 'sera', picks: true }, 'ULT_NOT_READY');
  });
  add('protocol-ult-bad-target', ['sera_round'], ['sera_round'], ({ command }) => command('ult', { actor: 'sera', target: 'missing' }, 'BAD_TARGET'), {}, [{ charId: 'sera', gauge: 100 }]);
  add('protocol-quick-circuit-reserve-only-has-no-mode', [spec('sera_timed', { mods: { c: 'c_quick' } })], ['sera_timed'], ({ find, command }) => {
    const uid = find('sera_timed'); command('play', { uid, mode: 'now' }, 'RESERVE_ONLY'); command('play', { uid, mode: 'next' }, 'NOT_RESERVABLE'); command('play', { uid, mode: 'after' }, 'NOT_RESERVABLE');
  });
  add('protocol-receipts-play-replay-order', ['sera_round'], ['sera_round'], ({ battle, command, row }) => {
    const request = { rid: '__proto__', v: battle.s.v, kind: 'play', args: { uid: 'c1', mode: 'now', target: battle.alive()[0].uid, picks: [] } };
    command('play', {}, null, request); command('end'); command('play', {}, null, request);
    const reordered = { ...request, args: { mode: 'now', uid: 'c1', target: request.args.target, picks: [] } };
    command('play', {}, 'IDEMPOTENCY_CONFLICT', reordered);
    command('play', {}, 'IDEMPOTENCY_CONFLICT', { ...request, args: { ...request.args, extra: 0 } });
    command('play', {}, 'STALE_VERSION', { ...request, rid: 'fresh-stale' });
    command('missing', {}, 'UNKNOWN_COMMAND'); command('begin', {}, 'WRONG_PHASE');
    assert.equal(row.steps.at(-1).receipts.__proto__.resp.v, 2);
  });
  return out;
}

function matrixWitnesses(cases) {
  const byId = new Map(cases.map(c => [c.id, c]));
  const proof = (id, kind, tag, facts, at = null) => {
    const row = byId.get(id); assert.ok(row, `Missing required witness case ${id}`);
    const index = at ?? row.steps.findIndex(s => row.mainOutcome ? s.response?.ev.some(e => e.e === 'resolved' && e.uid === row.mainOutcome.uid) : s.request.kind === 'ult');
    witness(row, kind, tag, index, facts);
  };
  proof('card-sera_round-now', 'op', 'dmg', [fact('response', ['ev', 1, 'n'], 8), fact('after', ['enemies', 0, 'hp'], 222)]);
  proof('card-harin_shield-now', 'op', 'blk', [fact('after', ['block'], 8), fact('after', ['counter'], 1)]);
  proof('card-kaya_clue-now', 'op', 'st', [fact('after', ['enemies', 0, 'st', 'mark'], 2)]);
  for (const tag of ['draw', 'discard']) proof('card-sera_reload-now', 'op', tag, [fact('before', ['zones', 'draw', 'length'], 3), fact('after', ['zones', 'draw', 'length'], 1), fact('after', ['zones', 'hand', 'length'], 5), fact('after', ['zones', 'discard', 'length'], 2)]);
  proof('card-lumi_overdrive-now', 'op', 'ap', [fact('before', ['ap'], 4), fact('after', ['ap'], 6)]);
  proof('card-com_rally-now', 'op', 'gauge', [fact('after', ['actors', 0, 'gauge'], 15), fact('after', ['actors', 1, 'gauge'], 15), fact('after', ['actors', 2, 'gauge'], 15)]);
  proof('card-nia_drone-now', 'op', 'create', [fact('before', ['seq'], 9), fact('after', ['seq'], 11), fact('after', ['cards', 'c10', 'token'], true), fact('after', ['cards', 'c11', 'id'], 'nia_drone_shot'), fact('after', ['zones', 'hand', 'length'], 6)]);
  proof('card-sera_calc-now', 'op', 'slot', [fact('before', ['slotsTemp', 'next'], 0), fact('after', ['slotsTemp', 'next'], 1)]);
  proof('part-overheat', 'op', 'costdown', [fact('after', ['cards', 'c1', 'costMod'], -1), fact('after', ['cards', 'c1', 'temp'], ['overheat'])], 1);
  proof('ult-sion-r0', 'op', 'exhaustHand', [fact('before', ['zones', 'hand', 'length'], 5), fact('after', ['zones', 'hand', 'length'], 0), fact('after', ['exhausted'], 5), fact('response', ['ev', 6, 'n'], 8), fact('response', ['ev', 'length'], 11)]);
  proof('ult-ravien-r0', 'op', 'clearDebt', [fact('before', ['debts', 0, 'n'], 2), fact('after', ['debts'], []), fact('response', ['ev', 1, 'n'], 18), fact('response', ['ev', 2, 'n'], 6)]);
  proof('ult-ravien-r6', 'op', 'apNext', [fact('before', ['apNext'], 0), fact('after', ['apNext'], 1)]);
  proof('card-sion_forget-now', 'counter', 'lastCount', [fact('before', ['exhausted'], 0), fact('after', ['exhausted'], 2), fact('after', ['block'], 15)]);
  proof('card-yuri_tend-now', 'condition', 'growthInHand:true', [fact('before', ['zones', 'draw', 'length'], 3), fact('after', ['zones', 'draw', 'length'], 2)]);
  proof('card-sera_double-now', 'condition', 'marked:false', [fact('after', ['enemies', 0, 'st', 'mark'], 0), fact('response', ['ev', 'length'], 4)]);
  proof('card-harin_taunt-now', 'condition', 'intentAttack:false', [fact('before', ['enemies', 0, 'intent', 't'], 'blockAll'), fact('after', ['enemies', 0, 'st', 'weak'], 0)]);
  proof('card-ravien_penalty-now', 'condition', 'burning:false', [fact('after', ['enemies', 0, 'st', 'burn'], 0), fact('after', ['enemies', 0, 'brk'], 12)]);
  proof('card-ravien_strike-now', 'condition', 'debt:false', [fact('after', ['debts'], []), fact('after', ['enemies', 0, 'st', 'burn'], 0)]);
  proof('card-nia_shock-now', 'condition', 'modded:false', [fact('response', ['ev', 1, 'n'], 14), fact('after', ['enemies', 0, 'brk'], 10)]);
  proof('card-yuri_cocoon_prey-now', 'condition', 'killedThis:false', [fact('after', ['enemies', 0, 'hp'], 221), fact('after', ['zones', 'draw', 'length'], 3)]);
  proof('card-rien_rest-now', 'condition', 'echo:false', [fact('after', ['echo'], 0), fact('after', ['zones', 'draw', 'length'], 3)]);
  proof('card-kaya_verdict-now', 'condition', 'targetHpBelow:false', [fact('response', ['ev', 1, 'n'], 20)]);
  const conditional = byId.get('edge-marked-conditional-true');
  const old = conditional.witnesses.find(w => w.kind === 'condition');
  witness(conditional, 'op', 'if', old.stepIndex, old.facts);
}

function inventory(cases) {
  const ops = new Set(), counters = new Set(), conditions = new Set(), fields = new Set(), authoredEffects = [];
  const visit = (value, path, cardId = null) => {
    if (!value || typeof value !== 'object') return;
    if (typeof value.op === 'string') {
      ops.add(value.op); Object.keys(value).forEach(k => fields.add(k));
      authoredEffects.push({ source: `data/game_data.json#/${path.join('/')}`, op: value.op, shapeHash: stateHash(value),
        ...(cardId ? { cardId } : {}), caseIds: cases.filter(c => cardId ? c.group === 'card' && c.main.cardId === cardId : c.group === 'ult' && c.characterId === path[1] && c.resonance === 6).map(c => c.id) });
    }
    if (typeof value.per === 'string') counters.add(value.per);
    if (typeof value.c === 'string') conditions.add(value.c);
    for (const [key, child] of Object.entries(value)) visit(child, [...path, key], cardId);
  };
  data.cards.forEach((card, i) => { visit(card.eff, ['cards', i, 'eff'], card.id); if (card.plus) visit(card.plus, ['cards', i, 'plus'], card.id); if (card.costIf) visit(card.costIf, ['cards', i, 'costIf'], card.id); });
  data.characters.forEach((c, i) => { const start = authoredEffects.length; visit(c.ult, ['characters', i, 'ult']); visit(c.resonance, ['characters', i, 'resonance']); for (const effect of authoredEffects.slice(start)) effect.caseIds = [`ult-${c.id}-r6`]; });
  const unique = (kind, tag) => [...new Set(cases.flatMap(c => (c.witnesses || []).filter(w => w.kind === kind).map(w => w[tag])))].sort();
  const sortedOps = [...ops].sort(), sortedCounters = [...counters].sort(), sortedConditions = [...conditions].sort();
  assert.deepEqual(unique('op', 'tag'), sortedOps, 'Every authored op needs positive literal evidence');
  assert.deepEqual(unique('counter', 'tag'), sortedCounters, 'Every authored scaling counter needs nonzero literal evidence');
  assert.deepEqual(unique('condition', 'tag'), sortedConditions.flatMap(c => [`${c}:false`, `${c}:true`]).sort(), 'Every authored condition needs both reachable branches');
  assert.equal(sortedOps.length, 39); assert.equal(sortedCounters.length, 12); assert.equal(sortedConditions.length, 10);
  assert.equal(fields.size, 35, 'Authored effect field inventory changed');
  const set = group => cases.filter(c => c.group === group);
  const coverage = {
    cards: data.cards.map(c => c.id), baseModePairs: set('card').map(c => `${c.main.cardId}:${c.main.mode}`), plusCards: set('plus').map(c => c.main.cardId),
    characterResonances: set('resonance').map(c => `${c.characterId}:r${c.resonance}`), ultimateResonances: set('ult').map(c => `${c.characterId}:r${c.resonance}`),
    weaponRefinements: set('weapon').map(c => `${c.weaponId}:r${c.refine}`), parts: set('part').map(c => c.partId), relics: set('relic').map(c => c.relicId), enemies: set('encounter').map(c => c.enemyId),
    ops: sortedOps, counters: sortedCounters, conditions: sortedConditions, conditionBranches: sortedConditions.flatMap(c => [`${c}:false`, `${c}:true`]), effectFields: [...fields].sort(),
    edgeIds: set('edge').map(c => c.id), protocolIds: set('protocol').map(c => c.id), encounterIds: set('encounter').map(c => c.id),
    witnesses: cases.flatMap(c => (c.witnesses || []).map((w, witnessIndex) => ({ caseId: c.id, witnessIndex, kind: w.kind, tag: w.tag, stepIndex: w.stepIndex }))),
  };
  assert.deepEqual(coverage.baseModePairs, data.cards.flatMap(c => baseModes(c).map(m => `${c.id}:${m}`)));
  assert.deepEqual(coverage.plusCards, coverage.cards);
  assert.deepEqual(coverage.characterResonances, data.characters.flatMap(c => Array.from({ length: 7 }, (_, r) => `${c.id}:r${r}`)));
  assert.deepEqual(coverage.ultimateResonances, coverage.characterResonances);
  assert.deepEqual(coverage.weaponRefinements, data.weapons.flatMap(w => Array.from({ length: 5 }, (_, r) => `${w.id}:r${r + 1}`)));
  for (const [key, defs] of [['parts', data.parts], ['relics', data.relics], ['enemies', data.enemies]]) assert.deepEqual(coverage[key], defs.map(d => d.id));
  assert.deepEqual([...coverage.edgeIds, ...coverage.encounterIds], edgeBuilders().map(([id]) => id), 'Missing required edge/encounter');
  assert.deepEqual(coverage.protocolIds, protocolBuilders().map(([id]) => id), 'Missing required protocol');
  return { coverage, authoredEffects };
}

export function generateCases() {
  assert.deepEqual(sources(), loadedSources, 'Action sources changed after load; rerun');
  const cases = [], failures = [];
  for (const card of data.cards) for (const mode of baseModes(card)) {
    try { cases.push(cardCase(card, mode)); }
    catch (error) { failures.push({ id: `card-${card.id}-${mode}`, code: error.code || error.name, message: error.message }); }
  }
  for (const card of data.cards) {
    try { cases.push(cardCase(card, baseModes(card).at(-1), { plus: true, group: 'plus' })); }
    catch (error) { failures.push({ id: `plus-${card.id}`, code: error.code || error.name, message: error.message }); }
  }
  const resCard = { sera: ['sera_timed', 'after'], nia: ['nia_tune', 'now'], yuri: ['yuri_cocoon', 'next'], ravien: ['ravien_advance', 'now'],
    harin: ['harin_counter', 'now'], rien: ['rien_note', 'now'], aila: ['aila_bell', 'now'], sion: ['sion_erase', 'now'], kaya: ['kaya_clue', 'now'], lumi: ['lumi_dash', 'now'] };
  for (const character of data.characters) for (let res = 0; res <= 6; res++) {
    const [id, mode] = resCard[character.id];
    try { cases.push(cardCase(ix.cards[id], mode, { actor: { res }, group: 'resonance', id: `resonance-${character.id}-r${res}`, metadata: { characterId: character.id } })); }
    catch (error) { failures.push({ id: `resonance-${character.id}-r${res}`, code: error.code || error.name, message: error.message }); }
    try { cases.push(ultCase(character, res)); }
    catch (error) { failures.push({ id: `ult-${character.id}-r${res}`, code: error.code || error.name, message: error.message }); }
  }
  for (const weapon of data.weapons) for (let r = 1; r <= 5; r++) {
    try { cases.push(weaponCase(weapon, r)); }
    catch (error) { failures.push({ id: `weapon-${weapon.id}-r${r}`, code: error.code || error.name, message: error.message }); }
  }
  for (const part of data.parts) {
    try {
      if (part.slot === 'temp') cases.push(temporaryPartCase(part.id));
      else {
        const mode = part.id === 'c_echo' || part.id === 'c_delay' ? 'next' : part.id === 'c_far' ? 'after' : 'now';
        cases.push(cardCase(ix.cards.sera_round, mode, { group: 'part', id: `part-${part.id}`, metadata: { partId: part.id },
          cardExtra: { mods: { [part.slot === 'circuit' ? 'c' : 'e']: part.id } } }));
      }
    } catch (error) { failures.push({ id: `part-${part.id}`, code: error.code || error.name, message: error.message }); }
  }
  for (const relic of data.relics) {
    try { cases.push(cardCase(ix.cards.sera_round, 'next', { group: 'relic', id: `relic-${relic.id}`, metadata: { relicId: relic.id }, setup: { relics: [relic.id] } })); }
    catch (error) { failures.push({ id: `relic-${relic.id}`, code: error.code || error.name, message: error.message }); }
  }
  for (const [id, build] of [...edgeBuilders(), ...protocolBuilders()]) {
    try { cases.push(build()); }
    catch (error) { failures.push({ id, code: error.code || error.name, message: error.message }); }
  }
  if (failures.length) throw new Error(JSON.stringify({ complete: false, expectedBasePairs: data.cards.reduce((n, c) => n + baseModes(c).length, 0),
    successfulBasePairs: cases.filter(c => c.group === 'card').length, successfulCases: cases.length, failures }));
  matrixWitnesses(cases);
  assert.equal(new Set(cases.map(c => c.id)).size, cases.length, 'Duplicate player-action case id');
  const { coverage, authoredEffects } = inventory(cases);
  const steps = cases.flatMap(c => c.steps), counts = { cases: cases.length, commands: steps.length,
    committed: steps.filter(s => s.outcome === 'committed').length, replayed: steps.filter(s => s.outcome === 'replayed').length, rejected: steps.filter(s => s.outcome === 'rejected').length };
  assert.equal(JSON.stringify(data), originalData, 'Original definitions changed');
  assert.deepEqual(sources(), loadedSources, 'Action sources changed during generation; rerun');
  return { schema, status, scope, dataVersion: data.meta.version, sources: clone(loadedSources),
    unresolved: { base0: { cardId: 'sion_forget', status: 'ORIGINAL_SOURCE_CONFLICT',
      retained: 'Current JS/live-card base5 +5*lastCount (added cap10); generic base0 conformance UNRESOLVED',
      anchors: ['docs/source_v0.8.1/06_chapter.md:42', 'docs/source_v0.8.1/09_chapter.md:833', 'content/characters.mjs:310', 'engine/rules-core.js:1064-1095'] } },
    reachability: { positiveSilenceUntil: 'UNREACHABLE: none of the20 live enemy definitions produces silence',
      battleOnly: true, runAcceptance: 'PENDING', setupGauge100: 'Explicit supported inputs labeled per case; earned recharge and ult use are separate cases',
      runOnlyRelics: ['virtue_of_refusal', 'forge_ember', 'old_compass', 'lucky_coin'],
      notAuthored: ['costIf', 'explicit plus.eff', 'broken/reserved/exhaustedAtLeast conditions', 'proposal-only enemy moves and phase definitions'],
      evolvedSetup: 'Persistent-deck id/from inputs do not assert Run.evolve eligibility',
      notes: 'No state, hand, mark, HP or definitions are injected after Create; all preparation uses concrete commands' },
    coverage, authoredEffects, counts, cases };
}

export function checkFixture(fixture) {
  assert.equal(fixture.schema, schema, 'Wrong player-action fixture schema'); assert.equal(fixture.status, status, 'Player-action closure is incomplete');
  assert.ok(Array.isArray(fixture.cases), 'Player-action fixture requires cases');
  assert.equal(new Set(fixture.cases.map(c => c.id)).size, fixture.cases.length, 'Duplicate player-action case id');
  assert.deepEqual(fixture.sources, loadedSources, 'Player-action source provenance is stale');
  const expected = generateCases();
  assert.deepEqual(fixture.coverage, expected.coverage, 'Player-action exact coverage or witnesses differ');
  assert.deepEqual(fixture.counts, expected.counts, 'Player-action command counts differ');
  assert.ok(isDeepStrictEqual(fixture, expected), 'Player-action fixture requests, state, diagnostics, hashes or source inventory differ');
  return fixture.counts;
}

export function exportVectors(output = fixturePath) {
  const fixture = generateCases(), json = Buffer.from(`${JSON.stringify(fixture)}\n`);
  const bytes = output.endsWith('.gz') ? gzipSync(json, { level: 9 }) : json;
  const temporary = mkdtempSync(join(dirname(output), '.battle-player-actions-'));
  try {
    const path = join(temporary, 'fixture'); writeFileSync(path, bytes);
    assert.deepEqual(sources(), fixture.sources, 'Player-action sources changed before export; old fixture preserved');
    renameSync(path, output);
  } finally { rmSync(temporary, { recursive: true, force: true }); }
  return { ...fixture.counts, bytes: bytes.length, uncompressedBytes: json.length, sha256: sha256(bytes), sources: fixture.sources };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  if (args.length > 1 || (args.length && !['--check', '--help'].includes(args[0]))) throw new Error('Usage: node tools/gen_battle_player_action_vectors.mjs [--check|--help]');
  if (args[0] === '--help') console.log('Export source-pinned actual current-content Battle player commands, or --check the lossless gzip against strict fresh regeneration. FullRun acceptance remains pending.');
  else if (args[0] === '--check') console.log(JSON.stringify({ checked: fixturePath, ...checkFixture(JSON.parse(gunzipSync(readFileSync(fixturePath)).toString('utf8'))) }));
  else console.log(JSON.stringify({ output: fixturePath, ...exportVectors() }, null, 2));
}
