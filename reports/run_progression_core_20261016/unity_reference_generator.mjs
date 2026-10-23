import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {writeFileSync} from 'node:fs';
import {stateHash} from '../../tools/gen_traces.mjs';
const require=createRequire(import.meta.url),MF=require('../../engine/rules-core.js'),data=require('../../data/game_data.json');
const setup={party:['sera','harin','nia'].map(charId=>({charId,res:0})),seed:1234,length:'short',memoryPolicy:'legacy',memory:{},relics:[],difficulty:{hp:100,dmg:100},zones:[1]};
const run=MF.Run.create(data,setup),steps=[];
const record=(scope,kind,args)=>{const command={scope,kind,args};if(scope==='battle'){command.v=run.battle.s.v;command.rid=`r${command.v}-${kind}`;run.battleCmd(kind,args);}else run[kind](...args);steps.push({command,hash:stateHash({run:run.s,memory:null}),phase:run.s.phase,hp:run.s.hp,coins:run.s.coins});};
const initial=stateHash({run:run.s,memory:null});record('run','enter',[run.available()[0]]);
for(let guard=0;run.s.phase==='BATTLE';guard++){
 assert.ok(guard<200,'Actual battle cycle guard');const b=run.battle,alive=b.alive();let choice;
 for(const uid of b.s.zones.hand){const modes=b.allowedModes(uid).slice().sort((a,z)=>(a==='now'?-1:1)-(z==='now'?-1:1));for(const mode of modes){const target=b.needsTarget(uid)==='fixed'?alive.find(e=>e.st.mark>0)?.uid:alive.slice().sort((a,z)=>a.hp-z.hp)[0]?.uid;if(b.check(uid,mode,target,[])===null){choice={uid,mode,target:target??null,picks:[]};break;}}if(choice)break;}
 if(choice)record('battle','play',choice);else record('battle','end',{});
}
assert.equal(run.s.phase,'REWARD');const offered=run.s.pending.cards.slice();record('run','takeCard',[offered[0]]);if(run.s.pending.part)record('run','takePart',[]);if(run.s.pending.relic?.length)record('run','takeRelic',[run.s.pending.relic[0]]);record('run','proceed',[]);assert.equal(run.s.phase,'MAP');
const value={scope:'Selected original full-content legacy first node cycle, actual public commands, not complete Run',setup,initial,offered,steps,final:{hp:run.s.hp,coins:run.s.coins,battles:run.s.stats.battles,keys:run.s.deck.map(c=>c.key)}};
writeFileSync(new URL('./unity_reference_actual_js.json',import.meta.url),JSON.stringify(value,null,2)+'\n');console.log(JSON.stringify({initial,steps:steps.length,phases:steps.map(x=>x.phase),final:value.final}));
