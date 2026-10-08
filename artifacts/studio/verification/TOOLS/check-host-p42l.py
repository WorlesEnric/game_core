#!/usr/bin/env python3
"""Read only allowlisted installation/process metadata; no credentials or service mutation."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import verify as v

state = v.ROOT / 'artifacts/studio/workflows/P4.2l'
activation = json.loads((state / 'worker-activation.json').read_text())
cli = str(Path.home() / '.local/opt/etos/bin/etos')
env = dict(os.environ, ETOS_ROOT=str(Path.home() / '.local/share/etos-studio'))
agents = json.loads(subprocess.check_output([cli, '--json', 'agent', 'list'], env=env, text=True))
agent = next(item for item in agents if item['agent'] == 'gamecore-studio')
executable = (Path('/proc') / str(agent['pid']) / 'exe').resolve(strict=True)
with executable.open('rb') as stream:
    binary = hashlib.file_digest(stream, 'sha256').hexdigest()
workers = json.loads(subprocess.check_output([cli, '--json', 'worker', 'list'], env=env, text=True))
designer = next(item for item in workers if item['name'] == 'gc-designer')
instructions = hashlib.sha256(designer['instructions'].encode()).hexdigest()
assert agent['state'] == 'ready'
assert executable.parent.parent.name == activation['release']
assert binary == activation['binarySha256']
assert instructions == activation['designerInstructionsSha256']
assert designer['budget'] == activation['workerBudget']
samples = []
for name in ('host-monitor-scoped.jsonl',):
    for line in (state / name).read_text().splitlines():
        samples.append(json.loads(line))
unknown, overlaps, foreign = [], [], []
maximum = 0
noneditors = {}
for sample in samples:
    editors = []
    for process in sample['editors']:
        if Path(process['executable']).name != 'Unity':
            noneditors[process['pid']] = process['executable']
            continue
        if process['importWorker']:
            continue
        if process['emptyArguments']:
            unknown.append({'utcEpoch': sample['utcEpoch'], **process})
        editors.append(process)
    maximum = max(maximum, len(editors))
    if len(editors) > 1:
        overlaps.append({'utcEpoch': sample['utcEpoch'], 'editors': editors})
    foreign.extend({'utcEpoch': sample['utcEpoch'], **process} for process in sample['packets'] if not process['owned'])
result = {'reportedAt': v.utc(), 'revision': activation['productRevision'], 'releaseId': activation['release'],
          'state': agent['state'], 'pid': agent['pid'], 'binarySha256': binary, 'workerInstructionsSha256': instructions,
          'sampleCount': len(samples), 'maximumActualEditorCount': maximum, 'editorOverlaps': overlaps,
          'unclassifiedEmptyUnityArguments': unknown, 'foreignProjectHubSessions': foreign,
          'nonEditorExecutablesWithInheritedUnityComm': noneditors,
          'exclusive': not overlaps and not unknown and not foreign,
          'scope': 'Project-hub OMP/Codex identities; unrelated owner sessions neither operated on nor retained.'}
(state / 'final-host-installation.json').write_text(v.scrub(json.dumps(result, indent=2)) + '\n')
print(json.dumps({key: result[key] for key in ('releaseId', 'state', 'sampleCount', 'maximumActualEditorCount', 'exclusive')}))
