import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { gzipSync } from 'node:zlib';
import { generateCases, checkFixture, readFixture, exportVectors, fixturePath } from '../../tools/gen_memory_rule_vectors.mjs';

const require = createRequire(import.meta.url);
const M = require('../../proposals/memory_v2.cjs');
const data = require('../../data/game_data.json');
const clone = value => JSON.parse(JSON.stringify(value));
const sha256 = value => createHash('sha256').update(value).digest('hex');
const fixture = generateCases();
const row = id => {
  const found = fixture.cases.find(vector => vector.id === id);
  assert.ok(found, `Missing memory vector ${id}`);
  return found;
};
const result = id => {
  const vector = row(id);
  assert.ok(Object.hasOwn(vector.outcome, 'result'), `${id} must succeed`);
  return vector.outcome.result;
};
const error = (id, name, code) => {
  assert.equal(row(id).outcome.error.name, name);
  assert.equal(row(id).outcome.error.code, code);
  assert.equal(typeof row(id).outcome.error.message, 'string');
};

test('all authored characters, eligible cards, evolution branches and permanent parts retain their complete denominators', () => {
  assert.equal(data.characters.length, 10);
  const known = new Set(data.characters.map(ch => ch.id));
  const eligible = data.cards.filter(card => known.has(card.owner) || card.owner === 'common');
  const branches = data.cards.flatMap(card => card.evo || []);
  const parts = data.parts.filter(part => ['circuit', 'engraving'].includes(part.slot));
  assert.equal(eligible.length, 149);
  assert.equal(branches.length, 10);
  assert.equal(parts.length, 14);
  assert.equal(fixture.cases.length, 1072);
  assert.equal(new Set(fixture.cases.map(vector => vector.id)).size, 1072);
  assert.deepEqual([...new Set(fixture.cases.map(vector => vector.method))].sort(),
    ['apply', 'applyChanges', 'baseline', 'capacityOf', 'cost', 'inspect', 'listChanges', 'migrate', 'recover'].sort());
  const inspected = row('inspect-all-149-authored-eligible-cards').input.snapshot;
  assert.deepEqual(Object.values(inspected).flat().map(card => card.id).sort(), eligible.map(card => card.id).sort());
  for (const card of eligible) {
    assert.ok(result(`cost-card-${card.id}`) > 0);
    assert.equal(result(`apply-card-${card.id}`).error, null);
  }
  for (const branch of branches) {
    assert.deepEqual(result(`changes-evolution-${branch}`).map(change => change.kind), ['evolve', 'upgrade', 'part', 'part']);
    assert.equal(result(`apply-evolution-all-${branch}`).error, null);
    assert.ok(result(`cost-new-evolution-${branch}`) > 0);
  }
  for (const part of parts) {
    assert.equal(result(`changes-part-${part.id}`).length, 1);
    assert.equal(result(`apply-part-${part.id}`).error, null);
    assert.equal(result(`applyChanges-part-${part.id}`).sera[0].mods[part.slot === 'circuit' ? 'c' : 'e'], part.id);
  }
  for (const ch of data.characters) {
    assert.equal(result(`baseline-${ch.id}`).length, 5);
    assert.equal(result(`cost-baseline-${ch.id}`), 0);
    assert.equal(result(`cost-baseline-keyed-${ch.id}`), 0);
    assert.deepEqual(result(`changes-identical-${ch.id}`), []);
  }
  assert.equal(Object.keys(result('inspect-all-149-authored-eligible-cards').cards).length, 173);
  assert.equal(Object.keys(result('inspect-all-149-authored-eligible-cards').parts).length, 16);
});

