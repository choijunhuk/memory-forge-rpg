// Offline art export and composition checks, not gameplay tests.
import fs from 'node:fs/promises';
import path from 'node:path';
import { createRequire } from 'node:module';
const require=createRequire(import.meta.url);
const sharp=require(process.env.BATTLE_KIT_SHARP||'/Users/choi/.npm/_npx/3d55143a3497ce4a/node_modules/sharp');
process.chdir(path.resolve(import.meta.dirname,'..'));
const manifest=JSON.parse(await fs.readFile('manifest.json','utf8'));
const entries=manifest.assets;
const checks=[];
for(const a of entries){
 const {data,info}=await sharp(a.file).ensureAlpha().raw().toBuffer({resolveWithObject:true});
 let alphaZero=0,alphaPartial=0,alphaSolid=0,edgeNonzero=0,lowAlphaColor=0;
 for(let y=0;y<info.height;y++)for(let x=0;x<info.width;x++){
  const k=(y*info.width+x)*4,alpha=data[k+3];
  if(alpha===0)alphaZero++;else if(alpha===255)alphaSolid++;else alphaPartial++;
  if(alpha>0&&(x===0||y===0||x===info.width-1||y===info.height-1))edgeNonzero++;
  if(alpha===0&&(data[k]||data[k+1]||data[k+2]))lowAlphaColor++;
 }
 const meta=await sharp(a.file).metadata();
 const svg=a.source.endsWith('.svg')?await fs.readFile(a.source,'utf8'):'';
 checks.push({file:a.file,dimensions_match:info.width===a.width&&info.height===a.height,has_alpha:meta.hasAlpha,alphaZero,alphaPartial,alphaSolid,edgeNonzero,zero_alpha_rgb_pixels:lowAlphaColor,no_svg_text:!/<text[\s>]/i.test(svg),edge_ok:edgeNonzero===0||a.id.startsWith('ui_drag_arrow_body_')});
}
async function sliced(a,w,h){
 const [l,t,r,b]=a.border_ltrb;
 const sx=[0,l,a.width-r],sy=[0,t,a.height-b],sw=[l,a.width-l-r,r],sh=[t,a.height-t-b,b];
 const dx=[0,l,w-r],dy=[0,t,h-b],dw=[l,w-l-r,r],dh=[t,h-t-b,b],layers=[];
 for(let yi=0;yi<3;yi++)for(let xi=0;xi<3;xi++){
  if(sw[xi]===0||sh[yi]===0||dw[xi]===0||dh[yi]===0)continue;
  const input=await sharp(a.file).extract({left:sx[xi],top:sy[yi],width:sw[xi],height:sh[yi]}).resize(dw[xi],dh[yi],{fit:'fill'}).png().toBuffer();
  layers.push({input,left:dx[xi],top:dy[yi]});
 }
 return sharp({create:{width:w,height:h,channels:4,background:'#00000000'}}).composite(layers).png().toBuffer();
}
const find=id=>entries.find(a=>a.id===id);
const intent=entries.filter(a=>a.id.startsWith('ui_intent_'));
const thumbs=[];
for(let row=0;row<4;row++)for(let n=0;n<intent.length;n++){
 const size=row%2?32:24;
 thumbs.push({input:await sharp(intent[n].file).resize(size,size).png().toBuffer(),left:n*48+Math.floor((48-size)/2),top:row*52+Math.floor((52-size)/2)});
}
await sharp({create:{width:intent.length*48,height:208,channels:4,background:'#0d1322'}}).composite([{input:await sharp({create:{width:intent.length*48,height:104,channels:4,background:'#f3f0e7'}}).png().toBuffer(),left:0,top:104},...thumbs]).png().toFile('qa/intents-24-32.png');
const tiles=[];
for(let n=0;n<entries.length;n++){
 const a=entries[n],image=await sharp(a.file).resize(122,122,{fit:'inside'}).png().toBuffer(),m=await sharp(image).metadata();
 tiles.push({input:image,left:(n%9)*144+Math.floor((144-m.width)/2),top:Math.floor(n/9)*144+Math.floor((144-m.height)/2)});
}
await sharp({create:{width:1296,height:Math.ceil(entries.length/9)*144,channels:4,background:'#0d1322'}}).composite(tiles).png().toFile('qa/contact-sheet.png');
const proof=[];
for(let row=0;row<2;row++)for(let n=0;n<5;n++){
 const id=['ui_button_endturn','ui_button_endturn_pressed','ui_button_endturn_disabled','ui_gem_ap','ui_portrait_ring_ready'][n];
 proof.push({input:await sharp(find(id).file).resize(200,200).png().toBuffer(),left:n*232+16,top:row*232+16});
}
await sharp({create:{width:1160,height:464,channels:4,background:'#0d1322'}}).composite([{input:await sharp({create:{width:1160,height:232,channels:4,background:'#f3f0e7'}}).png().toBuffer(),left:0,top:232},...proof]).png().toFile('qa/alpha-proof.png');
const sliceLayers=[];
for(const [id,w,h,left,top]of [
 ['ui_rail_now_empty',210,96,20,20],['ui_rail_next_reserved',320,96,250,20],['ui_rail_after_sealed',600,96,590,20],
 ['ui_hp_frame_enemy',210,40,20,140],['ui_hp_frame_party',600,40,250,140],
 ['ui_panel_frame',520,280,20,220],['ui_button_pill_brass',560,128,600,210],
 ['ui_ribbon_title',560,96,600,360],['ui_card_border_frost',300,192,20,550],['ui_panel_frame',200,320,370,540],
 ])sliceLayers.push({input:await sliced(find(id),w,h),left,top});
