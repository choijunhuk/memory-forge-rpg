#!/usr/bin/env python3
"""Validate current complete manuscript; --document permits a negative fixture."""
from pathlib import Path
import argparse,json,sys
sys.path.insert(0,str(Path(__file__).resolve().parent/'audit'))
from document_contract import evaluate
ROOT=Path(__file__).resolve().parents[1]
def main():
 p=argparse.ArgumentParser();p.add_argument('--document',type=Path);p.add_argument('--report',type=Path);args=p.parse_args()
 doc=args.document or ROOT/'docs/design_v0.8.1.md'
 d=json.loads((ROOT/'data/game_data.json').read_text());result=evaluate(doc.read_text(),d)
 result['document']=str(doc.name)
 for rel in ['package.json','docs/START_CODEX.md','docs/START_CLAUDE.md','docs/CURRENT.md','proposals/memory_v2.cjs','prototype/index.html']:
  if not (ROOT/rel).is_file():result['errors'].append('Missing file '+rel)
 result['pass']=not result['errors']
 output=args.report or ROOT/'reports/audit_v081/document_contract.json';output.parent.mkdir(parents=True,exist_ok=True);output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')
 print(json.dumps(result,ensure_ascii=False,indent=2));return 0 if result['pass'] else 1
if __name__=='__main__':sys.exit(main())
