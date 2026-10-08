#!/usr/bin/env python3
"""Run each retained real-worker/tray driver once; restore only the owned compile witness."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import verify as v

TOOLS = Path(__file__).resolve().parent
state = v.ROOT / 'artifacts/studio/workflows/P4.2l'
records = []
for lane, row, method in [('move', 'W-EDIT-01', 'JoinedMove'), ('query', 'W-ETOS-04', 'SelectedNpcQuery'),
                          ('cancel', 'W-ETOS-05', 'TrayCancel'), ('reload', 'W-ETOS-09', 'SourceReload')]:
    started = v.utc()
    command = [sys.executable, str(TOOLS / 'live-p42l.py'), lane, '--row', row,
               '--method', 'P42h.Tasks.TaskDriver.' + method]
    result = subprocess.run(command, cwd=v.ROOT)
    records.append({'lane': lane, 'row': row, 'startedAt': started, 'endedAt': v.utc(), 'exitCode': result.returncode})
    if lane == 'query':
        attempts = [p for p in (v.OUT / row).glob('p42l-query-*/result.json') if json.loads(p.read_text())['started'] >= started]
        if len(attempts) != 1:
            raise RuntimeError('Cannot identify this exact current query attempt')
        checked = subprocess.run([sys.executable, str(TOOLS / 'query-p42l.py'), '--workflow', str(attempts[0].parent / 'workflow')], cwd=v.ROOT)
        records[-1]['currentContractVerificationExitCode'] = checked.returncode
    (state / 'task-lanes.json').write_text(json.dumps(records, indent=2) + '\n')
    if lane == 'reload':
        project = v.ROOT / 'games/hollowmere'
        if (project / 'Temp/UnityLockfile').exists():
            raise RuntimeError('Cannot restore compile witness before the owned Editor exits')
        shutil.copy2(TOOLS / 'P42hTasks/CompilePulse.cs', project / 'Assets/Hollowmere/Tests/P42hTasks/CompilePulse.cs')
print('CURRENT_WORKER_ROWS_COMPLETE', flush=True)
