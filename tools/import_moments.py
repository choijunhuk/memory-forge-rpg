#!/usr/bin/env python3
"""Copy Codex's moment files and the art they reference into Unity, byte for byte.

art/moments/<id>/moments.json and every path it names (images, motion frames, voice) are copied to
client/MemoryForgeUnity/Assets/Resources/Art/<same path>. Nothing is edited (OWNERSHIP.md); tools/check_art_copies.py
verifies the copies. Referenced files that do not exist yet are listed, not invented. A source or destination that is a
symlink or resolves outside art/ or the Resources folder is refused. Usage: import_moments.py [id ...]
"""
import json
import shutil
import sys
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / 'art'
DEST = ROOT / 'client/MemoryForgeUnity/Assets/Resources/Art'
FORMAT = 'memory-forge-moments/1'


def _dicts(value):
    return [v for v in (value.values() if isinstance(value, dict) else []) if isinstance(v, dict)]


def referenced(data):
    """Every art path a moments file names; malformed entries are skipped here and caught by the Unity loader."""
    images = data.get('images')
    paths = [v for v in (images.values() if isinstance(images, dict) else []) if isinstance(v, str)]
    for motion in _dicts(data.get('motions')):
        frames = motion.get('frames')
        paths += [f for f in (frames if isinstance(frames, list) else []) if isinstance(f, str)]
    scenes = data.get('scenes')
    for scene in (scenes if isinstance(scenes, list) else []):
        beats = scene.get('beats') if isinstance(scene, dict) else None
        for beat in (beats if isinstance(beats, list) else []):
            if isinstance(beat, dict) and isinstance(beat.get('voice'), str):
                paths.append(beat['voice'])
    return sorted(set(paths))


def contained(path, base):
    """True when path has no symlink in it below base and resolves inside base."""
    base = base.resolve()
    probe = path
    while probe != probe.parent:
        if probe.is_symlink():
            return False
        if probe.resolve() == base:
            break
        probe = probe.parent
    return path.resolve().is_relative_to(base)


def safe_ancestry(path, base):
    """True when every existing component from base down to path is a real directory (no symlink, no file), checked
    before anything is created so a refused import never makes directories outside base."""
    try:
        rel = path.relative_to(base)
    except ValueError:
        return False
    probe = base
    if probe.is_symlink():
        return False
    for part in rel.parts:
        probe = probe / part
        if probe.is_symlink() or (probe.exists() and not probe.is_dir()):
            return False
    return True


def clean(rel):
    """A relative POSIX path with no '..' or absolute parts, else None."""
    p = PurePosixPath(rel)
    return None if p.is_absolute() or '..' in p.parts or not p.parts else p.as_posix()


def import_one(source, art=ART, dest=DEST):
    if not contained(source, art):
        return {'file': str(source), 'error': 'source outside art/ or a symlink'}
    try:
        data = json.loads(source.read_text(encoding='utf-8'))
    except (OSError, ValueError) as e:
        return {'file': str(source), 'error': 'unreadable: ' + str(e)}
    if not isinstance(data, dict) or data.get('format') != FORMAT:
        return {'file': str(source), 'error': 'format ' + str(data.get('format') if isinstance(data, dict) else type(data).__name__)}
    copies, missing, refused = [source.relative_to(art).as_posix()], [], []
    for rel in referenced(data):
        safe = clean(rel)
        if safe is None or not contained(art / safe, art):
            refused.append(rel)
        elif (art / safe).is_file():
            copies.append(safe)
        else:
            missing.append(rel)
    for rel in copies:  # destination keeps the JSON-relative path, so Resources lookups match the unchanged file
        target = dest / rel
        if not safe_ancestry(target.parent, dest):
            refused.append(rel)
            continue
        target.parent.mkdir(parents=True, exist_ok=True)
        if target.is_symlink() or not contained(target, dest):
            refused.append(rel)
            continue
        shutil.copyfile(art / rel, target)
    result = {'character': data.get('character'), 'status': data.get('status'),
              'scenes': len(data['scenes']) if isinstance(data.get('scenes'), list) else 0,
              'copied': len(copies) - sum(1 for r in refused if r in copies), 'missing': missing}
    if refused:
        result['refused'] = refused
    return result


def main(ids):
    sources = sorted((ART / 'moments').glob('*/moments.json'))
    if ids:
        sources = [s for s in sources if s.parent.name in ids]
    results = [import_one(s) for s in sources]
    print(json.dumps(results, ensure_ascii=False, indent=2))
    return 1 if any('error' in r or 'refused' in r for r in results) else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
