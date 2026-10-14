"""Content-preservation contracts: chapters are anchored H1s, never numbered characters."""
from pathlib import Path
import re,json
ANCHOR=re.compile(r'^# <a id="ch(\d+)"></a>[^\n]+$',re.M)
ORDER=['sera','nia','yuri','ravien','harin','rien','aila','sion','kaya','lumi']
TAGS=['SHEET','KEYART','CUTIN','SD','EXPR']
def chapters(text):
 m=list(ANCHOR.finditer(text));return {int(x[1]):text[x.start():m[i+1].start() if i+1<len(m) else len(text)] for i,x in enumerate(m)}
def evaluate(text,data):
 errors=[];ids=[int(x[1]) for x in ANCHOR.finditer(text)]
 if ids!=list(range(24)):errors.append(f'Exactly ch0..ch23 in order required; found {ids}')
 ch=chapters(text)
 for n,terms in {2:['2.1','2.4'],3:['3.1','3.4'],4:['4.1','4.4','4.6','4.7'],5:['5.3','5.15','5.16','5.17','5.18'],6:['6.3','6.6','6.7','6.8'],7:['7.2','7.7','7.8'],8:['8.3','8.8','8.14'],9:['9.2','9.3','9.4'],11:['11.11','11.14','11.15'],12:['12.1','12.7','12.8','12.12'],14:['14.3','14.7'],15:['15.3','15.5','15.6','15.7'],19:['19.1','19.5'],20:['20.1','20.10']}.items():
  for term in terms:
   if term not in ch.get(n,''):errors.append(f'Chapter {n} lacks {term}')
 c9=ch.get(9,'')
 hm=list(re.finditer(r'^### <a id="char-([^\"]+)"></a>9\.3\.(\d+) [^\n]+$',c9,re.M))
 if [m[1] for m in hm]!=ORDER:errors.append('All ten full character sheets in canonical order required')
 details={}
 for i,m in enumerate(hm):
  end=hm[i+1].start() if i+1<len(hm) else c9.find('## 9.4',m.end())
  body=c9[m.start():end];cid=m[1]
  expected=[x for x in data['cards'] if x['owner']==cid and x['rar'] in ['B','A','S'] and x.get('pool',True)]
  if len(expected)!=12:errors.append(f'Expected12 runtime core cards for {cid}, found{len(expected)}')
  for card in expected:
   if not re.search(r'`'+re.escape(card['id'])+r'`',body):errors.append(f'{cid} full sheet missing card {card["id"]}')
  for marker in ['패시브','필살기','공명','운용 가이드','현재 제작용 보완 브리프']:
   if marker not in body:errors.append(f'{cid} lacks full sheet field {marker}')
  details[cid]=len(expected)
 for collection,chapter in [('relics',7),('parts',6),('events',7),('weapons',10)]:
  for entry in data[collection]:
   if entry['id'] not in ch.get(chapter,''):errors.append(f'{collection} {entry["id"]} missing from chapter{chapter}')
 for card in data['cards']:
  if f'`{card["id"]}`' not in ch.get(6,''):errors.append('Card ID index missing '+card['id'])
 for tag in TAGS:
  n=len(re.findall(r'^\*\*\['+tag+r'\]\*\*$',ch.get(15,''),re.M))
  if n!=10:errors.append(f'{tag} count {n}, expected10 actual prompt blocks')
 if '<!-- PROMPTS_' in text or '<!-- AUDIT_RESULTS_' in text:errors.append('Unexpanded generation placeholder')
 # Compare against preserved source chapters, not guessed character-count thresholds.
 baseline=chapters((Path(__file__).resolve().parents[2]/'docs/design_v0.7.md').read_text())
 for n in [2,3,4,5,6,7,8,9,10,11,12,13,14,16,17,20,21,23]:
  if len(ch.get(n,'')) < len(baseline[n])*0.95:
   errors.append(f'Chapter{n} is materially shorter than the preserved baseline; inspect content diff')
 # Specific rule clauses catch a TOC-only restoration even when padded with unrelated text.
 for n,terms in {5:['WRONG_PHASE','NOT_IN_HAND','LOAN_OVERFLOW','PULL_DESTINATION_FULL','checkInvariants','xoshiro128','전투 시작','최초 1턴','power4','160','81','단계'],6:['hitsScale','growPick','growLowest','growAll','freeNext','exhaustPick','breakBonusAll','costIf','res','uses'],7:['0층','엘리트','보상','ev_promise','memory_anvil'],8:['OVER_CAPACITY','DEFEAT_KEEP_ONE','before','after'],10:['apDrain','sealSlot','delayRes','silence','eatName','phase'],17:['FOR UPDATE','command_receipt','request_id','COMMIT']}.items():
  for term in terms:
   if term not in ch.get(n,''):errors.append(f'Rule clause {term} missing from chapter{n}')
 return {'pass':not errors,'errors':errors,'chapters':ids,'character_core_cards':details,'entities':{k:len(data[k]) for k in ['cards','characters','weapons','parts','relics','enemies','events']},'prompt_count':sum(len(re.findall(r'^\*\*\['+tag+r'\]\*\*$',ch.get(15,''),re.M)) for tag in TAGS),'source_chars':len(text)}
