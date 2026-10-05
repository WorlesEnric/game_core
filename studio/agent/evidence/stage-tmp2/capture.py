"""Collect redacted STAGE-TMP results only; never read node credentials or keys."""
import json
from pathlib import Path
import sys
import subprocess
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[4]
out = Path(__file__).resolve().parent
sys.path.insert(0, str(repo / 'studio/stage'))
from redact import redact

timings = []
for label in ('cold', 'warm'):
    target = out / label
    if not (target / 'service-job.json').exists():
        continue
    job = json.loads((target / 'service-job.json').read_text())
    slot = Path((target / 'slot-path.txt').read_text())
    tails = []
    for log in sorted(set(slot.glob('*.log')) | set((slot / 'out').rglob('*.log'))):
        lines = log.read_text(errors='replace').splitlines()
        tails.append(str(log.relative_to(slot)) + '\n' + '\n'.join(lines[-100:]))
    marker = target / 'captured-job-id.txt'
    if not marker.exists() or marker.read_text() != job['jobId']:
        (target / 'engine-log-tails.txt').write_text(redact('\n\n'.join(tails)) + '\n')
        marker.write_text(job['jobId'])
        if (slot / 'stage.json').exists():
            checked = subprocess.run([sys.executable, str(repo / 'tools/check_stage_slot.py'), str(slot)], capture_output=True, text=True)
            (target / 'slot-check.txt').write_text(redact(checked.stdout + checked.stderr) + f'\nexitCode={checked.returncode}\n')
    verdict = job.get('verdict') or {}
    if verdict.get('message') and not (target / 'engine-log-tails.txt').read_text().strip():
        (target / 'engine-log-tails.txt').write_text('No engine stream was produced. Allocator output from the authenticated service refusal:\n' + redact(verdict['message']) + '\n')
    counts = {}
    for platform in ('editmode', 'playmode'):
        xml = target / (platform + '.xml')
        if xml.exists():
            counts[platform] = ET.parse(xml).getroot().attrib
    timings.append({
        'run': label, 'pass': verdict.get('pass'),
        'durationMs': verdict.get('durationMs'), 'budgetMs': verdict.get('budgetMs'),
        'coldCache': verdict.get('coldCache'), 'confinement': verdict.get('confinement'),
        'configuredBudgetMs': 360000,
        'executionBudgetMs': next((s.get('facts', {}).get('budgetMs') for s in verdict.get('steps', []) if s.get('id') == 'budget'), None),
        'warmMeasurementReason': None if verdict.get('coldCache') is False else ('Cold acceptance run; warm measurement is the subsequent request' if verdict.get('pass') else 'No completed cold import seeded a warm cache'),
        'serviceJobElapsedMs': job['updatedAt'] - job['createdAt'],
        'warmMeasurement': verdict.get('coldCache') is False,
        'status': 'verdict-produced' if 'pass' in verdict else 'blocked-before-pipeline',
        'candidateXmlCounts': counts,
    })
    for path in target.glob('*.json'):
        path.write_text(json.dumps(json.loads(redact(path.read_text())), indent=2) + '\n')
(out / 'stage-timings.json').write_text(json.dumps(timings, indent=2) + '\n')
print(json.dumps(timings, indent=2))
