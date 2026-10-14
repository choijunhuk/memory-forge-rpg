import importlib.util
from pathlib import Path
import subprocess
import shutil
import sys
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location('delivery_files', Path(__file__).resolve().parents[1] / 'tools/ai/delivery_files.py')
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


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


if __name__ == '__main__':
    unittest.main()
