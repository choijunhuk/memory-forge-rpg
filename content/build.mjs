// Build data/*.json from the authoring modules. Usage: node content/build.mjs <outDir>
import { writeFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { allCards } from './helpers.mjs';
import { characters, startingCommon } from './characters.mjs';
import { attributes, weaponTypes, statuses, keywords, weapons, parts, relics, enemies, encounters, events } from './world.mjs';
import { gacha } from './gacha.mjs';
import * as progression from './progression.mjs';

const out = process.argv[2] || 'data';
mkdirSync(out, { recursive: true });
const cards = allCards();
const charById = Object.fromEntries(characters.map((c) => [c.id, c]));
for (const c of cards) {
  if (!c.attr) c.attr = c.owner === 'weapon' ? 'wielder' : (charById[c.owner]?.attr ?? 'none');
}
const data = {
  meta: { version: '0.7.0', generated: 'content/build.mjs', rating: '15+', referencePower: 40 },
  rules: {
    apPerTurn: 4, apMax: 9, handDraw: 5, handMax: 10,
    slots: { next: 2, after: 1, nextMax: 3, afterMax: 2, totalMax: 5 },
    debtCap: 4, growthCapPerBattle: 3, gaugeMax: 100, gaugePerCard: 20, gaugeTurnCap: 50, // RULES_NOW_PLAY: ultimates as often as before
    breakApBonus: 1, breakApBonusMaxPerTurn: 2, brokenDamageBps: 2500, vulnerableBps: 2500, weakBps: 2500,
    upgradeMult: 1.3, capacityBase: 6,
    memoryCost: { addB: 1, addA: 2, addS: 3, addCommon: 1, upgrade: 1, partCommon: 1, partRare: 2, evolution: 2, remove: 1 },
  },
  attributes, weaponTypes, statuses, keywords,
  characters, startingCommon, cards, weapons, parts, relics, enemies, encounters, events,
  gacha: { ...gacha, seasonIncome: progression.seasonIncome },
  progression: { ...progression },
};
writeFileSync(join(out, 'game_data.json'), JSON.stringify(data, null, 1));
const counts = {
  characters: characters.length,
  cards: cards.length,
  uniquePool: cards.filter((c) => charById[c.owner] && c.pool !== false).length,
  common: cards.filter((c) => c.owner === 'common').length,
  weaponCards: cards.filter((c) => c.owner === 'weapon').length,
  weapons: weapons.length, parts: parts.length, relics: relics.length, enemies: enemies.length, events: events.length,
};
console.log(JSON.stringify(counts));
