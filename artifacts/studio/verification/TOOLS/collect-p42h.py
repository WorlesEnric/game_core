#!/usr/bin/env python3
"""Retain only this packet's task traces and sanitize/hash commissioned artifacts."""
import hashlib
import json
from pathlib import Path
import sqlite3
import verify as v

ROOT = v.ROOT
STATE = ROOT / 'artifacts/studio/workflows/P4.2h'
TARGETS = ['W-EDIT-01', 'W-EDIT-03', 'W-ETOS-04', 'W-ETOS-05', 'W-ETOS-09', 'W-AI-01', 'W-REC-03', 'W-GAME-01', 'W-E2E-01', 'P4.2h']
node = Path.home() / '.local/share/etos-studio/node.db'
with sqlite3.connect(node.as_uri() + '?mode=ro', uri=True) as connection:
    connection.execute('PRAGMA query_only=ON')
    for rid in TARGETS:
        for folder in (v.OUT / rid).glob('p42h-*'):
            if not folder.is_dir():
                continue
            for ids in folder.rglob('task-ids.txt'):
                tasks = [line.strip() for line in ids.read_text().splitlines() if line.strip()]
                trace = []
                for task in tasks:
                    for turn, index, name, args, outcome in connection.execute(
                        'SELECT turn,idx,name,args,outcome FROM tool_call WHERE task=? ORDER BY turn,idx', (task,)):
                        trace.append({'taskId': task, 'turn': turn, 'index': index, 'name': name,
                            'args': json.loads(args), 'outcome': json.loads(outcome) if outcome else None})
                (ids.parent / 'worker-tool-trace.json').write_text(v.scrub(json.dumps(trace, indent=2)) + '\n')
            for path in folder.rglob('*'):
                if path.is_file() and path.suffix in ('.txt', '.log', '.json', '.jsonl', '.xml', '.trx', '.md', '.csv'):
                    path.write_text(v.scrub(path.read_text(errors='replace')))
            files = sorted(path for path in folder.rglob('*') if path.is_file() and path.name != 'SHA256SUMS')
            (folder / 'SHA256SUMS').write_text(''.join(hashlib.sha256(path.read_bytes()).hexdigest() + '  ' + str(path.relative_to(folder)) + '\n' for path in files))
for path in STATE.rglob('*'):
    if path.is_file() and path.name != 'host-monitor.jsonl' and path.suffix in ('.txt', '.log', '.json', '.jsonl', '.trx', '.xml'):
        path.write_text(v.scrub(path.read_text(errors='replace')))
print('Retained owned tool-call traces; sanitized and hashed commissioned evidence')