test('independent literal baseline, complete-result and evolution costs catch shortcuts in the oracle and port', () => {
  assert.deepEqual(result('baseline-sera'), [
    { id: 'sera_round', key: 'base:sera:0' }, { id: 'sera_cover', key: 'base:sera:1' },
    { id: 'sera_aim', key: 'base:sera:2' }, { id: 'sera_timed', key: 'base:sera:3' },
    { id: 'com_guard', assigned: 'sera', key: 'base:sera:4' },
  ]);
  for (const [id, expected] of [
    ['cost-baseline-sera', 0], ['cost-same-kind-replaced-keyed-two', 2], ['cost-same-kind-replaced-default-zero', 0],
    ['cost-baseSpecs-null', 0], ['cost-baseSpecs-false', 0], ['cost-baseSpecs-empty', 6],
    ['cost-empty-default-five', 5], ['cost-empty-explicit-zero', 0], ['cost-empty-null-baseline-entry-one', 1], ['cost-existing-six', 6], ['cost-complete-nine', 9],
    ['cost-upgraded-rare-S-six', 6], ['cost-replaced-slots-one', 1],
    ['cost-new-evolution-yuri_cocoon_guard', 7], ['cost-new-evolution-yuri_frostbloom_ice', 8], ['cost-new-evolution-yuri_seedvault_fruit', 9],
    ['cost-card-com_breaker', 1], ['cost-card-sera_timed', 2], ['cost-card-sera_last', 3],
  ]) assert.equal(result(id), expected, id);
  assert.deepEqual(result('changes-same-kind-remove-add').map(change => change.kind), ['remove', 'add']);
  error('apply-over-capacity-commit', 'Error', 'OVER_CAPACITY');
  error('recover-over-capacity', 'Error', 'OVER_CAPACITY');
  assert.equal(result('apply-over-capacity-preview').used.sera, 9);
  assert.equal(result('apply-over-capacity-preview').error, 'OVER_CAPACITY');
  assert.equal(result('apply-upgraded-rare-S-six').used.sera, 6);
  assert.equal(result('apply-upgraded-rare-S-six').error, null);
  assert.deepEqual(result('apply-upgraded-rare-S-six-preview'), result('apply-upgraded-rare-S-six'));
  const pooled = [{ key: 'present', id: 'sera_round' }]; const invalidBase = [null];
  assert.throws(() => M.cost(data, 'sera', pooled, invalidBase), TypeError);
  assert.deepEqual(pooled, [{ key: 'present', id: 'sera_round' }]); assert.deepEqual(invalidBase, [null]);
});

test('current capacity preserves ascension and resonance, including actual JS fallback and negative slice boundaries', () => {
  for (const ch of data.characters) for (const [suffix, capacity] of [
    ['asc0-res0', 6], ['asc2-res0', 7], ['asc4-res0', 8], ['asc0-res4', 7], ['asc4-res6', 9],
    ['absent', 6], ['null', 6], ['false', 6], ['negative-one', 7], ['negative-two', 7], ['negative-three', 6],
    ['negative-length', 6], ['negative-past-length', 6], ['fraction-below', 6], ['fraction-above', 8],
    ['fraction-before-four', 8], ['fraction-after-four', 9], ['numeric-strings', 9], ['boolean-true', 6],
    ['slice-past-length', 9], ['empty-string', 6],
  ]) assert.equal(result(`capacity-${ch.id}-${suffix}`), capacity, `${ch.id}/${suffix}`);
  for (const suffix of ['null', 'unknown', 'missing-owner']) error(`capacity-invalid-${suffix}`, 'TypeError', undefined);
});

