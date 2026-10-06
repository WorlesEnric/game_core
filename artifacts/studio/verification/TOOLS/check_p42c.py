#!/usr/bin/env python3
"""Read-only packet integrity, scope and receipt audit; never reads credentials."""
import hashlib
import json
from pathlib import Path
import re
import subprocess
import verify as v

rows=json.loads((v.OUT/'ROWS.json').read_text())
assert len(rows['rows'])==68 and len({r['row'] for r in rows['rows']})==68
assert rows['counts']=={status:sum(r['status']==status for r in rows['rows']) for status in ('PASS','BLOCKED','FAIL')}
for row in rows['rows']:
    for reference in row['evidence']:assert (v.OUT/reference).is_file(),(row['row'],reference)
checked_hashes=0
for sums in v.OUT.glob('*/*/SHA256SUMS'):
    if 'p42c' not in str(sums).lower() and 'p4.2c' not in str(sums).lower():continue
    for line in sums.read_text().splitlines():
        digest,name=line.split('  ',1);p=sums.parent/name
        assert p.is_file() and hashlib.sha256(p.read_bytes()).hexdigest()==digest,str(p)
        checked_hashes+=1
baseline=subprocess.check_output(['git','show','origin/main:docs/studio/07-verification-matrix.md'],cwd=v.ROOT,text=True)
def cells(text):return {line.split('|')[1].strip():[c.strip() for c in line.split('|')[1:4]] for line in text.splitlines() if line.startswith('| W-')}
assert cells(baseline)==cells((v.ROOT/'docs/studio/07-verification-matrix.md').read_text()),'only matrix status/evidence cells may change'
changed=set(subprocess.check_output(['git','diff','--name-only','origin/main'],cwd=v.ROOT,text=True).splitlines())
allowed_files={'docs/studio/07-verification-matrix.md','docs/studio/packets/P4.2c-live-rows.md','studio/tools/verify-all.sh'}
for p in changed:
    assert p in allowed_files or p.startswith(('artifacts/studio/verification/','artifacts/studio/workflows/P4.2c/')),p
texts=0
credential=re.compile(r'\b(?:et[kpta]_[A-Za-z0-9_.~+/=-]{12,}|sk-[A-Za-z0-9_-]{16,})')
for root in [v.OUT,v.ROOT/'artifacts/studio/workflows/P4.2c']:
    for p in root.rglob('*'):
        if not p.is_file() or p.suffix not in ('.json','.jsonl','.xml','.trx','.log','.md','.txt','.csv','.diff'):continue
        if any(part in ('bin','obj','__pycache__') for part in p.parts):continue
        rel=str(p.relative_to(v.ROOT))
        if rel not in changed and not ('p42c' in rel.lower() or 'p4.2c' in rel.lower()):continue
        assert p.name not in ('auth.json','providers.env','GameCoreStudio.json')
        assert not credential.search(p.read_text(errors='replace')),'credential-shaped bytes: '+rel
        texts+=1
paid=json.loads((v.ROOT/'artifacts/studio/workflows/P4.2c/paid-ledger.json').read_text())
assert paid['operationCounts']=={'image':3,'tts':5,'describe':0,'3dPaid':0,'3dRefusals':1}
assert abs(sum(c['costUsd'] for c in paid['charges'])-paid['accountedUsd'])<1e-10
assert paid['accountedUsd']<10
print(json.dumps({'matrixRows':68,'counts':rows['counts'],'hashesChecked':checked_hashes,'textFilesScanned':texts,'scope':'PASS','matrixCells':'PASS','references':'PASS','credentialShapes':'none','accountedUsd':paid['accountedUsd']}))
