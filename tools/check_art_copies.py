#!/usr/bin/env python3
"""Check that every art file Unity loads is a byte copy of a creative original under art/.

OWNERSHIP.md: Codex owns the originals in art/; Claude only copies them into Unity. A copy whose SHA-256
matches no tracked art/ file was edited (or has no original) and fails the check.
"""
import hashlib
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
COPY_DIRS = ['client/MemoryForgeUnity/Assets/Resources/Art', 'client/MemoryForgeUnity/Assets/Art/Characters']


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def tracked(prefix):
    out = subprocess.run(['git', 'ls-files', '-z', '--', prefix], cwd=ROOT, capture_output=True, check=True).stdout
    return [ROOT / p for p in out.decode().split('\0') if p]


def check():
    originals = {sha(p) for p in tracked('art') if p.is_file()}
    copies = [p for d in COPY_DIRS for p in tracked(d) if p.is_file() and p.suffix != '.meta']
    bad = [str(p.relative_to(ROOT)) for p in copies if sha(p) not in originals]
    return len(copies), bad


if __name__ == '__main__':
    count, bad = check()
    for path in bad:
        print('NO_MATCHING_ORIGINAL', path)
    print(f'{count} copies checked, {len(bad)} without a matching original under art/')
    sys.exit(1 if bad else 0)
