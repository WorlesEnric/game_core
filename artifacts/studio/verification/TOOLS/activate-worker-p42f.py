#!/usr/bin/env python3
"""Deploy exact main worker instructions with an operator USD 0.50 worker ceiling.

The release installer restarts the binary but does not update the node's stored
worker definitions. Agent upgrade is the supported worker-definition update seam.
No credential files are copied, read, or printed.
"""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
import verify as v
root = v.ROOT
base = Path.home() / '.local/share/etos-studio/agents/gamecore-studio'
cli = str(Path.home() / '.local/opt/etos/bin/etos')
env = dict(os.environ, ETOS_ROOT=str(base.parents[1]))
if subprocess.run(['pgrep', '-x', 'Unity'], stdout=subprocess.DEVNULL).returncode == 0:
    raise SystemExit('Editor active; do not change installed service')
manifest = (root / 'studio/etos/agent/agent.toml').read_text()
needle = 'instructions = "workers/gc-designer.md"'
assert manifest.count(needle) == 1
manifest = manifest.replace(needle, needle + '\nbudget = { usd = 0.50 }')
binary = root / 'studio/agent/target/release/gamecore-studio'
contract = root / 'studio/etos/agent/workers'
identity = hashlib.sha256(manifest.encode() + binary.read_bytes() + (contract / 'gc-designer.md').read_bytes()).hexdigest()[:16]
dest = base / ('0.1.0-p42f-' + identity)
if not dest.exists():
    (dest / 'bin').mkdir(parents=True)
    shutil.copyfile(binary, dest / 'bin/gamecore-studio')
    (dest / 'bin/gamecore-studio').chmod(0o755)
    shutil.copytree(contract, dest / 'workers', ignore=shutil.ignore_patterns('__pycache__', '.pytest_cache'))
    (dest / 'agent.toml').write_text(manifest)
    (dest / 'SHA256SUMS').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest() + '  ' + str(p.relative_to(dest)) + '\n' for p in sorted(dest.rglob('*')) if p.is_file()))
for line in (dest / 'SHA256SUMS').read_text().splitlines():
    digest, name = line.split('  ', 1)
    assert hashlib.sha256((dest / name).read_bytes()).hexdigest() == digest
result = subprocess.run([cli, 'agent', 'upgrade', '--link', str(dest)], env=env, capture_output=True, text=True)
print(v.scrub(result.stdout + result.stderr))
result.check_returncode()
for _ in range(30):
    agents = json.loads(subprocess.check_output([cli, '--json', 'agent', 'list'], env=env, text=True))
    agent = next(a for a in agents if a['agent'] == 'gamecore-studio')
    if agent['state'] == 'ready':
        break
    time.sleep(1)
else:
    raise RuntimeError('activated agent not ready')
workers = json.loads(subprocess.check_output([cli, '--json', 'worker', 'list'], env=env, text=True))
worker = next(w for w in workers if w['name'] == 'gc-designer')
assert worker['instructions'] == (contract / 'gc-designer.md').read_text()
assert worker['budget']['usd'] == 0.50
receipt = {'release': dest.name, 'productRevision': '4ac7ba858b91e73e2d5de9dc6f02852c13feec56',
           'binarySha256': hashlib.sha256(binary.read_bytes()).hexdigest(),
           'designerInstructionsSha256': hashlib.sha256(worker['instructions'].encode()).hexdigest(),
           'workerBudget': worker['budget'], 'state': agent['state'],
           'runningExecutable': v.scrub(str(Path('/proc', str(agent['pid']), 'exe').resolve())),
           'operatorOverlay': 'Only designer worker budget = USD 0.50; exact main binary and instructions.'}
(root / 'artifacts/studio/workflows/P4.2f/worker-activation.json').write_text(json.dumps(receipt, indent=2) + '\n')
print(json.dumps(receipt, indent=2))
