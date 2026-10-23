"""Verify delivered bytes and referenced geometry without modifying any file."""
import hashlib,json
from pathlib import Path
from PIL import Image
R=Path(__file__).resolve().parent
M=json.loads((R/'manifest.json').read_text())
for x in M['files']:
 p=(R/x['file']).resolve();assert p.is_relative_to(R),p
 assert hashlib.sha256(p.read_bytes()).hexdigest()==x['sha256'],p
 assert p.stat().st_size==x['bytes'],p
 if p.suffix!='.svg':
  with Image.open(p) as im:im.load();assert list(im.size)==x['size'],p
for p in (R/'qa').glob('text-fit*.json'):assert json.loads(p.read_text())==[],p
assert M['totals']['selected_generated_enemies']==13
assert len([x for x in M['files'] if x['selected'] and '/enemies/' in x['file']])==13
assert len([x for x in M['files'] if x['selected'] and '/backgrounds/' in x['file']])==3
L=json.loads((R/'layout.json').read_text())
for name,s in L['screens'].items():
 assert (R/'assets/backgrounds'/(s['background']+'.png')).is_file(),name
assert json.loads((R/'qa/verification.json').read_text())['status']=='PASS'
print(f"PASS: {len(M['files'])} asset/reference/preview hashes and sizes;13 enemy ids;3 background bindings; all tested text-fit logs empty")
