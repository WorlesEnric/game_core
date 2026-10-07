#!/usr/bin/env python3
"""Read only this lane's actual task/budget records; optionally retain an operator-exported worker trace."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess


def redact(text):
    text = re.sub(r'\b(?:etk|ett|etp|eta)_[A-Za-z0-9_-]+', '[REDACTED]', text)
    return re.sub(r'(?i)\bBearer\s+\S+', 'Bearer [REDACTED]', text)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, required=True, help='The exact completed driver output directory')
    parser.add_argument('--worker-trace', type=Path, help='Explicit same-task exported tool trace; no node filesystem discovery is performed')
    args = parser.parse_args()
    output = args.out.resolve()
    result = json.loads((output / 'result.json').read_text())
    task_ids = result.get('taskIds', [])
    if not all(re.fullmatch(r't[0-9a-z]{16,}', identity) for identity in task_ids):
        raise SystemExit('Result contains an invalid ETOS task identity')
    if len(task_ids) != len(set(task_ids)):
        raise SystemExit('Result duplicated task IDs')
    destination = output / 'collection'
    destination.mkdir(exist_ok=False)
    cli = str(Path.home() / '.local/opt/etos/bin/etos')
    environment = dict(os.environ, ETOS_ROOT=str(Path.home() / '.local/share/etos-studio'))
    observations = []
    for identity in task_ids:
        for label, command in [('task', [cli, '--json', 'task', '--task', identity]),
                               ('budget', [cli, '--json', 'budget', '--task', identity])]:
            completed = subprocess.run(command, env=environment, text=True, capture_output=True, timeout=60, check=False)
            (destination / (identity + '-' + label + '.json')).write_text(redact(completed.stdout))
            (destination / (identity + '-' + label + '.stderr')).write_text(redact(completed.stderr))
            observations.append({'taskId': identity, 'kind': label, 'command': command, 'exitCode': completed.returncode})
    if args.worker_trace:
        # An explicit exported tool trace is input evidence, never a credential file or a guessed state path.
        trace = args.worker_trace.resolve()
        if trace.stat().st_size > 32 * 1024 * 1024:
            raise SystemExit('Export a bounded same-task trace, at most 32 MiB')
        raw = trace.read_bytes()
        retained = redact(raw.decode('utf-8')).encode('utf-8')
        target = destination / 'worker-tool-trace.txt'
        target.write_bytes(retained)
        observations.append({'kind': 'worker-tool-trace', 'sourceFileName': trace.name,
            'inputSha256': hashlib.sha256(raw).hexdigest(), 'retainedSha256': hashlib.sha256(retained).hexdigest(),
            'redacted': raw != retained, 'taskIds': task_ids,
            'limitation': 'Retention does not authenticate arbitrary input or automatically pass W-ETOS-04. Main must verify the trace belongs to these tasks, is a real tool invocation/result, and matches the query receipt.'})
    (destination / 'receipt.json').write_text(json.dumps({'requestId': result.get('requestId'), 'taskIds': task_ids,
        'observations': observations, 'originalDriverStatus': result['status'],
        'mediaGenerationRequested': False, 'providerInvoiceClaimed': False}, indent=2) + '\n')
    print(destination)
    return int(any(item.get('exitCode', 0) != 0 for item in observations))


if __name__ == '__main__':
    raise SystemExit(main())
