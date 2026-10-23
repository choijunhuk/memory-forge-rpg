import assert from 'node:assert/strict';
import test from 'node:test';
import {readFileSync} from 'node:fs';
import {generateReference, referencePath} from '../../reports/card_text_20261016/reference_generator.mjs';

test('every authored and actual upgraded/resonance card description binds to fresh original JS text', () => {
  const value = generateReference();
  assert.equal(value.count, 519); assert.equal(value.rows.length, 519);
  assert.equal(readFileSync(referencePath, 'utf8'), JSON.stringify(value) + '\n');
  for (const variant of ['authored', 'plus', 'resonance6']) {
    const rows = value.rows.filter(r => r.variant === variant);
    assert.equal(rows.length, 173); assert.equal(new Set(rows.map(r => r.id)).size, 173);
    assert.ok(rows.every(r => typeof r.text === 'string'));
  }
});
