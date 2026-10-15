import importlib.util
import json
from pathlib import Path
import subprocess
import shutil
import sys
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('delivery_files', REPO / 'tools/ai/delivery_files.py')
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)
FONT = 'client/MemoryForgeUnity/Assets/Resources/Fonts/IBMPlexSansKR-Regular.ttf'
LICENSE = 'client/MemoryForgeUnity/Assets/Resources/Fonts/IBMPlexSansKR-OFL.txt'


class DeliveryFiles(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)

    def write(self, name, value='fixture'):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(value)
        return path

    def names(self):
        return {p.relative_to(self.root).as_posix() for p in MODULE.delivery_files(self.root)}

    def copy_source(self, name, target=None, data=None):
        # Real repository bytes, so success cannot come from a fixture with a weakened hash.
        path = self.root / (target or name)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes((REPO / name).read_bytes() if data is None else data)
        return path

    def font_errors(self):
        return MODULE.font_errors(self.root, MODULE.handoff_files(self.root))

    def run_manifest(self):
        for name in ['build_manifest.py', 'delivery_files.py']:
            path = self.root / 'tools/ai' / name
            path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(REPO / 'tools/ai' / name, path)
        return subprocess.run([sys.executable, str(self.root / 'tools/ai/build_manifest.py')],
                              capture_output=True, text=True)

    def assert_previous_manifests_kept(self):
        for name in ['MANIFEST_AIO.json', 'MANIFEST.sha256']:
            self.assertEqual((self.root / name).read_text(), 'preserve previous manifest')

    def test_git_inventory_excludes_ignored_caches_but_keeps_forced_credentials_and_fonts(self):
        subprocess.run(['git', '-c', 'init.defaultBranch=main', 'init', '-q', str(self.root)], check=True)
        self.write('.gitignore', 'ignored/\n.env\nclient/MemoryForgeUnity/Library/\n')
        self.write('ignored/cache.ttf')
        self.write('art/real.ttf')
        self.write('.env')
        self.write('client/MemoryForgeUnity/Library/forced.otf')
        subprocess.run(['git', '-C', str(self.root), 'add', '-f', '.env',
                        'client/MemoryForgeUnity/Library/forced.otf'], check=True)
        self.assertEqual(self.names(), {'.gitignore', '.env', 'art/real.ttf',
                                        'client/MemoryForgeUnity/Library/forced.otf'})

    def test_unversioned_delivery_excludes_runtime_paths_and_keeps_shipped_assets(self):
        for name in ['ops/ai/local/auth.json', 'client/MemoryForgeUnity/Library/cache/font.ttf',
                     'client/MemoryForgeUnity/Builds/app/font.ttf', 'node_modules/font.ttf',
                     'core/MemoryForge.Core/obj/cache', '__pycache__/cache']:
            self.write(name)
        for name in ['art/real.ttf', 'client/MemoryForgeUnity/Assets/real.otf', 'auth.json', '.env', 'docs/guide.md']:
            self.write(name)
        self.assertEqual(self.names(), {'art/real.ttf', 'client/MemoryForgeUnity/Assets/real.otf',
                                        'auth.json', '.env', 'docs/guide.md'})

    def test_git_errors_fail_instead_of_returning_an_empty_successful_inventory(self):
        self.write('.git', 'gitdir: missing\n')
        with self.assertRaises(subprocess.CalledProcessError):
            self.names()

    def test_extracted_zip_checks_actual_private_payload_instead_of_hiding_it(self):
        names = ['ops/ai/local/job.auth.stdout.json', 'ops/ai/local/runtime-billing-evidence.json',
                 '.claude/settings.local.json', '.env.local', '.env', '.codex/auth.json']
        for name in names:
            self.write(name)
        self.write('.env.example')
        payload = {p.relative_to(self.root).as_posix() for p in MODULE.handoff_files(self.root)}
        self.assertEqual(payload, {*names, '.env.example'})
        self.assertTrue(all(MODULE.private_path(name) for name in names))
        self.assertFalse(MODULE.private_path('.env.example'))

    def test_manifest_refuses_private_zip_payload_before_replacing_existing_manifests(self):
        source = Path(__file__).resolve().parents[1] / 'tools/ai'
        for name in ['build_manifest.py', 'delivery_files.py']:
            path = self.root / 'tools/ai' / name
            path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source / name, path)
        self.write('ops/ai/local/job.auth.stdout.json')
        for name in ['MANIFEST_AIO.json', 'MANIFEST.sha256']:
            self.write(name, 'preserve previous manifest')
        result = subprocess.run([sys.executable, str(self.root / 'tools/ai/build_manifest.py')],
                                capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Local account files cannot be delivered', result.stderr)
        for name in ['MANIFEST_AIO.json', 'MANIFEST.sha256']:
            self.assertEqual((self.root / name).read_text(), 'preserve previous manifest')

    def test_approved_font_ships_byte_exact_with_crlf_or_lf_license_copy(self):
        self.copy_source(FONT)
        lf = (REPO / LICENSE).read_bytes().replace(b'\r\n', b'\n')
        for license_bytes in [lf.replace(b'\n', b'\r\n'), lf]:
            self.copy_source(LICENSE, data=license_bytes)
            self.assertEqual(self.font_errors(), [])

    def test_fonts_outside_the_single_approved_path_are_rejected(self):
        self.copy_source(FONT)
        self.copy_source(LICENSE)
        moved = 'client/MemoryForgeUnity/Assets/Fonts/IBMPlexSansKR-Regular.ttf'
        self.copy_source(FONT, moved)
        self.copy_source(LICENSE, 'client/MemoryForgeUnity/Assets/Fonts/IBMPlexSansKR-OFL.txt')
        self.write('art/Title.OTF')
        self.write('art/real.woff2')
        self.assertEqual(self.font_errors(), ['Unapproved font: art/Title.OTF', 'Unapproved font: art/real.woff2',
                                              f'Unapproved font: {moved}'])

    def test_altered_approved_font_bytes_are_rejected(self):
        self.copy_source(FONT, data=(REPO / FONT).read_bytes() + b'\0')
        self.copy_source(LICENSE)
        self.assertEqual(self.font_errors(), [f'Approved font bytes changed: {FONT}'])

    def test_missing_altered_or_undelivered_license_copy_is_rejected(self):
        self.copy_source(FONT)
        self.assertEqual(self.font_errors(), [f'Font license copy missing: {LICENSE}'])
        source = (REPO / LICENSE).read_bytes()
        altered = source.replace(b'sold by itself', b'sold freely')
        self.assertNotEqual(altered, source)
        self.copy_source(LICENSE, data=altered)
        self.assertEqual(self.font_errors(), [f'Font license copy changed: {LICENSE}'])
        self.copy_source(LICENSE)
        subprocess.run(['git', '-c', 'init.defaultBranch=main', 'init', '-q', str(self.root)], check=True)
        self.write('.gitignore', LICENSE + '\n')
        self.assertEqual(self.font_errors(), [f'Font license copy missing: {LICENSE}'])

    def test_font_errors_are_bounded(self):
        for index in range(MODULE.FONT_ERROR_LIMIT + 5):
            self.write(f'art/font{index:02}.ttf')
        errors = self.font_errors()
        self.assertEqual(len(errors), MODULE.FONT_ERROR_LIMIT + 1)
        self.assertEqual(errors[-1], '... 5 more font errors')

    def test_manifest_records_approved_font_and_license(self):
        self.copy_source(FONT)
        self.copy_source(LICENSE)
        result = self.run_manifest()
        self.assertEqual(result.returncode, 0, result.stderr)
        entries = {item['path']: item['sha256']
                   for item in json.loads((self.root / 'MANIFEST_AIO.json').read_text())['files']}
        self.assertEqual(entries[FONT], MODULE.APPROVED_FONTS[FONT][0])
        self.assertIn(LICENSE, entries)

    def test_manifest_refuses_unapproved_font_before_replacing_existing_manifests(self):
        self.copy_source(FONT)
        self.copy_source(LICENSE)
        self.write('art/real.ttf')
        for name in ['MANIFEST_AIO.json', 'MANIFEST.sha256']:
            self.write(name, 'preserve previous manifest')
        result = self.run_manifest()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Font files cannot be delivered: Unapproved font: art/real.ttf', result.stderr)
        self.assert_previous_manifests_kept()

    def test_manifest_still_refuses_private_payload_next_to_approved_font(self):
        self.copy_source(FONT)
        self.copy_source(LICENSE)
        self.write('.codex/auth.json')
        for name in ['MANIFEST_AIO.json', 'MANIFEST.sha256']:
            self.write(name, 'preserve previous manifest')
        result = self.run_manifest()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Local account files cannot be delivered: .codex/auth.json', result.stderr)
        self.assert_previous_manifests_kept()


if __name__ == '__main__':
    unittest.main()
