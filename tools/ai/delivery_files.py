"""Files eligible for delivery; runtime caches and local account evidence stay local."""
from pathlib import Path
import hashlib
import subprocess

FONT_SUFFIXES = {'.ttf', '.otf', '.ttc', '.woff', '.woff2'}
# The only shippable font: unchanged google/fonts ofl/ibmplexsanskr files required by design ch.14
# (reports/unity_client_20261015/font_provenance.json). New fonts need their own reviewed provenance entry.
APPROVED_FONTS = {
    'client/MemoryForgeUnity/Assets/Resources/Fonts/IBMPlexSansKR-Regular.ttf': (
        '53750379270312368cf7641901f43a98dd892e3d9d5798cf25cdc245c85c71c0',
        'client/MemoryForgeUnity/Assets/Resources/Fonts/IBMPlexSansKR-OFL.txt',
        '7e6b2818edbd8f6a01ae80641cc8f16a51080d08fb4e532be3a0b6f74adb07da'),
}
FONT_ERROR_LIMIT = 20


def license_sha256(data):
    # The recorded hash is of the upstream all-CRLF copy; Git eol=lf may check it out as LF.
    return hashlib.sha256(data.replace(b'\r\n', b'\n').replace(b'\n', b'\r\n')).hexdigest()


def font_errors(root, paths):
    """Fonts may ship only as the approved byte-exact file with its delivered license copy."""
    root = Path(root)
    paths = {Path(path).relative_to(root).as_posix() for path in paths}
    errors = []
    for relative in sorted(paths):
        if Path(relative).suffix.lower() not in FONT_SUFFIXES:
            continue
        if relative not in APPROVED_FONTS:
            errors.append(f'Unapproved font: {relative}')
            continue
        font_sha, license_path, license_sha = APPROVED_FONTS[relative]
        if hashlib.sha256((root / relative).read_bytes()).hexdigest() != font_sha:
            errors.append(f'Approved font bytes changed: {relative}')
        if license_path not in paths:
            errors.append(f'Font license copy missing: {license_path}')
        elif license_sha256((root / license_path).read_bytes()) != license_sha:
            errors.append(f'Font license copy changed: {license_path}')
    if len(errors) > FONT_ERROR_LIMIT:
        errors = errors[:FONT_ERROR_LIMIT] + [f'... {len(errors) - FONT_ERROR_LIMIT} more font errors']
    return errors


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