test('selection order, defeat limits, no selection and absent/null/false/partial fields preserve exact JSON shapes', () => {
  const preserved = result('apply-no-selection-preserves-prior').memory;
  assert.deepEqual(preserved, row('apply-no-selection-preserves-prior').input.before);
  assert.equal(preserved.yuri[0].metadata.ratio, 0.25);
  assert.equal(preserved.harin[0].plus, true);
  const normal = result('apply-normal-multiple');
  assert.deepEqual(normal.memory.sera.slice(-2).map(card => card.key), ['added-first', 'added-second']);
  assert.deepEqual(result('apply-selection-reversed').memory.sera.slice(-2).map(card => card.key), ['added-second', 'added-first']);
  assert.deepEqual(result('apply-defeat-zero').memory, row('apply-defeat-zero').input.before);
  assert.equal(result('apply-defeat-one').error, null);
  assert.equal(result('apply-defeat-multiple-preview').error, 'DEFEAT_KEEP_ONE');
  error('apply-defeat-before-capacity-error', 'Error', 'DEFEAT_KEEP_ONE');
  error('recover-many', 'Error', 'DEFEAT_KEEP_ONE');
  error('recover-remove-add-two-changes', 'Error', 'DEFEAT_KEEP_ONE');
  assert.deepEqual(result('apply-replace-and-remove-slots').memory.sera[0].mods, { c: 'c_delay', e: null });
  assert.equal(result('apply-upgrade-false').memory.sera[0].plus, false);
  assert.ok(!Object.hasOwn(result('apply-shape-absent').memory.sera[0], 'mods'));
  assert.equal(result('apply-shape-null-mods').memory.sera[0].mods, null);
  assert.deepEqual(result('apply-shape-partial-mods').memory.sera[0].mods, { c: 'c_delay' });
  assert.deepEqual(result('apply-shape-false-slot').memory.sera[0].mods, { c: false });
  assert.equal(result('apply-shape-unknown-metadata').memory.sera[0].metadata.fractional, 0.125);
  assert.deepEqual(result('changes-null-absent-false-no-delta'), []);
  assert.deepEqual(result('applyChanges-malformed-unknown-kind'), row('applyChanges-malformed-unknown-kind').input.before);
});

test('identity, owner, evolution, assignment, selection and part rejection precedence stays explicit', () => {
  for (const [id, code] of [
    ['inspect-invalid-array-card', 'IDENTITY_REQUIRED'], ['inspect-invalid-duplicate-global-key', 'IDENTITY_REQUIRED'],
    ['inspect-invalid-precedence-identity-card', 'IDENTITY_REQUIRED'], ['inspect-invalid-precedence-card-assignment-upgrade', 'INVALID_CARD'],
    ['inspect-invalid-precedence-assignment-evolution', 'INVALID_ASSIGNMENT'], ['inspect-invalid-precedence-evolution-upgrade', 'INVALID_EVOLUTION'],
    ['inspect-invalid-precedence-upgrade-part', 'INVALID_UPGRADE'], ['inspect-invalid-mods-false', 'INVALID_PART'],
    ['inspect-invalid-mods-extra-slot', 'INVALID_PART'], ['inspect-invalid-temporary-part', 'INVALID_PART'],
    ['changes-inspect-before-character-set', 'INVALID_CARD'], ['changes-character-set-mismatch', 'CHARACTER_SET_MISMATCH'],
    ['changes-cross-character-reassignment', 'REASSIGNMENT_REQUIRES_SEPARATE_FLOW'], ['changes-assigned-metadata', 'REASSIGNMENT_REQUIRES_SEPARATE_FLOW'],
    ['changes-arbitrary-transform', 'UNSUPPORTED_TRANSFORMATION'], ['changes-reverse-evolution', 'UNSUPPORTED_TRANSFORMATION'],
    ['changes-switch-evolution-branch', 'UNSUPPORTED_TRANSFORMATION'], ['apply-selection-before-invalid-snapshot', 'INVALID_SELECTION'],
    ['recover-selection-before-invalid-snapshot', 'DEFEAT_KEEP_ONE'], ['recover-many-before-invalid-ids', 'DEFEAT_KEEP_ONE'],
  ]) error(id, 'Error', code);
  error('applyChanges-malformed-missing-owner', 'TypeError', undefined);
  error('apply-options-null', 'TypeError', undefined);
  assert.deepEqual(result('apply-empty-snapshots'), { memory: {}, used: {}, error: null });
  const raw = JSON.parse('{"sera":[null],"0":[]}');
  assert.deepEqual(Object.keys(raw), ['0', 'sera']);
  assert.throws(() => M.inspect(data, raw), { code: 'INVALID_SNAPSHOT' });
  assert.equal(JSON.stringify(raw), '{"0":[],"sera":[null]}');
});

