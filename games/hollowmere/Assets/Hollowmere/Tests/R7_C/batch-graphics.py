#!/usr/bin/env python3
"""Keep unity-batch.sh allocation/redaction/batch mode while enabling native rendering."""
import os
import sys
from pathlib import Path

editor = Path.home() / "Unity/Hub/Editor/6000.0.75f1/Editor/Unity"
args = [arg for arg in sys.argv[1:] if arg != "-nographics"]
if "-batchmode" not in args:
    raise SystemExit("R7-C adapter requires batch mode")
os.execv(str(editor), [str(editor), *args])
