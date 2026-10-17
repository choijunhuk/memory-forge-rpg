"""Mutation check for ops/telemetry/test_events_schema.py: each broken schema copy must make the suite fail.
Run from the repository root: python3 reports/ops_specs_20261017/schema_mutation_check.py"""
import copy, io, json, sys, unittest
sys.path.insert(0, 'ops/telemetry')
import test_events_schema as t
base = copy.deepcopy(t.SCHEMA)
def run(label, mutate):
    s = copy.deepcopy(base); mutate(s); t.SCHEMA.clear(); t.SCHEMA.update(s)
    r = unittest.TextTestRunner(stream=io.StringIO()).run(unittest.defaultTestLoader.loadTestsFromTestCase(t.TelemetrySchema))
    print(f'{label}: failures={len(r.failures)+len(r.errors)}', 'OK-caught' if (r.failures or r.errors) else 'MISSED')
D = lambda s: s['$defs']
run('add email prop', lambda s: D(s)['purchase']['properties'].update(email={'$ref': '#/$defs/id'}))
run('add userId in party', lambda s: D(s)['party']['items']['properties'].update(user_id={'$ref': '#/$defs/id'}))
run('drop source field', lambda s: D(s)['battle_end']['properties'].pop('ultsUsed'))
run('field not required', lambda s: D(s)['card_play']['required'].remove('actualDmg'))
run('open object', lambda s: D(s)['tutorial_step'].update(additionalProperties=True))
run('free text string', lambda s: D(s)['error_shown']['properties'].update(screen={'type': 'string'}))
run('loose id pattern', lambda s: D(s)['id'].update(pattern='^.*$'))
run('extra event', lambda s: (D(s).update(screen_view={'type':'object','additionalProperties':False,'required':['event'],'properties':{'event':{'const':'screen_view'}}}), s['oneOf'].append({'$ref':'#/$defs/screen_view'})))
run('missing event', lambda s: s['oneOf'].pop())
run('sample rate', lambda s: D(s)['card_play'].update({'x-sampleRate': 1}))
run('retention', lambda s: s.update({'x-retentionMonths': 24}))
run('omit w/o reason', lambda s: D(s)['run_start']['x-omittedSourceFields'].update(result=''))
run('unsupported kw', lambda s: D(s)['count'].update(exclusiveMinimum=0))
run('seed field', lambda s: D(s)['run_end']['properties'].update(runSeed={'$ref': '#/$defs/count'}))
run('root oneOf sibling type', lambda s: s.update(type='integer'))
import tempfile as _tf
from pathlib import Path as _P
_orig = t.SOURCE_PATH
with _tf.TemporaryDirectory() as _d:
    _text = _orig.read_text(encoding='utf-8').replace('| `error_shown` | code, screen |', '| `error_shown` | code, screen |\n| crash_report | code, screen |')
    assert '| crash_report |' in _text
    t.SOURCE_PATH = _P(_d) / '16_chapter.md'; t.SOURCE_PATH.write_text(_text, encoding='utf-8')
    run('source row without backticks', lambda s: None)
t.SOURCE_PATH = _orig
import json as _json, tempfile as _tempfile
from pathlib import Path as _Path
with _tempfile.TemporaryDirectory() as _d:
    _p = _Path(_d) / 'dup.json'; _p.write_text('{"a":1,"a":2}')
    try:
        t.load_unique(_p); print('duplicate key: MISSED')
    except ValueError:
        print('duplicate key: OK-caught')
