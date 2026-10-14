// Reproducible editorial prompt pack. No game state or content data is modified.
import {readFileSync,writeFileSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {resolve,dirname} from 'node:path';
import {notes} from '../../content/character_notes.mjs';
const root=resolve(dirname(fileURLToPath(import.meta.url)),'../..');
const data=JSON.parse(readFileSync(resolve(root,'data/game_data.json'),'utf8'));
const raw=JSON.parse(readFileSync(resolve(root,'art/briefs/characters_v0.8.json'),'utf8')).characters;
const aliases={eila:'aila',shion:'sion'};
const poses={
 sera:'lowering her scope and meeting the viewer with a quiet dry smile; strong rifle diagonal, clock-tower wind',
 nia:'standing beside her workbench, presenting a hovering drone above her palm with a proud playful grin',
 yuri:'lifting her head in surprise as a seedling blooms inside her terrarium lantern; frosted greenhouse, face and lantern as focal points',
 ravien:'turning a contract to reveal a hidden clause, quick-witted smile, glowing brass scales tracing an arc',
 harin:'planting her shield and signalling that her teammates are safe; planted feet and outward shield silhouette',
 rien:'stepping forward in a lively violin finale, expressive smile, moving ribbons, eye-level full figure and instrument in focus',
 aila:'closing a record book and meeting the viewer with a gentle but decisive smile, a crescent of cleansing light',
 sion:'drawing a bookmark-shaped sword as loose pages sweep sideways, glasses catching a small light, reserved expression',
 kaya:'pointing at her newly found clue with a triumphant grin, crossbow held safely aside, eye-level adventurous composition',
 lumi:'sprinting with twin daggers, jacket and hair following the movement, confident athletic expression and electric trails'
};
const style='Korean subculture fantasy RPG, crisp illustrated line work, clean cel shading with soft painted light, expressive faces, brass clockwork and a harbor city of memories. Preserve the approved character sheet; no text, watermark or logo.';
const entries=raw.map(a=>{
 const id=aliases[a.id]||a.id,c=data.characters.find(x=>x.id===id),n=notes[id];
 if(!c||!n)throw Error('Unresolved character '+id);
 const en=n.en, age=a.age;
 const look=en.look.replace(/in her (mid|late|early) 20s/g,`aged ${age}`);
 const specifics=`${look}. Outfit baseline: ${en.outfit}. Weapon: ${en.weapon}. Motifs: ${en.motif}.`;
 const boundary=age<18?'Adolescent adventurer in a natural, non-sexual scene; expressive motion, everyday clothing and team interaction are welcome.':'Clearly adult character; elegant or sporty appeal and non-explicit fashion fanservice as appropriate to her personality.';
 const common=`${style} ${boundary}`;
 return {id,name:a.name,age,ageSource:'unchanged runtime age',editorialAlias:a.id===id?null:a.id,style:common,briefKorean:a.clothes,prompts:{
  SHEET:`Character reference sheet: front, three-quarter and back full-body views, neutral studio stance, light plain background. ${specifics} ${common}`,
  KEYART:`Gacha key illustration, 16:9. ${poses[id]}. ${specifics} ${common} Follow this action pose, not the neutral turnaround pose.`,
  CUTIN:`Card illustration 3:4: face, hands and signature weapon action readable within the top 70 percent safe area. ${poses[id]}. ${specifics} ${common}`,
  SD:`Map token, non-sexual chibi about 2.5 heads tall, simplified recognizable clothing and weapon, friendly standing pose, transparent background, 1:1. ${specifics} ${style}`,
  EXPR:`Six facial expression studies at the same angle and lighting: neutral, happy, annoyed, surprised, worried, determined. Distinct emotional range consistent with her personality. ${look}. ${style} Head portraits only.`
 }};
});
if(new Set(entries.map(x=>x.id)).size!==10)throw Error('Duplicate character id');
writeFileSync(resolve(root,'art/briefs/prompts_v0.8.1.json'),JSON.stringify({version:'0.8.1-editorial',notice:'Design prompts only; current ages unchanged; not generated art.',characters:entries},null,2)+'\n');
console.log('prompts: 10 characters / 50 prompts; aliases explicit');
