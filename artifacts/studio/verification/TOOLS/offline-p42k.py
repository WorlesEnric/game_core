#!/usr/bin/env python3
"""Run the retained P4.2i offline lanes serially; preserve every exit code."""
import json
import os
from pathlib import Path
import subprocess
import sys
import verify as v

TOOLS = Path(__file__).resolve().parent
STATE = v.ROOT / 'artifacts/studio/workflows/P4.2k'
env = dict(os.environ, GC_STUDIO_UNITY_SLOTS='1', EVIDENCE_DISPLAY=':1', DISPLAY=':1', PYTHONDONTWRITEBYTECODE='1')
commands = [[sys.executable, str(TOOLS / 'core-p42k.py')]]
commands += [[sys.executable, str(TOOLS / 'rows-p42k.py'), lane] for lane in
             ('install', 'editor', 'views', 'timing', 'lifecycle', 'native', 'recovery', 'validation', 'guide')]
receipts = []
for command in commands:
    started = v.utc()
    print('ORDERED_LANE_START ' + ' '.join(command), flush=True)
    result = subprocess.run(command, cwd=v.ROOT, env=env)
    receipts.append({'command': command, 'startedAt': started, 'endedAt': v.utc(), 'exitCode': result.returncode})
    (STATE / 'offline-lanes.json').write_text(json.dumps(receipts, indent=2) + '\n')
    print('ORDERED_LANE_END ' + str(result.returncode), flush=True)
    if command[-1] == 'install' and result.returncode:
        raise SystemExit('Fixture installation failed; do not launch missing drivers')
print('OFFLINE_LANES_COMPLETE', flush=True)
