#!/usr/bin/env python3
"""Regenerate only the two commissioned row cells; retain every other disposition."""
import hashlib
import json
from pathlib import Path
import verify as v
import report_rows
state = v.ROOT / 'artifacts/studio/workflows/P4.2f'
observations = json.loads((state / 'row-dispositions.json').read_text())
assert set(observations) == {'W-MECH-01', 'W-AI-02'}
ledger = json.loads((state / 'paid-ledger.json').read_text())
assert ledger['accountedUsd'] <= 0.50
assert all(ledger['operationCounts'][op] == 0 for op in ('image', 'tts', 'describe', '3dPaid'))
decisions = json.loads((v.OUT / 'ROW-DECISIONS.json').read_text())
rows = json.loads((v.OUT / 'ROWS.json').read_text())
matrix = v.ROOT / 'docs/studio/07-verification-matrix.md'
lines = matrix.read_text().splitlines()
for rid, observation in observations.items():
    assert observation['status'] in ('PASS', 'BLOCKED', 'FAIL')
    decision = dict(observation, baseline='4ac7ba858b91e73e2d5de9dc6f02852c13feec56', historical='P4.2f installed current-main qualification. P4.2e failed attempts remain retained; other rows keep revision-specific evidence.')
    decisions[rid] = decision
    i = next(i for i, line in enumerate(lines) if line.startswith('| ' + rid + ' |'))
    parts = [x.strip() for x in lines[i].split('|')[1:-1]]
    record = dict(decision, row=rid, scenario=parts[1], requirement=parts[2], reportedAt=v.utc())
    record['evidence'] = [report_rows.latest(pattern) for pattern in record.pop('patterns')]
    assert record['evidence'] and all(record['evidence'])
    folder = v.OUT / rid
    (folder / 'row.json').write_text(json.dumps(record, indent=2) + '\n')
    text = f"# {rid}: {parts[1]}\n\nVerdict: **{record['status']}**. {record['note']}\n\nProduct revision: `{record['baseline']}`. Earlier attempts remain historical evidence.\n\n## Reproduce\n\n```sh\n{record['command']}\n```\n\n## Retained evidence\n\n"
    text += '\n'.join(f'- [{p}](../{p})' for p in record['evidence']) + '\n'
    (folder / 'README.md').write_text(text)
    (folder / 'SHA256SUMS').write_text(''.join(hashlib.sha256((folder / name).read_bytes()).hexdigest() + '  ' + name + '\n' for name in ('README.md', 'row.json')))
    parts[3] = {'PASS': 'exercised', 'BLOCKED': 'blocked(prereq)', 'FAIL': 'failed'}[record['status']]
    parts[4] = f"[{record['status']} evidence](../../artifacts/studio/verification/{rid}/README.md): {record['note']}"
    lines[i] = '| ' + ' | '.join(parts) + ' |'
    rows['rows'][next(i for i, row in enumerate(rows['rows']) if row['row'] == rid)] = record
rows['counts'] = {status: sum(row['status'] == status for row in rows['rows']) for status in ('PASS', 'BLOCKED', 'FAIL')}
(v.OUT / 'ROW-DECISIONS.json').write_text(json.dumps(decisions, indent=2) + '\n')
(v.OUT / 'ROWS.json').write_text(json.dumps(rows, indent=2) + '\n')
matrix.write_text('\n'.join(lines) + '\n')
v.summary()
summary = v.OUT / 'SUMMARY.md'
summary.write_text(summary.read_text().replace('# P4.2 + P4.2b + P4.2c verification summary', '# P4.2f verification summary', 1))
(state / 'outcomes.json').write_text(json.dumps({'productRevision': '4ac7ba858b91e73e2d5de9dc6f02852c13feec56', 'counts': rows['counts'], 'rerunRows': observations, 'ledger': ledger}, indent=2) + '\n')
print(json.dumps(rows['counts']))
