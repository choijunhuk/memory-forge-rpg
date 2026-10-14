from pathlib import Path
import hashlib,json,sys
root=Path(__file__).resolve().parents[1]
required=['docs/design_v0.8.md','docs/START_CODEX.md','docs/START_CLAUDE.md','data/design_policy_v0.8.json','engine/rules-core.js','prototype/index.html','art/briefs/characters_v0.8.json']
for rel in required:
    assert (root/rel).is_file(), f'Missing {rel}'
p=json.loads((root/'data/design_policy_v0.8.json').read_text(encoding='utf-8'))
s=json.loads((root/'reports/integration_v080/status.json').read_text(encoding='utf-8'))
assert p['memory']['html_connected']==s['features']['preserve_previous_memory_v2']['runHtmlConnected']
assert p['execution_reference']['business_unchanged'] is True
for rel,expected in s['business_source_hashes'].items():
    assert hashlib.sha256((root/rel).read_bytes()).hexdigest()==expected, f'Unexpected business source change: {rel}'
text=(root/'docs/design_v0.8.md').read_text(encoding='utf-8')
for n in range(1,24):
    import re
    assert re.search(r'^#{1,3}\s+'+str(n)+r'\.\s',text,re.M), f'Missing full chapter {n}'
assert len(json.loads((root/'art/briefs/characters_v0.8.json').read_text(encoding='utf-8'))['characters'])==10
print('Full-document chapter inventory, policy status, art inventory and unchanged business sources: PASS')
