#!/usr/bin/env python3
"""Run permitted verify-all lanes in declared order; retain each nonzero result."""
import json
import os
from pathlib import Path
import subprocess
import verify as v

state = v.ROOT / 'artifacts/studio/workflows/P4.2i'
env = dict(os.environ, GC_STUDIO_UNITY_SLOTS='1', EVIDENCE_DISPLAY=':1')
lanes = ['bake', 'unity', 'perf', 'memory', 'clean']
receipts = []
for lane in lanes:
    started = v.utc()
    print('P42I_LANE_START ' + lane, flush=True)
    result = subprocess.run(['bash', 'studio/tools/verify-all.sh', lane], cwd=v.ROOT, env=env)
    receipts.append({'lane': lane, 'startedAt': started, 'endedAt': v.utc(), 'exitCode': result.returncode})
    (state / 'core-lanes.json').write_text(json.dumps(receipts, indent=2) + '\n')
    print('P42I_LANE_END ' + lane + ' ' + str(result.returncode), flush=True)
print('P42I_CORE_COMPLETE', flush=True)
