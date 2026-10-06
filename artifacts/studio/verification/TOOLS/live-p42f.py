#!/usr/bin/env python3
"""Installed-main P4.2f qualification; no paid media lanes are reachable."""
import importlib.util
import os
from pathlib import Path
import sys
import verify as v
spec = importlib.util.spec_from_file_location('live42e', Path(__file__).with_name('live-p42e.py'))
e = importlib.util.module_from_spec(spec)
spec.loader.exec_module(e)
e.live.STATE = v.ROOT / 'artifacts/studio/workflows/P4.2f'
e.live.STATE.mkdir(parents=True, exist_ok=True)
allowed = {'baseline', 'receipt-hello', 'receipt-stage', 'regression', 'stage-submit', 'stage-review', 'cleanup-npc'}
if sys.argv[1] not in allowed:
    raise SystemExit('lane not authorized by P4.2f adapter')
original_run = v.run
def run(row, label, command, **kwargs):
    label = label.replace('p42e-', 'p42f-')
    return original_run(row.replace('P4.2e', 'P4.2f'), label, command, **kwargs)
v.run = run
original_unity = e.live.unity
def unity(row, label, extra, **kwargs):
    environment = dict(kwargs.pop('environment', {}) or {})
    if 'UNITY' not in environment:
        environment['UNITY'] = str(e.live.TOOLS / 'unity-interactive-p42f.py')
    return original_unity(row, label, extra, environment=environment, **kwargs)
e.live.unity = unity
e.main()
raise SystemExit(int(any(r['status'] != 'PASS' for r in v.RESULTS)))
