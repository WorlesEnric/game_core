#!/usr/bin/env python3
"""Audit P4.2g row claims against retained runs, XML, receipts and host samples."""
import gzip
import hashlib
import json
import re

import verify as v

STATE = v.ROOT / 'artifacts/studio/workflows/P4.2g'
BASELINE = '55091b74be95eb6e47fe0e33c01ce2d8f2528779'
TARGETS = {'W-MECH-01', 'W-AI-02'}
CREDENTIAL = re.compile(rb'\b(?:et[kpta]_[A-Za-z0-9_.~+/=-]{16,}|sk-[A-Za-z0-9_-]{16,}|Bearer\s+[A-Za-z0-9_.~+/=-]{16,})')


def confined(relative, root):
    path = (root / relative).resolve()
    if not path.is_relative_to(root.resolve()) or not path.exists():
        raise ValueError(f'Missing or unconfined evidence: {relative}')
    return path


def field(value, pointer):
    if not pointer.startswith('/'):
        raise ValueError('JSON field must be an RFC 6901 pointer: ' + pointer)
    for part in pointer[1:].split('/'):
        part = part.replace('~1', '/').replace('~0', '~')
        value = value[int(part)] if isinstance(value, list) else value[part]
    return value


def check_evidence(rid, observation, references):
    checks = observation['checks']
    if not isinstance(checks, list) or not checks:
        raise ValueError(rid + ': explicit XML/receipt checks required')
    xml_seen = False
    failure_seen = False
    current_success = False
    blocker_seen = False
    for check in checks:
        path = check['path']
        if not any(path == ref or path.startswith(ref.rstrip('/') + '/') for ref in references):
            raise ValueError(rid + ': check is not linked as retained evidence: ' + path)
        source = confined(path, v.OUT)
        if check['kind'] == 'xml':
            if observation['status'] == 'PASS' and not path.startswith(rid + '/p42g-'):
                raise ValueError(rid + ': historical XML cannot establish a current PASS')
            xml_seen = True
            measured = v.xml_counts(source)
            assert measured['total'] == check['total'] and measured['counts'] == check['counts'], path
            if check.get('passing'):
                assert not measured['failedSuites'], path
            if check.get('passing'):
                assert measured['total'] > 0 and measured['counts'] == {'Passed': measured['total']}, path
            else:
                failure_seen |= path.startswith(rid + '/p42g-') and bool(measured['nonpassing'] or measured['failedSuites'])
        elif check['kind'] == 'json':
            record = json.loads(gzip.decompress(source.read_bytes()) if source.suffix == '.gz' else source.read_text())
            if not check.get('assertions'):
                raise ValueError(path + ': JSON receipt must assert observed fields')
            for assertion in check['assertions']:
                actual = field(record, assertion['pointer'])
                assert actual == assertion['equals'], (path, assertion['pointer'], actual)
                verdict = assertion['pointer'].rsplit('/', 1)[-1]
                current = path.startswith(rid + '/p42g-')
                current_success |= current and verdict in ('status', 'state', 'pass', 'verified', 'ok') and actual in (True, 'PASS', 'Passed', 'pass', 'passed', 'success', 'Succeeded', 'complete', 'Completed')
                failure_seen |= current and verdict in ('status', 'phase', 'state', 'pass', 'ok') and actual in ('FAIL', 'fail', 'failed', 'compile_timeout', False)
                blocker_seen |= current and verdict in ('status', 'phase', 'state', 'reason', 'error') and actual in ('BLOCKED', 'blocked', 'blocked(prereq)', 'timeout', 'compile_timeout', 'unavailable', 'missing_prerequisite')
        else:
            raise ValueError(path + ': unsupported check kind')
    if observation['status'] == 'PASS':
        assert xml_seen, rid + ': PASS needs measured passing XML'
        assert all(c['passing'] for c in checks if c['kind'] == 'xml'), rid + ': nonpassing XML cannot be a PASS'
        assert not failure_seen, rid + ': a cited failure cannot be promoted to PASS'
        assert current_success, rid + ': PASS needs a successful current workflow verdict'
    elif observation['status'] == 'FAIL':
        assert failure_seen, rid + ': FAIL needs a cited failure, not a narrative alone'
    else:
        assert blocker_seen, rid + ': BLOCKED needs a cited blocker verdict'
    return xml_seen


