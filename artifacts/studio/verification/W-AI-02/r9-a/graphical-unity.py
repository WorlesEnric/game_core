#!/usr/bin/env python3
"""Keep the host allocator/redactor and batch ETOS opt-out; enable real graphics."""
import os
from pathlib import Path
import sys

args = [arg for arg in sys.argv[1:] if arg.lower() != "-nographics"]
if "-batchmode" not in args:
    raise SystemExit("R9-A requires batch mode so the installed ETOS session never starts")
os.environ["GAMECORE_ETOS_AUTOSTART"] = "0"
os.environ.setdefault("DISPLAY", ":1")
editor = Path.home() / "Unity/Hub/Editor/6000.0.75f1/Editor/Unity"
os.execv(str(editor), [str(editor), *args])
