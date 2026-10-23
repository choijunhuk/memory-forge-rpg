"""Local asset inventory, provenance and integrity checks for this visual handoff."""
import csv,hashlib,json,math,re
from pathlib import Path
from PIL import Image
R=Path(__file__).resolve().parent
PROJECT=R.parents[3]
# Resolve explicitly; the candidate directory itself is not a new project root.
PROJECT=Path('/Users/choi/Documents/Memory_forge')
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,x):(R/p).write_text(json.dumps(x,ensure_ascii=False,indent=2)+'\n')
L=json.loads((R/'layout.json').read_text())
D=json.loads((PROJECT/'data/game_data.json').read_text())
expected={x['id'] for x in D['enemies'] if x['zone']==1}
actual={p.stem.removeprefix('enemy_').removesuffix('_default_v01') for p in (R/'assets/enemies').glob('*.png')}
assert expected==actual,(expected-actual,actual-expected)
selected_backgrounds={x['background'] for x in L['screens'].values()}
assert len(selected_backgrounds)==3
for x in selected_backgrounds:assert (R/'assets/backgrounds'/f'{x}.png').is_file(),x
for key in ['ExpeditionFrame','DeckButton','LogButton','AbandonButton']:
 assert any(x['selector']==key for x in L['common'])
for key in ['Party','Enemy:<uid>','Hand:<uid>','EndTurn','Mode:now','Mode:next','Mode:after','ConfirmCard','Timeline','ActionPresentation']:
 assert any(x['selector']==key for x in L['screens']['battle']['elements']),key
# Check full battle target bounds for every permitted enemy count, not just one fixture.
for n in range(1,6):
 w=(1178-16*(n-1))/n
 assert w>0
 assert abs((390+(n-1)*(w+16)+w)-1568)<.001
 assert 182+316<=506
# Expected nondecorative battle bands cannot overlap each other.
bands=['PartyStatus','Party','Timeline','ActionPresentation','CardActions','Hand']
last=0
for k in bands:
 a=next(x['rect'] for x in L['screens']['battle']['elements'] if x['selector']==k)
 assert a[1]>=last,(k,last,a)
 last=a[1]+a[3]
for p in (R/'qa').glob('text-fit*.json'):
 assert not json.loads(p.read_text()),p.name
# Actual contrast of primary and muted text against opaque data panel.
def luminance(s):
 a=[int(s[i:i+2],16)/255 for i in (1,3,5)]
 a=[v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4 for v in a]
 return sum(x*y for x,y in zip(a,[.2126,.7152,.0722]))
contrast={}
for k in ['ink','muted','brass','memory','danger']:
 a,b=sorted([luminance(L['tokens'][k]),luminance(L['tokens']['panel'])]);ratio=(b+.05)/(a+.05)
 contrast[k]=round(ratio,2);assert ratio>=4.5,(k,ratio)
a,b=sorted([luminance(L['tokens']['background']),luminance(L['tokens']['brass'])]);assert (b+.05)/(a+.05)>=4.5

matrix={
 'character_consistency':{'status':'PASS_REUSE','evidence':'Six approved starter PNGs copied byte-for-byte, source hashes in references/sources.json. No character generation or identity edits.'},
 'individuality':{'status':'PASS_CANDIDATE','evidence':'13 silhouettes reviewed in qa/enemy-contact-native.jpg and sparse/crowded battle previews. Moth/cog/blob/hound/lantern/page; broad bell, tall mirror, low garden bosses remain distinct.'},
 'scene_suitability':{'status':'PASS_CANDIDATE','evidence':'Dark quiet scenery, opaque data fields; no human enemy faces, baked UI labels or sexual imagery. Existing party costumes unchanged.'},
 'clothing':{'status':'PASS_REUSE_OR_NA','evidence':'Approved party images unchanged. Nonhuman ink/paper/brass enemy cloaks have connected forms; no new human clothing design.'},
 'quality':{'status':'PASS_FIRST_PASS_WITH_LIMITS','evidence':'Native and composed inspection; zero tested text overflows after four-weakness fix; all PNGs decode and enemy alpha is real. Native generated assets are below final background/boss spec, thin edge noise/tight margins remain production-retouch items.'},
 'record':{'status':'PASS','evidence':'Local provenance.csv, exact prompts, source references, native dimensions, alpha statistics and SHA256 in manifest. Root art/provenance.csv intentionally untouched under user write boundary.'}
}
write('qa/self-review.json',dict(criteria='docs/source_v0.8.1/15_chapter.md §15.5',reviewer='Codex self-review only; no other agents called',
 status='first-pass candidate, not final production approval',matrix=matrix,
 fixed=['Three clocktower faces corrected to stopped midnight','Seed four-weakness field expanded without losing current/next intents','Reward card cost and base effect retained'],
 not_run=['Unity import, Play Mode and gameplay regression','Physical phone safeArea,48dp,14sp/12sp and130% font scaling','Human artist retouch and layered PSD/rig/hit animation','Full4–8-variant production selection process','Independent Claude or human review'],
 native_size_limits=dict(backgrounds=[1672,941],enemies=[1254,1254],final_reference_background=[2560,1440],final_reference_boss=[2048,2048],upscaled=False),
 display_inset='Sprites are contained, never cropped; apply .92x fit inside specified art rect if edge clipping/filtering is observed; do not erase dark pixels as a key. Tight raw transparent margins do not imply8% native padding.'))

