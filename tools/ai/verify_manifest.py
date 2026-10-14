#!/usr/bin/env python3
"""Verify current delivery without rewriting any file. Unknown/unsafe/missing = failure."""
from pathlib import Path
import hashlib,json,sys
ROOT=Path(__file__).resolve().parents[2]
def main():
 errors=[]; mp=ROOT/'MANIFEST_AIO.json';sp=ROOT/'MANIFEST.sha256'
 if not mp.is_file() or not sp.is_file():
  print('FAIL: current manifest pair is missing.');return 1
 try:
  m=json.loads(mp.read_text(encoding='utf-8'));seen=set()
  for i in m['files']:
   rel=Path(i['path']);p=ROOT/rel
   if rel.is_absolute() or '..' in rel.parts or not p.resolve().is_relative_to(ROOT.resolve()):
    errors.append('UNSAFE '+str(rel));continue
   if i['path'] in seen:errors.append('DUPLICATE '+i['path']);continue
   seen.add(i['path'])
   if not p.is_file():errors.append('MISSING '+i['path']);continue
   b=p.read_bytes()
   if len(b)!=i['bytes'] or hashlib.sha256(b).hexdigest()!=i['sha256']:errors.append('CHANGED '+i['path'])
  sha_rows={}
  for line in sp.read_text(encoding='utf-8').splitlines():
   h,r=line.split('  ',1)
   if r in sha_rows:errors.append('DUPLICATE_SHA '+r)
   sha_rows[r]=h
  if set(sha_rows)!=seen|{'MANIFEST_AIO.json'}:errors.append('SHA_INDEX_MISMATCH')
  if sha_rows.get('MANIFEST_AIO.json')!=hashlib.sha256(mp.read_bytes()).hexdigest():errors.append('MANIFEST_HASH_MISMATCH')
  for i in m['files']:
   if sha_rows.get(i['path'])!=i['sha256']:errors.append('SHA_MISMATCH '+i['path'])
  # Ignore generated working files after extraction. Declared delivery files must still match.
  print(json.dumps({'release':m['version'],'entries':len(m['files']),'pass':not errors,'errors':errors},ensure_ascii=False,indent=2))
  return 1 if errors else 0
 except (KeyError,ValueError,OSError) as e:
  print('FAIL: malformed manifest: '+str(e));return 1
if __name__=='__main__':sys.exit(main())