test('URI escaping and UTF-16 cid sorting preserve non-BMP and instance order while lone-surrogate changes reject', () => {
  assert.equal(result('changes-key-reserved')[0].id, "delta:sera:%3A%2F%3F%23%5B%5D%40!%24%26'()*%2B%2C%3B%3D%20%25:remove");
  assert.equal(result('changes-key-non-BMP')[0].id, 'delta:sera:%ED%95%9C%EA%B8%80%F0%9F%99%82%F0%90%80%80:remove');
  assert.equal(result('changes-key-control')[0].id, 'delta:sera:%00%0A%09:remove');
  assert.equal(result('changes-key-combining')[0].id, 'delta:sera:e%CC%81:remove');
  assert.equal(result('changes-key-precomposed')[0].id, 'delta:sera:%C3%A9:remove');
  for (const suffix of ['high', 'low']) {
    error(`changes-key-lone-${suffix}`, 'URIError', undefined);
    assert.deepEqual(result(`changes-identical-key-lone-${suffix}`), []);
  }
  const ordered = result('changes-cid-sort-and-instance-order');
  assert.deepEqual(ordered.map(change => change.cid), ['harin', 'sera', 'sera', 'sera', 'yuri']);
  assert.deepEqual(ordered.filter(change => change.cid === 'sera').map(change => change.key), ['\ue000', '𐀀', 'a']);
  assert.deepEqual(Object.keys(result('migrate-utf16-unknown-cid-order').quarantine), ['𐀀', '\ue000']);
});

test('migration preserves deterministic keys, complete quarantine and fractional invalid legacy metadata', () => {
  const all = result('migrate-all-characters-sorted');
  assert.deepEqual(Object.keys(all.memory), ['aila', 'harin', 'kaya', 'lumi', 'nia', 'ravien', 'rien', 'sera', 'sion', 'yuri']);
  assert.deepEqual(all.legacyBackup, row('migrate-all-characters-sorted').input.legacy);
  assert.equal(all.schema, 'mf-memory-v2');
  assert.equal(all.version, 0);
  assert.deepEqual(all.history, []); assert.deepEqual(all.receipts, {});
  for (const ch of data.characters) assert.deepEqual(all.memory[ch.id], result(`baseline-${ch.id}`));
  const duplicate = result('migrate-global-key-sorted-winner');
  assert.ok(Object.hasOwn(duplicate.memory, 'harin'));
  assert.ok(!Object.hasOwn(duplicate.memory, 'sera'));
  assert.equal(duplicate.quarantine.sera.code, 'IDENTITY_REQUIRED');
  const over = result('migrate-whole-over-capacity-character');
  assert.ok(!Object.hasOwn(over.memory, 'sera'));
  assert.equal(over.quarantine.sera.code, 'OVER_CAPACITY');
  assert.deepEqual(over.quarantine.sera.original, row('migrate-whole-over-capacity-character').input.legacy.sera);
  const invalid = result('migrate-invalid-fractional-metadata-backup');
  const original = row('migrate-invalid-fractional-metadata-backup').input.legacy;
  assert.deepEqual(invalid.legacyBackup, original);
  assert.deepEqual(invalid.quarantine.sera, { code: 'INVALID_UPGRADE', original: original.sera });
  assert.equal(invalid.quarantine.sera.original[0].metadata.ratio, 0.25);
  assert.equal(invalid.quarantine.sera.original[0].metadata.nested[0], 1.5);
  assert.deepEqual(invalid.memory.harin, original.harin);
  assert.equal(result('migrate-valid-fractional-metadata').memory.sera[0].metadata.ratio, 0.125);
  assert.equal(result('migrate-deterministic-extra-and-duplicate-kind').memory.sera.at(-1).key, 'legacy:sera:5');
  assert.equal(result('migrate-evolution-origin-key').memory.yuri[0].key, 'base:yuri:0');
  const shapes = result('migrate-existing-keys-null-false-and-shapes').memory.sera;
  assert.equal(shapes[0].key, '__proto__'); assert.equal(shapes[0].plus, false); assert.equal(shapes[0].mods, null);
  assert.equal(shapes[1].key, 'base:sera:1'); assert.deepEqual(shapes[1].mods, { c: false });
});

