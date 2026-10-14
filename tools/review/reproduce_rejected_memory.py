#!/usr/bin/env python3
"""Runs a rejected patch ONLY in a temporary sandbox; never changes the active game."""
from pathlib import Path
import tempfile,shutil,subprocess,json
ROOT=Path(__file__).resolve().parents[2]
with tempfile.TemporaryDirectory(prefix='mf_rejected_') as td:
    p=Path(td);(p/'engine').mkdir();(p/'data').mkdir()
    shutil.copy2(ROOT/'review/sources/REJECTED_memory_patch_candidate.js.txt',p/'engine/rules-core.js')
    shutil.copy2(ROOT/'review/baseline/data/game_data.json',p/'data/game_data.json')
    run=subprocess.run(['node',str(ROOT/'tools/review/reproduce_cases.cjs'),str(p)],check=True,text=True,capture_output=True)
    result=json.loads(run.stdout);result['root']='temporary rejected-patch candidate (never active)'
    assert result['memoryCap']['resultCost'] > result['memoryCap']['capacity']
    assert result['memoryDuplicate']['count']==2
    print(json.dumps(result,ensure_ascii=False,indent=2))