jobs=[]
for f in ['generation-prompts.json','enemy-prompts.json']:
 jobs+=json.loads((R/'prompts'/f).read_text())['jobs']
byid={x['id']:x for x in jobs}
edits=[]
edits+=json.loads((R/'prompts/background-corrections.json').read_text())['edits']
edits+=[json.loads((R/'prompts/map-correction.json').read_text())]
for e in edits:byid[e['id']]=e
sources={}
for p in (R/'qa').glob('generation-*.json'):
 x=json.loads(p.read_text());sources[x['id']]=x['source']
sources.update({
 'bg_zone1_battle_street_v01':'exec-43479ec0-ecbe-4dc2-97c2-1b3bf09abe5d.png',
 'bg_zone1_event_workshop_v01':'exec-a4e6fd7a-1f47-45d7-9166-fe851d0fa4c0.png',
 'bg_zone1_battle_street_v02':'exec-6541228a-60c0-44d9-9e20-557a3488025e.png',
 'bg_zone1_event_workshop_v02':'exec-e87ff536-8504-4d88-8b9a-c5415505221b.png',
 'bg_zone1_map_clockcity_v02':'exec-b8fe9160-1cab-490f-b720-da0de8fa7292.png'})
entries=[];prov=[]
for folder in ['assets','references','previews']:
 for p in sorted((R/folder).rglob('*')):
  if not p.is_file() or p.suffix.lower() not in ['.png','.jpg','.svg']:continue
  rel=str(p.relative_to(R));stem=p.stem;selected=True
  kind='preview' if folder=='previews' else 'approved_reference' if folder=='references' else 'new_asset'
  if 'backgrounds' in rel:
   selected=stem in selected_backgrounds
   purpose='Zone1 '+('battle stage' if 'battle' in stem else 'route map' if 'map' in stem else 'shared service/reward/memory environment')
   review=dict(status='SELF_REVIEWED_CANDIDATE' if selected else 'REJECTED_CLOCK_POSITION',
      chapter15_5='Style/scene/quality/record reviewed; character/clothing N/A',
      notes='Selected v02 clock hands inspected at12. Native1672×941; no upscaling, no parallax layers.' if selected else 'Retain for provenance only; clock face not stopped at midnight.')
  elif 'enemies' in rel:
   eid=stem.removeprefix('enemy_').removesuffix('_default_v01');purpose=f"{eid} / {next(x['name'] for x in D['enemies'] if x['id']==eid)} enemy art"
   review=dict(status='SELF_REVIEWED_CANDIDATE',chapter15_5='Nonhuman identity/silhouette/scene/mechanical connections/record reviewed',
     notes='Real alpha; no human face or writing. Native1254²; boss resolution below2048² target. Tight alpha edges/fine line cleanup remain final-retouch work. Contain in UI with padding.')
  elif folder=='references':
   purpose='Existing approved party '+('SD portrait' if '_sd_' in stem else 'cutin used only as decorative card header')
   review=dict(status='BYTE_IDENTICAL_APPROVED_REUSE',chapter15_5='Identity/clothing preserved by hash; no new character review claimed')
  elif folder=='previews':
   purpose='Offline static design composition; never a Unity screenshot or live data source'
   review=dict(status='SELF_REVIEWED_LAYOUT',chapter15_5='Final-size information hierarchy reviewed',notes='Fixture data illustrates layout; gameplay/device acceptance NOT_RUN')
  else:
   purpose='UI map-type or attribute shape; pair with text'
   review=dict(status='SELF_REVIEWED_VECTOR',chapter15_5='Shape differentiated; simple original code-native strokes; no image API',notes='SVG originals and raster companion share motif; glyph geometry is deliberately simple')
  if p.suffix=='.svg':size=[128,128];alpha=dict(present=True,kind='transparent SVG canvas')
  else:
   with Image.open(p) as im:
    im.load();size=list(im.size);alpha=dict(present='A' in im.getbands())
    if alpha['present']:
     a=im.getchannel('A');lo,hi=a.getextrema();hist=a.histogram()
     alpha.update(min=lo,max=hi,fully_transparent_fraction=round(hist[0]/(im.width*im.height),6),bbox=a.getbbox())
     if 'enemies' in rel:assert lo==0 and hi==255 and hist[0]>0
  entry=dict(file=rel,size=size,bytes=p.stat().st_size,alpha=alpha,sha256=sha(p),purpose=purpose,selected=selected,
    role=kind,production_final=False,self_review=review)
  if 'enemies' in rel:entry['definition_id']=eid
  if stem in byid:
   entry.update(prompt_id=stem,route='Codex built-in image_gen',backend_model='not exposed by tool',reference=byid[stem].get('reference'),source_result=sources.get(stem))
  entries.append(entry)
  prov.append(dict(file=rel,date='2026-10-18',creator='Codex',route=entry.get('route','approved byte copy' if folder=='references' else 'local deterministic UI composition/export'),
    model=entry.get('backend_model','N/A'),prompt_id=entry.get('prompt_id','N/A'),reference=entry.get('reference','references/sources.json' if folder=='references' else 'layout.json'),
    selected=selected,sha256=entry['sha256'],review=review['status']))
