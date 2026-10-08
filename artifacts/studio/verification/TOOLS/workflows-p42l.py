#!/usr/bin/env python3
"""Execute each paid workflow once at the fixed product revision; never replay a reservation."""
import json
import os
from pathlib import Path
import subprocess
import sys
import verify as v

TOOLS = Path(__file__).resolve().parent
STATE = v.ROOT / 'artifacts/studio/workflows/P4.2l'
records = []


def execute(name, arguments, environment=None):
    started = v.utc()
    command = [sys.executable, *map(str, arguments)]
    print('WORKFLOW_START ' + name, flush=True)
    result = subprocess.run(command, cwd=v.ROOT, env=dict(os.environ, **(environment or {})))
    records.append({'lane': name, 'command': command, 'startedAt': started, 'endedAt': v.utc(), 'exitCode': result.returncode})
    (STATE / 'workflow-lanes.json').write_text(json.dumps(records, indent=2) + '\n')
    print('WORKFLOW_END ' + name + ' ' + str(result.returncode), flush=True)
    return result.returncode


def live(name, row, method, *extra, environment=None):
    return execute(name, [TOOLS / 'live-p42l.py', name, '--row', row, '--method', method, *extra], environment)


run = json.loads((STATE / 'run.json').read_text())
if v.git('rev-parse', 'HEAD') != run['revision']:
    raise SystemExit('Product revision changed')
if (STATE / 'workflow-lanes.json').exists():
    raise SystemExit('Retain completed attempts; this sequence cannot be replayed')
live('narrative', 'W-AI-03', 'Hollowmere.P4_2.EvidenceEntry.RunStage', '--workflow', 'narrative')
live('reopen', 'W-AI-06', 'Hollowmere.P4_2.EvidenceEntry.RunStage', '--workflow', 'reopen', '--offline')
if live('npc-view', 'W-AI-02', 'P42g.Live.NpcPrerequisiteProbe.Run', '--offline') == 0:
    probes = sorted((v.OUT / 'W-AI-02').glob('p42l-npc-view-*/npc-view-prerequisite.json'))
    if len(probes) != 1:
        raise RuntimeError('Exactly one fresh NPC prerequisite receipt is required')
    live('npc', 'W-AI-02', 'Hollowmere.P4_2.EvidenceEntry.RunStage', '--workflow', 'p42f-npc',
         environment={'GAMECORE_P42G_NPC_CREATION': '1', 'GAMECORE_P42G_NPC_VIEW': str(probes[0])})
execute('tasks', [TOOLS / 'tasks-p42l.py'])
execute('ledger', [TOOLS / 'retain-ledger-p42l.py'])
print('BOUNDED_WORKFLOWS_COMPLETE', flush=True)
