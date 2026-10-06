#!/usr/bin/env python3
"""Retain then reset only tracked authored fixtures in this packet's scratch clone."""
from pathlib import Path
import subprocess
import sys
import verify as v
live=v.ROOT/'.evidence/live'
if subprocess.run(['pgrep','-x','Unity'],capture_output=True).returncode==0:
    raise SystemExit('Refuse fixture reset while an Editor is active')
paths=['games/hollowmere/Assets/Hollowmere','games/hollowmere/ProjectSettings']
out=v.ROOT/'artifacts/studio/workflows/P4.2d'/('fixture-'+sys.argv[1]+'.diff')
if out.exists():raise SystemExit('refuse to overwrite retained fixture diff')
diff=subprocess.check_output(['git','diff','--binary','HEAD','--',*paths],cwd=live,text=True)
out.write_text(v.scrub(diff))
subprocess.run(['git','restore','--source=HEAD','--worktree','--',*paths],cwd=live,check=True)
