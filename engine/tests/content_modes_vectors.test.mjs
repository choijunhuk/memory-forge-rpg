// The C# content-mode check replays this fixture; it must be exactly what the generator produces from the pinned sources.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { gunzipSync } from 'node:zlib';
import { checkFixture, fixturePath, generateCases } from '../../tools/gen_content_modes_vectors.mjs';

test('content-mode vectors are current and cover every rule branch, error path and ledger', () => {
  const fixture = JSON.parse(gunzipSync(readFileSync(fixturePath)).toString('utf8'));
  const counts = checkFixture(fixture);
  assert.ok(counts.battleCommands > 1000 && counts.riftCommands > 200 && counts.errors > 100 && counts.time > 400, JSON.stringify(counts));
  for (const value of Object.values(fixture.witness)) assert.ok(value > 0);
  // TEST-ONLY variants never leak into the shipped one
  assert.equal(fixture.modes.shipped.dungeon.waves.encounters, null);
  assert.equal(fixture.modes.shipped.rift.rotation.cycles, null);
  assert.equal(fixture.modes.shipped.daily.targetPool.tasks, null);
});

test('content-mode generation is deterministic', () => {
  assert.deepEqual(generateCases().counts, generateCases().counts);
});
