#!/usr/bin/env python3
"""Build current release hashes AFTER verification; not an input-repair command."""
from pathlib import Path
import hashlib,json
ROOT=Path(__file__).resolve().parents[2]
SKIP={'MANIFEST_AIO.json','MANIFEST.sha256'}
def files():
 for p in sorted(ROOT.rglob('*')):
  if not p.is_file():continue
  rel=p.relative_to(ROOT)
  if any(x in rel.parts for x in ['.git','node_modules','__pycache__']):continue
  if rel.as_posix() in SKIP:continue
  yield p,rel.as_posix()
items=[]
for p,r in files():
 b=p.read_bytes();items.append({'path':r,'bytes':len(b),'sha256':hashlib.sha256(b).hexdigest()})
mp=ROOT/'MANIFEST_AIO.json'
mp.write_text(json.dumps({'version':'0.8.1-aio.2','game_design':'0.8.1','runtime_data':'0.7.0','scope':'All delivered files excluding current manifest pair and transient cache/dependency/Git directories','files':items},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(ROOT/'MANIFEST.sha256').write_text(''.join(f"{i['sha256']}  {i['path']}\n" for i in items)+hashlib.sha256(mp.read_bytes()).hexdigest()+'  MANIFEST_AIO.json\n',encoding='utf-8')
print(f'Manifest entries: {len(items)}')
