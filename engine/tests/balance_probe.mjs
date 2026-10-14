// Paired reference-battle probe. Usage: node engine/tests/balance_probe.mjs [N=300] [detail] [--late] [--out file.json]
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
const here = dirname(fileURLToPath(import.meta.url));
const enginePath = join(here, '..', 'rules-core.js');
const dataPath = join(here, '..', '..', 'data', 'game_data.json');
const MF = require(enginePath);
const baseData = JSON.parse(readFileSync(dataPath, 'utf8'));
const policies = ['random', 'greedy', 'planner'];
const turnGuard = 60;
const actionCap = 12;
const basicWeapon = { ranged: 'w_basic_ranged', melee: 'w_basic_melee', device: 'w_basic_device', ritual: 'w_basic_ritual' };
const expectedIllegalPlay = new Set([
  'AP_LOW', 'SLOT_FULL', 'BAD_TARGET', 'FIXED_MARK_REQUIRED', 'DEBT_CAP', 'LOAN_OVERFLOW',
  'NOT_DEFERRABLE', 'RESTRAINT_UNAVAILABLE', 'NO_EMPTY_SLOT', 'RESERVATION_REQUIRED',
  'PULL_DESTINATION_FULL', 'OVERLOAD_WOULD_KILL',
]);

export const hash = (value) => createHash('sha256').update(Buffer.isBuffer(value) || typeof value === 'string' ? value : JSON.stringify(value)).digest('hex');
export const quantile = (values, fraction) => {
  if (!values.length) return null;
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.ceil(fraction * sorted.length) - 1];
};
function rng(seed) {
  let state = seed >>> 0;
  return () => { state = (Math.imul(state, 1664525) + 1013904223) >>> 0; return state / 0x100000000; };
}
const pick = (items, random) => items[Math.floor(random() * items.length)];
const seedFrom = (value) => parseInt(hash(value).slice(0, 8), 16);

function deckFor(data, party) {
  const deck = [];
  for (const p of party) {
    for (const card of data.cards.filter((c) => c.owner === p.charId && c.start)) deck.push({ id: card.id });
    deck.push({ id: data.startingCommon[p.charId] });
    deck.push({ id: data.weapons.find((w) => w.id === p.weaponId).card, wielder: p.charId });
  }
  return deck;
}

export function makeScenario(data, group, repeat, scenarioSeed = 777) {
  const key = `${group.zone}/${group.tier}/${group.enc.join('+')}/${repeat}`;
  const random = rng(seedFrom(`${scenarioSeed}:party:${key}`));
  const pool = data.characters.map((c) => c.id);
  const party = [];
  while (party.length < 3) {
    const charId = pool.splice(Math.floor(random() * pool.length), 1)[0];
    const type = data.characters.find((c) => c.id === charId).weapon;
    party.push({ charId, res: 0, power4: 160, hp: 81, weaponId: basicWeapon[type], refine: 1 });
  }
  const setup = {
    party, deck: deckFor(data, party), relics: [], hp: 243, maxHp: 243,
    seed: seedFrom(`${scenarioSeed}:battle:${key}`), difficulty: { hp: 100, dmg: 100 }, enemies: [...group.enc],
  };
  const battle = MF.Battle.create(data, structuredClone(setup));
  battle.do('begin');
  return { key, zone: group.zone, tier: group.tier, encounter: [...group.enc], repeat, setup,
    setupHash: hash(setup), initialStateHash: hash(battle.s) };
}

function legalPlays(battle) {
  const alive = battle.alive();
  const choices = [];
  for (const uid of battle.s.zones.hand) for (const mode of battle.allowedModes(uid)) {
    const target = battle.needsTarget(uid) === 'fixed'
      ? alive.find((e) => e.st.mark > 0)?.uid
      : alive.slice().sort((a, b) => a.hp - b.hp)[0]?.uid;
    const error = battle.check(uid, mode, target, []);
    if (error === null) choices.push({ uid, mode, target, cost: battle.costOf(uid, mode) });
    else if (!expectedIllegalPlay.has(error)) throw new Error(`Unexpected play precheck: ${error}`);
  }
  return choices;
}

