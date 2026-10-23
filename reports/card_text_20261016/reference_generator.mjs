// Exact authored and actual effective card descriptions; no game state or numeric data is changed.
import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {createHash} from 'node:crypto';
import {readFileSync, writeFileSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {resolve} from 'node:path';
const require = createRequire(import.meta.url), MF = require('../../engine/rules-core.js'), data = require('../../data/game_data.json'), ix = MF.indexData(data);
export const referencePath = fileURLToPath(new URL('./reference.json', import.meta.url));
export function generateReference() {
const rows = [];
for (const card of data.cards) {
  rows.push({id: card.id, variant: 'authored', definition: card, text: MF.cardText(card, ix)});
  const weapon = data.weapons.find(w => w.card === card.id);
  const owner = data.characters.find(c => c.id === card.owner)?.id ?? (weapon ? data.characters.find(c => c.weapon === weapon.type)?.id : 'sera');
  assert.ok(owner, card.id + ' owner');
  const party = [owner, ...data.characters.map(c => c.id).filter(id => id !== owner)].slice(0, 3);
  for (const [variant, plus, res] of [['plus', true, 0], ['resonance6', false, 6]]) {
    const battle = MF.Battle.create(data, {party: party.map(charId => ({charId, res})), deck: [{id: card.id, plus,
      ...(card.owner === 'common' ? {assigned: owner} : {}), ...(weapon ? {wielder: owner} : {})}], seed: 1234, hp: 243, maxHp: 243, enemies: [data.enemies[0].id]});
    const definition = battle.effDef(Object.values(battle.s.cards)[0]);
    rows.push({id: card.id, variant, definition, text: MF.cardText(definition, ix)});
  }
}
assert.equal(rows.length, 519);
const sources = Object.fromEntries(['engine/rules-core.js', 'data/game_data.json', 'reports/card_text_20261016/reference_generator.mjs'].map(path => [path,
  createHash('sha256').update(readFileSync(new URL('../../' + path, import.meta.url))).digest('hex')]));
return {schema: 'mf-card-text-v1', sources, count: rows.length, rows};
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const value = generateReference(), text = JSON.stringify(value) + '\n';
  if (process.argv[2] === '--check') assert.equal(readFileSync(referencePath, 'utf8'), text);
  else writeFileSync(referencePath, text);
  console.log(JSON.stringify({actualJsDescriptions: value.count, authoredCards: 173, actualEffectivePlus: 173, actualEffectiveResonance6: 173}));
}
