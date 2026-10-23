import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { createHash } from 'node:crypto';
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gunzipSync } from 'node:zlib';
const output=dirname(fileURLToPath(import.meta.url)), root=resolve(process.argv[2]||''), expected=process.argv[3];
const sha=x=>createHash('sha256').update(x).digest('hex');
assert.match(expected||'',/^[a-f0-9]{64}$/,'Pass the author-frozen engine SHA before running semantics');
const paths=['engine/rules-core.js','engine/content-modes.js','engine/tests/support_partner.test.mjs','data/game_data.json'];
const pins=Object.fromEntries(paths.map(p=>[p,sha(readFileSync(join(root,p)))]));
assert.equal(pins['engine/rules-core.js'],expected,'JS author source changed before semantic run');
const require=createRequire(join(root,'package.json')), MF=require(join(root,'engine/rules-core.js'));
const DATA=require(join(root,'data/game_data.json')), ix=MF.indexData(DATA), clone=x=>JSON.parse(JSON.stringify(x));
const baseline='/Users/choi/Documents/Memory_forge/.omx/context/account-progression-s4-baseline-20261021';
const old=JSON.parse(gunzipSync(readFileSync(join(baseline,'growth-ults.json.gz'))));
assert.equal(pins['data/game_data.json'],old.dataSha256,'Production data changed');
const cases=[];
function dataRoot(id,data) { const dir=join(output,'fixtures',id);mkdirSync(join(dir,'data'),{recursive:true});writeFileSync(join(dir,'data/game_data.json'),JSON.stringify(data)+'\n');return 'fixtures/'+id; }
const production=dataRoot('production',DATA);
function record(id,data,where,setup,script) {
 const b=MF.Battle.create(data,clone(setup)), row={id,dataRoot:where,setup:clone(setup),created:clone(b.s),steps:[]};
 const command=(kind,args={},code=null,request=null)=>{const req=request||{rid:id+'-'+row.steps.length,v:b.s.v,kind,args},before=clone(b.s),receipts=clone(b.receipts);let error=null,response=null;
  try{response=clone(b.command(clone(req)));}catch(e){if(!(e instanceof MF.RuleError))throw e;error=e.code;}
  assert.equal(error,code,id+' '+kind+' refusal');if(error){assert.deepEqual(b.s,before);assert.deepEqual(b.receipts,receipts);}
  row.steps.push({request:req,...(error?{errorCode:error}:{response}),state:clone(b.s)});return req;};
 const query=(method,args=[],code=null)=>{const before=clone(b.s),receipts=clone(b.receipts);let result=null,error=null;
  try{result=clone(method==='effectiveCard'?b.effDef(b.s.cards[args[0]]):b[method](...clone(args)));}catch(e){if(!(e instanceof MF.RuleError))throw e;error=e.code;}
  assert.equal(error,code,id+' '+method+' refusal');assert.deepEqual(b.s,before);assert.deepEqual(b.receipts,receipts);
  row.steps.push({query:{method,args},...(error?{errorCode:error}:{result}),state:clone(b.s)});};
 script({b,command,query});cases.push(row);
}
for(const source of old.cases) {
 const ch=DATA.characters.find(c=>c.id===source.characterId), types=['ranged','melee','device','ritual'].filter(t=>t!==ch.weapon);
 const active=types.map(t=>DATA.characters.find(c=>c.weapon===t));
 const party=active.map((c,i)=>{const g={charId:c.id,level:1,weaponId:MF.Run.defaultWeapon(ix,c.id),weaponLevel:1,skillLv:1,ultLv:1};const d=MF.Stats.derive(ix,g);
  return {charId:c.id,res:0,gauge:i?0:100,weaponId:g.weaponId,refine:1,power4:d.power4,hp:d.hp,gearBps:d.gearBps,skillLv:d.skillLv,ultLv:d.ultLv};});
 const p=source.setup.party[0],support=Object.fromEntries(['charId','res','weaponId','refine','power4','hp','gearBps','skillLv','ultLv'].map(k=>[k,p[k]]));
 const deck=active.flatMap(c=>[...MF.Run.baseKit(ix,c.id),{id:ix.weapons[MF.Run.defaultWeapon(ix,c.id)].card,wielder:c.id}]);
 const maxHp=party.reduce((n,p)=>n+p.hp,0),setup={seed:800+cases.length,party,support,deck,hp:maxHp-33,maxHp,enemies:['b_archivist','b_garden'],relics:[]};
 record('authored-'+source.id,DATA,production,setup,({b,command,query})=>{
  command('begin');const caller=party[0].charId,target=b.supportNeedsTarget()?b.alive()[0].uid:null;
  query('supportInfo',[caller,target,[]]);query('supportChoices',[caller,[]]);query('supportForecast',[caller,target,[]]);query('publicView');
  command('support',{caller:source.characterId,target},'UNKNOWN_ACTOR');
  if(target)command('support',{caller,target:'missing'},'BAD_TARGET');
  const req=command('support',{caller,target,picks:[]});assert.equal(b.s.support.used,true);assert.equal(b.actor(caller).gauge,0);
  query('supportInfo',[caller,target,[]]);query('supportChoices',[caller,[]]);query('supportForecast',[caller,target,[]]);query('publicView');
  command('support',{},null,req);if(!b.isOver()){command('end');command('support',{caller},'SUPPORT_ALREADY_USED');}command('support',{},null,req);
 });
}
// Isolated test-only authored-schema variants cover paths absent from real production ultimates.
function variant(id,edit){const d=clone(DATA);edit(d);return {d,where:dataRoot(id,d)};}
const token=variant('token-copy',d=>{
 d.rules.handDraw=10;const nia=d.characters.find(c=>c.id==='nia');
 d.cards.push({id:'s4_parity_token',name:'S4 isolated token',owner:'nia',rar:'SR',cost:0,type:'atk',res:'y',kw:[],pool:false,attr:'void',growth:true,
  eff:[{op:'dmg',v:10},{op:'blk',v:10},{op:'gauge',n:50}]});
 d.cards.push({id:'s4_parity_copy',name:'S4 isolated copy',owner:'common',rar:'SR',cost:0,type:'skl',res:'n',kw:[],pool:false,attr:'none',eff:[{op:'copyPick'}]});
 nia.resonance[0].fx={type:'cardMod',cards:['s4_parity_token'],prop:'dmg',add:5};
 nia.ult.eff=[{op:'create',card:'s4_parity_token',n:1,zone:'hand'},{op:'create',card:'com_slash',n:1,zone:'hand'},
  {op:'create',card:'sera_round',n:1,zone:'hand'},{op:'tune',part:'s4_parity_echo',pick:'all'},{op:'gauge',n:50}];
 d.parts.push({id:'s4_parity_echo',name:'S4 isolated echo',slot:'circuit',rarity:'R',text:'test only',fx:{echoCopy:1}});
});
const tokenSupport={charId:'nia',res:3,weaponId:'w_basic_device',refine:5,power4:120,hp:70,gearBps:300,skillLv:2,ultLv:1};
const tokenSetup={seed:3,party:[{charId:'sera',gauge:100,power4:160,hp:81}],support:tokenSupport,deck:[{id:'s4_parity_copy'}],hp:200,maxHp:200,enemies:['b_archivist'],relics:[]};
record('isolated-token-copy-reservation',token.d,token.where,tokenSetup,({b,command,query})=>{
 command('begin');query('supportChoices',['sera',[]]);command('support',{caller:'sera'});
 const own=Object.values(b.s.cards).find(c=>c.id==='s4_parity_token'),copy=Object.values(b.s.cards).find(c=>c.id==='s4_parity_copy');
 assert.equal(own.supportSourceId,'nia');assert.equal(b.actor('sera').gauge,0);
 command('play',{uid:copy.uid,mode:'now',picks:[own.uid]});query('effectiveCard',[own.uid]);
 command('play',{uid:own.uid,mode:'now',target:b.alive()[0].uid});
 assert.ok(b.s.pendingCopies.some(c=>c.supportSourceId==='nia'));command('end');
 assert.ok(Object.values(b.s.cards).some(c=>c.temp.includes('echoHalf')&&c.supportSourceId==='nia'));
 command('play',{uid:own.uid,mode:'next',target:b.alive()[0].uid});command('end');query('publicView');
});
const pick=variant('support-pick',d=>{d.rules.handDraw=10;d.characters.find(c=>c.id==='nia').ult.eff=[{op:'costdown',n:1,pick:'one'}];});
record('isolated-support-query-picks',pick.d,pick.where,{...tokenSetup,deck:[{id:'sera_round'},{id:'com_guard'}]},({b,command,query})=>{
 command('begin');const uid=b.s.zones.hand.find(u=>b.s.cards[u].id==='sera_round');
 query('supportChoices',['sera',[]]);query('supportChoices',['sera',[uid]]);query('supportInfo',['sera',null,[uid]]);query('supportForecast',['sera',null,[uid]]);
 command('support',{caller:'sera',picks:['missing']},'PICK_INVALID');command('support',{caller:'sera',picks:[uid]});query('publicView');
});
for(const selected of [false,true]) {
 const setup={...clone(tokenSetup),deck:[{id:'sera_round'}],runFlags:{rng:7,skillLv:3000000000}};
 if(!selected)delete setup.support;
 record('opaque-runflags-'+(selected?'support':'no-support'),DATA,production,setup,({b,command,query})=>{
  command('begin');assert.deepEqual(b.s.runFlags,{rng:7,skillLv:3000000000});
  query('publicView');query('supportInfo',['sera',null,[]],selected?null:'SUPPORT_NOT_SELECTED');
  query('supportChoices',['sera',[]]);query('supportForecast',['sera',null,[]]);
  if(selected){command('support',{caller:'sera',picks:['missing']},'PICK_INVALID');command('support',{caller:'sera'});query('supportInfo',['sera',null,[]]);}
  else command('support',{caller:'sera'},'SUPPORT_NOT_SELECTED');
  assert.deepEqual(b.s.runFlags,{rng:7,skillLv:3000000000});query('publicView');
 });
}
for(const [path,hash]of Object.entries(pins))assert.equal(sha(readFileSync(join(root,path))),hash,path+' changed during semantics');
const fixture={schema:'mf-independent-s4-full-parity-v1',node:process.version,sourceRoot:root,sourceHashes:pins,cases};
writeFileSync(join(output,'fixture.json'),JSON.stringify(fixture)+'\n');
console.log(JSON.stringify({status:'PASS',cases:cases.length,authoredCases:140,steps:cases.reduce((n,c)=>n+c.steps.length,0),sourceHashes:pins}));
