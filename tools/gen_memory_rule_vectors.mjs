// §7/8/16.2/20.9: actual pure Memory APIs only; Run replay, persistence and account migration are not covered.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { mkdtempSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync, gunzipSync } from 'node:zlib';

const root = fileURLToPath(new URL('..', import.meta.url));
const require = createRequire(import.meta.url);
const M = require('../proposals/memory_v2.cjs');
const MF = require('../engine/rules-core.js');
const data = JSON.parse(readFileSync(join(root, 'data/game_data.json'), 'utf8'));
const ix = MF.indexData(data);
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = value => createHash('sha256').update(value).digest('hex');
const schema = 'mf-memory-rules-v1';
const scope = 'Pure Memory inspect/baseline/listChanges/cost/applyChanges/apply/recover/migrate and current capacityOf only; actual Run command replay, persistence, server and account migration NOT_IMPLEMENTED';
export const fixturePath = join(root, 'engine/tests/vectors/memory_rule_vectors.json.gz');

function sources() {
  return Object.fromEntries(Object.entries({ data: 'data/game_data.json', engine: 'engine/rules-core.js',
    memory: 'proposals/memory_v2.cjs', generator: 'tools/gen_memory_rule_vectors.mjs',
  }).map(([key, path]) => [key, { path, sha256: sha256(readFileSync(join(root, path))) }]));
}
const loadedSources = sources();
const dataBefore = JSON.stringify(data);
const freeze = value => {
  if (value && typeof value === 'object') { Object.values(value).forEach(freeze); Object.freeze(value); }
  return value;
};
freeze(data);

function invoke(method, input) {
  switch (method) {
    case 'inspect': return M.inspect(data, input.snapshot);
    case 'baseline': return M.baseline(data, input.cid);
    case 'listChanges': return M.listChanges(data, input.before, input.after);
    case 'cost': return M.cost(data, input.cid, input.specs, input.baseSpecs);
    case 'applyChanges': return M.applyChanges(input.before, input.changes);
    case 'apply': return M.apply(data, input.before, input.after, input.selectedIds, input.capacities, input.options);
    case 'recover': return M.recover(data, input.before, input.after, input.selectedIds, input.capacities);
    case 'migrate': return M.migrate(data, input.legacy, input.capacities);
    case 'capacityOf': return MF.Run.capacityOf(ix, input.partyEntry);
    default: throw new Error(`Unknown memory oracle method ${method}`);
  }
}

