import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {writeFileSync} from 'node:fs';
import {stateHash} from '../../tools/gen_traces.mjs';
const require = createRequire(import.meta.url);
const {Run, RuleError} = require('../../engine/rules-core.js'), data = require('../../data/game_data.json');
const cases = [];
for (const memoryPolicy of ['legacy', 'v2']) {
  const setup = {party: ['sera','harin','nia'].map(charId => ({charId,res:0})), seed:1234, length:'short', memoryPolicy,
    memory:{}, relics:[], difficulty:{hp:100,dmg:100}, zones:[1]};
  if (memoryPolicy === 'v2') Object.assign(setup, {runId:'unity-assignment', memoryVersion:0});
  const run = Run.create(data, setup), hash = () => stateHash({run:run.s,memory:null});
  const common = run.s.deck.find(c => c.owner === 'common'), own = run.s.deck.find(c => c.owner !== 'common');
  const row = {policy:memoryPolicy, initial:hash(), commonKey:common.key, ownKey:own.key, steps:[]};
  for (const args of [[common.key,'harin'], [common.key,common.assigned], [own.key,'harin'], [common.key,'unknown'], ['missing','harin']]) {
    let error = null;
    try { run.assign(...args); } catch (e) { if (!(e instanceof RuleError)) throw e; error = e.code; }
    row.steps.push({args,error,hash:hash()});
  }
  assert.equal(row.steps[0].error, memoryPolicy === 'legacy' ? null : 'REASSIGNMENT_REQUIRES_SEPARATE_FLOW');
  assert.equal(row.steps[1].error, memoryPolicy === 'legacy' ? null : 'REASSIGNMENT_REQUIRES_SEPARATE_FLOW');
  assert.ok(row.steps.slice(2).every(s => s.error === 'BAD_ASSIGN'));
  cases.push(row);
}
writeFileSync(new URL('./unity_assignment_actual_js.json', import.meta.url), JSON.stringify(cases,null,2)+'\n');
console.log(JSON.stringify({cases:cases.length, actualAttempts:cases.reduce((n,c)=>n+c.steps.length,0), noStateInjection:true}));