export function fight(data, scenario, policy, { maxTurns = turnGuard } = {}) {
  if (!policies.includes(policy)) throw new Error(`Unknown policy: ${policy}`);
  const setup = structuredClone(scenario.setup);
  const setupHash = hash(setup);
  const battle = MF.Battle.create(data, setup);
  const policySeed = seedFrom(`${scenario.key}:policy:${policy}`);
  const random = rng(policySeed);
  battle.do('begin');
  const initialStateHash = hash(battle.s);
  if (setupHash !== scenario.setupHash || initialStateHash !== scenario.initialStateHash)
    throw new Error(`Scenario starting state changed: ${scenario.key}`);
  const hp0 = battle.s.hp;
  let processedPlayerTurns = 0;
  for (let turns = 0; turns < maxTurns && battle.s.phase === 'PLAYER'; turns++) {
    processedPlayerTurns++;
    for (const actor of battle.s.actors) {
      if (battle.s.phase !== 'PLAYER') break;
      if (actor.gauge >= battle.ix.rules.gaugeMax) battle.do('ult', { actor: actor.id, target: battle.alive()[0]?.uid });
    }
    for (let actions = 0; actions < actionCap && battle.s.phase === 'PLAYER'; actions++) {
      const choices = legalPlays(battle);
      if (!choices.length) break;
      let choice;
      if (policy === 'random') choice = pick(choices, random);
      else {
        const immediate = choices.filter((c) => c.mode === 'now').sort((a, b) => a.cost - b.cost);
        choice = immediate[0] || choices.sort((a, b) => a.cost - b.cost)[0];
        if (policy === 'planner') {
          const enemyHp = battle.alive().reduce((sum, enemy) => sum + enemy.hp, 0);
          const reserve = choices.find((c) => {
            const effects = battle.effDef(battle.s.cards[c.uid]).eff;
            return c.mode === 'next' && enemyHp > 40 && /"(?:rv|ra)"/.test(JSON.stringify(effects));
          });
          if (reserve) choice = reserve;
        }
      }
      battle.do('play', choice);
    }
    if (battle.s.phase === 'PLAYER') battle.do('end');
  }
  const outcome = battle.s.phase === 'VICTORY' ? 'win' : battle.s.phase === 'DEFEAT' ? 'defeat' : 'timeout';
  const hpLoss = hp0 - battle.s.hp;
  return { scenarioKey: scenario.key, setupHash, initialStateHash, seed: setup.seed, policySeed, policy, outcome,
    turns: battle.s.turn, processedPlayerTurns, hpLoss, hpLossPct: 100 * hpLoss / setup.maxHp };
}

export function summarize(trials) {
  const attempts = trials.length;
  return {
    attempts, wins: trials.filter((t) => t.outcome === 'win').length,
    defeats: trials.filter((t) => t.outcome === 'defeat').length,
    timeouts: trials.filter((t) => t.outcome === 'timeout').length,
    denominator: 'all attempts, including defeats and timeouts',
    turns: { mean: attempts ? trials.reduce((sum, t) => sum + t.turns, 0) / attempts : null,
      p50: quantile(trials.map((t) => t.turns), 0.5), p90: quantile(trials.map((t) => t.turns), 0.9) },
    hpLossPct: { mean: attempts ? trials.reduce((sum, t) => sum + t.hpLossPct, 0) / attempts : null,
      p50: quantile(trials.map((t) => t.hpLossPct), 0.5), p90: quantile(trials.map((t) => t.hpLossPct), 0.9) },
  };
}

const referenceTargets = {
  1: {
    normal: { turns: [3, 5], hpLossPct: [4, 12], randomWinPct: 98 },
    elite: { turns: [5, 7], hpLossPct: [12, 22], randomWinPct: 95 },
    boss: { turns: [9, 12], hpLossPct: [25, 40], randomWinPct: 85 },
  },
  2: {
    normal: { turns: [4, 6], hpLossPct: [8, 15], randomWinPct: 95 },
    elite: { turns: [6, 8], hpLossPct: [18, 28], randomWinPct: 90 },
    boss: { turns: [14, 18], hpLossPct: [30, 50], randomWinPct: 80 },
  },
};

export function assessTargets(report) {
  const metrics = {};
  let failedMetrics = 0;
  const band = (value, min, max) => {
    const status = value < min ? 'below' : value > max ? 'above' : 'pass';
    if (status !== 'pass') failedMetrics++;
    return { value, min, max, status };
  };
  for (const [key, stats] of Object.entries(report.byZoneTierPolicy)) {
    const [zoneText, tier, policy] = key.split('/');
    const zone = Number(zoneText);
    const base = referenceTargets[zone <= 2 ? zone : 2]?.[tier];
    if (!base || zone < 1 || zone > 5) throw new Error(`No documented balance target: ${key}`);
    const late = zone >= 3;
    metrics[key] = {
      turns: band(stats.turns.mean, base.turns[0], late ? base.turns[1] * 1.1 : base.turns[1]),
      hpLossPct: band(stats.hpLossPct.mean, base.hpLossPct[0], late ? base.hpLossPct[1] + 5 : base.hpLossPct[1]),
    };
    if (policy === 'random') metrics[key].winPct = band(100 * stats.wins / stats.attempts, base.randomWinPct, 100);
  }
  return {
    overall: failedMetrics ? 'failed' : 'not_certified',
    failedMetrics,
    interpretation: 'zones 3-5 use zone 2 lower bounds unchanged and only upper bounds of turns +10% and HP loss +5 percentage points (docs §20.9); win threshold applies only to random',
    note: 'Automated bot targets are informational; passing bands do not certify balance or human play.',
    metrics,
  };
}

