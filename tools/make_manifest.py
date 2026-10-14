#!/usr/bin/env python3
"""Writes MANIFEST.sha256 for the package (all files except build outputs and the manifest itself)."""
import hashlib, pathlib, sys
root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else pathlib.Path(__file__).resolve().parents[1])
skip_dirs = {"bin", "obj", "node_modules", "__pycache__", "shots"}
lines = []
for p in sorted(root.rglob("*")):
    if not p.is_file() or p.name == "MANIFEST.sha256" or any(part in skip_dirs for part in p.relative_to(root).parts):
        continue
    if p.name == "design_v0.7.html":
        continue
    lines.append(f"{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.relative_to(root).as_posix()}")
(root / "MANIFEST.sha256").write_text("\n".join(lines) + "\n", encoding="utf-8")
print(f"{len(lines)} files hashed")
