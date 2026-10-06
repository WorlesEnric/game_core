#!/usr/bin/env python3
"""Sanitize/hash only P4.2e receipts; preserve every attempt and XML disposition."""
import hashlib
import json
import subprocess
from pathlib import Path
import verify as v
state=v.ROOT/'artifacts/studio/workflows/P4.2e'
folders=[state]
folders += [p for row in v.OUT.iterdir() if row.is_dir() for p in row.glob('p42e-*') if p.is_dir()]
for folder in folders:
    for p in folder.rglob('*'):
        if not p.is_file() or any(x in ('bin','obj','__pycache__') for x in p.parts): continue
        if p.suffix in ('.xml','.trx','.log','.txt','.json','.jsonl','.csv','.md','.diff'):
            s=p.read_text(errors='replace')
            p.write_text('\n'.join(line.rstrip() for line in v.scrub(s).splitlines())+'\n' if s else '')
    r=folder/'result.json'
    if r.exists():
        d=json.loads(r.read_text()); d['productRevision']=subprocess.check_output(['git','-C',str(v.ROOT/'.evidence/live'),'rev-parse','HEAD'],text=True).strip();d['installedRelease']='0.1.0-cac2f82c59be070b'
        r.write_text(json.dumps(d,indent=2)+'\n')
    (folder/'SHA256SUMS').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+str(p.relative_to(folder))+'\n' for p in sorted(folder.rglob('*')) if p.is_file() and p.name!='SHA256SUMS'))
