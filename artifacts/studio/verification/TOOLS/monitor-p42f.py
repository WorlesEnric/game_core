#!/usr/bin/env python3
"""Classify Editor/import-worker processes without retaining command-line secrets."""
import json
from pathlib import Path
import time
repo = Path(__file__).resolve().parents[4]
out = repo / 'artifacts/studio/workflows/P4.2f/editor-monitor.jsonl'
print('MONITOR_READY', flush=True)
while True:
    rows = []
    for p in Path('/proc').iterdir():
        if not p.name.isdigit():
            continue
        try:
            if (p / 'comm').read_text().strip() != 'Unity':
                continue
            args = (p / 'cmdline').read_bytes()
            status = (p / 'status').read_text().splitlines()
            parent = next(line.split()[1] for line in status if line.startswith('PPid:'))
            process_state = next(line.split()[1] for line in status if line.startswith('State:'))
            rows.append({'pid': int(p.name), 'parentPid': int(parent),
                         'assetImportWorker': b'AssetImportWorker' in args,
                         'thisProject': str(repo / '.evidence/live/games/hollowmere').encode() in args,
                         'batchMode': b'-batchmode' in args.lower(), 'emptyArguments': not args,
                         'processState': process_state})
        except (OSError, StopIteration):
            pass
    with out.open('a') as stream:
        stream.write(json.dumps({'utcEpoch': time.time(), 'processes': rows}) + '\n')
    time.sleep(1)
