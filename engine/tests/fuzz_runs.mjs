// Seeded random-play fuzz: plays full runs with a random legal-move bot and checks invariants.
// Usage: node engine/tests/fuzz_runs.mjs [runs=200] [length=short|full]
import { createRequire } from 'node:module';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
const require = createRequire(import.meta.url);
const here = dirname(fileURLToPath(import.meta.url));
const MF = require(join(here, '..', 'rules-core.js'));
const data = JSON.parse(readFileSync(join(here, '..', '..', 'data', 'game_data.json'), 'utf8'));

const RUNS = Number(process.argv[2] || 200);
const LENGTH = process.argv[3] || 'short';
const chars = data.characters.map((c) => c.id);
let seedBase = 12345;
const rnd = () => { seedBase = (seedBase * 1103515245 + 12345) >>> 0; return seedBase / 4294967296; };
const pick = (a) => a[Math.floor(rnd() * a.length)];
const stats = { runs: 0, clears: 0, defeats: 0, battles: 0, turns: 0, errors: {}, commands: 0, unexpected: [] };
const EXPECTED = new Set(['AP_LOW', 'SLOT_FULL', 'BAD_TARGET', 'FIXED_MARK_REQUIRED', 'NOT_RESERVABLE', 'RESERVE_ONLY', 'DEBT_CAP', 'LOAN_OVERFLOW',
  'NOT_DEFERRABLE', 'RESTRAINT_UNAVAILABLE', 'NO_EMPTY_SLOT', 'RESERVATION_REQUIRED', 'PULL_DESTINATION_FULL', 'OVERLOAD_WOULD_KILL', 'ULT_NOT_READY',
  'COINS_LOW', 'WOULD_KILL', 'CANNOT_HEAL', 'BAD_UPGRADE']);

function playBattle(run) {
  let guard = 0;
  while (run.s.phase === 'BATTLE' && guard++ < 400) {
    const b = run.battle;
    if (b.s.phase !== 'PLAYER') break;
    stats.turns += 1;
    let actions = 0;
    for (let tries = 0; tries < 30 && run.s.phase === 'BATTLE' && b.s.phase === 'PLAYER'; tries++) {
      // ultimates
      for (const a of b.s.actors) if (a.gauge >= 100 && rnd() < 0.7) {
        const t = b.alive().length ? pick(b.alive()).uid : null;
        try { run.battleCmd('ult', { actor: a.id, target: t }); stats.commands++; } catch (e) { note(e); }
        if (run.s.phase !== 'BATTLE') return;
      }
      if (!b.s.zones.hand.length) break;
      const uid = pick(b.s.zones.hand);
      const modes = b.allowedModes(uid);
      const mode = pick(modes);
      const alive = b.alive();
      let target = alive.length ? pick(alive).uid : null;
      if (b.needsTarget(uid) === 'fixed') { const m = alive.filter((e) => e.st.mark > 0); target = m.length ? pick(m).uid : target; }
      const picks = [];
      if (rnd() < 0.5 && b.s.zones.hand.length > 1) picks.push(pick(b.s.zones.hand.filter((u) => u !== uid)));
      if (rnd() < 0.3 && b.s.res.length) picks.unshift(pick(b.s.res).uid);
      try { run.battleCmd('play', { uid, mode, target, picks }); stats.commands++; actions++; } catch (e) { note(e); }
      if (b.s.ap === 0 && rnd() < 0.8) break;
    }
    if (run.s.phase === 'BATTLE' && b.s.phase === 'PLAYER') { try { run.battleCmd('end'); stats.commands++; } catch (e) { note(e, true); return; } }
  }
}
function note(e, fatal) {
  const code = e.code || e.message;
  stats.errors[code] = (stats.errors[code] || 0) + 1;
  if (!EXPECTED.has(code) || fatal) stats.unexpected.push({ code, msg: e.message, stack: (e.stack || '').split('\n').slice(0, 4).join(' | ') });
}

