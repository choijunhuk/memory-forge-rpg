// Balance probe: per-encounter turn counts and HP loss with two bots (random / greedy-immediate / planner).
// Usage: node engine/tests/balance_probe.mjs [battles=300]
import { createRequire } from 'node:module';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
const require = createRequire(import.meta.url);
const here = dirname(fileURLToPath(import.meta.url));
const MF = require(join(here, '..', 'rules-core.js'));
const data = JSON.parse(readFileSync(join(here, '..', '..', 'data', 'game_data.json'), 'utf8'));
const N = Number(process.argv[2] || 300);
let sd = 777; const rnd = () => { sd = (sd * 1103515245 + 12345) >>> 0; return sd / 4294967296; };
const pick = (a) => a[Math.floor(rnd() * a.length)];
const chars = data.characters.map((c) => c.id);

function deckFor(party) {
  const deck = [];
  for (const p of party) {
    for (const c of data.cards.filter((x) => x.owner === p.charId && x.start)) deck.push({ id: c.id });
    deck.push({ id: data.startingCommon[p.charId] });
    const t = data.characters.find((c) => c.id === p.charId).weapon;
    const w = data.weapons.find((x) => x.id === { ranged: 'w_basic_ranged', melee: 'w_basic_melee', device: 'w_basic_device', ritual: 'w_basic_ritual' }[t]);
    deck.push({ id: w.card, wielder: p.charId });
  }
  return deck;
}
function fight(enemies, bot, hpFrac = 1) {
  const pool = [...chars]; const party = [];
  while (party.length < 3) party.push({ charId: pool.splice(Math.floor(rnd() * pool.length), 1)[0], res: 0 });
  const maxHp = 3 * 81;
  const b = MF.Battle.create(data, { party, deck: deckFor(party), relics: [], hp: Math.floor(maxHp * hpFrac), maxHp, seed: Math.floor(rnd() * 1e9), enemies });
  b.do('begin');
  const hp0 = b.s.hp;
  for (let guard = 0; guard < 60 && b.s.phase === 'PLAYER'; guard++) {
    for (const a of b.s.actors) if (a.gauge >= 100) { try { b.do('ult', { actor: a.id, target: b.alive()[0]?.uid }); } catch (e) {} }
    for (let k = 0; k < 12 && b.s.phase === 'PLAYER'; k++) {
      const hand = b.s.zones.hand; if (!hand.length) break;
      let uid, mode;
      if (bot === 'random') { uid = pick(hand); mode = pick(b.allowedModes(uid)); }
      else {
        // planner: reserve if the card has a reserved value and the fight looks long; otherwise play immediately, cheapest first
        const sorted = [...hand].sort((x, y) => b.costOf(x, 'now') - b.costOf(y, 'now'));
        uid = sorted.find((u) => b.costOf(u, 'now') <= b.s.ap); if (!uid) break;
        const modes = b.allowedModes(uid);
        const def = b.effDef(b.s.cards[uid]);
        const hasRv = JSON.stringify(def.eff).includes('"rv"') || JSON.stringify(def.eff).includes('"ra"');
        const enemyHp = b.alive().reduce((a, e) => a + e.hp, 0);
        mode = bot === 'planner' && hasRv && modes.includes('next') && enemyHp > 40 && b.freeSlots('next') > 0 ? 'next' : (modes.includes('now') ? 'now' : modes[0]);
      }
      const alive = b.alive(); let target = alive[0]?.uid;
      if (b.needsTarget(uid) === 'fixed') { const m = alive.find((e) => e.st.mark > 0); if (m) target = m.uid; }
      else if (alive.length) target = alive.slice().sort((x, y) => x.hp - y.hp)[0].uid;
      try { b.do('play', { uid, mode, target }); } catch (e) { if (bot !== 'random') break; }
    }
    if (b.s.phase === 'PLAYER') b.do('end');
  }
  return { turns: b.s.turn, lost: hp0 - b.s.hp, win: b.s.phase === 'VICTORY', lostPct: (hp0 - b.s.hp) / maxHp };
}
const groups = [];
for (const zone of ['1', '2']) for (const tier of ['normal', 'elite', 'boss']) for (const enc of data.encounters[zone][tier]) groups.push({ zone, tier, enc });
const rows = [];
for (const g of groups) {
  for (const bot of ['random', 'greedy', 'planner']) {
    let t = 0, l = 0, w = 0; const reps = Math.max(10, Math.floor(N / groups.length));
    for (let i = 0; i < reps; i++) { const r = fight(g.enc, bot); t += r.turns; l += r.lostPct; w += r.win ? 1 : 0; }
    rows.push({ z: g.zone, tier: g.tier, enc: g.enc.join('+'), bot, turns: +(t / reps).toFixed(1), hpLostPct: Math.round(100 * l / reps), win: Math.round(100 * w / reps) });
  }
}
const agg = {};
for (const r of rows) { const k = `${r.z}/${r.tier}/${r.bot}`; agg[k] = agg[k] || { n: 0, turns: 0, lost: 0, win: 0 }; agg[k].n++; agg[k].turns += r.turns; agg[k].lost += r.hpLostPct; agg[k].win += r.win; }
for (const [k, v] of Object.entries(agg)) console.log(k.padEnd(22), 'turns', (v.turns / v.n).toFixed(1).padStart(5), ' hpLost%', String(Math.round(v.lost / v.n)).padStart(4), ' win%', String(Math.round(v.win / v.n)).padStart(4));
if (process.argv[3] === 'detail') for (const r of rows) console.log(JSON.stringify(r));
