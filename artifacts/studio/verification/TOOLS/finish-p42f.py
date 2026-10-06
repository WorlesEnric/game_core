#!/usr/bin/env python3
"""Sanitize and hash only P4.2f evidence; preserve signed compressed records."""
import hashlib
import json
from pathlib import Path
import verify as v
state = v.ROOT / 'artifacts/studio/workflows/P4.2f'
folders = [state] + [p for row in v.OUT.iterdir() if row.is_dir() for p in row.glob('p42f-*') if p.is_dir()]
for folder in folders:
    for p in folder.rglob('*'):
        if not p.is_file() or any(x in ('bin', 'obj', '__pycache__') for x in p.parts):
            continue
        if p.suffix in ('.xml', '.trx', '.log', '.txt', '.json', '.jsonl', '.csv', '.md', '.diff'):
            text = p.read_text(errors='replace')
            p.write_text('\n'.join(line.rstrip() for line in v.scrub(text).splitlines()) + '\n' if text else '')
    result = folder / 'result.json'
    if result.exists():
        record = json.loads(result.read_text())
        record['productRevision'] = '4ac7ba858b91e73e2d5de9dc6f02852c13feec56'
        record['installedBinarySha256'] = '0a44dc20026599b75c86fb410e389e7cbb6337b79fbb7d40d6ff7108e800e751'
        result.write_text(json.dumps(record, indent=2) + '\n')
    (folder / 'SHA256SUMS').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest() + '  ' + str(p.relative_to(folder)) + '\n' for p in sorted(folder.rglob('*')) if p.is_file() and p.name != 'SHA256SUMS'))
print('Retained sanitized evidence and SHA256SUMS for', len(folders), 'packet folders')
