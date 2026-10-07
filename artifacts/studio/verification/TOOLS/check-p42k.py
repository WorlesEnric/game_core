#!/usr/bin/env python3
"""Audit all 68 current dispositions, scoped evidence, installation and paid caps."""
import collections
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import subprocess
import verify as v

STATE = v.ROOT / 'artifacts/studio/workflows/P4.2k'
run = json.loads((STATE / 'run.json').read_text())
activation = json.loads((STATE / 'worker-activation.json').read_text())
reported = json.loads((v.OUT / 'ROWS.json').read_text())
contracts = json.loads((STATE / 'row-contracts.json').read_text())
rows = reported['rows']
assert len(rows) == 68 and {row['row'] for row in rows} == set(contracts)
assert reported['counts'] == {status: sum(row['status'] == status for row in rows) for status in ('PASS', 'BLOCKED', 'FAIL')}
spec = importlib.util.spec_from_file_location('report_i', Path(__file__).with_name('report-p42k.py'))
report = importlib.util.module_from_spec(spec)
spec.loader.exec_module(report)
manifest = json.loads((STATE / 'current-evidence-manifest.json').read_text())
assert manifest['revision'] == run['revision'] and manifest['releaseId'] == activation['release']
current_files = {item['path']: item['sha256'] for item in manifest['files']}
for row in rows:
    assert row['revision'] == run['revision'] and row['releaseId'] == activation['release'], row['row']
    assert row['reportedAt'] >= run['startedAt'] and row['evidencePath'] in row['evidence'], row['row']
    assert {key: row[key] for key in ('scenario', 'requirement')} == contracts[row['row']], row['row']
    assert row['evidence'] and row['note'] and row['command'], row['row']
    for name in row['evidence']:
        path = (v.OUT / name).resolve()
        assert path.is_relative_to(v.OUT.resolve()) and path.is_file(), (row['row'], name)
        relative = str(path.relative_to(v.ROOT))
        assert relative in current_files, ('Evidence not retained in this run', row['row'], name)
        assert hashlib.sha256(path.read_bytes()).hexdigest() == current_files[relative], ('Evidence changed', name)
    if row['status'] == 'PASS':
        assert row.get('checks'), row['row']
    for assertion in row.get('checks', []):
        report.check_assertion(assertion)
for rid in ('W-HOST-01', 'W-ETOS-01', 'W-ETOS-02', 'W-ETOS-06', 'W-ETOS-08', 'W-GAME-07'):
    assert next(row for row in rows if row['row'] == rid)['status'] == 'BLOCKED'
ledger = json.loads((STATE / 'paid-ledger.json').read_text())
assert ledger['accountedUsd'] <= 2
for op, cap in {'image': 2, 'tts': 2, 'describe': 1, '3dPaid': 0}.items():
    assert ledger['operationCounts'][op] <= cap
host = json.loads((STATE / 'final-host-installation.json').read_text())
assert host['releaseId'] == activation['release'] and host['binarySha256'] == activation['binarySha256']
assert host['state'] == 'ready'
if not host['exclusive']:
    assert next(row for row in rows if row['row'] == 'W-E2E-01')['status'] != 'PASS'
completion = v.ROOT / 'docs/studio/12-completion-report.md'
original = subprocess.check_output(['git', 'show', run['revision'] + ':docs/studio/12-completion-report.md'], cwd=v.ROOT)
assert completion.read_bytes().startswith(original), 'Completion Addendum must be append-only'
for name in ('P0.3-studio-model.md', 'P0.5-companion.md', 'P1.6-studio-core-unity.md', 'P2.1-studio-ui.md',
             'P2.2-studio-etos-client.md', 'P2.3-studio-views.md', 'P2.4-staging-lane.md', 'P4.3-docs-draft.md'):
    relative = 'docs/studio/packets/' + name
    original = subprocess.check_output(['git', 'show', run['revision'] + ':' + relative], cwd=v.ROOT)
    assert (v.ROOT / relative).read_bytes().startswith(original), name
    assert 'R2 fixes — P4.2k' in (v.ROOT / relative).read_text(), name
packet = (v.ROOT / 'docs/studio/packets/P4.2k-same-revision.md').read_text()
for row in rows:
    if row['status'] != 'PASS':
        assert row['row'] in packet and row['note'] in packet, row['row']
result = {'status': 'PASS', 'reportedAt': v.utc(), 'revision': run['revision'], 'releaseId': activation['release'],
          'rows': 68, 'counts': reported['counts'], 'accountedUsd': ledger['accountedUsd'],
          'hostExclusive': host['exclusive'], 'appendOnlyCompletionAndNotes': True,
          'allRowAssertionsVerified': True, 'unchangedScenarioAndRequirementCells': True}
(STATE / 'final-audit.json').write_text(json.dumps(result, indent=2) + '\n')
print(json.dumps(result))
