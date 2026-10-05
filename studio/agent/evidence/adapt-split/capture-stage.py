"""Retain redacted evidence only from this packet's latest scratch companion."""
import json
from pathlib import Path
import sys

repo = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(repo / 'studio/stage'))
from redact import redact

out = Path(__file__).resolve().parent
base = Path.home() / '.cache/gamecore-studio/adapt-split/service'
node = max(base.glob('node-*'), key=lambda p: p.stat().st_mtime)
job_path = out / 'service-job.json'
if job_path.exists():
    job = json.loads(job_path.read_text())
    slots = list((node / 'slots').glob('*/' + job['slot']))
    if len(slots) != 1:
        raise RuntimeError('Expected exactly one matching scratch slot')
    slot = slots[0]
    for name in ['verdict.json', 'semantic-findings.json', 'editmode.xml', 'playmode.xml']:
        source = slot / 'out' / name
        if source.exists():
            target = 'pressure-verdict.json' if name == 'verdict.json' else name
            (out / target).write_text(redact(source.read_text()))
    logs = sorted((slot / 'out').rglob('*.log'))
    tails = []
    for log in logs:
        tails.append(str(log.relative_to(slot)) + '\n' + '\n'.join(log.read_text(errors='replace').splitlines()[-60:]))
    (out / 'stage-log-tails.txt').write_text(redact('\n\n'.join(tails)))
    verdict = job.get('verdict', {})
    (out / 'stage-timing.json').write_text(json.dumps({
        'scratch': str(node), 'slot': str(slot),
        'pass': verdict.get('pass'), 'durationMs': verdict.get('durationMs'),
        'budgetMs': verdict.get('budgetMs'), 'coldCache': verdict.get('coldCache'),
        'confinement': verdict.get('confinement'),
        'warmMeasurement': verdict.get('coldCache') is False,
    }, indent=2) + '\n')
for name in ['service-job.json', 'signed-verdict.json', 'issuance.json']:
    path = out / name
    if path.exists():
        path.write_text(redact(path.read_text()))
print('Retained scratch evidence from', node)
