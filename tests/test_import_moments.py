import os
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
import import_moments as M  # noqa: E402


class ImportMomentsTest(unittest.TestCase):
    def test_referenced_skips_malformed_structures(self):
        data = {'images': None, 'motions': {'a': None, 'b': {'frames': None}, 'c': {'frames': ['x.png', 3]}},
                'scenes': [None, {'beats': None}, {'beats': [None, {'voice': 'v.ogg'}, {'voice': 5}]}]}
        self.assertEqual(M.referenced(data), ['v.ogg', 'x.png'])
        self.assertEqual(M.referenced({'images': {'a': 'i.png', 'b': None}}), ['i.png'])

    def test_clean_rejects_escapes(self):
        self.assertEqual(M.clean('moments/sera/a.png'), 'moments/sera/a.png')
        for bad in ('../x.png', '/etc/passwd', 'moments/../../x', ''):
            self.assertIsNone(M.clean(bad), bad)

    def test_contained_refuses_symlinks_and_outside_paths(self):
        with tempfile.TemporaryDirectory() as tmp:
            base = Path(tmp) / 'art'; (base / 'm').mkdir(parents=True)
            outside = Path(tmp) / 'outside.png'; outside.write_bytes(b'x')
            (base / 'm' / 'ok.png').write_bytes(b'x')
            os.symlink(outside, base / 'm' / 'link.png')
            os.symlink(Path(tmp), base / 'dirlink')
            self.assertTrue(M.contained(base / 'm' / 'ok.png', base))
            self.assertFalse(M.contained(base / 'm' / 'link.png', base))
            self.assertFalse(M.contained(base / 'dirlink' / 'outside.png', base))
            self.assertFalse(M.contained(outside, base))


    def test_refused_destination_creates_nothing_outside(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            art = tmp / 'art'; (art / 'moments' / 'x' / 'images').mkdir(parents=True)
            (art / 'moments' / 'x' / 'images' / 'a.png').write_bytes(b'png')
            (art / 'moments' / 'x' / 'moments.json').write_text('{"format": "memory-forge-moments/1", "character": "x", "images": {"a": "moments/x/images/a.png"}}')
            dest = tmp / 'dest'; dest.mkdir()
            outside = tmp / 'outside'; outside.mkdir()
            os.symlink(outside, dest / 'moments')  # Resources/Art/moments points outside the destination
            result = M.import_one(art / 'moments' / 'x' / 'moments.json', art=art, dest=dest)
            self.assertIn('refused', result)
            self.assertEqual(list(outside.iterdir()), [], 'nothing was created or copied outside the destination')

    def test_import_copies_json_and_referenced_art_byte_for_byte(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            art = tmp / 'art'; (art / 'moments' / 'x' / 'images').mkdir(parents=True)
            (art / 'moments' / 'x' / 'images' / 'a.png').write_bytes(b'png-bytes')
            source = art / 'moments' / 'x' / 'moments.json'
            source.write_text('{"format": "memory-forge-moments/1", "character": "x", "images": {"a": "moments/x/images/a.png", "b": "moments/x/images/todo.png"}}')
            dest = tmp / 'dest'; dest.mkdir()
            result = M.import_one(source, art=art, dest=dest)
            self.assertEqual(result['missing'], ['moments/x/images/todo.png'])
            self.assertEqual((dest / 'moments/x/images/a.png').read_bytes(), b'png-bytes')
            self.assertEqual((dest / 'moments/x/moments.json').read_bytes(), source.read_bytes())


if __name__ == '__main__':
    unittest.main()
