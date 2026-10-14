import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname } from 'node:path';
import { createRequire } from 'node:module';
import { assessTargets, fight, hash, makeScenario, parseArgs, quantile, runProbe, summarize } from './balance_probe.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const require = createRequire(import.meta.url);
const MF = require(join(here, '..', 'rules-core.js'));
const data = JSON.parse(readFileSync(join(here, '..', '..', 'data', 'game_data.json'), 'utf8'));
const group = { zone: '1', tier: 'normal', enc: data.encounters['1'].normal[0] };

test('all policies receive the exact same independent starting setup', () => {
  const scenario = makeScenario(data, group, 0);
  const before = structuredClone(scenario.setup);
  const results = ['random', 'greedy', 'planner'].map((policy) => fight(data, scenario, policy));
  assert.deepEqual(scenario.setup, before);
  assert.deepEqual(new Set(results.map((r) => r.initialStateHash)), new Set([scenario.initialStateHash]));
  assert.deepEqual(new Set(results.map((r) => r.setupHash)), new Set([scenario.setupHash]));
  assert.deepEqual(new Set(results.map((r) => r.seed)), new Set([scenario.setup.seed]));
  assert.deepEqual(new Set(results.map((r) => r.scenarioKey)), new Set([scenario.key]));
  const reconstructed = MF.Battle.create(data, structuredClone(scenario.setup));
  reconstructed.do('begin');
  assert.equal(scenario.setupHash, hash(scenario.setup));
  assert.equal(scenario.initialStateHash, hash(reconstructed.s));
  assert.notEqual(scenario.initialStateHash, scenario.setupHash);
  assert.equal(scenario.setup.party.length, 3);
  assert.ok(scenario.setup.party.every((p) => p.res === 0 && p.power4 === 160 && p.hp === 81 && p.refine === 1));
  assert.deepEqual(scenario.setup.relics, []);
});

test('policy randomness cannot change later scenarios; repeat is deterministic', () => {
  const next = makeScenario(data, group, 1);
  const first = makeScenario(data, group, 0);
  fight(data, first, 'random');
  fight(data, first, 'planner');
  assert.deepEqual(makeScenario(data, group, 1), next);
  assert.deepEqual(fight(data, first, 'random'), fight(data, first, 'random'));
});

test('report counts use actual scenarios and all policy trials', () => {
  const report = runProbe({ requestedN: 1, data, groups: [group] });
  assert.equal(report.method.requestedN, 1);
  assert.equal(report.method.repsPerGroup, 10);
  assert.equal(report.method.scenarios, 10);
  assert.equal(report.method.trials, 30);
  assert.equal(report.scenarios.length, 10);
  assert.equal(report.trials.length, 30);
  assert.equal(report.byGroup[`1/normal/${group.enc.join('+')}/random`].attempts, 10);
});

test('timeouts and defeats remain in the denominator and quantiles use all outcomes', () => {
  const timed = fight(data, makeScenario(data, group, 0), 'greedy', { maxTurns: 0 });
  assert.equal(timed.outcome, 'timeout');
  assert.equal(timed.processedPlayerTurns, 0);
  const stats = summarize([
    { outcome: 'win', turns: 2, hpLossPct: 10 },
    { outcome: 'defeat', turns: 4, hpLossPct: 80 },
    { outcome: 'timeout', turns: 60, hpLossPct: 30 },
  ]);
  assert.deepEqual([stats.attempts, stats.wins, stats.defeats, stats.timeouts], [3, 1, 1, 1]);
  assert.deepEqual([stats.turns.p50, stats.turns.p90, stats.hpLossPct.p50, stats.hpLossPct.p90], [4, 60, 30, 80]);
  assert.equal(quantile([7, 1, 4, 9], 0.5), 4);
});

test('unexpected internal precheck code aborts the fight', () => {
  const original = MF.Battle.prototype.check;
  try {
    MF.Battle.prototype.check = () => 'UNSUPPORTED_EFFECT';
    assert.throws(() => fight(data, makeScenario(data, group, 0), 'random'), /Unexpected play precheck: UNSUPPORTED_EFFECT/);
  } finally { MF.Battle.prototype.check = original; }
});

test('documented balance bands preserve exact upper boundaries and gate only random wins', () => {
  const stats = (turns, hpLossPct, wins = 0, attempts = 10) =>
    ({ turns: { mean: turns }, hpLossPct: { mean: hpLossPct }, wins, attempts });
  const atBoundary = assessTargets({ byZoneTierPolicy: {
    '5/boss/greedy': stats(19.8, 55, 0),
    '1/normal/random': stats(5, 12, 98, 100),
  } });
  assert.equal(atBoundary.overall, 'not_certified');
  assert.equal(atBoundary.failedMetrics, 0);
  assert.equal(atBoundary.metrics['5/boss/greedy'].winPct, undefined);
  assert.deepEqual([atBoundary.metrics['5/boss/greedy'].turns.max,
    atBoundary.metrics['5/boss/greedy'].hpLossPct.max], [19.8, 55]);
  const outside = assessTargets({ byZoneTierPolicy: {
    '5/boss/planner': stats(20, 55.1),
    '1/normal/random': stats(3, 4, 97, 100),
  } });
  assert.equal(outside.overall, 'failed');
  assert.deepEqual([outside.metrics['5/boss/planner'].turns.status,
    outside.metrics['5/boss/planner'].hpLossPct.status,
    outside.metrics['1/normal/random'].winPct.status], ['above', 'above', 'below']);
  assert.equal(outside.failedMetrics, 3);
});

test('CLI rejects malformed N and writes a readable full JSON report', () => {
  assert.throws(() => parseArgs(['1.5']), /Invalid argument/);
  assert.throws(() => parseArgs(['0']), /positive integer/);
  const dir = mkdtempSync(join(tmpdir(), 'mf-balance-'));
  try {
    const out = join(dir, 'report.json');
    execFileSync(process.execPath, [join(here, 'balance_probe.mjs'), '1', '--out', out], { stdio: 'pipe' });
    const report = JSON.parse(readFileSync(out, 'utf8'));
    assert.equal(report.method.groups, 21);
    assert.equal(report.method.scenarios, 210);
    assert.equal(report.method.trials, 630);
    assert.equal(report.trials.length, 630);
    assert.ok(report.hashes.baseContent && report.hashes.engine && report.hashes.runner);
    assert.ok(['failed', 'not_certified'].includes(report.assessment.overall));
  } finally { rmSync(dir, { recursive: true, force: true }); }
});
