const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const P=require('../memory_v2.cjs');
const D=JSON.parse(fs.readFileSync(path.join(__dirname,'../../data/game_data.json'),'utf8'));
const MF=require('../../engine/rules-core.js');
const ix=MF.indexData(D);
const copy=x=>JSON.parse(JSON.stringify(x));
function kit(cid){return MF.Run.baseKit(ix,cid).map((s,i)=>({...s,key:cid+'_'+i}));}
function base(){return {sera:kit('sera'),harin:kit('harin'),yuri:kit('yuri')};}
const cap={sera:6,harin:6,yuri:6};
function pick(before,after,kind){return P.listChanges(D,before,after).find(x=>x.kind===kind).id;}
test('M2-01 no selection preserves every prior card and trait',()=>{const b=base();b.sera[0].plus=true;const a=copy(b);a.harin.push({key:'new',id:'harin_frostwall'});assert.deepEqual(P.recover(D,b,a,[],cap).memory,b);});
test('M2-02 one new card preserves other characters previous upgrades',()=>{const b=base();b.sera[0].plus=true;const a=copy(b);a.harin.push({key:'new',id:'harin_frostwall'});const o=P.recover(D,b,a,[pick(b,a,'add')],cap);assert.ok(o.memory.sera[0].plus);assert.equal(o.memory.harin.at(-1).id,'harin_frostwall');});
test('M2-03 prior six plus delta three is rejected against full result',()=>{const b=base();b.sera.push({id:'sera_phase',key:'old1'},{id:'sera_lock',key:'old2'});const a=copy(b);a.sera.push({id:'sera_last',key:'new'});assert.equal(P.cost(D,'sera',b.sera),6);const original=copy(b);assert.throws(()=>P.recover(D,b,a,[pick(b,a,'add')],cap),{code:'OVER_CAPACITY'});assert.deepEqual(b,original);});
test('M2-04 an already saved addition is not offered as a new delta',()=>{const b=base();b.sera.push({key:'saved',id:'sera_phase'});assert.deepEqual(P.listChanges(D,b,copy(b)),[]);});
test('M2-05 duplicate card types have independent stable identities',()=>{const b=base();b.sera.push({key:'second_round',id:'sera_round'});const a=copy(b);a.sera.find(x=>x.key==='second_round').plus=true;const o=P.recover(D,b,a,[pick(b,a,'upgrade')],cap);assert.equal(o.memory.sera.filter(x=>x.id==='sera_round'&&x.plus).length,1);assert.ok(o.memory.sera.find(x=>x.key==='second_round').plus);});
test('M2-06 picking evolution does not also import an unselected upgrade',()=>{const b=base();const a=copy(b);const c=a.yuri.find(x=>x.id==='yuri_cocoon');c.from=c.id;c.id='yuri_cocoon_guard';c.plus=true;const o=P.recover(D,b,a,[pick(b,a,'evolve')],cap);const res=o.memory.yuri.find(x=>x.key===c.key);assert.equal(res.id,'yuri_cocoon_guard');assert.ok(!res.plus);assert.equal(o.used.yuri,2);});
test('M2-07 bundled new rare card charges rarity, upgrade and rare part',()=>{const b=base();const a=copy(b);a.sera.push({key:'rare',id:'sera_phase',plus:true,mods:{c:'c_echo'}});const o=P.recover(D,b,a,[pick(b,a,'add')],cap);assert.equal(o.used.sera,6);});
test('M2-08 more than one selected delta is rejected',()=>{const b=base();const a=copy(b);a.sera[0].plus=true;a.harin[0].plus=true;const cs=P.listChanges(D,b,a);assert.throws(()=>P.recover(D,b,a,cs.map(x=>x.id),cap),{code:'DEFEAT_KEEP_ONE'});});
test('M2-09 invalid change ID cannot modify the snapshot',()=>{const b=base();assert.throws(()=>P.recover(D,b,b,['no_such_delta'],cap),{code:'INVALID_SELECTION'});});
test('M2-10 missing or colliding stable identities are rejected',()=>{const b=base();b.sera[0].key=b.sera[1].key;assert.throws(()=>P.inspect(D,b),{code:'IDENTITY_REQUIRED'});});
test('M2-11 chosen part update imports only the server-derived changed slot',()=>{const b=base();const a=copy(b);a.sera[0].mods={c:'c_delay',e:'e_guard'};const cs=P.listChanges(D,b,a);const o=P.recover(D,b,a,[cs.find(x=>x.kind==='part'&&x.value.slot==='c').id],cap);assert.deepEqual(o.memory.sera[0].mods,{c:'c_delay'});assert.equal(o.used.sera,1);});
test('M2-12 temporary parts and invalid slot assignments are not persistent',()=>{const b=base();b.sera[0].mods={c:'amp'};assert.throws(()=>P.inspect(D,b),{code:'INVALID_PART'});b.sera[0].mods={c:'e_guard'};assert.throws(()=>P.inspect(D,b),{code:'INVALID_PART'});});
test('M2-13 removal preserves the other card of the same type',()=>{const b=base();b.sera.push({id:'sera_round',key:'extra'});const a=copy(b);a.sera=a.sera.filter(x=>x.key!=='extra');const o=P.recover(D,b,a,[pick(b,a,'remove')],cap);assert.equal(o.memory.sera.filter(x=>x.id==='sera_round').length,1);});
test('M2-14 arbitrary card replacement is not accepted as evolution',()=>{const b=base();const a=copy(b);a.sera[0].id='sera_phase';assert.throws(()=>P.listChanges(D,b,a),{code:'UNSUPPORTED_TRANSFORMATION'});});
