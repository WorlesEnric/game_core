#!/usr/bin/env python3
"""Installed-main P4.2g qualification; no paid media lanes are reachable."""
import importlib.util
import os
from pathlib import Path
import sys
import verify as v
spec = importlib.util.spec_from_file_location('live42e', Path(__file__).with_name('live-p42e.py'))
e = importlib.util.module_from_spec(spec)
spec.loader.exec_module(e)
e.live.STATE = v.ROOT / 'artifacts/studio/workflows/P4.2g'
e.live.STATE.mkdir(parents=True, exist_ok=True)
allowed = {'baseline', 'receipt-hello', 'receipt-stage', 'regression', 'regression-final', 'stage-submit', 'stage-review', 'cleanup-npc', 'npc'}
if sys.argv[1] not in allowed:
    raise SystemExit('lane not authorized by P4.2g adapter')
if sys.argv[1] == 'npc':
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
    label = label.replace('p42e-', 'p42g-')
    if label == 'p42g-receipt-stage':
        command = [x.replace('P42eReceipt/P42eReceipt.csproj', 'P42gReceipt/P42gReceipt.csproj').replace('R2_09_13_P42e_InstalledSignedRecordHasWorldAndPredicted', 'R2_09_13_P42g_InstalledSignedRecordBindsCurrentMain') for x in command]
    return original_run(row.replace('P4.2e', 'P4.2g'), label, command, **kwargs)
v.run = run
original_unity = e.live.unity
def unity(row, label, extra, **kwargs):
    extra = [arg.replace('P42e.Live.StageUi.', 'P42g.Live.StageUi.') for arg in extra]
    environment = dict(kwargs.pop('environment', {}) or {})
    if 'UNITY' not in environment:
        environment['UNITY'] = str(e.live.TOOLS / 'unity-interactive-p42f.py')
    return original_unity(row, label, extra, environment=environment, **kwargs)
e.live.unity = unity
if sys.argv[1] == 'npc':
    e.reserve('npc', {})
    unity('W-AI-02', 'p42g-npc', ['-executeMethod', 'Hollowmere.P4_2.EvidenceEntry.RunStage'], workflow='p42f-npc')
elif sys.argv[1] == 'regression-final':
    unity('W-AI-02', 'p42g-regression-final', ['-runTests', '-testPlatform', 'EditMode', '-testFilter', 'Hollowmere.R6_A.AdmissionLifecycleTests|Hollowmere.R6_B|Hollowmere.R6_E.Tests|Hollowmere.R6_F|Hollowmere.P3_2.Headless.DriverDryTests'], results='results.xml', environment={'GAMECORE_ETOS_AUTOSTART': '0', 'UNITY': str(Path.home() / 'Unity/Hub/Editor/6000.0.75f1/Editor/Unity')})
else:
    e.main()
raise SystemExit(int(any(r['status'] != 'PASS' for r in v.RESULTS)))
