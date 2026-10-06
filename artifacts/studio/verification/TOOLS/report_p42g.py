#!/usr/bin/env python3
"""Regenerate only P4.2g's commissioned row records and matrix cells."""
import hashlib
import json

import report_rows
import verify as v
import check_p42g

STATE = v.ROOT / 'artifacts/studio/workflows/P4.2g'
BASELINE = '55091b74be95eb6e47fe0e33c01ce2d8f2528779'
TARGETS = {'W-MECH-01', 'W-AI-02'}


def main():
    observations = json.loads((STATE / 'row-dispositions.json').read_text())
    if set(observations) != TARGETS:
        raise ValueError('Dispositions must contain exactly W-MECH-01 and W-AI-02')
    ledger = json.loads((STATE / 'paid-ledger.json').read_text())
    if not 0 <= ledger['accountedUsd'] <= 0.50 or any(
        ledger['operationCounts'][op] != 0 for op in ('image', 'tts', 'describe', '3dPaid')
    ):
        raise ValueError('P4.2g exceeds its USD 0.50 / zero-media accounting limits')
    decisions = json.loads((v.OUT / 'ROW-DECISIONS.json').read_text())
    rows = json.loads((v.OUT / 'ROWS.json').read_text())
    matrix = v.ROOT / 'docs/studio/07-verification-matrix.md'
    lines = matrix.read_text().splitlines()
    updates = {}
    for rid, observation in observations.items():
        if (observation['status'] not in ('PASS', 'BLOCKED', 'FAIL')
                or not isinstance(observation['note'], str) or not observation['note'].strip()
                or not isinstance(observation['command'], str) or not observation['command'].strip()
                or not observation['patterns'] or not observation.get('checks')):
            raise ValueError(rid + ': status, note, command, patterns and checks are required')
        # Resolve all evidence before writing anything, never silently drop missing runs.
        evidence = [report_rows.latest(pattern) for pattern in observation['patterns']]
        if not all(evidence):
            raise ValueError(rid + ': unmatched evidence pattern')
        if not any(p.startswith(rid + '/p42g-') for p in evidence):
            raise ValueError(rid + ': current P4.2g evidence is required')
        if any(not p.startswith(rid + '/') and not p.startswith('R6-') for p in evidence):
            raise ValueError(rid + ': evidence linked to unrelated row')
        matrix_index = next(i for i, line in enumerate(lines) if line.startswith('| ' + rid + ' |'))
        parts = [x.strip() for x in lines[matrix_index].split('|')[1:-1]]
        row_index = next(i for i, row in enumerate(rows['rows']) if row['row'] == rid)
        evidence = list(dict.fromkeys([*rows['rows'][row_index]['evidence'], *evidence]))
        for ref in evidence:
            check_p42g.confined(ref, v.OUT)
        check_p42g.check_evidence(rid, observation, evidence)
        decision = dict(observation, baseline=BASELINE,
                        historical='P4.2g installed qualification; earlier attempts remain retained as historical evidence. Other rows retain their own revision-specific evidence.')
        record = dict(decision, row=rid, scenario=parts[1], requirement=parts[2], reportedAt=v.utc())
        record['evidence'] = evidence
        record.pop('patterns')
        parts[3] = {'PASS': 'exercised', 'BLOCKED': 'blocked(prereq)', 'FAIL': 'failed'}[record['status']]
        parts[4] = f"[{record['status']} evidence](../../artifacts/studio/verification/{rid}/README.md): {record['note']}"
        updates[rid] = (decision, record, matrix_index, '| ' + ' | '.join(parts) + ' |', row_index)
    for rid, (decision, record, matrix_index, line, row_index) in updates.items():
        folder = v.OUT / rid
        (folder / 'row.json').write_text(json.dumps(record, indent=2) + '\n')
        text = f"# {rid}: {record['scenario']}\n\nVerdict: **{record['status']}**. {record['note']}\n\nProduct revision: `{BASELINE}`. Earlier attempts remain historical evidence.\n\n## Reproduce\n\n```sh\n{record['command']}\n```\n\n## Retained evidence\n\n"
        text += '\n'.join(f'- [{p}](../{p})' for p in record['evidence']) + '\n'
        (folder / 'README.md').write_text(text)
        (folder / 'SHA256SUMS').write_text(''.join(
            hashlib.sha256((folder / name).read_bytes()).hexdigest() + '  ' + name + '\n'
            for name in ('README.md', 'row.json')))
        decisions[rid] = decision
        rows['rows'][row_index] = record
        lines[matrix_index] = line
    rows['counts'] = {status: sum(row['status'] == status for row in rows['rows'])
                      for status in ('PASS', 'BLOCKED', 'FAIL')}
    (v.OUT / 'ROW-DECISIONS.json').write_text(json.dumps(decisions, indent=2) + '\n')
    (v.OUT / 'ROWS.json').write_text(json.dumps(rows, indent=2) + '\n')
    matrix.write_text('\n'.join(lines) + '\n')
    v.summary()
    summary = v.OUT / 'SUMMARY.md'
    summary.write_text(summary.read_text().replace(
        '# P4.2 + P4.2b + P4.2c verification summary', '# P4.2g verification summary', 1))
    (STATE / 'outcomes.json').write_text(json.dumps({
        'productRevision': BASELINE, 'counts': rows['counts'],
        'rerunRows': observations, 'ledger': ledger}, indent=2) + '\n')
    print(json.dumps(rows['counts']))


if __name__ == '__main__':
    main()