for (let r = 0; r < RUNS; r++) {
  const party = [];
  const pool = [...chars];
  while (party.length < 3) { const c = pool.splice(Math.floor(rnd() * pool.length), 1)[0]; party.push({ charId: c, res: Math.floor(rnd() * 7), weaponId: null, refine: 1 }); }
  // random weapon of matching type, sometimes
  for (const p of party) if (rnd() < 0.7) { const t = data.characters.find((c) => c.id === p.charId).weapon; const ws = data.weapons.filter((w) => w.type === t); const w = pick(ws); p.weaponId = w.id; p.refine = 1 + Math.floor(rnd() * 5); }
  let run;
  try { run = MF.Run.create(data, { party, seed: 1000 + r, length: LENGTH }); } catch (e) { note(e, true); continue; }
  stats.runs += 1;
  let steps = 0;
  try {
    while (steps++ < 2000) {
      const s = run.s;
      if (s.phase === 'MAP') { const av = run.available(); if (!av.length) break; run.enter(pick(av)); continue; }
      if (s.phase === 'BATTLE') { stats.battles++; playBattle(run); if (run.s.phase === 'BATTLE') { stats.unexpected.push({ code: 'STUCK_BATTLE' }); break; } continue; }
      if (s.phase === 'REWARD') {
        const p = s.pending;
        for (const ev of [...(p.evolutions || [])]) if (rnd() < 0.8) run.evolve(ev.key, pick(ev.options));
        if (p.cards.length) { if (rnd() < 0.75) run.takeCard(pick(p.cards)); else run.skipCard(); }
        if (p.relic && p.relic.length) run.takeRelic(pick(p.relic));
        if (p.part) run.takePart();
        if (s.partsFound.length && rnd() < 0.5) { const d = s.deck.filter((c) => !c.weapon); run.applyFoundPart(s.partsFound[0], pick(d).key); }
        run.proceed(); continue;
      }
      if (s.phase === 'REST') { try { if (rnd() < 0.5) run.rest('heal'); else run.rest('upgrade', pick(s.deck.filter((c) => !c.plus && !c.weapon)).key); } catch (e) { note(e); } run.proceed(); continue; }
      if (s.phase === 'FORGE') {
        const deck = s.deck.filter((c) => !c.weapon);
        try { if (rnd() < 0.6) { const o = run.forgeOffer(pick(deck).key); run.forgeApply(pick(o.parts)); } else run.forgeRemove(pick(deck).key); } catch (e) { note(e); }
        run.proceed(); continue;
      }
      if (s.phase === 'SHOP') {
        const p = s.pending;
        for (const kind of ['cards', 'parts', 'relics']) for (const it of p[kind]) if (rnd() < 0.4) { try { run.buy(kind, it.id); } catch (e) { note(e); } }
        if (rnd() < 0.3 && p.removeCost) { try { run.buy('remove', null, pick(s.deck.filter((c) => !c.weapon)).key); } catch (e) { note(e); } }
        run.proceed(); continue;
      }
      if (s.phase === 'EVENT') {
        const ev = data.events.find((x) => x.id === s.pending.id);
        let done = false;
        for (let k = 0; k < 4 && !done; k++) { try { run.eventChoose(Math.floor(rnd() * ev.choices.length), pick(s.deck.filter((c) => !c.weapon)).key); done = true; } catch (e) { note(e); } }
        if (!done) run.eventChoose(ev.choices.length - 1);
        if (run.s.phase === 'FORGE') continue;
        run.proceed(); continue;
      }
      if (s.phase === 'ARCHIVE') { run.archive(rnd() < 0.7 ? 'continue' : 'return'); continue; }
      if (s.phase === 'VICTORY' || s.phase === 'DEFEAT') {
        if (s.phase === 'VICTORY') stats.clears++; else stats.defeats++;
        const ch = run.memoryChanges(); const sel = {};
        let keptOne = false;
        for (const [cid, info] of Object.entries(ch)) {
          sel[cid] = []; let used = 0;
          info.changes.forEach((c, i) => {
            if (s.pending.defeat) { if (!keptOne) { sel[cid].push(i); keptOne = true; } return; }
            if (used + c.cost <= info.capacity && rnd() < 0.8) { sel[cid].push(i); used += c.cost; }
          });
        }
        const mem = run.commitMemory(sel);
        // the saved memory must rebuild into a valid run
        MF.Run.create(data, { party, memory: mem, seed: 99 + r, length: 'short' });
        break;
      }
      if (s.phase === 'DONE') break;
    }
  } catch (e) { note(e, true); }
}
const top = Object.entries(stats.errors).sort((a, b) => b[1] - a[1]).slice(0, 12);
console.log(JSON.stringify({ runs: stats.runs, clears: stats.clears, defeats: stats.defeats, battles: stats.battles, turns: stats.turns, commands: stats.commands, rejectedTop: top, unexpected: stats.unexpected.length }, null, 0));
for (const u of stats.unexpected.slice(0, 8)) console.log('UNEXPECTED', JSON.stringify(u));
process.exit(stats.unexpected.length ? 1 : 0);
