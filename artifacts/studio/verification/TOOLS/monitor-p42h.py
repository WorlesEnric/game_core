#!/usr/bin/env python3
"""Retain process identity without arguments, environments or credential contents."""
import json
import os
from pathlib import Path
import time

ROOT = Path(__file__).resolve().parents[4]
OUT = ROOT / 'artifacts/studio/workflows/P4.2h/host-monitor.jsonl'
OUT.parent.mkdir(parents=True, exist_ok=True)
print('MONITOR_READY', flush=True)
while True:
    editors, packets = [], []
    for process in Path('/proc').iterdir():
        if not process.name.isdigit():
            continue
        try:
            name = (process / 'comm').read_text().strip()
            if name not in ('Unity', 'omp', 'bun'):
                continue
            args = (process / 'cmdline').read_bytes().split(b'\0')
            cwd = os.readlink(process / 'cwd')
            if name == 'Unity':
                status = dict(line.split(':', 1) for line in (process / 'status').read_text().splitlines() if ':' in line)
                executable = os.readlink(process / 'exe')
                editors.append({'pid': int(process.name), 'parentPid': int(status['PPid']),
                    'state': status['State'].strip().split()[0],
                    'importWorker': any(b'AssetImportWorker' in arg for arg in args),
                    'ownedCwd': cwd.startswith(str(ROOT) + '/'),
                    'emptyArguments': not any(args), 'executable': executable.replace(str(Path.home()), '~')})
            elif b'-p' in args and cwd.startswith(str(ROOT.parent) + '/'):
                packets.append({'pid': int(process.name), 'cwd': cwd.replace(str(Path.home()), '~'), 'owned': cwd == str(ROOT)})
        except (OSError, ValueError, KeyError):
            continue
    with OUT.open('a') as stream:
        stream.write(json.dumps({'utcEpoch': time.time(), 'editors': editors, 'packets': packets}) + '\n')
    time.sleep(0.5)