test('migration preserves an own __proto__ quarantine entry and its complete original', () => {
  const vector = row('migrate-prototype-quarantine-preserves-own-property');
  assert.ok(Object.hasOwn(vector.input.legacy, '__proto__'), 'the regression input must contain a real own property');
  const state = M.migrate(data, clone(vector.input.legacy), {});
  assert.deepEqual(state.legacyBackup, vector.input.legacy);
  assert.deepEqual(state.quarantine.unknown, { code: 'INVALID_SNAPSHOT', original: [{ id: 'legacy_unknown' }] });
  assert.ok(Object.hasOwn(state.quarantine, '__proto__'), 'unknown __proto__ character must be quarantined as an own property');
  assert.equal(Object.getPrototypeOf(state.quarantine), Object.prototype);
  assert.deepEqual(state.quarantine.__proto__, {
    code: 'INVALID_SNAPSHOT', original: [{ id: 'legacy_unknown', note: 'keep complete original' }],
  });
});

test('every actual call verifies the entire input/data graph, and returned mutable memory graphs are detached', () => {
  for (const vector of fixture.cases) {
    assert.deepEqual(vector.inputAfter, vector.input, vector.id);
    assert.equal(JSON.stringify(vector.inputAfter), JSON.stringify(vector.input), `${vector.id}: property order`);
    assert.equal(vector.dataUnchanged, true, vector.id);
  }
  const authoredBefore = JSON.stringify(data);
  const before = { sera: M.baseline(data, 'sera') };
  before.sera[0].metadata = { fraction: 0.25, rows: [{ text: 'before' }] };
  const after = clone(before); after.sera.push({ key: 'new-copy', id: 'sera_round', metadata: { rows: [{ text: 'after' }] } });
  const changes = M.listChanges(data, before, after);
  const original = clone({ before, after, changes });
  const applied = M.apply(data, before, after, changes.map(change => change.id), { sera: 6 });
  applied.memory.sera[0].metadata.rows[0].text = 'caller edit';
  applied.memory.sera.at(-1).metadata.rows[0].text = 'caller add edit';
  changes[0].value.metadata.rows[0].text = 'change edit';
  assert.deepEqual(before, original.before); assert.deepEqual(after, original.after);
  const direct = M.applyChanges(before, original.changes);
  direct.sera[0].metadata.rows[0].text = 'direct edit';
  direct.sera.at(-1).metadata.rows[0].text = 'direct added edit';
  const recovered = M.recover(data, before, after, original.changes.map(change => change.id), { sera: 6 });
  recovered.memory.sera[0].metadata.rows[0].text = 'recovered edit';
  assert.deepEqual(before, original.before); assert.deepEqual(after, original.after);
  assert.deepEqual(original.changes, M.listChanges(data, before, after));
  const baseline = M.baseline(data, 'sera'); baseline[0].id = 'caller edit';
  assert.equal(M.baseline(data, 'sera')[0].id, 'sera_round');
  const legacy = { sera: before.sera, harin: [{ id: 'harin_shield', plus: 1, metadata: { fraction: 0.375 } }] };
  const legacyBefore = clone(legacy);
  const migrated = M.migrate(data, legacy, { sera: 6, harin: 6 });
  migrated.memory.sera[0].metadata.rows[0].text = 'migrated edit';
  migrated.quarantine.harin.original[0].metadata.fraction = 99.5;
  assert.deepEqual(legacy, legacyBefore);
  assert.deepEqual(migrated.legacyBackup, legacyBefore);
  assert.equal(JSON.stringify(data), authoredBefore);
});

