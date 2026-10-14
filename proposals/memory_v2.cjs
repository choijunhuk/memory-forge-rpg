/* PROPOSED ONLY: stable-identity, one-delta defeat recovery. Not wired into Battle, Run or the UI.
   before/after are server-created snapshots, never arbitrary client payloads.
   schema: { characterId: [{ key: persistedInstanceKey, id, plus?, from?, mods?, assigned? }] }
   Selection is a server-derived change ID. Capacity is checked on the COMPLETE result.
*/
'use strict';
const clone=o=>JSON.parse(JSON.stringify(o));
function fail(code){const e=new Error(code);e.code=code;throw e;}
function inspect(data,snap){
 const cards=Object.fromEntries(data.cards.map(c=>[c.id,c]));
 const parts=Object.fromEntries(data.parts.map(p=>[p.id,p]));
 const known=new Set(data.characters.map(c=>c.id));const seen=new Set();
 for(const [cid,rows] of Object.entries(snap)){
  if(!known.has(cid)||!Array.isArray(rows))fail('INVALID_SNAPSHOT');
  for(const sp of rows){
   if(typeof sp.key!=='string'||!sp.key||seen.has(sp.key))fail('IDENTITY_REQUIRED');seen.add(sp.key);
   const d=cards[sp.id];if(!d||!['common',cid].includes(d.owner)||sp.token||sp.weapon)fail('INVALID_CARD');
   if(d.owner==='common'&&sp.assigned!==cid)fail('INVALID_ASSIGNMENT');
   if(sp.from){const root=cards[sp.from];if(!root?.growth||!root.evo?.includes(sp.id))fail('INVALID_EVOLUTION');}
   if(sp.plus!==undefined&&typeof sp.plus!=='boolean')fail('INVALID_UPGRADE');
   for(const sl of ['c','e'])if(sp.mods?.[sl]){const p=parts[sp.mods[sl]];if(!p||p.slot!==({c:'circuit',e:'engraving'}[sl]))fail('INVALID_PART');}
  }
 }
 return {cards,parts};
}
function listChanges(data,before,after){
 inspect(data,before);inspect(data,after);
 if(JSON.stringify(Object.keys(before).sort())!==JSON.stringify(Object.keys(after).sort()))fail('CHARACTER_SET_MISMATCH');
 const out=[];const push=(cid,key,kind,value)=>out.push({id:`delta_${out.length}`,cid,key,kind,value});
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
function cost(data,cid,specs){
 const {cards,parts}=inspect(data,{[cid]:specs});const C=data.rules.memoryCost;
 const base=data.cards.filter(c=>c.owner===cid&&c.start).map(c=>c.id);base.push(data.startingCommon[cid]);
 const pool=[...specs];let total=0;
 const upgradeCost=c=>(c.plus?C.upgrade:0)+(c.from?C.evolution:0)+['c','e'].reduce((s,sl)=>s+(c.mods?.[sl]?(parts[c.mods[sl]].rarity==='rare'?C.partRare:C.partCommon):0),0);
 for(const id of base){const i=pool.findIndex(c=>(c.from||c.id)===id);if(i<0)total+=C.remove;else total+=upgradeCost(pool.splice(i,1)[0]);}
 for(const c of pool){const d=cards[c.from||c.id];total+=(d.owner==='common'?C.addCommon:({B:C.addB,A:C.addA,S:C.addS}[d.rar]||C.addA))+upgradeCost(c);}
 return total;
}
function recover(data,before,after,selectedIds,capacities){
 if(!Array.isArray(selectedIds)||selectedIds.length>1)fail('DEFEAT_KEEP_ONE');
 const changes=listChanges(data,before,after);const out=clone(before);
 for(const id of selectedIds){
  const ch=changes.find(c=>c.id===id);if(!ch)fail('INVALID_SELECTION');
  const rows=out[ch.cid];const idx=rows.findIndex(c=>c.key===ch.key);
  if(ch.kind==='add')rows.push(clone(ch.value));
  else if(ch.kind==='remove')rows.splice(idx,1);
  else if(ch.kind==='evolve'){rows[idx].id=ch.value.id;rows[idx].from=ch.value.from;}
  else if(ch.kind==='upgrade')rows[idx].plus=ch.value;
  else if(ch.kind==='part')rows[idx].mods={...rows[idx].mods,[ch.value.slot]:ch.value.part};
 }
 inspect(data,out);
 const used={};for(const cid of Object.keys(out)){used[cid]=cost(data,cid,out[cid]);if(!Number.isInteger(capacities[cid])||used[cid]>capacities[cid])fail('OVER_CAPACITY');}
 return {memory:out,used};
}
module.exports={inspect,listChanges,cost,recover};
