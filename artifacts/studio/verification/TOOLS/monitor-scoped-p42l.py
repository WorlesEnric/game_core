#!/usr/bin/env python3
"""Inspect Editor identity and project-hub sessions only; never inspect unrelated session arguments."""
import json
import os
from pathlib import Path
import time

ROOT = Path(__file__).resolve().parents[4]
OUT = ROOT / 'artifacts/studio/workflows/P4.2l/host-monitor-scoped.jsonl'
OUT.parent.mkdir(parents=True, exist_ok=True)
print('SCOPED_MONITOR_READY', flush=True)
while True:
    editors, packets = [], []
    for process in Path('/proc').iterdir():
        if not process.name.isdigit():
            continue
        try:
            name = (process / 'comm').read_text().strip()
            if name == 'Unity':
                args = (process / 'cmdline').read_bytes().split(b'\0')
                cwd = os.readlink(process / 'cwd')
                executable = os.readlink(process / 'exe')
                status = dict(line.split(':', 1) for line in (process / 'status').read_text().splitlines() if ':' in line)
                editors.append({'pid': int(process.name), 'parentPid': int(status['PPid']),
                    'state': status['State'].strip().split()[0], 'importWorker': any(b'AssetImportWorker' in arg for arg in args),
                    'ownedCwd': cwd == str(ROOT) or cwd.startswith(str(ROOT) + '/'), 'emptyArguments': not any(args),
                    'executable': executable.replace(str(Path.home()), '~'), 'actualEditorExecutable': Path(executable).name == 'Unity'})
                continue
            cwd = os.readlink(process / 'cwd')
            if not (cwd == str(ROOT.parent) or cwd.startswith(str(ROOT.parent) + '/')):
                continue
            if name not in ('omp', 'bun', 'node', 'codex'):
                continue
            args = (process / 'cmdline').read_bytes().split(b'\0')
            if any(b'omp' in arg or b'codex' in arg for arg in args[:2]):
                packets.append({'pid': int(process.name), 'cwd': cwd.replace(str(Path.home()), '~'), 'owned': cwd == str(ROOT)})
        except (OSError, ValueError, KeyError):
            continue
    with OUT.open('a') as stream:
        stream.write(json.dumps({'utcEpoch': time.time(), 'editors': editors, 'packets': packets}) + '\n')
    time.sleep(0.5)
