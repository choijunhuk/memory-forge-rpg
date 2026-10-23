import json, sys, unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools/playtest'))
import aggregate_p0_1 as A  # noqa: E402


def record(reservation='b', crack=('a', 'b', 'c'), replay=True, fun=4, difficulty=3):
    return {'run': {'outcome': 'completed', 'used_reservation': True},
            'survey': {'q_reservation': reservation, 'q_crack': list(crack), 'replay': replay, 'fun': fun, 'difficulty': difficulty}}


class PlaytestAggregateTests(unittest.TestCase):
    def test_template_is_a_valid_record(self):
        template = json.loads((ROOT / 'docs/playtest/P0-1/record.template.json').read_text(encoding='utf-8'))
        self.assertEqual(A.aggregate([template])['records'], 1)

    def test_targets_need_ten_runs_and_exact_crack_answer(self):
        self.assertFalse(A.aggregate([record()] * 9)['pass'])
        self.assertTrue(A.aggregate([record()] * 10)['pass'])
        partial = A.aggregate([record(crack=('a', 'b'))] * 3 + [record()] * 7)
        self.assertEqual(partial['crack_understood'], 0.7)
        self.assertFalse(partial['pass'])
        self.assertFalse(A.aggregate([record(replay=False)] * 4 + [record()] * 6)['pass'])
        self.assertEqual(A.aggregate([record(reservation='a')] * 2 + [record()] * 8)['reservation_understood'], 0.8)


if __name__ == '__main__':
    unittest.main()