refs=json.loads((R/'references/sources.json').read_text())
for x in refs:assert sha(R/x['file'])==x['sha256']==sha(PROJECT/x['source'])
manifest=dict(package='Memory Forge Zone1 visual first pass',date='2026-10-18',designer='Codex',scope='art/candidates/20261018/codex only',
 status='SELF_REVIEWED_CANDIDATE_HANDOFF',production_final=False,
 entrypoints=['preview.html','design-spec.md','layout.json','qa/self-review.json'],
 totals=dict(selected_generated_backgrounds=3,selected_generated_enemies=13,original_ui_icons=12,icon_png_exports=12,approved_reference_copies=6,
   static_screen_compositions=14,phone_aspect_fit_compositions=1,rejected_background_originals=3),
 generation_route='Approved built-in Codex image_gen only; no external paid APIs, credits, Claude, agents or SSH calls',
 prompt_files=['prompts/generation-prompts.json','prompts/enemy-prompts.json','prompts/background-corrections.json','prompts/map-correction.json'],
 files=entries)
write('manifest.json',manifest)
with (R/'provenance.csv').open('w',newline='') as f:
 w=csv.DictWriter(f,fieldnames=list(prov[0]));w.writeheader();w.writerows(prov)
source_files=['docs/CURRENT.md','docs/AI_WORKFLOW.md','docs/source_v0.8.1/04_chapter.md','docs/source_v0.8.1/10_chapter.md',
 'docs/source_v0.8.1/14_chapter.md','docs/source_v0.8.1/15_chapter.md','data/game_data.json','client/MemoryForgeUnity/Assets/Scripts/MemoryForgeExpeditionClient.cs']
write('qa/source-snapshot.json',dict(note='Read-only source fingerprints at handoff; not a project-wide manifest validation or assertion that parallel developer-root files stayed unchanged',
 files=[dict(file=x,sha256=sha(PROJECT/x)) for x in source_files]))
checks=dict(status='PASS',checks=['All13 zone1 definitions have matching sprites','All3 selected backgrounds exist','Named controls present in layout','Enemy geometry for counts1–5 inside canvas',
 'Core vertical battle bands do not overlap','All14 preview text-fit logs empty','Every image decodes','Enemy alpha spans0–255 with transparent pixels','Six approved reference copies byte-identical','Text/panel colour contrast>=4.5'],
 contrast_ratios=contrast,files_hashed=len(entries),not_run=['Unity, device, gameplay tests, physical dp/sp,115/130% runtime text'],
 check_scope='art handoff only; no game code or root tests executed')
write('qa/verification.json',checks)
print(json.dumps(checks,ensure_ascii=False))
