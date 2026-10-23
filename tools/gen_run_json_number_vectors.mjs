// §8 receipt-body equality / §16.2: actual JS JSON.stringify of exact binary64 inputs.
// No game state, account data or configurable numeric policy is changed.
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {mkdtempSync, readFileSync, renameSync, rmSync, writeFileSync} from 'node:fs';
import {dirname, join, resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {isDeepStrictEqual} from 'node:util';
import {gzipSync} from 'node:zlib';
import {decodeVectors} from './gen_run_create_vectors.mjs';

export const fixturePath = fileURLToPath(new URL('../engine/tests/vectors/run_json_number_vectors.json.gz', import.meta.url));
const sourcePath = 'tools/gen_run_json_number_vectors.mjs', schema = 'mf-run-json-number-v1';
const fingerprint = () => createHash('sha256').update(readFileSync(fileURLToPath(import.meta.url))).digest('hex');
const contract = 'Exact IEEE-754 bits are input; JSON.stringify(Number) is the expected string. Finite and nonfinite values, signs, subnormals, exponent/plain thresholds and adjacent representations. Source-only oracle; not complete memory-save or device acceptance.';

export function generateCases() {
  const cases = [], seen = new Set(), view = new DataView(new ArrayBuffer(8));
  const add = raw => {
    const bits = BigInt.asUintN(64, raw), hex = bits.toString(16).padStart(16, '0');
    if (seen.has(hex)) return;
    seen.add(hex); view.setBigUint64(0, bits, false);
    cases.push({bits: hex, json: JSON.stringify(view.getFloat64(0, false))});
  };
  const seeds = [0, -0, NaN, Infinity, -Infinity, Number.MIN_VALUE, -Number.MIN_VALUE, Number.MAX_VALUE, -Number.MAX_VALUE,
    1e-7, 1e-6, 1e20, 1e21, 1e23, 1000000000000000100, 0.8455124082255701,
    Number.MAX_SAFE_INTEGER, -Number.MAX_SAFE_INTEGER, 2 ** 53, -(2 ** 53), 2 ** 53 + 2, -(2 ** 53 + 2)];
  // Exact normal/subnormal and power-of-two boundaries have asymmetric adjacent rounding intervals.
  for (const exponent of [-1022, -1021, -1000, -100, -53, -1, 0, 1, 52, 53, 54, 100, 1000, 1023])
    seeds.push(2 ** exponent, -(2 ** exponent));
  for (const value of seeds) {
    view.setFloat64(0, value, false); const bits = view.getBigUint64(0, false);
    add(bits); add(bits + 1n); add(bits - 1n);
  }
  let state = 0x6d656d6f7279666fn;
  for (let i = 0; i < 2048; i++) {
    state = BigInt.asUintN(64, state * 6364136223846793005n + 1442695040888963407n); add(state);
  }
  assert.equal(cases.length, 2176);
  return {schema, status: 'JS_ORACLE_ONLY', source: {path: sourcePath, sha256: fingerprint()}, contract, count: 2176, cases};
}

export function checkFixture(fixture) {
  assert.deepEqual(Object.keys(fixture), ['schema', 'status', 'source', 'contract', 'count', 'cases']);
  assert.equal(fixture.schema, schema); assert.equal(fixture.status, 'JS_ORACLE_ONLY');
  assert.deepEqual(fixture.source, {path: sourcePath, sha256: fingerprint()});
  assert.equal(fixture.contract, contract); assert.equal(fixture.count, 2176);
  assert.ok(isDeepStrictEqual(fixture, generateCases()), 'Numeric JSON oracle differs from actual fresh JS execution');
  return {cases: fixture.cases.length};
}

export function exportVectors(output = fixturePath) {
  const fixture = generateCases(), bytes = gzipSync(Buffer.from(JSON.stringify(fixture) + '\n'), {level: 9});
  checkFixture(decodeVectors(bytes));
  const temporary = mkdtempSync(join(dirname(output), '.run-json-number-'));
  try {
    const path = join(temporary, 'fixture'); writeFileSync(path, bytes);
    assert.equal(fixture.source.sha256, fingerprint(), 'Generator changed before export; previous fixture preserved');
    renameSync(path, output);
  } finally { rmSync(temporary, {recursive: true, force: true}); }
  return {cases: fixture.count, bytes: bytes.length};
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2); assert.ok(args.length === 0 || args.length === 1 && args[0] === '--check', 'Use [--check]');
  console.log(JSON.stringify(args[0] === '--check' ? checkFixture(decodeVectors(readFileSync(fixturePath))) : exportVectors()));
}
