#!/usr/bin/env python3
"""Continue lanes refused before execution by the historical 40 GiB reserve."""
import json
import os
from pathlib import Path
import subprocess
import sys
import verify as v

TOOLS = Path(__file__).resolve().parent
STATE = v.ROOT / 'artifacts/studio/workflows/P4.2l'
environment = dict(os.environ, GC_STUDIO_UNITY_SLOTS='1', GC_STUDIO_DISK_RESERVE_GIB='28',
                   EVIDENCE_DISPLAY=':1', DISPLAY=':1', PYTHONDONTWRITEBYTECODE='1')
output = STATE / 'offline-continuation.json'
if output.exists():
    raise SystemExit('Continuation already attempted; preserve it')
commands = [['bash', 'studio/tools/verify-all.sh', lane] for lane in ('unity', 'perf', 'memory', 'clean')]
commands += [[sys.executable, str(TOOLS / 'rows-p42l.py'), lane] for lane in
             ('editor', 'views', 'timing', 'lifecycle', 'native', 'recovery', 'validation', 'guide')]
receipts = []
for command in commands:
    started = v.utc()
    print('ORDERED_LANE_START ' + ' '.join(command), flush=True)
    result = subprocess.run(command, cwd=v.ROOT, env=environment)
    receipts.append({'command': command, 'startedAt': started, 'endedAt': v.utc(), 'exitCode': result.returncode})
    output.write_text(json.dumps(receipts, indent=2) + '\n')
    print('ORDERED_LANE_END ' + str(result.returncode), flush=True)
print('OFFLINE_CONTINUATION_COMPLETE', flush=True)