await sharp({create:{width:1220,height:900,channels:4,background:'#0d1322'}}).composite(sliceLayers).png().toFile('qa/slice-proof.png');
const corners=[];
for(const a of entries.filter(a=>a.border_ltrb.some(Boolean))){
 const [l,t,r,b]=a.border_ltrb,w=a.width+174,h=t||b?a.height+90:a.height;
 const result=await sliced(a,w,h);
 const definitions=t&&b?[[0,0,l,t,0,0],[a.width-r,0,r,t,w-r,0],[0,a.height-b,l,b,0,h-b],[a.width-r,a.height-b,r,b,w-r,h-b]]:[[0,0,l,a.height,0,0],[a.width-r,0,r,a.height,w-r,0]];
 let alphaExact=true,maxPremultipliedRgbDifference=0;
 for(const [x,y,cw,ch,dx,dy]of definitions){
  if(!cw||!ch)continue;
  const first=await sharp(a.file).extract({left:x,top:y,width:cw,height:ch}).raw().toBuffer();
  const second=await sharp(result).extract({left:dx,top:dy,width:cw,height:ch}).raw().toBuffer();
  for(let i=0;i<first.length;i+=4){
   if(first[i+3]!==second[i+3])alphaExact=false;
   for(let c=0;c<3;c++)maxPremultipliedRgbDifference=Math.max(maxPremultipliedRgbDifference,Math.abs(first[i+c]*first[i+3]/255-second[i+c]*second[i+3]/255));
  }
 }
 corners.push({file:a.file,caps_or_corners_unchanged:alphaExact&&maxPremultipliedRgbDifference<=1,alpha_exact:alphaExact,max_premultiplied_rgb_difference:maxPremultipliedRgbDifference,tolerance:1});
}
const transparentCenters=[];
for(const id of ['ui_portrait_ring','ui_portrait_ring_ready','ui_node_medal_current','ui_node_medal_reachable','ui_node_medal_visited','ui_panel_frame','ui_card_border_common']){
 const a=find(id),pixel=await sharp(a.file).extract({left:Math.floor(a.width/2),top:Math.floor(a.height/2),width:1,height:1}).ensureAlpha().raw().toBuffer();
 transparentCenters.push({id,center_alpha:pixel[3]});
}
const pass=checks.every(c=>c.dimensions_match&&c.has_alpha&&c.alphaZero>0&&c.no_svg_text&&c.edge_ok)&&corners.every(c=>c.caps_or_corners_unchanged)&&transparentCenters.every(c=>c.center_alpha===0);
await fs.writeFile('qa/export-checks.json',JSON.stringify({scope:'art exports only; Unity/phone NOT_RUN',pass,count:entries.length,checks,slicing:corners,transparent_centers:transparentCenters,intent_columns:intent.map(a=>a.id),contact_sheet_order:entries.map(a=>a.id)},null,2)+'\n');
await fs.writeFile('preview.html',`<!doctype html><html lang="ko"><meta charset="utf-8"><title>Memory Forge UI art</title><style>body{background:#0d1322;color:#e9eef8;font:15px system-ui;margin:32px}h1{color:#e2b45f}main{display:grid;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:20px}figure{margin:0;border:1px solid #46516a;padding:12px;background:#141c31}img{max-width:100%;height:180px;object-fit:contain;background:repeating-conic-gradient(#28314a 0% 25%,#172036 0% 50%) 50%/20px 20px}figcaption{overflow-wrap:anywhere;margin-top:8px}a{color:#62d6c6}small{color:#b0bdd2}</style><h1>Memory Forge · Battle UI kit</h1><p>아트 검수용. Unity / 실제 기기 검증 아님. 모든 라벨은 HTML에만 있고 PNG에는 없다.</p><p><a href="README.md">README / 사용 조건</a> · <a href="qa/intents-24-32.png">의도 24/32px</a> · <a href="qa/slice-proof.png">9-slice proof</a> · <a href="qa/alpha-proof.png">Alpha proof</a></p><main>${entries.map(a=>`<figure><a href="${a.file}"><img src="${a.file}" alt="${a.id}"></a><figcaption>${a.id}<br><small>${a.width}×${a.height} · ${a.slot}<br>LTRB ${a.border_ltrb.join('/')}</small></figcaption></figure>`).join('')}</main></html>`);
console.log(JSON.stringify({pass,assets:entries.length,failures:checks.filter(c=>!c.edge_ok||!c.dimensions_match||!c.has_alpha),slice_failures:corners.filter(c=>!c.caps_or_corners_unchanged)}));
if(!pass)process.exitCode=1;
