#!/usr/bin/env python3
"""Package tests; no model calls, credentials, cloud access, or payments."""
import unittest,json,re,hashlib
from pathlib import Path
from delivery_files import font_errors, handoff_files, private_path
ROOT=Path(__file__).resolve().parents[2]
class HandoffTests(unittest.TestCase):
 def test_root_files(self):
  for r in ['START_HERE.txt','Memory_Forge_Complete_Design.pdf','README.md','AGENTS.md','CLAUDE.md','package.json','prototype/index.html','docs/AI_WORKFLOW.md','docs/AI_Start_Guide.md']:
   self.assertTrue((ROOT/r).is_file(),r)
 def test_starter_is_same_as_codex_instructions(self):
  self.assertEqual((ROOT/'START_HERE.txt').read_text(),(ROOT/'docs/START_CODEX.md').read_text().split('\n\n',1)[1])
 def test_shared_policy(self):
  d=json.loads((ROOT/'ops/ai/policy.json').read_text())
  self.assertEqual(d['image_generation_owner'],'Codex');self.assertEqual(d['fallbacks'],[])
  self.assertFalse(d['api_auth_allowed']);self.assertFalse(d['extra_usage_allowed']);self.assertFalse(d['purchased_credit_spend_allowed'])
  self.assertFalse(d['actual_cli_call_verified'])
 def test_active_entrypoints_contain_billing_and_owner(self):
  for r in ['AGENTS.md','CLAUDE.md','START_HERE.txt','docs/START_CLAUDE.md','docs/AI_WORKFLOW.md','docs/AI_Start_Guide.md']:
   s=(ROOT/r).read_text();self.assertIn('구독',s,r);self.assertIn('Codex',s,r);self.assertIn('API',s,r)
 def test_auth_precedes_model_call(self):
  s=(ROOT/'docs/AI_WORKFLOW.md').read_text()
  self.assertIn('모든 조건이 확인된 뒤에만',s);self.assertIn('--bare는 사용하지 않는다',s)
  self.assertIn('자동 충전 OFF만으로는',s);self.assertIn('비밀값은 추출·복사·출력하지 않는다',s)
 def test_game_document_contract(self):
  s=(ROOT/'docs/design_v0.8.1.md').read_text()
  self.assertEqual(re.findall(r'^# <a id="ch(\d+)"></a>',s,re.M),list(map(str,range(24))))
  self.assertEqual(len(list((ROOT/'docs/source_v0.8.1').glob('[0-9][0-9]_chapter.md'))),24)
  self.assertEqual(s.count('**[SHEET]**'),10);self.assertEqual(s.count('**[KEYART]**'),10)
 def test_game_inventory(self):
  d=json.loads((ROOT/'data/game_data.json').read_text())
  for k,n in [('cards',173),('characters',10),('weapons',22),('parts',16),('relics',32),('enemies',20),('events',8)]:self.assertEqual(len(d[k]),n,k)
 def test_reference_and_targets_remain_distinct(self):
  s=(ROOT/'docs/CURRENT.md').read_text();self.assertIn('legacy',s);self.assertIn('독립', (ROOT/'README.md').read_text());self.assertIn('구현된 것이 아니다',s)
 def test_pdf_contains_full_base_text(self):
  import fitz
  with fitz.open(ROOT/'docs/Memory_Forge_v0.8.1_Restored_Design.pdf') as base,fitz.open(ROOT/'Memory_Forge_Complete_Design.pdf') as combined:
   self.assertEqual(base.page_count,154);self.assertEqual(combined.page_count,159)
   for i in range(base.page_count):self.assertEqual(base[i].get_text(),combined[i+5].get_text(),f'page {i+1}')
 def test_pdf_internal_links_shift(self):
  import fitz
  with fitz.open(ROOT/'docs/Memory_Forge_v0.8.1_Restored_Design.pdf') as base,fitz.open(ROOT/'Memory_Forge_Complete_Design.pdf') as combined:
   checked=0
   for i in range(base.page_count):
    a=[x['page'] for x in base[i].get_links() if x['kind'] in (fitz.LINK_GOTO,fitz.LINK_NAMED) and x.get('page',-1)>=0]
    b=[x['page'] for x in combined[i+5].get_links() if x['kind'] in (fitz.LINK_GOTO,fitz.LINK_NAMED) and x.get('page',-1)>=0]
    self.assertEqual([x+5 for x in a],b,f'links page {i+1}');checked+=len(a)
   self.assertGreater(checked,0,'Must check actual links, not an empty set')
 def test_pdf_named_destinations(self):
  from pypdf import PdfReader
  a=PdfReader(ROOT/'docs/Memory_Forge_v0.8.1_Restored_Design.pdf');b=PdfReader(ROOT/'Memory_Forge_Complete_Design.pdf')
  self.assertEqual(set(a.named_destinations),set(b.named_destinations))
  for k,d in a.named_destinations.items():self.assertEqual(a.get_destination_page_number(d)+5,b.get_destination_page_number(b.named_destinations[k]),k)
 def test_full_pdf_build_entrypoint(self):
  d=json.loads((ROOT/'package.json').read_text())
  self.assertIn('build_delivery_pdf.py',d['scripts']['docs']);self.assertIn('build_integrated_design.py',d['scripts']['docs:game'])
 def test_only_approved_licensed_font_and_no_credentials_shipped(self):
  payload=list(handoff_files(ROOT))
  self.assertEqual(font_errors(ROOT,payload),[])
  for p in payload:
   self.assertFalse(private_path(p.relative_to(ROOT)),str(p.relative_to(ROOT)))
if __name__=='__main__':unittest.main(verbosity=2)
