import {createRequire} from 'node:module';import {writeFileSync} from 'node:fs';
const require=createRequire(import.meta.url),{Run,MemoryV2}=require('../../engine/rules-core.js'),data=require('../../data/game_data.json');
const runId='numeric-public-witness', setup={party:['sera','harin','nia'].map(charId=>({charId,res:0})),seed:1234,length:'short',memoryPolicy:'v2',memory:{},relics:[],difficulty:{hp:100,dmg:100},zones:[1],runId,memoryVersion:0};
const run=Run.create(data,setup),store=MemoryV2.migrate(data,{},{}),buf=new ArrayBuffer(8),v=new DataView(buf);v.setBigUint64(0,0x44b52d02c7e14af5n,false);const expectedMemoryVersion=v.getFloat64(0,false);
const request={requestId:'numeric-replay',runId,expectedMemoryVersion,selectedChangeIds:[]},body=JSON.stringify({runId,expectedMemoryVersion,selectedChangeIds:[]});
store.receipts[request.requestId]={body,response:{memory:{},used:{},version:0}};
let code=null;try{run.commitMemory(request,store);}catch(e){code=e.code??e.name;}
if(code!==null||run.s.phase!=='DONE')throw new Error('Actual source witness did not replay');
const output={scope:'Controlled public caller receipt input; no private Run state injected, no account/disk operation',bits:'44b52d02c7e14af5',body,expectedCode:code,expectedPhase:run.s.phase};writeFileSync(new URL('./public_numeric_js_witness.json',import.meta.url),JSON.stringify(output,null,2)+'\n');console.log(JSON.stringify(output));
