/** Independent diagnostic probes. Original source is not modified.
 * Run from bundle root on Node 22.16:
 * node --experimental-strip-types review/independent-probes.ts
 * On compatible newer Node, omit the experimental flag per the source README.
 */
import assert from 'node:assert/strict';
import { BattleEngine } from '../card-flow-study/prototype/src/engine.ts';
import { newBattle, play, TEST_CONTENT } from '../card-flow-study/prototype/test/helpers.ts';
import type { Content } from '../card-flow-study/prototype/src/types.ts';
const observations: object[] = [];

// §8 places TurnEnding reactions before hand clean-up.
{
  const content: Content = structuredClone(TEST_CONTENT);
  content.reactions.endDraw = {
    id:'endDraw', name:'turn-end draw probe', on:'TurnEnding',
    limitPerTurn:1, effects:[{op:'draw',count:1}],
  };
  const e = new BattleEngine(content, {
    seed:7, hero:{id:'hero',name:'probe',hp:50},
    deck:['guard','strike','strike'], enemies:[{id:'e1',defId:'dummy'}],
    shuffleDeck:false,
  }, {drawPerTurn:1});
  e.start();
  e.state.reactions.push({uid:'probe-reaction',reactionId:'endDraw',ownerId:'hero',usesThisTurn:0});
  const result=e.dispatch({type:'EndTurn'});
  assert.ok(result.ok);
  const discardIndex=result.events.findIndex(x=>x.type==='CardMoved'&&x.from==='hand'&&x.to==='discard');
  const reactionIndex=result.events.findIndex(x=>x.type==='ReactionTriggered'&&x.reactionId==='endDraw');
  assert.ok(discardIndex<reactionIndex);
  assert.equal(e.state.zones.hand.length,2);
  observations.push({
    id:'CF-01',classification:'SPEC_IMPLEMENTATION_ORDER_MISMATCH',
    scenario:'TurnEnding reaction draws 1; new-turn draw=1; no retain cards',
    expectedFromSpec:'Reaction draws before hand cleanup; new turn should end with 1 card',
    actual:{discardIndex,reactionIndex,handCount:e.state.zones.hand.length,events:result.events.map(x=>x.type)},
    limitation:'Synthetic valid reaction exercises a supported generic hook, not an existing sample card.',
  });
}

// Partial commit is explicitly specified by R-15: concern, not R-15 violation.
{
  const e=newBattle(['feedbackLoop','guard','guard','guard','guard'],
    [{id:'e1',defId:'dummy'}],{maxOpsPerCommand:200});
  play(e,'feedbackLoop');
  const before={ap:e.state.ap,block:e.unit('hero').block,fuse:e.unit('e1').fuse};
  const events=play(e,'guard');
  const after={ap:e.state.ap,block:e.unit('hero').block,fuse:e.unit('e1').fuse,phase:e.state.phase};
  assert.ok(events.some(x=>x.type==='ResolutionAborted'));
  assert.ok(after.block>6);
  assert.equal(after.fuse,before.fuse);
  observations.push({id:'CF-02',classification:'INTENTIONAL_REFERENCE_POLICY_NOT_FOR_PRODUCTION',before,after,
    actual:'Abort retains gained block and paid AP, skips remaining tempo tick, and returns player input.',
    recommendation:'Do not copy R-15 into authoritative gameplay. Reject/rollback the logical action or require authoritative recovery before further play or rewards.'});
}

// Default fuse idle behaviour is acknowledged by §6, not a new bug.
{
  const e=newBattle(['guard','guard','guard','guard','guard'],[{id:'e1',defId:'quick'}]);
  const initial={hp:e.unit('hero').hp,fuse:e.unit('e1').fuse};
  let enemyStarts=0;
  for(let i=0;i<10;i++){
    const res=e.dispatch({type:'EndTurn'});assert.ok(res.ok);
    enemyStarts+=res.events.filter(x=>x.type==='EnemyActionStarted').length;
  }
  assert.equal(enemyStarts,0);
  assert.equal(e.unit('e1').fuse,initial.fuse);
  observations.push({id:'CF-03',classification:'DOCUMENTED_RULE_CHOICE',initial,
    after:{hp:e.unit('hero').hp,fuse:e.unit('e1').fuse,turn:e.state.turn},enemyStarts,
    recommendation:'Do not transplant this model to Memory Forge: future-turn reservations could advance without normal enemy pressure.'});
}
console.log(JSON.stringify({kind:'independent_diagnostic_observations',observations},null,2));
