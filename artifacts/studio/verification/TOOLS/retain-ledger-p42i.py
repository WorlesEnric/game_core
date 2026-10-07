#!/usr/bin/env python3
"""Retain owned request/task accounting and the immutable packet media delta."""
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import verify as v

state = v.ROOT / 'artifacts/studio/workflows/P4.2i'
base = Path.home() / '.local/share/etos-studio'
cli = str(Path.home() / '.local/opt/etos/bin/etos')
project = json.loads((state / 'worker-activation.json').read_text())['projects']
project_id = next(identity for identity, path in project.items() if path.endswith('/games/hollowmere'))
with sqlite3.connect('file:' + str(base / 'agents/gamecore-studio/state/ledger.db') + '?mode=ro', uri=True) as conn:
    charges = [{'id': key, **json.loads(charge)} for key, charge in conn.execute('SELECT key,charge FROM media_charges ORDER BY key')]
    owner = json.dumps(['gamecore-unity', project_id], separators=(',', ':'))
    ids = {row[0] for row in conn.execute(
        'SELECT a.task_id FROM attempts a JOIN requests r ON r.request_id = a.request_id WHERE r.app = ? AND a.task_id IS NOT NULL', (owner,))}
tasks = []
for identity in sorted(ids):
    result = subprocess.run([cli, '--json', 'budget', '--task', identity], env=dict(os.environ, ETOS_ROOT=str(base)), capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError('Cannot collect accounting for owned task ' + identity)
    budget = json.loads(result.stdout)
    if budget['task']['scope'] != 'task:' + identity:
        raise RuntimeError('Task budget scope mismatch')
    tasks.append({'taskId': identity, 'budget': budget['task'], 'workerScope': budget['worker']['scope'], 'workerLimits': budget['worker']['limits']})
before = json.loads((state / 'ledger-before.json').read_text())
previous = {charge['id'] for charge in before['charges']}
new = [charge for charge in charges if charge['id'] not in previous]
worker = sum(task['budget']['used'].get('micro_usd', 0) for task in tasks)
paid = {'charges': new, 'companionLedgerUsd': sum(charge['costUsd'] for charge in new),
    'workerMicroUsd': worker, 'workerTaskCount': len(tasks),
    'tasksWithoutReportedMicroUsd': [task['taskId'] for task in tasks if 'micro_usd' not in task['budget']['used']],
    'accountedUsd': sum(charge['costUsd'] for charge in new) + worker / 1000000,
    'operationCounts': {'image': sum(charge['quantity'] for charge in new if charge['tariff']['unit'] == 'image'),
        'tts': sum(charge['tariff']['unit'] == 'bailian_character' for charge in new),
        'describe': sum(charge['tariff']['unit'] == 'call' for charge in new), '3dPaid': 0,
        '3dRefusals': sum(json.loads(path.read_text()).get('code') == 'not_configured' for path in v.OUT.glob('P4.2i/p42i-hello-*/3d-refusal.json'))},
    'accountingLimit': 'Sum of node-reported task usage and binding operator media charges. Missing task usage entries are listed, not asserted as invoiced zero. Upstream provider invoices/unreported charges are unavailable.'}
for op, cap in {'image': 2, 'tts': 2, 'describe': 1, '3dPaid': 0}.items():
    if paid['operationCounts'][op] > cap:
        raise RuntimeError(op + ' cap exceeded')
if paid['accountedUsd'] > 2.00:
    raise RuntimeError('Total USD 2.00 cap exceeded')
(state / 'task-ledger.json').write_text(v.scrub(json.dumps(tasks, indent=2)) + '\n')
(state / 'paid-ledger.json').write_text(v.scrub(json.dumps(paid, indent=2)) + '\n')
print(json.dumps(paid, indent=2))
