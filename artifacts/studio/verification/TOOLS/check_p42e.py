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
    if 'p42e' not in str(sums).lower() and 'p4.2e' not in str(sums).lower():continue
    for line in sums.read_text().splitlines():
        digest,name=line.split('  ',1);p=sums.parent/name
        assert p.is_file() and hashlib.sha256(p.read_bytes()).hexdigest()==digest,str(p)
        checked_hashes+=1
baseline=subprocess.check_output(['git','show','origin/main:docs/studio/07-verification-matrix.md'],cwd=v.ROOT,text=True)
def cells(text):return {line.split('|')[1].strip():[c.strip() for c in line.split('|')[1:4]] for line in text.splitlines() if line.startswith('| W-')}
assert cells(baseline)==cells((v.ROOT/'docs/studio/07-verification-matrix.md').read_text()),'only matrix status/evidence cells may change'
changed=set(subprocess.check_output(['git','diff','--name-only','origin/main'],cwd=v.ROOT,text=True).splitlines())
allowed_files={'docs/studio/07-verification-matrix.md','docs/studio/packets/P4.2e-final-rows.md','docs/studio/12-completion-report.md','studio/etos/ops.toml.tmpl','docs/studio/packets/P0.3-studio-model.md','docs/studio/packets/P0.5-companion.md','docs/studio/packets/P1.6-studio-core-unity.md','docs/studio/packets/P2.1-studio-ui.md','docs/studio/packets/P2.2-studio-etos-client.md','docs/studio/packets/P2.3-studio-views.md','docs/studio/packets/P2.4-staging-lane.md','docs/studio/packets/P4.3-docs-draft.md'}
for p in changed:
    assert p in allowed_files or p.startswith(('artifacts/studio/verification/','artifacts/studio/workflows/P4.2e/')),p
texts=0
credential=re.compile(r'\b(?:et[kpta]_[A-Za-z0-9_.~+/=-]{12,}|sk-[A-Za-z0-9_-]{16,})')
for root in [v.OUT,v.ROOT/'artifacts/studio/workflows/P4.2e']:
    for p in root.rglob('*'):
        if not p.is_file() or p.suffix not in ('.json','.jsonl','.xml','.trx','.log','.md','.txt','.csv','.diff'):continue
        if any(part in ('bin','obj','__pycache__') for part in p.parts):continue
        rel=str(p.relative_to(v.ROOT))
        if rel not in changed and not ('p42e' in rel.lower() or 'p4.2e' in rel.lower()):continue
        assert p.name not in ('auth.json','providers.env','GameCoreStudio.json')
        assert not credential.search(p.read_text(errors='replace')),'credential-shaped bytes: '+rel
        texts+=1
paid=json.loads((v.ROOT/'artifacts/studio/workflows/P4.2e/paid-ledger.json').read_text())
assert paid['operationCounts']['image']<=2 and paid['operationCounts']['tts']<=3 and paid['operationCounts']['describe']<=2
assert abs(sum(c['costUsd'] for c in paid['charges'])-paid['accountedUsd'])<1e-10
assert paid['accountedUsd']<=3
print(json.dumps({'matrixRows':68,'counts':rows['counts'],'hashesChecked':checked_hashes,'textFilesScanned':texts,'scope':'PASS','matrixCells':'PASS','references':'PASS','credentialShapes':'none','accountedUsd':paid['accountedUsd']}))

report=v.ROOT/'docs/studio/12-completion-report.md'
before=subprocess.check_output(['git','show','origin/main:docs/studio/12-completion-report.md'],cwd=v.ROOT,text=True)
old=before.split('## Addendum')[0]
new=report.read_text().split('## Addendum')[0]
added=[line for line in new.splitlines(True) if '**OPEN OWNER DECISION:** AI 3D mesh generation' in line]
assert len(added)==1
assert new.replace(added[0],'')==old, 'completion report edits must be one owner row plus Addendum'
for name in allowed_files:
    if name.startswith('docs/studio/packets/') and not name.endswith('P4.2e-final-rows.md'):
        original=subprocess.check_output(['git','show','origin/main:'+name],cwd=v.ROOT,text=True)
        assert (v.ROOT/name).read_text().startswith(original), name+': edit only appended packet section'
assert not (v.ROOT/'.evidence/live/games/hollowmere/Packages/com.hollowmere.mechanism.pressureplate').exists()
assert json.loads((v.ROOT/'artifacts/studio/workflows/P4.2e/editor-monitor.json').read_text())['maximum']<=1
print('PASS: completion report scope, shared-note append-only sections, no installed sample, at most one Editor')
