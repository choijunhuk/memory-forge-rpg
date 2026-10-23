// Verify the immutable new-expedition preset against unchanged paired battle targets.
// Run: node tools/review/expedition_balance.mjs
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { assessTargets, fight, hash, runProbe, summarize } from '../../engine/tests/balance_probe.mjs';

const require = createRequire(import.meta.url);
const MF = require('../../engine/rules-core.js');
const read = path => readFileSync(new URL('../../' + path, import.meta.url));
const policyPath = 'server/MemoryForge.Server/Policies/expedition-balance-20261023-v1.json';
const policyBytes = read(policyPath), policy = JSON.parse(policyBytes);
const dataBytes = read('data/game_data.json'), data = JSON.parse(dataBytes);
assert.equal(policy.version, 'expedition-balance-20261023-v1');
assert.equal(hash(policyBytes), 'da8a49e5a31a46e303ec3456b078d8c7c76ce8072e467f43ca847cc293f729d4');
assert.equal(hash(dataBytes), policy.mainContentSha256, 'calibration must use the registered unchanged content');
assert.deepEqual(policy.modes, ['story']);
assert.deepEqual(policy.difficulty, { hp: 130, dmg: 110 });
assert.deepEqual([data.rules.gaugeMax, data.rules.gaugePerCard, data.rules.gaugeTurnCap], [100, 20, 50]);
assert.deepEqual(Object.keys(data.encounters), ['1', '2'], 'a later content epoch needs its own calibration');

function aggregate(trials, scenarios) {
  const byScenario = new Map(scenarios.map(s => [s.key, s]));
  const groups = {};
  for (const trial of trials) {
    const s = byScenario.get(trial.scenarioKey);
    assert.ok(s, 'every trial belongs to a recorded starting scenario');
    (groups[`${s.zone}/${s.tier}/${trial.policy}`] ??= []).push(trial);
  }
  const byZoneTierPolicy = Object.fromEntries(Object.entries(groups).map(([key, values]) => [key, summarize(values)]));
  return { trials: trials.length, byZoneTierPolicy, assessment: assessTargets({ byZoneTierPolicy }) };
}

function compare(baseline, selected) {
  const scenarios = baseline.scenarios.filter(selected);
  const keys = new Set(scenarios.map(s => s.key));
  assert.equal(keys.size, scenarios.length, 'scenario keys must be unique');
  const before = baseline.trials.filter(t => keys.has(t.scenarioKey));
  const after = [], randomStrategyStalls = [];
  for (const old of scenarios) {
    const scenario = structuredClone(old);
    scenario.setup.difficulty = { ...policy.difficulty };
    scenario.setupHash = hash(scenario.setup);
    const battle = MF.Battle.create(dataFor(baseline), structuredClone(scenario.setup));
    battle.do('begin');
    scenario.initialStateHash = hash(battle.s);
    for (const bot of baseline.method.policies) {
      const trial = fight(dataFor(baseline), scenario, bot);
      after.push(trial);
      if (trial.outcome === 'timeout') {
        // Random can exhaust every attack; prove a winning line from the identical starting state.
        assert.equal(bot, 'random', 'a directed policy cannot stall');
        const recovery = fight(dataFor(baseline), scenario, 'greedy');
        assert.equal(recovery.outcome, 'win', 'a random stall needs a reproducible winning strategy');
        randomStrategyStalls.push({ scenarioKey: scenario.key, seed: scenario.setup.seed,
          party: scenario.setup.party.map(p => p.charId), stalled: trial, winningStrategy: recovery });
      }
    }
  }
  assert.equal(after.length, before.length);
  for (let i = 0; i < before.length; i++) {
    assert.equal(after[i].scenarioKey, before[i].scenarioKey);
    assert.equal(after[i].policySeed, before[i].policySeed);
    assert.equal(after[i].policy, before[i].policy);
  }
  return { scenarios: scenarios.length, before: aggregate(before, scenarios), after: aggregate(after, scenarios), randomStrategyStalls };
}

const lateData = require('../../proposals/late_enemies.cjs').withLateEnemies(data);
function dataFor(report) { return report.mode === 'candidate overlay, not live data' ? lateData : data; }
function requireBands(result, label) {
  assert.equal(result.after.assessment.failedMetrics, 0, label + ' must meet the existing targets');
  const timeouts = Object.values(result.after.byZoneTierPolicy).reduce((n, s) => n + s.timeouts, 0);
  assert.equal(timeouts, result.randomStrategyStalls.length, label + ' must explain every stalled strategy');
}

const baseline = runProbe({ requestedN: 1200 });
const training = compare(baseline, s => s.repeat < 14);
const heldOut = compare(baseline, s => s.repeat >= 14);
assert.equal(training.after.trials, 882);
assert.equal(heldOut.after.trials, 2709);
requireBands(training, 'training');
requireBands(heldOut, 'held-out repeats');

// Zones 3–5 are a proposal overlay. Only live zones receive this preset in the comparison.
const lateBaseline = runProbe({ requestedN: 350, late: true });
const lateLive = compare(lateBaseline, s => s.zone <= 2);
// Its ten repeats are a smaller subset of training, not independent validation; retain every deviation as informational evidence.
const lateProposal = aggregate(lateBaseline.trials.filter(t => {
  const scenario = lateBaseline.scenarios.find(s => s.key === t.scenarioKey);
  return scenario.zone >= 3;
}), lateBaseline.scenarios.filter(s => s.zone >= 3));

const report = {
  mode: 'new story expedition preset; historical launches and non-story content unchanged',
  policy: { path: policyPath, sha256: hash(policyBytes), ...policy },
  hashes: { content: hash(dataBytes), engine: hash(read('engine/rules-core.js')), probe: hash(read('engine/tests/balance_probe.mjs')) },
  method: { scenarioSeed: 777, baselineRequestedN: 1200, trainingRepeats: '0–13', heldOutRepeats: '14–56',
    policies: baseline.method.policies, independentPolicies: ['random', 'greedy'],
    note: 'planner is a legacy alias of greedy under now-only cards; random defeats/timeouts remain in every metric denominator; passing bands do not certify human balance' },
  training, heldOut, lateLive, unchangedLateProposal: lateProposal,
};
const output = new URL('../../reports/art_balance_phase2_20261023/balance-validation.json', import.meta.url);
writeFileSync(output, JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify({ training: training.after.assessment.failedMetrics, heldOut: heldOut.after.assessment.failedMetrics,
  lateLive: lateLive.after.assessment.failedMetrics, unchangedLateProposal: lateProposal.assessment.failedMetrics,
  report: fileURLToPath(output) }));
