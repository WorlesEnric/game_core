#!/usr/bin/env python3
"""Retain stage XML counts and each owned UPM request's measured duration."""
import json
from pathlib import Path
import re
import verify as v
state = v.ROOT / 'artifacts/studio/workflows/P4.2g'
rows = []
for run in sorted((v.OUT / 'W-MECH-01').glob('p42g-stage-submit-*')):
    job_path = run / 'service/job.json'
    if not job_path.exists():
        continue
    job = json.loads(job_path.read_text())
    verdict = job.get('verdict', {})
    rows.append({'run': str(run.relative_to(v.ROOT)), 'jobId': job['jobId'], 'state': job['state'],
                 'coldCache': verdict.get('coldCache'), 'durationMs': verdict.get('durationMs'),
                 'pass': verdict.get('pass'), 'catalogDelta': verdict.get('catalogDelta'),
                 'xml': {str(p.relative_to(run)): v.xml_counts(p) for p in (run / 'service/slot-out').glob('*.xml')}})
attempts = []
for run in sorted((v.OUT / 'W-MECH-01').glob('p42g-stage-review-*')):
    outcome = run / 'outcome.json'
    if not outcome.exists():
        continue
    resolves = []
    upm = run / 'upm.log'
    for line in upm.read_text().splitlines():
        match = re.search(r'^\[([^]]+)\].*project:resolve-packages --> (\d+) \((\d+) ms\)', line)
        if match:
            resolves.append({'completedUtc': match[1], 'status': int(match[2]), 'durationMs': int(match[3])})
    assert resolves or not (run / 'admit.json').exists()
    logs = '\n'.join(p.read_text() for p in (run / 'logs').glob('*.log'))
    item = {'run': str(run.relative_to(v.ROOT)), 'outcome': json.loads(outcome.read_text()),
            'upmResolves': resolves, 'authenticatedRefreshWaitMessages': logs.count('Awaiting authenticated companion verdict refresh.'),
            'refreshSeconds': [float(n) for n in re.findall(r'Asset Pipeline Refresh .*?Total: ([0-9.]+) seconds', logs)]}
    item['resumerEnsuredAuthenticatedService'] = 'UnityAdmissionServices:EnsureStageService' in logs and 'AdmissionResumer:RefreshPendingVerdicts' in logs
    for name in ('restored-world', 'smoke-witness', 'undo', 'timing-audit'):
        p = run / (name + '.json')
        if p.exists():
            item[name] = json.loads(p.read_text())
    attempts.append(item)
(state / 'stage-summary.json').write_text(json.dumps({'stages': rows, 'graphicalAttempts': attempts}, indent=2) + '\n')
print(json.dumps({'stages': len(rows), 'graphicalAttempts': len(attempts)}))
