import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { buildHashVectors } from '../../tools/gen_hash_vectors.mjs';
import { canonicalJSON, stateHash } from '../../tools/gen_traces.mjs';

test('committed canonical boundary vectors retain explicit expected strings and current source provenance', () => {
  const fixture = JSON.parse(readFileSync(new URL('./vectors/canonical_hash_vectors.json', import.meta.url), 'utf8'));
  assert.deepEqual(fixture, buildHashVectors());
  assert.equal(new Set(fixture.vectors.map(v => v.id)).size, 17);
  for (const vector of fixture.vectors) {
    assert.equal(canonicalJSON(vector.value), vector.canonical, vector.id);
    assert.equal(stateHash(vector.value), vector.sha256, vector.id);
    assert.equal(createHash('sha256').update(vector.canonical).digest('hex'), vector.sha256, vector.id);
  }
  const byId = Object.fromEntries(fixture.vectors.map(v => [v.id, v]));
  assert.equal(byId.null.sha256, '74234e98afe7498fb5daf1f36ac2d78acc339464f950703b8c019892f982b90b');
  assert.equal(byId['empty-array'].sha256, '4f53cda18c2baa0c0354bb5f9a3ecbe5ed12ab4d8e11ba873c2f11161202b945');
  assert.notEqual(byId['array-order-a'].sha256, byId['array-order-b'].sha256);
});

test('canonical JSON rejects unsupported values and unsafe numeric values without rounding', () => {
  assert.equal(canonicalJSON(-0), '0');
  for (const value of [0.5, -0.5, 9007199254740992, -9007199254740992, NaN, Infinity, -Infinity])
    assert.throws(() => canonicalJSON(value), /safe integer/i);
  for (const value of [1n, Symbol('unsupported'), () => 1])
    assert.throws(() => canonicalJSON(value), /unsupported/i);
});