export function generateCases() {
  assert.deepEqual(sources(), loadedSources, 'Memory source provenance changed after module load; rerun exporter');
  const cases = [];
  const add = (id, method, initial, expectedError = null) => {
    const input = clone(initial);
    const live = freeze(clone(input));
    let outcome;
    try {
      outcome = { result: clone(invoke(method, live)) };
    } catch (error) {
      // Only a specifically declared rejection belongs in a vector; unexpected failures abort export.
      if (!expectedError) throw error;
      assert.equal(error.name, ['TypeError', 'URIError'].includes(expectedError) ? expectedError : 'Error', `${id}: error name`);
      assert.equal(error.code, ['TypeError', 'URIError'].includes(expectedError) ? undefined : expectedError, `${id}: error code`);
      outcome = { error: { name: error.name, ...(error.code === undefined ? {} : { code: error.code }), message: error.message } };
    }
    assert.equal(!!outcome.error, expectedError !== null, `${id}: expected ${expectedError || 'success'}`);
    const inputAfter = clone(live);
    assert.equal(JSON.stringify(inputAfter), JSON.stringify(input), `${id}: full input changed`);
    assert.equal(JSON.stringify(data), dataBefore, `${id}: full authored data changed`);
    cases.push({ id, method, input, outcome, inputAfter, dataUnchanged: true });
  };
  const kit = cid => M.baseline(data, cid);
  const one = (card = {}) => ({ sera: [{ key: 'instance', id: 'sera_round', ...card }] });
  const selected = (before, after) => M.listChanges(data, before, after).map(change => change.id);
  const apply = (id, before, after, ids, capacities, options = {}) => add(id, 'apply', { before, after, selectedIds: ids, capacities, options });

  for (const ch of data.characters) {
    const cid = ch.id;
    const before = { [cid]: kit(cid) };
    add(`baseline-${cid}`, 'baseline', { cid });
    add(`inspect-baseline-${cid}`, 'inspect', { snapshot: before });
    add(`cost-baseline-${cid}`, 'cost', { cid, specs: before[cid] });
    add(`cost-baseline-keyed-${cid}`, 'cost', { cid, specs: before[cid], baseSpecs: kit(cid) });
    add(`changes-identical-${cid}`, 'listChanges', { before, after: clone(before) });
    apply(`apply-no-selection-${cid}`, before, before, [], { [cid]: 6 });
    add(`recover-no-selection-${cid}`, 'recover', { before, after: before, selectedIds: [], capacities: { [cid]: 6 } });
    add(`migrate-baseline-${cid}`, 'migrate', { legacy: { [cid]: before[cid].map(card => card.id) }, capacities: { [cid]: 6 } });
    for (const asc of [0, 2, 4]) for (let res = 0; res <= 6; res++) {
      add(`capacity-${cid}-asc${asc}-res${res}`, 'capacityOf', { partyEntry: { charId: cid, asc, res } });
    }
    for (const [label, fields] of [
      ['absent', {}], ['null', { asc: null, res: null }], ['false', { asc: false, res: false }],
      ['negative-one', { asc: -1, res: -1 }], ['negative-two', { asc: 0, res: -2 }], ['negative-three', { asc: 0, res: -3 }],
      ['negative-length', { asc: 0, res: -6 }], ['negative-past-length', { asc: 0, res: -7 }],
      ['fraction-below', { asc: 1.9, res: 3.9 }], ['fraction-above', { asc: 2.1, res: 4.1 }],
      ['fraction-before-four', { asc: 3.9, res: -2 }], ['fraction-after-four', { asc: 4.1, res: -2 }],
      ['numeric-strings', { asc: '4', res: '4' }], ['boolean-true', { asc: true, res: true }],
      ['slice-past-length', { asc: 99, res: 99 }], ['empty-string', { asc: '', res: '' }],
    ]) add(`capacity-${cid}-${label}`, 'capacityOf', { partyEntry: { charId: cid, ...fields } });
  }

  const known = new Set(data.characters.map(ch => ch.id));
  const eligible = data.cards.filter(card => known.has(card.owner) || card.owner === 'common');
  const allCards = Object.fromEntries(data.characters.map(ch => [ch.id, []]));
  for (const card of eligible) {
    const cid = card.owner === 'common' ? 'sera' : card.owner;
    const origin = data.cards.find(source => source.evo?.includes(card.id));
    const spec = { key: `authored:${card.id}`, id: card.id, ...(origin ? { from: origin.id } : {}),
      ...(card.owner === 'common' ? { assigned: cid } : {}) };
    allCards[cid].push(spec);
    const before = { [cid]: kit(cid) };
    const after = { [cid]: [...clone(before[cid]), spec] };
    add(`cost-card-${card.id}`, 'cost', { cid, specs: after[cid], baseSpecs: kit(cid) });
    apply(`apply-card-${card.id}`, before, after, selected(before, after), { [cid]: 20 });
  }
  add('inspect-all-149-authored-eligible-cards', 'inspect', { snapshot: allCards });

  for (const part of data.parts.filter(part => ['circuit', 'engraving'].includes(part.slot))) {
    const slot = part.slot === 'circuit' ? 'c' : 'e';
    const before = { sera: kit('sera') };
    const after = clone(before); after.sera[0].mods = { [slot]: part.id };
    const changes = M.listChanges(data, before, after);
    add(`changes-part-${part.id}`, 'listChanges', { before, after });
    add(`applyChanges-part-${part.id}`, 'applyChanges', { before, changes });
    add(`cost-part-${part.id}`, 'cost', { cid: 'sera', specs: after.sera });
    apply(`apply-part-${part.id}`, before, after, changes.map(change => change.id), { sera: 6 });
  }

  for (const origin of data.cards.filter(card => card.growth && card.evo?.length)) for (const branch of origin.evo) {
    const before = { [origin.owner]: kit(origin.owner) };
    let card = before[origin.owner].find(card => card.id === origin.id);
    if (!card) { card = { key: `growth:${origin.id}`, id: origin.id }; before[origin.owner].push(card); }
    const after = clone(before);
    Object.assign(after[origin.owner].find(row => row.key === card.key), { id: branch, from: origin.id, plus: true, mods: { c: 'c_delay', e: 'e_gauge' } });
    const changes = M.listChanges(data, before, after);
    add(`changes-evolution-${branch}`, 'listChanges', { before, after });
    add(`applyChanges-evolution-${branch}`, 'applyChanges', { before, changes });
    apply(`apply-evolution-only-${branch}`, before, after, [changes.find(change => change.kind === 'evolve').id], { [origin.owner]: 20 });
    apply(`apply-evolution-all-${branch}`, before, after, changes.map(change => change.id), { [origin.owner]: 20 });
    add(`cost-new-evolution-${branch}`, 'cost', { cid: origin.owner, specs: [...kit(origin.owner), { key: 'new-evolution', id: branch, from: origin.id, plus: true, mods: { c: 'c_delay', e: 'e_gauge' } }] });
  }

  const base = { sera: kit('sera'), harin: kit('harin'), yuri: kit('yuri') };
  const rich = clone(base);
  rich.sera.push({ key: 'old-S-1', id: 'sera_phase' }, { key: 'old-S-2', id: 'sera_lock' });
  rich.harin[0].plus = true;
  rich.yuri[0].metadata = { ratio: 0.25, nested: [false, null, { label: '원본🙂' }] };
  const grown = clone(rich); grown.sera.push({ key: 'new-S', id: 'sera_last' });
  const richIds = selected(rich, grown);
  add('cost-existing-six', 'cost', { cid: 'sera', specs: rich.sera });
  add('cost-complete-nine', 'cost', { cid: 'sera', specs: grown.sera });
  add('apply-over-capacity-commit', 'apply', { before: rich, after: grown, selectedIds: richIds, capacities: { sera: 6, harin: 6, yuri: 6 } }, 'OVER_CAPACITY');
  apply('apply-over-capacity-preview', rich, grown, richIds, { sera: 6, harin: 6, yuri: 6 }, { preview: true });
  apply('apply-no-selection-preserves-prior', rich, grown, [], { sera: 6, harin: 6, yuri: 6 });
  add('recover-over-capacity', 'recover', { before: rich, after: grown, selectedIds: richIds, capacities: { sera: 6, harin: 6, yuri: 6 } }, 'OVER_CAPACITY');
  const bundled = clone(base); bundled.sera.push({ key: 'bundled-S', id: 'sera_phase', plus: true, mods: { c: 'c_echo' } });
  add('cost-upgraded-rare-S-six', 'cost', { cid: 'sera', specs: bundled.sera });
  apply('apply-upgraded-rare-S-six', base, bundled, selected(base, bundled), { sera: 6, harin: 6, yuri: 6 });
  apply('apply-upgraded-rare-S-six-preview', base, bundled, selected(base, bundled), { sera: 6, harin: 6, yuri: 6 }, { preview: true });

  const replaced = { sera: kit('sera') };
  replaced.sera[0].key = 'same-kind-new-instance';
  add('cost-same-kind-replaced-keyed-two', 'cost', { cid: 'sera', specs: replaced.sera, baseSpecs: kit('sera') });
  add('cost-same-kind-replaced-default-zero', 'cost', { cid: 'sera', specs: replaced.sera });
  for (const [label, baseSpecs] of [['null', null], ['false', false], ['empty', []]]) {
    add(`cost-baseSpecs-${label}`, 'cost', { cid: 'sera', specs: kit('sera'), baseSpecs });
  }
  add('cost-empty-default-five', 'cost', { cid: 'sera', specs: [] });
  add('cost-empty-explicit-zero', 'cost', { cid: 'sera', specs: [], baseSpecs: [] });
  add('cost-empty-null-baseline-entry-one', 'cost', { cid: 'sera', specs: [], baseSpecs: [null] });
  for (const [label, input, error] of [
    ['unknown-character', { cid: 'missing-character', specs: [] }, 'INVALID_SNAPSHOT'],
    ['null-specs', { cid: 'sera', specs: null }, 'INVALID_SNAPSHOT'],
    ['scalar-specs', { cid: 'sera', specs: 1 }, 'INVALID_SNAPSHOT'],
    ['array-card', { cid: 'sera', specs: [[]] }, 'IDENTITY_REQUIRED'],
    ['duplicate', { cid: 'sera', specs: [{ key: 'same', id: 'sera_round' }, { key: 'same', id: 'sera_cover' }] }, 'IDENTITY_REQUIRED'],
    ['wrong-owner', { cid: 'sera', specs: [{ key: 'wrong', id: 'harin_shield' }] }, 'INVALID_CARD'],
    ['upgrade', { cid: 'sera', specs: [{ key: 'invalid', id: 'sera_round', plus: 1 }] }, 'INVALID_UPGRADE'],
    ['part', { cid: 'sera', specs: [{ key: 'invalid', id: 'sera_round', mods: { c: 'amp' } }] }, 'INVALID_PART'],
    ['baseSpecs-scalar', { cid: 'sera', specs: kit('sera'), baseSpecs: 1 }, 'TypeError'],
  ]) add(`cost-invalid-${label}`, 'cost', input, error);
  add('changes-same-kind-remove-add', 'listChanges', { before: { sera: kit('sera') }, after: replaced });
  add('recover-remove-add-two-changes', 'recover', { before: { sera: kit('sera') }, after: replaced, selectedIds: selected({ sera: kit('sera') }, replaced), capacities: { sera: 6 } }, 'DEFEAT_KEEP_ONE');

  const multi = clone(base);
  multi.sera[0].plus = true; multi.harin[0].mods = { c: 'c_delay', e: 'e_guard' };
  multi.sera.push({ key: 'added-first', id: 'sera_round' }, { key: 'added-second', id: 'sera_cover' });
  const multiIds = selected(base, multi);
  add('changes-normal-multiple', 'listChanges', { before: base, after: multi });
  add('applyChanges-normal-multiple', 'applyChanges', { before: base, changes: M.listChanges(data, base, multi) });
  apply('apply-normal-multiple', base, multi, multiIds, { sera: 6, harin: 6, yuri: 6 });
  apply('apply-selection-reversed', base, multi, [...multiIds].reverse(), { sera: 6, harin: 6, yuri: 6 });
  apply('apply-defeat-zero', base, multi, [], { sera: 6, harin: 6, yuri: 6 }, { defeat: true });
  apply('apply-defeat-one', base, multi, [multiIds[0]], { sera: 6, harin: 6, yuri: 6 }, { defeat: true });
  apply('apply-defeat-multiple-preview', base, multi, multiIds, { sera: 0, harin: 0, yuri: 0 }, { defeat: true, preview: true });
  add('apply-defeat-before-capacity-error', 'apply', { before: base, after: multi, selectedIds: multiIds, capacities: {}, options: { defeat: true } }, 'DEFEAT_KEEP_ONE');
  add('recover-one', 'recover', { before: base, after: multi, selectedIds: [multiIds[0]], capacities: { sera: 6, harin: 6, yuri: 6 } });
  add('recover-many', 'recover', { before: base, after: multi, selectedIds: multiIds, capacities: { sera: 6, harin: 6, yuri: 6 } }, 'DEFEAT_KEEP_ONE');

  const slotted = { sera: kit('sera') }; slotted.sera[0].mods = { c: 'c_echo', e: 'e_gauge' };
  const swapped = clone(slotted); swapped.sera[0].mods = { c: 'c_delay', e: null };
  apply('apply-replace-and-remove-slots', slotted, swapped, selected(slotted, swapped), { sera: 6 });
  add('cost-replaced-slots-one', 'cost', { cid: 'sera', specs: swapped.sera });
  const reverseUpgrade = { sera: kit('sera') }; reverseUpgrade.sera[0].plus = true;
  const falseUpgrade = clone(reverseUpgrade); falseUpgrade.sera[0].plus = false;
  apply('apply-upgrade-false', reverseUpgrade, falseUpgrade, selected(reverseUpgrade, falseUpgrade), { sera: 6 });
  for (const [label, fields] of [
    ['absent', {}], ['null-mods', { mods: null }], ['empty-mods', { mods: {} }], ['partial-mods', { mods: { c: 'c_delay' } }],
    ['false-plus', { plus: false }], ['false-slot', { mods: { c: false } }], ['null-slot', { mods: { c: null } }],
    ['empty-slot', { mods: { c: '' } }], ['zero-slot', { mods: { c: 0 } }],
    ['false-token-weapon-from', { token: false, weapon: false, from: false }],
    ['null-token-weapon-from', { token: null, weapon: null, from: null }],
    ['unknown-metadata', { owner: 'ignored-metadata', metadata: { fractional: 0.125, flags: [false, null], '\ue000': 'BMP', '𐀀': 'non-BMP' } }],
  ]) {
    const before = one(fields); const after = clone(before); after.sera[0].plus = true;
    add(`inspect-shape-${label}`, 'inspect', { snapshot: before });
    add(`changes-shape-${label}`, 'listChanges', { before, after });
    apply(`apply-shape-${label}`, before, after, selected(before, after), { sera: 6 }, { baseline: { sera: clone(before.sera) } });
  }
  add('changes-null-absent-false-no-delta', 'listChanges', { before: one({ mods: null }), after: one({ plus: false, mods: { c: false, e: null } }) });

  for (const [label, snapshot, error] of [
    ['null', null, 'INVALID_SNAPSHOT'], ['scalar', 1, 'INVALID_SNAPSHOT'], ['false', false, 'INVALID_SNAPSHOT'], ['string', 'sera', 'INVALID_SNAPSHOT'], ['array', [], 'INVALID_SNAPSHOT'],
    ['unknown-character', { unknown: [] }, 'INVALID_SNAPSHOT'], ['rows-scalar', { sera: 1 }, 'INVALID_SNAPSHOT'], ['rows-null', { sera: null }, 'INVALID_SNAPSHOT'],
    ['null-card', { sera: [null] }, 'INVALID_CARD'], ['scalar-card', { sera: [1] }, 'INVALID_CARD'], ['string-card', { sera: ['sera_round'] }, 'INVALID_CARD'],
    ['array-card', { sera: [[]] }, 'IDENTITY_REQUIRED'], ['missing-key', { sera: [{ id: 'sera_round' }] }, 'IDENTITY_REQUIRED'],
    ['empty-key', one({ key: '' }), 'IDENTITY_REQUIRED'], ['number-key', one({ key: 1 }), 'IDENTITY_REQUIRED'],
    ['duplicate-key', { sera: [{ id: 'sera_round', key: 'same' }, { id: 'sera_cover', key: 'same' }] }, 'IDENTITY_REQUIRED'],
    ['duplicate-global-key', { sera: [{ id: 'sera_round', key: 'same' }], harin: [{ id: 'harin_shield', key: 'same' }] }, 'IDENTITY_REQUIRED'],
    ['unknown-card', one({ id: 'missing-card' }), 'INVALID_CARD'], ['missing-card-id', { sera: [{ key: 'instance' }] }, 'INVALID_CARD'],
    ['wrong-owner', one({ id: 'harin_shield' }), 'INVALID_CARD'], ['token', one({ token: true }), 'INVALID_CARD'], ['weapon-flag', one({ weapon: true }), 'INVALID_CARD'],
    ['weapon-card', one({ id: 'wc_basic_rifle' }), 'INVALID_CARD'], ['curse-card', one({ id: 'smudge' }), 'INVALID_CARD'],
    ['common-missing-assignment', one({ id: 'com_guard' }), 'INVALID_ASSIGNMENT'], ['common-wrong-assignment', one({ id: 'com_guard', assigned: 'harin' }), 'INVALID_ASSIGNMENT'],
    ['invalid-evolution-origin', one({ from: 'sera_cover' }), 'INVALID_EVOLUTION'],
    ['evolution-missing-origin', { yuri: [{ key: 'instance', id: 'yuri_cocoon_guard' }] }, 'INVALID_EVOLUTION'],
    ['evolution-wrong-branch', { yuri: [{ key: 'instance', id: 'yuri_sprout_thorn', from: 'yuri_cocoon' }] }, 'INVALID_EVOLUTION'],
    ['plus-null', one({ plus: null }), 'INVALID_UPGRADE'], ['plus-number', one({ plus: 1 }), 'INVALID_UPGRADE'], ['plus-string', one({ plus: 'true' }), 'INVALID_UPGRADE'],
    ['mods-false', one({ mods: false }), 'INVALID_PART'], ['mods-scalar', one({ mods: 1 }), 'INVALID_PART'], ['mods-array', one({ mods: [] }), 'INVALID_PART'],
    ['mods-extra-slot', one({ mods: { temp: null } }), 'INVALID_PART'], ['temporary-part', one({ mods: { c: 'amp' } }), 'INVALID_PART'],
    ['wrong-part-slot', one({ mods: { c: 'e_guard' } }), 'INVALID_PART'], ['unknown-part', one({ mods: { e: 'missing-part' } }), 'INVALID_PART'],
    ['precedence-identity-card', { sera: [{ id: 'missing-card' }] }, 'IDENTITY_REQUIRED'],
    ['precedence-card-assignment-upgrade', one({ id: 'harin_shield', assigned: null, plus: 1, mods: false }), 'INVALID_CARD'],
    ['precedence-assignment-evolution', one({ id: 'com_guard', from: 'bad-origin', plus: 1 }), 'INVALID_ASSIGNMENT'],
    ['precedence-evolution-upgrade', one({ from: 'bad-origin', plus: 1 }), 'INVALID_EVOLUTION'],
    ['precedence-upgrade-part', one({ plus: null, mods: false }), 'INVALID_UPGRADE'],
  ]) add(`inspect-invalid-${label}`, 'inspect', { snapshot }, error);
  add('inspect-empty', 'inspect', { snapshot: {} });
  add('inspect-valid-common-assignment', 'inspect', { snapshot: one({ id: 'com_guard', assigned: 'sera' }) });

  const charsChanged = { sera: kit('sera'), harin: kit('harin') };
  add('changes-character-set-mismatch', 'listChanges', { before: { sera: kit('sera') }, after: charsChanged }, 'CHARACTER_SET_MISMATCH');
  add('changes-inspect-before-character-set', 'listChanges', { before: { sera: kit('sera') }, after: { harin: [{ key: 'bad', id: 'missing-card' }] } }, 'INVALID_CARD');
  add('changes-cross-character-reassignment', 'listChanges', { before: { sera: [{ key: 'shared', id: 'com_guard', assigned: 'sera' }], harin: [] }, after: { sera: [], harin: [{ key: 'shared', id: 'com_guard', assigned: 'harin' }] } }, 'REASSIGNMENT_REQUIRES_SEPARATE_FLOW');
  add('changes-assigned-metadata', 'listChanges', { before: one({ assigned: false }), after: one({ assigned: null }) }, 'REASSIGNMENT_REQUIRES_SEPARATE_FLOW');
  add('changes-arbitrary-transform', 'listChanges', { before: one(), after: one({ id: 'sera_phase' }) }, 'UNSUPPORTED_TRANSFORMATION');
  add('changes-reverse-evolution', 'listChanges', { before: { yuri: [{ key: 'evolved', id: 'yuri_cocoon_guard', from: 'yuri_cocoon' }] }, after: { yuri: [{ key: 'evolved', id: 'yuri_cocoon' }] } }, 'UNSUPPORTED_TRANSFORMATION');
  add('changes-switch-evolution-branch', 'listChanges', { before: { yuri: [{ key: 'evolved', id: 'yuri_cocoon_guard', from: 'yuri_cocoon' }] }, after: { yuri: [{ key: 'evolved', id: 'yuri_cocoon_prey', from: 'yuri_cocoon' }] } }, 'UNSUPPORTED_TRANSFORMATION');

  for (const [label, key] of [['reserved', ":/?#[]@!$&'()*+,;= %"], ['non-BMP', '한글🙂𐀀'], ['control', '\u0000\n\t'],
    ['combining', 'e\u0301'], ['precomposed', 'é'], ['prototype', '__proto__'], ['constructor', 'constructor'], ['lone-high', '\ud800'], ['lone-low', '\udfff']]) {
    const before = one({ key }); const after = { sera: [] };
    add(`inspect-key-${label}`, 'inspect', { snapshot: before });
    add(`changes-key-${label}`, 'listChanges', { before, after }, label.startsWith('lone-') ? 'URIError' : null);
    add(`changes-identical-key-${label}`, 'listChanges', { before, after: before });
  }
  const ordered = { yuri: [{ key: 'z', id: 'yuri_dew' }], sera: [
    { key: '\ue000', id: 'sera_round' }, { key: '𐀀', id: 'sera_cover' }, { key: 'a', id: 'sera_aim' },
  ], harin: [{ key: 'x', id: 'harin_shield' }] };
  add('changes-cid-sort-and-instance-order', 'listChanges', { before: ordered, after: { sera: [], yuri: [], harin: [] } });

  const simpleBefore = { sera: kit('sera') };
  const simpleAfter = clone(simpleBefore); simpleAfter.sera[0].plus = true;
  const singleId = selected(simpleBefore, simpleAfter)[0];
  for (const [label, selectedIds] of [['null', null], ['scalar', 1], ['false', false], ['object', {}], ['string', singleId], ['number-element', [1]], ['null-element', [null]], ['duplicate', [singleId, singleId]], ['unknown', ['missing-delta']]]) {
    add(`apply-invalid-selection-${label}`, 'apply', { before: simpleBefore, after: simpleAfter, selectedIds, capacities: { sera: 6 } }, 'INVALID_SELECTION');
  }
  add('apply-selection-before-invalid-snapshot', 'apply', { before: null, after: null, selectedIds: null, capacities: {} }, 'INVALID_SELECTION');
  add('recover-selection-before-invalid-snapshot', 'recover', { before: null, after: null, selectedIds: null, capacities: {} }, 'DEFEAT_KEEP_ONE');
  add('recover-many-before-invalid-ids', 'recover', { before: null, after: null, selectedIds: ['bad', 'worse'], capacities: {} }, 'DEFEAT_KEEP_ONE');
  add('recover-invalid-id-type', 'recover', { before: simpleBefore, after: simpleAfter, selectedIds: [1], capacities: { sera: 6 } }, 'INVALID_SELECTION');
  for (const [label, capacities, error] of [['missing', {}, 'OVER_CAPACITY'], ['fractional', { sera: 6.5 }, 'OVER_CAPACITY'], ['null-value', { sera: null }, 'OVER_CAPACITY'], ['false-value', { sera: false }, 'OVER_CAPACITY'], ['negative', { sera: -1 }, 'OVER_CAPACITY'], ['null', null, 'TypeError']]) {
    add(`apply-capacity-${label}`, 'apply', { before: simpleBefore, after: simpleAfter, selectedIds: [singleId], capacities }, error);
  }
  add('apply-options-null', 'apply', { before: simpleBefore, after: simpleAfter, selectedIds: [], capacities: { sera: 6 }, options: null }, 'TypeError');
  add('apply-options-false', 'apply', { before: simpleBefore, after: simpleAfter, selectedIds: [], capacities: { sera: 6 }, options: false });
  add('apply-empty-snapshots', 'apply', { before: {}, after: {}, selectedIds: [], capacities: {} });
  add('applyChanges-empty', 'applyChanges', { before: {}, changes: [] });
  add('applyChanges-null-no-changes', 'applyChanges', { before: null, changes: [] });
  add('applyChanges-malformed-missing-owner', 'applyChanges', { before: {}, changes: [{ cid: 'sera', key: 'missing', kind: 'remove' }] }, 'TypeError');
  add('applyChanges-malformed-unknown-kind', 'applyChanges', { before: simpleBefore, changes: [{ cid: 'sera', key: simpleBefore.sera[0].key, kind: 'unknown' }] });

  const reverseLegacy = Object.fromEntries([...data.characters].reverse().map(ch => [ch.id, kit(ch.id).map(card => card.id)]));
  const capacities = Object.fromEntries(data.characters.map(ch => [ch.id, 6]));
  add('migrate-all-characters-sorted', 'migrate', { legacy: reverseLegacy, capacities });
  const crossKey = { sera: kit('sera'), harin: kit('harin') };
  crossKey.sera[0].key = 'global-shared'; crossKey.harin[0].key = 'global-shared';
  add('migrate-global-key-sorted-winner', 'migrate', { legacy: crossKey, capacities: { sera: 6, harin: 6 } });
  add('migrate-whole-over-capacity-character', 'migrate', { legacy: grown, capacities: { sera: 6, harin: 6, yuri: 6 } });
  const fractional = { sera: [{ id: 'sera_round', key: 'keep-original', plus: 'invalid', metadata: { ratio: 0.25, nested: [1.5, null, false, { text: '보존🙂' }] } }], harin: kit('harin') };
  add('migrate-invalid-fractional-metadata-backup', 'migrate', { legacy: fractional, capacities: { sera: 6, harin: 6 } });
  add('migrate-valid-fractional-metadata', 'migrate', { legacy: { sera: [{ id: 'sera_round', metadata: { ratio: 0.125 } }, ...kit('sera').slice(1)] }, capacities: { sera: 6 } });
  add('migrate-deterministic-extra-and-duplicate-kind', 'migrate', { legacy: { sera: [...kit('sera').map(card => card.id), 'sera_round'] }, capacities: { sera: 6 } });
  add('migrate-evolution-origin-key', 'migrate', { legacy: { yuri: kit('yuri').map((card, index) => index ? card : { id: 'yuri_cocoon_guard', from: 'yuri_cocoon', plus: true }) }, capacities: { yuri: 6 } });
  add('migrate-existing-keys-null-false-and-shapes', 'migrate', { legacy: { sera: kit('sera').map((card, index) => index === 0 ? { ...card, key: '__proto__', plus: false, mods: null } : index === 1 ? { ...card, key: '', mods: { c: false } } : card) }, capacities: { sera: 6 } });
  add('migrate-utf16-unknown-cid-order', 'migrate', { legacy: { '\ue000': [{ fraction: 0.5 }], '𐀀': [null], sera: kit('sera') }, capacities: { sera: 6 } });
  add('migrate-invalid-owner-does-not-reserve-key', 'migrate', { legacy: { aaa: [{ key: 'shared', id: 'sera_round' }], sera: [{ key: 'shared', id: 'sera_round' }, ...kit('sera').slice(1)] }, capacities: { sera: 6 } });
  // Reserved character keys must remain own quarantine entries with the complete original.
  add('migrate-prototype-quarantine-preserves-own-property', 'migrate', {
    legacy: JSON.parse('{"__proto__":[{"id":"legacy_unknown","note":"keep complete original"}],"unknown":[{"id":"legacy_unknown"}]}'), capacities: {},
  });
  for (const [label, legacy] of [['null', null], ['array', []], ['scalar', 1], ['false', false]]) add(`migrate-invalid-root-${label}`, 'migrate', { legacy, capacities: {} }, 'INVALID_SNAPSHOT');
  for (const [label, rows] of [['scalar', 1], ['null', null], ['null-card', [null]], ['array-card', [[]]], ['number-card', [1]], ['unknown-card', ['missing-card']], ['duplicate-key', [{ key: 'same', id: 'sera_round' }, { key: 'same', id: 'sera_cover' }]], ['invalid-part', [{ id: 'sera_round', mods: { c: 'amp' } }]]]) {
    add(`migrate-quarantine-${label}`, 'migrate', { legacy: { sera: rows }, capacities: { sera: 6 } });
  }
  for (const [label, cap] of [['missing', {}], ['fractional', { sera: 6.5 }], ['negative', { sera: -1 }], ['zero', { sera: 0 }], ['null', null]]) {
    add(`migrate-capacity-${label}`, 'migrate', { legacy: { sera: kit('sera').map(card => card.id) }, capacities: cap });
  }
  add('migrate-empty', 'migrate', { legacy: {}, capacities: {} });
  for (const [label, partyEntry] of [['null', null], ['unknown', { charId: 'missing-character' }], ['missing-owner', {}]]) add(`capacity-invalid-${label}`, 'capacityOf', { partyEntry }, 'TypeError');

  assert.equal(new Set(cases.map(row => row.id)).size, cases.length, 'Duplicate memory case id');
  assert.deepEqual(sources(), loadedSources, 'Memory sources changed during generation');
  return { schema, scope, dataVersion: data.meta.version, sources: clone(loadedSources), cases };
}

