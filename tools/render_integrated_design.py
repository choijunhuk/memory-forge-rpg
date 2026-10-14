#!/usr/bin/env python3
"""Compatibility entrypoint: current version only. No partial fallback."""
from pathlib import Path
import subprocess,sys
root=Path(__file__).resolve().parents[1]
raise SystemExit(subprocess.call([sys.executable,str(root/'tools/build_integrated_design.py'),'--pdf']))
