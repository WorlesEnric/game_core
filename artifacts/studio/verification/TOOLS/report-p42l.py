#!/usr/bin/env python3
"""Replace every current disposition; never inherit historical acceptance."""
import collections
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import verify as v

STATE = v.ROOT / 'artifacts/studio/workflows/P4.2l'


def check_assertion(check):
    path = v.OUT / check['file']
    if 'xmlCase' in check:
        cases = [case for case in ET.parse(path).iter() if case.tag.split('}')[-1] in ('test-case', 'UnitTestResult')]
        matches = [case for case in cases if check['xmlCase'] in case.get('fullname', case.get('testName', ''))]
        if not matches or any(case.get('result', case.get('outcome')) != 'Passed' for case in matches):
            raise ValueError('Missing or nonpassing exact XML acceptance: ' + check['xmlCase'])
        return
    value = json.loads(path.read_text())
    for part in check['pointer'].strip('/').split('/'):
        value = value[int(part)] if isinstance(value, list) else value[part]
    if value != check['equals']:
        raise ValueError('Receipt assertion failed: ' + str(path) + ' ' + check['pointer'])


def main():
    run = json.loads((STATE / 'run.json').read_text())
    activation = json.loads((STATE / 'worker-activation.json').read_text())
    decisions = json.loads((STATE / 'row-dispositions.json').read_text())
    ledger = json.loads((STATE / 'paid-ledger.json').read_text())
    if activation['productRevision'] != run['revision'] or ledger['accountedUsd'] > 2:
        raise ValueError('Revision or paid ceiling mismatch')
    for op, cap in {'image': 2, 'tts': 2, 'describe': 1, '3dPaid': 0}.items():
        if ledger['operationCounts'][op] > cap:
            raise ValueError('Paid operation cap exceeded: ' + op)
    matrix = v.ROOT / 'docs/studio/07-verification-matrix.md'
    lines = matrix.read_text().splitlines()
    rowspec = {line.split('|')[1].strip(): [cell.strip() for cell in line.split('|')[1:-1]] for line in lines if line.startswith('| W-')}
    if set(decisions) != set(rowspec) or len(decisions) != 68:
        raise ValueError('Every one of the 68 rows needs a fresh disposition')
    reported = v.utc()
    rows = []
    for rid, cells in rowspec.items():
        item = decisions[rid]
        if item['status'] not in ('PASS', 'BLOCKED', 'FAIL') or not item['note'] or not item['evidence']:
            raise ValueError('Incomplete disposition: ' + rid)
        if item['status'] == 'PASS' and not item.get('checks'):
            raise ValueError('PASS requires exact current-run assertions: ' + rid)
        for name in item['evidence']:
            path = (v.OUT / name).resolve()
            if not path.is_relative_to(v.OUT.resolve()) or not path.is_file():
                raise ValueError('Missing or unconfined evidence: ' + name)
        for assertion in item.get('checks', []):
            check_assertion(assertion)
        row = dict(item, row=rid, scenario=cells[1], requirement=cells[2], reportedAt=reported,
                   revision=run['revision'], baseline=run['revision'], releaseId=activation['release'],
                   evidencePath=item['evidence'][0], runStartedAt=run['startedAt'])
        rows.append(row)
    counts = {status: sum(row['status'] == status for row in rows) for status in ('PASS', 'BLOCKED', 'FAIL')}
    for row in rows:
        rid = row['row']
        folder = v.OUT / rid
        (folder / 'row.json').write_text(json.dumps(row, indent=2) + '\n')
        (folder / 'README.md').write_text(f"# {rid}: {row['scenario']}\n\nVerdict: **{row['status']}**. {row['note']}\n\n"
            + f"P4.2l product revision: `{row['revision']}`; installed release: `{row['releaseId']}`. Reported: {reported}.\n"
            + "Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.\n\n"
            + '## Reproduce\n\n```sh\n' + row['command'] + '\n```\n\n## Current-run evidence\n\n'
            + '\n'.join(f'- [{name}](../{name})' for name in row['evidence']) + '\n')
        (folder / 'SHA256SUMS').write_text(''.join(hashlib.sha256((folder / name).read_bytes()).hexdigest() + '  ' + name + '\n' for name in ('README.md', 'row.json')))
        index = next(i for i, line in enumerate(lines) if line.startswith('| ' + rid + ' |'))
        cells = rowspec[rid]
        cells[3] = {'PASS': 'exercised', 'BLOCKED': 'blocked(prereq)', 'FAIL': 'failed'}[row['status']]
        cells[4] = f"[{row['status']} evidence](../../artifacts/studio/verification/{rid}/README.md): {row['note']}"
        lines[index] = '| ' + ' | '.join(cells) + ' |'
    status_index = next(i for i, line in enumerate(lines) if line.startswith('**Status:**'))
    lines[status_index] = (f"**Status:** P4.2l (2026-10-08; execution receipts 2026-10-07–08 UTC): **{counts['PASS']} PASS / {counts['BLOCKED']} BLOCKED / {counts['FAIL']} FAIL, 68 rows**. " 
        + f"Every row is freshly judged at product `{run['revision']}` and immutable installed companion `{activation['release']}`; no historical PASS is carried forward. "
        + 'B-FRAME uses VSync OFF; VSync ON is informational. W-VOICE-01 uses SR-4.8 and explicit Send, not mandatory partial-revision visibility. '
        + 'See [P4.2l](packets/P4.2l-same-revision.md) for exact non-PASS causes, lane outcomes and paid accounting.')
    matrix.write_text('\n'.join(lines) + '\n')
    (v.OUT / 'ROWS.json').write_text(json.dumps({'counts': counts, 'revision': run['revision'], 'releaseId': activation['release'], 'reportedAt': reported, 'rows': rows}, indent=2) + '\n')
    (v.OUT / 'ROW-DECISIONS.json').write_text(json.dumps(decisions, indent=2) + '\n')
    v.summary()
    summary = v.OUT / 'SUMMARY.md'
    summary.write_text(summary.read_text().replace('# P4.2 + P4.2b + P4.2c verification summary', '# P4.2l same-revision verification summary', 1))
    print(json.dumps(counts))


if __name__ == '__main__':
    main()
