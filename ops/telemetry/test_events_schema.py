"""Checks the §16.6 telemetry dictionary (ops/telemetry/events.schema.json) against the original text.

Run: python3 -m unittest discover -s ops/telemetry -v

It checks the schema itself, not any collector: there is no collection or transport code (approval boundary).
"""
import json
import re
import unittest
from pathlib import Path

try:
    import jsonschema
except ImportError:  # CI installs it with openapi-spec-validator; the local checks below do not need it.
    jsonschema = None

ROOT = Path(__file__).resolve().parents[2]
SCHEMA_PATH = ROOT / 'ops/telemetry/events.schema.json'
SOURCE_PATH = ROOT / 'docs/source_v0.8.1/16_chapter.md'
DATA_PATH = ROOT / 'data/game_data.json'
ENVELOPE = {'event', 'accountHash'}

# §16.6 "개인 식별 정보는 넣지 않는다(계정 ID는 해시)". Names are compared lowercased without _ - . so that
# accountId, account_id and ACCOUNT-ID all match. Adding a field that trips this list needs a privacy decision, not a rename.
FORBIDDEN_EXACT = {'ip', 'name', 'age', 'tel', 'lat', 'lon', 'lng', 'gps', 'city', 'country', 'locale'}
FORBIDDEN_PARTS = (
    'email', 'phone', 'address', 'ipaddr', 'device', 'android', 'advertising', 'adid', 'gaid', 'idfa', 'imei',
    'serial', 'macaddr', 'location', 'latitude', 'longitude', 'geo', 'birth', 'realname', 'fullname', 'nickname',
    'username', 'userid', 'accountid', 'playerid', 'gamertag', 'google', 'purchasetoken', 'orderid', 'token',
    'password', 'credential', 'secret', 'session', 'contact', 'message', 'chat', 'freetext', 'comment',
    # §16.3: RNG state and draw order stay server-private, so telemetry must not export them either.
    'seed', 'rng', 'draworder', 'privatestate',
)
SUPPORTED = {'$ref', 'oneOf', 'type', 'enum', 'const', 'pattern', 'minimum', 'maximum', 'minItems', 'maxItems',
             'items', 'required', 'properties', 'additionalProperties'}
ANNOTATIONS = {'$schema', '$id', 'title', 'description', '$defs'}


def load_unique(path):
    def pairs(items):
        keys = [k for k, _ in items]
        dupes = sorted({k for k in keys if keys.count(k) > 1})
        if dupes:
            raise ValueError(f'duplicate keys {dupes} in {path.name}')
        return dict(items)
    return json.loads(path.read_text(encoding='utf-8'), object_pairs_hook=pairs)


SCHEMA = load_unique(SCHEMA_PATH)


def resolve(node):
    return SCHEMA['$defs'][node['$ref'].split('/')[-1]] if '$ref' in node else node


def nodes(node, path='$'):
    yield path, node
    children = [(f'{path}.$defs.{k}', v) for k, v in node.get('$defs', {}).items()]
    children += [(f'{path}.{k}', v) for k, v in node.get('properties', {}).items()]
    children += [(f'{path}.items', node['items'])] if 'items' in node else []
    children += [(f'{path}.oneOf[{i}]', v) for i, v in enumerate(node.get('oneOf', []))]
    for child_path, child in children:
        yield from nodes(child, child_path)


def same(a, b):
    return type(a) is type(b) and a == b


def is_type(value, name):
    return {'string': isinstance(value, str), 'boolean': isinstance(value, bool), 'null': value is None,
            'integer': isinstance(value, int) and not isinstance(value, bool), 'array': isinstance(value, list),
            'object': isinstance(value, dict)}[name]


