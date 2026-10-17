#!/usr/bin/env python3
"""Aggregate P0-1 human playtest records (docs/playtest/P0-1) against the §20 targets."""
import json, sys
from pathlib import Path

TARGET_RUNS, TARGET_UNDERSTANDING, TARGET_REPLAY = 10, 0.80, 0.70
CRACK_ANSWER = {'a', 'b', 'c'}


def aggregate(records):
    n = len(records)
    rate = lambda hits: round(hits / n, 3) if n else 0.0
    survey = [r['survey'] for r in records]
    reservation = rate(sum(s.get('q_reservation') == 'b' for s in survey))
    crack = rate(sum(set(s.get('q_crack') or []) == CRACK_ANSWER for s in survey))
    replay = rate(sum(s.get('replay') is True for s in survey))
    mean = lambda key: round(sum(s[key] for s in survey) / n, 2) if n else None
    passed = n >= TARGET_RUNS and reservation >= TARGET_UNDERSTANDING and crack >= TARGET_UNDERSTANDING and replay >= TARGET_REPLAY
    return {'records': n, 'reservation_understood': reservation, 'crack_understood': crack, 'replay_intent': replay,
            'fun_mean': mean('fun'), 'difficulty_mean': mean('difficulty'),
            'completed': sum(r['run'].get('outcome') == 'completed' for r in records),
            'used_reservation': sum(r['run'].get('used_reservation') is True for r in records),
            'targets': {'records': TARGET_RUNS, 'understanding': TARGET_UNDERSTANDING, 'replay': TARGET_REPLAY}, 'pass': passed}


def main(folder):
    records = [json.loads(p.read_text(encoding='utf-8')) for p in sorted(Path(folder).glob('*.json'))]
    print(json.dumps(aggregate(records), ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else 'reports/playtest_p0_1/records')
