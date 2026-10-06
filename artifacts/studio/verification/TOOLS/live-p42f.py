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
allowed = {'baseline', 'receipt-hello', 'receipt-stage', 'regression', 'stage-submit', 'stage-review', 'cleanup-npc', 'text2', 'npc'}
if sys.argv[1] not in allowed:
    raise SystemExit('lane not authorized by P4.2f adapter')
if sys.argv[1] in ('text2', 'npc'):
    import json
    import subprocess
    activation = json.loads((e.live.STATE / 'worker-activation.json').read_text())
    assert activation['workerBudget']['usd'] == 0.50
    workers = json.loads(subprocess.check_output([str(Path.home() / '.local/opt/etos/bin/etos'), '--json', 'worker', 'list'], env=dict(os.environ, ETOS_ROOT=str(Path.home() / '.local/share/etos-studio')), text=True))
    worker = next(w for w in workers if w['name'] == 'gc-designer')
    assert worker['budget']['usd'] == 0.50
    assert worker['instructions'] == (v.ROOT / 'studio/etos/agent/workers/gc-designer.md').read_text()
original_run = v.run
def run(row, label, command, **kwargs):
    label = label.replace('p42e-', 'p42f-')
    if label == 'p42f-receipt-stage':
        command = [x.replace('P42eReceipt/P42eReceipt.csproj', 'P42fReceipt/P42fReceipt.csproj').replace('R2_09_13_P42e_InstalledSignedRecordHasWorldAndPredicted', 'R2_09_13_P42f_InstalledSignedRecordBindsCurrentMain') for x in command]
    return original_run(row.replace('P4.2e', 'P4.2f'), label, command, **kwargs)
v.run = run
original_unity = e.live.unity
def unity(row, label, extra, **kwargs):
    environment = dict(kwargs.pop('environment', {}) or {})
    if 'UNITY' not in environment:
        environment['UNITY'] = str(e.live.TOOLS / 'unity-interactive-p42f.py')
    return original_unity(row, label, extra, environment=environment, **kwargs)
e.live.unity = unity
if sys.argv[1] == 'npc':
    e.reserve('npc', {})
    unity('W-AI-02', 'p42f-npc', ['-executeMethod', 'Hollowmere.P4_2.EvidenceEntry.RunStage'], workflow='p42f-npc')
else:
    e.main()
raise SystemExit(int(any(r['status'] != 'PASS' for r in v.RESULTS)))