def errors(value, node, path='$'):
    """Validator for exactly the SUPPORTED keywords (test_keywords_are_supported keeps the schema inside them).
    Patterns are anchored ^...$ and matched whole, as ECMA-262 does, so a trailing newline cannot slip through."""
    if '$ref' in node:
        return errors(value, resolve(node), path)
    found = []
    if 'oneOf' in node:
        hits = sum(not errors(value, option, path) for option in node['oneOf'])
        found += [] if hits == 1 else [f'{path}: matches {hits} oneOf branches']
    types = node.get('type')
    if types is not None and not any(is_type(value, t) for t in ([types] if isinstance(types, str) else types)):
        return found + [f'{path}: not {types}']
    if 'const' in node and not same(value, node['const']):
        found.append(f'{path}: not const')
    if 'enum' in node and not any(same(value, option) for option in node['enum']):
        found.append(f'{path}: not in enum')
    if isinstance(value, str) and 'pattern' in node and not re.fullmatch(node['pattern'][1:-1], value):
        found.append(f'{path}: pattern')
    if is_type(value, 'integer'):
        if value < node.get('minimum', value) or value > node.get('maximum', value):
            found.append(f'{path}: out of range')
    if isinstance(value, list):
        if len(value) < node.get('minItems', 0) or len(value) > node.get('maxItems', len(value)):
            found.append(f'{path}: item count')
        for i, item in enumerate(value):
            found += errors(item, node.get('items', {}), f'{path}[{i}]')
    if isinstance(value, dict):
        props = node.get('properties', {})
        found += [f'{path}: missing {k}' for k in node.get('required', []) if k not in value]
        if node.get('additionalProperties', True) is False:
            found += [f'{path}: extra {k}' for k in value if k not in props]
        for k, v in value.items():
            if k in props:
                found += errors(v, props[k], f'{path}.{k}')
    return found


def camel(text):
    words = text.split()
    return words[0] + ''.join(w[:1].upper() + w[1:] for w in words[1:])


def source_fields(cell):
    """'runId, party[charId,res], battleId/runId, target tier' -> {'runId': None, 'party': ['charId', 'res'], ...}."""
    fields, depth, token = {}, 0, ''
    for ch in cell + ',':
        if ch == ',' and depth == 0:
            token = token.strip()
            if '[' in token:
                head, inner = token[:-1].split('[', 1)
                fields[camel(head)] = [camel(x.strip()) for x in inner.split(',')]
            else:
                fields.update({camel(part.strip()): None for part in token.split('/')})
            token = ''
        else:
            depth += (ch == '[') - (ch == ']')
            token += ch
    return fields


def source_table():
    text = SOURCE_PATH.read_text(encoding='utf-8')
    section = text.split('## 16.6', 1)[1].split('\n## ', 1)[0]
    table = [line.strip() for line in section.splitlines() if line.strip().startswith('|')]
    assert re.fullmatch(r'\|\s*이벤트\s*\|\s*필드\s*\|', table[0]), f'unexpected §16.6 header: {table[0]}'
    assert set(table[1]) <= set('|-: '), 'missing §16.6 table separator'
    rows = {}
    for line in table[2:]:  # every data row counts, with or without backticks
        cells = [c.strip() for c in line.strip('|').split('|')]
        assert len(cells) == 2, f'unexpected §16.6 row: {line}'
        event_cell, field_cell = cells
        sample = re.search(r'\(표본 (\d+)%\)', event_cell)
        for raw in re.sub(r'\(표본 \d+%\)', '', event_cell).split('/'):
            name = raw.strip().strip('`')
            assert re.fullmatch(r'[a-z][a-z_]*[a-z]', name), f'unrecognized §16.6 event syntax: {raw!r}'
            assert name not in rows, f'source lists {name} twice'
            rows[name] = (source_fields(field_cell), int(sample.group(1)) / 100 if sample else None)
    return section, rows


def event_defs():
    return {d['properties']['event']['const']: d for d in SCHEMA['$defs'].values()
            if 'event' in d.get('properties', {})}


PARTY = [{'charId': 'sera', 'res': 0, 'level': 1, 'weaponId': 'w_sera'},
         {'charId': 'nia', 'res': 6, 'level': 60, 'weaponId': None}]
RUN = {'accountHash': '0' * 64, 'runId': 'pr-' + '0' * 32, 'mode': 'short', 'chapter': 0, 'party': PARTY,
       'difficulty': 'normal'}
