#!/usr/bin/env python3
"""Run local checks. Does not invoke AI, log in, install dependencies, or deploy."""
from pathlib import Path
import subprocess,json,sys,os
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'reports/local_checks';OUT.mkdir(parents=True,exist_ok=True)
npm='npm.cmd' if os.name=='nt' else 'npm'
commands=[('js',[npm,'test']),('memory',[npm,'run','test:proposals']),('economy',[sys.executable,'-m','unittest','discover','-s','tests','-v']),('docs',[npm,'run','test:docs']),('handoff',[npm,'run','test:handoff'])]
results=[]
for name,cmd in commands:
 try:
  p=subprocess.run(cmd,cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,timeout=180)
  status='PASS' if p.returncode==0 else 'FAIL';text=p.stdout
 except (OSError,subprocess.TimeoutExpired) as e:status='FAIL';text=str(e)
 (OUT/(name+'.txt')).write_text(text,encoding='utf-8')
 results.append({'name':name,'status':status,'command':cmd});print(name,status,flush=True)
(OUT/'results.json').write_text(json.dumps({'checks':results,'scope':'local tests only; no Claude/Codex authentication, Unity, server, device, billing or deployment'},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
sys.exit(0 if all(x['status']=='PASS' for x in results) else 1)
