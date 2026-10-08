#!/usr/bin/env python3
"""Pause only the owned real stage dotnet container to serialize creator and sandbox Editors."""
import argparse
import fcntl
import hashlib
import json
from pathlib import Path
import re
import sqlite3
import subprocess
import time
import verify as v


def docker(*args):
    return subprocess.check_output(['/usr/bin/docker', *args], text=True).strip()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=('pause', 'verify'))
    parser.add_argument('--request', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    request = json.loads(args.request.read_text())
    job = request['jobId']
    state = Path.home() / '.local/share/etos-studio/agents/gamecore-studio/state'
    with sqlite3.connect((state / 'ledger.db').as_uri() + '?mode=ro', uri=True) as db:
        row = db.execute('SELECT r.app,s.state,s.slot,s.verdict FROM stage_jobs s JOIN requests r ON r.request_id=s.change_set_id WHERE s.job_id=?', (job,)).fetchone()
    expected_owner = json.dumps(['gamecore-unity', request['request']['projectId']], separators=(',', ':'))
    if row is None or row[0] != expected_owner:
        raise RuntimeError('Stage does not belong to this exact authenticated project')
    owner = hashlib.sha256(expected_owner.encode()).hexdigest()
    slot = Path.home() / '.cache/gamecore-studio/stage' / owner / row[2]
    label = hashlib.sha256(str(state / 'stage-control' / job).encode()).hexdigest()
    args.out.mkdir(parents=True, exist_ok=True)
    def containers():
        ids = docker('ps', '-aq', '--filter', 'label=gamecore.stage.job=' + label).split()
        if any(not re.fullmatch('[a-f0-9]{12,64}', item) for item in ids):
            raise RuntimeError('Invalid Docker identity')
        return ids
    lock = slot.parent / '.locks' / slot.name
    if args.action == 'verify':
        remaining = containers()
        if remaining or row[1] != 'cancelled' or row[3] is not None:
            raise RuntimeError('Cancellation did not durably remove execution and verdict authority')
        with lock.open('r+') as handle:
            fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
        if (slot / 'out/verdict.json').exists():
            raise RuntimeError('Cancelled stage retained a verdict file')
        receipt = {'status': 'PASS', 'jobId': job, 'containersRemaining': [], 'stageSlotLockAcquired': True,
                   'stageState': row[1], 'issuedVerdicts': 0, 'revision': v.git('rev-parse', 'HEAD')}
        (args.out / 'stage-resources.json').write_text(json.dumps(receipt, indent=2) + '\n')
        return
    deadline = time.monotonic() + 300
    while time.monotonic() < deadline:
        for container in containers():
            info = json.loads(docker('inspect', '--format', '{{json .State}}', container))
            name = docker('inspect', '--format', '{{.Name}}', container).lstrip('/')
            executable = docker('inspect', '--format', '{{.Path}} {{json .Args}}', container)
            prefix = 'gc-stage-' + hashlib.sha256(str(slot).encode()).hexdigest()[:32] + '-'
            if not info.get('Running') or not name.startswith(prefix) or 'dotnet' not in executable:
                continue
            docker('pause', container)
            paused = json.loads(docker('inspect', '--format', '{{json .State}}', container))
            if not paused.get('Paused'):
                raise RuntimeError('Owned dotnet stage was not paused')
            with lock.open('r+') as handle:
                try:
                    fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
                except BlockingIOError:
                    pass
                else:
                    raise RuntimeError('Actual running stage slot was not held')
            receipt = {'jobId': job, 'containerId': container, 'containerName': name, 'jobLabel': label,
                       'stateBefore': info, 'pausedForSerialEditor': paused, 'stageSlotLockHeld': True,
                       'slot': str(slot), 'intervention': 'Pause exact owned real dotnet container after sandbox Unity probe; no substitute process'}
            (args.out / 'container-running.json').write_text(v.scrub(json.dumps(receipt, indent=2)) + '\n')
            (args.out / 'stage-queued.json').write_text(json.dumps({'jobId': job, 'slot': row[2]}, indent=2) + '\n')
            (args.out / 'stage-context.json').write_text(json.dumps({'request': request['request']}, indent=2) + '\n')
            print('OWNED_STAGE_PAUSED', flush=True)
            return
        time.sleep(0.05)
    raise RuntimeError('No owned real dotnet container reached the cancellable phase')


if __name__ == '__main__':
    main()