VALID = {
    'run_start': {'event': 'run_start', **RUN},
    'run_end': {'event': 'run_end', **RUN, 'result': 'clear', 'floorsCleared': 3, 'turns': 40, 'durationSec': 900},
    'battle_end': {'event': 'battle_end', 'battleId': 'b1', 'runId': 'pr-' + '0' * 32, 'encounter': '1:normal:0',
                   'result': 'win', 'turns': 4, 'hpLost': 12, 'breaks': 1, 'reservesMade': 2, 'ultsUsed': 1},
    'card_play': {'event': 'card_play', 'cardId': 'sera_round', 'mode': 'next', 'cost': 1, 'apLeft': -1,
                  'targetTier': None, 'previewDmg': 0, 'actualDmg': 0},
    'memory_commit': {'event': 'memory_commit', 'charId': 'sera', 'capacity': 10, 'used': 7,
                      'changes': [{'type': 'add', 'cost': 2}], 'defeat': False},
    'gacha_pull': {'event': 'gacha_pull', 'bannerId': 'limited_lumi', 'count': 1,
                   'results': [{'rarity': 'SSR', 'id': 'lumi', 'featured': True, 'lost': False}], 'pityBefore': 0,
                   'currencyMix': [{'currency': 'limitedTicket', 'amount': 1}]},
    'purchase': {'event': 'purchase', 'productId': 'pk_160', 'priceKrw': 2400, 'firstBonus': 0, 'result': 'ok'},
    'tutorial_step': {'event': 'tutorial_step', 'step': 'battle_intro', 'durationSec': 30, 'skipped': False},
    'error_shown': {'event': 'error_shown', 'code': 'STALE_VERSION', 'screen': 'battle'},
}


def invalid_samples():
    """(label, payload) pairs that must all be refused; the trailing-newline case is ECMA-only (see errors())."""
    out = []
    for name, sample in VALID.items():
        out.append((f'{name}+email', {**sample, 'email': 'player@example.com'}))
        out.append((f'{name}+rawAccountId', {**sample, 'accountId': 'dev-local'}))
        out.append((f'{name} bad hash', {**sample, 'accountHash': 'ABC'}))
        out += [(f'{name}-{k}', {x: v for x, v in sample.items() if x != k})
                for k in event_defs()[name]['required']]
    out += [
        ('unknown event', {**VALID['error_shown'], 'event': 'screen_view'}),
        ('email as id', {**VALID['error_shown'], 'screen': 'player@example.com'}),
        ('space in id', {**VALID['tutorial_step'], 'step': 'free text'}),
        ('card mode', {**VALID['card_play'], 'mode': 'later'}),
        ('card tier', {**VALID['card_play'], 'targetTier': 'king'}),
        ('bool as int', {**VALID['battle_end'], 'turns': True}),
        ('negative count', {**VALID['battle_end'], 'hpLost': -1}),
        ('pull count', {**VALID['gacha_pull'], 'count': 5}),
        ('pull rarity', {**VALID['gacha_pull'], 'results': [{**VALID['gacha_pull']['results'][0], 'rarity': 'UR'}]}),
        ('empty party', {**VALID['run_start'], 'party': []}),
        ('res over 6', {**VALID['run_start'], 'party': [{**PARTY[0], 'res': 7}]}),
        ('party extra', {**VALID['run_start'], 'party': [{**PARTY[0], 'nickname': 'x'}]}),
        ('change extra', {**VALID['memory_commit'], 'changes': [{'type': 'add', 'cost': 1, 'note': 'x'}]}),
    ]
    return out


