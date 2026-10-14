import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { assessTargets, fight, hash, makeScenario, runProbe, summarize } from './balance_probe.mjs';
import { analyze, encounterBudgets, traceFight } from '../../tools/review/balance_causes.mjs';

const require = createRequire(import.meta.url);
const MF = require('../rules-core.js');
const base = JSON.parse(readFileSync(new URL('../../data/game_data.json', import.meta.url), 'utf8'));
const candidate = require('../../proposals/late_enemies.cjs');
const group = [{ zone: '1', tier: 'normal', enc: ['e_hound'] }];

test('tracing preserves battle result, input data, and prototype even on an error', () => {
  const report = runProbe({ requestedN: 1, groups: group, data: base, maxTurns: 10 });
  const scenario = report.scenarios[0];
  const original = Object.fromEntries(['command', 'loseHp', 'healParty', 'enemyHit', 'hitEnemy']
    .map(k => [k, MF.Battle.prototype[k]]));
  const dataBefore = JSON.stringify(base);
  const scenarioBefore = JSON.stringify(scenario);
  const direct = fight(base, scenario, 'random', { maxTurns: 10 });
  const { trial, trace } = traceFight(base, scenario, 'random', { maxTurns: 10 });
  assert.deepEqual(trial, direct);
  assert.equal(trace.grossLoss - trace.healing, trial.hpLoss);
  assert.ok(trace.grossLoss >= 0);
  assert.throws(() => traceFight(base, scenario, 'unknown'), /Unknown policy/);
  for (const [name, method] of Object.entries(original)) assert.equal(MF.Battle.prototype[name], method);
  assert.equal(JSON.stringify(base), dataBefore);
  assert.equal(JSON.stringify(scenario), scenarioBefore);
});

test('independent aggregation and signed contributors reconcile all failed metrics', () => {
  const report = runProbe({ requestedN: 1, groups: group, data: base, maxTurns: 10 });
  const analysis = analyze(report, base);
  assert.equal(analysis.method.trials, 30);
  assert.equal(analysis.failedMetrics, report.assessment.failedMetrics);
  for (const failure of analysis.failed) {
    const sum = failure.contributions.reduce((n, c) => n + c.signedContribution, 0);
    assert.ok(Math.abs(sum - failure.deviation) < 1e-9);
  }
  const corrupted = structuredClone(report);
  corrupted.byZoneTierPolicy['1/normal/random'].turns.mean += 1;
  assert.throws(() => analyze(corrupted, base), /turns mean mismatch/);
  const missingRow = structuredClone(report);
  delete missingRow.assessment.metrics['1/normal/random'];
  missingRow.assessment.failedMetrics = 0;
  assert.throws(() => analyze(missingRow, base), /Assessment metric row count mismatch/);
  const missingMetric = structuredClone(report);
  delete missingMetric.assessment.metrics['1/normal/random'].winPct;
  assert.throws(() => analyze(missingMetric, base), /Missing or extra metrics/);
});

test('analysis rejects altered target bounds and assessment results with unchanged source hashes', async t => {
  const report = runProbe({ requestedN: 1, groups: group, data: base, maxTurns: 10 });
  assert.equal(report.assessment.failedMetrics, 5);
  const cases = [
    ['weakened target bands', corrupted => {
      for (const metrics of Object.values(corrupted.assessment.metrics)) for (const metric of Object.values(metrics)) {
        metric.min = -1e9;
        metric.max = 1e9;
        metric.status = 'pass';
      }
      corrupted.assessment.failedMetrics = 0;
      corrupted.assessment.overall = 'not_certified';
    }, /Metric target bounds mismatch/],
    ['changed lower bound', corrupted => { corrupted.assessment.metrics['1/normal/random'].turns.min += 1; }, /Metric target bounds mismatch/],
    ['changed upper bound', corrupted => { corrupted.assessment.metrics['1/normal/random'].turns.max += 1; }, /Metric target bounds mismatch/],
    ['changed metric status', corrupted => { corrupted.assessment.metrics['1/normal/random'].turns.status = 'pass'; }, /Metric status mismatch/],
    ['changed failure count', corrupted => { corrupted.assessment.failedMetrics = 0; }, /Failed metric count mismatch/],
    ['changed overall result', corrupted => { corrupted.assessment.overall = 'not_certified'; }, /Assessment overall mismatch/],
  ];
  for (const [name, tamper, error] of cases) await t.test(name, () => {
    const corrupted = structuredClone(report);
    tamper(corrupted);
    assert.deepEqual(corrupted.hashes, report.hashes);
    assert.throws(() => analyze(corrupted, base), error);
  });
});

