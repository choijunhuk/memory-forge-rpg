#!/usr/bin/env python3
"""Regression tests for the document build, including the actual broken v0.8 fixture."""
import unittest,json,re
from pathlib import Path
from document_contract import evaluate,chapters
ROOT=Path(__file__).resolve().parents[2]
class DocumentContractTests(unittest.TestCase):
 @classmethod
 def setUpClass(cls):
  cls.data=json.loads((ROOT/'data/game_data.json').read_text());cls.current=(ROOT/'docs/design_v0.8.1.md').read_text()
 def test_complete_current_passes(self):self.assertTrue(evaluate(self.current,self.data)['pass'])
 def test_actual_broken_v08_fails(self):
  result=evaluate((ROOT/'docs/design_v0.8.md').read_text(),self.data)
  self.assertFalse(result['pass']);self.assertGreater(len(result['errors']),20)
 def test_missing_combat_chapter_fails(self):
  text=self.current.replace(chapters(self.current)[5],'')
  self.assertFalse(evaluate(text,self.data)['pass'])
 def test_character_numbers_cannot_replace_chapters(self):
  text=self.current.replace('# <a id="ch5"></a>5.', '### 5.')
  self.assertFalse(evaluate(text,self.data)['pass'])
 def test_missing_sera_sheet_fails_even_with_card_index(self):
  text=re.sub(r'^### <a id="char-sera".*?(?=^### <a id="char-nia")','',self.current,flags=re.M|re.S)
  self.assertFalse(evaluate(text,self.data)['pass'])
 def test_missing_sion_specific_card_fails(self):
  before,sep,after=self.current.partition('### <a id="char-sion"')
  body,end,rest=after.partition('### <a id="char-kaya"')
  text=before+sep+body.replace('`sion_lastpage`','`not_the_card`')+end+rest
  self.assertFalse(evaluate(text,self.data)['pass'])
 def test_missing_prompt_fails(self):
  self.assertFalse(evaluate(self.current.replace('**[SHEET]**','**[OMITTED]**',1),self.data)['pass'])
 def test_duplicate_chapter_fails(self):
  self.assertFalse(evaluate(self.current+'\n# <a id="ch5"></a>5. duplicate\n',self.data)['pass'])
 def test_relic_listing_cannot_be_replaced_by_name_only(self):
  ch=chapters(self.current)[7];t=self.current.replace(ch,ch.replace('`symbiotic_moss`','공생 이끼'))
  self.assertFalse(evaluate(t,self.data)['pass'])
if __name__=='__main__':unittest.main(verbosity=2)
