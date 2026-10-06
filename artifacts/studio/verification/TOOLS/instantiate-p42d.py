#!/usr/bin/env python3
"""Mint only a new request identity; keep sample package, proposal and operations exact."""
import hashlib
import json
from pathlib import Path
import secrets
import shutil
import sys
import time
src=Path(sys.argv[1]);dst=Path(sys.argv[2])
if dst.exists():raise SystemExit('refuse to overwrite retained candidate')
raw=(src/'change-set.json').read_text();before=json.loads(raw)
value=(int(time.time()*1000)<<80)|secrets.randbits(80)
alphabet='0123456789ABCDEFGHJKMNPQRSTVWXYZ';encoded=''
for _ in range(26):encoded=alphabet[value&31]+encoded;value>>=5
identity='cs_'+encoded
shutil.copytree(src,dst)
changed=raw.replace('"'+before['id']+'"','"'+identity+'"')
after=json.loads(changed);assert after=={**before,'id':identity}
(dst/'change-set.json').write_text(changed)
receipt={'oldId':before['id'],'newId':identity,'onlyChangedField':'id','sourceEnvelopeSha256':hashlib.sha256(raw.encode()).hexdigest(),'artifactHashes':{str(p.relative_to(src)):hashlib.sha256(p.read_bytes()).hexdigest() for p in (src/'artifacts').iterdir() if p.is_file()}}
(dst/'instantiation.json').write_text(json.dumps(receipt,indent=2)+'\n')
(dst/'SHA256SUMS').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+str(p.relative_to(dst))+'\n' for p in sorted(dst.rglob('*')) if p.is_file() and p.name!='SHA256SUMS'))
print(identity)
