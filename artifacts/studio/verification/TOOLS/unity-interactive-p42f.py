#!/usr/bin/env python3
"""Existing graphical allocator adapter plus an owned per-launch UPM log."""
import os
from pathlib import Path
import runpy
import sys
out = Path(os.environ['GAMECORE_P42_EVIDENCE'])
out.mkdir(parents=True, exist_ok=True)
sys.argv.extend(['-upmLogFile', str(out / 'upm.log')])
runpy.run_path(str(Path(__file__).with_name('unity-interactive-p42c.py')), run_name='__main__')
