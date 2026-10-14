#!/usr/bin/env python3
"""Generate checksums after all outputs are final. Never edit game data."""
from pathlib import Path
import hashlib,json
ROOT=Path(__file__).resolve().parents[2]
EXCLUDE={'MANIFEST_v0.8.1.json','MANIFEST.sha256'}
items=[]
for p in sorted(ROOT.rglob('*')):
 if not p.is_file():continue
 rel=p.relative_to(ROOT).as_posix()
 if rel in EXCLUDE or '.git' in p.parts:continue
 b=p.read_bytes();items.append({'path':rel,'bytes':len(b),'sha256':hashlib.sha256(b).hexdigest()})
manifest={'version':'0.8.1','scope':'All packaged files except the two checksum files; old manifests archived; runtime data unchanged','files':items}
mp=ROOT/'MANIFEST_v0.8.1.json';mp.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
lines=[f"{x['sha256']}  {x['path']}" for x in items];lines.append(hashlib.sha256(mp.read_bytes()).hexdigest()+'  MANIFEST_v0.8.1.json')
(ROOT/'MANIFEST.sha256').write_text('\n'.join(lines)+'\n')
print('Manifest entries',len(items))
