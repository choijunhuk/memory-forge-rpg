#!/usr/bin/env python3
"""Validate the current package manifest; fail closed on missing/changed/unsafe paths."""
from pathlib import Path
import json,hashlib,sys
ROOT=Path(__file__).resolve().parents[2]
p=ROOT/'MANIFEST_v0.8.1.json'
if not p.exists():raise SystemExit('Missing current manifest; an incomplete build is not a release.')
d=json.loads(p.read_text());fail=[]
for x in d['files']:
 rel=Path(x['path'])
 if rel.is_absolute() or '..' in rel.parts:fail.append('UNSAFE '+str(rel));continue
 f=ROOT/rel
 if not f.is_file():fail.append('MISSING '+str(rel));continue
 b=f.read_bytes()
 if len(b)!=x['bytes'] or hashlib.sha256(b).hexdigest()!=x['sha256']:fail.append('CHANGED '+str(rel))
for line in (ROOT/'MANIFEST.sha256').read_text().splitlines():
 h,rel=line.split('  ',1);f=ROOT/rel
 if not f.is_file() or hashlib.sha256(f.read_bytes()).hexdigest()!=h:fail.append('SHA256 '+rel)
print(json.dumps({'version':d['version'],'entries':len(d['files']),'pass':not fail,'errors':fail},indent=2))
raise SystemExit(1 if fail else 0)
