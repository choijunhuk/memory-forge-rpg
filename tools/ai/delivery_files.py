"""Files eligible for delivery; runtime caches and local account evidence stay local."""
from pathlib import Path
import subprocess


def private_path(relative):
    relative = Path(relative)
    return (relative.parts[:3] == ('ops', 'ai', 'local')
            or relative.name in {'.credentials.json', 'auth.json', 'settings.local.json'}
            or relative.name == '.env'
            or relative.name.startswith('.env.') and relative.name != '.env.example')


def handoff_files(root):
    root = Path(root)
    if (root / '.git').exists():
        return delivery_files(root)
    # An extracted ZIP has already shipped: inspect its actual payload, including local evidence.
    return (path for path in root.rglob('*') if path.is_file())


def delivery_files(root):
    root = Path(root)
    using_git = (root / '.git').exists()
    if using_git:
        result = subprocess.run(['git', '-C', str(root), 'ls-files', '-z', '--cached',
                                 '--others', '--exclude-standard'], check=True, capture_output=True)
        paths = (root / name.decode('utf-8') for name in result.stdout.split(b'\0') if name)
    else:
        paths = root.rglob('*')
    for path in sorted(set(paths)):
        relative = path.relative_to(root)
        if not path.is_file():
            continue
        if using_git:
            yield path
            continue
        if any(part in {'.git', 'node_modules', '__pycache__', 'bin', 'obj'} for part in relative.parts):
            continue
        if relative.parts[:3] == ('ops', 'ai', 'local'):
            continue
        if (relative.parts[:2] == ('client', 'MemoryForgeUnity') and len(relative.parts) > 2
                and relative.parts[2] in {'Library', 'Temp', 'Obj', 'Logs', 'UserSettings', 'Build', 'Builds', '.vs'}):
            continue
        yield path
