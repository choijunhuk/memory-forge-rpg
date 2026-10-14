from pathlib import Path
import json,re,csv,sys
R=Path(__file__).resolve().parents[2];sys.path.insert(0,str(R/'tools/audit'))
from document_contract import chapters
old=chapters((R/'docs/design_v0.7.md').read_text());new=chapters((R/'docs/design_v0.8.1.md').read_text());edits=json.loads((R/'reports/audit_v081/intentional_edits.json').read_text())
for e in edits:
 old[e['chapter']]=old[e['chapter']].replace(e['before'],e['after'])
# Exact editorial replacement sections are accounted for, not called unchanged.
exempt={0:['0.1','0.2','0.3','0.4','0.5'],9:['9.1'],15:['15.2','15.5','15.6'],18:['18.4'],19:['19.1'],20:['20.1'],22:['22.1']}
def normal(t):return re.sub(r'\s+','',t.replace('**','').replace('`','').replace('docs/design_v0.7.md','docs/design_v0.8.1.md'))
rows=[];missing=[]
for n,text in old.items():
 sec=f'{n}.0';dest=normal(new[n]);matched=0;ignored=0;oldlines=0
 for line in text.splitlines():
  h=re.match(r'^## (\d+\.\d+)',line)
  if h:sec=h[1]
  if not line.strip() or line.lstrip().startswith(('#','```','---','<!--')):continue
  if re.match(r'^\|[\s:|\-]+$',line):continue
  oldlines+=1
  if n==0 or sec in exempt.get(n,[]):ignored+=1;continue
  a=normal(line)
  # Legacy work instructions retain their scope with new current reference names.
  a=a.replace('§4.5규칙검수(10대플라토닉)','§4.5·§9.1관계·표현검수')
  a=a.replace('10대캐릭터에는[10대규칙]블록을반드시유지한다.','캐릭터별§9.1공통기준과§15.6현재프롬프트를사용한다.')
  if a in dest:matched+=1
  else:missing.append({'chapter':n,'section':sec,'line':line})
 rows.append({'chapter':n,'eligible_source_lines':oldlines-ignored,'matched':matched,'editorial_section_lines':ignored,'unaccounted':sum(x['chapter']==n for x in missing)})
(R/'reports/audit_v081/preservation_lines.json').write_text(json.dumps({'note':'Per-line source coverage after recorded literal edits. Explicitly rewritten editorial/status sections excluded and recorded; not a claim of unchanged wording.','chapters':rows,'unaccounted':missing},ensure_ascii=False,indent=2))
with (R/'reports/audit_v081/preservation_by_chapter.csv').open('w',newline='') as f:
 w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
print('UNACCOUNTED',len(missing));
for x in missing:print(x)