test('official CLI requires complete canonical probe provenance while analyze supports smaller probes', async t => {
  const dir = mkdtempSync(join(tmpdir(), 'mf-balance-causes-'));
  let invocation = 0;
  const invoke = report => {
    const input = join(dir, `report-${invocation}.json`), output = join(dir, `causes-${invocation++}.json`);
    writeFileSync(input, JSON.stringify(report));
    const result = spawnSync(process.execPath, [fileURLToPath(new URL('../../tools/review/balance_causes.mjs', import.meta.url)), input, output],
      { encoding: 'utf8' });
    return { ...result, output };
  };
  const replayChangedScenario = (report, scenario) => {
    scenario.setupHash = hash(scenario.setup);
    const battle = MF.Battle.create(base, structuredClone(scenario.setup));
    battle.do('begin');
    scenario.initialStateHash = hash(battle.s);
    report.trials = report.trials.map(trial => trial.scenarioKey === scenario.key
      ? { zone: trial.zone, tier: trial.tier, encounter: trial.encounter,
        ...fight(base, scenario, trial.policy, { maxTurns: report.method.maxTurns }) } : trial);
    for (const key of Object.keys(report.byGroup)) report.byGroup[key] = summarize(report.trials.filter(trial =>
      `${trial.zone}/${trial.tier}/${trial.encounter}/${trial.policy}` === key));
    for (const key of Object.keys(report.byZoneTierPolicy)) report.byZoneTierPolicy[key] = summarize(report.trials.filter(trial =>
      `${trial.zone}/${trial.tier}/${trial.policy}` === key));
    report.assessment = assessTargets(report);
  };
  try {
    const report = runProbe({ requestedN: 1 });
    const late = runProbe({ requestedN: 1, late: true });
    for (const [name, source, groups, trials] of [['base', report, 21, 630], ['candidate', late, 44, 1320]])
      await t.test(`accepts complete ${name} probe`, () => {
        assert.equal(source.method.groups, groups);
        const result = invoke(source);
        assert.equal(result.status, 0, result.stderr);
        const output = JSON.parse(readFileSync(result.output, 'utf8'));
        assert.equal(output.method.trials, trials);
        assert.equal(output.failedMetrics, source.assessment.failedMetrics);
      });
    const subset = runProbe({ requestedN: 1, groups: group });
    assert.equal(analyze(subset, base).method.trials, 30);
    const changedEnemies = structuredClone(report);
    changedEnemies.scenarios[0].setup.enemies = ['b_notary'];
    replayChangedScenario(changedEnemies, changedEnemies.scenarios[0]);
    const changedSeed = structuredClone(report);
    const first = changedSeed.scenarios[0];
    changedSeed.scenarios[0] = makeScenario(base, { zone: first.zone, tier: first.tier, enc: first.encounter }, first.repeat, 778);
    replayChangedScenario(changedSeed, changedSeed.scenarios[0]);
    for (const changed of [changedEnemies, changedSeed]) {
      assert.deepEqual(changed.hashes, report.hashes);
      assert.equal(analyze(changed, base).method.trials, report.method.trials);
    }
    const cases = [
      ['base encounter subset', subset, /Official probe method mismatch/],
      ['candidate encounter subset', runProbe({ requestedN: 1, late: true, groups: group }), /Official probe method mismatch/],
      ['mislabeled actual enemies', changedEnemies, /Official probe scenarios mismatch/],
      ['different scenario generation seed', changedSeed, /Official probe scenarios mismatch/],
      ['different turn limit', runProbe({ requestedN: 1, maxTurns: 1 }), /Official probe method mismatch/],
      ['oversized requestedN', { ...report, method: { ...report.method, requestedN: Number.MAX_SAFE_INTEGER } }, /Official probe size exceeds supplied trials/],
      ...[['scenarioSeed', 778], ['actionCap', 11], ['groups', 1], ['repsPerGroup', 1]].map(([field, value]) => {
        const changed = structuredClone(report);
        changed.method[field] = value;
        return [`forged method ${field}`, changed, /Official probe method mismatch/];
      }),
      ...[['baseContent', report], ['candidate', late]].map(([field, source]) => {
        const changed = structuredClone(source);
        changed.hashes[field] = 'forged';
        return [`forged ${field} source hash`, changed, /Report source hashes are stale/];
      }),
      ['unknown report mode', { ...report, mode: 'unknown' }, /Unknown report mode/],
    ];
    for (const [name, changed, error] of cases) await t.test(`rejects ${name}`, () => {
      const result = invoke(changed);
      assert.equal(result.status, 1, result.stderr);
      assert.match(result.stderr, error);
      assert.equal(existsSync(result.output), false);
    });
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

test('card reflection attribution uses actual HP loss and restores methods', () => {
  const scenario = makeScenario(base, { zone: '1', tier: 'boss', enc: ['b_notary'] }, 0);
  const before = MF.Battle.prototype.hitEnemy;
  const { trial, trace } = traceFight(base, scenario, 'random');
  assert.ok(trace.reflectionByAction.card > 0);
  assert.equal(trace.reflectionByAction.card + trace.reflectionByAction.ultimate, trace.lossBySource.reflect);
  assert.equal(trace.grossLoss - trace.healing, trial.hpLoss);
  assert.equal(MF.Battle.prototype.hitEnemy, before);
});

test('phase counts both bars and six candidate normal encounters miss the preserved HP budget', () => {
  const budgets = encounterBudgets(candidate.withLateEnemies(base));
  assert.equal(budgets.length, 44);
  assert.deepEqual(budgets.filter(b => b.status !== 'pass').map(b => b.encounter).sort(), [
    'e_ghostguest+e_ghostguest+e_ghostguest',
    'e_ratpack+e_ratpack+e_ratpack',
    'e_rustgear+e_rustgear+e_rustgear',
    'e_scaleknight',
    'e_springguard+e_rustgear',
    'e_stagerg+e_prompter',
  ].sort());
  assert.equal(budgets.find(b => b.encounter === 'b_nameless').hp, 340);
});