export function checkFixture(fixture) {
  assert.equal(fixture?.schema, schema, 'Wrong memory fixture schema');
  assert.ok(Array.isArray(fixture.cases), 'Memory fixture requires cases');
  assert.equal(new Set(fixture.cases.map(row => row.id)).size, fixture.cases.length, 'Duplicate memory case id');
  const expected = generateCases();
  assert.deepEqual(fixture.sources, expected.sources, 'Memory source provenance is stale');
  assert.equal(fixture.cases.length, expected.cases.length, 'Memory case count differs');
  assert.deepEqual(fixture, expected, 'Memory fixture result, error, input or shape differs');
  assert.equal(JSON.stringify(fixture), JSON.stringify(expected), 'Memory fixture property ordering differs');
  return fixture.cases.length;
}

export function readFixture(path = fixturePath) {
  const json = gunzipSync(readFileSync(path), { maxOutputLength: 64 * 1024 * 1024 });
  const value = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(json));
  assert.deepEqual(json, Buffer.from(`${JSON.stringify(value)}\n`), 'Memory fixture JSON encoding, duplicate keys or shape is noncanonical');
  return value;
}

export function exportVectors(output = fixturePath) {
  const fixture = generateCases();
  const json = Buffer.from(`${JSON.stringify(fixture)}\n`);
  const bytes = gzipSync(json, { level: 9 });
  const temporary = mkdtempSync(join(dirname(output), '.memory-rules-'));
  try {
    const path = join(temporary, 'fixture'); writeFileSync(path, bytes); renameSync(path, output);
  } finally { rmSync(temporary, { recursive: true, force: true }); }
  return { cases: fixture.cases.length, bytes: bytes.length, uncompressedBytes: json.length, sha256: sha256(bytes), sources: fixture.sources };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  if (args.length > 1 || (args.length && !['--check', '--help'].includes(args[0]))) throw new Error('Usage: node tools/gen_memory_rule_vectors.mjs [--check|--help]');
  if (args[0] === '--help') console.log(scope);
  else if (args[0] === '--check') console.log(`Memory rule vectors checked: ${checkFixture(readFixture())}`);
  else console.log(JSON.stringify({ output: fixturePath, ...exportVectors() }, null, 2));
}