export function runProbe({ requestedN = 300, late = false, data = baseData, groups, maxTurns = turnGuard } = {}) {
  if (!Number.isSafeInteger(requestedN) || requestedN <= 0) throw new Error('N must be a positive integer');
  let candidate = null;
  if (late) {
    candidate = require(join(here, '..', '..', 'proposals', 'late_enemies.cjs'));
    data = candidate.withLateEnemies(data);
  }
  groups ??= Object.entries(data.encounters).flatMap(([zone, tiers]) =>
    ['normal', 'elite', 'boss'].flatMap((tier) => (tiers[tier] || []).map((enc) => ({ zone, tier, enc }))));
  if (!groups.length) throw new Error('No encounter groups');
  const repsPerGroup = Math.max(10, Math.floor(requestedN / groups.length));
  const scenarios = [], trials = [];
  for (const group of groups) for (let repeat = 0; repeat < repsPerGroup; repeat++) {
    const scenario = makeScenario(data, group, repeat);
    scenarios.push(scenario);
    for (const policy of policies) trials.push({ zone: group.zone, tier: group.tier, encounter: group.enc.join('+'),
      ...fight(data, scenario, policy, { maxTurns }) });
  }
  const byGroup = {};
  for (const group of groups) for (const policy of policies) {
    const key = `${group.zone}/${group.tier}/${group.enc.join('+')}/${policy}`;
    byGroup[key] = summarize(trials.filter((t) => t.zone === group.zone && t.tier === group.tier &&
      t.encounter === group.enc.join('+') && t.policy === policy));
  }
  const byZoneTierPolicy = {};
  for (const trial of trials) {
    const key = `${trial.zone}/${trial.tier}/${trial.policy}`;
    (byZoneTierPolicy[key] ??= []).push(trial);
  }
  for (const [key, entries] of Object.entries(byZoneTierPolicy)) byZoneTierPolicy[key] = summarize(entries);
  const report = {
    mode: late ? 'candidate overlay, not live data' : 'base live data',
    hashes: { baseContent: hash(readFileSync(dataPath)), content: hash(data),
      candidate: candidate ? hash({ enemies: candidate.enemies, encounters: candidate.encounters }) : null,
      engine: hash(readFileSync(enginePath)), runner: hash(readFileSync(fileURLToPath(import.meta.url))) },
    provenance: candidate?.provenance || null,
    method: { requestedN, groups: groups.length, repsPerGroup, scenarios: scenarios.length, policies,
      trials: trials.length, scenarioSeed: 777, maxTurns, actionCap,
      reference: 'power4 160, HP 81 per actor, resonance 0, basic R1 weapons, no relics/gear/upgrades, normal difficulty',
      denominator: 'all outcomes', quantile: 'nearest rank',
      turnsMetric: 'battle state turn; may be maxTurns + 1 after the last end transition',
      processedPlayerTurns: 'number of player-turn loops executed, bounded by maxTurns' },
    scenarios, trials, byGroup, byZoneTierPolicy,
  };
  report.assessment = assessTargets(report);
  return report;
}

export function parseArgs(args) {
  let requestedN = 300, seenN = false, detail = false, late = false, out = null;
  for (let i = 0; i < args.length; i++) {
    const arg = args[i];
    if (arg === 'detail') detail = true;
    else if (arg === '--late') late = true;
    else if (arg === '--out') { out = args[++i]; if (!out || out.startsWith('--')) throw new Error('--out requires a JSON path'); }
    else if (!seenN && /^\d+$/.test(arg)) { requestedN = Number(arg); seenN = true; }
    else throw new Error(`Invalid argument: ${arg}`);
  }
  if (!Number.isSafeInteger(requestedN) || requestedN <= 0) throw new Error('N must be a positive integer');
  return { requestedN, detail, late, out };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const { requestedN, detail, late, out } = parseArgs(process.argv.slice(2));
    const report = runProbe({ requestedN, late });
    if (out) writeFileSync(out, `${JSON.stringify(report, null, 2)}\n`);
    console.log(JSON.stringify({ mode: report.mode, ...report.method, hashes: report.hashes }));
    console.log(JSON.stringify({ assessment: report.assessment.overall, failedMetrics: report.assessment.failedMetrics,
      interpretation: report.assessment.interpretation }));
    for (const [key, stats] of Object.entries(report.byZoneTierPolicy))
      console.log(key.padEnd(22), 'turns', stats.turns.mean.toFixed(1).padStart(5),
        'hpLost%', stats.hpLossPct.mean.toFixed(1).padStart(5),
        'wins', `${stats.wins}/${stats.attempts}`, 'defeats', stats.defeats, 'timeouts', stats.timeouts);
    if (detail) for (const trial of report.trials) console.log(JSON.stringify(trial));
  } catch (error) { console.error(error); process.exitCode = 1; }
}