test('strict fixture checks reject provenance, case coverage, result/error and input-shape tampering', () => {
  assert.equal(checkFixture(fixture), 1072);
  for (const mutate of [
    copy => { copy.sources.memory.sha256 = '0'.repeat(64); },
    copy => { copy.schema = 'other-schema'; },
    copy => { copy.extra = true; },
    copy => { copy.cases.pop(); },
    copy => { copy.cases.push(clone(copy.cases[0])); },
    copy => { copy.cases[0].outcome.result[0].key = 'changed'; },
    copy => { copy.cases[0].inputAfter.extra = null; },
    copy => { copy.cases.find(vector => vector.id === 'inspect-invalid-missing-key').outcome.error.code = 'OTHER_ERROR'; },
    copy => { delete copy.cases.find(vector => vector.id === 'inspect-shape-false-plus').input.snapshot.sera[0].plus; },
    copy => { copy.cases.find(vector => vector.id === 'migrate-invalid-fractional-metadata-backup').outcome.result.legacyBackup.sera[0].metadata.ratio = 0; },
    copy => { copy.cases[0].dataUnchanged = false; },
  ]) {
    const changed = clone(fixture); mutate(changed);
    assert.throws(() => checkFixture(changed), /memory|source|provenance|schema|case|fixture/i);
  }
});

test('gzip export is byte deterministic, strict on damaged/ambiguous encoding and atomic on unexpected exceptions', t => {
  const directory = mkdtempSync(join(tmpdir(), 'mf-memory-rules-'));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  const output = join(directory, 'vectors.json.gz');
  const summary = exportVectors(output); const first = readFileSync(output);
  assert.equal(summary.cases, 1072); assert.equal(summary.bytes, first.length); assert.equal(summary.sha256, sha256(first));
  assert.deepEqual(readFixture(output), fixture);
  exportVectors(output); assert.deepEqual(readFileSync(output), first);
  assert.deepEqual(readdirSync(directory), ['vectors.json.gz']);
  const crc = Buffer.from(first); crc[crc.length - 1] ^= 1;
  for (const bad of [first.subarray(0, first.length - 8), crc, gzipSync(Buffer.from([0xff])), gzipSync(Buffer.from('{')),
    gzipSync(Buffer.from(` ${JSON.stringify(fixture)}\n`)),
    gzipSync(Buffer.from(`${JSON.stringify(fixture).replace('{"schema":', '{"schema":"mf-memory-rules-v1","schema":')}\n`)),
  ]) { writeFileSync(output, bad); assert.throws(() => readFixture(output)); }
  writeFileSync(output, 'previous artifact');
  for (const [method, fault] of [
    ['baseline', new Error('unexpected generation failure')],
    ['inspect', Object.assign(new Error('unexpected familiar code'), { code: 'INVALID_SNAPSHOT' })],
  ]) {
    const broken = t.mock.method(M, method, () => { throw fault; });
    assert.throws(() => generateCases(), caught => caught === fault);
    assert.throws(() => exportVectors(output), caught => caught === fault);
    assert.equal(readFileSync(output, 'utf8'), 'previous artifact');
    assert.deepEqual(readdirSync(directory), ['vectors.json.gz']);
    broken.mock.restore();
  }
  const mutating = t.mock.method(M, 'apply', (_data, before) => { before.sera[0].id = 'mutated'; });
  assert.throws(() => generateCases(), TypeError);
  mutating.mock.restore();
});

test('checked-in artifact matches fresh actual JS execution without claiming Run or persistence acceptance', () => {
  assert.equal(checkFixture(readFixture(fixturePath)), 1072);
  assert.match(fixture.scope, /Pure Memory/);
  assert.match(fixture.scope, /Run command replay.*NOT_IMPLEMENTED/);
});