class TelemetrySchema(unittest.TestCase):
    def test_keywords_are_supported(self):
        for path, node in nodes(SCHEMA):
            unknown = {k for k in node if k not in SUPPORTED | ANNOTATIONS and not k.startswith('x-')}
            self.assertFalse(unknown, f'{path}: keyword(s) the local validator does not implement: {unknown}')
            if '$ref' in node:
                self.assertLessEqual(set(node), {'$ref', 'description'}, f'{path}: $ref siblings')
            if 'pattern' in node:
                self.assertRegex(node['pattern'], r'^\^.*\$$', f'{path}: unanchored pattern')

    def test_event_names_are_unique_and_match_source(self):
        _, rows = source_table()
        defs = event_defs()
        targets = [ref['$ref'].split('/')[-1] for ref in SCHEMA['oneOf']]
        self.assertEqual(len(targets), len(set(targets)), 'oneOf lists an event twice')
        self.assertEqual(set(targets), set(defs), 'oneOf and event definitions differ')
        for key, node in SCHEMA['$defs'].items():
            if 'event' in node.get('properties', {}):
                self.assertEqual(key, node['properties']['event']['const'], f'{key}: const differs from its key')
        self.assertEqual(set(defs), set(rows), 'events differ from §16.6')
        for name in defs:
            self.assertRegex(name, r'^[a-z][a-z_]*[a-z]$')

    def test_fields_and_required_match_source(self):
        _, rows = source_table()
        for name, node in event_defs().items():
            fields, _ = rows[name]
            props = set(node['properties'])
            omitted = node.get('x-omittedSourceFields', {})
            self.assertTrue(all(isinstance(r, str) and r.strip() for r in omitted.values()), f'{name}: omission without reason')
            self.assertEqual(props - ENVELOPE | set(omitted), set(fields), f'{name}: fields differ from §16.6')
            self.assertFalse(props & set(omitted), f'{name}: field both present and omitted')
            self.assertEqual(set(node['required']), props - {'accountHash'}, f'{name}: every source field is required')
            for field, inner in fields.items():
                if inner is None or field in omitted:
                    continue
                item = resolve(resolve(node['properties'][field])['items'])
                self.assertEqual(set(item['properties']), set(inner), f'{name}.{field}[]: item fields differ')
                self.assertEqual(set(item['required']), set(inner), f'{name}.{field}[]: item fields required')

    def test_sampling_and_retention_follow_source(self):
        section, rows = source_table()
        for name, node in event_defs().items():
            self.assertEqual(node.get('x-sampleRate'), rows[name][1], f'{name}: sample rate')
        self.assertEqual(SCHEMA['x-retentionMonths'], int(re.search(r'보존 기간 (\d+)개월', section).group(1)))
        self.assertIn('계정 ID는 해시', section)

    def test_no_personal_or_hidden_fields(self):
        for path, node in nodes(SCHEMA):
            for prop in node.get('properties', {}):
                flat = re.sub(r'[_.\-]', '', prop).lower()
                hit = [p for p in FORBIDDEN_PARTS if p in flat]
                self.assertFalse(flat in FORBIDDEN_EXACT or hit, f'{path}.{prop}: forbidden field name {hit or flat}')

    def test_objects_are_closed_and_strings_are_not_free_text(self):
        probes = ['player@example.com', 'free text', 'x' * 65, '']
        for path, node in nodes(SCHEMA):
            types = node.get('type')
            types = [types] if isinstance(types, str) else (types or [])
            if 'object' in types or 'properties' in node:
                self.assertIs(node.get('additionalProperties'), False, f'{path}: open object')
                self.assertIn('properties', node, f'{path}: object without fields')
            if 'string' in types:
                self.assertTrue({'pattern', 'enum', 'const'} & set(node), f'{path}: free-text string')
            if 'pattern' in node:
                for probe in probes:
                    self.assertFalse(re.fullmatch(node['pattern'][1:-1], probe), f'{path}: pattern accepts {probe!r}')

    def test_enums_stay_inside_game_data(self):
        data = json.loads(DATA_PATH.read_text(encoding='utf-8'))
        defs = event_defs()
        tiers = {t for t in defs['card_play']['properties']['targetTier']['enum'] if t is not None}
        self.assertLessEqual(tiers, {e['tier'] for e in data['enemies']})
        rarities = set(resolve(defs['gacha_pull']['properties']['results']['items'])['properties']['rarity']['enum'])
        self.assertLessEqual(rarities, {c['rarity'] for c in data['characters']} | {w['rarity'] for w in data['weapons']})

    def test_samples(self):
        self.assertEqual(set(VALID), set(event_defs()))
        for name, sample in VALID.items():
            self.assertEqual(errors(sample, SCHEMA), [], name)
        for label, sample in invalid_samples() + [('newline', {**VALID['error_shown'], 'screen': 'battle\n'})]:
            self.assertTrue(errors(sample, SCHEMA), f'accepted: {label}')

    @unittest.skipIf(jsonschema is None, 'jsonschema not installed; CI installs it with openapi-spec-validator')
    def test_reference_validator_agrees(self):
        validator = jsonschema.Draft202012Validator(SCHEMA)
        jsonschema.Draft202012Validator.check_schema(SCHEMA)
        for name, sample in VALID.items():
            self.assertTrue(validator.is_valid(sample), name)
        for label, sample in invalid_samples():
            self.assertFalse(validator.is_valid(sample), label)


if __name__ == '__main__':
    unittest.main()
