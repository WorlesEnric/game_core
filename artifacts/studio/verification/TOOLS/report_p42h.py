#!/usr/bin/env python3
"""Update only commissioned cells after checking concrete receipt assertions."""
import collections
import hashlib
import json
from pathlib import Path
import verify as v

BASELINE = 'a77cb38ba4a2265007fa40c38983e01a17bb0914'
TARGETS = {'W-EDIT-01', 'W-EDIT-03', 'W-ETOS-04', 'W-ETOS-05', 'W-ETOS-09', 'W-AI-01', 'W-REC-03', 'W-GAME-01', 'W-E2E-01'}
STATE = v.ROOT / 'artifacts/studio/workflows/P4.2h'

def main():
    observations = json.loads((STATE / 'row-dispositions.json').read_text())
    if set(observations) != TARGETS:
        raise ValueError('Every commissioned row requires an explicit disposition')
    ledger = json.loads((STATE / 'paid-ledger.json').read_text())
    if not 0 <= ledger['accountedUsd'] <= 1.50:
        raise ValueError('Packet spend exceeds total cap')
    for op, cap in {'image': 2, 'tts': 2, 'describe': 1, '3dPaid': 0}.items():
        if ledger['operationCounts'][op] > cap:
            raise ValueError(op + ' cap exceeded')
    rows = json.loads((v.OUT / 'ROWS.json').read_text())
    decisions = json.loads((v.OUT / 'ROW-DECISIONS.json').read_text())
    matrix = v.ROOT / 'docs/studio/07-verification-matrix.md'
    lines = matrix.read_text().splitlines()
    for rid, item in observations.items():
        if item['status'] not in ('PASS', 'BLOCKED', 'FAIL') or not item['note'] or not item['evidence']:
            raise ValueError('Incomplete disposition: ' + rid)
        for ref in item['evidence']:
            path = (v.OUT / ref).resolve()
            if not path.is_relative_to(v.OUT.resolve()) or not path.is_file():
                raise ValueError('Missing or unconfined evidence: ' + ref)
        if item['status'] == 'PASS' and not item.get('checks'):
            raise ValueError('PASS needs exact JSON assertions: ' + rid)
        for check in item.get('checks', []):
            value = json.loads((v.OUT / check['file']).read_text())
            for part in check['pointer'].strip('/').split('/'):
                value = value[int(part)] if isinstance(value, list) else value[part]
            if value != check['equals']:
                raise ValueError('Receipt assertion failed: ' + rid + ' ' + check['pointer'])
    for rid, item in observations.items():
        old = next(row for row in rows['rows'] if row['row'] == rid)
        record = {**old, **item, 'baseline': BASELINE, 'reportedAt': v.utc(),
            'historical': 'P4.2h qualifies only the commissioned rows. Earlier receipts remain historical; untouched rows are not same-revision acceptance.'}
        record['evidence'] = list(dict.fromkeys(old['evidence'] + item['evidence']))
        old.clear()
        old.update(record)
        decisions[rid] = {**item, 'baseline': BASELINE}
        folder = v.OUT / rid
        (folder / 'row.json').write_text(json.dumps(record, indent=2) + '\n')
        (folder / 'README.md').write_text(f"# {rid}: {record['scenario']}\n\nVerdict: **{record['status']}**. {record['note']}\n\nProduct baseline: `{BASELINE}`. Historical receipts retain their original revision.\n\n## Reproduce\n\n```sh\n{record['command']}\n```\n\n## Retained evidence\n\n" + '\n'.join(f'- [{ref}](../{ref})' for ref in record['evidence']) + '\n')
        (folder / 'SHA256SUMS').write_text(''.join(hashlib.sha256((folder / name).read_bytes()).hexdigest() + '  ' + name + '\n' for name in ('README.md', 'row.json')))
        index = next(i for i, line in enumerate(lines) if line.startswith('| ' + rid + ' |'))
        cells = [part.strip() for part in lines[index].split('|')[1:-1]]
        cells[3] = {'PASS': 'exercised', 'BLOCKED': 'blocked(prereq)', 'FAIL': 'failed'}[item['status']]
        cells[4] = f"[{item['status']} evidence](../../artifacts/studio/verification/{rid}/README.md): {item['note']}"
        lines[index] = '| ' + ' | '.join(cells) + ' |'
    rows['counts'] = dict(collections.Counter(row['status'] for row in rows['rows']))
    (v.OUT / 'ROWS.json').write_text(json.dumps(rows, indent=2) + '\n')
    (v.OUT / 'ROW-DECISIONS.json').write_text(json.dumps(decisions, indent=2) + '\n')
    matrix.write_text('\n'.join(lines) + '\n')
    v.summary()
    print(json.dumps(rows['counts']))

if __name__ == '__main__':
    main()
