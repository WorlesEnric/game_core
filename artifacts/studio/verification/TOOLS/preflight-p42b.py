#!/usr/bin/env python3
"""Read-only live/display guard and media-ledger accounting; never loads credentials."""
import json
from pathlib import Path
import sqlite3
import subprocess
import sys
import verify as v


def pids(*args):
    proc = subprocess.run(['pgrep', *args], capture_output=True, text=True)
    if proc.returncode not in (0, 1):
        raise RuntimeError('live-run guard unavailable')
    return [int(x) for x in proc.stdout.split()]


def ledger():
    database = Path.home() / '.local/share/etos-studio/agents/gamecore-studio/state/ledger.db'
    with sqlite3.connect('file:' + str(database) + '?mode=ro', uri=True) as connection:
        exists = connection.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name='media_charges'").fetchone()
        if not exists:
            return {'available': False, 'reason': 'Installed pre-R4 ledger has no media_charges table; no paid operation may start.'}
        # Export charge totals/provenance only; operation keys/owner credentials are never emitted.
        charges = [json.loads(row[0]) for row in connection.execute('SELECT charge FROM media_charges')]
        return {'available': True, 'count': len(charges), 'costUsd': sum(c.get('costUsd', 0) for c in charges),
                'charges': charges}


def capture():
    base = Path.home() / '.local/share/etos-studio/agents/gamecore-studio'
    return {'utc': v.utc(), 'sourceRevision': v.git('rev-parse', 'HEAD'), 'finalMain': '1752ca8a',
            'liveRunPids': pids('-f', '[g]c-studio/p3'), 'editorPids': pids('-x', 'Unity'),
            'recorderPids': pids('-x', 'ffmpeg'), 'installedRelease': (base / 'current').resolve().name,
            'ledger': ledger()}


if __name__ == '__main__':
    record = capture()
    record['status'] = 'BLOCKED' if record['liveRunPids'] else 'PASS'
    print(json.dumps(record, indent=2))
    Path(sys.argv[1]).write_text(json.dumps(record, indent=2) + '\n')
    sys.exit(2 if record['status'] == 'BLOCKED' else 0)
