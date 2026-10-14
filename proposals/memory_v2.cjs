/* Stable-identity memory operations. The original one-delta proposal remains available as recover.
   before/after are server-created snapshots, never arbitrary client payloads.
   schema: { characterId: [{ key: persistedInstanceKey, id, plus?, from?, mods?, assigned? }] }
   Selection is a server-derived change ID. Capacity is checked on the COMPLETE result.
*/
(function(root){
'use strict';
const clone=o=>JSON.parse(JSON.stringify(o));
function fail(code){const e=new Error(code);e.code=code;throw e;}
function inspect(data,snap){
 if(!snap||typeof snap!=='object'||Array.isArray(snap))fail('INVALID_SNAPSHOT');
 const cards=Object.fromEntries(data.cards.map(c=>[c.id,c]));
 const parts=Object.fromEntries(data.parts.map(p=>[p.id,p]));
 const known=new Set(data.characters.map(c=>c.id));const seen=new Set();
 for(const [cid,rows] of Object.entries(snap)){
  if(!known.has(cid)||!Array.isArray(rows))fail('INVALID_SNAPSHOT');
  for(const sp of rows){
   if(!sp||typeof sp!=='object')fail('INVALID_CARD');
   if(typeof sp.key!=='string'||!sp.key||seen.has(sp.key))fail('IDENTITY_REQUIRED');seen.add(sp.key);
   const d=cards[sp.id];if(!d||!['common',cid].includes(d.owner)||sp.token||sp.weapon)fail('INVALID_CARD');
   if(d.owner==='common'&&sp.assigned!==cid)fail('INVALID_ASSIGNMENT');
   if(sp.from){const origin=cards[sp.from];if(!origin?.growth||origin.owner!==cid||!origin.evo?.includes(sp.id))fail('INVALID_EVOLUTION');}
   if(data.cards.some(c=>c.evo?.includes(sp.id))&&!sp.from)fail('INVALID_EVOLUTION');
   if(sp.plus!==undefined&&typeof sp.plus!=='boolean')fail('INVALID_UPGRADE');
   if(sp.mods!=null&&(typeof sp.mods!=='object'||Array.isArray(sp.mods)||Object.keys(sp.mods).some(sl=>!['c','e'].includes(sl))))fail('INVALID_PART');
   for(const sl of ['c','e'])if(sp.mods?.[sl]){const p=parts[sp.mods[sl]];if(!p||p.slot!==({c:'circuit',e:'engraving'}[sl]))fail('INVALID_PART');}
  }
 }
 return {cards,parts};
}
function listChanges(data,before,after){
 inspect(data,before);inspect(data,after);
 if(JSON.stringify(Object.keys(before).sort())!==JSON.stringify(Object.keys(after).sort()))fail('CHARACTER_SET_MISMATCH');
 const owners=new Map(Object.entries(before).flatMap(([cid,rows])=>rows.map(c=>[c.key,cid])));
 for(const [cid,rows] of Object.entries(after))for(const c of rows)if(owners.has(c.key)&&owners.get(c.key)!==cid)fail('REASSIGNMENT_REQUIRES_SEPARATE_FLOW');
 const out=[];const push=(cid,key,kind,value)=>out.push({id:`delta:${encodeURIComponent(cid)}:${encodeURIComponent(key)}:${kind}${kind==='part'?':'+value.slot:''}`,cid,key,kind,value});
 for(const cid of Object.keys(before).sort()){
  const old=new Map(before[cid].map(c=>[c.key,c]));const now=new Map(after[cid].map(c=>[c.key,c]));
  for(const [key,a] of old){
   const b=now.get(key);if(!b){push(cid,key,'remove');continue;}
   if(a.assigned!==b.assigned)fail('REASSIGNMENT_REQUIRES_SEPARATE_FLOW');
   if(a.id!==b.id||a.from!==b.from){
    const root=data.cards.find(c=>c.id===(a.from||a.id));
    if(!root?.growth||a.from||b.from!==a.id||!root.evo?.includes(b.id))fail('UNSUPPORTED_TRANSFORMATION');
    push(cid,key,'evolve',{id:b.id,from:b.from});
   }
   if(!!a.plus!==!!b.plus)push(cid,key,'upgrade',!!b.plus);
   for(const sl of ['c','e'])if((a.mods?.[sl]||null)!==(b.mods?.[sl]||null))push(cid,key,'part',{slot:sl,part:b.mods?.[sl]||null});
  }
  for(const [key,c] of now)if(!old.has(key))push(cid,key,'add',clone(c));
 }
 return out;
}
function baseline(data,cid){
 return [...data.cards.filter(c=>c.owner===cid&&c.start).map(c=>({id:c.id})),{id:data.startingCommon[cid],assigned:cid}].map((sp,i)=>({...sp,key:`base:${cid}:${i}`}));
}
function cost(data,cid,specs,baseSpecs){
 const {cards,parts}=inspect(data,{[cid]:specs});const C=data.rules.memoryCost;
 const base=baseSpecs||baseline(data,cid);
 const pool=[...specs];let total=0;
 const upgradeCost=c=>(c.plus?C.upgrade:0)+(c.from?C.evolution:0)+['c','e'].reduce((s,sl)=>s+(c.mods?.[sl]?(parts[c.mods[sl]].rarity==='rare'?C.partRare:C.partCommon):0),0);
 for(const sp of base){const i=pool.findIndex(c=>baseSpecs?c.key===sp.key:(c.from||c.id)===sp.id);if(i<0)total+=C.remove;else total+=upgradeCost(pool.splice(i,1)[0]);}
 for(const c of pool){const d=cards[c.from||c.id];total+=(d.owner==='common'?C.addCommon:({B:C.addB,A:C.addA,S:C.addS}[d.rar]||C.addA))+upgradeCost(c);}
 return total;
}
function applyChanges(before,changes){
 const out=clone(before);
 for(const ch of changes){
  const rows=out[ch.cid];const idx=rows.findIndex(c=>c.key===ch.key);
  if(ch.kind==='add')rows.push(clone(ch.value));
  else if(ch.kind==='remove')rows.splice(idx,1);
  else if(ch.kind==='evolve'){rows[idx].id=ch.value.id;rows[idx].from=ch.value.from;}
  else if(ch.kind==='upgrade')rows[idx].plus=ch.value;
  else if(ch.kind==='part')rows[idx].mods={...rows[idx].mods,[ch.value.slot]:ch.value.part};
 }
 return out;
}
function apply(data,before,after,selectedIds,capacities,options={}){
 if(!Array.isArray(selectedIds)||selectedIds.some(id=>typeof id!=='string')||new Set(selectedIds).size!==selectedIds.length)fail('INVALID_SELECTION');
 const changes=listChanges(data,before,after);
 const selected=selectedIds.map(id=>{const ch=changes.find(c=>c.id===id);if(!ch)fail('INVALID_SELECTION');return ch;});
 const out=applyChanges(before,selected);
 inspect(data,out);
 let error=options.defeat&&selectedIds.length>1?'DEFEAT_KEEP_ONE':null;
 const used={};for(const cid of Object.keys(out)){used[cid]=cost(data,cid,out[cid],options.baseline?.[cid]);if(!Number.isInteger(capacities[cid])||used[cid]>capacities[cid])error=error||'OVER_CAPACITY';}
 if(error&&!options.preview)fail(error);
 return {memory:out,used,error};
}
function recover(data,before,after,selectedIds,capacities){
 if(!Array.isArray(selectedIds)||selectedIds.length>1)fail('DEFEAT_KEEP_ONE');
 const {memory,used}=apply(data,before,after,selectedIds,capacities,{defeat:true});return {memory,used};
}
function migrate(data,legacy,capacities){
 if(!legacy||typeof legacy!=='object'||Array.isArray(legacy))fail('INVALID_SNAPSHOT');
 const state={schema:'mf-memory-v2',version:0,memory:{},quarantine:{},legacyBackup:clone(legacy),history:[],receipts:{}};
 const seen=new Set();
 for(const cid of Object.keys(legacy).sort()){
  try{
   if(!data.characters.some(c=>c.id===cid)||!Array.isArray(legacy[cid]))fail('INVALID_SNAPSHOT');
   const remaining=baseline(data,cid);
   const rows=legacy[cid].map((raw,i)=>{
    const sp=typeof raw==='string'?{id:raw}:clone(raw);
    if(!sp||typeof sp!=='object')fail('INVALID_CARD');
    const pos=remaining.findIndex(b=>b.id===(sp.from||sp.id));const b=pos<0?null:remaining.splice(pos,1)[0];
    if(!sp.key)sp.key=b?.key||`legacy:${cid}:${i}`;
    if(data.cards.find(c=>c.id===sp.id)?.owner==='common'&&!sp.assigned)sp.assigned=cid;
    return sp;
   });
   inspect(data,{[cid]:rows});
   if(rows.some(c=>seen.has(c.key)))fail('IDENTITY_REQUIRED');
   if(!Number.isInteger(capacities[cid])||cost(data,cid,rows,baseline(data,cid))>capacities[cid])fail('OVER_CAPACITY');
   state.memory[cid]=rows;rows.forEach(c=>seen.add(c.key));
  }catch(e){state.quarantine[cid]={code:e.code||'INVALID_SNAPSHOT',original:clone(legacy[cid])};}
 }
 return state;
}
const api={inspect,listChanges,cost,recover,baseline,apply,applyChanges,migrate};
if(typeof module!=='undefined'&&module.exports)module.exports=api;
root.MFMemoryV2=api;
})(typeof globalThis!=='undefined'?globalThis:this);