def check_host():
    samples = [json.loads(line) for line in (STATE / 'editor-monitor.jsonl').read_text().splitlines() if line.strip()]
    assert samples, 'Missing host monitor samples'
    summary = json.loads((STATE / 'host-monitor-summary.json').read_text())
    assert summary['sampleCount'] == len(samples)
    unknown = []
    overlap = []
    maximum = 0
    for sample in samples:
        active = []
        for process in sample['processes']:
            if process.get('processState') == 'Z':
                continue  # zombie is not an active Editor
            if process.get('assetImportWorker'):
                continue  # import worker is not another Editor
            if process.get('emptyArguments') or not process.get('processState'):
                unknown.append((sample['utcEpoch'], process['pid']))
                continue  # unknown identity is a blocker, not a classified active Editor
            active.append(process)
        maximum = max(maximum, len(active))
        if len(active) > 1:
            overlap.append((sample['utcEpoch'], [p['pid'] for p in active]))
    assert summary['maximumClassifiedActiveEditors'] >= maximum
    if overlap or unknown or summary['strictExclusivity'] != 'proven':
        raise SystemExit('BLOCKED: strict host exclusivity unproven; active overlap '
                         f'{len(overlap)} samples, unknown identities {len(unknown)}; '
                         'zombies and import workers excluded only when observed as such.')
    assert maximum <= 1 and summary['strictExclusivity'] == 'proven'
    print('PASS: host exclusivity, observed import workers and zombies classified separately')


def main():
    rows = json.loads((v.OUT / 'ROWS.json').read_text())
    assert len(rows['rows']) == 68
    assert rows['counts'] == {s: sum(r['status'] == s for r in rows['rows'])
                              for s in ('PASS', 'BLOCKED', 'FAIL')}
    observations = json.loads((STATE / 'row-dispositions.json').read_text())
    assert set(observations) == TARGETS
    for rid, observation in observations.items():
        row = next(r for r in rows['rows'] if r['row'] == rid)
        assert row['status'] == observation['status'] and row['note'] == observation['note']
        assert row['baseline'] == BASELINE
        refs = row['evidence']
        assert refs and any(p.startswith(rid + '/p42g-') for p in refs)
        for ref in refs:
            confined(ref, v.OUT)
        check_evidence(rid, observation, refs)
    ledger = json.loads((STATE / 'paid-ledger.json').read_text())
    assert 0 <= ledger['accountedUsd'] <= 0.50
    assert all(ledger['operationCounts'][op] == 0 for op in ('image', 'tts', 'describe', '3dPaid'))
    activation = json.loads((STATE / 'worker-activation.json').read_text())
    assert activation['workerBudget']['usd'] == 0.50
    assert activation['productRevision'] == BASELINE
    assert activation['designerInstructionsSha256'] == hashlib.sha256(
        (v.ROOT / 'studio/etos/agent/workers/gc-designer.md').read_bytes()).hexdigest()
    runs = [run for row in TARGETS for run in (v.OUT / row).glob('p42g-*') if run.is_dir()]
    assert runs, 'No P4.2g runs'
    for run in runs:
        result = run / 'result.json'
        if result.exists():
            record = json.loads(result.read_text())
            for relative, expected in record.get('suites', {}).items():
                actual = v.xml_counts(confined(relative, run))
                assert actual['counts'] == expected['counts'] and actual['total'] == expected['total']
        for path in run.rglob('*'):
            if path.is_file() and path.suffix in ('.json', '.jsonl', '.xml', '.trx', '.log', '.txt', '.md'):
                assert not CREDENTIAL.search(path.read_bytes()), 'credential-shaped value: ' + str(path)
    receipts = list((v.OUT / 'W-MECH-01').glob('p42g-receipt-stage-*/signed-record-original.json.gz'))
    if any(job.get('verdict', {}).get('pass') for run in runs
           for path in [run / 'service/job.json'] if path.exists()
           for job in [json.loads(path.read_text())]):
        assert receipts, 'Missing current source-bound signed stage receipt for passing stage'
    for receipt in receipts:
        signed = json.loads(gzip.decompress(receipt.read_bytes()))
        assert signed['sourceRevision'] == BASELINE and signed['runner']['sourceCommit'] == BASELINE
        verified = json.loads((receipt.parent / 'verified.json').read_text())
        assert verified['verified'] is True and verified['sourceRevision'] == BASELINE
        assert verified['jobId'] == signed['jobId']
    print('PASS: 68 rows, cited XML/receipts, source-bound stage records, USD 0.50 zero media, credential scan')
    check_host()


if __name__ == '__main__':
    main()
