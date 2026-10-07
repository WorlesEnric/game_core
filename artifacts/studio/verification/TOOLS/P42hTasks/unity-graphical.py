#!/usr/bin/env python3
"""Use the established graphical adapter without bypassing the host allocator."""
import os
from pathlib import Path
import runpy

output = Path(os.environ['GAMECORE_P42H_OUT']).resolve()
output.mkdir(parents=True, exist_ok=True)
os.environ['GAMECORE_P42_EVIDENCE'] = str(output)
runpy.run_path(str(Path(__file__).resolve().parent.parent / 'unity-interactive-p42f.py'), run_name='__main__')
