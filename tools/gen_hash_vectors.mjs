// §16.2 portable canonical JSON boundaries, independent of Battle/Run replay.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { canonicalJSON } from './gen_traces.mjs';

const root = fileURLToPath(new URL('..', import.meta.url));
const hash = bytes => createHash('sha256').update(bytes).digest('hex');

// Expected strings are authored explicitly, never obtained from the serializer.
export const hashCases = [
  { id: 'null', value: null, canonical: 'null' },
  { id: 'empty-object', value: {}, canonical: '{}' },
  { id: 'empty-array', value: [], canonical: '[]' },
  { id: 'booleans', value: { yes: true, no: false }, canonical: '{"no":false,"yes":true}' },
  { id: 'numeric-looking-keys', value: { 2: 'two', 10: 'ten', 1: 'one', '01': 'leading', 4294967295: 'last' },
    canonical: '{"01":"leading","1":"one","10":"ten","2":"two","4294967295":"last"}' },
  { id: 'recursive-diagnostics', value: { log: [1], ev: [2], keep: [{ log: [3], ev: [4], z: 2, a: { log: 5, keep: true } }], nested: { ev: 6, log: 7 } },
    canonical: '{"keep":[{"a":{"keep":true},"z":2}],"nested":{}}' },
  { id: 'all-controls', value: Array.from({ length: 32 }, (_, i) => String.fromCharCode(i)).join(''),
    canonical: String.raw`"\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\b\t\n\u000b\f\r\u000e\u000f\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001a\u001b\u001c\u001d\u001e\u001f"` },
  { id: 'quotes-slashes', value: '"\\/<> &', canonical: String.raw`"\"\\/<> &"` },
  { id: 'unicode-preserved', value: '기억 café é \u2028\u2029 😀', canonical: '"기억 café é \u2028\u2029 😀"' },
  { id: 'surrogate-pairs', value: '\ud800\udc00\udbff\udfff', canonical: '"𐀀􏿿"' },
  { id: 'lone-surrogates', value: '\ud800x\udfff\udbff', canonical: String.raw`"\ud800x\udfff\udbff"` },
  { id: 'surrogate-boundary-keys', value: { '\ue000': 4, '\udfff': 3, '\ud800\udc00': 2, '\ud800': 1, a: 5 },
    canonical: '{"a":5,"\\ud800":1,"𐀀":2,"\\udfff":3,"":4}' },
  { id: 'safe-integer-boundaries', value: [9007199254740991, -9007199254740991, 4294967295, -2147483648, -1, -0],
    canonical: '[9007199254740991,-9007199254740991,4294967295,-2147483648,-1,0]' },
  { id: 'array-order-a', value: [3, 2, 1], canonical: '[3,2,1]' },
  { id: 'array-order-b', value: [1, 2, 3], canonical: '[1,2,3]' },
  { id: 'nested-objects-arrays', value: [{ 2: 'b', 10: 'a' }, null, [false, { z: 1, a: 2 }]],
    canonical: '[{"10":"a","2":"b"},null,[false,{"a":2,"z":1}]]' },
  { id: 'ordinary-special-keys', value: Object.fromEntries([['toString', 2], ['__proto__', 1], ['constructor', 3]]),
    canonical: '{"__proto__":1,"constructor":3,"toString":2}' },
];

export function buildHashVectors() {
  return {
    schema: 'mf-canonical-hash-v1',
    sources: Object.fromEntries(['tools/gen_hash_vectors.mjs', 'tools/gen_traces.mjs'].map(path => [path, hash(readFileSync(resolve(root, path)))])),
    vectors: hashCases.map(({ id, value, canonical }) => {
      assert.equal(canonicalJSON(value), canonical, `Independent canonical expectation: ${id}`);
      return { id, value: JSON.parse(JSON.stringify(value)), canonical, sha256: hash(canonical) };
    }),
  };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const output = resolve(root, 'engine/tests/vectors/canonical_hash_vectors.json');
  const fixture = buildHashVectors();
  writeFileSync(output, `${JSON.stringify(fixture, null, 2)}\n`);
  console.log(`Canonical hash vectors: ${fixture.vectors.length}; ${output}`);
}
