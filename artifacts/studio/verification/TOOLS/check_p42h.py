#!/usr/bin/env python3
"""Validate evidence assertions, untouched rows, append-only report and exact caps."""
import hashlib
import json
from pathlib import Path
import re
import verify as v
from report_p42h import BASELINE, STATE, TARGETS

rows = json.loads((v.OUT / 'ROWS.json').read_text())
before = json.loads((STATE / 'report-baseline.json').read_text())
observations = json.loads((STATE / 'row-dispositions.json').read_text())
assert len(rows['rows']) == 68 and len({row['row'] for row in rows['rows']}) == 68
assert set(observations) == TARGETS
assert rows['counts'] == {status: sum(row['status'] == status for row in rows['rows']) for status in ('PASS', 'BLOCKED', 'FAIL')}
for row in rows['rows']:
    identity = row['row']
    if identity not in TARGETS:
        assert hashlib.sha256(json.dumps(row, sort_keys=True).encode()).hexdigest() == before['rows'][identity], identity
    else:
        assert row['baseline'] == BASELINE and row['status'] == observations[identity]['status']
        for check in observations[identity].get('checks', []):
            value = json.loads((v.OUT / check['file']).read_text())
            for part in check['pointer'].strip('/').split('/'):
                value = value[int(part)] if isinstance(value, list) else value[part]
            assert value == check['equals'], (identity, check)
        for reference in row['evidence']:
            assert (v.OUT / reference).is_file(), reference
        line = next(line for line in (v.ROOT / 'docs/studio/07-verification-matrix.md').read_text().splitlines() if line.startswith('| ' + identity + ' |'))
        assert '[' + row['status'] + ' evidence]' in line, identity
report = (v.ROOT / 'docs/studio/12-completion-report.md').read_bytes()
assert hashlib.sha256(report[:before['completionReportBytes']]).hexdigest() == before['completionReportSha256']
assert b'P4.2h' in report[before['completionReportBytes']:]
paid = json.loads((STATE / 'paid-ledger.json').read_text())
assert paid['accountedUsd'] <= 1.50
for operation, cap in {'image': 2, 'tts': 2, 'describe': 1, '3dPaid': 0}.items():
    assert paid['operationCounts'][operation] <= cap, operation
current = json.loads((v.OUT / 'W-E2E-01/p42h-accounting/result.json').read_text())
assert len(current['rows']) == 68
fresh = sorted(row['row'] for row in current['rows'] if row['currentRevision'] == 'PASS')
assert fresh == sorted(identity for identity, row in observations.items() if row['status'] == 'PASS')
scanned = 0
for identity in TARGETS | {'P4.2h'}:
    for directory in (v.OUT / identity).glob('p42h-*'):
        if not directory.is_dir():
            continue
        manifest = directory / 'SHA256SUMS'
        assert manifest.is_file(), str(manifest)
        for line in manifest.read_text().splitlines():
            digest, name = line.split('  ', 1)
            path = directory / name
            assert path.is_file() and hashlib.sha256(path.read_bytes()).hexdigest() == digest, str(path)
        for path in directory.rglob('*'):
            if path.is_file() and path.suffix in ('.txt', '.json', '.jsonl', '.log', '.xml', '.trx', '.md'):
                text = path.read_text(errors='replace')
                assert not re.search(r'\b(?:et[kpta]_[A-Za-z0-9_.~+/=-]{20,}|sk-[A-Za-z0-9_-]{20,})', text), str(path)
                assert str(Path.home()) not in text, str(path)
                scanned += 1
host = json.loads((STATE / 'host-monitor-summary.json').read_text())
assert host['foreignPacketSamples'] == 0
assert host['strictExclusivity'] in ('PASS', 'BLOCKED')
print(json.dumps({'status': 'PASS', 'matrixCounts': rows['counts'], 'freshPassRows': fresh,
    'untouchedRowRecords': 68 - len(TARGETS), 'appendOnlyCompletionReport': True,
    'accountedUsd': paid['accountedUsd'], 'textFilesScanned': scanned,
    'strictHostGate': host['strictExclusivity'], 'hostUnknownPids': host['unclassifiedPids']}, indent=2))
