#!/usr/bin/env python3
"""Collect only packet-owned task budgets and the immutable media-ledger delta."""
import json
import os
from pathlib import Path
import re
import sqlite3
import subprocess
import verify as v
state = v.ROOT / 'artifacts/studio/workflows/P4.2f'
base = Path.home() / '.local/share/etos-studio'
cli = str(Path.home() / '.local/opt/etos/bin/etos')
env = dict(os.environ, ETOS_ROOT=str(base))
ids = set()
for run in v.OUT.glob('*/p42f-*/workflow'):
    for path in run.rglob('task-ids.txt'):
        ids.update(re.findall(r'\bt[0-9a-z]{16,}\b', path.read_text()))
tasks = []
for identity in sorted(ids):
    status = json.loads(subprocess.check_output([cli, '--json', 'task', '--task', identity], env=env, text=True))
    budget = json.loads(subprocess.check_output([cli, '--json', 'budget', '--task', identity], env=env, text=True))
    assert budget['task']['scope'] == 'task:' + identity
    assert budget['worker']['scope'] == 'worker:gc-designer'
    assert budget['worker']['limits']['micro_usd'] == 500000
    tasks.append({'taskId': identity, 'status': status['status'], 'budget': {'task': budget['task'], 'workerScope': budget['worker']['scope'], 'workerLimits': budget['worker']['limits']}})
with sqlite3.connect('file:' + str(base / 'agents/gamecore-studio/state/ledger.db') + '?mode=ro', uri=True) as conn:
    charges = [{'id': key, **json.loads(charge)} for key, charge in conn.execute('SELECT key,charge FROM media_charges ORDER BY key')]
before = json.loads((state / 'ledger-before.json').read_text())
previous = {x['id'] for x in before['charges']}
new = [x for x in charges if x['id'] not in previous]
worker = sum(t['budget']['task']['used']['micro_usd'] for t in tasks)
paid = {'charges': new, 'companionLedgerUsd': sum(x['costUsd'] for x in new),
        'workerMicroUsd': worker, 'workerTaskCount': len(tasks),
        'accountedUsd': sum(x['costUsd'] for x in new) + worker / 1000000,
        'operationCounts': {'image': sum(x['tariff']['unit'] == 'image' for x in new),
                            'tts': sum(x['tariff']['unit'] == 'bailian_character' for x in new),
                            'describe': sum(x['tariff']['unit'] == 'call' for x in new),
                            '3dPaid': 0,
                            '3dRefusals': sum(json.loads(p.read_text()).get('code') == 'not_configured' for p in v.OUT.glob('INSTALL-P4.2f/p42f-receipt-hello-*/3d-refusal.json'))},
        'accountingLimit': 'Node-reported task and companion media charges; no upstream provider invoice or unreported charge is inferred. Worker ceiling USD 0.50 is node accounting, not an unpriced reseller invoice guarantee.'}
assert paid['accountedUsd'] <= 0.50
assert not new, 'P4.2f forbids paid media calls'
(state / 'task-ledger.json').write_text(json.dumps(tasks, indent=2) + '\n')
(state / 'paid-ledger.json').write_text(json.dumps(paid, indent=2) + '\n')
print(json.dumps(paid, indent=2))
