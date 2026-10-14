// Replays the paired probe without changing its setup or policies.
// Usage: node tools/review/balance_causes.mjs input-report.json output-causes.json
import { readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { isDeepStrictEqual } from 'node:util';
import { assessTargets, fight, runProbe } from '../../engine/tests/balance_probe.mjs';

const require = createRequire(import.meta.url);
const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const MF = require(join(root, 'engine/rules-core.js'));
const candidate = require(join(root, 'proposals/late_enemies.cjs'));
const near = (a, b) => Number.isFinite(a) && Number.isFinite(b) && Math.abs(a - b) < 1e-9;
const assert = (ok, message) => { if (!ok) throw new Error(message); };
const add = (record, key, n = 1) => { record[key] = (record[key] || 0) + n; };
const probePolicies = ['random', 'greedy', 'planner'];

export function traceFight(data, scenario, policy, options) {
  const proto = MF.Battle.prototype;
  const original = Object.fromEntries(['command', 'loseHp', 'healParty', 'enemyHit', 'hitEnemy'].map(k => [k, proto[k]]));
  const trace = { grossLoss: 0, healing: 0, lossBySource: {}, lossByEnemy: {}, reflectionByAction: { card: 0, ultimate: 0 }, events: {}, eventAmounts: {}, plays: {} };
  let recording = false, currentEnemy = null, currentAction = null;
  try {
    for (const name of ['enemyHit', 'hitEnemy']) proto[name] = function (...args) {
      const before = currentEnemy, beforeAction = currentAction;
      currentEnemy = args[0]?.id || null;
      if (name === 'hitEnemy') currentAction = args[3]?.isUlt ? 'ultimate' : 'card';
      try { return original[name].apply(this, args); }
      finally { currentEnemy = before; currentAction = beforeAction; }
    };
    proto.loseHp = function (...args) {
      const before = this.s.hp;
      const result = original.loseHp.apply(this, args);
      if (recording) {
        const actual = before - this.s.hp;
        trace.grossLoss += actual;
        add(trace.lossBySource, args[1] || 'unknown', actual);
        if (currentEnemy) add(trace.lossByEnemy, currentEnemy, actual);
        if (args[1] === 'reflect' && currentAction) add(trace.reflectionByAction, currentAction, actual);
      }
      return result;
    };
    proto.healParty = function (...args) {
      const before = this.s.hp;
      const result = original.healParty.apply(this, args);
      if (recording) trace.healing += this.s.hp - before;
      return result;
    };
    proto.command = function (request) {
      const result = original.command.call(this, request);
      if (request.kind === 'begin') recording = true;
      else if (recording) {
        if (request.kind === 'play') add(trace.plays, request.args?.mode || 'now');
        for (const event of result.ev) {
          add(trace.events, event.e);
          if (['apDrain', 'sealSlot', 'delayRes', 'silence', 'eatName', 'phase', 'break', 'stunSkip'].includes(event.e)) {
            if (event.n) add(trace.eventAmounts, event.e, event.n);
            if (event.fallback) add(trace.eventAmounts, 'sealFallback');
            if (event.discarded) add(trace.eventAmounts, 'delayDiscarded');
          }
        }
      }
      return result;
    };
    const trial = fight(data, scenario, policy, options);
    assert(near(trace.grossLoss - trace.healing, trial.hpLoss), `HP accounting mismatch: ${scenario.key}/${policy}`);
    return { trial, trace };
  } finally {
    for (const [name, method] of Object.entries(original)) proto[name] = method;
  }
}

// Encounter total HP bands are from docs/source_v0.8.1/10_chapter.md §10.3.
// A phase adds its second HP bar; starting summons are not guessed or simulated here.
const hpBands = {
  1: { normal: [22, 48], elite: [52, 67], boss: [125, 140] },
  2: { normal: [46, 68], elite: [80, 95], boss: [230, 230] },
  3: { normal: [60, 85], elite: [105, 120], boss: [260, 260] },
  4: { normal: [70, 95], elite: [120, 135], boss: [290, 290] },
  5: { normal: [80, 105], elite: [140, 140], boss: [340, 340] },
};

export function encounterBudgets(data) {
  const defs = Object.fromEntries(data.enemies.map(e => [e.id, e]));
  return Object.entries(data.encounters).flatMap(([zone, tiers]) =>
    Object.entries(tiers).flatMap(([tier, groups]) => groups.map(encounter => {
      const [min, max] = hpBands[zone]?.[tier] || [];
      assert(Number.isFinite(min), `Unknown HP budget: ${zone}/${tier}`);
      const hp = encounter.reduce((sum, id) => {
        const enemy = defs[id];
        assert(enemy, `Unknown enemy: ${id}`);
        return sum + enemy.hp + (enemy.phase?.hp || 0);
      }, 0);
      return { zone: Number(zone), tier, encounter: encounter.join('+'), hp, min, max,
        status: hp < min ? 'below' : hp > max ? 'above' : 'pass' };
    })));
}

function verifyAggregate(expected, entries, key) {
  assert(expected && expected.attempts === entries.length, `Attempt count mismatch: ${key}`);
  for (const [field, outcome] of [['wins', 'win'], ['defeats', 'defeat'], ['timeouts', 'timeout']])
    assert(expected[field] === entries.filter(t => t.outcome === outcome).length, `${field} mismatch: ${key}`);
  for (const [field, source] of [['turns', 'turns'], ['hpLossPct', 'hpLossPct']])
    assert(near(expected[field].mean, entries.reduce((sum, t) => sum + t[source], 0) / entries.length),
      `${field} mean mismatch: ${key}`);
}

function groupTrace(rows) {
  const totals = { grossLoss: 0, healing: 0, lossBySource: {}, lossByEnemy: {}, reflectionByAction: { card: 0, ultimate: 0 }, events: {}, eventAmounts: {}, plays: {} };
  for (const { trace } of rows) {
    totals.grossLoss += trace.grossLoss;
    totals.healing += trace.healing;
    for (const field of ['lossBySource', 'lossByEnemy', 'reflectionByAction', 'events', 'eventAmounts', 'plays'])
      for (const [key, value] of Object.entries(trace[field])) add(totals[field], key, value);
  }
  return totals;
}

export function analyze(report, data) {
  assert(report?.scenarios?.length && report?.trials?.length, 'Missing scenarios or trials');
  assert(report.method.trials === report.trials.length && report.method.scenarios === report.scenarios.length,
    'Report count mismatch');
  assert(report.method.policies.join(',') === probePolicies.join(','), 'Unexpected policy set');
  const scenarios = new Map(report.scenarios.map(s => [s.key, s]));
  assert(scenarios.size === report.scenarios.length, 'Duplicate scenarios');
  const seen = new Set(), grouped = new Map(), zones = new Map();
  for (const expected of report.trials) {
    const scenario = scenarios.get(expected.scenarioKey);
    assert(scenario, `Missing scenario: ${expected.scenarioKey}`);
    assert(expected.zone === scenario.zone && expected.tier === scenario.tier &&
      expected.encounter === scenario.encounter.join('+'), `Trial identity mismatch: ${expected.scenarioKey}`);
    const identity = `${scenario.key}/${expected.policy}`;
    assert(!seen.has(identity), `Duplicate trial: ${identity}`);
    seen.add(identity);
    const actual = traceFight(data, scenario, expected.policy, { maxTurns: report.method.maxTurns });
    assert(JSON.stringify(actual.trial) === JSON.stringify(Object.fromEntries(Object.entries(expected).filter(([k]) =>
      !['zone', 'tier', 'encounter'].includes(k)))), `Replay mismatch: ${identity}`);
    const groupKey = `${scenario.zone}/${scenario.tier}/${scenario.encounter.join('+')}/${expected.policy}`;
    const zoneKey = `${scenario.zone}/${scenario.tier}/${expected.policy}`;
    if (!grouped.has(groupKey)) grouped.set(groupKey, []);
    grouped.get(groupKey).push(actual);
    if (!zones.has(zoneKey)) zones.set(zoneKey, []);
    zones.get(zoneKey).push(actual);
  }
  assert(seen.size === scenarios.size * probePolicies.length, 'Missing scenario-policy trials');
  assert(grouped.size === Object.keys(report.byGroup).length && zones.size === Object.keys(report.byZoneTierPolicy).length,
    'Aggregate key count mismatch');
  for (const [key, rows] of grouped) verifyAggregate(report.byGroup[key], rows.map(r => r.trial), key);
  for (const [key, rows] of zones) verifyAggregate(report.byZoneTierPolicy[key], rows.map(r => r.trial), key);
  assert(Object.keys(report.assessment.metrics).length === zones.size, 'Assessment metric row count mismatch');
  const targets = assessTargets(report);
  const failed = [];
  for (const [key, metrics] of Object.entries(report.assessment.metrics)) {
    const rows = zones.get(key);
    assert(rows, `Missing metric group: ${key}`);
    const expectedNames = key.endsWith('/random') ? ['hpLossPct', 'turns', 'winPct'] : ['hpLossPct', 'turns'];
    assert(Object.keys(metrics).sort().join(',') === expectedNames.join(','), `Missing or extra metrics: ${key}`);
    const stats = report.byZoneTierPolicy[key];
    for (const [name, metric] of Object.entries(metrics)) {
      const target = targets.metrics[key][name];
      assert(metric.min === target.min && metric.max === target.max, `Metric target bounds mismatch: ${key}/${name}`);
      const value = name === 'winPct' ? 100 * stats.wins / stats.attempts : stats[name]?.mean;
      assert(near(metric.value, value), `Metric value mismatch: ${key}/${name}`);
      const status = value < target.min ? 'below' : value > target.max ? 'above' : 'pass';
      assert(status === metric.status, `Metric status mismatch: ${key}/${name}`);
      if (status === 'pass') continue;
      const boundary = status === 'above' ? target.max : target.min;
      const contributions = [...grouped.entries()].filter(([group]) => group.startsWith(`${key.split('/').slice(0, 2).join('/')}/`) &&
        group.endsWith(`/${key.split('/')[2]}`)).map(([group, cases]) => {
        const s = report.byGroup[group];
        const mean = name === 'winPct' ? 100 * s.wins / s.attempts : s[name].mean;
        const weight = cases.length / rows.length;
        return { group, attempts: cases.length, mean, weight,
          signedContribution: weight * (status === 'above' ? mean - boundary : boundary - mean),
          traceTotals: groupTrace(cases) };
      }).sort((a, b) => b.signedContribution - a.signedContribution);
      const deviation = status === 'above' ? value - boundary : boundary - value;
      assert(near(contributions.reduce((sum, c) => sum + c.signedContribution, 0), deviation),
        `Contributor mismatch: ${key}/${name}`);
      const worst = rows.reduce((a, b) => {
        const av = name === 'winPct' ? (a.trial.outcome === 'win' ? 1 : 0) : a.trial[name];
        const bv = name === 'winPct' ? (b.trial.outcome === 'win' ? 1 : 0) : b.trial[name];
        return (status === 'above' ? bv > av : bv < av) ? b : a;
      });
      failed.push({ key, metric: name, status, value, boundary, deviation, contributions,
        representative: { scenarioKey: worst.trial.scenarioKey, policy: worst.trial.policy,
          outcome: worst.trial.outcome, turns: worst.trial.turns, hpLossPct: worst.trial.hpLossPct,
          trace: worst.trace } });
    }
  }
  assert(failed.length === report.assessment.failedMetrics, 'Failed metric count mismatch');
  assert(report.assessment.overall === targets.overall, 'Assessment overall mismatch');
  return { method: { scenarios: scenarios.size, trials: seen.size, replay: 'all trials; unchanged fight and policy',
      hpAccounting: 'actual post-begin loss minus actual post-begin healing',
      contributions: 'signed group weight times distance from target boundary; sums to metric deviation' },
    failedMetrics: failed.length, failed, encounterBudgets: encounterBudgets(data) };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const [, , input, output] = process.argv;
    assert(input && output, 'Usage: node tools/review/balance_causes.mjs input-report.json output-causes.json');
    const report = JSON.parse(readFileSync(input, 'utf8'));
    const base = JSON.parse(readFileSync(join(root, 'data/game_data.json'), 'utf8'));
    const late = report.mode === 'candidate overlay, not live data';
    assert(late || report.mode === 'base live data', 'Unknown report mode');
    const data = late ? candidate.withLateEnemies(base) : base;
    assert(Array.isArray(report.trials) && report.method?.requestedN <= report.trials.length,
      'Official probe size exceeds supplied trials');
    const canonical = runProbe({ requestedN: report.method?.requestedN, late });
    assert(isDeepStrictEqual(report.hashes, canonical.hashes), 'Report source hashes are stale');
    assert(isDeepStrictEqual(report.method, canonical.method), 'Official probe method mismatch');
    assert(isDeepStrictEqual(report.scenarios, canonical.scenarios), 'Official probe scenarios mismatch');
    const result = analyze(report, data);
    writeFileSync(output, `${JSON.stringify(result, null, 2)}\n`);
    console.log(JSON.stringify({ trials: result.method.trials, failedMetrics: result.failedMetrics,
      budgetMismatches: result.encounterBudgets.filter(b => b.status !== 'pass').length }));
  } catch (error) { console.error(error); process.exitCode = 1; }
}
