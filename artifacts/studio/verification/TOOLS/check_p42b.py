#!/usr/bin/env python3
"""Validate row references/counts and new evidence hashes without reading credential paths."""
import hashlib
import json
from pathlib import Path
import re
import subprocess
import verify as v

rows = json.loads((v.OUT/'ROWS.json').read_text())
assert len(rows['rows']) == 68
assert len({r['row'] for r in rows['rows']}) == 68
assert rows['counts'] == {status:sum(r['status']==status for r in rows['rows']) for status in ('PASS','BLOCKED','FAIL')}
for row in rows['rows']:
    folder = v.OUT/row['row']
    assert (folder/'README.md').exists(), row['row']
    for reference in row['evidence']:
        assert (v.OUT/reference).is_file(), (row['row'], reference)
for sums in v.OUT.glob('*/*/SHA256SUMS'):
    if not any(word in str(sums) for word in ('p42b-', 'owner-tariff-', 'final-main-', 'live-guard-')):
        continue
    for line in sums.read_text().splitlines():
        digest, name = line.split('  ', 1)
        path = sums.parent/name
        assert path.is_file() and hashlib.sha256(path.read_bytes()).hexdigest() == digest, str(path)
# Scan only new/modified packet-owned text, never settings, databases or key files.
paths = set(subprocess.check_output(['git','diff','--name-only','origin/main','--','artifacts/studio/verification','artifacts/studio/workflows/P4.2b'],cwd=v.ROOT,text=True).splitlines())
paths.update(subprocess.check_output(['git','ls-files','--others','--exclude-standard','--','artifacts/studio/verification','artifacts/studio/workflows/P4.2b'],cwd=v.ROOT,text=True).splitlines())
credential = re.compile(r'\b(?:et[kpta]_[A-Za-z0-9_.~+/=-]{12,}|sk-[A-Za-z0-9_-]{16,})')
checked = 0
for relative in paths:
    p=v.ROOT/relative
    if p.suffix not in ('.json','.jsonl','.xml','.log','.txt','.md','.csv','.trx') or not p.is_file(): continue
    assert p.name not in ('providers.env','auth.json','GameCoreStudio.json')
    assert not credential.search(p.read_text(errors='replace')), 'Credential-shaped bytes in '+relative
    checked += 1
print(json.dumps({'matrixRows':68,'counts':rows['counts'],'newTextFilesScanned':checked,'hashes':'PASS','references':'PASS','credentialShapes':'none'}))
