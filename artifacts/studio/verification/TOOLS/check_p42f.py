#!/usr/bin/env python3
"""Audit P4.2f evidence consistency without turning failed workflows into passes."""
import gzip
import hashlib
import json
from pathlib import Path
import re
import verify as v
state = v.ROOT / 'artifacts/studio/workflows/P4.2f'
rows = json.loads((v.OUT / 'ROWS.json').read_text())
assert len(rows['rows']) == 68
assert rows['counts'] == {s: sum(r['status'] == s for r in rows['rows']) for s in ('PASS', 'BLOCKED', 'FAIL')}
observations = json.loads((state / 'row-dispositions.json').read_text())
assert set(observations) == {'W-MECH-01', 'W-AI-02'}
for rid, observation in observations.items():
    row = next(r for r in rows['rows'] if r['row'] == rid)
    assert row['status'] == observation['status'] and row['note'] == observation['note']
    assert row['baseline'] == '4ac7ba858b91e73e2d5de9dc6f02852c13feec56'
    assert all((v.OUT / p).exists() for p in row['evidence'])
ledger = json.loads((state / 'paid-ledger.json').read_text())
assert ledger['accountedUsd'] <= 0.50
assert all(ledger['operationCounts'][op] == 0 for op in ('image', 'tts', 'describe', '3dPaid'))
assert ledger['operationCounts']['3dRefusals'] >= 1
activation = json.loads((state / 'worker-activation.json').read_text())
assert activation['workerBudget']['usd'] == 0.50
assert activation['designerInstructionsSha256'] == hashlib.sha256((v.ROOT / 'studio/etos/agent/workers/gc-designer.md').read_bytes()).hexdigest()
samples = [json.loads(line) for line in (state / 'editor-monitor.jsonl').read_text().splitlines()]
maximum = max(sum(not p['assetImportWorker'] and p.get('processState') != 'Z' for p in sample['processes']) for sample in samples)
runs = [p for row in v.OUT.iterdir() if row.is_dir() for p in row.glob('p42f-*') if p.is_dir()]
assert runs
credential = re.compile(rb'\b(?:et[kpta]_[A-Za-z0-9_.~+/=-]{16,}|sk-[A-Za-z0-9_-]{16,}|Bearer\s+[A-Za-z0-9_.~+/=-]{16,})')
for run in runs:
    result = run / 'result.json'
    if result.exists():
        record = json.loads(result.read_text())
        for path, expected in record.get('suites', {}).items():
            actual = v.xml_counts(run / path)
            assert actual['counts'] == expected['counts'] and actual['total'] == expected['total']
    for p in run.rglob('*'):
        if p.is_file() and p.suffix in ('.json', '.jsonl', '.xml', '.trx', '.log', '.txt', '.md'):
            assert not credential.search(p.read_bytes()), 'credential-shaped evidence value: ' + str(p.relative_to(v.ROOT))
for p in (v.OUT / 'W-MECH-01').glob('p42f-receipt-stage-*/signed-record-original.json.gz'):
    signed = json.loads(gzip.decompress(p.read_bytes()))
    assert signed['sourceRevision'] == '4ac7ba858b91e73e2d5de9dc6f02852c13feec56'
print('PASS: 68-row consistency, exact XML counts, current-source receipts, worker contract/budget, zero-media ledger, credential-shape scan')
if maximum > 1:
    raise SystemExit('BLOCKED: strict host exclusivity has an unclassified transient child; see host-monitor-summary.json. No unknown PID is reclassified to pass.')
print('PASS: strict host Editor exclusivity')
